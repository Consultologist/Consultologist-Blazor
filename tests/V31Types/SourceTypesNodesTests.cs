using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="SourceTypes.Check"/> on the leaves: an agent's
/// binding (S02-012, D21), its union root (S02-027) and the strict-mode limits (S02-055, S02-056,
/// D3); a rendered node's text (S02-013, D15); a leaf's <c>reproducible</c> (S01-027, D5).
/// </summary>
public class SourceTypesNodesTests
{
    private const string Dialect = "urn:consultologist:spec:31:dialect";

    private static IReadOnlyList<SourceRefusal> Refusals(TypeCheckResult result)
    {
        Assert.Equal(result.Refusals.Count == 0, result.Types != null);
        Assert.All(result.Refusals, V31Contract.AssertSentence);
        return result.Refusals;
    }

    private static IReadOnlyList<SourceRefusal> Refusals(Dictionary<string, byte[]> files) => Refusals(TypeFixture.CheckTypes(files));

    private static SourceRefusal Only(IReadOnlyList<SourceRefusal> refusals)
    {
        Assert.True(refusals.Count == 1, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Sentence} ({r.Path})")));
        return refusals[0];
    }

    private static SourceRefusal Only(Dictionary<string, byte[]> files) => Only(Refusals(files));

    // ---- Agents -----------------------------------------------------------------------------------

