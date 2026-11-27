using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Diagram;
using static Consultologist.Api.Tests.V31Diagram.DiagramFixture;

namespace Consultologist.Api.Tests.V31Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): the diagram model built from <c>resolve.json</c> (part 09 §§ 10.2 to 10.4): production
/// nodes, pills, loops, branches, deliverables and their edges (D4, D6), drawn the same for every golden.
/// </summary>
public class DiagramModelTests
{
    [Fact]
    public void IdOf_ReplacesEveryDotWithTwoUnderscores()
    {
        Assert.Equal("node__sections__paragraphs__draft_paragraph", DiagramModel.IdOf("node.sections.paragraphs.draft_paragraph"));
        Assert.Equal("input__consult_draft", DiagramModel.IdOf("input.consult_draft"));
        Assert.Equal("deliverable__consult_note", DiagramModel.IdOf("deliverable.consult_note"));
    }

    [Fact]
    public void AnAgentNode_IsARectangleWithItsFormatAndProducer()
    {
        var lines = Lines(Document(("node.draft", Agent("draft", "Drafting", "markdown_block"))));

        Assert.Contains("    node__draft[\"draft<br/>Drafting<br/>agent → markdown_block · generated\"]", lines);
    }

    [Fact]
    public void AComputedNode_IsADoubleBox()
    {
        var lines = Lines(Resolve("nested-note"));

        Assert.Contains("    node__section_count[[\"section_count<br/>Counting the sections<br/>computed → number\"]]", lines);
        Assert.Contains("    node__plan -->|\"value\"| node__section_count", lines);
    }

    [Fact]
    public void ARenderedNode_IsARectangle()
    {
        var lines = Lines(Resolve("macros"));

        Assert.Contains("    node__stamp[\"stamp<br/>Rendered stamp<br/>rendered → plaintext · computed\"]", lines);
    }

    [Fact]
    public void AnUnknownKind_IsAPlainRectangle()
    {
        var model = DiagramModel.Build(Document(("node.oracle", Leaf("oracle", "oracle", "Asking the oracle", "plaintext", "generated", "input.consult_draft.content"))));

        var box = Assert.Single(model.AllBoxes(), b => b.Key == "node.oracle");
        Assert.Equal(BoxShape.Rectangle, box.Shape);
        Assert.Equal(["oracle", "Asking the oracle"], box.Lines);
        Assert.Contains(new DiagramEdge("input__consult_draft", "node__oracle", EdgeStyle.Read, "content"), model.Edges);
    }

    [Fact]
    public void AnInput_IsAPillAtTheTopLevel_OncePerPackage()
    {
        var model = DiagramModel.Build(Document(
            ("node.plan", Agent("plan", "Planning", "plaintext", "input.consult_draft.content")),
            ("node.loop", For("loop", "Each point", "input.points.value", "node.loop.draft", "node.loop.draft")),
            ("node.loop.draft", Agent("draft", "Drafting", "plaintext", "input.consult_draft.content", "item.value").In("node.loop"))));

        var pill = Assert.Single(model.AllBoxes(), b => b.Key == "input.consult_draft");
        Assert.Equal(("input__consult_draft", BoxShape.Pill, false), (pill.Id, pill.Shape, pill.ModelDerived));
        Assert.Equal(["input.consult_draft"], pill.Lines);
        Assert.Contains(pill, model.Root.Boxes);
        // Pills first, in order of first read.
        Assert.Equal(["input.consult_draft", "input.points"], model.Root.Boxes.TakeWhile(b => b.Shape == BoxShape.Pill).Select(b => b.Key));
        Assert.Contains("    input__consult_draft([\"input.consult_draft\"])", DiagramWriter.Write(model).Split('\n'));
    }

    [Fact]
    public void AnInputNothingReads_IsStillAPill_AfterTheReadOnes()
    {
        var document = JsonNode.Parse(Document(("node.plan", Agent("plan", "Planning", "plaintext", "input.consult_draft.content"))))!.AsObject();
        document["inputs"]!["unused_letter"] = new JsonObject { ["label"] = "Unused" };

        var model = DiagramModel.Build(document.ToJsonString());

        Assert.Equal(["input.consult_draft", "input.unused_letter"], model.Root.Boxes.Where(b => b.Shape == BoxShape.Pill).Select(b => b.Key));
    }

