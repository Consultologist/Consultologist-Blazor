using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10, Task 17; part 10 §§ 1.1, 1.4; D2, D6, D7): <c>scripts/migrate-workflow-package.cs --to 31</c>.
/// No test runs a script as a process, so the script is thin and what it decides is <see cref="Migration31Script"/>'s:
/// the arguments (<c>--write</c>, <c>--snapshot</c>, <c>--rename old=new</c>, <c>--when-empty &lt;deliverable&gt;=&lt;value&gt;</c>),
/// the run on a folder, the file plan it prints and writes, and the exit (0 clean, 1 written with hand edits left and
/// S10-001, 2 refused with nothing written). Each test runs it on a copy of a fixture in a temporary folder.
/// </summary>
public class MigrateScriptTests
{
    private static Migration31Options BaseOptions() =>
        new(V31Contract.PublishedTypes(), CompiledGoldenTests.Fixed, TestFormats.Catalog);

    private static (int Exit, string Output, string Error) Run(string folder, params string[] args)
    {
        var (arguments, usage) = Migration31Script.Parse([folder, "--to", "31", .. args]);
        Assert.True(usage is null, usage);
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var exit = Migration31Script.Run(folder, arguments!, BaseOptions(), output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static string CopyOf(string package)
    {
        var dir = Directory.CreateTempSubdirectory("migrate-script-").FullName;
        var source = Path.Combine(ByteEquivalenceFixtures.Root, "vendored", package);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dir, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return dir;
    }

    private static string FolderOf(string manifestJson, IReadOnlyDictionary<string, string> files)
    {
        var dir = Directory.CreateTempSubdirectory("migrate-script-").FullName;
        foreach (var (path, text) in files.Append(new("manifest.json", manifestJson)))
        {
            var target = Path.Combine([dir, .. path.Split('/')]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
        }

        return dir;
    }

    /// <summary>Every file under <paramref name="dir"/>, by its forward-slash path, with its text.</summary>
    private static SortedDictionary<string, string> Contents(string dir) => new(
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/'),
            ByteEquivalenceFixtures.ReadText,
            StringComparer.Ordinal),
        StringComparer.Ordinal);

    private static SortedDictionary<string, string> Expected(Migration31Result result) =>
        new(result.Files, StringComparer.Ordinal) { ["manifest.json"] = result.ManifestJson };

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void TheDryRun_PrintsTheNoticesAndThePlan_AndWritesNothing()
    {
        var dir = CopyOf("example-nested-note");
        try
        {
            var before = Contents(dir);

            var (exit, output, error) = Run(dir, "--rename", "parts=sections", "--when-empty", "note=fail");

            Assert.Equal(Migration31Script.Clean, exit);
            Assert.Equal("", error);
            Assert.Equal(before, Contents(dir));
            var lines = Lines(output);
            var migrated = MigratedPackagesTests.Migrated("example-nested-note");
            foreach (var notice in migrated.Notices)
                Assert.Contains($"notice: {notice}", lines);
            Assert.Contains("delete:  dag.mmd", lines);
            Assert.Contains("delete:  prompts/plan.md", lines);
            Assert.Contains("add:     nodes/index.json", lines);
            Assert.Contains("add:     tests/migration_snapshot.json", lines);
            Assert.Contains("rewrite: manifest.json", lines);
            Assert.Single(lines, l => l == Migration31Script.NoDiagram);
            Assert.EndsWith(" (dry run; pass --write to apply)", lines[^1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheWrite_LeavesExactlyThe31Files_AndExits0WithNoHandEdit()
    {
        // D6, D7: --rename and --when-empty fill the options as ExampleChoices does for the golden.
        var dir = CopyOf("example-nested-note");
        try
        {
            var (exit, output, error) = Run(dir, "--rename", "parts=sections", "--when-empty", "note=fail", "--write");

            Assert.Equal(Migration31Script.Clean, exit);
            Assert.Equal("", error);
            Assert.Equal(Expected(MigratedPackagesTests.Migrated("example-nested-note")), Contents(dir));
            Assert.False(Directory.Exists(Path.Combine(dir, "prompts")));
            Assert.False(File.Exists(Path.Combine(dir, "dag.mmd")));
            var lines = Lines(output);
            Assert.Single(lines, l => l == Migration31Script.NoDiagram);
            Assert.EndsWith("; written", lines[^1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheWrite_WithHandEditsLeft_Exits1_WrittenHandEditsRemain()
    {
        var dir = CopyOf("example-macros");
        try
        {
            var (exit, output, _) = Run(dir, "--write");

            Assert.Equal(Migration31Script.HandEditsLeft, exit);
            var migrated = MigratedPackagesTests.Migrated("example-macros");
            Assert.NotEmpty(migrated.HandEdits);
            Assert.Equal(Expected(migrated), Contents(dir));
            var lines = Lines(output);
            foreach (var edit in migrated.HandEdits)
                Assert.Contains($"notice: {edit}", lines);
            Assert.EndsWith($"; {Migration31Script.HandEditsRemain}", lines[^1], StringComparison.Ordinal);
            V31Contract.AssertSentence(new SourceRefusal("S10-001", Migration31Script.HandEditsRemain, ""));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AFolderBelow30_IsChainedInTheSameRun()
    {
        var (manifest27, files27) = V27Package.Minimal();
        var json27 = JsonSerializer.Serialize(manifest27, WorkflowPackageManifestJson.WriteOptions);
        var dir = FolderOf(json27, files27);
        try
        {
            var (exit, _, error) = Run(dir, "--write");

            var migrated = WorkflowPackageMigration30To31.Migrate(json27, files27, BaseOptions());
            Assert.True(migrated.Succeeded, string.Join("\n", migrated.Refusals));
            Assert.Equal(migrated.HandEdits.Count == 0 ? Migration31Script.Clean : Migration31Script.HandEditsLeft, exit);
            Assert.Equal("", error);
            Assert.Equal(Expected(migrated), Contents(dir));
            Assert.Equal(31, JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")))!["specVersion"]!.GetValue<int>());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public static TheoryData<string> Unreadable() => new(
        "not json",
        "[]",
        """{ "name": "general", "version": "v2026.11.3" }""",
        V24Package.ManifestJson(specVersion: 4));

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void ARefusal_Exits2_AndWritesNothing(string manifestJson)
    {
        // Part 10 § 1.4: an unreadable manifest and a version below 5 are refused, with no package written.
        var dir = FolderOf(manifestJson, V24Package.Files());
        try
        {
            var before = Contents(dir);

            var (exit, output, error) = Run(dir, "--write");

            Assert.Equal(Migration31Script.Refused, exit);
            Assert.Equal(before, Contents(dir));
            var expected = WorkflowPackageMigration30To31.Migrate(manifestJson, V24Package.Files(), BaseOptions());
            Assert.NotEmpty(expected.Refusals);
            foreach (var refusal in expected.Refusals)
                Assert.Contains($"error: {refusal}", Lines(error));
            Assert.EndsWith("nothing written.", Lines(error)[^1], StringComparison.Ordinal);
            Assert.DoesNotContain(Migration31Script.NoDiagram, output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheSnapshot_OnAnEdited31Folder_WritesTheSnapshotTest()
    {
        // Part 10 § 4, D13: once the hand edits are made, --snapshot writes the file Snapshot gives.
        const string package = "example-problem-list";
        var edited = MigrationFixture.Edited(package);
        Assert.DoesNotContain(SnapshotTest.File, edited.Files.Keys);
        var dir = FolderOf(edited.ManifestJson, edited.Files);
        try
        {
            var before = Contents(dir);
            var (dryExit, dryOutput, _) = Run(dir, "--snapshot");
            Assert.Equal(Migration31Script.Clean, dryExit);
            Assert.Equal(before, Contents(dir));
            Assert.Contains($"add:     {SnapshotTest.File}", Lines(dryOutput));
            Assert.DoesNotContain(Migration31Script.NoDiagram, dryOutput, StringComparison.Ordinal);

            var (exit, _, error) = Run(dir, "--snapshot", "--write");

            Assert.Equal(Migration31Script.Clean, exit);
            Assert.Equal("", error);
            var snapshot = WorkflowPackageMigration30To31.Snapshot(edited.ManifestJson, edited.Files, MigrationFixture.Options(package));
            Assert.True(snapshot.Succeeded, string.Join("\n", snapshot.Refusals));
            Assert.Equal(Expected(snapshot), Contents(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheSnapshot_LeavesThe31FoldersOwnDiagramAlone()
    {
        // A 31 folder may carry its own dag.mmd (area 13's); --snapshot neither reads nor deletes it.
        const string package = "example-problem-list";
        var edited = MigrationFixture.Edited(package);
        const string diagram = "flowchart TD\n  plan --> sections\n";
        var dir = FolderOf(edited.ManifestJson, edited.Files.Append(new("dag.mmd", diagram)).ToDictionary(StringComparer.Ordinal));
        try
        {
            var (exit, output, error) = Run(dir, "--snapshot", "--write");

            Assert.Equal(Migration31Script.Clean, exit);
            Assert.Equal("", error);
            Assert.DoesNotContain("dag.mmd", output, StringComparison.Ordinal);
            var snapshot = WorkflowPackageMigration30To31.Snapshot(edited.ManifestJson, edited.Files, MigrationFixture.Options(package));
            Assert.Equal(new SortedDictionary<string, string>(Expected(snapshot), StringComparer.Ordinal) { ["dag.mmd"] = diagram }, Contents(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AWriteThatFails_Exits2_AndDeletesNothing()
    {
        // The folder is made read-only: the first new file cannot be written, so no 30 file is deleted or rewritten.
        if (OperatingSystem.IsWindows())
            return;
        var dir = CopyOf("example-nested-note");
        var mode = File.GetUnixFileMode(dir);
        try
        {
            var before = Contents(dir);
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            if (CanWrite(dir))
                return; // Run as root, the folder is writable anyway: there is no failure to see.

            var (exit, _, error) = Run(dir, "--rename", "parts=sections", "--when-empty", "note=fail", "--write");

            Assert.Equal(Migration31Script.Refused, exit);
            Assert.StartsWith($"error: {dir} could not be written: ", error, StringComparison.Ordinal);
            Assert.EndsWith("stopped part-way; restore the folder, then run again.", Lines(error)[^1], StringComparison.Ordinal);
            File.SetUnixFileMode(dir, mode);
            Assert.Equal(before, Contents(dir));
        }
        finally
        {
            File.SetUnixFileMode(dir, mode);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static bool CanWrite(string dir)
    {
        var probe = Path.Combine(dir, ".probe");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("nodes/../../outside.md")]
    [InlineData("/etc/outside.md")]
    [InlineData("")]
    public void APathThatLeavesTheFolder_IsFound(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "package");

        Assert.Equal(path, Migration31Script.OutsideOf(root, ["manifest.json", path, "nodes/index.json"]));
    }

    [Fact]
    public void PathsInsideTheFolder_AreNotFound()
    {
        var root = Path.Combine(Path.GetTempPath(), "package");

        Assert.Null(Migration31Script.OutsideOf(root, ["manifest.json", "nodes/a/../index.json", "nodes/sections/outline.md"]));
        Assert.Null(Migration31Script.OutsideOf(root + Path.DirectorySeparatorChar, ["manifest.json"]));
        Assert.Equal("../package-other/x.md", Migration31Script.OutsideOf(root, ["../package-other/x.md"]));
    }

    [Fact]
    public void TheFolderFormat_FillsTheOptions_AndTestGeneralGetsItsGolden()
    {
        // Part 01 § 3: test/general's snippet folder standards types its items with standard_section (ExampleChoices).
        var dir = CopyOf("test/general");
        try
        {
            var (exit, _, error) = Run(dir, "--when-empty", "consult=hold", "--folder-format", "standards=standard_section", "--write");

            var migrated = MigratedPackagesTests.Migrated("test/general");
            Assert.Equal(migrated.HandEdits.Count == 0 ? Migration31Script.Clean : Migration31Script.HandEditsLeft, exit);
            Assert.Equal("", error);
            Assert.Equal(Expected(migrated), Contents(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("example-nested-note", new[] { "--rename", "parts=sections", "--when-empty", "note=fail" })]
    [InlineData("test/general", new[] { "--when-empty", "consult=hold", "--folder-format", "standards=standard_section" })]
    public void TheOptionsFile_GivesThePackageTheFlagsGave(string package, string[] flags)
    {
        // R18: --options reads the package's options.json (the one ExampleChoices reads), and writes what the three flags did.
        var byFlags = CopyOf(package);
        var byFile = CopyOf(package);
        try
        {
            var (flagsExit, _, flagsError) = Run(byFlags, [.. flags, "--write"]);
            var (fileExit, output, fileError) = Run(byFile, "--options", ExampleChoices.FileOf(package), "--write");

            Assert.Equal("", flagsError);
            Assert.Equal("", fileError);
            Assert.Equal(flagsExit, fileExit);
            Assert.Equal(Contents(byFlags), Contents(byFile));
            Assert.Equal(Expected(MigratedPackagesTests.Migrated(package)), Contents(byFile));
            Assert.DoesNotContain(Lines(output), l => l.Contains(MigrationOptionsFile.FileName, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(byFlags, recursive: true);
            Directory.Delete(byFile, recursive: true);
        }
    }

    public static TheoryData<string, string> RefusedOptions() => new()
    {
        { """{ "renames": {}, "whenEmpty": { "note": "maybe" }, "folderFormats": {} }""", "error: options.json gives deliverable 'note' a whenEmpty that is not release, hold or fail." },
        { """{ "renames": {}, "whenEmpty": {} }""", "error: options.json must give folderFormats as an object whose values are text; write {} for none." },
        { """[]""", "is not a JSON object" },
        { """{ "renames": {""", "is not a JSON object" },
    };

    [Theory]
    [MemberData(nameof(RefusedOptions))]
    public void AnOptionsFileThatDoesNotRead_Exits2_AndWritesNothing(string optionsJson, string said)
    {
        var dir = CopyOf("example-nested-note");
        var file = Path.Combine(Directory.CreateTempSubdirectory("migrate-options-").FullName, MigrationOptionsFile.FileName);
        File.WriteAllText(file, optionsJson);
        try
        {
            var before = Contents(dir);

            var (exit, output, error) = Run(dir, "--options", file, "--write");

            Assert.Equal(Migration31Script.Refused, exit);
            Assert.Equal(before, Contents(dir));
            Assert.Equal("", output);
            Assert.Contains(said, error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [Fact]
    public void AnOptionsFileThatIsMissing_Exits2()
    {
        var dir = CopyOf("example-nested-note");
        try
        {
            var (exit, _, error) = Run(dir, "--options", Path.Combine(dir, "no-such-options.json"));

            Assert.Equal(Migration31Script.Refused, exit);
            Assert.StartsWith("error: ", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheSnapshot_OfAFolderThatDoesNotPublish_Exits2_AndWritesNothing()
    {
        var migrated = MigratedPackagesTests.Migrated("example-macros");
        var dir = FolderOf(migrated.ManifestJson, migrated.Files);
        try
        {
            var before = Contents(dir);

            var (exit, _, error) = Run(dir, "--snapshot", "--write");

            Assert.Equal(Migration31Script.Refused, exit);
            Assert.Equal(before, Contents(dir));
            Assert.StartsWith("error: ", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TheArguments_FillTheOptions_AndRepeat()
    {
        var (arguments, usage) = Migration31Script.Parse(
            ["pkg", "--to", "31", "--rename", "parts=sections", "--when-empty", "note=fail", "--rename", "items=entries", "--when-empty", "letter=hold",
             "--folder-format", "standards=standard_section", "--folder-format", "notes=note_item", "--write"]);

        Assert.Null(usage);
        Assert.True(arguments!.Write);
        Assert.False(arguments.Snapshot);
        Assert.Equal(new Dictionary<string, string> { ["parts"] = "sections", ["items"] = "entries" }, arguments.Renames);
        Assert.Equal(new Dictionary<string, string> { ["note"] = "fail", ["letter"] = "hold" }, arguments.WhenEmpty);
        Assert.Equal(new Dictionary<string, string> { ["standards"] = "standard_section", ["notes"] = "note_item" }, arguments.FolderFormats);

        var options = arguments.Fill(BaseOptions());
        Assert.Same(arguments.Renames, options.Renames);
        Assert.Same(arguments.WhenEmpty, options.WhenEmpty);
        Assert.Same(arguments.FolderFormats, options.FolderFormats);

        var (snapshot, none) = Migration31Script.Parse(["pkg", "--snapshot", "--to", "31"]);
        Assert.Null(none);
        Assert.True(snapshot!.Snapshot);
        Assert.False(snapshot.Write);
        Assert.Empty(snapshot.Renames);
        Assert.Empty(snapshot.WhenEmpty);
        Assert.Empty(snapshot.FolderFormats);
        Assert.Null(snapshot.Options);

        var (byFile, noUsage) = Migration31Script.Parse(["pkg", "--to", "31", "--options", "choices/options.json", "--write"]);
        Assert.Null(noUsage);
        Assert.Equal("choices/options.json", byFile!.Options);
        Assert.True(byFile.Write);
        Assert.Empty(byFile.Renames);
    }

    [Theory]
    [InlineData("--rename")]
    [InlineData("--rename", "parts")]
    [InlineData("--rename", "=sections")]
    [InlineData("--rename", "parts=")]
    [InlineData("--rename", "parts=sections", "--rename", "parts=chapters")]
    [InlineData("--when-empty", "note")]
    [InlineData("--when-empty", "note=maybe")]
    [InlineData("--when-empty", "note=fail", "--when-empty", "note=hold")]
    [InlineData("--snapshot", "--rename", "parts=sections")]
    [InlineData("--snapshot", "--when-empty", "note=fail")]
    [InlineData("--folder-format")]
    [InlineData("--folder-format", "standards")]
    [InlineData("--folder-format", "standards=a", "--folder-format", "standards=b")]
    [InlineData("--snapshot", "--folder-format", "standards=standard_section")]
    [InlineData("--dag")]
    [InlineData("--options")]
    [InlineData("--options", "a.json", "--options", "b.json")]
    [InlineData("--options", "options.json", "--rename", "parts=sections")]
    [InlineData("--when-empty", "note=fail", "--options", "options.json")]
    [InlineData("--options", "options.json", "--folder-format", "standards=standard_section")]
    [InlineData("--snapshot", "--options", "options.json")]
    public void AnArgumentThatDoesNotRead_IsAUsageError(params string[] args)
    {
        var (arguments, usage) = Migration31Script.Parse(["pkg", "--to", "31", .. args]);

        Assert.Null(arguments);
        Assert.False(string.IsNullOrEmpty(usage));
    }

    [Fact]
    public void ThePackageDirectory_IsNotAnOptionsValue()
    {
        Assert.Equal("pkg", Migration31Script.PackageDirectory(["--rename", "parts=sections", "--to", "31", "--folder-format", "a=b", "pkg", "--when-empty", "note=fail"]));
        Assert.Equal("pkg", Migration31Script.PackageDirectory(["pkg", "--to", "25"]));
        Assert.Equal("pkg", Migration31Script.PackageDirectory(["--options", "options.json", "--to", "31", "pkg"]));
        Assert.Null(Migration31Script.PackageDirectory(["--to", "31", "--write"]));
    }

    [Fact]
    public void TheUsageComment_ListsTo31()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
            dir = dir.Parent;
        var script = File.ReadAllLines(Path.Combine(dir!.FullName, "scripts", "migrate-workflow-package.cs"), Encoding.UTF8);

        Assert.Contains(script, l => l.StartsWith("//   dotnet run -v q --file scripts/migrate-workflow-package.cs -- <package-dir> --to 31", StringComparison.Ordinal));
        Assert.Contains(script, l => l.StartsWith("//", StringComparison.Ordinal) && l.Contains("--snapshot", StringComparison.Ordinal));
        Assert.Contains(script, l => l.StartsWith("//", StringComparison.Ordinal) && l.Contains("--folder-format <folder>=<format>", StringComparison.Ordinal));
        Assert.Contains(script, l => l.StartsWith("//", StringComparison.Ordinal) && l.Contains("--options <file>", StringComparison.Ordinal));
        Assert.Contains("--options <file>", Migration31Script.Usage, StringComparison.Ordinal);
        Assert.Contains(script, l => l.Contains("Migration31Script.Run(", StringComparison.Ordinal));
    }
}
