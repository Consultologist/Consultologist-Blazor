using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.Api.Tests.V31Source;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 4; part 08 §§ 4, 8.2, 10; D4): run spots through whole runs on
/// <see cref="ComposingRunHost"/>. A run-dependent <c>rendered</c> node settles with its run-neutral text beside its
/// content; a reader whose run spot names it with <c>neutral: "text"</c> is given that text (and only such a reader), so
/// its own run-neutral render holds the node's markers; every row carries part 08's <c>runSpots</c> and
/// <c>runNeutralHash</c>, a spot's place indexing the sent text; and two runs on two dates share every run-neutral hash
/// while their sent texts differ. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class RunNeutralTests
{
    private const string Seen = "Seen {{ run.date | date.to_string \"%d %B %Y\" }}\n";

    /// <summary>The run-dependent rendered node <c>stamp</c> (a run spot), read by the run-dependent agent <c>draft</c> and by the deliverable.</summary>
    private static RunPlan Plan() =>
        RunFixture.Plan(Package()
            .WithRendered("stamp", Seen).RunDependent("nodes/stamp.md")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ node.stamp.content }}\n").RunDependent("nodes/draft.md")
            .WithDeliverable("{{ node.plan.content }}\n{{ node.stamp.content }}\n"));

    private static RunInputs On(string date) => PlanRunnerLeavesTests.Inputs() with { RunFacts = new JsonObject { ["date"] = date } };

    private static async Task<(RunResult Result, ComposingRunHost Host, ListRecorder Recorder)> RunAsync(RunPlan plan, string date)
    {
        var host = new ComposingRunHost(plan, (call, prompt) =>
            call.EntryKey == "node.plan" ? PlacementDecisionsTests.Sections : ComposingRunHost.EchoMarkers(call, prompt));
        var recorder = new ListRecorder();
        var result = await new PlanRunner(plan, new RunLimits(), host, recorder).RunAsync(On(date), FakeRunHost.Start);
        Assert.True(result.State == RunState.Completed, result.Sentence);
        return (result, host, recorder);
    }

    [Fact]
    public async Task ARenderedNodeWithARunSpot_SettlesWithItsNeutralText_AndItsReaderCarriesIt()
    {
        var plan = Plan();
        Assert.Equal(["node.stamp.neutral"], plan.Entries["node.draft"].NeutralReads);
        Assert.Empty(plan.Entries["node.plan"].NeutralReads);

        var (result, host, recorder) = await RunAsync(plan, "2026-11-21");

        // The rendered node: its value is the sent text, its read form carries the run-neutral text, its row both hashes.
        var stamp = result.Values["node.stamp"]!;
        Assert.Equal("Seen 21 November 2026\n", (string?)stamp["content"]);
        Assert.Equal("Seen {{run:1}}\n", (string?)stamp["neutral"]);
        var stampRow = recorder.Row("node.stamp").Detail;
        Assert.Equal(CanonicalJson.HashText("Seen 21 November 2026\n"), (string?)stampRow["inputHash"]);
        Assert.Equal(CanonicalJson.HashText("Seen {{run:1}}\n"), (string?)stampRow["runNeutralHash"]);
        Assert.Equal(
            CanonicalJson.Write(JsonNode.Parse($$"""
                [{ "id": "node.stamp/spot:1", "site": 1, "pass": null, "reads": "run.date", "valueHash": "{{CanonicalJson.Hash(JsonValue.Create("2026-11-21"))}}",
                   "output": "21 November 2026", "outputHash": "{{CanonicalJson.HashText("21 November 2026")}}", "start": 5, "length": 16 }]
                """)),
            CanonicalJson.Write(stampRow["runSpots"]));
        var render = host.Renders.Single(r => r.Key == "node.stamp");
        Assert.False(render.Scope.ToJsonString().Contains("neutral", StringComparison.Ordinal));

        // Its reader is given the run-neutral text beside the content, and its own neutral render holds the node's marker.
        var call = host.Calls.Single(c => c.Key == "node.draft");
        Assert.Equal("Seen {{run:1}}\n", (string?)call.Scope["node"]!["stamp"]!["neutral"]);
        var prompt = host.Prompt("node.draft");
        Assert.Equal("Draft the plan for Margaret Okafor.\nSeen 21 November 2026\n\n", prompt.Text);
        Assert.Equal("Draft the plan for Margaret Okafor.\nSeen {{run:1}}\n\n", prompt.Composition.NeutralText);
        var spot = Assert.Single(prompt.Composition.Spots);
        Assert.Equal("Seen 21 November 2026\n", prompt.Text!.Substring(spot.Start, spot.Length)); // UTF-16 units of the sent text
        var draftRow = recorder.Row("node.draft").Detail;
        Assert.Equal(CanonicalJson.HashText(prompt.Text), (string?)draftRow["inputHash"]);
        Assert.Equal(CanonicalJson.HashText(prompt.Composition.NeutralText!), (string?)draftRow["runNeutralHash"]);
        var draftSpot = Assert.Single(draftRow["runSpots"]!.AsArray())!;
        Assert.Equal(("node.draft/spot:1", "node.stamp.content"), ((string)draftSpot["id"]!, (string)draftSpot["reads"]!));
        Assert.Null(draftSpot["output"]); // a rendered node's output follows 7-day retention

        // The deliverable reads it too, and is released with its own neutral text and spots.
        var note = result.Values["deliverable.note"]!;
        Assert.Equal("released", (string?)note["state"]);
        Assert.EndsWith("\nSeen {{run:1}}\n\n", (string?)note["neutral"], StringComparison.Ordinal);
        var noteRow = recorder.Row("deliverable.note").Detail;
        Assert.Equal(CanonicalJson.HashText((string)note["neutral"]!), (string?)noteRow["runNeutralHash"]);
        Assert.Single(noteRow["runSpots"]!.AsArray());

        // A row with no spot has one hash.
        Assert.Null(recorder.Row("node.plan").Detail["runNeutralHash"]);
        Assert.Empty(recorder.Row("node.plan").Detail["runSpots"]!.AsArray());
        Assert.Null(result.Values["node.plan"]!["neutral"]);
    }

    [Fact]
    public async Task TwoRunsOnTwoDates_ShareEveryRunNeutralHash_AndDifferInInputHash()
    {
        var plan = Plan();

        var (_, _, first) = await RunAsync(plan, "2026-11-21");
        var (_, _, second) = await RunAsync(plan, "2026-12-02");

        var spotted = first.Rows.Where(r => r.Detail["runNeutralHash"] is JsonValue).Select(r => r.Key).ToList();
        Assert.Equal(["node.stamp", "node.draft", "deliverable.note"], spotted);
        foreach (var key in spotted)
        {
            var (a, b) = (first.Row(key).Detail, second.Row(key).Detail);
            Assert.Equal((string?)a["runNeutralHash"], (string?)b["runNeutralHash"]);
            Assert.NotEqual((string?)a["inputHash"], (string?)b["inputHash"]);
        }

        // What no run fact reaches is the same in both.
        Assert.Equal((string?)first.Row("node.plan").Detail["inputHash"], (string?)second.Row("node.plan").Detail["inputHash"]);
    }
    [Fact]
    public async Task ANeutralText_IsCarriedThroughAWhile()
    {
        // Final review M2 (D4): a while whose ret is a run-dependent rendered node settles with that node's neutral text,
        // and a reader of the while is given it, so its own neutral render holds the marker.
        var plan = RunFixture.Plan(Package()
            .Listed("nodes/index.json", "stamps")
            .With("nodes/stamps/index.json", Json(SourceFixture.Node("while", "Stamping", Plain, true, false,
                "\"while\": { \"<\": [{ \"var\": \"pass.number\" }, 2] }, \"max\": 3, \"until\": null, \"checkFirst\": false, "
                + "\"nodes\": [\"stamp\"], \"ret\": \"node.stamps.stamp\"")))
            .RunDependent("nodes/stamps/index.json")
            .With("nodes/stamps/stamp.md", Md(SourceFixture.Node("rendered", "Rendered stamp", Plain, true, false, "\"if\": null"), Seen))
            .RunDependent("nodes/stamps/stamp.md")
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ node.stamps.content }}\n").RunDependent("nodes/draft.md"));
        Assert.Equal(["node.stamps.neutral"], plan.Entries["node.draft"].NeutralReads);

        var (result, host, _) = await RunAsync(plan, "2026-11-21");

        Assert.Equal("Seen {{run:1}}\n", (string?)result.Values["node.stamps"]!["neutral"]);
        Assert.Equal("Seen {{run:1}}\n", (string?)host.Calls.Single(c => c.Key == "node.draft").Scope["node"]!["stamps"]!["neutral"]);
        Assert.Equal("Draft the plan for Margaret Okafor.\nSeen {{run:1}}\n\n", host.Prompt("node.draft").Composition.NeutralText);
    }

    [Fact]
    public async Task ANeutralText_IsCarriedThroughACall_AndIsNoPartOfTheArgumentsHash()
    {
        // Final review M2 (D4): a run-dependent rendered node passed to a function reads there as param.<id>.neutral, and
        // the call's argument hash is of its value and content alone.
        var plan = RunFixture.Plan(Package()
            .WithRendered("stamp", Seen).RunDependent("nodes/stamp.md")
            .Listed("nodes/index.json", "first")
            .With("nodes/first.md", Md(SourceFixture.Node("call", "First section", Plain, false, true,
                "\"if\": null, \"function\": \"section\", \"with\": { \"stamp\": \"node.stamp\" }"), ""))
            .RunDependent("nodes/first.md")
            .With("functions/index.json", Index(["section"]))
            .With("functions/section/index.json", Json(
                "{ \"label\": \"Draft a section\", \"description\": null, "
                + "\"inputs\": { \"stamp\": { \"label\": \"Stamp\", \"description\": null, \"format\": \"plaintext\" } }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + "\"nodes\": [\"draft\"], \"ret\": \"node.draft\", \"format\": \"plaintext\" }"))
            .With("functions/section/draft.md", Agent("Drafting the section", Plain,
                "Draft the section for Margaret Okafor.\n{{ param.stamp.content }}\n"))
            .RunDependent("functions/section/draft.md")
            .With("functions/section/snippets/index.json", Index([]))
            .With("functions/section/macros/index.json", Index([]))
            .With("functions/section/formats/index.json", Index([]))
            .With("functions/section/renderers/index.json", Index([])));
        Assert.Equal(["param.stamp.neutral"], plan.Functions["section"].Entries["node.draft"].NeutralReads);

        var (result, host, recorder) = await RunAsync(plan, "2026-11-21");

        var call = host.Calls.Single(c => c.Key == "node.first.draft");
        Assert.Equal("Seen {{run:1}}\n", (string?)call.Scope["param"]!["stamp"]!["neutral"]);
        Assert.Equal("Draft the section for Margaret Okafor.\nSeen {{run:1}}\n\n", host.Prompt("node.first.draft").Composition.NeutralText);
        var stamp = result.Values["node.stamp"]!;
        var hashed = new JsonObject { ["value"] = stamp["value"]?.DeepClone(), ["content"] = stamp["content"]?.DeepClone() };
        Assert.Equal(
            CanonicalJson.Write(new JsonArray(new JsonObject { ["param"] = "stamp", ["hash"] = CanonicalJson.Hash(hashed) })),
            CanonicalJson.Write(recorder.Row("node.first").Detail["arguments"]));
    }
}
