using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Publish;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Publish;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 15; part 07 §§ 5, 9; D1, D2, D10): publish as one call
/// (<see cref="PackagePublish.RunAsync"/>, <see cref="PackagePublish.CheckAsync"/>) on the nested-note package with one
/// passing test, the migration snapshot's shape written from its own run. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PackagePublishTests
{
    private const string TestPath = "tests/snapshot.json";

    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The engine's limits with a ceiling the nested note passes, so its note is among the warnings.</summary>
    private static readonly PublishLimits Limits = new(1, 200, 500);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static PublishOptions Options(
        Func<string, string>? diagram = null, ITemplateReads? templates = null, string? migrationReport = null,
        string? migratedFrom = null, TimeProvider? clock = null, RunLimits? runLimits = null) =>
        new(ResolveFixture.Published(), CompiledGoldenTests.Fixed, Limits, "api", clock ?? new FixedClock(Now),
            templates, diagram, migrationReport, migratedFrom, RunLimits: runLimits);

    /// <summary>The nested note with <c>tests/snapshot.json</c>, a test written from the package's own offline run.</summary>
    private static async Task<Dictionary<string, byte[]>> WithTest(Action<JsonObject>? change = null)
    {
        var upload = ResolveFixture.NestedNote();
        var plan = RunFixture.Plan(upload);
        var request = SnapshotTest.Fictional(plan);
        var file = SnapshotTest.Write(plan, request, await OfflineRun.RunAsync(plan, request));
        change?.Invoke(file);
        return upload.With("tests/index.json", Index(["snapshot"])).With(TestPath, file.ToJsonString());
    }

    private static string Text(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);

    private static void AssertRefusedAndNothingStored(PublishOutcome outcome)
    {
        Assert.False(outcome.Published);
        Assert.False(outcome.Report.Published);
        Assert.Null(outcome.Stamp);
        Assert.Empty(outcome.Files);
        Assert.Null(outcome.Report.ResolveHash);
        Assert.Null(outcome.Report.LockHash);
        Assert.Null(outcome.Report.PublishedAtUtc);
        Assert.NotEmpty(outcome.Report.Refusals);
        Assert.Equal("refused", (string?)outcome.Report.ToJson()["outcome"]);
    }

    // ---- published ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task APassingPackage_IsPublished_WithItsFilesInWriteOrder_AndThePairTheEngineLoads()
    {
        var upload = await WithTest();
        var outcome = await PackagePublish.RunAsync(upload, Options(diagram: _ => "flowchart TD\n"));

        Assert.True(outcome.Published);
        var report = outcome.Report;
        Assert.True(report.Published);
        Assert.Equal(("example-minimal", "api", Now), (report.Name, report.PublishedBy, report.PublishedAtUtc));
        Assert.Equal((string?)JsonNode.Parse(upload["manifest.json"])!["version"], report.Version);
        Assert.Empty(report.Refusals);
        Assert.True(Assert.Single(report.Tests).Pass);

        var sources = upload.Keys.Order(StringComparer.Ordinal).ToList();
        Assert.Contains("manifest.json", sources);
        Assert.Equal(
            [.. sources, "dag.mmd", "lock.json", PublishReport.FileName, CompiledPackage.ResolveFile],
            outcome.Files.Select(f => f.Path));
        foreach (var path in sources)
            Assert.Equal(upload[path], outcome.Files.Single(f => f.Path == path).Bytes);
        Assert.Equal("flowchart TD\n", Text(outcome.Files.Single(f => f.Path == "dag.mmd").Bytes));

        var resolve = outcome.Files[^1].Bytes;
        var lockBytes = outcome.Files.Single(f => f.Path == "lock.json").Bytes;
        Assert.Equal(report.ResolveHash, CanonicalJson.HashText(Text(resolve)));
        Assert.Equal(report.LockHash, CanonicalJson.HashText(Text(lockBytes)));
        Assert.Equal(report.ToCanonical(), Text(outcome.Files.Single(f => f.Path == PublishReport.FileName).Bytes));
        Assert.Equal(new PublishedStamp(report.ResolveHash!, report.LockHash!), outcome.Stamp);
        Assert.Equal(report.SourceHash, outcome.Compiled!.SourceHash);
        Assert.Empty(outcome.Dropped);

        Assert.Null(CompiledPackage.Verify(report.Name, report.Version, resolve, lockBytes, outcome.Stamp!.ResolveHash, outcome.Stamp.LockHash));
        var (plan, refusal) = RunPlan.Build(Text(resolve), outcome.Stamp.ResolveHash, outcome.Stamp.LockHash, null);
        Assert.Null(refusal);
        Assert.NotNull(plan);
    }

    [Fact]
    public async Task WithNoDiagram_NoDagIsWritten_AndAMigrationReportGoesBeforeResolve_WithTheStampNamingTheOldVersion()
    {
        const string migration = """{"oldVersion":"v2026.1.1","newVersion":"v2026.12.1"}""";
        var outcome = await PackagePublish.RunAsync(await WithTest(), Options(migrationReport: migration, migratedFrom: "v2026.1.1"));

        Assert.True(outcome.Published);
        Assert.DoesNotContain(outcome.Files, f => f.Path == "dag.mmd");
        Assert.Equal(
            ["lock.json", PublishReport.FileName, "migration-report.json", CompiledPackage.ResolveFile],
            outcome.Files.TakeLast(4).Select(f => f.Path));
        Assert.Equal(migration, Text(outcome.Files.Single(f => f.Path == "migration-report.json").Bytes));
        Assert.Equal("v2026.1.1", outcome.Stamp!.MigratedFrom);
    }

    [Fact]
    public async Task TheWarnings_AreResolvesThenTheTests_WithTheCeilingNote()
    {
        var outcome = await PackagePublish.RunAsync(
            await WithTest(f => f["outputs"]!.AsObject()["node.extra"] = new JsonObject { ["sections"] = new JsonArray() }),
            Options());

        Assert.True(outcome.Published);
        var warnings = outcome.Report.Warnings;
        Assert.Equal([.. outcome.Compiled!.Warnings, PackagePublishSentences.StubNotUsed("snapshot", "node.extra", TestPath)], warnings);
        Assert.Contains(warnings, w => w.Id is "S04-072" or "S04-073");
        foreach (var warning in warnings)
            V31Contract.AssertSentence(warning);
    }

    // ---- refused ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ASourceRefusal_IsRefused_WithEverySentenceAndPath_AndNothingStored()
    {
        var upload = (await WithTest()).With("nodes/index.json", Index(["plan", "section_count", "sections", "refine", "nowhere"]));
        var check = ResolveFixture.Run(upload);
        Assert.NotEmpty(check.Refusals);

        var outcome = await PackagePublish.RunAsync(upload, Options());

        AssertRefusedAndNothingStored(outcome);
        Assert.Equal(check.Refusals, outcome.Report.Refusals);
        Assert.Equal(SourceHash.Of(check.Parse.Files), outcome.Report.SourceHash);
        Assert.Equal("example-minimal", outcome.Report.Name);
        Assert.Empty(outcome.Report.Tests);
        Assert.Null(outcome.Compiled);
    }

    [Fact]
    public async Task AManifestWithoutTextNameOrVersion_IsRefusedWithA11004()
    {
        var upload = await WithTest();
        var manifest = JsonNode.Parse(upload["manifest.json"])!.AsObject();
        manifest["version"] = 3;
        upload["manifest.json"] = Utf8(manifest.ToJsonString());

        var outcome = await PackagePublish.RunAsync(upload, Options());

        AssertRefusedAndNothingStored(outcome);
        Assert.Equal(PackagePublishSentences.ManifestNameAndVersion(), outcome.Report.Refusals[0]);
        Assert.Equal(("example-minimal", ""), (outcome.Report.Name, outcome.Report.Version));
    }

    [Fact]
    public async Task AnIncompletePair_IsRefusedAsAnEngineFault_NeverStored()
    {
        var unknown = new StubTemplates(new Dictionary<string, string[]>(), everyBody: false);

        var outcome = await PackagePublish.RunAsync(await WithTest(), Options(templates: unknown));

        AssertRefusedAndNothingStored(outcome);
        var refusal = Assert.Single(outcome.Report.Refusals);
        Assert.Equal("A11-007", refusal.Id);
        // No exception was caught: the pair is incomplete, so there is no fault type to name.
        Assert.Null(refusal.EngineFault);
    }

    [Fact]
    public async Task AFailingTest_IsRefused_WithTheTestsFilled_AndNothingStored()
    {
        var upload = await WithTest(f => f["expect"]!.AsObject()["ran"] = new JsonArray("node.nope"));

        var outcome = await PackagePublish.RunAsync(upload, Options());

        AssertRefusedAndNothingStored(outcome);
        Assert.Equal("S07-007", Assert.Single(outcome.Report.Refusals).Id);
        Assert.False(Assert.Single(outcome.Report.Tests).Pass);
        Assert.NotNull(outcome.Compiled);
    }

    [Fact]
    public async Task ThePackagesTests_RunAtTheGivenRunLimits()
    {
        var upload = await WithTest();

        // unset: the spec's starting values, which the nested note's test passes
        Assert.True((await PackagePublish.RunAsync(upload, Options(runLimits: null))).Published);

        // one agent call a job: the nested note makes many, so its test fails at its run
        var outcome = await PackagePublish.RunAsync(upload, Options(runLimits: new RunLimits() with { JobAgentCalls = 1 }));

        AssertRefusedAndNothingStored(outcome);
        Assert.False(Assert.Single(outcome.Report.Tests).Pass);
        Assert.Contains(outcome.Report.Refusals, r => r.Id == "S07-007");
    }

    [Fact]
    public async Task AnExceptionInside_IsRefusedWithA11007_AndItsType_NeverThrown()
    {
        var outcome = await PackagePublish.RunAsync(await WithTest(), Options(diagram: _ => throw new InvalidOperationException("Margaret Okafor")));

        AssertRefusedAndNothingStored(outcome);
        var refusal = Assert.Single(outcome.Report.Refusals);
        Assert.Equal(PackagePublishSentences.EngineFailed().Sentence, refusal.Sentence);
        Assert.Equal("A11-007", refusal.Id);
        Assert.Equal(nameof(InvalidOperationException), refusal.EngineFault);
        Assert.DoesNotContain("Okafor", outcome.Report.ToCanonical(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), outcome.Report.ToCanonical(), StringComparison.Ordinal);
    }

    // ---- receiving ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task UploadedEvidenceAndDag_AreInNoneOfTheFiles_AndTheEvidenceIsDropped()
    {
        var upload = (await WithTest())
            .With("resolve.json", "{\"uploaded\":1}")
            .With("lock.json", "{\"uploaded\":2}")
            .With(PublishReport.FileName, "{\"uploaded\":3}")
            .With(PublishedStamp.FileName, "{\"uploaded\":4}")
            .With("dag.mmd", "uploaded dag\n");

        var outcome = await PackagePublish.RunAsync(upload, Options(diagram: _ => "flowchart TD\n"));

        Assert.True(outcome.Published, string.Join(" | ", outcome.Report.Refusals.Select(r => r.Sentence)));
        Assert.Equal(["lock.json", PublishReport.FileName, PublishedStamp.FileName, "resolve.json"], outcome.Dropped);
        Assert.DoesNotContain(outcome.Files, f => Text(f.Bytes).Contains("uploaded", StringComparison.Ordinal));
        Assert.Single(outcome.Files, f => f.Path == "dag.mmd");
        Assert.DoesNotContain(outcome.Files, f => f.Path == PublishedStamp.FileName);
    }

    [Fact]
    public async Task CrLfFiles_AreNoted_AndStoredNormalised()
    {
        var upload = await WithTest();
        upload["nodes/plan.md"] = Utf8(Text(upload["nodes/plan.md"]).Replace("\n", "\r\n", StringComparison.Ordinal));

        var outcome = await PackagePublish.RunAsync(upload, Options());

        Assert.True(outcome.Published);
        Assert.Equal(["nodes/plan.md"], outcome.Report.Normalised);
        var stored = Text(outcome.Files.Single(f => f.Path == "nodes/plan.md").Bytes);
        Assert.DoesNotContain('\r', stored);
        Assert.Equal(Text(upload["nodes/plan.md"]).Replace("\r\n", "\n", StringComparison.Ordinal), stored);
    }

    // ---- determinism and checking -------------------------------------------------------------------------------------

    /// <summary>The report with what may differ between two runs taken out: the time and each test's duration.</summary>
    private static string Comparable(PublishReport report) =>
        (report with { PublishedAtUtc = null, Tests = [.. report.Tests.Select(t => t with { DurationMs = 0 })] }).ToCanonical();

    [Fact]
    public async Task TheSameUploadTwice_GivesTheSameHashes_OnlyTheTimeAndDurationsDiffer()
    {
        var upload = await WithTest();

        var first = await PackagePublish.RunAsync(upload, Options(clock: new FixedClock(Now)));
        var second = await PackagePublish.RunAsync(upload, Options(clock: new FixedClock(Now.AddHours(1))));

        Assert.True(first.Published && second.Published);
        Assert.Equal(first.Report.ResolveHash, second.Report.ResolveHash);
        Assert.Equal(first.Report.LockHash, second.Report.LockHash);
        Assert.Equal(first.Report.Tests.Select(t => t.OutcomeHash), second.Report.Tests.Select(t => t.OutcomeHash));
        Assert.NotEqual(first.Report.PublishedAtUtc, second.Report.PublishedAtUtc);
        Assert.Equal(Comparable(first.Report), Comparable(second.Report));
        Assert.Equal(first.Stamp, second.Stamp);
    }

    [Fact]
    public async Task Check_GivesTheSameReport_WithTheOutcomeAsItWouldBe_NoTimeAndNoFiles()
    {
        var upload = await WithTest();

        var published = await PackagePublish.RunAsync(upload, Options(diagram: _ => "flowchart TD\n"));
        var checkedOnly = await PackagePublish.CheckAsync(upload, Options(diagram: _ => throw new InvalidOperationException()));

        Assert.False(checkedOnly.Published);
        Assert.Null(checkedOnly.Stamp);
        Assert.Empty(checkedOnly.Files);
        Assert.Null(checkedOnly.Report.PublishedAtUtc);
        Assert.Equal("published", (string?)checkedOnly.Report.ToJson()["outcome"]);
        Assert.Equal(Comparable(published.Report), Comparable(checkedOnly.Report));

        var failing = await PackagePublish.CheckAsync(
            await WithTest(f => f["expect"]!.AsObject()["ran"] = new JsonArray("node.nope")), Options());
        Assert.Equal("refused", (string?)failing.Report.ToJson()["outcome"]);
        Assert.Empty(failing.Files);
    }
}
