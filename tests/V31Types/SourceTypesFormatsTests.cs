using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): <see cref="SourceTypes.Check"/> on a package's format files and
/// on every <c>format</c> member, each in the scope that owns it (part 02 §§ 2–6, D11, D13, D30).
/// </summary>
public class SourceTypesFormatsTests
{
    private const string Ghost = "Format 'ghost' is not a built-in, catalogue or package format.";

    private static IReadOnlyList<SourceRefusal> Refusals(Dictionary<string, byte[]> files)
    {
        var result = TypeFixture.CheckTypes(files);
        Assert.Equal(result.Refusals.Count == 0, result.Types != null);
        Assert.All(result.Refusals, V31Contract.AssertSentence);
        return result.Refusals;
    }

    private static SourceRefusal Only(Dictionary<string, byte[]> files)
    {
        var refusals = Refusals(files);
        Assert.True(refusals.Count == 1, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Sentence} ({r.Path})")));
        return refusals[0];
    }

    /// <summary>Sets the member at the dotted <paramref name="member"/> of the JSON at <paramref name="path"/>.</summary>
    private static Dictionary<string, byte[]> Set(Dictionary<string, byte[]> files, string path, string member, JsonNode? value) =>
        files.WithJson(path, json =>
        {
            var names = member.Split('.');
            JsonNode target = json;
            foreach (var name in names[..^1])
                target = target[name]!;
            target[names[^1]] = value;
        });

    [Fact]
    public void Minimal_And_Full_PassTheTypeRules()
    {
        foreach (var files in new[] { SourceFixture.Minimal(), SourceFixture.Full() })
        {
            var result = TypeFixture.CheckTypes(files);

            Assert.Empty(result.Refusals);
            Assert.NotNull(result.Types);
        }
    }

    [Fact]
    public void ThePackagesTypes_HoldEachScopesFormats()
    {
        var types = TypeFixture.CheckTypes(SourceFixture.Full()).Types!;

        Assert.Equal(FormatOrigin.Package, types.Formats.Find("verdict")!.Origin);
        Assert.Equal(FormatOrigin.Catalogue, types.Formats.Find("concept_list")!.Origin);
        var function = Assert.Single(types.FunctionFormats);
        Assert.Equal("draft_section", function.Key);
        Assert.Equal(FormatOrigin.Package, function.Value.Find("standard_section")!.Origin);
        Assert.Null(function.Value.Find("verdict"));
        Assert.Empty(types.NodeFormats);
        Assert.Empty(types.Bindings);
    }

    [Fact]
    public void Check_TakesNoNull()
    {
        var package = SourceFixture.Minimal().Parse(TypeFixture.Published().Names).Package!;

        Assert.Throws<ArgumentNullException>(() => SourceTypes.Check(null!, TypeFixture.Published()));
        Assert.Throws<ArgumentNullException>(() => SourceTypes.Check(package, null!));
    }

    // ---- Format files ------------------------------------------------------------------------------

    [Fact]
    public void APackageFormatFile_IsHeldToTheDialect()
    {
        var refusal = Only(SourceFixture.Full().WithJson("formats/verdict.json", f => f["properties"]!["verdict"]!["minLength"] = 1));

        Assert.Equal("S02-021", refusal.Id);
        Assert.Equal("Format 'verdict' uses keyword 'minLength' at /properties/verdict, which is outside the formats dialect.", refusal.Sentence);
        Assert.Equal("formats/verdict.json", refusal.Path);
    }

    [Fact]
    public void AFunctionsOwnFormatFile_IsHeldToTheDialect()
    {
        var files = SourceFixture.Full()
            .With("functions/draft_section/formats/index.json", SourceFixture.Index(["paragraph_plan"]))
            .With("functions/draft_section/formats/paragraph_plan.json",
                "{ \"type\": \"object\", \"properties\": {}, \"required\": [], \"additionalProperties\": false }\n");

        var refusal = Only(files);

        Assert.Equal("S02-022", refusal.Id);
        Assert.Equal(
            "Format 'paragraph_plan' declares no $schema; every format file names the dialect (urn:consultologist:spec:31:dialect).",
            refusal.Sentence);
        Assert.Equal("functions/draft_section/formats/paragraph_plan.json", refusal.Path);
    }

