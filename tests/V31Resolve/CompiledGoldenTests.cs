using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Publish;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): the compiled documents of four packages frozen as goldens (part 07 § 1: the same
/// source and the same toolchain give the same bytes; § 4.3). <see cref="CompiledPackage.Publish"/> on
/// <see cref="ResolveFixture.NestedNote"/>, <see cref="ResolveFixture.Conditional"/>,
/// <see cref="ResolveFixture.Functions"/> and (#1505, area 08) <see cref="ResolveFixture.CheckedNote"/> with
/// <see cref="Fixed"/>, the toolchain written by hand so that a machine's patch level never moves a golden, gives exactly
/// the bytes of <c>Golden/&lt;name&gt;.resolve.json</c> and <c>Golden/&lt;name&gt;.lock.json</c>: canonical, one line, no final newline. With <c>GOLDEN_UPDATE_V31=1</c> the test
/// rewrites them instead; a rewritten golden is read before it is committed. What each body reads is what the dialect
/// says (#1504, area 07, D18: no stub). The same cases are the conformance family <c>compiled-documents</c>
/// (<see cref="CompiledDocumentsExport"/>), which the pinned content repo must hold as published here. #1508 (area 11): each
/// case is published against the limits it states (<see cref="GoldenCase.Limits"/>), and its folder also holds the publish
/// report and the stamp <see cref="PackagePublish.RunAsync"/> writes, at a fixed time on a clock that does not move.
/// </summary>
public class CompiledGoldenTests
{
    /// <summary>
    /// One golden: its file name, the package, its <c>about</c> and (#1508, area 11) the publish limits it is published
    /// against, which its <c>about.txt</c> states (none for a case another test builds only to run it).
    /// </summary>
    internal sealed record GoldenCase(string Name, Func<Dictionary<string, byte[]>> Source, string About, PublishLimits? Limits = null);

    /// <summary>
    /// #1508 (area 11): the engine's default publish limits (<c>RunLimits</c>' starting values: 16 agent calls at once, 200
    /// items a loop, 500 agent calls, 10,000 instances and 30 minutes a job), which a case is published against unless it
    /// says otherwise.
    /// </summary>
    internal static readonly PublishLimits EngineLimits = new(16, 200, 500, Instances: 10_000, JobMinutes: 30);

    /// <summary>
    /// #1508 (area 11): nested-note's limits: the engine's, but 100,000 instances a job, since its worst case (two loops over
    /// lists known only at run time, each up to 200 items) opens 80,614 and the default 10,000 would refuse it (A11-001);
    /// so it publishes, with the ceiling note among its warnings.
    /// </summary>
    internal static readonly PublishLimits NestedNoteLimits = EngineLimits with { Instances = 100_000 };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>
    /// The goldens' toolchain (D9's members, by hand): the release and catalogues of v2026.12.1, Scriban 7.2.5, Markdig
    /// 0.41.3, both Corvus libraries 5.8.12, .NET 10.0.0 and Unicode 16.0; the dialect and the evaluator are this
    /// library's own constants, which move a golden only when they change.
    /// </summary>
    public static readonly Toolchain Fixed = new(
        "package-format@v2026.12.1",
        Toolchain.TemplatesDialect,
        new Dictionary<string, string>
        {
            ["Corvus.JsonSchema"] = "5.8.12",
            ["Corvus.Text.Json.JsonLogic"] = "5.8.12",
            ["Markdig"] = "0.41.3",
            ["Scriban"] = "7.2.5",
        },
        JsonLogicOperations.EvaluatorVersion,
        "10.0.0",
        "16.0",
        "formats@v2026.12.1",
        "agents@v2026.12.1",
        null);

    /// <summary>The goldens, by name.</summary>
    internal static IReadOnlyList<GoldenCase> Cases() =>
    [
        new("nested-note",
            () => WithTest(Named(ResolveFixture.NestedNote(), "example-nested-note", "Nested note demo"), "two_sections", NestedNoteTest),
            "example-nested-note: an agent planning the sections, a computed count, the loop sections over the plan's "
            + "sections holding an outline and the loop paragraphs over its points, and the while refine of a summary and "
            + "its judge, after chapter 4 § 4.2.8 and § 4.3.5, with the package formats of chapter 7 § 7.2.9. #1508 (area 11): "
            + "the test two_sections plans two sections of three points in all and refines the summary twice; it also stubs "
            + "node.sections.outline:3, a third section that is not planned, so the report warns that the stub is not used.",
            NestedNoteLimits),
        new("conditional",
            () => Named(ResolveFixture.Conditional(), "example-conditional-sections", "Conditional sections demo"),
            "example-conditional-sections: a required list input of sections, each with a title and a kind; a loop "
            + "routing each section by a case on its kind (the omit branch empty), a loop holding an if on the kind, and "
            + "a loop holding an if on the section's id and a boolean input, after chapter 4 § 4.5.4.",
            EngineLimits),
        new("functions",
            () => Named(ResolveFixture.Functions(), "example-functions", "Functions demo"),
            "example-functions, after chapter 4 § 4.7.11 (with an agent plan and a second agent extracting terms, which "
            + "the example does not have): the function draft_section of three agents, called in a loop over the "
            + "snippet folder standards; the check loop section_coverage over the loop's .items and the check "
            + "all_sections; and the deliverable deliverable.emr.consult, gated by all_sections, in the delivery folder "
            + "emr, which delivers to emr and email as one zip.",
            EngineLimits),
        new("checked-note",
            () => Named(ResolveFixture.CheckedNote(), "example-checked-note", "Checked note demo"),
            "example-checked-note, chapter 6 § 6.3.4's package cut down: an agent planning the sections, the loop sections "
            + "drafting each with its planned terms and the terms it covers; the check loop part_coverage over the loop's "
            + ".items, the aggregate all_sections_covered over its .items, and the check proofread, which reads the "
            + "composed note's .content and gates it; the note (markdown_block, delivered to emr, failing when empty) and an "
            + "ungated patient letter (plaintext) in the delivery folder patient, delivered by email and pdf and held when "
            + "empty.",
            EngineLimits),
        new("macros",
            () => WithTest(Named(ResolveFixture.Macros(), "example-macros", "Macros demo"), "close_line", MacrosTest),
            "example-macros (#1508): an agent planning the sections; the classifier classify, a judgement on the plan; the "
            + "run-dependent rendered node stamp, a run spot on run.date; the agent draft, reading stamp and placing the part "
            + "close, whose text places the macro hpi of the folder section_notes (the folder and the macro each decided on "
            + "the classifier's answer); and the deliverable, reading the draft. The agent terms "
            + "returns a concept_list, which the rendered node terms_text reads through the built-in renderer renderer.concept_bullets. "
            + "#1508 (area 11): the test close_line answers the classifier yes, so the closing line is placed, on a fixed run date.",
            EngineLimits),
    ];

    public static TheoryData<string> Names() => new(Cases().Select(c => c.Name));

    /// <summary><paramref name="files"/> with the manifest's <c>name</c> and <c>title</c> set.</summary>
    private static Dictionary<string, byte[]> Named(Dictionary<string, byte[]> files, string name, string title) =>
        files.WithJson("manifest.json", m =>
        {
            m["name"] = name;
            m["title"] = title;
        });

    /// <summary><paramref name="files"/> with the test <paramref name="id"/> (<c>tests/&lt;id&gt;.json</c>, <paramref name="json"/>) listed in <c>tests/index.json</c>.</summary>
    private static Dictionary<string, byte[]> WithTest(Dictionary<string, byte[]> files, string id, string json) =>
        files.With("tests/index.json", SourceFixture.Index([id])).With($"tests/{id}.json", json);

    /// <summary>
    /// #1508 (area 11): nested-note's test. Fictional (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic): two planned
    /// sections, of two points and one; the summary judged "revise", then "good"; and a stub for a third section's outline,
    /// which is not planned, so it does not run (S07-014). It pins the loops, the count, and the note by hash.
    /// </summary>
    private const string NestedNoteTest = """
        {
          "description": ["Two planned sections of three points in all; the summary is refined once; a third section's outline is stubbed and never runs."],
          "inputs": { "consult_draft": "Margaret Okafor, 58, seen by Dr A. Sample at Sample Oncology Clinic after treatment; tired for three months (fictional)." },
          "profile": {},
          "run": {},
          "outputs": {
            "node.plan": { "sections": [{ "title": "History", "kind": "narrative" }, { "title": "Plan", "kind": "narrative" }] },
            "node.sections.outline:1": { "points": ["Tired for three months.", "No weight change."] },
            "node.sections.outline:2": { "points": ["Review in six weeks."] },
            "node.sections.outline:3": { "points": ["Never planned."] },
            "node.sections.paragraphs.draft_paragraph:1/1": "Ms Okafor has been tired for three months (fictional).",
            "node.sections.paragraphs.draft_paragraph:1/2": "Her weight has not changed (fictional).",
            "node.sections.paragraphs.draft_paragraph:2/1": "Dr A. Sample will review her in six weeks (fictional).",
            "node.refine.draft_summary#1": "Tired; review planned.",
            "node.refine.judge#1": { "verdict": "revise" },
            "node.refine.draft_summary#2": "Tired for three months; review at Sample Oncology Clinic in six weeks (fictional).",
            "node.refine.judge#2": { "verdict": "good" }
          },
          "expect": {
            "loops": { "node.sections": { "count": 2 }, "node.refine": { "passes": 2, "stopped": "check" } },
            "values": { "node.section_count": 2 },
            "deliverables": { "deliverable.note": { "state": "released", "hash": "sha256:e2ad3b9ec9aa1f147b19d1b2462d20c1b2c323e05f6be93a2ee16ac7fd4caa64" } }
          }
        }

        """;

    /// <summary>
    /// #1508 (area 11): macros' test. Fictional (Margaret Okafor, Dr A. Sample): the classifier answers yes, so the folder
    /// section_notes and its macro hpi are placed; the run date is fixed, as the stamp reads it. The note is pinned by hash.
    /// </summary>
    private const string MacrosTest = """
        {
          "description": ["The classifier answers yes, so the closing history line is placed; the run date is fixed."],
          "inputs": { "consult_draft": "Margaret Okafor, 58, seen by Dr A. Sample at Sample Oncology Clinic for a routine review (fictional)." },
          "profile": {},
          "run": { "date": { "value": "2026-11-16", "timeZone": "America/Toronto" } },
          "outputs": {
            "node.plan": { "sections": [{ "title": "History", "kind": "narrative" }] },
            "node.classify": { "answer": true, "reason": "The plan has a history section (fictional)." },
            "node.terms": { "concepts": [{ "id": "fictional-1", "term": "Fatigue", "type": "finding", "source": "consult_draft", "support": "tired for three months", "isActive": true, "isSnomedConcept": false }] },
            "node.draft": "Ms Okafor was seen for a routine review (fictional)."
          },
          "expect": {
            "ran": ["node.classify", "node.draft", "node.stamp"],
            "deliverables": { "deliverable.note": { "state": "released", "hash": "sha256:8dcf6c28610231fc2b1b69d1f2c2b50e155b57630353c179cfea42758164e5c0" } }
          }
        }

        """;

    /// <summary>#1508 (area 11): the time the cases are published at, <c>2026-12-01T09:00:00Z</c>.</summary>
    internal static readonly DateTimeOffset PublishedAt = new(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>#1508 (area 11): a clock that does not move: it is always <see cref="PublishedAt"/>, so every test's <c>durationMs</c> is 0.</summary>
    internal sealed class StoppedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => PublishedAt;

        public override long GetTimestamp() => 0;
    }

    /// <summary>
    /// #1508 (area 11): <see cref="PackagePublish.RunAsync"/> of <paramref name="golden"/> as CI publishes it
    /// (<c>publishedBy</c> <c>ci</c>), with <see cref="Fixed"/>, its limits and <see cref="StoppedClock"/>, asserted published.
    /// </summary>
    internal static PublishOutcome Publish(GoldenCase golden)
    {
        Assert.NotNull(golden.Limits);
        var outcome = PackagePublish.RunAsync(golden.Source(), new PublishOptions(ResolveFixture.Published(), Fixed, golden.Limits!, "ci", new StoppedClock()))
            .GetAwaiter().GetResult();
        Assert.True(outcome.Published, $"{golden.Name}: " + string.Join(" | ", outcome.Report.Refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));
        return outcome;
    }

    /// <summary><see cref="CompiledPackage.Publish"/> of <paramref name="golden"/> with <see cref="Fixed"/> and its limits, asserted accepted and complete.</summary>
    internal static CompiledPackage Published(GoldenCase golden)
    {
        var (refusals, compiled) = CompiledPackage.Publish(golden.Source(), ResolveFixture.Published(), Fixed, golden.Limits);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));
        Assert.NotNull(compiled);
        Assert.True(compiled!.Complete, $"{golden.Name}: a body's reads are not known.");
        return compiled;
    }

    /// <summary>The folder of the goldens in the source tree (<c>tests/V31Resolve/Golden</c>).</summary>
    internal static string GoldenFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "V31Resolve", "Golden");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Publish_GivesTheGolden(string name)
    {
        var golden = Cases().Single(c => c.Name == name);
        var compiled = Published(golden);
        var files = new[] { ($"{name}.resolve.json", compiled.Resolve), ($"{name}.lock.json", compiled.Lock) };

        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            Directory.CreateDirectory(GoldenFolder());
            foreach (var (file, text) in files)
                File.WriteAllText(Path.Combine(GoldenFolder(), file), text, Utf8NoBom);
        }

        foreach (var (file, text) in files)
        {
            var path = Path.Combine(GoldenFolder(), file);
            Assert.True(File.Exists(path), $"{file} is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.AsSpan().SequenceEqual(Utf8NoBom.GetBytes(text)), $"{file} is not what publish gives.");
            Assert.DoesNotContain('\n', text);
            Assert.Equal(CanonicalJson.Write(JsonNode.Parse(text)), text);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void GraphOrder_IsTopological_OverEveryEdgeButGates(string name)
    {
        // #1505 (area 08, D19): every edge but gates goes forward in graph.order, but where one end encloses the other
        // (a flow node comes before its body, and its ret edge comes back from it).
        var document = JsonNode.Parse(Published(Cases().Single(c => c.Name == name)).Resolve)!;
        var order = document["graph"]!["order"]!.AsArray().Select(k => (string)k!).ToList();
        foreach (var edge in document["graph"]!["edges"]!.AsArray())
        {
            var (from, to, kind) = ((string)edge!["from"]!, (string)edge["to"]!, (string)edge["kind"]!);
            if (kind == "gates" || from.StartsWith(to + ".", StringComparison.Ordinal) || to.StartsWith(from + ".", StringComparison.Ordinal)
                || !order.Contains(from) || !order.Contains(to))
            {
                continue;
            }

            Assert.True(order.IndexOf(from) < order.IndexOf(to), $"{name}: {from} -{kind}-> {to} goes back in {string.Join(", ", order)}");
        }

        if (name == "checked-note")
            Assert.True(order.IndexOf("deliverable.note") < order.IndexOf("check.proofread"));
    }

    [Fact]
    public void EveryGolden_VerifiesAgainstItsOwnHashes()
    {
        var names = Cases().Select(c => c.Name).ToList();
        var onDisk = Directory.GetFiles(GoldenFolder()).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(names.SelectMany(n => new[] { $"{n}.lock.json", $"{n}.resolve.json" }).Order(StringComparer.Ordinal), onDisk);

        foreach (var name in names)
        {
            var resolve = File.ReadAllBytes(Path.Combine(GoldenFolder(), $"{name}.resolve.json"));
            var @lock = File.ReadAllBytes(Path.Combine(GoldenFolder(), $"{name}.lock.json"));
            var resolveHash = CanonicalJson.HashText(Utf8NoBom.GetString(resolve));
            var lockHash = CanonicalJson.HashText(Utf8NoBom.GetString(@lock));
            var package = JsonNode.Parse(resolve)!["package"]!;

            Assert.Null(CompiledPackage.Verify((string)package["name"]!, (string)package["version"]!, resolve, @lock, resolveHash, lockHash));
            Assert.Equal(resolveHash, (string?)JsonNode.Parse(@lock)!["resolve"]!["hash"]);
        }
    }

    [Fact]
    public void EveryGolden_ValidatesAgainstTheSchema()
    {
        // #1508 (area 11): the closed entry names every member the compiler writes, so each golden fits resolve.schema.json
        // and lock.schema.json as the pinned content repository holds them.
        var resolve = ResolveFixture.PinnedSchema("resolve");
        var @lock = ResolveFixture.PinnedSchema("lock");
        foreach (var name in Cases().Select(c => c.Name))
        {
            Assert.True(resolve.Validate(File.ReadAllText(Path.Combine(GoldenFolder(), $"{name}.resolve.json")), null), $"{name}.resolve.json does not fit resolve.schema.json.");
            Assert.True(@lock.Validate(File.ReadAllText(Path.Combine(GoldenFolder(), $"{name}.lock.json")), null), $"{name}.lock.json does not fit lock.schema.json.");
        }
    }

    [Theory]
    [InlineData("macros", "nodes")]
    [InlineData("macros", "macros")]
    [InlineData("macros", "renderers")]
    [InlineData("macros", "deliverables")]
    [InlineData("checked-note", "checks")]
    [InlineData("functions", "snippets")] // a snippet folder (a key of one segment)
    [InlineData("functions", "functions")]
    public void AnEntry_WithAnUnnamedMember_DoesNotFitTheSchema(string golden, string map)
    {
        var resolve = ResolveFixture.PinnedSchema("resolve");
        var document = JsonNode.Parse(Published(Cases().Single(c => c.Name == golden)).Resolve)!;
        Assert.True(resolve.Validate(document.ToJsonString(), null));
        var changed = document.DeepClone();
        var entry = changed[map]!.AsObject().First(e => map != "snippets" || e.Key.Count(c => c == '.') == 1).Value!;
        entry["madeUp"] = 1;
        Assert.False(resolve.Validate(changed.ToJsonString(), null), $"{map}: an unnamed member still fits.");
    }

    [Fact]
    public void AFunctionsOwnNodesAndSnippetFolder_AreClosedToo_AndAFolderItemIsOpen()
    {
        var resolve = ResolveFixture.PinnedSchema("resolve");
        var document = JsonNode.Parse(Published(Cases().Single(c => c.Name == "functions")).Resolve)!;
        var item = document["snippets"]!.AsObject().First(e => e.Key.Count(c => c == '.') == 2).Value!;
        item["fromTheFoldersFormat"] = 1;
        Assert.True(resolve.Validate(document.ToJsonString(), null), "a folder item's own header member must fit.");

        var function = document["functions"]!.AsObject().First().Value!;
        function["nodes"]!.AsObject().First().Value!["madeUp"] = 1;
        Assert.False(resolve.Validate(document.ToJsonString(), null), "a function's node entry is closed.");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void EachCase_Publishes_ItsGoldenPair_AndAReportAndStampNamingIt(string name)
    {
        // #1508 (area 11): the case's publish-report.json and publish.json are of the pair its golden holds, as a version's
        // folder has them; the report is canonical, published by ci at the fixed time, every durationMs 0.
        var golden = Cases().Single(c => c.Name == name);
        var published = Publish(golden);
        var compiled = Published(golden);
        Assert.Equal(compiled.Resolve, published.Compiled!.Resolve);
        Assert.Equal(compiled.Lock, published.Compiled.Lock);

        var files = CompiledDocumentsExport.Files(golden, published);
        var report = JsonNode.Parse(files[PublishReport.FileName])!;
        Assert.Equal(CanonicalJson.Write(report), Utf8NoBom.GetString(files[PublishReport.FileName]));
        Assert.Equal("published", (string?)report["outcome"]);
        Assert.Equal("ci", (string?)report["publishedBy"]);
        Assert.Equal("2026-12-01T09:00:00Z", (string?)report["publishedAtUtc"]);
        Assert.Equal(compiled.SourceHash, (string?)report["sourceHash"]);
        Assert.Equal(CanonicalJson.HashText(Utf8NoBom.GetString(files["resolve.json"])), (string?)report["resolveHash"]);
        Assert.Equal(CanonicalJson.HashText(Utf8NoBom.GetString(files["lock.json"])), (string?)report["lockHash"]);
        Assert.Empty(report["refusals"]!.AsArray());
        Assert.All(report["tests"]!.AsArray(), t =>
        {
            Assert.Equal("pass", (string?)t!["result"]);
            Assert.Equal(0L, (long?)t["durationMs"]);
        });
        Assert.True(ResolveFixture.PinnedSchema("publish-report").Validate(report.ToJsonString(), null), $"{name}: publish-report.json does not fit its schema.");

        var stamp = JsonNode.Parse(files[PublishedStamp.FileName])!;
        Assert.Equal(report["resolveHash"]!.ToJsonString(), stamp["resolveHash"]!.ToJsonString());
        Assert.Equal(report["lockHash"]!.ToJsonString(), stamp["lockHash"]!.ToJsonString());
        Assert.True(ResolveFixture.PinnedSchema("publish-stamp").Validate(stamp.ToJsonString(), null), $"{name}: publish.json does not fit its schema.");
    }

    [Fact]
    public void TheReports_HoldATestEach_ForNestedNoteAndMacros_NestedNotesWithTheCeilingNoteAndAnUnusedStub()
    {
        // #1508 (area 11): nested-note publishes against its raised instances (at the engine's 10,000 it is A11-001), and
        // its report warns of the ceiling (S04-073) and of the stub its test gives a section that is not planned (S07-014).
        var nested = Cases().Single(c => c.Name == "nested-note");
        Assert.Equal("A11-001", Assert.Single(PackagePublish.CheckAsync(nested.Source(),
            new PublishOptions(ResolveFixture.Published(), Fixed, EngineLimits, "ci", new StoppedClock())).GetAwaiter().GetResult().Report.Refusals).Id);

        var report = Publish(nested).Report;
        Assert.Equal("two_sections", Assert.Single(report.Tests).Id);
        Assert.Contains(report.Warnings, w => w.Id == "S04-073");
        Assert.Equal("Test 'two_sections' stubs node.sections.outline:3, which doesn't run.", Assert.Single(report.Warnings, w => w.Id == "S07-014").Sentence);

        Assert.Equal("close_line", Assert.Single(Publish(Cases().Single(c => c.Name == "macros")).Report.Tests).Id);
        Assert.All(Cases().Where(c => c.Name is not ("nested-note" or "macros")), c => Assert.Empty(Publish(c).Report.Tests));
    }

    [Fact]
    public void ThePinnedFamily_IsWhatPublishGives()
    {
        var family = V31Contract.CompiledDocumentsFolder();
        var cases = Cases();
        Assert.Equal(
            cases.Select(c => c.Name).Order(StringComparer.Ordinal),
            Directory.GetDirectories(family).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal));

        foreach (var golden in cases)
        {
            var folder = Path.Combine(family, golden.Name);
            var expected = CompiledDocumentsExport.Files(golden, Publish(golden));
            var pinned = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);

            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), pinned.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in expected)
                Assert.True(pinned[path].AsSpan().SequenceEqual(bytes), $"{golden.Name}/{path}: the pinned file is not what publish gives.");
        }

        var index = V31Contract.Json("conformance", "index.json")["families"]!.AsArray()
            .Single(f => (string?)f!["id"] == "compiled-documents")!;
        Assert.Equal(cases.Select(c => c.Name).Order(StringComparer.Ordinal), index["cases"]!.AsArray().Select(c => (string)c!));
    }
}
