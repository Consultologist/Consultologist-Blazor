using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #368: delete one job's produced text everywhere it rests — the entity
/// (DropText, which records the act), the Durable orchestration instance
/// (the input with the referral text, every rendered prompt and raw model
/// output), and the streamed-event rows. Entity first: if the sweep is
/// interrupted, the record already says the text is gone and the next run
/// removes the copies. The entity's own instance is never purged.
/// </summary>
public interface IJobTextPurger
{
    Task PurgeAsync(DurableTaskClient client, string jobId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// #548: the inputs clock fired alone — signal the entity to drop the
    /// held inputs and nothing else. No instance purge, no events delete:
    /// the produced text is still being served, and those are the full
    /// drop's to run when the outputs clock arrives.
    /// </summary>
    Task DropInputsAsync(DurableTaskClient client, string jobId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// #557: the transition's second delete — the events table moved to the text
/// account, and rows written before the move sit on the records account
/// until their jobs purge. This deletes a job's old-table partition; it
/// never creates the old table (M6/#558 wants it empty and removable) and a
/// missing table reads as nothing to delete. Removed by #558 once empty.
/// No dual-READ on purpose: the SSE loop exits on terminal status whatever
/// the sequence says, events re-derive from the response into the new
/// table, and the GET path never depends on this table — the loss is
/// bounded to resume continuity for streams open at the deploy minute.
/// </summary>
public interface ILegacyJobEventDelete
{
    Task DeleteJobAsync(string jobId, CancellationToken cancellationToken);
}

public sealed class LegacyJobEventDelete : ILegacyJobEventDelete
{
    private readonly Azure.Data.Tables.TableClient _oldTable;

    public LegacyJobEventDelete(Microsoft.Extensions.Configuration.IConfiguration configuration, Azure.Core.TokenCredential credential)
    {
        // The pre-#557 chain, verbatim — the records account.
        _oldTable = StorageTables.CreateClient(
            configuration, credential, "ConsultGenerationJobEvents", "ConsultGenerationJobEventStorage", "AccountStorage");
    }

    public async Task DeleteJobAsync(string jobId, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var row in _oldTable.QueryAsync<Azure.Data.Tables.TableEntity>(
                e => e.PartitionKey == jobId, select: new[] { "PartitionKey", "RowKey" }, cancellationToken: cancellationToken))
            {
                await _oldTable.DeleteEntityAsync(row.PartitionKey, row.RowKey, cancellationToken: cancellationToken);
            }
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // The old table is gone (or was never created locally): nothing
            // to delete — the state #558 drives toward.
        }
    }
}

public sealed class JobTextPurger : IJobTextPurger
{
    private readonly IConsultGenerationJobEventStore _events;
    private readonly ILegacyJobEventDelete _legacyEvents;

    public JobTextPurger(IConsultGenerationJobEventStore events, ILegacyJobEventDelete legacyEvents)
    {
        _events = events;
        _legacyEvents = legacyEvents;
    }

    /// <summary>The orchestration instance is the job id; the entity is its own instance and stays.</summary>
    public static string OrchestrationInstanceId(string jobId) => jobId;

    public async Task PurgeAsync(DurableTaskClient client, string jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), jobId);
        await client.Entities.SignalEntityAsync(entityId, nameof(ConsultGenerationJobEntity.DropText), new ConsultGenerationTextDrop(now), cancellation: cancellationToken);
        await client.PurgeInstanceAsync(OrchestrationInstanceId(jobId), cancellationToken);
        await _events.DeleteJobAsync(jobId, cancellationToken);
        await _legacyEvents.DeleteJobAsync(jobId, cancellationToken);
    }

    public async Task DropInputsAsync(DurableTaskClient client, string jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), jobId);
        await client.Entities.SignalEntityAsync(entityId, nameof(ConsultGenerationJobEntity.DropInputs), new ConsultGenerationInputsDrop(now), cancellation: cancellationToken);
    }
}

/// <summary>
/// #368: the retention sweep. Every account, every terminal job completed
/// more than TextRetention__Days ago whose text is still present, purged.
/// Provenance stays; the text is the one thing the record ever loses.
/// </summary>
public sealed class TextRetentionSweep
{
    public const string DaysSetting = "TextRetention__Days";
    public const int DefaultDays = 7;

    private readonly IAccountStore _accounts;
    private readonly IConsultGenerationJobIndexStore _index;
    private readonly IJobTextPurger _purger;
    private readonly ILogger<TextRetentionSweep> _logger;

    public TextRetentionSweep(IAccountStore accounts, IConsultGenerationJobIndexStore index, IJobTextPurger purger, ILogger<TextRetentionSweep> logger)
    {
        _accounts = accounts;
        _index = index;
        _purger = purger;
        _logger = logger;
    }

    public static int RetentionDays() =>
        int.TryParse(Environment.GetEnvironmentVariable(DaysSetting), out var days) && days > 0 ? days : DefaultDays;

    /// <summary>The rule: terminal, completed before the cutoff, text still present.</summary>
    public static bool IsDue(ConsultGenerationJobIndexEntry entry, DateTimeOffset completedBefore) =>
        ConsultGenerationJobEntity.IsTerminal(entry.Status)
        && entry.CompletedAtUtc is { } completed && completed < completedBefore
        && entry.TextDroppedAtUtc == null;

    public async Task<(int Accounts, int Due, int Dropped)> RunOnceAsync(DurableTaskClient client, DateTimeOffset now, int retentionDays, CancellationToken cancellationToken)
    {
        var cutoff = now.AddDays(-retentionDays);
        int accounts = 0, due = 0, dropped = 0;

        foreach (var account in await _accounts.ListAsync(cancellationToken))
        {
            accounts++;
            IReadOnlyList<ConsultGenerationJobIndexEntry> jobs;
            try
            {
                jobs = await _index.ListDueForTextDropAsync(account.AppUserId, cutoff, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Text retention: could not list an account's jobs.");
                continue;
            }

            foreach (var job in jobs)
            {
                due++;
                try
                {
                    await _purger.PurgeAsync(client, job.JobId, now, cancellationToken);
                    dropped++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Next run retries: the index row still says the text is present.
                    _logger.LogWarning(ex, "Text retention: could not purge job {JobId}.", job.JobId);
                }
            }
        }

        var summary = $"{accounts} accounts, {due} jobs past {retentionDays} days, {dropped} dropped";
        _logger.LogInformation("Text retention: {Summary}", summary);
        Console.Error.WriteLine($"[TextRetention] {summary}");
        return (accounts, due, dropped);
    }
}

public sealed class TextRetentionFunctions
{
    private readonly TextRetentionSweep _sweep;

    public TextRetentionFunctions(TextRetentionSweep sweep)
    {
        _sweep = sweep;
    }

    // Flat name by necessity, as EmailIntakePollSchedule: %…% binding
    // expressions resolve literal config keys. Unset → this function fails
    // indexing and is disabled (host unaffected) — the retention policy then
    // does not run, and CONFIGURATION.md says the setting is required.
    [Function("TextRetentionSweep")]
    public Task RunAsync(
        [TimerTrigger("%TextRetentionSweepSchedule%")] TimerInfo timer,
        [DurableClient] DurableTaskClient client,
        FunctionContext context)
        => _sweep.RunOnceAsync(client, DateTimeOffset.UtcNow, TextRetentionSweep.RetentionDays(), context.CancellationToken);
}
