using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06): a 31 job's orchestration input. Its inputs travel by reference (<paramref name="InputsBlob"/>,
/// the blob <see cref="IRunInputsStore"/> holds for <paramref name="JobId"/>), never inline. <paramref name="Limits"/> is
/// the header's <c>limits</c> (<see cref="RunLimits.ToJson"/>) and <paramref name="RegexMilliseconds"/> R2's limit, both as
/// the job's start read them, so a replay never depends on a setting that changed. <paramref name="RunFactsBase"/> holds
/// the run facts only the start knows (<c>id</c>, <c>engine</c>), by name; <see cref="V31RunFacts.Of"/> adds the rest.
/// <paramref name="DrainAware"/> marks an orchestration started by a build that drains (D9). <paramref name="AccountKind"/>
/// picks the outputs container at completion, as a 5–30 job's does.
/// </summary>
public sealed record ConsultGenerationOrchestrationInputV31(
    string JobId,
    string AppUserId,
    string PackageName,
    string PackageVersion,
    string ResolveHash,
    string LockHash,
    JsonObject Limits,
    int RegexMilliseconds,
    string InputsBlob,
    JsonObject RunFactsBase,
    string? TimeZone,
    DateTimeOffset? ScheduledAtUtc,
    bool DrainAware,
    string? AccountKind = null);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.2 step 6, 3.1, 3.2, 3.4; part 04 § 7.7; D7, D12): a 31 job's orchestration.
/// It waits for a schedule as the 5–30 one does; takes <c>startedAtUtc</c> from the replay-safe clock and records it on the
/// job's entity with the hashes and the limits, in one call, before any node runs; marks the job running; loads the plan
/// replay-safely (<see cref="ConsultGenerationActivityNames.LoadPlanV31"/> gives the verified <c>resolve.json</c>, and the
/// plan is a process-wide cache's by <c>resolveHash</c>, or built from that text, with no I/O of its own) and the inputs by
/// reference (<see cref="ConsultGenerationActivityNames.LoadRunInputsV31"/>); runs <see cref="PlanRunner"/> inline, its
/// calls activities and its waits durable timers (<see cref="DurableRunHost"/>), its rows on the entity
/// (<see cref="EntityRunRecorder"/>); and finalises the job: <c>Completed</c>, or <c>Failed</c> with the run's sentence (a
/// timed-out run is a failed job with S09-012's). A fault fails the job with its message, as the 5–30 orchestration's does.
/// Telemetry at the end: the run's state, its agent calls and the peak in flight, never text.
/// </summary>
public sealed class ConsultGenerationOrchestratorV31
{
    /// <summary>The plans built from a history's <c>resolve.json</c>, by <c>resolveHash</c>: a plan is a function of that text.</summary>
    private static readonly ConcurrentDictionary<string, RunPlan> Plans = new(StringComparer.Ordinal);

