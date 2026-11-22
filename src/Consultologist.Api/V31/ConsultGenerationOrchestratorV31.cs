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
/// #1503 (specVersion 31, area 06): a 31 job's orchestration input. Its inputs travel by reference, never inline:
/// <paramref name="InputsBlob"/> is the name of the job's held inputs in <paramref name="AccountKind"/>'s held-inputs container
/// (<see cref="RunInputsBlobStore.PointerOf"/>; the pointer the job's entity records).
/// <paramref name="Limits"/> is the header's <c>limits</c> (<see cref="RunLimits.ToJson"/>) and <paramref name="RegexMilliseconds"/>
/// R2's limit, both as the job's start read them, so a replay never depends on a setting that changed.
/// <paramref name="RunFactsBase"/> holds the run facts only the start knows (<c>id</c>, <c>engine</c>), by name;
/// <see cref="V31StartChecks.RunFacts"/> adds the rest. <paramref name="DrainAware"/> marks an orchestration started by a build that
/// drains (D9). <paramref name="AccountKind"/> picks the held inputs' container, and the outputs container at completion, as a
/// 5–30 job's does.
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
    string? AccountKind);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.2 step 6, 3.1, 3.2, 3.4; part 04 § 7.7; D7, D12): a 31 job's orchestration.
/// It waits for a schedule as the 5–30 one does; takes <c>startedAtUtc</c> from the replay-safe clock and records it on the
/// job's entity with the hashes and the limits, in one call, before any node runs; marks the job running; loads the plan
/// replay-safely (<see cref="ConsultGenerationActivityNames.LoadPlanV31"/> gives the verified <c>resolve.json</c>; the plan
/// is the process cache's (<see cref="V31PlanCache"/>), or built from that text, with no I/O of its own) and the inputs and
/// run facts by reference (<see cref="ConsultGenerationActivityNames.LoadRunInputsV31"/>, so the time zone is read in the
/// activity, never here); runs <see cref="PlanRunner"/> inline, its calls activities and its waits durable timers
/// (<see cref="DurableRunHost"/>), its rows on the entity (<see cref="EntityRunRecorder"/>); #1505 (area 08) delivers a
/// job that succeeded (<see cref="DeliverAsync"/>), or records a failed job's released deliverables not sent; and
/// finalises the job: <c>Completed</c>, or <c>Failed</c> with the run's sentence (a timed-out run is a failed job with
/// S09-012's). #1505 (part 09 § 3.9; D14): a failed job (an engine fault included) and a failed delivery alert the
/// operator through <see cref="ConsultGenerationActivityNames.AlertOperatorV31"/>, as do the run's notices
/// (<see cref="EntityRunRecorder.NoticeAsync"/>); an alert's failure never fails the job. A fault fails the job with
/// A06-010, never the exception's message, which can carry a service's response. Telemetry at the end: the run's state,
/// its agent calls and the peak in flight, never text.
/// </summary>
public sealed class ConsultGenerationOrchestratorV31
{
    /// <summary>A06-010: the job failed for a fault of the engine, not of its package or the model service.</summary>
    public const string EngineFault = "The engine couldn't finish this job. Tell the operator.";

    /// <summary>The plans built from a history's <c>resolve.json</c>.</summary>
    internal static V31PlanCache Plans { get; } = new(64);

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

            // Part 09 § 3.6 (D9): while the engine drains, the job waits here, before anything else; the time it waits is
            // not counted towards its limit (§ 3.4), since startedAtUtc is taken after.
            if (input.DrainAware)
                await EngineDrainJobs.WaitWhileDrainingAsync(context, entityId);

