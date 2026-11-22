using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1504 (specVersion 31, area 07; part 09 §§ 3.1, 3.4; part 05 §§ 4, 8.3 to 8.6; D8, D9, D11): the Api's two activities
/// are thin callers of the template library. The agent activity sends <see cref="AgentTurn.Prepare"/>'s text (the
/// attested agent's <c>strictRejects</c> from <see cref="V31AgentsCatalogue"/>) and returns <see cref="AgentTurn.Finish"/>'s
/// outcome; a prompt that could not be prepared is never sent; the render activity returns <see cref="RenderTurn.Render"/>'s
/// text, neutral text and detail. Whole runs go through <see cref="PlanRunner"/> on a host that calls the real activities,
/// each input and outcome serialised as Durable Functions does. No log line carries a prompt, a reply or a profile value.
/// Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class AgentTurnActivityTests
{
    private const string Signature = "Dr A. Sample, consultant physician (fictional)";

    /// <summary>The package of <c>macros.run.txt</c> (<see cref="RunGoldenTests.Macros"/>): a classifier, a folder macro placed by a part, and run spots.</summary>
    internal static readonly CompiledGoldenTests.GoldenCase MacrosGolden =
        new("macros", RunGoldenTests.Macros, "a classifier (judgement), a part placing a folder macro, and run spots");

    /// <summary>The plan and the agent <c>draft</c> placing the macro <c>signoff</c>, whose body reads <c>profile.signature</c>.</summary>
    private static readonly CompiledGoldenTests.GoldenCase SignoffGolden =
        new("signoff", () => Package()
            .WithMacroOf("signoff", "With thanks,\n{{ profile.signature }}\n", ["End the letter with:", "{{~ macro.signoff.content }}"])
            .WithAgent("draft", "Draft the letter to Margaret Okafor's GP.\n\n{{ node.plan.content }}\n{{~ macro.signoff }}\n")
            .WithJson("nodes/draft.md", h => h["profileCarrying"] = true),
            "a macro whose body reads the profile");

    /// <summary>The plan and the agent <c>outline</c>, whose prompt loops over the plan's sections.</summary>
    private static readonly CompiledGoldenTests.GoldenCase OutlineGolden =
        new("outline", () => Package()
            .WithAgent("outline", "Outline the note for Margaret Okafor.\n{{ for section in node.plan.value.sections }}- {{ section.title }}\n{{ end }}"),
            "a prompt with a for");

    private static readonly JsonSerializerOptions Durable = new();

    [Fact]
    public async Task TheAgentActivity_SendsThePreparedText_AndReturnsTheFinishedReply()
    {
        var run = new ActivityRun(MacrosGolden);

        var result = await run.RunAsync(RunFixture.GoldenInputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-21" } });

        Assert.True(result.State == RunState.Completed, $"{result.State}: {result.FailedKey} {result.Sentence}");
        Assert.Equal(["node.plan", "node.classify", "node.draft"], run.Agent.Sent.Select(s => s.Stage));
        foreach (var (stage, message, agentName, agentVersion) in run.Agent.Sent)
        {
            var call = run.Calls[stage];
            var prepared = AgentTurn.Prepare(run.Plan, call, new RunLimits().RenderSteps, run.Catalogue.StrictRejects(agentName, int.Parse(agentVersion, System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Equal(prepared.Text, message);
        }

        // the draft's prompt offers the folder macro; its reply's marker is replaced by the macro's body
        var draft = run.Agent.Sent.Single(s => s.Stage == "node.draft");
        Assert.Contains("{{~ macro.section_notes.hpi.content }}", draft.Message, StringComparison.Ordinal);
        Assert.Equal(("test-json", "48"), (draft.AgentName, draft.AgentVersion));
        var classify = run.Agent.Sent.Single(s => s.Stage == "node.classify");
        Assert.Equal(("judgement", "1"), (classify.AgentName, classify.AgentVersion));
        var content = SourceLayout.Text(result.Values["node.draft"]?["content"]);
        Assert.Contains("History taken from Margaret Okafor and her daughter; reviewed by Dr A. Sample.", content, StringComparison.Ordinal);
        Assert.DoesNotContain("macro.section_notes.hpi", content, StringComparison.Ordinal);

        // the outcome's detail reaches the row: the macro offered and placed, the prompt's hashes
        var row = run.Recorder.Row("node.draft").Detail;
        Assert.Equal(CanonicalJson.HashText(draft.Message), (string?)row["inputHash"]);
        var macro = Assert.Single(row["macros"]!.AsArray())!.AsObject();
        Assert.True((bool)macro["offered"]! && (bool)macro["placed"]!);
        Assert.NotNull((string?)row["runNeutralHash"]);
    }

    [Fact]
    public async Task ACustomBinding_EndsThePromptWithItsFormat()
    {
        // D8: a via: "custom" binding (the plan's section_plan) is sent its format after AgentTurn.CustomTrailer
        var run = new ActivityRun(MacrosGolden);

        await run.RunAsync(RunFixture.GoldenInputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-21" } });

        var plan = run.Agent.Sent.Single(s => s.Stage == "node.plan");
        Assert.Equal(("custom-json", "1"), (plan.AgentName, plan.AgentVersion));
        Assert.Contains(AgentTurn.CustomTrailer, plan.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AgentTurn.CustomTrailer, run.Agent.Sent.Single(s => s.Stage == "node.classify").Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APromptThatFailsToRender_IsNeverSent()
    {
        var run = new ActivityRun(MacrosGolden);

        // the draft's body reads node.stamp and node.classify; an empty scope cannot compose it
        var outcome = await run.AgentAsync(new AgentCall("node.draft", "node.draft", null, 1, false, new JsonObject(), new Dictionary<string, bool>()));

        Assert.Equal((AgentOutcomeKind.Failed, "compose"), (outcome.Kind, outcome.ErrorKind));
        Assert.Null(outcome.Reply);
        Assert.Empty(run.Agent.Sent);
    }

    [Fact]
    public async Task APromptPastTheJobsStepBudget_IsNeverSent()
    {
        // the prompt is composed in the job's recorded renderSteps: its for over the plan's two sections takes two
        var run = new ActivityRun(OutlineGolden);
        var call = await run.CallOfAsync("node.outline");

        var outcome = await run.AgentAsync(call, renderSteps: 1);

        Assert.Equal((AgentOutcomeKind.Failed, "steps"), (outcome.Kind, outcome.ErrorKind));
        Assert.Empty(run.Agent.Sent);
        Assert.Equal(AgentOutcomeKind.Completed, (await run.AgentAsync(call, renderSteps: 2)).Kind);
        Assert.Single(run.Agent.Sent);
    }

    [Fact]
    public async Task ARepair_SendsTheRetryText()
    {
        var run = new ActivityRun(MacrosGolden);
        var call = await run.CallOfAsync("node.draft");
        var fault = "Macro 'section_notes.hpi' is offered, but the reply holds no marker for it.";

        await run.AgentAsync(call with { Attempt = 2, Repair = true, Faults = [] });
        await run.AgentAsync(call with { Attempt = 2, Repair = true, Faults = [fault] });

        var (repair, faulted) = (run.Agent.Sent[0].Message, run.Agent.Sent[1].Message);
        Assert.EndsWith("\n\nYour last reply could not be used: it did not fit the required format. Write the reply again.", repair, StringComparison.Ordinal);
        Assert.EndsWith($"\n\nYour last reply could not be used:\n- {fault}\nWrite the reply again, with each marker exactly as given above.", faulted, StringComparison.Ordinal);
        var first = AgentTurn.Prepare(run.Plan, call, new RunLimits().RenderSteps, ["format"]).Text!;
        Assert.StartsWith(first.TrimEnd(), repair, StringComparison.Ordinal);
        Assert.StartsWith(first.TrimEnd(), faulted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AReplyWithWrongMarkers_IsGivenBackAsItCame_WithItsFaults()
    {
        var run = new ActivityRun(MacrosGolden);
        var call = await run.CallOfAsync("node.draft");
        run.Reply = (_, _) => "The history, with no marker.";

        var outcome = await run.AgentAsync(call);

        Assert.Equal(AgentOutcomeKind.Completed, outcome.Kind);
        Assert.Equal("The history, with no marker.", outcome.Reply);
        var fault = Assert.Single(outcome.Invalid!);
        Assert.Equal("S05-053", fault.Id);
    }

    [Fact]
    public async Task TheRenderActivity_ReturnsTheNeutralText()
    {
        var run = new ActivityRun(MacrosGolden);

        var result = await run.RunAsync(RunFixture.GoldenInputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-21" } });

        Assert.Equal(RunState.Completed, result.State);
        var (call, outcome) = run.Renders.Single(r => r.Call.Key == "node.stamp");
        var expected = RenderTurn.Render(run.Plan, call);
        Assert.Equal("Seen 21 November 2026\n", outcome.Text);
        Assert.NotNull(outcome.NeutralText);
        Assert.NotEqual(outcome.Text, outcome.NeutralText);
        Assert.Equal(expected.NeutralText, outcome.NeutralText);
        Assert.Equal(CanonicalJson.Write(expected.Detail), CanonicalJson.Write(outcome.Detail));
        Assert.Equal(outcome.NeutralText, SourceLayout.Text(result.Values["node.stamp"]?["neutral"]));
        Assert.Equal(CanonicalJson.HashText(outcome.NeutralText!), (string?)run.Recorder.Row("node.stamp").Detail["runNeutralHash"]);
    }

    // ---- an engine fault in reading a template (#1505 ruling 6, R14) -----------------------------------

    /// <summary>The error lines of <paramref name="log"/> that say A08-006.</summary>
    private static List<string> EngineFaults<T>(CapturingLogger<T> log) =>
        log.Entries.Where(e => e.Level == LogLevel.Error && e.Message.Contains("A08-006", StringComparison.Ordinal)).Select(e => e.Message).ToList();

    private static Func<string, Scriban.Template> Throwing(Exception exception) => _ => throw exception;

    [Fact]
    public async Task AnEngineFault_ComposingAPrompt_IsLoggedOnce_ByItsType()
    {
        var run = new ActivityRun(MacrosGolden);
        var call = await run.CallOfAsync("node.draft");

        TemplateSyntax.TestParser.Value = Throwing(new InvalidOperationException("Margaret Okafor's history"));
        AgentOutcome outcome;
        try
        {
            outcome = await run.AgentAsync(call);
        }
        finally
        {
            TemplateSyntax.TestParser.Value = null;
        }

        Assert.Equal((AgentOutcomeKind.Failed, "compose"), (outcome.Kind, outcome.ErrorKind));
        Assert.Empty(run.Agent.Sent);
        var line = Assert.Single(EngineFaults(run.AgentLog));
        Assert.Contains("node.draft", line, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Margaret", run.AgentLog.Everything, StringComparison.Ordinal);
        Assert.DoesNotContain("Draft", run.AgentLog.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEngineFault_InARender_IsLoggedOnce_ByItsType()
    {
        var run = new ActivityRun(MacrosGolden);
        await run.RunAsync(RunFixture.GoldenInputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-21" } });
        var (call, _) = run.Renders.Single(r => r.Call.Key == "node.stamp");

        TemplateSyntax.TestParser.Value = Throwing(new NullReferenceException("Seen 21 November 2026"));
        RenderOutcome outcome;
        try
        {
            outcome = await run.RenderAsync(call);
        }
        finally
        {
            TemplateSyntax.TestParser.Value = null;
        }

        Assert.Null(outcome.Text);
        Assert.NotNull(outcome.Failure);
        var line = Assert.Single(EngineFaults(run.RenderLog));
        Assert.Contains("node.stamp", line, StringComparison.Ordinal);
        Assert.Contains("System.NullReferenceException", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Seen", run.RenderLog.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAuthorsFault_LogsNoEngineFault()
    {
        // What Scriban throws for unreadable text is A07-005, the author's: the call fails as before and nothing says A08-006.
        var run = new ActivityRun(MacrosGolden);
        var call = await run.CallOfAsync("node.draft");
        var (render, _) = run.Renders.Single(r => r.Call.Key == "node.stamp");

        TemplateSyntax.TestParser.Value = Throwing(new Scriban.Syntax.ScriptRuntimeException(default, "unreadable"));
        try
        {
            Assert.Equal(AgentOutcomeKind.Failed, (await run.AgentAsync(call)).Kind);
            Assert.Null((await run.RenderAsync(render)).Text);
        }
        finally
        {
            TemplateSyntax.TestParser.Value = null;
        }

        Assert.Empty(EngineFaults(run.AgentLog));
        Assert.Empty(EngineFaults(run.RenderLog));
        Assert.DoesNotContain(run.AgentLog.Entries.Concat(run.RenderLog.Entries), e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task NoLogLine_CarriesText()
    {
        var run = new ActivityRun(SignoffGolden);
        var inputs = PlanRunnerLeavesTests.Inputs() with
        {
            Profile = new Dictionary<string, string?>(StringComparer.Ordinal) { ["signature"] = Signature },
        };

        var result = await run.RunAsync(inputs);

        Assert.True(result.State == RunState.Completed, $"{result.State}: {result.FailedKey} {result.Sentence}");
        var value = SourceLayout.Text(result.Values["node.draft"]?["content"])!;
        Assert.Contains(Signature, value, StringComparison.Ordinal);
        Assert.NotEmpty(run.AgentLog.Recorded);
        var logged = run.AgentLog.Everything + "\n" + run.RenderLog.Everything;
        foreach (var (stage, message, _, _) in run.Agent.Sent)
        {
            Assert.DoesNotContain(message, logged, StringComparison.Ordinal);
            Assert.DoesNotContain(run.Replied[stage], logged, StringComparison.Ordinal);
        }

        foreach (var (_, outcome) in run.Renders)
            Assert.DoesNotContain(outcome.Text!, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(value, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(Signature, logged, StringComparison.Ordinal);
        Assert.DoesNotContain("Margaret Okafor", logged, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActivitiesInputsAndOutcomes_RoundTripAsDurableFunctionsSerialisesThem()
    {
        var placed = new Dictionary<string, bool>(StringComparer.Ordinal) { ["part.close"] = true, ["macro.section_notes.hpi"] = false };
        var scope = new JsonObject { ["node"] = new JsonObject { ["stamp"] = new JsonObject { ["content"] = "Seen today", ["neutral"] = "Seen {{run:1}}" } } };
        var detail = new JsonObject { ["inputHash"] = "sha256:" + new string('a', 64), ["macros"] = new JsonArray(new JsonObject { ["macro"] = "macro.m", ["placed"] = true }) };

        var agentInput = new RunAgentNodeV31Input("example", "1.0.0", "sha256:r", 7,
            new AgentCall("node.draft", "node.draft", null, 2, true, scope, placed, ["Macro 'm' is missing."]));
        var back = RoundTrip(agentInput);
        Assert.Equal(Json(agentInput), Json(back));
        Assert.Equal(placed, back.Call.Placed);
        Assert.Equal(["Macro 'm' is missing."], back.Call.Faults);
        Assert.Null(RoundTrip(agentInput with { Call = agentInput.Call with { Placed = null, Faults = null } }).Call.Faults);

        var outcome = new AgentOutcome(AgentOutcomeKind.Completed, "The reply.", 0, null, detail,
            [new ReplyFault("S05-053", "macro.m", "Macro 'm' is missing.", "macro m: missing")]);
        var outcomeBack = RoundTrip(outcome);
        Assert.Equal(Json(outcome), Json(outcomeBack));
        Assert.Equal(outcome.Invalid, outcomeBack.Invalid);
        Assert.Equal(CanonicalJson.Write(detail), CanonicalJson.Write(outcomeBack.Detail));

        var renderInput = new RenderNodeV31Input("example", "1.0.0", "sha256:r", new RenderCall("node.stamp", "node.stamp", null, scope, 100, placed));
        var renderBack = RoundTrip(renderInput);
        Assert.Equal(Json(renderInput), Json(renderBack));
        Assert.Equal(placed, renderBack.Call.Placed);

        var rendered = new RenderOutcome("Seen today", false, null, "Seen {{run:1}}", detail);
        var renderedBack = RoundTrip(rendered);
        Assert.Equal(("Seen today", "Seen {{run:1}}"), (renderedBack.Text, renderedBack.NeutralText));
        Assert.Equal(CanonicalJson.Write(detail), CanonicalJson.Write(renderedBack.Detail));

        // #1505 (R6): a rule's evaluation, its value a node and its failure the kind, the limit and the detail
        var ruleInput = new EvaluateRuleV31Input("example", "1.0.0", "sha256:r", 2000, new RuleCall("node.draft", "node.draft", null, "macro.m", scope));
        Assert.Equal(Json(ruleInput), Json(RoundTrip(ruleInput)));
        foreach (var ruled in new[] { new RuleOutcome(JsonValue.Create(true), null, null), new RuleOutcome(new JsonObject { ["named"] = false }, null, null),
            new RuleOutcome(null, "MatchTooSlow", 0.05, "has_word") })
            Assert.Equal(Json(ruled), Json(RoundTrip(ruled)));
    }

    private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Durable), Durable)!;

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Durable);

    /// <summary>
    /// A run of <paramref name="golden"/>, published into a <see cref="FakeRegistry"/> and loaded by the real store, on a host
    /// whose agent calls and renders are the real activities, each input and outcome serialised there and back.
    /// <see cref="Reply"/> answers the agent: by default the plan's sections, a <c>judgement</c> of yes, and for a text
    /// format the row key's stub followed by each marker its prompt offers.
    /// </summary>
    private sealed class ActivityRun : IRunHost
    {
        private readonly RunAgentNodeV31Activity _agent;
        private readonly RenderNodeV31Activity _render;
        private readonly EvaluateRuleV31Activity _rule;
        private readonly WorkflowPackageRef _ref;
        private readonly CompiledPackage _pair;

        public ActivityRun(CompiledGoldenTests.GoldenCase golden)
        {
            var registry = new FakeRegistry();
            (_ref, _pair) = registry.Put(golden);
            var store = V31Job.StoreOver(registry);
            var load = store.TryLoadAsync(_ref, CancellationToken.None).GetAwaiter().GetResult()!;
            Assert.True(load.Plan != null, $"{load.RefusalId} {load.OperatorDetail}");
            Plan = load.Plan!;
            Agent = new FakeAttempts(this);
            _agent = new RunAgentNodeV31Activity(store, Agent, Catalogue, AgentLog, new Random(1));
            _render = new RenderNodeV31Activity(store, RenderLog);
            _rule = new EvaluateRuleV31Activity(store, NullLogger<EvaluateRuleV31Activity>.Instance);
        }

        public RunPlan Plan { get; }

        public V31AgentsCatalogue Catalogue { get; } = V31AgentsCatalogue.Load();

        public FakeAttempts Agent { get; }

        public ListRecorder Recorder { get; } = new();

        public CapturingLogger<RunAgentNodeV31Activity> AgentLog { get; } = new();

        public CapturingLogger<RenderNodeV31Activity> RenderLog { get; } = new();

        /// <summary>The last agent call made for each row key, as the activity received it.</summary>
        public ConcurrentDictionary<string, AgentCall> Calls { get; } = new(StringComparer.Ordinal);

        /// <summary>What the model replied, by row key.</summary>
        public ConcurrentDictionary<string, string> Replied { get; } = new(StringComparer.Ordinal);

        /// <summary>The renders, each with what the activity returned.</summary>
        public List<(RenderCall Call, RenderOutcome Outcome)> Renders { get; } = [];

        /// <summary>The model's reply to the call keyed by its stage, given the message sent.</summary>
        public Func<string, string, string> Reply { get; set; } = Default;

        public DateTime UtcNow => FakeRunHost.Start;

        public Task<RunResult> RunAsync(RunInputs inputs) =>
            new PlanRunner(Plan, new RunLimits(), this, Recorder).RunAsync(inputs, FakeRunHost.Start);

        /// <summary>The call the runner makes for <paramref name="key"/> in a run of the golden inputs (the run is on this host's activities, then forgotten).</summary>
        public async Task<AgentCall> CallOfAsync(string key)
        {
            await RunAsync(RunFixture.GoldenInputs() with { RunFacts = new JsonObject { ["date"] = "2026-11-21" } });
            var call = Calls[key];
            Agent.Sent.Clear();
            return call;
        }

        public async Task<AgentOutcome> AgentAsync(AgentCall call, int? renderSteps = null)
        {
            var input = RoundTrip(new RunAgentNodeV31Input(_ref.Name, _ref.Version, _pair.ResolveHash, renderSteps ?? new RunLimits().RenderSteps, call));
            Calls[call.Key] = input.Call;
            return RoundTrip(await _agent.RunAsync(input, CancellationToken.None));
        }

        public Task<AgentOutcome> CallAgentAsync(AgentCall call) => AgentAsync(call);

        public async Task<RenderOutcome> RenderAsync(RenderCall call)
        {
            var input = RoundTrip(new RenderNodeV31Input(_ref.Name, _ref.Version, _pair.ResolveHash, call));
            var outcome = RoundTrip(await _render.RunAsync(input, CancellationToken.None));
            lock (Renders)
                Renders.Add((input.Call, outcome));
            return outcome;
        }

        public async Task<RuleOutcome> EvaluateAsync(RuleCall call) =>
            RoundTrip(await _rule.RunAsync(RoundTrip(new EvaluateRuleV31Input(_ref.Name, _ref.Version, _pair.ResolveHash, 2000, call)), CancellationToken.None));

        public Task DelayAsync(TimeSpan wait, CancellationToken cancel) =>
            wait < TimeSpan.FromMinutes(1) ? Task.CompletedTask : Task.Delay(Timeout.Infinite, cancel);

        private static string Default(string stage, string message)
        {
            if (stage == "node.plan")
                return PlacementDecisionsTests.Sections;
            if (stage.EndsWith("classify", StringComparison.Ordinal))
                return ComposingRunHost.Judgement(true);
            var text = WorkflowStubOutputs.Text(stage);
            foreach (var marker in new[] { "macro.section_notes.hpi.content", "macro.signoff.content" })
            {
                if (message.Contains($"{{{{~ {marker} }}}}", StringComparison.Ordinal))
                    text += $"\n{{{{~ {marker} }}}}\n";
            }

            return text;
        }
    }

    /// <summary>The model: records what it was sent and answers with <see cref="ActivityRun.Reply"/>.</summary>
    private sealed class FakeAttempts(ActivityRun run) : IAgentAttempts
    {
        public List<(string Stage, string Message, string AgentName, string AgentVersion)> Sent { get; } = [];

        public Task<AgentAttempt> TryPromptOnceAsync(string stage, string userMessage, string agentName, string agentVersion, CancellationToken cancellationToken)
        {
            lock (Sent)
                Sent.Add((stage, userMessage, agentName, agentVersion));
            var reply = run.Reply(stage, userMessage);
            run.Replied[stage] = reply;
            return Task.FromResult(new AgentAttempt(AgentOutcomeKind.Completed, reply, null, null, null));
        }
    }
}
