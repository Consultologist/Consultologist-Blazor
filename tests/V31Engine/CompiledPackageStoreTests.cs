using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 1.1, 1.2 steps 1 to 5; part 07 §§ 3, 4.4, 5; D1, D2, D4, D5): a 31 version is
/// loaded from its stamp, <c>resolve.json</c> and <c>lock.json</c> alone: verified against the stamp, its <c>requires</c>
/// held to this engine, and its plan built and cached. A version with no 31 stamp is today's path's, after one read.
/// </summary>
public class CompiledPackageStoreTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly Lazy<EngineCapabilities> ThisEngine =
        new(() => V31Job.Factory().Current());

    private static CompiledPackageStore Store(FakeRegistry registry, EngineCapabilities? engine = null, string? preview = "31") =>
        new(registry, registry, engine ?? ThisEngine.Value, SpecVersionAdmissionTests.With(preview),
            new EngineLimitSettings(new ConfigurationBuilder().Build()));

    private static CompiledGoldenTests.GoldenCase Golden(string name = "nested-note") =>
        CompiledGoldenTests.Cases().Single(c => c.Name == name);

    private static string PathOf(WorkflowPackageRef packageRef, string file) => FakeRegistry.PathOf(packageRef, file);

    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public async Task AStamped31Version_Loads(string name)
    {
        var registry = new FakeRegistry();
        var (packageRef, pair) = registry.Put(Golden(name));

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.NotNull(load);
        Assert.True(load!.Refusal == null, $"{load.RefusalId}: {load.Refusal} ({load.OperatorDetail})");
        Assert.NotNull(load.Plan);
        Assert.Equal(pair.ResolveHash, load.Plan!.ResolveHash);
        Assert.Equal(pair.LockHash, load.Plan.LockHash);
        Assert.Equal(packageRef.Name, load.Plan.Package);
        Assert.Equal(packageRef.Version, load.Plan.Version);
        Assert.Equal(
            [PathOf(packageRef, PublishedStamp.FileName), PathOf(packageRef, CompiledPackage.ResolveFile), PathOf(packageRef, ResolveCompiler.LockFile)],
            registry.Reads);
    }

    [Fact]
    public async Task ALatestRef_LoadsTheVersionThePointerNames()
    {
        var registry = new FakeRegistry();
        var (packageRef, pair) = registry.Put(Golden());
        registry.Latest[packageRef.Name] = packageRef.Version;

        var load = await Store(registry).TryLoadAsync(new WorkflowPackageRef(packageRef.Name, WorkflowPackageRef.LatestVersion), CancellationToken.None);

        Assert.Equal(pair.ResolveHash, load?.Plan?.ResolveHash);
    }

    [Fact]
    public async Task ASecondLoad_ReadsNothing()
    {
        var registry = new FakeRegistry();
        var (packageRef, _) = registry.Put(Golden());
        var store = Store(registry);
        var first = await store.TryLoadAsync(packageRef, CancellationToken.None);
        registry.ClearReads();

        var second = await store.TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Empty(registry.Reads);
        Assert.Same(first!.Plan, second!.Plan);
    }

    [Fact]
    public async Task ConcurrentFirstLoads_EndWithOnePlan()
    {
        var registry = new FakeRegistry();
        var (packageRef, _) = registry.Put(Golden());
        var store = Store(registry);

        var loads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.TryLoadAsync(packageRef, CancellationToken.None))));
        var again = await store.TryLoadAsync(packageRef, CancellationToken.None);

        Assert.All(loads, l => Assert.Same(again!.Plan, l!.Plan));
    }

    [Fact]
    public async Task AVersionWithNoStamp_IsNotThisPaths()
    {
        var registry = new FakeRegistry();
        var packageRef = new WorkflowPackageRef("example-nested-note", "v2026.12.1");
        registry.Blobs[PathOf(packageRef, "manifest.json")] = Utf8NoBom.GetBytes("{\"specVersion\":30}");

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load);
        Assert.Equal([PathOf(packageRef, PublishedStamp.FileName)], registry.Reads);
    }

    [Fact]
    public async Task A30Stamp_IsNotThisPaths()
    {
        var registry = new FakeRegistry();
        var (packageRef, pair) = registry.Put(Golden());
        registry.Blobs[PathOf(packageRef, PublishedStamp.FileName)] = Utf8NoBom.GetBytes(
            $"{{\"specVersion\":30,\"resolveHash\":\"{pair.ResolveHash}\",\"lockHash\":\"{pair.LockHash}\"}}");

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load);
        Assert.Equal([PathOf(packageRef, PublishedStamp.FileName)], registry.Reads);
    }

    [Fact]
    public async Task WithoutThePreview_ReadsNothing()
    {
        var registry = new FakeRegistry();
        var (packageRef, _) = registry.Put(Golden());

        var load = await Store(registry, preview: null).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load);
        Assert.Empty(registry.Reads);
    }

    public static TheoryData<string, string, string> Tampered() => new()
    {
        { CompiledPackage.ResolveFile, "re-indented", "S09-009" },
        { CompiledPackage.ResolveFile, "one byte changed", "S09-009" },
        { CompiledPackage.ResolveFile, "a trailing newline", "S09-009" },
        { ResolveCompiler.LockFile, "one byte changed", "S09-014" },
        { ResolveCompiler.LockFile, "naming another hash", "S09-014" },
    };

    [Theory]
    [MemberData(nameof(Tampered))]
    public async Task Verify_AcceptsOnlyTheBytesPublished(string file, string how, string id)
    {
        var registry = new FakeRegistry();
        var (packageRef, pair) = registry.Put(Golden());
        var path = PathOf(packageRef, file);
        var text = Encoding.UTF8.GetString(registry.Blobs[path]);
        switch (how)
        {
            case "re-indented":
                registry.Blobs[path] = Utf8NoBom.GetBytes(JsonNode.Parse(text)!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                break;
            case "one byte changed":
                // The package's name, inside a string, one letter later: still JSON, no longer the bytes stamped.
                registry.Blobs[path] = Utf8NoBom.GetBytes(text.Replace("example-nested-note", "example-nested-notf", StringComparison.Ordinal));
                break;
            case "a trailing newline":
                registry.Blobs[path] = Utf8NoBom.GetBytes(text + "\n");
                break;
            case "naming another hash":
                // The lock names another resolve.json, and the stamp is made to agree with the lock: only the link is wrong.
                var document = JsonNode.Parse(text)!.AsObject();
                document["resolve"]!["hash"] = CanonicalJson.HashText("{}");
                var lockText = CanonicalJson.Write(document);
                registry.Blobs[path] = Utf8NoBom.GetBytes(lockText);
                registry.Blobs[PathOf(packageRef, PublishedStamp.FileName)] =
                    Utf8NoBom.GetBytes(new PublishedStamp(pair.ResolveHash, CanonicalJson.HashText(lockText)).ToJson());
                break;
        }

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load!.Plan);
        Assert.Equal(id, load.RefusalId);
        V31Contract.AssertSentence(new SourceRefusal(id, load.Refusal!, ""));
        Assert.Contains($"{packageRef.Name}@{packageRef.Version}", load.Refusal, StringComparison.Ordinal);
        var detailId = id == "S09-009" ? "S07-006" : "S07-017";
        V31Contract.AssertSentence(new SourceRefusal(detailId, load.OperatorDetail!, ""));
    }

    [Theory]
    [InlineData(CompiledPackage.ResolveFile)]
    [InlineData(ResolveCompiler.LockFile)]
    public async Task AStampWithoutItsDocuments_IsNotPublished(string missing)
    {
        var registry = new FakeRegistry();
        var (packageRef, _) = registry.Put(Golden());
        registry.Blobs.TryRemove(PathOf(packageRef, missing), out _);

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load!.Plan);
        Assert.Equal("A06-004", load.RefusalId);
        V31Contract.AssertSentence(new SourceRefusal("A06-004", load.Refusal!, ""));
        Assert.Equal($"Workflow {packageRef.Name}@{packageRef.Version} isn't published.", load.Refusal);
    }

    [Fact]
    public async Task ADocumentForAnotherPackage_IsRefused()
    {
        var registry = new FakeRegistry();
        var (packageRef, _) = registry.Put(Golden(), folder: "example-other-note");

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load!.Plan);
        Assert.Equal("S09-009", load.RefusalId);
        V31Contract.AssertSentence(new SourceRefusal("S09-009", load.Refusal!, ""));
        Assert.Contains("example-other-note@", load.Refusal, StringComparison.Ordinal);
        V31Contract.AssertSentence(new SourceRefusal("S07-006", load.OperatorDetail!, ""));
        Assert.Contains("example-other-note@", load.OperatorDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAgentVersionWrittenAsText_IsS09009()
    {
        // #1504 ruling 4 (R6): publish writes each requires.agents version as an integer; a document holding text is not
        // publish's (S07-006 for the operator), shown on the job as S09-009.
        var registry = new FakeRegistry();
        var packageRef = registry.PutChanged(Golden(), resolve: r => r["requires"]!["agents"]!["test-json"] = "48");

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load!.Plan);
        Assert.Equal("S09-009", load.RefusalId);
        V31Contract.AssertSentence(new SourceRefusal("S09-009", load.Refusal!, ""));
        V31Contract.AssertSentence(new SourceRefusal("S07-006", load.OperatorDetail!, ""));
    }

    [Theory]
    [InlineData("requires.spec", 32)]
    [InlineData("specVersion", 30)]
    public async Task RequiresSpecOtherThan31_IsS09001(string member, int declared)
    {
        var registry = new FakeRegistry();
        var packageRef = registry.PutChanged(Golden(), resolve: r =>
        {
            if (member == "specVersion")
                r["specVersion"] = declared;
            else
                r["requires"]!["spec"] = declared.ToString(System.Globalization.CultureInfo.InvariantCulture);
        });
        var admission = SpecVersionAdmissionTests.With("31");

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load!.Plan);
        Assert.Equal("S09-001", load.RefusalId);
        Assert.Equal(admission.NotSupported(declared), load.Refusal);
    }

    [Theory]
    [InlineData("requires.spec absent")]
    [InlineData("requires.spec a number")]
    [InlineData("requires.spec not an integer")]
    [InlineData("requires.spec +31")]
    [InlineData("specVersion absent")]
    [InlineData("specVersion text")]
    public async Task ASpecVersionNotDeclaredAsPublishWritesIt_IsS09009(string how)
    {
        var registry = new FakeRegistry();
        var packageRef = registry.PutChanged(Golden(), resolve: r =>
        {
            var requires = r["requires"]!.AsObject();
            switch (how)
            {
                case "requires.spec absent": requires.Remove("spec"); break;
                case "requires.spec a number": requires["spec"] = 31; break;
                case "requires.spec not an integer": requires["spec"] = "thirty-one"; break;
                case "requires.spec +31": requires["spec"] = "+31"; break;
                case "specVersion absent": r.Remove("specVersion"); break;
                default: r["specVersion"] = "31"; break;
            }
        });

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        // Not "31 is not supported" on an engine that runs 31: the document is not what publish wrote.
        Assert.Null(load!.Plan);
        Assert.Equal("S09-009", load.RefusalId);
        V31Contract.AssertSentence(new SourceRefusal("S09-009", load.Refusal!, ""));
        V31Contract.AssertSentence(new SourceRefusal("S07-006", load.OperatorDetail!, ""));
    }

    public static TheoryData<string, string, string?> NotPublishs() => new()
    {
        { PublishedStamp.FileName, "not JSON", null },
        { PublishedStamp.FileName, "empty", null },
        { PublishedStamp.FileName, "not UTF-8", null },
        { CompiledPackage.ResolveFile, "empty", "S09-009" },
        { CompiledPackage.ResolveFile, "not JSON", "S09-009" },
        { CompiledPackage.ResolveFile, "not UTF-8", "S09-009" },
        { ResolveCompiler.LockFile, "not JSON", "S09-014" },
        // Verifies against its own stamp, but is no object: Verify finds no lock member in it (S07-017), so the store's
        // own not-an-object check after Verify is never reached by a stamped pair.
        { CompiledPackage.ResolveFile, "a stamped list", "S09-014" },
    };

    [Theory]
    [MemberData(nameof(NotPublishs))]
    public async Task ContentThatIsNotPublishs_IsNeverThrownOn(string file, string how, string? id)
    {
        var registry = new FakeRegistry();
        var (packageRef, pair) = registry.Put(Golden());
        var path = PathOf(packageRef, file);
        switch (how)
        {
            case "not JSON":
                registry.Blobs[path] = Utf8NoBom.GetBytes("Margaret Okafor's note {");
                break;
            case "empty":
                registry.Blobs[path] = [];
                break;
            case "not UTF-8":
                registry.Blobs[path] = [0x7b, 0xff, 0xfe, 0x7d];
                break;
            default:
                // "[]" stamped as the resolve, its lock linked to it and re-stamped: every hash agrees.
                const string list = "[]";
                registry.Blobs[path] = Utf8NoBom.GetBytes(list);
                var lockDocument = JsonNode.Parse(pair.Lock)!.AsObject();
                lockDocument["resolve"]!["hash"] = CanonicalJson.HashText(list);
                var lockText = CanonicalJson.Write(lockDocument);
                registry.Blobs[PathOf(packageRef, ResolveCompiler.LockFile)] = Utf8NoBom.GetBytes(lockText);
                registry.Blobs[PathOf(packageRef, PublishedStamp.FileName)] =
                    Utf8NoBom.GetBytes(new PublishedStamp(CanonicalJson.HashText(list), CanonicalJson.HashText(lockText)).ToJson());
                break;
        }

        var load = await Store(registry).TryLoadAsync(packageRef, CancellationToken.None);

        if (id == null)
        {
            Assert.Null(load);
            Assert.Equal([PathOf(packageRef, PublishedStamp.FileName)], registry.Reads);
            return;
        }

        Assert.Null(load!.Plan);
        Assert.Equal(id, load.RefusalId);
        V31Contract.AssertSentence(new SourceRefusal(id, load.Refusal!, ""));
        V31Contract.AssertSentence(new SourceRefusal(id == "S09-009" ? "S07-006" : "S07-017", load.OperatorDetail!, ""));
    }

    public static TheoryData<string, string> Lacking() => new()
    {
        { "Scriban ^99.0.0", "S07-003" },
        { "kind teleport", "S07-004" },
        { "agent test-json 99", "S07-005" },
        { "locked before a Scriban break point", "S07-003" },
    };

    [Theory]
    [MemberData(nameof(Lacking))]
    public async Task APackageNeedingWhatTheEngineLacks_IsRefused(string what, string id)
    {
        var registry = new FakeRegistry();
        var engine = ThisEngine.Value;
        WorkflowPackageRef packageRef;
        switch (what)
        {
            case "Scriban ^99.0.0":
                packageRef = registry.PutChanged(Golden(), resolve: r => r["requires"]!["scriban"] = "^99.0.0");
                break;
            case "kind teleport":
                packageRef = registry.PutChanged(Golden(), resolve: r => r["requires"]!["kinds"]!.AsArray().Add("teleport"));
                break;
            case "agent test-json 99":
                packageRef = registry.PutChanged(Golden(), resolve: r => r["requires"]!["agents"]!["test-json"] = 99);
                break;
            default:
                // Locked a patch before this engine's Scriban, and a break point at this engine's version: the range
                // still admits the engine, the break point does not.
                var running = CaretRange.IsVersion(engine.Scriban) ? engine.Scriban : throw new InvalidOperationException(engine.Scriban);
                var parts = running.Split('.');
                Assert.True(int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) > 0, "the test needs a Scriban patch above 0");
                var earlier = $"{parts[0]}.{parts[1]}.{int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) - 1}";
                packageRef = registry.PutChanged(Golden(), resolve: r => r["requires"]!["scriban"] = "^" + earlier,
                    @lock: l => l["libraries"]!["Scriban"] = earlier);
                engine = engine with { BreakPoints = [new BreakPoint("Scriban", running)] };
                break;
        }

        var load = await Store(registry, engine).TryLoadAsync(packageRef, CancellationToken.None);

        Assert.Null(load!.Plan);
        Assert.Equal(id, load.RefusalId);
        V31Contract.AssertSentence(new SourceRefusal(id, load.Refusal!, ""));
        Assert.Contains(load.Refusal!, load.OperatorDetail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusal_IsNotCached()
    {
        var registry = new FakeRegistry();
        var (packageRef, _) = registry.Put(Golden());
        var lockPath = PathOf(packageRef, ResolveCompiler.LockFile);
        registry.Blobs.TryRemove(lockPath, out var lockBytes);
        var store = Store(registry);
        Assert.Equal("A06-004", (await store.TryLoadAsync(packageRef, CancellationToken.None))!.RefusalId);

        registry.Blobs[lockPath] = lockBytes!;
        var load = await store.TryLoadAsync(packageRef, CancellationToken.None);

        Assert.NotNull(load!.Plan);
        Assert.Null(load.Refusal);
    }
}
