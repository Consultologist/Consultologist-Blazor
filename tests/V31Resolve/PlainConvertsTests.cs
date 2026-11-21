using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1503 (specVersion 31, area 06; D3, 04-D14): <c>resolve.json</c> keeps, on every entry of <c>nodes</c>, <c>checks</c>,
/// <c>deliverables</c> and a function's <c>nodes</c>, <c>plainConverts</c>: for each of <c>if</c>, <c>expression</c>,
/// <c>while</c> and <c>until</c> the entry has non-null, whether each <c>plain</c> of that rule converts
/// (<see cref="ExpressionCheck.PlainConverts"/>), <c>[]</c> for a rule with none, <c>{}</c> for an entry with no rule.
/// </summary>
public class PlainConvertsTests
{
    private static JsonObject Compile(Dictionary<string, byte[]> files)
    {
        var check = ResolveFixture.Run(files);
        Assert.True(check.Refusals.Count == 0, ResolveFixture.Said(check));
        var result = ResolveCompiler.Compile(check, ResolveFixture.Published(), "7.2.5");
        Assert.True(result.Refusals.Count == 0, string.Join(" | ", result.Refusals.Select(r => $"{r.Id} {r.Sentence}")));
        return result.Document!;
    }

    private static string PlainConverts(JsonObject document, string map, string key) =>
        CanonicalJson.Write(document[map]![key]!["plainConverts"]);

    /// <summary>
    /// <see cref="ResolveFixture.NestedNote"/> with two computed nodes: <c>converted</c>, <c>plain</c> of the Markdown
    /// input, and <c>kept</c>, <c>plain</c> of the while's <c>plaintext</c> result.
    /// </summary>
    private static Dictionary<string, byte[]> WithPlains() =>
        ResolveFixture.NestedNote()
            .With("nodes/index.json", Index(["plan", "section_count", "sections", "refine", "converted", "kept"]))
            .With("nodes/converted.md", Md(Node("computed", "The draft as plain text", "\"plaintext\"", true, false,
                "\"if\": null, \"expression\": { \"plain\": [{ \"var\": \"input.consult_draft.content\" }] }"), ""))
            .With("nodes/kept.md", Md(Node("computed", "The summary as plain text", "\"plaintext\"", true, true,
                "\"if\": null, \"expression\": { \"plain\": [{ \"var\": \"node.refine.content\" }] }"), ""));

    [Fact]
    public void EveryRuleMember_HasItsList()
    {
        var document = Compile(WithPlains());

        Assert.Equal("""{"expression":[true]}""", PlainConverts(document, "nodes", "node.converted"));
        Assert.Equal("""{"expression":[false]}""", PlainConverts(document, "nodes", "node.kept"));
        Assert.Equal("""{"expression":[]}""", PlainConverts(document, "nodes", "node.section_count"));
        Assert.Equal("""{"if":[]}""", PlainConverts(document, "nodes", "node.sections.paragraphs.draft_paragraph"));
        Assert.Equal("""{"while":[]}""", PlainConverts(document, "nodes", "node.refine"));
        Assert.Equal("{}", PlainConverts(document, "nodes", "node.sections"));
        Assert.Equal("{}", PlainConverts(document, "nodes", "node.plan"));
        Assert.Equal("{}", PlainConverts(document, "deliverables", "deliverable.note"));
    }

    [Fact]
    public void ChecksAndFunctionNodes_HaveTheirLists()
    {
        var document = Compile(ResolveFixture.Functions());

        Assert.Equal("""{"expression":[],"if":[]}""", PlainConverts(document, "checks", "check.section_coverage.coverage"));
        Assert.Equal("{}", PlainConverts(document, "checks", "check.section_coverage"));
        Assert.Equal("""{"if":[]}""",
            CanonicalJson.Write(document["functions"]!["draft_section"]!["nodes"]!["node.standard_draft"]!["plainConverts"]));
    }

    [Fact]
    public void AnUntilAndADeliverablesIf_HaveTheirLists()
    {
        // a for's until and a deliverable's if, each with one plain of the Markdown input
        var files = ResolveFixture.NestedNote()
            .WithJson("nodes/sections/index.json", h =>
            {
                h["parallel"] = false;
                h["until"] = JsonNode.Parse("""{ "has_word": [{ "plain": [{ "var": "input.consult_draft.content" }] }, "urgent"] }""");
            })
            .WithJson("deliverables/note.md", h =>
                h["if"] = JsonNode.Parse("""{ "!": [{ "blank": [{ "plain": [{ "var": "input.consult_draft.content" }] }] }] }"""));

        var document = Compile(files);

        Assert.Equal("""{"until":[true]}""", PlainConverts(document, "nodes", "node.sections"));
        Assert.Equal("""{"if":[true]}""", PlainConverts(document, "deliverables", "deliverable.note"));
    }
}