    [Fact]
    public void AnAgent_NeedsABinding()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["format"] = "check_result"));

        Assert.Equal("S02-012", refusal.Id);
        Assert.Equal(
            "Node 'plan' is kind agent and returns check_result, which has no binding for producer generated in agents/bindings.json.",
            refusal.Sentence);
        Assert.Equal("nodes/plan.md", refusal.Path);
    }

    [Fact]
    public void AnAgent_InAFunction_NeedsABinding_InItsScope()
    {
        var refusal = Only(SourceFixture.Full()
            .WithJson("functions/draft_section/standard_draft.md", h => h["format"] = "date"));

        Assert.Equal("S02-012", refusal.Id);
        Assert.Equal(
            "Node 'standard_draft' is kind agent and returns date, which has no binding for producer generated in agents/bindings.json.",
            refusal.Sentence);
        Assert.Equal("functions/draft_section/standard_draft.md", refusal.Path);
    }

    [Fact]
    public void AnAgent_WithAPackageFormat_IsBoundThroughCustom()
    {
        var files = SourceFixture.Full()
            .WithJson("functions/draft_section/standard_draft.md", h => h["format"] = "standard_section");
        var types = TypeFixture.CheckTypes(files).Types!;

        Assert.Equal("custom", types.Bindings["nodes/plan.md"].Via);
        Assert.Equal("custom-json", types.Bindings["nodes/plan.md"].AgentName);
        Assert.Equal(1, types.Bindings["nodes/plan.md"].AgentVersion);
        Assert.Equal("custom", types.Bindings["functions/draft_section/standard_draft.md"].Via);
        Assert.Equal("format", types.Bindings["nodes/extract_input_terms.md"].Via);
        Assert.Equal("concept-extraction", types.Bindings["nodes/extract_input_terms.md"].AgentName);
        Assert.Equal("format", types.Bindings["nodes/sections/paragraphs/draft_paragraph.md"].Via);
        Assert.Equal("test-json", types.Bindings["nodes/sections/paragraphs/draft_paragraph.md"].AgentName);
        Assert.Equal(["format"], types.Bindings["nodes/plan.md"].StrictRejects);

        // Agents only: a rendered, computed or call node, and a flow node, has no binding.
        Assert.False(types.Bindings.ContainsKey("nodes/billing_gate/billing_text.md"));
        Assert.False(types.Bindings.ContainsKey("nodes/section_count.md"));
        Assert.False(types.Bindings.ContainsKey("nodes/standards/section.md"));
        Assert.False(types.Bindings.ContainsKey("nodes/refine"));
        Assert.Equal(12, types.Bindings.Count);
    }

    [Fact]
    public void EveryNode_HasItsDeclaredFormat_AsWritten()
    {
        var types = TypeFixture.CheckTypes(SourceFixture.Full()).Types!;

        Assert.Equal("section_plan", types.NodeFormats["nodes/plan.md"].GetValue<string>());
        Assert.Equal("plaintext", types.NodeFormats["nodes/refine"].GetValue<string>());
        Assert.Equal(
            """{"type":"array","items":{"type":"array","items":{"$ref":"plaintext"}}}""",
            types.NodeFormats["nodes/sections"].ToJsonString());
        Assert.Equal("check_result", types.NodeFormats["checks/all_sections.md"].GetValue<string>());
        Assert.Equal("plaintext", types.NodeFormats["functions/draft_section/apply_instructions.md"].GetValue<string>());

        // Every node and flow node of nodes/, checks/ and the function's body; nothing else.
        Assert.Equal(25, types.NodeFormats.Count);
    }

    [Fact]
    public void AnAgentsUnionRoot_IsRefused()
    {
        // A package format can't be an anyOf root (A03-001), so the registry is built for the test:
        // a catalogue format whose root is an anyOf, bound for generated.
        var published = TypeFixture.Published();
        var catalogue = published.CatalogueFormats.ToDictionary(p => p.Key, p => p.Value);
        catalogue["either"] = TypeFixture.Schema(
            "{ \"$schema\": \"" + Dialect + "\", \"anyOf\": [ { \"$ref\": \"plaintext\" }, { \"$ref\": \"number\" } ] }");
        var bindings = published.Bindings.DeepClone().AsObject();
        bindings["formats"]!["either"] = new JsonObject
        {
            ["generated"] = new JsonObject { ["agentName"] = "test-json", ["agentVersion"] = 48 },
        };
        var registry = new TypeRegistry(published.BuiltInFormats, catalogue, bindings, published.Agents, published.BuiltInRenderers);

        var parsed = SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["format"] = "either").Parse(registry.Names);
        Assert.NotNull(parsed.Package);
        var refusal = Only(Refusals(SourceTypes.Check(parsed.Package!, registry)));

        Assert.Equal("S02-027", refusal.Id);
        Assert.Equal(
            "Node 'plan' returns either, an anyOf at its root, which strict mode can't send to a model; wrap it in an object.",
            refusal.Sentence);
        Assert.Equal("nodes/plan.md", refusal.Path);
    }

    [Fact]
    public void AFormatOverAStrictLimit_IsRefusedOnce_HoweverManyAgentsReturnIt()
    {
        var properties = string.Join(", ", Enumerable.Range(1, 101).Select(i => $"\"p{i}\": {{ \"$ref\": \"plaintext\" }}"));
        var required = string.Join(", ", Enumerable.Range(1, 101).Select(i => $"\"p{i}\""));
        string Nest(int levels) => levels == 0
            ? "{ \"$ref\": \"plaintext\" }"
            : "{ \"type\": \"object\", \"required\": [\"a\"], \"additionalProperties\": false, \"properties\": { \"a\": " + Nest(levels - 1) + " } }";

        var files = SourceFixture.Minimal()
            .With("formats/index.json", SourceFixture.Index(["big", "deep"]))
            .With("formats/big.json", TypeFixture.FormatFile(
                $"\"type\": \"object\", \"required\": [{required}], \"additionalProperties\": false, \"properties\": {{ {properties} }}"))
            .With("formats/deep.json", TypeFixture.FormatFile(Nest(6).Trim()[1..^1]))
            .With("nodes/index.json", SourceFixture.Index(["second", "plan", "third", "fourth"]))
            .With("nodes/second.md", SourceFixture.Agent("Second", "\"big\"", "Write it.\n"))
            .WithJson("nodes/plan.md", h => h["format"] = "big")
            .With("nodes/third.md", SourceFixture.Agent("Third", "\"deep\"", "Write it.\n"))
            .With("nodes/fourth.md", SourceFixture.Agent("Fourth", "\"deep\"", "Write it.\n"));

        var refusals = Refusals(files);

        Assert.Equal(2, refusals.Count);
        Assert.Equal("S02-055", refusals[0].Id);
        Assert.Equal("Format 'big' declares 101 properties; a format sent to a model declares at most 100.", refusals[0].Sentence);
        Assert.Equal("nodes/second.md", refusals[0].Path);
        Assert.Equal("S02-056", refusals[1].Id);
        Assert.Equal(
            "Format 'deep' nests 6 levels deep at /properties/a/properties/a/properties/a/properties/a/properties/a; a format sent to a model nests at most 5.",
            refusals[1].Sentence);
        Assert.Equal("nodes/third.md", refusals[1].Path);
    }

    [Fact]
    public void AFormatAtTheStrictLimits_IsAccepted()
    {
        var properties = string.Join(", ", Enumerable.Range(1, 100).Select(i => $"\"p{i}\": {{ \"$ref\": \"plaintext\" }}"));
        var required = string.Join(", ", Enumerable.Range(1, 100).Select(i => $"\"p{i}\""));
        var files = SourceFixture.Minimal()
            .With("formats/index.json", SourceFixture.Index(["big"]))
            .With("formats/big.json", TypeFixture.FormatFile(
                $"\"type\": \"object\", \"required\": [{required}], \"additionalProperties\": false, \"properties\": {{ {properties} }}"))
            .WithJson("nodes/plan.md", h => h["format"] = "big");

        Assert.Empty(Refusals(files));
    }

    // ---- Rendered nodes (D15) ---------------------------------------------------------------------

    [Fact]
    public void ARenderedNode_ReturnsText()
    {
        var refusal = Only(SourceFixture.Full().WithJson("nodes/billing_gate/billing_text.md", h => h["format"] = "number"));

        Assert.Equal("S02-013", refusal.Id);
        Assert.Equal(
            "Node 'billing_text' is kind rendered and returns number; a rendered node returns plaintext, markdown_block or markdown_inline.",
            refusal.Sentence);
        Assert.Equal("nodes/billing_gate/billing_text.md", refusal.Path);
    }

    [Theory]
    [InlineData("markdown_block")]
    [InlineData("markdown_inline")]
    [InlineData("plaintext")]
    public void ARenderedNode_MayReturnAnyTextFormat(string format)
    {
        var files = SourceFixture.Full()
            .WithJson("nodes/billing_gate/index.json", h => h["format"] = format)
            .WithJson("nodes/billing_gate/billing_text.md", h => h["format"] = format);

        Assert.Empty(Refusals(files));
    }

    [Fact]
    public void ARenderedNodesInlineFormat_IsReadThenRefusedAsNotText()
    {
        var files = SourceFixture.Full().WithJson("nodes/billing_gate/billing_text.md", h => h["format"] = TypeFixture.Schema(
            "{ \"type\": \"object\", \"required\": [\"line\"], \"additionalProperties\": false, "
            + "\"properties\": { \"line\": { \"$ref\": \"plaintext\" } } }"));

        var refusal = Only(files);

        Assert.Equal("S02-013", refusal.Id);
        Assert.Equal(
            "Node 'billing_text' is kind rendered and returns { line: plaintext }; a rendered node returns plaintext, markdown_block or markdown_inline.",
            refusal.Sentence);
    }

    [Fact]
    public void ARenderedNodesInlineFormat_IsHeldToTheDialect_First()
    {
        var files = SourceFixture.Full().WithJson("nodes/billing_gate/billing_text.md", h => h["format"] = new JsonObject());

        var refusal = Only(files);

        Assert.Equal("A03-005", refusal.Id);
        Assert.Equal("Node 'billing_text' at the root declares no type; write type, $ref or anyOf.", refusal.Sentence);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("[]")]
    [InlineData("true")]
    public void ARenderedNodesFormat_OfAnotherJsonType_IsStillTheReaders(string value)
    {
        var parsed = SourceFixture.Full()
            .WithJson("nodes/billing_gate/billing_text.md", h => h["format"] = JsonNode.Parse(value))
            .Parse(TypeFixture.Published().Names);

        var refusal = Assert.Single(parsed.Refusals);
        Assert.Equal("A02-001", refusal.Id);
    }

    [Fact]
    public void ARenderedChecksInlineFormat_IsStillTheReaders()
    {
        var parsed = SourceFixture.Full()
            .With("checks/index.json", SourceFixture.Index(["section_coverage", "all_sections", "summary"]))
            .With("checks/summary.md", SourceFixture.Md(
                SourceFixture.Node("rendered", "Summary", "{ \"type\": \"object\" }", true, false, "\"if\": null"),
                "All sections were checked.\n"))
            .Parse(TypeFixture.Published().Names);

        var refusal = Assert.Single(parsed.Refusals);
        Assert.Equal("A02-001", refusal.Id);
    }

    [Fact]
    public void ARenderedNode_InAFunction_ReturnsText()
    {
        var files = SourceFixture.Full().With("functions/draft_section/standard_draft.md", SourceFixture.Md(
            SourceFixture.Node("rendered", "Drafting from the standard", "\"number\"", true, false, "\"if\": null"),
            "{{ param.standard.content }}\n"));

        var refusal = Only(files);

        Assert.Equal("S02-013", refusal.Id);
        Assert.Equal(
            "Node 'standard_draft' is kind rendered and returns number; a rendered node returns plaintext, markdown_block or markdown_inline.",
            refusal.Sentence);
        Assert.Equal("functions/draft_section/standard_draft.md", refusal.Path);
    }

    // ---- reproducible (D5) ------------------------------------------------------------------------

    [Fact]
    public void ALeafsReproducible_IsItsKinds()
    {
        var files = SourceFixture.Full()
            .WithJson("nodes/plan.md", h => h["reproducible"] = true)
            .WithJson("nodes/billing_gate/billing_text.md", h => h["reproducible"] = false)
            .WithJson("nodes/section_count.md", h => h["reproducible"] = false)
            .WithJson("checks/all_sections.md", h => h["reproducible"] = false)
            .WithJson("functions/draft_section/standard_draft.md", h => h["reproducible"] = true)
            // A call's reproducible is area 05's, a flow node's area 04's: neither is judged here.
            .WithJson("nodes/standards/section.md", h => h["reproducible"] = true)
            .WithJson("nodes/refine/index.json", h => h["reproducible"] = true);

        var refusals = Refusals(files);

        Assert.Equal(
            [
                ("checks/all_sections.md", "Check 'check.all_sections' declares reproducible false, but its kind makes it true."),
                ("functions/draft_section/standard_draft.md", "Node 'node.standard_draft' declares reproducible true, but its kind makes it false."),
                ("nodes/billing_gate/billing_text.md", "Node 'node.billing_gate.billing_text' declares reproducible false, but its kind makes it true."),
                ("nodes/plan.md", "Node 'node.plan' declares reproducible true, but its kind makes it false."),
                ("nodes/section_count.md", "Node 'node.section_count' declares reproducible false, but its kind makes it true."),
            ],
            refusals.Select(r => (r.Path, r.Sentence)).ToList());
        Assert.All(refusals, r => Assert.Equal("S01-027", r.Id));
    }
}
