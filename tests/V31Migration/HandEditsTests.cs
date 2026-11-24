using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10, Task 16; part 10 §§ 1.4, 1.5, 3.2): the hand edits an operator would make, so every
/// package is compared. Each package Task 12 migrates with hand-edit notices has a folder <c>hand-edits/&lt;package&gt;/</c>
/// holding the files the operator edited (laid over the migrated files by <see cref="MigrationFixture.Edited"/>; a file
/// named <c>&lt;path&gt;.deleted</c> removes <c>&lt;path&gt;</c>) and a <c>README.md</c> that lists every hand-edit
/// notice of the package beside the files that answer it. The edited package publishes with no refusal, gets its
/// snapshot (<see cref="WorkflowPackageMigration30To31.Snapshot"/>), and is compared with its 30 captures; its report is
/// frozen as <c>reports/&lt;package&gt;.json</c>, with <c>handEdits</c> the migration's notices. An edit that needs a design
/// choice the notices do not settle is not made: the package is in <see cref="NotEdited"/> with the reason. No test
/// asserts how many texts differ. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class HandEditsTests
{
    private const string ReportsFolder = "reports";

    /// <summary>The line that opens a notice in a README's list.</summary>
    private const string NoticeLine = "- Hand edit needed in ";

    /// <summary>The line under a notice that names the files answering it: <c>  - Answered in `a`, `b`: how.</c></summary>
    private const string AnsweredLine = "  - Answered in ";

    /// <summary>The line under a notice of a package in <see cref="NotEdited"/>: <c>  - Not made: why.</c></summary>
    private const string NotMadeLine = "  - Not made: ";

    /// <summary>
    /// The packages whose edits are not made, and why: each needs a design choice the notices do not settle (the
    /// conservative path of the plan's Task 16, Step 3). Each stays refused at publish and uncompared, and is listed in
    /// the plan's <i>Results</i> for the PR.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> NotEdited = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["example-macros"] = SilentAndWrapUp,
        ["example-macros-warnings"] = SilentAndWrapUp + " Its self_read instruction read the macro's own .content, which rendered the body into the prompt; what the model is told in its place is a choice too.",
    };

    private const string SilentAndWrapUp =
        "silent_reminder's 30 instruction is {{ \"\" }}, which rendered as nothing, so 30 never told the model where to write the marker; a 31 "
        + "instruction says where and the model must then write it (part 05 §§ 8.3, 8.6), so the place is a choice 30's package does not make. "
        + "wrap_up's read the sign-off's body (the profile's name) into the prompt, which 31 never sends, and sign_off is itself a marker at 31, "
        + "so where wrap_up goes against it is a choice too.";

    /// <summary>The packages Task 12 migrates with hand edits (<see cref="MigratedPackagesTests.PackagesWithHandEdits"/>).</summary>
    public static TheoryData<string> Packages() => MigratedPackagesTests.PackagesWithHandEdits();

    /// <summary>The packages with hand edits whose edits are made.</summary>
    public static TheoryData<string> EditedPackages() =>
        new(MigratedPackagesTests.NamesWithHandEdits.Where(p => !NotEdited.ContainsKey(p)));

    [Fact]
    public void EveryPackageWithHandEdits_HasItsEdits()
    {
        // A folder per package of Task 12's list, each with its README, and none for a package with no hand edit.
        var root = Path.Combine(ByteEquivalenceFixtures.Root, MigrationFixture.HandEditsFolder);
        var folders = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, MigrationFixture.HandEditsReadme, SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, Path.GetDirectoryName(f)!).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal).ToList()
            : [];

        var expected = MigratedPackagesTests.NamesWithHandEdits.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, folders);
        Assert.All(NotEdited.Keys, p => Assert.Contains(p, expected));
        foreach (var package in expected)
        {
            var folder = MigrationFixture.HandEditsOf(package);
            var edits = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetRelativePath(folder, f) != MigrationFixture.HandEditsReadme);
            if (NotEdited.ContainsKey(package))
                Assert.False(edits.Any(), $"{MigrationFixture.HandEditsFolder}/{package} is not edited, but holds edits.");
            else
                Assert.True(edits.Any(), $"{MigrationFixture.HandEditsFolder}/{package} holds no edit.");
        }
    }

    /// <summary>The packages whose edits are not made (<see cref="NotEdited"/>).</summary>
    public static TheoryData<string> NotEditedPackages() => new(NotEdited.Keys.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(NotEditedPackages))]
    public void APackageNotEdited_StaysRefused(string package)
    {
        // An edit not made leaves its notice's construct where the checker refuses it, so the package is not compared.
        Assert.NotEmpty(MigratedPackagesTests.Publish(MigrationFixture.Edited(package)));
    }

    [Theory]
    [MemberData(nameof(EditedPackages))]
    public void TheEditedPackage_Publishes(string package)
    {
        // The invariant (Task 12): no hand edit left means CompiledPackage.Publish refuses nothing.
        var refusals = MigratedPackagesTests.Publish(MigrationFixture.Edited(package));

        Assert.True(refusals.Count == 0, "Refusals:\n" + string.Join("\n", refusals.Select(r => $"  {r.Id} {r.Path}: {r.Sentence}")));
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void EachNotice_IsAnswered(string package)
    {
        // The README lists every hand-edit notice of the package, in the migration's order (notices.json's handEdits),
        // each beside the files that answer it, every one of them a file of the folder (or the deletion of one).
        var folder = MigrationFixture.HandEditsOf(package);
        var readme = Path.Combine(folder, MigrationFixture.HandEditsReadme);
        Assert.True(File.Exists(readme), $"{MigrationFixture.HandEditsFolder}/{package}/{MigrationFixture.HandEditsReadme} is missing.");
        var lines = ByteEquivalenceFixtures.ReadText(readme).Split('\n');

        var notices = new List<string>();
        var answering = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(NoticeLine, StringComparison.Ordinal))
                continue;
            var notice = lines[i][2..];
            notices.Add(notice);
            var next = i + 1 < lines.Length ? lines[i + 1] : "";
            if (NotEdited.ContainsKey(package))
            {
                Assert.True(next.StartsWith(NotMadeLine, StringComparison.Ordinal), $"'{notice}' has no '{NotMadeLine.Trim()}' line.");
                continue;
            }

            Assert.True(next.StartsWith(AnsweredLine, StringComparison.Ordinal), $"'{notice}' has no '{AnsweredLine.Trim()}' line.");
            var files = AnsweringFiles(next[AnsweredLine.Length..]);
            Assert.True(files.Count > 0, $"'{notice}' names no file.");
            foreach (var file in files)
            {
                var path = Path.Combine([folder, .. file.Split('/')]);
                Assert.True(File.Exists(path) || File.Exists(path + MigrationFixture.DeletedSuffix), $"'{notice}' names {file}, which the folder does not hold.");
                answering.Add(file);
            }
        }

        var said = JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine([ByteEquivalenceFixtures.Root, "migrated", .. package.Split('/'), "notices.json"])))!
            ["handEdits"]!.AsArray().Select(n => (string)n!).ToList();
        Assert.Equal(said, notices);
        Assert.Equal(MigratedPackagesTests.Migrated(package).HandEdits, notices);

        // Every edited file answers some notice, or is named as a consequence of one (a derived flag, an index).
        if (!NotEdited.ContainsKey(package))
        {
            var edited = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(f => f != MigrationFixture.HandEditsReadme)
                .Select(f => f.EndsWith(MigrationFixture.DeletedSuffix, StringComparison.Ordinal) ? f[..^MigrationFixture.DeletedSuffix.Length] : f);
            var text = ByteEquivalenceFixtures.ReadText(readme);
            Assert.All(edited, f => Assert.True(text.Contains($"`{f}`", StringComparison.Ordinal), $"{f} is edited, but the README does not name it."));
        }
    }

    [Fact]
    public void AMovedNode_IsComparedThroughItsKeys()
    {
        // example-problem-list's edit moves section_draft under the if node applies (part 04 § 5.4); its keys.json lays the
        // move over the migration's keys, so 30's section_draft:<item> maps to the moved row and back, and no text of the
        // report is left under a 30 key the migration does not know.
        var edited = MigrationFixture.Edited("example-problem-list");
        var plan = EquivalenceFixture.Plan("example-problem-list");
        Assert.Equal("node.sections.section_draft", MigratedPackagesTests.Migrated("example-problem-list").Keys["section_draft"]);
        Assert.Equal("node.sections.applies.section_draft:follow_up_plan", KeyMap.To31("section_draft:follow_up_plan", edited, plan));
        Assert.Equal("section_draft:overview", KeyMap.To30("node.sections.applies.section_draft:overview", edited, plan));
        Assert.DoesNotContain(EquivalenceFixture.Report("example-problem-list").GetAwaiter().GetResult().Texts, t => t.Site.Contains("@30/", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(EditedPackages))]
    public void TheEditedPackage_GetsItsSnapshot(string package)
    {
        // Part 10 § 4, D13: once the edits are made, Snapshot writes tests/migration_snapshot.json and lists it.
        var edited = MigrationFixture.Edited(package);
        var snapshot = WorkflowPackageMigration30To31.Snapshot(edited.ManifestJson, edited.Files, MigrationFixture.Options(package));

        Assert.True(snapshot.Succeeded, string.Join("\n", snapshot.Refusals));
        Assert.True(snapshot.Files.ContainsKey(SnapshotTest.File));
        Assert.Contains(SnapshotTest.Id, JsonNode.Parse(snapshot.Files["tests/index.json"])!["items"]!.AsArray().Select(i => (string?)i));
        Assert.Empty(MigratedPackagesTests.Publish(snapshot));
    }

    [Theory]
    [MemberData(nameof(EditedPackages))]
    public async Task TheReport_IsValidAgainstItsSchema(string package)
    {
        var report = await EquivalenceFixture.Report(package);

        Assert.True(V31ContractsTests.SchemaOf("migration-report").Validate(report.ToJson(), null), report.ToJson());
        Assert.Equal(MigratedPackagesTests.Migrated(package).HandEdits, report.HandEdits);
        Assert.Empty(report.Notices.Intersect(report.HandEdits));
        Assert.NotEmpty(report.Texts);
        Assert.All(report.Texts, t => Assert.Equal(t.Expected, t.Explanation != null));
    }

    [Theory]
    [MemberData(nameof(EditedPackages))]
    public async Task TheReport_IsTheGolden(string package)
    {
        var report = (await EquivalenceFixture.Report(package)).ToJson();
        var file = Path.Combine([ByteEquivalenceFixtures.Root, ReportsFolder, .. package.Split('/')]) + ".json";
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, new UTF8Encoding(false).GetBytes(report));
        }

        Assert.True(File.Exists(file), $"{ReportsFolder}/{package}.json is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
        Assert.True(report == ByteEquivalenceFixtures.ReadText(file), $"{ReportsFolder}/{package}.json differs");
    }

    /// <summary>The backticked paths of an <see cref="AnsweredLine"/>'s rest, up to its colon.</summary>
    private static List<string> AnsweringFiles(string rest)
    {
        var colon = rest.IndexOf("`: ", StringComparison.Ordinal);
        var list = colon < 0 ? rest : rest[..(colon + 1)];
        return list.Split(", ").Select(p => p.Trim()).Where(p => p.Length > 2 && p[0] == '`' && p[^1] == '`').Select(p => p[1..^1]).ToList();
    }
}
