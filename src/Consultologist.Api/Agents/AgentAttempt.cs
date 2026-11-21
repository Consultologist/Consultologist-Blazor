using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using Consultologist.Api.Models;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Agents;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.4; D7): how one attempt of an agent call ended: the reply and its tokens; or
/// a 429 (<see cref="AgentOutcomeKind.Throttled"/>) or another service failure, with the wait the service asked for in
/// milliseconds when it named one, and the failure's kind (<c>"error"</c>, <c>"timeout"</c>, <c>"unreachable"</c>), never its
/// content.
/// </summary>
public sealed record AgentAttempt(AgentOutcomeKind Kind, string? Text, ConsultTokenUsage? Tokens, int? RetryAfterMilliseconds, string? ErrorKind)
{
    /// <summary>
    /// The attempt <paramref name="failure"/> ends, or null when it is not a service failure a 31 call retries (it then
    /// propagates, as it does today): HTTP 429 is throttled, with the response's <c>retry-after-ms</c> or else its
    /// <c>retry-after</c> (seconds, or an HTTP date from <paramref name="now"/>); a 5xx is <c>error</c>, with the same
    /// wait; a time-out (a cancellation not of <paramref name="cancellation"/>, which is the host's own and propagates) is
    /// <c>timeout</c>; no connection (an <see cref="HttpRequestException"/> with no status, bare or as the SDK wraps it) is
    /// <c>unreachable</c>. An empty reply (<see cref="IsEmptyReply"/>) is not a failure: it is <see cref="EmptyReply"/>, a
    /// completed attempt with empty text, which the runner validates as it validates any reply.
    /// </summary>
    public static AgentAttempt? Of(Exception failure, CancellationToken cancellation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (cancellation.IsCancellationRequested)
            return null;
        if (IsEmptyReply(failure))
            return EmptyReply;

        switch (failure)
        {
            case ClientResultException { Status: 429 } throttled:
                return new AgentAttempt(AgentOutcomeKind.Throttled, null, null, RetryAfter(throttled.GetRawResponse(), now), null);
            case ClientResultException { Status: >= 500 and <= 599 } failed:
                return new AgentAttempt(AgentOutcomeKind.ServiceFailure, null, null, RetryAfter(failed.GetRawResponse(), now), "error");
            case ClientResultException { Status: 0 } wrapped when wrapped.InnerException is { } inner:
                return Of(inner, cancellation, now);
            case HttpRequestException { StatusCode: null }:
                return Failure("unreachable");
            case OperationCanceledException or TimeoutException:
                return Failure("timeout");
            default:
                return null;
        }
    }

    /// <summary>
    /// An empty reply, as a completed attempt with empty text: model output, which the runner's format validation,
    /// <c>failIfEmpty</c> and <c>onInvalid</c> decide, never an engine fault ("never throw on model output").
    /// </summary>
    public static AgentAttempt EmptyReply { get; } = new(AgentOutcomeKind.Completed, "", null, null, null);

    /// <summary>
    /// What <see cref="AgentSectionGenerator.SendPromptAsync"/> throws when the response carries no assistant text. It throws a
    /// bare <see cref="InvalidOperationException"/> with this message and nothing else marks the case, so, with
    /// <c>SendPromptAsync</c> left unchanged for 5–30, the message is matched exactly (a test pins it to that method's source).
    /// </summary>
    internal const string EmptyReplyMessage = "No assistant response found";

    /// <summary>Whether <paramref name="failure"/> is <see cref="AgentSectionGenerator.SendPromptAsync"/>'s empty reply.</summary>
    internal static bool IsEmptyReply(Exception failure) =>
        failure.GetType() == typeof(InvalidOperationException)
        && failure.InnerException == null
        && string.Equals(failure.Message, EmptyReplyMessage, StringComparison.Ordinal);

    private static AgentAttempt Failure(string kind) => new(AgentOutcomeKind.ServiceFailure, null, null, null, kind);

    /// <summary>The wait the response names, in milliseconds: <c>retry-after-ms</c>, else <c>retry-after</c>; null for none or one unreadable.</summary>
    private static int? RetryAfter(PipelineResponse? response, DateTimeOffset now)
    {
        if (response == null)
            return null;

        if (response.Headers.TryGetValue("retry-after-ms", out var milliseconds)
            && double.TryParse(milliseconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) && ms >= 0)
        {
            return Clamp(ms);
        }

        if (response.Headers.TryGetValue("retry-after", out var after) && after is { Length: > 0 })
        {
            if (double.TryParse(after, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                return Clamp(seconds * 1000);
            if (DateTimeOffset.TryParse(after, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                return Clamp(Math.Max(0, (at - now).TotalMilliseconds));
        }

        return null;
    }

    private static int Clamp(double milliseconds) => milliseconds >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(milliseconds);
}

/// <summary>
/// #1503 (specVersion 31, area 06; D7): one attempt of an agent call, with no retry of its own: the orchestration waits and
/// calls again. <see cref="AgentSectionGenerator"/> is the engine's.
/// </summary>
public interface IAgentAttempts
{
    /// <summary>Sends <paramref name="userMessage"/> once to the agent and says how the attempt ended (<see cref="AgentAttempt.Of"/>).</summary>
    Task<AgentAttempt> TryPromptOnceAsync(string stage, string userMessage, string agentName, string agentVersion, CancellationToken cancellationToken);
}

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.4): the back-off before an agent call's next attempt when the service named
/// no wait: 5 seconds on the first attempt, doubling, capped at 60 seconds, plus up to 20% jitter.
/// </summary>
public static class AgentBackoff
{
    private const int StartMilliseconds = 5000;
    private const int CapMilliseconds = 60_000;

    /// <summary>
    /// The wait after attempt <paramref name="attempt"/> (from 1) failed, in milliseconds, with <paramref name="jitter"/>
    /// (from 0 up to 1, as <see cref="Random.NextDouble"/> gives it) of a fifth of it added: 5,000 to 6,000 after the
    /// first, 10,000 to 12,000 after the second, never over 72,000.
    /// </summary>
    public static int Wait(int attempt, double jitter)
    {
        var doublings = Math.Clamp(attempt, 1, 32) - 1;
        var wait = Math.Min((long)StartMilliseconds << Math.Min(doublings, 4), CapMilliseconds);
        return (int)(wait + (long)(wait * 0.2 * Math.Clamp(jitter, 0, 1)));
    }
}
