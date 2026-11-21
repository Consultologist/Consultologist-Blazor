using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Consultologist.Api.V31;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #1503 (part 09 § 3.6; D9): a job started during a drain: its id, the orchestrator it runs on, its orchestration input
/// as the starter built it (JSON, as Durable writes it), and when it was parked.
/// </summary>
public sealed record ParkedJob(string JobId, string OrchestratorName, string InputJson, DateTimeOffset ParkedAtUtc);

/// <summary>
/// #1503 (part 09 § 3.6; D9): the drain before a deploy. While the engine drains, every door still answers a new job with
/// its id, and the starter parks the job's orchestration input here instead of scheduling it; <c>Admin/Resume</c> ends the
/// drain and schedules what is parked, oldest first. Every call reads the engine's own storage; nothing is cached, so
/// every instance of the engine agrees.
/// </summary>
public interface IEngineDrain
{
    /// <summary>Whether the engine is draining.</summary>
    Task<bool> IsDrainingAsync(CancellationToken ct);

    /// <summary>Begins a drain (again, if one is on: nothing changes).</summary>
    Task BeginAsync(CancellationToken ct);

    /// <summary>
    /// Ends the drain and returns every job still parked, oldest first (ties by job id, ordinally). A job stays parked
    /// until <see cref="ForgetAsync"/>, which Resume calls once the job's orchestration is scheduled, so a Resume that
    /// stopped part way leaves the rest to the next one.
    /// </summary>
    Task<IReadOnlyList<ParkedJob>> EndAsync(CancellationToken ct);

    /// <summary>Parks <paramref name="job"/>.</summary>
    Task ParkAsync(ParkedJob job, CancellationToken ct);

    /// <summary>Forgets the parked job <paramref name="jobId"/>: its orchestration is scheduled.</summary>
    Task ForgetAsync(string jobId, CancellationToken ct);

    /// <summary>How many jobs are parked.</summary>
    Task<int> ParkedCountAsync(CancellationToken ct);
}

/// <summary>#1503 (D9): the blobs the drain keeps, as JSON documents by name.</summary>
public interface IDrainBlobs
{
    /// <summary>The document named <paramref name="name"/>, or null when there is none.</summary>
    Task<JsonObject?> ReadAsync(string name, CancellationToken ct);

    Task WriteAsync(string name, JsonObject document, CancellationToken ct);

    /// <summary>Deletes <paramref name="name"/>; one already gone is no error.</summary>
    Task DeleteAsync(string name, CancellationToken ct);

    /// <summary>The names under <paramref name="prefix"/>, ordinally.</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct);
}

/// <summary>
/// #1503 (part 09 § 3.6; D9): the drain's state in the engine's own storage: the flag is <see cref="StateBlob"/>
/// (<c>{ "draining": true|false }</c>) and each parked job is its own blob under <see cref="ParkedPrefix"/>.
/// </summary>
public sealed class EngineDrain(IDrainBlobs blobs) : IEngineDrain
{
    /// <summary>The flag's blob.</summary>
    public const string StateBlob = "drain/state.json";

    /// <summary>Where the parked jobs are, one blob each: <c>drain/parked/&lt;jobId&gt;.json</c>.</summary>
    public const string ParkedPrefix = "drain/parked/";

    /// <summary>The external event Resume raises for every orchestration waiting behind the drain.</summary>
    public const string ResumedEvent = "engine-resumed";

    public async Task<bool> IsDrainingAsync(CancellationToken ct) =>
        await blobs.ReadAsync(StateBlob, ct) is { } state
        && state["draining"] is JsonValue draining
        && draining.TryGetValue<bool>(out var on)
        && on;

    public Task BeginAsync(CancellationToken ct) => blobs.WriteAsync(StateBlob, new JsonObject { ["draining"] = true }, ct);

    public async Task<IReadOnlyList<ParkedJob>> EndAsync(CancellationToken ct)
    {
        await blobs.WriteAsync(StateBlob, new JsonObject { ["draining"] = false }, ct);

        var parked = new List<ParkedJob>();
        foreach (var name in await blobs.ListAsync(ParkedPrefix, ct))
        {
            if (await blobs.ReadAsync(name, ct) is { } document && document.Deserialize<ParkedJob>() is { } job)
            {
                parked.Add(job);
            }
        }

        return parked
            .OrderBy(job => job.ParkedAtUtc)
            .ThenBy(job => job.JobId, StringComparer.Ordinal)
            .ToList();
    }

