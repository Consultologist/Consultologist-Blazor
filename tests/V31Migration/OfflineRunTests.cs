using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Engine;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.Api.V31;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Harness;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10, Task 13; part 10 § 3.1; part 07 § 8; D12; Review Focus 5): the offline, deterministic run
/// of a compiled 31 package (<see cref="OfflineRun"/>) and the start's input building moved into the library
/// (<see cref="RunInputsBuilder"/>). Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class OfflineRunTests
{
    private static readonly DateTime Start = FakeRunHost.Start;

    private static readonly IReadOnlyDictionary<string, string?> NoProfile = new Dictionary<string, string?>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, JsonNode?> NoOutputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);

    // ---- the start's inputs, in the library ---------------------------------------------------------------------

    /// <summary>The cases of <see cref="V31StartChecksTests"/>' input tests, each a change to its valid inputs (and its plan).</summary>
    public static TheoryData<string> StartCases() => new(StartCase.Keys.Order(StringComparer.Ordinal));

    private static readonly Dictionary<string, (Action<Dictionary<string, JsonNode?>> Change, bool Headline, JsonObject Facts)> StartCase = new(StringComparer.Ordinal)
    {
        ["valid"] = (_ => { }, false, new JsonObject()),
        ["missing-required"] = (s => s.Remove("consult_draft"), false, new JsonObject()),
        ["required-null"] = (s => s["sections"] = null, false, new JsonObject()),
        ["blank-required-texts"] = (s => { s["consult_draft"] = "\n\t"; s["code"] = "  "; }, false, new JsonObject()),
        ["one-character"] = (s => { s["consult_draft"] = "x"; s["code"] = "x"; }, false, new JsonObject()),
        ["blank-optional"] = (s => s["notes"] = "   ", false, new JsonObject()),
        ["empty-list"] = (s => s["sections"] = new JsonArray(), false, new JsonObject()),
        ["numeric-enum"] = (s => s["dose"] = 5, false, new JsonObject()),
        ["numeric-enum-unlisted"] = (s => s["dose"] = 3, false, new JsonObject()),
        ["wrong-shape"] = (s => { s["include_billing"] = "yes"; s["sections"] = JsonNode.Parse("""[{"title":"History"}]"""); }, false, new JsonObject()),
        ["inline-one-line"] = (s => s["headline"] = new JsonObject { ["line"] = "Seen by **Dr A. Sample** in clinic." }, true, new JsonObject()),
        ["inline-two-lines"] = (s => s["headline"] = new JsonObject { ["line"] = "Seen by **Dr A. Sample**\nin clinic." }, true, new JsonObject()),
        ["undeclared"] = (s => s["referral_letter"] = V31StartChecksTests.Draft, false, new JsonObject()),
        ["labelled-choice"] = (s => { s["kind"] = "follow_up"; s["include_billing"] = true; }, false, new JsonObject()),
        ["run-facts"] = (_ => { }, false, new JsonObject { ["id"] = "job-1", ["date"] = "2026-11-16" }),
    };

    [Theory]
    [MemberData(nameof(StartCases))]
    public void TheStartsInputs_AreBuiltInTheLibrary(string name)
    {
        var (change, headline, facts) = StartCase[name];
        var plan = V31StartChecksTests.Plan(headline: headline);
        var supplied = V31StartChecksTests.Valid();
        change(supplied);
        var profile = new Dictionary<string, string?>(StringComparer.Ordinal) { ["name"] = "Dr A. Sample" };

        var api = V31StartChecks.Inputs(plan, supplied, facts, profile);
        var library = RunInputsBuilder.Build(plan, supplied, facts, profile);

        Assert.Equal(Said(api.Refusals), Said(library.Refusals));
        Assert.Equal(api.Inputs is null, library.Inputs is null);
        if (api.Inputs is { } a && library.Inputs is { } b)
        {
            Assert.Equal(a.Inputs.Keys, b.Inputs.Keys);
            Assert.All(a.Inputs.Keys, id => Assert.Equal(CanonicalJson.Write(a.Inputs[id]), CanonicalJson.Write(b.Inputs[id])));
            Assert.Equal(CanonicalJson.Write(a.RunFacts), CanonicalJson.Write(b.RunFacts));
            Assert.Equal(a.Profile, b.Profile);
        }
    }

    // ---- the same texts as the composing host ----------------------------------------------------------------

    [Fact]
    public async Task AnOfflineRun_GivesWhatTheComposingHostGives_OnMacros()
    {
        // The macros run's package (RunGoldenTests.Macros): the plan and the classifier pinned as the golden's host answers
        // them; the draft generated, with the markers its prompt offers appended (D12), as ComposingRunHost.EchoMarkers writes.
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        var facts = new JsonObject { ["date"] = "2026-11-21" };
        var host = new ComposingRunHost(plan, (call, prompt) =>
            call.EntryKey == "node.plan" ? PlacementDecisionsTests.Sections
            : prompt.TextReply ? ComposingRunHost.EchoMarkers(call, prompt)
            : ComposingRunHost.Judgement(true));
        var expected = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(RunFixture.GoldenInputs() with { RunFacts = facts }, Start);

        var offline = await OfflineRun.RunAsync(plan, Request(plan, facts, new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["node.plan"] = JsonNode.Parse(PlacementDecisionsTests.Sections),
            ["node.classify"] = JsonNode.Parse(ComposingRunHost.Judgement(true)),
        }));

        Assert.Equal(RunState.Completed, expected.State);
        AssertSame(expected, host, offline);
        Assert.Contains(offline.Prompts, p => p.Key == "node.draft");
        Assert.Equal(ComposingRunHost.EchoMarkers(host.Calls.Single(c => c.Key == "node.draft"), host.Prompt("node.draft")), (string?)offline.Outputs["node.draft"]);
    }

    [Fact]
    public async Task AnOfflineRun_GivesWhatTheComposingHostGives_OnCheckedNote()
    {
        // checked-note with every reply generated: the composing host answers each call with part 10 § 3.1's stub of its row
        // key (RunFixture.StubReply), which is what the offline run generates for a missing output.
        var plan = RunFixture.Plan("checked-note");
        var host = new ComposingRunHost(plan, (call, _) => RunFixture.StubReply(plan, call));
        var expected = await new PlanRunner(plan, new RunLimits(), host, new ListRecorder()).RunAsync(RunFixture.GoldenInputs(), Start);

        var offline = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), NoOutputs));

        AssertSame(expected, host, offline);
        Assert.Equal(host.Calls.Select(c => c.Key).Distinct(), offline.Outputs.Keys);
        Assert.Empty(offline.UnusedOutputs);
    }

    // ---- generated outputs ------------------------------------------------------------------------------------

    [Fact]
    public async Task AMissingOutput_IsGeneratedFromTheFormat()
    {
        var plan = RunFixture.Plan(Formats());

        var run = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), NoOutputs));

        Assert.True(run.State == RunState.Completed, run.Sentence);
        foreach (var id in new[] { "plain", "block", "inline", "judge", "classify", "terms", "nullable", "visit", "dated" })
        {
            var key = $"node.{id}";
            var format = plan.Entries[key].Format;
            var served = run.Outputs[key];
            Assert.True(plan.Formats.Validate(served, format).Valid, $"{key}: the stub is not a value of its format.");
            var expected = plan.Formats.IsText(format)
                ? (JsonNode?)JsonValue.Create(WorkflowStubOutputs.Text(key))
                : WorkflowStubOutputs.FromSchema(plan.Formats.Expand(format), key);
            Assert.Equal(CanonicalJson.Write(expected), CanonicalJson.Write(served));
            Assert.Equal(RowState.Completed, run.Rows.Single(r => r.Key == key).State);
        }

        // a markdown_inline stub is one line (D21)
        Assert.DoesNotContain('\n', (string)run.Outputs["node.inline"]!);
        // a labelled oneOf's stub is one of its constants; a date's a calendar date
        Assert.Contains((string?)run.Outputs["node.visit"]!["visit_type"], new[] { "new_patient", "follow_up" });
        Assert.Matches("^20[0-9]{2}-[01][0-9]-[0-3][0-9]$", (string)run.Outputs["node.dated"]!["seen_on"]!);

        // seeded by StubSeed when given: the row key goes in, the seed comes out
        var seeded = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), NoOutputs) with { StubSeed = key => key["node.".Length..] });
        Assert.Equal(WorkflowStubOutputs.Text("plain"), (string?)seeded.Outputs["node.plain"]);
        Assert.Equal(CanonicalJson.Write(WorkflowStubOutputs.FromSchema(plan.Formats.Expand(plan.Entries["node.judge"].Format), "judge")), CanonicalJson.Write(seeded.Outputs["node.judge"]));
    }

    [Fact]
    public void TheStubs_KeepEveryExistingSeedsOutput()
    {
        // only a labelled oneOf and format: date are new; a plain string, an enum and a nullable string are as before
        Assert.Equal("\"stub " + Hex("s", "$") + "\"", WorkflowStubOutputs.FromSchema(JsonNode.Parse("""{"type":"string"}"""), "s")!.ToJsonString());
        Assert.Equal("\"stub " + Hex("s", "$") + "\"", WorkflowStubOutputs.FromSchema(JsonNode.Parse("""{"type":["string","null"]}"""), "s")!.ToJsonString());

        var labelled = JsonNode.Parse("""{"type":["string","null"],"oneOf":[{"const":"new_patient","title":"New patient"},{"const":"follow_up","title":"Follow-up"},{"const":null}]}""");
        var picks = Enumerable.Range(0, 32).Select(i => (string?)WorkflowStubOutputs.FromSchema(labelled, $"n{i}")).ToHashSet();
        Assert.Equal(new HashSet<string?> { "new_patient", "follow_up" }, picks);

        var date = (string)WorkflowStubOutputs.FromSchema(JsonNode.Parse("""{"type":"string","format":"date"}"""), "d")!;
        Assert.True(DateOnly.TryParseExact(date, "yyyy-MM-dd", out _), date);
        Assert.Equal(date, (string)WorkflowStubOutputs.FromSchema(JsonNode.Parse("""{"type":"string","format":"date"}"""), "d")!);
    }

    // ---- D12's markers -----------------------------------------------------------------------------------------

    [Fact]
    public async Task AnOfferedMarker_IsAppendedToAStub()
    {
        // The ordered agent draft places macro b before macro a: a generated stub gets b's marker, then a's.
        var plan = RunFixture.Plan(Ordered());
        var generated = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), NoOutputs));

        Assert.True(generated.State == RunState.Completed, generated.Sentence);
        Assert.Equal(
            WorkflowStubOutputs.Text("node.draft") + " {{ macro.b.content }} {{ macro.a.content }}",
            (string?)generated.Outputs["node.draft"]);

        // A pinned reply that holds b's marker already gets a's alone; one holding both gets none: never twice.
        var pinned = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["node.draft"] = "Seen with Margaret Okafor. {{ macro.b.content }}",
        }));
        Assert.True(pinned.State == RunState.Completed, pinned.Sentence);
        Assert.Equal("Seen with Margaret Okafor. {{ macro.b.content }} {{ macro.a.content }}", (string?)pinned.Outputs["node.draft"]);

        var both = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["node.draft"] = "{{ macro.b.content }} then {{ macro.a.content }}",
        }));
        Assert.Equal("{{ macro.b.content }} then {{ macro.a.content }}", (string?)both.Outputs["node.draft"]);
        Assert.Equal(RunState.Completed, both.State);
    }

    // ---- pinned outputs, the start, the end, the clock ---------------------------------------------------------

    [Fact]
    public async Task APinnedOutputThatNeverRuns_IsReported()
    {
        var plan = RunFixture.Plan("checked-note");
        var run = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["node.sections.draft:9"] = "A section the plan never names.",
            ["node.parts_for.draft:1"] = "A node the migration renamed.",
            ["node.plan"] = JsonNode.Parse(CanonicalJson.Write(WorkflowStubOutputs.FromSchema(plan.Formats.Expand(plan.Entries["node.plan"].Format), "node.plan"))),
        }));

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(["node.parts_for.draft:1", "node.sections.draft:9"], run.UnusedOutputs);
    }

    [Fact]
    public async Task ARefusedStart_HasNoState()
    {
        var plan = RunFixture.Plan("checked-note");
        var request = Request(plan, new JsonObject(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["node.plan"] = "x" }) with
        {
            Inputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal) { ["consult_draft"] = "  ", ["referral"] = "Margaret Okafor" },
        };

        var run = await OfflineRun.RunAsync(plan, request);

        Assert.Null(run.State);
        Assert.Null(run.Sentence);
        Assert.Equal(["A06-013", "A06-011"], run.StartRefusals.Select(r => r.Id));
        Assert.Empty(run.Prompts);
        Assert.Empty(run.Renders);
        Assert.Empty(run.Deliverables);
        Assert.Empty(run.Rows);
        Assert.Empty(run.Decisions);
        Assert.Empty(run.Outputs);
        Assert.Equal(["node.plan"], run.UnusedOutputs);
    }

    [Fact]
    public async Task AnOfflineRun_AlwaysEnds_AWhileThatNeverStops_EndsAtItsMax()
    {
        // nested-note's refine runs while the judge says revise, at most 3 passes; every pass is pinned to revise.
        var plan = RunFixture.Plan("nested-note");
        var outputs = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        for (var pass = 1; pass <= 4; pass++)
            outputs[$"node.refine.judge#{pass}"] = new JsonObject { ["verdict"] = "revise" };

        var run = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), outputs));

        Assert.True(run.State == RunState.Completed, run.Sentence);
        Assert.Equal(["node.refine.judge#1", "node.refine.judge#2", "node.refine.judge#3"], run.Prompts.Select(p => p.Key).Where(k => k.StartsWith("node.refine.judge", StringComparison.Ordinal)));
        Assert.Equal(["node.refine.judge#4"], run.UnusedOutputs);
    }

    [Fact]
    public async Task AnOfflineRun_AlwaysEnds_AReplyThatCannotFit_FailsTheNodeWithItsSentence()
    {
        // 31's formats have no pattern (S02-021), so no stub meets one; the one pattern a value is held to is markdown_inline's
        // single line (D21). A reply holding a line ending fails its node, after its repair, with the runner's sentence.
        var refusals = TemplateFixture.RefusalsOf(Formats().WithFormat("coded", """{ "type": "object", "required": ["code"], "additionalProperties": false, "properties": { "code": { "type": "string", "pattern": "^[A-Z][0-9]{2}$" } } }"""));
        Assert.Contains(refusals, r => r.Id == "S02-021");

        var plan = RunFixture.Plan(Formats());
        var run = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), new Dictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["node.inline"] = "Seen by Dr A. Sample\nin clinic.",
        }));

        Assert.Equal(RunState.Failed, run.State);
        Assert.Equal("Node 'inline' gave a reply that isn't markdown_inline: markdown_inline text sits inside a line and holds no line ending.", run.Sentence);
        Assert.Equal(RowState.Failed, run.Rows.Single(r => r.Key == "node.inline").State);
    }

    [Fact]
    public async Task TheRun_ReadsNoClock()
    {
        foreach (var name in new[] { "nested-note", "checked-note", "conditional" })
        {
            var plan = RunFixture.Plan(name);
            var first = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), NoOutputs));
            await Task.Delay(5);
            var second = await OfflineRun.RunAsync(plan, Request(plan, new JsonObject(), NoOutputs));

            Assert.Equal(Trace(first), Trace(second));
            Assert.All(first.Decisions, d => Assert.Equal(Start, d.DecidedAtUtc));
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    /// <summary>A request on the golden inputs <paramref name="plan"/> declares, as sent (each input's value), with no profile.</summary>
    private static OfflineRequest Request(RunPlan plan, JsonObject facts, IReadOnlyDictionary<string, JsonNode?> outputs)
    {
        var supplied = RunFixture.GoldenInputs().Inputs
            .Where(i => plan.Inputs.ContainsKey(i.Key))
            .ToDictionary(i => i.Key, i => i.Value?["value"]?.DeepClone(), StringComparer.Ordinal);
        return new OfflineRequest(supplied, NoProfile, facts, outputs, Start);
    }

    /// <summary>The offline run's prompts, renders and deliverables are the composing host's, text for text and hash for hash.</summary>
    private static void AssertSame(RunResult expected, ComposingRunHost host, OfflineResult offline)
    {
        Assert.Equal(expected.State, offline.State);
        var prompts = host.Calls.Zip(host.Prompts).Where(p => p.Second.Text != null)
            .GroupBy(p => p.First.Key).Select(g => g.Last()).Select(p => (p.First.Key, p.Second.Text!)).ToList();
        Assert.Equal(prompts, offline.Prompts.Select(p => (p.Key, p.Text)));
        Assert.All(offline.Prompts, p => Assert.Equal(CanonicalJson.HashText(p.Text), p.Hash));
        Assert.All(offline.Prompts, p => Assert.Equal(p.Hash, (string?)offline.Rows.Single(r => r.Key == p.Key).Detail["inputHash"]));

        var renders = host.Renders.Zip(host.RenderOutcomes).Where(r => r.Second.Text != null && !r.First.Key.StartsWith("deliverable.", StringComparison.Ordinal))
            .Select(r => (r.First.Key, r.Second.Text!)).ToList();
        Assert.Equal(renders, offline.Renders.Select(r => (r.Key, r.Text)));
        Assert.All(offline.Renders, r => Assert.Equal(CanonicalJson.HashText(r.Text), r.Hash));

        Assert.Equal(
            expected.Deliverables.Select(d => (d.Key, d.State, d.Text, d.ContentHash)),
            offline.Deliverables.Select(d => (d.Key, d.State, d.Text, d.ContentHash)));
        Assert.NotEmpty(offline.Deliverables);
    }

    /// <summary>Everything a run gave, as text: its state, rows (with their detail), decisions, prompts, renders and deliverables.</summary>
    private static string Trace(OfflineResult run) =>
        $"{run.State} {run.Sentence}\n"
        + string.Join("\n", run.Rows.Select(r => $"{RunTrace.Row(r)} | {CanonicalJson.Write(r.Detail)}")) + "\n--\n"
        + string.Join("\n", run.Decisions.Select(d => CanonicalJson.Write(d.ToJson()))) + "\n--\n"
        + string.Join("\n", run.Prompts.Concat(run.Renders).Select(t => $"{t.Key} {t.Hash}")) + "\n--\n"
        + string.Join("\n", run.Deliverables.Select(d => $"{d.Key} {d.State} {d.ContentHash}"));

    private static string Said(IEnumerable<SourceRefusal> refusals) => string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}"));

    private static string Hex(string seed, string path) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed + "\n" + path)))[..8];

    /// <summary>
    /// A package with an agent per format the stubs must fit: plaintext, markdown_block, markdown_inline, judgement,
    /// classification, concept_list, an object with nullable properties, a labelled oneOf and a date.
    /// </summary>
    internal static Dictionary<string, byte[]> Formats()
    {
        return TemplateFixture.Package()
            .WithAgent("plain", "Write a line for Margaret Okafor.\n")
            .WithAgent("block", "Write a paragraph for Margaret Okafor.\n", "\"markdown_block\"")
            .WithAgent("inline", "Write a heading for Margaret Okafor.\n", "\"markdown_inline\"")
            .WithAgent("judge", "Is the draft complete?\n", "\"judgement\"")
            .WithAgent("classify", "Classify the referral: urgent or routine.\n", "\"classification\"")
            .WithAgent("terms", "List the terms in the draft.\n", "\"concept_list\"")
            .WithFormat("nullable", """{ "type": "object", "required": ["note", "seen_by"], "additionalProperties": false, "properties": { "note": { "type": ["string", "null"] }, "seen_by": { "type": ["string", "null"] } } }""")
            .WithAgent("nullable", "Note who saw Margaret Okafor.\n", "\"nullable\"")
            .WithFormat("visit", """{ "type": "object", "required": ["visit_type"], "additionalProperties": false, "properties": { "visit_type": { "type": "string", "oneOf": [ { "const": "new_patient", "title": "New patient" }, { "const": "follow_up", "title": "Follow-up" } ] } } }""")
            .WithAgent("visit", "Say what kind of visit this was.\n", "\"visit\"")
            .WithFormat("dated", """{ "type": "object", "required": ["seen_on"], "additionalProperties": false, "properties": { "seen_on": { "type": "string", "format": "date" } } }""")
            .WithAgent("dated", "Say when Margaret Okafor was seen.\n", "\"dated\"")
            .WithDeliverable("{{ node.block.content }}\n");
    }

    /// <summary>A package whose ordered agent <c>draft</c> places the macro <c>b</c>, then the macro <c>a</c>.</summary>
    private static Dictionary<string, byte[]> Ordered() =>
        TemplateFixture.Package()
            .WithMacro("a", "Reviewed with Margaret Okafor.\n")
            .WithMacro("b", "Seen by Dr A. Sample.\n")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.b }}\n{{ macro.a }}\n")
            .WithJson("nodes/draft.md", h => h["ordered"] = true)
            .WithDeliverable("{{ node.draft.content }}\n");
}

/// <summary>A package format added to a fixture.</summary>
internal static class OfflineRunFixture
{
    /// <summary>Adds the package format <paramref name="id"/> whose schema is <paramref name="schema"/> (JSON text, without <c>$schema</c>).</summary>
    public static Dictionary<string, byte[]> WithFormat(this Dictionary<string, byte[]> files, string id, string schema)
    {
        var json = JsonNode.Parse(schema)!.AsObject();
        var format = new JsonObject { ["$schema"] = "urn:consultologist:spec:31:dialect" };
        foreach (var (name, value) in json)
            format[name] = value?.DeepClone();
        return files.Listed("formats/index.json", id).With($"formats/{id}.json", SourceFixture.Json(format.ToJsonString()));
    }
}
