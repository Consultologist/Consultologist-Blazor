using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Consultologist.Api.Models;

namespace Consultologist.Api.Email;

public sealed record EmailIntakeReplyInput(
    string JobId,
    string ToAddress,
    string FinalStatus,
    // #159: when present alongside a set delivery password, the completed
    // document travels as an encrypted attachment. Trailing optional members —
    // Durable payload compatibility.
    string? AppUserId = null,
    string? AssembledDocument = null,
    // v7: the deliverable set in result-set order, one attachment each.
    // v5/v6 jobs leave it null and travel the AssembledDocument path.
    IReadOnlyList<EmailIntakeReplyDocument>? Documents = null,
    // #315: declared deliverables this job's inputs excluded. Trailing
    // optional — a job already in flight replies exactly as it did.
    IReadOnlyList<ConsultSkippedDocument>? SkippedDocuments = null);

/// <summary>
/// #486: what the reply leg did, for the job record. Null on replay of a run
/// from before the activity returned anything — treated as unknown.
/// </summary>
public sealed record EmailIntakeReplyOutcome(bool Sent, bool DocumentAttached, string? Reason = null);

/// <summary>One deliverable bound for the reply: authored identity plus its text.</summary>
public sealed record EmailIntakeReplyDocument(string ResultId, string Label, string Text);

/// <summary>
/// The completion reply (#158/#157/#486 — every job with an address: the
/// email door's sender, or the account's verified delivery address): a fresh no-PHI message — never a Graph
/// /reply, which would quote the PHI-bearing original — whose body is
/// boilerplate plus the History deep link. #159: when the account has set a
/// delivery password and the job produced documents, each is attached as an
/// AES-256 password-protected PDF; any failure in that leg degrades to the
/// link-only reply, never to silence. #217: a v7 job attaches one PDF per
/// deliverable, and an oversize set degrades WHOLE — a partial document set
/// would misrepresent what the consult produced.
/// </summary>
public sealed class SendEmailIntakeReplyActivity
{
    public const string Name = "send-email-intake-reply";

    private readonly IGraphMailClient _mail;
    private readonly IAccountSettingsStore _settingsStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendEmailIntakeReplyActivity> _logger;

    public SendEmailIntakeReplyActivity(
        IGraphMailClient mail,
        IAccountSettingsStore settingsStore,
        IConfiguration configuration,
        ILogger<SendEmailIntakeReplyActivity> logger)
    {
        _mail = mail;
        _settingsStore = settingsStore;
        _configuration = configuration;
        _logger = logger;
    }

    [Function(Name)]
    public Task<EmailIntakeReplyOutcome> RunAsync([ActivityTrigger] EmailIntakeReplyInput input, FunctionContext context)
        => SendAsync(input, context.CancellationToken);

    /// <summary>The activity body, minus the trigger plumbing — the tests' entry point.</summary>
    internal async Task<EmailIntakeReplyOutcome> SendAsync(EmailIntakeReplyInput input, CancellationToken cancellationToken)
    {
        var mailbox = _configuration["EmailIntake:MailboxAddress"];
        var appBaseUrl = _configuration["EmailIntake:AppBaseUrl"];

        if (string.IsNullOrWhiteSpace(mailbox) || string.IsNullOrWhiteSpace(appBaseUrl))
        {
            _logger.LogWarning(
                "Email intake reply skipped: EmailIntake settings incomplete. JobId={JobId}",
                input.JobId);
            return new EmailIntakeReplyOutcome(false, false, DeliveryOutcomes.NotConfigured);
        }

        var outcome = await TryCreateAttachmentsAsync(input, cancellationToken);

        var (subject, body) = EmailIntakeReply.Compose(
            appBaseUrl,
            input.JobId,
            input.FinalStatus,
            outcome.Labels,
            outcome.OmittedForSize,
            input.SkippedDocuments);
        await _mail.SendMailAsync(mailbox, input.ToAddress, subject, body, cancellationToken, outcome.Attachments);

        _logger.LogInformation(
            "Email intake reply sent. JobId={JobId}, FinalStatus={FinalStatus}, Attached={Attached}, OmittedForSize={OmittedForSize}",
            input.JobId,
            input.FinalStatus,
            outcome.Attachments.Count,
            outcome.OmittedForSize);

        return new EmailIntakeReplyOutcome(true, outcome.Attachments.Count > 0);
    }

    /// <summary>
    /// The whole-message budget for inline attachments: Graph caps a sendMail
    /// request around 3 MB and base64 inflates by ~1.33x, so 2 MB of raw PDF
    /// leaves room for the encoding and the envelope. A set over budget is
    /// dropped ENTIRELY — attaching the ones that fit would misrepresent the
    /// consult, and letting Graph reject the request would cost the reply too.
    /// </summary>
    internal const int MaxAttachmentBytes = 2 * 1024 * 1024;

    internal sealed record AttachmentOutcome(
        IReadOnlyList<GraphMailAttachment> Attachments,
        IReadOnlyList<string> Labels,
        bool OmittedForSize)
    {
        public static readonly AttachmentOutcome None =
            new(Array.Empty<GraphMailAttachment>(), Array.Empty<string>(), false);
    }

