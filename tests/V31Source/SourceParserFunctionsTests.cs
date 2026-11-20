using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): <c>functions/&lt;id&gt;/</c> read into the model — its index,
/// parameters, body and own definitions — and the shape rules of part 04 § 7 that need nothing
/// but the function itself (D16).
/// </summary>
public class SourceParserFunctionsTests
{
    private const string Index = "functions/draft_section/index.json";

    private static SourceRefusal Only(SourceParseResult result)
    {
        var refusal = Assert.Single(result.Refusals);
        Assert.Null(result.Package);
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private static Dictionary<string, byte[]> Edit(Action<JsonObject> edit) =>
        SourceFixture.Full().WithJson(Index, edit);

    [Fact]
    public void Full_ParsesItsFunction()
    {
        var result = SourceFixture.Full().Parse();

        Assert.Empty(result.Refusals);
        var functions = result.Package!.Functions;
        Assert.Equal("functions", functions.Path);
        var function = Assert.Single(functions.Items);
        Assert.Equal(("draft_section", "functions/draft_section"), (function.Id, function.Path));
        Assert.Equal("Draft a section", (string?)function.Index["label"]);
        Assert.Equal(["standard", "draft"], function.Inputs.Select(p => p.Id));
        Assert.Equal("standard_section", (string?)function.Inputs[0].Json["format"]);
        Assert.Equal("node.apply_instructions", function.Body.Ret);
        Assert.Equal(["standard_draft", "patient_draft", "apply_instructions"], function.Body.Nodes.Select(n => n.Id));
        var inner = Assert.IsType<SourceLeaf>(function.Body.Nodes[0]);
        Assert.Equal(("node.standard_draft", "functions/draft_section/standard_draft.md"), (inner.Key, inner.Path));

        var definitions = function.Definitions;
        Assert.Equal("functions/draft_section/snippets", definitions.Snippets.Path);
        Assert.Equal("functions/draft_section/macros", definitions.Macros.Path);
        Assert.Equal("functions/draft_section/formats", definitions.Formats.Path);
        Assert.Equal("functions/draft_section/renderers", definitions.Renderers.Path);
        Assert.Empty(definitions.Snippets.Items);
    }

    [Fact]
    public void AFunctionsOwnDefinitions_AreRead()
    {
        var files = SourceFixture.Full()
            .With("functions/draft_section/snippets/tone.md",
                "---\n{ \"label\": \"Tone\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }\n---\nWrite plainly for Dr A. Sample.\n");
        files.WithJson("functions/draft_section/snippets/index.json", json => json["items"] = new JsonArray("tone"));

        var result = files.Parse();

        Assert.Empty(result.Refusals);
        var snippet = Assert.IsType<SourceSingleText>(Assert.Single(result.Package!.Functions.Items[0].Definitions.Snippets.Items));
        Assert.Equal(("tone", "functions/draft_section/snippets/tone.md"), (snippet.Id, snippet.Text.Path));
        Assert.DoesNotContain(result.Package.Definitions.Snippets.Items, s => s.Id == "tone");
    }

    [Fact]
    public void AFunctionInANestedFlowNode_KeysItsNodesFromTheFunctionsRoot()
    {
        var files = SourceFixture.Full()
            .With("functions/draft_section/loop/index.json", SourceFixture.Text(SourceFixture.Full(), "nodes/standards/index.json")
                .Replace("\"nodes\": [\"section\"]", "\"nodes\": [\"child\"]")
                .Replace("node.standards.section", "node.loop.child"))
            .With("functions/draft_section/loop/child.md", SourceFixture.Text(SourceFixture.Full(), "functions/draft_section/patient_draft.md"));
        files.WithJson(Index, json => ((JsonArray)json["nodes"]!).Add("loop"));

        var result = files.Parse();

        Assert.Empty(result.Refusals);
        var loop = Assert.IsType<SourceFor>(result.Package!.Functions.Items[0].Body.Nodes[3]);
        Assert.Equal("node.loop", loop.Key);
        Assert.Equal("node.loop.child", Assert.Single(loop.Body.Nodes).Key);
    }

    [Theory]
    [InlineData("checks")]
    [InlineData("deliverables")]
    public void AFunctionHoldingChecks_IsRefused(string folder)
    {
        var files = SourceFixture.Full()
            .With($"functions/draft_section/{folder}/index.json", "{ \"description\": null, \"items\": [\"x\"] }")
            .With($"functions/draft_section/{folder}/x.md", "x")
            .With($"functions/draft_section/{folder}/inner/y.md", "y");

        var refusal = Only(files.Parse());

        Assert.Equal("S04-062", refusal.Id);
        Assert.Equal(
            $"Function 'draft_section' holds a {folder} folder; checks and deliverables live only at the package's top level: return a check_result or a value instead.",
            refusal.Sentence);
        Assert.Equal($"functions/draft_section/{folder}", refusal.Path);
    }

    [Theory]
    [InlineData("required", true)]
    [InlineData("channel", null)]
    public void AParameterDeclaringRequired_IsRefused(string member, bool? value)
    {
        var refusal = Only(Edit(json => json["inputs"]!["standard"]![member] = value).Parse());

        Assert.Equal("S04-067", refusal.Id);
        Assert.Equal(
            $"Function 'draft_section' parameter 'standard' declares {member}; a parameter has label, description and format, and every one is passed.",
            refusal.Sentence);
        Assert.Equal(Index, refusal.Path);
    }

    [Fact]
    public void AParameter_IsHeldToItsShape()
    {
        var refusal = Only(Edit(json => json["inputs"]!["standard"]!.AsObject().Remove("format")).Parse());
        Assert.Equal(
            "Function 'draft_section' parameter 'standard' is missing 'format'; every member is written, and null means none.",
            refusal.Sentence);

        var text = Only(Edit(json => json["inputs"]!["standard"] = "standard_section").Parse());
        Assert.Equal("Function 'draft_section' declares 'inputs.standard' as text; it is an object.", text.Sentence);

        var unknown = Only(Edit(json => json["inputs"]!["standard"]!["formatt"] = "x").Parse());
        Assert.Equal("S01-023", unknown.Id);
    }

    [Fact]
    public void AParameterId_IsHeldToTheIdRules()
    {
        var files = Edit(json =>
        {
            var inputs = json["inputs"]!.AsObject();
            var standard = inputs["standard"]!.DeepClone();
            inputs.Remove("standard");
            inputs["Standard"] = standard;
        });

        var refusal = Only(files.Parse());

        Assert.Equal("S01-030", refusal.Id);
        Assert.Contains("'Standard'", refusal.Sentence);
        Assert.Equal(Index, refusal.Path);

        var reserved = Only(Edit(json => json["inputs"]!["items"] = json["inputs"]!["draft"]!.DeepClone()).Parse());
        Assert.Equal("S01-033", reserved.Id);
    }

    [Fact]
    public void NoParameters_IsAnEmptyObject()
    {
        var result = Edit(json => json["inputs"] = new JsonObject()).Parse();

        Assert.Empty(result.Refusals);
        Assert.Empty(result.Package!.Functions.Items[0].Inputs);
    }

    [Fact]
    public void AFunctionMissingUsesRenderers_IsRefused()
    {
        var refusal = Only(Edit(json => json["uses"]!.AsObject().Remove("renderers")).Parse());

        Assert.Equal("S01-021", refusal.Id);
        Assert.Equal(
            "Function 'draft_section' uses is missing 'renderers'; every member is written, and null means none.",
            refusal.Sentence);

        var list = Only(Edit(json => json["uses"]!["formats"] = new JsonArray(1)).Parse());
        Assert.Equal("Function 'draft_section' uses declares 'formats' as a list; it is a list of text.", list.Sentence);
    }

    [Fact]
    public void AFunctionIndex_IsHeldToItsShape()
    {
        Assert.Equal(
            "Function 'draft_section' is missing 'uses'; every member is written, and null means none.",
            Only(Edit(json => json.Remove("uses")).Parse()).Sentence);
        Assert.Equal("S01-022", Only(Edit(json => json["label"] = null).Parse()).Id);
        Assert.StartsWith(
            "Function 'draft_section' in 'functions/draft_section/index.json' declares unknown member 'kind'.",
            Only(Edit(json => json["kind"] = "for").Parse()).Sentence);
    }

    [Fact]
    public void AFunctionWithANullRet_ReturnsNothing()
    {
        var refusal = Only(Edit(json => json["ret"] = null).Parse());

        Assert.Equal("S04-011", refusal.Id);
        Assert.Equal("Function 'draft_section' returns nothing; name the body node the outside reads with ret.", refusal.Sentence);
    }

    [Fact]
    public void AFunctionWithAnEmptyBody_HasNoBody()
    {
        var files = Edit(json => json["nodes"] = new JsonArray())
            .Without("functions/draft_section/standard_draft.md")
            .Without("functions/draft_section/patient_draft.md")
            .Without("functions/draft_section/apply_instructions.md");

        var refusal = Only(files.Parse());

        Assert.Equal("A02-008", refusal.Id);
        Assert.Equal("Function 'draft_section' has no body; name the nodes that run in nodes.", refusal.Sentence);
    }

    [Fact]
    public void AFunctionListingANodeTwice_IsRefused()
    {
        var refusal = Only(Edit(json => ((JsonArray)json["nodes"]!).Add("patient_draft")).Parse());

        Assert.Equal("S04-013", refusal.Id);
        Assert.Equal("Function 'draft_section' lists node 'patient_draft' twice; a body is a set.", refusal.Sentence);
    }

    [Fact]
    public void AFunctionAndANode_MayShareAnId()
    {
        var full = SourceFixture.Full();
        var files = full.ToDictionary(
            f => f.Key.Replace("functions/draft_section/", "functions/plan/"),
            f => f.Value);
        files.WithJson("functions/index.json", json => json["items"] = new JsonArray("plan"));
        files.WithJson("nodes/standards/section.md", h => h["function"] = "plan");

        var result = files.Parse();

        Assert.Empty(result.Refusals);
        Assert.Equal("plan", Assert.Single(result.Package!.Functions.Items).Id);
        Assert.Equal("plan", result.Package.Nodes.Nodes[0].Id);
    }

    [Fact]
    public void Functions_AreInListedOrder()
    {
        var full = SourceFixture.Full();
        var files = new Dictionary<string, byte[]>(full);
        foreach (var (path, bytes) in full.Where(f => f.Key.StartsWith("functions/draft_section/", StringComparison.Ordinal)))
            files[path.Replace("functions/draft_section/", "functions/another/")] = bytes;
        files.WithJson("functions/index.json", json => json["items"] = new JsonArray("another", "draft_section"));

        var result = files.Parse();

        Assert.Empty(result.Refusals);
        Assert.Equal(["another", "draft_section"], result.Package!.Functions.Items.Select(f => f.Id));
    }

    [Fact]
    public void WrongTypesInAFunctionIndex_AreRefused_AndNeverThrow()
    {
        JsonNode?[] values = [null, 1, 1.5, "x", true, new JsonObject(), new JsonArray(1, null, new JsonObject())];
        string[] members = ["label", "description", "inputs", "uses", "nodes", "ret", "format", "kind"];
        foreach (var member in members)
        {
            foreach (var value in values)
            {
                var result = Edit(json => json[member] = value?.DeepClone()).Parse();
                Assert.Equal(result.Refusals.Count == 0, result.Package != null);
                // A text ret is valid only when it names a child (S04-012), which no sweep value does.
                var valid = (member, value) switch
                {
                    ("description", null) => true,
                    ("label" or "format", JsonValue v) => v.GetValueKind() == System.Text.Json.JsonValueKind.String,
                    ("format" or "inputs", JsonObject) => true,
                    _ => false,
                };
                Assert.True(valid == (result.Refusals.Count == 0), $"{member} = {value?.ToJsonString() ?? "null"}");
            }
        }

        foreach (var value in values)
        {
            foreach (var member in new[] { "label", "description", "format", "required" })
            {
                var result = Edit(json => json["inputs"]!["standard"]![member] = value?.DeepClone()).Parse();
                Assert.Equal(result.Refusals.Count == 0, result.Package != null);
            }

            foreach (var member in new[] { "snippets", "macros", "formats", "renderers", "extra" })
            {
                var result = Edit(json => json["uses"]![member] = value?.DeepClone()).Parse();
                Assert.NotEmpty(result.Refusals);
            }
        }
    }

    [Fact]
    public void EveryFunctionSentence_IsTheDesigns()
    {
        var sentences = new[]
        {
            SourceSentences.FunctionHoldsTopLevelFolder("draft_section", "checks", "p"),
            SourceSentences.FunctionHoldsTopLevelFolder("draft_section", "deliverables", "p"),
            SourceSentences.ParameterDeclaresInputMember("draft_section", "standard", "required", "p"),
            SourceSentences.ParameterDeclaresInputMember("draft_section", "standard", "channel", "p"),
            SourceSentences.NoBody("Function 'draft_section'", "p"),
            SourceSentences.ReturnsNothing("Function 'draft_section'", "p"),
        };

        Assert.Equal(["S04-062", "S04-062", "S04-067", "S04-067", "A02-008", "S04-011"], sentences.Select(s => s.Id));
        Assert.All(sentences, V31Contract.AssertSentence);
    }
}
