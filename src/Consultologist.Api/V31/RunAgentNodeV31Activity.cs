using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06): an activity's input: the job's package version, its <c>resolveHash</c>, the step budget
/// of its body (the job's recorded <c>renderSteps</c>, never the live setting), and the call (#1504: with the runner's
/// placement decisions, <see cref="AgentCall.Placed"/>, and a retry's <see cref="AgentCall.Faults"/>).
/// </summary>
public sealed record RunAgentNodeV31Input(string PackageName, string PackageVersion, string ResolveHash, int RenderSteps, AgentCall Call);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.1, 3.4; D7): one attempt of a 31 agent call. The plan comes from the store's
/// cache (the job's own, by its <c>resolveHash</c>); the agent is the one the plan's <c>bindings</c> name for the entry's
/// format and producer (<c>bindings.&lt;format key&gt;.&lt;producer&gt;</c>), which the 31 agents catalogue must hold. #1504
/// (area 07; part 05 §§ 8.3 to 8.6; D8, D11): the prompt is <see cref="AgentTurn.Prepare"/>'s (the body composed over the
/// call's scope with its placements decided, a custom binding's format sent without the attested agent's
/// <c>strictRejects</c>, a retry's text), sent once as the user message (<see cref="IAgentAttempts.TryPromptOnceAsync"/>),
/// and a reply is <see cref="AgentTurn.Finish"/>'s (unwrapped, its macro markers checked and replaced, the row's detail).
/// A 429 or another service failure is answered with the wait before the next attempt: the service's, else
/// <see cref="AgentBackoff.Wait"/> with jitter from <paramref name="random"/> (<see cref="Random.Shared"/> unless a test
/// gives one; the wait is in the history, so a replay repeats it). There is no Durable retry policy: the orchestration
/// retries. A call that cannot be made answers <see cref="AgentOutcomeKind.Failed"/> and is never sent, so the runner fails
/// the node with a row and a sentence: <c>plan</c> (a plan that is not the job's, an entry, a binding or a catalogue agent
/// it lacks), <c>steps</c> (the body passed the job's step budget), <c>compose</c> (it could not be composed). Telemetry:
/// the key, the attempt, lengths and hashes, never text.
/// </summary>
public sealed class RunAgentNodeV31Activity(
    ICompiledPackageStore store,
    IAgentAttempts agent,
    V31AgentsCatalogue catalogue,
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

        var (plan, _) = await V31Plans.LoadAsync(store, input.PackageName, input.PackageVersion, input.ResolveHash, logger, cancellationToken);
        var entry = plan == null ? null : V31Entries.Find(plan, call.EntryKey, call.Function);
        if (plan == null || entry == null || Binding(plan, entry) is not { } binding
            || !int.TryParse(binding.AgentVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            || !catalogue.Holds(binding.AgentName, version))
        {
            return CouldNotCall(call, "plan");
        }

        var (agentName, agentVersion) = binding;
        var prepared = AgentTurn.Prepare(plan, call, input.RenderSteps, catalogue.StrictRejects(agentName, version));
        if (prepared.Text is not { } prompt)
            return CouldNotCall(call, prepared.ErrorKind ?? "plan");

        var attempt = await agent.TryPromptOnceAsync(call.Key, prompt, agentName, agentVersion, cancellationToken);
        var outcome = attempt.Kind == AgentOutcomeKind.Completed
            ? AgentTurn.Finish(prepared, attempt.Text ?? "")
            : new AgentOutcome(attempt.Kind, null, attempt.RetryAfterMilliseconds ?? AgentBackoff.Wait(call.Attempt, _random.NextDouble()), attempt.ErrorKind);

        logger.LogInformation(
            "V31 agent attempt. Key={Key}, Attempt={Attempt}, Repair={Repair}, Faults={Faults}, AgentName={AgentName}, AgentVersion={AgentVersion}, Outcome={Outcome}, ErrorKind={ErrorKind}, WaitMs={WaitMs}, PromptLength={PromptLength}, PromptHash={PromptHash}, RunNeutralHash={RunNeutralHash}, ReplyLength={ReplyLength}, ReplyHash={ReplyHash}, Invalid={Invalid}, ValueLength={ValueLength}, ValueHash={ValueHash}, InputTokens={InputTokens}, OutputTokens={OutputTokens}, ElapsedMs={ElapsedMs}",
            call.Key,
            call.Attempt,
            call.Repair,
            call.Faults?.Count ?? 0,
            agentName,
            agentVersion,
            outcome.Kind,
            outcome.ErrorKind,
            outcome.WaitMilliseconds,
            prompt.Length,
            CanonicalJson.HashText(prompt),
            SourceLayout.Text(prepared.Detail["runNeutralHash"]),
            attempt.Text?.Length,
            attempt.Text == null ? null : CanonicalJson.HashText(attempt.Text),
            outcome.Invalid?.Count ?? 0,
            outcome.Reply?.Length,
            outcome.Reply == null ? null : CanonicalJson.HashText(outcome.Reply),
            attempt.Tokens?.Input,
            attempt.Tokens?.Output,
            stopwatch.ElapsedMilliseconds);
        return outcome;
    }

    /// <summary>A call that could not be made, of <paramref name="kind"/>; logged by key and kind only.</summary>
    private AgentOutcome CouldNotCall(AgentCall call, string kind)
    {
        logger.LogWarning("V31 agent call could not be made. Key={Key}, Attempt={Attempt}, Kind={Kind}", call.Key, call.Attempt, kind);
        return new AgentOutcome(AgentOutcomeKind.Failed, null, 0, kind);
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
