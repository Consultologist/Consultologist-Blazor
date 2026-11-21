using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 5.1 to 5.4; part 01 § 9.1; <c>scriban-dialect.md</c> § 6; D6): a
/// renderer's file returns a text format and its body reads only <c>value</c> and pipes into no renderer; at a use the
/// renderer exists (the package's, the function's own, or a built-in), the piped value is of its <c>input</c>, each step
/// of a chain returns the next one's <c>input</c>, each step is one render in source order, and a <c>markdown_block</c>
/// renderer piped inside a line warns. The registry carries each built-in renderer's header and body.
/// </summary>
public class RendererTests
{
    /// <summary>The package with the agent <c>extract</c>, returning a <c>concept_list</c>.</summary>
    private static Dictionary<string, byte[]> WithExtract() =>
        Package().WithAgent("extract", "List the clinical terms in the draft.\n\n{{ input.consult_draft.content }}\n", "\"concept_list\"");

    public static TheoryData<string, string, string, string> RendererFiles => new()
    {
        { "\"concept_list\"", "number", "{{ value.concepts.size }}\n",
            "Renderer 'terms' declares format number; a renderer returns plaintext, markdown_block or markdown_inline." },
        { "\"concept_list\"", "markdown_block", "{{ node.plan.content }}\n",
            "Renderer 'terms' reads node.*; a renderer reads only the value piped into it." },
        { "\"concept_list\"", "markdown_block", "{{ for c in value.concepts }}{{ c.term }} {{ run.date }}{{ end }}\n",
            "Renderer 'terms' reads run.*; a renderer reads only the value piped into it." },
        { "\"concept_list\"", "markdown_block", "{{ value | renderer.concept_bullets }}\n",
            "Renderer 'terms' pipes into renderer.concept_bullets at line 1; chain renderers where they're used, not inside a renderer." },
    };

    [Theory]
    [MemberData(nameof(RendererFiles))]
    public void ARendererFile_IsHeldToItsRules(string input, string format, string body, string sentence)
    {
        var refusal = TemplateFixture.Single(Check(Package().WithRenderer("terms", input, body, format)));

        Assert.Equal(sentence, refusal.Sentence);
        Assert.Equal("renderers/terms.md", refusal.Path);
    }

