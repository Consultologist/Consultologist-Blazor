using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Diagram;
using static Consultologist.Api.Tests.V31Diagram.DiagramFixture;

namespace Consultologist.Api.Tests.V31Diagram;

/// <summary>
/// #1510 (specVersion 31, area 13): calls and functions (part 09 § 10.4, R1, D3): one <c>tag-rect</c> box per call, its
/// <c>with</c> arguments running in and <c>-.-o|calls|</c> to the function's frame; each function drawn once, in a
/// <c>fn_&lt;function&gt;</c> frame holding its <c>param.*</c> pills and its own nodes, drawn from its entry's
/// <c>nodes</c> and <c>reads</c> (R1: <c>graph.edges</c> holds no edge inside a function).
/// </summary>
public class DiagramFunctionsTests
{
    /// <summary>A <c>call</c> entry of <paramref name="function"/>; its reads are its <c>with</c> arguments.</summary>
    private static JsonObject Call(string id, string label, string function, params (string Param, string Source)[] with) => new()
    {
        ["id"] = id,
        ["kind"] = "call",
        ["label"] = label,
        ["producer"] = "computed",
        ["format"] = new JsonObject { ["$ref"] = "#/$defs/plaintext" },
        ["function"] = function,
        ["with"] = new JsonObject(with.Select(w => KeyValuePair.Create(w.Param, (JsonNode?)w.Source))),
        ["modelDerived"] = true,
        ["reads"] = Strings(with.Select(w => w.Source).Where(s => s != "item").Order(StringComparer.Ordinal)),
    };

    /// <summary>
    /// A <c>functions</c> entry: its <paramref name="inputs"/> (id, format) in that order as <c>inputOrder</c> but written
    /// sorted in <c>inputs</c>, as the canonical document writes a map; <c>body</c> the keys with no parent.
    /// </summary>
    private static JsonObject Function(string label, string format, (string Id, string Format)[] inputs, string ret, params (string Key, JsonObject Entry)[] nodes) => new()
    {
        ["label"] = label,
        ["format"] = new JsonObject { ["$ref"] = "#/$defs/" + format },
        ["inputs"] = new JsonObject(inputs.OrderBy(i => i.Id, StringComparer.Ordinal).Select(i =>
            KeyValuePair.Create(i.Id, (JsonNode?)new JsonObject { ["label"] = i.Id, ["format"] = new JsonObject { ["$ref"] = "#/$defs/" + i.Format } }))),
        ["inputOrder"] = Strings(inputs.Select(i => i.Id)),
        ["ret"] = ret,
        ["body"] = Strings(nodes.Where(n => n.Entry["parent"] == null).Select(n => n.Key)),
        ["nodes"] = new JsonObject(nodes.Select(n => KeyValuePair.Create(n.Key, (JsonNode?)n.Entry))),
    };

    /// <summary><paramref name="document"/> with <paramref name="functions"/> as its <c>functions</c> (and the renderer <c>concept_bullets</c>).</summary>
    private static string WithFunctions(string document, params (string Id, JsonObject Function)[] functions)
    {
        var resolve = JsonNode.Parse(document)!.AsObject();
        resolve["functions"] = new JsonObject(functions.Select(f => KeyValuePair.Create(f.Id, (JsonNode?)f.Function)));
        resolve["renderers"] = new JsonObject
        {
            ["renderer.concept_bullets"] = new JsonObject
            {
                ["id"] = "concept_bullets",
                ["input"] = new JsonObject { ["$ref"] = "#/$defs/concept_list" },
                ["format"] = new JsonObject { ["$ref"] = "#/$defs/markdown_block" },
            },
        };
        return resolve.ToJsonString();
    }

    /// <summary>§ 10.5's function <c>standard_section</c>: a draft from both parameters, then the titled section it returns.</summary>
    private static JsonObject StandardSection()
    {
        var section = Leaf("computed", "section", "The titled section", "titled_section", "computed", "node.standard_draft.content");
        section["expression"] = JsonNode.Parse("""{"cat":[{"var":"node.standard_draft.content"}]}""");
        return Function("A titled section", "titled_section", [("standard", "standard_section"), ("draft", "markdown_block")], "node.section",
            ("node.standard_draft", Agent("standard_draft", "Drafting the section", "markdown_block", "param.draft.content", "param.standard.value")),
            ("node.section", section));
    }

