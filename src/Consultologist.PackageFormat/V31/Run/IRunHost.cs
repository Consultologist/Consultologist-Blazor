using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>#1503 (specVersion 31, area 06): how one attempt of an agent call ended.</summary>
public enum AgentOutcomeKind
{
    /// <summary>The model replied.</summary>
    Completed,

    /// <summary>The model deployment answered 429.</summary>
    Throttled,

    /// <summary>Another service failure: a 5xx, a time-out, no connection.</summary>
    ServiceFailure,

    /// <summary>
    /// The call could not be made, for a reason not the model service's, which no retry changes: its <c>ErrorKind</c> is
    /// <c>"steps"</c> (its body passed the step budget, S05-062), <c>"compose"</c> (its body could not be composed,
    /// A06-008) or <c>"plan"</c> (its plan is not the job's, or lacks its entry or its agent: S07-006). The node fails.
    /// </summary>
    Failed,
}

/// <summary>
/// #1503 (specVersion 31, area 06): one attempt of an agent call. <paramref name="Key"/> is the instance's row key
/// (<see cref="RunKeys.Of"/>; inside a function, through the call node: <c>node.standards.section.patient_draft:hpi</c>),
/// <paramref name="EntryKey"/> its entry's key (inside a function, the key local to it, as
/// <see cref="PlanFunction.Entries"/> holds it: <c>node.patient_draft</c>), <paramref name="Function"/> the function the entry
/// lives in (null at the package's level), <paramref name="Attempt"/> from 1, <paramref name="Repair"/> whether it asks
/// again after an invalid reply, and <paramref name="Scope"/> the data the body reads: exactly the paths it reads
/// (<see cref="RunScope.Data"/>). #1504 (area 07; D3, D11): <paramref name="Placed"/> is each decided placement of the
/// entry's <c>placements</c> (<c>part.&lt;id&gt;</c>, <c>snippet.&lt;id&gt;</c>, <c>macro.&lt;id&gt;</c>,
/// <c>macro-folder.&lt;id&gt;</c>) to its result, none when null; <paramref name="Faults"/> the sentences of the last
/// reply's marker faults (S05-053 to S05-059) a retry names, none when null.
/// </summary>
public sealed record AgentCall(
    string Key, string EntryKey, string? Function, int Attempt, bool Repair, JsonObject Scope,
    IReadOnlyDictionary<string, bool>? Placed = null, IReadOnlyList<string>? Faults = null);

/// <summary>
/// #1503 (specVersion 31, area 06; D7): what one attempt gave: the reply, or the wait the activity worked out
/// (<c>retry-after</c>, else the back-off with its jitter) and the error's kind (<c>"timeout"</c>, <c>"error"</c> or
/// <c>"unreachable"</c>; for <see cref="AgentOutcomeKind.Failed"/> <c>"steps"</c>, <c>"compose"</c> or <c>"plan"</c>), never
/// its content. #1504 (area 07; part 08 §§ 4, 4.3): <paramref name="Detail"/> is the attempt's row detail in part 08's names
/// (<see cref="AgentTurn"/>), and <paramref name="Invalid"/> the reply's marker faults, null when there were none.
/// </summary>
public sealed record AgentOutcome(
    AgentOutcomeKind Kind, string? Reply, int WaitMilliseconds, string? ErrorKind, JsonObject? Detail = null, IReadOnlyList<ReplyFault>? Invalid = null);

/// <summary>
/// #1503 (specVersion 31, area 06): one render of a rendered node or a deliverable, with its step budget. #1504 (area 07;
/// D3): <paramref name="Placed"/> is each decided placement of the entry's <c>placements</c> to its result, as
/// <see cref="AgentCall.Placed"/> is; none when null.
/// </summary>
public sealed record RenderCall(string Key, string EntryKey, string? Function, JsonObject Scope, int StepLimit, IReadOnlyDictionary<string, bool>? Placed = null);

