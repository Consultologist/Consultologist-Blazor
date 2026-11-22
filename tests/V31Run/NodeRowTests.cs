using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 2.3, 4, 7, 8.2, 9, 11; R7, R9, R10, R14, D2, D5): a node row is part 08's row.
/// <see cref="RunRow.ToJson"/> writes every member of § 4, the entry's facts from <c>resolve.json</c>, its sub-rows placed in
/// runes, each with its pass inside a template <c>for</c>; a macro body's run facts are on its macro row; a snippet
/// substituted into a macro's body is marked not sent; tokens are summed over the attempts; and no row holds a value's text:
/// an item name drawn from an input and a run spot's output that is not a raw run fact are on <see cref="RunRow.Text"/>
/// only. Runs go through <see cref="ComposingRunHost"/> (the real <see cref="AgentTurn"/> and <see cref="RenderTurn"/>) or
/// the stubbed goldens. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class NodeRowTests
{
    /// <summary>Part 08 § 4's members of a node row (storage 11's, § 4's), and area 06's <c>function</c> and <c>depth</c>.</summary>
    private static readonly string[] NodeMembers =
    [
        "nodeId", "itemId", "label", "status", "inputHash", "outputHash", "hashVersion", "tokens", "stateReason",
        "kind", "producer", "formatHash", "typeHash", "sentFormatHash", "notSent", "agent", "itemName", "itemNameHash", "positionName",
        "runDependent", "modelDerived", "profileCarrying", "runNeutralHash", "runSpots", "renders", "macros", "units", "invalid",
        "retries", "profileReads",
        "function", "depth",
        "startedAtUtc", "completedAtUtc", "durationMs",
    ];

    private static readonly JsonObject RunDate = new() { ["date"] = "2026-11-21" };

    // ---- the members ----------------------------------------------------------------------------

    [Fact]
    public async Task AnAgentRow_HasEveryMemberOfPart08()
    {
        var run = await ComposedAsync(RunGoldenTests.Macros());

        var row = run.Recorder.Row("node.draft").ToJson();

        Assert.Equal(NodeMembers.Order(StringComparer.Ordinal), row.Select(m => m.Key).Order(StringComparer.Ordinal));
        Assert.Equal("node.draft", (string?)row["nodeId"]);
        Assert.Null(row["itemId"]);
        Assert.Equal("agent", (string?)row["kind"]);
        Assert.Equal("Completed", (string?)row["status"]);
        Assert.Null(row["stateReason"]);
        Assert.Equal("generated", (string?)row["producer"]);
        Assert.True((bool)row["modelDerived"]!);
        Assert.True((bool)row["runDependent"]!);
        Assert.Equal(7, (int)row["hashVersion"]!);
        Assert.Null(row["tokens"]); // the composing host's model says no tokens
        var entry = run.Plan.Entries["node.draft"].Json;
        Assert.Equal((string?)entry["formatHash"], (string?)row["formatHash"]);
        Assert.Equal((string?)entry["typeHash"], (string?)row["typeHash"]);
        Assert.Equal((string?)entry["label"], (string?)row["label"]);

        var binding = run.Plan.Document["bindings"]!["plaintext"]!["generated"]!;
        var agent = row["agent"]!.AsObject();
        Assert.Equal((string?)binding["agentName"], (string?)agent["name"]);
        Assert.Equal((int)binding["agentVersion"]!, (int)agent["version"]!);
        Assert.Equal("plaintext", (string?)agent["binding"]!["format"]);
        Assert.Equal("generated", (string?)agent["binding"]!["producer"]);
        Assert.Equal((string?)binding["via"], (string?)agent["binding"]!["via"]);
    }

    [Fact]
    public async Task ARenderedRow_AndAComputedRow_AreComputed()
    {
        var rendered = await ComposedAsync(RunGoldenTests.Macros());
        var computed = await RunGoldenTests.Run(RunFixture.Plan("nested-note"), new RunLimits(), seed: null);

        foreach (var (row, entry) in new[]
        {
            (rendered.Recorder.Row("node.stamp"), rendered.Plan.Entries["node.stamp"]),
            (computed.Recorder.Row("node.section_count"), RunFixture.Plan("nested-note").Entries["node.section_count"]),
        })
        {
            var json = row.ToJson();
            Assert.Equal(NodeMembers.Order(StringComparer.Ordinal), json.Select(m => m.Key).Order(StringComparer.Ordinal));
            Assert.Equal("computed", (string?)json["producer"]);
            Assert.Null(json["agent"]);
            Assert.Null(json["sentFormatHash"]);
            Assert.Empty(json["notSent"]!.AsArray());
            Assert.Empty(json["macros"]!.AsArray());
            Assert.Null(json["tokens"]);
            foreach (var flag in new[] { "runDependent", "modelDerived", "profileCarrying" })
                Assert.Equal((bool)entry.Json[flag]!, (bool)json[flag]!);
            Assert.Equal((string?)entry.Json["formatHash"], (string?)json["formatHash"]);
        }

        Assert.Equal("rendered", (string?)rendered.Recorder.Row("node.stamp").ToJson()["kind"]);
        Assert.Equal("computed", (string?)computed.Recorder.Row("node.section_count").ToJson()["kind"]);
        Assert.True((bool)rendered.Recorder.Row("node.stamp").ToJson()["runDependent"]!);
    }

    [Fact]
    public async Task AFlowRow_HasNoFormat()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[] { "conditional", "nested-note", "functions" })
        {
            var run = await RunGoldenTests.Run(RunFixture.Plan(name), new RunLimits(), seed: null);
            foreach (var row in run.Recorder.Rows.Where(r => r.Kind is PlanKind.For or PlanKind.While or PlanKind.If or PlanKind.Case or PlanKind.Call))
            {
                var json = row.ToJson();
                Assert.Null(json["formatHash"]);
                Assert.Null(json["typeHash"]);
                Assert.Equal("computed", (string?)json["producer"]);
                seen.Add((string)json["kind"]!);
            }
        }

        Assert.Equal(["call", "case", "for", "if", "while"], seen.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ASkippedRow_SaysWhy()
    {
        var run = await RunGoldenTests.Run(RunFixture.Plan("conditional"), new RunLimits(), seed: null);

        var row = run.Recorder.Row("node.patient_summaries.narrative_only:2").ToJson();

        Assert.Equal("Skipped", (string?)row["status"]);
        Assert.Equal("skipped: item.value.kind = \"narrative\" is false", (string?)row["stateReason"]);
        Assert.Null(row["outputHash"]);
        Assert.Equal("node.patient_summaries.narrative_only", (string?)row["nodeId"]);
        Assert.Equal("2", (string?)row["itemId"]);
    }

    [Fact]
    public async Task AnItemNameFromAnInput_IsNotOnTheRow()
    {
        // conditional's sections_draft loops over input.sections, named by their titles: an input's text
        var conditional = await RunGoldenTests.Run(RunFixture.Plan("conditional"), new RunLimits(), seed: null);
        foreach (var key in new[] { "node.sections_draft:1", "node.sections_draft.route:1" })
        {
            var row = conditional.Recorder.Row(key);
            var json = row.ToJson();
            Assert.Null(json["itemName"]);
            Assert.Equal(CanonicalJson.HashText("History"), (string?)json["itemNameHash"]);
            Assert.False((bool)json["positionName"]!);
            Assert.Equal("History", (string?)row.Text?["itemName"]);
            Assert.DoesNotContain("History", json.ToJsonString(), StringComparison.Ordinal);
        }

        // functions' standards loops over a snippet folder, named by its items' headers: package text, kept on the row
        var functions = await RunGoldenTests.Run(RunFixture.Plan("functions"), new RunLimits(), seed: null);
        var folder = functions.Recorder.Row("node.standards:hpi");
        Assert.Equal("History of Present Illness", (string?)folder.ToJson()["itemName"]);
        Assert.Null(folder.ToJson()["itemNameHash"]);
        Assert.False(folder.Text?.ContainsKey("itemName") ?? false);

        // outside every loop, none of the three
        var top = conditional.Recorder.Row("node.plan").ToJson();
        Assert.Null(top["itemName"]);
        Assert.Null(top["itemNameHash"]);
        Assert.Null(conditional.Recorder.Row("node.plan").Text?["itemName"]);
    }

    // ---- run spots, in runes, with their pass ---------------------------------------------------

    [Fact]
    public async Task ARunSpot_IsPlacedInRunes()
    {
        var run = await ComposedAsync(Package()
            .WithComputed("when", Plain, "{ \"cat\": [{ \"var\": \"run.date\" }, \" / \", { \"plain\": [{ \"var\": \"input.consult_draft.content\" }] }] }", runDependent: true)
            .WithAgent("dated", "🙂 {{ run.date }}\n{{ node.when.value }}\n").RunDependent("nodes/dated.md"));

        var row = run.Recorder.Row("node.dated");
        var spots = row.ToJson()["runSpots"]!.AsArray();
        Assert.Equal(2, spots.Count);
        var date = spots[0]!.AsObject();
        Assert.Equal("node.dated/spot:1", (string?)date["id"]);
        Assert.Equal("run.date", (string?)date["reads"]);
        Assert.Equal(2, (int)date["start"]!); // the emoji is one rune, two UTF-16 units
        Assert.Equal("2026-11-21".Length, (int)date["length"]!);
        Assert.Equal("2026-11-21", (string?)date["output"]);
        Assert.Null(date["pass"]);

        var sent = run.Host.Prompt("node.dated").Text!;
        foreach (var spot in spots.Select(s => s!.AsObject()))
        {
            var slice = RuneSlice(sent, (int)spot["start"]!, (int)spot["length"]!);
            Assert.Equal((string?)spot["outputHash"], CanonicalJson.HashText(slice));
        }

        Assert.Equal("2026-11-21", RuneSlice(sent, 2, 10));

        // a spot reading a run-dependent value drawn from an input keeps its output in the settled text only
        var when = spots[1]!.AsObject();
        Assert.Equal("node.dated/spot:2", (string?)when["id"]);
        Assert.Null(when["output"]);
        var expected = "2026-11-21 / " + PlanRunnerLeavesTests.Draft;
        Assert.Equal(CanonicalJson.HashText(expected), (string?)when["outputHash"]);
        Assert.Equal(expected, (string?)row.Text!["spots"]!["node.dated/spot:2"]);
        Assert.False(row.Text!["spots"]!.AsObject().ContainsKey("node.dated/spot:1")); // a raw run fact is on the row
        Assert.DoesNotContain("Margaret", row.ToJson().ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASpotInATemplateFor_HasItsPass()
    {
        var run = await ComposedAsync(Package()
            .WithAgent("dated", "{{ for s in node.plan.value.sections }}{{ s.title | renderer.quote }} on {{ run.date }}\n{{ end }}")
            .WithRenderer("quote", Plain, "> {{ value }}", "plaintext")
            .RunDependent("nodes/dated.md"));

        var row = run.Recorder.Row("node.dated").ToJson();

        var spots = row["runSpots"]!.AsArray().Select(s => s!.AsObject()).ToList();
        Assert.Equal(["node.dated/spot:1", "node.dated/spot:2"], spots.Select(s => (string)s["id"]!));
        Assert.Equal([1, 1], spots.Select(s => (int)s["site"]!));
        Assert.Equal(["[1]", "[2]"], spots.Select(s => s["pass"]!.ToJsonString()));

        var renders = row["renders"]!.AsArray().Select(r => r!.AsObject()).ToList();
        Assert.Equal(["node.dated/render:1", "node.dated/render:1"], renders.Select(r => (string)r["id"]!));
        Assert.Equal(["[1]", "[2]"], renders.Select(r => r["pass"]!.ToJsonString()));

        // the renderer's texts are produced text: on the outcome's Produced, by sub-row id and pass, never in the detail
        var produced = run.Host.Outcomes.Last(o => o.Detail?["inputHash"] is not null && SourceLayout.Text(o.Produced?["sent"])?.Contains("on 2026", StringComparison.Ordinal) == true).Produced!;
        Assert.Equal("> History", (string?)produced["renders"]!["node.dated/render:1#1"]!["output"]);
        Assert.Equal("Plan", (string?)produced["renders"]!["node.dated/render:1#2"]!["input"]);
        Assert.DoesNotContain("> History", row.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMacroBodysRunFact_IsOnItsMacroRow()
    {
        // R7: a macro body's run.* is filled directly: no spot in the placing body; the macro row carries its own
        var run = await ComposedAsync(Package()
            .WithMacroOf("seen", "Seen on {{ run.date }}.\n", ["Close with {{ macro.seen.content }}"])
            .WithAgent("draft", "Draft the letter to Margaret Okafor's GP.\n{{ macro.seen }}\n").RunDependent("nodes/draft.md"));

        var row = run.Recorder.Row("node.draft").ToJson();

        Assert.Empty(row["runSpots"]!.AsArray());
        Assert.Null(row["runNeutralHash"]);
        var macro = Assert.Single(row["macros"]!.AsArray())!.AsObject();
        Assert.Equal(CanonicalJson.HashText("Seen on 2026-11-21."), (string?)macro["contentHash"]);
        Assert.Equal(CanonicalJson.HashText("Seen on {{run:1}}."), (string?)macro["runNeutralHash"]);
        var spot = Assert.Single(macro["runSpots"]!.AsArray())!.AsObject();
        Assert.Equal("node.draft/macro:seen/spot:1", (string?)spot["id"]);
        Assert.Equal(1, (int)spot["site"]!);
        Assert.Equal("run.date", (string?)spot["reads"]);
        Assert.Equal("2026-11-21", (string?)spot["output"]);
        Assert.Equal("Seen on ".Length, (int)spot["start"]!);
        Assert.Equal(10, (int)spot["length"]!);
        Assert.Contains("Seen on 2026-11-21.", SourceLayout.Text(run.Result.Values["node.draft"]?["content"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASubstitutedSnippet_IsMarkedNotSent()
    {
        // R9: a snippet placed in a macro's body is substituted into the reply, never sent to the model
        var run = await ComposedAsync(Package()
            .WithSnippet("sign", "Dr A. Sample")
            .WithSnippet("tone", "Keep a calm tone.")
            .WithMacroOf("signoff", "With thanks,\n{{ snippet.sign.content }}\n", ["End the letter with {{ macro.signoff.content }}"])
            .WithAgent("draft", "Draft the letter to Margaret Okafor's GP.\n{{ snippet.tone.content }}\n{{ macro.signoff }}\n"));

        var units = run.Recorder.Row("node.draft").ToJson()["units"]!.AsArray().Select(u => u!.AsObject()).ToList();

        Assert.Equal(["snippet.tone", "snippet.sign"], units.Select(u => (string)u["site"]!));
        Assert.Equal([true, false], units.Select(u => (bool)u["sent"]!));
        Assert.Equal(CanonicalJson.HashText("Dr A. Sample"), (string?)units[1]["hash"]);
    }

    [Fact]
    public async Task TheRowKeepsItsUnits()
    {
        var run = await ComposedAsync(Package()
            .WithSnippet("tone", "Keep a calm tone.")
            .WithAgent("draft", "Draft the letter to Margaret Okafor's GP.\n{{ snippet.tone.content }}\n"));

        var units = run.Recorder.Row("node.draft").ToJson()["units"]!;
        var composed = run.Host.Prompt("node.draft").Composition.Units;

        Assert.Equal(
            new JsonArray([.. composed.Select(u => (JsonNode)new JsonObject { ["site"] = u.Site, ["hash"] = u.TextHash, ["sent"] = true })]).ToJsonString(),
            units.ToJsonString());
        Assert.Equal(CanonicalJson.HashText("Keep a calm tone."), (string?)units[0]!["hash"]);
    }

    // ---- tokens, calls, faults -------------------------------------------------------------------

    [Fact]
    public async Task ARetriedCall_SumsItsTokens()
    {
        // R14: an invalid reply asked again once (onInvalid: retry); each attempt's tokens, summed on the row
        var plan = RunFixture.Plan(Package());
        var host = new FakeRunHost(auto: true, plan).Agent("node.plan",
            new AgentOutcome(AgentOutcomeKind.Completed, "not a plan", 0, null, Tokens(10, 5)),
            new AgentOutcome(AgentOutcomeKind.Completed, PlacementDecisionsTests.Sections, 0, null, Tokens(20, 7)));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.True(result.State == RunState.Completed, $"{result.State}: {result.FailedKey} {result.Sentence}");
        var row = recorder.Row("node.plan").ToJson();
        Assert.Equal("""{"input":30,"output":12,"total":42}""", CanonicalJson.Write(row["tokens"]));
        Assert.Equal(2, (int)row["invalid"]!["attempts"]!);
        Assert.Null(recorder.Row("deliverable.note").ToJson()["tokens"]);
    }

    [Fact]
    public async Task AFailedCall_StillHasEveryMember()
    {
        var plan = RunFixture.Plan(Package());
        var host = new FakeRunHost(auto: true, plan).Agent("node.plan", new AgentOutcome(AgentOutcomeKind.Failed, null, 0, "compose"));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        var row = recorder.Row("node.plan").ToJson();
        Assert.Equal(NodeMembers.Order(StringComparer.Ordinal), row.Select(m => m.Key).Order(StringComparer.Ordinal));
        Assert.Equal("Failed", (string?)row["status"]);
        Assert.NotNull((string?)row["stateReason"]);
        Assert.Null(row["inputHash"]);
        Assert.Null(row["tokens"]);
        Assert.Empty(row["runSpots"]!.AsArray());
        Assert.Empty(row["retries"]!.AsArray());
        Assert.Equal((string?)plan.Document["bindings"]!["section_plan"]!["generated"]!["agentName"], (string?)row["agent"]!["name"]);
    }

    [Fact]
    public async Task ACallRow_HasItsFunctionArgumentsAndDepth()
    {
        var functions = await RunGoldenTests.Run(RunFixture.Plan("functions"), new RunLimits(), seed: null);

        var call = functions.Recorder.Row("node.standards.section:hpi").ToJson();

        Assert.Equal("call", (string?)call["kind"]);
        Assert.Equal("draft_section", (string?)call["function"]);
        Assert.Equal(0, (int)call["depth"]!);
        var arguments = call["arguments"]!.AsArray().Select(a => a!.AsObject()).ToList();
        Assert.Equal(["standard", "draft"], arguments.Select(a => (string)a["param"]!));
        Assert.All(arguments, a => Assert.StartsWith("sha256:", (string)a["hash"]!, StringComparison.Ordinal));
        Assert.Null(call["formatHash"]);

        // its inner rows are keyed through the call node
        var inner = functions.Recorder.Row("node.standards.section.standard_draft:hpi").ToJson();
        Assert.Equal("node.standards.section.standard_draft", (string?)inner["nodeId"]);
        Assert.Equal("hpi", (string?)inner["itemId"]);
        Assert.Equal("draft_section", (string?)inner["function"]);
        Assert.Equal(1, (int)inner["depth"]!);

        // a call inside a function is at depth 1
        var plan = RunFixture.Plan(PlanRunnerCallsTests.Nested(), PlanRunnerCallsTests.NestedReads());
        var recorder = new ListRecorder();
        await new PlanRunner(plan, new RunLimits(), new FakeRunHost(auto: true), recorder).RunAsync(PlanRunnerLeavesTests.Inputs(), FakeRunHost.Start);
        var nested = recorder.Row("node.hpi_section.refine_call").ToJson();
        Assert.Equal("call", (string?)nested["kind"]);
        Assert.Equal("refine", (string?)nested["function"]);
        Assert.Equal(1, (int)nested["depth"]!);
        Assert.Equal(0, (int)recorder.Row("node.hpi_section").ToJson()["depth"]!);
    }

    [Fact]
    public async Task AnUnknownMarkersId_NeverReachesTheRow()
    {
        // The controller's ruling 2: a marker that names no macro is "macro: unknown", never the id the model wrote
        const string invented = "zz_invented_by_the_model";
        var plan = RunFixture.Plan(RunGoldenTests.Macros());
        var host = new ComposingRunHost(plan, (call, prompt) =>
            call.EntryKey == "node.plan" ? PlacementDecisionsTests.Sections
            : call.EntryKey == "node.draft" ? ComposingRunHost.EchoMarkers(call, prompt) + $" {{{{ macro.{invented}.content }}}}"
            : prompt.TextReply ? ComposingRunHost.EchoMarkers(call, prompt)
            : ComposingRunHost.Judgement(true));
        var recorder = new ListRecorder();

        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(RunFixture.GoldenInputs() with { RunFacts = RunDate }, FakeRunHost.Start);

        Assert.Equal(RunState.Failed, result.State);
        var row = recorder.Row("node.draft");
        Assert.Equal("macro: unknown", (string?)row.ToJson()["invalid"]!["reason"]);
        foreach (var each in recorder.Rows)
        {
            Assert.DoesNotContain(invented, each.ToJson().ToJsonString(), StringComparison.Ordinal);
            Assert.DoesNotContain(invented, each.Reason ?? "", StringComparison.Ordinal);
        }

        Assert.DoesNotContain(invented, result.Sentence ?? "", StringComparison.Ordinal);
    }

    // ---- no text ---------------------------------------------------------------------------------

    [Fact]
    public async Task NoDetail_CarriesText()
    {
        // the inputs' section titles as whole strings ("History of Present Illness" is a snippet folder's header: package text)
        string[] needles = ["Stub reply for", "\"stub ", "Margaret Okafor, 58", PlanRunnerLeavesTests.Draft, "\"History\"", "\"Results\"", "\"Coding\""];
        var rows = new List<RunRow>();
        var details = new List<JsonObject>();
        foreach (var name in CompiledGoldenTests.Cases().Select(c => c.Name))
            rows.AddRange((await RunGoldenTests.Run(RunFixture.Plan(name), new RunLimits(), seed: null)).Recorder.Rows);

        var macros = await ComposedAsync(RunGoldenTests.Macros(), RunFixture.GoldenInputs());
        rows.AddRange(macros.Recorder.Rows);
        details.AddRange(macros.Host.Outcomes.Select(o => o.Detail).OfType<JsonObject>());
        details.AddRange(macros.Host.RenderOutcomes.Select(o => o.Detail).OfType<JsonObject>());
        Assert.NotEmpty(details);

        foreach (var row in rows)
        {
            var json = CanonicalJson.Write(row.ToJson());
            foreach (var needle in needles)
                Assert.False(json.Contains(needle, StringComparison.Ordinal), $"{row.Key} holds '{needle}': {json}");
        }

        foreach (var detail in details)
        {
            var json = CanonicalJson.Write(detail);
            foreach (var needle in needles)
                Assert.False(json.Contains(needle, StringComparison.Ordinal), $"a detail holds '{needle}': {json}");
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    /// <summary>A composed run: its plan, host, recorder and result.</summary>
    internal sealed record Composed(RunPlan Plan, ComposingRunHost Host, ListRecorder Recorder, RunResult Result);

    /// <summary>
    /// <paramref name="files"/> run on <see cref="ComposingRunHost"/> with <c>run.date</c> 2026-11-21: the plan replies
    /// <see cref="PlacementDecisionsTests.Sections"/>, a text agent echoes its markers, a <c>judgement</c> says yes.
    /// </summary>
    internal static async Task<Composed> ComposedAsync(Dictionary<string, byte[]> files, RunInputs? inputs = null)
    {
        var plan = RunFixture.Plan(files);
        var host = new ComposingRunHost(plan, (call, prompt) =>
            call.EntryKey == "node.plan" ? PlacementDecisionsTests.Sections
            : prompt.TextReply ? ComposingRunHost.EchoMarkers(call, prompt)
            : ComposingRunHost.Judgement(true));
        var recorder = new ListRecorder();
        var given = (inputs ?? RunFixture.GoldenInputs()) with { RunFacts = RunDate };
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(given, FakeRunHost.Start);
        Assert.True(result.State == RunState.Completed, $"{result.State}: {result.FailedKey} {result.Sentence}");
        return new Composed(plan, host, recorder, result);
    }

    /// <summary>An attempt's detail holding its tokens.</summary>
    private static JsonObject Tokens(int input, int output) =>
        new() { ["tokens"] = new JsonObject { ["input"] = input, ["output"] = output, ["total"] = input + output } };

    /// <summary>The <paramref name="length"/> runes of <paramref name="text"/> from rune <paramref name="start"/>.</summary>
    private static string RuneSlice(string text, int start, int length) =>
        string.Concat(text.EnumerateRunes().Skip(start).Take(length).Select(r => r.ToString()));

    /// <summary>
    /// #1506 (D2): an item's name as a test reads it: on the row when it is package text, else on its settled text
    /// (<see cref="RunRow.Text"/>).
    /// </summary>
    internal static string? ItemName(RunRow row) => (string?)row.Detail["itemName"] ?? (string?)row.Text?["itemName"];
}
