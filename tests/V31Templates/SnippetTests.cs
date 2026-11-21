using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 6; part 01 §§ 5.2, 5.3; D2, D12): a snippet is placed as
/// <c>snippet.&lt;id&gt;.content</c> (a folder item qualified), and its body is checked in every place it is placed, with
/// that place's reads (S05-022); a snippet the package does not define is S05-035; a folder item's header is of the
/// folder's format (S05-036); a snippet reaching itself is S05-023, said once; a chain deeper than 15 is S05-013; a
/// snippet placed in a part's text places no part or macro, directly or through another snippet (A07-001). Each placing
/// body holds the checked bodies of what it places, in order, placed units expanded in place.
/// </summary>
public class SnippetTests
{
    [Fact]
    public void ASnippet_IsCheckedInEveryPlaceItIsPlaced()
    {
        var heading = Package()
            .WithSnippet("heading", "Section: {{ item.value.title }}\n")
            .WithLoop("sections", "node.plan.value.sections", "outline", "Outline the section.\n{{~ snippet.heading.content }}\n")
            .WithAgent("draft", "Draft the note.\n{{~ snippet.heading.content }}\n");

        var refusal = TemplateFixture.Single(Check(heading));

        Assert.Equal("S05-022", refusal.Id);
        Assert.Equal("Snippet 'heading' reads item.value.title, which isn't available where Node 'draft' places it at line 2.", refusal.Sentence);
        Assert.Equal("nodes/draft.md", refusal.Path);

        var signature = Package().WithSnippet("signoff", "{{ profile.signature }}\n");
        var inAPrompt = TemplateFixture.Single(Check(signature.WithAgent("draft", "Draft the note.\n\n{{~ snippet.signoff.content }}\n")));
        Assert.Equal("Snippet 'signoff' reads profile.signature, which isn't available where Node 'draft' places it at line 3.", inAPrompt.Sentence);

        var inADeliverable = Check(Package().WithSnippet("signoff", "{{ profile.signature }}\n")
            .WithDeliverable("{{ node.plan.content }}\n{{~ snippet.signoff.content }}\n"));
        var deliverable = Body(inADeliverable, "deliverables/note.md");
        var unit = Assert.Single(deliverable.Units);
        Assert.Equal("snippet.signoff", unit.Site);
        Assert.Null(unit.Within);
        Assert.Equal("snippets/signoff.md", unit.Body.Host.Path);
        Assert.Equal(TemplateSite.SnippetBody, unit.Body.Host.Site);
        Assert.Equal(["profile.signature"], unit.Body.Reads.Select(r => r.Written));
    }

    [Fact]
    public void APlacedSnippet_ReadsAtItsPlacersPlace()
    {
        var files = Package()
            .WithSnippet("heading", "Section: {{ item.value.title }}\n")
            .WithLoop("sections", "node.plan.value.sections", "outline", "Outline the section.\n{{~ snippet.heading.content }}\n");

        var body = Body(Check(files), "nodes/sections/outline.md");

        var unit = Assert.Single(body.Units);
        Assert.Equal("item.value.title", Assert.Single(unit.Body.Reads).Written);
        Assert.Equal("plaintext", Assert.Single(unit.Body.Reads).Format!["$ref"]!.GetValue<string>());
    }

    [Fact]
    public void Units_AreInOrder_WithPlacedUnitsExpandedInPlace()
    {
        var files = Package()
            .WithSnippet("closing", "Thank you.\n")
            .WithSnippet("tone", "Write warmly.\n{{~ snippet.closing.content }}\n")
            .WithSnippet("brevity", "Be brief.\n")
            .WithJson("snippets/brevity.md", h => h["if"] = JsonNode.Parse("{ \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }"))
            .WithAgent("draft", "{{~ snippet.tone.content }}\nDraft the note.\n{{~ snippet.brevity.content }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal(
            [("snippet.tone", null), ("snippet.closing", "snippet.tone"), ("snippet.brevity", null)],
            body.Units.Select(u => (u.Site, u.Within)));
        Assert.Null(body.Units[0].If);
        Assert.Equal("{\"!\":[{\"blank\":[{\"var\":\"input.consult_draft.content\"}]}]}", body.Units[2].If!.ToJsonString());
        Assert.Equal(["snippet.closing"], body.Units[0].Body.Units.Select(u => u.Site));
    }

