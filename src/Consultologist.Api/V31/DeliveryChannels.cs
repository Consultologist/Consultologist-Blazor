using System.IO.Compression;
using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; D10): one kind of destination, as this account has it set up. A send's
/// failure comes back as an outcome (<see cref="DeliverySendResult.Failed"/> with its error kind), never a throw; a kind the
/// account has not set up is <see cref="DeliverySendResult.NotSetUp"/>.
/// </summary>
internal interface IDeliveryChannel
{
    /// <summary>The kind it sends: <c>email</c> or <c>pdf</c>.</summary>
    string Kind { get; }

    /// <summary>Makes one attempt of <paramref name="input"/>'s send.</summary>
    Task<DeliverySendOutcome> SendAsync(DeliverV31Input input, CancellationToken ct);
}

/// <summary>
/// #1505 (specVersion 31, area 08; D10): the channels this build has, by kind: <c>email</c> and <c>pdf</c>. <c>emr</c> and
/// <c>zip</c> have none (no write-back and no protected archive exist in this build), so <see cref="For"/> gives null and
/// the kind is not set up for any account.
/// </summary>
public sealed class DeliveryChannels
{
    private readonly IReadOnlyDictionary<string, IDeliveryChannel> _byKind;

    /// <summary>The build's channels, over the mailbox and the account's settings, as the 5–30 reply reads them.</summary>
    public DeliveryChannels(IGraphMailClient mail, IAccountSettingsStore settings, IConfiguration configuration, ILoggerFactory loggers)
        : this(
        [
            new EmailDeliveryChannel(mail, settings, configuration, loggers.CreateLogger<EmailDeliveryChannel>()),
            new PdfDeliveryChannel(mail, settings, configuration, loggers.CreateLogger<PdfDeliveryChannel>()),
        ])
    {
    }

    /// <summary>The channels <paramref name="channels"/> (tests: a channel of their own).</summary>
    internal DeliveryChannels(IEnumerable<IDeliveryChannel> channels) =>
        _byKind = channels.ToDictionary(channel => channel.Kind, StringComparer.Ordinal);

    /// <summary>The channel of <paramref name="kind"/>; null when this build has none.</summary>
    internal IDeliveryChannel? For(string kind) => _byKind.GetValueOrDefault(kind);
}

