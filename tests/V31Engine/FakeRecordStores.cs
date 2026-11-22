using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1506 (Task 9): an in-memory table with Azure's limits that matter here (a string property of at most 32K characters,
/// a 409 on a second insert) and a record of what was asked of it.
/// </summary>
internal sealed class InMemoryRecordTable : IRecordTable
{
    private readonly object _gate = new();
    private readonly SortedDictionary<(string Partition, string Row), TableEntity> _entities = new();

    public int Creates { get; private set; }

    public int Calls { get; private set; }

    public bool Created { get; private set; }

    public Task EnsureAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            Calls++;
            Creates++;
            Created = true;
        }

        return Task.CompletedTask;
    }

    public Task<bool> TryAddAsync(TableEntity entity, CancellationToken ct)
    {
        lock (_gate)
        {
            Calls++;
            if (!Created)
                throw new InvalidOperationException("The table was never created.");

            foreach (var (name, value) in entity)
            {
                if (value is string { Length: > 32 * 1024 })
                    throw new InvalidOperationException($"Property {name} is over 32K characters.");
            }

            return Task.FromResult(_entities.TryAdd((entity.PartitionKey, entity.RowKey), new TableEntity(entity)));
        }
    }

    public Task<TableEntity?> GetAsync(string partitionKey, string rowKey, CancellationToken ct)
    {
        lock (_gate)
        {
            Calls++;
            return Task.FromResult(_entities.TryGetValue((partitionKey, rowKey), out var found) ? new TableEntity(found) : null);
        }
    }

    public Task<IReadOnlyList<TableEntity>> ListAsync(string partitionKey, CancellationToken ct)
    {
        lock (_gate)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<TableEntity>>(
                _entities.Where(e => e.Key.Partition == partitionKey).Select(e => new TableEntity(e.Value)).ToList());
        }
    }

    /// <summary>The size of each batch given to <see cref="DeleteBatchAsync"/>, in order.</summary>
    public List<int> DeleteBatches { get; } = [];

    public Task<IReadOnlyList<string>> ListRowKeysAsync(string partitionKey, CancellationToken ct)
    {
        lock (_gate)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<string>>(_entities.Keys.Where(k => k.Partition == partitionKey).Select(k => k.Row).ToList());
        }
    }

    public Task DeleteBatchAsync(string partitionKey, IReadOnlyList<string> rowKeys, CancellationToken ct)
    {
        lock (_gate)
        {
            Calls++;
            DeleteBatches.Add(rowKeys.Count);
            if (rowKeys.Count > 100)
                throw new InvalidOperationException("A table transaction holds at most 100 actions.");

            foreach (var rowKey in rowKeys)
                _entities.Remove((partitionKey, rowKey)); // a row already gone is fine
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<TableEntity> All()
    {
        lock (_gate)
            return _entities.Values.Select(e => new TableEntity(e)).ToList();
    }
}

/// <summary>#1506 (Task 9): in-memory blobs: names, text, a create-if-absent put.</summary>
internal sealed class InMemoryRecordBlobs : IRecordBlobs
{
    private readonly ConcurrentDictionary<(string Container, string Name), byte[]> _blobs = new();

    public int Calls { get; private set; }

    public Task<bool> PutAsync(string container, string name, string content, bool onlyIfAbsent, CancellationToken ct)
    {
        Calls++;
        var bytes = new UTF8Encoding(false).GetBytes(content);
        if (onlyIfAbsent)
            return Task.FromResult(_blobs.TryAdd((container, name), bytes));

        _blobs[(container, name)] = bytes;
        return Task.FromResult(true);
    }

    public Task<string?> GetAsync(string container, string name, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(_blobs.TryGetValue((container, name), out var bytes) ? new UTF8Encoding(false).GetString(bytes) : null);
    }

    public Task DeleteByPrefixAsync(string container, string prefix, CancellationToken ct)
    {
        Calls++;
        foreach (var key in _blobs.Keys.Where(k => k.Container == container && k.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            _blobs.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    /// <summary>Every (container, name) held, ordinally.</summary>
    public IReadOnlyList<(string Container, string Name)> Names() =>
        _blobs.Keys.OrderBy(k => k.Container, StringComparer.Ordinal).ThenBy(k => k.Name, StringComparer.Ordinal).ToList();

    /// <summary>The raw bytes of one blob (to check no BOM), or null.</summary>
    public byte[]? BytesOf(string container, string name) => _blobs.TryGetValue((container, name), out var bytes) ? bytes : null;
}

/// <summary>
/// #1506 (Task 9): the row store for tests of the engine. It IS <see cref="TableRecordRowStore"/>, over
/// <see cref="InMemoryRecordTable"/>, so it answers exactly as the real one does (the same results, splitting, size
/// refusal and ordering); <see cref="Table"/> shows what was stored.
/// </summary>
internal sealed class FakeRecordRowStore : IRecordRowStore
{
    private readonly TableRecordRowStore _store;

    public FakeRecordRowStore(ILogger? logger = null)
    {
        Table = new InMemoryRecordTable();
        _store = new TableRecordRowStore(Table, logger ?? NullLogger.Instance);
    }

    public InMemoryRecordTable Table { get; }

    public Task<IReadOnlyList<(string Key, RecordWrite Result)>> WriteAsync(string jobId, IReadOnlyList<RecordRow> rows, CancellationToken ct) =>
        _store.WriteAsync(jobId, rows, ct);

    public Task<RecordRow?> ReadAsync(string jobId, string key, CancellationToken ct) => _store.ReadAsync(jobId, key, ct);

    public Task<IReadOnlyList<RecordRow>> ListAsync(string jobId, CancellationToken ct) => _store.ListAsync(jobId, ct);

    public Task DeleteJobAsync(string jobId, CancellationToken ct) => _store.DeleteJobAsync(jobId, ct);
}

/// <summary>#1506 (Task 9): the blob store for tests of the engine: <see cref="BlobRecordStore"/> over <see cref="InMemoryRecordBlobs"/>.</summary>
internal sealed class FakeRecordBlobStore : IRecordBlobStore
{
    private readonly BlobRecordStore _store;

    public FakeRecordBlobStore()
    {
        Blobs = new InMemoryRecordBlobs();
        _store = new BlobRecordStore(Blobs);
    }

    public InMemoryRecordBlobs Blobs { get; }

    public Task WriteTextAsync(string? accountKind, string appUserId, string jobId, string key, string part, JsonObject text, CancellationToken ct) =>
        _store.WriteTextAsync(accountKind, appUserId, jobId, key, part, text, ct);

    public Task<JsonObject?> ReadTextAsync(string? accountKind, string appUserId, string jobId, string key, CancellationToken ct) =>
        _store.ReadTextAsync(accountKind, appUserId, jobId, key, ct);

    public Task DeleteTextAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct) =>
        _store.DeleteTextAsync(accountKind, appUserId, jobId, ct);

    public Task<bool> WriteRecordAsync(string? accountKind, string appUserId, string jobId, string document, CancellationToken ct) =>
        _store.WriteRecordAsync(accountKind, appUserId, jobId, document, ct);

    public Task<string?> ReadRecordAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct) =>
        _store.ReadRecordAsync(accountKind, appUserId, jobId, ct);
}
