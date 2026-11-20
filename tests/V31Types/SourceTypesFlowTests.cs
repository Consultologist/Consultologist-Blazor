using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Types;

/// <summary>
/// #1500 (specVersion 31, area 03): a flow node's declared format against what its <c>ret</c> gives
/// (part 02 § 2.2, D24, D25): a <c>for</c> a list of it, a <c>while</c> it, an <c>if</c> or
/// <c>case</c> the union of its non-empty bodies; S02-015, or S02-016 for a body outside a declared
/// union. Each on <see cref="SourceFixture.Full"/>.
/// </summary>
public class SourceTypesFlowTests
{
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

    private static JsonObject Union(params string[] names) =>
        new() { ["anyOf"] = new JsonArray(names.Select(n => (JsonNode)new JsonObject { ["$ref"] = n }).ToArray()) };

    /// <summary>Full() with an <c>else</c> on <c>billing_gate</c> whose one agent returns <c>verdict</c>.</summary>
    private static Dictionary<string, byte[]> IfWithElse() => SourceFixture.Full()
        .WithJson("nodes/billing_gate/index.json", h => h["else"] = new JsonObject
        {
            ["nodes"] = new JsonArray("billing_verdict"),
            ["ret"] = "node.billing_gate.billing_verdict",
        })
        .With("nodes/billing_gate/billing_verdict.md", SourceFixture.Agent(
            "Judging whether billing applies", "\"verdict\"", "Say whether billing applies to this consultation.\n"));

    [Fact]
    public void ALoop_IsAListOfWhatItsRetReturns()
    {
        var refusal = Only(SourceFixture.Full().WithJson("nodes/sections/paragraphs/index.json", h => h["format"] = "plaintext"));

        Assert.Equal("S02-015", refusal.Id);
        Assert.Equal("Loop 'paragraphs' declares format plaintext, but its ret gives list<plaintext>.", refusal.Sentence);
        Assert.Equal("nodes/sections/paragraphs/index.json", refusal.Path);
    }

    [Fact]
    public void NestedLoops_Nest()
    {
        var files = SourceFixture.Full().WithJson("nodes/sections/index.json",
            h => h["format"] = JsonNode.Parse(SourceFixture.ArrayOf("\"plaintext\"")));

        var refusal = Only(files);

        Assert.Equal("S02-015", refusal.Id);
        Assert.Equal("Loop 'sections' declares format list<plaintext>, but its ret gives list<list<plaintext>>.", refusal.Sentence);
        Assert.Equal("nodes/sections/index.json", refusal.Path);
    }

    [Fact]
    public void ALoopsItems_AreWrittenAsAReference_OrInline()
    {
        // A name written bare or as a $ref is one type; an inline item is compared as written.
        var files = SourceFixture.Full()
            .WithJson("nodes/sections/paragraphs/draft_paragraph.md", h => h["format"] = "outline")
            .WithJson("nodes/sections/paragraphs/index.json", h => h["format"] = JsonNode.Parse(SourceFixture.ArrayOf("outline")))
            .WithJson("nodes/sections/index.json", h => h["format"] = JsonNode.Parse(SourceFixture.ArrayOf(SourceFixture.ArrayOf("outline"))));

        Assert.Empty(Refusals(files));
    }

    [Fact]
    public void AWhile_IsWhatItsRetReturns()
    {
        var refusal = Only(SourceFixture.Full().WithJson("nodes/refine/index.json", h => h["format"] = "verdict"));

        Assert.Equal("S02-015", refusal.Id);
        Assert.Equal("While 'refine' declares format verdict, but its ret gives plaintext.", refusal.Sentence);
        Assert.Equal("nodes/refine/index.json", refusal.Path);

        var accepted = SourceFixture.Full().WithJson("nodes/refine/index.json", h =>
        {
            h["format"] = "verdict";
            h["ret"] = "node.refine.judge";
        });
        Assert.Empty(Refusals(accepted));
    }

    [Fact]
    public void AnIf_IsItsThen_AndAnElseMakesAUnion()
    {
        Assert.Empty(Refusals(SourceFixture.Full()));

        var plain = Only(IfWithElse());
        Assert.Equal("S02-015", plain.Id);
        Assert.Equal("If 'billing_gate' declares format plaintext, but its ret gives plaintext | verdict.", plain.Sentence);
        Assert.Equal("nodes/billing_gate/index.json", plain.Path);

        var union = Only(IfWithElse().WithJson("nodes/billing_gate/index.json", h => h["format"] = Union("plaintext", "outline")));
        Assert.Equal("S02-016", union.Id);
        Assert.Equal("Branch 2 of if 'billing_gate' returns verdict, which is not in the declared union plaintext | outline.", union.Sentence);
        Assert.Equal("nodes/billing_gate/index.json", union.Path);

        // A declared member no body returns is S02-015 (D25).
        var wider = Only(IfWithElse().WithJson("nodes/billing_gate/index.json", h => h["format"] = Union("plaintext", "verdict", "outline")));
        Assert.Equal("S02-015", wider.Id);
        Assert.Equal("If 'billing_gate' declares format plaintext | verdict | outline, but its ret gives plaintext | verdict.", wider.Sentence);
    }

