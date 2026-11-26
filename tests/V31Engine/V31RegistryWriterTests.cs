using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 17; part 07 § 5 step 4.5; Review Focus 1 and 2): <see cref="V31RegistryWriter"/>
/// over the registry's blob seam (<see cref="FakeRegistry"/>). The files go in the order given, with their media types,
/// then the stamp with create-if-absent; a stamped folder is never touched; an unstamped one is cleared first; a stamp is
/// created only over the <c>resolve.json</c> and <c>lock.json</c> it names; and what it writes the engine loads.
/// </summary>
public class V31RegistryWriterTests
{
    private const string Kind = SignInKinds.Organisation;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly Lazy<EngineCapabilities> ThisEngine = new(() => V31Job.Factory().Current());

    /// <summary>A golden's pair as a publish gives it: its name, version, files in part 07 § 5's order and stamp.</summary>
    private sealed record Publish(string Name, string Version, IReadOnlyList<PublishFile> Files, PublishedStamp Stamp)
    {
        public string Folder => $"{Name}/{Version}/";
    }

    private static Publish Of(string golden = "nested-note", string? name = null, string? version = null, string note = "Margaret Okafor's note.", bool old = false)
    {
        var pair = CompiledGoldenTests.Published(CompiledGoldenTests.Cases().Single(c => c.Name == golden));
        var package = JsonNode.Parse(pair.Resolve)!["package"]!;
        List<PublishFile> files =
        [
            new("manifest.json", Utf8NoBom.GetBytes($"{{\"name\":\"{golden}\"}}\n")),
            new("nodes/draft.md", Utf8NoBom.GetBytes(note)),
        ];
        if (old)
            files.Add(new("nodes/old.md", Utf8NoBom.GetBytes("Dr A. Sample's old node.")));
        files.Add(new(ResolveCompiler.LockFile, Utf8NoBom.GetBytes(pair.Lock)));
        files.Add(new(PublishReport.FileName, Utf8NoBom.GetBytes("{\"outcome\":\"published\"}")));
        files.Add(new(CompiledPackage.ResolveFile, Utf8NoBom.GetBytes(pair.Resolve)));
        return new(name ?? (string)package["name"]!, version ?? (string)package["version"]!, files, new PublishedStamp(pair.ResolveHash, pair.LockHash));
    }

    private static Task<bool> Write(IV31RegistryWriter writer, Publish publish) =>
        writer.WriteAsync(Kind, publish.Name, publish.Version, publish.Files, publish.Stamp, CancellationToken.None);

    private static CompiledPackageStore Store(FakeRegistry registry) =>
        new(registry, registry, ThisEngine, SpecVersionAdmissionTests.With("31"), new EngineLimitSettings(new ConfigurationBuilder().Build()));

    [Fact]
    public async Task TheFiles_AreWrittenInTheirOrder_ThenTheStampLast_CreatedOnlyIfAbsent()
    {
        var registry = new FakeRegistry();
        var publish = Of();

        Assert.True(await Write(new V31RegistryWriter(registry), publish));

        var lease = V31RegistryWriter.LeasePath(publish.Name, publish.Version);
        List<(string, string?, string)> expected = [("acquire", Kind, lease), ("delete", Kind, publish.Folder)];
        foreach (var file in publish.Files)
            expected.AddRange([("renew", Kind, lease), ("write", Kind, publish.Folder + file.Path)]);
        expected.AddRange([("renew", Kind, lease), ("create", Kind, publish.Folder + PublishedStamp.FileName), ("release", Kind, lease)]);
        Assert.Equal(expected, registry.Changes);
        Assert.False(registry.IsLeased(lease));
        foreach (var file in publish.Files)
            Assert.Equal(file.Bytes, registry.Blobs[publish.Folder + file.Path]);
        Assert.Equal(publish.Stamp.ToJson(), Utf8NoBom.GetString(registry.Blobs[publish.Folder + PublishedStamp.FileName]));
    }

