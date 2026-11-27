using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Diagram;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Publish;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using PublishedTypeRegistry = Consultologist.Api.V31.PublishedTypeRegistry;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 16; part 07 § 5; D2 to D5): <c>WorkflowPackages/Publish</c>'s 31 branch
/// (<see cref="WorkflowPackagePublisher"/> into <see cref="V31PackagePublisher"/>) over substitutes for the registry, the
/// settings, the ownership record and the operator's alerts, on the nested-note package with one passing test. With the
/// preview setting unset a 31 publish answers S09-001 and touches nothing. Content is fictional (Margaret Okafor).
/// </summary>
public class V31PackagePublisherTests
{
    private const string OwnerId = "0123456789abcdef0123456789abcdef";
    private const string AccountName = "acct-0123456789ab";
    private const string OperatorId = "fedcba9876543210fedcba9876543210";
    private const string Kind = SignInKinds.Organisation;
    private const string TestPath = "tests/snapshot.json";

    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 30, 0, TimeSpan.Zero);

    private static readonly Lazy<Task<Dictionary<string, string>>> Package = new(() => WithTest());

    /// <summary>The nested note with <c>tests/snapshot.json</c> (its own offline run), as the request's text files.</summary>
    private static async Task<Dictionary<string, string>> WithTest(Action<JsonObject>? change = null)
    {
        var upload = ResolveFixture.NestedNote();
        var plan = RunFixture.Plan(upload);
        var request = SnapshotTest.Fictional(plan);
        var file = SnapshotTest.Write(plan, request, await OfflineRun.RunAsync(plan, request));
        change?.Invoke(file);
        var files = upload.With("tests/index.json", Index(["snapshot"])).With(TestPath, file.ToJsonString());
        return files.ToDictionary(f => f.Key, f => new UTF8Encoding(false, true).GetString(f.Value), StringComparer.Ordinal);
    }

    private static async Task<Dictionary<string, string>> Files() => new(await Package.Value, StringComparer.Ordinal);

    private static string Text(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);

    /// <summary>The publisher and its substitutes: nothing published yet, every write wins, the owner owns nothing.</summary>
    private sealed class Rig
    {
        public IV31RegistryWriter Registry { get; } = Substitute.For<IV31RegistryWriter>();
        public IWorkflowPackageRegistryWriter Writer { get; } = Substitute.For<IWorkflowPackageRegistryWriter>();
        public IAccountSettingsStore Settings { get; } = Substitute.For<IAccountSettingsStore>();
        public IWorkflowPackageOwnership Ownership { get; } = Substitute.For<IWorkflowPackageOwnership>();
        public IWorkflowPackageStore Store { get; } = Substitute.For<IWorkflowPackageStore>();
        public IOperatorAlerts Alerts { get; } = Substitute.For<IOperatorAlerts>();
        public CapturingLogger<V31PackagePublisher> Log { get; } = new();
        /// <summary>
        /// The engine's limits, its instances raised: the nested note's worst case (80,614 instances, its loops unbounded by
        /// <c>max</c>) passes the default 10,000, which A11-001 refuses (R1).
        /// </summary>
        public EngineLimitSettings Limits { get; init; } = new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConsultGeneration:JobInstances"] = "100000" })
            .Build());
        public PublishedTypeRegistry Types { get; } = new(AppContext.BaseDirectory, OutputContractCatalog.ResolveDirectory());

        /// <summary>What each <see cref="IV31RegistryWriter.WriteAsync"/> was given, in order.</summary>
        public List<(string Version, IReadOnlyList<PublishFile> Files, PublishedStamp Stamp)> Writes { get; } = [];

        /// <summary>The options each run of the pipeline was given.</summary>
        public List<PublishOptions> Options { get; } = [];

        /// <summary>Stands in for <see cref="PackagePublish.RunAsync"/> when set.</summary>
        public Func<IReadOnlyDictionary<string, byte[]>, PublishOptions, Task<PublishOutcome>>? Pipeline { get; set; }

        public Queue<bool> WriteResults { get; } = new();

        public Rig()
        {
            // NSubstitute answers "" for an unstubbed string: the name has no latest pointer
            Writer.ReadLatestVersionAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);
            Registry.WriteAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<PublishFile>>(),
                    Arg.Any<PublishedStamp>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Writes.Add(((string)call[2], (IReadOnlyList<PublishFile>)call[3], (PublishedStamp)call[4]));
                    return WriteResults.Count > 0 ? WriteResults.Dequeue() : true;
                });
        }

        public WorkflowPackagePublisher Publisher(string? preview = "31")
        {
            var v31 = new V31PackagePublisher(
                Registry, Writer, Settings, Ownership, Types, V31Job.Toolchain.Value, Limits, Alerts,
                new FixedTimeProvider(Now), Log, new HashSet<string>(StringComparer.Ordinal) { OperatorId })
            {
                Pipeline = (upload, options) =>
                {
                    Options.Add(options);
                    return Pipeline?.Invoke(upload, options) ?? PackagePublish.RunAsync(upload, options);
                },
            };
            return new WorkflowPackagePublisher(
                Store, Writer, Settings, OutputContractCatalog.Load(), new FixedTimeProvider(Now),
                NullLogger<WorkflowPackagePublisher>.Instance, Ownership, FormatCatalogLoader.Load(),
                SpecVersionAdmissionTests.With(preview), v31);
        }

        /// <summary>The stored files of the write that won.</summary>
        public IReadOnlyList<PublishFile> Stored => Writes[^1].Files;

        public void AssertNothingWritten()
        {
            Assert.Empty(Writes);
            Writer.DidNotReceiveWithAnyArgs().SetLatestPointerAsync(default, default!, default!, default);
            Writer.DidNotReceiveWithAnyArgs().UploadFileAsync(default, default!, default!, default!, default!, default);
            Writer.DidNotReceiveWithAnyArgs().CreateManifestAsync(default, default!, default!, default!, default);
            Settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default!, default);
        }
    }

    private static WorkflowPackagePublishRequest Request(
        Dictionary<string, string> files, string? source = null, string? target = null, string? slug = null,
        bool fromUpload = false, JsonElement? migration = null) =>
        new(source, Manifest: null, Files: files, Target: target, NewPackageSlug: slug, FromUpload: fromUpload, MigrationReport: migration);

    private static JsonObject Manifest(IReadOnlyList<PublishFile> files) =>
        JsonNode.Parse(Text(files.Single(f => f.Path == "manifest.json").Bytes))!.AsObject();

    // ---- not admitted -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WithThePreviewSettingUnset_A31Publish_AnswersS09001_AndReadsAndWritesNothing()
    {
        var rig = new Rig();

        var result = await rig.Publisher(preview: null).PublishAsync(OwnerId, Kind, Request(await Files(), slug: "breast"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Forbidden);
        Assert.Equal([SpecVersionAdmissionTests.With(null).NotSupported(31)], result.Errors);
        Assert.Null(result.Report);
        Assert.Empty(rig.Options);
        Assert.Empty(rig.Registry.ReceivedCalls());
        Assert.Empty(rig.Writer.ReceivedCalls());
        Assert.Empty(rig.Settings.ReceivedCalls());
        Assert.Empty(rig.Ownership.ReceivedCalls());
        Assert.Empty(rig.Store.ReceivedCalls());
        Assert.Empty(rig.Alerts.ReceivedCalls());
    }

    [Theory]
    [InlineData("{\"specVersion\": 30}")]
    [InlineData("{\"specVersion\": \"31\"}")]
    [InlineData("{\"specVersion\": 31.5}")]
    [InlineData("[31]")]
    [InlineData("not json")]
    public void OnlyAManifestFileWhoseSpecVersionIsTheInteger31_TakesThe31Branch(string manifest)
    {
        Assert.True(V31PackagePublisher.IsV31(new Dictionary<string, string> { ["manifest.json"] = "{\"specVersion\": 31}" }));
        Assert.False(V31PackagePublisher.IsV31(new Dictionary<string, string> { ["manifest.json"] = manifest }));
        Assert.False(V31PackagePublisher.IsV31(new Dictionary<string, string> { ["nodes/index.json"] = "{\"specVersion\": 31}" }));
        Assert.False(V31PackagePublisher.IsV31(null));
    }

    // ---- published ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APassingPackage_IsPublished_UnderTheDerivedName_WithTheAssignedNameAndVersionInItsManifest()
    {
        var rig = new Rig();
        var files = await Files();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(files), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var response = result.Response!;
        Assert.Equal((AccountName, "v2026.12.1", $"{AccountName}@v2026.12.1"), (response.Name, response.Version, response.Ref));

        // D2: name and version replaced in place; every other member, its order and its nulls as uploaded
        var uploaded = JsonNode.Parse(files["manifest.json"])!.AsObject();
        var stored = Manifest(rig.Stored);
        Assert.Equal(uploaded.Select(m => m.Key), stored.Select(m => m.Key));
        Assert.Equal((AccountName, "v2026.12.1"), ((string?)stored["name"], (string?)stored["version"]));
        foreach (var (key, value) in uploaded.Where(m => m.Key is not ("name" or "version")))
            Assert.True(JsonNode.DeepEquals(value, stored[key]), $"manifest member '{key}' changed");
        Assert.Contains(uploaded, m => m.Value is null);

        // every other source file as uploaded; the report, the response's Report, and the stamp agree
        foreach (var (path, text) in files.Where(f => f.Key != "manifest.json"))
            Assert.Equal(text, Text(rig.Stored.Single(f => f.Path == path).Bytes));
        var report = JsonNode.Parse(Text(rig.Stored.Single(f => f.Path == PublishReport.FileName).Bytes))!;
        Assert.Equal("published", (string?)report["outcome"]);
        Assert.Equal(AccountName, (string?)report["package"]!["name"]);
        Assert.Equal("v2026.12.1", (string?)report["package"]!["version"]);
        Assert.True(JsonNode.DeepEquals(report, JsonNode.Parse(response.Report!.Value.GetRawText())));
        Assert.Equal(new PublishedStamp((string)report["resolveHash"]!, (string)report["lockHash"]!), rig.Writes[^1].Stamp);
        Assert.Equal(
            report["warnings"]!.AsArray().Select(w => (string)w!["sentence"]!),
            response.Warnings);
        Assert.NotEmpty(response.Warnings);
        await rig.Ownership.Received(1).RecordAsync(OwnerId, AccountName, Arg.Any<CancellationToken>());
        await rig.Registry.Received(1).WriteAsync(Kind, AccountName, "v2026.12.1", Arg.Any<IReadOnlyList<PublishFile>>(), Arg.Any<PublishedStamp>(), Arg.Any<CancellationToken>());
        Assert.Empty(rig.Alerts.ReceivedCalls());
    }

    [Fact]
    public async Task APublish_SetsLatestAndFlipsThePin_AfterTheStamp()
    {
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Received.InOrder(() =>
        {
            rig.Ownership.RecordAsync(OwnerId, AccountName, Arg.Any<CancellationToken>());
            rig.Registry.WriteAsync(Kind, AccountName, "v2026.12.1", Arg.Any<IReadOnlyList<PublishFile>>(), Arg.Any<PublishedStamp>(), Arg.Any<CancellationToken>());
            rig.Writer.SetLatestPointerAsync(Kind, AccountName, "v2026.12.1", Arg.Any<CancellationToken>());
            rig.Settings.SaveAsync(OwnerId, WorkflowPackagePinResolver.PackagePinSettingKey, $"{AccountName}@v2026.12.1", "text/plain", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task ThePipeline_IsGivenTheEnginesRegistryToolchainLimitsAndClock_AndTheDiagram()
    {
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var options = Assert.Single(rig.Options);
        Assert.Same(rig.Types.Registry, options.Registry);
        var current = V31Job.Toolchain.Value.Current();
        Assert.Equal(current with { Libraries = options.Toolchain.Libraries }, options.Toolchain);
        Assert.Equal(current.Libraries, options.Toolchain.Libraries);
        Assert.StartsWith("package-format@v", options.Toolchain.PackageFormat, StringComparison.Ordinal);
        Assert.StartsWith("formats@v", options.Toolchain.FormatsCatalogue, StringComparison.Ordinal);
        Assert.StartsWith("agents@v", options.Toolchain.AgentsCatalogue, StringComparison.Ordinal);
        Assert.Null(options.Toolchain.OutputContracts);
        Assert.Equal(rig.Limits.PublishLimits, options.Limits);
        Assert.Equal(rig.Limits.RegexLimit, options.MatchTimeLimit);
        Assert.Equal(Now, options.Clock.GetUtcNow());
        // #1510 (area 13, D13): publish writes dag.mmd, drawn by the library's one drawing.
        Assert.NotNull(options.Diagram);
        Assert.Null(options.Templates);
        Assert.Null(options.MigrationReport);
        Assert.Null(options.MigratedFrom);
        Assert.Contains(rig.Stored, f => f.Path == PackagePublish.DiagramFile);
    }

    [Fact]
    public async Task APublishedPackage_HoldsItsDiagram()
    {
        // #1510 (area 13, D13; part 07 §§ 5, 7): the stored dag.mmd is the drawing of the stored resolve.json, written
        // before lock.json; it is a view, so the stamp's hashes are the pair's alone.
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var resolve = Text(rig.Stored.Single(f => f.Path == CompiledPackage.ResolveFile).Bytes);
        var diagram = Text(rig.Stored.Single(f => f.Path == PackagePublish.DiagramFile).Bytes);
        Assert.Equal(PackageDiagram.Generate(resolve), diagram);
        Assert.StartsWith("---\nconfig:\n  layout: elk\n---\nflowchart TD\n", diagram, StringComparison.Ordinal);
        var paths = rig.Stored.Select(f => f.Path).ToList();
        Assert.True(paths.IndexOf(PackagePublish.DiagramFile) < paths.IndexOf(ResolveCompiler.LockFile), "dag.mmd is written before lock.json");
        Assert.Equal(CanonicalJson.HashText(resolve), rig.Writes[^1].Stamp.ResolveHash);
    }

    [Fact]
    public async Task TheEnginesInstancesLimit_ReachesThePipeline_AndRefusesAWorstCasePastIt()
    {
        var rig = new Rig { Limits = new EngineLimitSettings(new ConfigurationBuilder().Build()) };

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.StartsWith("Up to 80,614 steps could run in one job, through node.sections; this engine runs at most 10,000", result.Errors[0], StringComparison.Ordinal);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task APublish_RunsItsTestsAtTheEngineConfiguredLimits()
    {
        var unset = new Rig();
        var passed = await unset.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);
        Assert.True(passed.Succeeded, string.Join(" | ", passed.Errors));
        Assert.Equal(unset.Limits.Limits, Assert.Single(unset.Options).RunLimits);

        var rig = new Rig
        {
            Limits = new EngineLimitSettings(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConsultGeneration:JobInstances"] = "100000",
                    ["ConsultGeneration:JobAgentCalls"] = "1",
                })
                .Build()),
        };

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.Equal(1, Assert.Single(rig.Options).RunLimits!.JobAgentCalls);
        Assert.False(result.Succeeded);
        rig.AssertNothingWritten();
    }

    // ---- where it goes, and who may ----------------------------------------------------------------------------------

    [Fact]
    public async Task ANewPackageSlug_PublishesUnderTheAccountsNameForIt()
    {
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files(), slug: "oncology/breast"), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var name = WorkflowPackageNaming.ForAccount(OwnerId, "oncology/breast");
        Assert.Equal(name, result.Response!.Name);
        Assert.Equal(name, (string?)Manifest(rig.Stored)["name"]);
    }

    [Fact]
    public async Task ATargetTheAccountDoesNotOwn_IsForbiddenAsToday_AndNothingIsRun()
    {
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files(), target: "acct-ffffffffffff"), CancellationToken.None);

        Assert.True(result.Forbidden);
        Assert.Equal(["Target package is not owned by this account."], result.Errors);
        Assert.Empty(rig.Options);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task ASourceTheAccountCannotAccess_IsForbiddenAsToday()
    {
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files(), source: "acct-ffffffffffff@v2026.11.1"), CancellationToken.None);

        Assert.True(result.Forbidden);
        Assert.Equal(["Source package is not accessible from this account."], result.Errors);
        Assert.Empty(rig.Options);
        rig.AssertNothingWritten();
    }

    [Theory]
    [InlineData(null, false, "api")]
    [InlineData("general@v2026.08.1", true, "editor")]
    public async Task PublishedBy_IsTheEditorWhenTheRequestCarriesItsSource_ElseTheApi(string? source, bool fromUpload, string publishedBy)
    {
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files(), source: source, fromUpload: fromUpload), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(publishedBy, Assert.Single(rig.Options).PublishedBy);
        Assert.Equal(publishedBy, (string?)JsonNode.Parse(result.Response!.Report!.Value.GetRawText())!["publishedBy"]);
    }

    private static JsonElement Migration(string text = """{"oldVersion":"v2026.01.1","newVersion":"v2026.12.1","package":"example-minimal"}""") =>
        JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public async Task AMigrationReportFromACallerWhoIsNotTheOperator_IsForbiddenAsToday_AndNothingIsReadOrRun()
    {
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files(), migration: Migration()), CancellationToken.None);

        Assert.True(result.Forbidden);
        Assert.Single(result.Errors);
        Assert.Empty(rig.Options);
        Assert.Empty(rig.Writer.ReceivedCalls());
        Assert.Empty(rig.Ownership.ReceivedCalls());
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task AMigrationReportFromTheOperator_IsStored_AndTheStampNamesTheOldVersion_PublishedByTheOperatorsMigration()
    {
        var rig = new Rig();
        var migration = Migration();

        var result = await rig.Publisher().PublishAsync(OperatorId, Kind, Request(await Files(), migration: migration), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        var (_, files, stamp) = rig.Writes[^1];
        Assert.Equal("v2026.01.1", stamp.MigratedFrom);
        Assert.Equal(migration.GetRawText(), Text(files.Single(f => f.Path == PackagePublish.MigrationReportFile).Bytes));
        Assert.Equal("operator-migration", Assert.Single(rig.Options).PublishedBy);
    }

    [Theory]
    [InlineData("""{"newVersion":"v2026.12.1"}""")]
    [InlineData("""{"oldVersion":3,"newVersion":"v2026.12.1"}""")]
    [InlineData("""["v2026.01.1"]""")]
    public async Task AMigrationReportWithoutItsVersionsAsText_IsRefused_AndNothingIsRun(string text)
    {
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OperatorId, Kind, Request(await Files(), migration: Migration(text)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Forbidden);
        Assert.Single(result.Errors);
        Assert.Empty(rig.Options);
        rig.AssertNothingWritten();
    }

    [Theory]
    [InlineData("""{"oldVersion":"v2026.01.1","newVersion":"v2026.12.1","package":"example-minimal","package":"example-minimal"}""")]
    [InlineData("""{"oldVersion":"v2026.01.1","newVersion":"v2026.11.1","newVersion":"v2026.12.1"}""")]
    public async Task AMigrationReportWithAMemberWrittenTwice_IsRefused_NotThrown_AndNothingIsRun(string text)
    {
        // Final review of Part F, item 6: the report's newVersion is rewritten through JsonNode, which throws on a member
        // written twice; such a report is refused as one that does not read, a 400, never a 500.
        var rig = new Rig();

        var result = await rig.Publisher().PublishAsync(OperatorId, Kind, Request(await Files(), migration: Migration(text)), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Forbidden);
        Assert.Equal(["MigrationReport must be a JSON object with each member written once."], result.Errors);
        Assert.Empty(rig.Options);
        rig.AssertNothingWritten();
    }

    // ---- refused ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AManifestWithoutTextVersion_IsRefusedWithA11004_WithTheReport_AndNothingWritten()
    {
        var rig = new Rig();
        var files = await Files();
        var manifest = JsonNode.Parse(files["manifest.json"])!.AsObject();
        manifest["version"] = 3;
        files["manifest.json"] = manifest.ToJsonString();

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(files), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(result.Forbidden);
        Assert.Equal(PackagePublishSentences.ManifestNameAndVersion().Sentence, result.Errors[0]);
        var report = JsonNode.Parse(result.Report!.Value.GetRawText())!;
        Assert.Equal("refused", (string?)report["outcome"]);
        Assert.Equal("manifest.json", (string?)report["refusals"]![0]!["at"]);
        rig.AssertNothingWritten();
    }

    [Fact]
    public async Task AFailingTest_IsRefused_WithTheReport_NothingWritten_AndTheOperatorAlerted()
    {
        var rig = new Rig();
        var files = await WithTest(f => f["expect"]!.AsObject()["ran"] = new JsonArray("node.nope"));

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(files), CancellationToken.None);

        Assert.False(result.Succeeded);
        var report = JsonNode.Parse(result.Report!.Value.GetRawText())!;
        Assert.Equal("refused", (string?)report["outcome"]);
        Assert.Equal(report["refusals"]!.AsArray().Select(r => (string)r!["sentence"]!), result.Errors);
        Assert.StartsWith("Test 'snapshot' failed", result.Errors[0], StringComparison.Ordinal);
        rig.AssertNothingWritten();
        rig.Ownership.DidNotReceiveWithAnyArgs().RecordAsync(default!, default!, default);
        await rig.Alerts.Received(1).RaiseAsync(
            new OperatorAlert(OperatorAlertKinds.PublishFailed, null, AccountName, 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnEngineFault_IsLoggedByItsTypeOnly_NeverReturned()
    {
        var rig = new Rig
        {
            Pipeline = (upload, options) => Task.FromResult(new PublishOutcome(
                new PublishReport(AccountName, "v2026.12.1", false, null, options.PublishedBy, SourceHash.Of(new Dictionary<string, string>()),
                    null, null, [], [PackagePublishSentences.EngineFailed(nameof(InvalidOperationException)), PackagePublishSentences.EngineFailed("S07-006")], [], []),
                [], null, null, [])),
        };

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(PackagePublishSentences.EngineFailed().Sentence, result.Errors[0]);
        Assert.DoesNotContain(nameof(InvalidOperationException), result.Report!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Errors, e => e.Contains(nameof(InvalidOperationException), StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains($"Fault={nameof(InvalidOperationException)}", StringComparison.Ordinal));
        Assert.Contains(rig.Log.Entries, e => e.Message.Contains("Fault=S07-006", StringComparison.Ordinal));
        await rig.Alerts.Received(1).RaiseAsync(
            new OperatorAlert(OperatorAlertKinds.PublishFailed, null, AccountName, 2), Arg.Any<CancellationToken>());
        rig.AssertNothingWritten();
    }

    // ---- the race -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ALostRace_RetriesOnTheNextCalVer_WithTheManifestRewrittenForIt()
    {
        var rig = new Rig();
        rig.WriteResults.Enqueue(false);

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(["v2026.12.1", "v2026.12.2"], rig.Writes.Select(w => w.Version));
        Assert.Equal("v2026.12.2", result.Response!.Version);
        Assert.Equal("v2026.12.2", (string?)Manifest(rig.Stored)["version"]);
        await rig.Writer.Received(1).SetLatestPointerAsync(Kind, AccountName, "v2026.12.2", Arg.Any<CancellationToken>());
        rig.Writer.DidNotReceive().SetLatestPointerAsync(Kind, AccountName, "v2026.12.1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AStampedVersion_IsSkippedForTheNext_WithoutAWrite()
    {
        var rig = new Rig();
        rig.Registry.IsPublishedAsync(Kind, AccountName, "v2026.12.1", Arg.Any<CancellationToken>()).Returns(true);

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(" | ", result.Errors));
        Assert.Equal(["v2026.12.2"], rig.Writes.Select(w => w.Version));
    }

    [Fact]
    public async Task ThreeLostRaces_AnswerA11006_WithTheReport_AndNothingIsPinned()
    {
        var rig = new Rig();
        rig.WriteResults.Enqueue(false);
        rig.WriteResults.Enqueue(false);
        rig.WriteResults.Enqueue(false);

        var result = await rig.Publisher().PublishAsync(OwnerId, Kind, Request(await Files()), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(3, rig.Writes.Count);
        Assert.Equal([PackagePublishSentences.AlreadyPublished(AccountName, "v2026.12.3").Sentence], result.Errors);
        var report = JsonNode.Parse(result.Report!.Value.GetRawText())!;
        Assert.Equal("refused", (string?)report["outcome"]);
        Assert.Null(report["publishedAtUtc"]);
        rig.Writer.DidNotReceiveWithAnyArgs().SetLatestPointerAsync(default, default!, default!, default);
        rig.Settings.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, default!, default!, default);
        await rig.Alerts.Received(1).RaiseAsync(
            new OperatorAlert(OperatorAlertKinds.PublishFailed, null, AccountName, 1), Arg.Any<CancellationToken>());
    }

    // ---- the endpoint's answer ----------------------------------------------------------------------------------------

    [Fact]
    public void ARefusalWithAReport_Is400sBody_TheFirstSentenceAndTheReport_TodaysErrorsBeside()
    {
        var report = JsonDocument.Parse("""{"outcome":"refused"}""").RootElement.Clone();
        var refused = new WorkflowPackagePublishResult(null, ["first", "second"], Report: report);

        var body = JsonNode.Parse(JsonSerializer.Serialize(WorkflowPackages.RefusalBody(refused)))!;

        Assert.Equal("first", (string?)body["error"]);
        Assert.Equal(["first", "second"], body["errors"]!.AsArray().Select(e => (string)e!));
        Assert.Equal("refused", (string?)body["report"]!["outcome"]);

        // below 31 the body is today's
        var today = JsonNode.Parse(JsonSerializer.Serialize(WorkflowPackages.RefusalBody(new WorkflowPackagePublishResult(null, ["only"]))))!;
        Assert.Equal("""{"errors":["only"]}""", today.ToJsonString());
    }
}