    [Fact]
    public void ARunFactAndAProfileField_ArePills()
    {
        var lines = Lines(Document(("deliverable.letter", Deliverable("letter", "Letter", "profile.signature", "run.date"))));

        Assert.Contains("    run__date([\"run.date\"])", lines);
        Assert.Contains("    profile__signature([\"profile.signature\"])", lines);
        Assert.Contains("    run__date --> deliverable__letter", lines);
        Assert.Contains("    profile__signature --> deliverable__letter", lines);
    }

    [Fact]
    public void ASnippet_IsAPill()
    {
        var lines = Lines(Resolve("functions"));

        Assert.Contains("    snippet__standards([\"snippet.standards\"])", lines);
        Assert.Contains("    snippet__standards -->|\"for\"| for_node__standards", lines);
    }

    [Fact]
    public void ARead_IsLabelledWithTheMemberRead()
    {
        var model = DiagramModel.Build(Document(
            ("node.plan", Agent("plan", "Planning", "outline", "input.consult_draft.content")),
            ("node.count", Leaf("computed", "count", "Counting", "number", "computed", "node.plan.value")),
            ("node.points", For("points", "Each point", "node.plan.value.points", "node.points.draft", "node.points.draft")),
            ("node.points.draft", Agent("draft", "Drafting", "plaintext", "item.value").In("node.points")),
            ("deliverable.note", Deliverable("note", "Note", "node.plan.content", "node.points.items", "run.date"))));

        Assert.Equal(
            [
                new DiagramEdge("input__consult_draft", "node__plan", EdgeStyle.Read, "content"),
                new DiagramEdge("node__plan", "node__count", EdgeStyle.Read, "value"),
                new DiagramEdge("node__plan", "for_node__points", EdgeStyle.Read, "for"),
                new DiagramEdge("node__points__draft", "node__points", EdgeStyle.Read, "ret"),
                new DiagramEdge("node__plan", "deliverable__note", EdgeStyle.Read, "content"),
                new DiagramEdge("node__points", "deliverable__note", EdgeStyle.Read, "items"),
                new DiagramEdge("run__date", "deliverable__note", EdgeStyle.Read, null),
            ],
            model.Edges);
        var lines = DiagramWriter.Write(model).Split('\n');
        Assert.Contains("    input__consult_draft -->|\"content\"| node__plan", lines);
        Assert.Contains("    node__plan -->|\"value\"| node__count", lines);
        Assert.Contains("    node__points -->|\"items\"| deliverable__note", lines);
        Assert.Contains("    run__date --> deliverable__note", lines);
    }

    [Fact]
    public void ARead_IsOneEdgePerSourceReaderAndMember()
    {
        var model = DiagramModel.Build(Document(
            ("node.plan", Agent("plan", "Planning", "outline")),
            ("node.draft", Agent("draft", "Drafting", "plaintext", "node.plan.content", "node.plan.value", "node.plan.value"))));

        Assert.Equal(
            [new DiagramEdge("node__plan", "node__draft", EdgeStyle.Read, "content"), new DiagramEdge("node__plan", "node__draft", EdgeStyle.Read, "value")],
            model.Edges);
    }

    [Fact]
    public void ANodesIf_IsADottedIfEdgeFromWhatItReads()
    {
        var gated = Agent("billing", "Drafting the billing section", "plaintext", "input.consult_draft.content", "input.include_billing.value");
        gated["if"] = JsonNode.Parse("""{"===":[{"var":"input.include_billing.value"},true]}""");

        var model = DiagramModel.Build(Document(("node.billing", gated)));

        Assert.Equal(
            [
                new DiagramEdge("input__consult_draft", "node__billing", EdgeStyle.Read, "content"),
                new DiagramEdge("input__include_billing", "node__billing", EdgeStyle.If, "if"),
            ],
            model.Edges);
        Assert.Contains("    input__include_billing -.->|\"if\"| node__billing", DiagramWriter.Write(model).Split('\n'));
    }

