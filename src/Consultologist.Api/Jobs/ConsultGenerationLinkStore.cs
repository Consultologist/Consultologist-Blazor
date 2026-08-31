using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Consultologist.Api.Models;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Jobs;

/// <summary>
/// #546: one edge of the lineage graph, ids only, never text. The consumer's
/// own origins name the source (the "copied from" side); this row is the
/// inversion — keyed by the source so its History can say "used by". Kind is
/// the origin vocabulary: previous-run edges carry the slot and deliverable
/// (one row per copied element — one consumer may copy twice from one
/// source), a rerun edge is one row for the whole replay.
/// </summary>
public sealed record ConsultGenerationLink(
    string SourceJobId,
    string ConsumerJobId,
    string Kind,
    string? InputId,
    string? ResultId,
    string AppUserId,
    DateTimeOffset CreatedAtUtc,
    int ElementIndex = 0);

/// <summary>
/// #546: the links index — a records-account table (history/record class,
/// storage-separation.md § 2): PK the source job id, RK the consumer side,
/// never deleted while the account exists (account closure, #559, is the one
/// deleter). Written best-effort at start; the record's origins remain the
/// truth and this is their inverted projection.
/// </summary>
public interface IConsultGenerationLinkStore
{
    Task WriteAsync(IReadOnlyList<ConsultGenerationLink> links, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConsultGenerationLink>> ListConsumersAsync(string sourceJobId, CancellationToken cancellationToken);
}

internal sealed class TableConsultGenerationLinkStore : IConsultGenerationLinkStore
{
    private const string LinksTableName = "ConsultGenerationLinks";

    private readonly TableClient _links;
    private readonly SemaphoreSlim _ensureTableLock = new(1, 1);
    private bool _tableEnsured;

    public TableConsultGenerationLinkStore(IConfiguration configuration, TokenCredential credential)
    {
        // The records account, as the index table: links are record, not text.
        _links = StorageTables.CreateClient(
            configuration, credential, LinksTableName, "ConsultGenerationLinksStorage", "AccountStorage");
    }

    /// <summary>
    /// The consumer-side key. The #545 record says consumerJobId; the
    /// refinement carries the slot and element so one consumer copying twice
    /// from one source keeps both rows. A rerun edge is the bare consumer id
    /// — one row for the whole replay. Upserts are Replace, so a replayed
    /// start writes the same rows again harmlessly.
    /// </summary>
    internal static string RowKeyFor(ConsultGenerationLink link) =>
        link.Kind == ConsultInputOriginKinds.Rerun
            ? link.ConsumerJobId
            : $"{link.ConsumerJobId}_{link.InputId}_{link.ElementIndex}";

    public async Task WriteAsync(IReadOnlyList<ConsultGenerationLink> links, CancellationToken cancellationToken)
    {
        if (links.Count == 0)
        {
            return;
        }

        await EnsureTableAsync(cancellationToken);

        foreach (var link in links)
        {
            await _links.UpsertEntityAsync(ToEntity(link), TableUpdateMode.Replace, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ConsultGenerationLink>> ListConsumersAsync(string sourceJobId, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var consumers = new List<ConsultGenerationLink>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {sourceJobId}");
        await foreach (var entity in _links.QueryAsync<ConsultGenerationLinkEntity>(filter, cancellationToken: cancellationToken))
        {
            consumers.Add(ToLink(entity));
        }

        return consumers
            .OrderBy(link => link.CreatedAtUtc)
            .ThenBy(link => link.ConsumerJobId, StringComparer.Ordinal)
            .ToList();
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

            await _links.CreateIfNotExistsAsync(cancellationToken);
            _tableEnsured = true;
        }
        finally
        {
            _ensureTableLock.Release();
        }
    }

    private static ConsultGenerationLinkEntity ToEntity(ConsultGenerationLink link) => new()
    {
        PartitionKey = link.SourceJobId,
        RowKey = RowKeyFor(link),
        ConsumerJobId = link.ConsumerJobId,
        Kind = link.Kind,
        InputId = link.InputId,
        ResultId = link.ResultId,
        AppUserId = link.AppUserId,
        CreatedAtUtc = link.CreatedAtUtc,
        ElementIndex = link.ElementIndex
    };

    private static ConsultGenerationLink ToLink(ConsultGenerationLinkEntity entity) => new(
        entity.PartitionKey,
        entity.ConsumerJobId,
        entity.Kind,
        entity.InputId,
        entity.ResultId,
        entity.AppUserId,
        entity.CreatedAtUtc,
        entity.ElementIndex);
}

internal sealed class ConsultGenerationLinkEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string ConsumerJobId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? InputId { get; set; }
    public string? ResultId { get; set; }
    public string AppUserId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public int ElementIndex { get; set; }
}
