using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>#1503 (specVersion 31, area 06): an activity's input: the job's package version, its <c>resolveHash</c>, and the call.</summary>
public sealed record RunAgentNodeV31Input(string PackageName, string PackageVersion, string ResolveHash, AgentCall Call);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.1, 3.4; D7): one attempt of a 31 agent call. The plan comes from the store's
/// cache (the job's own, by its <c>resolveHash</c>); the entry's body is composed over the call's scope; the agent is the
/// one the plan's <c>bindings</c> name for the entry's format and producer (<c>bindings.&lt;format key&gt;.&lt;producer&gt;</c>);
/// and the composed body is sent once, as the user message (<see cref="IAgentAttempts.TryPromptOnceAsync"/>). A 429 or
/// another service failure is answered with the wait before the next attempt: the service's, else
/// <see cref="AgentBackoff.Wait"/> with jitter from <paramref name="random"/> (<see cref="Random.Shared"/> unless a test
/// gives one; the wait is in the history, so a replay repeats it). There is no Durable retry policy: the orchestration
/// retries. A plan that is not the job's, an entry or binding it lacks, or a body that cannot be composed fails the
/// activity with its sentence. Telemetry: the key, the attempt, lengths and hashes, never text.
/// </summary>
public sealed class RunAgentNodeV31Activity(
    ICompiledPackageStore store,
    IAgentAttempts agent,
    IBodyComposer composer,
    EngineLimitSettings limits,
    ILogger<RunAgentNodeV31Activity> logger,
    Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;

    [Function(ConsultGenerationActivityNames.RunAgentNodeV31)]
    public async Task<AgentOutcome> RunAsync([ActivityTrigger] RunAgentNodeV31Input input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var call = input.Call;
        var stopwatch = Stopwatch.StartNew();

        var (plan, refusal) = await V31Plans.LoadAsync(store, input.PackageName, input.PackageVersion, input.ResolveHash, cancellationToken);
        if (plan == null)
            throw new InvalidOperationException(refusal);

        var notPublished = ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence;
        var entry = V31Entries.Find(plan, call.EntryKey, call.Function) ?? throw new InvalidOperationException(notPublished);
        var (agentName, agentVersion) = Binding(plan, entry) ?? throw new InvalidOperationException(notPublished);

        var composed = composer.Compose(plan, call.EntryKey, call.Function, call.Scope, limits.Limits.RenderSteps);
        if (composed.TooManySteps)
        {
            throw new InvalidOperationException(
                $"{V31Entries.Subject(entry, call.Function)} stopped rendering: it took more than {limits.Limits.RenderSteps.ToString("N0", CultureInfo.InvariantCulture)} steps.");
        }

        if (composed.Text is not { } body)
            throw new InvalidOperationException(composed.Failure ?? notPublished);

        var attempt = await agent.TryPromptOnceAsync(call.Key, body, agentName, agentVersion, cancellationToken);
        var outcome = attempt.Kind == AgentOutcomeKind.Completed
            ? new AgentOutcome(AgentOutcomeKind.Completed, attempt.Text ?? "", 0, null)
            : new AgentOutcome(attempt.Kind, null, attempt.RetryAfterMilliseconds ?? AgentBackoff.Wait(call.Attempt, _random.NextDouble()), attempt.ErrorKind);

        logger.LogInformation(
            "V31 agent attempt. Key={Key}, Attempt={Attempt}, Repair={Repair}, AgentName={AgentName}, AgentVersion={AgentVersion}, Outcome={Outcome}, ErrorKind={ErrorKind}, WaitMs={WaitMs}, PromptLength={PromptLength}, PromptHash={PromptHash}, ReplyLength={ReplyLength}, ReplyHash={ReplyHash}, InputTokens={InputTokens}, OutputTokens={OutputTokens}, ElapsedMs={ElapsedMs}",
            call.Key,
            call.Attempt,
            call.Repair,
            agentName,
            agentVersion,
            outcome.Kind,
            outcome.ErrorKind,
            outcome.WaitMilliseconds,
            body.Length,
            CanonicalJson.HashText(body),
            outcome.Reply?.Length,
            outcome.Reply == null ? null : CanonicalJson.HashText(outcome.Reply),
            attempt.Tokens?.Input,
            attempt.Tokens?.Output,
            stopwatch.ElapsedMilliseconds);
        return outcome;
    }

    /// <summary>
    /// The agent the plan binds to <paramref name="entry"/>: <c>bindings.&lt;format key&gt;.&lt;producer&gt;</c>, the format key
    /// being the bundle key its <c>format</c>'s <c>$ref</c> names (<c>#/$defs/&lt;key&gt;</c>); null when there is none.
    /// </summary>
    internal static (string AgentName, string AgentVersion)? Binding(RunPlan plan, PlanEntry entry)
    {
        const string defs = "#/$defs/";
        if (entry.Format is not JsonObject format || SourceLayout.Text(format["$ref"]) is not { } reference
            || !reference.StartsWith(defs, StringComparison.Ordinal)
            || SourceLayout.Text(entry.Json["producer"]) is not { } producer)
        {
            return null;
        }

        if (plan.Document["bindings"] is not JsonObject bindings
            || bindings[reference[defs.Length..]] is not JsonObject producers
            || producers[producer] is not JsonObject binding
            || SourceLayout.Text(binding["agentName"]) is not { Length: > 0 } name)
        {
            return null;
        }

        var version = binding["agentVersion"] switch
        {
            JsonValue number when number.GetValueKind() == JsonValueKind.Number && number.TryGetValue<long>(out var n) => n.ToString(CultureInfo.InvariantCulture),
            JsonValue text when text.GetValueKind() == JsonValueKind.String => text.GetValue<string>(),
            _ => null,
        };
        return version is { Length: > 0 } ? (name, version) : null;
    }
}
