using System.Globalization;
using System.Text;
using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): the weekly summary's message: the jobs, the failures, the p50 and p90
/// of the durations (nearest rank, over the jobs that have one), the largest record sealed in the week (#1508, area 11:
/// <see cref="RecordFigures"/>) and #1507's (area 10; R11, D22) outstanding migration hand edits. Counts, sizes, minutes
/// and a job id only.
/// </summary>
public static class OperatorSummary
{
    /// <summary>
    /// The summary of <paramref name="jobs"/>, each its status and its duration (null when it has none), and of
    /// <paramref name="migration"/>, the hand-edits line's figures: subject and body.
    /// </summary>
    public static (string Subject, string Body) Compose(
        IReadOnlyList<(string Status, TimeSpan? Duration)> jobs, MigrationFigures migration, RecordFigures? record = null)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(migration);
        var durations = jobs.Where(j => j.Duration != null).Select(j => j.Duration!.Value).Order().ToList();
        var failures = jobs.Count(j => string.Equals(j.Status, ConsultGenerationJobStatuses.Failed, StringComparison.Ordinal));
        var body = new StringBuilder()
            .Append("Jobs: ").Append(jobs.Count.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("Failures: ").Append(failures.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("p50 duration: ").Append(Minutes(NearestRank(durations, 50))).Append('\n')
            .Append("p90 duration: ").Append(Minutes(NearestRank(durations, 90))).Append('\n');
        foreach (var line in (record ?? RecordFigures.None).Lines)
            body.Append(line).Append('\n');
        foreach (var line in migration.Lines)
            body.Append(line).Append('\n');
        return ("Consultologist weekly summary", body.ToString());
    }

    /// <summary>The <paramref name="percent"/>th percentile of <paramref name="sorted"/> by nearest rank: the value at rank ⌈p/100 × n⌉; null for none.</summary>
    private static TimeSpan? NearestRank(IReadOnlyList<TimeSpan> sorted, int percent)
    {
        if (sorted.Count == 0)
            return null;
        var rank = (int)Math.Ceiling(percent / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    /// <summary>A duration in minutes, to a tenth: <c>1 minute</c>, <c>5 minutes</c>, <c>1.5 minutes</c>; <c>none</c> for none.</summary>
    private static string Minutes(TimeSpan? duration)
    {
        if (duration is not { } value)
            return "none";
        var minutes = Math.Round(value.TotalMinutes, 1, MidpointRounding.AwayFromZero);
        return minutes == 1 ? "1 minute" : $"{minutes.ToString("0.#", CultureInfo.InvariantCulture)} minutes";
    }
}

/// <summary>
/// #1508 (area 11, Task 21): the week's sealed records as the job index rows give them: the largest (bytes, rows, job) and
/// how many sealed 31 jobs' rows hold no size (sealed before sizes were written, or not sealed yet). What is not read is
/// said, never a zero: with none read, <c>not measured</c>; with some, the largest of those and the count not counted. Ids,
/// counts and sizes only.
/// </summary>
public sealed record RecordFigures(long? Bytes, int? Rows, string? JobId, int NotRead)
{
    /// <summary>No 31 job sealed in the week.</summary>
    public static RecordFigures None { get; } = new(null, null, null, 0);

    /// <summary>The figures of the week's index <paramref name="entries"/> (the jobs that ended in the week); ties go to the lower job id, ordinal.</summary>
    public static RecordFigures Of(IEnumerable<ConsultGenerationJobIndexEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ConsultGenerationJobIndexEntry? largest = null;
        var notRead = 0;
        foreach (var entry in entries.Where(e => e.RecordStorage >= RecordSealer.SealedFrom))
        {
            if (entry.RecordBytes is not { } bytes || entry.RecordRows == null)
            {
                notRead++;
                continue;
            }

            if (largest == null || bytes > largest.RecordBytes
                || (bytes == largest.RecordBytes && string.CompareOrdinal(entry.JobId, largest.JobId) < 0))
                largest = entry;
        }

        return new RecordFigures(largest?.RecordBytes, largest?.RecordRows, largest?.JobId, notRead);
    }

    /// <summary>The summary's lines for them.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var number = CultureInfo.InvariantCulture;
            if (Bytes is not { } bytes)
                return [NotRead == 0
                    ? "Largest record: none"
                    : $"Largest record: not measured; the sizes of {NotRead.ToString("N0", number)} sealed records could not be read."];
            var largest = $"Largest record: {bytes.ToString("N0", number)} bytes, {Rows!.Value.ToString("N0", number)} rows (job {JobId})";
            return NotRead == 0
                ? [largest]
                : [largest, $"Largest record: {NotRead.ToString("N0", number)} sealed records could not be read and are not counted."];
        }
    }
}

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): the weekly summary for the operator, on
/// <c>%OperatorWeeklySummarySchedule%</c>: off when that is unset (the function fails indexing and is disabled, the host
/// unaffected, as <see cref="TextRetentionFunctions"/>' timer is), and reading nothing unless this engine runs 31
/// (<see cref="SpecVersionAdmission.Runs31"/>). The week's jobs are every account's (<see cref="IAccountStore.ListAsync"/>,
/// the accounts the operator surfaces read) jobs in the job index that ended in the 7 days before the run, each its status
/// and its duration from its start to its end. The summary is logged as counts and mailed as alerts are
/// (<see cref="OperatorAlerts"/>: to <c>Operators__AlertAddress</c> from the mailbox, else only logged). #1507 (area 10;
/// R11, D22): the hand-edits line is the same accounts' active packages migrated (<see cref="OutstandingHandEdits"/>), in
/// this function, never in an orchestration; what it cannot read is its error line, never an exception out of the summary.
/// </summary>
public sealed class OperatorWeeklySummary(
    SpecVersionAdmission admission,
    IAccountStore accounts,
    IConsultGenerationJobIndexStore index,
    OutstandingHandEdits handEdits,
    IGraphMailClient mail,
    IConfiguration configuration,
    ILogger<OperatorWeeklySummary> logger)
{
    private readonly OutstandingHandEdits _handEdits = handEdits ?? throw new ArgumentNullException(nameof(handEdits));

    /// <summary>The schedule's setting: a flat name, since a <c>%…%</c> binding expression cannot resolve a <c>__</c> key.</summary>
    public const string ScheduleSetting = "OperatorWeeklySummarySchedule";

    [Function("OperatorWeeklySummary")]
    public Task RunAsync([TimerTrigger("%OperatorWeeklySummarySchedule%")] TimerInfo timer, FunctionContext context)
        => RunOnceAsync(DateTimeOffset.UtcNow, context.CancellationToken);

    /// <summary>Composes the week to <paramref name="now"/> and sends it. False when this engine does not run 31, and nothing is read.</summary>
    public async Task<bool> RunOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!admission.Runs31)
            return false;

        var from = now.AddDays(-7);
        var jobs = new List<(string Status, TimeSpan? Duration)>();
        var inWeek = new List<ConsultGenerationJobIndexEntry>();
        var listed = await accounts.ListAsync(ct);
        foreach (var account in listed)
        {
            IReadOnlyList<ConsultGenerationJobIndexEntry> entries;
            try
            {
                entries = await index.ListAllAsync(account.AppUserId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Operator weekly summary: could not list an account's jobs. ExceptionType={ExceptionType}", ex.GetType().FullName);
                continue;
            }

            foreach (var entry in entries)
            {
                if (!ConsultGenerationJobEntity.IsTerminal(entry.Status) || entry.CompletedAtUtc is not { } ended || ended <= from || ended > now)
                    continue;
                inWeek.Add(entry);
                jobs.Add((entry.Status, entry.StartedAtUtc is { } started && ended >= started ? ended - started : null));
            }
        }

        var migration = await _handEdits.CountAsync(listed, ct);
        var (subject, body) = OperatorSummary.Compose(jobs, migration, RecordFigures.Of(inWeek));
        logger.LogInformation(
            "Operator weekly summary. Jobs={Jobs}, Failures={Failures}",
            jobs.Count,
            jobs.Count(j => string.Equals(j.Status, ConsultGenerationJobStatuses.Failed, StringComparison.Ordinal)));
        // the migration figures only as far as they were gathered: no zero for what was not read
        if (migration.NotGathered is { } notGathered)
            logger.LogInformation("Operator weekly summary: migration hand edits not counted. NotRead={NotRead}", notGathered);
        if (migration.HandEdits is { } handEdits)
            logger.LogInformation(
                "Operator weekly summary: migration hand edits. HandEdits={HandEdits}, HandEditPackages={HandEditPackages}, NotMigrating={NotMigrating}",
                handEdits, migration.Packages, migration.DoNotMigrate);
        if (migration.NotCounted > 0)
            logger.LogInformation("Operator weekly summary: active packages not read. NotCounted={NotCounted}", migration.NotCounted);
        await OperatorAlerts.MailAsync(mail, configuration, logger, "weekly-summary", subject, body, ct);
        return true;
    }
}