    public Task ParkAsync(ParkedJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        return blobs.WriteAsync(NameOf(job.JobId), JsonSerializer.SerializeToNode(job)!.AsObject(), ct);
    }

    public Task ForgetAsync(string jobId, CancellationToken ct) => blobs.DeleteAsync(NameOf(jobId), ct);

    public async Task<int> ParkedCountAsync(CancellationToken ct) => (await blobs.ListAsync(ParkedPrefix, ct)).Count;

    private static string NameOf(string jobId) => $"{ParkedPrefix}{jobId}.json";
}

/// <summary>
/// #1503 (D9): the drain's blobs on the text account, in the container the job outputs use (a personal account's,
/// <see cref="JobOutputsBlobStore"/>'s default), under <c>drain/</c>: a parked job's input can hold a request's text, so it
/// lives where the produced text does. No account's prefix is <c>drain/</c>, so an account's closure never touches it.
/// </summary>
public sealed class TextDrainBlobs : IDrainBlobs
{
    private readonly TextBlobClientFactory _blobs;

    public TextDrainBlobs(IConfiguration configuration, TokenCredential credential, ILogger<TextDrainBlobs> logger)
    {
        _blobs = new TextBlobClientFactory(configuration, credential, logger, "Engine drain store");
    }

    /// <summary>The container: the job outputs' (a personal account's).</summary>
    internal static string Container => JobOutputsBlobStore.ContainerFor(null);

    public Task<JsonObject?> ReadAsync(string name, CancellationToken ct) => _blobs.ReadJsonAsync<JsonObject>(Container, name, ct);

    public Task WriteAsync(string name, JsonObject document, CancellationToken ct) => _blobs.WriteJsonAsync(Container, name, document, ct);

    public Task DeleteAsync(string name, CancellationToken ct) => _blobs.DeleteAsync(Container, name, ct);

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct) => _blobs.ListNamesAsync(Container, prefix, ct);
}

/// <summary>
/// #1503 (part 09 §§ 3.4, 3.6; D9): the drain at the doors and in the orchestrations. A door parks a job instead of
/// scheduling it while the engine drains (<see cref="ParkIfDrainingAsync"/>); Resume starts what is parked
/// (<see cref="StartParkedAsync"/>); and a drain-aware orchestration waits, after its schedule wait and before anything
/// else, while the engine drains (<see cref="WaitWhileDrainingAsync"/>).
/// </summary>
public static class EngineDrainJobs
{
    /// <summary>How long a waiting orchestration waits for <see cref="EngineDrain.ResumedEvent"/> before it checks the drain again.</summary>
    public static readonly TimeSpan Recheck = TimeSpan.FromMinutes(5);

    /// <summary>check-drain reads one blob; a storage hiccup is retried before it fails the job.</summary>
    private static readonly TaskOptions CheckDrainRetry = TaskOptions.FromRetryPolicy(
        new RetryPolicy(maxNumberOfAttempts: 3, firstRetryInterval: TimeSpan.FromSeconds(5), backoffCoefficient: 2.0));

    /// <summary>A job of <paramref name="orchestratorName"/> parked with <paramref name="input"/>, written as Durable writes an input.</summary>
    public static ParkedJob Park(string jobId, string orchestratorName, object input, DateTimeOffset parkedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new ParkedJob(jobId, orchestratorName, JsonSerializer.Serialize(input, input.GetType()), parkedAtUtc);
    }

    /// <summary>The parked input, typed as its orchestrator reads it, so it is scheduled exactly as the door would have.</summary>
    public static object InputOf(ParkedJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return job.OrchestratorName switch
        {
            nameof(ConsultGenerationOrchestrator) => JsonSerializer.Deserialize<ConsultGenerationOrchestrationInput>(job.InputJson)!,
            nameof(ConsultGenerationOrchestratorV31) => JsonSerializer.Deserialize<ConsultGenerationOrchestrationInputV31>(job.InputJson)!,
            _ => throw new InvalidOperationException($"Parked job {job.JobId} names no orchestrator of this engine."),
        };
    }