    [Fact]
    public void AnUnknownNameInAFormatFile_IsRefused()
    {
        var refusal = Only(SourceFixture.Full().WithJson("formats/outline.json", f => f["properties"]!["points"]!["items"]!["$ref"] = "ghost"));

        Assert.Equal("S02-017", refusal.Id);
        Assert.Equal(Ghost, refusal.Sentence);
        Assert.Equal("formats/outline.json", refusal.Path);
    }

    [Fact]
    public void ALoopThroughAPackageFormatAFunctionUses_IsSaidOnce()
    {
        var files = SourceFixture.Full().WithJson("formats/standard_section.json", f =>
        {
            f["properties"]!["parent"] = JsonNode.Parse("""{ "anyOf": [{ "$ref": "standard_section" }, { "type": "null" }] }""");
            f["required"]!.AsArray().Add("parent");
        });

        var refusal = Only(files);

        Assert.Equal("S02-032", refusal.Id);
        Assert.Equal(
            "Format 'standard_section' at /properties/parent/anyOf/0 refers to itself; recursive formats aren't supported.",
            refusal.Sentence);
        Assert.Equal("formats/standard_section.json", refusal.Path);
    }

    [Fact]
    public void AFunction_CannotNameAPackageFormatItDoesNotUse()
    {
        var refusal = Only(Set(SourceFixture.Full(), "functions/draft_section/standard_draft.md", "format", "outline"));

        Assert.Equal("S02-017", refusal.Id);
        Assert.Equal("Format 'outline' is not a built-in, catalogue or package format.", refusal.Sentence);
        Assert.Equal("functions/draft_section/standard_draft.md", refusal.Path);
    }

    [Fact]
    public void AFunction_NamesItsOwnFormats_AndTheBuiltIns()
    {
        var files = SourceFixture.Full()
            .With("functions/draft_section/formats/index.json", SourceFixture.Index(["paragraph_plan"]))
            .With("functions/draft_section/formats/paragraph_plan.json", TypeFixture.FormatFile(
                "\"type\": \"object\", \"properties\": { \"lines\": { \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } } }, "
                + "\"required\": [\"lines\"], \"additionalProperties\": false"));
        Set(files, "functions/draft_section/standard_draft.md", "format", "paragraph_plan");

        Assert.Empty(Refusals(files));
    }

    [Fact]
    public void AFunctionsUses_NamingNoFormat_IsRefused()
    {
        var files = Set(SourceFixture.Full(), "functions/draft_section/index.json", "uses.formats",
            new JsonArray("standard_section", "ghost", "plaintext"));

        var refusal = Only(files);

        Assert.Equal("S02-017", refusal.Id);
        Assert.Equal(Ghost, refusal.Sentence);
        Assert.Equal("functions/draft_section/index.json", refusal.Path);
    }

    [Fact]
    public void AFormatThatDoesNotExpand_IsRefused()
    {
        // f0 refers to f1 twice, f1 to f2 twice, and so on: f0's full form would be about 2^20 schemas.
        const int count = 20;
        var files = SourceFixture.Minimal()
            .With("formats/index.json", SourceFixture.Index(Enumerable.Range(0, count).Select(i => $"f{i}")));
        for (var i = 0; i < count; i++)
        {
            files.With($"formats/f{i}.json", TypeFixture.FormatFile(i == count - 1
                ? "\"type\": \"object\", \"properties\": {}, \"required\": [], \"additionalProperties\": false"
                : $"\"type\": \"object\", \"properties\": {{ \"l\": {{ \"$ref\": \"f{i + 1}\" }}, \"r\": {{ \"$ref\": \"f{i + 1}\" }} }}, "
                  + "\"required\": [\"l\", \"r\"], \"additionalProperties\": false"));
        }

        var result = TypeFixture.CheckTypes(files);

        var parsed = files.Parse(TypeFixture.Published().Names).Package!;
        var set = FormatSet.ForPackage(TypeFixture.Published(), parsed.Definitions.Formats.Items);
        var expected = parsed.Definitions.Formats.Items
            .Where(f => !FormatTypes.Expands(JsonValue.Create(f.Id), set))
            .Select(f => f.Path)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Contains("formats/f0.json", expected);
        Assert.DoesNotContain($"formats/f{count - 1}.json", expected);
        Assert.Equal(expected, result.Refusals.Select(r => r.Path));
        Assert.All(result.Refusals, r => Assert.Equal("A03-011", r.Id));
        Assert.All(result.Refusals, V31Contract.AssertSentence);
        Assert.Equal(
            "Format 'f0' expands to more than 10,000 schemas through the formats it refers to; refer to fewer.",
            result.Refusals.Single(r => r.Path == "formats/f0.json").Sentence);
        Assert.Null(result.Types);
    }