    [Function(nameof(ConsultGenerationOrchestratorV31))]
    public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ConsultGenerationOrchestrationInputV31>()
            ?? throw new InvalidOperationException("A 31 job's orchestration input is required.");
        var logger = context.CreateReplaySafeLogger(nameof(ConsultGenerationOrchestratorV31));
        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), input.JobId);

        try
        {
            if (input.ScheduledAtUtc is { } scheduledAt && scheduledAt > context.CurrentUtcDateTime)
            {
                context.SetCustomStatus(new { status = ConsultGenerationJobStatuses.Scheduled });
                await context.CreateTimer(scheduledAt.UtcDateTime, CancellationToken.None);
            }

            // Part 09 § 1.2 step 6: the header, before any node runs. The job limit is timed from here.
            var startedAtUtc = context.CurrentUtcDateTime;
            var limits = RunLimits.FromJson(input.Limits);
            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.RecordV31Header),
                new ConsultGenerationV31Header(
                    input.ResolveHash,
                    input.LockHash,
                    limits.ToJson().ToDictionary(member => member.Key, member => (int)member.Value!, StringComparer.Ordinal),
                    new DateTimeOffset(startedAtUtc, TimeSpan.Zero)));
            await context.Entities.CallEntityAsync(entityId, nameof(ConsultGenerationJobEntity.MarkRunning));
            context.SetCustomStatus(new { status = ConsultGenerationJobStatuses.Running });

            logger.LogInformation(
                "ConsultGenerationOrchestratorV31 started. JobId={JobId}, Package={Package}@{Version}, ResolveHash={ResolveHash}, MaxAgentCalls={MaxAgentCalls}",
                input.JobId, input.PackageName, input.PackageVersion, input.ResolveHash, limits.MaxAgentCalls);

            var loaded = await context.CallActivityAsync<LoadPlanV31Result>(
                ConsultGenerationActivityNames.LoadPlanV31,
                new LoadPlanV31Input(input.PackageName, input.PackageVersion, input.ResolveHash, input.LockHash));
            var plan = loaded.Resolve is { } packed ? PlanOf(LoadPlanV31Activity.Unpack(packed), input) : null;
            if (plan == null)
            {
                await FinalizeAsync(context, entityId, input, ConsultGenerationJobStatuses.Failed,
                    loaded.Refusal ?? V31Plans.HashMismatch(input.PackageName, input.PackageVersion));
                return;
            }

            var document = await context.CallActivityAsync<JsonObject?>(ConsultGenerationActivityNames.LoadRunInputsV31, input.JobId)
                ?? throw new InvalidOperationException($"The inputs of job {input.JobId} could not be read.");
            var inputs = InputsOf(document, RunFacts(plan, input, startedAtUtc));

            var result = await new PlanRunner(plan, limits, new DurableRunHost(context, input), new EntityRunRecorder(context, input))
                .RunAsync(inputs, startedAtUtc);

            logger.LogInformation(
                "ConsultGenerationOrchestratorV31 ran. JobId={JobId}, State={State}, FailedKey={FailedKey}, AgentCalls={AgentCalls}, PeakInFlight={PeakInFlight}",
                input.JobId, result.State, result.FailedKey, result.AgentCalls, result.PeakInFlight);

            if (result.State == RunState.Completed)
                await FinalizeAsync(context, entityId, input, ConsultGenerationJobStatuses.Completed, null);
            else
                await FinalizeAsync(context, entityId, input, ConsultGenerationJobStatuses.Failed, result.Sentence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As the 5–30 orchestration does: the failure is the job's, said where its failures are said, and the
            // orchestration fails with it. An activity's failure is said by its own message.
            var reason = ex is TaskFailedException { FailureDetails: { } details } ? details.ErrorMessage : ex.Message;
            logger.LogError(
                "ConsultGenerationOrchestratorV31 failed. JobId={JobId}, ExceptionType={ExceptionType}",
                input.JobId,
                ex is TaskFailedException { FailureDetails: { } failed } ? failed.ErrorType : ex.GetType().FullName);

            try
            {
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.FinalizeJob),
                    new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, reason, input.AccountKind));
            }
            catch (Exception cleanup)
            {
                logger.LogWarning(
                    "ConsultGenerationOrchestratorV31 FinalizeJob cleanup failed. JobId={JobId}, ExceptionType={ExceptionType}",
                    input.JobId,
                    cleanup.GetType().FullName);
            }

            throw;
        }
    }

    /// <summary>Finalises the job on its entity and publishes its status.</summary>
    private static async Task FinalizeAsync(
        TaskOrchestrationContext context, EntityInstanceId entityId, ConsultGenerationOrchestrationInputV31 input, string status, string? error)
    {
        await context.Entities.CallEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.FinalizeJob),
            new ConsultGenerationJobFinalize(status, error, input.AccountKind));
        context.SetCustomStatus(new { status });
    }

    /// <summary>
    /// The plan of <paramref name="resolve"/>: the cache's by the job's <c>resolveHash</c>, else built from the text and
    /// cached. Null when the text is not the job's (its hash differs) or no plan can be made of it.
    /// </summary>
    private static RunPlan? PlanOf(string resolve, ConsultGenerationOrchestrationInputV31 input)
    {
        if (Plans.TryGetValue(input.ResolveHash, out var cached))
            return cached;
        if (!string.Equals(CanonicalJson.HashText(resolve), input.ResolveHash, StringComparison.Ordinal))
            return null;

        var (plan, _) = RunPlan.Build(resolve, input.ResolveHash, input.LockHash, TimeSpan.FromMilliseconds(input.RegexMilliseconds));
        return plan == null ? null : Plans.GetOrAdd(input.ResolveHash, plan);
    }

    /// <summary>The run facts: what the job's start knew (<see cref="ConsultGenerationOrchestrationInputV31.RunFactsBase"/>), then <see cref="V31RunFacts.Of"/>'s.</summary>
    private static JsonObject RunFacts(RunPlan plan, ConsultGenerationOrchestrationInputV31 input, DateTime startedAtUtc)
    {
        var facts = (JsonObject)input.RunFactsBase.DeepClone();
        foreach (var (name, value) in V31RunFacts.Of(plan, new DateTimeOffset(startedAtUtc, TimeSpan.Zero), input.TimeZone))
            facts[name] = value?.DeepClone();
        return facts;
    }

    /// <summary>
    /// The run's inputs from the inputs document (<see cref="IRunInputsStore"/>'s shape): each input's read form, by id; the
    /// profile's fields, a field not text reading <c>null</c>; and <paramref name="runFacts"/>.
    /// </summary>
    internal static RunInputs InputsOf(JsonObject document, JsonObject runFacts)
    {
        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (document["inputs"] is JsonObject given)
        {
            foreach (var (id, read) in given)
                inputs[id] = read?.DeepClone();
        }

        var profile = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (document["profile"] is JsonObject fields)
        {
            foreach (var (field, value) in fields)
                profile[field] = SourceLayout.Text(value);
        }

        return new RunInputs(inputs, runFacts, profile);
    }
}
