using System.IO.Compression;
using System.Text;
using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; D10, D11): the email and PDF channels. Email is set up by the account's
/// delivery address, its <c>delivery.emailPdf</c> not <c>false</c> and a configured mailbox, and sends today's link-only
/// message; PDF also needs the document password, and attaches a protected PDF per document, or one zip for a bundle,
/// within the 5–30 reply's budget. A mail fault is a failed send, never a throw. Content is fictional (Margaret Okafor,
/// Dr A. Sample, Sample Oncology Clinic).
/// </summary>
public class DeliveryChannelsTests
{
    private const string JobId = "job-31-0001";
    private const string Mailbox = "consults@example.com";
    private const string Address = "dr.sample@example.com";
    private const string Letter = "Dear Margaret Okafor,\n\n# 1 *not bold*\n\nDr A. Sample, Sample Oncology Clinic";

    private readonly IGraphMailClient _mail = Substitute.For<IGraphMailClient>();
    private readonly IAccountSettingsStore _settings = Substitute.For<IAccountSettingsStore>();
    private readonly List<(string To, string Subject, string Body, IReadOnlyList<GraphMailAttachment>? Attachments)> _sent = [];

    public DeliveryChannelsTests()
    {
        _mail.SendMailAsync(Mailbox, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<GraphMailAttachment>?>())
            .Returns(call =>
            {
                _sent.Add((call.ArgAt<string>(1), call.ArgAt<string>(2), call.ArgAt<string>(3), call.ArgAt<IReadOnlyList<GraphMailAttachment>?>(5)));
                return Task.CompletedTask;
            });
        Set(AccountSettingKeys.DeliveryAddress, Address);
        Set(AccountSettingKeys.DeliveryPassword, "correct-horse-battery-16");
    }

