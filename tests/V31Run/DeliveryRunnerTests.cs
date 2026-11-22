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
        // close and attempt 3 never starts; the letter's send is still in flight at the close. Both are failed, and no timer
        // is left outstanding.
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
            return run;
        });

        Assert.Equal(
            ["delivery:deliverable.note/email", "delivery:deliverable.letter/email", "delivery:deliverable.note/email"],
            host.Sends.Select(s => FakeDeliveryHost.NameOf(s.Send)));
        Assert.Equal(["delivery:deliverable.note/email", "delivery:deliverable.letter/email"], result.Rows.Select(r => r.Key));
        Assert.All(result.Rows, r => Assert.Equal(("failed", "failed: email"), (r.Result, r.Reason)));
        Assert.Equal(2, result.Rows[0].Attempts.Count);
        Assert.Equal([new DeliveryAttempt(FakeDeliveryHost.Start, "failed: window")], result.Rows[1].Attempts);
        Assert.Equal(2, result.Failed);
        Assert.Equal(result.Rows, recorder.Deliveries);
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
        RunLimits limits, FakeDeliveryHost host, IReadOnlyList<DeliverableOutcome> outcomes)
    {
        var recorder = new ListRecorder();
        var result = await FakeDeliveryHost.Drive(() => new DeliveryRunner(limits, host, recorder).RunAsync(outcomes));
        return (result, recorder);
    }
}
