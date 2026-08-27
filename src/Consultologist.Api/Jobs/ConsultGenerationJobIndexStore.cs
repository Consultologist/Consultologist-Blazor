using System.Globalization;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Jobs;

public interface IConsultGenerationJobIndexStore
{
    Task UpsertAsync(ConsultGenerationJobIndexEntry entry, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ConsultGenerationJobIndexEntry> Jobs, string? ContinuationToken)> ListAsync(
        string appUserId,
        int limit,
        string? continuationToken,
        CancellationToken cancellationToken);

    /// <summary>#368: every terminal job of the account completed before the cutoff whose text is still present.</summary>
    Task<IReadOnlyList<ConsultGenerationJobIndexEntry>> ListDueForTextDropAsync(
        string appUserId,
        DateTimeOffset completedBefore,
        CancellationToken cancellationToken);
}

internal sealed class TableConsultGenerationJobIndexStore : IConsultGenerationJobIndexStore
{
    private const string IndexTableName = "ConsultGenerationJobIndex";

    private readonly TableClient _index;
    private readonly SemaphoreSlim _ensureTableLock = new(1, 1);
    private bool _tableEnsured;

    public TableConsultGenerationJobIndexStore(IConfiguration configuration, TokenCredential credential)
    {
        _index = StorageTables.CreateClient(
            configuration, credential, IndexTableName, "ConsultGenerationJobIndexStorage", "AccountStorage");
    }

    public async Task UpsertAsync(ConsultGenerationJobIndexEntry entry, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        await _index.UpsertEntityAsync(ToEntity(entry), TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<(IReadOnlyList<ConsultGenerationJobIndexEntry> Jobs, string? ContinuationToken)> ListAsync(
        string appUserId,
        int limit,
        string? continuationToken,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var decodedToken = DecodeToken(continuationToken);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appUserId}");
        var jobs = new List<ConsultGenerationJobIndexEntry>(limit);
        string? nextToken = null;

        await foreach (var page in _index
            .QueryAsync<ConsultGenerationJobIndexEntity>(filter, maxPerPage: limit, cancellationToken: cancellationToken)
            .AsPages(decodedToken))
        {
            foreach (var entity in page.Values)
            {
                jobs.Add(ToEntry(entity));
            }

            nextToken = EncodeToken(page.ContinuationToken);
            break;
        }

        return (jobs, nextToken);
    }

    public async Task<IReadOnlyList<ConsultGenerationJobIndexEntry>> ListDueForTextDropAsync(
        string appUserId,
        DateTimeOffset completedBefore,
        CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        // A partition scan filtered in memory: Table Storage cannot test a
        // column for absence, and an account's job count is small.
        var due = new List<ConsultGenerationJobIndexEntry>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appUserId} and CompletedAtUtc lt {completedBefore}");
        await foreach (var entity in _index.QueryAsync<ConsultGenerationJobIndexEntity>(filter, cancellationToken: cancellationToken))
        {
            var entry = ToEntry(entity);
            if (TextRetentionSweep.IsDue(entry, completedBefore))
            {
                due.Add(entry);
            }
        }

        return due;
    }

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableEnsured)
        {
            return;
        }

        await _ensureTableLock.WaitAsync(cancellationToken);

        try
        {
            if (_tableEnsured)
            {
                return;
            }

            await _index.CreateIfNotExistsAsync(cancellationToken);
            _tableEnsured = true;
        }
        finally
        {
            _ensureTableLock.Release();
        }
    }

    private static ConsultGenerationJobIndexEntity ToEntity(ConsultGenerationJobIndexEntry entry)
    {
        return new ConsultGenerationJobIndexEntity
        {
            PartitionKey = entry.AppUserId,
            RowKey = FormatRowKey(entry.CreatedAtUtc, entry.JobId),
            JobId = entry.JobId,
            Status = entry.Status,
            CreatedAtUtc = entry.CreatedAtUtc,
            StartedAtUtc = entry.StartedAtUtc,
            CompletedAtUtc = entry.CompletedAtUtc,
            TotalBlockCount = entry.TotalBlockCount,
            CompletedBlockCount = entry.CompletedBlockCount,
            FailedBlockCount = entry.FailedBlockCount,
            Source = entry.Source,
            ScheduledAtUtc = entry.ScheduledAtUtc,
            FailedAtStart = entry.FailedAtStart,
            TextDroppedAtUtc = entry.TextDroppedAtUtc,
            DeliveryOutcome = entry.DeliveryOutcome,
            DeliveredAtUtc = entry.DeliveredAtUtc,
            Deciding = entry.Deciding,
            DecisionFailureKind = entry.DecisionFailureKind
        };
    }

    private static ConsultGenerationJobIndexEntry ToEntry(ConsultGenerationJobIndexEntity entity)
    {
        return new ConsultGenerationJobIndexEntry(
            entity.JobId,
            entity.PartitionKey,
            entity.Status,
            entity.CreatedAtUtc,
            entity.StartedAtUtc,
            entity.CompletedAtUtc,
            entity.TotalBlockCount,
            entity.CompletedBlockCount,
            entity.FailedBlockCount,
            entity.Source,
            entity.ScheduledAtUtc,
            entity.FailedAtStart,
            entity.TextDroppedAtUtc,
            entity.DeliveryOutcome,
            entity.DeliveredAtUtc,
            entity.Deciding,
            entity.DecisionFailureKind);
    }

    private static string FormatRowKey(DateTimeOffset createdAtUtc, string jobId)
    {
        var reverseTimestamp = (DateTimeOffset.MaxValue.Ticks - createdAtUtc.UtcTicks)
            .ToString("D19", CultureInfo.InvariantCulture);
        return $"{reverseTimestamp}_{jobId}";
    }

    private static string? EncodeToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
    }

    private static string? DecodeToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(token));
        }
        catch
        {
            return null;
        }
    }
}

public sealed record ConsultGenerationJobIndexEntry(
    string JobId,
    string AppUserId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalBlockCount,
    int CompletedBlockCount,
    int FailedBlockCount,
    string? Source = null,
    DateTimeOffset? ScheduledAtUtc = null,
    // #434: created already Failed because no deliverable applied. Additive
    // column; rows written before it read false.
    bool FailedAtStart = false,
    // #368: when the retention sweep deleted the text. Additive column; null
    // before it, which is what the sweep selects on.
    DateTimeOffset? TextDroppedAtUtc = null,
    // #486: the completion email's outcome and time. Additive columns.
    string? DeliveryOutcome = null,
    DateTimeOffset? DeliveredAtUtc = null,
    // v10 (#496): still deciding what to produce; and why a job ended in that
    // stage. Additive columns.
    bool Deciding = false,
    string? DecisionFailureKind = null);

internal sealed class ConsultGenerationJobIndexEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int TotalBlockCount { get; set; }
    public int CompletedBlockCount { get; set; }
    public int FailedBlockCount { get; set; }
    public string? Source { get; set; }
    public DateTimeOffset? ScheduledAtUtc { get; set; }
    public bool FailedAtStart { get; set; }
    public DateTimeOffset? TextDroppedAtUtc { get; set; }
    public string? DeliveryOutcome { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public bool Deciding { get; set; }
    public string? DecisionFailureKind { get; set; }
}
