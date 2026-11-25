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
/// #1508 (specVersion 31, area 11, Task 20; part 07 §§ 4.4, 5, 9; D11): <c>scripts/check-workflow-package-v31.cs</c> and
/// <c>scripts/rerender-workflow-packages-v31.cs</c>. As for <c>migrate-workflow-package.cs</c> (MigrateScriptTests), no test
/// runs a script as a process (a file-based app takes a build each time). What each script decides is the library's
/// (<see cref="PackagePublish"/>), so these tests pin the pipeline's answers the scripts print and write from (the file order the
/// stamp follows, S07-015 per dropped evidence file, a re-run's hashes against the stored report) and the scripts' text (their
/// usage, exits and the lines that carry the behaviours). What stays covered by the recorded manual runs alone (the task report
/// and the final fix report): the folder walks (nested names, links skipped), the out-folder refusals, <c>--version</c>
/// editing the manifest, the stamp written after the files, the trailing-separator out folder and the re-render's
/// difference lines.
/// </summary>
public class ScriptsTests
{
    private const string TestPath = "tests/snapshot.json";

    private static readonly DateTimeOffset Now = new(2026, 12, 1, 9, 30, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static PublishOptions Options(string publishedBy = "ci v2026.12.7") =>
        new(ResolveFixture.Published(), CompiledGoldenTests.Fixed, new PublishLimits(1, 200, 500), publishedBy, new FixedClock(Now));

    private static async Task<Dictionary<string, byte[]>> Folder()
    {
        var upload = ResolveFixture.NestedNote();
        var plan = RunFixture.Plan(upload);
        var request = SnapshotTest.Fictional(plan);
        var file = SnapshotTest.Write(plan, request, await OfflineRun.RunAsync(plan, request));
        return upload.With("tests/index.json", Index(["snapshot"])).With(TestPath, file.ToJsonString());
    }

    private static string Script(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "scripts", name), Encoding.UTF8);
    }

    // ---- what the scripts print and write from ------------------------------------------------------------------------

    [Fact]
    public async Task APublish_GivesTheFilesInWriteOrder_WithTheStampSeparateForTheScriptToWriteLast()
    {
        var outcome = await PackagePublish.RunAsync(await Folder(), Options());

        Assert.True(outcome.Published);
        Assert.Equal(CompiledPackage.ResolveFile, outcome.Files[^1].Path);
        Assert.DoesNotContain(outcome.Files, f => f.Path == PublishedStamp.FileName);
        Assert.NotNull(outcome.Stamp);
        Assert.Equal(outcome.Report.ResolveHash, outcome.Stamp!.ResolveHash);
        Assert.StartsWith("{", outcome.Stamp.ToJson(), StringComparison.Ordinal);
        Assert.Equal("ci v2026.12.7", (string?)outcome.Report.ToJson()["publishedBy"]);
    }

    [Fact]
    public async Task ACheck_GivesNoFilesAndNoStamp_AndTheSameResolveHashAsThePublish()
    {
        var upload = await Folder();
        var check = await PackagePublish.CheckAsync(upload, Options());
        var publish = await PackagePublish.RunAsync(upload, Options());

        Assert.True(check.Report.Published);
        Assert.Empty(check.Files);
        Assert.Null(check.Stamp);
        Assert.Null(check.Report.PublishedAtUtc);
        Assert.Equal(publish.Report.ResolveHash, check.Report.ResolveHash);
    }

    [Fact]
    public async Task EachEvidenceFileInTheFolder_IsDroppedAndSaidAsS07015()
    {
        var upload = await Folder();
        string[] evidence = ["lock.json", "publish-report.json", "publish.json", "resolve.json"];
        foreach (var path in evidence)
            upload[path] = Encoding.UTF8.GetBytes("{}");

        var outcome = await PackagePublish.CheckAsync(upload, Options());

        Assert.True(outcome.Report.Published, string.Join(" | ", outcome.Report.Refusals.Select(r => r.Sentence)));
        Assert.Equal(evidence, outcome.Dropped);
        foreach (var path in evidence)
        {
            var sentence = PackagePublishSentences.EvidenceCommitted(path);
            Assert.Equal("S07-015", sentence.Id);
            Assert.Equal($"{path} is evidence written only by publish; it is ignored and shouldn't be committed.", sentence.Sentence);
        }
    }

    [Fact]
    public async Task ARefusal_IsAReportThatIsNotPublished_ForExitOne()
    {
        var upload = await Folder();
        upload["manifest.json"] = Encoding.UTF8.GetBytes("{ \"specVersion\": 31, \"name\": 7 }");

        var outcome = await PackagePublish.CheckAsync(upload, Options());

        Assert.False(outcome.Report.Published);
        Assert.NotEmpty(outcome.Report.Refusals);
        Assert.Empty(outcome.Files);
    }

    [Fact]
    public async Task ARerun_OfWhatWasPublished_GivesTheStoredResolveHashAndOutcomeHashes_AndAnEditedStubDoesNot()
    {
        var upload = await Folder();
        var published = await PackagePublish.RunAsync(upload, Options());
        var stored = (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(published.Files.Single(f => f.Path == PublishReport.FileName).Bytes))!;

        // What the re-render rebuilds from: the stored source (evidence is dropped), checked in memory.
        var stored31 = published.Files.ToDictionary(f => f.Path, f => f.Bytes, StringComparer.Ordinal);
        stored31[PublishedStamp.FileName] = Encoding.UTF8.GetBytes(published.Stamp!.ToJson());
        var again = await PackagePublish.CheckAsync(stored31, Options());

        Assert.Equal((string?)stored["resolveHash"], again.Report.ResolveHash);
        var storedTest = stored["tests"]!.AsArray().Single()!;
        Assert.Equal((string?)storedTest["outcomeHash"], again.Report.Tests.Single().OutcomeHash);

        // An edit to the test's stubs: a difference (here the test fails, so the package is refused now).
        var edited = (JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(stored31[TestPath]))!;
        var first = edited["outputs"]!.AsObject().First();
        edited["outputs"]![first.Key] = "An edited stub.";
        stored31[TestPath] = Encoding.UTF8.GetBytes(edited.ToJsonString());
        var after = await PackagePublish.CheckAsync(stored31, Options());

        Assert.False(after.Report.Published && after.Report.Tests.Single().OutcomeHash == (string?)storedTest["outcomeHash"]);
    }

    // ---- the scripts' own text ----------------------------------------------------------------------------------------

    [Fact]
    public void TheCheckScript_ListsItsUsage_ItsExits_AndCallsThePipeline()
    {
        var script = Script("check-workflow-package-v31.cs");

        Assert.Contains("//   dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <folder> --write <out folder> [--version <tag>] [--published-by \"ci <tag>\"]", script, StringComparison.Ordinal);
        Assert.Contains("// Exit codes: 0 published", script, StringComparison.Ordinal);
        Assert.Contains("PackagePublish.RunAsync(upload, options)", script, StringComparison.Ordinal);
        Assert.Contains("PackagePublish.CheckAsync(upload, options)", script, StringComparison.Ordinal);
        Assert.Contains("PackagePublishSentences.EvidenceCommitted(path)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCheckScript_WritesTheStampAfterTheFiles_AndKeepsTheOutFolderWithoutATrailingSeparator()
    {
        var script = Script("check-workflow-package-v31.cs");

        var files = script.IndexOf("foreach (var file in outcome.Files)", StringComparison.Ordinal);
        var stamp = script.IndexOf("WriteInto(outRoot, PublishedStamp.FileName", StringComparison.Ordinal);
        Assert.True(files > 0 && stamp > files, "the stamp is written after the files");
        Assert.Contains("Path.TrimEndingDirectorySeparator(Path.GetFullPath(outFolder))", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.GetFullPath(outFolder)", script.Replace("Path.TrimEndingDirectorySeparator(Path.GetFullPath(outFolder))", ""), StringComparison.Ordinal);
        Assert.Contains("a stamped version is never written into", script, StringComparison.Ordinal);
        Assert.Contains("is not empty; write into a new or empty folder", script, StringComparison.Ordinal);
        Assert.Contains("manifest[\"version\"] = tag;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("manifest[\"name\"] =", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRerenderScript_ListsItsUsage_ItsExits_AndComparesBothHashes()
    {
        var script = Script("rerender-workflow-packages-v31.cs");

        Assert.Contains("//   dotnet run -v q --file scripts/rerender-workflow-packages-v31.cs -- <folder of name/version folders>", script, StringComparison.Ordinal);
        Assert.Contains("// Exit codes: 0 no difference; 1 at least one difference; 2 usage or environment, or no stamped 31 version found.", script, StringComparison.Ordinal);
        Assert.Contains("PackagePublish.CheckAsync(upload, options)", script, StringComparison.Ordinal);
        Assert.Contains("resolveHash was", script, StringComparison.Ordinal);
        Assert.Contains("outcomeHash was", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRerenderScript_WalksNestedNames_ToEveryFolderWithAStamp()
    {
        // An account package published with a slug is acct-<12hex>/<slug>/<version>, and a public name nests too
        // (test/general): the walk is not two levels. A folder holding publish.json is a version, its name the relative
        // path of its parent; a stamp that is not readable JSON is a difference, one whose specVersion is not 31 is skipped,
        // and a walk that checked nothing is exit 2, never "no difference".
        var script = Script("rerender-workflow-packages-v31.cs");

        Assert.DoesNotContain("Directory.EnumerateDirectories(nameFolder)", script, StringComparison.Ordinal);
        Assert.Contains("foreach (var versionFolder in StampedFolders(corpus))", script, StringComparison.Ordinal);
        Assert.Contains("var name = Relative(corpus, Path.GetDirectoryName(versionFolder)!);", script, StringComparison.Ordinal);
        Assert.Contains("var version = Path.GetFileName(versionFolder);", script, StringComparison.Ordinal);
        Assert.Contains("case StampKind.Unreadable:", script, StringComparison.Ordinal);
        Assert.Contains("is not readable or not valid JSON", script, StringComparison.Ordinal);
        Assert.Contains("case StampKind.Other:", script, StringComparison.Ordinal);
        Assert.Contains("if (checkedCount == 0)", script, StringComparison.Ordinal);
        Assert.Contains("no stamped 31 version found under", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("check-workflow-package-v31.cs")]
    [InlineData("rerender-workflow-packages-v31.cs")]
    public void TheScripts_SkipLinks_FilesAndFolders_AndSaySo(string name)
    {
        // A symbolic link (a reparse point) in the folder is never followed: --write would copy a link's target into
        // Release assets, and a re-render would read a file outside the version.
        var script = Script(name);

        Assert.DoesNotContain("SearchOption.AllDirectories", script, StringComparison.Ordinal);
        Assert.Contains("static bool IsLink(FileSystemInfo entry) =>", script, StringComparison.Ordinal);
        Assert.Contains("(entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget != null", script, StringComparison.Ordinal);
        Assert.Contains("// A symbolic link (or any reparse point), file or folder, is skipped", script, StringComparison.Ordinal);
    }
}
