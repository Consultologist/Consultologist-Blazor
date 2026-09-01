using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Consultologist.Api.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Forms;

/// <summary>
/// #539 (storage-separation.md § 2.1): one JSON blob per held form response —
/// the values as the strings the flow sent, never validated against
/// declarations (an "Other" answer is free text) and never logged. Inputs
/// before any job: the same container family and the same account clock as
/// the held job inputs.
/// </summary>
public sealed record FormResponsePayload(
    int Version,
    string FormId,
    string ResponseId,
    DateTimeOffset SubmittedAtUtc,
    IReadOnlyDictionary<string, string> Inputs)
{
    public const int CurrentVersion = 1;
}

/// <summary>Where a response's values rest — container + name, never a URL.</summary>
public sealed record FormResponseBlobPointer(string Container, string Name);

public interface IFormResponseBlobStore
{
    Task<FormResponseBlobPointer> WriteAsync(string? accountKind, string appUserId, FormResponsePayload payload, CancellationToken cancellationToken);

    /// <summary>
    /// #540: the held values behind a pointer, or null when the blob is
    /// gone (the sweep or a discard between the row read and this one).
    /// </summary>
    Task<FormResponsePayload?> ReadAsync(FormResponseBlobPointer pointer, CancellationToken cancellationToken);

    Task DeleteAsync(FormResponseBlobPointer pointer, CancellationToken cancellationToken);

    /// <summary>#559: every blob under {appUserId}/ in BOTH kind containers — kind-blind, safer than trusting the stamp.</summary>
    Task<int> DeleteAccountAsync(string appUserId, CancellationToken cancellationToken);
}

public sealed class FormResponseBlobStore : IFormResponseBlobStore
{
    private const string OrganisationContainer = "org-form-responses";
    private const string PersonalContainer = "personal-form-responses";

    private readonly TextBlobClientFactory _blobs;

    public FormResponseBlobStore(IConfiguration configuration, TokenCredential credential, ILogger<FormResponseBlobStore> logger)
    {
        _blobs = new TextBlobClientFactory(configuration, credential, logger, "Form responses store");
    }

    internal static string ContainerFor(string? accountKind) =>
        TextBlobNaming.ContainerFor(accountKind, OrganisationContainer, PersonalContainer);

    internal static string NameFor(string appUserId, string formId, string responseId) =>
        $"{appUserId}/{formId}-{responseId}.json";

    public async Task<FormResponseBlobPointer> WriteAsync(string? accountKind, string appUserId, FormResponsePayload payload, CancellationToken cancellationToken)
    {
        var pointer = new FormResponseBlobPointer(ContainerFor(accountKind), NameFor(appUserId, payload.FormId, payload.ResponseId));
        await _blobs.WriteJsonAsync(pointer.Container, pointer.Name, payload, cancellationToken);
        return pointer;
    }

    public Task<FormResponsePayload?> ReadAsync(FormResponseBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.ReadJsonAsync<FormResponsePayload>(pointer.Container, pointer.Name, cancellationToken);

    public Task DeleteAsync(FormResponseBlobPointer pointer, CancellationToken cancellationToken) =>
        _blobs.DeleteAsync(pointer.Container, pointer.Name, cancellationToken);

    public async Task<int> DeleteAccountAsync(string appUserId, CancellationToken cancellationToken) =>
        await _blobs.DeleteByPrefixAsync(OrganisationContainer, $"{appUserId}/", cancellationToken)
        + await _blobs.DeleteByPrefixAsync(PersonalContainer, $"{appUserId}/", cancellationToken);
}

/// <summary>
/// One held response as the list knows it — ids, times, the input ids
/// present, the blob pointer, and the deleted stamp. Never a value.
/// </summary>
public sealed record FormResponseRow(
    string AppUserId,
    string FormId,
    string ResponseId,
    DateTimeOffset SubmittedAtUtc,
    IReadOnlyList<string> InputIds,
    string BlobContainer,
    string BlobName,
    DateTimeOffset? DeletedAtUtc);

/// <summary>
/// #539: the FormResponses table — records account (storage-separation.md
/// § 2.1: the row never holds a value; it stays after the sweep so the list
/// can say deleted). PK appUserId, RK formId:responseId.
/// </summary>
public interface IFormResponseStore
{
    Task UpsertAsync(FormResponseRow row, CancellationToken cancellationToken);

