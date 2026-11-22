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
/// <c>depth</c>). Never a value from an input or a model.
/// </summary>
public sealed record RunRow(string Key, string EntryKey, PlanKind Kind, RowState State, string? Reason, string? ValueHash, JsonObject Detail);

/// <summary>
/// #1503 (specVersion 31, area 06; part 08 § 5): one condition decided. <paramref name="Site"/> is the owner's row key,
/// <paramref name="Member"/> the decision's site in part 08 § 5.2's words (<c>self</c> for a header <c>if</c>),
/// <paramref name="Printed"/> the condition as the printer writes it, <paramref name="Result"/> its result (null for a
/// <c>case</c>), <paramref name="Branch"/> a <c>case</c>'s branch taken, and the time from the host's clock.
/// </summary>
public sealed record RunDecision(string Site, string Member, string Printed, bool? Result, string? Branch, DateTime DecidedAtUtc);

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
}
