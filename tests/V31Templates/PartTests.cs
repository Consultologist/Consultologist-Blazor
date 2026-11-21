using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 7; part 01 § 5): an agent's <c>parts</c>: each part has exactly <c>if</c>
/// and <c>text</c> (the reader's member sentences), its text, lines joined with <c>\n</c>, is a template that only
/// presents (A07-002), and the body places each declared part exactly once (S05-038, S05-039, S05-040). Only agent nodes
/// have parts (S05-041). A placed part is a unit of the body, its own placements expanded after it.
/// </summary>
public class PartTests
{
    private const string Brief = "{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\"] } }";

    [Fact]
    public void TheSpecsExample_IsValid()
    {
        var files = ResolveFixture.WithFormats(SourceFixture.Minimal(), "standard_section")
            .Listed("snippets/index.json", "standards")
            .With("snippets/standards/index.json", SourceFixture.Index(["hpi"], extra: ", \"format\": \"standard_section\""))
            .With("snippets/standards/hpi.md", SourceFixture.Md("{ \"title\": \"History of Present Illness\" }", "Chronological prose narrative.\n"))
            .WithLoop("standards", "snippet.standards", "section_instructions",
                "Requested section:\n{{ item.value.title }}\n\nSection standard and section-specific instructions:\n{{~ part.no_standard }}\n{{ item.content }}\n")
            .WithJson("nodes/standards/section_instructions.md", h => h["parts"] = JsonNode.Parse(
                "{ \"no_standard\": { \"if\": { \"blank\": [ { \"var\": \"item.content\" } ] }, "
                + "\"text\": [ \"No changes. Preserve the patient-updated draft's clinical content and\", "
                + "\"produce polished final prose for this section.\" ] } }"));

        var body = Body(Check(files), "nodes/standards/section_instructions.md");

        var unit = Assert.Single(body.Units);
        Assert.Equal("part.no_standard", unit.Site);
        Assert.Null(unit.Within);
        Assert.Equal("{\"blank\":[{\"var\":\"item.content\"}]}", unit.If!.ToJsonString());
        Assert.Equal(TemplateSite.PartText, unit.Body.Host.Site);
        Assert.Equal("Part 'no_standard' of node 'section_instructions'", unit.Body.Host.Subject);
        Assert.Equal("no_standard", unit.Body.Host.PartId);
        Assert.Equal(
            "No changes. Preserve the patient-updated draft's clinical content and\nproduce polished final prose for this section.",
            unit.Body.Syntax.Text);
    }

