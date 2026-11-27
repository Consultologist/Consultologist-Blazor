using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Diagram;
using static Consultologist.Api.Tests.V31Diagram.DiagramFixture;

namespace Consultologist.Api.Tests.V31Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): checks, check loops and gates (part 09 § 10.4, D5, D9): every check in a
/// <c>checks</c> frame apart from production, with a heavier border (<c>style checks stroke-width:3px</c>, last); a
/// check a double box whose reads are ordinary data edges; a check loop a frame inside Checks, <c>-.-o|aligned|</c> to
/// the production loop whose <c>.items</c> it iterates; a gate <c>==&gt;|gates|</c> from the check to its deliverable.
/// </summary>
public class DiagramChecksTests
{
    private static DiagramFrame ChecksFrame(DiagramModel model) => Assert.Single(model.Root.Frames, f => f.Id == "checks");

    private static IEnumerable<DiagramBox> BoxesOf(DiagramFrame frame) => frame.Boxes.Concat(frame.Frames.SelectMany(BoxesOf));

    /// <summary>The last line of a text that ends with a line end.</summary>
    private static string LastLine(string text) => text.TrimEnd('\n').Split('\n')[^1];

    [Theory]
    [InlineData("checked-note")]
    [InlineData("functions")]
    public void EveryCheck_IsInsideTheChecksFrame_ApartFromProduction(string golden)
    {
        var resolve = Resolve(golden);
        var model = DiagramModel.Build(resolve);
        var text = DiagramWriter.Write(model);
        var lines = text.Split('\n');

        var frame = ChecksFrame(model);
        Assert.Equal("Checks", frame.Title);
        var checks = JsonNode.Parse(resolve)!["checks"]!.AsObject().Select(c => c.Key).ToList();
        Assert.NotEmpty(checks);
        Assert.Equal(checks.Order(StringComparer.Ordinal), BoxesOf(frame).Select(b => b.Key).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(model.AllBoxes().Except(BoxesOf(frame)), b => b.Key.StartsWith("check.", StringComparison.Ordinal));
        Assert.True(model.HasChecks);

        Assert.Contains("    subgraph checks[\"Checks\"]", lines);
        Assert.Single(lines, l => l.Contains("style checks", StringComparison.Ordinal));
        Assert.Equal("    style checks stroke-width:3px", LastLine(text));
        // After the model-derived class lines too (the checks read model output).
        Assert.Contains(lines, l => l.StartsWith("    class ", StringComparison.Ordinal));
    }

    [Fact]
    public void APackageWithNoCheck_HasNoChecksFrameAndNoStyle()
    {
        var model = DiagramModel.Build(Resolve("nested-note"));
        var text = DiagramWriter.Write(model);

        Assert.False(model.HasChecks);
        Assert.DoesNotContain(model.Root.Frames, f => f.Id == "checks");
        Assert.DoesNotContain("subgraph checks", text, StringComparison.Ordinal);
        Assert.DoesNotContain("style checks", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACheck_IsADoubleBox_WithOrdinaryReadEdges()
    {
        var model = DiagramModel.Build(Resolve("checked-note"));
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Equal(BoxShape.DoubleBox, Assert.Single(model.AllBoxes(), b => b.Key == "check.proofread").Shape);
        Assert.Contains("        check__proofread[[\"proofread<br/>Proofreading the note<br/>computed → check_result\"]]", lines);
        Assert.Contains("        check__all_sections_covered[[\"all_sections_covered<br/>Every section covers its planned terms<br/>computed → check_result\"]]", lines);
        Assert.Contains("            check__part_coverage__coverage[[\"coverage<br/>Section covers its planned terms<br/>computed → check_result\"]]", lines);
        // A check reading a deliverable, another check, a check loop's items: ordinary data edges labelled by member (D4).
        Assert.Contains("    deliverable__note -->|\"content\"| check__proofread", lines);
        Assert.Contains("    check__all_sections_covered -->|\"value\"| check__proofread", lines);
        Assert.Contains("    check__part_coverage -->|\"items\"| check__all_sections_covered", lines);

        // A check in a check loop that reads production nodes directly.
        var functions = DiagramWriter.Write(DiagramModel.Build(Resolve("functions"))).Split('\n');
        Assert.Contains("    node__extract_input_terms -->|\"value\"| check__section_coverage__coverage", functions);
        Assert.Contains("    node__extract_note_terms -->|\"value\"| check__section_coverage__coverage", functions);
    }

    [Fact]
    public void ANodeReadingACheck_IsFedByTheChecksBox()
    {
        var check = Leaf("computed", "coverage", "Terms covered", "check_result", "computed", "node.plan.value");
        var model = DiagramModel.Build(Document(
            ("node.plan", Agent("plan", "Planning", "outline", "input.consult_draft.content")),
            ("check.coverage", check),
            ("node.summary", Agent("summary", "Summarising", "plaintext", "check.coverage.value"))));

        Assert.Contains(new DiagramEdge("node__plan", "check__coverage", EdgeStyle.Read, "value"), model.Edges);
        Assert.Contains(new DiagramEdge("check__coverage", "node__summary", EdgeStyle.Read, "value"), model.Edges);
        Assert.Equal(["check.coverage"], BoxesOf(ChecksFrame(model)).Select(b => b.Key));
    }

    [Fact]
    public void ACheckLoop_IsAFrameInsideChecks_AlignedWithItsProductionLoop()
    {
        var model = DiagramModel.Build(Resolve("checked-note"));
        var lines = DiagramWriter.Write(model).Split('\n');

        var loop = Assert.Single(ChecksFrame(model).Frames);
        Assert.Equal(("for_check__part_coverage", "part_coverage: per node.sections.items (aligned)"), (loop.Id, loop.Title));
        Assert.Equal(["check.part_coverage.coverage"], loop.Boxes.Select(b => b.Key));
        Assert.Contains(ChecksFrame(model).Boxes, b => b.Key == "check.part_coverage");

        Assert.Contains("        subgraph for_check__part_coverage[\"part_coverage: per node.sections.items (aligned)\"]", lines);
        Assert.Contains("        check__part_coverage[\"part_coverage<br/>Section coverage<br/>item in node.sections.items\"]", lines);
        Assert.Contains(new DiagramEdge("for_check__part_coverage", "for_node__sections", EdgeStyle.Reference, "aligned"), model.Edges);
        Assert.Contains("    for_check__part_coverage -.-o|\"aligned\"| for_node__sections", lines);
        Assert.Contains("    node__sections -->|\"for\"| for_check__part_coverage", lines);
        Assert.Contains("    check__part_coverage__coverage -->|\"ret\"| check__part_coverage", lines);

        var functions = DiagramWriter.Write(DiagramModel.Build(Resolve("functions"))).Split('\n');
        Assert.Contains("    for_check__section_coverage -.-o|\"aligned\"| for_node__standards", functions);
        Assert.Contains("    node__standards -->|\"for\"| for_check__section_coverage", functions);
    }

    /// <summary>A production loop <c>points</c> over an input and <paramref name="second"/>, a loop over <paramref name="list"/>; <c>graph.edges</c> holds the compiler's <c>aligned</c> edge.</summary>
    private static string TwoLoops(string second, string list)
    {
        var loop = For(second.Split('.')[1], "Reviewing each point", list, $"{second}.review", $"{second}.review");
        var document = JsonNode.Parse(Document(
            ("node.points", For("points", "Each point", "input.points.value", "node.points.draft", "node.points.draft")),
            ("node.points.draft", Agent("draft", "Drafting", "plaintext", "item.value").In("node.points")),
            (second, loop),
            ($"{second}.review", Leaf("computed", "review", "Reviewing", "check_result", "computed", "item.value").In(second))))!.AsObject();
        document["graph"]!["edges"] = new JsonArray(
            new JsonObject { ["from"] = "node.points", ["kind"] = "for", ["to"] = second },
            new JsonObject { ["from"] = "node.points", ["kind"] = "aligned", ["to"] = second });
        return document.ToJsonString();
    }

    [Fact]
    public void AProductionLoopOverItems_GetsNoAlignedEdge()
    {
        var model = DiagramModel.Build(TwoLoops("node.reviews", "node.points.items"));
        var lines = DiagramWriter.Write(model).Split('\n');

        // D5: graph.edges holds an aligned edge for it, but only a check loop's frame draws one.
        Assert.DoesNotContain(model.Edges, e => e.Label == "aligned" || e.Style == EdgeStyle.Reference);
        Assert.Contains(new DiagramEdge("node__points", "for_node__reviews", EdgeStyle.Read, "for"), model.Edges);
        Assert.Contains("    subgraph for_node__reviews[\"reviews: per node.points.items item\"]", lines);
    }

    [Fact]
    public void ACheckLoopOverAListNoProductionLoopReturns_IsNotAligned()
    {
        var model = DiagramModel.Build(TwoLoops("check.reviews", "input.points.value"));

        var loop = Assert.Single(ChecksFrame(model).Frames);
        Assert.Equal("reviews: per input.points.value item", loop.Title);
        Assert.DoesNotContain(model.Edges, e => e.Label == "aligned");
        Assert.Contains(new DiagramEdge("input__points", "for_check__reviews", EdgeStyle.Read, "for"), model.Edges);

        var aligned = DiagramModel.Build(TwoLoops("check.reviews", "node.points.items"));
        Assert.Equal("reviews: per node.points.items (aligned)", Assert.Single(ChecksFrame(aligned).Frames).Title);
        Assert.Contains(new DiagramEdge("for_check__reviews", "for_node__points", EdgeStyle.Reference, "aligned"), aligned.Edges);
    }

    [Fact]
    public void AGate_IsTheThickEdgeFromTheCheckToItsDeliverable()
    {
        var model = DiagramModel.Build(Resolve("checked-note"));
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Equal([new DiagramEdge("check__proofread", "deliverable__note", EdgeStyle.Gate, "gates")], model.Edges.Where(e => e.Style == EdgeStyle.Gate));
        Assert.Contains("    check__proofread ==>|\"gates\"| deliverable__note", lines);

        var functions = DiagramWriter.Write(DiagramModel.Build(Resolve("functions"))).Split('\n');
        Assert.Contains("    check__all_sections ==>|\"gates\"| deliverable__emr__consult", functions);
    }

    [Fact]
    public void AGateNamingNoCheck_DrawsNothing()
    {
        var letter = Deliverable("letter", "Letter", "input.consult_draft.content");
        letter["check"] = "check.missing";

        var model = DiagramModel.Build(Document(("deliverable.letter", letter)));

        Assert.DoesNotContain(model.Edges, e => e.Style == EdgeStyle.Gate);
        Assert.False(model.HasChecks);
    }
}
