namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8): one released deliverable as a send carries it: its row key, its
/// <c>label</c>, its format's name and its composed text.
/// </summary>
public sealed record DeliveryDocument(string Deliverable, string Label, string Format, string Text);

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; D11): one send: one kind, for one deliverable, or for every released
/// deliverable of a bundle folder (<paramref name="Bundle"/> <c>zip</c>), in plan order. <paramref name="Folder"/> is
/// <c>""</c> for <c>deliverables/</c>, else the folder's id; <paramref name="Attempt"/> counts from 1. #1507 (area 10; D19,
/// ruled by the owner on #1507, 2026-11-22T21:38Z): <paramref name="EveryDeliverableBlocked"/> marks the link-only email of a
/// job that released nothing, its documents the blocked and held ones, each with its label and no text. Trailing and
/// optional, so a send written before it reads <c>false</c>.
/// </summary>
public sealed record DeliverySend(
    string Kind, string? Bundle, string Folder, IReadOnlyList<DeliveryDocument> Documents, int Attempt, bool EveryDeliverableBlocked = false);

/// <summary>#1505 (specVersion 31, area 08; D10, D12): how one attempt of a send ended.</summary>
public enum DeliverySendResult
{
    /// <summary>It went out.</summary>
    Sent,

    /// <summary>The account has not set the kind up (D10): final at once, never retried.</summary>
    NotSetUp,

    /// <summary>It did not go out; it is retried while attempts and the window are left (D12).</summary>
    Failed,
}

/// <summary>
/// #1505 (specVersion 31, area 08): what one attempt of a send gave: its result and, for a failure, the error's kind
/// (<c>"timeout"</c>, <c>"error"</c>, <c>"unreachable"</c>), never its content. #1507 (area 10; R8, D17, D19):
/// <paramref name="LinkOnly"/> is, for a send that went out as the link-only email in place of what was asked, why
/// (<see cref="DeliveryPlan.NoDocumentPassword"/>, <see cref="DeliveryPlan.OverTheBudget"/> or
/// <see cref="DeliveryPlan.EveryDeliverableBlocked"/>); <c>null</c> otherwise. Trailing and optional, so an outcome in a
/// history written before it reads <c>null</c>.
/// </summary>
public sealed record DeliverySendOutcome(DeliverySendResult Result, string? ErrorKind, string? LinkOnly = null);

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.8): everything outside delivery that <see cref="DeliveryRunner"/> talks to:
/// sends, timers and the clock. The engine's host is Durable Functions' (activities, durable timers, the replay-safe clock),
/// so the runner awaits no task but these, and reads no time but <see cref="UtcNow"/>.
/// </summary>
public interface IDeliveryHost
{
    /// <summary>The time now, from the host's replay-safe clock.</summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Makes one attempt of a send. A send's failure comes back as an outcome (<see cref="DeliverySendResult.Failed"/> with
    /// its error kind), and the task never faults: the host wraps its transport's failures. A fault is the host's own and
    /// passes through the runner.
    /// </summary>
    Task<DeliverySendOutcome> SendAsync(DeliverySend send);

    /// <summary>
    /// Waits <paramref name="wait"/> on the host's timer. When <paramref name="cancel"/> is cancelled first, the timer is
    /// cancelled and the task ends cancelled: an orchestration does not complete while a durable timer is outstanding, so the
    /// runner cancels every timer it made when delivery ends (as <see cref="IRunHost.DelayAsync"/>).
    /// </summary>
    Task DelayAsync(TimeSpan wait, CancellationToken cancel);
}

/// <summary>
/// #1505 (specVersion 31, area 08; part 08 § 10): one attempt on a destination: when it started, and <c>sent</c> or
/// <c>failed: &lt;error kind&gt;</c>; #1507 (D17): <c>sent link-only</c> for the link-only email.
/// </summary>
public sealed record DeliveryAttempt(DateTime AtUtc, string Outcome);