    /// <summary>
    /// A door's last step: while the engine drains, parks the job (its entity already initialised) and marks it waiting
    /// (S09-018), and answers true, so the door answers with the job's id and schedules nothing. Should the drain end
    /// between the check and the park (its Resume already past), the job is started at once. False when not draining.
    /// </summary>
    public static async Task<bool> ParkIfDrainingAsync(
        IEngineDrain? drain, DurableTaskClient client, string jobId, string orchestratorName, object input, ILogger logger, CancellationToken ct)
    {
        if (drain == null || !await drain.IsDrainingAsync(ct))
        {
            return false;
        }

        var job = Park(jobId, orchestratorName, input, DateTimeOffset.UtcNow);
        await drain.ParkAsync(job, ct);
        await client.Entities.SignalEntityAsync(EntityOf(jobId), nameof(ConsultGenerationJobEntity.MarkWaitingForUpdate), null, null, ct);
        logger.LogInformation("Consult generation job parked behind a drain. JobId={JobId}, Orchestrator={Orchestrator}", jobId, orchestratorName);

        if (!await drain.IsDrainingAsync(ct))
        {
            await StartParkedAsync(client, drain, job, ct);
        }

        return true;
    }

    /// <summary>
    /// Starts a parked job once: unless its orchestration already exists (a Resume before this one scheduled it), its
    /// record stops waiting and its orchestration is scheduled with the job's id as its instance id; then the job is
    /// forgotten. True when this call scheduled it.
    /// </summary>
    public static async Task<bool> StartParkedAsync(DurableTaskClient client, IEngineDrain drain, ParkedJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var started = false;
        if (await client.GetInstanceAsync(job.JobId, getInputsAndOutputs: false, ct) == null)
        {
            await client.Entities.SignalEntityAsync(EntityOf(job.JobId), nameof(ConsultGenerationJobEntity.EndWaitingForUpdate), null, null, ct);
            await client.ScheduleNewOrchestrationInstanceAsync(
                job.OrchestratorName, InputOf(job), new StartOrchestrationOptions { InstanceId = job.JobId }, ct);
            started = true;
        }

        await drain.ForgetAsync(job.JobId, ct);
        return started;
    }

    /// <summary>
    /// A drain-aware orchestration's step after its schedule wait: while check-drain says the engine drains, it publishes
    /// the waiting status (and marks its job waiting, once) and waits for <see cref="EngineDrain.ResumedEvent"/>, checking
    /// again every <see cref="Recheck"/> in case Resume came before it began to wait. Every step is in the history, so a
    /// replay repeats it.
    /// </summary>
    internal static async Task WaitWhileDrainingAsync(TaskOrchestrationContext context, EntityInstanceId entityId)
    {
        var marked = false;
        while (await context.CallActivityAsync<bool>(ConsultGenerationActivityNames.CheckDrain, options: CheckDrainRetry))
        {
            context.SetCustomStatus(new { status = ConsultGenerationJobStatuses.WaitingForUpdate });
            if (!marked)
            {
                await context.Entities.CallEntityAsync(entityId, nameof(ConsultGenerationJobEntity.MarkWaitingForUpdate));
                marked = true;
            }

            using var wait = new CancellationTokenSource();
            var resumed = context.WaitForExternalEvent<object?>(EngineDrain.ResumedEvent, wait.Token);
            var recheck = context.CreateTimer(context.CurrentUtcDateTime.Add(Recheck), wait.Token);
            var first = await Task.WhenAny(resumed, recheck);
            wait.Cancel();
            if (first == resumed)
            {
                return;
            }
        }
    }

    private static EntityInstanceId EntityOf(string jobId) => new(nameof(ConsultGenerationJobEntity), jobId);
}

/// <summary>#1503 (part 09 § 3.6; D9): check-drain — whether the engine is draining, for a drain-aware orchestration.</summary>
public sealed class CheckDrainActivity(IEngineDrain drain)
{
    [Function(ConsultGenerationActivityNames.CheckDrain)]
    public Task<bool> RunAsync([ActivityTrigger] string? input, CancellationToken cancellationToken) => drain.IsDrainingAsync(cancellationToken);
}
