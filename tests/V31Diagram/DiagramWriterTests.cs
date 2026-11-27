using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Diagram;
using static Consultologist.Api.Tests.V31Diagram.DiagramFixture;

namespace Consultologist.Api.Tests.V31Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): the Mermaid text written from a diagram model (part 09 §§ 10.1, 10.4): the ELK
/// frontmatter (D2), labels escaped (D8), the model-derived class (D9) and a run's overlay.
/// </summary>
public class DiagramWriterTests
{
    [Fact]
    public void Generate_StartsWithTheElkFrontmatter()
    {
        var text = PackageDiagram.Generate(Resolve("nested-note"));

        Assert.StartsWith("---\nconfig:\n  layout: elk\n---\nflowchart TD\n    ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.Equal(text, DiagramWriter.Write(DiagramModel.Build(Resolve("nested-note"))));
    }

    [Fact]
    public void ALabelWithMermaidSyntax_IsEscaped()
    {
        var lines = Lines(Document(
            ("node.quoted", Agent("quoted", "He said \"<b>stop</b>\" | end ]", "plaintext")),
            ("node.broken", Agent("broken", "First line\nsecond line\r\nthird", "plaintext"))));

        Assert.Contains("    node__quoted[\"quoted<br/>He said #quot;#lt;b#gt;stop#lt;/b#gt;#quot; | end ]<br/>agent → plaintext · generated\"]", lines);
        Assert.Contains("    node__broken[\"broken<br/>First line second line third<br/>agent → plaintext · generated\"]", lines);
    }

    [Fact]
    public void AHashInPackageText_IsEscapedFirst_SoNoEntityIsDecoded()
    {
        // Mermaid decodes #name; and #35; as entities: a package's own '#' is written #35;, before the writer's #quot;, #lt;, #gt;.
        var lines = Lines(Document(("node.hashed", Agent("hashed", "item #35; and See note #quot; for \"x\"", "plaintext"))));

        Assert.Contains("    node__hashed[\"hashed<br/>item #35;35; and See note #35;quot; for #quot;x#quot;<br/>agent → plaintext · generated\"]", lines);
    }

    [Fact]
    public void AnEdgeLabelAndAFrameTitle_AreEscaped()
    {
        var model = new DiagramModel(
            new DiagramFrame("", "",
                [new DiagramBox("a", "node.a", BoxShape.Rectangle, ["a"], false), new DiagramBox("b", "node.b", BoxShape.Rectangle, ["b"], false)],
                [new DiagramFrame("for_b", "b: per \"x\" <y>", [], [], [], 1)], [], 0),
            [new DiagramEdge("a", "b", EdgeStyle.Read, "ret (\"<z>\")")], false);

        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Contains("    subgraph for_b[\"b: per #quot;x#quot; #lt;y#gt;\"]", lines);
        Assert.Contains("    a -->|\"ret (#quot;#lt;z#gt;#quot;)\"| b", lines);
    }

    [Fact]
    public void EachShapeAndEdgeStyle_IsWrittenAsTheSpecDrawsIt()
    {
        var model = new DiagramModel(
            new DiagramFrame("", "",
            [
                new DiagramBox("p", "input.p", BoxShape.Pill, ["input.p"], false),
                new DiagramBox("r", "node.r", BoxShape.Rectangle, ["r"], false),
                new DiagramBox("d", "node.d", BoxShape.DoubleBox, ["d"], false),
                new DiagramBox("g", "node.g#render:1", BoxShape.Parallelogram, ["renderer.g"], false),
                new DiagramBox("m", "node.m#macro:m", BoxShape.Doc, ["macro.m"], false),
                new DiagramBox("t", "node.t", BoxShape.TagRect, ["t", "call f"], false),
                new DiagramBox("h", "deliverable.h", BoxShape.Hexagon, ["h"], false),
            ], [], [], 0),
            [
                new DiagramEdge("p", "r", EdgeStyle.Read, null),
                new DiagramEdge("p", "d", EdgeStyle.If, "if"),
                new DiagramEdge("t", "fn_f", EdgeStyle.Reference, "calls"),
                new DiagramEdge("d", "h", EdgeStyle.Gate, "gates"),
            ], false);

        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Contains("    p([\"input.p\"])", lines);
        Assert.Contains("    r[\"r\"]", lines);
        Assert.Contains("    d[[\"d\"]]", lines);
        Assert.Contains("    g[/\"renderer.g\"/]", lines);
        Assert.Contains("    m@{ shape: doc, label: \"macro.m\" }", lines);
        Assert.Contains("    t@{ shape: tag-rect, label: \"t<br/>call f\" }", lines);
        Assert.Contains("    h{{\"h\"}}", lines);
        Assert.Contains("    p --> r", lines);
        Assert.Contains("    p -.->|\"if\"| d", lines);
        Assert.Contains("    t -.-o|\"calls\"| fn_f", lines);
        Assert.Contains("    d ==>|\"gates\"| h", lines);
    }

    /// <summary>The entry with one <c>renders</c> step: <paramref name="source"/> through <c>renderer.concept_bullets</c>.</summary>
    private static JsonObject Rendering(JsonObject entry, string source)
    {
        entry["renders"] = new JsonArray(new JsonObject { ["n"] = 1, ["source"] = source, ["renderer"] = "concept_bullets" });
        return entry;
    }

    /// <summary>The index of the one line of <paramref name="lines"/> that is <paramref name="line"/>.</summary>
    private static int At(string[] lines, string line)
    {
        Assert.Single(lines, l => l == line);
        return Array.IndexOf(lines, line);
    }

    [Fact]
    public void AUsesFrame_IsWrittenInThePlaceOfTheBoxItWraps()
    {
        // As § 10.5's second example: the frame of the node that uses a renderer stands where that node stands in its scope.
        var lines = Lines(Document(
            ("node.plan", Agent("plan", "Planning", "concept_list", "input.consult_draft.content")),
            ("node.plan_text", Rendering(Leaf("rendered", "plan_text", "The plan as text", "markdown_block", "computed", "node.plan.value"), "node.plan.value")),
            ("node.summary", Agent("summary", "Summarising", "plaintext", "node.plan_text.content")),
            ("deliverable.note", Deliverable("note", "Consultation note", "node.summary.content"))));

        var plan = At(lines, "    node__plan[\"plan<br/>Planning<br/>agent → concept_list · generated\"]");
        var frame = At(lines, "    subgraph uses_node__plan_text[\"plan_text · The plan as text\"]");
        var summary = At(lines, "    node__summary[\"summary<br/>Summarising<br/>agent → plaintext · generated\"]");
        Assert.True(plan < frame && frame < summary, "the uses frame is not between the boxes it stands between");
        Assert.Equal(["", "    subgraph uses_node__plan_text[\"plan_text · The plan as text\"]"], lines[(frame - 1)..(frame + 1)]);
        Assert.Equal(["    end", ""], lines[(summary - 2)..summary]);
    }

    [Fact]
    public void AUsesFrameInsideALoop_IsWrittenInThePlaceOfTheBoxItWraps()
    {
        var text = Rendering(Leaf("rendered", "inner_text", "The terms as text", "plaintext", "computed", "node.each.inner_terms.value"), "node.inner_terms.value");
        text["names"] = new JsonObject { ["node.inner_terms"] = "node.each.inner_terms" };
        var lines = Lines(Document(
            ("node.each", For("each", "Each section", "input.sections.value", "node.each.inner_after", "node.each.inner_terms", "node.each.inner_text", "node.each.inner_after")),
            ("node.each.inner_terms", Agent("inner_terms", "The section's terms", "concept_list", "item.value").In("node.each")),
            ("node.each.inner_text", text.In("node.each")),
            ("node.each.inner_after", Agent("inner_after", "After the terms", "plaintext", "node.each.inner_text.content").In("node.each"))));

        var terms = At(lines, "        node__each__inner_terms[\"inner_terms<br/>The section's terms<br/>agent → concept_list · generated\"]");
        var frame = At(lines, "        subgraph uses_node__each__inner_text[\"inner_text · The terms as text\"]");
        var after = At(lines, "        node__each__inner_after[\"inner_after<br/>After the terms<br/>agent → plaintext · generated\"]");
        Assert.True(terms < frame && frame < after, "the uses frame is not between its siblings");
    }

    [Fact]
    public void TheTopLevel_IsPillsProductionFunctionsChecksThenDeliverables()
    {
        // § 10.5: a function's frame after the call (the third example), the Checks frame before the deliverables (the
        // first); a deliverable's uses frame in the deliverable's place; the Checks frame's style the last line.
        var document = JsonNode.Parse(Document(
            ("node.hpi_section", new JsonObject
            {
                ["id"] = "hpi_section", ["kind"] = "call", ["label"] = "The history section", ["function"] = "standard_section",
                ["with"] = new JsonObject { ["draft"] = "input.consult_draft.content" }, ["reads"] = Strings(["input.consult_draft.content"]),
            }),
            ("check.coverage", Leaf("computed", "coverage", "Every section is present", "check_result", "computed", "deliverable.note.content")),
            ("deliverable.note", Rendering(Deliverable("note", "Consultation note", "node.hpi_section.value"), "node.hpi_section.value")),
            ("deliverable.letter", Deliverable("letter", "Letter", "node.hpi_section.content"))))!.AsObject();
        document["functions"] = new JsonObject
        {
            ["standard_section"] = new JsonObject
            {
                ["format"] = new JsonObject { ["$ref"] = "#/$defs/plaintext" },
                ["inputs"] = new JsonObject { ["draft"] = new JsonObject() },
                ["inputOrder"] = Strings(["draft"]),
                ["body"] = Strings(["node.section"]),
                ["nodes"] = new JsonObject { ["node.section"] = Agent("section", "Drafting the section", "plaintext", "param.draft.content") },
            },
        };
        var lines = Lines(document.ToJsonString());

        int[] order =
        [
            At(lines, "    input__consult_draft([\"input.consult_draft\"])"),
            At(lines, "    node__hpi_section@{ shape: tag-rect, label: \"hpi_section<br/>call standard_section\" }"),
            At(lines, "    subgraph fn_standard_section[\"function standard_section (draft) → plaintext\"]"),
            At(lines, "    subgraph checks[\"Checks\"]"),
            At(lines, "    subgraph uses_deliverable__note[\"note · Consultation note\"]"),
            At(lines, "    deliverable__letter{{\"letter<br/>Letter\"}}"),
        ];
        Assert.Equal(order.Order(), order);
        Assert.Equal("    style checks stroke-width:3px", lines[^2]);
        Assert.Equal("", lines[^1]);
    }

    [Fact]
    public void AFrameAtAPosition_IsWrittenBeforeTheBoxThere_AndAfterEveryBoxPastTheEnd()
    {
        var box = (string id) => new DiagramBox(id, "node." + id, BoxShape.Rectangle, [id], false);
        var model = new DiagramModel(
            new DiagramFrame("", "", [box("a"), box("b")],
                [new DiagramFrame("uses_x", "x", [box("x")], [], [], 1), new DiagramFrame("late", "late", [], [], [], 7)], [], 0),
            [], false);

        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Equal(
            ["flowchart TD", "    a[\"a\"]", "", "    subgraph uses_x[\"x\"]", "        x[\"x\"]", "    end", "", "    b[\"b\"]", "", "    subgraph late[\"late\"]", "    end", ""],
            lines[4..]);
    }

    [Fact]
    public void AModelDerivedNode_GetsTheDashedClass()
    {
        var text = PackageDiagram.Generate(Document(
            ("node.plan", Agent("plan", "Planning", "plaintext")),
            ("node.count", Leaf("computed", "count", "Counting", "number", "computed")),
            ("node.notes", Agent("notes", "Noting", "plaintext"))));

        Assert.EndsWith("\n    classDef modelDerived stroke-dasharray: 5 5\n    class node__plan,node__notes modelDerived\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoModelDerivedNode_WritesNoClassDef()
    {
        var text = PackageDiagram.Generate(Document(("node.count", Leaf("computed", "count", "Counting", "number", "computed", "input.points.value"))));

        Assert.DoesNotContain("classDef", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n    class ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverlay_AddsLinesAClassAndAnAnchor_ToTheBoxItsKeyNames()
    {
        var model = DiagramModel.Build(Document(
            ("node.plan", Agent("plan", "Planning", "plaintext", "input.consult_draft.content")),
            ("node.count", Leaf("computed", "count", "Counting", "number", "computed", "node.plan.value"))));
        var overlay = new Dictionary<string, BoxOverlay>
        {
            ["node.plan"] = new(["Completed", "sha256:ab12"], "completed", "row-node.plan"),
            ["node.count"] = new(["Skipped \"not run\""], "skipped", null),
        };

        var written = DiagramWriter.Write(model, overlay, [("completed", "fill:#dfd"), ("skipped", "fill:#eee")]);
        var lines = written.Mermaid.Split('\n');

        Assert.Contains("    node__plan[\"plan<br/>Planning<br/>agent → plaintext · generated<br/>Completed<br/>sha256:ab12\"]", lines);
        Assert.Contains("    node__count[[\"count<br/>Counting<br/>computed → number<br/>Skipped #quot;not run#quot;\"]]", lines);
        Assert.EndsWith(
            "\n    classDef modelDerived stroke-dasharray: 5 5\n    class node__plan modelDerived\n"
            + "    classDef completed fill:#dfd\n    classDef skipped fill:#eee\n    class node__plan completed\n    class node__count skipped\n",
            written.Mermaid, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, string> { ["node__plan"] = "row-node.plan" }, written.Anchors);
        // The overlay adds to boxes; it never adds, removes or renames one.
        Assert.Equal(DiagramWriter.Write(model).Split('\n').Length, lines.Length - 4);
    }
}
