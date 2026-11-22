using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>#1503 (specVersion 31, area 06; part 08): how an instance's row ended.</summary>
public enum RowState
{
    /// <summary>It ran and has a value.</summary>
    Completed,

    /// <summary>Its <c>if</c> was false or <c>null</c>, or its branch had nothing to run.</summary>
    Skipped,

    /// <summary>It was never reached (a loop's items beyond its <c>max</c>, a branch not taken).</summary>
    NotRun,

    /// <summary>It failed, and its job with it.</summary>
    Failed,

    /// <summary>It was still running when the job's time ran out.</summary>
    Cancelled,

    /// <summary>#1505 (area 08; part 06 § 7): a deliverable composed and released (its check passed, was skipped, or it has none).</summary>
    Released,

    /// <summary>#1505 (area 08; part 06 § 6.6): a deliverable composed empty under <c>whenEmpty: hold</c>.</summary>
    Held,

    /// <summary>#1505 (area 08; part 06 § 7; R2): a deliverable composed and withheld: its gate did not pass, or could not run.</summary>
    Blocked,
}

/// <summary>
/// #1503 (specVersion 31, area 06): one finished row, as area 09 turns it into a storage row: the instance's key
/// (<see cref="RunKeys.Of"/>), its entry's key and kind, its state, the reason for a state other than completed, the hash of
/// its <c>.content</c>, and <paramref name="Detail"/> (counts, <c>stopped</c>, retries, <c>itemLabel</c>, <c>function</c>,
/// <c>depth</c>; #1506: the entry's facts and what only the runner knows, in part 08's names). Never a value from an input
/// or a model. #1506 (area 09; D2): <paramref name="Text"/> is the row's settled text, which never enters the row itself
/// and goes to the retained text: <c>content</c> (the value's <c>.content</c>), <c>value</c> (the value's canonical JSON when
/// it is not text), <c>itemName</c> (an item's name not drawn from package text) and <c>spots</c> (<c>{ "&lt;spot id&gt;":
/// output }</c> for each run spot whose row <c>output</c> is <c>null</c>), and on a check's row <c>uncovered</c> and
/// <c>untested</c> (its <c>check_result</c>'s lists, which come from inputs; the row holds their hashes and sizes);
/// <c>null</c> when there is none.
/// </summary>
public sealed record RunRow(string Key, string EntryKey, PlanKind Kind, RowState State, string? Reason, string? ValueHash, JsonObject Detail, JsonObject? Text = null)
{
    /// <summary>#1506 (area 09; part 08 § 2.2): the record's section the row is in, from its key's root: <c>node</c>, <c>check</c> or <c>deliverable</c>.</summary>
    public string Section => Key.StartsWith("check.", StringComparison.Ordinal) ? "check"
        : Key.StartsWith("deliverable.", StringComparison.Ordinal) ? "deliverable"
        : "node";

    /// <summary>
    /// #1506 (area 09; part 08 §§ 4, 6, 7, 10): the row in part 08's shape for its <see cref="Section"/>, every member written
    /// (<see cref="RecordRows"/>): a check's is part 08 § 6's (<see cref="RecordRows.Check"/>); a deliverable's is a node's for
    /// now (area 09's Task 6).
    /// </summary>
    public JsonObject ToJson() => Section switch
    {
        "check" => RecordRows.Check(this),
        _ => RecordRows.Node(this),
    };
}

/// <summary>#1506 (specVersion 31, area 09; part 08 § 5.1; D4): one value a decision's rule read: its path as <c>resolve.json</c> writes it, and the canonical hash of the value (<c>null</c> hashes as <c>null</c>). Never the value.</summary>
public sealed record RunRead(string Path, string Hash);