/// <summary>
/// #1505 (specVersion 31, area 08; D10, D11): what the email and the PDF channel share. A kind is set up when the mailbox is
/// configured (<c>EmailIntake:MailboxAddress</c> and <c>EmailIntake:AppBaseUrl</c>, as the 5–30 reply reads them), the
/// job has a delivery address, and the account had not turned <c>delivery.emailPdf</c> off. #1507 (area 10; R8, D18): the
/// address and <c>delivery.emailPdf</c> are the ones the job's start read (<see cref="DeliverV31Input.Delivery"/>), as a
/// 5–30 job's are (#518); a job started before that build carries none, and they are read at the attempt as before. The
/// activity's outcome is in the orchestration's history, so a replay reads what the attempt found. The message is a fresh
/// one whose body is boilerplate, the documents' labels (package text) and the History link
/// (<see cref="EmailIntakeReply.Compose"/>), never a document's text. #1507 (D19): the send of a job whose every deliverable
/// is blocked is 30's link-only message saying no document could be sent (<see cref="NoDocumentSent"/>), by either kind,
/// <c>sent</c> link-only. A fault of the mailbox or of a render is a failed send: <c>unreachable</c> when the mail service
/// could not be reached, else <c>error</c>; only the exception's type is logged.
/// </summary>
internal abstract class MailDeliveryChannel(
    IGraphMailClient mail, IAccountSettingsStore settings, IConfiguration configuration, ILogger logger) : IDeliveryChannel
{
    /// <summary>The opening of <see cref="EmailIntakeReply.Compose"/>'s completed message, after which a bundle's labels are named.</summary>
    internal const string Opening = "Your consult has finished processing.\n\n";

    public abstract string Kind { get; }

    protected IAccountSettingsStore Settings => settings;

    protected ILogger Logger => logger;

    public async Task<DeliverySendOutcome> SendAsync(DeliverV31Input input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            var mailbox = configuration["EmailIntake:MailboxAddress"];
            var appBaseUrl = configuration["EmailIntake:AppBaseUrl"];
            if (string.IsNullOrWhiteSpace(mailbox) || string.IsNullOrWhiteSpace(appBaseUrl))
                return NotSetUp;

            var (address, emailRequested) = input.Delivery is { } atStart
                ? (atStart.Address, atStart.EmailRequested)
                : await AtTheAttemptAsync(input.AppUserId, ct);
            if (string.IsNullOrWhiteSpace(address) || !emailRequested)
                return NotSetUp;

            var message = input.Send.EveryDeliverableBlocked
                ? Blocked(input, appBaseUrl)
                : await ComposeAsync(input, appBaseUrl, ct);
            await mail.SendMailAsync(mailbox, address, message.Subject, message.Body, ct, message.Attachments);
            return new DeliverySendOutcome(DeliverySendResult.Sent, null, message.LinkOnly);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // #1508: the cancellation of the attempt's own bound is not a fault of the send: it passes through, and the
            // activity records it as a failed send of kind timeout.
            var kind = ex is HttpRequestException ? "unreachable" : "error";
            logger.LogWarning(
                "V31 delivery send failed. JobId={JobId}, Kind={Kind}, Attempt={Attempt}, ErrorKind={ErrorKind}, ExceptionType={ExceptionType}",
                input.JobId, Kind, input.Send.Attempt, kind, ex.GetType().FullName);
            return new DeliverySendOutcome(DeliverySendResult.Failed, kind);
        }
    }

    /// <summary>
    /// #1507 (A10-057; D19): the line a job whose every deliverable is blocked says after the opening: the documents by
    /// label (package text), never their text.
    /// </summary>
    internal static string NoDocumentSent(IReadOnlyList<string> labels) =>
        $"No document could be sent: {string.Join(", ", labels)} {(labels.Count == 1 ? "was" : "were")} not released.";

    /// <summary>
    /// #1507 (D19, ruled by the owner on #1507, 2026-11-22T21:38Z): the link-only message of a job whose every deliverable is
    /// blocked, as 30's reply still went out: <see cref="EmailIntakeReply.Compose"/>'s completed message, nothing attached,
    /// <see cref="NoDocumentSent"/> after its opening.
    /// </summary>
    private static Message Blocked(DeliverV31Input input, string appBaseUrl)
    {
        var (subject, body) = EmailIntakeReply.Compose(appBaseUrl, input.JobId, ConsultGenerationJobStatuses.Completed);
        if (body.StartsWith(Opening, StringComparison.Ordinal))
            body = Opening + NoDocumentSent(input.Send.Documents.Select(d => d.Label).ToList()) + "\n\n" + body[Opening.Length..];
        return new Message(subject, body, null, LinkOnly: DeliveryPlan.EveryDeliverableBlocked);
    }

    /// <summary>The delivery address and <c>delivery.emailPdf</c> as the account has them now: a job started before #1507's D18.</summary>
    private async Task<(string? Address, bool EmailRequested)> AtTheAttemptAsync(string appUserId, CancellationToken ct)
    {
        var address = (await settings.GetAsync(appUserId, AccountSettingKeys.DeliveryAddress, ct))?.Value;
        var emailPdf = (await settings.GetAsync(appUserId, AccountSettingKeys.EmailPdf, ct))?.Value;
        return (address, DeliveryAddress.EmailPdfOf(emailPdf) != false);
    }

    /// <summary>The message of <paramref name="input"/>'s send.</summary>
    protected abstract Task<Message> ComposeAsync(DeliverV31Input input, string appBaseUrl, CancellationToken ct);

    /// <summary>The kind is not set up for this account.</summary>
    protected static DeliverySendOutcome NotSetUp { get; } = new(DeliverySendResult.NotSetUp, null);

    /// <summary>The first 8 characters of the job's id, as the 5–30 reply's filenames have them.</summary>
    protected static string JobIdPrefix(string jobId) => jobId[..Math.Min(8, jobId.Length)];

    /// <summary>A deliverable's id in a filename: its row key without <c>deliverable.</c> (package text, never a value).</summary>
    protected static string FileId(string deliverable) =>
        deliverable.StartsWith("deliverable.", StringComparison.Ordinal) ? deliverable["deliverable.".Length..] : deliverable;

    /// <summary>
    /// A message to send (#1507, R8: every attempt that is set up sends one). #1507 (D17, D19):
    /// <see cref="LinkOnly"/> says why a message is the link-only email in place of what was asked.
    /// </summary>
    protected sealed record Message(
        string Subject, string Body, IReadOnlyList<GraphMailAttachment>? Attachments, string? LinkOnly = null);
}

/// <summary>
/// #1505 (specVersion 31, area 08; D10, D11): <c>email</c>: today's link-only message (<see cref="EmailIntakeReply.Compose"/>
/// for a completed job, nothing attached); a bundle's one message names each of its documents by label.
/// </summary>
internal sealed class EmailDeliveryChannel(
    IGraphMailClient mail, IAccountSettingsStore settings, IConfiguration configuration, ILogger<EmailDeliveryChannel> logger)
    : MailDeliveryChannel(mail, settings, configuration, logger)
{
    public override string Kind => "email";

    protected override Task<Message> ComposeAsync(DeliverV31Input input, string appBaseUrl, CancellationToken ct)
    {
        var (subject, body) = EmailIntakeReply.Compose(appBaseUrl, input.JobId, ConsultGenerationJobStatuses.Completed);
        var documents = input.Send.Documents;
        if (documents.Count > 1 && body.StartsWith(Opening, StringComparison.Ordinal))
            body = Opening + $"{string.Join(", ", documents.Select(d => d.Label))} are ready.\n\n" + body[Opening.Length..];
        return Task.FromResult(new Message(subject, body, null));
    }
}

