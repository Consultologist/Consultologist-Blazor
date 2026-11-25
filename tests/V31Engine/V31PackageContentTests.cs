using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 19; part 09 § 8.2; part 07 §§ 6, 7; D5, D14, D15; Review Focus 4):
/// <c>WorkflowPackages/Current/Content</c> serves a stamped 31 version from its folder (every source file by path, the
/// manifest as stored, <c>resolve.json</c>, <c>lock.json</c> and the publish report parsed from the stored bytes, and
/// <c>MigratedFrom</c> from the stamp), after today's access check; with the preview setting unset a 31 version is S09-001
/// as a 400. <c>WorkflowPackages/Ingest</c> reads a 31 zip read-only: steps 0 to 2 in memory, a refusal as the 400's
/// sentences, S07-016 once when the zip held <c>resolve.json</c> or <c>lock.json</c>. A 5–30 package is answered as before.
/// A version is put in place by the real publish and the real writer over the in-memory registry. Content is fictional.
/// </summary>
public class V31PackageContentTests
{
    private const string AppUserId = "0123456789abcdef0123456789abcdef";
    private const string Kind = SignInKinds.Organisation;
    private const string MigrationReport = """{"newVersion":"v2026.12.1","oldVersion":"v2026.11.1"}""";

    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 30, 0, TimeSpan.Zero);

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly Lazy<EngineCapabilities> ThisEngine = new(() => V31Job.Factory().Current());

    private static readonly PublishedTypeRegistry Types = new(AppContext.BaseDirectory, OutputContractCatalog.ResolveDirectory());

    /// <summary>The engine's limits, its instances raised past the nested note's worst case (R1), as the publisher's tests do.</summary>
    private static readonly EngineLimitSettings Limits = new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["ConsultGeneration:JobInstances"] = "100000" })
        .Build());

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>What a publish stored: the ref, the files written in order and the stamp.</summary>
    private sealed record Stored(WorkflowPackageRef Ref, IReadOnlyList<PublishFile> Files, PublishedStamp Stamp)
    {
        public string Text(string path) => Utf8NoBom.GetString(Files.Single(f => f.Path == path).Bytes);
    }

    /// <summary>
    /// The nested note published by <see cref="PackagePublish.RunAsync"/> (a diagram given, and the operator's migration
    /// report when <paramref name="migrated"/>) and written by <see cref="V31RegistryWriter"/> into <paramref name="registry"/>.
    /// </summary>
    private static async Task<Stored> PublishAsync(FakeRegistry registry, bool migrated = false)
    {
        var options = new PublishOptions(
            Types.Registry, V31Job.Toolchain.Value.Current(), Limits.PublishLimits, V31PackagePublisher.ByApi, new FixedClock(Now),
            Diagram: _ => "flowchart TD\n  plan --> refine\n",
            MigrationReport: migrated ? MigrationReport : null,
            MigratedFrom: migrated ? "v2026.11.1" : null);
        var outcome = await PackagePublish.RunAsync(ResolveFixture.NestedNote(), options);
        Assert.True(outcome.Published, string.Join("\n", outcome.Report.Refusals.Select(r => r.Sentence)));

        var package = JsonNode.Parse(outcome.Compiled!.Resolve)!["package"]!;
        var packageRef = new WorkflowPackageRef((string)package["name"]!, (string)package["version"]!);
        Assert.True(await new V31RegistryWriter(registry).WriteAsync(Kind, packageRef.Name, packageRef.Version, outcome.Files, outcome.Stamp!, CancellationToken.None));
        return new Stored(packageRef, outcome.Files, outcome.Stamp!);
    }

    private static string Canonical(JsonElement element) => CanonicalJson.Write(JsonNode.Parse(element.GetRawText()));

    private static string Canonical(string text) => CanonicalJson.Write(JsonNode.Parse(text));

    private static JsonNode Body(HttpResponseData response) => JsonNode.Parse(FakeHttp.TextOf(response))!;

    private static WorkflowPackageContentResponse ContentOf(HttpResponseData response) =>
        JsonSerializer.Deserialize<WorkflowPackageContentResponse>(FakeHttp.TextOf(response))!;

    // ---- Current/Content ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Content_ServesAStamped31Version_FromItsFolder()
    {
        var rig = new Rig();
        var stored = await PublishAsync(rig.Registry);

        var response = await rig.ContentAsync(stored.Ref);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = ContentOf(response);
        Assert.Equal((stored.Ref.Name, stored.Ref.Version, 31), (content.Name, content.Version, content.SpecVersion));

        // D14: every stored source file by path, and dag.mmd; never manifest.json (it is Manifest), the evidence or the lease.
        var expected = stored.Files
            .Where(f => f.Path != "manifest.json" && (SourceHash.IsSource(f.Path) || f.Path == PackagePublish.DiagramFile))
            .Select(f => f.Path)
            .Order(StringComparer.Ordinal);
        Assert.Equal(expected, content.Files.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("nodes/plan.md", content.Files.Keys);
        Assert.Contains(PackagePublish.DiagramFile, content.Files.Keys);
        foreach (var (path, text) in content.Files)
            Assert.Equal(stored.Text(path), text);
        Assert.True(rig.Registry.Blobs.ContainsKey(V31RegistryWriter.LeasePath(stored.Ref.Name, stored.Ref.Version)));

        // The manifest member for member, as stored: the null the author wrote is there, nothing is added.
        Assert.Equal(Canonical(stored.Text("manifest.json")), Canonical(content.Manifest));
        Assert.Equal(JsonValueKind.Null, content.Manifest.GetProperty("derivedFrom").ValueKind);

        // The evidence parsed from the stored bytes; resolve.json's canonical text hashes to the stamp's resolveHash.
        Assert.Equal(stored.Text(CompiledPackage.ResolveFile), Canonical(content.Resolve!.Value));
        Assert.Equal(stored.Stamp.ResolveHash, CanonicalJson.HashText(Canonical(content.Resolve.Value)));
        Assert.Equal(stored.Text(ResolveCompiler.LockFile), Canonical(content.Lock!.Value));
        Assert.Equal(stored.Stamp.LockHash, CanonicalJson.HashText(Canonical(content.Lock.Value)));
        Assert.Equal(Canonical(stored.Text(PublishReport.FileName)), Canonical(content.PublishReport!.Value));
        Assert.Null(content.MigratedFrom);
        Assert.Null(content.Notices);
        await rig.Store.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task Content_OfAVersionTheOperatorMigrated_SaysWhichVersionItCameFrom()
    {
        var rig = new Rig();
        var stored = await PublishAsync(rig.Registry, migrated: true);
        Assert.Contains(stored.Files, f => f.Path == PackagePublish.MigrationReportFile);

        var content = ContentOf(await rig.ContentAsync(stored.Ref));

        Assert.Equal("v2026.11.1", content.MigratedFrom);
        Assert.DoesNotContain(PackagePublish.MigrationReportFile, content.Files.Keys);
    }

    [Fact]
    public async Task Content_OfAnotherAccountsPackage_IsTodaysForbiddenAnswer_AndReadsNothing()
    {
        var rig = new Rig();
        var foreign = new WorkflowPackageRef("acct-fedcba987654", "v2026.12.1");
        rig.Ownership.OwnsAsync(AppUserId, foreign.Name, Arg.Any<CancellationToken>()).Returns(false);

        var response = await rig.ContentAsync(foreign);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Workflow package is not accessible from this account.", (string)Body(response)["error"]!);
        Assert.Empty(rig.Registry.Reads);
        await rig.Store.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);
    }

    [Fact]
    public async Task Content_OfAnUnstampedFolder_IsNotServed()
    {
        // A publish that died before its stamp: the folder does not exist for any reader. The compiled load passes it to
        // today's path, whose reader refuses 31; with 31 admitted that is A06-004.
        var rig = new Rig();
        var stored = await PublishAsync(rig.Registry);
        rig.Registry.Blobs.TryRemove(FakeRegistry.PathOf(stored.Ref, PublishedStamp.FileName), out _);
        rig.Store.ResolveAsync(stored.Ref, Arg.Any<CancellationToken>())
            .ThrowsAsync(new WorkflowPackageSpecVersionException(stored.Ref.ToString(), 31, WorkflowPackageStore.SupportedSpecVersions));

        var response = await rig.ContentAsync(stored.Ref);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CompiledPackageStore.NotPublished(stored.Ref.Name, stored.Ref.Version), (string)Body(response)["error"]!);
        Assert.Null(Body(response)["Files"]);
    }

    [Fact]
    public async Task Content_WithThePreviewUnset_AnswersA31VersionWithS09001_As400()
    {
        // Review Focus 4: the compiled load reads nothing; today's reader refuses 31, which is S09-001, never a 500.
        var rig = new Rig(preview: null);
        var stored = await PublishAsync(rig.Registry);
        rig.Registry.ClearReads();
        rig.Store.ResolveAsync(stored.Ref, Arg.Any<CancellationToken>()).Returns<WorkflowPackage>(_ =>
            throw Assert.Throws<WorkflowPackageSpecVersionException>(() => WorkflowPackageManifestJson.Read(
                stored.Text("manifest.json"), stored.Ref.ToString(), WorkflowPackageStore.SupportedSpecVersions)));

        var response = await rig.ContentAsync(stored.Ref);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (string)Body(response)["error"]!;
        Assert.Equal(SpecVersionAdmissionTests.With(null).NotSupported(31), error);
        V31Contract.AssertSentence(new SourceRefusal("S09-001", error, ""));
        Assert.Empty(rig.Registry.Reads);
    }

    [Fact]
    public async Task Content_AnswersALoadRefusal_WithItsSentence_As400()
    {
        var rig = new Rig();
        var stored = await PublishAsync(rig.Registry);
        var path = FakeRegistry.PathOf(stored.Ref, CompiledPackage.ResolveFile);
        rig.Registry.Blobs[path] = Utf8NoBom.GetBytes(Utf8NoBom.GetString(rig.Registry.Blobs[path]).Replace("Sections", "Sectionz", StringComparison.Ordinal));

        var response = await rig.ContentAsync(stored.Ref);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CompiledPackageStore.HashMismatch(stored.Ref.Name, stored.Ref.Version), (string)Body(response)["error"]!);
    }

    [Fact]
    public async Task Content_WithThePreviewSet_AnswersA31VersionWhoseStampCouldNotBeRead_503()
    {
        var rig = new Rig();
        var stored = await PublishAsync(rig.Registry);
        rig.Registry.ReadFailure = new Azure.RequestFailedException(503, "unavailable");
        rig.Store.ResolveAsync(stored.Ref, Arg.Any<CancellationToken>())
            .ThrowsAsync(new WorkflowPackageSpecVersionException(stored.Ref.ToString(), 31, WorkflowPackageStore.SupportedSpecVersions));

        var response = await rig.ContentAsync(stored.Ref);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Workflow package registry is unavailable.", (string)Body(response)["error"]!);
    }

    [Fact]
    public async Task Content_Below31_IsAsBefore_ButForTheNewNullMembers()
    {
        var rig = new Rig();
        var packageRef = new WorkflowPackageRef("general", "v2026.08.1");
        var manifest = V7Fixtures.Minimal();
        var files = V6Fixtures.Files(manifest);
        var data = WorkflowDataResolver.Resolve(manifest, files, new List<string>());
        rig.Store.ResolveAsync(packageRef, Arg.Any<CancellationToken>()).Returns(new WorkflowPackage(
            manifest, Nodes: manifest.Nodes, Data: data, ResultNodeId: "assemble-note", Results: [new("consult", "assemble-note", "Assemble note")],
            SourceFiles: files));

        var response = await rig.ContentAsync(packageRef);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = Body(response).AsObject();
        Assert.Equal(
            ["Name", "Version", "SpecVersion", "Manifest", "Files", "Resolve", "Lock", "PublishReport", "MigratedFrom", "Notices"],
            body.Select(member => member.Key));
        Assert.All(["Resolve", "Lock", "PublishReport", "MigratedFrom", "Notices"], name => Assert.Null(body[name]));
        var before = JsonNode.Parse(JsonSerializer.Serialize(WorkflowPackageContentResponse.For(manifest, files)))!.AsObject();
        foreach (var name in new[] { "Name", "Version", "SpecVersion", "Manifest", "Files" })
            Assert.True(JsonNode.DeepEquals(before[name], body[name]), name);
        Assert.Equal([FakeRegistry.PathOf(packageRef, PublishedStamp.FileName)], rig.Registry.Reads);
    }

    // ---- Ingest -----------------------------------------------------------------------------------------------------------

    private static byte[] Zip(IReadOnlyDictionary<string, byte[]> files)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in files.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                using var entry = archive.CreateEntry(path).Open();
                entry.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public async Task Ingest_ReadsA31Zip_ReadOnly()
    {
        var rig = new Rig();
        var upload = ResolveFixture.NestedNote();

        var response = await rig.IngestAsync(Zip(upload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ingested = JsonSerializer.Deserialize<WorkflowPackageIngestResponse>(FakeHttp.TextOf(response))!;
        var content = ingested.Content;
        Assert.Equal(31, content.SpecVersion);
        var manifest = SourceFixture.Text(upload, "manifest.json");
        Assert.Equal((string)JsonNode.Parse(manifest)!["name"]!, content.Name);
        Assert.Equal(Canonical(manifest), Canonical(content.Manifest));
        Assert.Equal(upload.Keys.Where(p => p != "manifest.json").Order(StringComparer.Ordinal), content.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, text) in content.Files)
            Assert.Equal(SourceFixture.Text(upload, path), text);
        Assert.Null(content.Resolve);
        Assert.Null(content.Lock);
        Assert.Null(content.PublishReport);
        Assert.Null(content.MigratedFrom);
        Assert.Null(content.Notices);
        Assert.Empty(ingested.Findings);
        Assert.Empty(rig.Registry.Changes);
    }

    public static TheoryData<string[]> Evidence() => [["resolve.json"], ["lock.json"], ["resolve.json", "lock.json", "publish-report.json", "publish.json"]];

    [Theory]
    [MemberData(nameof(Evidence))]
    public async Task Ingest_OfA31ZipHoldingResolveOrLock_SaysS07016Once_AndDropsThem(string[] evidence)
    {
        var rig = new Rig();
        var upload = ResolveFixture.NestedNote();
        foreach (var path in evidence)
            upload.With(path, """{"forged":true}""");

        var content = (await rig.IngestContentAsync(Zip(upload)))!;

        var notice = Assert.Single(content.Notices!);
        Assert.Equal("This package's resolve.json and lock.json were ignored; publishing writes new ones.", notice);
        V31Contract.AssertSentence(new SourceRefusal("S07-016", notice, ""));
        Assert.All(evidence, path => Assert.DoesNotContain(path, content.Files.Keys));
        Assert.Null(content.Resolve);
        Assert.Null(content.Lock);
        Assert.Null(content.PublishReport);
    }

    [Theory]
    [InlineData("publish-report.json")]
    [InlineData("publish.json")]
    [InlineData("dag.mmd")]
    public async Task Ingest_OfA31ZipHoldingOtherEvidenceOrTheDiagram_SaysNothing_AndKeepsNoneOfIt(string path)
    {
        var rig = new Rig();
        var upload = ResolveFixture.NestedNote().With(path, path == "dag.mmd" ? "flowchart TD\n" : """{"forged":true}""");

        var content = (await rig.IngestContentAsync(Zip(upload)))!;

        Assert.Null(content.Notices);
        Assert.DoesNotContain(path, content.Files.Keys);
        Assert.Null(content.PublishReport);
    }

    [Fact]
    public async Task Ingest_OfA31ZipTheSourceCheckRefuses_Is400_WithItsSentences()
    {
        var rig = new Rig();
        var upload = ResolveFixture.NestedNote().Without("nodes/plan.md");
        var expected = SourceCheck.Run(upload, Types.Registry).Refusals.Select(r => r.Sentence).ToList();
        Assert.NotEmpty(expected);

        var response = await rig.IngestAsync(Zip(upload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = Body(response);
        Assert.Equal(expected, body["errors"]!.AsArray().Select(e => (string)e!));
        Assert.Equal(expected[0], (string)body["error"]!);
    }

    [Fact]
    public async Task Ingest_OfA31ZipWithAnEntryThatIsNotUtf8_IsIntakesSentence()
    {
        var rig = new Rig();
        var upload = ResolveFixture.NestedNote();
        upload["nodes/plan.md"] = [0x2D, 0x2D, 0x2D, 0xFF, 0xFE, 0x0A];

        var response = await rig.IngestAsync(Zip(upload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("File 'nodes/plan.md' is not valid UTF-8; save it as UTF-8.", (string)Body(response)["error"]!);
    }

    [Fact]
    public async Task Ingest_OfA31Zip_WithThePreviewUnset_IsS09001_As400()
    {
        var rig = new Rig(preview: null);

        var response = await rig.IngestAsync(Zip(ResolveFixture.NestedNote()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = (string)Body(response)["error"]!;
        Assert.Equal(SpecVersionAdmissionTests.With(null).NotSupported(31), error);
        V31Contract.AssertSentence(new SourceRefusal("S09-001", error, ""));
    }

    [Theory]
    [InlineData("31")]
    [InlineData(null)]
    public async Task Ingest_OfA5To30Zip_IsUnchanged(string? preview)
    {
        var manifest = V7Fixtures.Minimal();
        var files = new Dictionary<string, string>(V6Fixtures.Files(manifest), StringComparer.Ordinal)
        {
            ["manifest.json"] = JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions),
        };
        var zip = WorkflowPackageArchive.Zip(files);

        var today = new WorkflowPackageIngestor(OutputContractCatalog.Load()).Ingest(zip);
        var response = await new Rig(preview).IngestAsync(zip);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var expected = JsonNode.Parse(JsonSerializer.Serialize(new WorkflowPackageIngestResponse(today.Content!, today.Findings)));
        Assert.True(JsonNode.DeepEquals(expected, Body(response)), FakeHttp.TextOf(response));
        Assert.Equal(manifest.SpecVersion, (int)Body(response)["Content"]!["SpecVersion"]!);
    }

    /// <summary>The endpoints over the in-memory registry, the real compiled store and ingestor, and a substituted 5–30 store.</summary>
    private sealed class Rig
    {
        public Rig(string? preview = "31")
        {
            var admission = SpecVersionAdmissionTests.With(preview);
            var compiled = new CompiledPackageStore(Registry, Registry, ThisEngine, admission, new EngineLimitSettings(new ConfigurationBuilder().Build()));
            var authorizer = Substitute.For<IAccountAuthorizer>();
            authorizer.AuthorizeAsync(Arg.Any<HttpRequestData>(), Arg.Any<CancellationToken>()).Returns(AdminDrainTests.Admin.Account(AppUserId));
            Functions = new WorkflowPackages(
                Store, Substitute.For<IWorkflowPackagePinResolver>(), publisher: null!,
                new WorkflowPackageIngestor(OutputContractCatalog.Load(), admission, Types), lineage: null!, containerFactory: null!,
                authorizer, Ownership, NullLogger<WorkflowPackages>.Instance, compiled, admission, Registry);
        }

        public FakeRegistry Registry { get; } = new();

        public IWorkflowPackageStore Store { get; } = Substitute.For<IWorkflowPackageStore>();

        public IWorkflowPackageOwnership Ownership { get; } = Substitute.For<IWorkflowPackageOwnership>();

        public WorkflowPackages Functions { get; }

        public async Task<HttpResponseData> ContentAsync(WorkflowPackageRef packageRef)
        {
            Registry.ClearReads();
            return await Functions.GetCurrentContentAsync(FakeHttp.Request(
                "GET", null, new Uri($"https://api.example.test/api/WorkflowPackages/Current/Content?ref={packageRef}")));
        }

        public Task<HttpResponseData> IngestAsync(byte[] zip) =>
            Functions.IngestAsync(FakeHttp.RequestWithBytes("POST", zip, new Uri("https://api.example.test/api/WorkflowPackages/Ingest")));

        public async Task<WorkflowPackageContentResponse?> IngestContentAsync(byte[] zip)
        {
            var response = await IngestAsync(zip);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonSerializer.Deserialize<WorkflowPackageIngestResponse>(FakeHttp.TextOf(response))?.Content;
        }
    }
}
