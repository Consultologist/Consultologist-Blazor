using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.V31;
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

        var changes = registry.Changes;
        Assert.Equal(("delete", Kind, publish.Folder), changes[0]);
        Assert.Equal(
            publish.Files.Select(file => ("write", (string?)Kind, publish.Folder + file.Path)).Append(("create", Kind, publish.Folder + PublishedStamp.FileName)),
            changes.Skip(1));
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
    public async Task EachFile_HasItsMediaType(string path, string mediaType)
    {
        var registry = new FakeRegistry();
        var publish = Of(old: true);
        publish = publish with { Files = [.. publish.Files, new("migration-report.json", Utf8NoBom.GetBytes("{}")), new("formats/resolve.json", Utf8NoBom.GetBytes("{}"))] };
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
        Assert.Empty(registry.Changes);
    }

    [Fact]
    public async Task AStampedFolder_IsNeverTouched_AndTheWriteReturnsFalse()
    {
        var registry = new FakeRegistry();
        var (packageRef, _) = registry.Put(CompiledGoldenTests.Cases().Single(c => c.Name == "nested-note"));
        registry.Blobs[FakeRegistry.PathOf(packageRef, "nodes/draft.md")] = Utf8NoBom.GetBytes("Margaret Okafor's first note.");
        var before = registry.Blobs.ToDictionary(blob => blob.Key, blob => blob.Value, StringComparer.Ordinal);

        Assert.False(await Write(new V31RegistryWriter(registry), Of("conditional", packageRef.Name, packageRef.Version, "Margaret Okafor's second note.")));

        Assert.Empty(registry.Changes);
        Assert.Equal(before.OrderBy(b => b.Key, StringComparer.Ordinal), registry.Blobs.OrderBy(b => b.Key, StringComparer.Ordinal));
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
            registry.Blobs.Keys.Order(StringComparer.Ordinal));
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
        var one = Of("nested-note", "acct-0123456789ab", "v2026.12.1", "Margaret Okafor's note, one.");
        var two = Of("conditional", "acct-0123456789ab", "v2026.12.1", "Margaret Okafor's note, two.");
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

    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public async Task AWrittenVersion_IsLoadedByTheEngine(string golden)
    {
        var registry = new FakeRegistry();
        var publish = Of(golden);

        Assert.True(await Write(new V31RegistryWriter(registry), publish));

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

        public Task WriteAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct) =>
            Turn(async () =>
            {
                await registry.WriteAsync(accountKind, path, bytes, contentType, ct);
                return true;
            });

        public Task<bool> TryCreateAsync(string? accountKind, string path, byte[] bytes, string contentType, CancellationToken ct) =>
            Turn(() => registry.TryCreateAsync(accountKind, path, bytes, contentType, ct));

        public Task DeleteAllAsync(string? accountKind, string prefix, CancellationToken ct) =>
            Turn(async () =>
            {
                await registry.DeleteAllAsync(accountKind, prefix, ct);
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