/// <summary>
/// #1505 (specVersion 31, area 08; D10, D11): <c>pdf</c>: set up when <c>email</c> is; the message
/// (<see cref="EmailIntakeReply.Compose"/> naming what is attached) carries each document as a password-protected PDF
/// (<see cref="ConsultDocumentPdf.Render"/>), or, for a bundle, one <c>.zip</c> holding each. A document's text is Markdown
/// first: a <c>plaintext</c> one escaped (<see cref="MarkdownEscape"/>), a Markdown one as it is, an object one its JSON in a
/// fenced block. #1507 (area 10; R8, ruled by the owner on #1507, 2026-11-22; D17): as the 5–30 reply does, the document
/// password is read at the attempt, and with none, or with attachments over the 5–30 reply's budget
/// (<see cref="SendEmailIntakeReplyActivity.MaxAttachmentBytes"/>, the set dropped whole), the attempt sends 30's link-only
/// message for that case, nothing attached, <c>sent</c> link-only with A10-054 or A10-055.
/// </summary>
internal sealed class PdfDeliveryChannel(
    IGraphMailClient mail, IAccountSettingsStore settings, IConfiguration configuration, ILogger<PdfDeliveryChannel> logger)
    : MailDeliveryChannel(mail, settings, configuration, logger)
{
    public override string Kind => "pdf";

    /// <summary>The most bytes of attachments one message carries: the 5–30 reply's budget (tests: a smaller one).</summary>
    internal long AttachmentBudget { get; init; } = SendEmailIntakeReplyActivity.MaxAttachmentBytes;

    protected override async Task<Message> ComposeAsync(DeliverV31Input input, string appBaseUrl, CancellationToken ct)
    {
        var password = (await Settings.GetAsync(input.AppUserId, AccountSettingKeys.DeliveryPassword, ct))?.Value;
        if (string.IsNullOrEmpty(password))
        {
            // 30: "Explicit over default: no password → link-only reply."
            var (linkSubject, linkBody) = EmailIntakeReply.Compose(appBaseUrl, input.JobId, ConsultGenerationJobStatuses.Completed);
            return new Message(linkSubject, linkBody, null, LinkOnly: DeliveryPlan.NoDocumentPassword);
        }

        var send = input.Send;
        var prefix = JobIdPrefix(input.JobId);
        var pdfs = send.Documents
            .Select(document => new GraphMailAttachment(
                $"{FileId(document.Deliverable)}-{prefix}.pdf",
                ConsultDocumentPdf.Render(MarkdownOf(document), PublishedStamp.SpecVersion, password, Logger, new PdfRenderContext(input.JobId, document.Deliverable))))
            .ToList();
        IReadOnlyList<GraphMailAttachment> attachments = string.Equals(send.Bundle, "zip", StringComparison.Ordinal)
            ? [new GraphMailAttachment($"{(send.Folder.Length > 0 ? send.Folder : "deliverables")}-{prefix}.zip", Zip(pdfs), "application/zip")]
            : pdfs;

        var bytes = attachments.Sum(attachment => (long)attachment.Content.Length);
        if (bytes > AttachmentBudget)
        {
            Logger.LogWarning(
                "V31 delivery attachments exceed the message budget; sending link-only. JobId={JobId}, Documents={Documents}, Bytes={Bytes}",
                input.JobId, send.Documents.Count, bytes);
            // 30: a set over the budget travels not at all, and the reply says the documents were too large
            var (overSubject, overBody) = EmailIntakeReply.Compose(
                appBaseUrl, input.JobId, ConsultGenerationJobStatuses.Completed, [], omittedForSize: true);
            return new Message(overSubject, overBody, null, LinkOnly: DeliveryPlan.OverTheBudget);
        }

        var (subject, body) = EmailIntakeReply.Compose(
            appBaseUrl, input.JobId, ConsultGenerationJobStatuses.Completed, send.Documents.Select(d => d.Label).ToList());
        return new Message(subject, body, attachments);
    }

    /// <summary>
    /// A document's text as the PDF's Markdown: <c>plaintext</c> escaped, so it reads exactly as written; a Markdown format
    /// as it is; an object format's JSON in a fenced block, its fence longer than any run of backticks in it.
    /// </summary>
    internal static string MarkdownOf(DeliveryDocument document) => document.Format switch
    {
        "plaintext" => MarkdownEscape.Escape(document.Text),
        "markdown_block" or "markdown_inline" => document.Text,
        _ => Fenced(document.Text),
    };

    private static string Fenced(string json)
    {
        var longest = 0;
        var run = 0;
        foreach (var c in json)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        var fence = new string('`', Math.Max(3, longest + 1));
        return $"{fence}json\n{json}\n{fence}\n";
    }

    /// <summary>One archive holding each PDF, in the documents' order.</summary>
    private static byte[] Zip(IReadOnlyList<GraphMailAttachment> pdfs)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pdf in pdfs)
            {
                using var entry = archive.CreateEntry(pdf.Name, CompressionLevel.Optimal).Open();
                entry.Write(pdf.Content);
            }
        }

        return stream.ToArray();
    }
}