    [Theory]
    [InlineData("resolve.json", "application/vnd.consultologist.resolve+json")]
    [InlineData("lock.json", "application/vnd.consultologist.lock+json")]
    [InlineData("publish-report.json", "application/vnd.consultologist.publish-report+json")]
    [InlineData("publish.json", "application/json")]
    [InlineData("manifest.json", "application/json")]
    [InlineData("nodes/draft.md", "text/markdown")]
    [InlineData("nodes/old.md", "text/markdown")]
    [InlineData("migration-report.json", "application/json")]
    [InlineData("formats/resolve.json", "application/json")]
    [InlineData("dag.mmd", "text/plain")]
    public async Task EachFile_HasItsMediaType(string path, string mediaType)
    {
        var registry = new FakeRegistry();
        var publish = Of(old: true);
        publish = publish with { Files = [.. publish.Files, new("migration-report.json", Utf8NoBom.GetBytes("{}")), new("formats/resolve.json", Utf8NoBom.GetBytes("{}")), new("dag.mmd", Utf8NoBom.GetBytes("flowchart TD"))] };
        // the extra files go after resolve.json here, which the writer does not mind: it writes what it is given, in order
        Assert.True(await Write(new V31RegistryWriter(registry), publish));

        Assert.Equal(mediaType, registry.ContentTypes[publish.Folder + path]);
    }

    [Fact]
    public async Task IsPublished_IsTrueOnlyWithAStamp()
    {
        var registry = new FakeRegistry();
        var writer = new V31RegistryWriter(registry);
        var publish = Of();

        Assert.False(await writer.IsPublishedAsync(Kind, publish.Name, publish.Version, CancellationToken.None));

        registry.ChangeFailure = path => path.EndsWith(PublishedStamp.FileName, StringComparison.Ordinal) ? new IOException("died") : null;
        await Assert.ThrowsAsync<IOException>(() => Write(writer, publish));
        Assert.True(registry.Blobs.ContainsKey(publish.Folder + CompiledPackage.ResolveFile));
        Assert.False(await writer.IsPublishedAsync(Kind, publish.Name, publish.Version, CancellationToken.None));

        registry.ChangeFailure = null;
        Assert.True(await Write(writer, publish));
        Assert.True(await writer.IsPublishedAsync(Kind, publish.Name, publish.Version, CancellationToken.None));
        Assert.False(await writer.IsPublishedAsync(Kind, publish.Name, "v2026.12.99", CancellationToken.None));
    }

    [Fact]
    public async Task AnyPublishJson_IsAStamp_SoA530StampedFolderIsPublishedToo()
    {
        var registry = new FakeRegistry();
        var publish = Of();
        registry.Blobs[publish.Folder + PublishedStamp.FileName] = Utf8NoBom.GetBytes("{\"specVersion\":30}");

        Assert.True(await new V31RegistryWriter(registry).IsPublishedAsync(Kind, publish.Name, publish.Version, CancellationToken.None));
        Assert.False(await Write(new V31RegistryWriter(registry), publish));
        Assert.Equal(["acquire", "release"], registry.Changes.Select(change => change.Operation));
    }

