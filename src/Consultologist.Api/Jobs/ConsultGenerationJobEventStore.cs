using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

public interface IConsultGenerationJobEventStore
{
    Task<IReadOnlyList<ConsultGenerationJobStoredEvent>> AppendAsync(
        string jobId,
        string appUserId,
        IReadOnlyList<ConsultGenerationJobEventCandidate> candidates,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ConsultGenerationJobStoredEvent>> ReadAfterAsync(
        string jobId,
        string appUserId,
        long sequence,
        CancellationToken cancellationToken);

    /// <summary>#368: every row of the job's partition — the streamed snapshots carry the produced text.</summary>
    Task<int> DeleteJobAsync(string jobId, CancellationToken cancellationToken);
}

internal sealed class TableConsultGenerationJobEventStore : IConsultGenerationJobEventStore
{
    private const string EventsTableName = "ConsultGenerationJobEvents";
    private const string SequenceRowKey = "!sequence";
    private const string EventKeyRowPrefix = "!event-key:";
    private const int MaxAppendAttempts = 10;

    private readonly TableClient _events;
    private readonly ILogger<TableConsultGenerationJobEventStore> _logger;
    private readonly SemaphoreSlim _ensureTableLock = new(1, 1);
    private bool _tableEnsured;

    public TableConsultGenerationJobEventStore(
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<TableConsultGenerationJobEventStore> logger)
    {
        _logger = logger;
        _events = StorageTables.CreateClient(
            configuration, credential, EventsTableName, "ConsultGenerationJobEventStorage", "AccountStorage");
    }

    public async Task<int> DeleteJobAsync(string jobId, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var deleted = 0;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {jobId}");
        var batch = new List<TableTransactionAction>();
        await foreach (var entity in _events.QueryAsync<TableEntity>(filter, select: new[] { "PartitionKey", "RowKey" }, cancellationToken: cancellationToken))
        {
            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity, ETag.All));
            if (batch.Count == 100)
            {
                await _events.SubmitTransactionAsync(batch, cancellationToken);
                deleted += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _events.SubmitTransactionAsync(batch, cancellationToken);
            deleted += batch.Count;
        }

        return deleted;
    }

    public async Task<IReadOnlyList<ConsultGenerationJobStoredEvent>> AppendAsync(
        string jobId,
        string appUserId,
        IReadOnlyList<ConsultGenerationJobEventCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return Array.Empty<ConsultGenerationJobStoredEvent>();
        }

        await EnsureTableAsync(cancellationToken);

        var storedEvents = new List<ConsultGenerationJobStoredEvent>(candidates.Count);

        foreach (var candidate in candidates)
        {
            storedEvents.Add(await AppendOneAsync(jobId, appUserId, candidate, cancellationToken));
        }

