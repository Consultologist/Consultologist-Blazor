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
/// #1504 (specVersion 31, area 07; part 05 §§ 8.2 to 8.8; part 08 §§ 4, 4.3, 9; part 04 § 7.10; D3, D10, D11): macros
/// through whole runs (<see cref="PlanRunner.RunAsync"/>) on <see cref="ComposingRunHost"/>, which prepares, finishes and
/// renders with the code the engine's activities run. A classifier is an ordinary agent node that runs before the node
/// placing the macro its answer decides, and counts toward the job's agent calls; a triggered macro's body ends up in the
/// node's value and its row says it was offered and placed; a marker the model should not have written is invalid, and
/// <c>onInvalid</c> retries once naming what was wrong or fails naming the macro; a function decides its macros within
/// each call; a profile read in a macro body reaches the value and never a prompt. Content is fictional (Margaret
/// Okafor, Dr A. Sample).
/// </summary>
public class MacroRunTests
{
    private const string Body = "Follow up with Dr A. Sample in six weeks.";

    private const string Signature = "Dr A. Sample, consultant physician (fictional)";

    /// <summary>The classifier <c>classify</c> on the plan, and the agent <c>draft</c> placing the macro <c>m</c> (its form <paramref name="marker"/>) that the classifier's answer decides; the draft's prompt does not read the classifier.</summary>
    private static Dictionary<string, byte[]> Classified(string marker = "{{~ macro.m.content }}") =>
        Package()
            .WithAgent("classify", "Does the plan for Margaret Okafor need a follow-up?\n\n{{ node.plan.content }}\n", "\"judgement\"")
            .WithMacroOf("m", Body + "\n", ["Place the follow-up line after the plan:", marker], condition: PlacementDecisionsTests.MacroIf)
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ macro.m }}\n");

    /// <summary>
    /// A reply: the plan's sections for <c>plan</c>, a <c>judgement</c> of <paramref name="answer"/> for a classifier (any
    /// other agent of an object format), and
    /// <see cref="ComposingRunHost.EchoMarkers"/> (or <paramref name="text"/>, when given) for every other agent.
    /// </summary>
    private static Func<AgentCall, PreparedPrompt, string> Replies(
        Func<AgentCall, bool>? answer = null, Func<AgentCall, PreparedPrompt, string>? text = null) =>
        (call, prompt) =>
            call.EntryKey == "node.plan" ? PlacementDecisionsTests.Sections
            : !prompt.TextReply ? ComposingRunHost.Judgement(answer?.Invoke(call) ?? true)
            : (text ?? ComposingRunHost.EchoMarkers)(call, prompt);

    private static async Task<(RunResult Result, ComposingRunHost Host, ListRecorder Recorder)> RunAsync(
        RunPlan plan, Func<AgentCall, PreparedPrompt, string> reply, RunLimits? limits = null, RunInputs? inputs = null)
    {
        var host = new ComposingRunHost(plan, reply);
        var recorder = new ListRecorder(() => host.Calls.Count);
        var result = await new PlanRunner(plan, limits ?? new RunLimits(), host, recorder).RunAsync(inputs ?? PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        return (result, host, recorder);
    }

    private static string? Content(RunResult result, string key) => SourceLayout.Text(result.Values[key]?["content"]);

    private static JsonObject MacroRow(ListRecorder recorder, string key) =>
        Assert.Single(recorder.Row(key).Detail["macros"]!.AsArray())!.AsObject();

    [Fact]
    public async Task AClassifierRunsBeforeTheNodeThatPlacesItsMacro()
    {
        var plan = RunFixture.Plan(Classified());
        // The draft's compiled reads do not name the classifier; its macro's if does, and that is what holds it back.
        Assert.DoesNotContain("node.classify", plan.Entries["node.draft"].Json["reads"]!.AsArray().Select(r => (string)r!));
        Assert.Contains("node.classify", plan.Entries["node.draft"].Depends);

        var (result, host, recorder) = await RunAsync(plan, Replies());

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Equal(["node.plan", "node.classify", "node.draft"], host.Dispatched);
        var classified = recorder.Rows.FindIndex(r => r.Key == "node.classify");
        Assert.True(recorder.Stamps[classified] <= host.Dispatched.IndexOf("node.draft"), "the draft was called before its classifier settled");
        Assert.Contains("Place the follow-up line after the plan:", host.Prompt("node.draft").Text, StringComparison.Ordinal);
        Assert.Equal(3, result.AgentCalls);

        // The classifier is one of the job's agent calls: at two, the third call (the draft) is refused.
        var (capped, cappedHost, _) = await RunAsync(plan, Replies(), new RunLimits(JobAgentCalls: 2));
        Assert.Equal(RunState.Failed, capped.State);
        Assert.Equal("node.draft", capped.FailedKey);
        Assert.Equal("The job reached 2 agent calls, the most this engine runs for one job.", capped.Sentence);
        Assert.Equal(["node.plan", "node.classify"], cappedHost.Dispatched);

        // And at one call in flight it waits its turn like any other agent.
        var (serial, serialHost, _) = await RunAsync(plan, Replies(), new RunLimits(MaxAgentCalls: 1));
        Assert.Equal(RunState.Completed, serial.State);
        Assert.Equal(["node.plan", "node.classify", "node.draft"], serialHost.Dispatched);
    }

    [Fact]
    public async Task ATriggeredMacro_EndsUpInTheValue_AndItsRowSaysOfferedAndPlaced()
    {
        var plan = RunFixture.Plan(Classified());

        var (result, host, recorder) = await RunAsync(plan, Replies());

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Equal($"{WorkflowStubOutputs.Text("node.draft")}\n{Body}\n", Content(result, "node.draft"));
        Assert.DoesNotContain(Body, host.Prompt("node.draft").Text, StringComparison.Ordinal);

        var row = recorder.Row("node.draft");
        Assert.Equal(CanonicalJson.HashText(Content(result, "node.draft")!), row.ValueHash);
        Assert.Null(row.Detail["invalid"]);
        Assert.Equal(CanonicalJson.HashText(host.Prompt("node.draft").Text!), (string)row.Detail["inputHash"]!);
        var macro = MacroRow(recorder, "node.draft");
        Assert.Equal(
            CanonicalJson.Write(JsonNode.Parse($$"""
                { "id": "node.draft/macro:m", "macro": "macro.m", "offered": true, "decisions": ["node.draft/if:macro.m"],
                  "classifier": "node.classify", "placed": true, "form": "block", "contentHash": "{{CanonicalJson.HashText(Body)}}" }
                """)),
            CanonicalJson.Write(macro));

        // Its decision is recorded under the placing row's key, at the macro's site.
        var decision = Assert.Single(recorder.Decisions, d => d.Site == "node.draft");
        Assert.Equal(("macro.m", true), (decision.Member, decision.Result));
    }

    [Fact]
    public async Task AnUntriggeredMacro_IsNotOffered_AndItsMarkerInTheReplyIsInvalid()
    {
        var plan = RunFixture.Plan(Classified());

        // The classifier says no; the model writes the marker anyway, the first time.
        var (result, host, recorder) = await RunAsync(plan, Replies(_ => false,
            (call, _) => WorkflowStubOutputs.Text(call.Key) + (call.Attempt == 1 ? "\n{{~ macro.m.content }}\n" : "")));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var first = host.Prompt("node.draft");
        Assert.DoesNotContain("macro.", first.Text, StringComparison.Ordinal);
        Assert.Empty(first.Composition.Macros);

        var repair = host.Calls.Single(c => c.Key == "node.draft" && c.Attempt == 2);
        Assert.True(repair.Repair);
        var notOffered = ReplySentences.NotOffered("m");
        V31Contract.AssertSentence(new SourceRefusal("S05-055", notOffered, ""));
        Assert.Equal([notOffered], repair.Faults);
        Assert.EndsWith(
            $"\n\nYour last reply could not be used:\n- {notOffered}\nWrite the reply again, with each marker exactly as given above.",
            host.Prompt("node.draft", 2).Text, StringComparison.Ordinal);

        Assert.Equal(WorkflowStubOutputs.Text("node.draft"), Content(result, "node.draft"));
        var row = recorder.Row("node.draft");
        Assert.Equal(
            """{"action":"retry","attempts":2,"reason":"macro m: not offered"}""",
            CanonicalJson.Write(row.Detail["invalid"]));
        var macro = MacroRow(recorder, "node.draft");
        Assert.False((bool)macro["offered"]!);
        Assert.False((bool)macro["placed"]!);
        Assert.Null(macro["form"]);
        Assert.Null(macro["contentHash"]);
        Assert.Equal(("macro.m", false), (recorder.Decisions.Single(d => d.Site == "node.draft").Member, recorder.Decisions.Single(d => d.Site == "node.draft").Result));
    }

    [Fact]
    public async Task OnInvalidRetry_AsksOnce_WithTheFaults_ThenFails()
    {
        var plan = RunFixture.Plan(Classified());
        const string forgot = "Margaret Okafor will start letrozole 2.5 mg daily; reviewed with her daughter.";

        var (result, host, recorder) = await RunAsync(plan, Replies(text: (_, _) => forgot));

        var noMarker = ReplySentences.NoMarker("m");
        V31Contract.AssertSentence(new SourceRefusal("S05-053", noMarker, ""));
        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal("node.draft", result.FailedKey);
        Assert.Equal($"Node 'draft': {noMarker}", result.Sentence);

        var calls = host.Calls.Where(c => c.Key == "node.draft").ToList();
        Assert.Equal([1, 2], calls.Select(c => c.Attempt));
        Assert.Equal([false, true], calls.Select(c => c.Repair));
        Assert.Null(calls[0].Faults);
        Assert.Equal([noMarker], calls[1].Faults);
        Assert.All(host.Sent.OfType<string>(), sent => Assert.DoesNotContain(forgot, sent, StringComparison.Ordinal)); // never quoted

        var row = recorder.Row("node.draft");
        Assert.Equal(RowState.Failed, row.State);
        Assert.Equal($"Node 'draft': {noMarker}", row.Reason);
        Assert.Equal("""{"action":"retry","attempts":2,"reason":"macro m: missing"}""", CanonicalJson.Write(row.Detail["invalid"]));
        Assert.False((bool)MacroRow(recorder, "node.draft")["placed"]!);
        Assert.True((bool)MacroRow(recorder, "node.draft")["offered"]!);
    }

    [Fact]
    public async Task OnInvalidFail_FailsNamingTheMacro()
    {
        var plan = RunFixture.Plan(Classified().WithJson("nodes/draft.md", h => h["onInvalid"] = "fail"));

        // The block marker written inside a sentence.
        var (result, host, recorder) = await RunAsync(plan, Replies(text: (call, _) => WorkflowStubOutputs.Text(call.Key) + " {{~ macro.m.content }} and more."));

        var wrongForm = ReplySentences.WrongForm("m", askedBlock: true);
        V31Contract.AssertSentence(new SourceRefusal("S05-057", wrongForm, ""));
        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal($"Node 'draft': {wrongForm}", result.Sentence);
        Assert.Single(host.Calls, c => c.Key == "node.draft");
        Assert.Equal("""{"action":"fail","attempts":1,"reason":"macro m: altered"}""", CanonicalJson.Write(recorder.Row("node.draft").Detail["invalid"]));
    }

    [Fact]
    public async Task AFormatRepair_SendsNoFaults()
    {
        // D11: a reply that does not fit its format is repaired with the format's line, and no marker faults.
        var plan = RunFixture.Plan(Classified());

        var (result, host, recorder) = await RunAsync(plan, (call, prompt) =>
            call.EntryKey == "node.classify" && call.Attempt == 1 ? "Yes, it does." : Replies()(call, prompt));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        var repair = host.Calls.Single(c => c.Key == "node.classify" && c.Attempt == 2);
        Assert.True(repair.Repair);
        Assert.Equal([], repair.Faults!);
        Assert.EndsWith("Your last reply could not be used: it did not fit the required format. Write the reply again.",
            host.Prompt("node.classify", 2).Text, StringComparison.Ordinal);
        Assert.Equal("retry", (string)recorder.Row("node.classify").Detail["invalid"]!["action"]!);
    }

    /// <summary>
    /// Part 04 § 7.10: the function <c>section_a</c> (pattern A: its own classifier <c>classify</c>) and <c>section_b</c>
    /// (pattern B: the caller's answer passed in as <c>decision</c> and held by the computed node <c>classify</c>), each with
    /// the agent <c>draft</c> placing the function's own macro <c>follow_up</c> (decided on <c>node.classify</c>); the
    /// package calls <c>section_a</c> twice (<c>first</c>, <c>second</c>) and <c>section_b</c> once (<c>third</c>, on the
    /// top-level classifier <c>decide</c>).
    /// </summary>
    private static Dictionary<string, byte[]> Functions()
    {
        static string Call(string label, string function, string with) => Md(
            SourceFixture.Node("call", label, Plain, false, true, $"\"if\": null, \"function\": \"{function}\", \"with\": {{ {with} }}"), "");

        var files = Package()
            .WithAgent("decide", "Does the plan for Margaret Okafor need a follow-up?\n\n{{ node.plan.content }}\n", "\"judgement\"")
            .Listed("nodes/index.json", "first").With("nodes/first.md", Call("First section", "section_a", "\"draft\": \"input.consult_draft.content\""))
            .Listed("nodes/index.json", "second").With("nodes/second.md", Call("Second section", "section_a", "\"draft\": \"input.consult_draft.content\""))
            .Listed("nodes/index.json", "third").With("nodes/third.md", Call("Third section", "section_b",
                "\"draft\": \"input.consult_draft.content\", \"decision\": \"node.decide\""))
            .With("functions/index.json", Index(["section_a", "section_b"]));

        foreach (var (function, own) in new[] { ("section_a", false), ("section_b", true) })
        {
            var decision = own ? ", \"decision\": { \"label\": \"Decision\", \"description\": null, \"format\": \"judgement\" }" : "";
            files.With($"functions/{function}/index.json", Json(
                "{ \"label\": \"Draft a section\", \"description\": null, "
                + "\"inputs\": { \"draft\": { \"label\": \"Consult draft\", \"description\": null, \"format\": \"markdown_block\" }" + decision + " }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + "\"nodes\": [\"classify\", \"draft\"], \"ret\": \"node.draft\", \"format\": \"plaintext\" }"))
                .With($"functions/{function}/classify.md", own
                    ? Md(SourceFixture.Node("computed", "Decision (passed in)", "\"judgement\"", true, true,
                        "\"if\": null, \"expression\": { \"var\": \"param.decision.value\" }"), "")
                    : Agent("Classifying the section", "\"judgement\"", "Does this draft need a follow-up?\n\n{{ param.draft.content }}\n"))
                .With($"functions/{function}/draft.md", Agent("Drafting the section", Plain,
                    "Draft the section for Margaret Okafor.\n\n{{ param.draft.content }}\n{{~ macro.follow_up }}\n"))
                .With($"functions/{function}/macros/index.json", Index(["follow_up"]))
                .With($"functions/{function}/macros/follow_up.md", Md(
                    $"{{ \"label\": \"Follow-up\", \"description\": null, \"if\": {PlacementDecisionsTests.MacroIf}, \"format\": \"plaintext\", "
                    + "\"instruction\": [\"Place the follow-up line alone at the end:\", \"{{~ macro.follow_up.content }}\"] }",
                    $"{Body} ({function})\n"));
            foreach (var folder in new[] { "snippets", "formats", "renderers" })
                files.With($"functions/{function}/{folder}/index.json", Index([]));
        }

        return files;
    }

    [Fact]
    public async Task AMacroInAFunction_DecidesWithinTheCall()
    {
        var plan = RunFixture.Plan(Functions());

        // Pattern A: each call's own classifier decides, yes in the first call and no in the second; pattern B: the caller's.
        var (result, host, recorder) = await RunAsync(plan, Replies(call => call.Key is "node.first.classify" or "node.decide"));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Equal(
            [("node.first.draft", true), ("node.second.draft", false), ("node.third.draft", true)],
            recorder.Decisions.Where(d => d.Member == "macro.follow_up").Select(d => (d.Site, d.Result == true)).OrderBy(d => d.Site, StringComparer.Ordinal));
        Assert.Equal($"{WorkflowStubOutputs.Text("node.first.draft")}\n{Body} (section_a)\n", Content(result, "node.first"));
        Assert.Equal(WorkflowStubOutputs.Text("node.second.draft"), Content(result, "node.second"));
        Assert.Equal($"{WorkflowStubOutputs.Text("node.third.draft")}\n{Body} (section_b)\n", Content(result, "node.third"));

        // The macro rows are the call's, each linking to the classifier the decision read in that call.
        Assert.Equal("node.first.draft/macro:follow_up", (string)MacroRow(recorder, "node.first.draft")["id"]!);
        Assert.Equal("node.first.classify", (string?)MacroRow(recorder, "node.first.draft")["classifier"]);
        Assert.Equal("node.third.classify", (string?)MacroRow(recorder, "node.third.draft")["classifier"]);
        Assert.True((bool)MacroRow(recorder, "node.third.draft")["placed"]!);
        Assert.False((bool)MacroRow(recorder, "node.second.draft")["offered"]!);

        // Pattern B makes no agent call inside the function for its decision.
        Assert.DoesNotContain("node.third.classify", host.Dispatched);
        Assert.Contains("node.first.classify", host.Dispatched);
    }

    [Fact]
    public async Task AMacroBodyReadingTheProfile_PutsItInTheValue_NeverInThePrompt()
    {
        var plan = RunFixture.Plan(Package()
            .WithMacroOf("signoff", "With thanks,\n{{ profile.signature }}\n", ["End the letter with:", "{{~ macro.signoff.content }}"])
            .WithAgent("draft", "Draft the letter to Margaret Okafor's GP.\n\n{{ node.plan.content }}\n{{~ macro.signoff }}\n")
            .WithJson("nodes/draft.md", h => h["profileCarrying"] = true));
        var inputs = PlanRunnerLeavesTests.Inputs() with
        {
            Profile = new Dictionary<string, string?>(StringComparer.Ordinal) { ["signature"] = Signature },
        };

        var (result, host, recorder) = await RunAsync(plan, Replies(), inputs: inputs);

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Equal($"{WorkflowStubOutputs.Text("node.draft")}\nWith thanks,\n{Signature}\n", Content(result, "node.draft"));
        Assert.NotEmpty(host.Sent);
        Assert.All(host.Sent, sent => Assert.DoesNotContain(Signature, sent!, StringComparison.Ordinal));
        Assert.Equal(
            $$"""[{"field":"signature","hash":"{{CanonicalJson.HashText(Signature)}}"}]""",
            CanonicalJson.Write(recorder.Row("node.draft").Detail["profileReads"]));
        Assert.All(recorder.Rows, r => Assert.DoesNotContain(Signature, CanonicalJson.Write(r.Detail), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFolderMacro_RoundTripsThroughARun()
    {
        // #1504's first comment, through PlanRunner.RunAsync: the folder item's marker as the prompt shows it, written back.
        const string content = "History noted: Dr A. Sample.";
        static RunPlan Plan(params string[] instruction) =>
            RunFixture.Plan(Package()
                .Listed("macros/index.json", "section_notes")
                .With("macros/section_notes/index.json", Index(["hpi"], extra: ", \"if\": null"))
                .With("macros/section_notes/hpi.md", Md(
                    "{ \"label\": \"History\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
                    + $"\"instruction\": {new JsonArray(instruction.Select(l => (JsonNode?)l).ToArray()).ToJsonString()} }}",
                    content + "\n"))
                .WithAgent("draft", "Draft the history of present illness for Margaret Okafor.\n{{~ macro.section_notes.hpi }}\n"));

        var block = await RunAsync(Plan("Place the history's closing line alone, after the narrative:", "{{~ macro.section_notes.hpi.content }}"), Replies());
        var inline = await RunAsync(Plan("End the history with {{ macro.section_notes.hpi.content }} as its last sentence."), Replies());

        Assert.Equal($"{WorkflowStubOutputs.Text("node.draft")}\n{content}\n", Content(block.Result, "node.draft"));
        Assert.Equal($"{WorkflowStubOutputs.Text("node.draft")} {content}", Content(inline.Result, "node.draft"));
        Assert.Contains("{{~ macro.section_notes.hpi.content }}", block.Host.Prompt("node.draft").Text, StringComparison.Ordinal);
        Assert.Contains("{{ macro.section_notes.hpi.content }}", inline.Host.Prompt("node.draft").Text, StringComparison.Ordinal);
        foreach (var (run, form) in new[] { (block, "block"), (inline, "inline") })
        {
            var macro = MacroRow(run.Recorder, "node.draft");
            Assert.Equal(("macro.section_notes.hpi", true, true, form), ((string)macro["macro"]!, (bool)macro["offered"]!, (bool)macro["placed"]!, (string)macro["form"]!));
            Assert.Equal(new Dictionary<string, bool> { ["macro-folder.section_notes"] = true, ["macro.section_notes.hpi"] = true },
                run.Host.Calls.Single(c => c.Key == "node.draft").Placed);
            Assert.Empty(run.Recorder.Decisions);
        }
    }

    /// <summary>
    /// The folder <c>f</c> (its <c>if</c> null) with the macros <c>m</c> and <c>n</c> (theirs null): the part <c>p</c> of the
    /// agent <c>draft</c> (decided on the classifier's reason, <see cref="PlacementDecisionsTests.PartIf"/>) places
    /// <c>m</c>, first; the prompt itself places <c>n</c>. <paramref name="folderIf"/> is the folder's rule.
    /// </summary>
    internal static Dictionary<string, byte[]> FolderInTwoPlaces(string folderIf = "null")
    {
        static string Item(string id, string body) => Md(
            $"{{ \"label\": \"Macro {id}\", \"description\": null, \"if\": null, \"format\": \"plaintext\", "
            + $"\"instruction\": [\"Place this line alone at the end:\", \"{{{{~ macro.f.{id}.content }}}}\"] }}",
            body + "\n");

        return Package()
            .WithAgent("classify", "Does the plan for Margaret Okafor need a follow-up?\n\n{{ node.plan.content }}\n", "\"judgement\"")
            .Listed("macros/index.json", "f")
            .With("macros/f/index.json", Index(["m", "n"], extra: $", \"if\": {folderIf}"))
            .With("macros/f/m.md", Item("m", "Close with Dr A. Sample."))
            .With("macros/f/n.md", Item("n", Body))
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ part.p }}\n{{~ macro.f.n }}\n")
            .WithJson("nodes/draft.md", h => h["parts"] = JsonNode.Parse(
                $"{{ \"p\": {{ \"if\": {PlacementDecisionsTests.PartIf}, \"text\": [\"Close the plan.\", \"{{{{~ macro.f.m }}}}\"] }} }}"));
    }

    [Fact]
    public async Task AFolderMacro_IsOffered_WhenTheFolderFirstMacrosUnitIsNotPlaced()
    {
        // The part does not hold (an empty reason): its macro f.m is not decided, and the prompt's own f.n is offered.
        var plan = RunFixture.Plan(FolderInTwoPlaces());

        var (result, host, recorder) = await RunAsync(plan, (call, prompt) =>
            call.EntryKey == "node.classify" ? """{"answer":true,"reason":""}""" : Replies()(call, prompt));

        Assert.True(result.State == RunState.Completed, result.Sentence);
        Assert.Equal(
            new Dictionary<string, bool> { ["part.p"] = false, ["macro-folder.f"] = true, ["macro.f.n"] = true },
            host.Calls.Single(c => c.Key == "node.draft").Placed);
        Assert.Equal(["macro.f.n"], host.Prompt("node.draft").Composition.Macros.Select(m => m.Site));
        Assert.Equal($"{WorkflowStubOutputs.Text("node.draft")}\n{Body}\n", Content(result, "node.draft"));
        Assert.Equal([("part.p", false)], recorder.Decisions.Where(d => d.Site == "node.draft").Select(d => (d.Member, d.Result == true)));

        // A folder rule is decided once for the row, wherever its macros stand, and holds the macro back when false.
        var ruled = RunFixture.Plan(FolderInTwoPlaces(PlacementDecisionsTests.FolderIf));
        foreach (var reason in new[] { "", "The plan says so.", "Another reason." })
        {
            var (run, ruledHost, ruledRecorder) = await RunAsync(ruled, (call, prompt) =>
                call.EntryKey == "node.classify" ? $$"""{"answer":true,"reason":"{{reason}}"}""" : Replies()(call, prompt));
            Assert.True(run.State == RunState.Completed, run.Sentence);
            var folder = reason == "The plan says so.";
            Assert.Equal([folder], ruledRecorder.Decisions.Where(d => d.Site == "node.draft" && d.Member == "macro-folder.f").Select(d => d.Result == true));
            string[] offered = folder ? ["macro.f.m", "macro.f.n"] : []; // the reason given, the part holds
            Assert.Equal(offered, ruledHost.Prompt("node.draft").Composition.Macros.Select(m => m.Site));
        }
    }

    [Theory]
    [InlineData("  {{ \"\" }}\n")]
    [InlineData("\t{{ \"\" }}\n\n")]
    public async Task AMarkdownReply_OfOnlyAPlainMacroRenderingBlank_FailsIfEmpty(string body)
    {
        // Task 15's review: the plain-text macro's blank body ("  " or "\t\n") is escaped into the Markdown node (R9 writes
        // its leading space or tab as an entity), and the node still fails with failIfEmpty, as it did before R9.
        const string sentence = "The draft came back empty for Margaret Okafor.";
        var plan = RunFixture.Plan(Package()
            .WithMacroOf("m", body, ["Place the closing line alone:", "{{~ macro.m.content }}"])
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{~ macro.m }}\n", "\"markdown_block\"")
            .WithJson("nodes/draft.md", h => h["failIfEmpty"] = sentence));

        var (result, _, recorder) = await RunAsync(plan, Replies(text: (_, _) => "{{~ macro.m.content }}"));

        Assert.Equal(RunState.Failed, result.State);
        Assert.Equal(sentence, result.Sentence);
        Assert.Equal(sentence, recorder.Row("node.draft").Reason);
    }
}
