using System.Text.Json.Nodes;

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
}

/// <summary>
/// #1503 (specVersion 31, area 06): one attempt of an agent call. <paramref name="Key"/> is the instance's row key
/// (<see cref="RunKeys.Of"/>), <paramref name="EntryKey"/> its entry's key, <paramref name="Function"/> the function the entry
/// lives in (null at the package's top level), <paramref name="Attempt"/> from 1, <paramref name="Repair"/> whether it asks
/// again after an invalid reply, and <paramref name="Scope"/> the data the body reads: exactly the paths it reads
/// (<see cref="RunScope.Data"/>).
/// </summary>
public sealed record AgentCall(string Key, string EntryKey, string? Function, int Attempt, bool Repair, JsonObject Scope);

/// <summary>
/// #1503 (specVersion 31, area 06; D7): what one attempt gave: the reply, or the wait the activity worked out
/// (<c>retry-after</c>, else the back-off with its jitter) and the error's kind (<c>"timeout"</c>, <c>"error"</c> or
/// <c>"unreachable"</c>), never its content.
/// </summary>
public sealed record AgentOutcome(AgentOutcomeKind Kind, string? Reply, int WaitMilliseconds, string? ErrorKind);

/// <summary>#1503 (specVersion 31, area 06): one render of a rendered node or a deliverable, with its step budget.</summary>
public sealed record RenderCall(string Key, string EntryKey, string? Function, JsonObject Scope, int StepLimit);

/// <summary>
/// #1503 (specVersion 31, area 06): what a render gave: its text; or <paramref name="TooManySteps"/> when it passed its
/// step budget; or <paramref name="Failure"/>, the composer's sentence for another failure.
/// </summary>
public sealed record RenderOutcome(string? Text, bool TooManySteps, string? Failure);

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
    /// Waits <paramref name="wait"/> on the host's timer. When <paramref name="cancel"/> is cancelled first, the timer is
    /// cancelled and the task ends cancelled: an orchestration does not complete while a durable timer is outstanding, so the
    /// runner cancels every timer it made when its run ends.
    /// </summary>
    Task DelayAsync(TimeSpan wait, CancellationToken cancel);
}
