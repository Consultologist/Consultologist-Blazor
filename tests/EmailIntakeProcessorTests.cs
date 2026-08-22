using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests;

public class EmailIntakeProcessorTests
{
    private const string Mailbox = "consults@example.com";
    private const string PassingAuthHeader = "spf=pass smtp.mailfrom=x; dkim=pass header.d=x; dmarc=pass action=none";

    private readonly IGraphMailClient _mail = Substitute.For<IGraphMailClient>();
    private readonly IEmailSenderResolver _senderResolver = Substitute.For<IEmailSenderResolver>();
    private readonly IEmailIntakeClaimStore _claims = Substitute.For<IEmailIntakeClaimStore>();
    private readonly IConsultGenerationJobStarter _starter = Substitute.For<IConsultGenerationJobStarter>();
    private readonly IWorkflowPackagePinResolver _pinResolver = Substitute.For<IWorkflowPackagePinResolver>();
    private readonly IWorkflowPackageStore _packageStore = Substitute.For<IWorkflowPackageStore>();
    private readonly DurableTaskClient _client = Substitute.For<DurableTaskClient>("test");
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-07-25T12:00:00Z"));

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    public EmailIntakeProcessorTests()
    {
        _mail.EnsureInboxChildFolderAsync(Mailbox, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult("folder-" + callInfo.ArgAt<string>(1)));

        // The default: no attachments. Set here rather than in CreateProcessor
        // so a test's own stub is not overwritten by processor construction.
        _mail.ListAttachmentsAsync(Mailbox, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GraphAttachmentListing(Array.Empty<GraphInboundAttachment>(), Array.Empty<string>()));
    }

    private EmailIntakeProcessor CreateProcessor(
        bool configured = true,
        ILogger<EmailIntakeProcessor>? logger = null,
        int? maxEmailDeferralHours = null,
        int? maxMessagesPerPoll = null)
    {
        var settings = new Dictionary<string, string?>();
        if (configured)
        {
            settings["EmailIntake:MailboxAddress"] = Mailbox;
            settings["EmailIntake:AppBaseUrl"] = "https://app.example.com";
        }

        if (maxEmailDeferralHours is { } hours)
        {
            settings["RateLimits:MaxEmailDeferralHours"] = hours.ToString();
        }

        if (maxMessagesPerPoll is { } perPoll)
        {
            settings["EmailIntake:MaxMessagesPerPoll"] = perPoll.ToString();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new EmailIntakeProcessor(
            configuration,
            _mail,
            _senderResolver,
            _claims,
            _starter,
            _pinResolver,
            _packageStore,
            _time,
            logger ?? NullLogger<EmailIntakeProcessor>.Instance);
    }

    private static GraphMessageRef Ref(string id = "g-1", string? imid = "<m1@x>") =>
        new(id, imid, DateTimeOffset.Parse("2026-07-25T11:59:00Z"));

    private static GraphMessage Message(
        string id = "g-1",
        string? from = "doc@example.com",
        string? body = "Referral body",
        string authHeader = PassingAuthHeader,
        bool hasAttachments = false)
    {
        return new GraphMessage(
            id,
            "<m1@x>",
            from,
            body,
            new[] { new GraphInternetMessageHeader("Authentication-Results", authHeader) },
            hasAttachments);
    }

    /// <summary>A matched sender and a claimed message — the shape every
    /// attachment test starts from.</summary>
    private void SetupAcceptedSender(GraphMessage message)
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(message);
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        _starter.StartAsync(_client, Arg.Any<ConsultGenerationRequest>(), "user-1",
                Arg.Any<ConsultGenerationJobOrigin>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome("job-1"));
    }

    private void WithAttachments(params GraphInboundAttachment[] attachments) =>
        _mail.ListAttachmentsAsync(Mailbox, "g-1", Arg.Any<CancellationToken>())
            .Returns(new GraphAttachmentListing(attachments, Array.Empty<string>()));

    /// <summary>#249: Graph listed something that yielded no bytes.</summary>
    private void WithUnreadable(string kind, params GraphInboundAttachment[] readable) =>
        _mail.ListAttachmentsAsync(Mailbox, "g-1", Arg.Any<CancellationToken>())
            .Returns(new GraphAttachmentListing(readable, new[] { kind }));

    private static GraphInboundAttachment Attachment(string name, string text) =>
        Attachment(name, System.Text.Encoding.UTF8.GetBytes(text));

    // #237: bytes, because email no longer decodes anything. Every fixture
    // used to be UTF-8 by construction, which is why nothing here could catch
    // the mojibake #242 records.
    private static GraphInboundAttachment Attachment(
        string name,
        byte[] content,
        string contentType = "text/plain") =>
        new(name, contentType, content.Length, content);

