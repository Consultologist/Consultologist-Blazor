using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Diagram;
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
    public async Task ACheck_GivesTheCompiledResolve_WhoseDrawingIsThePublishedDiagram()
    {
        // #1510 (area 13, D13): what the check script compares a committed dag.mmd with, and what --write-dag writes, is the
        // drawing of the check's in-memory resolve.json, which is the dag.mmd a publish with the drawing writes.
        var upload = await Folder();
        var check = await PackagePublish.CheckAsync(upload, Options());
        var publish = await PackagePublish.RunAsync(upload, Options() with { Diagram = PackageDiagram.Generate });

        Assert.True(check.Report.Published);
        var stored = Encoding.UTF8.GetString(publish.Files.Single(f => f.Path == PackagePublish.DiagramFile).Bytes);
        Assert.Equal(stored, PackageDiagram.Generate(check.Compiled!.Resolve));
    }

    [Fact]
    public void TheCheckScript_FailsOnAStaleCommittedDiagram_AndWritesItWithWriteDag()
    {
        // #1510 (area 13, D13; part 07 § 7): a committed dag.mmd that differs from a fresh one fails (exit 1); --write-dag
        // writes the fresh one into the folder; --write publishes the drawing with the version.
        var script = Script("check-workflow-package-v31.cs");

        Assert.Contains("//   dotnet run -v q --file scripts/check-workflow-package-v31.cs -- <folder> --write-dag", script, StringComparison.Ordinal);
        Assert.Contains("case \"--write-dag\":", script, StringComparison.Ordinal);
        Assert.Contains("Diagram: PackageDiagram.Generate", script, StringComparison.Ordinal);
        Assert.Contains("var fresh = PackageDiagram.Generate(outcome.Compiled!.Resolve);", script, StringComparison.Ordinal);
        Assert.Contains("dag.mmd differs from the drawing of the package's resolve.json", script, StringComparison.Ordinal);
        var check = script.IndexOf("var fresh = PackageDiagram.Generate", StringComparison.Ordinal);
        var written = script.IndexOf("foreach (var file in outcome.Files)", StringComparison.Ordinal);
        Assert.True(check > 0 && written > check, "the diagram is checked before a version is written");
    }

    [Fact]
    public void TheCheckScript_ReadsACommittedDiagramsCrlfAsLf_AndNeverWritesThroughALink()
    {
        // #1510 (final review): .gitattributes is text=auto, so a checkout may write dag.mmd with \r\n: the committed side
        // is compared with \r\n read as \n. --write-dag never follows a link, as the folder's read never does.
        var script = Script("check-workflow-package-v31.cs");

        Assert.Contains("Encoding.UTF8.GetString(committed).Replace(\"\\r\\n\", \"\\n\", StringComparison.Ordinal), fresh, StringComparison.Ordinal)", script, StringComparison.Ordinal);
        Assert.DoesNotContain("committed.AsSpan().SequenceEqual", script, StringComparison.Ordinal);
        var refusal = script.IndexOf("is a symbolic link, which this script never follows", StringComparison.Ordinal);
        var write = script.IndexOf("File.WriteAllBytes(dagPath", StringComparison.Ordinal);
        Assert.True(refusal > 0 && write > refusal, "a linked dag.mmd is refused before it is written");
        Assert.Contains("if (existing.LinkTarget != null || (existing.Exists && IsLink(existing)))", script, StringComparison.Ordinal);
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

    // ---- the bulk publish (Task 36) --------------------------------------------------------------------------------------

    private const string Owner1 = "0123456789abcdef0123456789abcdef";
    private const string Owner2 = "fedcba9876543210fedcba9876543210";
    private const string Letters = "acct-0123456789ab-letters";
    private const string Nested = "acct-0123456789ab/nested";
    private const string Other = "acct-fedcba987654-other";
    private const string A11001 = "Up to 80,614 steps could run in one job, through node.sections; this engine runs at most 10,000 for one job. Lower a loop's max or split the workflow.";
    private const string FlagNotice = "Node 'draft' kept a reply that did not fit its format (onInvalid: flag); 31 makes one more attempt with the validation error, then the node fails.";

    private static readonly Uri Engine = new("https://engine.example.test");

    /// <summary>An in-process engine: it answers <c>Admin/MigratePackage</c> as the route does, and records every request.</summary>
    private sealed class FakeEngine : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Authorization, JsonObject? Body)> Requests { get; } = [];

        public JsonObject List { get; set; } = new()
        {
            ["packages"] = new JsonArray(
                Row(Owner1, Letters, "v2026.11.3"), Row(Owner1, Nested, "v2026.11.2"), Row(Owner2, Other, "v2026.11.1")),
            ["notRead"] = 0,
        };

        /// <summary>The outcome for each package name; one not named publishes.</summary>
        public Dictionary<string, string> Outcomes { get; } = new(StringComparer.Ordinal) { [Nested] = "not migrated: " + A11001 };

        /// <summary>A name whose POST is answered with a 500.</summary>
        public string? Faulting { get; set; }

        /// <summary>Called with the package's name when its POST arrives, before it is answered.</summary>
        public Action<string>? OnPost { get; set; }

        private static JsonObject Row(string owner, string name, string version) =>
            new() { ["owner"] = owner, ["name"] = name, ["version"] = version, ["specVersion"] = 30 };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body));
            if (request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, List);

            var name = (string)body!["name"]!;
            OnPost?.Invoke(name);
            if (name == Faulting)
                return Json(HttpStatusCode.InternalServerError, new JsonObject { ["error"] = "boom" });
            var publish = (bool)body["publish"]!;
            var outcome = Outcomes.GetValueOrDefault(name) ?? (publish ? "published" : "would publish");
            var published = !outcome.StartsWith("not migrated: ", StringComparison.Ordinal);
            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["owner"] = body["owner"]!.DeepClone(),
                ["name"] = name,
                ["oldVersion"] = name == Letters ? "v2026.11.3" : name == Nested ? "v2026.11.2" : "v2026.11.1",
                ["newVersion"] = published ? "v2026.12.1" : null,
                ["outcome"] = outcome,
                ["handEditsLeft"] = new JsonArray(),
                ["notices"] = new JsonArray(name == Letters ? FlagNotice : MigrationReport.OneMessageEach),
                ["report"] = new JsonObject { ["outcome"] = published ? "published" : "refused" },
                ["options"] = new JsonObject { ["renames"] = new JsonObject(), ["whenEmpty"] = new JsonObject(), ["folderFormats"] = new JsonObject() },
                ["defaults"] = body["options"] is null ? MigrationOptionsFile.Defaults : null,
            });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, JsonObject body) =>
            new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    /// <summary>
    /// An operator's folder: <c>letters</c> has its options.json and hand edits (one a <c>.deleted</c>), <c>nested</c> (a
    /// nested name) only its options.json, <c>other</c> no folder at all.
    /// </summary>
    private static string OperatorFolder()
    {
        var dir = Directory.CreateTempSubdirectory("bulk-publish-").FullName;
        var letters = Path.Combine(dir, Owner1, Letters);
        Directory.CreateDirectory(Path.Combine(letters, "hand-edits", "nodes"));
        File.WriteAllText(Path.Combine(letters, "options.json"), """{ "renames": { "parts": "sections" }, "whenEmpty": {}, "folderFormats": {} }""");
        File.WriteAllText(Path.Combine(letters, "hand-edits", "nodes", "draft.md"), "Write the letter for Dr A. Sample.\n");
        File.WriteAllText(Path.Combine(letters, "hand-edits", "nodes", "old.md.deleted"), "");
        var nested = Path.Combine([dir, Owner1, .. Nested.Split('/')]);
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "options.json"), """{ "renames": {}, "whenEmpty": { "note": "hold" }, "folderFormats": {} }""");
        return dir;
    }

    private static async Task<(int Exit, string Output, string Error)> BulkAsync(FakeEngine engine, string folder, bool publish, string? token = "operator-token")
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        using var http = new HttpClient(engine);
        var exit = await BulkPublishScript.RunAsync(new BulkPublishArguments(folder, Engine, publish), http, token, new FixedClock(Now), output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private static JsonObject ReadJson(string folder, string file) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(folder, file), Encoding.UTF8))!.AsObject();

    [Fact]
    public async Task TheBulkPublish_SendsEachPackageWithItsFolder_AndWritesTheRecordAndTheOwnersList()
    {
        var engine = new FakeEngine();
        var folder = OperatorFolder();
        try
        {
            var (exit, output, error) = await BulkAsync(engine, folder, publish: true);

            // one package not migrated: exit 1
            Assert.Equal(BulkPublishScript.NotMigratedExit, exit);
            Assert.Equal("", error);
            Assert.Equal(
                $"{Owner1} {Letters}@v2026.11.3: published\n{Owner1} {Nested}@v2026.11.2: not migrated: {A11001}\n{Owner2} {Other}@v2026.11.1: published\n"
                + "2 published, 0 would publish, 1 not migrated; written bulk-publish-record.json and owners.json.\n",
                output);

            // it talks only to the engine it is given, with the operator's token, the list first
            Assert.All(engine.Requests, r => Assert.Equal(new Uri("https://engine.example.test/api/Admin/MigratePackage"), r.Uri));
            Assert.All(engine.Requests, r => Assert.Equal("Bearer operator-token", r.Authorization));
            Assert.Equal([HttpMethod.Get, HttpMethod.Post, HttpMethod.Post, HttpMethod.Post], engine.Requests.Select(r => r.Method));

            // each package's folder: its options.json and its hand edits (a .deleted file as null); none for no folder
            var letters = engine.Requests[1].Body!;
            Assert.Equal(["owner", "name", "options", "handEdits", "publish"], letters.Select(p => p.Key));
            Assert.Equal("""{"renames":{"parts":"sections"},"whenEmpty":{},"folderFormats":{}}""", letters["options"]!.ToJsonString());
            Assert.Equal("""{"nodes/draft.md":"Write the letter for Dr A. Sample.\n","nodes/old.md":null}""", letters["handEdits"]!.ToJsonString());
            Assert.True((bool)letters["publish"]!);
            Assert.Equal(Nested, (string?)engine.Requests[2].Body!["name"]);
            Assert.Equal("""{}""", engine.Requests[2].Body!["handEdits"]!.ToJsonString());
            var other = engine.Requests[3].Body!;
            Assert.Null(other["options"]);
            Assert.Equal("""{}""", other["handEdits"]!.ToJsonString());

            // the record: one row per package, the engine's answer with the time
            var record = ReadJson(folder, BulkPublishScript.RecordFile);
            Assert.Equal(["engine", "publish", "notRead", "rows"], record.Select(p => p.Key));
            Assert.True((bool)record["publish"]!);
            var rows = record["rows"]!.AsArray();
            Assert.Equal(3, rows.Count);
            Assert.Equal(["owner", "name", "oldVersion", "newVersion", "outcome", "handEditsLeft", "notices", "report", "options", "defaults", "time"],
                rows[0]!.AsObject().Select(p => p.Key));
            Assert.Equal("2026-12-01T09:30:00.000Z", (string?)rows[0]!["time"]);
            Assert.Null((string?)rows[0]!["defaults"]);
            Assert.Equal(MigrationOptionsFile.Defaults, (string?)rows[2]!["defaults"]);

            // the owners' list: per owner, what was published with its notices, and what was not with the reason and the deadline
            var owners = ReadJson(folder, BulkPublishScript.OwnersFile);
            Assert.Equal([Owner1, Owner2], owners.Select(p => p.Key));
            var first = owners[Owner1]!;
            var published = Assert.Single(first["published"]!.AsArray())!;
            Assert.Equal(Letters, (string?)published["name"]);
            Assert.Equal("v2026.12.1", (string?)published["newVersion"]);
            Assert.Equal([FlagNotice], published["notices"]!.AsArray().Select(n => (string)n!));
            var notMigrated = Assert.Single(first["notMigrated"]!.AsArray())!;
            Assert.Equal(["name", "version", "reason", "notice", "deadline"], notMigrated.AsObject().Select(p => p.Key));
            Assert.Equal(Nested, (string?)notMigrated["name"]);
            Assert.Equal("v2026.11.2", (string?)notMigrated["version"]);
            Assert.Equal(A11001, (string?)notMigrated["reason"]);
            Assert.Equal(PackagePublishSentences.StaysAt30(Nested, A11001.TrimEnd('.')).Sentence, (string?)notMigrated["notice"]);
            Assert.Equal("This package stops working when specVersion 30 is retired.", (string?)notMigrated["deadline"]);
            V31Contract.AssertSentence(new SourceRefusal("A11-015", (string)notMigrated["deadline"]!, ""));
            Assert.Empty(owners[Owner2]!["notMigrated"]!.AsArray());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutPublish_ItIsADryRun_AndExits0WhenEveryPackageWouldPublish()
    {
        var engine = new FakeEngine();
        engine.Outcomes.Clear();
        var folder = OperatorFolder();
        try
        {
            var (exit, output, error) = await BulkAsync(engine, folder, publish: false);

            Assert.Equal(BulkPublishScript.Clean, exit);
            Assert.Equal("", error);
            Assert.All(engine.Requests.Skip(1), r => Assert.False((bool)r.Body!["publish"]!));
            Assert.EndsWith("0 published, 3 would publish, 0 not migrated (dry run; pass --publish to publish); written bulk-publish-record.json and owners.json.\n", output, StringComparison.Ordinal);
            Assert.False((bool)ReadJson(folder, BulkPublishScript.RecordFile)["publish"]!);
            Assert.Equal("would publish", (string?)ReadJson(folder, BulkPublishScript.OwnersFile)[Owner2]!["published"]![0]!["outcome"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private const string A11017 = "The engine did not answer for this package; check whether it was published before sending this list.";

    [Fact]
    public async Task AFaultOnTheSecondOfThree_KeepsTheFirstsRow_RecordsTheSecondAsUnknown_AndSendsNothingForTheThird()
    {
        // Final review of Part F, finding 1: both files are written after every answered row, so when the second request
        // arrives the first's row is on disk and readable; the in-flight package of a fault is recorded "unknown: <fault>"
        // (it may have been published), the run stops with exit 2, and owners.json names everything known.
        var engine = new FakeEngine { Faulting = Nested };
        engine.Outcomes.Clear();
        var folder = OperatorFolder();
        JsonArray? onDiskAtSecond = null;
        JsonObject? ownersAtSecond = null;
        engine.OnPost = name =>
        {
            if (name != Nested)
                return;
            onDiskAtSecond = ReadJson(folder, BulkPublishScript.RecordFile)["rows"]!.AsArray();
            ownersAtSecond = ReadJson(folder, BulkPublishScript.OwnersFile);
        };
        try
        {
            var (exit, _, error) = await BulkAsync(engine, folder, publish: true);

            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.Contains($"error: POST https://engine.example.test/api/Admin/MigratePackage for {Owner1} {Nested}: 500 boom", error, StringComparison.Ordinal);

            // what was on disk when the second request arrived: the first's row
            Assert.NotNull(onDiskAtSecond);
            Assert.Equal([(Letters, "published")], onDiskAtSecond!.Select(r => ((string)r!["name"]!, (string)r["outcome"]!)));
            Assert.Equal([Letters], ownersAtSecond![Owner1]!["published"]!.AsArray().Select(p => (string)p!["name"]!));

            // the run stops at the fault: the third package is never sent
            Assert.Equal(3, engine.Requests.Count);
            var rows = ReadJson(folder, BulkPublishScript.RecordFile)["rows"]!.AsArray();
            Assert.Equal([(Letters, "published"), (Nested, "unknown: the engine answered 500")],
                rows.Select(r => ((string)r!["name"]!, (string)r["outcome"]!)));
            Assert.Equal(["owner", "name", "oldVersion", "outcome", "time"], rows[1]!.AsObject().Select(p => p.Key));
            Assert.Equal("v2026.11.2", (string?)rows[1]!["oldVersion"]);
            Assert.DoesNotContain("boom", rows.ToJsonString(), StringComparison.Ordinal);

            var owners = ReadJson(folder, BulkPublishScript.OwnersFile);
            Assert.Equal([Owner1], owners.Select(p => p.Key));
            Assert.Equal(["published", "notMigrated", "unknown"], owners[Owner1]!.AsObject().Select(p => p.Key));
            Assert.Equal([Letters], owners[Owner1]!["published"]!.AsArray().Select(p => (string)p!["name"]!));
            var unknown = Assert.Single(owners[Owner1]!["unknown"]!.AsArray())!;
            Assert.Equal(["name", "notice"], unknown.AsObject().Select(p => p.Key));
            Assert.Equal(Nested, (string?)unknown["name"]);
            Assert.Equal(A11017, (string?)unknown["notice"]);
            Assert.Equal(PackagePublishSentences.NotAnswered, A11017);
            V31Contract.AssertSentence(new SourceRefusal("A11-017", A11017, ""));
            Assert.False(File.Exists(Path.Combine(folder, BulkPublishScript.RecordFile + ".tmp")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ACarriedUnknownRow_StaysInTheOwnersList_AndTheRunExits2UntilThePackageIsAnswered()
    {
        var folder = OperatorFolder();
        try
        {
            var engine = new FakeEngine { Faulting = Other };
            engine.Outcomes.Clear();
            await BulkAsync(engine, folder, publish: true);

            // a rerun the engine no longer lists the package for: the unknown row is carried, and the run exits 2
            var rerun = new FakeEngine { List = Listing() };
            var (exit, _, error) = await BulkAsync(rerun, folder, publish: true);
            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.Contains($"error: {Owner2} {Other} is unknown in the record", error, StringComparison.Ordinal);
            var rows = ReadJson(folder, BulkPublishScript.RecordFile)["rows"]!.AsArray();
            Assert.Equal([Letters, Nested, Other], rows.Select(r => (string)r!["name"]!));
            Assert.StartsWith("unknown: ", (string?)rows[2]!["outcome"], StringComparison.Ordinal);
            Assert.Equal([Other], ReadJson(folder, BulkPublishScript.OwnersFile)[Owner2]!["unknown"]!.AsArray().Select(p => (string)p!["name"]!));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ARecordFromAnotherEngine_Exits2_NeitherFileChanged_AndNothingIsSent()
    {
        var folder = OperatorFolder();
        try
        {
            var record = """{ "engine": "https://dev.example.test/", "publish": true, "notRead": 0, "rows": [] }""";
            File.WriteAllText(Path.Combine(folder, BulkPublishScript.RecordFile), record);
            File.WriteAllText(Path.Combine(folder, BulkPublishScript.OwnersFile), "kept");
            var engine = new FakeEngine();

            var (exit, _, error) = await BulkAsync(engine, folder, publish: true);

            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.Contains("was written for the engine https://dev.example.test/, not https://engine.example.test/", error, StringComparison.Ordinal);
            Assert.Empty(engine.Requests);
            Assert.Equal(record, File.ReadAllText(Path.Combine(folder, BulkPublishScript.RecordFile)));
            Assert.Equal("kept", File.ReadAllText(Path.Combine(folder, BulkPublishScript.OwnersFile)));

            // the same engine, written without the trailing '/', is this engine
            File.WriteAllText(Path.Combine(folder, BulkPublishScript.RecordFile), record.Replace("https://dev.example.test/", "https://engine.example.test", StringComparison.Ordinal));
            (exit, _, _) = await BulkAsync(engine, folder, publish: true);
            Assert.Equal(BulkPublishScript.NotMigratedExit, exit);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task TwoCarriedRowsForOnePackage_GiveOne_TheLatest()
    {
        var folder = OperatorFolder();
        try
        {
            JsonObject Published(string name, string newVersion) => new()
            {
                ["owner"] = Owner1, ["name"] = name, ["oldVersion"] = "v2026.11.3", ["newVersion"] = newVersion,
                ["outcome"] = "published", ["notices"] = new JsonArray(), ["time"] = "2026-11-30T09:00:00.000Z",
            };
            var record = new JsonObject
            {
                ["engine"] = Engine.AbsoluteUri, ["publish"] = true, ["notRead"] = 0,
                ["rows"] = new JsonArray(Published(Letters, "v2026.12.1"), Published(Nested, "v2026.12.1"), Published(Letters, "v2026.12.2")),
            };
            File.WriteAllText(Path.Combine(folder, BulkPublishScript.RecordFile), record.ToJsonString());
            var engine = new FakeEngine { List = Listing((Owner2, Other, "v2026.11.1")) };

            var (exit, _, _) = await BulkAsync(engine, folder, publish: true);

            Assert.Equal(BulkPublishScript.Clean, exit);
            var rows = ReadJson(folder, BulkPublishScript.RecordFile)["rows"]!.AsArray();
            Assert.Equal([(Nested, "v2026.12.1"), (Letters, "v2026.12.2"), (Other, "v2026.12.1")],
                rows.Select(r => ((string)r!["name"]!, (string)r["newVersion"]!)));
            Assert.Equal([Nested, Letters], ReadJson(folder, BulkPublishScript.OwnersFile)[Owner1]!["published"]!.AsArray().Select(p => (string)p!["name"]!));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AFolderThatMatchesNoPackage_Exits2_NothingSent_NeitherFileWritten()
    {
        // Final review of Part F, finding 4: a misspelt <owner>/<name> folder would leave the real package at the defaults.
        var folder = OperatorFolder();
        try
        {
            var misspelt = Path.Combine(folder, Owner2, "acct-fedcba987654-othr");
            Directory.CreateDirectory(misspelt);
            File.WriteAllText(Path.Combine(misspelt, "options.json"), """{ "renames": {}, "whenEmpty": { "note": "hold" }, "folderFormats": {} }""");
            var edits = Path.Combine(folder, Owner1, "acct-0123456789ab", "nestd", "hand-edits");
            Directory.CreateDirectory(edits);
            var engine = new FakeEngine();

            var (exit, _, error) = await BulkAsync(engine, folder, publish: true);

            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.Contains($"error: {Path.Combine(folder, Owner1, "acct-0123456789ab", "nestd")} matches no package", error, StringComparison.Ordinal);
            Assert.Contains($"error: {misspelt} matches no package", error, StringComparison.Ordinal);
            Assert.Equal([HttpMethod.Get], engine.Requests.Select(r => r.Method));
            Assert.False(File.Exists(Path.Combine(folder, BulkPublishScript.RecordFile)));
            Assert.False(File.Exists(Path.Combine(folder, BulkPublishScript.OwnersFile)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static JsonObject Listing(params (string Owner, string Name, string Version)[] packages) => new()
    {
        ["packages"] = new JsonArray([.. packages.Select(p => (JsonNode?)new JsonObject
        {
            ["owner"] = p.Owner, ["name"] = p.Name, ["version"] = p.Version, ["specVersion"] = 30,
        })]),
        ["notRead"] = 0,
    };

    [Fact]
    public async Task ARerunAfterAFault_CarriesForwardWhatWasPublished_AndItsOwnersStayInTheList()
    {
        // Fix round 1, ruling (3): A and B publish, C faults (exit 2); the rerun lists only C (A and B are at 31 now), and
        // the record and the owners' list still hold A and B.
        var folder = OperatorFolder();
        try
        {
            var engine = new FakeEngine { Faulting = Other };
            engine.Outcomes.Clear();
            var (exit, _, _) = await BulkAsync(engine, folder, publish: true);
            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.Equal(["published", "published", "unknown: the engine answered 500"],
                ReadJson(folder, BulkPublishScript.RecordFile)["rows"]!.AsArray().Select(r => (string)r!["outcome"]!));

            // the rerun answers the unknown package: its answer replaces the unknown row
            var rerun = new FakeEngine { List = Listing((Owner2, Other, "v2026.11.1")) };
            string output;
            (exit, output, _) = await BulkAsync(rerun, folder, publish: true);

            Assert.Equal(BulkPublishScript.Clean, exit);
            Assert.EndsWith("1 published, 0 would publish, 0 not migrated; 2 published by an earlier run carried forward; written bulk-publish-record.json and owners.json.\n", output, StringComparison.Ordinal);
            var rows = ReadJson(folder, BulkPublishScript.RecordFile)["rows"]!.AsArray();
            Assert.Equal([Letters, Nested, Other], rows.Select(r => (string)r!["name"]!));
            Assert.All(rows, r => Assert.Equal("published", (string?)r!["outcome"]));
            var owners = ReadJson(folder, BulkPublishScript.OwnersFile);
            Assert.Equal([Owner1, Owner2], owners.Select(p => p.Key));
            Assert.Equal([Letters, Nested], owners[Owner1]!["published"]!.AsArray().Select(p => (string)p!["name"]!));
            Assert.Empty(owners[Owner2]!["unknown"]!.AsArray());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ADryRunAfterAPublish_KeepsThePublishedRows()
    {
        var folder = OperatorFolder();
        try
        {
            var engine = new FakeEngine();
            engine.Outcomes.Clear();
            await BulkAsync(engine, folder, publish: true);

            var dry = new FakeEngine { List = Listing((Owner2, "acct-fedcba987654-second", "v2026.11.4")) };
            var (exit, _, _) = await BulkAsync(dry, folder, publish: false);

            Assert.Equal(BulkPublishScript.Clean, exit);
            var rows = ReadJson(folder, BulkPublishScript.RecordFile)["rows"]!.AsArray();
            Assert.Equal(["published", "published", "published", "would publish"], rows.Select(r => (string)r!["outcome"]!));
            Assert.False((bool)ReadJson(folder, BulkPublishScript.RecordFile)["publish"]!);
            Assert.Equal(2, ReadJson(folder, BulkPublishScript.OwnersFile)[Owner2]!["published"]!.AsArray().Count);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AnEarlierRecordThatDoesNotRead_Exits2_NeitherFileOverwritten_AndNothingIsSent()
    {
        var folder = OperatorFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, BulkPublishScript.RecordFile), """{ "rows": [1] }""");
            File.WriteAllText(Path.Combine(folder, BulkPublishScript.OwnersFile), "kept");
            var engine = new FakeEngine();

            var (exit, _, error) = await BulkAsync(engine, folder, publish: true);

            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.Contains("is not a bulk publish record", error, StringComparison.Ordinal);
            Assert.Empty(engine.Requests);
            Assert.Equal("""{ "rows": [1] }""", File.ReadAllText(Path.Combine(folder, BulkPublishScript.RecordFile)));
            Assert.Equal("kept", File.ReadAllText(Path.Combine(folder, BulkPublishScript.OwnersFile)));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AListThatDoesNotAnswer_OrAFolderThatDoesNotRead_Exits2_AndNothingIsSent()
    {
        var folder = OperatorFolder();
        try
        {
            var engine = new FakeEngine { List = new JsonObject { ["error"] = "not a list" } };
            var (exit, _, error) = await BulkAsync(engine, folder, publish: true);
            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.StartsWith("error: GET https://engine.example.test/api/Admin/MigratePackage did not answer the list", error, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(folder, BulkPublishScript.RecordFile)));

            // an options.json that does not read is said before anything is sent
            File.WriteAllText(Path.Combine([folder, Owner1, .. Nested.Split('/'), "options.json"]), """{ "renames": {}, "renames": {} }""");
            engine = new FakeEngine();
            (exit, _, error) = await BulkAsync(engine, folder, publish: true);
            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.Contains("does not read: it names the member 'renames' twice", error, StringComparison.Ordinal);
            Assert.Equal([HttpMethod.Get], engine.Requests.Select(r => r.Method));
            Assert.False(File.Exists(Path.Combine(folder, BulkPublishScript.RecordFile)));

            // a name that would leave the folder is never read
            engine = new FakeEngine();
            engine.List["packages"] = new JsonArray(new JsonObject { ["owner"] = Owner1, ["name"] = "acct-0123456789ab/../../x", ["version"] = "v1", ["specVersion"] = 30 });
            (exit, _, error) = await BulkAsync(engine, folder, publish: true);
            Assert.Equal(BulkPublishScript.Fault, exit);
            Assert.Contains("without a usable owner and name", error, StringComparison.Ordinal);
            Assert.Single(engine.Requests);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task AccountPackagesThatCouldNotBeRead_Exit1_AndAreSaid()
    {
        var engine = new FakeEngine();
        engine.Outcomes.Clear();
        engine.List["notRead"] = 2;
        var folder = OperatorFolder();
        try
        {
            var (exit, _, error) = await BulkAsync(engine, folder, publish: true);

            Assert.Equal(BulkPublishScript.NotMigratedExit, exit);
            Assert.Equal("warning: 2 of the accounts' active packages could not be read and are not in the record.\n", error);
            Assert.Equal(2, (int)ReadJson(folder, BulkPublishScript.RecordFile)["notRead"]!);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(new[] { "folder" }, "--engine is not given.")]
    [InlineData(new[] { "--engine", "https://engine.example.test" }, "The operator folder is not given.")]
    [InlineData(new[] { "folder", "--engine", "engine.example.test" }, "--engine takes the engine's URL (https://<host>), with no query.")]
    [InlineData(new[] { "folder", "--engine", "https://engine.example.test/?code=x" }, "--engine takes the engine's URL (https://<host>), with no query.")]
    [InlineData(new[] { "folder", "--engine", "http://engine.example.test" }, "--engine takes https:// (http:// only for localhost, 127.0.0.1 or [::1]): the token is sent to it.")]
    [InlineData(new[] { "folder", "--engine", "http://127.0.0.2" }, "--engine takes https:// (http:// only for localhost, 127.0.0.1 or [::1]): the token is sent to it.")]
    [InlineData(new[] { "folder", "--engine", "ftp://engine.example.test" }, "--engine takes the engine's URL (https://<host>), with no query.")]
    [InlineData(new[] { "folder", "--engine", "https://a.test", "--engine", "https://b.test" }, "--engine is given twice.")]
    [InlineData(new[] { "folder", "other", "--engine", "https://a.test" }, "'other' is a second operator folder.")]
    [InlineData(new[] { "folder", "--engine", "https://a.test", "--write" }, "--write is not an option of the bulk publish.")]
    public void TheBulkPublishsArguments_AreRefusedWithAReason(string[] args, string usage)
    {
        Assert.Equal((null, usage), BulkPublishScript.Parse(args));
    }

    [Fact]
    public void TheBulkPublishScript_IsTheLibrarysRun()
    {
        Assert.Equal((new BulkPublishArguments("folder", new Uri("https://engine.example.test"), true), null),
            BulkPublishScript.Parse(["folder", "--engine", "https://engine.example.test", "--publish"]));
        Assert.Equal((new BulkPublishArguments("folder", new Uri("https://a.test"), false), null),
            BulkPublishScript.Parse(["--engine", "https://a.test", "folder"]));
        // http:// only to this machine (final review of Part F, item 7: the bearer token is sent to the engine)
        foreach (var local in new[] { "http://localhost:7071", "http://127.0.0.1:7071", "http://[::1]:7071" })
            Assert.Equal((new BulkPublishArguments("folder", new Uri(local), false), null), BulkPublishScript.Parse(["folder", "--engine", local]));

        var script = Script("bulk-publish-v31.cs");
        Assert.Contains("BulkPublishScript.Parse(args)", script, StringComparison.Ordinal);
        Assert.Contains("Environment.GetEnvironmentVariable(BulkPublishScript.TokenVariable)", script, StringComparison.Ordinal);
        Assert.Contains("return await BulkPublishScript.RunAsync(", script, StringComparison.Ordinal);
        Assert.Contains("return BulkPublishScript.Fault;", script, StringComparison.Ordinal);
        Assert.Equal("ACCESS_TOKEN", BulkPublishScript.TokenVariable);
    }
}
