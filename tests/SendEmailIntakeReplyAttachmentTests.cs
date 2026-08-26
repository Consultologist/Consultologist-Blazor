using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// The attachment leg of the completion reply (#159/#217): which PDFs are
/// produced, what they are named, and when the set degrades to link-only.
/// ConsultDocumentPdf.Render runs for real — it is fast, already byte-pinned,
/// and substituting it would mean inventing an interface for it.
/// </summary>
public class SendEmailIntakeReplyAttachmentTests
{
    private const string Password = "correct-horse-battery-16";
    private const string JobId = "0123456789abcdef0123456789abcdef";
    private const string Mailbox = "consults@example.com";

    private readonly IGraphMailClient _mail = Substitute.For<IGraphMailClient>();
    private readonly IAccountSettingsStore _settings = Substitute.For<IAccountSettingsStore>();

    private SendEmailIntakeReplyActivity CreateActivity()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EmailIntake:MailboxAddress"] = Mailbox,
                ["EmailIntake:AppBaseUrl"] = "https://app.example.com"
            })
            .Build();

        return new SendEmailIntakeReplyActivity(
            _mail, _settings, configuration, NullLogger<SendEmailIntakeReplyActivity>.Instance);
    }

    private void WithDeliveryPassword(string? password)
    {
        _settings.GetAsync("user-1", AccountSettingKeys.DeliveryPassword, Arg.Any<CancellationToken>())
            .Returns(password is null
                ? (AccountSetting?)null
                : new AccountSetting(AccountSettingKeys.DeliveryPassword, password, "text/plain", DateTimeOffset.UtcNow));
    }

    private EmailIntakeReplyOutcome? _lastOutcome;

    private async Task<(IReadOnlyList<GraphMailAttachment>? Attachments, string Body)> SendAsync(
        EmailIntakeReplyInput input)
    {
        IReadOnlyList<GraphMailAttachment>? attachments = null;
        var body = string.Empty;

        await _mail.SendMailAsync(
            Mailbox, Arg.Any<string>(), Arg.Any<string>(),
            Arg.Do<string>(value => body = value),
            Arg.Any<CancellationToken>(),
            Arg.Do<IReadOnlyList<GraphMailAttachment>?>(value => attachments = value));

        _lastOutcome = await CreateActivity().SendAsync(input, CancellationToken.None);

        return (attachments, body);
    }

    // #486: the outcome the orchestrator records on the job.
    [Fact]
    public async Task TheOutcome_SaysSentAndWhetherADocumentWasAttached()
    {
        WithDeliveryPassword("correct horse battery staple");
        await SendAsync(Input(assembledDocument: "## Note\n\nBody."));
        Assert.Equal(new EmailIntakeReplyOutcome(true, true), _lastOutcome);

        WithDeliveryPassword(null);
        await SendAsync(Input(assembledDocument: "## Note\n\nBody."));
        Assert.Equal(new EmailIntakeReplyOutcome(true, false), _lastOutcome);
    }

    [Fact]
    public async Task TheOutcome_NamesAnUnconfiguredMailbox()
    {
        var activity = new SendEmailIntakeReplyActivity(
            _mail, _settings,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SendEmailIntakeReplyActivity>.Instance);

        var outcome = await activity.SendAsync(Input(), CancellationToken.None);

        Assert.Equal(new EmailIntakeReplyOutcome(false, false, DeliveryOutcomes.NotConfigured), outcome);
        await _mail.DidNotReceiveWithAnyArgs().SendMailAsync(default!, default!, default!, default!, default);
    }

    private static EmailIntakeReplyInput Input(
        string? assembledDocument = null,
        IReadOnlyList<EmailIntakeReplyDocument>? documents = null,
        string finalStatus = ConsultGenerationJobStatuses.Completed) =>
        new(JobId, "doc@example.com", finalStatus, "user-1", assembledDocument, documents);

    [Fact]
    public async Task V6SingleDocument_KeepsTodaysFilename()
    {
        WithDeliveryPassword(Password);

        var (attachments, _) = await SendAsync(Input(assembledDocument: "## Note\n\nBody."));

        var attachment = Assert.Single(attachments!);
        Assert.Equal("consult-01234567.pdf", attachment.Name);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(attachment.Content, 0, 4));
    }

    [Fact]
    public async Task V7Deliverables_AttachOnePdfEachNamedByResultId()
    {
        WithDeliveryPassword(Password);

        var (attachments, body) = await SendAsync(Input(documents: new[]
        {
            new EmailIntakeReplyDocument("consult_note", "Consultation note", "## Note\n\nBody."),
            new EmailIntakeReplyDocument("patient_letter", "Patient letter", "## Letter\n\nBody.")
        }));

        Assert.Equal(
            new[] { "consult_note-01234567.pdf", "patient_letter-01234567.pdf" },
            attachments!.Select(attachment => attachment.Name).ToArray());
        Assert.All(attachments, attachment =>
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(attachment.Content, 0, 4)));
        Assert.Contains("Consultation note, Patient letter are attached", body);
    }

    [Fact]
    public async Task V7SingleDeliverable_UsesTheSugarIdSoFilenamesMatchV6()
    {
        WithDeliveryPassword(Password);

        var (attachments, _) = await SendAsync(Input(documents: new[]
        {
            new EmailIntakeReplyDocument("consult", "Consultation note", "## Note\n\nBody.")
        }));

        Assert.Equal("consult-01234567.pdf", Assert.Single(attachments!).Name);
    }

    [Fact]
    public async Task NoDeliveryPassword_SendsLinkOnly()
    {
        WithDeliveryPassword(null);

        var (attachments, body) = await SendAsync(Input(assembledDocument: "## Note\n\nBody."));

        Assert.Empty(attachments!);
        Assert.DoesNotContain("attached", body);
    }

    [Fact]
    public async Task FailedJob_NeverAttaches()
    {
        WithDeliveryPassword(Password);

        var (attachments, _) = await SendAsync(Input(
            assembledDocument: "## Note\n\nBody.",
            finalStatus: ConsultGenerationJobStatuses.Failed));

        Assert.Empty(attachments!);
    }

    [Fact]
    public void ApplyBudget_WithinBudget_KeepsTheWholeSet()
    {
        var attachments = new[] { Sized("a.pdf", 1024), Sized("b.pdf", 1024) };

        var outcome = SendEmailIntakeReplyActivity.ApplyBudget(attachments, new[] { "A", "B" });

        Assert.Equal(2, outcome.Attachments.Count);
        Assert.Equal(new[] { "A", "B" }, outcome.Labels);
        Assert.False(outcome.OmittedForSize);
    }

    [Fact]
    public void ApplyBudget_OverBudget_DropsTheWholeSet()
    {
        // Degrade WHOLE, never a subset: the budget is a per-message ceiling,
        // and a partial set would misrepresent what the consult produced.
        var attachments = new[]
        {
            Sized("a.pdf", SendEmailIntakeReplyActivity.MaxAttachmentBytes),
            Sized("b.pdf", 1)
        };

        var outcome = SendEmailIntakeReplyActivity.ApplyBudget(attachments, new[] { "A", "B" });

        Assert.Empty(outcome.Attachments);
        Assert.Empty(outcome.Labels);
        Assert.True(outcome.OmittedForSize);
    }

    [Fact]
    public void MaxAttachmentBytes_LeavesRoomForBase64AndTheEnvelope()
    {
        // Graph caps the request near 3 MB; base64 inflates by ~1.33x.
        Assert.True(SendEmailIntakeReplyActivity.MaxAttachmentBytes * 4 / 3 < 3 * 1024 * 1024);
    }

    private static GraphMailAttachment Sized(string name, int bytes) => new(name, new byte[bytes]);
}