    /// <summary>A leaf whose <c>if</c> and prompt both read <c>input.include_billing.value</c>, with <paramref name="edges"/> as its <c>graph.edges</c> (null: none).</summary>
    private static string IfAndBodyRead(JsonArray? edges, string bodyRead = "input.include_billing.value")
    {
        var gated = Agent("billing", "Drafting the billing section", "plaintext",
            new[] { "input.consult_draft.content", bodyRead, "input.include_billing.value" }.Distinct().Order(StringComparer.Ordinal).ToArray());
        gated["if"] = JsonNode.Parse("""{"===":[{"var":"input.include_billing.value"},true]}""");
        var document = JsonNode.Parse(Document(("node.billing", gated)))!.AsObject();
        if (edges == null)
            document["graph"]!.AsObject().Remove("edges");
        else
            document["graph"]!["edges"] = edges;
        return document.ToJsonString();
    }

    private static JsonObject GraphEdge(string from, string kind, string to) => new() { ["from"] = from, ["kind"] = kind, ["to"] = to };

    [Fact]
    public void AReadOfTheIfAndTheBody_DrawsTheDataEdgeAndTheIfEdge_WhenGraphEdgesHoldsBoth()
    {
        var model = DiagramModel.Build(IfAndBodyRead(
        [
            GraphEdge("input.consult_draft", "reads", "node.billing"),
            GraphEdge("input.include_billing", "reads", "node.billing"),
            GraphEdge("input.include_billing", "if", "node.billing"),
        ]));

        Assert.Equal(
            [
                new DiagramEdge("input__consult_draft", "node__billing", EdgeStyle.Read, "content"),
                new DiagramEdge("input__include_billing", "node__billing", EdgeStyle.Read, "value"),
                new DiagramEdge("input__include_billing", "node__billing", EdgeStyle.If, "if"),
            ],
            model.Edges);
    }

    [Fact]
    public void AReadOfTheIfAndTheBody_DrawsTheIfEdgeOnly_WithoutGraphEdges()
    {
        var resolve = IfAndBodyRead(null);

        var model = DiagramModel.Build(resolve);

        Assert.Equal(
            [
                new DiagramEdge("input__consult_draft", "node__billing", EdgeStyle.Read, "content"),
                new DiagramEdge("input__include_billing", "node__billing", EdgeStyle.If, "if"),
            ],
            model.Edges);
        Assert.Contains("    input__include_billing -.->|\"if\"| node__billing", Lines(resolve));
    }

    [Fact]
    public void AConditionOnlyRead_DrawsNoDataEdge_WhenGraphEdgesHoldsOnlyTheIf()
    {
        var model = DiagramModel.Build(IfAndBodyRead(
        [
            GraphEdge("input.consult_draft", "reads", "node.billing"),
            GraphEdge("input.include_billing", "if", "node.billing"),
        ]));

        Assert.DoesNotContain(model.Edges, e => e.From == "input__include_billing" && e.Style == EdgeStyle.Read);
        Assert.Contains(new DiagramEdge("input__include_billing", "node__billing", EdgeStyle.If, "if"), model.Edges);
    }

    [Fact]
    public void AReadsEdgeAnotherMemberExplains_DrawsNoDataEdgeForTheConditionsMember()
    {
        // The body reads input.include_billing.label, the if its value: graph.edges' reads edge (per key, not per member) is the label's.
        var model = DiagramModel.Build(IfAndBodyRead(
        [
            GraphEdge("input.consult_draft", "reads", "node.billing"),
            GraphEdge("input.include_billing", "reads", "node.billing"),
            GraphEdge("input.include_billing", "if", "node.billing"),
        ], bodyRead: "input.include_billing.label"));

        Assert.Contains(new DiagramEdge("input__include_billing", "node__billing", EdgeStyle.Read, "label"), model.Edges);
        Assert.DoesNotContain(new DiagramEdge("input__include_billing", "node__billing", EdgeStyle.Read, "value"), model.Edges);
        Assert.Contains(new DiagramEdge("input__include_billing", "node__billing", EdgeStyle.If, "if"), model.Edges);
    }

