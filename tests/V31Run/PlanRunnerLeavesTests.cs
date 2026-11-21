using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.1; part 03 §§ 6, 9, 11.3): <see cref="PlanRunner"/> on the top level's
/// leaves. An agent is called with exactly what its body reads; a computed node is evaluated inline, with no call, and its
/// value held to its format (A06-003, 04-D49); an <c>if</c> that is false skips its leaf, and a reader sees <c>null</c>; a
/// condition that fails fails its node; a node is ready when what it reads has settled; a deliverable renders, takes no
/// slot and settles composed. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PlanRunnerLeavesTests
{
    internal const string Plain = "\"plaintext\"";

    internal const string Draft = "Margaret Okafor, 58, referred by Dr A. Sample for fatigue and a raised TSH (fictional).";

    private const string ThreeSections =
        """{"sections":[{"title":"History","kind":"narrative"},{"title":"Examination","kind":"narrative"},{"title":"Plan","kind":"table"}]}""";

    // ---- agents ---------------------------------------------------------------------------------

    [Fact]
    public async Task ASingleAgent_Completes()
    {
        var plan = RunFixture.Plan(Minimal(), MinimalReads());
        var host = new FakeRunHost(auto: true).Reply("node.plan", "History, examination, plan.");
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Null(result.Sentence);
        var call = Assert.Single(host.Calls);
        Assert.Equal("node.plan", call.Key);
        Assert.Equal("node.plan", call.EntryKey);
        Assert.Null(call.Function);
        Assert.Equal(1, call.Attempt);
        Assert.False(call.Repair);
        Assert.Equal(new JsonObject { ["input"] = new JsonObject { ["consult_draft"] = new JsonObject { ["content"] = Draft } } }.ToJsonString(),
            call.Scope.ToJsonString());

        var row = recorder.Row("node.plan");
        Assert.Equal(RowState.Completed, row.State);
        Assert.Equal(PlanKind.Agent, row.Kind);
        Assert.Null(row.Reason);
        Assert.Equal(CanonicalJson.HashText("History, examination, plan."), row.ValueHash);

        var read = result.Values["node.plan"]!;
        Assert.Equal("History, examination, plan.", (string?)read["value"]);
        Assert.Equal("History, examination, plan.", (string?)read["content"]);
        Assert.Equal("completed", (string?)read["state"]);
        Assert.Equal(1, result.AgentCalls);
    }

    [Fact]
    public async Task AComputedNode_IsEvaluatedInline_WithNoCall()
    {
        var plan = RunFixture.Plan(Counting("\"number\""), CountingReads());
        var host = new FakeRunHost(auto: true).Reply("node.plan", ThreeSections);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan"], host.Dispatched);
        Assert.Equal(3, (int)result.Values["node.section_count"]!["value"]!);
        Assert.Equal("3", (string?)result.Values["node.section_count"]!["content"]);
        Assert.Equal("completed", (string?)result.Values["node.section_count"]!["state"]);
        Assert.Equal(CanonicalJson.HashText("3"), recorder.Row("node.section_count").ValueHash);

        // A value that is not text reads its canonical JSON as .content (D11).
        Assert.Equal(CanonicalJson.Write(JsonNode.Parse(ThreeSections)), (string?)result.Values["node.plan"]!["content"]);
    }

    [Fact]
    public async Task AComputedValue_ThatDoesNotFitItsFormat_FailsWithA06003()
    {
        // a value that escaped validation: a boolean input supplied as text, passed on by a computed boolean
        var files = WithBilling(Minimal(), required: true)
            .With("nodes/index.json", Index(["plan", "billing"]))
            .With("nodes/billing.md", Md(
                SourceFixture.Node("computed", "Whether to bill", "\"boolean\"", true, false,
                    "\"if\": null, \"expression\": { \"var\": \"input.include_billing.value\" }"),
                ""));
        var plan = RunFixture.Plan(files, MinimalReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Escaped(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.billing", result.FailedKey);
        Assert.Equal("Node 'billing' computed a value that isn't boolean.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A06-003", result.Sentence!, ""));
        Assert.Equal(RowState.Failed, recorder.Row("node.billing").State);
        Assert.Equal(result.Sentence, recorder.Row("node.billing").Reason);
    }

    [Fact]
    public async Task AComputedNull_IsAValue()
    {
        // 04-D49: a computed node typed non-nullable can still evaluate to null (part 03 § 6.2): here a required input
        // that is absent (as no job start lets it be)
        var files = WithBilling(Minimal(), required: true)
            .With("nodes/index.json", Index(["plan", "billing"]))
            .With("nodes/billing.md", Md(
                SourceFixture.Node("computed", "Whether to bill", "\"boolean\"", true, false,
                    "\"if\": null, \"expression\": { \"var\": \"input.include_billing.value\" }"),
                ""));
        var plan = RunFixture.Plan(files, MinimalReads());
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), new FakeRunHost(auto: true), recorder).RunAsync(Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Null(result.Values["node.billing"]!["value"]);
        Assert.Null(result.Values["node.billing"]!["content"]);
        Assert.Equal("completed", (string?)result.Values["node.billing"]!["state"]);
        Assert.Null(recorder.Row("node.billing").ValueHash);
    }

    // ---- if -------------------------------------------------------------------------------------

    [Fact]
    public async Task AnIfThatIsFalse_SkipsTheLeaf_AndReadersSeeNull()
    {
        var files = WithBilling(Minimal())
            .With("nodes/index.json", Index(["plan", "copy"]))
            .With("nodes/plan.md", GatedAgent("{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }"))
            .With("nodes/copy.md", Md(
                SourceFixture.Node("computed", "Whether the plan is blank", "\"boolean\"", true, true,
                    "\"if\": null, \"expression\": { \"blank\": [{ \"var\": \"node.plan.content\" }] }"),
                ""));
        var plan = RunFixture.Plan(files, MinimalReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Inputs(includeBilling: false), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Empty(host.Calls);
        var row = recorder.Row("node.plan");
        Assert.Equal(RowState.Skipped, row.State);
        Assert.Equal("skipped: input.include_billing.value = true is false", row.Reason);
        Assert.Null(row.ValueHash);

        var decision = Assert.Single(recorder.Decisions);
        Assert.Equal("node.plan", decision.Site);
        Assert.Equal("self", decision.Member);
        Assert.Equal("input.include_billing.value = true", decision.Printed);
        Assert.False(decision.Result);
        Assert.Equal(FakeRunHost.Start, decision.DecidedAtUtc);

        var skipped = result.Values["node.plan"]!;
        Assert.Null(skipped["value"]);
        Assert.Null(skipped["content"]);
        Assert.Equal("skipped", (string?)skipped["state"]);

        // the computed node read the skipped node's .content as null
        Assert.Equal(RowState.Completed, recorder.Row("node.copy").State);
        Assert.True((bool)result.Values["node.copy"]!["value"]!);
    }

    [Fact]
    public async Task AnIfThatIsTrue_RunsTheLeaf_AndIsRecorded()
    {
        var files = WithBilling(Minimal())
            .With("nodes/plan.md", GatedAgent("{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }"));
        var plan = RunFixture.Plan(files, MinimalReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Inputs(includeBilling: true), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(["node.plan"], host.Dispatched);
        Assert.True(Assert.Single(recorder.Decisions).Result);

        // the call's scope holds what the entry reads: its body's read and its condition's
        Assert.Equal(Draft, (string?)host.Calls[0].Scope["input"]!["consult_draft"]!["content"]);
        Assert.True((bool)host.Calls[0].Scope["input"]!["include_billing"]!["value"]!);
    }

    [Fact]
    public async Task AConditionThatYieldsNoBoolean_FailsItsNode()
    {
        var files = WithBilling(Minimal())
            .With("nodes/plan.md", GatedAgent("{ \"var\": \"input.include_billing.value\" }"));
        var plan = RunFixture.Plan(files, MinimalReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Escaped(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.plan", result.FailedKey);
        Assert.Equal("Node 'plan' if yields text; a condition yields a boolean.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S03-041", result.Sentence!, ""));
        Assert.Equal(RowState.Failed, recorder.Row("node.plan").State);
        Assert.Empty(host.Calls);
        Assert.Empty(host.Renders);
    }

    [Fact]
    public async Task ASlowMatch_FailsItsNode()
    {
        var files = Minimal()
            .With("nodes/plan.md", GatedAgent("{ \"matches\": [{ \"var\": \"input.consult_draft.content\" }, \"(.*a.{100}){3}b\"] }"));
        var plan = RunFixture.Plan(files, MinimalReads(), TimeSpan.FromTicks(1));
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var text = new string('a', 1 << 20);
        var inputs = new RunInputs(
            new Dictionary<string, JsonNode?> { ["consult_draft"] = new JsonObject { ["value"] = text, ["content"] = text, ["title"] = null } },
            new JsonObject(),
            new Dictionary<string, string?>());
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(inputs, FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.plan", result.FailedKey);
        Assert.Matches("^Node 'plan' if could not be decided: matches took longer than .+ seconds on this text\\. Use a simpler pattern\\.$", result.Sentence!);
        V31Contract.AssertSentence(new SourceRefusal("A06-001", result.Sentence!, ""));
        Assert.Empty(host.Calls);
    }

    // ---- readiness ------------------------------------------------------------------------------

    [Fact]
    public async Task Readiness_FollowsReads()
    {
        var files = Minimal().Without("nodes/plan.md")
            .With("nodes/index.json", Index(["a", "b", "c"]))
            .With("nodes/a.md", Agent("Drafting a", Plain, "Draft a.\n"))
            .With("nodes/b.md", Agent("Drafting b", Plain, "Draft b from a.\n\n{{ node.a.content }}\n"))
            .With("nodes/c.md", Agent("Drafting c", Plain, "Draft c.\n"))
            .With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "null", "hold"), "{{ node.b.content }}\n"));
        var plan = RunFixture.Plan(files, new StubTemplates(new Dictionary<string, string[]>
        {
            ["node.b"] = ["node.a.content"],
            ["deliverable.note"] = ["node.b.content"],
        }));
        var host = new FakeRunHost();
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(Inputs(), FakeRunHost.Start);
            Assert.Equal(["node.a", "node.c"], host.Dispatched);
            host.Release("node.c");
            Assert.Equal(["node.a", "node.c"], host.Dispatched);
            host.Release("node.a");
            Assert.Equal(["node.a", "node.c", "node.b"], host.Dispatched);
            Assert.Equal(new JsonObject { ["node"] = new JsonObject { ["a"] = new JsonObject { ["content"] = "Text of node.a." } } }.ToJsonString(),
                host.Calls[2].Scope.ToJsonString());
            Assert.Empty(host.Renders);
            host.ReleaseAll();
            return run;
        });

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(3, result.AgentCalls);
        Assert.Equal(2, result.PeakInFlight);
    }

    // ---- failures -------------------------------------------------------------------------------

    [Fact]
    public async Task AFailedCall_FailsTheJob_AndDispatchesNothingMore()
    {
        var files = Minimal().Without("nodes/plan.md")
            .With("nodes/index.json", Index(["a", "b", "c"]))
            .With("nodes/a.md", Agent("Drafting a", Plain, "Draft a.\n"))
            .With("nodes/b.md", Agent("Drafting b", Plain, "Draft b.\n"))
            .With("nodes/c.md", Agent("Drafting c", Plain, "Draft c.\n"))
            .With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "null", "hold"), "{{ node.c.content }}\n"));
        var plan = RunFixture.Plan(files, new StubTemplates(new Dictionary<string, string[]> { ["deliverable.note"] = ["node.c.content"] }));
        var host = new FakeRunHost().Agent("node.a", new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 0, "error"));
        var recorder = new ListRecorder();
        var runner = new PlanRunner(plan, new RunLimits(MaxAgentCalls: 2, ServiceAttempts: 1), host, recorder);

        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(Inputs(), FakeRunHost.Start);
            Assert.Equal(["node.a", "node.b"], host.Dispatched);
            host.Release("node.a");
            return run; // node.b is still in flight: it is awaited no further
        });

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.a", result.FailedKey);
        Assert.Equal("The model service returned an error 1 times in a row. Run the workflow again later.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S09-011", result.Sentence!, ""));
        Assert.Equal(["node.a", "node.b"], host.Dispatched);
        Assert.Equal(["node.b"], host.Held);
        Assert.Empty(host.Renders);
        Assert.Equal(RowState.Failed, Assert.Single(recorder.Rows).State);
        Assert.False(result.Values.ContainsKey("node.b"));
    }

    // ---- deliverables ---------------------------------------------------------------------------

    [Fact]
    public async Task ADeliverable_IsRenderedAndSettlesComposed()
    {
        var plan = RunFixture.Plan(Minimal(), MinimalReads());
        var host = new FakeRunHost(auto: true).Reply("node.plan", "History, examination, plan.");
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        var render = Assert.Single(host.Renders);
        Assert.Equal("deliverable.note", render.Key);
        Assert.Equal(100_000, render.StepLimit);
        Assert.Equal(new JsonObject { ["node"] = new JsonObject { ["plan"] = new JsonObject { ["content"] = "History, examination, plan." } } }.ToJsonString(),
            render.Scope.ToJsonString());
        var row = recorder.Row("deliverable.note");
        Assert.Equal(RowState.Composed, row.State);
        Assert.Equal(PlanKind.Deliverable, row.Kind);
        Assert.Equal(CanonicalJson.HashText("Rendered deliverable.note."), row.ValueHash);
        Assert.Equal("composed", (string?)result.Values["deliverable.note"]!["state"]);
        Assert.Equal("Rendered deliverable.note.", (string?)result.Values["deliverable.note"]!["content"]);

        // the deliverable takes no slot
        Assert.Equal(1, result.AgentCalls);
        Assert.Equal(["node.plan", "deliverable.note"], recorder.Rows.Select(r => r.Key));
    }

    [Fact]
    public async Task ADeliverableWhoseIfIsFalse_IsSkipped()
    {
        var files = WithBilling(Minimal())
            .WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse("{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }"));
        var plan = RunFixture.Plan(files, MinimalReads());
        var host = new FakeRunHost(auto: true);
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(Inputs(includeBilling: false), FakeRunHost.Start);

        Assert.Equal(RunState.Completed, result.State);
        Assert.Empty(host.Renders);
        var row = recorder.Row("deliverable.note");
        Assert.Equal(RowState.Skipped, row.State);
        Assert.Equal("skipped: input.include_billing.value = true is false", row.Reason);
        var decision = Assert.Single(recorder.Decisions);
        Assert.Equal("deliverable.note", decision.Site);
        Assert.False(decision.Result);
        Assert.Equal("skipped", (string?)result.Values["deliverable.note"]!["state"]);
    }

    [Fact]
    public async Task ARenderPastItsSteps_FailsWithItsSentence()
    {
        var plan = RunFixture.Plan(Minimal(), MinimalReads());
        var host = new FakeRunHost(auto: true).Render("deliverable.note", new RenderOutcome(null, true, null));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(RenderSteps: 100_000), host, recorder).RunAsync(Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("deliverable.note", result.FailedKey);
        Assert.Equal("Deliverable 'note' stopped rendering: it took more than 100,000 steps.", result.Sentence);
        V31Contract.AssertSentence(new SourceRefusal("S05-062", result.Sentence!, ""));
        Assert.Equal(RowState.Failed, recorder.Row("deliverable.note").State);
    }

    // ---- determinism ----------------------------------------------------------------------------

    [Fact]
    public void TheRunner_ReadsNoClockButTheHosts()
    {
        var folder = Path.Combine(RepoRoot(), "src", "Consultologist.PackageFormat", "V31", "Run");
        var files = Directory.GetFiles(folder, "*.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == "PlanRunner.cs");
        string[] forbidden = ["DateTime.UtcNow", "DateTime.Now", "DateTimeOffset.UtcNow", "DateTimeOffset.Now", "Stopwatch",
            "Guid.NewGuid", "new Random", "Random.Shared", "Task.Delay", "Task.Run", "ConfigureAwait(false)"];
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in forbidden)
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{Path.GetFileName(file)} uses {word}.");
        }
    }

    // ---- fixtures -------------------------------------------------------------------------------

    /// <summary>The inputs: the consult draft, and <c>include_billing</c> when given.</summary>
    internal static RunInputs Inputs(bool? includeBilling = null)
    {
        var inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["consult_draft"] = new JsonObject { ["value"] = Draft, ["content"] = Draft, ["title"] = null },
        };
        if (includeBilling is { } billing)
            inputs["include_billing"] = new JsonObject { ["value"] = billing, ["content"] = null, ["title"] = null };
        return new RunInputs(inputs, new JsonObject(), new Dictionary<string, string?>(StringComparer.Ordinal));
    }

    /// <summary>The inputs with a value that escaped validation: the boolean <c>include_billing</c> supplied as text.</summary>
    private static RunInputs Escaped()
    {
        var inputs = Inputs();
        var supplied = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
        {
            ["include_billing"] = new JsonObject { ["value"] = "yes", ["content"] = null, ["title"] = null },
        };
        return inputs with { Inputs = supplied };
    }

    /// <summary>What <see cref="SourceFixture.Minimal"/>'s bodies read.</summary>
    internal static StubTemplates MinimalReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["deliverable.note"] = ["node.plan.content"],
    });

    /// <summary>The boolean input <c>include_billing</c> (optional unless <paramref name="required"/>) added to the manifest.</summary>
    private static Dictionary<string, byte[]> WithBilling(Dictionary<string, byte[]> files, bool required = false) =>
        files.WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
            "{ \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", "
            + $"\"required\": {(required ? "true" : "false")}, \"channel\": null }}"));

    /// <summary><see cref="SourceFixture.Minimal"/>'s <c>plan</c> with the <c>if</c> <paramref name="condition"/>.</summary>
    private static string GatedAgent(string condition) => Md(
        SourceFixture.Node("agent", "Planning the sections", Plain, false, true,
            $"\"if\": {condition}, \"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
        "Plan the sections of a consultation note for the draft below.\n\n{{ input.consult_draft.content }}\n");

    /// <summary>
    /// <c>plan</c> (a <c>section_plan</c>) and <c>section_count</c>, the size of its sections, of format
    /// <paramref name="format"/>.
    /// </summary>
    private static Dictionary<string, byte[]> Counting(string format)
    {
        var files = ResolveFixture.WithFormats(Minimal(), "section_plan")
            .With("nodes/index.json", Index(["plan", "section_count"]))
            .With("nodes/plan.md", Agent("Planning the sections", "\"section_plan\"",
                "Plan the sections of a consultation note for the draft below.\n\n{{ input.consult_draft.content }}\n"))
            .With("nodes/section_count.md", Md(
                SourceFixture.Node("computed", "Counting the sections", format, true, true,
                    "\"if\": null, \"expression\": { \"size\": [{ \"var\": \"node.plan.value.sections\" }] }"),
                ""));
        return files;
    }

    private static StubTemplates CountingReads() => new(new Dictionary<string, string[]>
    {
        ["node.plan"] = ["input.consult_draft.content"],
        ["deliverable.note"] = ["node.plan.content"],
    });

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