    /// <summary>
    /// The budget decision, pure: a set within budget travels whole, a set over
    /// it travels not at all. Never a subset — a partial document set would
    /// misrepresent what the consult produced.
    /// </summary>
    internal static AttachmentOutcome ApplyBudget(
        IReadOnlyList<GraphMailAttachment> attachments,
        IReadOnlyList<string> labels)
    {
        var totalBytes = attachments.Sum(attachment => (long)attachment.Content.Length);

        return totalBytes > MaxAttachmentBytes
            ? new AttachmentOutcome(Array.Empty<GraphMailAttachment>(), Array.Empty<string>(), true)
            : new AttachmentOutcome(attachments, labels, false);
    }

    private async Task<AttachmentOutcome> TryCreateAttachmentsAsync(
        EmailIntakeReplyInput input,
        CancellationToken cancellationToken)
    {
        // v7 carries the deliverable set; v5/v6 the single assembled document.
        // The v7 sugar id is "consult", so a single-deliverable job produces the
        // same filename either way.
        var documents = input.Documents is { Count: > 0 }
            ? input.Documents
            : !string.IsNullOrWhiteSpace(input.AssembledDocument)
                ? new[] { new EmailIntakeReplyDocument("consult", "Consultation note", input.AssembledDocument) }
                : Array.Empty<EmailIntakeReplyDocument>();

        if (input.FinalStatus != ConsultGenerationJobStatuses.Completed
            || documents.Count == 0
            || string.IsNullOrWhiteSpace(input.AppUserId))
        {
            return AttachmentOutcome.None;
        }

        try
        {
            var password = await _settingsStore.GetAsync(
                input.AppUserId,
                AccountSettingKeys.DeliveryPassword,
                cancellationToken);

            if (string.IsNullOrEmpty(password?.Value))
            {
                // Explicit over default: no password → link-only reply.
                return AttachmentOutcome.None;
            }

            var jobIdPrefix = input.JobId[..Math.Min(8, input.JobId.Length)];
            var attachments = documents
                // Filenames carry no PHI — an authored result id and the short job id.
                .Select(document => new GraphMailAttachment(
                    $"{document.ResultId}-{jobIdPrefix}.pdf",
                    // #252: the logger is what makes an undrawable character
                    // visible. Without it the render still folds what it can,
                    // but anything it cannot goes unreported — which is the
                    // silence this issue exists to end.
                    //
                    // #302: the context names which delivery, so a report can
                    // be traced to one document rather than only counted. Both
                    // values already appear in the filename above, so neither
                    // is a new disclosure.
                    ConsultDocumentPdf.Render(
                        document.Text,
                        password.Value,
                        _logger,
                        new PdfRenderContext(input.JobId, document.ResultId))))
                .ToList();

            var outcome = ApplyBudget(attachments, documents.Select(document => document.Label).ToList());

            if (outcome.OmittedForSize)
            {
                _logger.LogWarning(
                    "Encrypted attachments exceed the message budget; sending link-only reply. JobId={JobId}, Documents={Documents}, Bytes={Bytes}",
                    input.JobId,
                    attachments.Count,
                    attachments.Sum(attachment => (long)attachment.Content.Length));
            }

            return outcome;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Encrypted attachments could not be produced; sending link-only reply. JobId={JobId}",
                input.JobId);
            return AttachmentOutcome.None;
        }
    }
}

internal static class EmailIntakeReply
{
    internal static (string Subject, string Body) Compose(
        string appBaseUrl,
        string jobId,
        string finalStatus,
        IReadOnlyList<string>? attachedLabels = null,
        bool omittedForSize = false,
        IReadOnlyList<ConsultSkippedDocument>? skippedDocuments = null)
    {
        var link = $"{appBaseUrl.TrimEnd('/')}/history/{jobId}";
        var labels = attachedLabels ?? Array.Empty<string>();
        var includesAttachment = labels.Count > 0;

        if (finalStatus == ConsultGenerationJobStatuses.Completed)
        {
            // Authored package labels are never patient data, so naming the
            // documents lets the recipient see the set is complete before
            // decrypting anything.
            var attachmentNote = labels.Count switch
            {
                0 when omittedForSize =>
                    "The consult documents were too large to send by email — open them in History.\n\n",
                0 => string.Empty,
                1 => "The consult document is attached, encrypted with your delivery password.\n\n",
                _ => $"{string.Join(", ", labels)} are attached, encrypted with your delivery password.\n\n"
            };

            // #315: a shorter attachment list with no explanation reads as a
            // document that failed. Labels and the reason are authored package
            // content and declared values, so this carries no clinical detail.
            var skippedNote = skippedDocuments is { Count: > 0 }
                ? string.Join(string.Empty, skippedDocuments.Select(
                    d => $"{d.Label} was not produced: it {d.Reason}.\n")) + "\n"
                : string.Empty;

            return (
                "Your consult is ready",
                "Your consult has finished processing.\n\n"
                + attachmentNote
                + skippedNote
                + "View the result in Consultologist History (sign-in required):\n"
                + link + "\n\n"
                + (includesAttachment
                    ? "The message body intentionally contains no clinical content.\n"
                    : "This message intentionally contains no clinical content.\n")
                + "If you did not submit this consult, please delete this message.");
        }

        return (
            "Your consult run did not complete",
            "Your consult could not be completed.\n\n"
            + "Details are in Consultologist History (sign-in required):\n"
            + link + "\n\n"
            + "You can submit the consult again to retry.\n"
            + "This message intentionally contains no clinical content.");
    }
}
