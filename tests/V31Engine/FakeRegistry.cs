using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06): a registry over a dictionary of blob paths (<c>{name}/{version}/{file}</c>) to bytes,
/// counting every read, for <see cref="CompiledPackageStore"/>. <see cref="Put(CompiledGoldenTests.GoldenCase, string?)"/>
/// publishes a golden into it as publish would leave it: <c>resolve.json</c>, <c>lock.json</c> and the stamp. A
/// <c>latest</c> ref resolves through <see cref="Latest"/>, as the store's pointer read would.
/// #1508 (area 11): one container for every account kind; writes, create-if-absent and deletes by prefix are recorded in
/// <see cref="Changes"/>, with each blob's content type.
/// </summary>
internal sealed class FakeRegistry : IRegistryFiles, IWorkflowPackageVersions
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly ConcurrentQueue<string> _reads = new();

    /// <summary>The blobs, by path.</summary>
    public ConcurrentDictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

    /// <summary>Each package's latest version, as its <c>latest.json</c> would name it.</summary>
    public Dictionary<string, string> Latest { get; } = new(StringComparer.Ordinal);

    /// <summary>The paths read, in order.</summary>
    public IReadOnlyList<string> Reads => _reads.ToArray();

    /// <summary>What a version's resolution throws, as the 5–30 store's pointer read would (null: it resolves).</summary>
    public Exception? ResolveFailure { get; set; }

    /// <summary>What a read throws, as storage would (null: it reads).</summary>
    public Exception? ReadFailure { get; set; }

    public Task<byte[]?> TryReadAsync(string packageName, string path, CancellationToken ct)
    {
        Assert.StartsWith(packageName + "/", path, StringComparison.Ordinal);
        _reads.Enqueue(path);
        if (ReadFailure != null)
            return Task.FromException<byte[]?>(ReadFailure);
        return Task.FromResult(Blobs.TryGetValue(path, out var bytes) ? bytes : null);
    }

    public async Task WriteAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct)
    {
        await Change("write", accountKind, path);
        Blobs[path] = bytes;
        ContentTypes[path] = contentType;
    }

    public async Task<bool> TryCreateAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct)
    {
        await Change("create", accountKind, path);
        if (!Blobs.TryAdd(path, bytes))
            return false;
        ContentTypes[path] = contentType;
        return true;
    }

    public async Task DeleteAllAsync(string? accountKind, string prefix, string keep, CancellationToken ct)
    {
        await Change("delete", accountKind, prefix);
        foreach (var path in Blobs.Keys.Where(path => path.StartsWith(prefix, StringComparison.Ordinal) && path != keep))
        {
            Blobs.TryRemove(path, out _);
            ContentTypes.TryRemove(path, out _);
        }
    }

    public async Task<string?> TryAcquireLeaseAsync(string? accountKind, string path, CancellationToken ct)
    {
        await Change("acquire", accountKind, path);
        lock (_leases)
        {
            Blobs.TryAdd(path, []);
            if (_leases.TryGetValue(path, out var held) && !held.Lapsed)
                return null;
            var id = Guid.NewGuid().ToString();
            _leases[path] = (id, false);
            return id;
        }
    }

    public async Task<bool> RenewLeaseAsync(string? accountKind, string path, string leaseId, CancellationToken ct)
    {
        await Change("renew", accountKind, path);
        lock (_leases)
        {
            // as Azure renews: a lapsed lease too, unless another holder has taken it since
            if (!_leases.TryGetValue(path, out var held) || held.Id != leaseId)
                return false;
            _leases[path] = (leaseId, false);
            return true;
        }
    }

    public async Task ReleaseLeaseAsync(string? accountKind, string path, string leaseId, CancellationToken ct)
    {
        if (DropReleases)
            return;
        await Change("release", accountKind, path);
        lock (_leases)
        {
            if (_leases.TryGetValue(path, out var held) && held.Id == leaseId)
                _leases.Remove(path);
        }
    }

    /// <summary>#1508 (area 11): the lease on <paramref name="path"/> lapses, as one unrenewed for <see cref="IRegistryFiles.LeaseSeconds"/> does.</summary>
    public void LapseLease(string path)
    {
        lock (_leases)
        {
            if (_leases.TryGetValue(path, out var held))
                _leases[path] = (held.Id, true);
        }
    }

    /// <summary>#1508 (area 11): whether a live lease is held on <paramref name="path"/>.</summary>
    public bool IsLeased(string path)
    {
        lock (_leases)
            return _leases.TryGetValue(path, out var held) && !held.Lapsed;
    }

    /// <summary>#1508 (area 11): releases are lost, as a writer that dies loses its own.</summary>
    public bool DropReleases { get; set; }

    /// <summary>#1508 (area 11): the content type each blob was written with, by path.</summary>
    public ConcurrentDictionary<string, string> ContentTypes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// #1508 (area 11): each write, create, delete and lease call, in order: the operation, the account kind and the path
    /// (a delete's prefix, a lease's lock blob).
    /// </summary>
    public IReadOnlyList<(string Operation, string? Kind, string Path)> Changes => _changes.ToArray();

    /// <summary>#1508 (area 11): what a change of a path throws, as storage would (null: it is made).</summary>
    public Func<string, Exception?>? ChangeFailure { get; set; }

    /// <summary>#1508 (area 11): run before each change (its operation and path), after it is recorded: another writer's step, say.</summary>
    public Func<string, string, Task>? BeforeChange { get; set; }

    private readonly ConcurrentQueue<(string Operation, string? Kind, string Path)> _changes = new();

    private readonly Dictionary<string, (string Id, bool Lapsed)> _leases = new(StringComparer.Ordinal);

    private async Task Change(string operation, string? accountKind, string path)
    {
        if (ChangeFailure?.Invoke(path) is { } failure)
            throw failure;
        _changes.Enqueue((operation, accountKind, path));
        if (BeforeChange is { } before)
            await before(operation, path);
    }

    public Task<string> ResolveVersionAsync(WorkflowPackageRef packageRef, CancellationToken cancellationToken) =>
        ResolveFailure != null
            ? Task.FromException<string>(ResolveFailure)
            : Task.FromResult(packageRef.IsLatest ? Latest[packageRef.Name] : packageRef.Version);

    /// <summary>Forgets the reads so far.</summary>
    public void ClearReads() => _reads.Clear();

    /// <summary>
    /// Publishes <paramref name="golden"/> into the folder of its package (or <paramref name="folder"/>) at its version and
    /// returns its ref and the pair.
    /// </summary>
    public (WorkflowPackageRef Ref, CompiledPackage Pair) Put(CompiledGoldenTests.GoldenCase golden, string? folder = null)
    {
        var pair = CompiledGoldenTests.Published(golden);
        return (Put(pair.Resolve, pair.Lock, folder), pair);
    }

    /// <summary>
    /// Publishes <paramref name="golden"/> with <paramref name="resolve"/> and <paramref name="lock"/> applied to its
    /// documents, the lock's <c>resolve.hash</c> and the stamp made to agree, so the pair verifies.
    /// </summary>
    public WorkflowPackageRef PutChanged(CompiledGoldenTests.GoldenCase golden, Action<JsonObject>? resolve = null, Action<JsonObject>? @lock = null)
    {
        var pair = CompiledGoldenTests.Published(golden);
        var resolveDocument = JsonNode.Parse(pair.Resolve)!.AsObject();
        resolve?.Invoke(resolveDocument);
        var resolveText = CanonicalJson.Write(resolveDocument);

        var lockDocument = JsonNode.Parse(pair.Lock)!.AsObject();
        lockDocument["resolve"]!["hash"] = CanonicalJson.HashText(resolveText);
        @lock?.Invoke(lockDocument);
        return Put(resolveText, CanonicalJson.Write(lockDocument));
    }

    /// <summary>Writes the pair and its stamp into the folder of the package <paramref name="resolve"/> names (or <paramref name="folder"/>).</summary>
    public WorkflowPackageRef Put(string resolve, string @lock, string? folder = null)
    {
        var package = JsonNode.Parse(resolve)!["package"]!;
        var packageRef = new WorkflowPackageRef(folder ?? (string)package["name"]!, (string)package["version"]!);
        Blobs[PathOf(packageRef, CompiledPackage.ResolveFile)] = Utf8NoBom.GetBytes(resolve);
        Blobs[PathOf(packageRef, ResolveCompiler.LockFile)] = Utf8NoBom.GetBytes(@lock);
        Blobs[PathOf(packageRef, PublishedStamp.FileName)] =
            Utf8NoBom.GetBytes(new PublishedStamp(CanonicalJson.HashText(resolve), CanonicalJson.HashText(@lock)).ToJson());
        return packageRef;
    }

    /// <summary>The blob path of <paramref name="file"/> in the folder of <paramref name="packageRef"/>.</summary>
    public static string PathOf(WorkflowPackageRef packageRef, string file) => $"{packageRef.Name}/{packageRef.Version}/{file}";
}
