using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Diagram;
using static Consultologist.Api.Tests.V31Diagram.DiagramFixture;

namespace Consultologist.Api.Tests.V31Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): renderers and macros, each drawn inside the frame of what uses them (part 09 § 10.4,
/// D7): one parallelogram per renderer use, one doc box per placed macro, in a <c>uses_&lt;box id&gt;</c> frame around
/// the node or deliverable; the source's edge enters the renderer box, a macro's rule points at its box.
/// </summary>
public class DiagramUsesTests
{
    /// <summary>A placement of <paramref name="site"/> with <paramref name="rule"/> (JSON text, or null for none).</summary>
    private static JsonObject Placement(string site, string? rule, string? within = null) => new()
    {
        ["site"] = site,
        ["within"] = within,
        ["if"] = rule == null ? null : JsonNode.Parse(rule),
        ["plainConverts"] = new JsonArray(),
        ["form"] = site.StartsWith("macro.", StringComparison.Ordinal) ? "block" : null,
    };

    private static JsonObject Render(int n, string source, string renderer) => new() { ["n"] = n, ["source"] = source, ["renderer"] = renderer };

    /// <summary>The entry with <paramref name="renders"/> as its <c>renders</c>.</summary>
    private static JsonObject Rendering(JsonObject entry, params JsonObject[] renders)
    {
        entry["renders"] = new JsonArray(renders.Select(r => (JsonNode?)r).ToArray());
        return entry;
    }

    /// <summary>The entry with <paramref name="placements"/> as its <c>placements</c>.</summary>
    private static JsonObject Placing(JsonObject entry, params JsonObject[] placements)
    {
        entry["placements"] = new JsonArray(placements.Select(p => (JsonNode?)p).ToArray());
        return entry;
    }

    /// <summary><see cref="DiagramFixture.Document"/> with the package's renderers and macros (fictional).</summary>
    private static string WithDefinitions(string document)
    {
        static JsonObject Format(string id) => new() { ["$ref"] = "#/$defs/" + id };
        var resolve = JsonNode.Parse(document)!.AsObject();
        resolve["renderers"] = new JsonObject
        {
            ["renderer.concept_bullets"] = new JsonObject { ["id"] = "concept_bullets", ["input"] = Format("concept_list"), ["format"] = Format("markdown_block") },
            ["renderer.headed"] = new JsonObject { ["id"] = "headed", ["input"] = Format("markdown_block"), ["format"] = Format("markdown_block") },
        };
        resolve["macros"] = new JsonObject
        {
            ["macro.letrozole_monitoring"] = new JsonObject { ["id"] = "letrozole_monitoring", ["format"] = Format("markdown_block") },
            ["macro.tone"] = new JsonObject { ["id"] = "tone", ["items"] = Strings(["formal"]), ["body"] = null },
            ["macro.tone.formal"] = new JsonObject { ["id"] = "formal", ["format"] = Format("plaintext") },
        };
        return resolve.ToJsonString();
    }

    /// <summary>§ 10.5's second example as a built document: a renderer and a macro inside one node's frame.</summary>
    private static string SecondExample()
    {
        var identify = Agent("identify_problem", "Identifying the primary problem", "markdown_block",
            "node.extract_patient_concepts.value", "node.mentions_letrozole.value");
        Rendering(identify, Render(1, "node.extract_patient_concepts.value", "concept_bullets"));
        Placing(identify, Placement("macro.letrozole_monitoring", """{"===":[{"var":"node.mentions_letrozole.value"},true]}"""));
        return WithDefinitions(Document(
            ("node.extract_patient_concepts", Agent("extract_patient_concepts", "Extracting clinical concepts", "concept_list", "input.consult_draft.content")),
            ("node.mentions_letrozole", Agent("mentions_letrozole", "Does the draft prescribe letrozole?", "judgement", "input.consult_draft.content")),
            ("node.identify_problem", identify)));
    }

    [Fact]
    public void ARendererUse_IsOneBoxPerUse_InsideTheNodesFrame()
    {
        var model = DiagramModel.Build(SecondExample());
        var lines = DiagramWriter.Write(model).Split('\n');

        string[] frame =
        [
            "    subgraph uses_node__identify_problem[\"identify_problem · Identifying the primary problem\"]",
            "        node__identify_problem__render_1[/\"renderer.concept_bullets<br/>concept_list → markdown_block\"/]",
            "        node__identify_problem__macro_letrozole_monitoring@{ shape: doc, label: \"macro.letrozole_monitoring<br/>markdown_block · placed verbatim\" }",
            "        node__identify_problem[\"identify_problem<br/>Identifying the primary problem<br/>agent → markdown_block · generated\"]",
            "        node__identify_problem__render_1 --> node__identify_problem",
            "        node__identify_problem__macro_letrozole_monitoring -->|\"placed\"| node__identify_problem",
            "    end",
        ];
        var opened = Array.IndexOf(lines, frame[0]);
        Assert.True(opened >= 0, "the uses frame is not written");
        Assert.Equal(frame, lines.Skip(opened).Take(frame.Length));
        Assert.Contains("    node__extract_patient_concepts -->|\"value\"| node__identify_problem__render_1", lines);
        Assert.Contains("    node__mentions_letrozole -.->|\"if\"| node__identify_problem__macro_letrozole_monitoring", lines);
        // The source feeds the renderer box, never the node: the read is transformed.
        Assert.DoesNotContain(model.Edges, e => e.From == "node__extract_patient_concepts" && e.To == "node__identify_problem");

        var render = Assert.Single(model.AllBoxes(), b => b.Shape == BoxShape.Parallelogram);
        Assert.Equal(("node__identify_problem__render_1", "node.identify_problem#render:1"), (render.Id, render.Key));
        Assert.False(render.ModelDerived);
        Assert.DoesNotContain(model.Root.Boxes, b => b.Key == "node.identify_problem");
    }

    [Fact]
    public void AChainOfRenderers_FeedsStepToStep()
    {
        var note = Rendering(Leaf("rendered", "plan_text", "The plan as text", "markdown_block", "computed", "node.plan.value"),
            Render(1, "node.plan.value", "concept_bullets"), Render(2, "render:1", "headed"));
        var model = DiagramModel.Build(WithDefinitions(Document(
            ("node.plan", Agent("plan", "Planning", "concept_list", "input.consult_draft.content")),
            ("node.plan_text", note))));

        var frame = Assert.Single(model.Root.Frames, f => f.Id == "uses_node__plan_text");
        Assert.Equal(["node__plan_text__render_1", "node__plan_text__render_2", "node__plan_text"], frame.Boxes.Select(b => b.Id));
        Assert.Equal(["renderer.headed", "markdown_block → markdown_block"], frame.Boxes[1].Lines);
        Assert.Equal(
            [
                new DiagramEdge("node__plan_text__render_1", "node__plan_text__render_2", EdgeStyle.Read, null),
                new DiagramEdge("node__plan_text__render_2", "node__plan_text", EdgeStyle.Read, null),
            ],
            frame.Edges);
        Assert.Contains(new DiagramEdge("node__plan", "node__plan_text__render_1", EdgeStyle.Read, "value"), model.Edges);
        Assert.DoesNotContain(model.Edges, e => e.To == "node__plan_text");
    }

    [Fact]
    public void ADirectRead_KeepsAPlainEdge_AndNoFrame()
    {
        // The body pipes node.plan.value into the renderer and reads node.plan.value.title as it is: the second keeps its edge.
        var note = Rendering(Leaf("rendered", "plan_text", "The plan as text", "markdown_block", "computed", "node.plan.value", "node.plan.value.title"),
            Render(1, "node.plan.value", "concept_bullets"));
        var model = DiagramModel.Build(WithDefinitions(Document(
            ("node.plan", Agent("plan", "Planning", "concept_list", "input.consult_draft.content")),
            ("node.direct", Agent("direct", "Reading the plan", "plaintext", "node.plan.content")),
            ("node.plan_text", note))));

        Assert.Contains(model.Root.Boxes, b => b.Key == "node.direct");
        Assert.DoesNotContain(model.Root.Frames, f => f.Id == "uses_node__direct");
        Assert.Contains(new DiagramEdge("node__plan", "node__direct", EdgeStyle.Read, "content"), model.Edges);
        Assert.Contains(new DiagramEdge("node__plan", "node__plan_text", EdgeStyle.Read, "value"), model.Edges);
        Assert.Contains(new DiagramEdge("node__plan", "node__plan_text__render_1", EdgeStyle.Read, "value"), model.Edges);
    }

    [Fact]
    public void AMacro_IsADocBox_PlacedIntoTheNode()
    {
        var model = DiagramModel.Build(Resolve("macros"));
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Contains("    subgraph uses_node__draft[\"draft · Agent draft\"]", lines);
        Assert.Contains("        node__draft__macro_section_notes__hpi@{ shape: doc, label: \"macro.section_notes.hpi<br/>plaintext · placed verbatim\" }", lines);
        Assert.Contains("        node__draft__macro_section_notes__hpi -->|\"placed\"| node__draft", lines);
        var box = Assert.Single(model.AllBoxes(), b => b.Shape == BoxShape.Doc);
        Assert.Equal(("node__draft__macro_section_notes__hpi", "node.draft#macro:section_notes.hpi"), (box.Id, box.Key));
        // The node's own reads still run into the node.
        Assert.Contains("    node__stamp -->|\"content\"| node__draft", lines);
    }

    [Fact]
    public void AMacrosIf_PointsAtTheMacroBox_NeverAtTheNode()
    {
        var letter = Placing(Agent("letter", "Drafting the letter", "markdown_block", "input.consult_draft.content", "input.on_letrozole.value"),
            Placement("macro.letrozole_monitoring", """{"===":[{"var":"input.on_letrozole.value"},true]}"""));
        var model = DiagramModel.Build(WithDefinitions(Document(("node.letter", letter))));

        Assert.Equal(
            [
                new DiagramEdge("input__consult_draft", "node__letter", EdgeStyle.Read, "content"),
                new DiagramEdge("input__on_letrozole", "node__letter__macro_letrozole_monitoring", EdgeStyle.If, "if"),
            ],
            model.Edges);
        Assert.Contains("    input__on_letrozole -.->|\"if\"| node__letter__macro_letrozole_monitoring", DiagramWriter.Write(model).Split('\n'));
    }

    [Fact]
    public void AMacroFolderPlacement_IsOneBox()
    {
        // The folder's rule and its macro's rule both decide the one macro box; the folder draws no box of its own.
        var letter = Placing(Agent("letter", "Drafting the letter", "markdown_block", "input.consult_draft.content", "input.tone.value", "input.formal_ok.value"),
            Placement("macro-folder.tone", """{"===":[{"var":"input.tone.value"},"formal"]}"""),
            Placement("macro.tone.formal", """{"===":[{"var":"input.formal_ok.value"},true]}"""));
        var model = DiagramModel.Build(WithDefinitions(Document(("node.letter", letter))));

        var frame = Assert.Single(model.Root.Frames);
        Assert.Equal(["node__letter__macro_tone__formal", "node__letter"], frame.Boxes.Select(b => b.Id));
        Assert.Equal(["macro.tone.formal", "plaintext · placed verbatim"], frame.Boxes[0].Lines);
        Assert.Contains(new DiagramEdge("input__tone", "node__letter__macro_tone__formal", EdgeStyle.If, "if"), model.Edges);
        Assert.Contains(new DiagramEdge("input__formal_ok", "node__letter__macro_tone__formal", EdgeStyle.If, "if"), model.Edges);
        Assert.DoesNotContain(model.Edges, e => e.To == "node__letter" && e.From != "input__consult_draft");

        var golden = DiagramModel.Build(Resolve("macros"));
        Assert.Single(golden.AllBoxes(), b => b.Shape == BoxShape.Doc);
        Assert.Contains(new DiagramEdge("node__classify", "node__draft__macro_section_notes__hpi", EdgeStyle.If, "if"), golden.Edges);
    }

    [Fact]
    public void AUsesFrameInsideALoop_SitsInTheLoopsFrame()
    {
        // The body writes the sibling as node.inner_terms; the entry's names member maps it to its rooted key.
        var text = Rendering(Leaf("rendered", "inner_text", "The terms as text", "plaintext", "computed", "node.each.inner_terms.value"),
            Render(1, "node.inner_terms.value", "concept_bullets"));
        text["names"] = new JsonObject { ["node.inner_terms"] = "node.each.inner_terms" };
        var model = DiagramModel.Build(WithDefinitions(Document(
            ("node.each", For("each", "Each section", "input.sections.value", "node.each.inner_text", "node.each.inner_terms", "node.each.inner_text")),
            ("node.each.inner_terms", Agent("inner_terms", "The section's terms", "concept_list", "item.value").In("node.each")),
            ("node.each.inner_text", text.In("node.each")))));
        var lines = DiagramWriter.Write(model).Split('\n');

        var loop = Assert.Single(model.Root.Frames, f => f.Id == "for_node__each");
        var uses = Assert.Single(loop.Frames, f => f.Id == "uses_node__each__inner_text");
        Assert.Equal(["node__each__inner_text__render_1", "node__each__inner_text"], uses.Boxes.Select(b => b.Id));
        Assert.Contains("        subgraph uses_node__each__inner_text[\"inner_text · The terms as text\"]", lines);
        Assert.Contains("            node__each__inner_text__render_1 --> node__each__inner_text", lines);
        Assert.Contains(new DiagramEdge("node__each__inner_terms", "node__each__inner_text__render_1", EdgeStyle.Read, "value"), model.Edges);
        Assert.Contains(new DiagramEdge("node__each__inner_text", "node__each", EdgeStyle.Read, "ret"), model.Edges);
    }

    [Fact]
    public void ADeliverableThatUsesARenderer_IsFramedToo()
    {
        var note = Rendering(Deliverable("note", "Consultation note", "node.plan.value", "run.date"), Render(1, "node.plan.value", "concept_bullets"));
        var model = DiagramModel.Build(WithDefinitions(Document(
            ("node.plan", Agent("plan", "Planning", "concept_list", "input.consult_draft.content")),
            ("deliverable.note", note))));
        var lines = DiagramWriter.Write(model).Split('\n');

        var frame = Assert.Single(model.Root.Frames, f => f.Id == "uses_deliverable__note");
        Assert.Equal("note · Consultation note", frame.Title);
        Assert.Equal(["deliverable__note__render_1", "deliverable__note"], frame.Boxes.Select(b => b.Id));
        Assert.Contains("        deliverable__note{{\"note<br/>Consultation note\"}}", lines);
        Assert.Contains("        deliverable__note__render_1 --> deliverable__note", lines);
        Assert.Contains(new DiagramEdge("node__plan", "deliverable__note__render_1", EdgeStyle.Read, "value"), model.Edges);
        Assert.Contains(new DiagramEdge("run__date", "deliverable__note", EdgeStyle.Read, null), model.Edges);
    }

    [Fact]
    public void AnOlderResolveWithoutRenders_DrawsNoFrame()
    {
        // A stored document from before renders and placements: the body's pipe is a plain read, nothing is framed.
        var text = Leaf("rendered", "plan_text", "The plan as text", "markdown_block", "computed", "node.plan.value");
        text["body"] = "{{ node.plan.value | renderer.concept_bullets }}\n";
        var model = DiagramModel.Build(WithDefinitions(Document(
            ("node.plan", Agent("plan", "Planning", "concept_list", "input.consult_draft.content")),
            ("node.plan_text", text))));

        Assert.Empty(model.Root.Frames);
        Assert.DoesNotContain(model.AllBoxes(), b => b.Shape is BoxShape.Parallelogram or BoxShape.Doc);
        Assert.Contains(new DiagramEdge("node__plan", "node__plan_text", EdgeStyle.Read, "value"), model.Edges);
    }

    [Theory]
    // A renderer or macro the document does not define: its formats are '?'.
    [InlineData("""[{"n":1,"source":"input.consult_draft.content","renderer":"missing"}]""", """[{"site":"macro.missing","if":null}]""",
        "        node__x__render_1[/\"renderer.missing<br/>? → ?\"/]", "        node__x__macro_missing@{ shape: doc, label: \"macro.missing<br/>? · placed verbatim\" }")]
    // Steps of no known shape (no n, no renderer, not an object), a placement with no site: nothing is drawn for them.
    [InlineData("""[{"source":"input.consult_draft.content","renderer":"r"},{"n":1},7,{"n":"1","renderer":"r"}]""", """[{"if":null},5,{"site":7}]""")]
    // A later step whose earlier step is not listed: it is drawn, fed by nothing.
    [InlineData("""[{"n":2,"source":"render:1","renderer":"r"}]""", "[]", "        node__x__render_2[/\"renderer.r<br/>? → ?\"/]")]
    public void ARenderOrPlacementOfNoKnownShape_StillDraws(string renders, string placements, params string[] expected)
    {
        var resolve = """{"nodes":{"node.x":{"kind":"agent","reads":["input.consult_draft.content"],"renders":""" + renders + ",\"placements\":" + placements + "}}}";

        var lines = Lines(resolve);

        foreach (var line in expected)
            Assert.Contains(line, lines);
        Assert.Contains(lines, l => l.Trim().StartsWith("node__x[", StringComparison.Ordinal));
    }
}