    [Fact]
    public void ALoopWithNoNodes_DrawsNoFrame_AndItsListFeedsItsSummaryBox()
    {
        var empty = For("points", "Each point", "input.points.value", "node.points.draft");
        var idle = new JsonObject { ["id"] = "idle", ["kind"] = "while", ["label"] = "Idling", ["while"] = true, ["max"] = 2, ["nodes"] = new JsonArray() };

        var model = DiagramModel.Build(Document(("node.points", empty), ("node.idle", idle)));
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Empty(model.Root.Frames);
        Assert.DoesNotContain(lines, l => l.TrimStart().StartsWith("subgraph", StringComparison.Ordinal));
        Assert.Contains(new DiagramEdge("input__points", "node__points", EdgeStyle.Read, "for"), model.Edges);
        Assert.Contains("    input__points -->|\"for\"| node__points", lines);
        Assert.Contains(model.AllBoxes(), b => b.Key == "node.idle");
    }

    [Fact]
    public void APartsIf_IsAnIfEdgeIntoTheNode()
    {
        var lines = Lines(Resolve("macros"));

        Assert.Contains("    node__classify -.->|\"if\"| node__draft", lines);
        Assert.Contains("    node__stamp -->|\"content\"| node__draft", lines);
        // The macro's own rule reads node.classify.value too: it is the macro box's (D7), never a read edge into the node.
        Assert.DoesNotContain("    node__classify -->|\"value\"| node__draft", lines);
    }

    [Fact]
    public void AFor_IsAFrameASummaryBoxAndForAndRetEdges()
    {
        var model = DiagramModel.Build(Resolve("nested-note"));
        var lines = DiagramWriter.Write(model).Split('\n');

        var frame = Assert.Single(model.Root.Frames, f => f.Id == "for_node__sections");
        Assert.Equal("sections: per node.plan.value.sections item", frame.Title);
        // The inner loop's summary box sits beside its frame, inside the outer loop's.
        Assert.Equal(["node.sections.outline", "node.sections.paragraphs"], frame.Boxes.Select(b => b.Key));
        var inner = Assert.Single(frame.Frames);
        Assert.Equal("for_node__sections__paragraphs", inner.Id);
        Assert.Equal(["node.sections.paragraphs.draft_paragraph"], inner.Boxes.Select(b => b.Key));
        Assert.Contains(model.Root.Boxes, b => b.Key == "node.sections");

        Assert.Contains("    subgraph for_node__sections[\"sections: per node.plan.value.sections item\"]", lines);
        Assert.Contains("        subgraph for_node__sections__paragraphs[\"paragraphs: per node.sections.outline.value.points item\"]", lines);
        Assert.Contains("    node__sections[\"sections<br/>Sections<br/>item in node.plan.value.sections\"]", lines);
        Assert.Contains("    node__plan -->|\"for\"| for_node__sections", lines);
        Assert.Contains("    node__sections__outline -->|\"for\"| for_node__sections__paragraphs", lines);
        Assert.Contains("    node__sections__paragraphs -->|\"ret\"| node__sections", lines);
        Assert.Contains("    node__sections__paragraphs__draft_paragraph -->|\"ret\"| node__sections__paragraphs", lines);
        // The frame, then its summary box after it, in the scope that holds the loop.
        var opened = Array.IndexOf(lines, "    subgraph for_node__sections[\"sections: per node.plan.value.sections item\"]");
        var summary = Array.IndexOf(lines, "    node__sections[\"sections<br/>Sections<br/>item in node.plan.value.sections\"]");
        Assert.True(opened >= 0 && summary > opened && lines[summary - 1] == "    end");
    }

    [Fact]
    public void AWhile_CarriesItsConditionAndBound()
    {
        var model = DiagramModel.Build(Resolve("nested-note"));
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Contains(model.Root.Frames, f => f.Id == "while_node__refine" && f.Boxes.Select(b => b.Key).SequenceEqual(["node.refine.draft_summary", "node.refine.judge"]));
        Assert.Contains("    node__refine[\"refine<br/>Refining the summary<br/>while node.refine.judge.value.verdict ≠ #quot;good#quot; — at most 3\"]", lines);
        Assert.Contains("    node__refine__judge -.->|\"if\"| node__refine", lines);
        Assert.Contains("    node__refine__draft_summary -->|\"ret\"| node__refine", lines);
        Assert.Contains("    node__refine__draft_summary -->|\"content\"| node__refine__judge", lines);
    }

