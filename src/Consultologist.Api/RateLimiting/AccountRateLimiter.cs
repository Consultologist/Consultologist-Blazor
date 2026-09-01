using System.Globalization;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.RateLimiting;

/// <summary>
/// What one account may spend, and what is left of it.
/// <paramref name="RetryAfter"/> is the distance to the next window and is
/// only meaningful when refused.
/// </summary>
public sealed record RateLimitDecision(bool Allowed, int Limit, int Remaining, TimeSpan RetryAfter);

public interface IAccountRateLimiter
{
    /// <summary>
    /// Spends one submission against the account's current window. A refused
    /// call spends nothing — otherwise a rate-limited email would burn a unit
    /// on every retry and starve the account permanently.
    /// </summary>
    Task<RateLimitDecision> TryAcquireAsync(string appUserId, CancellationToken cancellationToken);

    /// <summary>
    /// #558: drops the account's windows strictly older than the cutoff and
    /// returns how many went. The cutoff is itself a window key
    /// (<see cref="TableAccountRateLimiter.CleanupCutoffWindow"/>) — the keys
    /// are ordinal-comparable, so <c>RowKey lt</c> IS the date comparison.
    /// A row deleted underneath by a concurrent run is tolerated, not a fault.
    /// </summary>
    Task<int> DeleteWindowsBeforeAsync(string appUserId, string cutoffWindow, CancellationToken cancellationToken);
}

public static class AccountRateLimiterExtensions
{
    /// <summary>
    /// What every door calls. <see cref="TableAccountRateLimiter"/> already
    /// fails open on its own storage faults, so this is a second layer of the
    /// same guarantee — worth having precisely because the failure is
    /// asymmetric: losing the limit costs CPU for the duration of a fault,
    /// while refusing on one costs a clinician their referral. Enforcing it at
    /// the boundary makes it true of any implementation, not just that one.
    ///
    /// Cancellation is not a fault and is not swallowed.
    /// </summary>
    public static async Task<RateLimitDecision> AcquireOrAllowAsync(
        this IAccountRateLimiter limiter,
        string appUserId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            return await limiter.TryAcquireAsync(appUserId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Rate limiter faulted; allowing the submission. AppUserId={AppUserId}", appUserId);
            return new RateLimitDecision(true, 0, int.MaxValue, TimeSpan.Zero);
        }
    }
}

/// <summary>
/// Per-account rate limiting (#266, docs/DOCUMENT_INPUT.md § 9). The first
/// thing in this app that deliberately refuses a valid request from a
/// legitimate user, and the first rate limiting of any kind.
///
/// **The unit is one submission** — a preview call or a job start, whatever
/// it carries. That is what a clinician recognises as "a consult", and it is
/// deliberately not the CPU unit: a submission with three attachments costs
/// exactly what a 20 KB text file costs. The concurrency gate (#265) is what
/// bounds parse cost; this bounds how often one account can ask.
///
/// **Fixed window, aligned to the UTC hour.** Simplest to reason about and
/// to explain, at the cost of a burst of up to twice the limit straddling a
/// boundary. Sliding would cost a row per submission and a pruning story for
/// an edge that does not matter at this scale.
///
/// **Table-backed rather than in-memory**, because Flex Consumption scales
/// to many instances and recycles workers: N instances would mean N times
/// the stated limit, and a recycle would reset it. The stated limit would
/// not be the enforced one.
/// </summary>
public sealed class TableAccountRateLimiter : IAccountRateLimiter
{
    private const string TableName = "AccountRateLimits";
    private const int MaxAttempts = 3;
    internal const int DefaultSubmissionsPerHour = 60;

