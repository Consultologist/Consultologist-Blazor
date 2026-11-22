using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 4, 6, 11; part 06 §§ 2.1, 5; D7, D8, D9): a check's row is part 08 § 6's
/// row. <see cref="RunRow.ToJson"/> writes its members in order: what it checked (the one deliverable it reads, with its
/// composed hash; else the one node), its other reads by hash, its <c>check_result</c>'s verdict, sentence and counts, its
/// lists by hash and size only (the lists themselves are retained text, on <see cref="RunRow.Text"/>), and its effect,
/// written on its one row: a gate's row waits for the deliverables it gates. A computed row's <c>inputHash</c> covers its
/// rule and the values it read, and its run-neutral hash leaves out the run-dependent reads' values. Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class CheckRowTests
{
    private const string Plain = "\"plaintext\"";

    /// <summary>Part 08 § 6's members of a check row, in order.</summary>
    private static readonly string[] CheckMembers =
    [
        "key", "kind", "label", "status", "itemName", "itemNameHash", "alignedWith", "checked", "reads", "inputHash", "outputHash",
        "runNeutralHash", "producer", "modelDerived", "runDependent", "profileCarrying", "pass", "failWith", "passed", "total",
        "uncoveredHash", "uncoveredSize", "untestedHash", "untestedSize", "effect", "count", "startedAtUtc", "completedAtUtc", "durationMs", "stateReason",
    ];

    // ---- the members ----------------------------------------------------------------------------

    [Fact]
    public async Task ACheckRow_HasPart08sMembers()
    {
        // checked-note on the default stubs: no section covers its planned terms, so all_sections_covered fails with its
        // sentence and proofread (whose object writes message null) fails; the note is blocked
        var run = await RunGoldenTests.Run(RunFixture.Plan("checked-note"), new RunLimits(), seed: null);

        var row = run.Recorder.Row("check.proofread").ToJson();

        Assert.Equal(CheckMembers, row.Select(m => m.Key));
        Assert.Equal("check.proofread", (string?)row["key"]);
        Assert.Equal("computed", (string?)row["kind"]);
        Assert.Equal("Proofreading the note", (string?)row["label"]);
        Assert.Equal("Completed", (string?)row["status"]);
        Assert.Equal("computed", (string?)row["producer"]);
        Assert.True((bool)row["modelDerived"]!);
        Assert.False((bool)row["runDependent"]!);
        Assert.Null(row["alignedWith"]);
        Assert.Null(row["itemName"]);
        Assert.Null(row["count"]);
        Assert.Null(row["stateReason"]);
        Assert.Equal(run.Recorder.Row("check.proofread").ValueHash, (string?)row["outputHash"]);
        Assert.StartsWith("sha256:", (string?)row["inputHash"], StringComparison.Ordinal);

        // D8: it checked the note, by its composed hash; every other read is in reads
        var note = run.Recorder.Row("deliverable.note");
        Assert.Equal("deliverable.note", (string?)row["checked"]!["ref"]);
        Assert.Equal((string?)note.Detail["composedHash"], (string?)row["checked"]!["hash"]);
        var reads = row["reads"]!.AsArray().Select(r => r!.AsObject()).ToList();
        Assert.Equal(["check.all_sections_covered.value"], reads.Select(r => (string)r["path"]!));
        Assert.Equal(CanonicalJson.Hash(run.Result.Values["check.all_sections_covered"]!["value"]), (string?)reads[0]["hash"]);

        // the verdict, and failWith the check_result's message when it did not pass
        Assert.False((bool)row["pass"]!);
        Assert.Null(row["failWith"]); // proofread's object writes message: null
        var aggregate = run.Recorder.Row("check.all_sections_covered").ToJson();
        Assert.Equal(CheckMembers, aggregate.Select(m => m.Key));
        Assert.False((bool)aggregate["pass"]!);
        Assert.Equal("Not every section covers its planned terms.", (string?)aggregate["failWith"]);
        Assert.Null(aggregate["checked"]); // it reads a check, neither a deliverable nor a node

        // a check that passed has no failWith
        var functions = await RunGoldenTests.Run(RunFixture.Plan("functions"), new RunLimits(), seed: null);
        var passed = functions.Recorder.Row("check.all_sections").ToJson();
        Assert.True((bool)passed["pass"]!);
        Assert.Null(passed["failWith"]);
    }

    [Fact]
    public async Task ACheckResultsLists_AreByHash()
    {
        var run = await RunGoldenTests.Run(RunFixture.Plan("functions"), new RunLimits(), seed: null);

        // terms_subset: the lists by hash and size, the lists themselves on the settled text only
        var coverage = run.Recorder.Row("check.section_coverage.coverage:hpi");
        var json = coverage.ToJson();
        var result = JsonNode.Parse((string)coverage.Text!["value"]!)!.AsObject();
        // on the stubs every planned term is covered (uncovered null) and three are untested
        Assert.Null(result["uncovered"]);
        var untested = result["untested"]!.AsArray();
        Assert.Equal(3, untested.Count);
        Assert.Equal(CanonicalJson.Hash(untested), (string?)json["untestedHash"]);
        Assert.Equal(untested.Count, (int)json["untestedSize"]!);
        Assert.Equal(CanonicalJson.Write(untested), CanonicalJson.Write(coverage.Text["untested"]));
        Assert.Null(json["uncoveredHash"]); // a null list: no hash, no size, nothing retained
        Assert.Null(json["uncoveredSize"]);
        Assert.False(coverage.Text.ContainsKey("uncovered"));
        Assert.True((bool)json["pass"]!);
        Assert.DoesNotContain("stub ", json.ToJsonString(), StringComparison.Ordinal);
        Assert.Null(json["passed"]);
        Assert.Null(json["total"]);
        Assert.False(json.ContainsKey("uncovered"));
        Assert.False(json.ContainsKey("untested"));
        Assert.Equal("node.standards:hpi", (string?)json["alignedWith"]);

        // all_pass: the counts on the row, no lists (a null list has a null hash and size)
        var aggregate = run.Recorder.Row("check.all_sections");
        var counts = aggregate.ToJson();
        var value = run.Result.Values["check.all_sections"]!["value"]!;
        Assert.Equal((int)value["passed"]!, (int)counts["passed"]!);
        Assert.Equal((int)value["total"]!, (int)counts["total"]!);
        Assert.Equal(2, (int)counts["total"]!);
        foreach (var member in new[] { "uncoveredHash", "uncoveredSize", "untestedHash", "untestedSize" })
            Assert.Null(counts[member]);
        Assert.False(aggregate.Text?.ContainsKey("uncovered") ?? false);
    }

    // ---- effect ---------------------------------------------------------------------------------

    [Fact]
    public async Task AGatesEffect_IsItsDeliverablesState()
    {
        // released: functions' all_sections passes and the consult is released
        var functions = await RunGoldenTests.Run(RunFixture.Plan("functions"), new RunLimits(), seed: null);
        Assert.Equal("released", (string?)functions.Recorder.Row("check.all_sections").ToJson()["effect"]);
        AssertAfter(functions.Recorder, "check.all_sections", "deliverable.emr.consult");

        // blocked: checked-note's proofread fails and the note is blocked
        var checkedNote = await RunGoldenTests.Run(RunFixture.Plan("checked-note"), new RunLimits(), seed: null);
        Assert.Equal("blocked", (string?)checkedNote.Recorder.Row("check.proofread").ToJson()["effect"]);
        AssertAfter(checkedNote.Recorder, "check.proofread", "deliverable.note");

        // held: the gate passes, but its deliverable rendered only white space and is held under whenEmpty: hold
        var plan = RunFixture.Plan(Gated(["a"], "true"));
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true).Render("deliverable.a", Rendered(" \n")));
        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(RowState.Held, recorder.Row("deliverable.a").State);
        Assert.Equal("held", (string?)recorder.Row("check.gate").ToJson()["effect"]);
        AssertAfter(recorder, "check.gate", "deliverable.a");

        // two deliverables: each word with its key, in key order; the gate's row once, after both
        plan = RunFixture.Plan(Gated(["b", "a"], "false"));
        (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true)
            .Render("deliverable.a", Rendered("Margaret Okafor's plan: rest, fluids and a repeat TSH in six weeks."))
            .Render("deliverable.b", Rendered(" \n")));
        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(RowState.Blocked, recorder.Row("deliverable.a").State);
        Assert.Equal(RowState.Held, recorder.Row("deliverable.b").State);
        Assert.Single(recorder.Rows, r => r.Key == "check.gate");
        Assert.Equal("blocked deliverable.a; held deliverable.b", (string?)recorder.Row("check.gate").ToJson()["effect"]);
        AssertAfter(recorder, "check.gate", "deliverable.a");
        AssertAfter(recorder, "check.gate", "deliverable.b");
    }

    [Fact]
    public async Task ACheckThatIsRead_SaysByWhat()
    {
        var functions = await RunGoldenTests.Run(RunFixture.Plan("functions"), new RunLimits(), seed: null);
        Assert.Equal("read by check.all_sections", (string?)functions.Recorder.Row("check.section_coverage").ToJson()["effect"]);

        var checkedNote = await RunGoldenTests.Run(RunFixture.Plan("checked-note"), new RunLimits(), seed: null);
        Assert.Equal("read by check.proofread", (string?)checkedNote.Recorder.Row("check.all_sections_covered").ToJson()["effect"]);
        Assert.Equal("read by check.all_sections_covered", (string?)checkedNote.Recorder.Row("check.part_coverage").ToJson()["effect"]);

        // a check nothing reads, and that gates nothing
        var files = Minimal()
            .With("checks/index.json", Index(["seen"]))
            .With("checks/seen.md", Check("The note was composed", Result(NotBlank("deliverable.note.content")), modelDerived: true));
        var (result, recorder) = await RunAsync(RunFixture.Plan(files), new FakeRunHost(auto: true));
        Assert.Equal(RunState.Completed, result.State);
        var seen = recorder.Row("check.seen").ToJson();
        Assert.Null(seen["effect"]);
        Assert.Equal("deliverable.note", (string?)seen["checked"]!["ref"]);
        Assert.Equal((string?)recorder.Row("deliverable.note").Detail["composedHash"], (string?)seen["checked"]!["hash"]);
    }

    [Fact]
    public async Task ACheckLoopsItem_IsAlignedWithItsProductionItem()
    {
        var plan = RunFixture.Plan("checked-note");
        var host = RunFixture.Stubbed(plan).Reply("node.plan", PlacementDecisionsTests.Sections);
        var recorder = new ListRecorder();
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(RunFixture.GoldenInputs(), FakeRunHost.Start);
        Assert.True(result.State == RunState.Completed, $"{result.State}: {result.FailedKey} {result.Sentence}");

        var item = recorder.Row("check.part_coverage.coverage:2").ToJson();
        Assert.Equal(CheckMembers, item.Select(m => m.Key));
        Assert.Equal("computed", (string?)item["kind"]);
        Assert.Equal("node.sections:2", (string?)item["alignedWith"]);
        Assert.Equal("node.sections:1", (string?)recorder.Row("check.part_coverage.coverage:1").ToJson()["alignedWith"]);
        Assert.Null(item["itemName"]); // the production item's name is drawn from the model's plan: by hash only
        Assert.Equal(CanonicalJson.HashText("Plan"), (string?)item["itemNameHash"]);
        Assert.Null(item["count"]);

        // the loop's item row is aligned too; the loop's own row has its count
        Assert.Equal("node.sections:2", (string?)recorder.Row("check.part_coverage:2").ToJson()["alignedWith"]);
        var loop = recorder.Row("check.part_coverage").ToJson();
        Assert.Equal(CheckMembers, loop.Select(m => m.Key));
        Assert.Equal("for", (string?)loop["kind"]);
        Assert.Equal(2, (int)loop["count"]!);
        Assert.Null(loop["alignedWith"]);
        Assert.Null(loop["pass"]);
        Assert.Null(recorder.Row("check.all_sections_covered").ToJson()["alignedWith"]);
    }

    // ---- the hashes of a computed value (D9) ----------------------------------------------------

    [Fact]
    public async Task AComputedRowsInputHash_CoversItsRuleAndItsReads()
    {
        var files = Package()
            .WithComputed("when", Plain, "{ \"cat\": [{ \"var\": \"run.date\" }, \" / \", { \"plain\": [{ \"var\": \"input.consult_draft.content\" }] }] }", runDependent: true)
            .WithAgent("dated", "{{ node.when.value }}\n").RunDependent("nodes/dated.md");
        var plan = RunFixture.Plan(files);
        var rule = plan.Entries["node.when"].Rules["expression"].Json!;

        var first = await WhenRowAsync(plan, "2026-11-21");
        var reads = new[]
        {
            new RunRead("input.consult_draft.content", CanonicalJson.Hash(JsonValue.Create(PlanRunnerLeavesTests.Draft))),
            new RunRead("run.date", CanonicalJson.Hash(JsonValue.Create("2026-11-21"))),
        };
        Assert.Equal(RecordRows.ComputedInputHash(rule, reads), (string?)first["inputHash"]);
        Assert.Equal(RecordRows.ComputedNeutralHash(rule, reads, ["run.date"]), (string?)first["runNeutralHash"]);
        Assert.NotEqual((string?)first["inputHash"], (string?)first["runNeutralHash"]);

        // the reads go in path order, whatever order they are given in
        Assert.Equal(RecordRows.ComputedInputHash(rule, reads), RecordRows.ComputedInputHash(rule, [reads[1], reads[0]]));

        // another run date: another value read, so another inputHash; the run-neutral hash is unchanged
        var second = await WhenRowAsync(plan, "2026-11-22");
        Assert.NotEqual((string?)first["inputHash"], (string?)second["inputHash"]);
        Assert.Equal((string?)first["runNeutralHash"], (string?)second["runNeutralHash"]);

        // a computed value that is not run-dependent has no run-neutral hash, and its inputHash is still taken
        var nested = await RunGoldenTests.Run(RunFixture.Plan("nested-note"), new RunLimits(), seed: null);
        var count = nested.Recorder.Row("node.section_count").ToJson();
        Assert.StartsWith("sha256:", (string?)count["inputHash"], StringComparison.Ordinal);
        Assert.Null(count["runNeutralHash"]);
    }

    // ---- a run that ends first ------------------------------------------------------------------

    [Fact]
    public async Task ARunThatFailsBeforeAGateSettles_StillRecordsTheCheck()
    {
        // the gate passes at once; the plan its deliverable reads fails, and the job with it, before the deliverable settles
        var plan = RunFixture.Plan(Gated(["a"], "true"));
        var host = new FakeRunHost(auto: true, plan).Agent("node.plan", new AgentOutcome(AgentOutcomeKind.Failed, null, 0, "compose"));
        var (result, recorder) = await RunAsync(plan, host);

        Assert.Equal(RunState.Failed, result.State);
        Assert.DoesNotContain(recorder.Rows, r => r.Key == "deliverable.a");
        var gate = Assert.Single(recorder.Rows, r => r.Key == "check.gate");
        var json = gate.ToJson();
        Assert.Equal("Completed", (string?)json["status"]);
        Assert.True((bool)json["pass"]!);
        Assert.Null(json["effect"]);
        Assert.Equal("check.gate", recorder.Rows[^1].Key); // recorded when the run ended
    }

    // ---- the clock (#1506, final review F3) ----------------------------------------------------------

    [Fact]
    public async Task ARowsClock_IsTheHostsWhenItStartedAndSettled()
    {
        // the plan dispatched at the start and answered 1.5 s later; the gate evaluated at once; the letter's render sent
        // when the plan settled and answered 250 ms later
        var plan = RunFixture.Plan(Gated(["a"], "true"));
        var host = new FakeRunHost(auto: false, plan);
        var recorder = new ListRecorder();

        var result = await FakeRunHost.Drive(() =>
        {
            var run = new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            host.Advance(TimeSpan.FromMilliseconds(1500));
            host.Release("node.plan");
            host.Advance(TimeSpan.FromMilliseconds(250));
            host.Release("deliverable.a");
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        static string At(double ms) => FakeRunHost.Start.AddMilliseconds(ms).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        static (string?, string?, long?) Clock(JsonObject row) => ((string?)row["startedAtUtc"], (string?)row["completedAtUtc"], (long?)row["durationMs"]);
        Assert.Equal((At(0), At(1500), 1500L), Clock(recorder.Row("node.plan").ToJson()));
        Assert.Equal((At(0), At(0), 0L), Clock(recorder.Row("check.gate").ToJson()));
        Assert.Equal((At(1500), At(1750), 250L), Clock(recorder.Row("deliverable.a").ToJson()));
    }

    [Fact]
    public async Task ARowThatNeverStarted_HasNoClock()
    {
        // the gate's if is false: skipped, never started
        var plan = RunFixture.Plan(Gated(["a"], "true").WithJson("checks/gate.md", h => h["if"] = JsonNode.Parse("false")));
        var (result, recorder) = await RunAsync(plan, new FakeRunHost(auto: true, plan));

        var gate = recorder.Row("check.gate").ToJson();
        Assert.Equal("Skipped", (string?)gate["status"]);
        Assert.Null(gate["startedAtUtc"]);
        Assert.Null(gate["completedAtUtc"]);
        Assert.Null(gate["durationMs"]);
        Assert.True(gate.ContainsKey("durationMs"));
        _ = result;
    }

    [Fact]
    public async Task ARunThatThrows_StillRecordsTheHeldGate_AndTheFaultPropagates()
    {
        // #1506 (final review F2): the host's own fault (an engine fault) throws out of the run; the gate's row is recorded
        // first, effect null, and the fault is the host's, not replaced
        var plan = RunFixture.Plan(Gated(["a"], "true"));
        var host = new FakeRunHost(auto: true, plan).Fault("node.plan");
        var recorder = new ListRecorder();

        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start));

        Assert.Equal("The fake host failed node.plan.", fault.Message);
        var gate = Assert.Single(recorder.Rows, r => r.Key == "check.gate");
        Assert.Null(gate.ToJson()["effect"]);
        Assert.DoesNotContain(recorder.Rows, r => r.Key == "deliverable.a");
    }

    [Fact]
    public async Task ARunThatTimesOut_RecordsTheHeldGateOnce()
    {
        // #1506 (final review F2; Task 5's review): the gate passed at once; the plan is still running when the time runs out
        var plan = RunFixture.Plan(Gated(["a"], "true"));
        var host = new FakeRunHost(auto: false, plan);
        var recorder = new ListRecorder();

        var result = await FakeRunHost.Drive(() =>
        {
            var run = new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            host.Advance(TimeSpan.FromMinutes(31));
            return run;
        });

        Assert.Equal(RunState.TimedOut, result.State);
        var gate = Assert.Single(recorder.Rows, r => r.Key == "check.gate");
        Assert.Null(gate.ToJson()["effect"]);
    }

    [Fact]
    public async Task ARunPastTheInstancesLimit_RecordsTheHeldGateOnce()
    {
        // #1506 (final review F2; Task 5's review): the gate passes at once; the loop its deliverable reads opens its items
        // once the plan is in, and the second item is one past the limit, before the deliverable settles
        var plan = RunFixture.Plan(GatedLoop());
        var inputs = PlanRunnerLeavesTests.Inputs();
        inputs = inputs with
        {
            Inputs = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
            {
                ["sections"] = new JsonObject
                {
                    ["value"] = new JsonArray(new JsonObject { ["title"] = "Plan", ["kind"] = "narrative" }, new JsonObject { ["title"] = "Follow-up", ["kind"] = "narrative" }),
                    ["content"] = null,
                    ["title"] = null,
                },
            },
        };
        var recorder = new ListRecorder();

        var whole = await new PlanRunner(plan, new RunLimits(), new FakeRunHost(auto: true, plan), new ListRecorder()).RunAsync(inputs, FakeRunHost.Start);
        Assert.Equal(RunState.Completed, whole.State);
        var result = await new PlanRunner(plan, new RunLimits(JobInstances: 5), new FakeRunHost(auto: true, plan), recorder)
            .RunAsync(inputs, FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal(RunSentences.JobInstances(5), result.Sentence);
        Assert.DoesNotContain(recorder.Rows, r => r.Key == "deliverable.a");
        var gate = Assert.Single(recorder.Rows, r => r.Key == "check.gate");
        Assert.True((bool)gate.ToJson()["pass"]!);
        Assert.Null(gate.ToJson()["effect"]);
    }

    // ---- fixtures -------------------------------------------------------------------------------

    private static async Task<JsonObject> WhenRowAsync(RunPlan plan, string date)
    {
        var inputs = PlanRunnerLeavesTests.Inputs() with { RunFacts = new JsonObject { ["date"] = date } };
        var (result, recorder) = await RunAsync(plan, RunFixture.Stubbed(plan), inputs);
        Assert.True(result.State == RunState.Completed, $"{result.State}: {result.FailedKey} {result.Sentence}");
        return recorder.Row("node.when").ToJson();
    }

    private static async Task<(RunResult Result, ListRecorder Recorder)> RunAsync(RunPlan plan, FakeRunHost host, RunInputs? inputs = null)
    {
        var recorder = new ListRecorder();
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs ?? PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        return (result, recorder);
    }

    /// <summary>The row of <paramref name="later"/> is recorded after that of <paramref name="earlier"/>.</summary>
    private static void AssertAfter(ListRecorder recorder, string later, string earlier)
    {
        var keys = recorder.Rows.Select(r => r.Key).ToList();
        Assert.True(keys.IndexOf(earlier) >= 0 && keys.IndexOf(earlier) < keys.IndexOf(later), $"{later} is not recorded after {earlier}: {string.Join(", ", keys)}");
    }

    private static RenderOutcome Rendered(string text) => new(text, false, null);

    /// <summary>A check file: a <c>computed</c> check of <paramref name="expression"/>.</summary>
    private static string Check(string label, string expression, bool modelDerived = false) =>
        Md(Node("computed", label, "\"check_result\"", true, modelDerived, $"\"if\": null, \"expression\": {expression}"), "");

    /// <summary>A <c>check_result</c> built by <c>object</c> whose <c>pass</c> is <paramref name="pass"/> (JSON Logic).</summary>
    private static string Result(string pass) =>
        $"{{ \"object\": {{ \"pass\": {pass}, \"message\": null, \"uncovered\": null, \"untested\": null, \"passed\": null, \"total\": null }} }}";

    /// <summary>JSON Logic: <paramref name="path"/> is not blank.</summary>
    private static string NotBlank(string path) => $"{{ \"!\": [{{ \"blank\": [{{ \"var\": \"{path}\" }}] }}] }}";

    /// <summary>
    /// Minimal whose deliverables <paramref name="ids"/> (in that order in the index), each reading the plan and held when
    /// empty, are all gated by the check <c>gate</c>, whose <c>pass</c> is <paramref name="pass"/>.
    /// </summary>
    /// <summary>
    /// <see cref="Gated"/> with one deliverable, <c>a</c>, reading the loop <c>drafts</c> over the list input <c>sections</c>,
    /// over <c>wanted</c>, a computed node that reads the plan, so the loop runs only once the plan is in, so its items open after the gate has settled.
    /// </summary>
    private static Dictionary<string, byte[]> GatedLoop() =>
        Consultologist.Api.Tests.V31Resolve.ResolveFixture.WithFormats(Gated(["a"], "true"), "section_request")
            .WithJson("manifest.json", m => m["inputs"]!.AsObject()["sections"] = JsonNode.Parse(
                "{ \"label\": \"Sections\", \"description\": null, "
                + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"section_request\" } }, \"required\": true, \"channel\": null }"))
            .With("nodes/index.json", Index(["plan", "wanted", "drafts"]))
            .With("nodes/wanted.md", Md(Node("computed", "The sections wanted", ArrayOf("section_request"), true, true,
                "\"if\": null, \"expression\": { \"if\": [{ \"blank\": [{ \"var\": \"node.plan.content\" }] }, [], "
                + "{ \"var\": \"input.sections.value\" }] }"), ""))
            .With("nodes/drafts/index.json", Json(Node("for", "Sections", ArrayOf(Plain), false, true,
                "\"in\": \"node.wanted.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"draft\"], \"ret\": \"node.drafts.draft\"")))
            .With("nodes/drafts/draft.md", Agent("Drafting a section", Plain, "Write the section '{{ item.value.title }}'.\n"))
            .With("deliverables/a.md", Md(Deliverable("Letter a", Plain, "\"check.gate\"", "hold"), "{{ node.drafts.content }}\n"));

    internal static Dictionary<string, byte[]> Gated(string[] ids, string pass)
    {
        var files = Minimal()
            .Without("deliverables/note.md")
            .With("checks/index.json", Index(["gate"]))
            .With("checks/gate.md", Check("The gate", Result(pass)))
            .With("deliverables/index.json", Index(ids, extra: ", \"delivery\": [], \"bundle\": null"));
        foreach (var id in ids)
            files.With($"deliverables/{id}.md", Md(Deliverable($"Letter {id}", Plain, "\"check.gate\"", "hold"), "{{ node.plan.content }}\n"));
        return files;
    }
}