            // Part 09 § 1.2 step 6: the header, before any node runs. The job limit is timed from here.
            var startedAtUtc = context.CurrentUtcDateTime;
            var startedAt = new DateTimeOffset(startedAtUtc, TimeSpan.Zero);
            var limits = RunLimits.FromJson(input.Limits);
            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.RecordV31Header),
                new ConsultGenerationV31Header(
                    input.ResolveHash,
                    input.LockHash,
                    limits.ToJson().ToDictionary(member => member.Key, member => (int)member.Value!, StringComparer.Ordinal),
                    startedAt));
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
                    loaded.Refusal ?? V31Plans.HashMismatch(input.PackageName, input.PackageVersion), logger);
                return;
            }

            var held = await context.CallActivityAsync<LoadRunInputsV31Result>(
                ConsultGenerationActivityNames.LoadRunInputsV31,
                new LoadRunInputsV31Input(RunInputsBlobStore.PointerOf(input.AccountKind, input.InputsBlob), input.PackageName, input.PackageVersion, input.ResolveHash, startedAt,
                    input.TimeZone, input.RunFactsBase));
            if (held.Document is not { } document)
            {
                await FinalizeAsync(context, entityId, input, ConsultGenerationJobStatuses.Failed, held.Refusal ?? EngineFault, logger);
                return;
            }

            var inputs = InputsOf(document, held.RunFacts ?? new JsonObject());
            var recorder = new EntityRunRecorder(context, input);
            var result = await new PlanRunner(plan, limits, new DurableRunHost(context, input, limits), recorder)
                .RunAsync(inputs, startedAtUtc);

            logger.LogInformation(
                "ConsultGenerationOrchestratorV31 ran. JobId={JobId}, State={State}, FailedKey={FailedKey}, AgentCalls={AgentCalls}, PeakInFlight={PeakInFlight}",
                input.JobId, result.State, result.FailedKey, result.AgentCalls, result.PeakInFlight);

            if (result.State == RunState.Completed)
            {
                await DeliverAsync(context, entityId, input, limits, recorder, result.Deliverables, logger);
                await FinalizeAsync(context, entityId, input, ConsultGenerationJobStatuses.Completed, null, logger);
            }
            else
            {
                // Part 06 § 8: a job that fails delivers nothing; each destination of each released deliverable is recorded
                // not sent (S06-038).
                foreach (var row in DeliveryPlan.NotSentJobFailed(result.Deliverables))
                    await recorder.DeliveryAsync(row);
                await FinalizeAsync(context, entityId, input, ConsultGenerationJobStatuses.Failed, result.Sentence, logger, result.FailedKey);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // As the 5–30 orchestration does, the job is failed and the orchestration fails with it; but the job says A06-010,
            // never the exception's message (an activity's failure can carry a service's response). The type alone is logged.
            logger.LogError(
                "ConsultGenerationOrchestratorV31 failed. JobId={JobId}, ExceptionType={ExceptionType}",
                input.JobId,
                FaultType(ex));

            try
            {
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.FinalizeJob),
                    new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, EngineFault, input.AccountKind));
            }
            catch (Exception cleanup)
            {
                logger.LogWarning(
                    "ConsultGenerationOrchestratorV31 FinalizeJob cleanup failed. JobId={JobId}, ExceptionType={ExceptionType}",
                    input.JobId,
                    cleanup.GetType().FullName);
            }

            // #1505 (D14): an engine fault is a failed job too; the alert's own failure is logged, never thrown.
            await AlertOperatorV31Activity.RaiseAsync(context, new OperatorAlert(OperatorAlertKinds.JobFailed, input.JobId, null, null), logger);
            throw;
        }
    }

    /// <summary>The fault's type: an activity's own (as Durable records it), else the exception's.</summary>
    internal static string? FaultType(Exception ex) =>
        ex is TaskFailedException { FailureDetails: { } details } ? details.ErrorType : ex.GetType().FullName;

    /// <summary>
    /// #1505 (specVersion 31, area 08; part 06 § 8; part 09 § 3.8; R2, R3): a job that succeeded with something to send
    /// enters <see cref="ConsultGenerationJobStatuses.Delivering"/> and delivers its released deliverables
    /// (<see cref="DeliveryRunner"/> inline, its sends <see cref="ConsultGenerationActivityNames.DeliverV31"/> and its waits
    /// durable timers, <see cref="DurableDeliveryHost"/>; its rows on the entity). A job with nothing to send (every
    /// deliverable held, skipped, blocked, or delivered nowhere) never enters it. However delivery goes, the job is then
    /// Completed: a destination that failed is a row, not the job's failure.
    /// </summary>
    private static async Task DeliverAsync(
        TaskOrchestrationContext context, EntityInstanceId entityId, ConsultGenerationOrchestrationInputV31 input, RunLimits limits,
        EntityRunRecorder recorder, IReadOnlyList<DeliverableOutcome> outcomes, ILogger logger)
    {
        if (DeliveryPlan.Sends(outcomes).Count == 0)
            return;

        await context.Entities.CallEntityAsync(entityId, nameof(ConsultGenerationJobEntity.MarkDelivering));
        context.SetCustomStatus(new { status = ConsultGenerationJobStatuses.Delivering });
        var delivered = await new DeliveryRunner(limits, new DurableDeliveryHost(context, input), recorder).RunAsync(outcomes);
        logger.LogInformation(
            "ConsultGenerationOrchestratorV31 delivered. JobId={JobId}, Rows={Rows}, Failed={Failed}",
            input.JobId, delivered.Rows.Count, delivered.Failed);

        // #1505 (part 09 § 3.9; D14): a destination marked failed alerts the operator, once for the job: the first such row
        // and how many there are.
        if (delivered.Failed > 0)
        {
            var first = delivered.Rows.First(row => string.Equals(row.Result, "failed", StringComparison.Ordinal));
            await AlertOperatorV31Activity.RaiseAsync(
                context, new OperatorAlert(OperatorAlertKinds.DeliveryFailed, input.JobId, first.Key, delivered.Failed), logger);
        }
    }

    /// <summary>
    /// Finalises the job on its entity and publishes its status; #1505 (part 09 § 3.9; D14): a failed job then alerts the
    /// operator, with the key of what failed (<paramref name="failedKey"/>; none for a time-out or a job that failed before
    /// its run).
    /// </summary>
    private static async Task FinalizeAsync(
        TaskOrchestrationContext context, EntityInstanceId entityId, ConsultGenerationOrchestrationInputV31 input, string status, string? error,
        ILogger logger, string? failedKey = null)
    {
        await context.Entities.CallEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.FinalizeJob),
            new ConsultGenerationJobFinalize(status, error, input.AccountKind));
        context.SetCustomStatus(new { status });
        if (status == ConsultGenerationJobStatuses.Failed)
            await AlertOperatorV31Activity.RaiseAsync(context, new OperatorAlert(OperatorAlertKinds.JobFailed, input.JobId, failedKey, null), logger);
    }

    /// <summary>
    /// The plan of <paramref name="resolve"/>: the cache's for the job's <c>resolveHash</c> and regex limit, else built from
    /// the text and cached. Null when the text is not the job's (its hash differs) or no plan can be made of it.
    /// </summary>
    private static RunPlan? PlanOf(string resolve, ConsultGenerationOrchestrationInputV31 input)
    {
        if (Plans.TryGet(input.ResolveHash, input.RegexMilliseconds) is { } cached)
            return cached;
        if (!string.Equals(CanonicalJson.HashText(resolve), input.ResolveHash, StringComparison.Ordinal))
            return null;

        var (plan, _) = RunPlan.Build(resolve, input.ResolveHash, input.LockHash, TimeSpan.FromMilliseconds(input.RegexMilliseconds));
        return plan == null ? null : Plans.Add(input.ResolveHash, input.RegexMilliseconds, plan);
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

/// <summary>
/// #1503 (specVersion 31, area 06): the plans a process built from orchestration histories, by <c>resolveHash</c> and regex
/// limit (a plan is a function of the two), the most recently used <see cref="Capacity"/> kept. Safe to use concurrently.
/// </summary>
internal sealed class V31PlanCache(int capacity)
{
    private readonly object _gate = new();
    private readonly Dictionary<(string, int), LinkedListNode<((string, int) Key, RunPlan Plan)>> _byKey = new();
    private readonly LinkedList<((string, int) Key, RunPlan Plan)> _recent = new();

    /// <summary>The most plans kept.</summary>
    public int Capacity { get; } = capacity >= 1 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    /// <summary>How many plans are kept.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _byKey.Count;
        }
    }

    /// <summary>The plan kept for the two, made the most recently used; null when none is.</summary>
    public RunPlan? TryGet(string resolveHash, int regexMilliseconds)
    {
        lock (_gate)
        {
            if (!_byKey.TryGetValue((resolveHash, regexMilliseconds), out var node))
                return null;
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Plan;
        }
    }

    /// <summary>Keeps <paramref name="plan"/> for the two (the one already kept, if any, is returned instead), dropping the least recently used past the capacity.</summary>
    public RunPlan Add(string resolveHash, int regexMilliseconds, RunPlan plan)
    {
        lock (_gate)
        {
            var key = (resolveHash, regexMilliseconds);
            if (_byKey.TryGetValue(key, out var kept))
                return kept.Value.Plan;
            _byKey[key] = _recent.AddFirst((key, plan));
            while (_byKey.Count > Capacity)
            {
                var last = _recent.Last!;
                _recent.RemoveLast();
                _byKey.Remove(last.Value.Key);
            }

            return plan;
        }
    }

    /// <summary>Forgets every plan (tests: a cold cache).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _byKey.Clear();
            _recent.Clear();
        }
    }
}
