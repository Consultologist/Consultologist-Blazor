using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 04 §§ 3, 5; part 03 § 6.1; part 08 § 5): <see cref="PlanRunner"/> on <c>while</c>,
/// <c>if</c> and <c>case</c>. A while runs its passes one after another, each in a frame of its own keyed <c>#n</c>, until
/// its check stops holding, its <c>until</c> holds or it reaches its bound; an <c>if</c> and a <c>case</c> decide when
/// what they read has settled and open the body they chose; a node in a body not taken has no row; every decision is
/// recorded once. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PlanRunnerFlowTests
{
    private const string Plain = PlanRunnerLeavesTests.Plain;

    // The verdict format's enum is good and revise (ResolveFixture): revise is the verdict that asks for another pass.
    private const string Revise = """{"verdict":"revise"}""";

    private const string Good = """{"verdict":"good"}""";

    // ---- while ------------------------------------------------------------------------------------

    [Fact]
    public async Task AWhile_RunsUntilItsCheckStopsHolding()
    {
        var plan = RunFixture.Plan("nested-note");
        var host = NestedHost().Reply("node.refine.judge#1", Revise).Reply("node.refine.judge#2", Good);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var refine = host.Dispatched.Where(k => k.StartsWith("node.refine.", StringComparison.Ordinal)).ToList();
        Assert.Equal(["node.refine.draft_summary#1", "node.refine.judge#1", "node.refine.draft_summary#2", "node.refine.judge#2"], refine);

        var read = result.Values["node.refine"]!;
        Assert.Equal("Text of node.refine.draft_summary#2.", (string?)read["value"]);
        Assert.Equal("Text of node.refine.draft_summary#2.", (string?)read["content"]);
        Assert.Equal("completed", (string?)read["state"]);
        Assert.Equal(2, (int)read["passes"]!);
        Assert.Equal("check", (string?)read["stopped"]);
        var history = read["history"]!.AsArray();
        Assert.Equal([1, 2], history.Select(p => (int)p!["number"]!));
        Assert.Equal("revise", (string?)history[0]!["node"]!["judge"]!["value"]!["verdict"]);
        Assert.Equal("Text of node.refine.draft_summary#1.", (string?)history[0]!["node"]!["draft_summary"]!["content"]);

        // the while's row, and a row per pass
        var row = recorder.Row("node.refine");
        Assert.Equal(RowState.Completed, row.State);
        Assert.Equal(PlanKind.While, row.Kind);
        Assert.Equal(2, (int)row.Detail["passes"]!);
        Assert.Equal("check", (string?)row.Detail["stopped"]);
        Assert.Equal(3, (int)row.Detail["max"]!);
        Assert.False((bool)row.Detail["checkFirst"]!);
        Assert.Null(row.Detail["reason"]);
        Assert.Equal(CanonicalJson.HashText("Text of node.refine.draft_summary#2."), row.ValueHash);
        Assert.Equal(RowState.Completed, recorder.Row("node.refine#1").State);
        Assert.Equal(1, (int)recorder.Row("node.refine#1").Detail["pass"]!);
        Assert.Equal(RowState.Completed, recorder.Row("node.refine#2").State);
        Assert.Equal(RowState.Completed, recorder.Row("node.refine.judge#2").State);

        // each decision with its pass
        var decisions = recorder.Decisions.Where(d => d.Site == "node.refine").ToList();
        Assert.Equal(["pass.1", "pass.2"], decisions.Select(d => d.Member));
        Assert.Equal([true, false], decisions.Select(d => d.Result));
        Assert.All(decisions, d => Assert.Equal("node.refine.judge.value.verdict ≠ \"good\"", d.Printed));

        // the deliverable reads the last pass
        var render = host.Renders.Single(r => r.Key == "deliverable.note");
        Assert.Equal("""{"node":{"refine":{"content":"Text of node.refine.draft_summary#2."}}}""", render.Scope.ToJsonString());
    }

    [Fact]
    public async Task AWhile_AtItsBound_ContinuesWithTheLastPass()
    {
        var plan = RunFixture.Plan("nested-note");
        var host = NestedHost().Reply("node.refine.judge", Revise);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(3, host.Dispatched.Count(k => k.StartsWith("node.refine.judge#", StringComparison.Ordinal)));
        var read = result.Values["node.refine"]!;
        Assert.Equal("bound", (string?)read["stopped"]);
        Assert.Equal(3, (int)read["passes"]!);
        Assert.Equal("Text of node.refine.draft_summary#3.", (string?)read["value"]);

        var row = recorder.Row("node.refine");
        Assert.Equal(RowState.Completed, row.State);
        Assert.Equal("bound", (string?)row.Detail["stopped"]);
        var reason = (string)row.Detail["reason"]!;
        Assert.Equal("stopped at its bound of 3; the check never stopped holding", reason);
        V31Contract.AssertSentence(new SourceRefusal("S04-005", reason, ""));
        Assert.Equal([true, true, true], recorder.Decisions.Where(d => d.Site == "node.refine").Select(d => d.Result));
        Assert.Equal(RowState.Composed, recorder.Row("deliverable.note").State);
    }

    [Fact]
    public async Task ACheckFirstWhile_ThatFailsAtOnce_RunsNoPass()
    {
        var files = WithBilling(DraftOnly())
            .WithJson("nodes/refine/index.json", j =>
            {
                j["checkFirst"] = true;
                j["while"] = JsonNode.Parse("""{ "===": [{ "var": "input.include_billing.value" }, true] }""");
            });
        var plan = RunFixture.Plan(files, ResolveFixture.NestedNoteReads());
        var host = NestedHost();
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(includeBilling: false), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.DoesNotContain(host.Dispatched, k => k.StartsWith("node.refine.", StringComparison.Ordinal));
        var read = result.Values["node.refine"]!;
        Assert.Equal("skipped", (string?)read["state"]);
        Assert.Null(read["value"]);
        Assert.Null(read["content"]);
        Assert.Equal(0, (int)read["passes"]!);
        Assert.Equal("check", (string?)read["stopped"]);

        var row = recorder.Row("node.refine");
        Assert.Equal(RowState.Skipped, row.State);
        Assert.Equal("skipped: input.include_billing.value = true is false", row.Reason);
        Assert.Equal(0, (int)row.Detail["passes"]!);
        Assert.DoesNotContain(recorder.Rows, r => r.Key.StartsWith("node.refine#", StringComparison.Ordinal) || r.Key.StartsWith("node.refine.", StringComparison.Ordinal));
        var decision = Assert.Single(recorder.Decisions, d => d.Site == "node.refine");
        Assert.Equal("pass.1", decision.Member);
        Assert.False(decision.Result);

        // a reader of the while reads null
        var render = host.Renders.Single(r => r.Key == "deliverable.note");
        Assert.Equal("""{"node":{"refine":{"content":null}}}""", render.Scope.ToJsonString());
    }

    [Fact]
    public async Task ACheckFirstWhile_AtItsBound_ChecksBeforeTheNextPass()
    {
        // a check that always holds, decided before each pass: before a fourth too, which the bound does not run
        var files = WithBilling(DraftOnly())
            .WithJson("nodes/refine/index.json", j =>
            {
                j["checkFirst"] = true;
                j["while"] = JsonNode.Parse("""{ "===": [{ "var": "input.include_billing.value" }, true] }""");
            });
        var plan = RunFixture.Plan(files, ResolveFixture.NestedNoteReads());
        var host = NestedHost();
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(includeBilling: true), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.refine.draft_summary#1", "node.refine.draft_summary#2", "node.refine.draft_summary#3"],
            host.Dispatched.Where(k => k.StartsWith("node.refine.", StringComparison.Ordinal)));
        Assert.Equal("bound", (string?)result.Values["node.refine"]!["stopped"]);
        Assert.Equal("Text of node.refine.draft_summary#3.", (string?)result.Values["node.refine"]!["value"]);
        Assert.Equal(["pass.1", "pass.2", "pass.3", "pass.4"], recorder.Decisions.Where(d => d.Site == "node.refine").Select(d => d.Member));
        Assert.True((bool)recorder.Row("node.refine").Detail["checkFirst"]!);
        V31Contract.AssertSentence(new SourceRefusal("S04-005", (string)recorder.Row("node.refine").Detail["reason"]!, ""));
    }

    [Fact]
    public async Task ABodyNodeReadingPassNode_WaitsForIt()
    {
        // the judge reads the draft as pass.node.draft_summary: a dependency edge like node.draft_summary's
        var reads = new StubTemplates(new Dictionary<string, string[]>
        {
            ["node.plan"] = ["input.consult_draft.content"],
            ["node.sections.outline"] = ["item.value.title"],
            ["node.sections.paragraphs.draft_paragraph"] = ["item.parent.value.title", "item.value"],
            ["node.refine.draft_summary"] = [],
            ["node.refine.judge"] = ["pass.node.draft_summary.content"],
            ["deliverable.note"] = ["node.refine.content"],
        }, everyBody: false);
        var plan = RunFixture.Plan(ResolveFixture.NestedNote(), reads);
        Assert.Equal(["node.refine.draft_summary"], plan.Entries["node.refine.judge"].Depends);
        var host = new FakeRunHost()
            .Reply("node.plan", SectionPlan("History"))
            .Reply("node.sections.outline", """{"points":["Onset"]}""")
            .Reply("node.refine.judge", Good);
        var runner = new PlanRunner(plan, new RunLimits(), host, new ListRecorder());

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            Assert.DoesNotContain("node.refine.judge#1", host.Dispatched);
            host.Release("node.refine.draft_summary#1");
            Assert.Contains("node.refine.judge#1", host.Dispatched);
            host.ReleaseAll();
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal("""{"pass":{"node":{"draft_summary":{"content":"Text of node.refine.draft_summary#1."}}}}""",
            host.Calls.Single(c => c.Key == "node.refine.judge#1").Scope.ToJsonString());
    }

    [Fact]
    public async Task Until_StopsAWhile()
    {
        var files = ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", j => j["until"] = JsonNode.Parse("""{ "===": [{ "var": "pass.number" }, 2] }"""));
        var plan = RunFixture.Plan(files, ResolveFixture.NestedNoteReads());
        var host = NestedHost().Reply("node.refine.judge", Revise);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal("until", (string?)result.Values["node.refine"]!["stopped"]);
        Assert.Equal(2, (int)result.Values["node.refine"]!["passes"]!);
        Assert.Equal("until", (string?)recorder.Row("node.refine").Detail["stopped"]);
        Assert.Null(recorder.Row("node.refine").Detail["reason"]);

        // after each pass the check, then the until while the check still holds
        var decisions = recorder.Decisions.Where(d => d.Site == "node.refine").ToList();
        Assert.Equal(["pass.1", "until.1", "pass.2", "until.2"], decisions.Select(d => d.Member));
        Assert.Equal([true, false, true, true], decisions.Select(d => d.Result));
        Assert.Equal("pass.number = 2", decisions[1].Printed);
    }

    [Fact]
    public async Task APass_ReadsThePreviousOne()
    {
        var files = ResolveFixture.NestedNote()
            .With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                "Summarise the note in two sentences for Dr A. Sample, improving on {{ pass.previous.node.draft_summary.content }}\n"));
        var reads = new StubTemplates(new Dictionary<string, string[]>
        {
            ["node.plan"] = ["input.consult_draft.content"],
            ["node.sections.outline"] = ["item.value.title"],
            ["node.sections.paragraphs.draft_paragraph"] = ["item.parent.value.title", "item.value"],
            ["node.refine.draft_summary"] = ["pass.previous.node.draft_summary.content"],
            ["node.refine.judge"] = ["node.draft_summary.content"],
            ["deliverable.note"] = ["node.refine.content"],
        }, everyBody: false);
        var plan = RunFixture.Plan(files, reads);
        var host = NestedHost().Reply("node.refine.judge#1", Revise).Reply("node.refine.judge#2", Good);

        var result = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var first = host.Calls.Single(c => c.Key == "node.refine.draft_summary#1");
        Assert.Null(first.Scope["pass"]!["previous"]!["node"]!["draft_summary"]!["content"]);
        var second = host.Calls.Single(c => c.Key == "node.refine.draft_summary#2");
        Assert.Equal("""{"pass":{"previous":{"node":{"draft_summary":{"content":"Text of node.refine.draft_summary#1."}}}}}""",
            second.Scope.ToJsonString());
    }

    [Fact]
    public async Task APassNodeRead_IsThisPassesNode()
    {
        // the while written with pass.node.judge, the same node as node.judge
        var files = ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", j => j["while"] = JsonNode.Parse("""{ "!==": [{ "var": "pass.node.judge.value.verdict" }, "good"] }"""));
        var plan = RunFixture.Plan(files, ResolveFixture.NestedNoteReads());
        var host = NestedHost().Reply("node.refine.judge#1", Revise).Reply("node.refine.judge#2", Good);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(2, (int)result.Values["node.refine"]!["passes"]!);
        Assert.Equal("check", (string?)result.Values["node.refine"]!["stopped"]);
        Assert.Equal([true, false], recorder.Decisions.Where(d => d.Site == "node.refine").Select(d => d.Result));
    }

    [Fact]
    public async Task PassHistory_IsSizedInAConditions()
    {
        // another pass while fewer than one pass came before this one: two passes
        var files = DraftOnly()
            .WithJson("nodes/refine/index.json", j => j["while"] = JsonNode.Parse("""{ "<": [{ "size": [{ "var": "pass.history" }] }, 1] }"""));
        var plan = RunFixture.Plan(files, ResolveFixture.NestedNoteReads());
        var host = NestedHost();
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var read = result.Values["node.refine"]!;
        Assert.Equal(2, (int)read["passes"]!);
        Assert.Equal("check", (string?)read["stopped"]);
        Assert.Equal(2, read["history"]!.AsArray().Count);
        var decisions = recorder.Decisions.Where(d => d.Site == "node.refine").ToList();
        Assert.Equal([true, false], decisions.Select(d => d.Result));
        Assert.Equal("size(pass.history) < 1", decisions[0].Printed);
    }

    [Fact]
    public async Task AWhileInsideALoop_RunsPerItem()
    {
        var plan = RunFixture.Plan(WhileInLoop(), WhileInLoopReads());
        var host = new FakeRunHost(auto: true)
            .Reply("node.plan", SectionPlan("History", "Plan"))
            .Reply("node.sections.refine.judge", Good)
            .Reply("node.sections.refine.judge:2#1", Revise);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.sections.refine.judge:1#1", "node.sections.refine.judge:2#1", "node.sections.refine.judge:2#2"],
            host.Dispatched.Where(k => k.StartsWith("node.sections.refine.judge", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(["Text of node.sections.refine.draft:1#1.", "Text of node.sections.refine.draft:2#2."],
            result.Values["node.sections"]!["value"]!.AsArray().Select(v => (string)v!));

        Assert.Equal(1, (int)recorder.Row("node.sections.refine:1").Detail["passes"]!);
        Assert.Equal(2, (int)recorder.Row("node.sections.refine:2").Detail["passes"]!);
        Assert.Equal(RowState.Completed, recorder.Row("node.sections.refine:2#1").State);
        Assert.Equal(["pass.1", "pass.2"], recorder.Decisions.Where(d => d.Site == "node.sections.refine:2").Select(d => d.Member));

        // a row inside a pass inside an item carries the item's name
        Assert.Equal("Plan", (string?)recorder.Row("node.sections.refine.judge:2#1").Detail["itemName"]);
        Assert.Equal("Plan", (string?)recorder.Row("node.sections.refine:2#1").Detail["itemName"]);
        Assert.Equal("History", (string?)recorder.Row("node.sections.refine:1").Detail["itemName"]);
    }

    [Fact]
    public async Task ALoopInsideAWhile_RunsPerPass()
    {
        var plan = RunFixture.Plan(LoopInWhile(), LoopInWhileReads());
        var host = new FakeRunHost(auto: true).Reply("node.refine.judge#1", Revise).Reply("node.refine.judge#2", Good);
        var recorder = new ListRecorder();
        var sections = new JsonArray(Section("History"), Section("Plan"));

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Supplied("sections", sections), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.refine.notes.note#1:0", "node.refine.notes.note#1:1", "node.refine.notes.note#2:0", "node.refine.notes.note#2:1"],
            host.Dispatched.Where(k => k.StartsWith("node.refine.notes.note", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(RowState.Completed, recorder.Row("node.refine.notes#1").State);
        Assert.Equal(RowState.Completed, recorder.Row("node.refine.notes#2").State);
        Assert.Equal(RowState.Completed, recorder.Row("node.refine.notes#2:1").State);
        Assert.Equal(["Text of node.refine.notes.note#2:0.", "Text of node.refine.notes.note#2:1."],
            result.Values["node.refine"]!["value"]!.AsArray().Select(v => (string)v!));
        Assert.Equal("History", (string?)recorder.Row("node.refine.notes.note#2:0").Detail["itemName"]);
    }

    // ---- if ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, "node.letter.with_billing", "node.letter.without_billing")]
    [InlineData(false, "node.letter.without_billing", "node.letter.with_billing")]
    public async Task AnIf_RunsThenOrElse(bool billing, string taken, string untaken)
    {
        var plan = RunFixture.Plan(IfElse(), IfElseReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(includeBilling: billing), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Contains(taken, host.Dispatched);
        Assert.DoesNotContain(untaken, host.Dispatched);
        Assert.DoesNotContain(recorder.Rows, r => r.Key == untaken);
        var read = result.Values["node.letter"]!;
        Assert.Equal($"Text of {taken}.", (string?)read["value"]);
        Assert.Equal($"Text of {taken}.", (string?)read["content"]);
        Assert.Equal("completed", (string?)read["state"]);
        var row = recorder.Row("node.letter");
        Assert.Equal(RowState.Completed, row.State);
        Assert.Equal(PlanKind.If, row.Kind);
        Assert.Equal(CanonicalJson.HashText($"Text of {taken}."), row.ValueHash);
        var decision = Assert.Single(recorder.Decisions, d => d.Site == "node.letter");
        Assert.Equal("self", decision.Member);
        Assert.Equal(billing, decision.Result);
        Assert.Equal("input.include_billing.value = true", decision.Printed);
        Assert.Equal(FakeRunHost.Start, decision.DecidedAtUtc);

        // the deliverable reads the if once it has settled
        var render = host.Renders.Single(r => r.Key == "deliverable.note");
        Assert.Equal($"Text of {taken}.", (string?)render.Scope["node"]!["letter"]!["content"]);
    }

    [Fact]
    public async Task AFlowNodeInAnUntakenBranch_IsSkipped_AndItsNodesHaveNoRows()
    {
        var plan = RunFixture.Plan(IfElse(), IfElseReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(includeBilling: false), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.DoesNotContain(recorder.Rows, r => r.Key.StartsWith("node.letter.with_billing", StringComparison.Ordinal)
            || r.Key.StartsWith("node.letter.billing_extra", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Decisions, d => d.Site.StartsWith("node.letter.", StringComparison.Ordinal));
        // the if inside the then branch reads skipped; a leaf beside it, never reached, not_run
        var scope = host.Renders.Single(r => r.Key == "deliverable.note").Scope;
        Assert.Equal("skipped", (string?)scope["node"]!["letter"]!["billing_extra"]!["state"]);
        Assert.Equal("not_run", (string?)scope["node"]!["letter"]!["with_billing"]!["state"]);

        // taken, the branch's nodes are read from outside it
        var taken = new FakeRunHost(auto: true);
        var takenResult = await new PlanRunner(plan, new RunLimits(), taken, new ListRecorder()).RunAsync(PlanRunnerLeavesTests.Inputs(includeBilling: true), FakeRunHost.Start);
        Assert.Equal(RunState.Completed, takenResult.State);
        var takenScope = taken.Renders.Single(r => r.Key == "deliverable.note").Scope;
        Assert.Equal("completed", (string?)takenScope["node"]!["letter"]!["billing_extra"]!["state"]);
        Assert.Equal("completed", (string?)takenScope["node"]!["letter"]!["with_billing"]!["state"]);
        Assert.Contains("node.letter.billing_extra.billing_line", taken.Dispatched);
    }

    [Fact]
    public async Task AnIfWithNoElse_ThatIsFalse_IsSkipped()
    {
        var plan = RunFixture.Plan("conditional");
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();
        var sections = new JsonArray(Section("History"), Section("Results", "table"));

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Supplied("sections", sections), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Contains("node.patient_summaries.narrative_only.patient_summary:0", host.Dispatched);
        Assert.DoesNotContain("node.patient_summaries.narrative_only.patient_summary:1", host.Dispatched);
        Assert.DoesNotContain(recorder.Rows, r => r.Key == "node.patient_summaries.narrative_only.patient_summary:1");

        const string Reason = "skipped: item.value.kind = \"narrative\" is false";
        var row = recorder.Row("node.patient_summaries.narrative_only:1");
        Assert.Equal(RowState.Skipped, row.State);
        Assert.Equal(Reason, row.Reason);
        V31Contract.AssertSentence(new SourceRefusal("S04-003", row.Reason!, ""));
        Assert.Equal("Results", (string?)row.Detail["itemName"]);
        Assert.Equal(RowState.Skipped, recorder.Row("node.patient_summaries:1").State);
        Assert.Equal(Reason, recorder.Row("node.patient_summaries:1").Reason);
        Assert.Equal(RowState.Completed, recorder.Row("node.patient_summaries.narrative_only:0").State);

        var decision = Assert.Single(recorder.Decisions, d => d.Site == "node.patient_summaries.narrative_only:1");
        Assert.Equal("self", decision.Member);
        Assert.False(decision.Result);
        Assert.Equal(["Text of node.patient_summaries.narrative_only.patient_summary:0."],
            result.Values["node.patient_summaries"]!["value"]!.AsArray().Select(v => (string)v!));
    }

    // ---- case -------------------------------------------------------------------------------------

    [Fact]
    public async Task ACase_RunsTheMatchingBranch()
    {
        var plan = RunFixture.Plan("conditional");
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();
        var sections = new JsonArray(Section("History"), Section("Results", "table"), Section("Billing", "omit"));

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Supplied("sections", sections), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.sections_draft.route.narrative_draft:0", "node.sections_draft.route.table_draft:1"],
            host.Dispatched.Where(k => k.StartsWith("node.sections_draft.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(["Text of node.sections_draft.route.narrative_draft:0.", "Text of node.sections_draft.route.table_draft:1."],
            result.Values["node.sections_draft"]!["value"]!.AsArray().Select(v => (string)v!));

        var omitted = recorder.Row("node.sections_draft.route:2");
        Assert.Equal(RowState.Skipped, omitted.State);
        Assert.Equal(PlanKind.Case, omitted.Kind);
        Assert.Equal("case 'route': arm for omit does nothing", omitted.Reason);
        V31Contract.AssertSentence(new SourceRefusal("S04-006", omitted.Reason!, ""));
        V31Contract.AssertSentence(new SourceRefusal("S04-049", omitted.Reason!, ""));
        Assert.Equal(RowState.Skipped, recorder.Row("node.sections_draft:2").State);
        Assert.Equal(RowState.Completed, recorder.Row("node.sections_draft.route:1").State);
        Assert.Equal("Text of node.sections_draft.route.table_draft:1.", (string?)result.Values["node.sections_draft"]!["items"]![1]!["value"]);

        // the decision records the branch taken
        var decisions = recorder.Decisions.Where(d => d.Site.StartsWith("node.sections_draft.route:", StringComparison.Ordinal)).OrderBy(d => d.Site, StringComparer.Ordinal).ToList();
        Assert.Equal(["1", "2", "3"], decisions.Select(d => d.Branch));
        Assert.All(decisions, d =>
        {
            Assert.Equal("case", d.Member);
            Assert.Equal("item.value.kind", d.Printed);
            Assert.Null(d.Result);
        });
    }

    [Fact]
    public async Task ANullSubject_GoesToElse()
    {
        var plan = RunFixture.Plan(Encounter(), EncounterReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Contains("node.opening.general_opening", host.Dispatched);
        Assert.DoesNotContain("node.opening.clinic_opening", host.Dispatched);
        Assert.Equal("Text of node.opening.general_opening.", (string?)result.Values["node.opening"]!["value"]);
        var decision = Assert.Single(recorder.Decisions, d => d.Site == "node.opening");
        Assert.Equal("else", decision.Branch);
        Assert.Equal("input.encounter_kind.value", decision.Printed);
    }

    [Theory]
    [InlineData("telephone", "node.opening.general_opening", "else")]
    [InlineData("clinic", "node.opening.clinic_opening", "1")]
    public async Task AnUnlistedValue_GoesToElse(string kind, string taken, string branch)
    {
        var plan = RunFixture.Plan(Encounter(), EncounterReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Supplied("encounter_kind", JsonValue.Create(kind)), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal([taken], host.Dispatched.Where(k => k.StartsWith("node.opening.", StringComparison.Ordinal)));
        Assert.Equal($"Text of {taken}.", (string?)result.Values["node.opening"]!["content"]);
        Assert.Equal(branch, Assert.Single(recorder.Decisions, d => d.Site == "node.opening").Branch);
    }

    [Fact]
    public async Task AValueNoBranchLists_WithNoElse_FailsTheCase()
    {
        // every value listed, so no else; a value that escaped its format reaches it
        var files = Encounter(required: true)
            .WithJson("nodes/opening/index.json", j =>
            {
                j["branches"] = JsonNode.Parse(
                    """[{ "when": ["clinic"], "nodes": ["clinic_opening"], "ret": "node.opening.clinic_opening" }, """
                    + """{ "when": ["ward", "telephone"], "nodes": ["general_opening"], "ret": "node.opening.general_opening" }]""");
                j["else"] = null;
            });
        var plan = RunFixture.Plan(files, EncounterReads());
        var host = new FakeRunHost(auto: true);

        var result = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(Supplied("encounter_kind", JsonValue.Create("home visit")), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.opening", result.FailedKey);
        Assert.Equal("Case 'opening' was given input.encounter_kind.value, which no branch lists, and it has no else.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A06-007", result.Sentence!, ""));
        Assert.DoesNotContain("home visit", result.Sentence!, StringComparison.Ordinal);
        Assert.DoesNotContain(host.Dispatched, k => k.StartsWith("node.opening.", StringComparison.Ordinal));
    }

    // ---- decisions and the golden -------------------------------------------------------------------

    [Fact]
    public async Task EveryDecision_IsRecordedOnce_WithItsPrintedCondition()
    {
        var plan = RunFixture.Plan("conditional");
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();
        var sections = new JsonArray(Section("History"), Section("Results", "table"), Section("Billing", "omit"));

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Supplied("sections", sections), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(recorder.Decisions.Count, recorder.Decisions.Select(d => (d.Site, d.Member)).Distinct().Count());
        string[] expected =
        [
            "node.patient_summaries.narrative_only:0 self item.value.kind = \"narrative\"",
            "node.patient_summaries.narrative_only:1 self item.value.kind = \"narrative\"",
            "node.patient_summaries.narrative_only:2 self item.value.kind = \"narrative\"",
            "node.sections_draft.route:0 case item.value.kind",
            "node.sections_draft.route:1 case item.value.kind",
            "node.sections_draft.route:2 case item.value.kind",
            "node.standing_sections.applies:0 self item.id ≠ \"billing\" or input.include_billing.value = true",
            "node.standing_sections.applies:1 self item.id ≠ \"billing\" or input.include_billing.value = true",
            "node.standing_sections.applies:2 self item.id ≠ \"billing\" or input.include_billing.value = true",
        ];
        Assert.Equal(expected, recorder.Decisions.Select(d => $"{d.Site} {d.Member} {d.Printed}").Order(StringComparer.Ordinal));
        Assert.All(recorder.Decisions, d => Assert.Equal(FakeRunHost.Start, d.DecidedAtUtc));

        // the nested note: the paragraphs' header if per item, the while's check per pass
        var nested = new ListRecorder();
        var nestedHost = NestedHost().Reply("node.refine.judge#1", Revise).Reply("node.refine.judge#2", Good);
        await new PlanRunner(RunFixture.Plan("nested-note"), new RunLimits(), nestedHost, nested).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        Assert.Equal(nested.Decisions.Count, nested.Decisions.Select(d => (d.Site, d.Member)).Distinct().Count());
        Assert.Equal(["node.refine pass.1", "node.refine pass.2"],
            nested.Decisions.Where(d => d.Site.StartsWith("node.refine", StringComparison.Ordinal)).Select(d => $"{d.Site} {d.Member}"));
        Assert.Equal(3, nested.Decisions.Count(d => d.Site.StartsWith("node.sections.paragraphs.draft_paragraph:", StringComparison.Ordinal) && d.Member == "self"));
    }

    [Fact]
    public async Task TheNestedNoteGolden_RunsToCompleted()
    {
        var plan = RunFixture.Plan("nested-note");
        var host = NestedHost().Reply("node.refine.judge", Good);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Null(result.Sentence);
        Assert.Equal(plan.TopLevel.Order(StringComparer.Ordinal), result.Values.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, (int)result.Values["node.section_count"]!["value"]!);
        Assert.Equal(3, result.Values["node.sections"]!["value"]!.AsArray().Sum(s => s!.AsArray().Count));
        Assert.Equal(1, (int)result.Values["node.refine"]!["passes"]!);
        Assert.Equal("composed", (string?)result.Values["deliverable.note"]!["state"]);
        Assert.All(recorder.Rows, r => Assert.True(r.State is RowState.Completed or RowState.Composed, $"{r.Key} is {r.State}"));
    }

    // ---- fixtures -----------------------------------------------------------------------------------

    /// <summary>A host for the nested note: its plan has the sections History and Plan, each outline one or two points.</summary>
    private static FakeRunHost NestedHost() => new FakeRunHost(auto: true)
        .Reply("node.plan", SectionPlan("History", "Plan"))
        .Reply("node.sections.outline:1", """{"points":["Onset"]}""")
        .Reply("node.sections.outline:2", """{"points":["Tests","Follow-up"]}""");

    /// <summary>The nested note whose while <c>refine</c> has one node, <c>draft_summary</c>, for a check that reads none of its body.</summary>
    private static Dictionary<string, byte[]> DraftOnly() => ResolveFixture.NestedNote()
        .Without("nodes/refine/judge.md")
        .WithJson("nodes/refine/index.json", j => j["nodes"] = new JsonArray("draft_summary"));

    /// <summary>A <c>section_plan</c> reply with a narrative section for each title.</summary>
    private static string SectionPlan(params string[] titles) =>
        new JsonObject { ["sections"] = new JsonArray(titles.Select(t => (JsonNode?)new JsonObject { ["title"] = t, ["kind"] = "narrative" }).ToArray()) }
            .ToJsonString();

    /// <summary>A <c>section_request</c> row.</summary>
    private static JsonObject Section(string title, string kind = "narrative") => new() { ["title"] = title, ["kind"] = kind };

    /// <summary>The consult draft, and the input <paramref name="id"/> supplied as <paramref name="value"/>.</summary>
    private static RunInputs Supplied(string id, JsonNode value)
    {
        var inputs = PlanRunnerLeavesTests.Inputs();
        var supplied = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
        {
            [id] = new JsonObject { ["value"] = value, ["content"] = null, ["title"] = null },
        };
        return inputs with { Inputs = supplied };
    }

    /// <summary>The optional boolean input <c>include_billing</c> added to the manifest.</summary>
    private static Dictionary<string, byte[]> WithBilling(Dictionary<string, byte[]> files) =>
        files.WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
            "{ \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", \"required\": false, \"channel\": null }"));

    /// <summary>
    /// The plan, and the loop <c>sections</c> over its sections whose body is the while <c>refine</c> (a draft of the
    /// section and its judge, at most 2 passes); the note reads the sections.
    /// </summary>
    private static Dictionary<string, byte[]> WhileInLoop() => ResolveFixture.WithFormats(Minimal(), "section_plan", "verdict")
        .With("nodes/index.json", Index(["plan", "sections"]))
        .With("nodes/plan.md", Agent("Planning the sections", "\"section_plan\"",
            "Plan the sections of a consultation note for the draft below.\n\n{{ input.consult_draft.content }}\n"))
        .With("nodes/sections/index.json", Json(SourceFixture.Node("for", "Sections", ArrayOf(Plain), false, true,
            "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
            + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"refine\"], \"ret\": \"node.sections.refine\"")))
        .With("nodes/sections/refine/index.json", Json(SourceFixture.Node("while", "Refining the section", Plain, false, true,
            "\"while\": { \"!==\": [{ \"var\": \"node.judge.value.verdict\" }, \"good\"] }, \"max\": 2, "
            + "\"until\": null, \"checkFirst\": false, \"nodes\": [\"draft\", \"judge\"], \"ret\": \"node.sections.refine.draft\"")))
        .With("nodes/sections/refine/draft.md", Agent("Drafting the section", Plain, "Write the section '{{ item.value.title }}'.\n"))
        .With("nodes/sections/refine/judge.md", Agent("Judging the section", "\"verdict\"",
            "Say whether the section is good or needs another pass.\n\n{{ node.draft.content }}\n"))
        .With("deliverables/note.md", Md(Deliverable("Consultation note", "\"markdown_block\"", "null", "hold"), "{{ node.sections.content }}\n"));

    private static StubTemplates WhileInLoopReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["node.sections.refine.draft"] = ["item.value.title"],
        ["node.sections.refine.judge"] = ["node.draft.content"],
        ["deliverable.note"] = ["node.sections.content"],
    }, everyBody: false);

    /// <summary>
    /// The list input <c>sections</c> and the while <c>refine</c> whose body is the loop <c>notes</c> over it (a note per
    /// section) and <c>judge</c>, reading the notes; it returns the notes.
    /// </summary>
    private static Dictionary<string, byte[]> LoopInWhile() => ResolveFixture.WithFormats(Minimal(), "section_request", "verdict")
        .WithJson("manifest.json", m => m["inputs"]!.AsObject()["sections"] = JsonNode.Parse(
            "{ \"label\": \"Sections\", \"description\": null, "
            + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"section_request\" } }, \"required\": true, \"channel\": null }"))
        .With("nodes/index.json", Index(["plan", "refine"]))
        .With("nodes/refine/index.json", Json(SourceFixture.Node("while", "Refining the notes", ArrayOf(Plain), false, true,
            "\"while\": { \"!==\": [{ \"var\": \"node.judge.value.verdict\" }, \"good\"] }, \"max\": 3, "
            + "\"until\": null, \"checkFirst\": false, \"nodes\": [\"notes\", \"judge\"], \"ret\": \"node.refine.notes\"")))
        .With("nodes/refine/notes/index.json", Json(SourceFixture.Node("for", "Notes", ArrayOf(Plain), false, true,
            "\"in\": \"input.sections.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
            + "\"max\": null, \"itemLabel\": \"title\", \"nodes\": [\"note\"], \"ret\": \"node.refine.notes.note\"")))
        .With("nodes/refine/notes/note.md", Agent("Noting a section", Plain, "Note the section '{{ item.value.title }}'.\n"))
        .With("nodes/refine/judge.md", Agent("Judging the notes", "\"verdict\"",
            "Say whether the notes are good or need another pass.\n\n{{ node.notes.content }}\n"))
        .With("deliverables/note.md", Md(Deliverable("Consultation note", "\"markdown_block\"", "null", "hold"), "{{ node.refine.content }}\n"));

    private static StubTemplates LoopInWhileReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["node.refine.notes.note"] = ["item.value.title"],
        ["node.refine.judge"] = ["node.notes.content"],
        ["deliverable.note"] = ["node.refine.content"],
    }, everyBody: false);

    /// <summary>
    /// The optional boolean input <c>include_billing</c> and the if <c>letter</c> on it: its then is
    /// <c>with_billing</c> (its ret) and the if <c>billing_extra</c> on the same input (holding <c>billing_line</c>); its
    /// else is <c>without_billing</c>. The note reads the letter, and the then branch's two nodes' states.
    /// </summary>
    private static Dictionary<string, byte[]> IfElse() => WithBilling(Minimal())
        .With("nodes/index.json", Index(["plan", "letter"]))
        .With("nodes/letter/index.json", Json(SourceFixture.Node("if", "Writing the letter", Plain, false, true,
            "\"if\": { \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }, "
            + "\"then\": { \"nodes\": [\"with_billing\", \"billing_extra\"], \"ret\": \"node.letter.with_billing\" }, "
            + "\"else\": { \"nodes\": [\"without_billing\"], \"ret\": \"node.letter.without_billing\" }")))
        .With("nodes/letter/with_billing.md", Agent("Writing the letter with billing", Plain, "Write the letter for Margaret Okafor, with billing.\n"))
        .With("nodes/letter/without_billing.md", Agent("Writing the letter", Plain, "Write the letter for Margaret Okafor.\n"))
        .With("nodes/letter/billing_extra/index.json", Json(SourceFixture.Node("if", "Adding the billing line", Plain, false, true,
            "\"if\": { \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }, "
            + "\"then\": { \"nodes\": [\"billing_line\"], \"ret\": \"node.letter.billing_extra.billing_line\" }, \"else\": null")))
        .With("nodes/letter/billing_extra/billing_line.md", Agent("Writing the billing line", Plain, "Write one billing line.\n"))
        .With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "null", "hold"),
            "{{ node.letter.content }} {{ node.billing_extra.state }} {{ node.with_billing.state }}\n"));

    private static StubTemplates IfElseReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["deliverable.note"] = ["node.letter.content", "node.letter.billing_extra.state", "node.letter.with_billing.state"],
    });

    /// <summary>
    /// The input <c>encounter_kind</c> (clinic, ward or telephone; optional unless <paramref name="required"/>) and the
    /// case <c>opening</c> on it: a branch for clinic, and an else for the rest and for <c>null</c>.
    /// </summary>
    private static Dictionary<string, byte[]> Encounter(bool required = false) => Minimal()
        .WithJson("manifest.json", m => m["inputs"]!["encounter_kind"] = JsonNode.Parse(
            "{ \"label\": \"Encounter kind\", \"description\": null, "
            + "\"format\": { \"type\": \"string\", \"enum\": [\"clinic\", \"ward\", \"telephone\"] }, "
            + $"\"required\": {(required ? "true" : "false")}, \"channel\": null }}"))
        .With("nodes/index.json", Index(["plan", "opening"]))
        .With("nodes/opening/index.json", Json(SourceFixture.Node("case", "Opening the letter", Plain, false, true,
            "\"case\": \"input.encounter_kind.value\", \"branches\": ["
            + "{ \"when\": [\"clinic\"], \"nodes\": [\"clinic_opening\"], \"ret\": \"node.opening.clinic_opening\" }], "
            + "\"else\": { \"nodes\": [\"general_opening\"], \"ret\": \"node.opening.general_opening\" }")))
        .With("nodes/opening/clinic_opening.md", Agent("Opening after a clinic visit", Plain, "Open the letter after the clinic visit.\n"))
        .With("nodes/opening/general_opening.md", Agent("Opening the letter", Plain, "Open the letter.\n"))
        .With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "null", "hold"), "{{ node.opening.content }}\n"));

    private static StubTemplates EncounterReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["deliverable.note"] = ["node.opening.content"],
    });
}