    [Fact]
    public async Task Email_WithNoAddress_IsNotSetUp()
    {
        Set(AccountSettingKeys.DeliveryAddress, null);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.NotSetUp, null), await Email().SendAsync(Input("email"), CancellationToken.None));
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.NotSetUp, null), await Pdf().SendAsync(Input("pdf"), CancellationToken.None));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Email_TurnedOff_IsNotSetUp()
    {
        Set(AccountSettingKeys.EmailPdf, " FALSE ");

        Assert.Equal(DeliverySendResult.NotSetUp, (await Email().SendAsync(Input("email"), CancellationToken.None)).Result);
        Assert.Equal(DeliverySendResult.NotSetUp, (await Pdf().SendAsync(Input("pdf"), CancellationToken.None)).Result);

        // and with no mailbox configured, nothing is set up whatever the account says
        Set(AccountSettingKeys.EmailPdf, "true");
        Assert.Equal(DeliverySendResult.NotSetUp, (await Email(mailbox: null).SendAsync(Input("email"), CancellationToken.None)).Result);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Email_Sends_TheLinkOnlyMessage()
    {
        var outcome = await Email().SendAsync(Input("email"), CancellationToken.None);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Sent, null), outcome);
        var (to, subject, body, attachments) = Assert.Single(_sent);
        Assert.Equal(Address, to);
        Assert.Equal(EmailIntakeReply.Compose("https://app.example.com", JobId, "Completed"), (subject, body));
        Assert.Contains($"https://app.example.com/history/{JobId}", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Okafor", body, StringComparison.Ordinal);
        Assert.Null(attachments);

        // a bundle's one message names each document by its label, and holds no text of either
        _sent.Clear();
        await Email().SendAsync(Bundle("email"), CancellationToken.None);
        var bundled = Assert.Single(_sent).Body;
        Assert.Contains("Letter to the GP, Summary are ready.", bundled, StringComparison.Ordinal);
        Assert.Contains($"https://app.example.com/history/{JobId}", bundled, StringComparison.Ordinal);
        Assert.DoesNotContain("Okafor", bundled, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARealChannel_ThatOutlastsTheActivitysBound_IsAFailedSendOfKindTimeout()
    {
        // #1508: the mail seam waits on its token, as the real client's HTTP call does; the bound's expiry reaches the
        // activity as a cancellation, not as the channel's own `error`.
        _mail.SendMailAsync(Mailbox, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<GraphMailAttachment>?>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(4));
            });
        var activity = new DeliverV31Activity(new DeliveryChannels([Email()]), NullLogger<DeliverV31Activity>.Instance)
        {
            SendTimeout = TimeSpan.FromMilliseconds(50),
        };

        var outcome = await activity.RunAsync(Input("email"), CancellationToken.None);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Failed, "timeout"), outcome);
    }

    [Fact]
    public void TheCompletedReply_OpensWithWhatTheBundleSplicesAfter()
    {
        // The final review: a bundle's labels line goes after MailDeliveryChannel.Opening only when the 5-30 reply starts with
        // it; a reworded reply would drop the line silently, so the two are held together here.
        var (_, body) = EmailIntakeReply.Compose("https://app.example.com", JobId, "Completed");

        Assert.StartsWith(MailDeliveryChannel.Opening, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_WithNoPassword_SendsLinkOnly()
    {
        // #1507 (R8, D17): as 30's reply does with no password, the link-only message goes out, nothing attached
        Set(AccountSettingKeys.DeliveryPassword, null);

        var outcome = await Pdf().SendAsync(Input("pdf"), CancellationToken.None);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Sent, null, "no document password"), outcome);
        var (to, subject, body, attachments) = Assert.Single(_sent);
        Assert.Equal(Address, to);
        Assert.Equal(EmailIntakeReply.Compose("https://app.example.com", JobId, "Completed"), (subject, body));
        Assert.DoesNotContain("Okafor", body, StringComparison.Ordinal);
        Assert.Null(attachments);

        // Review Focus 5: email is still sent beside it
        Assert.Equal(DeliverySendResult.Sent, (await Email().SendAsync(Input("email"), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Pdf_AttachesAProtectedPdf_PerDocument()
    {
        var outcome = await Pdf().SendAsync(Input("pdf"), CancellationToken.None);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Sent, null), outcome);
        var (to, _, body, attachments) = Assert.Single(_sent);
        Assert.Equal(Address, to);
        Assert.Contains("The consult document is attached, encrypted with your delivery password.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Okafor", body, StringComparison.Ordinal);
        var pdf = Assert.Single(attachments!);
        Assert.Equal(("patient.patient_letter-job-31-0.pdf", "application/pdf"), (pdf.Name, pdf.ContentType));
        AssertProtectedPdf(pdf.Content);

        // a plaintext document is escaped to Markdown first, so it reads as written; Markdown is as it is; an object is a
        // fenced block of its JSON, its fence longer than any run of backticks in it
        Assert.Equal(MarkdownEscape.Escape(Letter), PdfDeliveryChannel.MarkdownOf(new DeliveryDocument("d", "L", "plaintext", Letter)));
        Assert.Equal("## Plan\n\n*x*", PdfDeliveryChannel.MarkdownOf(new DeliveryDocument("d", "L", "markdown_block", "## Plan\n\n*x*")));
        Assert.Equal("```json\n{\"a\":1}\n```\n", PdfDeliveryChannel.MarkdownOf(new DeliveryDocument("d", "L", "visit_summary", "{\"a\":1}")));
        Assert.Equal("````json\n{\"a\":\"```\"}\n````\n", PdfDeliveryChannel.MarkdownOf(new DeliveryDocument("d", "L", "visit_summary", "{\"a\":\"```\"}")));
    }

    [Fact]
    public async Task Pdf_OfABundle_AttachesOneZip()
    {
        var outcome = await Pdf().SendAsync(Bundle("pdf"), CancellationToken.None);

        Assert.Equal(DeliverySendResult.Sent, outcome.Result);
        var (_, _, body, attachments) = Assert.Single(_sent);
        Assert.Contains("Letter to the GP, Summary are attached", body, StringComparison.Ordinal);
        var zip = Assert.Single(attachments!);
        Assert.Equal(("referral_pack-job-31-0.zip", "application/zip"), (zip.Name, zip.ContentType));
        using var archive = new ZipArchive(new MemoryStream(zip.Content), ZipArchiveMode.Read);
        Assert.Equal(["referral_pack.gp_letter-job-31-0.pdf", "referral_pack.summary-job-31-0.pdf"], archive.Entries.Select(e => e.FullName));
        foreach (var entry in archive.Entries)
        {
            using var stream = new MemoryStream();
            using (var opened = entry.Open())
                opened.CopyTo(stream);
            AssertProtectedPdf(stream.ToArray());
        }
    }

    [Fact]
    public async Task Pdf_OverTheAttachmentBudget_SendsLinkOnly()
    {
        // #1507 (R8, D17): as 30's reply does over the budget, the whole set is dropped and the link-only message says so
        Assert.Equal(SendEmailIntakeReplyActivity.MaxAttachmentBytes, Pdf().AttachmentBudget);
        Assert.Equal(2 * 1024 * 1024, SendEmailIntakeReplyActivity.MaxAttachmentBytes);

        var outcome = await Pdf(budget: 1024).SendAsync(Bundle("pdf"), CancellationToken.None);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Sent, null, "attachments over the budget"), outcome);
        var (_, subject, body, attachments) = Assert.Single(_sent);
        Assert.Equal(EmailIntakeReply.Compose("https://app.example.com", JobId, "Completed", [], omittedForSize: true), (subject, body));
        Assert.Contains("The consult documents were too large to send by email — open them in History.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Okafor", body, StringComparison.Ordinal);
        Assert.Null(attachments);

        // the migrated case: one deliverable, one PDF, over the budget, falls back the same way
        _sent.Clear();
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Sent, null, "attachments over the budget"),
            await Pdf(budget: 1024).SendAsync(Input("pdf", AtStart), CancellationToken.None));
        var single = Assert.Single(_sent);
        Assert.Equal(EmailIntakeReply.Compose("https://app.example.com", JobId, "Completed", [], omittedForSize: true), (single.Subject, single.Body));
        Assert.Null(single.Attachments);
    }

    [Fact]
    public async Task TheDeliverySettings_AreReadAtTheStart()
    {
        // #1507 (R8, D18): the address and emailPdf the start read reach the channel, though the account's settings changed
        // since; a retry sees the same
        Set(AccountSettingKeys.DeliveryAddress, "someone.else@example.com");
        Set(AccountSettingKeys.EmailPdf, "false");

        foreach (var attempt in new[] { 1, 2 })
        {
            Assert.Equal(DeliverySendResult.Sent, (await Email().SendAsync(Input("email", AtStart, attempt), CancellationToken.None)).Result);
            Assert.Equal(DeliverySendResult.Sent, (await Pdf().SendAsync(Input("pdf", AtStart, attempt), CancellationToken.None)).Result);
        }

        Assert.Equal(4, _sent.Count);
        Assert.All(_sent, s => Assert.Equal(Address, s.To));
        await _settings.DidNotReceive().GetAsync("user-1", AccountSettingKeys.DeliveryAddress, Arg.Any<CancellationToken>());
        await _settings.DidNotReceive().GetAsync("user-1", AccountSettingKeys.EmailPdf, Arg.Any<CancellationToken>());

        // a start with no address is not set up, whatever the account has now
        _sent.Clear();
        Set(AccountSettingKeys.DeliveryAddress, Address);
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.NotSetUp, null),
            await Email().SendAsync(Input("email", new V31DeliverySettings(null, true)), CancellationToken.None));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task AnOrchestrationStartedBeforeThisBuild_ReadsTheSettingsAtTheAttempt()
    {
        // an input with no delivery settings (a job started by area 08's build, replaying) reads them as that build did
        var json = System.Text.Json.JsonSerializer.Serialize(Input("email"));
        Assert.DoesNotContain("\"Delivery\":{", json, StringComparison.Ordinal);
        var replayed = System.Text.Json.JsonSerializer.Deserialize<DeliverV31Input>(json.Replace(",\"Delivery\":null", "", StringComparison.Ordinal))!;
        Assert.Null(replayed.Delivery);

        Assert.Equal(DeliverySendResult.Sent, (await Email().SendAsync(replayed, CancellationToken.None)).Result);
        Assert.Equal(Address, Assert.Single(_sent).To);
    }

    [Fact]
    public async Task ThePassword_IsReadAtTheAttempt()
    {
        // #1507 (D18): the document password is read when the attempt is made, as 30 reads it at reply time; it is never in
        // what crosses the activity boundary
        Set(AccountSettingKeys.DeliveryPassword, null);
        Assert.Equal("no document password", (await Pdf().SendAsync(Input("pdf", AtStart, 1), CancellationToken.None)).LinkOnly);

        Set(AccountSettingKeys.DeliveryPassword, "correct-horse-battery-16");
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Sent, null), await Pdf().SendAsync(Input("pdf", AtStart, 2), CancellationToken.None));
        Assert.Single(_sent[1].Attachments!);
        await _settings.Received(2).GetAsync("user-1", AccountSettingKeys.DeliveryPassword, Arg.Any<CancellationToken>());
        Assert.DoesNotContain("correct-horse", System.Text.Json.JsonSerializer.Serialize(Input("pdf", AtStart, 2)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAccountWithEmailOff_GetsNothing_AsAt30()
    {
        // #518: a start with delivery.emailPdf false sends nothing, by either kind, even for a job whose every deliverable is
        // blocked; the account turning it on since changes nothing
        Set(AccountSettingKeys.EmailPdf, "true");
        var off = new V31DeliverySettings(Address, false);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.NotSetUp, null), await Email().SendAsync(Input("email", off), CancellationToken.None));
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.NotSetUp, null), await Pdf().SendAsync(Input("pdf", off), CancellationToken.None));
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.NotSetUp, null), await Pdf().SendAsync(Blocked("pdf") with { Delivery = off }, CancellationToken.None));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task AJobWhoseEveryDeliverableIsBlocked_SendsTheLinkOnlyEmail()
    {
        // #1507 (D19, ruled by the owner on #1507, 2026-11-22T21:38Z): the link-only email, saying no document could be sent,
        // with the History link and the documents' labels only; no password is needed, nothing is attached
        Set(AccountSettingKeys.DeliveryPassword, null);
        foreach (var kind in new[] { "pdf", "email" })
        {
            _sent.Clear();
            var outcome = await (kind == "pdf" ? (MailDeliveryChannel)Pdf() : Email()).SendAsync(Blocked(kind), CancellationToken.None);

            Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Sent, null, "every deliverable blocked"), outcome);
            var (to, subject, body, attachments) = Assert.Single(_sent);
            Assert.Equal(Address, to);
            Assert.Null(attachments);
            var (completedSubject, completed) = EmailIntakeReply.Compose("https://app.example.com", JobId, "Completed");
            Assert.Equal(completedSubject, subject);
            var line = MailDeliveryChannel.NoDocumentSent(["Consultation note", "Letter to the patient"]);
            Assert.Equal("No document could be sent: Consultation note, Letter to the patient were not released.", line);
            V31Contract.AssertSentence(new SourceRefusal("A10-057", line, ""));
            Assert.Equal(MailDeliveryChannel.Opening + line + "\n\n" + completed[MailDeliveryChannel.Opening.Length..], body);
            Assert.Contains($"https://app.example.com/history/{JobId}", body, StringComparison.Ordinal);
            Assert.Contains("This message intentionally contains no clinical content.", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Okafor", body, StringComparison.Ordinal);
        }

        Assert.Equal("No document could be sent: Consultation note was not released.", MailDeliveryChannel.NoDocumentSent(["Consultation note"]));
    }

    [Fact]
    public async Task AMailFault_IsAFailedSend_NotAThrow()
    {
        _mail.SendMailAsync(default!, default!, default!, default!, default, default)
            .ReturnsForAnyArgs(Task.FromException(new InvalidOperationException("Graph mail request failed with status 503.")));
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Failed, "error"), await Email().SendAsync(Input("email"), CancellationToken.None));
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Failed, "error"), await Pdf().SendAsync(Input("pdf"), CancellationToken.None));

        _mail.SendMailAsync(default!, default!, default!, default!, default, default)
            .ThrowsAsyncForAnyArgs(new HttpRequestException("No such host is known."));
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Failed, "unreachable"), await Email().SendAsync(Input("email"), CancellationToken.None));

        // a settings read that faults is a failed send too
        _settings.GetAsync(default!, default!, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("table"));
        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Failed, "error"), await Pdf().SendAsync(Input("pdf"), CancellationToken.None));
    }

    private void Set(string key, string? value) =>
        _settings.GetAsync("user-1", key, Arg.Any<CancellationToken>())
            .Returns(value is null ? null : new AccountSetting(key, value, "text/plain", DateTimeOffset.UtcNow));

    private static IConfiguration Configuration(string? mailbox) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EmailIntake:MailboxAddress"] = mailbox,
            ["EmailIntake:AppBaseUrl"] = "https://app.example.com/",
        })
        .Build();

    private EmailDeliveryChannel Email(string? mailbox = Mailbox) =>
        new(_mail, _settings, Configuration(mailbox), NullLogger<EmailDeliveryChannel>.Instance);

    private PdfDeliveryChannel Pdf(long? budget = null)
    {
        var channel = new PdfDeliveryChannel(_mail, _settings, Configuration(Mailbox), NullLogger<PdfDeliveryChannel>.Instance);
        return budget is { } bytes
            ? new PdfDeliveryChannel(_mail, _settings, Configuration(Mailbox), NullLogger<PdfDeliveryChannel>.Instance) { AttachmentBudget = bytes }
            : channel;
    }

    /// <summary>#1507 (D18): what the start read: the address and delivery.emailPdf on.</summary>
    private static readonly V31DeliverySettings AtStart = new(Address, true);

    private static DeliverV31Input Input(string kind, V31DeliverySettings? delivery = null, int attempt = 1) => new(JobId, "user-1", new DeliverySend(kind, null, "patient",
        [new DeliveryDocument("deliverable.patient.patient_letter", "Letter to the patient", "plaintext", Letter)], attempt), delivery);

    /// <summary>#1507 (D19): the send of a job whose every deliverable is blocked: labels only, no text.</summary>
    private static DeliverV31Input Blocked(string kind) => new(JobId, "user-1", new DeliverySend(kind, null, "",
        [
            new DeliveryDocument("deliverable.note", "Consultation note", "markdown_block", ""),
            new DeliveryDocument("deliverable.patient.patient_letter", "Letter to the patient", "plaintext", ""),
        ], 1, EveryDeliverableBlocked: true), AtStart);

    private static DeliverV31Input Bundle(string kind) => new(JobId, "user-1", new DeliverySend(kind, "zip", "referral_pack",
        [
            new DeliveryDocument("deliverable.referral_pack.gp_letter", "Letter to the GP", "plaintext", Letter),
            new DeliveryDocument("deliverable.referral_pack.summary", "Summary", "markdown_block", "## Summary\n\nMargaret Okafor was seen."),
        ], 1));

    private static void AssertProtectedPdf(byte[] content)
    {
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(content, 0, 4), StringComparison.Ordinal);
        Assert.Contains("/Encrypt", Encoding.ASCII.GetString(content), StringComparison.Ordinal);
    }
}