/// <summary>
/// #1505 (specVersion 31, area 08; part 08 § 10; D13): one destination's delivery row. <paramref name="Key"/> is
/// <c>delivery:&lt;deliverable key&gt;/&lt;kind&gt;</c>; <paramref name="Result"/> is <c>sent</c>, <c>not sent</c> or
/// <c>failed</c>; <paramref name="Reason"/> S08-006's sentence for a kind not set up, <c>not sent: the job failed</c>,
/// <c>failed: &lt;kind&gt;</c>, #1507 (D17, D19) why a send went as the link-only email (A10-054 to A10-056), or <c>null</c>
/// when sent as asked; <paramref name="AtUtc"/> the time of the attempt that sent it
/// (<c>null</c> otherwise); <paramref name="Attempts"/> each attempt made, in order.
/// </summary>
public sealed record DeliveryRow(
    string Key, string Deliverable, string Kind, string Result, string? Reason, DateTime? AtUtc, IReadOnlyList<DeliveryAttempt> Attempts)
{
    /// <summary>
    /// #1506 (area 09; part 08 § 10): the row as the record holds it: <c>key</c>, <c>deliverable</c>, <c>kind</c>,
    /// <c>result</c>, <c>reason</c>, <c>atUtc</c> (the round-trip UTC text, or null) and <c>attempts</c>
    /// (<c>[{ atUtc, outcome }]</c>). Never a value from an input or a model.
    /// </summary>
    public System.Text.Json.Nodes.JsonObject ToJson()
    {
        var attempts = new System.Text.Json.Nodes.JsonArray();
        foreach (var attempt in Attempts)
            attempts.Add(new System.Text.Json.Nodes.JsonObject { ["atUtc"] = Utc(attempt.AtUtc), ["outcome"] = attempt.Outcome });
        return new System.Text.Json.Nodes.JsonObject
        {
            ["key"] = Key,
            ["deliverable"] = Deliverable,
            ["kind"] = Kind,
            ["result"] = Result,
            ["reason"] = Reason,
            ["atUtc"] = AtUtc is { } at ? Utc(at) : null,
            ["attempts"] = attempts,
        };
    }

    private static string Utc(DateTime time) => (time.Kind switch
    {
        DateTimeKind.Utc => time,
        DateTimeKind.Local => time.ToUniversalTime(),
        _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
    }).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>#1505 (specVersion 31, area 08; part 06 § 8; D11, D13): what is sent where, and the rows of a job that failed.</summary>
public static class DeliveryPlan
{
    /// <summary>The kinds of destination, in the fixed order a deliverable's sends are made in.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["email", "pdf", "emr", "zip"];

    /// <summary>A kind as a sentence names it: <c>Email</c>, <c>PDF</c>, <c>EMR</c> or <c>Zip</c> (another as it is).</summary>
    public static string KindWords(string kind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        return kind switch
        {
            "email" => "Email",
            "pdf" => "PDF",
            "emr" => "EMR",
            "zip" => "Zip",
            _ => kind,
        };
    }

    /// <summary>A delivery row's key: <c>delivery:&lt;deliverable&gt;/&lt;kind&gt;</c>.</summary>
    public static string Key(string deliverable, string kind)
    {
        ArgumentNullException.ThrowIfNull(deliverable);
        ArgumentNullException.ThrowIfNull(kind);
        return $"delivery:{deliverable}/{kind}";
    }

    /// <summary>
    /// The first attempt of every send, in plan order then <see cref="Kinds"/> order: for a released deliverable, one per
    /// kind of its <c>delivery</c>; for a folder with <c>bundle: "zip"</c>, one per kind for all its released deliverables,
    /// at the place of its first (D11). A deliverable not released, or with <c>delivery: []</c>, has none; so has a bundle
    /// folder with nothing released. #1507 (area 10; D19, ruled by the owner on #1507, 2026-11-22T21:38Z): a job that
    /// released nothing, with a deliverable blocked or held that a mail kind (<c>email</c>, <c>pdf</c>) delivers, still sends
    /// 30's link-only email, once per job (the controller's ruling on Task 22's review): one send, of <c>email</c> when any of
    /// those lists it, else <c>pdf</c>, carrying each of them, in plan order, by its label and with no text
    /// (<see cref="DeliverySend.EveryDeliverableBlocked"/>); each mail destination each lists gets a row from that one send
    /// (<see cref="MailKindsOf"/>). <paramref name="blockedEmail"/> <c>false</c> leaves that send out: an orchestration
    /// started before #1507's build replays the sends it made then.
    /// </summary>
    public static IReadOnlyList<DeliverySend> Sends(IReadOnlyList<DeliverableOutcome> outcomes, bool blockedEmail = true)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var released = outcomes.Where(o => o is { State: DeliverableState.Released }).ToList();
        if (released.Count == 0)
            return blockedEmail ? Blocked(outcomes) : [];
        var sends = new List<DeliverySend>();
        var bundled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var outcome in released)
        {
            if (!IsBundled(outcome))
            {
                foreach (var kind in KindsOf(outcome))
                    sends.Add(new DeliverySend(kind, outcome.Bundle, outcome.Folder, [Document(outcome)], 1));
                continue;
            }

            if (!bundled.Add(outcome.Folder))
                continue;
            var documents = released
                .Where(o => IsBundled(o) && string.Equals(o.Folder, outcome.Folder, StringComparison.Ordinal))
                .Select(Document)
                .ToList();
            foreach (var kind in KindsOf(outcome))
                sends.Add(new DeliverySend(kind, outcome.Bundle, outcome.Folder, documents, 1));
        }

        return sends;
    }

    /// <summary>
    /// Part 06 § 8: a job that failed delivers nothing; each destination of each released deliverable, in plan order then
    /// <see cref="Kinds"/> order, is a row <c>not sent</c> with S06-038, <c>not sent: the job failed</c>.
    /// </summary>
    public static IReadOnlyList<DeliveryRow> NotSentJobFailed(IReadOnlyList<DeliverableOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        return outcomes
            .Where(o => o is { State: DeliverableState.Released })
            .SelectMany(o => KindsOf(o).Select(kind => new DeliveryRow(Key(o.Key, kind), o.Key, kind, NotSent, JobFailed, null, [])))
            .ToList();
    }

    internal const string Sent = "sent";
    internal const string NotSent = "not sent";
    internal const string Failed = "failed";

    /// <summary>#1507 (area 10; R8, D17): the attempt's outcome word for a send that went as the link-only email.</summary>
    internal const string SentLinkOnly = "sent link-only";

    /// <summary>#1507 (R8, D17) A10-054: a <c>pdf</c> send with no document password went as the link-only email, as 30's reply did.</summary>
    internal const string NoDocumentPassword = "no document password";

    /// <summary>#1507 (R8, D17) A10-055: a <c>pdf</c> send whose attachments passed the budget went as the link-only email, as 30's reply did.</summary>
    internal const string OverTheBudget = "attachments over the budget";

    /// <summary>#1507 (D19, ruled by the owner on #1507, 2026-11-22T21:38Z) A10-056: the link-only email of a job that released nothing.</summary>
    internal const string EveryDeliverableBlocked = "every deliverable blocked";

    /// <summary>The mail kinds, in <see cref="Kinds"/> order: those whose channel can send the link-only email.</summary>
    private static readonly string[] MailKinds = ["email", "pdf"];

    /// <summary>S06-038: a released deliverable of a job that failed.</summary>
    internal const string JobFailed = "not sent: the job failed";

    /// <summary>S08-006 (D13): a kind the account has not set up.</summary>
    internal static string NotSetUp(string kind) => $"not sent: {KindWords(kind)} isn't set up for this account";

    /// <summary>D13: a destination unsent after its attempts or when the window closed.</summary>
    internal static string FailedKind(string kind) => $"failed: {kind}";

    /// <summary>D19: the one link-only send of a job that released nothing, its blocked and held deliverables by label only.</summary>
    private static IReadOnlyList<DeliverySend> Blocked(IReadOnlyList<DeliverableOutcome> outcomes)
    {
        var withheld = outcomes
            .Where(o => o.State is DeliverableState.Blocked or DeliverableState.Held && MailKindsOf(o).Count > 0)
            .ToList();
        if (withheld.Count == 0)
            return [];
        var kind = MailKinds.First(k => withheld.Any(o => MailKindsOf(o).Contains(k, StringComparer.Ordinal)));
        var documents = withheld.Select(o => new DeliveryDocument(o.Key, o.Label, o.Format, "")).ToList();
        return [new DeliverySend(kind, null, "", documents, 1, EveryDeliverableBlocked: true)];
    }

    /// <summary>D19: the mail kinds (<c>email</c>, <c>pdf</c>) <paramref name="outcome"/>'s <c>delivery</c> lists, in <see cref="Kinds"/> order.</summary>
    internal static IReadOnlyList<string> MailKindsOf(DeliverableOutcome outcome) =>
        KindsOf(outcome).Where(k => MailKinds.Contains(k, StringComparer.Ordinal)).ToList();

    private static bool IsBundled(DeliverableOutcome outcome) => string.Equals(outcome.Bundle, "zip", StringComparison.Ordinal);

    private static IEnumerable<string> KindsOf(DeliverableOutcome outcome) =>
        Kinds.Where(kind => outcome.Delivery?.Contains(kind, StringComparer.Ordinal) == true);

    private static DeliveryDocument Document(DeliverableOutcome outcome) =>
        new(outcome.Key, outcome.Label, outcome.Format, outcome.Text ?? "");
}