    [Fact]
    public void AnUntil_IsPrintedOnItsLoop()
    {
        var loop = For("points", "Each point", "input.points.value", "node.points.draft", "node.points.draft");
        loop["until"] = JsonNode.Parse("""{"===":[{"var":"input.stop_early.value"},true]}""");
        loop["reads"] = Strings(["input.points.value", "input.stop_early.value"]);

        var model = DiagramModel.Build(Document(
            ("node.points", loop),
            ("node.points.draft", Agent("draft", "Drafting", "plaintext", "item.value").In("node.points"))));

        var box = Assert.Single(model.AllBoxes(), b => b.Key == "node.points");
        Assert.Equal(["points", "Each point", "item in input.points.value · until input.stop_early.value = true"], box.Lines);
        Assert.Contains(new DiagramEdge("input__points", "for_node__points", EdgeStyle.Read, "for"), model.Edges);
        Assert.Contains(new DiagramEdge("input__stop_early", "node__points", EdgeStyle.If, "if"), model.Edges);
        Assert.DoesNotContain(model.Edges, e => e.From == "input__points" && e.To == "node__points");
    }

    [Fact]
    public void AnIf_DrawsAFramePerBranch_AndRetNamesTheBranch()
    {
        var model = DiagramModel.Build(Resolve("conditional"));
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Contains("        subgraph if_node__patient_summaries__narrative_only_then[\"narrative_only: then\"]", lines);
        Assert.DoesNotContain(lines, l => l.Contains("if_node__patient_summaries__narrative_only_else", StringComparison.Ordinal));
        Assert.Contains("        node__patient_summaries__narrative_only[\"narrative_only<br/>Narrative sections only<br/>if item.value.kind = #quot;narrative#quot;\"]", lines);
        Assert.Contains("    node__patient_summaries__narrative_only__patient_summary -->|\"ret (then)\"| node__patient_summaries__narrative_only", lines);
        Assert.Contains("    node__patient_summaries__narrative_only -->|\"ret\"| node__patient_summaries", lines);
        Assert.Contains("    input__include_billing -.->|\"if\"| node__standing_sections__applies", lines);
    }

    [Fact]
    public void ACase_DrawsEachBranch_ExceptAnExplicitlyEmptyOne()
    {
        var model = DiagramModel.Build(Resolve("conditional"));
        var lines = DiagramWriter.Write(model).Split('\n');

        var loop = Assert.Single(model.Root.Frames, f => f.Id == "for_node__sections_draft");
        Assert.Equal(["case_node__sections_draft__route_branch_1", "case_node__sections_draft__route_branch_2"], loop.Frames.Select(f => f.Id));
        Assert.Equal(["route: narrative", "route: table"], loop.Frames.Select(f => f.Title));
        Assert.Contains("        node__sections_draft__route[\"route<br/>Routing a section by its kind<br/>case item.value.kind\"]", lines);
        Assert.Contains("    node__sections_draft__route__narrative_draft -->|\"ret (narrative)\"| node__sections_draft__route", lines);
        Assert.Contains("    node__sections_draft__route__table_draft -->|\"ret (table)\"| node__sections_draft__route", lines);
        Assert.DoesNotContain(lines, l => l.Contains("branch_3", StringComparison.Ordinal) || l.Contains("ret (omit)", StringComparison.Ordinal));
    }