    [Fact]
    public void ARenderersUnknownInput_IsRefusedBeforeTheDialect()
    {
        // Part 05 § 5.1: `input` is a format that exists. The type rules hold every format member to that (S02-017);
        // the dialect, given the package anyway, types nothing through it and never throws.
        var files = Package().WithRenderer("terms", "\"no_such_list\"", "{{ value.concepts.size }}\n")
            .WithAgent("draft", "{{ node.plan.value | renderer.terms }}\n");
        var registry = ResolveFixture.Published();
        var parsed = SourceParser.Parse(files, registry.Names);
        Assert.NotNull(parsed.Package);

        var types = SourceTypes.Check(parsed.Package!, registry);

        var refusal = Assert.Single(types.Refusals, r => r.Path == "renderers/terms.md");
        Assert.Contains("no_such_list", refusal.Sentence, StringComparison.Ordinal);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void APipe_IsTyped()
    {
        var check = Check(WithExtract().WithAgent("draft", "{{ node.extract.value | renderer.concept_bullets }}\n"));

        var body = Body(check, "nodes/draft.md");
        Assert.Equal([new TemplateRender(1, "node.extract.value", "concept_bullets")], body.Renders);
        Assert.Empty(check.Warnings);
    }

    [Fact]
    public void AValueOfAnotherFormat_IsRefused_WithTheSpecsExample()
    {
        var refusal = TemplateFixture.Single(Check(Package().WithAgent("identify_problem", "{{ node.plan.value | renderer.concept_bullets }}\n")));

        Assert.Equal("S05-026", refusal.Id);
        Assert.Equal("Node 'identify_problem' pipes node.plan.value (section_plan) into renderer.concept_bullets, which renders concept_list.", refusal.Sentence);
        Assert.Equal("nodes/identify_problem.md", refusal.Path);
    }

    [Fact]
    public void ANodesContent_IsNotItsValue()
    {
        var refusal = TemplateFixture.Single(Check(WithExtract().WithAgent("draft", "{{ node.extract.content | renderer.concept_bullets }}\n")));

        Assert.Equal("S05-026", refusal.Id);
        Assert.Equal("Node 'draft' pipes node.extract.content (plaintext) into renderer.concept_bullets, which renders concept_list.", refusal.Sentence);
    }

    [Fact]
    public void ABuiltInStepBeforeARenderer_IsTypedThroughIt()
    {
        var files = Package().WithRenderer("quote", "\"plaintext\"", "> {{ value }}\n", "markdown_inline")
            .WithAgent("draft", "{{ node.plan.value.sections | array.size | renderer.quote }}\n");

        var refusal = TemplateFixture.Single(Check(files));

        Assert.Equal("S05-026", refusal.Id);
        Assert.Equal("Node 'draft' pipes node.plan.value.sections | array.size (integer) into renderer.quote, which renders plaintext.", refusal.Sentence);
    }

    [Fact]
    public void AChain_IsTypedStepByStep()
    {
        var files = WithExtract().WithRenderer("quote", "\"markdown_block\"", "{{ value }}\n", "plaintext")
            .WithAgent("draft", "{{ node.extract.value | renderer.concept_bullets | renderer.quote }}\n")
            .WithAgent("wrong", "{{ node.extract.value | renderer.quote }}\n{{ node.extract.value | renderer.concept_bullets | renderer.concept_context }}\n");

        var check = Check(files);

        Assert.Equal(
            [
                "S05-026 Node 'wrong' pipes node.extract.value (concept_list) into renderer.quote, which renders markdown_block.",
                "S05-034 Node 'wrong' pipes renderer.concept_bullets (markdown_block) into renderer.concept_context, which renders concept_list.",
            ],
            check.Refusals.Select(r => $"{r.Id} {r.Sentence}"));
        check.Refusals.ToList().ForEach(V31Contract.AssertSentence);

        var valid = Check(WithExtract().WithRenderer("quote", "\"markdown_block\"", "{{ value }}\n", "plaintext")
            .WithAgent("draft", "{{ node.extract.value | renderer.concept_bullets | renderer.quote }}\n{{ node.extract.value | renderer.concept_context }}\n"));
        Assert.Equal(
            [
                new TemplateRender(1, "node.extract.value", "concept_bullets"),
                new TemplateRender(2, "render:1", "quote"),
                new TemplateRender(3, "node.extract.value", "concept_context"),
            ],
            Body(valid, "nodes/draft.md").Renders);
    }

    [Fact]
    public void AnUndefinedRenderer_IsRefused()
    {
        var refusal = TemplateFixture.Single(Check(WithExtract().WithAgent("draft", "{{ node.extract.value | renderer.terms }}\n")));

        Assert.Equal("S05-031", refusal.Id);
        Assert.Equal("Node 'draft' pipes into renderer.terms, which this package doesn't define.", refusal.Sentence);
    }

    [Fact]
    public void AFunctionsOwnRenderer_IsDefinedInIt()
    {
        var files = ResolveFixture.Functions()
            .With("functions/draft_section/renderers/index.json", SourceFixture.Index(["shout"]))
            .With("functions/draft_section/renderers/shout.md", SourceFixture.Md(
                "{ \"label\": \"Shout\", \"kind\": \"rendered\", \"description\": null, \"input\": \"markdown_block\", \"format\": \"plaintext\" }",
                "{{ value | string.upcase }}\n"))
            .With("functions/draft_section/patient_draft.md", SourceFixture.Agent("Drafting from the patient's facts", Plain,
                "Write what the draft says about Margaret Okafor.\n\n{{ param.draft.content | renderer.shout }}\n"));

        var body = Body(Check(files), "functions/draft_section/patient_draft.md");

        Assert.Equal([new TemplateRender(1, "param.draft.content", "shout")], body.Renders);
    }

    [Theory]
    [InlineData("Terms: {{ node.extract.value | renderer.concept_bullets }}\n", 1)]
    [InlineData("{{ node.extract.value | renderer.concept_bullets }} and more.\n", 1)]
    [InlineData("Terms:\n{{ node.extract.value | renderer.concept_bullets }}\n", 0)]
    public void ABlockRendererInsideALine_Warns(string body, int warnings)
    {
        var check = Check(WithExtract().WithAgent("draft", body));

        Assert.Empty(check.Refusals);
        Assert.Equal(warnings, check.Warnings.Count);
        foreach (var warning in check.Warnings)
        {
            Assert.Equal("S05-061", warning.Id);
            Assert.Equal("Node 'draft' pipes renderer.concept_bullets, a markdown_block, inside a line at line 1.", warning.Sentence);
            Assert.Equal("nodes/draft.md", warning.Path);
            V31Contract.AssertSentence(warning);
        }
    }

    [Fact]
    public void ARunSpot_MayPipeToARenderer()
    {
        var files = Package().WithRenderer("long_date", "\"date\"", "{{ value | date.to_string \"%d %B %Y\" }}\n", "plaintext")
            .WithAgent("draft", "{{ run.date | renderer.long_date }}\n");

        var check = Check(files);

        var body = Body(check, "nodes/draft.md");
        Assert.Equal([new TemplateRunSpot(1, "1:1", "run.date")], body.RunSpots);
        Assert.Equal([new TemplateRender(1, "run.date", "long_date")], body.Renders);
        Assert.Equal("S05-060", Assert.Single(check.Warnings).Id);
    }

    [Theory]
    [InlineData("{{ (node.plan.content) | renderer.concept_bullets }}")]
    [InlineData("{{ node.extract.value | renderer.concept_bullets | renderer.nope | renderer.concept_context }}")]
    [InlineData("{{ renderer.concept_bullets | renderer.concept_bullets }}")]
    [InlineData("{{ node.extract.value | string.strip | renderer.concept_bullets }}")]
    [InlineData("{{ node.extract.value | renderer.concept_bullets(1) | renderer.concept_bullets }}")]
    [InlineData("{{ node.nope.value | renderer.concept_bullets }}")]
    [InlineData("{{ for c in node.extract.value.concepts }}{{ c | renderer.concept_bullets }}{{ end }}")]
    public void AnyRendererUse_IsCheckedOrRefused_NeverThrows(string body)
    {
        var check = Check(WithExtract().WithAgent("draft", body + "\n"));

        foreach (var refusal in check.Refusals.Concat(check.Warnings))
            V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void TheRegistry_CarriesEachBuiltInRenderer()
    {
        var registry = ResolveFixture.Published();

        Assert.Equal(["concept_bullets", "concept_context"], registry.BuiltInRenderers.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(registry.BuiltInRendererIds, registry.Names.BuiltInRenderers);
        foreach (var (id, renderer) in registry.BuiltInRenderers)
        {
            var text = File.ReadAllText(V31Contract.Path("renderers", id + ".md")).ReplaceLineEndings("\n");
            var split = FrontMatter.Split(text)!;
            Assert.Equal(id, renderer.Id);
            Assert.Equal("1.0.0", renderer.Version);
            Assert.Equal(JsonNode.Parse(split.Header)!.ToJsonString(), renderer.Header.ToJsonString());
            Assert.Equal(split.Body, renderer.Body);
            Assert.Equal("concept_list", (string?)renderer.Header["input"]);
        }
    }

    [Fact]
    public void TheBuiltInRenderers_AreValidRendererBodies()
    {
        // The built-ins are checked once, here, not per package: each, as a package renderer under another id.
        foreach (var renderer in ResolveFixture.Published().BuiltInRenderers.Values)
        {
            var files = Package().Listed("renderers/index.json", "copy")
                .With("renderers/copy.md", SourceFixture.Md(renderer.Header.ToJsonString(), renderer.Body));

            var check = Check(files);

            Assert.True(check.Refusals.Count == 0, renderer.Id + ": " + Said(check.Refusals));
            Assert.Empty(Body(check, "renderers/copy.md").Renders);
        }
    }
}
