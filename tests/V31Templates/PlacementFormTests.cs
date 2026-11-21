using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 2.6): a placement (an output whose whole expression is
/// <c>snippet.&lt;…&gt;.content</c>, <c>part.&lt;id&gt;</c> or <c>macro.&lt;…&gt;</c>) is held to § 2.6's table of forms,
/// and is placed at the top level of its template (A07-009).
/// </summary>
public class PlacementFormTests
{
    private static Dictionary<string, byte[]> Prompt(string body) =>
        Package().WithSnippet("tone", "Write warmly for Margaret Okafor.\n").WithAgent("draft", "Draft the note.\n" + body + "\nThank you.\n");

    /// <summary>§ 2.6's table, one row each: the line written, and the sentence (<c>null</c>: allowed) and whether it is a block.</summary>
    public static TheoryData<string, string?, bool> Rows => new()
    {
        { "{{~ snippet.tone.content }}", null, true },
        { "Patient with diabetes{{ snippet.tone.content }}.", null, false },
        { "  {{ snippet.tone.content }}", null, false },
        { "{{~ snippet.tone.content ~}}", "S05-015", false },
        { "{{- snippet.tone.content }}", "S05-016", false },
        { "{{ snippet.tone.content -}}", "S05-016", false },
        { "Tone: {{~ snippet.tone.content }}", "S05-017", false },
        { "{{~ snippet.tone.content }} please.", "S05-018", false },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void EachRowOfTheTable_IsAllowedOrRefused(string line, string? id, bool block)
    {
        var check = Check(Prompt(line));

        if (id == null)
        {
            var placement = Assert.Single(Body(check, "nodes/draft.md").Placements);
            Assert.Equal(new Placement("snippet.tone.content", "snippet", block, 2), placement);
            return;
        }

        var refusal = TemplateFixture.Single(check);
        Assert.Equal(id, refusal.Id);
        Assert.Contains(" snippet.tone.content", refusal.Sentence, StringComparison.Ordinal);
        Assert.Contains(" at line 2", refusal.Sentence, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{{~ snippet.tone.content ~}}", "Node 'draft' places snippet.tone.content with ~}} at line 2; it removes the line's newline, joining two lines. Use {{~ snippet.tone.content }} alone on a line.")]
    [InlineData("{{- snippet.tone.content }}", "Node 'draft' places snippet.tone.content with - at line 2; it removes blank lines between paragraphs.")]
    [InlineData("Tone: {{~ snippet.tone.content }}", "Node 'draft' places {{~ snippet.tone.content }} inside a line at line 2; it removes the space before it. Use {{ snippet.tone.content }}.")]
    [InlineData("{{~ snippet.tone.content }} please.", "Node 'draft' has text after {{~ snippet.tone.content }} at line 2; a block placement stands alone on its line.")]
    public void ARefusedForm_SaysItsSentence(string line, string sentence)
    {
        Assert.Equal(sentence, TemplateFixture.Single(Check(Prompt(line))).Sentence);
    }

    [Fact]
    public void ADashOnAnOrdinaryOutput_IsStillTheDash()
    {
        var refusal = TemplateFixture.Single(Check(Prompt("{{ input.consult_draft.content -}}")));

        Assert.Equal("S05-019", refusal.Id);
    }

    [Fact]
    public void PartsAndMacros_ArePlacementsToo()
    {
        var files = Package().WithMacro("follow_up", "Follow up with Dr A. Sample in six weeks.\n")
            .WithAgent("draft", "Draft the note.\n{{~ part.brief }}\nEnd with {{ macro.follow_up }}.\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse("{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\"] } }"));

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal(
            [new Placement("part.brief", "part", true, 2), new Placement("macro.follow_up", "macro", false, 3)],
            body.Placements);
    }

    [Theory]
    [InlineData("{{ if check.coverage.value.pass }}\n{{~ snippet.tone.content }}\n{{ end }}", "an if", 3)]
    [InlineData("{{ for s in node.plan.value.sections }}\n{{ snippet.tone.content }}\n{{ end }}", "a for", 3)]
    public void APlacementInsideAnIfOrAFor_IsRefused(string body, string where, int line)
    {
        var files = Package().WithCheck().WithSnippet("tone", "Write warmly.\n").WithAgent("draft", "Draft.\n" + body + "\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("A07-009", refusal.Id);
        Assert.Equal(
            $"Node 'draft' places snippet.tone.content at line {line} inside {where}; a part, a snippet or a macro is placed at the top level of its template, and its own if decides it.",
            refusal.Sentence);
    }

    [Fact]
    public void AFolderItem_IsPlacedQualified()
    {
        var files = Package()
            .WithJson("snippets/index.json", i => i["items"]!.AsArray().Add((JsonNode)"standards"))
            .With("snippets/standards/index.json", SourceFixture.Index(["hpi"], extra: ", \"format\": \"outline\""))
            .With("snippets/standards/hpi.md", SourceFixture.Md("{ \"points\": [] }", "Chronological prose.\n"))
            .WithAgent("draft", "Draft.\n{{~ snippet.standards.hpi.content }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal(new Placement("snippet.standards.hpi.content", "snippet", true, 2), Assert.Single(body.Placements));
        Assert.Equal("markdown_block", body.Reads.Single(r => r.Written == "snippet.standards.hpi.content").Format!.GetValue<string>());
    }
}
