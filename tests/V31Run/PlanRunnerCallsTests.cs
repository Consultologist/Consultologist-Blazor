using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 04 §§ 7.4, 7.7, 7.9, 8; part 08 § 7; part 09 § 3.1): <see cref="PlanRunner"/> on a
/// <c>call</c>. A call runs its function's body inline, in a frame of its own: <c>param.&lt;id&gt;</c> bound from its
/// arguments, <c>node.&lt;id&gt;</c> the function's own nodes and never the caller's, the caller's <c>item</c> and
/// <c>pass</c> out of sight. Its nodes are keyed through the call node, ranked under the call and scheduled by the same
/// rounds, so the ceiling, the call limit and the fixed order hold inside it; its row has the function, its depth and its
/// arguments' hashes, and its value is the function's <c>ret</c>'s. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PlanRunnerCallsTests
{
    private const string Concepts = """{"concepts":[]}""";

    private const string HpiBody = "Chronological prose narrative. Open with a one-sentence orientation to the patient.\n";

    private const string PlanBody = "One assessment, then a numbered plan.\n";

    private static readonly string[] Standards = ["hpi", "assessment_plan"];

    private static readonly string[] Body = ["standard_draft", "patient_draft", "apply_instructions"];

    // ---- a call in a loop -------------------------------------------------------------------------

    [Fact]
    public async Task ACallInALoop_RunsTheFunctionPerItem()
    {
        var plan = RunFixture.Plan("functions");
        var host = ConceptsHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);

        // each item runs the function's three agents, keyed through the call, entry keys the function's own, in the fixed
        // order of part 09 § 3.2: the node's place (the call's order, then the node's in the function), then the item path
        string[] inner = [.. Body.SelectMany(n => Standards.Select(s => $"node.standards.section.{n}:{s}"))];
        Assert.Equal(["node.plan", .. inner.Where(k => !k.Contains("apply_instructions", StringComparison.Ordinal)), "node.extract_input_terms",
                .. inner.Where(k => k.Contains("apply_instructions", StringComparison.Ordinal)), "node.extract_note_terms"],
            host.Dispatched);
        Assert.Equal("node.standards.section.standard_draft:assessment_plan", host.Dispatched[2]);
        foreach (var standard in Standards)
        {
            foreach (var node in Body)
            {
                var call = host.Calls.Single(c => c.Key == $"node.standards.section.{node}:{standard}");
                Assert.Equal($"node.{node}", call.EntryKey);
                Assert.Equal("draft_section", call.Function);
                Assert.Same(plan.Functions["draft_section"].Entries[call.EntryKey], plan.Functions[call.Function!].Entries[call.EntryKey]);
            }
        }

        Assert.Equal(
            """{"param":{"standard":{"content":"Chronological prose narrative. Open with a one-sentence orientation to the patient.\n","value":{"title":"History of Present Illness"}}}}""",
            host.Calls.Single(c => c.Key == "node.standards.section.standard_draft:hpi").Scope.ToJsonString());
        Assert.Equal(
            """{"node":{"patient_draft":{"content":"Text of node.standards.section.patient_draft:assessment_plan."},"standard_draft":{"content":"Text of node.standards.section.standard_draft:assessment_plan."}}}""",
            host.Calls.Single(c => c.Key == "node.standards.section.apply_instructions:assessment_plan").Scope.ToJsonString());

        // the call's value is its function's ret's; the loop's value holds the two apply_instructions texts
        Assert.Equal(
            ["Text of node.standards.section.apply_instructions:hpi.", "Text of node.standards.section.apply_instructions:assessment_plan."],
            result.Values["node.standards"]!["value"]!.AsArray().Select(v => (string)v!));
        Assert.Equal("Text of node.standards.section.apply_instructions:hpi.",
            (string?)result.Values["node.standards"]!["items"]![0]!["nodes"]!["section"]!["value"]);

        // the call's row: kind call, the function's id, depth 0, its arguments' hashes in parameter order, its result's hash
        var row = recorder.Row("node.standards.section:hpi");
        Assert.Equal(PlanKind.Call, row.Kind);
        Assert.Equal("node.standards.section", row.EntryKey);
        Assert.Equal(RowState.Completed, row.State);
        Assert.Null(row.Reason);
        Assert.Equal(CanonicalJson.HashText("Text of node.standards.section.apply_instructions:hpi."), row.ValueHash);
        Assert.Equal("draft_section", (string?)row.Detail["function"]);
        Assert.Equal(0, (int)row.Detail["depth"]!);
        Assert.Equal("History of Present Illness", (string?)row.Detail["itemName"]);
        var standardArgument = new JsonObject { ["value"] = new JsonObject { ["title"] = "History of Present Illness" }, ["content"] = HpiBody };
        var draftArgument = new JsonObject { ["value"] = PlanRunnerLeavesTests.Draft, ["content"] = PlanRunnerLeavesTests.Draft };
        Assert.Equal(
            new JsonArray(
                new JsonObject { ["param"] = "standard", ["hash"] = CanonicalJson.Hash(standardArgument) },
                new JsonObject { ["param"] = "draft", ["hash"] = CanonicalJson.Hash(draftArgument) }).ToJsonString(),
            row.Detail["arguments"]!.ToJsonString());
        Assert.DoesNotContain("History of Present Illness", row.Detail["arguments"]!.ToJsonString());
        Assert.DoesNotContain("Margaret", row.Detail.ToJsonString());

        // the function's rows: keyed through the call, entry keys its own, in the function at depth 1, named by the item
        var inside = recorder.Row("node.standards.section.patient_draft:assessment_plan");
        Assert.Equal("node.patient_draft", inside.EntryKey);
        Assert.Equal("draft_section", (string?)inside.Detail["function"]);
        Assert.Equal(1, (int)inside.Detail["depth"]!);
        Assert.Equal("Assessment and Plan", (string?)inside.Detail["itemName"]);
        Assert.Equal(RowState.Completed, inside.State);
        Assert.Equal(
            ["node.standards.section.standard_draft:hpi", "node.standards.section.patient_draft:hpi", "node.standards.section.apply_instructions:hpi",
                "node.standards.section:hpi"],
            recorder.Rows.Select(r => r.Key).Where(k => k.StartsWith("node.standards.section", StringComparison.Ordinal) && k.EndsWith(":hpi", StringComparison.Ordinal)));

        // the function's instances are not the package's top level
        Assert.DoesNotContain(result.Values.Keys, k => k.StartsWith("node.standards.section", StringComparison.Ordinal));
    }

    // ---- parameters -------------------------------------------------------------------------------

    /// <summary>An argument for draft_section's <c>draft</c>, its parameter's format, and what <c>param.draft</c>'s value and content read.</summary>
    public static TheoryData<string, string, string, string> Arguments() => new()
    {
        // a field path: its value, and its text
        { "\"input.consult_draft.content\"", "\"markdown_block\"", Quoted(PlanRunnerLeavesTests.Draft), Quoted(PlanRunnerLeavesTests.Draft) },
        // a whole item: its value and content only (a snippet folder item: its header data and its body)
        { "\"item\"", "\"standard_section\"", """{"title":"History of Present Illness"}""", Quoted(HpiBody) },
        // null: the parameter reads null
        { "null", "{ \"type\": [\"string\", \"null\"] }", "null", "null" },
        // a run fact: its value
        { "\"run.date\"", "\"date\"", "\"2026-11-16\"", "\"2026-11-16\"" },
        // a snippet item: its header data and its body
        { "\"snippet.standards.assessment_plan\"", "\"standard_section\"", """{"title":"Assessment and Plan"}""", Quoted(PlanBody) },
        // a profile field: its value (publish lets it reach only deliverables and macro bodies, so the read is hand-written)
        { "\"profile.name\"", "\"plaintext\"", "\"Dr A. Sample\"", "\"Dr A. Sample\"" },
    };

    [Theory]
    [MemberData(nameof(Arguments))]
    public async Task Parameters_AreBoundFromArguments(string argument, string format, string value, string content)
    {
        var files = ResolveFixture.Functions()
            .WithJson("nodes/standards/section.md", header => header["with"]!["draft"] = JsonNode.Parse(argument))
            .WithJson("functions/draft_section/index.json", f => f["inputs"]!["draft"]!["format"] = JsonNode.Parse(format));
        if (argument is "null" or "\"run.date\"")
        {
            // #1504: a date or a nullable string has no .content, so the prompt reads .value to be a valid template; the
            // runner is still told it reads both (the stub below).
            files.With("functions/draft_section/patient_draft.md", SourceFixture.Agent("Drafting from the patient's facts", "\"plaintext\"",
                "Using the draft below, write what it says about Margaret Okafor for this section.\n\n{{ param.draft.value }}\n"));
        }


        if (argument == "\"run.date\"")
        {
            // a run fact makes the parameter, and everything reading it, run-dependent (part 03 § 4.4)
            foreach (var path in new[] { "nodes/standards/section.md", "nodes/standards/index.json", "nodes/extract_note_terms.md",
                         "functions/draft_section/patient_draft.md", "functions/draft_section/apply_instructions.md",
                         "checks/section_coverage/index.json", "checks/section_coverage/coverage.md", "checks/all_sections.md" })
                files.WithJson(path, header => header["runDependent"] = true);
        }

        RunPlan plan;
        if (argument.StartsWith("\"profile.", StringComparison.Ordinal))
        {
            // part 04 § 7.7 / part 06 § 10.2: a profile parameter may not be read in a prompt, so publish refuses this
            // package; the compiled document is changed by hand to bind and read one, as the runner must still do
            var document = Golden();
            document["nodes"]!["node.standards.section"]!["with"]!["draft"] = JsonNode.Parse(argument);
            document["nodes"]!["node.standards.section"]!["reads"] = new JsonArray("item", "profile.name");
            document["functions"]!["draft_section"]!["nodes"]!["node.patient_draft"]!["reads"] = new JsonArray("param.draft.content", "param.draft.value");
            var (built, refusal) = RunPlan.Build(document.ToJsonString(), "sha256:x", "sha256:y", null);
            Assert.True(refusal == null, refusal?.Sentence);
            plan = built!;
        }
        else
        {
            plan = RunFixture.Plan(files, FunctionsReads(("draft_section/node.patient_draft", ["param.draft.value", "param.draft.content"])));
        }

        var host = ConceptsHost(auto: true);
        var recorder = new ListRecorder();
        var inputs = Dated() with { Profile = new Dictionary<string, string?>(StringComparer.Ordinal) { ["name"] = "Dr A. Sample" } };

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs, FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        // what the function reads (a null parameter reads null through every path)
        var draft = host.Calls.Single(c => c.Key == "node.standards.section.patient_draft:hpi").Scope["param"]!["draft"]!;
        Assert.Equal(CanonicalJson.Write(JsonNode.Parse(value)), CanonicalJson.Write(draft["value"]));
        Assert.Equal(CanonicalJson.Write(JsonNode.Parse(content)), CanonicalJson.Write(draft["content"]));

        // its hash is on the call's row, never its value: the hash of { value, content }, or of null
        var bound = value == "null" ? null : new JsonObject { ["value"] = JsonNode.Parse(value), ["content"] = JsonNode.Parse(content) };
        var detail = recorder.Row("node.standards.section:hpi").Detail;
        var arguments = detail["arguments"]!.AsArray();
        Assert.Equal("draft", (string?)arguments[1]!["param"]);
        Assert.Equal(CanonicalJson.Hash(bound), (string?)arguments[1]!["hash"]);
        if (value != "null")
            Assert.DoesNotContain(JsonNode.Parse(content)!.GetValue<string>().Trim(), detail.ToJsonString());
    }

    [Fact]
    public async Task AFunction_SeesOnlyItsOwnNodes()
    {
        // the function has a node plan of its own; its apply_instructions reads node.plan, and gets the function's
        var files = ResolveFixture.Functions()
            .WithJson("functions/draft_section/index.json", f => f["nodes"] = new JsonArray("plan", "standard_draft", "patient_draft", "apply_instructions"))
            .With("functions/draft_section/plan.md", Agent("Planning the section", PlanRunnerLeavesTests.Plain,
                "Plan the section '{{ param.standard.value.title }}'.\n"));
        var plan = RunFixture.Plan(files, FunctionsReads(
            ("draft_section/node.plan", ["param.standard.value.title"]),
            ("draft_section/node.apply_instructions", ["node.plan.content", "node.standard_draft.content", "node.patient_draft.content"])));
        var host = ConceptsHost(auto: true).Reply("node.standards.section.plan:hpi", "The function's plan.");
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal("Text of node.plan.", (string?)result.Values["node.plan"]!["value"]);
        var apply = host.Calls.Single(c => c.Key == "node.standards.section.apply_instructions:hpi");
        Assert.Equal("The function's plan.", (string?)apply.Scope["node"]!["plan"]!["content"]);
        Assert.Contains("node.standards.section.plan:assessment_plan", host.Dispatched);
    }

    [Fact]
    public async Task AFunction_NeverSeesItsCallersItemOrPass()
    {
        // the call runs inside a loop item and a while pass; its function's node is made to read item and pass (publish
        // refuses that, part 04 § 7.4, so the compiled reads are changed by hand): both read null, never the caller's
        var files = ResolveFixture.Functions()
            .Without("nodes/standards/section.md")
            .WithJson("nodes/standards/index.json", j =>
            {
                j["nodes"] = new JsonArray("rounds");
                j["ret"] = "node.standards.rounds";
            })
            .With("nodes/standards/rounds/index.json", Json(SourceFixture.Node("while", "Rounds", PlanRunnerLeavesTests.Plain, false, true,
                "\"while\": { \"blank\": [{ \"var\": \"node.section.content\" }] }, \"max\": 2, "
                + "\"until\": null, \"checkFirst\": false, \"nodes\": [\"section\"], \"ret\": \"node.standards.rounds.section\"")))
            .With("nodes/standards/rounds/section.md", Md(
                SourceFixture.Node("call", "Drafting the section", PlanRunnerLeavesTests.Plain, false, true,
                    "\"if\": null, \"function\": \"draft_section\", "
                    + "\"with\": { \"standard\": \"item\", \"draft\": \"input.consult_draft.content\" }"),
                ""));
        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));
        var document = JsonNode.Parse(compiled!.Resolve)!.AsObject();
        var reads = document["functions"]!["draft_section"]!["nodes"]!["node.standard_draft"]!["reads"]!.AsArray();
        reads.Add("item.value.title");
        reads.Add("item.id");
        reads.Add("pass.number");
        var (plan, refusal) = RunPlan.Build(document.ToJsonString(), compiled.ResolveHash, compiled.LockHash, null);
        Assert.True(refusal == null, refusal?.Sentence);
        var host = ConceptsHost(auto: true);

        var result = await new PlanRunner(plan!, new RunLimits(), host, new ListRecorder()).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var call = host.Calls.Single(c => c.Key == "node.standards.rounds.section.standard_draft:hpi#1");
        Assert.Equal("""{"item":{"id":null,"value":{"title":null}},"pass":{"number":null}}""",
            CanonicalJson.Write(new JsonObject { ["item"] = call.Scope["item"]?.DeepClone(), ["pass"] = call.Scope["pass"]?.DeepClone() }));
        // what it reads through its parameter is the caller's item, passed in
        Assert.Equal("History of Present Illness", (string?)call.Scope["param"]!["standard"]!["value"]!["title"]);
    }

    // ---- nesting ----------------------------------------------------------------------------------

    [Fact]
    public async Task ANestedCall_KeysThroughBothCalls()
    {
        var plan = RunFixture.Plan(Nested(), NestedReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan", "node.hpi_section.draft_text", "node.hpi_section.refine_call.judge"], host.Dispatched);
        var judge = host.Calls.Single(c => c.Key == "node.hpi_section.refine_call.judge");
        Assert.Equal("node.judge", judge.EntryKey);
        Assert.Equal("refine", judge.Function);
        Assert.Equal("""{"param":{"text":{"content":"Text of node.hpi_section.draft_text."}}}""", judge.Scope.ToJsonString());

        var outer = recorder.Row("node.hpi_section");
        Assert.Equal("section_fn", (string?)outer.Detail["function"]);
        Assert.Equal(0, (int)outer.Detail["depth"]!);
        var inner = recorder.Row("node.hpi_section.refine_call");
        Assert.Equal(PlanKind.Call, inner.Kind);
        Assert.Equal("node.refine_call", inner.EntryKey);
        Assert.Equal("refine", (string?)inner.Detail["function"]);
        Assert.Equal(1, (int)inner.Detail["depth"]!);
        Assert.Equal(2, (int)recorder.Row("node.hpi_section.refine_call.judge").Detail["depth"]!);

        // each call's value is its function's ret's, through both
        Assert.Equal("Text of node.hpi_section.refine_call.judge.", (string?)result.Values["node.hpi_section"]!["value"]);
        Assert.Equal(CanonicalJson.HashText("Text of node.hpi_section.refine_call.judge."), outer.ValueHash);
    }

    // ---- the ceiling and the call limit -----------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(1503)]
    public async Task CallsInsideFunctions_CountTowardTheCeilingAndTheCallLimit(int seed)
    {
        var plan = RunFixture.Plan("functions");

        // under a ceiling of 2, 2 items × 3 agents never pass 2 in flight, and dispatch keeps the fixed order
        var host = ConceptsHost(auto: false);
        var runner = new PlanRunner(plan, new RunLimits(MaxAgentCalls: 2), host, new ListRecorder());
        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            Assert.Equal(["node.plan", "node.standards.section.standard_draft:hpi"], host.Dispatched);
            host.Release("node.plan");
            Assert.Equal("node.standards.section.standard_draft:assessment_plan", host.Dispatched[^1]);
            host.ReleaseInOrder(new Random(seed));
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(2, host.InFlightPeak);
        Assert.Equal(2, result.PeakInFlight);
        Assert.Equal(9, result.AgentCalls);
        Assert.Equal(6, host.Dispatched.Count(k => k.StartsWith("node.standards.section.", StringComparison.Ordinal)));

        // the function's calls count toward the job's: the fifth is the last made, and the sixth fails the job
        var limited = ConceptsHost(auto: true);
        var failed = await new PlanRunner(plan, new RunLimits(JobAgentCalls: 5), limited, new ListRecorder())
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, failed.State);
        Assert.Equal("The job reached 5 agent calls, the most this engine runs for one job.", failed.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S09-015", failed.Sentence!, ""));
        Assert.Equal(5, failed.AgentCalls);
        Assert.Equal(4, limited.Dispatched.Count(k => k.StartsWith("node.standards.section.", StringComparison.Ordinal)));
        Assert.Equal("node.extract_input_terms", failed.FailedKey);
    }

    // ---- if ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ACallWhoseIfIsFalse_IsSkipped_AndRunsNothing()
    {
        var files = ResolveFixture.Functions()
            .WithJson("nodes/standards/section.md",
                header => header["if"] = JsonNode.Parse("""{ "!==": [{ "var": "item.id" }, "hpi"] }"""));
        var plan = RunFixture.Plan(files);
        var host = ConceptsHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.DoesNotContain(host.Dispatched, k => k.EndsWith(":hpi", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Rows, r => r.Key.StartsWith("node.standards.section.", StringComparison.Ordinal) && r.Key.EndsWith(":hpi", StringComparison.Ordinal));
        Assert.Equal(3, host.Dispatched.Count(k => k.EndsWith(":assessment_plan", StringComparison.Ordinal)));

        var row = recorder.Row("node.standards.section:hpi");
        Assert.Equal(RowState.Skipped, row.State);
        Assert.StartsWith("skipped: ", row.Reason);
        Assert.Equal("draft_section", (string?)row.Detail["function"]);
        Assert.Equal("[]", row.Detail["arguments"]!.ToJsonString());
        Assert.Contains(recorder.Decisions, d => d.Owner == "node.standards.section:hpi" && d.Site == "self" && d.Result == false);
        Assert.Equal(RowState.Skipped, recorder.Row("node.standards:hpi").State);
        Assert.Single(result.Values["node.standards"]!["value"]!.AsArray());
    }

    // ---- a corrupt document -----------------------------------------------------------------------

    [Fact]
    public async Task ACallRing_IsRefusedAtBuild_AndAChainPastTheDepthBound_FailsWithoutHanging()
    {
        // a ring of calls is refused before any run
        var ring = Golden();
        Chain(ring, ["ring_a", "ring_b"], "ring_a");
        var (refused, refusal) = RunPlan.Build(ring.ToJsonString(), "sha256:x", "sha256:y", null);
        Assert.Null(refused);
        Assert.Equal("S07-006", refusal!.Id);

        // a chain no ring makes, nested deeper than publish bounds (FormatBounds.Depth): the call that would open the 17th
        // function frame fails the job with S07-006, and the run ends
        var deep = Golden();
        var names = Enumerable.Range(0, FormatBounds.Depth + 1).Select(i => $"chain_{i}").ToList();
        Chain(deep, names, "draft_section");
        deep["nodes"]!["node.standards.section"]!["function"] = names[0];
        var (plan, built) = RunPlan.Build(deep.ToJsonString(), "sha256:x", "sha256:y", null);
        Assert.True(built == null, built?.Sentence);
        var host = ConceptsHost(auto: true);

        var result = await FakeRunHost.Drive(() => new PlanRunner(plan!, new RunLimits(), host, new ListRecorder())
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start));

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("Workflow example-functions@v2026.12.1 can't run: its compiled document doesn't match the hash published for it.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S07-006", result.Sentence!, ""));
        Assert.Equal("node.standards.section" + string.Concat(Enumerable.Repeat(".apply_instructions", FormatBounds.Depth + 1)) + ":hpi", result.FailedKey);

        // one function fewer is within the bound, and runs
        var within = Golden();
        Chain(within, names[..^1], "draft_section");
        within["nodes"]!["node.standards.section"]!["function"] = names[0];
        var (fits, _) = RunPlan.Build(within.ToJsonString(), "sha256:x", "sha256:y", null);
        var ran = await new PlanRunner(fits!, new RunLimits(), ConceptsHost(auto: true), new ListRecorder())
            .RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        Assert.Equal(RunState.Completed, ran.State);
    }

    // ---- loops inside and over functions ----------------------------------------------------------

    [Fact]
    public async Task ASnippetFolderLoop_InsideAFunction_ReadsTheFunctionsOwnFolder()
    {
        var plan = RunFixture.Plan(WithChecklist(), FunctionsReads(
            ("draft_section/node.points", []),
            ("draft_section/node.points.note", ["item.value.title", "item.content"]),
            ("draft_section/node.apply_instructions", ["node.points.content", "node.standard_draft.content", "node.patient_draft.content"]),
            ("functions/draft_section/snippets/checklist/onset.md", []),
            ("functions/draft_section/snippets/checklist/medicines.md", []),
            ("snippets/checklist/allergies.md", [])));
        var host = ConceptsHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        // the function's own folder, not the package's folder of the same id
        Assert.Equal(
            ["node.standards.section.points.note:hpi/onset", "node.standards.section.points.note:hpi/medicines"],
            host.Dispatched.Where(k => k.Contains(".note:hpi", StringComparison.Ordinal)));
        Assert.DoesNotContain(host.Dispatched, k => k.Contains("allergies", StringComparison.Ordinal));
        var note = host.Calls.Single(c => c.Key == "node.standards.section.points.note:hpi/medicines");
        Assert.Equal("""{"item":{"content":"List every medicine with its dose.\n","value":{"title":"Medicines"}}}""", note.Scope.ToJsonString());
        Assert.Equal("node.points.note", note.EntryKey);
        Assert.Equal("draft_section", note.Function);

        // the item row is keyed through the call, named by its own item, inside the function
        var item = recorder.Row("node.standards.section.points:hpi/medicines");
        Assert.Equal("Medicines", (string?)item.Detail["itemName"]);
        Assert.Equal("draft_section", (string?)item.Detail["function"]);
        Assert.Equal(RowState.Completed, recorder.Row("node.standards.section.points:hpi").State);
    }

    [Fact]
    public async Task ACheckLoop_OverACallsLoop_TakesEachItemAsItsCallSettles()
    {
        // the functions golden: check.section_coverage iterates node.standards.items, whose items settle through calls
        var plan = RunFixture.Plan("functions");
        var host = ConceptsHost(auto: false);
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
            host.Release("node.plan");
            host.Release("node.extract_input_terms");
            foreach (var node in Body)
                host.Release($"node.standards.section.{node}:hpi");

            // the call settles its item, while the other item's function still runs
            Assert.Equal(RowState.Completed, recorder.Row("node.standards.section:hpi").State);
            Assert.Equal(RowState.Completed, recorder.Row("node.standards:hpi").State);
            Assert.DoesNotContain(recorder.Rows, r => r.Key == "node.standards:assessment_plan");
            Assert.Contains("node.standards.section.standard_draft:assessment_plan", host.Held);
            host.ReleaseAll();
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        var checks = result.Values["check.section_coverage"]!["items"]!.AsArray();
        Assert.Equal(Standards, checks.Select(i => (string)i!["item"]!["id"]!));
        Assert.Equal(["History of Present Illness", "Assessment and Plan"], checks.Select(i => (string)i!["label"]!));
        Assert.Equal("Text of node.standards.section.apply_instructions:hpi.", (string?)checks[0]!["item"]!["value"]);
        Assert.Equal("Text of node.standards.section.apply_instructions:hpi.", (string?)checks[0]!["item"]!["nodes"]!["section"]!["value"]);
        Assert.Equal(RowState.Completed, recorder.Row("check.section_coverage.coverage:assessment_plan").State);
        Assert.True((bool)result.Values["check.all_sections"]!["value"]!["pass"]!);
        Assert.Equal("released", (string?)result.Values["deliverable.emr.consult"]!["state"]);
    }

    // ---- failures inside a function ---------------------------------------------------------------

    [Theory]
    [InlineData("A06-003")]
    [InlineData("A06-005")]
    public async Task AFunctionsNode_FailsWithItsRegisteredSentence(string id)
    {
        Dictionary<string, byte[]> files;
        StubTemplates reads;
        RunInputs inputs;
        FakeRunHost host;
        if (id == "A06-003")
        {
            // a value that escaped validation (a boolean input supplied as text), passed in and computed on in the function
            files = ResolveFixture.Functions()
                .WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
                    "{ \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", \"required\": true, \"channel\": null }"))
                .WithJson("nodes/standards/section.md", header => header["with"]!["billing"] = "input.include_billing.value")
                .WithJson("functions/draft_section/index.json", f =>
                {
                    f["inputs"]!["billing"] = JsonNode.Parse("{ \"label\": \"Billing\", \"description\": null, \"format\": \"boolean\" }");
                    f["nodes"] = new JsonArray("standard_draft", "patient_draft", "bill", "apply_instructions");
                })
                .With("functions/draft_section/bill.md", Md(
                    SourceFixture.Node("computed", "Whether to bill", "\"boolean\"", true, false,
                        "\"if\": null, \"expression\": { \"var\": \"param.billing.value\" }"),
                    ""));
            reads = FunctionsReads(("draft_section/node.apply_instructions", ["node.bill.value", "node.standard_draft.content", "node.patient_draft.content"]));
            var given = PlanRunnerLeavesTests.Inputs();
            inputs = given with
            {
                Inputs = new Dictionary<string, JsonNode?>(given.Inputs, StringComparer.Ordinal)
                {
                    ["include_billing"] = new JsonObject { ["value"] = "yes", ["content"] = null, ["title"] = null },
                },
            };
            host = ConceptsHost(auto: true);
        }
        else
        {
            // a reply that isn't the function's node's format, twice (onInvalid: retry)
            files = ResolveFixture.Functions()
                .WithJson("functions/draft_section/standard_draft.md", header => header["format"] = "concept_list");
            reads = FunctionsReads();
            inputs = PlanRunnerLeavesTests.Inputs();
            host = ConceptsHost(auto: true).Reply("node.standards.section.standard_draft:hpi", "not a concept list");
        }

        var plan = RunFixture.Plan(files, reads);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs, FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        var (key, sentence) = id == "A06-003"
            ? ("node.standards.section.bill:hpi", "Node 'bill' computed a value that isn't boolean.")
            : ("node.standards.section.standard_draft:hpi", "Node 'standard_draft' gave a reply that isn't concept_list.");
        Assert.Equal(key, result.FailedKey);
        Assert.Equal(sentence, result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal(id, result.Sentence!, ""));
        var row = recorder.Row(key);
        Assert.Equal(RowState.Failed, row.State);
        Assert.Equal(sentence, row.Reason);
        Assert.Equal("draft_section", (string?)row.Detail["function"]);
    }

    // ---- the goldens ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(CompiledGoldenTests.Names), MemberType = typeof(CompiledGoldenTests))]
    public async Task EachCompiledGolden_RunsToCompleted(string name)
    {
        var plan = RunFixture.Plan(name);
        var host = RunFixture.Stubbed(plan);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(RunFixture.GoldenInputs(), FakeRunHost.Start);

        Assert.True(result.State == RunState.Completed, $"{result.State}: {result.FailedKey} {result.Sentence}");
        Assert.Equal(plan.TopLevel.Order(StringComparer.Ordinal), result.Values.Keys.Order(StringComparer.Ordinal));
        Assert.NotEmpty(host.Calls);
        // #1505: a deliverable's outcome is one the job goes on with (the stubs' terms do not cover checked-note's plan, so
        // its note is blocked)
        Assert.All(recorder.Rows, r => Assert.True(
            r.State is RowState.Completed or RowState.Released or RowState.Blocked or RowState.Skipped or RowState.NotRun,
            $"{r.Key} is {r.State}"));
        Assert.Contains(recorder.Rows, r => r.State == RowState.Released);
    }

    // ---- fixtures ---------------------------------------------------------------------------------

    /// <summary>A host whose concept-list agents reply an empty list.</summary>
    private static FakeRunHost ConceptsHost(bool auto) => new FakeRunHost(auto)
        .Reply("node.extract_input_terms", Concepts)
        .Reply("node.extract_note_terms", Concepts);

    /// <summary>The inputs, with the run fact <c>run.date</c>.</summary>
    private static RunInputs Dated() => PlanRunnerLeavesTests.Inputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-16" } };

    /// <summary>A JSON string of <paramref name="text"/>.</summary>
    private static string Quoted(string text) => JsonValue.Create(text)!.ToJsonString();

    /// <summary>What each body of <see cref="ResolveFixture.Functions"/> reads, with some bodies' reads replaced or added.</summary>
    private static StubTemplates FunctionsReads(params (string Name, string[] Reads)[] changed)
    {
        var reads = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["node.plan"] = ["input.consult_draft.content"],
            ["node.extract_input_terms"] = ["input.consult_draft.content"],
            ["node.extract_note_terms"] = ["node.standards.content"],
            ["draft_section/node.standard_draft"] = ["param.standard.value.title", "param.standard.content"],
            ["draft_section/node.patient_draft"] = ["param.draft.content"],
            ["draft_section/node.apply_instructions"] = ["node.standard_draft.content", "node.patient_draft.content"],
            ["snippets/standards/hpi.md"] = [],
            ["snippets/standards/assessment_plan.md"] = [],
            ["deliverable.emr.consult"] = ["node.standards.content"],
        };
        foreach (var (name, paths) in changed)
            reads[name] = paths;
        return new StubTemplates(reads, everyBody: false);
    }

    /// <summary>
    /// The functions package with a snippet folder <c>checklist</c> of the function's own (items <c>onset</c> and
    /// <c>medicines</c>), the package a folder of the same id with another item (<c>allergies</c>), and in the function the loop
    /// <c>points</c> over <c>snippet.checklist</c>, holding the agent <c>note</c>, read by <c>apply_instructions</c>.
    /// </summary>
    private static Dictionary<string, byte[]> WithChecklist()
    {
        var files = ResolveFixture.Functions()
            .WithJson("functions/draft_section/index.json", f => f["nodes"] = new JsonArray("standard_draft", "patient_draft", "points", "apply_instructions"))
            .With("functions/draft_section/snippets/index.json", Index(["checklist"]))
            .With("functions/draft_section/snippets/checklist/index.json", Index(["onset", "medicines"],
                description: "What every section checks.", extra: ", \"format\": \"standard_section\""))
            .With("functions/draft_section/snippets/checklist/onset.md", Md("{ \"title\": \"Onset\" }", "Say when the symptoms began.\n"))
            .With("functions/draft_section/snippets/checklist/medicines.md", Md("{ \"title\": \"Medicines\" }", "List every medicine with its dose.\n"))
            .With("functions/draft_section/points/index.json", Json(SourceFixture.Node("for", "Points", ArrayOf(PlanRunnerLeavesTests.Plain), false, true,
                "\"in\": \"snippet.checklist\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"note\"], \"ret\": \"node.points.note\"")))
            .With("functions/draft_section/points/note.md", Agent("Noting a point", PlanRunnerLeavesTests.Plain,
                "Note '{{ item.value.title }}' for the section.\n\n{{ item.content }}\n"))
            .With("snippets/index.json", Index(["standards", "checklist"]))
            .With("snippets/checklist/index.json", Index(["allergies"],
                description: "The package's own checklist.", extra: ", \"format\": \"standard_section\""))
            .With("snippets/checklist/allergies.md", Md("{ \"title\": \"Allergies\" }", "List every allergy.\n"));
        return files;
    }

    /// <summary>
    /// <c>example-minimal</c> with the top-level call <c>hpi_section</c> of <c>section_fn</c> (its agent <c>draft_text</c>
    /// and its call <c>refine_call</c> of <c>refine</c>, which returns its agent <c>judge</c>).
    /// </summary>
    private static Dictionary<string, byte[]> Nested()
    {
        var files = Minimal()
            .With("nodes/index.json", Index(["plan", "hpi_section"]))
            .With("nodes/hpi_section.md", Md(
                SourceFixture.Node("call", "Drafting the history", PlanRunnerLeavesTests.Plain, false, true,
                    "\"if\": null, \"function\": \"section_fn\", \"with\": { \"draft\": \"input.consult_draft.content\" }"),
                ""))
            .With("functions/index.json", Index(["section_fn", "refine"]))
            .With("functions/section_fn/index.json", Json(FunctionIndex("Draft a section",
                "\"draft\": { \"label\": \"Consult draft\", \"description\": null, \"format\": \"markdown_block\" }",
                ["draft_text", "refine_call"], "node.refine_call")))
            .With("functions/section_fn/draft_text.md", Agent("Drafting the text", PlanRunnerLeavesTests.Plain,
                "Draft the history for Margaret Okafor.\n\n{{ param.draft.content }}\n"))
            .With("functions/section_fn/refine_call.md", Md(
                SourceFixture.Node("call", "Refining the text", PlanRunnerLeavesTests.Plain, false, true,
                    "\"if\": null, \"function\": \"refine\", \"with\": { \"text\": \"node.draft_text\" }"),
                ""))
            .With("functions/refine/index.json", Json(FunctionIndex("Refine a text",
                "\"text\": { \"label\": \"Text\", \"description\": null, \"format\": \"plaintext\" }",
                ["judge"], "node.judge")))
            .With("functions/refine/judge.md", Agent("Judging the text", PlanRunnerLeavesTests.Plain,
                "Improve this text.\n\n{{ param.text.content }}\n"));
        foreach (var function in new[] { "section_fn", "refine" })
        {
            foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
                files.With($"functions/{function}/{folder}/index.json", Index([]));
        }

        return files;
    }

    private static StubTemplates NestedReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["section_fn/node.draft_text"] = ["param.draft.content"],
        ["refine/node.judge"] = ["param.text.content"],
        ["deliverable.note"] = ["node.plan.content"],
    }, everyBody: false);

    /// <summary>A function's <c>index.json</c>, of format plaintext, using nothing of the package's.</summary>
    private static string FunctionIndex(string label, string inputs, string[] nodes, string ret) =>
        $"{{ \"label\": \"{label}\", \"description\": null, \"inputs\": {{ {inputs} }}, "
        + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
        + $"\"nodes\": [{string.Join(", ", nodes.Select(n => $"\"{n}\""))}], \"ret\": \"{ret}\", \"format\": \"plaintext\" }}";

    /// <summary>The functions golden's compiled document, to change.</summary>
    private static JsonObject Golden() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(CompiledGoldenTests.GoldenFolder(), "functions.resolve.json")))!.AsObject();

    /// <summary>
    /// Adds to <paramref name="document"/> a function per name, each draft_section with its <c>apply_instructions</c> made a
    /// call of the next name, and the last of <paramref name="last"/>.
    /// </summary>
    private static void Chain(JsonObject document, IReadOnlyList<string> names, string last)
    {
        var functions = document["functions"]!.AsObject();
        var template = functions["draft_section"]!;
        for (var i = 0; i < names.Count; i++)
        {
            var link = template.DeepClone().AsObject();
            link["id"] = names[i];
            link["nodes"]!["node.apply_instructions"] = new JsonObject
            {
                ["format"] = new JsonObject { ["$ref"] = "#/$defs/plaintext" },
                ["function"] = i + 1 < names.Count ? names[i + 1] : last,
                ["id"] = "apply_instructions",
                ["if"] = null,
                ["kind"] = "call",
                ["parent"] = null,
                ["plainConverts"] = new JsonObject(),
                ["reads"] = new JsonArray("param.draft", "param.standard"),
                ["with"] = new JsonObject { ["draft"] = "param.draft", ["standard"] = "param.standard" },
            };
            functions[names[i]] = link;
        }
    }
}