    /// <summary>§ 10.5's third example as a built document: a call and its function.</summary>
    private static string ThirdExample() => WithFunctions(
        Document(("node.hpi_section", Call("hpi_section", "The history section", "standard_section",
            ("standard", "input.hpi_standard.value"), ("draft", "input.consult_draft.content")))),
        ("standard_section", StandardSection()));

    [Fact]
    public void ACall_IsATagRect_WithItsArgumentsRunningIn()
    {
        var model = DiagramModel.Build(ThirdExample());
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Contains("    node__hpi_section@{ shape: tag-rect, label: \"hpi_section<br/>call standard_section\" }", lines);
        var box = Assert.Single(model.Root.Boxes, b => b.Key == "node.hpi_section");
        Assert.Equal(BoxShape.TagRect, box.Shape);
        Assert.Contains("    input__consult_draft -->|\"content\"| node__hpi_section", lines);
        Assert.Contains("    input__hpi_standard -->|\"value\"| node__hpi_section", lines);

        // On the golden the call sits in a loop: its `item` argument draws no edge, its input does.
        var golden = DiagramWriter.Write(DiagramModel.Build(Resolve("functions"))).Split('\n');
        Assert.Contains("        node__standards__section@{ shape: tag-rect, label: \"section<br/>call draft_section\" }", golden);
        Assert.Contains("    input__consult_draft -->|\"content\"| node__standards__section", golden);
    }

    [Fact]
    public void ACall_RefersToItsFunctionsFrame()
    {
        var model = DiagramModel.Build(ThirdExample());
        var lines = DiagramWriter.Write(model).Split('\n');

        Assert.Contains(new DiagramEdge("node__hpi_section", "fn_standard_section", EdgeStyle.Reference, "calls"), model.Edges);
        Assert.Contains("    node__hpi_section -.-o|\"calls\"| fn_standard_section", lines);
        Assert.Contains(new DiagramEdge("node__standards__section", "fn_draft_section", EdgeStyle.Reference, "calls"), DiagramModel.Build(Resolve("functions")).Edges);
    }

    [Fact]
    public void AFunction_IsDrawnOnce_HoweverManyCalls()
    {
        var model = DiagramModel.Build(WithFunctions(
            Document(
                ("node.hpi_section", Call("hpi_section", "The history", "standard_section", ("standard", "input.hpi_standard.value"), ("draft", "input.consult_draft.content"))),
                ("node.plan_section", Call("plan_section", "The plan", "standard_section", ("standard", "input.plan_standard.value"), ("draft", "input.consult_draft.content")))),
            ("standard_section", StandardSection())));
        var lines = DiagramWriter.Write(model).Split('\n');

        var frame = Assert.Single(model.Root.Frames, f => f.Id.StartsWith("fn_", StringComparison.Ordinal));
        Assert.Equal("fn_standard_section", frame.Id);
        Assert.Single(model.AllBoxes(), b => b.Id == "fn_standard_section__node__standard_draft");
        Assert.Single(lines, l => l.Contains("subgraph fn_standard_section[", StringComparison.Ordinal));
        Assert.Equal(["node__hpi_section", "node__plan_section"],
            model.Edges.Where(e => e.To == "fn_standard_section" && e.Style == EdgeStyle.Reference && e.Label == "calls").Select(e => e.From));
        Assert.Equal(2, model.AllBoxes().Count(b => b.Shape == BoxShape.TagRect));
    }

    [Fact]
    public void AFunctionsFrame_NamesItsInputsAndResult()
    {
        var model = DiagramModel.Build(ThirdExample());

        // inputOrder (standard, draft), not the inputs map's order (draft, standard).
        var frame = Assert.Single(model.Root.Frames, f => f.Id == "fn_standard_section");
        Assert.Equal("function standard_section (standard, draft) → titled_section", frame.Title);
        Assert.Contains("    subgraph fn_standard_section[\"function standard_section (standard, draft) → titled_section\"]", DiagramWriter.Write(model).Split('\n'));
        Assert.Equal("function draft_section (standard, draft) → plaintext",
            Assert.Single(DiagramModel.Build(Resolve("functions")).Root.Frames, f => f.Id == "fn_draft_section").Title);
    }

