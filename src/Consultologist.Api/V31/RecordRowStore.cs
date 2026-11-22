using System.Globalization;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>#1506 (D11): what one row's write came to. A write never throws on <see cref="Conflict"/>.</summary>
public enum RecordWrite
{
    /// <summary>The row was stored by this write.</summary>
    Written,

    /// <summary>A row with this key and the same hash was stored already; nothing changed.</summary>
    AlreadyThere,

    /// <summary>A row with this key and another hash is stored; it was left as it is.</summary>
    Conflict,
}

/// <summary>#1506 (D10): a 31 job's record rows, each written once, in a table.</summary>
public interface IRecordRowStore
{
    /// <summary>Inserts each row alone; one result per row, in the order given.</summary>
    /// <exception cref="InvalidOperationException">A row is too large to store (an engine fault); nothing is written.</exception>
    Task<IReadOnlyList<(string Key, RecordWrite Result)>> WriteAsync(string jobId, IReadOnlyList<RecordRow> rows, CancellationToken ct);

    Task<RecordRow?> ReadAsync(string jobId, string key, CancellationToken ct);

    /// <summary>Every row of the job, in key order (ordinal).</summary>
    Task<IReadOnlyList<RecordRow>> ListAsync(string jobId, CancellationToken ct);

    Task DeleteJobAsync(string jobId, CancellationToken ct);
}

/// <summary>The few table calls <see cref="TableRecordRowStore"/> makes; the tests drive the store over an in-memory one.</summary>
internal interface IRecordTable
{
    Task EnsureAsync(CancellationToken ct);

    /// <summary>False when a row with the partition and row key is there already (409).</summary>
    Task<bool> TryAddAsync(TableEntity entity, CancellationToken ct);

    /// <summary>Null when the row, or the whole table, is not there.</summary>
    Task<TableEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct);

    /// <summary>Empty when the table is not there.</summary>
    Task<IReadOnlyList<TableEntity>> ListAsync(string partitionKey, CancellationToken ct);

    Task DeleteAllAsync(string partitionKey, CancellationToken ct);
}

internal sealed class AzureRecordTable(TableClient client) : IRecordTable
{
    public Task EnsureAsync(CancellationToken ct) => client.CreateIfNotExistsAsync(ct);

