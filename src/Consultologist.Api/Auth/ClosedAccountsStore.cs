using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Auth;

/// <summary>
/// #559: the audit of a closure — appUserId, when, which operator, and the
/// counts of what was removed. No names, no text; the row is also the
/// idempotence claim: a second Close finds it and deletes nothing.
/// </summary>
public sealed record ClosedAccountRecord(
    string AppUserId,
    DateTimeOffset ClosedAtUtc,
    string OperatorAppUserId,
    int Jobs,
    int Blobs,
    int Packages,
    int Links,
    int Responses,
    int Settings,
    int Identities);

public interface IClosedAccountsStore
{
    Task<ClosedAccountRecord?> TryGetAsync(string appUserId, CancellationToken cancellationToken);

    Task RecordAsync(ClosedAccountRecord record, CancellationToken cancellationToken);
}

internal sealed class TableClosedAccountsStore : IClosedAccountsStore
{
    private const string ClosedAccountsTableName = "ClosedAccounts";

    private readonly TableClient _closed;
    private readonly SemaphoreSlim _ensureTableLock = new(1, 1);
    private bool _tableEnsured;

    public TableClosedAccountsStore(IConfiguration configuration, TokenCredential credential)
    {
        // The records account: the audit outlives everything it counts.
        _closed = StorageTables.CreateClient(
            configuration, credential, ClosedAccountsTableName, "ClosedAccountsStorage", "AccountStorage");
    }

    public async Task<ClosedAccountRecord?> TryGetAsync(string appUserId, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);

        try
        {
            var entity = await _closed.GetEntityAsync<ClosedAccountEntity>(
                "closed-account", appUserId, cancellationToken: cancellationToken);
            return ToRecord(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task RecordAsync(ClosedAccountRecord record, CancellationToken cancellationToken)
    {
        await EnsureTableAsync(cancellationToken);
        await _closed.UpsertEntityAsync(ToEntity(record), TableUpdateMode.Replace, cancellationToken);
    }

    private static ClosedAccountEntity ToEntity(ClosedAccountRecord record) => new()
    {
        PartitionKey = "closed-account",
        RowKey = record.AppUserId,
        ClosedAtUtc = record.ClosedAtUtc,
        OperatorAppUserId = record.OperatorAppUserId,
        Jobs = record.Jobs,
        Blobs = record.Blobs,
        Packages = record.Packages,
        Links = record.Links,
        Responses = record.Responses,
        Settings = record.Settings,
        Identities = record.Identities
    };

    private static ClosedAccountRecord ToRecord(ClosedAccountEntity entity) => new(
        entity.RowKey, entity.ClosedAtUtc, entity.OperatorAppUserId,
        entity.Jobs, entity.Blobs, entity.Packages, entity.Links,
        entity.Responses, entity.Settings, entity.Identities);

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableEnsured)
        {
            return;
        }

        await _ensureTableLock.WaitAsync(cancellationToken);
        try
        {
            if (!_tableEnsured)
            {
                await _closed.CreateIfNotExistsAsync(cancellationToken);
                _tableEnsured = true;
            }
        }
        finally
        {
            _ensureTableLock.Release();
        }
    }
}

internal sealed class ClosedAccountEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public DateTimeOffset ClosedAtUtc { get; set; }
    public string OperatorAppUserId { get; set; } = string.Empty;
    public int Jobs { get; set; }
    public int Blobs { get; set; }
    public int Packages { get; set; }
    public int Links { get; set; }
    public int Responses { get; set; }
    public int Settings { get; set; }
    public int Identities { get; set; }
}