    [Fact]
    public void AFunctionsInside_IsDrawnFromItsNodesAndReads()
    {
        var model = DiagramModel.Build(ThirdExample());
        var lines = DiagramWriter.Write(model).Split('\n');

        // § 10.5's function, with D3's pill ids; the read into the ret node is drawn, and no extra ret edge.
        string[] frame =
        [
            "    subgraph fn_standard_section[\"function standard_section (standard, draft) → titled_section\"]",
            "        fn_standard_section__param__standard([\"param.standard\"])",
            "        fn_standard_section__param__draft([\"param.draft\"])",
            "        fn_standard_section__node__standard_draft[\"standard_draft<br/>Drafting the section<br/>agent → markdown_block · generated\"]",
            "        fn_standard_section__node__section[[\"section<br/>The titled section<br/>computed → titled_section\"]]",
            "        fn_standard_section__param__draft --> fn_standard_section__node__standard_draft",
            "        fn_standard_section__param__standard --> fn_standard_section__node__standard_draft",
            "        fn_standard_section__node__standard_draft -->|\"content\"| fn_standard_section__node__section",
            "    end",
        ];
        var opened = Array.IndexOf(lines, frame[0]);
        Assert.True(opened >= 0, "the function's frame is not written");
        Assert.Equal(frame, lines.Skip(opened).Take(frame.Length));
        Assert.DoesNotContain(model.Edges.Concat(model.Root.Frames.SelectMany(f => f.Edges)), e => e.Label?.StartsWith("ret", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(model.Edges, e => e.To == "fn_standard_section" && e.Label != "calls");

        // The golden (R1): its graph.edges hold no edge between the function's nodes; they are drawn from the entries' reads.
        // A parameter's data read is unlabelled (D4); its condition read (the if on param.standard.value.title) is the if edge.
        var golden = Assert.Single(DiagramModel.Build(Resolve("functions")).Root.Frames, f => f.Id == "fn_draft_section");
        Assert.Equal(
            ["fn_draft_section__param__standard", "fn_draft_section__param__draft", "fn_draft_section__node__standard_draft",
                "fn_draft_section__node__patient_draft", "fn_draft_section__node__apply_instructions"],
            golden.Boxes.Select(b => b.Id));
        Assert.Equal(["param.standard"], golden.Boxes[0].Lines);
        Assert.Equal(
            [
                new DiagramEdge("fn_draft_section__param__standard", "fn_draft_section__node__standard_draft", EdgeStyle.Read, null),
                new DiagramEdge("fn_draft_section__param__standard", "fn_draft_section__node__standard_draft", EdgeStyle.If, "if"),
                new DiagramEdge("fn_draft_section__param__draft", "fn_draft_section__node__patient_draft", EdgeStyle.Read, null),
                new DiagramEdge("fn_draft_section__node__patient_draft", "fn_draft_section__node__apply_instructions", EdgeStyle.Read, "content"),
                new DiagramEdge("fn_draft_section__node__standard_draft", "fn_draft_section__node__apply_instructions", EdgeStyle.Read, "content"),
            ],
            golden.Edges);
    }

    [Fact]
    public void AParameterOfTheSameNameInTwoFunctions_GetsTwoPills()
    {
        // Two functions with a parameter `draft` and a node `draft`, and a top-level node `draft` too (Review Focus 2).
        JsonObject Drafting(string label) => Function(label, "plaintext", [("draft", "markdown_block")], "node.draft",
            ("node.draft", Agent("draft", label, "plaintext", "param.draft.content")));
        var model = DiagramModel.Build(WithFunctions(
            Document(
                ("node.draft", Agent("draft", "Drafting the note", "plaintext", "input.consult_draft.content")),
                ("node.short", Call("short", "Short form", "short_form", ("draft", "node.draft.content"))),
                ("node.long", Call("long", "Long form", "long_form", ("draft", "node.draft.content")))),
            ("long_form", Drafting("Drafting the long form")), ("short_form", Drafting("Drafting the short form"))));

        var pills = model.AllBoxes().Where(b => b.Lines is ["param.draft"]).ToList();
        Assert.Equal(["fn_long_form__param__draft", "fn_short_form__param__draft"], pills.Select(b => b.Id));
        Assert.Equal(2, pills.Select(b => b.Key).Distinct().Count());
        Assert.Contains(model.Root.Frames, f => f.Id == "fn_long_form" && f.Boxes.Any(b => b.Id == "fn_long_form__param__draft"));
        Assert.Contains(model.Root.Frames, f => f.Id == "fn_short_form" && f.Boxes.Any(b => b.Id == "fn_short_form__param__draft"));
        Assert.DoesNotContain(model.Root.Boxes, b => b.Lines is ["param.draft"]);

        var drafts = model.AllBoxes().Where(b => b.Lines[0] == "draft").ToList();
        Assert.Equal(["node__draft", "fn_long_form__node__draft", "fn_short_form__node__draft"], drafts.Select(b => b.Id));
        Assert.Equal(3, drafts.Select(b => b.Key).Distinct().Count());
        Assert.Contains(new DiagramEdge("node__draft", "node__short", EdgeStyle.Read, "content"), model.Edges);
        Assert.DoesNotContain(model.Edges, e => e.From == "node__draft" && e.To.StartsWith("fn_", StringComparison.Ordinal));
    }

    [Fact]
    public void AFunctionsLoopsAndUses_AreDrawnAsOutside()
    {
        // A loop over a parameter's list and a renderer use, inside a function: Task 2's and 3's rules, ids prefixed.
        var text = Leaf("rendered", "terms_text", "The terms as text", "markdown_block", "computed", "node.terms.value", "run.date");
        text["renders"] = new JsonArray(new JsonObject { ["n"] = 1, ["source"] = "node.terms.value", ["renderer"] = "concept_bullets" });
        var function = Function("Each section", "markdown_block", [("sections", "section_list")], "node.terms_text",
            ("node.each", For("each", "Each section", "param.sections.value", "node.each.point", "node.each.point")),
            ("node.each.point", Agent("point", "Drafting a point", "plaintext", "item.value").In("node.each")),
            ("node.terms", Agent("terms", "The terms", "concept_list", "node.each.content")),
            ("node.terms_text", text));
        var model = DiagramModel.Build(WithFunctions(
            Document(("node.sections", Call("sections", "The sections", "per_section", ("sections", "input.sections.value")))),
            ("per_section", function)));
        var lines = DiagramWriter.Write(model).Split('\n');

        var frame = Assert.Single(model.Root.Frames, f => f.Id == "fn_per_section");
        var loop = Assert.Single(frame.Frames, f => f.Id == "for_fn_per_section__node__each");
        Assert.Equal("each: per param.sections.value item", loop.Title);
        Assert.Equal(["fn_per_section__node__each__point"], loop.Boxes.Select(b => b.Id));
        var uses = Assert.Single(frame.Frames, f => f.Id == "uses_fn_per_section__node__terms_text");
        Assert.Equal(["fn_per_section__node__terms_text__render_1", "fn_per_section__node__terms_text"], uses.Boxes.Select(b => b.Id));
        Assert.Contains(new DiagramEdge("fn_per_section__node__terms_text__render_1", "fn_per_section__node__terms_text", EdgeStyle.Read, null), uses.Edges);

        Assert.Contains(new DiagramEdge("fn_per_section__param__sections", "for_fn_per_section__node__each", EdgeStyle.Read, "for"), frame.Edges);
        Assert.Contains(new DiagramEdge("fn_per_section__node__each__point", "fn_per_section__node__each", EdgeStyle.Read, "ret"), frame.Edges);
        Assert.Contains(new DiagramEdge("fn_per_section__node__each", "fn_per_section__node__terms", EdgeStyle.Read, "content"), frame.Edges);
        Assert.Contains(new DiagramEdge("fn_per_section__node__terms", "fn_per_section__node__terms_text__render_1", EdgeStyle.Read, "value"), frame.Edges);
        // A run fact read inside a function is the package's one pill, at the top level.
        Assert.Contains(model.Root.Boxes, b => b.Id == "run__date");
        Assert.Contains(new DiagramEdge("run__date", "fn_per_section__node__terms_text", EdgeStyle.Read, null), model.Edges);

        Assert.Contains("        subgraph for_fn_per_section__node__each[\"each: per param.sections.value item\"]", lines);
        Assert.Contains("        fn_per_section__node__each[\"each<br/>Each section<br/>item in param.sections.value\"]", lines);
        Assert.Contains("        subgraph uses_fn_per_section__node__terms_text[\"terms_text · The terms as text\"]", lines);
        Assert.Contains("        fn_per_section__param__sections -->|\"for\"| for_fn_per_section__node__each", lines);
    }
}