    public async Task<bool> TryAddAsync(TableEntity entity, CancellationToken ct)
    {
        try
        {
            await client.AddEntityAsync(entity, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }

    public async Task<TableEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct)
    {
        try
        {
            var response = await client.GetEntityIfExistsAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
            return response.HasValue ? response.Value : null;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TableEntity>> ListAsync(string partitionKey, CancellationToken ct)
    {
        var entities = new List<TableEntity>();
        try
        {
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
            await foreach (var entity in client.QueryAsync<TableEntity>(filter, cancellationToken: ct))
                entities.Add(entity);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }

        return entities;
    }

    public async Task DeleteAllAsync(string partitionKey, CancellationToken ct)
    {
        try
        {
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
            var batch = new List<TableTransactionAction>();
            await foreach (var entity in client.QueryAsync<TableEntity>(filter, select: ["PartitionKey", "RowKey"], cancellationToken: ct))
            {
                batch.Add(new TableTransactionAction(TableTransactionActionType.Delete, entity, ETag.All));
                if (batch.Count == 100)
                {
                    await client.SubmitTransactionAsync(batch, ct);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
                await client.SubmitTransactionAsync(batch, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
    }
}

/// <summary>
/// #1506 (D10, D11; part 08 § 1.1): the record's rows in table <c>ConsultGenerationRecordRows</c>: partition key the job id,
/// row key <see cref="RecordKeys.Escape"/> of the key, properties <c>key</c>, <c>section</c>, <c>row</c> (continued in
/// <c>row1</c>…) and <c>hash</c> (of the row's JSON). Constructing it touches no storage; the table is created on a first write.
/// </summary>
public sealed class TableRecordRowStore : IRecordRowStore
{
    internal const string TableName = "ConsultGenerationRecordRows";

    /// <summary>One property holds at most 32K characters; a row's text is cut at this many.</summary>
    internal const int ChunkLength = 30_000;

    /// <summary>An entity is at most 1 MiB; what the store counts is the properties' names and values, as Azure does, in UTF-16.</summary>
    internal const int MaxEntityBytes = 1024 * 1024;

    private const int EntityOverheadBytes = 4096;

    private readonly IRecordTable _table;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);
    private bool _ensured;

    public TableRecordRowStore(IConfiguration configuration, TokenCredential credential, ILogger<TableRecordRowStore> logger)
        : this(new AzureRecordTable(StorageTables.CreateClient(
            configuration, credential, TableName, "ConsultGenerationRecordStorage", "AccountStorage")), logger)
    {
    }

    internal TableRecordRowStore(IRecordTable table, ILogger logger)
    {
        _table = table;
        _logger = logger;
    }

    public async Task<IReadOnlyList<(string Key, RecordWrite Result)>> WriteAsync(string jobId, IReadOnlyList<RecordRow> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return [];

        var entities = rows.Select(row => ToEntity(jobId, row)).ToList();
        await EnsureTableAsync(ct);

        var results = new List<(string, RecordWrite)>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
            results.Add((rows[i].Key, await WriteOneAsync(jobId, rows[i], entities[i], ct)));

        return results;
    }

    public async Task<RecordRow?> ReadAsync(string jobId, string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        ArgumentNullException.ThrowIfNull(key);
        var entity = await _table.GetAsync(jobId, RecordKeys.Escape(key), ct);
        return entity is null ? null : FromEntity(entity);
    }

    public async Task<IReadOnlyList<RecordRow>> ListAsync(string jobId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return (await _table.ListAsync(jobId, ct))
            .Select(FromEntity)
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToList();
    }

    public Task DeleteJobAsync(string jobId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        return _table.DeleteAllAsync(jobId, ct);
    }

    private async Task<RecordWrite> WriteOneAsync(string jobId, RecordRow row, TableEntity entity, CancellationToken ct)
    {
        var hash = CanonicalJson.HashText(row.Json);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (await _table.TryAddAsync(entity, ct))
                return RecordWrite.Written;

            var stored = await _table.GetAsync(jobId, entity.RowKey, ct);
            if (stored is null)
                continue; // gone between the two calls: try the insert again

            var storedHash = stored.GetString("hash");
            if (string.Equals(storedHash, hash, StringComparison.Ordinal))
                return RecordWrite.AlreadyThere;

            _logger.LogWarning(
                "V31 record conflict. JobId={JobId}, Key={Key}, StoredHash={StoredHash}, WrittenHash={WrittenHash}",
                jobId, row.Key, storedHash, hash);
            return RecordWrite.Conflict;
        }

        // the row keeps vanishing between insert and read: report it as a conflict rather than loop for ever
        _logger.LogWarning("V31 record conflict. JobId={JobId}, Key={Key}, the stored row could not be read", jobId, row.Key);
        return RecordWrite.Conflict;
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (_ensured)
            return;

        await _ensureLock.WaitAsync(ct);
        try
        {
            if (_ensured)
                return;

            await _table.EnsureAsync(ct);
            _ensured = true;
        }
        finally
        {
            _ensureLock.Release();
        }
    }

    private static TableEntity ToEntity(string jobId, RecordRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rowKey = RecordKeys.Escape(row.Key);
        var entity = new TableEntity(jobId, rowKey)
        {
            ["key"] = row.Key,
            ["section"] = row.Section,
            ["hash"] = CanonicalJson.HashText(row.Json),
        };

        var json = row.Json;
        var start = 0;
        var part = 0;
        do
        {
            var length = Math.Min(ChunkLength, json.Length - start);
            // never cut between the two halves of a surrogate pair: each part must be well-formed text on its own
            if (start + length < json.Length && length > 1 && char.IsHighSurrogate(json[start + length - 1]))
                length--;

            entity[part == 0 ? "row" : "row" + part.ToString(CultureInfo.InvariantCulture)] = json.Substring(start, length);
            start += length;
            part++;
        }
        while (start < json.Length);

        long bytes = EntityOverheadBytes;
        foreach (var (name, value) in entity)
        {
            if (value is string text)
                bytes += 2L * (name.Length + text.Length);
        }

        if (bytes > MaxEntityBytes)
        {
            throw new InvalidOperationException(
                $"A record row is too large to store. Key={row.Key}, Characters={json.Length.ToString(CultureInfo.InvariantCulture)}");
        }

        return entity;
    }

    private static RecordRow FromEntity(TableEntity entity)
    {
        var json = new StringBuilder();
        for (var part = 0; ; part++)
        {
            var name = part == 0 ? "row" : "row" + part.ToString(CultureInfo.InvariantCulture);
            if (entity.GetString(name) is not { } text)
                break;

            json.Append(text);
        }

        return new RecordRow(entity.GetString("section") ?? "", entity.GetString("key") ?? "", json.ToString());
    }
}