    [Fact]
    public void APartsText_IsExpandedAfterIt()
    {
        var files = Package().WithSnippet("tone", "Write warmly.\n")
            .WithAgent("draft", "Draft the note.\n{{~ part.brief }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse(
                "{ \"brief\": { \"if\": null, \"text\": [\"Be brief, and:\", \"{{~ snippet.tone.content }}\"] } }"));

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal([("part.brief", null), ("snippet.tone", "part.brief")], body.Units.Select(u => (u.Site, u.Within)));
    }

    [Theory]
    [InlineData("Draft.\n{{~ part.nope }}\n", "null", "S05-038", "Node 'draft' places part.nope, which its header doesn't declare.")]
    [InlineData("Draft.\n", Brief, "S05-039", "Node 'draft' declares part 'brief' but never places it.")]
    [InlineData("Draft.\n{{~ part.brief }}\nAgain: {{ part.brief }}\n", Brief, "S05-040", "Node 'draft' places part.brief 2 times; place a part once.")]
    [InlineData("Draft.\n{{~ part.brief }}\n",
        "{ \"brief\": { \"if\": null, \"text\": [\"{{ if check.coverage.value.pass }}Covered.{{ end }}\"] } }",
        "A07-002", "Part 'brief' of node 'draft' uses if; a part's text only presents.")]
    [InlineData("Draft.\n{{~ part.brief }}\n",
        "{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\", \"{{ part.brief }}\"] } }",
        "A07-002", "Part 'brief' of node 'draft' uses part.brief; a part's text only presents.")]
    public void APartsRule_IsRefused(string prompt, string parts, string id, string sentence)
    {
        var files = Package().WithCheck().WithAgent("draft", prompt)
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse(parts));

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal(id, refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
        Assert.Equal("nodes/draft.md", refusal.Path);
    }

    [Fact]
    public void APartsText_ReadsWhatItsNodesPromptReads()
    {
        var files = Package().WithAgent("draft", "Draft.\n{{~ part.brief }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse(
                "{ \"brief\": { \"if\": null, \"text\": [\"Sign as {{ profile.signature }}.\"] } }"));

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S05-021", refusal.Id);
        Assert.Equal("Node 'draft' reads profile.signature; the clinician's profile is never sent to a model. Read it in a deliverable or a macro body.", refusal.Sentence);
    }

    [Fact]
    public void PartsOnANodeThatIsNoAgent_AreRefused()
    {
        var files = Package().WithRendered("summary", "A summary of {{ node.plan.content }}.\n");

        var check = Check(files, package => package.Nodes.Nodes.OfType<SourceLeaf>().Single(l => l.Id == "summary")
            .Header["parts"] = JsonNode.Parse(Brief));

        var refusal = TemplateFixture.Single(check);
        Assert.Equal("S05-041", refusal.Id);
        Assert.Equal("Node 'summary' declares parts, and only agent nodes have parts.", refusal.Sentence);
    }

    [Fact]
    public void PartsOnARenderedNode_AreTheReadersUnknownMember()
    {
        var files = Package().WithRendered("summary", "A summary of {{ node.plan.content }}.\n")
            .WithJson("nodes/summary.md", h => h["parts"] = JsonNode.Parse(Brief));

        Assert.Equal("S01-023", Assert.Single(RefusalsOf(files)).Id);
    }

    public static TheoryData<string, string?> PartsMembers => new()
    {
        { "\"brief\"", "A02-001" },
        { "{ \"brief\": \"Be brief.\" }", "A02-001" },
        { "{ \"brief\": { \"if\": null } }", "S01-021" },
        { "{ \"brief\": { \"text\": [\"Be brief.\"] } }", "S01-021" },
        { "{ \"brief\": { \"if\": null, \"text\": null } }", "S01-022" },
        { "{ \"brief\": { \"if\": null, \"text\": \"Be brief.\" } }", "A02-001" },
        { "{ \"brief\": { \"if\": null, \"text\": [1, 2] } }", "A02-001" },
        { "{ \"brief\": { \"if\": null, \"text\": [] } }", null },
        { "{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\"], \"label\": \"Brief\" } }", "S01-023" },
        { "{ \"Bad-Id\": { \"if\": null, \"text\": [\"Be brief.\"] }, \"brief\": { \"if\": null, \"text\": [\"Be brief.\"] } }", "S01-030" },
        { "{ }", null },
        { "null", null },
    };

    [Theory]
    [MemberData(nameof(PartsMembers))]
    public void AnyPartsMember_IsRefusedOrRead_NeverThrows(string parts, string? id)
    {
        var files = Package().WithAgent("draft", "Draft.\n" + (parts.Contains("brief", StringComparison.Ordinal) ? "{{~ part.brief }}\n" : ""))
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse(parts));

        var refusals = RefusalsOf(files);

        Assert.All(refusals, V31Contract.AssertSentence);
        if (id == null)
        {
            Assert.Empty(refusals);
            return;
        }

        Assert.Equal(id, Assert.Single(refusals).Id);
    }

    [Fact]
    public void APartThatIsNoObject_IsNamedByItsNode()
    {
        var files = Package().WithAgent("draft", "Draft.\n{{~ part.brief }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse("{ \"brief\": \"Be brief.\" }"));

        Assert.Equal("Node 'draft' declares 'parts.brief' as text; it is an object.", Assert.Single(RefusalsOf(files)).Sentence);
    }

    [Fact]
    public void APartsMissingText_IsNamedAsThePart()
    {
        var files = Package().WithAgent("draft", "Draft.\n{{~ part.brief }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse("{ \"brief\": { \"if\": null } }"));

        Assert.Equal(
            "Part 'brief' of node 'draft' is missing 'text'; every member is written, and null means none.",
            Assert.Single(RefusalsOf(files)).Sentence);
    }
}