    private readonly TableClient _table;
    private readonly int _limit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TableAccountRateLimiter> _logger;
    private bool _tableEnsured;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    public TableAccountRateLimiter(
        IConfiguration configuration,
        TokenCredential credential,
        TimeProvider timeProvider,
        ILogger<TableAccountRateLimiter> logger)
    {
        _table = StorageTables.CreateClient(configuration, credential, TableName, "AccountStorage");
        _limit = configuration.GetValue("RateLimits:SubmissionsPerHour", DefaultSubmissionsPerHour);
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<RateLimitDecision> TryAcquireAsync(string appUserId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (_limit <= 0)
        {
            // The kill switch, and what local dev and CI run on — the same
            // idiom EmailIntake:MailboxAddress uses for the poller.
            return Unlimited(now);
        }

        var windowKey = WindowKey(now);

        try
        {
            await EnsureTableAsync(cancellationToken);

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entity = await TryGetAsync(appUserId, windowKey, cancellationToken);
                var used = entity?.Count ?? 0;
                var decision = Decide(used, _limit, now);

                if (!decision.Allowed)
                {
                    _logger.LogWarning(
                        "Rate limit reached. AppUserId={AppUserId}, Window={Window}, Used={Used}, Limit={Limit}",
                        appUserId,
                        windowKey,
                        used,
                        _limit);
                    return decision;
                }

                try
                {
                    if (entity == null)
                    {
                        await _table.AddEntityAsync(
                            new AccountRateLimitEntity
                            {
                                PartitionKey = appUserId,
                                RowKey = windowKey,
                                Count = 1,
                                UpdatedAtUtc = now
                            },
                            cancellationToken);
                    }
                    else
                    {
                        entity.Count = used + 1;
                        entity.UpdatedAtUtc = now;
                        await _table.UpdateEntityAsync(
                            entity,
                            entity.ETag,
                            TableUpdateMode.Merge,
                            cancellationToken);
                    }

                    return decision;
                }
                catch (RequestFailedException ex) when (ex.Status is 409 or 412)
                {
                    // Another instance spent from the same window between the
                    // read and the write. Re-read and decide again: the loser
                    // of the race may now be over the limit, which is the
                    // whole reason this is conditional rather than a blind
                    // increment.
                    _logger.LogDebug(
                        ex,
                        "Retrying rate-limit increment after a storage concurrency response. AppUserId={AppUserId}, Attempt={Attempt}",
                        appUserId,
                        attempt);
                }
            }

            _logger.LogWarning(
                "Rate-limit increment gave up after {Attempts} contended attempts; allowing. AppUserId={AppUserId}",
                MaxAttempts,
                appUserId);
            return Unlimited(now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail OPEN, and this is the deliberate asymmetry. A storage
            // fault must never reject a clinician's consult: the limiter's
            // failure mode is "no limit", never "false refusal". Losing the
            // limit for the duration of an outage costs CPU; refusing on one
            // costs a referral.
            _logger.LogError(ex, "Rate-limit store unavailable; allowing the submission. AppUserId={AppUserId}", appUserId);
            return Unlimited(now);
        }
    }

    private RateLimitDecision Unlimited(DateTimeOffset now) =>
        new(true, _limit, int.MaxValue, RetryAfter(now));

    /// <summary>
    /// The window a moment falls in, as a sortable row key: the UTC hour.
    /// </summary>
    internal static string WindowKey(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyy-MM-ddTHH", CultureInfo.InvariantCulture);

    /// <summary>
    /// #558's cleanup horizon: rows older than the longest window anyone
    /// reads go. Defined FROM the read clamp so read and cleanup can never
    /// drift — a wider read window would widen retention with it.
    /// </summary>
    internal const int CleanupHorizonDays = Account.MaxUsageWindowDays;

    /// <summary>
    /// The cutoff <see cref="IAccountRateLimiter.DeleteWindowsBeforeAsync"/>
    /// takes: the horizon ago, as a window key.
    /// </summary>
    internal static string CleanupCutoffWindow(DateTimeOffset now) =>
        WindowKey(now.AddDays(-CleanupHorizonDays));

    public async Task<int> DeleteWindowsBeforeAsync(string appUserId, string cutoffWindow, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var deleted = 0;
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {appUserId} and RowKey lt {cutoffWindow}");
        await foreach (var entity in _table.QueryAsync<AccountRateLimitEntity>(
            filter, select: new[] { "PartitionKey", "RowKey" }, cancellationToken: cancellationToken))
        {
            try
            {
                await _table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: cancellationToken);
                deleted++;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // A concurrent run (or the CONFIGURATION.md recipe) got there first.
            }
        }

        return deleted;
    }

    /// <summary>
    /// What is left, and how long until it resets. Pure, because this is the
    /// part worth testing — the table plumbing around it cannot be, there
    /// being no Azurite in CI.
    /// </summary>
    internal static RateLimitDecision Decide(int used, int limit, DateTimeOffset now)
    {
        var remaining = Math.Max(0, limit - used);

        return new RateLimitDecision(remaining > 0, limit, remaining, RetryAfter(now));
    }

    /// <summary>
    /// To the top of the next hour. Never zero: a Retry-After of 0 invites an
    /// immediate retry that is certain to be refused again.
    /// </summary>
    internal static TimeSpan RetryAfter(DateTimeOffset now)
    {
        var utc = now.UtcDateTime;
        var nextWindow = new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);

        return nextWindow - utc is { TotalSeconds: > 1 } remaining ? remaining : TimeSpan.FromSeconds(1);
    }

    private async Task<AccountRateLimitEntity?> TryGetAsync(
        string appUserId,
        string windowKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.GetEntityAsync<AccountRateLimitEntity>(
                appUserId,
                windowKey,
                cancellationToken: cancellationToken);
            return response.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableEnsured)
        {
            return;
        }

        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            if (!_tableEnsured)
            {
                await _table.CreateIfNotExistsAsync(cancellationToken);
                _tableEnsured = true;
            }
        }
        finally
        {
            _ensureLock.Release();
        }
    }
}

/// <summary>
/// One account's spend in one window. The retention sweep drops rows older
/// than <see cref="TableAccountRateLimiter.CleanupHorizonDays"/> (#558 —
/// closing #266's "eventually rather than now" deferral).
/// </summary>
public sealed class AccountRateLimitEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public int Count { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
