using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; part 09 § 3.8; D10 to D13): <see cref="DeliveryRunner"/> sends each released
/// deliverable to each of its kinds (a <c>zip</c> folder's released deliverables in one send per kind), side by side; a
/// failed send is retried with back-off inside the delivery window, a kind not set up is final at once, and what is unsent
/// when the window closes is failed. Each destination's row is recorded once, in the fixed order, when every result is
/// final; no timer is left outstanding. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class DeliveryRunnerTests
{
    private const string Letter = "Dear Dr A. Sample, Margaret Okafor was seen today at Sample Oncology Clinic.";

    [Fact]
    public async Task ASend_PerKind_PerReleasedDeliverable()
    {
        IReadOnlyList<DeliverableOutcome> outcomes =
        [
            Outcome("deliverable.note", ["email", "pdf"]),
            Outcome("deliverable.draft", ["email", "pdf"], DeliverableState.Held),
            Outcome("deliverable.kept", []),
            Outcome("deliverable.summary", ["pdf", "email"]),
        ];

        var sends = DeliveryPlan.Sends(outcomes);
        Assert.Equal(
            ["delivery:deliverable.note/email", "delivery:deliverable.note/pdf", "delivery:deliverable.summary/email", "delivery:deliverable.summary/pdf"],
            sends.Select(FakeDeliveryHost.NameOf));
        Assert.All(sends, s => Assert.Equal(1, s.Attempt));
        Assert.All(sends, s => Assert.Null(s.Bundle));
        var document = Assert.Single(sends[0].Documents);
        Assert.Equal(new DeliveryDocument("deliverable.note", "Label of deliverable.note", "plaintext", Letter), document);

        var host = new FakeDeliveryHost();
        var (result, recorder) = await RunAsync(new RunLimits(), host, outcomes);

        Assert.Equal(sends.Select(FakeDeliveryHost.NameOf), host.Sends.Select(s => FakeDeliveryHost.NameOf(s.Send)));
        Assert.Equal(sends.Select(FakeDeliveryHost.NameOf), result.Rows.Select(r => r.Key));
        Assert.All(result.Rows, r =>
        {
            Assert.Equal("sent", r.Result);
            Assert.Null(r.Reason);
            Assert.Equal(FakeDeliveryHost.Start, r.AtUtc);
            Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "sent")], r.Attempts);
        });
        Assert.Equal(["email", "pdf", "email", "pdf"], result.Rows.Select(r => r.Kind));
        Assert.Equal(["deliverable.note", "deliverable.note", "deliverable.summary", "deliverable.summary"], result.Rows.Select(r => r.Deliverable));
        Assert.Equal(0, result.Failed);
        Assert.Equal(result.Rows, recorder.Deliveries);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task ABundle_IsOneSendPerKind_OfItsReleasedDeliverables()
    {
        // D11: the blocked one is left out of the bundle; each of the two released gets its own row, with the send's result.
        IReadOnlyList<DeliverableOutcome> outcomes =
        [
            Outcome("deliverable.referral_pack.gp_letter", ["email"], folder: "referral_pack", bundle: "zip"),
            Outcome("deliverable.referral_pack.risks", ["email"], DeliverableState.Blocked, folder: "referral_pack", bundle: "zip"),
            Outcome("deliverable.referral_pack.summary", ["email"], folder: "referral_pack", bundle: "zip"),
        ];

        var host = new FakeDeliveryHost();
        var (result, recorder) = await RunAsync(new RunLimits(), host, outcomes);

        var (send, _) = Assert.Single(host.Sends);
        Assert.Equal(("email", "zip", "referral_pack", 1), (send.Kind, send.Bundle, send.Folder, send.Attempt));
        Assert.Equal(["deliverable.referral_pack.gp_letter", "deliverable.referral_pack.summary"], send.Documents.Select(d => d.Deliverable));

        Assert.Equal(["delivery:deliverable.referral_pack.gp_letter/email", "delivery:deliverable.referral_pack.summary/email"], result.Rows.Select(r => r.Key));
        Assert.All(result.Rows, r => Assert.Equal("sent", r.Result));
        Assert.Equal(result.Rows[0].Attempts, result.Rows[1].Attempts);
        Assert.Equal(result.Rows, recorder.Deliveries);
    }

    [Fact]
    public async Task NotSetUp_IsRecordedAtOnce_AndNeverRetried()
    {
        // Review Focus 5: one kind set up, one not; nothing is retried for the second.
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["email", "emr"])];
        var host = new FakeDeliveryHost()
            .Script("delivery:deliverable.note/emr", new DeliverySendOutcome(DeliverySendResult.NotSetUp, null));

        var (result, _) = await RunAsync(new RunLimits(), host, outcomes);

        Assert.Equal(["delivery:deliverable.note/email", "delivery:deliverable.note/emr"], host.Sends.Select(s => FakeDeliveryHost.NameOf(s.Send)));
        Assert.Equal("sent", result.Rows[0].Result);
        var row = result.Rows[1];
        Assert.Equal(("not sent", "not sent: EMR isn't set up for this account"), (row.Result, row.Reason));
        V31Contract.AssertSentence(new SourceRefusal("S08-006", row.Reason!, ""));
        Assert.Null(row.AtUtc);
        Assert.Empty(row.Attempts);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task AFailedSend_IsRetried_ThenFailed()
    {
        // D12: 2 minutes before attempt 2, 4 before attempt 3; then the attempts are spent.
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["email"])];
        var host = new FakeDeliveryHost()
            .Script("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.Failed, "timeout"));
        var recorder = new ListRecorder();
        var runner = new DeliveryRunner(new RunLimits(DeliveryAttempts: 3), host, recorder);

        var result = await FakeDeliveryHost.Drive(() =>
        {
            var run = runner.RunAsync(outcomes);
            host.Advance(TimeSpan.FromMinutes(2));
            host.Advance(TimeSpan.FromMinutes(4));
            return run;
        });

        var start = FakeDeliveryHost.Start;
        Assert.Equal([start, start.AddMinutes(2), start.AddMinutes(6)], host.Sends.Select(s => s.AtUtc));
        Assert.Equal([1, 2, 3], host.Sends.Select(s => s.Send.Attempt));
        var row = Assert.Single(result.Rows);
        Assert.Equal(("failed", "failed: email"), (row.Result, row.Reason));
        Assert.Null(row.AtUtc);
        Assert.Equal(
            [new DeliveryAttempt(start, "failed: timeout"), new DeliveryAttempt(start.AddMinutes(2), "failed: timeout"), new DeliveryAttempt(start.AddMinutes(6), "failed: timeout")],
            row.Attempts);
        Assert.Equal(1, result.Failed);
        Assert.Equal(result.Rows, recorder.Deliveries);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task TheWindow_ClosesOnWhatIsUnsent()
    {
        // Review Focus 4: the note's wait before attempt 3 (4 minutes) is cut to the 3 the window has left, so it ends at the
        // close and attempt 3 never starts. The letter's send is still in flight at the close and is waited for (#1508); it
        // then fails with its own error kind. Both are failed, and no timer is left outstanding.
        IReadOnlyList<DeliverableOutcome> outcomes =
        [
            Outcome("deliverable.note", ["email"]),
            Outcome("deliverable.letter", ["email"]),
        ];
        var host = new FakeDeliveryHost()
            .Script("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.Failed, "error"))
            .Hold("delivery:deliverable.letter/email");
        var recorder = new ListRecorder();
        var runner = new DeliveryRunner(new RunLimits(DeliveryMinutes: 5), host, recorder);

        var result = await FakeDeliveryHost.Drive(() =>
        {
            var run = runner.RunAsync(outcomes);
            host.Advance(TimeSpan.FromMinutes(2));
            Assert.False(run.IsCompleted);
            host.Advance(TimeSpan.FromMinutes(3));
            Assert.False(run.IsCompleted);
            host.Answer("delivery:deliverable.letter/email", new DeliverySendOutcome(DeliverySendResult.Failed, "error"));
            return run;
        });

        Assert.Equal(
            ["delivery:deliverable.note/email", "delivery:deliverable.letter/email", "delivery:deliverable.note/email"],
            host.Sends.Select(s => FakeDeliveryHost.NameOf(s.Send)));
        Assert.Equal(["delivery:deliverable.note/email", "delivery:deliverable.letter/email"], result.Rows.Select(r => r.Key));
        Assert.All(result.Rows, r => Assert.Equal(("failed", "failed: email"), (r.Result, r.Reason)));
        Assert.Equal(2, result.Rows[0].Attempts.Count);
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "failed: error")], result.Rows[1].Attempts);
        Assert.Equal(2, result.Failed);
        Assert.Equal(result.Rows, recorder.Deliveries);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task ASendInFlightAtTheWindowsClose_IsWaitedFor_AndRecordedAsItEnded()
    {
        // #1508: a Durable activity is not cancelled, so the email can still go out after the window's timer ends. The runner
        // waits for it: the row is sent, never failed: window, and nothing is recorded until it has landed.
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["email"])];
        var host = new FakeDeliveryHost().Hold("delivery:deliverable.note/email");
        var recorder = new ListRecorder();
        var runner = new DeliveryRunner(new RunLimits(DeliveryMinutes: 5), host, recorder);

        var result = await FakeDeliveryHost.Drive(() =>
        {
            var run = runner.RunAsync(outcomes);
            host.Advance(TimeSpan.FromMinutes(5));
            Assert.False(run.IsCompleted);
            Assert.Empty(recorder.Deliveries);
            host.Answer("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.Sent, null));
            return run;
        });

        var row = Assert.Single(result.Rows);
        Assert.Equal(("sent", null, FakeDeliveryHost.Start), (row.Result, row.Reason, row.AtUtc));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "sent")], row.Attempts);
        Assert.Equal(0, result.Failed);
        Assert.Equal(result.Rows, recorder.Deliveries);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task ASendInFlightThatFails_AfterTheClose_IsFailedWithItsOwnReason_AndNotRetried()
    {
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["email"])];
        var host = new FakeDeliveryHost().Hold("delivery:deliverable.note/email");
        var runner = new DeliveryRunner(new RunLimits(DeliveryMinutes: 5), host, new ListRecorder());

        var result = await FakeDeliveryHost.Drive(() =>
        {
            var run = runner.RunAsync(outcomes);
            host.Advance(TimeSpan.FromMinutes(5));
            Assert.False(run.IsCompleted);
            host.Answer("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.Failed, "unreachable"));
            return run;
        });

        Assert.Single(host.Sends);
        var row = Assert.Single(result.Rows);
        Assert.Equal(("failed", "failed: email"), (row.Result, row.Reason));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "failed: unreachable")], row.Attempts);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task ADestinationNeverStarted_AtTheClose_IsFailedWindow()
    {
        // The window is already closed when delivery starts: no attempt starts, and the destination is failed.
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["email"])];
        var host = new FakeDeliveryHost();
        var runner = new DeliveryRunner(new RunLimits(DeliveryMinutes: 0), host, new ListRecorder());

        var result = await FakeDeliveryHost.Drive(() => runner.RunAsync(outcomes));

        Assert.Empty(host.Sends);
        var row = Assert.Single(result.Rows);
        Assert.Equal(("failed", "failed: email"), (row.Result, row.Reason));
        Assert.Empty(row.Attempts);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task ASendInFlightAtTheWindowsClose_IsWaitedFor_WhileADestinationNeverStarted_IsFailedWithNoAttempt()
    {
        // One run, both cases. The note's email fails, and its retry wait (2 minutes) ends at the close of a 2-minute window
        // before attempt 2 can start: it never gets another attempt. The letter is in flight at the close and goes out.
        IReadOnlyList<DeliverableOutcome> outcomes =
        [
            Outcome("deliverable.letter", ["email"]),
            Outcome("deliverable.note", ["email"]),
        ];
        var host = new FakeDeliveryHost()
            .Hold("delivery:deliverable.letter/email")
            .Hold("delivery:deliverable.note/email");
        var runner = new DeliveryRunner(new RunLimits(DeliveryMinutes: 2), host, new ListRecorder());

        var result = await FakeDeliveryHost.Drive(() =>
        {
            var run = runner.RunAsync(outcomes);
            host.Answer("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.Failed, "error"));
            host.Advance(TimeSpan.FromMinutes(2));
            Assert.False(run.IsCompleted);
            host.Answer("delivery:deliverable.letter/email", new DeliverySendOutcome(DeliverySendResult.Sent, null));
            return run;
        });

        Assert.Equal(2, host.Sends.Count);
        var (letter, note) = (result.Rows[0], result.Rows[1]);
        Assert.Equal(("sent", null), (letter.Result, letter.Reason));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "sent")], letter.Attempts);
        Assert.Equal(("failed", "failed: email"), (note.Result, note.Reason));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "failed: error")], note.Attempts);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task ASend_ThatLandsAtOrAfterTheClose_IsSent(int minutes)
    {
        // Under Durable Functions the clock is the replay step's: a send that finished before the close can be read by a step
        // that starts at or after it. A send that went out is sent, whatever the clock says when it is read.
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["email"])];
        var host = new FakeDeliveryHost().Hold("delivery:deliverable.note/email");
        var runner = new DeliveryRunner(new RunLimits(DeliveryMinutes: 5), host, new ListRecorder());

        var result = await FakeDeliveryHost.Drive(() =>
        {
            var run = runner.RunAsync(outcomes);
            host.MoveClock(TimeSpan.FromMinutes(minutes));
            host.Answer("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.Sent, null));
            return run;
        });

        var row = Assert.Single(result.Rows);
        Assert.Equal(("sent", null, FakeDeliveryHost.Start), (row.Result, row.Reason, row.AtUtc));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "sent")], row.Attempts);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task AFailure_ThatLandsAtOrAfterTheClose_KeepsItsErrorKind_AndIsNotRetried(int minutes)
    {
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["email"])];
        var host = new FakeDeliveryHost().Hold("delivery:deliverable.note/email");
        var runner = new DeliveryRunner(new RunLimits(DeliveryMinutes: 5), host, new ListRecorder());

        var result = await FakeDeliveryHost.Drive(() =>
        {
            var run = runner.RunAsync(outcomes);
            host.MoveClock(TimeSpan.FromMinutes(minutes));
            host.Answer("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.Failed, "unreachable"));
            return run;
        });

        Assert.Single(host.Sends);
        var row = Assert.Single(result.Rows);
        Assert.Equal(("failed", "failed: email"), (row.Result, row.Reason));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "failed: unreachable")], row.Attempts);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Theory]
    [InlineData("a retry wait")]
    [InlineData("the window")]
    public async Task ASend_ThatFinishedInTheStepThatCloses_IsSent(string closedBy)
    {
        // Under Durable Functions a step reads what finished before it in one go. The note's retry wait (due at +2, read by a
        // step whose clock is past the close) or the window's timer ends delivery in the same step in which the letter's
        // send finished, unread: the letter went out, so it is sent, never failed: window.
        IReadOnlyList<DeliverableOutcome> outcomes =
        [
            Outcome("deliverable.note", ["email"]),
            Outcome("deliverable.letter", ["email"]),
        ];
        var host = new FakeDeliveryHost()
            .Script("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.Failed, "error"))
            .Hold("delivery:deliverable.letter/email");
        var runner = new DeliveryRunner(new RunLimits(DeliveryMinutes: 5), host, new ListRecorder());

        var result = await FakeDeliveryHost.DriveInSteps(
            () => runner.RunAsync(outcomes),
            () =>
            {
                if (closedBy == "a retry wait")
                {
                    host.MoveClock(TimeSpan.FromMinutes(5));
                    host.EndDelaysDueBy(TimeSpan.FromMinutes(2));
                }
                else
                {
                    host.Advance(TimeSpan.FromMinutes(5));
                }

                host.Answer("delivery:deliverable.letter/email", new DeliverySendOutcome(DeliverySendResult.Sent, null));
            });

        Assert.Equal(2, host.Sends.Count);
        Assert.Equal(["delivery:deliverable.note/email", "delivery:deliverable.letter/email"], result.Rows.Select(r => r.Key));
        var (note, letter) = (result.Rows[0], result.Rows[1]);
        Assert.Equal(("failed", "failed: email"), (note.Result, note.Reason));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "failed: error")], note.Attempts);
        Assert.Equal(("sent", null, FakeDeliveryHost.Start), (letter.Result, letter.Reason, letter.AtUtc));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "sent")], letter.Attempts);
        Assert.Equal(1, result.Failed);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public async Task ASecondAttempt_ThatSends_IsSent()
    {
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["pdf"])];
        var host = new FakeDeliveryHost().Script(
            "delivery:deliverable.note/pdf",
            new DeliverySendOutcome(DeliverySendResult.Failed, "unreachable"),
            new DeliverySendOutcome(DeliverySendResult.Sent, null));
        var runner = new DeliveryRunner(new RunLimits(), host, new ListRecorder());

        var result = await FakeDeliveryHost.Drive(() =>
        {
            var run = runner.RunAsync(outcomes);
            host.Advance(TimeSpan.FromMinutes(2));
            return run;
        });

        var row = Assert.Single(result.Rows);
        var at = FakeDeliveryHost.Start.AddMinutes(2);
        Assert.Equal(("sent", null, at), (row.Result, row.Reason, row.AtUtc));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "failed: unreachable"), new DeliveryAttempt(at, "sent")], row.Attempts);
        Assert.Equal(0, result.Failed);
        Assert.Equal(0, host.OutstandingDelays);
    }

    [Fact]
    public void AFailedJob_RecordsNotSent()
    {
        IReadOnlyList<DeliverableOutcome> outcomes =
        [
            Outcome("deliverable.note", ["email", "emr"]),
            Outcome("deliverable.draft", ["email"], DeliverableState.Held),
            Outcome("deliverable.referral_pack.gp_letter", ["email"], folder: "referral_pack", bundle: "zip"),
            Outcome("deliverable.referral_pack.summary", ["email"], folder: "referral_pack", bundle: "zip"),
        ];

        var rows = DeliveryPlan.NotSentJobFailed(outcomes);

        Assert.Equal(
            [
                "delivery:deliverable.note/email", "delivery:deliverable.note/emr",
                "delivery:deliverable.referral_pack.gp_letter/email", "delivery:deliverable.referral_pack.summary/email",
            ],
            rows.Select(r => r.Key));
        Assert.All(rows, r =>
        {
            Assert.Equal(("not sent", "not sent: the job failed"), (r.Result, r.Reason));
            V31Contract.AssertSentence(new SourceRefusal("S06-038", r.Reason!, ""));
            Assert.Null(r.AtUtc);
            Assert.Empty(r.Attempts);
        });
    }

    // ---- #1507 (area 10; R8, ruled by the owner on #1507, 2026-11-22; D17, D19): delivery keeps 30's promise ----------

    [Theory]
    [InlineData("A10-054", "no document password")]
    [InlineData("A10-055", "attachments over the budget")]
    public async Task ALinkOnlySend_IsRecordedAsSentLinkOnly(string id, string reason)
    {
        // D17: a pdf send that fell back to the link-only email is sent; its attempt says sent link-only, its row why.
        Assert.Equal(reason, id == "A10-054" ? DeliveryPlan.NoDocumentPassword : DeliveryPlan.OverTheBudget);
        IReadOnlyList<DeliverableOutcome> outcomes = [Outcome("deliverable.note", ["pdf"])];
        var host = new FakeDeliveryHost()
            .Script("delivery:deliverable.note/pdf", new DeliverySendOutcome(DeliverySendResult.Sent, null, reason));

        var (result, recorder) = await RunAsync(new RunLimits(), host, outcomes);

        var row = Assert.Single(result.Rows);
        Assert.Equal(("sent", reason, FakeDeliveryHost.Start), (row.Result, row.Reason, row.AtUtc));
        V31Contract.AssertSentence(new SourceRefusal(id, row.Reason!, ""));
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "sent link-only")], row.Attempts);
        Assert.Single(host.Sends);
        Assert.Equal(0, result.Failed);
        Assert.Equal(result.Rows, recorder.Deliveries);
        AssertTheRecordsContractHolds(row);
    }

    [Fact]
    public async Task AJobWhoseEveryDeliverableIsBlocked_SendsTheLinkOnlyEmail()
    {
        // D19, ruled, and the controller's ruling on Task 22's review: nothing released, and a deliverable blocked or held:
        // one link-only send for the job (email, as one of them lists it), carrying their labels and never their text; each
        // mail destination each lists gets a row from that send. An emr-only one and a skipped one are not in it.
        IReadOnlyList<DeliverableOutcome> outcomes =
        [
            Outcome("deliverable.note", ["pdf"], DeliverableState.Blocked),
            Outcome("deliverable.kept", ["pdf"], DeliverableState.Skipped),
            Outcome("deliverable.emr_copy", ["emr"], DeliverableState.Blocked),
            Outcome("deliverable.patient.patient_letter", ["email", "pdf", "emr"], DeliverableState.Held, folder: "patient"),
        ];

        var send = Assert.Single(DeliveryPlan.Sends(outcomes));

        Assert.Equal(("email", true, null, "", 1), (send.Kind, send.EveryDeliverableBlocked, send.Bundle, send.Folder, send.Attempt));
        Assert.Equal(["deliverable.note", "deliverable.patient.patient_letter"], send.Documents.Select(d => d.Deliverable));
        Assert.All(send.Documents, d =>
        {
            Assert.Equal($"Label of {d.Deliverable}", d.Label);
            Assert.Equal("", d.Text);
        });

        var host = new FakeDeliveryHost();
        var (result, recorder) = await RunAsync(new RunLimits(), host, outcomes);

        Assert.Single(host.Sends);
        Assert.Equal(
            ["delivery:deliverable.note/pdf", "delivery:deliverable.patient.patient_letter/email", "delivery:deliverable.patient.patient_letter/pdf"],
            result.Rows.Select(r => r.Key));
        Assert.Equal(["pdf", "email", "pdf"], result.Rows.Select(r => r.Kind));
        Assert.All(result.Rows, r =>
        {
            Assert.Equal(("sent", "every deliverable blocked", FakeDeliveryHost.Start), (r.Result, r.Reason, r.AtUtc));
            V31Contract.AssertSentence(new SourceRefusal("A10-056", r.Reason!, ""));
            Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "sent link-only")], r.Attempts);
            AssertTheRecordsContractHolds(r);
        });
        Assert.Equal(result.Rows, recorder.Deliveries);

        // pdf only: the send is pdf's
        Assert.Equal("pdf", Assert.Single(DeliveryPlan.Sends([outcomes[0]])).Kind);

        // the one send not going: each row says it in its own kind
        var failing = new FakeDeliveryHost().Script("delivery:deliverable.note/email", new DeliverySendOutcome(DeliverySendResult.NotSetUp, null));
        var (notSent, _) = await RunAsync(new RunLimits(), failing, outcomes);
        Assert.Equal(
            ["not sent: PDF isn't set up for this account", "not sent: Email isn't set up for this account", "not sent: PDF isn't set up for this account"],
            notSent.Rows.Select(r => r.Reason));

        // a job with one deliverable released sends that one only, as before; one with nothing blocked or held sends nothing
        Assert.Equal(["delivery:deliverable.letter/pdf"], DeliveryPlan.Sends([.. outcomes, Outcome("deliverable.letter", ["pdf"])]).Select(FakeDeliveryHost.NameOf));
        Assert.All(DeliveryPlan.Sends([.. outcomes, Outcome("deliverable.letter", ["pdf"])]), s => Assert.False(s.EveryDeliverableBlocked));
        Assert.Empty(DeliveryPlan.Sends([Outcome("deliverable.kept", ["pdf"], DeliverableState.Skipped), Outcome("deliverable.emr_copy", ["emr"], DeliverableState.Blocked)]));

        // an orchestration started before this build (no Delivery in its input) replays without it: no send, no row
        Assert.Empty(DeliveryPlan.Sends(outcomes, blockedEmail: false));
        var (old, oldRecorder) = await RunAsync(new RunLimits(), new FakeDeliveryHost(), outcomes, blockedEmail: false);
        Assert.Empty(old.Rows);
        Assert.Empty(oldRecorder.Deliveries);
    }

    /// <summary>
    /// D17 (the record's contract, provenance-record.md § 6.10 and § 6.13): the row has exactly the delivery row's members, its
    /// result is one of the three, a <c>sent link-only</c> attempt and its reason are words the contract lists.
    /// </summary>
    private static void AssertTheRecordsContractHolds(DeliveryRow row)
    {
        Assert.Equal(["key", "deliverable", "kind", "result", "reason", "atUtc", "attempts"], row.ToJson().Select(m => m.Key));
        Assert.Contains(row.Result, new[] { "sent", "not sent", "failed" });
        var contract = File.ReadAllText(Path.Combine(RepoRoot(), "external", "consultologist-provenance", "provenance-record.md"));
        var section = contract[contract.IndexOf("## 6. Storage 12", StringComparison.Ordinal)..];
        var deliveryRow = section[section.IndexOf("**A delivery row**", StringComparison.Ordinal)..];
        deliveryRow = deliveryRow[..deliveryRow.IndexOf("**Replay:**", StringComparison.Ordinal)];
        Assert.All(row.Attempts, a => Assert.Contains($"`{a.Outcome}`", deliveryRow, StringComparison.Ordinal));
        if (row.Reason is { } reason)
        {
            var recorded = section[section.IndexOf("### 6.13 Recorded sentences", StringComparison.Ordinal)..];
            Assert.Contains($"*{reason}*", recorded, StringComparison.Ordinal);
            Assert.Contains($"*{reason}*", deliveryRow, StringComparison.Ordinal);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Consultologist.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    [Fact]
    public void KindWords_AndKeys_AreD13s()
    {
        Assert.Equal(["Email", "PDF", "EMR", "Zip"], DeliveryPlan.Kinds.Select(DeliveryPlan.KindWords));
        Assert.Equal("delivery:deliverable.patient.patient_letter/email", DeliveryPlan.Key("deliverable.patient.patient_letter", "email"));
    }

    private static DeliverableOutcome Outcome(
        string key, IReadOnlyList<string> delivery, DeliverableState state = DeliverableState.Released, string folder = "", string? bundle = null) =>
        new(key, $"Label of {key}", "plaintext", folder, state, null, null, Letter, CanonicalJson.HashText(Letter), delivery, bundle);

    private static async Task<(DeliveryResult Result, ListRecorder Recorder)> RunAsync(
        RunLimits limits, FakeDeliveryHost host, IReadOnlyList<DeliverableOutcome> outcomes, bool blockedEmail = true)
    {
        var recorder = new ListRecorder();
        var result = await FakeDeliveryHost.Drive(() => new DeliveryRunner(limits, host, recorder).RunAsync(outcomes, blockedEmail));
        return (result, recorder);
    }
}