        return storedEvents
            .OrderBy(storedEvent => storedEvent.Sequence)
            .ToArray();
    }

    public async Task<IReadOnlyList<ConsultGenerationJobStoredEvent>> ReadAfterAsync(
        string jobId,
        string appUserId,
        long sequence,
        CancellationToken cancellationToken)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "Sequence must be greater than or equal to zero.");
        }

        await EnsureTableAsync(cancellationToken);
        await ValidatePartitionOwnerAsync(jobId, appUserId, cancellationToken);

        var minRowKey = FormatSequence(sequence);
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {jobId} and RowKey gt {minRowKey}");
        var storedEvents = new List<ConsultGenerationJobStoredEvent>();

        await foreach (var entity in _events.QueryAsync<ConsultGenerationJobEventEntity>(
            filter,
            cancellationToken: cancellationToken))
        {
            if (!string.Equals(entity.AppUserId, appUserId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Consult generation SSE event app user mismatch. JobId={jobId}, Sequence={entity.Sequence}");
            }

            storedEvents.Add(ToStoredEvent(entity));
        }

        return storedEvents
            .OrderBy(storedEvent => storedEvent.Sequence)
            .ToArray();
    }

    private async Task<ConsultGenerationJobStoredEvent> AppendOneAsync(
        string jobId,
        string appUserId,
        ConsultGenerationJobEventCandidate candidate,
        CancellationToken cancellationToken)
    {
        var existingEvent = await TryGetExistingEventAsync(jobId, appUserId, candidate.EventKey, cancellationToken);

        if (existingEvent != null)
        {
            return existingEvent;
        }

        for (var attempt = 1; attempt <= MaxAppendAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var counter = await TryGetEntityAsync<ConsultGenerationJobEventSequenceEntity>(
                jobId,
                SequenceRowKey,
                cancellationToken);

            var sequence = counter?.NextSequence > 0
                ? counter.NextSequence
                : 1;
            var nextSequence = sequence + 1;
            var eventRowKey = FormatSequence(sequence);
            var now = DateTimeOffset.UtcNow;

            var eventEntity = new ConsultGenerationJobEventEntity
            {
                PartitionKey = jobId,
                RowKey = eventRowKey,
                AppUserId = appUserId,
                Sequence = sequence,
                EventType = candidate.EventType,
                EventKey = candidate.EventKey,
                PayloadJson = candidate.PayloadJson,
                CreatedAtUtc = now
            };

            var eventKeyEntity = new ConsultGenerationJobEventKeyEntity
            {
                PartitionKey = jobId,
                RowKey = CreateEventKeyRowKey(candidate.EventKey),
                AppUserId = appUserId,
                EventKey = candidate.EventKey,
                EventRowKey = eventRowKey,
                Sequence = sequence,
                CreatedAtUtc = now
            };

            var actions = new List<TableTransactionAction>(3);

            if (counter == null)
            {
                actions.Add(new TableTransactionAction(
                    TableTransactionActionType.Add,
                    new ConsultGenerationJobEventSequenceEntity
                    {
                        PartitionKey = jobId,
                        RowKey = SequenceRowKey,
                        AppUserId = appUserId,
                        NextSequence = nextSequence,
                        UpdatedAtUtc = now
                    }));
            }
            else
            {
                counter.AppUserId = string.IsNullOrWhiteSpace(counter.AppUserId)
                    ? appUserId
                    : counter.AppUserId;
                counter.NextSequence = nextSequence;
                counter.UpdatedAtUtc = now;
                actions.Add(new TableTransactionAction(
                    TableTransactionActionType.UpdateMerge,
                    counter,
                    counter.ETag));
            }

            actions.Add(new TableTransactionAction(TableTransactionActionType.Add, eventEntity));
            actions.Add(new TableTransactionAction(TableTransactionActionType.Add, eventKeyEntity));

            try
            {
                await _events.SubmitTransactionAsync(actions, cancellationToken);

                _logger.LogDebug(
                    "Consult generation SSE event persisted. JobId={JobId}, EventType={EventType}, EventKey={EventKey}, Sequence={Sequence}",
                    jobId,
                    candidate.EventType,
                    candidate.EventKey,
                    sequence);

                return ToStoredEvent(eventEntity);
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                var racedEvent = await TryGetExistingEventAsync(jobId, appUserId, candidate.EventKey, cancellationToken);

                if (racedEvent != null)
                {
                    return racedEvent;
                }

                _logger.LogDebug(
                    ex,
                    "Retrying consult generation SSE event append after storage concurrency response. JobId={JobId}, EventKey={EventKey}, Attempt={Attempt}",
                    jobId,
                    candidate.EventKey,
                    attempt);
            }
        }

        throw new InvalidOperationException($"Could not persist consult generation SSE event after {MaxAppendAttempts} attempts.");
    }

    private async Task<ConsultGenerationJobStoredEvent?> TryGetExistingEventAsync(
        string jobId,
        string appUserId,
        string eventKey,
        CancellationToken cancellationToken)
    {
        var keyEntity = await TryGetEntityAsync<ConsultGenerationJobEventKeyEntity>(
            jobId,
            CreateEventKeyRowKey(eventKey),
            cancellationToken);

        if (keyEntity == null)
        {
            return null;
        }

        var eventEntity = await TryGetEntityAsync<ConsultGenerationJobEventEntity>(
            jobId,
            keyEntity.EventRowKey,
            cancellationToken);

        if (eventEntity == null)
        {
            throw new InvalidOperationException($"Consult generation SSE event key exists without event row. JobId={jobId}, EventKey={eventKey}");
        }

        if (!string.Equals(eventEntity.AppUserId, appUserId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Consult generation SSE event app user mismatch. JobId={jobId}, EventKey={eventKey}");
        }

        return ToStoredEvent(eventEntity);
    }

    private async Task ValidatePartitionOwnerAsync(
        string jobId,
        string appUserId,
        CancellationToken cancellationToken)
    {
        var counter = await TryGetEntityAsync<ConsultGenerationJobEventSequenceEntity>(
            jobId,
            SequenceRowKey,
            cancellationToken);

        if (counter == null || string.IsNullOrWhiteSpace(counter.AppUserId))
        {
            return;
        }

        if (!string.Equals(counter.AppUserId, appUserId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Consult generation SSE event app user mismatch. JobId={jobId}");
        }
    }

    private async Task<T?> TryGetEntityAsync<T>(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken)
        where T : class, ITableEntity, new()
    {
        try
        {
            var response = await _events.GetEntityAsync<T>(
                partitionKey,
                rowKey,
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

        await _ensureTableLock.WaitAsync(cancellationToken);

        try
        {
            if (_tableEnsured)
            {
                return;
            }

            await _events.CreateIfNotExistsAsync(cancellationToken);
            _tableEnsured = true;
        }
        finally
        {
            _ensureTableLock.Release();
        }
    }

    private static ConsultGenerationJobStoredEvent ToStoredEvent(ConsultGenerationJobEventEntity entity)
    {
        return new ConsultGenerationJobStoredEvent(
            entity.PartitionKey,
            entity.AppUserId,
            entity.Sequence,
            entity.EventType,
            entity.EventKey,
            entity.PayloadJson,
            entity.CreatedAtUtc);
    }

    private static string FormatSequence(long sequence)
    {
        return sequence.ToString("D12", CultureInfo.InvariantCulture);
    }

    private static string CreateEventKeyRowKey(string eventKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(eventKey));
        return EventKeyRowPrefix + Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record ConsultGenerationJobEventCandidate(
    string EventType,
    string EventKey,
    string PayloadJson);

public sealed record ConsultGenerationJobStoredEvent(
    string JobId,
    string AppUserId,
    long Sequence,
    string EventType,
    string EventKey,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc)
{
    public string SseId => $"{JobId}:{Sequence.ToString("D12", CultureInfo.InvariantCulture)}";
}

internal sealed class ConsultGenerationJobEventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string AppUserId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class ConsultGenerationJobEventKeyEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string AppUserId { get; set; } = string.Empty;
    public string EventKey { get; set; } = string.Empty;
    public string EventRowKey { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

internal sealed class ConsultGenerationJobEventSequenceEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string AppUserId { get; set; } = string.Empty;
    public long NextSequence { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
