using System.Diagnostics;
using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 04 §§ 2, 4, 8; part 06 § 3.2; R1; D11, D13): <see cref="PlanRunner"/> on <c>for</c>
/// loops. Each item runs in a frame of its own, keyed by its path; <c>parallel</c>, <c>maxParallel</c> (per loop instance)
/// and sequential loops open their items as § 2.3 says; <c>until</c> and <c>max</c> leave the rest <c>not_run</c> with
/// their reasons; a loop reads <c>.value</c>, <c>.items</c>, <c>.content</c> and <c>.stopped</c>; items are named by
/// <c>itemLabel</c>, else by position; a loop over another loop's <c>.items</c> runs item by item. Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PlanRunnerLoopsTests
{
    private const string Plain = PlanRunnerLeavesTests.Plain;

    private const string Concepts = """{"concepts":[]}""";

    // ---- an item's state word ---------------------------------------------------------------------

    [Fact]
    public void AnItemsStateWord_NamesEachState_AndNoOtherReadsAsBlocked()
    {
        // #1505 (the final review): one arm per state, as StoredRunRecorder.StateName; a state added later is not "blocked"
        Assert.Equal(
            ["completed", "skipped", "not_run", "failed", "cancelled", "released", "held", "blocked"],
            Enum.GetValues<RowState>().Select(PlanRunner.StateWord));
        Assert.NotEqual("blocked", PlanRunner.StateWord((RowState)99));
    }

    // ---- nesting ----------------------------------------------------------------------------------

    [Fact]
    public async Task NestedLoops_RunEveryItem_AndReadByPath()
    {
        var plan = RunFixture.Plan(NestedLoops(), NestedLoopsReads());
        var host = new FakeRunHost(auto: true)
            .Reply("node.plan", SectionPlan("History", "Plan"))
            .Reply("node.sections.outline:1", """{"points":["Onset","Course"]}""")
            .Reply("node.sections.outline:2", """{"points":["Tests","Treatment","Follow-up"]}""");
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var paragraphs = host.Calls.Where(c => c.EntryKey == "node.sections.paragraphs.draft_paragraph").ToList();
        Assert.Equal(
            ["node.sections.paragraphs.draft_paragraph:1/1", "node.sections.paragraphs.draft_paragraph:1/2",
             "node.sections.paragraphs.draft_paragraph:2/1", "node.sections.paragraphs.draft_paragraph:2/2",
             "node.sections.paragraphs.draft_paragraph:2/3"],
            paragraphs.Select(c => c.Key).Order(StringComparer.Ordinal));

        // each call reads its own item and its section's title, by path
        var second = paragraphs.Single(c => c.Key == "node.sections.paragraphs.draft_paragraph:2/3");
        Assert.Equal("""{"item":{"parent":{"value":{"title":"Plan"}},"value":"Follow-up"}}""", second.Scope.ToJsonString());
        var first = paragraphs.Single(c => c.Key == "node.sections.paragraphs.draft_paragraph:1/2");
        Assert.Equal("""{"item":{"parent":{"value":{"title":"History"}},"value":"Course"}}""", first.Scope.ToJsonString());

        // the outer loop's value is a list of two lists, in item order
        var value = result.Values["node.sections"]!["value"]!.AsArray();
        Assert.Equal(2, value.Count);
        Assert.Equal(["Text of node.sections.paragraphs.draft_paragraph:1/1.", "Text of node.sections.paragraphs.draft_paragraph:1/2."],
            value[0]!.AsArray().Select(v => (string)v!));
        Assert.Equal(3, value[1]!.AsArray().Count);
        Assert.Equal("completed", (string?)result.Values["node.sections"]!["state"]);
        Assert.Equal("end", (string?)result.Values["node.sections"]!["stopped"]);

        // a row for each loop instance and each item, keyed by RunKeys
        Assert.Equal(RowState.Completed, recorder.Row("node.sections.paragraphs:2").State);
        Assert.Equal(RowState.Completed, recorder.Row("node.sections.paragraphs:2/3").State);
        Assert.Equal(RowState.Completed, recorder.Row("node.sections:1").State);
        Assert.Equal(3, (int)recorder.Row("node.sections.paragraphs:2").Detail["count"]!);
        Assert.Equal(RowState.Released, recorder.Row("deliverable.note").State);

        // every row inside an item carries the innermost item's name (part 08 § 4); rows outside a loop carry null
        Assert.Equal("History", NodeRowTests.ItemName(recorder.Row("node.sections.outline:1")));
        Assert.False((bool)recorder.Row("node.sections.outline:1").Detail["positionName"]!);
        Assert.Equal("Plan", NodeRowTests.ItemName(recorder.Row("node.sections.paragraphs:2")));
        Assert.Equal("Paragraphs 3", NodeRowTests.ItemName(recorder.Row("node.sections.paragraphs.draft_paragraph:2/3")));
        Assert.True((bool)recorder.Row("node.sections.paragraphs.draft_paragraph:2/3").Detail["positionName"]!);
        Assert.Null(recorder.Row("node.plan").Detail["itemName"]);
        Assert.Null(recorder.Row("node.sections").Detail["itemName"]);
    }

    [Fact]
    public async Task AnItemValueFieldSource_RunsANestedLoopOverIt()
    {
        var plan = RunFixture.Plan(Visits(), VisitsReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();
        var visits = new JsonArray(Visit("March visit", "Fatigue", "Weight"), Visit("April visit", "Results"));

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Supplied("visits", visits), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        // #1506 (R3): ids are 1-based everywhere
        Assert.Equal(["node.visits.points.note:1/1", "node.visits.points.note:1/2", "node.visits.points.note:2/1"],
            host.Dispatched.Where(k => k.StartsWith("node.visits.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal("""{"item":{"parent":{"value":{"title":"April visit"}},"value":"Results"}}""",
            host.Calls.Single(c => c.Key == "node.visits.points.note:2/1").Scope.ToJsonString());
        Assert.Equal(["node.visits.points:1/1", "node.visits.points:1/2", "node.visits.points:2/1"],
            recorder.Rows.Where(r => r.EntryKey == "node.visits.points" && r.Key.Contains('/', StringComparison.Ordinal)).Select(r => r.Key).Order(StringComparer.Ordinal));
        Assert.Equal(["node.visits:1", "node.visits:2"],
            recorder.Rows.Where(r => r.EntryKey == "node.visits" && r.Key != "node.visits").Select(r => r.Key).Order(StringComparer.Ordinal));
        Assert.Equal(2, result.Values["node.visits"]!["value"]![0]!.AsArray().Count);
    }

    // ---- #1506: item ids start at 1; the instances limit ------------------------------------------

    [Fact]
    public async Task AnArrayInputsItems_AreNumberedFromOne()
    {
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Sections(Rows("A", "B", "C")), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.drafts.draft:1", "node.drafts.draft:2", "node.drafts.draft:3"],
            host.Dispatched.Where(k => k.StartsWith("node.drafts.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(["node.drafts:1", "node.drafts:2", "node.drafts:3"],
            recorder.Rows.Where(r => r.EntryKey == "node.drafts" && r.Key != "node.drafts").Select(r => r.Key).Order(StringComparer.Ordinal));
        Assert.Equal("1", (string?)result.Values["node.drafts"]!["items"]![0]!["item"]!["id"]);
    }

    [Fact]
    public async Task AJobPastItsInstances_FailsWithItsSentence()
    {
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();
        var titles = Enumerable.Range(1, 10).Select(n => $"S{n}").ToArray();

        var result = await new PlanRunner(plan, new RunLimits(JobInstances: 5), host, recorder).RunAsync(Sections(Rows(titles)), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("The job reached 5 steps, the most this engine runs for one job.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A09-001", result.Sentence!, ""));
        Assert.True(host.Dispatched.Count(k => k.StartsWith("node.drafts.", StringComparison.Ordinal)) < 5);
        Assert.True(recorder.Rows.Count(r => r.Key.Contains(':', StringComparison.Ordinal)) <= 5);

        // the step that opened one too many has its row, failed with the sentence
        Assert.NotNull(result.FailedKey);
        var failed = recorder.Row(result.FailedKey!);
        Assert.Equal(RowState.Failed, failed.State);
        Assert.Equal(result.Sentence, failed.Reason);
    }

    [Fact]
    public async Task TheInstancesLimit_HoldsAtExactlyWhatARunOpens()
    {
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var inputs = Sections(Rows("S1", "S2", "S3"));

        // every instance a run opens (a node, an item, a deliverable) records one row
        var open = new ListRecorder();
        var whole = await new PlanRunner(plan, new RunLimits(), new FakeRunHost(auto: true), open).RunAsync(inputs, FakeRunHost.Start);
        Assert.Equal(RunState.Completed, whole.State);
        var opened = open.Rows.Count;

        var exact = await new PlanRunner(plan, new RunLimits(JobInstances: opened), new FakeRunHost(auto: true), new ListRecorder()).RunAsync(inputs, FakeRunHost.Start);
        Assert.Equal(RunState.Completed, exact.State);

        var recorder = new ListRecorder();
        var under = await new PlanRunner(plan, new RunLimits(JobInstances: opened - 1), new FakeRunHost(auto: true), recorder).RunAsync(inputs, FakeRunHost.Start);
        Assert.Equal(RunState.Failed, under.State);
        Assert.Equal($"The job reached {opened - 1} steps, the most this engine runs for one job.", under.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A09-001", under.Sentence!, ""));
        Assert.Equal(RowState.Failed, recorder.Row(under.FailedKey!).State);
    }

    // ---- sizes ------------------------------------------------------------------------------------

    [Fact]
    public async Task AnEmptyList_SettlesAtZeroItems()
    {
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Sections(new JsonArray()), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan"], host.Dispatched);
        var read = result.Values["node.drafts"]!;
        Assert.Equal("[]", read["value"]!.ToJsonString());
        Assert.Equal("[]", read["items"]!.ToJsonString());
        Assert.Equal("end", (string?)read["stopped"]);
        var row = recorder.Row("node.drafts");
        Assert.Equal(RowState.Completed, row.State);
        Assert.Equal(0, (int)row.Detail["count"]!);
        Assert.Equal("end", (string?)row.Detail["stopped"]);
        Assert.Null(row.Detail["reason"]);
        Assert.Equal(RowState.Released, recorder.Row("deliverable.note").State);
    }

    [Fact]
    public async Task ANullSource_SettlesAtZero_WithItsReason()
    {
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan"], host.Dispatched);
        Assert.Equal("end", (string?)result.Values["node.drafts"]!["stopped"]);
        Assert.Equal("[]", result.Values["node.drafts"]!["items"]!.ToJsonString());
        var row = recorder.Row("node.drafts");
        Assert.Equal(RowState.Completed, row.State);
        Assert.Equal(0, (int)row.Detail["count"]!);
        var reason = (string)row.Detail["reason"]!;
        Assert.Equal("0 items: input.sections.value is null", reason);
        V31Contract.AssertSentence(new SourceRefusal("S04-077", reason, ""));
    }

    [Fact]
    public async Task ALoopGivenTooManyItems_FailsWithItsSentence()
    {
        // an input of 201 rows
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var rows = new JsonArray(Enumerable.Range(1, 201).Select(i => (JsonNode?)Row($"Section {i}")).ToArray());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Sections(rows), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.drafts", result.FailedKey);
        Assert.Equal("Loop 'drafts' was given 201 items; this engine runs at most 200 per loop.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S04-078", result.Sentence!, ""));
        Assert.DoesNotContain(host.Dispatched, k => k.StartsWith("node.drafts.", StringComparison.Ordinal));
        Assert.Equal(201, (int)recorder.Row("node.drafts").Detail["count"]!);

        // a model's list of 5,000
        var nested = RunFixture.Plan(NestedLoops(), NestedLoopsReads());
        var big = new FakeRunHost(auto: true)
            .Reply("node.plan", SectionPlan(Enumerable.Range(1, 5000).Select(i => $"Section {i}").ToArray()));
        var bigResult = await new PlanRunner(nested, new RunLimits(), big, new ListRecorder()).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, bigResult.State);
        Assert.Equal("node.sections", bigResult.FailedKey);
        Assert.Equal("Loop 'sections' was given 5,000 items; this engine runs at most 200 per loop.", bigResult.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S04-078", bigResult.Sentence!, ""));
        Assert.Equal(["node.plan"], big.Dispatched);
    }

    [Fact]
    public async Task ASourceThatIsNotAList_FailsTheLoop()
    {
        // a value that escaped validation: the list input supplied as one row
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var host = new FakeRunHost(auto: true);

        var result = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(Sections(Row("History")), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.drafts", result.FailedKey);
        Assert.Equal("Loop 'drafts' was given input.sections.value, which isn't a list.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A06-006", result.Sentence!, ""));
        Assert.DoesNotContain("History", result.Sentence!, StringComparison.Ordinal);
    }

    // ---- parallel, maxParallel, sequential ----------------------------------------------------------

    [Theory]
    [InlineData(16, 1)]
    [InlineData(16, 31)]
    [InlineData(16, 1503)]
    [InlineData(4, 1)]
    [InlineData(4, 31)]
    [InlineData(4, 1503)]
    public async Task MaxParallel_CapsEachLoopInstance(int ceiling, int seed)
    {
        // outer maxParallel 2 over 4 sections, inner maxParallel 3 over 5 points
        var files = NestedLoops()
            .WithJson("nodes/sections/index.json", j => j["maxParallel"] = 2)
            .WithJson("nodes/sections/paragraphs/index.json", j => j["maxParallel"] = 3);
        var plan = RunFixture.Plan(files, NestedLoopsReads());
        var host = new FakeRunHost()
            .Reply("node.plan", SectionPlan("History", "Examination", "Assessment", "Plan"))
            .Reply("node.sections.outline", """{"points":["One","Two","Three","Four","Five"]}""");
        var runner = new PlanRunner(plan, new RunLimits(MaxAgentCalls: ceiling), host, new ListRecorder());

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            host.ReleaseInOrder(new Random(seed));
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(20, host.Dispatched.Count(k => k.StartsWith("node.sections.paragraphs.draft_paragraph:", StringComparison.Ordinal)));
        for (var section = 1; section <= 4; section++)
        {
            var prefix = $"node.sections.paragraphs.draft_paragraph:{section}/";
            Assert.InRange(host.PeakOf(k => k.StartsWith(prefix, StringComparison.Ordinal)), 1, 3);
        }

        // the outer loop instance: at most 2 sections with a call in flight or a body unfinished
        Assert.InRange(host.PeakGroupsOf(SectionOf), 1, 2);
        Assert.InRange(host.InFlightPeak, 1, Math.Min(6, ceiling));
    }

    [Fact]
    public async Task ASequentialLoop_RunsInOrder()
    {
        var plan = RunFixture.Plan(InputLoop(Members(parallel: false)), InputLoopReads());
        var host = new FakeRunHost();
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(Sections(Rows("History", "Examination", "Plan")), FakeRunHost.Start);
            Assert.Equal(["node.plan", "node.drafts.draft:1"], host.Dispatched);
            host.Release("node.plan");
            Assert.Equal(["node.plan", "node.drafts.draft:1"], host.Dispatched);
            host.Release("node.drafts.draft:1");
            Assert.Equal(RowState.Completed, recorder.Row("node.drafts:1").State);
            Assert.Equal(["node.plan", "node.drafts.draft:1", "node.drafts.draft:2"], host.Dispatched);
            host.Release("node.drafts.draft:2");
            Assert.Equal(["node.plan", "node.drafts.draft:1", "node.drafts.draft:2", "node.drafts.draft:3"], host.Dispatched);
            host.ReleaseAll();
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(3, result.Values["node.drafts"]!["value"]!.AsArray().Count);
        var detail = recorder.Row("node.drafts").Detail;
        Assert.False((bool)detail["parallel"]!);
        Assert.Null(detail["maxParallel"]);
        Assert.Equal("title", (string?)detail["itemLabel"]);
        Assert.Equal(3, (int)detail["count"]!);
        Assert.Equal("end", (string?)detail["stopped"]);
    }

    // ---- until and max ------------------------------------------------------------------------------

    [Fact]
    public async Task Until_StopsAfterTheItemWhereItHolds()
    {
        // part 04 § 2.4's example: reading prior letters in order until one answers the referral question
        var plan = RunFixture.Plan(PriorLetters(), PriorLettersReads());
        var host = new FakeRunHost(auto: true)
            .Reply("node.letters.read_letter", """{"answers_question":false}""")
            .Reply("node.letters.read_letter:2", """{"answers_question":true}""");
        var recorder = new ListRecorder();
        var letters = new JsonArray("Letter of 3 March (fictional).", "Letter of 9 April (fictional).",
            "Letter of 2 June (fictional).", "Letter of 7 July (fictional).");

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Letters(letters), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.letters.read_letter:1", "node.letters.letter_summary:1", "node.letters.read_letter:2", "node.letters.letter_summary:2"],
            host.Dispatched.Where(k => k.StartsWith("node.letters.", StringComparison.Ordinal)));

        var read = result.Values["node.letters"]!;
        Assert.Equal("until", (string?)read["stopped"]);
        Assert.Equal(2, read["value"]!.AsArray().Count);
        var items = read["items"]!.AsArray();
        Assert.Equal(4, items.Count);
        foreach (var (index, key) in new[] { (2, "node.letters:3"), (3, "node.letters:4") })
        {
            Assert.Equal("not_run", (string?)items[index]!["state"]);
            Assert.Equal("stopped early at Prior letters 2", (string?)items[index]!["reason"]);
            Assert.Null(items[index]!["value"]);
            var row = recorder.Row(key);
            Assert.Equal(RowState.NotRun, row.State);
            Assert.Equal("stopped early at Prior letters 2", row.Reason);
            V31Contract.AssertSentence(new SourceRefusal("S04-001", row.Reason!, ""));
        }

        // a decision per evaluation, after items 1 and 2
        var decisions = recorder.Decisions.Where(d => d.Owner == "node.letters").ToList();
        Assert.Equal(["until.1", "until.2"], decisions.Select(d => d.Site));
        Assert.Equal([false, true], decisions.Select(d => d.Result));
        Assert.Equal("node.letters.read_letter.value.answers_question = true", decisions[0].Condition);
        Assert.Equal("until", (string?)recorder.Row("node.letters").Detail["stopped"]);
        Assert.Equal(4, (int)recorder.Row("node.letters").Detail["count"]!);
    }

    [Fact]
    public async Task Max_RunsTheFirstN()
    {
        var plan = RunFixture.Plan(InputLoop(Members(max: "2")), InputLoopReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Sections(Rows("History", "Examination", "Plan")), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan", "node.drafts.draft:1", "node.drafts.draft:2"], host.Dispatched);
        var read = result.Values["node.drafts"]!;
        Assert.Equal("bound", (string?)read["stopped"]);
        Assert.Equal(2, read["value"]!.AsArray().Count);
        Assert.Equal(3, read["items"]!.AsArray().Count);
        Assert.Equal("not_run", (string?)read["items"]![2]!["state"]);

        var row = recorder.Row("node.drafts:3");
        Assert.Equal(RowState.NotRun, row.State);
        Assert.Equal("not run: beyond max 2", row.Reason);
        V31Contract.AssertSentence(new SourceRefusal("S04-002", row.Reason!, ""));
        Assert.Equal("bound", (string?)recorder.Row("node.drafts").Detail["stopped"]);
        Assert.Equal("not run: beyond max 2", (string?)recorder.Row("node.drafts").Detail["reason"]);
    }

    [Fact]
    public async Task MaxParallel_AndMax_Together()
    {
        var plan = RunFixture.Plan(InputLoop(Members(max: "3", maxParallel: "2")), InputLoopReads());
        var host = new FakeRunHost();
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(Sections(Rows("History", "Examination", "Results", "Assessment", "Plan")), FakeRunHost.Start);

            // two open at a time; the items beyond max are not run at once, while the first two still run
            Assert.Equal(["node.plan", "node.drafts.draft:1", "node.drafts.draft:2"], host.Dispatched);
            Assert.Equal(RowState.NotRun, recorder.Row("node.drafts:4").State);
            Assert.Equal("not run: beyond max 3", recorder.Row("node.drafts:5").Reason);
            host.Release("node.drafts.draft:2");
            Assert.Equal(["node.plan", "node.drafts.draft:1", "node.drafts.draft:2", "node.drafts.draft:3"], host.Dispatched);
            host.ReleaseAll();
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(4, host.Dispatched.Count);
        Assert.Equal("bound", (string?)result.Values["node.drafts"]!["stopped"]);
        Assert.Equal(3, result.Values["node.drafts"]!["value"]!.AsArray().Count);
        Assert.Equal(2, (int)recorder.Row("node.drafts").Detail["maxParallel"]!);
    }

    [Fact]
    public async Task Until_WithItemLabel_GivesThePositionName()
    {
        // until holds after the first section, named "History" by its title: the reason names it by position
        var plan = RunFixture.Plan(InputLoop(Members(parallel: false,
            until: "{ \"!\": [{ \"blank\": [{ \"var\": \"node.draft.content\" }] }] }")), InputLoopReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Sections(Rows("History", "Examination", "Plan")), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan", "node.drafts.draft:1"], host.Dispatched);
        Assert.Equal("History", NodeRowTests.ItemName(recorder.Row("node.drafts:1")));
        foreach (var key in new[] { "node.drafts:2", "node.drafts:3" })
        {
            var row = recorder.Row(key);
            Assert.Equal("stopped early at Sections 1", row.Reason);
            V31Contract.AssertSentence(new SourceRefusal("S04-001", row.Reason!, ""));
            Assert.DoesNotContain("History", row.Reason!, StringComparison.Ordinal);
        }

        Assert.Equal("stopped early at Sections 1", (string?)recorder.Row("node.drafts").Detail["reason"]);
        Assert.Equal("until", (string?)result.Values["node.drafts"]!["stopped"]);
    }

    // ---- names --------------------------------------------------------------------------------------

    [Fact]
    public async Task ItemLabel_NamesItems_AndFallsBack()
    {
        var plan = RunFixture.Plan(NestedLoops(), NestedLoopsReads());
        var title = "Past medical history of Margaret Okafor " + new string('x', 160);
        Assert.Equal(200, title.Length);
        var host = new FakeRunHost(auto: true)
            .Reply("node.plan", SectionPlan(title, "   "))
            .Reply("node.sections.outline", """{"points":["Onset"]}""");
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var named = recorder.Row("node.sections:1").Detail;
        var name = NodeRowTests.ItemName(recorder.Row("node.sections:1"))!; // #1506 (D2): drawn from a model's reply, so on the settled text
        Assert.Null(named["itemName"]);
        Assert.Equal(CanonicalJson.HashText(name), (string?)named["itemNameHash"]);
        Assert.Equal(120, name.Length);
        Assert.EndsWith("…", name, StringComparison.Ordinal);
        Assert.Equal(title[..119] + "…", name);
        Assert.False((bool)named["positionName"]!);

        var blank = recorder.Row("node.sections:2").Detail;
        Assert.Equal("Sections 2", (string?)blank["itemName"]);
        Assert.True((bool)blank["positionName"]!);
        V31Contract.AssertSentence(new SourceRefusal("S04-004", "Sections 2", ""));

        var items = result.Values["node.sections"]!["items"]!.AsArray();
        Assert.Equal(name, (string?)items[0]!["label"]);
        Assert.Equal("Sections 2", (string?)items[1]!["label"]);

        // with itemLabel null, items take position names
        Assert.Equal("Paragraphs 1", NodeRowTests.ItemName(recorder.Row("node.sections.paragraphs:1/1")));
        Assert.True((bool)recorder.Row("node.sections.paragraphs:1/1").Detail["positionName"]!);
    }

    // ---- sources ------------------------------------------------------------------------------------

    [Fact]
    public async Task ASnippetFolderLoop_ReadsHeaderAndBody()
    {
        var plan = RunFixture.Plan(ChecksPerItem(), ChecksPerItemReads());
        var host = ConceptsHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var call = host.Calls.Single(c => c.Key == "node.standards.section:hpi");
        Assert.Equal(
            """{"item":{"content":"Chronological prose narrative. Open with a one-sentence orientation to the patient.\n","value":{"title":"History of Present Illness"}}}""",
            call.Scope.ToJsonString());
        Assert.Contains("node.standards.section:assessment_plan", host.Dispatched);

        var items = result.Values["node.standards"]!["items"]!.AsArray();
        Assert.Equal(["hpi", "assessment_plan"], items.Select(i => (string)i!["item"]!["id"]!));
        Assert.Equal(
            """{"content":"One assessment, then a numbered plan.\n","id":"assessment_plan","value":{"title":"Assessment and Plan"}}""",
            CanonicalJson.Write(items[1]!["item"]));
        Assert.Equal(["History of Present Illness", "Assessment and Plan"], items.Select(i => (string)i!["label"]!));
        Assert.Equal("Text of node.standards.section:hpi.", (string?)items[0]!["nodes"]!["section"]!["value"]);
        Assert.Equal("completed", (string?)items[0]!["nodes"]!["section_terms"]!["state"]);
        Assert.Equal(RowState.Completed, recorder.Row("node.standards:hpi").State);
    }

    [Fact]
    public async Task ACheckLoop_RunsEachItemAsItsProductionItemSettles()
    {
        // The production loop's body here is two agents, so each check can overlap production; the functions golden's call form
        // is PlanRunnerCallsTests.ACheckLoop_OverACallsLoop_TakesEachItemAsItsCallSettles.
        var plan = RunFixture.Plan(ChecksPerItem(), ChecksPerItemReads());
        var host = ConceptsHost(auto: false);
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            host.Release("node.plan");
            host.Release("node.extract_input_terms");

            // production item hpi settles: its check runs at once, before assessment_plan is released
            host.Release("node.standards.section:hpi");
            Assert.DoesNotContain(recorder.Rows, r => r.Key == "check.section_coverage.coverage:hpi");
            host.Release("node.standards.section_terms:hpi");
            Assert.Equal(RowState.Completed, recorder.Row("node.standards:hpi").State);
            Assert.Equal(RowState.Completed, recorder.Row("check.section_coverage.coverage:hpi").State);
            Assert.DoesNotContain(recorder.Rows, r => r.Key.StartsWith("check.section_coverage.coverage:assessment_plan", StringComparison.Ordinal));
            Assert.Contains("node.standards.section:assessment_plan", host.Held);

            host.Release("node.standards.section:assessment_plan");
            host.Release("node.standards.section_terms:assessment_plan");
            Assert.Equal(RowState.Completed, recorder.Row("check.section_coverage.coverage:assessment_plan").State);
            host.ReleaseAll();
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);

        // check items share the production items' ids, and their item is the .items element
        var checks = result.Values["check.section_coverage"]!["items"]!.AsArray();
        Assert.Equal(["hpi", "assessment_plan"], checks.Select(i => (string)i!["item"]!["id"]!));
        Assert.Equal(["History of Present Illness", "Assessment and Plan"], checks.Select(i => (string)i!["label"]!));
        // the element's item is the item the body iterated: the production element with its id, not wrapped again
        Assert.Equal("completed", (string?)checks[0]!["item"]!["state"]);
        Assert.Equal("hpi", (string?)checks[0]!["item"]!["item"]!["id"]);
        Assert.Equal("Text of node.standards.section:hpi.", (string?)checks[0]!["item"]!["value"]);
        Assert.True((bool)checks[0]!["value"]!["pass"]!);
        Assert.Equal(RowState.Completed, recorder.Row("check.section_coverage:assessment_plan").State);
        Assert.True((bool)result.Values["check.all_sections"]!["value"]!["pass"]!);
    }

    [Fact]
    public async Task ALoopOverItems_RecordsTheItemItsBodyRead()
    {
        // a production loop chained over the loop's .items: its body's item and its .items element's item are one JSON
        var files = ChecksPerItem()
            .With("nodes/index.json", Index(["plan", "standards", "extract_input_terms", "extract_note_terms", "summaries"]))
            .With("nodes/summaries/index.json", Json(SourceFixture.Node("for", "Summaries", ArrayOf(Plain), false, true,
                "\"in\": \"node.standards.items\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"label\", \"nodes\": [\"summary\"], \"ret\": \"node.summaries.summary\"")))
            .With("nodes/summaries/summary.md", Agent("Summarising a section", Plain,
                "Summarise the section {{ item.label }}: {{ item.value }}\n"));
        var plan = RunFixture.Plan(files, new StubTemplates(new Dictionary<string, string[]>
        {
            ["node.plan"] = ["input.consult_draft.content"],
            ["node.extract_input_terms"] = ["input.consult_draft.content"],
            ["node.extract_note_terms"] = ["node.standards.content"],
            ["node.standards.section"] = ["item.value.title", "item.content"],
            ["node.standards.section_terms"] = ["item.value.title"],
            ["node.summaries.summary"] = ["item"],
            ["snippets/standards/hpi.md"] = [],
            ["snippets/standards/assessment_plan.md"] = [],
            ["deliverable.emr.consult"] = ["node.standards.content"],
        }, everyBody: false));
        var host = ConceptsHost(auto: true);

        var result = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var items = result.Values["node.summaries"]!["items"]!.AsArray();
        Assert.Equal(["hpi", "assessment_plan"], items.Select(i => (string)i!["item"]!["id"]!));
        for (var k = 0; k < 2; k++)
        {
            var bound = host.Calls.Single(c => c.Key == $"node.summaries.summary:{(string)items[k]!["item"]!["id"]!}").Scope["item"];
            Assert.Equal(CanonicalJson.Write(bound), CanonicalJson.Write(items[k]!["item"]));
        }

        Assert.Equal("Text of node.standards.section:hpi.", (string?)items[0]!["item"]!["value"]);
    }

    // ---- reading a loop -----------------------------------------------------------------------------

    [Fact]
    public async Task ALoopsContent_IsItsItemsUnderTheirNames()
    {
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var host = new FakeRunHost(auto: true)
            .Reply("node.drafts.draft:1", "Margaret Okafor reports three months of fatigue.")
            .Reply("node.drafts.draft:2", "Pale; thyroid not enlarged.")
            .Reply("node.drafts.draft:3", "Repeat TSH in six weeks with Dr A. Sample.");
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Sections(Rows("History", "Examination", "Plan")), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        const string Expected = "## History\n\nMargaret Okafor reports three months of fatigue.\n\n"
            + "## Examination\n\nPale; thyroid not enlarged.\n\n"
            + "## Plan\n\nRepeat TSH in six weeks with Dr A. Sample.";
        Assert.Equal(Expected, (string?)result.Values["node.drafts"]!["content"]);
        Assert.Equal(CanonicalJson.HashText(Expected), recorder.Row("node.drafts").ValueHash);
        Assert.Equal("Rendered deliverable.note.", (string?)result.Values["deliverable.note"]!["content"]);
        Assert.Equal(Expected, (string?)host.Renders.Single().Scope["node"]!["drafts"]!["content"]);
        Assert.Equal(CanonicalJson.HashText("Pale; thyroid not enlarged."), recorder.Row("node.drafts:2").ValueHash);
    }

    [Fact]
    public async Task ASkippedRet_MakesTheItemSkipped()
    {
        var files = InputLoop(Members(), Md(
            SourceFixture.Node("agent", "Drafting a section", Plain, false, true,
                "\"if\": { \"===\": [{ \"var\": \"item.value.kind\" }, \"narrative\"] }, "
                + "\"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
            "Write the section '{{ item.value.title }}'.\n"));
        var plan = RunFixture.Plan(files, InputLoopReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();
        var rows = new JsonArray(Row("History"), Row("Results", "table"), Row("Plan"));

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Sections(rows), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var read = result.Values["node.drafts"]!;
        Assert.Equal(["Text of node.drafts.draft:1.", "Text of node.drafts.draft:3."], read["value"]!.AsArray().Select(v => (string)v!));
        var skipped = read["items"]!.AsArray()[1]!;
        Assert.Equal("skipped", (string?)skipped["state"]);
        Assert.Equal("skipped: item.value.kind = \"narrative\" is false", (string?)skipped["reason"]);
        V31Contract.AssertSentence(new SourceRefusal("S04-003", (string)skipped["reason"]!, ""));
        Assert.Null(skipped["value"]);
        Assert.Null(skipped["content"]);
        Assert.Equal("skipped", (string?)skipped["nodes"]!["draft"]!["state"]);
        Assert.Equal("2", (string?)skipped["item"]!["id"]);
        Assert.Equal(RowState.Skipped, recorder.Row("node.drafts:2").State);
        Assert.Equal("skipped: item.value.kind = \"narrative\" is false", recorder.Row("node.drafts:2").Reason);
        Assert.Equal("## History\n\nText of node.drafts.draft:1.\n\n## Plan\n\nText of node.drafts.draft:3.", (string?)read["content"]);
    }

    // ---- failures -----------------------------------------------------------------------------------

    [Fact]
    public async Task AFailedItem_FailsTheJob()
    {
        var plan = RunFixture.Plan(InputLoop(Members()), InputLoopReads());
        var host = new FakeRunHost(auto: true).Agent("node.drafts.draft:2", new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 0, "error"));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(ServiceAttempts: 1), host, recorder).RunAsync(Sections(Rows("History", "Examination", "Plan")), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.drafts.draft:2", result.FailedKey);
        V31Contract.AssertSentence(new SourceRefusal("S09-011", result.Sentence!, ""));
        Assert.DoesNotContain(recorder.Rows, r => r.Key == "node.drafts");
        Assert.Empty(host.Renders);
    }

    // ---- scale --------------------------------------------------------------------------------------

    [Fact]
    public async Task ALoopOf200Items_WithThreeBodyNodes_FinishesInSeconds()
    {
        var files = InputLoop(Members(), body: ["first", "second", "third"]);
        var plan = RunFixture.Plan(files, InputLoopReads(["first", "second", "third"]));
        var host = new FakeRunHost(auto: true);
        var rows = new JsonArray(Enumerable.Range(1, 200).Select(i => (JsonNode?)Row($"Section {i}")).ToArray());

        var clock = Stopwatch.StartNew();
        var result = await new PlanRunner(plan, new RunLimits(JobAgentCalls: 1000), host, new ListRecorder()).RunAsync(Sections(rows), FakeRunHost.Start);
        clock.Stop();

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(601, result.AgentCalls);
        Assert.Equal(200, result.Values["node.drafts"]!["value"]!.AsArray().Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"The run took {clock.Elapsed}.");
    }

    // ---- fixtures -----------------------------------------------------------------------------------

    /// <summary><see cref="ResolveFixture.NestedNote"/> without its while: the plan, the count, and the two loops; the note reads the sections.</summary>
    internal static Dictionary<string, byte[]> NestedLoops() => ResolveFixture.NestedNote()
        .Without("nodes/refine/index.json")
        .Without("nodes/refine/draft_summary.md")
        .Without("nodes/refine/judge.md")
        .With("nodes/index.json", Index(["plan", "section_count", "sections"]))
        .With("deliverables/note.md", Md(
            Deliverable("Nested consultation note", "\"markdown_block\"", "null", "hold"),
            "{{ node.sections.content }}\n"));

    internal static StubTemplates NestedLoopsReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["node.sections.outline"] = ["item.value.title"],
        ["node.sections.paragraphs.draft_paragraph"] = ["item.parent.value.title", "item.value"],
        ["deliverable.note"] = ["node.sections.content"],
    }, everyBody: false);

    /// <summary>A <c>section_plan</c> reply with a narrative section for each title.</summary>
    private static string SectionPlan(params string[] titles) =>
        new JsonObject { ["sections"] = new JsonArray(titles.Select(t => (JsonNode?)new JsonObject { ["title"] = t, ["kind"] = "narrative" }).ToArray()) }
            .ToJsonString();

    /// <summary>The section of a draft_paragraph's or outline's key (<c>node.sections.outline:3</c>, <c>…draft_paragraph:3/2</c>), else null.</summary>
    private static string? SectionOf(string key) =>
        key.StartsWith("node.sections.", StringComparison.Ordinal) && key.IndexOf(':') is var colon and >= 0
            ? key[(colon + 1)..].Split('/')[0]
            : null;

    /// <summary>A loop's members after <c>in</c>: <c>parallel</c>, <c>maxParallel</c>, <c>until</c>, <c>max</c> and <c>itemLabel</c>.</summary>
    private static string Members(bool parallel = true, string max = "null", string itemLabel = "\"title\"", string maxParallel = "null", string until = "null") =>
        $"\"parallel\": {(parallel ? "true" : "false")}, \"maxParallel\": {maxParallel}, \"until\": {until}, \"max\": {max}, \"itemLabel\": {itemLabel}";

    /// <summary>
    /// <see cref="SourceFixture.Minimal"/> with the optional list input <c>sections</c> (of <c>section_request</c>) and the
    /// loop <c>drafts</c> over it with <paramref name="members"/>, whose body is <paramref name="body"/> (each an agent
    /// reading <c>item.value.title</c>; <paramref name="draft"/> the file of a body of one, <c>draft</c>), returning the
    /// first; the note reads the loop's <c>.content</c>.
    /// </summary>
    private static Dictionary<string, byte[]> InputLoop(string members, string? draft = null, string[]? body = null)
    {
        body ??= ["draft"];
        var files = ResolveFixture.WithFormats(Minimal(), "section_request")
            .WithJson("manifest.json", m => m["inputs"]!.AsObject()["sections"] = JsonNode.Parse(
                "{ \"label\": \"Sections\", \"description\": null, "
                + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"section_request\" } }, \"required\": false, \"channel\": null }"))
            .With("nodes/index.json", Index(["plan", "drafts"]))
            .With("nodes/drafts/index.json", Json(SourceFixture.Node("for", "Sections", ArrayOf(Plain), false, true,
                $"\"in\": \"input.sections.value\", {members}, "
                + $"\"nodes\": [{string.Join(", ", body.Select(b => $"\"{b}\""))}], \"ret\": \"node.drafts.{body[0]}\"")))
            .With("deliverables/note.md", Md(Deliverable("Consultation note", "\"markdown_block\"", "null", "hold"), "{{ node.drafts.content }}\n"));
        foreach (var id in body)
            files.With($"nodes/drafts/{id}.md", Agent($"Drafting a section ({id})", Plain, "Write the section '{{ item.value.title }}'.\n"));
        if (draft != null)
            files.With("nodes/drafts/draft.md", draft);
        return files;
    }

    private static StubTemplates InputLoopReads(string[]? body = null)
    {
        var reads = new Dictionary<string, string[]>
        {
            ["node.plan"] = ["input.consult_draft.content"],
            ["deliverable.note"] = ["node.drafts.content"],
        };
        foreach (var id in body ?? ["draft"])
            reads[$"node.drafts.{id}"] = ["item.value.title"];
        return new StubTemplates(reads, everyBody: false);
    }

    /// <summary>A <c>section_request</c> row.</summary>
    private static JsonObject Row(string title, string kind = "narrative") => new() { ["title"] = title, ["kind"] = kind };

    private static JsonArray Rows(params string[] titles) => new(titles.Select(t => (JsonNode?)Row(t)).ToArray());

    /// <summary>The consult draft, and the input <c>sections</c> supplied as <paramref name="value"/>.</summary>
    private static RunInputs Sections(JsonNode value) => Supplied("sections", value);

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

    /// <summary>
    /// The list input <c>visits</c> (of <c>visit_request</c>: a <c>title</c> and a list of <c>points</c>) and the loop
    /// <c>visits</c> over it, whose body is the loop <c>points</c> over <c>item.value.points</c>, drafting a note per point.
    /// </summary>
    private static Dictionary<string, byte[]> Visits() => Minimal()
        .WithJson("manifest.json", m => m["inputs"]!.AsObject()["visits"] = JsonNode.Parse(
            "{ \"label\": \"Visits\", \"description\": null, "
            + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"visit_request\" } }, \"required\": true, \"channel\": null }"))
        .With("formats/index.json", Index(["visit_request"]))
        .With("formats/visit_request.json", Json(
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"title\", \"points\"], "
            + "\"additionalProperties\": false, \"properties\": { \"title\": { \"type\": \"string\", \"title\": \"Title\" }, "
            + "\"points\": { \"type\": \"array\", \"items\": { \"type\": \"string\" }, \"title\": \"Points\" } } }"))
        .With("nodes/index.json", Index(["plan", "visits"]))
        .With("nodes/visits/index.json", Json(SourceFixture.Node("for", "Visits", ArrayOf(ArrayOf(Plain)), false, true,
            "\"in\": \"input.visits.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
            + "\"itemLabel\": \"title\", \"nodes\": [\"points\"], \"ret\": \"node.visits.points\"")))
        .With("nodes/visits/points/index.json", Json(SourceFixture.Node("for", "Points", ArrayOf(Plain), false, true,
            "\"in\": \"item.value.points\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
            + "\"itemLabel\": null, \"nodes\": [\"note\"], \"ret\": \"node.visits.points.note\"")))
        .With("nodes/visits/points/note.md", Agent("Noting a point", Plain,
            "Note the point {{ item.value }} of {{ item.parent.value.title }}.\n"));

    private static StubTemplates VisitsReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["node.visits.points.note"] = ["item.parent.value.title", "item.value"],
        ["deliverable.note"] = ["node.plan.content"],
    }, everyBody: false);

    private static JsonObject Visit(string title, params string[] points) =>
        new() { ["title"] = title, ["points"] = new JsonArray(points.Select(p => (JsonNode?)p).ToArray()) };

    /// <summary>
    /// Part 04 § 2.4's example: the list input <c>prior_letters</c> and the sequential loop <c>letters</c> over it, reading
    /// each letter (<c>read_letter</c>, of <c>letter_reading</c>) and summarising it (<c>letter_summary</c>, its ret),
    /// until a letter answers the referral question.
    /// </summary>
    internal static Dictionary<string, byte[]> PriorLetters() => Minimal()
        .WithJson("manifest.json", m => m["inputs"]!.AsObject()["prior_letters"] = JsonNode.Parse(
            "{ \"label\": \"Prior letters\", \"description\": null, "
            + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }, \"required\": true, \"channel\": null }"))
        .With("formats/index.json", Index(["letter_reading"]))
        .With("formats/letter_reading.json", Json(
            "{ \"$schema\": \"urn:consultologist:spec:31:dialect\", \"type\": \"object\", \"required\": [\"answers_question\"], "
            + "\"additionalProperties\": false, \"properties\": { \"answers_question\": { \"type\": \"boolean\" } } }"))
        .With("nodes/index.json", Index(["plan", "letters"]))
        .With("nodes/letters/index.json", Json(SourceFixture.Node("for", "Prior letters", ArrayOf(Plain), false, true,
            "\"in\": \"input.prior_letters.value\", \"parallel\": false, \"maxParallel\": null, "
            + "\"until\": { \"===\": [ { \"var\": \"node.read_letter.value.answers_question\" }, true ] }, "
            + "\"max\": null, \"itemLabel\": null, \"nodes\": [\"read_letter\", \"letter_summary\"], "
            + "\"ret\": \"node.letters.letter_summary\"")))
        .With("nodes/letters/read_letter.md", Agent("Reading a letter", "\"letter_reading\"",
            "Say whether this letter answers the referral question.\n\n{{ item.value }}\n"))
        .With("nodes/letters/letter_summary.md", Agent("Summarising a letter", Plain,
            "Summarise this letter in one sentence.\n\n{{ item.value }}\n"));

    internal static StubTemplates PriorLettersReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["node.letters.read_letter"] = ["item.value"],
        ["node.letters.letter_summary"] = ["item.value"],
        ["deliverable.note"] = ["node.plan.content"],
    }, everyBody: false);

    private static RunInputs Letters(JsonArray letters)
    {
        var inputs = PlanRunnerLeavesTests.Inputs();
        var supplied = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
        {
            ["prior_letters"] = new JsonObject { ["value"] = letters, ["content"] = null, ["title"] = null },
        };
        return inputs with { Inputs = supplied };
    }

    /// <summary>
    /// <see cref="ResolveFixture.Functions"/> with the loop <c>standards</c>'s body made two agents, <c>section</c> (its
    /// ret, reading the folder item's title and body) and <c>section_terms</c>, in place of the call,
    /// and the function removed; the check <c>coverage</c> reads its item's <c>section_terms</c>, so each check runs as its
    /// production item settles.
    /// </summary>
    internal static Dictionary<string, byte[]> ChecksPerItem()
    {
        var files = ResolveFixture.Functions();
        foreach (var path in files.Keys.Where(k => k.StartsWith("functions/draft_section/", StringComparison.Ordinal)).ToList())
            files.Without(path);
        return files
            .With("functions/index.json", Index([]))
            .With("nodes/standards/index.json", Json(SourceFixture.Node("for", "Standards", ArrayOf(Plain), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": 4, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"section\", \"section_terms\"], \"ret\": \"node.standards.section\"")))
            .With("nodes/standards/section.md", Agent("Drafting the section", Plain,
                "Draft the section '{{ item.value.title }}' following these instructions.\n\n{{ item.content }}\n"))
            .With("nodes/standards/section_terms.md", Agent("Extracting the section's terms", "\"concept_list\"",
                "List the clinical terms the section '{{ item.value.title }}' should cover.\n"))
            .With("checks/section_coverage/coverage.md", Md(
                SourceFixture.Node("computed", "Terms covered", "\"check_result\"", true, true,
                    "\"if\": { \"===\": [{ \"var\": \"item.state\" }, \"completed\"] }, "
                    + "\"expression\": { \"terms_subset\": [{ \"var\": \"item.nodes.section_terms.value\" }, "
                    + "{ \"var\": \"node.extract_input_terms.value\" }, "
                    + "\"The section does not cover every clinical term found in the draft.\"] }"),
                ""));
    }

    internal static StubTemplates ChecksPerItemReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["node.extract_input_terms"] = ["input.consult_draft.content"],
        ["node.extract_note_terms"] = ["node.standards.content"],
        ["node.standards.section"] = ["item.value.title", "item.content"],
        ["node.standards.section_terms"] = ["item.value.title"],
        ["snippets/standards/hpi.md"] = [],
        ["snippets/standards/assessment_plan.md"] = [],
        ["deliverable.emr.consult"] = ["node.standards.content"],
    }, everyBody: false);

    /// <summary>A host whose concept-list agents reply an empty list.</summary>
    private static FakeRunHost ConceptsHost(bool auto) => new FakeRunHost(auto)
        .Reply("node.extract_input_terms", Concepts)
        .Reply("node.extract_note_terms", Concepts)
        .Reply("node.standards.section_terms", Concepts);
}