    [Fact]
    public void ACase_IgnoresItsEmptyBranches()
    {
        // route: two plaintext branches and an empty one gives plaintext, as declared.
        Assert.Empty(Refusals(SourceFixture.Full()));

        var refusal = Only(SourceFixture.Full().WithJson("nodes/sections_draft/route/index.json",
            h => h["format"] = JsonNode.Parse(SourceFixture.ArrayOf("\"plaintext\""))));
        Assert.Equal("S02-015", refusal.Id);
        Assert.Equal("Case 'route' declares format list<plaintext>, but its ret gives plaintext.", refusal.Sentence);
        Assert.Equal("nodes/sections_draft/route/index.json", refusal.Path);
    }

    [Fact]
    public void ACasesBranches_AreCountedInOrder_TheEmptyOnesIncluded_ItsElseLast()
    {
        var files = SourceFixture.Full()
            .WithJson("nodes/sections_draft/route/index.json", h =>
            {
                h["format"] = Union("plaintext", "outline");
                h["else"] = new JsonObject
                {
                    ["nodes"] = new JsonArray("other_draft"),
                    ["ret"] = "node.sections_draft.route.other_draft",
                };
            })
            .WithJson("nodes/sections_draft/route/table_draft.md", h => h["format"] = "outline")
            .With("nodes/sections_draft/route/other_draft.md", SourceFixture.Agent(
                "Judging a section of another kind", "\"verdict\"", "Say whether the section '{{ item.value.title }}' is needed.\n"));

        var refusal = Only(files);

        Assert.Equal("S02-016", refusal.Id);
        Assert.Equal("Branch 4 of case 'route' returns verdict, which is not in the declared union plaintext | outline.", refusal.Sentence);
    }

    [Fact]
    public void ADeclaredUnion_InAnotherOrder_IsTheSameType()
    {
        var files = IfWithElse().WithJson("nodes/billing_gate/index.json", h => h["format"] = Union("verdict", "plaintext"));

        Assert.Empty(Refusals(files));
    }

    [Fact]
    public void AFlowNode_WhoseRetWasRefused_IsNotJudgedTwice()
    {
        // paragraphs' ret names no format: S02-017 alone, neither loop above it judged.
        var unknown = Only(SourceFixture.Full().WithJson("nodes/sections/paragraphs/draft_paragraph.md", h => h["format"] = "ghost"));
        Assert.Equal("S02-017", unknown.Id);

        // paragraphs is refused for its own declared format; sections, whose ret it is, is not judged.
        var inner = Only(SourceFixture.Full().WithJson("nodes/sections/paragraphs/index.json", h => h["format"] = "plaintext"));
        Assert.Equal("Loop 'paragraphs' declares format plaintext, but its ret gives list<plaintext>.", inner.Sentence);

        // A rendered ret that returns no text is refused; the if whose ret it is is not judged.
        var rendered = Only(SourceFixture.Full().WithJson("nodes/billing_gate/billing_text.md", h => h["format"] = "number"));
        Assert.Equal("S02-013", rendered.Id);

        // A flow node whose own declared format was refused is not judged either.
        var own = Only(SourceFixture.Full().WithJson("nodes/refine/index.json", h => h["format"] = "ghost"));
        Assert.Equal("S02-017", own.Id);
    }

    [Fact]
    public void ACheckLoop_IsAListOfCheckResults()
    {
        var refusal = Only(SourceFixture.Full().WithJson("checks/section_coverage/index.json", h => h["format"] = "check_result"));

        Assert.Equal("S02-015", refusal.Id);
        Assert.Equal("Loop 'section_coverage' declares format check_result, but its ret gives list<check_result>.", refusal.Sentence);
        Assert.Equal("checks/section_coverage/index.json", refusal.Path);
    }

    [Fact]
    public void AFunctionsFlowNode_IsJudgedInItsScope()
    {
        var files = SourceFixture.Full()
            .WithJson("functions/draft_section/index.json", h =>
            {
                h["nodes"] = new JsonArray("standard_draft", "patient_draft", "apply_instructions", "again");
            })
            .With("functions/draft_section/again/index.json", SourceFixture.Json(SourceFixture.Node("while", "Again", "\"standard_section\"", false, true,
                "\"while\": false, \"max\": 2, \"until\": null, \"checkFirst\": false, \"nodes\": [\"redo\"], "
                + "\"ret\": \"node.again.redo\"")))
            .With("functions/draft_section/again/redo.md", SourceFixture.Agent("Redoing", "\"plaintext\"", "Again.\n"));

        var refusal = Only(files);

        Assert.Equal("S02-015", refusal.Id);
        Assert.Equal("While 'again' declares format standard_section, but its ret gives plaintext.", refusal.Sentence);
        Assert.Equal("functions/draft_section/again/index.json", refusal.Path);
    }
}
