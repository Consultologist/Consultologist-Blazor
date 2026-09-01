using System.Globalization;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Consultologist.Api.Models;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Jobs;

/// <summary>One account-day of usage: counts only, never text (#552).</summary>
public sealed record AccountUsageDay(
    string AppUserId,
    string Day,
    int ConsultsCompleted,
    int TokensIn,
    int TokensOut);

/// <summary>
/// #552: the derived usage store — the #545 record's class 4. A records-account
/// table (PK appUserId, RK yyyy-MM-dd UTC), written once per job at its
/// completion from the numbers #551 stamped on the record, NEVER re-derived
/// from job records (which the sweep purges), kept indefinitely (M6's 90-day
/// cleanup rule is #558's). AccountRateLimits is its older cousin and this
/// copies its increment: Azure Tables has no atomic add, so read-modify-write
/// under the ETag with a bounded retry on the create/update races.
/// </summary>
public interface IAccountUsageStore
{
    Task AddAsync(string appUserId, string day, int consultsCompleted, ConsultTokenUsage? tokens, CancellationToken cancellationToken);

    Task<IReadOnlyList<AccountUsageDay>> ListAsync(string appUserId, string fromDay, string toDay, CancellationToken cancellationToken);

    /// <summary>
    /// #553: the operator panel's read — every account's rows for the window.
    /// A cross-partition RowKey-range scan, the repo's first: the table has
    /// one partition per account, so the AppUsers argument restated — fine at
    /// current account counts, and the noted follow-up if that changes is a
    /// day-keyed index. Reads only the derived store, never job records.
    /// </summary>
    Task<IReadOnlyList<AccountUsageDay>> ListAllAsync(string fromDay, string toDay, CancellationToken cancellationToken);
}

public sealed class TableAccountUsageStore : IAccountUsageStore
{
    internal const string TableName = "AccountUsage";
    private const int MaxAttempts = 3;

    private readonly TableClient _table;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private bool _tableEnsured;

    public TableAccountUsageStore(IConfiguration configuration, TokenCredential credential)
    {
        // The records account — the store that survives every purge.
        _table = StorageTables.CreateClient(
            configuration, credential, TableName, "AccountUsageStorage", "AccountStorage");
    }

    /// <summary>
    /// The row key: the UTC day, invariant — two callers in different offsets
    /// land on the same row, the WindowKey rule one segment shorter.
    /// </summary>
    internal static string DayKey(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task AddAsync(string appUserId, string day, int consultsCompleted, ConsultTokenUsage? tokens, CancellationToken cancellationToken)
    {
        if (consultsCompleted == 0 && tokens == null)
        {
            // Nothing to add writes nothing — no empty rows.
            return;
        }

        await EnsureTableAsync(cancellationToken);

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entity = await TryGetAsync(appUserId, day, cancellationToken);

            try
            {
                if (entity == null)
                {
                    await _table.AddEntityAsync(new AccountUsageEntity
                    {
                        PartitionKey = appUserId,
                        RowKey = day,
                        ConsultsCompleted = consultsCompleted,
                        TokensIn = tokens?.Input ?? 0,
                        TokensOut = tokens?.Output ?? 0,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    }, cancellationToken);
                }
                else
                {
                    entity.ConsultsCompleted += consultsCompleted;
                    entity.TokensIn += tokens?.Input ?? 0;
                    entity.TokensOut += tokens?.Output ?? 0;
                    entity.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Merge, cancellationToken);
                }

                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < MaxAttempts)
            {
                // 409: the create race was lost; 412: the ETag went stale.
                // Re-read and add again — the increment is over fresh numbers.
            }
        }

        throw new InvalidOperationException($"Usage row for {day} could not be written after {MaxAttempts} attempts.");
    }

    public async Task<IReadOnlyList<AccountUsageDay>> ListAsync(string appUserId, string fromDay, string toDay, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var days = new List<AccountUsageDay>();
        var filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {appUserId} and RowKey ge {fromDay} and RowKey le {toDay}");
        await foreach (var entity in _table.QueryAsync<AccountUsageEntity>(filter, cancellationToken: cancellationToken))
        {
            days.Add(new AccountUsageDay(
                entity.PartitionKey, entity.RowKey, entity.ConsultsCompleted, entity.TokensIn, entity.TokensOut));
        }

        return days.OrderBy(day => day.Day, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<AccountUsageDay>> ListAllAsync(string fromDay, string toDay, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var days = new List<AccountUsageDay>();
        var filter = TableClient.CreateQueryFilter($"RowKey ge {fromDay} and RowKey le {toDay}");
        await foreach (var entity in _table.QueryAsync<AccountUsageEntity>(filter, cancellationToken: cancellationToken))
        {
            days.Add(new AccountUsageDay(
                entity.PartitionKey, entity.RowKey, entity.ConsultsCompleted, entity.TokensIn, entity.TokensOut));
        }

        return days
            .OrderBy(day => day.AppUserId, StringComparer.Ordinal)
            .ThenBy(day => day.Day, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<AccountUsageEntity?> TryGetAsync(string appUserId, string day, CancellationToken cancellationToken)
    {
        try
        {
            return await _table.GetEntityAsync<AccountUsageEntity>(appUserId, day, cancellationToken: cancellationToken);
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
            if (_tableEnsured)
            {
                return;
            }

            await _table.CreateIfNotExistsAsync(cancellationToken);
            _tableEnsured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }
}

internal sealed class AccountUsageEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public int ConsultsCompleted { get; set; }
    public int TokensIn { get; set; }
    public int TokensOut { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
