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
            Row("node.classify", PlanKind.Agent, RowState.Completed, "sha256:c0", """{ "retries": [] }"""),
            Row("node.stamp", PlanKind.Rendered, RowState.Failed, null, reason: "failed: the model service's limits were reached")));

        Assert.Equal(["Completed", "out sha256:01234…", "retries 2"], Added("macros", run, "node__plan"));
        Assert.Equal("done", ClassOf(run, "node__plan"));
        // the hash shortened as History shortens it; no retries line for a row whose retries are empty
        Assert.Equal(["Completed", "out " + ProvenanceHashes.HashPrefix("sha256:fedcba9876543210")], Added("macros", run, "node__terms"));
        // the stored form of no retries, part 08 § 4: "retries": []
        Assert.Equal(["Completed", "out sha256:c0…"], Added("macros", run, "node__classify"));
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
            Row("node.plan", PlanKind.Agent, RowState.Completed, "sha256:b0"),
            Decision("node.draft", "part.close", "not (blank node.classify.value.reason)", true, null, "node.classify.value.reason", "input.consult_draft.value"),
            Decision("node.draft", "self", "node.plan.value.ready = true", false, null, "node.plan.value.ready")));

        Assert.Equal(["Completed", "out sha256:c0…"], Added("macros", run, "node__classify"));
        // node.plan returns section_plan: it is no classifier, so a decision reading only its value is no answer of its own
        Assert.Equal(["Completed", "out sha256:b0…"], Added("macros", run, "node__plan"));
    }

    [Fact]
    public void ACaseSubject_IsAnAnswerOnlyOnAClassifiersBox()
    {
        // a case's subject is its one read: the node it names shows it only when that node returns judgement or classification
        var run = Draw("macros", Record(
            Row("node.classify", PlanKind.Agent, RowState.Completed, "sha256:c0"),
            Row("node.plan", PlanKind.Agent, RowState.Completed, "sha256:b0"),
            Decision("node.draft", "case", "node.plan.value.kind", null, "2", "node.plan.value.kind"),
            Decision("node.stamp", "case", "node.classify.value.answer", null, "1", "node.classify.value.answer")));

        Assert.Equal(["Completed", "out sha256:b0…"], Added("macros", run, "node__plan"));
        Assert.Equal(["Completed", "out sha256:c0…", "node.classify.value.answer: 1"], Added("macros", run, "node__classify"));
    }

    /// <summary>
    /// A <c>resolve.json</c> holding only what the drawing reads (no golden has a renderer or a macro inside a loop): a
    /// <c>for</c> over <c>input.sections</c> whose body is a classifier and an agent that pipes the item through a renderer
    /// and places a macro whose <c>if</c> names the classifier as the body names a sibling, by its bare id.
    /// </summary>
    private const string LoopBody = """
        {
          "inputs": { "sections": { "label": "sections" } },
          "nodes": {
            "node.each": { "id": "each", "kind": "for", "label": "Each section", "producer": "computed", "parent": null,
                           "in": "input.sections.value", "nodes": ["node.each.classify", "node.each.draft"], "ret": "node.each.draft",
                           "until": null, "reads": ["input.sections.value"] },
            "node.each.classify": { "id": "classify", "kind": "agent", "label": "Needs a history line?", "producer": "generated",
                                    "format": { "$ref": "#/$defs/judgement" }, "modelDerived": true, "parent": "node.each", "reads": ["item.value"] },
            "node.each.draft": { "id": "draft", "kind": "agent", "label": "Drafting the section", "producer": "generated",
                                 "format": { "$ref": "#/$defs/plaintext" }, "modelDerived": true, "parent": "node.each",
                                 "reads": ["item.value", "node.each.classify.value.answer"], "names": { "node.classify": "node.each.classify" },
                                 "renders": [ { "n": 1, "source": "item.value", "renderer": "renderer.bullets" } ],
                                 "placements": [ { "site": "macro.hpi", "within": null, "form": "block", "plainConverts": [],
                                                   "if": { "===": [ { "var": "node.classify.value.answer" }, true ] } } ] }
          },
          "checks": {}, "deliverables": {},
          "renderers": { "renderer.bullets": { "id": "bullets", "input": { "$ref": "#/$defs/concept_list" }, "format": { "$ref": "#/$defs/markdown_block" } } },
          "macros": { "macro.hpi": { "id": "hpi", "format": { "$ref": "#/$defs/plaintext" } } },
          "graph": { "edges": [], "order": ["node.each", "node.each.classify", "node.each.draft"] }
        }
        """;

    [Fact]
    public void AClassifierRendererAndMacroInsideALoop_ShowTheirItemsSubRowsAndTheAnswer()
    {
        // the engine names the sub-rows by the row key with its items (AgentTurn), and the macro row its classifier's row key
        var run = RunDiagram31.Build(LoopBody, Record(
            Row("node.each", PlanKind.For, RowState.Completed),
            Row("node.each:1", PlanKind.For, RowState.Completed),
            Row("node.each.classify:1", PlanKind.Agent, RowState.Completed, "sha256:c0"),
            Row("node.each.draft:1", PlanKind.Agent, RowState.Completed, "sha256:d0", """
                { "renders": [ { "id": "node.each.draft:1/render:1", "renderer": "renderer.bullets", "templateHash": "sha256:t1", "source": "item.value",
                                 "inputHash": "sha256:1111111111", "pass": null, "inputSize": null, "outputHash": "sha256:2222222222", "outputLength": 40, "runDependent": false } ],
                  "macros": [ { "id": "node.each.draft:1/macro:hpi", "macro": "macro.hpi", "offered": true, "decisions": ["node.each.draft:1/if:macro.hpi"],
                                "classifier": "node.each.classify:1", "placed": true, "form": "block", "contentHash": "sha256:c1c1c1c1c1",
                                "runNeutralHash": null, "runSpots": [] } ] }
                """),
            Decision("node.each.draft:1", "macro.hpi", "node.classify.value.answer = true", true, null, "node.classify.value.answer")), linkCalls: false).Mermaid;
        var package = PackageDiagram.Generate(LoopBody);
        string[] Of(string id) => Label(run, id).Skip(Label(package, id).Length).ToArray();

        Assert.Equal(["Completed", "out sha256:c0…", "node.classify.value.answer = true: true"], Of("node__each__classify"));
        Assert.Equal(["in sha256:11111… · out sha256:22222…"], Of("node__each__draft__render_1"));
        Assert.Equal("done", ClassOf(run, "node__each__draft__render_1"));
        Assert.Equal(["offered · placed", "content sha256:c1c1c…"], Of("node__each__draft__macro_hpi"));
        Assert.Equal("done", ClassOf(run, "node__each__draft__macro_hpi"));
        // the drafting node reads the classifier but is none: no answer on its box
        Assert.Equal(["Completed", "out sha256:d0…"], Of("node__each__draft"));
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
        // the loop's own row is written when it ends: a running job has its item rows and not its own, and the box lists them
        var running = Draw("nested-note", Running(
            Row("node.plan", PlanKind.Agent, RowState.Completed),
            Row("node.sections:1", PlanKind.For, RowState.Completed, detail: """{ "itemName": "Assessment", "positionName": false }"""),
            Row("node.sections:2", PlanKind.For, RowState.Failed, null),
            Row("node.sections.outline:1", PlanKind.Agent, RowState.Completed)));
        Assert.Equal(["1 · Assessment: Completed", "2: Failed"], Added("nested-note", running, "node__sections"));
        Assert.Equal("running", ClassOf(running, "node__sections"));
        Assert.Equal("ranNot", ClassOf(running, "node__refine"));
        Assert.Equal("ranNot", ClassOf(running, "deliverable__note"));

        // a job that failed mid-loop: the engine writes no row for the loop (PlanRunner stops at the failure), so its items
        // still list; the job has ended, so the box is not running but takes the class of its worst item (D10)
        var failed = Draw("nested-note", Record(Row("node.sections:1", PlanKind.For, RowState.Completed), Row("node.sections:2", PlanKind.For, RowState.Failed, null)));
        Assert.Equal(["1: Completed", "2: Failed"], Added("nested-note", failed, "node__sections"));
        Assert.Equal("failed", ClassOf(failed, "node__sections"));

        // a while's passes likewise, and the list stays bounded (D10)
        var passes = Enumerable.Range(1, 14).Select(n => Row($"node.refine#{n}", PlanKind.While, RowState.Completed, detail: $$"""{ "pass": {{n}} }""")).ToArray();
        var refining = Added("nested-note", Draw("nested-note", Running(passes)), "node__refine");
        Assert.Equal(RunDiagram31.Listed + 1, refining.Length);
        Assert.Equal("pass 1: Completed", refining[0]);
        Assert.Equal("… and 2 more", refining[^1]);

        // a nested loop's items of one instance are listed; of several instances they are no one box's items
        var one = Draw("nested-note", Running(
            Row("node.sections:1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1/1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1/2", PlanKind.For, RowState.Completed)));
        Assert.Equal(["1: Completed", "2: Completed"], Added("nested-note", one, "node__sections__paragraphs"));
        // of several instances they are no one box's items: a tally, as a box inside a loop body shows (D10)
        var several = Draw("nested-note", Running(
            Row("node.sections.paragraphs:1/1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:2/1", PlanKind.For, RowState.Completed)));
        Assert.Equal(["2 Completed"], Added("nested-note", several, "node__sections__paragraphs"));
        Assert.Equal("running", ClassOf(several, "node__sections__paragraphs"));

        // a loop whose list was empty has its row and no item
        var empty = Draw("nested-note", Record(Row("node.sections", PlanKind.For, RowState.Completed, detail: """{ "count": 0 }""")));
        Assert.Equal(["Completed", "out sha256:aa…"], Added("nested-note", empty, "node__sections"));
    }

    [Fact]
    public void AJobThatFailedInANestedLoopOfSeveralInstances_DrawsItsItemsAsATallyOfTheWorstClass()
    {
        // two outer items started, the job failed in the second's inner loop: the inner loop has no row of its own
        var failed = Draw("nested-note", Record(
            Row("node.sections:1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1/1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1/2", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:2/1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:2/2", PlanKind.For, RowState.Failed, null)));
        // the first instance's own row is there: one own row, so the box is that row's, with that instance's items only
        Assert.Equal(["Completed", "out sha256:aa…", "1: Completed", "2: Completed"], Added("nested-note", failed, "node__sections__paragraphs"));

        var noOwnRow = Draw("nested-note", Record(
            Row("node.sections:1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1/1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1/2", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:2/1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:2/2", PlanKind.For, RowState.Failed, null)));
        Assert.Equal(["3 Completed · 1 Failed"], Added("nested-note", noOwnRow, "node__sections__paragraphs"));
        Assert.Equal("failed", ClassOf(noOwnRow, "node__sections__paragraphs"));
    }

    [Fact]
    public void ANestedLoopWithOneOwnRow_ListsOnlyThatInstancesItems()
    {
        var run = Draw("nested-note", Running(
            Row("node.sections:1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:1/1", PlanKind.For, RowState.Completed),
            Row("node.sections.paragraphs:2/1", PlanKind.For, RowState.Failed, null),
            Row("node.sections.paragraphs:2/2", PlanKind.For, RowState.Completed)));
        Assert.Equal(["Completed", "out sha256:aa…", "1: Completed"], Added("nested-note", run, "node__sections__paragraphs"));
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