    [Fact]
    public void AFormatThatReachesARefusedFormat_IsNotAlsoSaidNotToExpand()
    {
        var files = SourceFixture.Full()
            .WithJson("formats/outline.json", f => f["properties"]!["points"]!["items"]!["$ref"] = "standard_section")
            .WithJson("formats/standard_section.json", f => f["properties"]!["title"]!["$ref"] = "ghost");

        var refusal = Only(files);

        Assert.Equal(Ghost, refusal.Sentence);
        Assert.Equal("formats/standard_section.json", refusal.Path);
    }

    // ---- Every format member -----------------------------------------------------------------------

    /// <summary>Every <c>format</c> member, by the file that holds it and the member's dotted name.</summary>
    public static TheoryData<string, string, string> Members => new()
    {
        { "manifest.json", "inputs.consult_draft.format", "manifest.json" },
        { "nodes/plan.md", "format", "nodes/plan.md" },
        { "nodes/section_count.md", "format", "nodes/section_count.md" },
        { "nodes/standards/section.md", "format", "nodes/standards/section.md" },
        { "nodes/billing_gate/billing_text.md", "format", "nodes/billing_gate/billing_text.md" },
        { "nodes/refine/index.json", "format", "nodes/refine/index.json" },
        { "checks/all_sections.md", "format", "checks/all_sections.md" },
        { "checks/section_coverage/index.json", "format", "checks/section_coverage/index.json" },
        { "functions/draft_section/index.json", "format", "functions/draft_section/index.json" },
        { "functions/draft_section/index.json", "inputs.draft.format", "functions/draft_section/index.json" },
        { "functions/draft_section/apply_instructions.md", "format", "functions/draft_section/apply_instructions.md" },
        { "renderers/term_bullets.md", "input", "renderers/term_bullets.md" },
        { "renderers/term_bullets.md", "format", "renderers/term_bullets.md" },
        { "snippets/closing_line.md", "format", "snippets/closing_line.md" },
        { "snippets/standards/index.json", "format", "snippets/standards/index.json" },
        { "macros/follow_up.md", "format", "macros/follow_up.md" },
        { "macros/phrases/thanks.md", "format", "macros/phrases/thanks.md" },
        { "deliverables/note.md", "format", "deliverables/note.md" },
        { "deliverables/emr/consult.md", "format", "deliverables/emr/consult.md" },
    };

    [Theory]
    [MemberData(nameof(Members))]
    public void EveryFormatMember_IsLookedUp(string file, string member, string path)
    {
        var refusal = Only(Set(SourceFixture.Full(), file, member, "ghost"));

        Assert.Equal("S02-017", refusal.Id);
        Assert.Equal(Ghost, refusal.Sentence);
        Assert.Equal(path, refusal.Path);
    }

