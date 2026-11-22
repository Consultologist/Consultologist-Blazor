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
/// of the durations (nearest rank, over the jobs that have one), and the two lines areas 09 and 10 fill. Counts and minutes
/// only.
/// </summary>
public static class OperatorSummary
{
    /// <summary>D14: what the lines this build cannot measure read.</summary>
    public const string NotMeasured = "not measured in this build";

    /// <summary>The summary of <paramref name="jobs"/>, each its status and its duration (null when it has none): subject and body.</summary>
    public static (string Subject, string Body) Compose(IReadOnlyList<(string Status, TimeSpan? Duration)> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var durations = jobs.Where(j => j.Duration != null).Select(j => j.Duration!.Value).Order().ToList();
        var failures = jobs.Count(j => string.Equals(j.Status, ConsultGenerationJobStatuses.Failed, StringComparison.Ordinal));
        var body = new StringBuilder()
            .Append("Jobs: ").Append(jobs.Count.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("Failures: ").Append(failures.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("p50 duration: ").Append(Minutes(NearestRank(durations, 50))).Append('\n')
            .Append("p90 duration: ").Append(Minutes(NearestRank(durations, 90))).Append('\n')
            .Append("Largest record: ").Append(NotMeasured).Append('\n')
            .Append("Outstanding migration hand edits: ").Append(NotMeasured).Append('\n');
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
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): the weekly summary for the operator, on
/// <c>%OperatorWeeklySummarySchedule%</c>: off when that is unset (the function fails indexing and is disabled, the host
/// unaffected, as <see cref="TextRetentionFunctions"/>' timer is), and reading nothing unless this engine runs 31
/// (<see cref="SpecVersionAdmission.Runs31"/>). The week's jobs are every account's (<see cref="IAccountStore.ListAsync"/>,
/// the accounts the operator surfaces read) jobs in the job index that ended in the 7 days before the run, each its status
/// and its duration from its start to its end. The summary is logged as counts and mailed as alerts are
/// (<see cref="OperatorAlerts"/>: to <c>Operators__AlertAddress</c> from the mailbox, else only logged).
/// </summary>
public sealed class OperatorWeeklySummary(
    SpecVersionAdmission admission,
    IAccountStore accounts,
    IConsultGenerationJobIndexStore index,
    IGraphMailClient mail,
    IConfiguration configuration,
    ILogger<OperatorWeeklySummary> logger)
{
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
        foreach (var account in await accounts.ListAsync(ct))
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
                jobs.Add((entry.Status, entry.StartedAtUtc is { } started && ended >= started ? ended - started : null));
            }
        }

        var (subject, body) = OperatorSummary.Compose(jobs);
        logger.LogInformation(
            "Operator weekly summary. Jobs={Jobs}, Failures={Failures}",
            jobs.Count,
            jobs.Count(j => string.Equals(j.Status, ConsultGenerationJobStatuses.Failed, StringComparison.Ordinal)));
        await OperatorAlerts.MailAsync(mail, configuration, logger, "weekly-summary", subject, body, ct);
        return true;
    }
}