    [Fact]
    public void ACasesSubject_IsAnIfEdgeFromWhatItReads()
    {
        var route = new JsonObject
        {
            ["id"] = "route",
            ["kind"] = "case",
            ["label"] = "Routing by the plan's kind",
            ["case"] = "node.plan.value.kind",
            ["branches"] = new JsonArray(new JsonObject { ["when"] = Strings(["short"]), ["nodes"] = Strings(["node.route.brief"]), ["ret"] = "node.route.brief" }),
            ["else"] = new JsonObject { ["nodes"] = Strings(["node.route.full"]), ["ret"] = "node.route.full" },
            ["nodes"] = Strings(["node.route.brief", "node.route.full"]),
            ["reads"] = Strings(["node.plan.value"]),
        };

        var model = DiagramModel.Build(Document(
            ("node.plan", Agent("plan", "Planning", "section_plan")),
            ("node.route", route),
            ("node.route.brief", Agent("brief", "A brief note", "plaintext").In("node.route")),
            ("node.route.full", Agent("full", "A full note", "plaintext").In("node.route"))));

        Assert.Equal(["case_node__route_branch_1", "case_node__route_else"], model.Root.Frames.Select(f => f.Id));
        Assert.Equal(
            [
                new DiagramEdge("node__plan", "node__route", EdgeStyle.If, "if"),
                new DiagramEdge("node__route__brief", "node__route", EdgeStyle.Read, "ret (short)"),
                new DiagramEdge("node__route__full", "node__route", EdgeStyle.Read, "ret (else)"),
            ],
            model.Edges);
    }

    [Fact]
    public void ADeliverable_IsAHexagon_FedByWhatItsTemplateReads()
    {
        var model = DiagramModel.Build(Resolve("nested-note"));
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Equal(BoxShape.Hexagon, Assert.Single(model.AllBoxes(), b => b.Key == "deliverable.note").Shape);
        Assert.Equal("deliverable.note", model.Root.Boxes[^1].Key);
        Assert.Contains("    deliverable__note{{\"note<br/>Nested consultation note\"}}", lines);
        Assert.Contains("    node__refine -->|\"content\"| deliverable__note", lines);
    }

    [Fact]
    public void ADeliverableInAFolder_HasItsRootedKeyAsItsId()
    {
        var lines = Lines(Resolve("checked-note"));

        Assert.Contains("    deliverable__patient__patient_letter{{\"patient_letter<br/>Letter to the patient\"}}", lines);
        Assert.Contains("    node__sections -->|\"value\"| deliverable__patient__patient_letter", lines);
    }