    /// <summary>Each owner of an inline schema (a member the reader lets be an object), by the file, the member, and the owner D11 names.</summary>
    public static TheoryData<string, string, string> Owners => new()
    {
        { "manifest.json", "inputs.consult_draft.format", "Input 'consult_draft'" },
        { "nodes/section_count.md", "format", "Node 'section_count'" },
        { "nodes/standards/section.md", "format", "Node 'section'" },
        { "nodes/sections/index.json", "format", "Loop 'sections'" },
        { "nodes/refine/index.json", "format", "While 'refine'" },
        { "nodes/billing_gate/index.json", "format", "If 'billing_gate'" },
        { "nodes/sections_draft/route/index.json", "format", "Case 'route'" },
        { "checks/section_coverage/index.json", "format", "Loop 'section_coverage'" },
        { "functions/draft_section/index.json", "format", "Function 'draft_section'" },
        { "functions/draft_section/index.json", "inputs.draft.format", "Function 'draft_section' parameter 'draft'" },
        { "renderers/term_bullets.md", "input", "Renderer 'term_bullets'" },
        { "snippets/standards/index.json", "format", "Snippet folder 'standards'" },
    };

    [Theory]
    [MemberData(nameof(Owners))]
    public void AnInlineSchema_IsHeldToTheDialect_UnderItsOwnersName(string file, string member, string owner)
    {
        var refusal = Only(Set(SourceFixture.Full(), file, member, JsonNode.Parse("""{ "type": "string", "minLength": 1 }""")));

        Assert.Equal("S02-021", refusal.Id);
        Assert.Equal($"{owner} uses keyword 'minLength' at the root, which is outside the formats dialect.", refusal.Sentence);
        Assert.Equal(file, refusal.Path);
    }

    [Theory]
    [MemberData(nameof(Owners))]
    public void AnInlineSchema_IsLookedUpInItsOwnersScope(string file, string member, string owner)
    {
        Assert.NotEmpty(owner);
        var refusal = Only(Set(SourceFixture.Full(), file, member, JsonNode.Parse("""{ "type": "array", "items": { "$ref": "ghost" } }""")));

        Assert.Equal("S02-017", refusal.Id);
        Assert.Equal(Ghost, refusal.Sentence);
        Assert.Equal(file, refusal.Path);
    }

    [Fact]
    public void ACallInChecks_IsNamedACheck()
    {
        // A check that is a call: its inline schema is named by the word of checks/ (D11).
        var files = SourceFixture.Full()
            .With("checks/index.json", SourceFixture.Index(["section_coverage", "all_sections", "redraft"]))
            .With("checks/redraft.md", SourceFixture.Md(
                SourceFixture.Node("call", "Redrafting a section", """{ "type": "string", "minLength": 1 }""", false, true,
                    "\"if\": null, \"function\": \"draft_section\", "
                    + "\"with\": { \"standard\": \"node.standards.items\", \"draft\": \"input.consult_draft.content\" }"),
                ""));

        var refusal = Assert.Single(Refusals(files), r => r.Path == "checks/redraft.md");

        Assert.Equal("Check 'redraft' uses keyword 'minLength' at the root, which is outside the formats dialect.", refusal.Sentence);
    }

    [Fact]
    public void IndependentTypeFaults_AreAllReported_InPathOrder()
    {
        var files = SourceFixture.Full()
            .WithJson("renderers/term_bullets.md", h => h["input"] = "ghost")
            .WithJson("nodes/plan.md", h => h["format"] = "ghost")
            .WithJson("manifest.json", m => m["inputs"]!["consult_draft"]!["channel"] = "audio")
            .WithJson("functions/draft_section/index.json", f => f["inputs"]!["draft"]!["format"] = JsonNode.Parse("""{ "type": "string", "pattern": "x" }"""))
            .WithJson("formats/verdict.json", f => f["properties"]!["verdict"]!["minLength"] = 1);

        var result = TypeFixture.CheckTypes(files);

        Assert.Null(result.Types);
        Assert.Equal(
            [
                ("S02-021", "formats/verdict.json"),
                ("S02-021", "functions/draft_section/index.json"),
                ("S02-042", "manifest.json"),
                ("S02-017", "nodes/plan.md"),
                ("S02-017", "renderers/term_bullets.md"),
            ],
            result.Refusals.Select(r => (r.Id, r.Path)));
        Assert.All(result.Refusals, V31Contract.AssertSentence);
    }
}
