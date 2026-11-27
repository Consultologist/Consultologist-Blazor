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
    public void AnEdgeLabelAndAFrameTitle_AreEscaped()
    {
        var model = new DiagramModel(
            new DiagramFrame("", "",
                [new DiagramBox("a", "node.a", BoxShape.Rectangle, ["a"], false), new DiagramBox("b", "node.b", BoxShape.Rectangle, ["b"], false)],
                [new DiagramFrame("for_b", "b: per \"x\" <y>", [], [], [])], []),
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
            ], [], []),
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