    [Fact]
    public void TheSameIdInTwoScopes_GetsTwoBoxes()
    {
        var model = DiagramModel.Build(Document(
            ("node.draft", Agent("draft", "Drafting the summary", "plaintext", "input.consult_draft.content")),
            ("node.loop", For("loop", "Each point", "input.points.value", "node.loop.draft", "node.loop.draft")),
            ("node.loop.draft", Agent("draft", "Drafting a point", "plaintext", "node.draft.content", "item.value").In("node.loop"))));

        Assert.Equal(["node__draft", "node__loop__draft"], model.AllBoxes().Where(b => b.Lines[0] == "draft").Select(b => b.Id));
        Assert.Contains(new DiagramEdge("node__draft", "node__loop__draft", EdgeStyle.Read, "content"), model.Edges);
        var lines = DiagramWriter.Write(model).Split('\n');
        Assert.Single(lines, l => l.StartsWith("    node__draft[", StringComparison.Ordinal));
        Assert.Single(lines, l => l.StartsWith("        node__loop__draft[", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("""{"nodes":{},"nodes":{}}""")]
    [InlineData("""{"nodes":{"node.x":{},"node.x":{}}}""")]
    [InlineData("""{"nodes":{"node.x":{}},"deliverables":{"deliverable.y":{}},"graph":{"order":"x"}}""", "    node__x[\"x\"]", "    deliverable__y{{\"y\"}}")]
    [InlineData("""{"nodes":{"node.x":{"kind":"agent","reads":5,"label":7,"format":[]}},"inputs":[],"inputOrder":{"a":1}}""", "    node__x[\"x<br/>agent → ?\"]")]
    [InlineData("""{"nodes":{"node.l":{"kind":"for","nodes":["node.l.gone"]},"node.w":{"kind":"while"},"node.i":{"kind":"if","then":7},"node.c":{"kind":"case","branches":[1,{"when":null}]}}}""",
        "    node__l[\"l<br/>item in ?\"]", "    node__w[\"w<br/>while null — at most —\"]", "    node__i[\"i<br/>if null\"]", "    node__c[\"c<br/>case ?\"]")]
    // A loop that lists itself is in a body, so no top-level entry: nothing is drawn.
    [InlineData("""{"nodes":{"node.a":{"kind":"for","nodes":["node.a"],"ret":"node.a","in":"node.a.items"}}}""")]
    public void AResolveWithMembersMissing_StillDraws(string resolve, params string[] boxes)
    {
        var text = PackageDiagram.Generate(resolve);

        Assert.StartsWith("---\nconfig:\n  layout: elk\n---\nflowchart TD\n", text, StringComparison.Ordinal);
        var lines = text.Split('\n');
        foreach (var box in boxes)
            Assert.Contains(box, lines);
    }

    [Fact]
    public void AnEntryWithNoLabelNoReadsNoRenders_IsItsKeysLastName()
    {
        var model = DiagramModel.Build("""{"nodes":{"node.sections.draft":{"kind":"agent"}}}""");

        var box = Assert.Single(model.AllBoxes());
        Assert.Equal("node__sections__draft", box.Id);
        Assert.Equal("draft", box.Lines[0]);
        Assert.Empty(model.Edges);
    }

    [Theory]
    [MemberData(nameof(GoldenNames))]
    public void EveryGraphEdgeBetweenDrawnBoxes_IsDrawn(string golden)
    {
        var resolve = Resolve(golden);
        var model = DiagramModel.Build(resolve);
        var drawn = model.AllBoxes().Select(b => b.Key).ToHashSet(StringComparer.Ordinal);
        // A macro is drawn as its box in the frame of what places it (D7): a placed edge's source is that box.
        bool Placed(string from, string to) => from.StartsWith("macro.", StringComparison.Ordinal) && drawn.Contains($"{to}#macro:{from["macro.".Length..]}");
        var edges = JsonNode.Parse(resolve)!["graph"]!["edges"]!.AsArray()
            .Select(e => (From: (string)e!["from"]!, To: (string)e["to"]!, Kind: (string)e["kind"]!))
            .Where(e => (e.Kind is "reads" or "if" or "for" or "ret" && drawn.Contains(e.From) && drawn.Contains(e.To)) || (e.Kind == "placed" && Placed(e.From, e.To)))
            .ToList();
        var written = model.Edges.Concat(FrameEdges(model.Root)).ToList();

        Assert.NotEmpty(edges);
        foreach (var (from, to, kind) in edges)
        {
            var source = DiagramModel.IdOf(from);
            var target = DiagramModel.IdOf(to);
            // A read piped through renderers enters the reader's first renderer box (D7), which feeds the reader.
            var readers = model.AllBoxes().Where(b => b.Key == to || b.Key.StartsWith(to + "#render:", StringComparison.Ordinal)).Select(b => b.Id).ToHashSet();
            Assert.True(kind switch
            {
                "reads" => model.Edges.Any(e => e.From == source && readers.Contains(e.To) && e.Style == EdgeStyle.Read && e.Label is not "for" && e.Label?.StartsWith("ret", StringComparison.Ordinal) != true),
                "if" => model.Edges.Contains(new DiagramEdge(source, target, EdgeStyle.If, "if")),
                "for" => model.Edges.Any(e => e.From == source && e.To == "for_" + target && e.Label == "for"),
                "placed" => written.Contains(new DiagramEdge($"{target}__macro_{DiagramModel.IdOf(from["macro.".Length..])}", target, EdgeStyle.Read, "placed")),
                _ => model.Edges.Any(e => e.From == source && e.To == target && e.Label?.StartsWith("ret", StringComparison.Ordinal) == true),
            }, $"{golden}: {from} -{kind}-> {to} is not drawn");
        }
    }

    private static IEnumerable<DiagramEdge> FrameEdges(DiagramFrame frame) => frame.Edges.Concat(frame.Frames.SelectMany(FrameEdges));

    public static TheoryData<string> GoldenNames() => new(Goldens);
}
