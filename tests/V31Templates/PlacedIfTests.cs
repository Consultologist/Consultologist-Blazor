using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 6.3 R-R3, 8.2; part 04 § 7.10; Review Focus 4): a placed snippet's or
/// macro's header <c>if</c> is judged where it is placed, at each placer's frame, through nested placements: a read the
/// place does not give is refused at publish (S05-022 for a snippet, S03-014 or S04-063 for a macro), and a circle
/// through a placed unit's <c>if</c> (reading the node that places it, or a node that reads it, directly or through
/// others) is A05-003, as <see cref="RunPlan"/> would refuse it at load. A valid <c>if</c> at each kind of place, and
/// the same shapes without the back-read, publish, build a <see cref="RunPlan"/> and are decided on their place's keys.
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PlacedIfTests
{
    private const string Nope = "{ \"!==\": [ { \"var\": \"node.nope.value\" }, null ] }";

    /// <summary>The snippet <paramref name="id"/> decided by <paramref name="condition"/>.</summary>
    private static Dictionary<string, byte[]> WithRuledSnippet(Dictionary<string, byte[]> files, string id, string condition, string body = "in six weeks") =>
        files.Listed("snippets/index.json", id).With($"snippets/{id}.md", Md(
            $"{{ \"label\": \"Snippet {id}\", \"description\": null, \"if\": {condition}, \"format\": \"plaintext\" }}", body));

    /// <summary>
    /// The function <c>section</c> (its own classifier <c>classify</c> and agent <c>draft</c>, whose prompt places the
    /// package snippet <c>s</c>, listed in <c>uses</c>), called by the package node <c>first</c>.
    /// </summary>
    private static Dictionary<string, byte[]> InAFunction(Dictionary<string, byte[]> files) =>
        files.Listed("nodes/index.json", "first")
            .With("nodes/first.md", Md(SourceFixture.Node("call", "First section", Plain, false, true,
                "\"if\": null, \"function\": \"section\", \"with\": { \"draft\": \"input.consult_draft.content\" }"), ""))
            .With("functions/index.json", Index(["section"]))
            .With("functions/section/index.json", Json(
                "{ \"label\": \"Draft a section\", \"description\": null, "
                + "\"inputs\": { \"draft\": { \"label\": \"Consult draft\", \"description\": null, \"format\": \"markdown_block\" } }, "
                + "\"uses\": { \"snippets\": [\"s\"], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + "\"nodes\": [\"classify\", \"draft\"], \"ret\": \"node.draft\", \"format\": \"plaintext\" }"))
            .With("functions/section/classify.md", Agent("Classifying the section", "\"judgement\"",
                "Does this draft need a follow-up?\n\n{{ param.draft.content }}\n"))
            .With("functions/section/draft.md", Agent("Drafting the section", Plain,
                "Draft the section for Margaret Okafor.\n{{~ snippet.s.content }}\n"))
            .With("functions/section/snippets/index.json", Index([]))
            .With("functions/section/macros/index.json", Index([]))
            .With("functions/section/formats/index.json", Index([]))
            .With("functions/section/renderers/index.json", Index([]));

    private static Dictionary<string, byte[]> Case(string name) => name switch
    {
        "a snippet at the top level" => WithRuledSnippet(Package(), "s", Nope)
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ snippet.s.content }}\n"),
        "a snippet through a nested snippet" => WithRuledSnippet(Package(), "s", Nope)
            .WithSnippet("outer", "Follow up {{ snippet.s.content }}.")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ snippet.outer.content }}\n"),
        "a macro" => Package()
            .WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["End with {{ macro.m.content }}"], condition: Nope)
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.m }}\n"),
        "a macro folder" => Package()
            .Listed("macros/index.json", "f")
            .With("macros/f/index.json", Index(["m"], extra: $", \"if\": {Nope}"))
            .With("macros/f/m.md", Md(
                "{ \"label\": \"Macro m\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                + "\"instruction\": [\"End with {{ macro.f.m.content }}\"] }", "Follow up with Dr A. Sample in six weeks.\n"))
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.f.m }}\n"),
        "a package snippet in a function" => InAFunction(WithRuledSnippet(Package(), "s",
            "{ \"!==\": [ { \"var\": \"node.plan.value\" }, null ] }")),
        "a snippet reading item outside a loop" => WithRuledSnippet(Package(), "s",
                "{ \"===\": [ { \"var\": \"item.value.title\" }, \"Plan\" ] }")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ snippet.s.content }}\n"),
        "a snippet reading its placing node" => WithRuledSnippet(Package(), "s",
                "{ \"!\": [ { \"blank\": [ { \"var\": \"node.draft.content\" } ] } ] }")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ snippet.s.content }}\n"),
        "a macro reading its placing node" => Package()
            .WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["End with {{ macro.m.content }}"],
                condition: "{ \"!\": [ { \"blank\": [ { \"var\": \"node.draft.content\" } ] } ] }")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.m }}\n"),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [InlineData("a snippet at the top level", "S05-022", "nodes/draft.md",
        "Snippet 's' reads node.nope.value, which isn't available where Node 'draft' places it at line 2.")]
    [InlineData("a snippet through a nested snippet", "S05-022", "snippets/outer.md",
        "Snippet 's' reads node.nope.value, which isn't available where Snippet 'outer' places it at line 1.")]
    [InlineData("a macro", "S03-014", "macros/m.md", "Macro 'm' reads node.nope.value, which doesn't exist here.")]
    [InlineData("a macro folder", "S03-014", "macros/f/index.json", "Macro 'f' reads node.nope.value, which doesn't exist here.")]
    [InlineData("a package snippet in a function", "S05-022", "functions/section/draft.md",
        "Snippet 's' reads node.plan.value, which isn't available where Node 'draft' places it at line 2.")]
    [InlineData("a snippet reading item outside a loop", "S05-022", "nodes/draft.md",
        "Snippet 's' reads item.value.title, which isn't available where Node 'draft' places it at line 2.")]
    [InlineData("a snippet reading its placing node", "A05-003", "nodes/draft.md",
        "Node 'draft' reads node 'draft', which reads node 'draft': nodes can't read each other in a circle. Break the circle.")]
    [InlineData("a macro reading its placing node", "A05-003", "nodes/draft.md",
        "Node 'draft' reads node 'draft', which reads node 'draft': nodes can't read each other in a circle. Break the circle.")]
    public void AUnitsIf_IsJudgedWhereItIsPlaced(string name, string id, string path, string sentence)
    {
        var (refusals, compiled) = CompiledPackage.Publish(Case(name), ResolveFixture.Published(), CompiledGoldenTests.Fixed);

        Assert.Null(compiled);
        var refusal = Assert.Single(refusals);
        V31Contract.AssertSentence(refusal);
        Assert.Equal((id, path, sentence), (refusal.Id, refusal.Path, refusal.Sentence));
    }

    [Fact]
    public void AMacrosIf_InAFunction_IsStillS04063()
    {
        // Part 04 § 7.10: the function's own wording when a macro's if reads a node the function has no node of.
        var files = InAFunction(WithRuledSnippet(Package(), "s", "null"))
            .Listed("macros/index.json", "m")
            .With("macros/m.md", Md(
                "{ \"label\": \"Macro m\", \"description\": null, \"if\": { \"!==\": [ { \"var\": \"node.plan.value\" }, null ] }, "
                + "\"format\": \"plaintext\", \"instruction\": [\"End with {{ macro.m.content }}\"] }",
                "Follow up with Dr A. Sample in six weeks.\n"))
            .WithJson("functions/section/index.json", f => f["uses"]!["macros"]!.AsArray().Add((JsonNode)"m"))
            .With("functions/section/draft.md", Agent("Drafting the section", Plain,
                "Draft the section for Margaret Okafor.\n{{~ snippet.s.content }}\n{{ macro.m }}\n"));

        var (refusals, _) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);

        var refusal = Assert.Single(refusals);
        V31Contract.AssertSentence(refusal);
        Assert.Equal(("S04-063", "functions/section/draft.md"), (refusal.Id, refusal.Path));
    }

    private const string Answer = "{ \"===\": [ { \"var\": \"node.classify.value.answer\" }, true ] }";

    /// <summary>A valid snippet <c>if</c> on <c>node.classify</c> at each kind of place, with the key its place roots it to and the row that decides it.</summary>
    public static TheoryData<string, string, string> ValidPlaces() => new()
    {
        { "top level", "node.classify", "node.draft" },
        { "loop body", "node.sections.classify", "node.sections.draft:1" },
        { "function", "node.classify", "node.first.draft" },
        { "nested snippet", "node.sections.classify", "node.sections.draft:1" },
    };

    private static Dictionary<string, byte[]> Valid(string place)
    {
        var files = WithRuledSnippet(Package(), "s", Answer);
        Dictionary<string, byte[]> Loop(string placing) =>
            files.Listed("nodes/index.json", "sections")
                .With("nodes/sections/index.json", Json(SourceFixture.Node("for", "Sections", ArrayOf(Plain), false, true,
                    "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                    + "\"itemLabel\": \"title\", \"nodes\": [\"classify\", \"draft\"], \"ret\": \"node.sections.draft\"")))
                .With("nodes/sections/classify.md", Agent("Classifying the section", "\"judgement\"",
                    "Does the section '{{ item.value.title }}' need a follow-up?\n"))
                .With("nodes/sections/draft.md", Agent("Drafting the section", Plain,
                    $"Draft the section '{{{{ item.value.title }}}}'.\n{{{{~ {placing} }}}}\n"));
        return place switch
        {
            "top level" => files
                .WithAgent("classify", "Does the plan for Margaret Okafor need a follow-up?\n\n{{ node.plan.content }}\n", "\"judgement\"")
                .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ snippet.s.content }}\n"),
            "loop body" => Loop("snippet.s.content"),
            "function" => InAFunction(files),
            _ => Loop("snippet.outer.content").WithSnippet("outer", "Follow up {{ snippet.s.content }}."),
        };
    }

    [Theory]
    [MemberData(nameof(ValidPlaces))]
    public async Task AValidUnitsIf_AtEachPlace_PublishesAndIsDecidedOnItsPlacesKeys(string place, string key, string row)
    {
        var plan = RunFixture.Plan(Valid(place));
        var entries = place == "function" ? plan.Functions["section"].Entries : plan.Entries;
        var draft = entries.Values.Single(e => e.Id == "draft");
        var placement = draft.Json["placements"]!.AsArray().Single(p => (string)p!["site"]! == "snippet.s")!;
        Assert.Equal($"{{\"===\":[{{\"var\":\"{key}.value.answer\"}},true]}}", placement["if"]!.ToJsonString());

        var host = PlacementDecisionsTests.Host(plan, _ => ComposingRunHost.Judgement(true));
        var (result, _, recorder) = await PlacementDecisionsTests.RunAsync(plan, host);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var decision = Assert.Single(recorder.Decisions, d => d.Site == row && d.Member == "snippet.s");
        Assert.True(decision.Result);
        Assert.Contains(key, decision.Printed, StringComparison.Ordinal);
    }
    // ---- A longer circle through a placed unit's if ----------------------------------------------

    /// <summary>
    /// <c>classify</c> reads <paramref name="classifyReads"/> (<c>node.draft</c>: the circle; <c>node.plan</c>: none), and
    /// <c>draft</c> places what <paramref name="unit"/> names, whose <c>if</c> reads <c>node.classify.value.answer</c>.
    /// </summary>
    private static Dictionary<string, byte[]> Longer(string unit, string classifyReads = "node.draft")
    {
        var files = Package()
            .WithAgent("classify", $"Does the draft for Margaret Okafor need a follow-up?\n\n{{{{ {classifyReads}.content }}}}\n", "\"judgement\"");
        return unit switch
        {
            "snippet" => WithRuledSnippet(files, "s", Answer)
                .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ snippet.s.content }}\n"),
            "macro" => files
                .WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["End with {{ macro.m.content }}"], condition: Answer)
                .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.m }}\n"),
            "macro folder" => files
                .Listed("macros/index.json", "f")
                .With("macros/f/index.json", Index(["m"], extra: $", \"if\": {Answer}"))
                .With("macros/f/m.md", Md(
                    "{ \"label\": \"Macro m\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                    + "\"instruction\": [\"End with {{ macro.f.m.content }}\"] }", "Follow up with Dr A. Sample in six weeks.\n"))
                .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.f.m }}\n"),
            "part" => files
                .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ part.brief }}\n")
                .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse(
                    $"{{ \"brief\": {{ \"if\": {Answer}, \"text\": [\"Be brief.\"] }} }}")),
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };
    }

    /// <summary>
    /// The three-node circle: <c>classify</c> reads <c>review</c>, <c>review</c> reads <c>draft</c>, and <c>draft</c>
    /// places <c>outer</c>, which places <c>s</c>, whose <c>if</c> reads <c>classify</c> (none when
    /// <paramref name="classifyReads"/> is <c>node.plan</c>).
    /// </summary>
    private static Dictionary<string, byte[]> ThroughANestedSnippet(string classifyReads = "node.review") =>
        WithRuledSnippet(Package(), "s", Answer)
            .WithSnippet("outer", "Follow up {{ snippet.s.content }}.")
            .WithAgent("classify", $"Does the review for Margaret Okafor need a follow-up?\n\n{{{{ {classifyReads}.content }}}}\n", "\"judgement\"")
            .WithAgent("review", "Review the draft for Margaret Okafor.\n\n{{ node.draft.content }}\n")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ snippet.outer.content }}\n");

    private const string Circle = ": nodes can't read each other in a circle. Break the circle.";

    [Theory]
    [InlineData("snippet")]
    [InlineData("macro")]
    [InlineData("macro folder")]
    [InlineData("part")]
    public void APlacedUnitsIf_ReadingANodeThatReadsItsPlacer_IsACircle(string unit)
    {
        var (refusals, compiled) = CompiledPackage.Publish(Longer(unit), ResolveFixture.Published(), CompiledGoldenTests.Fixed);

        Assert.Null(compiled);
        var refusal = Assert.Single(refusals);
        V31Contract.AssertSentence(refusal);
        Assert.Equal(("A05-003", "nodes/classify.md", $"Node 'classify' reads node 'draft', which reads node 'classify'{Circle}"),
            (refusal.Id, refusal.Path, refusal.Sentence));
    }

    [Fact]
    public void ANestedSnippetsIf_InAThreeNodeCircle_IsACircle()
    {
        var (refusals, compiled) = CompiledPackage.Publish(ThroughANestedSnippet(), ResolveFixture.Published(), CompiledGoldenTests.Fixed);

        Assert.Null(compiled);
        var refusal = Assert.Single(refusals);
        V31Contract.AssertSentence(refusal);
        Assert.Equal(("A05-003", "nodes/classify.md",
                $"Node 'classify' reads node 'review', which reads node 'draft', which reads node 'classify'{Circle}"),
            (refusal.Id, refusal.Path, refusal.Sentence));
    }

    [Theory]
    [InlineData("snippet")]
    [InlineData("macro")]
    [InlineData("macro folder")]
    [InlineData("part")]
    [InlineData("nested snippet")]
    public void TheSameShape_WithoutTheBackRead_PublishesAndBuildsARunPlan(string unit)
    {
        var plan = RunFixture.Plan(unit == "nested snippet" ? ThroughANestedSnippet("node.plan") : Longer(unit, "node.plan"));

        var draft = plan.Entries.Values.Single(e => e.Id == "draft");
        Assert.Contains("node.classify", draft.Depends);
    }
}