    Task<FormResponseRow?> TryGetAsync(string appUserId, string formId, string responseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FormResponseRow>> ListAsync(string appUserId, CancellationToken cancellationToken);

    /// <summary>Rows submitted before the cutoff whose values still rest — the sweep's list.</summary>
    Task<IReadOnlyList<FormResponseRow>> ListDueAsync(string appUserId, DateTimeOffset submittedBefore, CancellationToken cancellationToken);

    /// <summary>The stamp, after the blob is gone — Merge, so no other column is clobbered.</summary>
    Task MarkDeletedAsync(string appUserId, string formId, string responseId, DateTimeOffset deletedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// #559: the hard delete — the account's whole partition, tombstones and
    /// all (MarkDeleted only stamps; closure removes the list itself).
    /// Returns how many rows went.
    /// </summary>
    Task<int> DeleteAllAsync(string appUserId, CancellationToken cancellationToken);
}

public sealed class TableFormResponseStore : IFormResponseStore
{
    private const string ResponsesTableName = "FormResponses";

    private readonly TableClient _responses;
    private readonly SemaphoreSlim _ensureTableLock = new(1, 1);
    private bool _tableEnsured;

    public TableFormResponseStore(IConfiguration configuration, TokenCredential credential)
    {
        // The records account: the list survives the sweep; only the values go.
        _responses = StorageTables.CreateClient(
            configuration, credential, ResponsesTableName, "FormResponsesStorage", "AccountStorage");
    }

    internal static string RowKeyFor(string formId, string responseId) => $"{formId}:{responseId}";

    public async Task UpsertAsync(FormResponseRow row, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        await _responses.UpsertEntityAsync(ToEntity(row), TableUpdateMode.Replace, cancellationToken);
    }

    public async Task<FormResponseRow?> TryGetAsync(string appUserId, string formId, string responseId, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        try
        {
            var entity = await _responses.GetEntityAsync<FormResponseEntity>(
                appUserId, RowKeyFor(formId, responseId), cancellationToken: cancellationToken);
            return ToRow(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<FormResponseRow>> ListAsync(string appUserId, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var rows = new List<FormResponseRow>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appUserId}");
        await foreach (var entity in _responses.QueryAsync<FormResponseEntity>(filter, cancellationToken: cancellationToken))
        {
            rows.Add(ToRow(entity));
        }

        return rows
            .OrderByDescending(row => row.SubmittedAtUtc)
            .ThenBy(row => row.FormId, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyList<FormResponseRow>> ListDueAsync(string appUserId, DateTimeOffset submittedBefore, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        // The non-key time filter, as the job index does for CompletedAtUtc;
        // the absence test (DeletedAtUtc unset) is the sweep predicate's,
        // client-side — Table Storage cannot test a column for absence.
        var due = new List<FormResponseRow>();
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appUserId} and SubmittedAtUtc lt {submittedBefore}");
        await foreach (var entity in _responses.QueryAsync<FormResponseEntity>(filter, cancellationToken: cancellationToken))
        {
            var row = ToRow(entity);
            if (TextRetentionSweep.IsResponseDue(row, submittedBefore))
            {
                due.Add(row);
            }
        }

        return due;
    }

    public async Task<int> DeleteAllAsync(string appUserId, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        var deleted = 0;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {appUserId}");
        var batch = new List<TableTransactionAction>();
        await foreach (var entity in _responses.QueryAsync<TableEntity>(filter, select: new[] { "PartitionKey", "RowKey" }, cancellationToken: cancellationToken))
        {
            batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity, ETag.All));
            if (batch.Count == 100)
            {
                await _responses.SubmitTransactionAsync(batch, cancellationToken);
                deleted += batch.Count;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await _responses.SubmitTransactionAsync(batch, cancellationToken);
            deleted += batch.Count;
        }

        return deleted;
    }

    public async Task MarkDeletedAsync(string appUserId, string formId, string responseId, DateTimeOffset deletedAtUtc, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        await _responses.UpdateEntityAsync(
            new TableEntity(appUserId, RowKeyFor(formId, responseId)) { ["DeletedAtUtc"] = deletedAtUtc },
            ETag.All,
            TableUpdateMode.Merge,
            cancellationToken);
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

            await _responses.CreateIfNotExistsAsync(cancellationToken);
            _tableEnsured = true;
        }
        finally
        {
            _ensureTableLock.Release();
        }
    }

    private static FormResponseEntity ToEntity(FormResponseRow row) => new()
    {
        PartitionKey = row.AppUserId,
        RowKey = RowKeyFor(row.FormId, row.ResponseId),
        FormId = row.FormId,
        ResponseId = row.ResponseId,
        SubmittedAtUtc = row.SubmittedAtUtc,
        InputIds = string.Join(",", row.InputIds),
        BlobContainer = row.BlobContainer,
        BlobName = row.BlobName,
        DeletedAtUtc = row.DeletedAtUtc
    };

    private static FormResponseRow ToRow(FormResponseEntity entity) => new(
        entity.PartitionKey,
        entity.FormId,
        entity.ResponseId,
        entity.SubmittedAtUtc,
        string.IsNullOrEmpty(entity.InputIds) ? Array.Empty<string>() : entity.InputIds.Split(','),
        entity.BlobContainer,
        entity.BlobName,
        entity.DeletedAtUtc);
}

/// <summary>
/// #539: the sweep's drop for one held response. No entity owns this state,
/// so the purger reaches the stores directly (the JobTextPurger precedent
/// for its table legs): blob first, then the Merge stamp — a failure
/// persists nothing and the next sweep re-lists the row.
/// </summary>
public interface IFormResponsePurger
{
    Task DropAsync(FormResponseRow row, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class FormResponsePurger : IFormResponsePurger
{
    private readonly IFormResponseBlobStore _blobs;
    private readonly IFormResponseStore _rows;

    public FormResponsePurger(IFormResponseBlobStore blobs, IFormResponseStore rows)
    {
        _blobs = blobs;
        _rows = rows;
    }

    public async Task DropAsync(FormResponseRow row, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _blobs.DeleteAsync(new FormResponseBlobPointer(row.BlobContainer, row.BlobName), cancellationToken);
        await _rows.MarkDeletedAsync(row.AppUserId, row.FormId, row.ResponseId, now, cancellationToken);
    }
}

internal sealed class FormResponseEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string FormId { get; set; } = string.Empty;
    public string ResponseId { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public string InputIds { get; set; } = string.Empty;
    public string BlobContainer { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public DateTimeOffset? DeletedAtUtc { get; set; }
}