    private void WithV7Package()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(V7Fixtures.MultiDeliverable()));
    }

    /// <summary>#428: a package whose prior_notes is an array of text.</summary>
    private void WithV9Package()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(V9Fixtures.Structured()));
    }

    private void SetupSingleUnread()
    {
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Ref() });
    }

    [Fact]
    public async Task MailboxUnset_NoGraphCalls()
    {
        var summary = await CreateProcessor(configured: false).RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(0, summary.Listed);
        await _mail.DidNotReceiveWithAnyArgs().ListUnreadInboxMessagesAsync(default!, default, default);
    }

    [Fact]
    public async Task HappyPath_ClaimsBeforeStartAndDisposesToProcessed()
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        _starter.StartAsync(
                _client,
                Arg.Any<ConsultGenerationRequest>(),
                "user-1",
                Arg.Any<ConsultGenerationJobOrigin>(),
                Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome("job-1"));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        Received.InOrder(() =>
        {
            _claims.TryClaimAsync(Arg.Is<EmailIntakeClaim>(c => c.ClaimKey == "<m1@x>"), Arg.Any<CancellationToken>());
            _starter.StartAsync(
                _client,
                // #210: intake now always supplies the named map — a legacy
                // package's consult_draft entry folds back to the draft in
                // the starter, so the wire shape is one path for both eras.
                Arg.Is<ConsultGenerationRequest>(r =>
                    r.ConsultDraft == null && r.Inputs!["consult_draft"] == "Referral body"),
                "user-1",
                Arg.Is<ConsultGenerationJobOrigin>(o =>
                    o.Source == ConsultGenerationJobSources.Email && o.ReplyToAddress == "doc@example.com"),
                Arg.Any<CancellationToken>());
        });
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", "folder-Processed", Arg.Any<CancellationToken>());
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.JobId == "job-1" && c.Outcome == EmailIntakeOutcomes.Accepted && c.AppUserId == "user-1"),
            Arg.Any<CancellationToken>());
        await _mail.DidNotReceiveWithAnyArgs().SendMailAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ClaimLost_TerminalClaim_RepairsDispositionWithoutStart()
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(false);
        _claims.GetAsync("<m1@x>", Arg.Any<CancellationToken>())
            .Returns(new EmailIntakeClaim("<m1@x>", "g-1", null, _time.GetUtcNow().AddMinutes(-1), "user-1", "job-1", EmailIntakeOutcomes.Accepted));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Repaired);
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", "folder-Processed", Arg.Any<CancellationToken>());
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ClaimLost_YoungPendingClaim_SkipsEntirely()
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(false);
        _claims.GetAsync("<m1@x>", Arg.Any<CancellationToken>())
            .Returns(new EmailIntakeClaim("<m1@x>", "g-1", null, _time.GetUtcNow().AddMinutes(-2)));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Skipped);
        await _mail.DidNotReceiveWithAnyArgs().MoveMessageAsync(default!, default!, default!, default);
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task ClaimLost_StaleOutcomelessClaim_RepairsToRejected()
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(false);
        _claims.GetAsync("<m1@x>", Arg.Any<CancellationToken>())
            .Returns(new EmailIntakeClaim("<m1@x>", "g-1", null, _time.GetUtcNow().AddMinutes(-30)));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Repaired);
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", "folder-Rejected", Arg.Any<CancellationToken>());
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task AuthFloorFailure_RejectsSilently()
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>())
            .Returns(Message(authHeader: "spf=fail; dkim=fail; dmarc=fail"));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", "folder-Rejected", Arg.Any<CancellationToken>());
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedAuth),
            Arg.Any<CancellationToken>());
        await _senderResolver.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
        await _mail.DidNotReceiveWithAnyArgs().SendMailAsync(default!, default!, default!, default!, default);
    }

    [Theory]
    [InlineData(EmailSenderMatchOutcome.NoMatch)]
    [InlineData(EmailSenderMatchOutcome.Ambiguous)]
    [InlineData(EmailSenderMatchOutcome.NotActive)]
    public async Task SenderGateFailure_RejectsSilently(EmailSenderMatchOutcome outcome)
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(outcome));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedSender),
            Arg.Any<CancellationToken>());
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
        await _mail.DidNotReceiveWithAnyArgs().SendMailAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task BlankBody_RejectsSilently()
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message(body: "   "));
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedEmpty),
            Arg.Any<CancellationToken>());
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task InputsMismatch_RecordsTheEligibilityOutcome()
    {
        // v7 email eligibility: a body-only email cannot satisfy a package
        // whose declaration needs more — a distinct claim slug, same disposal.
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        _starter.StartAsync(default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(new ConsultGenerationJobStartOutcome(
                null, ConsultGenerationJobStartError.InputsMismatch, "Required input(s) 'labs' missing."));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", "folder-Rejected", Arg.Any<CancellationToken>());
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedInputs),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// #369: the refusal the sender receives is whatever the starter marked
    /// sender-safe, and nothing else. Before this the door allowlisted three
    /// error kinds, so a refused fire set came back as "could not be processed"
    /// with no cause — while the server held the sentence naming every
    /// deliverable and what its condition wanted.
    /// </summary>
    private void SetupStartFailure(ConsultGenerationJobStartOutcome outcome)
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        _starter.StartAsync(default!, default!, default!, default!, default).ReturnsForAnyArgs(outcome);
    }

    [Fact]
    public async Task NoApplicableDeliverable_TellsTheSenderWhichDocumentsWantedWhat()
    {
        const string detail = "No document applies to these inputs. "
            + "'Consultation note' needs billable to be 'true'; it is not supplied.";
        SetupStartFailure(new ConsultGenerationJobStartOutcome(
            null, ConsultGenerationJobStartError.NoApplicableDeliverable, detail, SenderSafeDetail: detail));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _mail.Received(1).SendMailAsync(
            Mailbox,
            "doc@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("needs billable to be 'true'")),
            Arg.Any<CancellationToken>());
        // Nothing went wrong here — the package simply declares no document for
        // these inputs — so it is not filed under start-failed.
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedNoDeliverable),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnsafeDetail_IsNotQuotedBackToTheSender()
    {
        // The starter withheld it because the sentence quotes a supplied value.
        // The door must not second-guess that from the error code: this is the
        // same InputsMismatch whose missing-input form IS forwarded.
        SetupStartFailure(new ConsultGenerationJobStartOutcome(
            null,
            ConsultGenerationJobStartError.InputsMismatch,
            "Input 'seen_on' is a date and must be written YYYY-MM-DD; got '1965-03-02x'."));

        await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        await _mail.Received(1).SendMailAsync(
            Mailbox,
            "doc@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => !body.Contains("1965-03-02x") && !body.Contains("seen_on")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StarterError_RejectsAndSendsOneGenericReply()
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        _starter.StartAsync(default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(new ConsultGenerationJobStartOutcome(null, ConsultGenerationJobStartError.RegistryUnavailable, "down"));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", "folder-Rejected", Arg.Any<CancellationToken>());
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.StartFailed),
            Arg.Any<CancellationToken>());
        await _mail.Received(1).SendMailAsync(
            Mailbox,
            "doc@example.com",
            Arg.Is<string>(s => !s.Contains("Referral")),
            Arg.Is<string>(b => !b.Contains("Referral body")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnreadableDocument_IsRecordedAsAnAttachmentProblem()
    {
        // The starter refuses the document (a scan, a corrupt file) after the
        // claim is written. The claim table is the audit surface, so this has
        // to read as an attachment problem — recorded as a generic start
        // failure it would be invisible among registry outages.
        WithV7Package();
        SetupAcceptedSender(Message(hasAttachments: true));
        WithAttachments(Attachment("consult_draft.pdf", "%PDF-1.7 …"u8.ToArray(), "application/pdf"));
        _starter.StartAsync(default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.InputFileUnreadable,
                "This PDF has no text layer, so it is a scan or a fax.",
                // #369: the door forwards what the starter authorised, so the
                // double has to mark it the way the starter does.
                SenderSafeDetail: "This PDF has no text layer, so it is a scan or a fax."));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedAttachments),
            Arg.Any<CancellationToken>());
        // And the sender is told which of their attachments to fix, not just
        // that something went wrong.
        await _mail.Received(1).SendMailAsync(
            Mailbox,
            "doc@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("no text layer")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VanishedMessage_RecordsOutcomeAndSkips()
    {
        SetupSingleUnread();
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns((GraphMessage?)null);

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Skipped);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.Vanished),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BlankBodyWithAttachment_StartsFromTheAttachment()
    {
        // The referral-as-attachment case: a blank-body rejection would have
        // thrown this away. The file fills the draft slot and travels as bytes.
        WithV7Package();
        SetupAcceptedSender(Message(body: "  ", hasAttachments: true));
        WithAttachments(Attachment("referral.txt", "The referral text."));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        await _starter.Received(1).StartAsync(
            _client,
            Arg.Is<ConsultGenerationRequest>(r =>
                r.InputFiles!["consult_draft"][0].Content.Length > 0 && r.Inputs == null),
            "user-1",
            Arg.Any<ConsultGenerationJobOrigin>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PdfAttachment_TravelsToTheParserRatherThanBouncing()
    {
        // The inversion #237 makes: a PDF used to bounce here because this
        // class decided what it could read. It no longer decides — the bytes
        // go to the starter, and the parser rules on them there.
        WithV7Package();
        SetupAcceptedSender(Message(body: "Please see attached.", hasAttachments: true));
        WithAttachments(Attachment("consult_draft.pdf", "%PDF-1.7 …"u8.ToArray(), "application/pdf"));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        await _starter.Received(1).StartAsync(
            _client,
            Arg.Is<ConsultGenerationRequest>(r =>
                r.InputFiles!["consult_draft"][0].ContentType == "application/pdf"),
            "user-1",
            Arg.Any<ConsultGenerationJobOrigin>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Utf16Attachment_ReachesTheStarterByteForByte()
    {
        // #242's regression test, and the shape of its fix: the defect was
        // this class decoding as UTF-8 and substituting U+FFFD. It cannot
        // mangle what it never reads, so the assertion is that the bytes
        // arrive unchanged and the parser decides how to read them.
        var utf16 = System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes("Résumé of prior notes — see attached."))
            .ToArray();

        WithV7Package();
        SetupAcceptedSender(Message(hasAttachments: true));
        WithAttachments(Attachment("prior_notes.txt", utf16));

        await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        await _starter.Received(1).StartAsync(
            _client,
            Arg.Is<ConsultGenerationRequest>(r => r.InputFiles!["prior_notes"][0].Content.SequenceEqual(utf16)),
            "user-1",
            Arg.Any<ConsultGenerationJobOrigin>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LegacyPackageAttachment_IsRefusedAndTheReplySaysWhy()
    {
        // A package declaring no inputs has one implicit slot, so a file has
        // nowhere of its own to go. The sender is told that, rather than
        // getting the generic bounce.
        SetupAcceptedSender(Message(hasAttachments: true));
        WithAttachments(Attachment("referral.txt", "The referral."));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
        await _mail.Received(1).SendMailAsync(
            Mailbox,
            "doc@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("accepts a single input")),
            Arg.Any<CancellationToken>());
    }

    // ---- the logging audit (#241, § 9) ----------------------------------

    [Fact]
    public async Task ARejectedAttachment_PutsNeitherItsNameNorItsContentInTheLog()
    {
        // This is the only door that has a filename at all — InputFilePayload
        // carries content and a content type and nothing else — so it is the
        // only place the "no filename in logs" half of § 9 can be violated.
        //
        // The rejection paths are the ones worth pinning: they log
        // Detail={Detail}, which carries either an attachment size message or
        // a reject reason. Reject reasons name input SLOTS ("consult_draft"),
        // never files, and Application Insights stores those template values
        // as customDimensions — so the assertion is over the structured
        // values, not just the rendered message.
        const string FileName = "SENTINEL-FILENAME-4a5b6c.txt";
        const string Content = "SENTINEL-CLINICAL-CONTENT-0f1e2d";

        var log = new CapturingLogger<EmailIntakeProcessor>();
        WithV7Package();
        SetupAcceptedSender(Message(body: "  ", hasAttachments: true));
        WithAttachments(Attachment(FileName, Content), Attachment("b.txt", Content));

        var summary = await CreateProcessor(logger: log).RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        Assert.DoesNotContain(FileName, log.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain(Content, log.Everything, StringComparison.Ordinal);

        // The log is not empty — otherwise this passes for the wrong reason,
        // which is the failure mode an absence check always has.
        Assert.NotEmpty(log.Recorded);
    }

    [Fact]
    public async Task AmbiguousAttachments_AreRejectedRatherThanGuessed()
    {
        // Two unnamed files with nowhere unambiguous to go. The reply cannot
        // say where they went, so a guess would be silent wrong data.
        WithV7Package();
        SetupAcceptedSender(Message(body: "  ", hasAttachments: true));
        WithAttachments(Attachment("a.txt", "One."), Attachment("b.txt", "Two."));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedAttachments),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NumberedAttachments_ReachTheStarterAsOneSlotInOrder()
    {
        // #428: the sender's numbers are the order the elements hash in.
        WithV9Package();
        SetupAcceptedSender(Message(hasAttachments: true));
        WithAttachments(Attachment("prior_notes-2.txt", "Two."), Attachment("prior_notes-1.txt", "One."));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        await _starter.Received(1).StartAsync(
            _client,
            Arg.Is<ConsultGenerationRequest>(r =>
                r.InputFiles!["prior_notes"].Count == 2
                && System.Text.Encoding.UTF8.GetString(r.InputFiles["prior_notes"][0].Content) == "One."
                && System.Text.Encoding.UTF8.GetString(r.InputFiles["prior_notes"][1].Content) == "Two."),
            "user-1",
            Arg.Any<ConsultGenerationJobOrigin>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANumberedAttachmentForATextSlot_IsRejectedAndTheReplySaysWhy()
    {
        WithV9Package();
        SetupAcceptedSender(Message(body: "  ", hasAttachments: true));
        WithAttachments(Attachment("consult_draft-1.txt", "Referral."));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
        await _mail.Received(1).SendMailAsync(
            Mailbox,
            "doc@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("'consult_draft' takes one document and cannot be numbered")),
            Arg.Any<CancellationToken>());
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedAttachments),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANumberedRejection_PutsNeitherFilenameNorContentInTheLog()
    {
        // The numbered paths log Detail={Detail} like every other rejection;
        // the sentinel rides in the stem so a reason that echoed a filename
        // would be caught.
        const string FileName = "prior_notes-SENTINEL-FILENAME-4a5b6c-1.txt";
        const string Content = "SENTINEL-CLINICAL-CONTENT-0f1e2d";

        var log = new CapturingLogger<EmailIntakeProcessor>();
        WithV9Package();
        SetupAcceptedSender(Message(body: "  ", hasAttachments: true));
        WithAttachments(Attachment(FileName, Content), Attachment("prior_notes-1.txt", Content), Attachment("prior_notes-3.txt", Content));

        var summary = await CreateProcessor(logger: log).RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        Assert.DoesNotContain("SENTINEL-FILENAME", log.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain(Content, log.Everything, StringComparison.Ordinal);
        Assert.NotEmpty(log.Recorded);
    }

    [Fact]
    public async Task InputTooLong_IsFiledAsRejectedAttachmentsAndTheReplySaysWhy()
    {
        // #428: the aggregate cap is about the attachments, not a failure.
        const string detail = "Input 'prior_notes' exceeds 256 KB across its 3 documents.";
        SetupStartFailure(new ConsultGenerationJobStartOutcome(
            null, ConsultGenerationJobStartError.InputTooLong, detail, SenderSafeDetail: detail));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _mail.Received(1).SendMailAsync(
            Mailbox,
            "doc@example.com",
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("exceeds 256 KB across its 3 documents")),
            Arg.Any<CancellationToken>());
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedAttachments),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NamedAttachment_FillsItsDeclaredSlot()
    {
        WithV7Package();
        SetupAcceptedSender(Message(hasAttachments: true));
        WithAttachments(Attachment("prior_notes.txt", "Old records."));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        await _starter.Received(1).StartAsync(
            _client,
            // The body is text, the attachment is bytes — the same split the
            // Consults page now sends.
            Arg.Is<ConsultGenerationRequest>(r =>
                r.Inputs!["consult_draft"] == "Referral body"
                && r.InputFiles!.ContainsKey("prior_notes")),
            "user-1",
            Arg.Any<ConsultGenerationJobOrigin>(),
            Arg.Any<CancellationToken>());
    }

    // #249 — an attachment Graph lists but yields no bytes for was skipped
    // with no log, no error and no count check, so a consult could be built
    // from a partial referral and presented as a whole one.

    [Fact]
    public async Task AnUnreadableAttachment_RejectsTheMessageRatherThanProceeding()
    {
        SetupAcceptedSender(Message(hasAttachments: true));
        WithUnreadable("#microsoft.graph.referenceAttachment");

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedAttachments),
            Arg.Any<CancellationToken>());
        // The whole point: no consult runs on what did arrive.
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task AnUnreadableAttachment_TellsTheSenderWhatToDoDifferently()
    {
        SetupAcceptedSender(Message(hasAttachments: true));
        WithUnreadable("#microsoft.graph.referenceAttachment");

        string? body = null;
        await _mail.SendMailAsync(Mailbox, "doc@example.com", Arg.Any<string>(),
            Arg.Do<string>(b => body = b), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<GraphMailAttachment>?>());

        await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.NotNull(body);
        Assert.Contains("link to a file", body);
        Assert.Contains("attach the document directly", body);
    }

    [Fact]
    public async Task AForwardedEmailAttachment_GetsItsOwnAdvice()
    {
        SetupAcceptedSender(Message(hasAttachments: true));
        WithUnreadable("#microsoft.graph.itemAttachment");

        string? body = null;
        await _mail.SendMailAsync(Mailbox, "doc@example.com", Arg.Any<string>(),
            Arg.Do<string>(b => body = b), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<GraphMailAttachment>?>());

        await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Contains("forwarded email", body);
    }

    [Fact]
    public async Task TheRejectionReply_NeverNamesTheFile()
    {
        // A filename can itself be PHI. One readable attachment alongside the
        // unreadable one, so there is a name available to leak.
        SetupAcceptedSender(Message(hasAttachments: true));
        WithUnreadable("#microsoft.graph.referenceAttachment",
            Attachment("Smith_John_referral.pdf", "Referral text"));

        string? body = null;
        await _mail.SendMailAsync(Mailbox, "doc@example.com", Arg.Any<string>(),
            Arg.Do<string>(b => body = b), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<GraphMailAttachment>?>());

        await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.NotNull(body);
        Assert.DoesNotContain("Smith", body);
        Assert.DoesNotContain(".pdf", body);
    }

    [Fact]
    public async Task OneReadableAndOneUnreadable_IsStillRejected()
    {
        // The harm is the partial referral, not the empty one.
        SetupAcceptedSender(Message(hasAttachments: true));
        WithUnreadable("#microsoft.graph.referenceAttachment",
            Attachment("prior_notes.txt", "Old notes"));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task AListingWithNoUnreadableKinds_IsProcessedAsBefore()
    {
        // The control: the rejection fires on UnreadableKinds alone, so a
        // message with only readable attachments is untouched by #249.
        // That inline parts never *reach* UnreadableKinds is pinned where the
        // rule actually runs — GraphAttachmentClassificationTests.
        SetupAcceptedSender(Message(hasAttachments: true));
        WithV7Package();
        WithAttachments(Attachment("prior_notes.txt", "Old notes"));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        Assert.Equal(0, summary.Rejected);
    }

    [Theory]
    [InlineData("#microsoft.graph.referenceAttachment", "link to a file")]
    [InlineData("#microsoft.graph.itemAttachment", "forwarded email")]
    [InlineData("unknown", "could not be read")]
    public void DescribeUnreadable_NamesTheKindAndTheRemedy(string kind, string expected)
    {
        var described = EmailIntakeProcessor.DescribeUnreadable([kind]);

        Assert.Contains(expected, described);
        Assert.Contains("re-send", described);
    }

    [Fact]
    public async Task AReferralWithNoContent_IsRejectedWithASentenceTheSenderCanActOn()
    {
        // #290: the message parsed and the sender matched; there was simply
        // no referral in it. The reply has to say so, and say what to do.
        SetupAcceptedSender(Message());
        _starter.StartAsync(_client, Arg.Any<ConsultGenerationRequest>(), "user-1",
                Arg.Any<ConsultGenerationJobOrigin>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.InputWithoutContent,
                "'consult_draft' does not contain a referral to work from. If the document was attached as a cloud link, please attach the file itself and re-send.",
                SenderSafeDetail: "'consult_draft' does not contain a referral to work from. If the document was attached as a cloud link, please attach the file itself and re-send."));

        string? body = null;
        await _mail.SendMailAsync(Mailbox, "doc@example.com", Arg.Any<string>(),
            Arg.Do<string>(b => body = b), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<GraphMailAttachment>?>());

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Rejected);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedEmpty),
            Arg.Any<CancellationToken>());
        Assert.NotNull(body);
        Assert.Contains("attach the file itself", body);
    }

    [Fact]
    public async Task MessageWithoutAttachments_NeverCallsGraphForThem()
    {
        SetupAcceptedSender(Message());

        await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        await _mail.DidNotReceiveWithAnyArgs().ListAttachmentsAsync(default!, default!, default);
    }

    // #266 — the Queued folder. This is the branch that did not exist before:
    // every other start failure moves the message to Rejected and tells the
    // sender their consult could not be processed, which for a rate limit
    // would be false.

    private const string QueuedFolderId = "folder-Queued";

    /// <summary>Puts a message in the Queued folder listing rather than the Inbox.</summary>
    private void SetupQueued(params GraphMessageRef[] refs)
    {
        _mail.FindInboxChildFolderAsync(Mailbox, "Queued", Arg.Any<CancellationToken>())
            .Returns(QueuedFolderId);
        _mail.ListFolderMessagesAsync(Mailbox, QueuedFolderId, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(refs);
    }

    private void RateLimitTheSender() =>
        _starter.StartAsync(_client, Arg.Any<ConsultGenerationRequest>(), "user-1",
                Arg.Any<ConsultGenerationJobOrigin>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.RateLimited,
                "over the limit",
                RetryAfter: TimeSpan.FromMinutes(37)));

    [Fact]
    public async Task RateLimited_ParksTheMessageInQueuedAndTellsTheSenderOnce()
    {
        SetupAcceptedSender(Message());
        RateLimitTheSender();

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Queued);
        Assert.Equal(0, summary.Rejected);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.Queued),
            Arg.Any<CancellationToken>());
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", QueuedFolderId, Arg.Any<CancellationToken>());
        await _mail.Received(1).SendMailAsync(
            Mailbox, "doc@example.com", "Your consult email is queued",
            Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<GraphMailAttachment>?>());
    }

    [Fact]
    public async Task RateLimited_NeverSendsTheRejectionReply()
    {
        // The failure this branch exists to prevent: a clinician told their
        // referral could not be read when nothing is wrong with it.
        SetupAcceptedSender(Message());
        RateLimitTheSender();

        await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        await _mail.DidNotReceive().SendMailAsync(
            Mailbox, Arg.Any<string>(), "Your consult email could not be processed",
            Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<GraphMailAttachment>?>());
        await _mail.DidNotReceive().MoveMessageAsync(
            Mailbox, Arg.Any<string>(), "folder-Rejected", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMessageRequeuedFromTheQueuedFolder_IsNotRepliedToAgain()
    {
        // The one assertion that justifies threading the source folder
        // through: replying on every retry would be ~30 emails over a
        // two-hour wait.
        SetupQueued(Ref());
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GraphMessageRef>());
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        RateLimitTheSender();

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Queued);
        await _mail.DidNotReceiveWithAnyArgs().SendMailAsync(
            default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task RateLimited_MarksTheClaimBeforeMovingTheMessage()
    {
        // Order is load-bearing. A move that fails must leave a `queued` claim
        // behind, so the next poll's repair clears it and retries the message
        // from wherever it actually is — rather than a message parked in
        // Queued with a claim saying nothing about why.
        SetupAcceptedSender(Message());
        RateLimitTheSender();
        _mail.MoveMessageAsync(Mailbox, "g-1", QueuedFolderId, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("graph down"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateProcessor().RunOnceAsync(_client, CancellationToken.None));

        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.Queued),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AQueuedClaimOnALaterPoll_IsReleasedAndTheMessageLeftWhereItIs()
    {
        SetupQueued(Ref());
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GraphMessageRef>());
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(false);
        _claims.GetAsync("<m1@x>", Arg.Any<CancellationToken>()).Returns(new EmailIntakeClaim(
            "<m1@x>", "g-1", "doc@example.com", _time.GetUtcNow(), "user-1",
            Outcome: EmailIntakeOutcomes.Queued));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Queued);
        await _claims.Received(1).DeleteAsync("<m1@x>", Arg.Any<CancellationToken>());
        await _mail.DidNotReceiveWithAnyArgs().MoveMessageAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task AQueuedMessageThatWaitedTooLong_IsRejectedWithoutEverBeingRead()
    {
        // Checked at the listing, so an expired message costs no message
        // fetch, no attachment fetch and no job start.
        SetupQueued(new GraphMessageRef("g-1", "<m1@x>", DateTimeOffset.Parse("2026-07-25T09:00:00Z")));
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GraphMessageRef>());
        _claims.GetAsync("<m1@x>", Arg.Any<CancellationToken>()).Returns(new EmailIntakeClaim(
            "<m1@x>", "g-1", "doc@example.com", _time.GetUtcNow(), "user-1",
            Outcome: EmailIntakeOutcomes.Queued));

        var summary = await CreateProcessor(maxEmailDeferralHours: 2).RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Expired);
        await _claims.Received(1).UpdateAsync(
            Arg.Is<EmailIntakeClaim>(c => c.Outcome == EmailIntakeOutcomes.RejectedRateLimit),
            Arg.Any<CancellationToken>());
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", "folder-Rejected", Arg.Any<CancellationToken>());
        await _mail.DidNotReceiveWithAnyArgs().GetMessageAsync(default!, default!, default);
        await _claims.DidNotReceiveWithAnyArgs().TryClaimAsync(default!, default);
        await _starter.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task AnOldUnreadInboxMessage_IsProcessedRatherThanExpired()
    {
        // The post-outage guarantee. After the poller has been down for hours
        // every unread message is old, and auto-rejecting that backlog would
        // tell senders who had heard nothing at all that they had failed. The
        // age test applies to Queued only, and this is what stops it being
        // "helpfully" generalised to both listings later.
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new GraphMessageRef("g-1", "<m1@x>", DateTimeOffset.Parse("2026-07-20T09:00:00Z")) });
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        _starter.StartAsync(_client, Arg.Any<ConsultGenerationRequest>(), "user-1",
                Arg.Any<ConsultGenerationJobOrigin>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome("job-1"));

        var summary = await CreateProcessor(maxEmailDeferralHours: 2).RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        Assert.Equal(0, summary.Expired);
    }

    [Fact]
    public async Task AQueuedMessageWithNoReceivedTime_WaitsRatherThanBeingRejected()
    {
        // Refusing a referral over missing metadata is the wrong failure
        // direction, so an unknown age never expires.
        SetupQueued(new GraphMessageRef("g-1", "<m1@x>", null));
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GraphMessageRef>());
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        RateLimitTheSender();

        var summary = await CreateProcessor(maxEmailDeferralHours: 0).RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(0, summary.Expired);
        Assert.Equal(1, summary.Queued);
    }

    [Fact]
    public async Task AQueuedMessageWhoseAccountIsBackUnderTheLimit_IsProcessed()
    {
        SetupQueued(Ref());
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GraphMessageRef>());
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(true);
        _mail.GetMessageAsync(Mailbox, "g-1", Arg.Any<CancellationToken>()).Returns(Message());
        _senderResolver.ResolveAsync("doc@example.com", Arg.Any<CancellationToken>())
            .Returns(new EmailSenderMatch(EmailSenderMatchOutcome.Matched, "user-1"));
        _starter.StartAsync(_client, Arg.Any<ConsultGenerationRequest>(), "user-1",
                Arg.Any<ConsultGenerationJobOrigin>(), Arg.Any<CancellationToken>())
            .Returns(new ConsultGenerationJobStartOutcome("job-1"));

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        await _mail.Received(1).MoveMessageAsync(Mailbox, "g-1", "folder-Processed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheQueuedBacklogIsDrainedBeforeTheInboxAndSharesThePollBudget()
    {
        // Without queue-first, a steady stream of new arrivals spends the
        // account's budget every window and the backlog never drains.
        SetupQueued(Ref("q-1", "<q1@x>"), Ref("q-2", "<q2@x>"));
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GraphMessageRef>());
        _claims.TryClaimAsync(Arg.Any<EmailIntakeClaim>(), Arg.Any<CancellationToken>()).Returns(false);
        _claims.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(callInfo =>
            new EmailIntakeClaim(callInfo.ArgAt<string>(0), "g", "doc@example.com", _time.GetUtcNow(), "user-1",
                Outcome: EmailIntakeOutcomes.Queued));

        await CreateProcessor(maxMessagesPerPoll: 3).RunOnceAsync(_client, CancellationToken.None);

        // Two of the three slots went to the backlog, so the Inbox was asked
        // for the one that was left.
        await _mail.Received(1).ListFolderMessagesAsync(Mailbox, QueuedFolderId, 3, Arg.Any<CancellationToken>());
        await _mail.Received(1).ListUnreadInboxMessagesAsync(Mailbox, 1, Arg.Any<CancellationToken>());
    }

    // #266, found by reading the two replies a real run actually sent.

    [Fact]
    public async Task TheQueuedReply_PromisesAFollowUpRatherThanAnOutcome()
    {
        // It said "you do not need to re-send it" full stop, and the expiry
        // path then said "please re-send it" ninety seconds later. Whichever
        // way a queued message goes, this reply has to stay true.
        SetupAcceptedSender(Message());
        RateLimitTheSender();

        string? body = null;
        await _mail.SendMailAsync(Mailbox, "doc@example.com", "Your consult email is queued",
            Arg.Do<string>(b => body = b), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<GraphMailAttachment>?>());

        await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.NotNull(body);
        Assert.Contains("write again", body);
        // No hard-coded interval: MaxEmailDeferralHours changes without a
        // deploy, so naming hours here would make the copy lie on a re-tune.
        Assert.DoesNotContain("two hours", body);
        Assert.DoesNotContain("2 hours", body);
    }

    [Fact]
    public async Task TheExpiryReply_DoesNotReadLikeADocumentFailure()
    {
        // It reused the start-failure copy, so a clinician whose message was
        // perfectly fine got the same subject and opening line as someone who
        // sent a scan with no text layer. busy and the preview 429 both say
        // "nothing is wrong"; this door has to say it too.
        SetupQueued(new GraphMessageRef("g-1", "<m1@x>", DateTimeOffset.Parse("2026-07-25T09:00:00Z")));
        _mail.ListUnreadInboxMessagesAsync(Mailbox, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GraphMessageRef>());
        _claims.GetAsync("<m1@x>", Arg.Any<CancellationToken>()).Returns(new EmailIntakeClaim(
            "<m1@x>", "g-1", "doc@example.com", _time.GetUtcNow(), "user-1",
            Outcome: EmailIntakeOutcomes.Queued));

        string? subject = null;
        string? body = null;
        await _mail.SendMailAsync(Mailbox, "doc@example.com",
            Arg.Do<string>(s => subject = s), Arg.Do<string>(b => body = b),
            Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<GraphMailAttachment>?>());

        await CreateProcessor(maxEmailDeferralHours: 2).RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(EmailIntakeProcessor.ExpiredReplySubject, subject);
        Assert.NotEqual(EmailIntakeProcessor.StartFailureReplySubject, subject);
        Assert.NotNull(body);
        Assert.Contains("Nothing is wrong", body);
        Assert.DoesNotContain("could not be processed", body);
        Assert.Contains("no clinical content", body);
    }

    [Fact]
    public async Task WithNoQueuedFolder_TheInboxIsStillDrainedAndNothingIsCreated()
    {
        // The normal state until the first message is ever rate limited.
        SetupAcceptedSender(Message());

        var summary = await CreateProcessor().RunOnceAsync(_client, CancellationToken.None);

        Assert.Equal(1, summary.Accepted);
        await _mail.DidNotReceiveWithAnyArgs().ListFolderMessagesAsync(default!, default!, default, default);
        await _mail.DidNotReceive().EnsureInboxChildFolderAsync(Mailbox, "Queued", Arg.Any<CancellationToken>());
    }
}