/// <summary>
/// #1503 (specVersion 31, area 06; part 08 § 5); #1506 (area 09; R6, D3, D4): one condition decided, with part 08 § 5.1's
/// members. <paramref name="Owner"/> is the row key (check row key, deliverable key) it concerns, <paramref name="Site"/>
/// what was decided in part 08 § 5.2's words (<c>self</c>, <c>case</c>, <c>pass.&lt;n&gt;</c>, <c>until.&lt;i&gt;</c>,
/// <c>macro.&lt;id&gt;</c>, …), <paramref name="Condition"/> the condition as the printer writes it (a <c>case</c>'s
/// subject), <paramref name="ExpressionHash"/> the hash of its canonical JSON Logic, <paramref name="Result"/> its result
/// (null for a <c>case</c>), <paramref name="Branch"/> a <c>case</c>'s arm taken (its 1-based position as text, or
/// <c>else</c>), <paramref name="Reads"/> the paths its own rule reads with the hash of each value read, its two flags, and
/// the time from the host's clock.
/// </summary>
public sealed record RunDecision(
    string Owner,
    string Site,
    string Condition,
    string ExpressionHash,
    bool? Result,
    string? Branch,
    IReadOnlyList<RunRead> Reads,
    bool RunDependent,
    bool ModelDerived,
    DateTime DecidedAtUtc)
{
    /// <summary>The decision's id: <c>&lt;owner&gt;/if:&lt;site&gt;</c> (part 08 § 5.1).</summary>
    public string Id => $"{Owner}/if:{Site}";

    /// <summary>
    /// The decision as part 08 § 5.1 writes it, its ten members in that order: <c>result</c> the boolean, or a <c>case</c>'s
    /// arm as text (D3); <c>reads</c> <c>[{ path, hash }]</c>; <c>decidedAtUtc</c> the round-trip UTC text.
    /// </summary>
    public JsonObject ToJson()
    {
        var reads = new JsonArray();
        foreach (var read in Reads)
            reads.Add(new JsonObject { ["path"] = read.Path, ["hash"] = read.Hash });
        var at = DecidedAtUtc.Kind switch
        {
            DateTimeKind.Utc => DecidedAtUtc,
            DateTimeKind.Local => DecidedAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(DecidedAtUtc, DateTimeKind.Utc),
        };
        return new JsonObject
        {
            ["id"] = Id,
            ["owner"] = Owner,
            ["site"] = Site,
            ["condition"] = Condition,
            ["expressionHash"] = ExpressionHash,
            ["result"] = Result is { } result ? JsonValue.Create(result) : JsonValue.Create(Branch),
            ["reads"] = reads,
            ["runDependent"] = RunDependent,
            ["modelDerived"] = ModelDerived,
            ["decidedAtUtc"] = at.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): something the operator is told of while the job runs.
/// <paramref name="Kind"/> is <c>capacity-wait</c> (the job's 429 waits passed <see cref="PlanRunner.CapacityAlertMinutes"/>
/// in all; <paramref name="Key"/> is the row whose wait passed it, <paramref name="Minutes"/> the waits in all, in whole
/// minutes) or <c>job-slow</c> (the job reached <see cref="PlanRunner.SlowJobPercent"/> of its <c>jobMinutes</c>;
/// <paramref name="Key"/> null, <paramref name="Minutes"/> the minutes run). Each is raised at most once per run. Never a
/// value from an input or a model.
/// </summary>
public sealed record RunNotice(string Kind, string? Key, int Minutes);

/// <summary>
/// #1503 (specVersion 31, area 06): where the run's rows and decisions go (area 09 stores them). The runner awaits each task
/// inline, in the orchestration, so an implementation's tasks must complete synchronously (a Durable entity signal,
/// telemetry) or be durable, replay-safe tasks the orchestration's context gave; never other I/O.
/// </summary>
public interface IRunRecorder
{
    /// <summary>Records a finished row.</summary>
    Task RowAsync(RunRow row);

    /// <summary>Records a decision.</summary>
    Task DecisionAsync(RunDecision decision);

    /// <summary>#1505 (area 08; part 08 § 10): records a destination's delivery row, once its result is final (<see cref="DeliveryRunner"/>).</summary>
    Task DeliveryAsync(DeliveryRow row);

    /// <summary>
    /// #1505 (area 08; part 09 § 3.9; D14): records a notice for the operator (<see cref="RunNotice"/>). An implementation
    /// that alerts the operator never fails the run for an alert that could not be sent.
    /// </summary>
    Task NoticeAsync(RunNotice notice);
}
