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
/// (area 01's thirteen vendored and its three composer fixtures) through the migration and the 31 checker. With no
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

    /// <summary>The sixteen packages: the vendored ones in the index's order, then the fixture packages.</summary>
    public static TheoryData<string> Packages() => new(Names);

    private static IEnumerable<string> Names =>
        ByteEquivalenceFixtures.Vendored.Select(v => v.Name).Concat(ByteEquivalenceFixtures.FixturePackages);

    /// <summary>The packages whose migration says no hand edit.</summary>
    public static TheoryData<string> PackagesWithoutHandEdits() => new(Names.Where(p => Migrated(p).HandEdits.Count == 0));

    /// <summary>The packages whose migration says at least one hand edit.</summary>
    public static TheoryData<string> PackagesWithHandEdits() => new(Names.Where(p => Migrated(p).HandEdits.Count > 0));

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

    private static IReadOnlyList<SourceRefusal> Publish(Migration31Result result) =>
        CompiledPackage.Publish(Upload(result), V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed).Refusals;

    private static string Explain(IReadOnlyList<SourceRefusal> refusals, Migration31Result result) =>
        "Refusals:\n" + string.Join("\n", refusals.Select(r => $"  {r.Id} {r.Path}: {r.Sentence}"))
        + "\nHand edits:\n" + string.Join("\n", result.HandEdits.Select(e => "  " + e));

    [Fact]
    public void TheSixteenPackages_AreRun()
    {
        Assert.Equal(16, Packages().Count);
        Assert.Equal(16, PackagesWithoutHandEdits().Count + PackagesWithHandEdits().Count);
    }

    [Theory]
    [MemberData(nameof(PackagesWithoutHandEdits))]
    public void NoHandEdit_MeansPublishRefusesNothing(string package)
    {
        var result = Migrated(package);
        var refusals = Publish(result);

        Assert.True(refusals.Count == 0, Explain(refusals, result));
    }

    [Theory]
    [MemberData(nameof(PackagesWithHandEdits))]
    public void AHandEdit_MeansPublishRefuses(string package)
    {
        var result = Migrated(package);
        var refusals = Publish(result);

        Assert.True(refusals.Count > 0, Explain(refusals, result));
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
    private static string NoticesJson(Migration31Result result) => ByteEquivalenceFixtures.WriteJson(new JsonObject
    {
        ["notices"] = Strings(result.Notices),
        ["handEdits"] = Strings(result.HandEdits),
        ["refusals"] = Strings(result.Refusals),
    });

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode?)JsonValue.Create(v))]);
}
