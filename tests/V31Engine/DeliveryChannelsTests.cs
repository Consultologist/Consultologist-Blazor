using System.IO.Compression;
using System.Text;
using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.V31;
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
    public async Task Pdf_WithNoPassword_IsNotSetUp()
    {
        Set(AccountSettingKeys.DeliveryPassword, null);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.NotSetUp, null), await Pdf().SendAsync(Input("pdf"), CancellationToken.None));
        Assert.Empty(_sent);

        // Review Focus 5: email is still set up beside it
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
    public async Task Pdf_OverTheAttachmentBudget_Fails()
    {
        Assert.Equal(SendEmailIntakeReplyActivity.MaxAttachmentBytes, Pdf().AttachmentBudget);
        Assert.Equal(2 * 1024 * 1024, SendEmailIntakeReplyActivity.MaxAttachmentBytes);

        var outcome = await Pdf(budget: 1024).SendAsync(Input("pdf"), CancellationToken.None);

        Assert.Equal(new DeliverySendOutcome(DeliverySendResult.Failed, "too-large"), outcome);
        Assert.Empty(_sent);
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

    private static DeliverV31Input Input(string kind) => new(JobId, "user-1", new DeliverySend(kind, null, "patient",
        [new DeliveryDocument("deliverable.patient.patient_letter", "Letter to the patient", "plaintext", Letter)], 1));

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
