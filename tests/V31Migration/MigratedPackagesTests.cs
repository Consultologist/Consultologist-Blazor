using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.4; the invariant of the plan's Global Constraints): the sixteen packages
/// (area 01's thirteen vendored and its three composer fixtures), and (#1508, Task 35) the seven edge-case fixtures, through the migration and the 31 checker. With no
/// hand-edit notice, <see cref="CompiledPackage.Publish"/> refuses nothing; with one, it refuses something. Each migrated
/// package is frozen under <c>migrated/&lt;package&gt;/</c> with its <c>notices.json</c> (<c>{ "notices", "handEdits",
/// "refusals" }</c>); the migration gives the same bytes twice and never writes an unknown member. With
/// <c>GOLDEN_UPDATE_V31=1</c> each golden folder is rewritten from <see cref="Migrated"/>; a rewritten golden is read
/// before it is committed.
/// </summary>
public class MigratedPackagesTests
{
    private const string GoldenFolder = "migrated";

    private static readonly ConcurrentDictionary<string, Migration31Result> Cache = new(StringComparer.Ordinal);

    /// <summary>The twenty-four packages: the vendored ones in the index's order, then the fixture packages.</summary>
    public static TheoryData<string> Packages() => new(Names);

    internal static IEnumerable<string> Names =>
        ByteEquivalenceFixtures.Vendored.Select(v => v.Name).Concat(ByteEquivalenceFixtures.FixturePackages);

    /// <summary>The packages whose migration says no hand edit.</summary>
    public static TheoryData<string> PackagesWithoutHandEdits() => new(Names.Where(p => Migrated(p).HandEdits.Count == 0));

    /// <summary>The packages whose migration says at least one hand edit.</summary>
    public static TheoryData<string> PackagesWithHandEdits() => new(NamesWithHandEdits);

    /// <summary>The names of <see cref="PackagesWithHandEdits"/>, in its order.</summary>
    internal static IReadOnlyList<string> NamesWithHandEdits => Names.Where(p => Migrated(p).HandEdits.Count > 0).ToList();

    /// <summary>
    /// <paramref name="package"/> migrated with its options (<see cref="MigrationFixture.Migrate"/>), once per test run;
    /// the migration must succeed.
    /// </summary>
    internal static Migration31Result Migrated(string package) => Cache.GetOrAdd(package, name =>
    {
        var result = MigrationFixture.Migrate(name);
        Assert.True(result.Succeeded, string.Join("\n", result.Refusals));
        return result;
    });

    /// <summary><paramref name="result"/>'s files and manifest as the upload the 31 checker reads (path to UTF-8 bytes).</summary>
    internal static Dictionary<string, byte[]> Upload(Migration31Result result)
    {
        var upload = result.Files.ToDictionary(f => f.Key, f => Encoding.UTF8.GetBytes(f.Value), StringComparer.Ordinal);
        upload["manifest.json"] = Encoding.UTF8.GetBytes(result.ManifestJson);
        return upload;
    }

    /// <summary>What the checker refuses of <paramref name="result"/> (<see cref="CompiledPackage.Publish(IReadOnlyDictionary{string, byte[]}, TypeRegistry, Toolchain, PublishLimits?, Templates.ITemplateReads?)"/> with no limits).</summary>
    internal static IReadOnlyList<SourceRefusal> Publish(Migration31Result result) =>
        CompiledPackage.Publish(Upload(result), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed).Refusals;

    /// <summary>
    /// #1508 (R11): what the engine's publish refuses of <paramref name="result"/>: the checker's refusals, and, at the
    /// engine's default limits (<see cref="WorkflowPackageMigration30To31.EngineLimits"/>), a worst case past the instances one
    /// job may open (A11-001).
    /// </summary>
    internal static IReadOnlyList<SourceRefusal> PublishAtTheEngine(Migration31Result result) =>
        CompiledPackage.Publish(Upload(result), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, WorkflowPackageMigration30To31.EngineLimits).Refusals;

    /// <summary>#1508 (R11): whether <paramref name="edit"/> is the hand edit of a package past the instances limit (A10-081), which <c>example-nested-note</c> carries.</summary>
    internal static bool PastTheInstanceLimit(string edit) => edit.Contains(", max: up to ", StringComparison.Ordinal);

    private static string Explain(IReadOnlyList<SourceRefusal> refusals, Migration31Result result) =>
        "Refusals:\n" + string.Join("\n", refusals.Select(r => $"  {r.Id} {r.Path}: {r.Sentence}"))
        + "\nHand edits:\n" + string.Join("\n", result.HandEdits.Select(e => "  " + e));

    [Fact]
    public void TheTwentyFourPackages_AreRun()
    {
        // #1508 (Task 35): the sixteen and the seven edge-case packages (EdgeCasePackagesTests); #1543 (Task A6): the eighth,
        // edge-source-no-branch.
        Assert.Equal(24, Packages().Count);
        Assert.Equal(24, PackagesWithoutHandEdits().Count + PackagesWithHandEdits().Count);
    }

