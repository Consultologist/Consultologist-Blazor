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