/// <summary>
/// #1503 (specVersion 31, area 06): what a render gave: its text; or <paramref name="TooManySteps"/> when it passed its
/// step budget; or <paramref name="Failure"/>, the composer's sentence for another failure. #1504 (area 07; part 05 § 4;
/// part 08 §§ 8.2, 10): <paramref name="NeutralText"/> is the run-neutral text (<see cref="Composition.NeutralText"/>),
/// null when no run spot was filled, and <paramref name="Detail"/> the row's detail in part 08's names
/// (<see cref="RenderTurn"/>).
/// </summary>
public sealed record RenderOutcome(string? Text, bool TooManySteps, string? Failure, string? NeutralText = null, JsonObject? Detail = null)
{
    /// <summary>#1505 ruling 6 (R14): <see cref="Templates.Composition.EngineFault"/> of the failed render, for the activity to log; not serialised.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? EngineFault { get; init; }
}

/// <summary>
/// #1505 (specVersion 31, area 08; R6): one evaluation of a rule that holds <c>matches</c> or <c>has_word</c>
/// (<see cref="IJsonLogicRule.ReadsPattern"/>). <paramref name="Key"/>, <paramref name="EntryKey"/> and
/// <paramref name="Function"/> are as <see cref="AgentCall"/>'s; <paramref name="Member"/> names the entry's rule:
/// <c>"if"</c>, <c>"expression"</c>, <c>"while"</c>, <c>"until"</c>, or a placement's site (<c>"macro.&lt;id&gt;"</c>,
/// <c>"part.&lt;id&gt;"</c>, …) for that placement's <c>if</c>; <paramref name="Scope"/> the data the rule is evaluated
/// over: what the runner would evaluate it over inline (<see cref="RunScope.Data"/> of the entry's reads).
/// </summary>
public sealed record RuleCall(string Key, string EntryKey, string? Function, string Member, JsonObject Scope);

/// <summary>
/// #1505 (specVersion 31, area 08; R6): what an evaluation gave (<see cref="RuleTurn.Evaluate"/>). <paramref name="Value"/>
/// is the rule's value; a condition's (every member but <c>"expression"</c>) is its boolean. With a failure,
/// <paramref name="FailureKind"/> is its <see cref="JsonLogicFailureKind"/>'s name and <paramref name="FailureLimitSeconds"/>
/// and <paramref name="FailureDetail"/> its <see cref="JsonLogicFailure.Limit"/> and <see cref="JsonLogicFailure.Detail"/>
/// (which never holds a value from the data); or <paramref name="FailureKind"/> is <see cref="RuleTurn.PlanFailure"/>:
/// the plan is not the job's, or lacks the entry or its rule.
/// </summary>
public sealed record RuleOutcome(JsonNode? Value, string? FailureKind, double? FailureLimitSeconds, string? FailureDetail = null);

/// <summary>
/// #1503 (specVersion 31, area 06): everything outside the run that <see cref="PlanRunner"/> talks to: agent calls, renders,
/// timers and the clock. The engine's host is Durable Functions' (activities, durable timers, the replay-safe clock), so the
/// runner awaits no task but these, and reads no time but <see cref="UtcNow"/>.
/// </summary>
public interface IRunHost
{
    /// <summary>The time now, from the host's replay-safe clock.</summary>
    DateTime UtcNow { get; }

    /// <summary>Makes one attempt of an agent call.</summary>
    Task<AgentOutcome> CallAgentAsync(AgentCall call);

    /// <summary>Renders a rendered node's or a deliverable's body.</summary>
    Task<RenderOutcome> RenderAsync(RenderCall call);

    /// <summary>
    /// #1505 (area 08; R6): evaluates a rule that holds <c>matches</c> or <c>has_word</c>, so its outcome is stored and a
    /// replay reads it rather than timing the pattern again. The runner awaits it where the rule is decided; it takes no slot.
    /// </summary>
    Task<RuleOutcome> EvaluateAsync(RuleCall call);

    /// <summary>
    /// Waits <paramref name="wait"/> on the host's timer. When <paramref name="cancel"/> is cancelled first, the timer is
    /// cancelled and the task ends cancelled: an orchestration does not complete while a durable timer is outstanding, so the
    /// runner cancels every timer it made when its run ends.
    /// </summary>
    Task DelayAsync(TimeSpan wait, CancellationToken cancel);
}
