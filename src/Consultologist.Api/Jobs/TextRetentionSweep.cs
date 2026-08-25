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
}

public sealed class JobTextPurger : IJobTextPurger
{
    private readonly IConsultGenerationJobEventStore _events;

    public JobTextPurger(IConsultGenerationJobEventStore events)
    {
        _events = events;
    }

    /// <summary>The orchestration instance is the job id; the entity is its own instance and stays.</summary>
    public static string OrchestrationInstanceId(string jobId) => jobId;

    public async Task PurgeAsync(DurableTaskClient client, string jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), jobId);
        await client.Entities.SignalEntityAsync(entityId, nameof(ConsultGenerationJobEntity.DropText), new ConsultGenerationTextDrop(now), cancellation: cancellationToken);
        await client.PurgeInstanceAsync(OrchestrationInstanceId(jobId), cancellationToken);
        await _events.DeleteJobAsync(jobId, cancellationToken);
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
