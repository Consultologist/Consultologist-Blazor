using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): the five places where area 02's reader says part 02's own
/// sentence in place of its generic one (D13–D16): <c>kind: "let"</c> (S02-008), a reserved
/// producer (S02-010), an agent's inline format (S02-014), an input's <c>default</c> (S02-046) and
/// a missing file of the package's <c>formats/</c> (S02-039). Each test proves the new sentence and
/// that the old one is no longer said.
/// </summary>
public class SourceReaderAmendmentsTests
{
    private static SourceRefusal Only(SourceParseResult result)
    {
        Assert.True(result.Refusals.Count == 1, string.Join(" | ", result.Refusals.Select(r => $"{r.Id} {r.Sentence}")));
        V31Contract.AssertSentence(result.Refusals[0]);
        return result.Refusals[0];
    }

    [Fact]
    public void KindLet_IsPart02sOwnSentence()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["kind"] = "let").Parse());

        Assert.Equal("S02-008", refusal.Id);
        Assert.Equal("Node 'plan' declares kind let; a let is now kind computed, with its JSON Logic in expression.", refusal.Sentence);
        Assert.Equal("nodes/plan.md", refusal.Path);
    }

    [Fact]
    public void KindLet_InAFlowFolder_IsPart02sOwnSentence()
    {
        var refusal = Only(SourceFixture.Full().WithJson("nodes/refine/index.json", h => h["kind"] = "let").Parse());

        Assert.Equal("S02-008", refusal.Id);
        Assert.Equal("Node 'refine' declares kind let; a let is now kind computed, with its JSON Logic in expression.", refusal.Sentence);
        Assert.Equal("nodes/refine/index.json", refusal.Path);
    }

    [Fact]
    public void AnotherUnknownKind_IsStillS02_007()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["kind"] = "lets").Parse());

        Assert.Equal("S02-007", refusal.Id);
    }

    [Theory]
    [InlineData("nodes/plan.md", "grounded", "plan")]
    [InlineData("nodes/section_count.md", "extracted", "section_count")]
    [InlineData("nodes/billing_gate/billing_text.md", "attention", "billing_text")]
    [InlineData("nodes/standards/section.md", "grounded", "section")]
    [InlineData("checks/all_sections.md", "grounded", "all_sections")]
    [InlineData("functions/draft_section/standard_draft.md", "extracted", "standard_draft")]
    [InlineData("nodes/refine/index.json", "grounded", "refine")]
    [InlineData("nodes/sections_draft/route/index.json", "attention", "route")]
    [InlineData("checks/section_coverage/index.json", "extracted", "section_coverage")]
    public void AReservedProducer_IsPart02sOwnSentence(string path, string producer, string id)
    {
        var refusal = Only(SourceFixture.Full().WithJson(path, h => h["producer"] = producer).Parse());

        Assert.Equal("S02-010", refusal.Id);
        Assert.Equal(
            $"Node '{id}' declares producer '{producer}', which is reserved for a later version (accepted at 31: generated, computed).",
            refusal.Sentence);
        Assert.Equal(path, refusal.Path);
    }

    [Theory]
    [InlineData("nodes/plan.md", "computed")]
    [InlineData("nodes/plan.md", "groundd")]
    [InlineData("nodes/refine/index.json", "generated")]
    public void AnyOtherWrongProducer_IsStillS01_028(string path, string producer)
    {
        var refusal = Only(SourceFixture.Full().WithJson(path, h => h["producer"] = producer).Parse());

        Assert.Equal("S01-028", refusal.Id);
    }

    [Fact]
    public void TheReservedProducers_AreTheOnesConstantsJsonPublishes()
    {
        var producers = V31Contract.Json("constants.json")["producers"]!;

        Assert.Equal(producers["reserved"]!.AsArray().Select(v => (string)v!), SourceParser.ReservedProducers);
        Assert.Equal(["generated", "computed"], producers["accepted"]!.AsArray().Select(v => (string)v!));
    }

    [Fact]
    public void AnAgentsInlineFormat_IsPart02sOwnSentence()
    {
        var refusal = Only(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["format"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
            ["additionalProperties"] = false,
        }).Parse());

        Assert.Equal("S02-014", refusal.Id);
        Assert.Equal("Node 'plan' is kind agent and declares an inline format; an agent's format is named, in formats/.", refusal.Sentence);
        Assert.Equal("nodes/plan.md", refusal.Path);
    }

    [Fact]
    public void AnAgentsInlineFormat_InAFunction_IsPart02sOwnSentence()
    {
        var refusal = Only(SourceFixture.Full()
            .WithJson("functions/draft_section/patient_draft.md", h => h["format"] = new JsonObject { ["type"] = "string" })
            .Parse());

        Assert.Equal("S02-014", refusal.Id);
        Assert.Equal("Node 'patient_draft' is kind agent and declares an inline format; an agent's format is named, in formats/.", refusal.Sentence);
    }

    [Fact]
    public void AnAgentsFormatOfAnotherWrongType_IsStillA02_001()
    {
        var number = Only(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["format"] = 3).Parse());
        var list = Only(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["format"] = new JsonArray()).Parse());
        var nul = Only(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h["format"] = null).Parse());
        var missing = Only(SourceFixture.Minimal().WithJson("nodes/plan.md", h => h.Remove("format")).Parse());

        Assert.Equal("A02-001", number.Id);
        Assert.Equal("Node 'plan' declares 'format' as a whole number; it is text.", number.Sentence);
        Assert.Equal("A02-001", list.Id);
        Assert.Equal("S01-022", nul.Id);
        Assert.Equal("S01-021", missing.Id);
    }

    [Fact]
    public void AnInlineFormatOnAnotherKind_IsNotS02_014()
    {
        var files = SourceFixture.Full().WithJson("nodes/section_count.md", h => h["format"] = new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject { ["$ref"] = "plaintext" },
        });

        Assert.Empty(files.Parse().Refusals);
    }

    [Fact]
    public void AnInputsDefault_IsPart02sOwnSentence()
    {
        var refusal = Only(SourceFixture.Minimal()
            .WithJson("manifest.json", m => m["inputs"]!["consult_draft"]!["default"] = "Margaret Okafor, 58.")
            .Parse());

        Assert.Equal("S02-046", refusal.Id);
        Assert.Equal("Input 'consult_draft' declares default; inputs have no default at 31.", refusal.Sentence);
        Assert.Equal("manifest.json", refusal.Path);
    }

    [Fact]
    public void AnInputsDefaultOfNull_IsStillPart02sOwnSentence()
    {
        var refusal = Only(SourceFixture.Minimal()
            .WithJson("manifest.json", m => m["inputs"]!["consult_draft"]!["default"] = null)
            .Parse());

        Assert.Equal("S02-046", refusal.Id);
    }

    [Fact]
    public void AnotherUnknownInputMember_IsStillS01_023()
    {
        var refusal = Only(SourceFixture.Minimal()
            .WithJson("manifest.json", m => m["inputs"]!["consult_draft"]!["defaults"] = "x")
            .Parse());

        Assert.Equal("S01-023", refusal.Id);
    }

    [Fact]
    public void AMissingFormatFile_IsPart02sOwnSentence()
    {
        var files = SourceFixture.Full();
        files.WithJson("formats/index.json", json => json["items"] = new JsonArray("section_plan", "outline", "verdict", "standard_section", "ghost"));

        var refusal = Only(files.Parse());

        Assert.Equal("S02-039", refusal.Id);
        Assert.Equal("formats/index.json lists 'ghost', but formats/ has no ghost.json.", refusal.Sentence);
        Assert.Equal("formats/index.json", refusal.Path);
    }

    [Fact]
    public void AMissingFormatFile_OfAFunction_IsStillS01_009()
    {
        var files = SourceFixture.Full();
        files.WithJson("functions/draft_section/formats/index.json", json => json["items"] = new JsonArray("ghost"));

        var refusal = Only(files.Parse());

        Assert.Equal("S01-009", refusal.Id);
        Assert.Equal("functions/draft_section/formats/index.json lists 'ghost', but functions/draft_section/formats has no ghost.md or ghost/.", refusal.Sentence);
    }
}
