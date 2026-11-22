using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1505 (specVersion 31, area 08; R6; part 09 § 3.1): a rule that holds <c>matches</c> or <c>has_word</c> has a time limit
/// on each evaluation (#1503 R2), so the runner asks its host to evaluate it (<see cref="IRunHost.EvaluateAsync"/>: in the
/// engine an activity, whose outcome a replay reads) and uses the host's answer; every other rule is evaluated inline, as
/// before. Wherever the runner decides a rule (a leaf's, a check's or a deliverable's <c>if</c>, a computed
/// <c>expression</c>, an <c>if</c> node, a <c>while</c> and its <c>until</c>, a <c>for</c>'s <c>until</c>, a placement's
/// <c>if</c>) it goes through the one seam. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PatternRulesTests
{
    private const string Plain = "\"plaintext\"";

    /// <summary>Holds on <see cref="PlanRunnerLeavesTests.Draft"/>, and holds a pattern.</summary>
    private const string Pattern = "{ \"matches\": [{ \"var\": \"input.consult_draft.content\" }, \"Okafor\"] }";

    /// <summary>Holds on <see cref="PlanRunnerLeavesTests.Draft"/>, and holds no pattern.</summary>
    private const string NoPattern = "{ \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }";

    /// <summary>Holds once a while's judge has given its verdict.</summary>
    private const string Judged = "{ \"!==\": [{ \"var\": \"node.judge.value.verdict\" }, null] }";

    /// <summary>A while's judge as its rules read it in pass 1: the stub's verdict.</summary>
    private const string Judge = "{\"refine\":{\"judge\":{\"value\":{\"verdict\":\"revise\"}}}}";

    public static TheoryData<string> Sites() => new(Cases.Keys);

    [Theory]
    [MemberData(nameof(Sites))]
    public async Task ARuleWithAPattern_IsDecidedByTheHost(string site)
    {
        var (files, key, member, node) = Cases[site];

        var plan = RunFixture.Plan(files(Pattern));
        var host = RunFixture.Stubbed(plan);
        var result = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var call = Assert.Single(host.Rules);
        Assert.Equal((key, key, (string?)null, member), (call.Key, call.EntryKey, call.Function, call.Member));
        // the data the rule is evaluated over, as inline: the entry's reads (the draft, and what else the entry reads)
        var scope = new JsonObject { ["input"] = new JsonObject { ["consult_draft"] = new JsonObject { ["content"] = PlanRunnerLeavesTests.Draft } } };
        if (node != null)
            scope["node"] = JsonNode.Parse(node);
        Assert.Equal(scope.ToJsonString(), call.Scope.ToJsonString());

        // the same rule with no pattern is evaluated inline: no call
        var inline = RunFixture.Plan(files(NoPattern));
        var inlineHost = RunFixture.Stubbed(inline);
        var inlineResult = await new PlanRunner(inline, new RunLimits(), inlineHost, new ListRecorder()).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        Assert.True(inlineResult.State == RunState.Completed, inlineResult.Sentence);
        Assert.Empty(inlineHost.Rules);
    }

    [Fact]
    public async Task TheHostsAnswer_IsWhatTheRunUses()
    {
        var plan = RunFixture.Plan(GatedPlan(Pattern));

        // inline the rule holds; the host says it does not, and the plan is skipped
        var host = new FakeRunHost(auto: true, plan).Rule("node.plan", "if", new RuleOutcome(JsonValue.Create(false), null, null));
        var recorder = new ListRecorder();
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(RowState.Skipped, recorder.Row("node.plan").State);
        Assert.Equal([false], recorder.Decisions.Where(d => d.Owner == "node.plan").Select(d => d.Result));
        Assert.Empty(host.Calls);

        // a time-out the host met fails the node with A06-001, its operation and limit as the host gave them
        host = new FakeRunHost(auto: true, plan).Rule("node.plan", "if", new RuleOutcome(null, "MatchTooSlow", 2, "matches"));
        recorder = new ListRecorder();
        result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.plan", result.FailedKey);
        Assert.Equal("Node 'plan' if could not be decided: matches took longer than 2 seconds on this text. Use a simpler pattern.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A06-001", result.Sentence!, ""));
        Assert.Equal(RowState.Failed, recorder.Row("node.plan").State);
        Assert.Empty(host.Calls);

        // a host whose plan is not the job's: S07-006, as for an agent call
        host = new FakeRunHost(auto: true, plan).Rule("node.plan", "if", new RuleOutcome(null, RuleTurn.PlanFailure, null));
        result = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal(ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence, result.Sentence);
    }

    [Fact]
    public async Task AGateTheHostCouldNotDecide_BlocksItsDeliverable_AndTheJobCompletes()
    {
        // D4, as for a gate decided inline: the gate's if failed at run time, so the gate settles failed with its sentence,
        // the job does not, and the note is blocked with S08-008.
        var plan = RunFixture.Plan(Minimal()
            .With("checks/index.json", Index(["all_sections"]))
            .With("checks/all_sections.md", Md(SourceFixture.Node("computed", "Every section covers its planned terms", "\"check_result\"", true, false,
                $"\"if\": {Pattern}, \"expression\": {{ \"object\": {{ \"pass\": true, \"message\": null, \"uncovered\": null, \"untested\": null, \"passed\": null, \"total\": null }} }}"), ""))
            .With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "\"check.all_sections\"", "hold"), "{{ node.plan.content }}\n")));
        var host = new FakeRunHost(auto: true, plan).Rule("check.all_sections", "if", new RuleOutcome(null, "MatchTooSlow", 2, "matches"));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Null(result.FailedKey);
        var gate = recorder.Row("check.all_sections");
        Assert.Equal(RowState.Failed, gate.State);
        Assert.Equal("Check 'all_sections' if could not be decided: matches took longer than 2 seconds on this text. Use a simpler pattern.", gate.Reason);
        var row = recorder.Row("deliverable.note");
        Assert.Equal(RowState.Blocked, row.State);
        Assert.Equal("blocked: check 'all_sections' could not run (Check 'all_sections' if could not be decided: matches took longer than 2 seconds "
            + "on this text. Use a simpler pattern)", row.Reason);
        V31Contract.AssertSentence(new SourceRefusal("S08-008", row.Reason!, ""));
        Assert.Equal(DeliverableState.Blocked, Assert.Single(result.Deliverables).State);
    }

    [Fact]
    public void ReadsPattern_IsAnyMatchesOrHasWord_AtAnyDepth()
    {
        var evaluator = new CorvusJsonLogicEvaluator();
        bool Reads(string rule) => evaluator.Compile(JsonNode.Parse(rule)).ReadsPattern;

        Assert.True(Reads(Pattern));
        Assert.True(Reads("{ \"and\": [true, { \"!\": [{ \"has_word\": [{ \"var\": \"input.consult_draft.content\" }, \"TSH\"] }] }] }"));
        Assert.True(Reads("{ \"object\": { \"pass\": { \"matches\": [{ \"var\": \"input.consult_draft.content\" }, \"a\"] }, \"message\": null } }"));
        Assert.False(Reads(NoPattern));
        Assert.False(Reads("{ \"var\": \"node.matches.value\" }"));
        Assert.False(Reads("{ \"object\": { \"matches\": true, \"has_word\": null } }"));
    }

    // ---- fixtures -----------------------------------------------------------------------------

    /// <summary>
    /// Each site the runner decides a rule at: the package with the rule given there, and the call it makes for it (the
    /// row's key, which is the entry's at the top level, the member, and what its scope holds under <c>node</c> beside the
    /// draft, from the entry's other reads; null for nothing).
    /// </summary>
    private static readonly Dictionary<string, (Func<string, Dictionary<string, byte[]>> Files, string Key, string Member, string? Node)> Cases = new()
    {
        ["a leaf's if"] = (GatedPlan, "node.plan", "if", null),
        ["a deliverable's if"] = (rule => Minimal().WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse(rule)), "deliverable.note", "if",
            "{\"plan\":{\"content\":\"Stub reply for node.plan (4a09f874).\"}}"),
        ["a computed expression"] = (rule => Minimal()
            .With("nodes/index.json", Index(["plan", "named"]))
            .With("nodes/named.md", Md(SourceFixture.Node("computed", "Naming the patient", "\"boolean\"", true, false,
                $"\"if\": null, \"expression\": {rule}"), "")), "node.named", "expression", null),
        ["an if node"] = (rule => Minimal()
            .With("nodes/index.json", Index(["plan", "letter"]))
            .With("nodes/letter/index.json", Json(SourceFixture.Node("if", "Writing the letter", Plain, false, true,
                $"\"if\": {rule}, \"then\": {{ \"nodes\": [\"to_gp\"], \"ret\": \"node.letter.to_gp\" }}, \"else\": null")))
            .With("nodes/letter/to_gp.md", Agent("Writing to the GP", Plain, "Write to Dr A. Sample about Margaret Okafor.\n")), "node.letter", "if", null),
        // a while's rules read its judge, as publish requires of a body node not returned (S04-035)
        ["a while"] = (rule => ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", j => j["while"] = JsonNode.Parse($"{{ \"and\": [{Judged}, {{ \"!\": [{rule}] }}] }}")),
            "node.refine", "while", Judge),
        ["a while's until"] = (rule => ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", j =>
            {
                j["while"] = JsonNode.Parse(Judged);
                j["until"] = JsonNode.Parse($"{{ \"and\": [{Judged}, {rule}] }}");
            }), "node.refine", "until", Judge),
        ["a for's until"] = (rule => ResolveFixture.NestedNote()
            .WithJson("nodes/sections/index.json", j =>
            {
                j["parallel"] = false;
                j["until"] = JsonNode.Parse(rule);
            }), "node.sections", "until", "{\"plan\":{\"value\":{\"sections\":[{\"kind\":\"narrative\",\"title\":\"stub 117e5730\"}]}}}"),
        ["a placement's if"] = (rule => Package()
            .WithMacroOf("m", "Follow up with Dr A. Sample in six weeks.\n", ["End with {{ macro.m.content }}"], condition: rule)
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.m }}\n"), "node.draft", "macro.m", null),
    };

    /// <summary><see cref="SourceFixture.Minimal"/>'s <c>plan</c> with the <c>if</c> <paramref name="rule"/>.</summary>
    private static Dictionary<string, byte[]> GatedPlan(string rule) => Minimal().With("nodes/plan.md", Md(
        SourceFixture.Node("agent", "Planning the sections", Plain, false, true,
            $"\"if\": {rule}, \"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
        "Plan the sections of a consultation note for the draft below.\n\n{{ input.consult_draft.content }}\n"));
}
