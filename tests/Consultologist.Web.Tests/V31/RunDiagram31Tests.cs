using System.Text.RegularExpressions;
using Consultologist.PackageFormat.V31.Diagram;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;
using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 9 (#1510; part 09 § 10.6; D10, D11, D12): a storage 12 record laid over the package's drawing. The drawings are the
/// resolve goldens (<c>tests/V31Resolve/Golden</c>); the rows are written by the engine's own writers, with part 08's members.
/// </summary>
public partial class RunDiagram31Tests
{
    private static readonly string[] Goldens = ["checked-note", "conditional", "functions", "macros", "nested-note"];

    private static readonly string[] RunClasses = ["done", "skipped", "ranNot", "failed", "running"];

    private static readonly DateTime At = new(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string> AllGoldens() => new(Goldens);

    private static string Resolve(string golden) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Golden", "V31Resolve", golden + ".resolve.json"));

    private static Record31 Record(params RecordRowRef[] rows) => Record31.From(Record31Fixtures.Header(), rows);

    private static Record31 Running(params RecordRowRef[] rows) => Record31.From(Record31Fixtures.Header() with { Status = "Running" }, rows);

    private static RecordRowRef Row(string key, PlanKind kind, RowState state, string? hash = "sha256:aa", string detail = "{}", string? reason = null) =>
        Record31Fixtures.NodeRow(key, kind, state, reason, hash, detail);

    private static RecordRowRef Decision(string owner, string site, string condition, bool? result, string? branch, params string[] reads) =>
        new($"{owner}/if:{site}", "decision", System.Text.Json.JsonSerializer.SerializeToElement(
            new RunDecision(owner, site, condition, "sha256:ee", result, branch, reads.Select(r => new RunRead(r, "sha256:ff")).ToList(), false, true, At).ToJson()));

    private static string Draw(string golden, Record31 record, bool linkCalls = true) => RunDiagram31.Build(Resolve(golden), record, linkCalls).Mermaid;

    /// <summary>The label lines of the box <paramref name="id"/>, as written (escaped).</summary>
    private static string[] Label(string mermaid, string id)
    {
        var line = mermaid.Split('\n').Single(l => BoxLine().Match(l) is { Success: true } m && m.Groups[1].Value == id);
        var label = line[(line.IndexOf('"') + 1)..line.LastIndexOf('"')];
        return label.Split("<br/>");
    }

    /// <summary>The run class the box <paramref name="id"/> is given; null for none.</summary>
    private static string? ClassOf(string mermaid, string id) =>
        mermaid.Split('\n').Select(l => ClassLine().Match(l)).Where(m => m.Success && RunClasses.Contains(m.Groups[2].Value))
            .Where(m => m.Groups[1].Value.Split(',').Contains(id)).Select(m => m.Groups[2].Value).SingleOrDefault();

    /// <summary>The label lines after the package drawing's own for the box <paramref name="id"/>.</summary>
    private static string[] Added(string golden, string mermaid, string id) =>
        Label(mermaid, id).Skip(Label(PackageDiagram.Generate(Resolve(golden)), id).Length).ToArray();

    /// <summary>
    /// The drawing with the run's additions taken out: each box's label emptied, the run classes' <c>classDef</c> and
    /// <c>class</c> lines dropped. What is left is the boxes' ids and shapes, the frames and the edges.
    /// </summary>
    private static string[] Structure(string mermaid) =>
        mermaid.Split('\n')
            .Where(l => !(RunClasses.Any(c => l.Trim().StartsWith($"classDef {c} ", StringComparison.Ordinal))
                || (ClassLine().Match(l) is { Success: true } m && RunClasses.Contains(m.Groups[2].Value))))
            .Select(l => BoxLine().IsMatch(l) ? l[..(l.IndexOf('"') + 1)] + l[l.LastIndexOf('"')..] : l)
            .ToArray();

    /// <summary>The ids of the boxes a record row can name: every box but the pills and a function's (whose keys no row carries).</summary>
    private static List<DiagramBox> RowBoxes(string golden) =>
        DiagramModel.Build(Resolve(golden)).AllBoxes()
            .Where(b => b.Shape != BoxShape.Pill && !b.Key.StartsWith("function.", StringComparison.Ordinal)).ToList();

    [GeneratedRegex(@"^\s*([A-Za-z0-9_]+)(\(\[""|\[\[""|\[/""|\[""|@\{ shape: [a-z-]+, label: ""|\{\{"")")]
    private static partial Regex BoxLine();

    [GeneratedRegex(@"^\s*class ([A-Za-z0-9_,]+) ([A-Za-z]+)$")]
    private static partial Regex ClassLine();

    [Theory]
    [MemberData(nameof(AllGoldens))]
    public void TheRunDiagram_HasTheBoxesFramesAndEdgesOfThePackageDiagram(string golden)
    {
        // a row for every box a row can name, so every box gets lines and a class
        var rows = RowBoxes(golden).Where(b => !b.Key.Contains('#', StringComparison.Ordinal)).Select(b =>
            b.Key.StartsWith("deliverable.", StringComparison.Ordinal) ? Row(b.Key, PlanKind.Deliverable, RowState.Released)
            : b.Key.StartsWith("check.", StringComparison.Ordinal) ? Row(b.Key, PlanKind.Computed, RowState.Completed, detail: """{ "pass": true }""")
            : Row(b.Key, PlanKind.Agent, RowState.Completed)).ToArray();

        var run = Draw(golden, Record(rows));
        var package = PackageDiagram.Generate(Resolve(golden));

        Assert.Equal(Structure(package), Structure(run));
        Assert.NotEqual(package, run);
    }

    [Fact]
    public void TheClasses_AreRunDagDiagramsColours()
    {
        var run = Draw("macros", Record());
        var classDefs = RunDagDiagram.Build(Record31Fixtures.Header()).Split('\n').Where(l => l.StartsWith("classDef ", StringComparison.Ordinal)).ToList();

        Assert.Equal(5, classDefs.Count);
        foreach (var classDef in classDefs)
            Assert.Contains("    " + classDef + "\n", run, StringComparison.Ordinal);
    }

    [Fact]
    public void ANode_ShowsItsStoredStatusItsOutputHashAndItsRetries()
    {
        var run = Draw("macros", Record(
            Row("node.plan", PlanKind.Agent, RowState.Completed, "sha256:0123456789abcdef", """
                { "retries": [ { "atUtc": "2026-11-16T14:00:05Z", "waitMs": 2000, "kind": "capacity", "error": null },
                               { "atUtc": "2026-11-16T14:00:09Z", "waitMs": 4000, "kind": "service", "error": "timeout" } ] }
                """),
            Row("node.terms", PlanKind.Agent, RowState.Completed, "sha256:fedcba9876543210"),
            Row("node.stamp", PlanKind.Rendered, RowState.Failed, null, reason: "failed: the model service's limits were reached")));

        Assert.Equal(["Completed", "out sha256:01234…", "retries 2"], Added("macros", run, "node__plan"));
        Assert.Equal("done", ClassOf(run, "node__plan"));
        // the hash shortened as History shortens it; no retries line for a row whose retries are empty
        Assert.Equal(["Completed", "out " + ProvenanceHashes.HashPrefix("sha256:fedcba9876543210")], Added("macros", run, "node__terms"));
        Assert.Equal(["Failed"], Added("macros", run, "node__stamp"));
        Assert.Equal("failed", ClassOf(run, "node__stamp"));
    }

    [Fact]
    public void ALoop_ListsItsItemsByNameWithTheirStatus()
    {
        var run = Draw("nested-note", Record(
            Row("node.sections", PlanKind.For, RowState.Completed, detail: """{ "count": 3, "itemLabel": "title" }"""),
            Row("node.sections:1", PlanKind.For, RowState.Completed, detail: """{ "itemName": "Assessment", "positionName": false }"""),
            Row("node.sections:2", PlanKind.For, RowState.Skipped, detail: """{ "itemName": null, "itemNameHash": "sha256:a2" }"""),
            Row("node.sections:3", PlanKind.For, RowState.NotRun, detail: """{ "itemName": "Plan", "positionName": false }""", reason: "not run: beyond max 2")));

        Assert.Equal(["Completed", "out sha256:aa…", "1 · Assessment: Completed", "2: Skipped", "3 · Plan: NotRun"], Added("nested-note", run, "node__sections"));
        Assert.Equal("done", ClassOf(run, "node__sections"));
    }

    [Fact]
    public void ALoop_ListsItsItemsByNameWithTheirStatus_StopsAtTwelve()
    {
        var rows = new List<RecordRowRef> { Row("node.sections", PlanKind.For, RowState.Completed, detail: """{ "count": 40 }""") };
        for (var i = 1; i <= 40; i++)
            rows.Add(Row($"node.sections:{i}", PlanKind.For, RowState.Completed, detail: $$"""{ "itemName": "Section {{i}}", "positionName": true }"""));
        // and 40 rows inside each body box: a tally, never a line per row
        for (var i = 1; i <= 40; i++)
            rows.Add(Row($"node.sections.outline:{i}", PlanKind.Agent, i == 40 ? RowState.Failed : RowState.Completed));

        var run = Draw("nested-note", Record([.. rows]));

        var added = Added("nested-note", run, "node__sections");
        Assert.Equal(2 + 12 + 1, added.Length);
        Assert.Equal("1 · Section 1: Completed", added[2]);
        Assert.Equal("12 · Section 12: Completed", added[13]);
        Assert.Equal("… and 28 more", added[^1]);
        Assert.Equal(["39 Completed · 1 Failed"], Added("nested-note", run, "node__sections__outline"));
        Assert.Equal("failed", ClassOf(run, "node__sections__outline"));
    }

    [Fact]
    public void AWhile_ListsItsPasses()
    {
        var run = Draw("nested-note", Record(
            Row("node.refine", PlanKind.While, RowState.Completed, "sha256:bb", """{ "passes": 2, "stopped": "check", "max": 3, "checkFirst": false }"""),
            Row("node.refine#1", PlanKind.While, RowState.Completed, detail: """{ "pass": 1 }"""),
            Row("node.refine#2", PlanKind.While, RowState.Completed, detail: """{ "pass": 2 }"""),
            Row("node.refine.judge#1", PlanKind.Agent, RowState.Completed),
            Row("node.refine.judge#2", PlanKind.Agent, RowState.Completed)));

        Assert.Equal(["Completed", "out sha256:bb…", "pass 1: Completed", "pass 2: Completed"], Added("nested-note", run, "node__refine"));
        Assert.Equal(["2 Completed"], Added("nested-note", run, "node__refine__judge"));
    }

    [Fact]
    public void ABoxInsideALoop_ShowsATallyAndTheWorstClass()
    {
        var run = Draw("nested-note", Record(
            Row("node.sections", PlanKind.For, RowState.Completed),
            Row("node.sections.outline:1", PlanKind.Agent, RowState.Completed),
            Row("node.sections.outline:2", PlanKind.Agent, RowState.Skipped, null),
            Row("node.sections.outline:3", PlanKind.Agent, RowState.Completed),
            Row("node.sections.outline:4", PlanKind.Agent, RowState.Completed),
            // a loop nested in two items: its own rows are a tally too, and its body's
            Row("node.sections.paragraphs:1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1/1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:2", PlanKind.For, RowState.NotRun, null),
            Row("node.sections.paragraphs.draft_paragraph:1/1", PlanKind.Agent, RowState.Cancelled, null),
            Row("node.sections.paragraphs.draft_paragraph:1/2", PlanKind.Agent, RowState.Completed)));

        Assert.Equal(["3 Completed · 1 Skipped"], Added("nested-note", run, "node__sections__outline"));
        Assert.Equal("skipped", ClassOf(run, "node__sections__outline"));
        Assert.Equal(["1 Completed · 1 NotRun"], Added("nested-note", run, "node__sections__paragraphs"));
        Assert.Equal("ranNot", ClassOf(run, "node__sections__paragraphs"));
        Assert.Equal(["1 Completed · 1 Cancelled"], Added("nested-note", run, "node__sections__paragraphs__draft_paragraph"));
        Assert.Equal("failed", ClassOf(run, "node__sections__paragraphs__draft_paragraph"));
    }

    [Fact]
    public void ARendererBox_ShowsThatUsesInputAndOutputHashes()
    {
        var run = Draw("macros", Record(Row("node.terms_text", PlanKind.Rendered, RowState.Completed, "sha256:03", """
            { "renders": [ { "id": "node.terms_text/render:1", "renderer": "renderer.concept_bullets", "templateHash": "sha256:t1", "source": "node.terms.value",
                             "inputHash": "sha256:1111111111", "pass": null, "inputSize": "14 concepts", "outputHash": "sha256:2222222222", "outputLength": 612, "runDependent": false } ] }
            """)));

        Assert.Equal(["in sha256:11111… · out sha256:22222…"], Added("macros", run, "node__terms_text__render_1"));
        Assert.Equal("done", ClassOf(run, "node__terms_text__render_1"));
    }

    [Fact]
    public void AMacroBox_SaysOfferedAndPlacedAndItsContentHash()
    {
        string Macro(bool offered, bool placed, string? hash) => $$"""
            { "macros": [ { "id": "node.draft/macro:section_notes.hpi", "macro": "macro.section_notes.hpi", "offered": {{(offered ? "true" : "false")}},
                            "decisions": [], "classifier": "node.classify", "placed": {{(placed ? "true" : "false")}}, "form": null,
                            "contentHash": {{(hash == null ? "null" : $"\"{hash}\"")}}, "runNeutralHash": null, "runSpots": [] } ] }
            """;
        const string box = "node__draft__macro_section_notes__hpi";

        var placed = Draw("macros", Record(Row("node.draft", PlanKind.Agent, RowState.Completed, detail: Macro(true, true, "sha256:c1c1c1c1c1"))));
        Assert.Equal(["offered · placed", "content sha256:c1c1c…"], Added("macros", placed, box));
        Assert.Equal("done", ClassOf(placed, box));

        var missing = Draw("macros", Record(Row("node.draft", PlanKind.Agent, RowState.Completed, detail: Macro(true, false, "sha256:c1c1c1c1c1"))));
        Assert.Equal(["offered · not placed", "content sha256:c1c1c…"], Added("macros", missing, box));
        Assert.Equal("failed", ClassOf(missing, box));

        var notOffered = Draw("macros", Record(Row("node.draft", PlanKind.Agent, RowState.Completed, detail: Macro(false, false, null))));
        Assert.Equal(["not offered"], Added("macros", notOffered, box));
        Assert.Equal("skipped", ClassOf(notOffered, box));
    }

    [Fact]
    public void AClassifier_ShowsTheAnswerADecisionRecorded()
    {
        var run = Draw("macros", Record(
            Row("node.classify", PlanKind.Agent, RowState.Completed, "sha256:c0"),
            Row("node.draft", PlanKind.Agent, RowState.Completed),
            Decision("node.draft", "macro.section_notes.hpi", "node.classify.value.answer = true", true, null, "node.classify.value.answer")));

        Assert.Equal(["Completed", "out sha256:c0…", "node.classify.value.answer = true: true"], Added("macros", run, "node__classify"));
    }

    [Fact]
    public void AClassifier_ShowsTheAnswerADecisionRecorded_ShowsNothingWithoutOne()
    {
        // a decision that reads the classifier and something else is not its answer; nor is one that reads another node
        var run = Draw("macros", Record(
            Row("node.classify", PlanKind.Agent, RowState.Completed, "sha256:c0"),
            Decision("node.draft", "part.close", "not (blank node.classify.value.reason)", true, null, "node.classify.value.reason", "input.consult_draft.value"),
            Decision("node.draft", "self", "node.plan.value.ready = true", false, null, "node.plan.value.ready")));

        Assert.Equal(["Completed", "out sha256:c0…"], Added("macros", run, "node__classify"));
    }

    [Fact]
    public void AConditionSummaryBox_ShowsTheDecisionAndThePrintedCondition()
    {
        var run = Draw("conditional", Record(
            Row("node.patient_summaries", PlanKind.For, RowState.Completed),
            Row("node.patient_summaries:1", PlanKind.For, RowState.Completed),
            Row("node.patient_summaries.narrative_only:1", PlanKind.If, RowState.Completed, "sha256:d1"),
            Decision("node.patient_summaries.narrative_only:1", "self", "item.value.kind = \"narrative\"", true, null, "item.value.kind"),
            Row("node.sections_draft", PlanKind.For, RowState.Completed),
            Row("node.sections_draft:1", PlanKind.For, RowState.Completed),
            Row("node.sections_draft.route:1", PlanKind.Case, RowState.Completed, "sha256:d2"),
            Decision("node.sections_draft.route:1", "case", "item.value.kind", null, "2", "item.value.kind")));

        Assert.Equal(["Completed", "out sha256:d1…", "item.value.kind = #quot;narrative#quot;: true"], Added("conditional", run, "node__patient_summaries__narrative_only"));
        Assert.Equal(["Completed", "out sha256:d2…", "item.value.kind: arm 2"], Added("conditional", run, "node__sections_draft__route"));
    }

    [Fact]
    public void ACallBox_ShowsItsStatusAndHashes_AndLinksToItsRows()
    {
        var rows = new[]
        {
            Row("node.standards", PlanKind.For, RowState.Completed),
            Row("node.standards:1", PlanKind.For, RowState.Completed),
            Row("node.standards.section:1", PlanKind.Call, RowState.Completed, "sha256:5555555555", """
                { "function": "draft_section", "arguments": [ { "param": "standard", "hash": "sha256:3333333333" }, { "param": "draft", "hash": "sha256:4444444444" } ], "depth": 0 }
                """),
        };

        var linked = RunDiagram31.Build(Resolve("functions"), Record(rows), linkCalls: true);
        Assert.Equal(["Completed", "in standard sha256:33333…", "in draft sha256:44444…", "out sha256:55555…"], Added("functions", linked.Mermaid, "node__standards__section"));
        Assert.Equal("row-node.standards.section:1", linked.Anchors["node__standards__section"]);
        Assert.Single(linked.Anchors);

        var unlinked = RunDiagram31.Build(Resolve("functions"), Record(rows), linkCalls: false);
        Assert.Empty(unlinked.Anchors);
        Assert.Equal(linked.Mermaid, unlinked.Mermaid);
    }

    [Fact]
    public void TheFunctionsFrame_ShowsNoStatus()
    {
        var run = Draw("functions", Record(
            Row("node.standards", PlanKind.For, RowState.Completed),
            Row("node.standards:1", PlanKind.For, RowState.Completed),
            Row("node.standards.section:1", PlanKind.Call, RowState.Completed, detail: """{ "function": "draft_section", "depth": 0 }"""),
            Row("node.standards.section.standard_draft:1", PlanKind.Agent, RowState.Failed, "sha256:77", """{ "function": "draft_section", "depth": 1 }"""),
            Row("node.standards.section.patient_draft:1", PlanKind.Agent, RowState.Completed, "sha256:78", """{ "function": "draft_section", "depth": 1 }""")));

        var package = PackageDiagram.Generate(Resolve("functions"));
        foreach (var box in DiagramModel.Build(Resolve("functions")).AllBoxes().Where(b => b.Key.StartsWith("function.", StringComparison.Ordinal)))
        {
            Assert.Equal(Label(package, box.Id), Label(run, box.Id));
            Assert.Null(ClassOf(run, box.Id));
        }

        Assert.DoesNotContain("Failed", run, StringComparison.Ordinal);
    }

    [Fact]
    public void ACheck_ShowsPassOrFail()
    {
        var run = Draw("checked-note", Record(
            Row("check.proofread", PlanKind.Computed, RowState.Completed, "sha256:e1", """{ "pass": true, "passed": 3, "total": 3 }"""),
            Row("check.all_sections_covered", PlanKind.Computed, RowState.Completed, "sha256:e2", """{ "pass": false, "passed": 1, "total": 2, "failWith": "Not every section covers its planned terms." }""")));

        Assert.Equal("pass", Added("checked-note", run, "check__proofread")[0]);
        Assert.Equal("done", ClassOf(run, "check__proofread"));
        Assert.Equal("fail", Added("checked-note", run, "check__all_sections_covered")[0]);
        Assert.Equal("failed", ClassOf(run, "check__all_sections_covered"));
    }

    [Fact]
    public void ACheckLoop_ListsItsItems()
    {
        var run = Draw("checked-note", Record(
            Row("check.part_coverage", PlanKind.For, RowState.Completed, detail: """{ "count": 3 }"""),
            Row("check.part_coverage:1", PlanKind.For, RowState.Completed, detail: """{ "itemName": "Assessment", "alignedWith": "node.sections:1", "pass": true }"""),
            Row("check.part_coverage:2", PlanKind.For, RowState.Completed, detail: """{ "itemName": null, "alignedWith": "node.sections:2", "pass": false }"""),
            Row("check.part_coverage:3", PlanKind.For, RowState.Skipped, null, """{ "alignedWith": "node.sections:3" }"""),
            Row("check.part_coverage.coverage:1", PlanKind.Computed, RowState.Completed, detail: """{ "pass": true }"""),
            Row("check.part_coverage.coverage:2", PlanKind.Computed, RowState.Completed, detail: """{ "pass": false, "failWith": "Not covered." }""")));

        Assert.Equal(["Completed", "1 · Assessment: pass", "2: fail", "3: Skipped"], Added("checked-note", run, "check__part_coverage"));
        Assert.Equal(["1 pass · 1 fail"], Added("checked-note", run, "check__part_coverage__coverage"));
        Assert.Equal("failed", ClassOf(run, "check__part_coverage__coverage"));
    }

    [Fact]
    public void AnAggregate_ShowsItsResult_AndItsFailWithWhenItFailed()
    {
        var failed = Draw("checked-note", Record(
            Row("check.all_sections_covered", PlanKind.Computed, RowState.Completed, "sha256:e2", """{ "pass": false, "passed": 1, "total": 2, "failWith": "Not every section covers its planned terms." }""")));
        Assert.Equal(["fail", "1 of 2", "Not every section covers its planned terms."], Added("checked-note", failed, "check__all_sections_covered"));

        var passed = Draw("checked-note", Record(
            Row("check.all_sections_covered", PlanKind.Computed, RowState.Completed, "sha256:e2", """{ "pass": true, "passed": 2, "total": 2, "failWith": null }""")));
        Assert.Equal(["pass", "2 of 2"], Added("checked-note", passed, "check__all_sections_covered"));

        var skipped = Draw("checked-note", Record(
            Row("check.all_sections_covered", PlanKind.Computed, RowState.Skipped, null, reason: "skipped: input.kind.value = \"letter\" is false")));
        Assert.Equal(["Skipped"], Added("checked-note", skipped, "check__all_sections_covered"));
        Assert.Equal("skipped", ClassOf(skipped, "check__all_sections_covered"));
    }

    [Theory]
    [InlineData(RowState.Released, "released", "done")]
    [InlineData(RowState.Held, "held", "skipped")]
    [InlineData(RowState.Failed, "failed", "failed")]
    [InlineData(RowState.Skipped, "skipped", "skipped")]
    [InlineData(RowState.Blocked, "blocked", "failed")]
    public void ADeliverable_ShowsItsStoredState(RowState state, string word, string cls)
    {
        var run = Draw("checked-note", Record(Row("deliverable.note", PlanKind.Deliverable, state, "sha256:b1")));

        Assert.Equal([word], Added("checked-note", run, "deliverable__note"));
        Assert.Equal(cls, ClassOf(run, "deliverable__note"));
    }

    [Theory]
    [MemberData(nameof(AllGoldens))]
    public void AJobWithNoRows_DrawsEveryBoxAsNotRun(string golden)
    {
        var package = PackageDiagram.Generate(Resolve(golden));
        foreach (var record in new[] { Record(), Running() })
        {
            var run = Draw(golden, record);
            foreach (var box in RowBoxes(golden))
            {
                Assert.Equal(Label(package, box.Id), Label(run, box.Id));
                Assert.Equal("ranNot", ClassOf(run, box.Id));
            }
        }
    }

    [Fact]
    public void AJobStillRunning_DrawsWhatHasRowsInsideItAsRunning_AndALoopWithNoItemsAsItsRow()
    {
        // the loop's own row is written when it ends: a running job has its item rows and not its own
        var running = Draw("nested-note", Running(
            Row("node.plan", PlanKind.Agent, RowState.Completed),
            Row("node.sections:1", PlanKind.For, RowState.Completed),
            Row("node.sections.outline:1", PlanKind.Agent, RowState.Completed)));
        Assert.Empty(Added("nested-note", running, "node__sections"));
        Assert.Equal("running", ClassOf(running, "node__sections"));
        Assert.Equal("ranNot", ClassOf(running, "node__refine"));
        Assert.Equal("ranNot", ClassOf(running, "deliverable__note"));

        // a job that ended without the row is not running
        var failed = Draw("nested-note", Record(Row("node.sections:1", PlanKind.For, RowState.Completed)));
        Assert.Equal("ranNot", ClassOf(failed, "node__sections"));

        // a loop whose list was empty has its row and no item
        var empty = Draw("nested-note", Record(Row("node.sections", PlanKind.For, RowState.Completed, detail: """{ "count": 0 }""")));
        Assert.Equal(["Completed", "out sha256:aa…"], Added("nested-note", empty, "node__sections"));
    }

    [Fact]
    public void ADeliverableWithNoRow_ShowsNoState()
    {
        var run = Draw("checked-note", Record(Row("deliverable.note", PlanKind.Deliverable, RowState.Released, "sha256:b1")));

        var package = PackageDiagram.Generate(Resolve("checked-note"));
        Assert.Equal(Label(package, "deliverable__patient__patient_letter"), Label(run, "deliverable__patient__patient_letter"));
        Assert.Equal("ranNot", ClassOf(run, "deliverable__patient__patient_letter"));
    }

    [Fact]
    public void NoLabelCarriesARowsText()
    {
        const string text = """ "text": "Margaret Okafor reports a sore throat", "itemNameHash": "sha256:a9" """;
        var run = Draw("nested-note", Record(
            Row("node.plan", PlanKind.Agent, RowState.Completed, detail: "{" + text + "}"),
            Row("node.sections", PlanKind.For, RowState.Completed, detail: "{" + text + "}"),
            Row("node.sections:1", PlanKind.For, RowState.Completed, detail: "{" + text + """, "itemName": null }"""),
            Row("node.sections.outline:1", PlanKind.Agent, RowState.Completed, detail: "{" + text + "}"),
            Row("deliverable.note", PlanKind.Deliverable, RowState.Released, detail: "{" + text + "}")));

        Assert.DoesNotContain("Margaret", run, StringComparison.Ordinal);
        Assert.DoesNotContain("sha256:a9", run, StringComparison.Ordinal);
        Assert.Equal(["Completed", "out sha256:aa…", "1: Completed"], Added("nested-note", run, "node__sections"));
    }
}
