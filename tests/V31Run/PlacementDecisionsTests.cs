using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 8.2, 9; part 08 § 5.2; D2, D3): the runner decides each placement of an
/// agent's body from its <c>placements</c> alone, in order, once its gate held: a unit is decided only when the unit that
/// places it is placed; each decision is recorded under the placing row's key, its member the placement's site; a
/// placement with no rule is placed with no decision; each rule reads its own place (per item inside a loop); a rule
/// that fails fails the node; and the results are the call's <see cref="AgentCall.Placed"/>. The host is scripted
/// (<see cref="FakeRunHost"/>): nothing here composes. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PlacementDecisionsTests
{
    internal const string PartIf = "{ \"!\": [ { \"blank\": [ { \"var\": \"node.classify.value.reason\" } ] } ] }";
    internal const string FolderIf = "{ \"in\": [ \"says so\", { \"var\": \"node.classify.value.reason\" } ] }";
    internal const string MacroIf = "{ \"===\": [ { \"var\": \"node.classify.value.answer\" }, true ] }";
    internal const string SnippetIf = "{ \"!==\": [ { \"var\": \"node.classify.value.answer\" }, null ] }";

    internal const string Sections = """{"sections":[{"title":"History","kind":"narrative"},{"title":"Plan","kind":"table"}]}""";

    /// <summary>
    /// The classifier <c>classify</c> (a <c>judgement</c> on the plan) and the agent <c>draft</c>, whose part <c>p</c>
    /// (decided on the classifier's reason) places the macro <c>m</c> of the folder <c>f</c> (the folder decided on the
    /// reason's words, the macro on the answer), whose body places the snippet <c>s</c> (decided on the answer).
    /// </summary>
    internal static Dictionary<string, byte[]> Decided() =>
        Package()
            .WithAgent("classify", "Does the plan for Margaret Okafor need a follow-up?\n\n{{ node.plan.content }}\n", "\"judgement\"")
            .Listed("snippets/index.json", "s")
            .With("snippets/s.md", Md(
                $"{{ \"label\": \"When\", \"description\": null, \"if\": {SnippetIf}, \"format\": \"plaintext\" }}", "in six weeks"))
            .Listed("macros/index.json", "f")
            .With("macros/f/index.json", Index(["m"], extra: $", \"if\": {FolderIf}"))
            .With("macros/f/m.md", Md(
                $"{{ \"label\": \"Follow-up\", \"description\": null, \"if\": {MacroIf}, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place the follow-up line alone after the plan:\", \"{{~ macro.f.m.content }}\"] }",
                "Follow up with Dr A. Sample {{ snippet.s.content }}.\n"))
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ part.p }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse(
                $"{{ \"p\": {{ \"if\": {PartIf}, \"text\": [\"Close the plan.\", \"{{{{~ macro.f.m }}}}\"] }} }}"));

    /// <summary>A host answering the plan with <see cref="Sections"/>, each classifier with <paramref name="judgement"/> and every other agent with its stub.</summary>
    internal static FakeRunHost Host(RunPlan plan, Func<AgentCall, string> judgement) =>
        new FakeRunHost(auto: true).Stub(call =>
            call.EntryKey == "node.plan" ? Sections
            : call.EntryKey.EndsWith(".classify", StringComparison.Ordinal) ? judgement(call)
            : RunFixture.StubReply(plan, call));

    internal static async Task<(RunResult Result, FakeRunHost Host, ListRecorder Recorder)> RunAsync(
        RunPlan plan, FakeRunHost host, RunInputs? inputs = null, RunLimits? limits = null)
    {
        var recorder = new ListRecorder(() => host.Calls.Count);
        var result = await new PlanRunner(plan, limits ?? new RunLimits(), host, recorder).RunAsync(inputs ?? PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        return (result, host, recorder);
    }

    /// <summary>The decisions recorded for the placements of <paramref name="site"/>'s row: <c>(member, printed, result)</c>.</summary>
    internal static List<(string Member, string Printed, bool Result)> Placements(ListRecorder recorder, string site) =>
        recorder.Decisions.Where(d => d.Site == site && d.Member != "self").Select(d => (d.Member, d.Printed, d.Result == true)).ToList();

    private static string Printed(string rule) => JsonLogicPrinter.Print(JsonNode.Parse(rule));

    private static IReadOnlyDictionary<string, bool> PlacedOf(FakeRunHost host, string key) => host.Calls.Single(c => c.Key == key).Placed!;

    [Fact]
    public async Task EachPlacement_IsDecidedInOrder_AndRecorded()
    {
        var plan = RunFixture.Plan(Decided());
        var (result, host, recorder) = await RunAsync(plan, Host(plan, _ => ComposingRunHost.Judgement(true)));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Equal(
            [
                ("part.p", Printed(PartIf), true),
                ("macro-folder.f", Printed(FolderIf), true),
                ("macro.f.m", Printed(MacroIf), true),
                ("snippet.s", Printed(SnippetIf), true),
            ],
            Placements(recorder, "node.draft"));
        Assert.All(recorder.Decisions.Where(d => d.Site == "node.draft"), d => Assert.Equal(FakeRunHost.Start, d.DecidedAtUtc));
        Assert.Equal(
            new Dictionary<string, bool> { ["part.p"] = true, ["macro-folder.f"] = true, ["macro.f.m"] = true, ["snippet.s"] = true },
            PlacedOf(host, "node.draft"));

        // An entry that places nothing is called with nothing placed, and decides nothing.
        Assert.Empty(PlacedOf(host, "node.plan"));
        Assert.Empty(Placements(recorder, "node.plan"));
    }

    [Fact]
    public async Task AUnitInsideAnUnplacedUnit_IsNotDecided()
    {
        var plan = RunFixture.Plan(Decided());

        // The part does not hold: nothing it places is decided.
        var (_, partHost, partRecorder) = await RunAsync(plan, Host(plan, _ => """{"answer":true,"reason":""}"""));
        Assert.Equal([("part.p", Printed(PartIf), false)], Placements(partRecorder, "node.draft"));
        Assert.Equal(new Dictionary<string, bool> { ["part.p"] = false }, PlacedOf(partHost, "node.draft"));

        // The folder does not hold: its macro is decided (the record shows which was false), the macro's body's snippet is not.
        var (_, folderHost, folderRecorder) = await RunAsync(plan, Host(plan, _ => """{"answer":true,"reason":"Another reason."}"""));
        Assert.Equal(
            [("part.p", Printed(PartIf), true), ("macro-folder.f", Printed(FolderIf), false), ("macro.f.m", Printed(MacroIf), true)],
            Placements(folderRecorder, "node.draft"));
        Assert.Equal(
            new Dictionary<string, bool> { ["part.p"] = true, ["macro-folder.f"] = false, ["macro.f.m"] = true },
            PlacedOf(folderHost, "node.draft"));

        // The macro does not hold: its body's snippet is not decided.
        var (_, macroHost, macroRecorder) = await RunAsync(plan, Host(plan, _ => """{"answer":false,"reason":"The plan says so."}"""));
        Assert.Equal(["part.p", "macro-folder.f", "macro.f.m"], Placements(macroRecorder, "node.draft").Select(d => d.Member));
        Assert.False(PlacedOf(macroHost, "node.draft")["macro.f.m"]);
        Assert.False(PlacedOf(macroHost, "node.draft").ContainsKey("snippet.s"));
    }

    [Fact]
    public async Task ANullIf_IsPlacedWithNoDecision()
    {
        var plan = RunFixture.Plan(Package()
            .WithSnippet("clinician", "Dr A. Sample")
            .WithMacro("m", "Follow up with Dr A. Sample in six weeks.\n")
            .WithAgent("draft", "Draft the plan for Margaret Okafor with {{ snippet.clinician.content }}.\n{{ macro.m }}\n"));
        var (result, host, recorder) = await RunAsync(plan, Host(plan, _ => ComposingRunHost.Judgement(true)));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Equal(new Dictionary<string, bool> { ["snippet.clinician"] = true, ["macro.m"] = true }, PlacedOf(host, "node.draft"));
        Assert.Empty(recorder.Decisions);
    }

    /// <summary>
    /// Review Focus 4: the macro <c>m</c>, decided on <c>node.classify</c>, placed by the agent <c>draft</c> of two sibling
    /// loops over the plan's sections, <c>sections</c> and <c>extras</c>, each beside its own classifier <c>classify</c>
    /// (S01-002: an id repeats only where neither can see the other, so a top-level classifier cannot share it).
    /// </summary>
    internal static Dictionary<string, byte[]> TwoPlaces()
    {
        var files = Package()
            .WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["End with {{ macro.m.content }}"], condition: MacroIf);
        foreach (var loop in new[] { "sections", "extras" })
        {
            files.Listed("nodes/index.json", loop)
                .With($"nodes/{loop}/index.json", Json(SourceFixture.Node("for", $"Loop {loop}", ArrayOf(Plain), false, true,
                    "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                    + $"\"itemLabel\": \"title\", \"nodes\": [\"classify\", \"draft\"], \"ret\": \"node.{loop}.draft\"")))
                .With($"nodes/{loop}/classify.md", Agent("Classifying the section", "\"judgement\"",
                    "Does the section '{{ item.value.title }}' need a follow-up?\n"))
                .With($"nodes/{loop}/draft.md", Agent("Drafting the section", Plain, "Draft the section '{{ item.value.title }}'.\n{{ macro.m }}\n"));
        }

        return files;
    }

    [Fact]
    public async Task APlacementsIf_ReadsItsOwnPlace()
    {
        var plan = RunFixture.Plan(TwoPlaces());
        string[] yes = ["node.sections.classify:1", "node.extras.classify:2"];

        var (result, host, recorder) = await RunAsync(plan, Host(plan, call => ComposingRunHost.Judgement(yes.Contains(call.Key))));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        foreach (var loop in new[] { "sections", "extras" })
        {
            var printed = Printed($"{{ \"===\": [ {{ \"var\": \"node.{loop}.classify.value.answer\" }}, true ] }}");
            for (var i = 1; i <= 2; i++)
            {
                var draft = $"node.{loop}.draft:{i}";
                var classify = $"node.{loop}.classify:{i}";
                Assert.Equal([("macro.m", printed, yes.Contains(classify))], Placements(recorder, draft));
                Assert.Equal(yes.Contains(classify), PlacedOf(host, draft)["macro.m"]);

                // each placing instance is called after its own classifier settled
                var settled = recorder.Rows.FindIndex(r => r.Key == classify);
                Assert.True(host.Dispatched.IndexOf(draft) >= recorder.Stamps[settled], $"{draft} was called before {classify} settled.");
            }
        }
    }

    [Fact]
    public async Task APlacementsIf_IsCompiledWithItsPlainConverts()
    {
        // Task 7's carry-over: plain of a string literal does not convert (plainConverts [false]); a miswired list, read as
        // none, would convert "**urgent** today" to "urgent today", which holds no "**urgent**", and the macro would not be placed.
        // (The needle is a literal: in takes a string to find, and a node's plaintext is no string, A04-001, now that a
        // macro's if is checked where it is placed.)
        const string rule = "{ \"in\": [ \"**urgent**\", { \"plain\": [ \"**urgent** today\" ] } ] }";
        var plan = RunFixture.Plan(Package()
            .WithMacroOf("m", "Follow up with Dr A. Sample this week.\n", ["End with {{ macro.m.content }}"], condition: rule)
            .WithAgent("kind", "How urgent is the plan for Margaret Okafor?\n\n{{ node.plan.content }}\n")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.m }}\n"));
        Assert.Equal("[false]", plan.Entries["node.draft"].Json["placements"]![0]!["plainConverts"]!.ToJsonString());

        var host = Host(plan, _ => ComposingRunHost.Judgement(true)).Reply("node.kind", "**urgent**");
        var (result, _, recorder) = await RunAsync(plan, host);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Equal([true], Placements(recorder, "node.draft").Select(d => d.Result));
        Assert.True(PlacedOf(host, "node.draft")["macro.m"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ARenderedNodeAndADeliverable_DecideWhatTheyPlace_AndRenderOnlyThat(bool answer)
    {
        // Final review M2: a rendered node's and a deliverable's placements are decided as a prompt's are, recorded under
        // their own rows, and handed to the render (RenderCall.Placed), which writes a snippet only when it holds.
        const string text = "Follow up with Dr A. Sample in six weeks.";
        var plan = RunFixture.Plan(Package()
            .WithAgent("classify", "Does the plan for Margaret Okafor need a follow-up?\n\n{{ node.plan.content }}\n", "\"judgement\"")
            .Listed("snippets/index.json", "s")
            .With("snippets/s.md", Md($"{{ \"label\": \"When\", \"description\": null, \"if\": {MacroIf}, \"format\": \"plaintext\" }}", text))
            .WithRendered("summary", "Summary for Margaret Okafor.\n{{~ snippet.s.content }}\n")
            .WithDeliverable("{{ node.plan.content }}\n{{~ snippet.s.content }}\n"));
        var host = new ComposingRunHost(plan, (call, prompt) =>
            call.EntryKey == "node.plan" ? Sections
            : !prompt.TextReply ? ComposingRunHost.Judgement(answer)
            : ComposingRunHost.EchoMarkers(call, prompt));
        var recorder = new ListRecorder(() => host.Calls.Count);

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        foreach (var key in new[] { "node.summary", "deliverable.note" })
        {
            Assert.Equal(new Dictionary<string, bool> { ["snippet.s"] = answer }, host.Renders.Single(r => r.Key == key).Placed);
            Assert.Equal([("snippet.s", Printed(MacroIf), answer)], Placements(recorder, key));
            // #1505 (area 08, D7): the Markdown deliverable escapes the plain-text snippet it places, whole.
            var written = key == "deliverable.note" ? MarkdownEscape.Escape(text) : text;
            Assert.Equal(answer, SourceLayout.Text(result.Values[key]!["content"])!.Contains(written, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task AFailingIf_FailsTheNode()
    {
        var plan = RunFixture.Plan(Package()
            .WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["End with {{ macro.m.content }}"],
                condition: "{ \"matches\": [{ \"var\": \"input.consult_draft.content\" }, \"(.*a.{100}){3}b\"] }")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.m }}\n"), matchTimeLimit: TimeSpan.FromTicks(1));
        var text = new string('a', 1 << 20);
        var inputs = new RunInputs(
            new Dictionary<string, JsonNode?> { ["consult_draft"] = new JsonObject { ["value"] = text, ["content"] = text, ["title"] = null } },
            new JsonObject(),
            new Dictionary<string, string?>());

        var (result, host, recorder) = await RunAsync(plan, Host(plan, _ => ComposingRunHost.Judgement(true)), inputs);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.draft", result.FailedKey);
        Assert.Matches("^Macro 'm' if could not be decided: matches took longer than .+ seconds on this text\\. Use a simpler pattern\\.$", result.Sentence!);
        V31Contract.AssertSentence(new SourceRefusal("A06-001", result.Sentence!, ""));
        Assert.Equal(RowState.Failed, recorder.Row("node.draft").State);
        Assert.DoesNotContain("node.draft", host.Dispatched);
        Assert.Empty(Placements(recorder, "node.draft"));
    }
}