    [Fact]
    public void AnUndefinedSnippet_IsRefused()
    {
        var refusal = TemplateFixture.Single(Check(Package().WithAgent("draft", "Draft.\n{{~ snippet.nope.content }}\n")));

        Assert.Equal("S05-035", refusal.Id);
        Assert.Equal("Node 'draft' places snippet.nope, which this package doesn't define.", refusal.Sentence);
    }

    [Fact]
    public void AFolderItem_IsReadQualified()
    {
        var files = Standards("{ \"points\": [\"Onset\"] }", "Chronological prose.\n")
            .WithAgent("draft", "Draft.\n{{~ snippet.standards.hpi.content }}\n{{ snippet.standards.hpi.value.points | array.join \", \" }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        var unit = Assert.Single(body.Units);
        Assert.Equal("snippet.standards.hpi", unit.Site);
        Assert.Equal("snippets/standards/hpi.md", unit.Body.Host.Path);
        Assert.Equal("Snippet 'standards/hpi'", unit.Body.Host.Subject);
        Assert.Null(unit.If);
        Assert.Contains(body.Reads, r => r.Written == "snippet.standards.hpi.value.points" && r.Format != null);
    }

    [Theory]
    [InlineData("{ \"points\": \"Onset\" }", "type at /points")]
    [InlineData("{ }", "required at the header")]
    public void AFolderItemsHeader_IsHeldToTheFolderFormat(string header, string error)
    {
        var refusal = TemplateFixture.Single(Check(Standards(header, "Chronological prose.\n")));

        Assert.Equal("S05-036", refusal.Id);
        Assert.Equal($"Snippet 'standards/hpi' has a header that doesn't match format outline: {error}.", refusal.Sentence);
        Assert.Equal("snippets/standards/hpi.md", refusal.Path);
    }

    [Fact]
    public void AFolderItemWithAnEmptyBody_IsValid()
    {
        var check = Check(Standards("{ \"points\": [] }", ""));

        Assert.Empty(check.Refusals);
    }

    [Fact]
    public void AFolderItemWhoseHeaderIsAList_IsTheReadersRefusal()
    {
        var files = Standards("[\"Onset\"]", "Chronological prose.\n");

        var refusal = Assert.Single(RefusalsOf(files));

        Assert.Equal("S01-016", refusal.Id);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void ALargeSnippetPlacedOftenOnManyLines_IsCheckedOncePerPlace()
    {
        // A body of 4,000 reads, placed on 400 lines of each of 8 prompts: one check per place, not per line.
        var files = Package().WithSnippet("big", string.Concat(Enumerable.Repeat("{{ input.consult_draft.content }}\n", 4_000)));
        var placements = string.Concat(Enumerable.Repeat("{{ snippet.big.content }}\n", 400));
        for (var i = 0; i < 8; i++)
            files.WithAgent($"draft_{i}", "Draft.\n" + placements);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var check = Check(files);
        watch.Stop();

        Assert.Empty(check.Refusals);
        var units = check.Templates!.Bodies.Where(b => b.Host.Path.StartsWith("nodes/draft_", StringComparison.Ordinal)).SelectMany(b => b.Units).ToList();
        Assert.Equal(3_200, units.Count);
        Assert.Single(units.Select(u => u.Body).Distinct(ReferenceEqualityComparer.Instance));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"The check took {watch.Elapsed}.");
    }

    [Fact]
    public void AnUnavailableRead_IsSaidForEachPlacement()
    {
        var files = Package()
            .WithSnippet("heading", "Section: {{ item.value.title }}\n")
            .WithAgent("draft", "Draft the note.\n{{~ snippet.heading.content }}\nAgain:\n{{~ snippet.heading.content }}\n");

        var check = Check(files);

        Assert.Equal(
            ["Snippet 'heading' reads item.value.title, which isn't available where Node 'draft' places it at line 2.",
             "Snippet 'heading' reads item.value.title, which isn't available where Node 'draft' places it at line 4."],
            check.Refusals.Select(r => r.Sentence));
    }

    [Fact]
    public void ASnippetsFormat_IsATextFormat()
    {
        var files = Package().WithSnippet("tone", "Write warmly.\n", "outline");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("A07-010", refusal.Id);
        Assert.Equal("Snippet 'tone' declares format outline; a snippet's text is plaintext, markdown_block or markdown_inline.", refusal.Sentence);
    }

    [Fact]
    public void ASnippetNeverPlaced_IsHeldToTheDialect()
    {
        var refusal = TemplateFixture.Single(Check(Package().WithSnippet("tone", "Write warmly.\n{{ x = 1 }}\n")));

        Assert.Equal("S05-001", refusal.Id);
        Assert.Equal("snippets/tone.md", refusal.Path);
        Assert.StartsWith("Snippet 'tone' uses ", refusal.Sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnippetNeverPlaced_IsNotTyped()
    {
        var check = Check(Package().WithSnippet("heading", "Section: {{ item.value.title }} {{ profile.signature }}\n"));

        Assert.Empty(check.Refusals);
    }

    [Theory]
    [InlineData("a", "{{~ snippet.b.content }}\n", "{{~ snippet.a.content }}\n", "Snippet 'a' reaches itself: a → b → a.")]
    [InlineData("a", "Again: {{ snippet.a.content }}\n", "Plain.\n", "Snippet 'a' reaches itself: a → a.")]
    public void ASnippetCycle_IsRefusedOnce(string placed, string a, string b, string sentence)
    {
        var files = Package().WithSnippet("a", a).WithSnippet("b", b)
            .WithAgent("draft", $"Draft.\n{{{{~ snippet.{placed}.content }}}}\n")
            .WithAgent("review", "Review.\n{{~ snippet.b.content }}\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S05-023", refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
    }

    [Fact]
    public void ASnippetChain_IsAtMost15Deep()
    {
        var fifteen = Chain(15);
        Assert.Equal(15, Body(Check(fifteen), "nodes/draft.md").Units.Count);

        var refusal = TemplateFixture.Single(Check(Chain(16)));

        Assert.Equal("S05-013", refusal.Id);
        Assert.Equal("Snippet 's16' nests if and for 16 deep at line 1; the limit is 15.", refusal.Sentence);
        Assert.Equal("snippets/s15.md", refusal.Path);
    }

    [Fact]
    public void TheGuard_HoldsThroughASnippet()
    {
        var files = Package()
            .WithMacro("m", "Follow up with Dr A. Sample in six weeks.\n")
            .WithSnippet("b", "Then: {{ macro.m }}\n")
            .WithSnippet("a", "{{~ snippet.b.content }}\n")
            .WithAgent("draft", "Draft the note.\n{{~ part.p }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse("{ \"p\": { \"if\": null, \"text\": [\"Follow this:\", \"{{~ snippet.a.content }}\"] } }"));

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("A07-001", refusal.Id);
        Assert.Equal(
            "Snippet 'a' places macro.m, and Part 'p' of node 'draft' places it in a part's text; a snippet placed there doesn't place parts or macros.",
            refusal.Sentence);
        Assert.Equal("nodes/draft.md", refusal.Path);
    }

    [Fact]
    public void ASnippetPlacingAMacro_IsAllowedInAPrompt()
    {
        var files = Package()
            .WithMacro("m", "Follow up with Dr A. Sample in six weeks.\n")
            .WithSnippet("a", "Then: {{ macro.m }}\n")
            .WithAgent("draft", "Draft the note.\n{{~ snippet.a.content }}\n");

        var body = Body(Check(files), "nodes/draft.md");

        Assert.Equal(new Placement("macro.m", "macro", false, 1), Assert.Single(body.Units).Body.Placements.Single());
    }

    [Fact]
    public void ASnippetPlacingAPart_IsRefusedWhereItIsPlaced()
    {
        var files = Package()
            .WithSnippet("a", "{{~ part.brief }}\n")
            .WithAgent("draft", "Draft the note.\n{{~ part.brief }}\n{{~ snippet.a.content }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse("{ \"brief\": { \"if\": null, \"text\": [\"Be brief.\"] } }"));

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("Snippet 'a' reads part.brief, which isn't available where Node 'draft' places it at line 3.", refusal.Sentence);
    }

    [Fact]
    public void ASnippetInsideAFunction_ReadsOnlyWhatTheFunctionHas()
    {
        var path = "functions/draft_section/patient_draft.md";
        var function = ResolveFixture.Functions()
            .With("functions/draft_section/snippets/index.json", SourceFixture.Index(["facts", "plan_text"]))
            .With("functions/draft_section/snippets/facts.md", SourceFixture.Md(
                "{ \"label\": \"Facts\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }", "{{ param.draft.content }}\n"))
            .With("functions/draft_section/snippets/plan_text.md", SourceFixture.Md(
                "{ \"label\": \"Plan text\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }", "{{ node.plan.content }}\n"));

        var valid = Check(function.With(path, SourceFixture.Agent("Drafting from the patient's facts", Plain, "Write the facts.\n{{~ snippet.facts.content }}\n")));
        Assert.Equal("param.draft.content", Assert.Single(Body(valid, path).Units).Body.Reads.Single().Written);

        var refusal = TemplateFixture.Single(Check(function.With(path,
            SourceFixture.Agent("Drafting from the patient's facts", Plain, "Write the facts.\n{{~ snippet.facts.content }}\n{{~ snippet.plan_text.content }}\n"))));
        Assert.Equal("Snippet 'plan_text' reads node.plan.content, which isn't available where Node 'patient_draft' places it at line 3.", refusal.Sentence);
        Assert.Equal(path, refusal.Path);
    }

    [Fact]
    public void ManyPlacements_AreBounded_NeverExhaustThePublish()
    {
        // Ten snippets, each placing the next ten times: 10^10 units if expanded, refused at 1,000.
        var files = Package();
        for (var i = 1; i <= 10; i++)
        {
            var next = string.Concat(Enumerable.Repeat($"{{{{ snippet.f{i + 1}.content }}}}\n", 10));
            files.WithSnippet($"f{i}", i == 10 ? "Leaf.\n" : next);
        }

        files.WithAgent("draft", "Draft.\n{{~ snippet.f1.content }}\n");

        var check = Check(files);

        Assert.Contains(check.Refusals, r => r.Id == "A07-011" && r.Path == "nodes/draft.md");
        Assert.All(check.Refusals, V31Contract.AssertSentence);
    }

    [Theory]
    [InlineData("{{ snippet.nope.content }} {{ snippet.a.x.y }} {{ part }} {{ macro.a.b.c.d }} {{ part.x | string.upcase }}")]
    [InlineData("{{ for s in item.value }}{{ s.x }}{{ end }} {{ if s.state == \"done\" }}{{ end }}")]
    [InlineData("{{ x = 1 }}")]
    [InlineData("{{~ snippet.a.content }}\n{{~ part.brief }}\n{{~ macro.m }}\n{{ profile.nope }} {{ run.date }} {{ pass.number }}")]
    public void AnySnippetBody_IsCheckedOrRefused_NeverThrows(string body)
    {
        var files = Package().WithMacro("m", "Follow up.\n").WithSnippet("a", body + "\n")
            .WithAgent("draft", "Draft.\n{{~ snippet.a.content }}\n{{~ part.brief }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse("{ \"brief\": { \"if\": null, \"text\": [\"{{~ snippet.a.content }}\"] } }"))
            .WithDeliverable("{{ node.plan.content }}\n{{~ snippet.a.content }}\n");

        var check = Check(files);

        Assert.NotEmpty(check.Refusals);
        Assert.All(check.Refusals, V31Contract.AssertSentence);
    }

    /// <summary><see cref="TemplateFixture.Package"/> with the snippet folder <c>standards</c> (format <c>outline</c>) holding <c>hpi</c>.</summary>
    private static Dictionary<string, byte[]> Standards(string header, string body) =>
        Package()
            .Listed("snippets/index.json", "standards")
            .With("snippets/standards/index.json", SourceFixture.Index(["hpi"], extra: ", \"format\": \"outline\""))
            .With("snippets/standards/hpi.md", SourceFixture.Md(header, body));

    /// <summary>The snippets <c>s1</c> to <c>s&lt;n&gt;</c>, each placing the next on its first line, and <c>draft</c> placing <c>s1</c>.</summary>
    private static Dictionary<string, byte[]> Chain(int n)
    {
        var files = Package();
        for (var i = 1; i <= n; i++)
            files.WithSnippet($"s{i}", i == n ? "The end.\n" : $"{{{{~ snippet.s{i + 1}.content }}}}\n");
        return files.WithAgent("draft", "Draft.\n{{~ snippet.s1.content }}\n");
    }
}