    [Fact]
    public async Task AStampedFolder_IsNeverTouched_AndTheWriteReturnsFalse()
    {
        var registry = new FakeRegistry();
        var (packageRef, _) = registry.Put(CompiledGoldenTests.Cases().Single(c => c.Name == "nested-note"));
        registry.Blobs[FakeRegistry.PathOf(packageRef, "nodes/draft.md")] = Utf8NoBom.GetBytes("Margaret Okafor's first note.");
        var before = registry.Blobs.ToDictionary(blob => blob.Key, blob => blob.Value, StringComparer.Ordinal);
        var lease = V31RegistryWriter.LeasePath(packageRef.Name, packageRef.Version);

        Assert.False(await Write(new V31RegistryWriter(registry), Of("conditional", packageRef.Name, packageRef.Version, "Margaret Okafor's second note.")));

        Assert.Equal([("acquire", Kind, lease), ("release", Kind, lease)], registry.Changes);
        Assert.Equal(before.OrderBy(b => b.Key, StringComparer.Ordinal), registry.Blobs.Where(b => b.Key != lease).OrderBy(b => b.Key, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ReviewFocus1_ARetryAfterAnAttemptThatDiedBeforeTheStamp_LeavesNoStaleFile()
    {
        var registry = new FakeRegistry();
        var writer = new V31RegistryWriter(registry);
        var first = Of(old: true);
        registry.ChangeFailure = path => path.EndsWith(PublishedStamp.FileName, StringComparison.Ordinal) ? new IOException("died") : null;
        await Assert.ThrowsAsync<IOException>(() => Write(writer, first));
        Assert.True(registry.Blobs.ContainsKey(first.Folder + "nodes/old.md"));

        registry.ChangeFailure = null;
        var retry = Of("conditional", first.Name, first.Version, "Margaret Okafor's revised note.");
        Assert.True(await Write(writer, retry));

        Assert.False(registry.Blobs.ContainsKey(first.Folder + "nodes/old.md"));
        Assert.Equal(
            retry.Files.Select(file => retry.Folder + file.Path).Append(retry.Folder + PublishedStamp.FileName).Order(StringComparer.Ordinal),
            registry.Blobs.Keys.Where(path => path.StartsWith(retry.Folder, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        foreach (var file in retry.Files)
            Assert.Equal(file.Bytes, registry.Blobs[retry.Folder + file.Path]);
        Assert.Equal(retry.Stamp.ToJson(), Utf8NoBom.GetString(registry.Blobs[retry.Folder + PublishedStamp.FileName]));
    }

    [Fact]
    public async Task AResolveJsonNotTheStamps_IsNotStamped()
    {
        var registry = new FakeRegistry();
        var publish = Of();
        var other = Of("conditional");
        // resolve.json from another package: the stamp about to be written does not name it
        publish = publish with { Files = [.. publish.Files.Where(f => f.Path != CompiledPackage.ResolveFile), other.Files.Single(f => f.Path == CompiledPackage.ResolveFile)] };

        Assert.False(await Write(new V31RegistryWriter(registry), publish));

        Assert.DoesNotContain(registry.Changes, change => change.Operation == "create");
        Assert.False(registry.Blobs.ContainsKey(publish.Folder + PublishedStamp.FileName));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ReviewFocus2_TwoWritersInterleavedFileByFile_StampOnce_AndTheLoserReturnsFalse(int first)
    {
        var registry = new FakeRegistry();
        var alternation = new Alternation(first);
        var one = Of("nested-note", Account, Version, "Margaret Okafor's note, one.");
        var two = Of("conditional", Account, Version, "Margaret Okafor's note, two.");
        Publish[] publishes = [one, two];

        var writes = Enumerable.Range(0, 2).Select(async who =>
        {
            try
            {
                return await Write(new V31RegistryWriter(new Alternating(registry, alternation, who)), publishes[who]);
            }
            finally
            {
                alternation.Finish(who);
            }
        }).ToArray();
        var won = await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Single(won, true);
        Assert.Single(registry.Changes, change => change.Operation == "create");
        var winner = publishes[Array.IndexOf(won, true)];
        var folder = one.Folder;
        Assert.Equal(winner.Stamp.ToJson(), Utf8NoBom.GetString(registry.Blobs[folder + PublishedStamp.FileName]));
        Assert.Equal(winner.Files.Single(f => f.Path == CompiledPackage.ResolveFile).Bytes, registry.Blobs[folder + CompiledPackage.ResolveFile]);
        Assert.Equal(winner.Files.Single(f => f.Path == ResolveCompiler.LockFile).Bytes, registry.Blobs[folder + ResolveCompiler.LockFile]);
        Assert.Null(CompiledPackage.Verify(one.Name, one.Version, registry.Blobs[folder + CompiledPackage.ResolveFile],
            registry.Blobs[folder + ResolveCompiler.LockFile], winner.Stamp.ResolveHash, winner.Stamp.LockHash));
    }

    private const string Account = "acct-0123456789ab";
    private const string Version = "v2026.12.1";

    [Fact]
    public async Task Critical1_AStampMadeBetweenTheStampCheckAndTheClear_IsNeverDeleted_AndTheWriteIsLost()
    {
        // a stamp no lease kept out (a writer whose step outlived its lease, or one not of this class) lands between this
        // writer's stamp check and its clear: the clear keeps it, and this writer's create fails
        var registry = new FakeRegistry();
        var rival = Of("conditional", Account, Version, "Margaret Okafor's rival note.");
        var publish = Of("nested-note", Account, Version);
        var stampPath = publish.Folder + PublishedStamp.FileName;
        registry.BeforeChange = (operation, _) =>
        {
            if (operation == "delete")
                registry.Blobs[stampPath] = Utf8NoBom.GetBytes(rival.Stamp.ToJson());
            return Task.CompletedTask;
        };

        Assert.False(await Write(new V31RegistryWriter(registry), publish));

        Assert.Equal(rival.Stamp.ToJson(), Utf8NoBom.GetString(registry.Blobs[stampPath]));
    }

    [Fact]
    public async Task ReviewFocus2_AWriterStartingWhileAnotherHoldsTheLease_ReturnsFalse_AndWritesNothing()
    {
        var registry = new FakeRegistry();
        var one = Of("nested-note", Account, Version, "Margaret Okafor's note, one.");
        var two = Of("conditional", Account, Version, "Margaret Okafor's note, two.");
        bool? second = null;
        IReadOnlyList<(string Operation, string? Kind, string Path)>? secondChanges = null;
        registry.BeforeChange = async (operation, _) =>
        {
            if (operation != "write" || second != null)
                return;
            var before = registry.Changes.Count;
            second = false;
            second = await Write(new V31RegistryWriter(registry), two);
            secondChanges = registry.Changes.Skip(before).ToList();
        };

        Assert.True(await Write(new V31RegistryWriter(registry), one));

        Assert.False(second);
        Assert.Equal([("acquire", Kind, V31RegistryWriter.LeasePath(Account, Version))], secondChanges);
        Assert.Single(registry.Changes, change => change.Operation == "create");
        foreach (var file in one.Files)
            Assert.Equal(file.Bytes, registry.Blobs[one.Folder + file.Path]);
        Assert.Equal(one.Stamp.ToJson(), Utf8NoBom.GetString(registry.Blobs[one.Folder + PublishedStamp.FileName]));
    }

    [Fact]
    public async Task ReviewFocus2_AWriterWhoseLeaseIsLostMidWrite_DoesNotStamp_AndWritesNoMore()
    {
        // one's lease lapses after its first write (one stalled past it) and two takes the folder and publishes; one's next
        // renew fails
        var registry = new FakeRegistry();
        var one = Of("nested-note", Account, Version, "Margaret Okafor's note, one.", old: true);
        var two = Of("conditional", Account, Version, "Margaret Okafor's note, two.");
        var lease = V31RegistryWriter.LeasePath(Account, Version);
        bool? second = null;
        registry.BeforeChange = async (operation, _) =>
        {
            if (operation != "renew" || second != null || !registry.Changes.Any(change => change.Operation == "write"))
                return;
            second = false;
            registry.LapseLease(lease);
            second = await Write(new V31RegistryWriter(registry), two);
        };

        Assert.False(await Write(new V31RegistryWriter(registry), one));

        Assert.True(second);
        Assert.Single(registry.Changes, change => change.Operation == "create");
        Assert.Equal(
            two.Files.Select(file => two.Folder + file.Path).Append(two.Folder + PublishedStamp.FileName).Order(StringComparer.Ordinal),
            registry.Blobs.Keys.Where(path => path.StartsWith(two.Folder, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        foreach (var file in two.Files)
            Assert.Equal(file.Bytes, registry.Blobs[two.Folder + file.Path]);
        Assert.Equal(two.Stamp.ToJson(), Utf8NoBom.GetString(registry.Blobs[two.Folder + PublishedStamp.FileName]));
    }

    [Fact]
    public async Task ReviewFocus1_ACrashedWritersLease_HoldsTheFolderUntilItLapses_ThenARetryLeavesNoStaleFile()
    {
        var registry = new FakeRegistry();
        var writer = new V31RegistryWriter(registry);
        var first = Of("nested-note", Account, Version, old: true);
        var lease = V31RegistryWriter.LeasePath(Account, Version);
        // dies writing resolve.json, its release lost with it
        registry.DropReleases = true;
        registry.ChangeFailure = path => path.EndsWith(CompiledPackage.ResolveFile, StringComparison.Ordinal) ? new IOException("died") : null;
        await Assert.ThrowsAsync<IOException>(() => Write(writer, first));
        registry.ChangeFailure = null;
        registry.DropReleases = false;
        Assert.True(registry.Blobs.ContainsKey(first.Folder + "nodes/old.md"));

        var retry = Of("conditional", Account, Version, "Margaret Okafor's revised note.");
        var changes = registry.Changes.Count;
        Assert.False(await Write(writer, retry));
        Assert.Equal([("acquire", Kind, lease)], registry.Changes.Skip(changes));

        registry.LapseLease(lease);
        Assert.True(await Write(writer, retry));

        Assert.Equal(
            retry.Files.Select(file => retry.Folder + file.Path).Append(retry.Folder + PublishedStamp.FileName).Order(StringComparer.Ordinal),
            registry.Blobs.Keys.Where(path => path.StartsWith(retry.Folder, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(retry.Stamp.ToJson(), Utf8NoBom.GetString(registry.Blobs[retry.Folder + PublishedStamp.FileName]));
    }

    [Fact]
    public async Task TheLockBlob_IsBesideTheFolder_SoNoListingTakesItForAFileOrAVersion()
    {
        var registry = new FakeRegistry();
        var publish = Of("nested-note", Account, Version);

        Assert.True(await Write(new V31RegistryWriter(registry), publish));

        var lease = V31RegistryWriter.LeasePath(Account, Version);
        Assert.Equal($"{Account}/{Version}.lease", lease);
        Assert.True(registry.Blobs.ContainsKey(lease));
        Assert.False(registry.IsLeased(lease));
        // a folder's files are the blobs under {name}/{version}/; a version is listed by its {name}/{version}/manifest.json
        Assert.DoesNotContain(registry.Blobs.Keys, path => path.StartsWith(publish.Folder, StringComparison.Ordinal) && path.EndsWith(".lease", StringComparison.Ordinal));
        Assert.Equal(
            [(Account, Version)],
            registry.Blobs.Keys.Select(path => WorkflowPackageRef.TryParseManifestPath(path, out var name, out var version) ? (name, version) : default)
                .Where(found => found != default).Distinct());
        Assert.Equal([Account], AccountPackageListing.NamesIn(registry.Blobs.Keys));
    }

    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public async Task AWrittenVersion_IsLoadedByTheEngine(string golden)
    {
        var registry = new FakeRegistry();
        var publish = Of(golden);

        Assert.True(await Write(new V31RegistryWriter(registry), publish));
        Assert.True(registry.Blobs.ContainsKey(V31RegistryWriter.LeasePath(publish.Name, publish.Version)));

        var load = await Store(registry).TryLoadAsync(new WorkflowPackageRef(publish.Name, publish.Version), CancellationToken.None);
        Assert.NotNull(load);
        Assert.True(load!.Refusal == null, $"{load.RefusalId}: {load.Refusal} ({load.OperatorDetail})");
        Assert.Equal(publish.Stamp.ResolveHash, load.Plan!.ResolveHash);
        Assert.Equal(publish.Stamp.LockHash, load.Plan.LockHash);
    }

    /// <summary>
    /// Two writers taking turns, one registry call each: <c>first</c> goes first; a writer that has finished gives every
    /// later turn to the other.
    /// </summary>
    private sealed class Alternation(int first)
    {
        private readonly SemaphoreSlim[] _turns = [new(first == 0 ? 1 : 0), new(first == 1 ? 1 : 0)];
        private readonly bool[] _done = new bool[2];
        private readonly Lock _gate = new();

        public Task WaitAsync(int who) => _turns[who].WaitAsync();

        public void Pass(int who)
        {
            lock (_gate)
                _turns[_done[1 - who] ? who : 1 - who].Release();
        }

        public void Finish(int who)
        {
            lock (_gate)
            {
                _done[who] = true;
                _turns[1 - who].Release();
            }
        }
    }

    /// <summary>The registry as one of two alternating writers sees it.</summary>
    private sealed class Alternating(FakeRegistry registry, Alternation alternation, int who) : IRegistryFiles
    {
        public Task<byte[]?> TryReadAsync(string packageName, string path, CancellationToken ct) =>
            Turn(() => registry.TryReadAsync(packageName, path, ct));

        public Task<IReadOnlyList<string>> ListAsync(string packageName, string prefix, CancellationToken ct) =>
            Turn(() => registry.ListAsync(packageName, prefix, ct));

        public Task WriteAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct) =>
            Turn(async () =>
            {
                await registry.WriteAsync(accountKind, path, bytes, contentType, ct);
                return true;
            });

        public Task<bool> TryCreateAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct) =>
            Turn(() => registry.TryCreateAsync(accountKind, path, bytes, contentType, ct));

        public Task DeleteAllAsync(string? accountKind, string prefix, string keep, CancellationToken ct) =>
            Turn(async () =>
            {
                await registry.DeleteAllAsync(accountKind, prefix, keep, ct);
                return true;
            });

        public Task<string?> TryAcquireLeaseAsync(string? accountKind, string path, CancellationToken ct) =>
            Turn(() => registry.TryAcquireLeaseAsync(accountKind, path, ct));

        public Task<bool> RenewLeaseAsync(string? accountKind, string path, string leaseId, CancellationToken ct) =>
            Turn(() => registry.RenewLeaseAsync(accountKind, path, leaseId, ct));

        public Task ReleaseLeaseAsync(string? accountKind, string path, string leaseId, CancellationToken ct) =>
            Turn(async () =>
            {
                await registry.ReleaseLeaseAsync(accountKind, path, leaseId, ct);
                return true;
            });

        private async Task<T> Turn<T>(Func<Task<T>> call)
        {
            await alternation.WaitAsync(who);
            try
            {
                return await call();
            }
            finally
            {
                alternation.Pass(who);
            }
        }
    }
}
