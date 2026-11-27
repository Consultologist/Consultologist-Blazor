using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31.Diagram;

namespace Consultologist.Api.Tests.V31Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): the drawing frozen. Each golden <c>Golden/&lt;name&gt;.dag.mmd</c> is
/// <see cref="PackageDiagram.Generate"/> of a package's <c>resolve.json</c> to the byte (<see cref="DiagramFixture.Drawings"/>:
/// the five resolve goldens and two migrated packages). With <c>GOLDEN_UPDATE_V31=1</c> the test rewrites them instead; a
/// rewritten golden is read in full against part 09 §§ 10.2 to 10.4 before it is committed.
/// </summary>
public class DiagramGoldenTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static TheoryData<string> Names() => new(DiagramFixture.Drawings);

    /// <summary>The folder of the goldens in the source tree (<c>tests/V31Diagram/Golden</c>).</summary>
    private static string GoldenFolder() => Path.GetFullPath(Path.Combine(CompiledGoldenTests.GoldenFolder(), "..", "..", "V31Diagram", "Golden"));

    [Theory]
    [MemberData(nameof(Names))]
    public void TheDiagram_IsTheGolden(string name)
    {
        var text = PackageDiagram.Generate(DiagramFixture.ResolveOf(name));
        var path = Path.Combine(GoldenFolder(), name + ".dag.mmd");
        if (Environment.GetEnvironmentVariable("GOLDEN_UPDATE_V31") == "1")
        {
            Directory.CreateDirectory(GoldenFolder());
            File.WriteAllText(path, text, Utf8NoBom);
        }

        Assert.True(File.Exists(path), $"{name}.dag.mmd is missing: run with GOLDEN_UPDATE_V31=1, then read it.");
        Assert.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(Utf8NoBom.GetBytes(text)), $"{name}.dag.mmd is not what the diagram gives.");
    }

    /// <summary>Part 09 § 10.5's first drawing, verbatim.</summary>
    private const string SpecsDrawing = """
        ---
        config:
          layout: elk
        ---
        flowchart TD
            input__sections(["input.sections"])
            input__consult_draft(["input.consult_draft"])
            run__date(["run.date"])

            subgraph for_node__sections_draft["sections_draft: per input.sections.value item"]
                subgraph case_node__sections_draft__route_branch_1["route: narrative"]
                    node__sections_draft__route__narrative_draft["narrative_draft<br/>Drafting a narrative section<br/>agent → markdown_block · generated"]
                end
                subgraph case_node__sections_draft__route_branch_2["route: table"]
                    node__sections_draft__route__table_draft["table_draft<br/>Drafting a table section<br/>agent → markdown_block · generated"]
                end
                node__sections_draft__route["route<br/>Routing a section by its kind<br/>case item.value.kind"]
            end
            node__sections_draft["sections_draft<br/>Sections — drafting each requested section by its kind<br/>item in input.sections.value"]

            subgraph checks["Checks"]
                check__coverage[["coverage<br/>Every requested section is present<br/>computed → check_result"]]
            end

            deliverable__consult_note{{"consult_note<br/>Consult note"}}

            input__consult_draft -->|"content"| node__sections_draft__route__narrative_draft
            input__consult_draft -->|"content"| node__sections_draft__route__table_draft
            node__sections_draft__route__narrative_draft -->|"ret (narrative)"| node__sections_draft__route
            node__sections_draft__route__table_draft -->|"ret (table)"| node__sections_draft__route
            input__sections -->|"for"| for_node__sections_draft
            node__sections_draft__route -->|"ret"| node__sections_draft
            node__sections_draft -->|"items"| deliverable__consult_note
            run__date --> deliverable__consult_note
            deliverable__consult_note -->|"content"| check__coverage
            input__sections -->|"value"| check__coverage
            check__coverage ==>|"gates"| deliverable__consult_note
            style checks stroke-width:3px
        """;

    /// <summary>
    /// § 10.5's first example as a built <c>resolve.json</c>: <c>example-conditional-sections</c> abridged to its routing
    /// loop and its deliverable, with the coverage check gating the note; only the members the model reads.
    /// </summary>
    private static string SpecsExample()
    {
        JsonObject Branch(string kind) => new()
        {
            ["when"] = DiagramFixture.Strings([kind]),
            ["nodes"] = DiagramFixture.Strings([$"node.sections_draft.route.{kind}_draft"]),
            ["ret"] = $"node.sections_draft.route.{kind}_draft",
        };
        var route = new JsonObject
        {
            ["id"] = "route",
            ["kind"] = "case",
            ["label"] = "Routing a section by its kind",
            ["producer"] = "computed",
            ["case"] = "item.value.kind",
            ["branches"] = new JsonArray(Branch("narrative"), Branch("table")),
            ["reads"] = DiagramFixture.Strings(["item.value.kind"]),
        };
        var note = DiagramFixture.Deliverable("consult_note", "Consult note", "node.sections_draft.items", "run.date");
        note["check"] = "check.coverage";
        var document = JsonNode.Parse(DiagramFixture.Document(
            ("node.sections_draft", DiagramFixture.For("sections_draft", "Sections — drafting each requested section by its kind", "input.sections.value",
                "node.sections_draft.route", "node.sections_draft.route")),
            ("node.sections_draft.route", route.In("node.sections_draft")),
            ("node.sections_draft.route.narrative_draft", DiagramFixture.Agent("narrative_draft", "Drafting a narrative section", "markdown_block",
                "input.consult_draft.content", "item.value").In("node.sections_draft.route")),
            ("node.sections_draft.route.table_draft", DiagramFixture.Agent("table_draft", "Drafting a table section", "markdown_block",
                "input.consult_draft.content", "item.value").In("node.sections_draft.route")),
            ("check.coverage", DiagramFixture.Leaf("computed", "coverage", "Every requested section is present", "check_result", "computed",
                "deliverable.consult_note.content", "input.sections.value")),
            ("deliverable.consult_note", note)))!.AsObject();
        document["inputOrder"] = DiagramFixture.Strings(["sections", "consult_draft"]);
        return document.ToJsonString();
    }

    [Fact]
    public void TheSpecsExample_IsDrawnAsTheSpecDrawsIt()
    {
        var drawn = PackageDiagram.Generate(SpecsExample()).Split('\n');
        var spec = SpecsDrawing.Split('\n');

        // Every line of the spec's drawing, in the spec's order; the drawing adds only the model-derived class (D9).
        var next = 0;
        foreach (var line in spec)
        {
            var found = Array.IndexOf(drawn, line, next);
            Assert.True(found >= 0, $"not drawn, or not in the spec's order: {line}");
            next = found + 1;
        }

        string[] added =
            ["    classDef modelDerived stroke-dasharray: 5 5", "    class node__sections_draft__route__narrative_draft,node__sections_draft__route__table_draft modelDerived"];
        Assert.Equal(added, drawn.Except(spec));
        // And nothing written twice: the drawing is the spec's lines, those two and the text's final line end, no more.
        Assert.Equal(spec.Length + added.Length + 1, drawn.Length);
        Assert.Equal("    style checks stroke-width:3px", drawn[^2]);
        Assert.Equal("", drawn[^1]);
    }
}