    [Theory]
    [MemberData(nameof(PackagesWithoutHandEdits))]
    public void NoHandEdit_MeansPublishRefusesNothing(string package)
    {
        var result = Migrated(package);
        var refusals = PublishAtTheEngine(result);

        Assert.True(refusals.Count == 0, Explain(refusals, result));
    }

    [Theory]
    [MemberData(nameof(PackagesWithHandEdits))]
    public void AHandEdit_MeansPublishRefuses(string package)
    {
        var result = Migrated(package);
        var refusals = PublishAtTheEngine(result);

        Assert.True(refusals.Count > 0, Explain(refusals, result));
    }

    [Fact]
    public void ANestedLoopPastTheInstanceLimit_IsAHandEdit()
    {
        // #1508 (ruled by the owner, B2 of 2026-11-26 and its clarification; R11): A11-001 refuses a package whose worst case
        // passes the instances one job may open. The migration says it first, as a hand edit on the outermost loop's max
        // with A11-001's count and limit, and guesses no max: the package keeps its 30 version until one is chosen.
        var result = Migrated("example-nested-note");
        var refused = Assert.Single(PublishAtTheEngine(result));
        Assert.Equal("A11-001", refused.Id);

        const string reason = "up to 120,814 steps could run in one job through node.sections, and this engine runs at most 10,000 for one job, "
            + "so publish refuses the package; the migration guesses no max: lower a loop's max or split the workflow.";
        Assert.Equal("nodes/sections/index.json", refused.Path);
        Assert.Equal("Up to 120,814 steps could run in one job, through node.sections; this engine runs at most 10,000 for one job. Lower a loop's max or split the workflow.", refused.Sentence);
        var edit = Assert.Single(result.HandEdits);
        Assert.Equal($"Hand edit needed in {refused.Path}, max: {reason}", edit);
        V31Contract.AssertSentence(new SourceRefusal("S10-002", edit, refused.Path));
        V31Contract.AssertSentence(new SourceRefusal("A10-081", reason, ""));

        // No max is written: the loops are as 30 had them.
        Assert.Null(MigrateNodesTests.Json(result, "nodes/sections/index.json")["max"]);
        Assert.Null(MigrateNodesTests.Json(result, "nodes/sections/paragraphs/index.json")["max"]);

        // A package within the limit is no hand edit, and nested-note within a larger one neither.
        Assert.DoesNotContain(Migrated("composer-sources").HandEdits, e => e.Contains(", max: ", StringComparison.Ordinal));
        Assert.Empty(CompiledPackage.Publish(Upload(result), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, WorkflowPackageMigration30To31.EngineLimits with { Instances = 200_000 }).Refusals);
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void NothingUnknownIsWritten(string package)
    {
        // Part 01 / part 10 § 1.1: hand edits or not, every member written is the declaration's own.
        var parse = SourceParser.Parse(Upload(Migrated(package)), V31Contract.PublishedTypes().Names);

        Assert.DoesNotContain(parse.Refusals, r => r.Id is "S01-023" or "S04-010");
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void MigratingTwice_GivesTheSameBytes(string package)
    {
        var first = MigrationFixture.Migrate(package);
        var second = MigrationFixture.Migrate(package);

        Assert.Equal(first.ManifestJson, second.ManifestJson);
        Assert.Equal(first.Files.Keys.Order(StringComparer.Ordinal), second.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, text) in first.Files)
            Assert.True(text == second.Files[path], path);
        Assert.Equal(NoticesJson(first), NoticesJson(second));
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void TheMigratedSource_IsTheGolden(string package)
    {
        var expected = Golden(Migrated(package));
        var folder = Path.Combine([ByteEquivalenceFixtures.Root, GoldenFolder, .. package.Split('/')]);
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            foreach (var (path, text) in expected)
            {
                var file = Path.Combine([folder, .. path.Split('/')]);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes(text));
            }
        }

        Assert.True(Directory.Exists(folder), $"{GoldenFolder}/{package} is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
        var onDisk = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), onDisk);
        foreach (var (path, text) in expected)
            Assert.True(text == ByteEquivalenceFixtures.ReadText(Path.Combine([folder, .. path.Split('/')])), $"{GoldenFolder}/{package}/{path} differs");
    }

    /// <summary>What <c>migrated/&lt;package&gt;/</c> holds: the manifest, every file, and <c>notices.json</c>.</summary>
    private static SortedDictionary<string, string> Golden(Migration31Result result)
    {
        var files = new SortedDictionary<string, string>(result.Files, StringComparer.Ordinal)
        {
            ["manifest.json"] = result.ManifestJson,
            ["notices.json"] = NoticesJson(result),
        };
        return files;
    }

    /// <summary><c>notices.json</c>: every notice, the hand edits among them, and the refusals, each in the result's order.</summary>
    internal static string NoticesJson(Migration31Result result) => ByteEquivalenceFixtures.WriteJson(new JsonObject
    {
        ["notices"] = Strings(result.Notices),
        ["handEdits"] = Strings(result.HandEdits),
        ["refusals"] = Strings(result.Refusals),
    });

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode?)JsonValue.Create(v))]);
}
