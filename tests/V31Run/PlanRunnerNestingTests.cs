using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.2; part 04 §§ 2.3, 8; Review Focus 3): the per-job ceiling and the fixed
/// dispatch order under nesting. Three nested parallel loops of 10 with <c>maxParallel</c> 3, 2 and <c>null</c>, a call
/// in the innermost whose function holds one agent: at a ceiling of 4, with the replies released in a shuffled order,
/// never more than 4 calls are in flight, never more items of a loop instance than its cap hold a call, every dispatch is
/// the first waiting call in the fixed order (<see cref="DispatchOracle"/>), and a second run released in the same order
/// dispatches the same calls in the same order. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class PlanRunnerNestingTests
{
    private const int Ten = 10;

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(1503)]
    public async Task TheCeiling_HoldsUnderNesting_AndDispatchIsDeterministic(int seed)
    {
        var plan = RunFixture.Plan(Nested(), NestedReads());

        // 1,000 agent calls: the job's limit of 500 is raised for the test
        var limits = new RunLimits(MaxAgentCalls: 4, JobAgentCalls: 1000);
        var (first, initial) = await Run(plan, limits, seed);
        var (second, _) = await Run(plan, limits, seed);
        Assert.Equal(4, initial);

        Assert.True(first.Result.State == RunState.Completed, $"{first.Result.State}: {first.Result.FailedKey} {first.Result.Sentence}");
        Assert.Equal(Ten * Ten * Ten, first.Result.AgentCalls);
        Assert.All(first.Host.Calls, c => Assert.Equal("note_fn", c.Function));

        // the ceiling: never more than 4 in flight
        Assert.Equal(4, first.Host.InFlightPeak);
        Assert.Equal(4, first.Result.PeakInFlight);

        // each loop instance within its cap: at most 3 outer items with a call in flight, and within each outer item at
        // most 2 middle items; the inner loop has no cap of its own
        Assert.InRange(first.Host.PeakGroupsOf(k => Items(k)[0]), 1, 3);
        for (var outer = 0; outer < Ten; outer++)
        {
            var mine = Id(outer);
            Assert.InRange(first.Host.PeakGroupsOf(k => Items(k)[0] == mine ? Items(k)[1] : null), 1, 2);
        }

        // the fixed order: each dispatch the first call waiting then, whatever order the answers came back in
        DispatchOracle.Holds(plan, first.Host.Calls, first.Recorder, first.Result);

        // the same release order, the same dispatches. This proves only that the run is a function of the order its answers
        // come in, within one process: an iteration over an unordered collection that is stable within a process would pass
        // it. What catches a dispatch out of the fixed order is DispatchOracle above: making the runner take the waiting
        // calls in reverse order fails it here; taking them in ordinal key order (stable, not the fixed order) fails it in
        // RunGoldenTests, though not here, where item ids 0 to 9 sort the same both ways.
        Assert.Equal(first.Host.Dispatched, second.Host.Dispatched);
        Assert.Equal(RunTrace.Of(first.Host, first.Recorder, first.Result), RunTrace.Of(second.Host, second.Recorder, second.Result));
        Assert.Equal(Ten * Ten * Ten, first.Host.Dispatched.Distinct(StringComparer.Ordinal).Count());
        Assert.All(first.Recorder.Rows, r => Assert.True(r.State is RowState.Completed or RowState.Released, $"{r.Key} is {r.State}"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(1503)]
    public async Task TheCaps_Bind_WhenTheCeilingDoesNot(int seed)
    {
        // at a ceiling of 64 the caps are what limit: 3 outer items open, 2 middle items in each, 10 inner calls in each,
        // so 60 in flight at the most, and each cap reached exactly
        var plan = RunFixture.Plan(Nested(), NestedReads());
        var (run, initial) = await Run(plan, new RunLimits(MaxAgentCalls: 64, JobAgentCalls: 1000), seed);

        Assert.True(run.Result.State == RunState.Completed, $"{run.Result.State}: {run.Result.FailedKey} {run.Result.Sentence}");
        Assert.Equal(Ten * Ten * Ten, run.Result.AgentCalls);
        Assert.Equal(3 * 2 * Ten, run.Host.InFlightPeak);
        Assert.Equal(3 * 2 * Ten, run.Result.PeakInFlight);
        Assert.Equal(3, run.Host.PeakGroupsOf(k => Items(k)[0]));
        for (var outer = 0; outer < Ten; outer++)
        {
            var mine = Id(outer);
            Assert.Equal(2, run.Host.PeakGroupsOf(k => Items(k)[0] == mine ? Items(k)[1] : null));
            for (var middle = 0; middle < Ten; middle++)
            {
                var prefix = $"node.outer.middle.inner.note.write:{mine}/{Id(middle)}/";
                Assert.Equal(Ten, run.Host.PeakOf(k => k.StartsWith(prefix, StringComparison.Ordinal)));
            }
        }

        // before any answer, exactly what the caps open
        Assert.Equal(3 * 2 * Ten, initial);
        DispatchOracle.Holds(plan, run.Host.Calls, run.Recorder, run.Result);
    }

    private static string Id(int position) => position.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A call's item ids, outermost first: <c>node.outer.middle.inner.note.write:3/1/4</c> gives 3, 1 and 4.</summary>
    private static string[] Items(string key) => key[(key.IndexOf(':') + 1)..].Split('/');

    /// <summary>A run released in the order <paramref name="seed"/> chooses, and the agent calls made before any answer came.</summary>
    private static async Task<(RunGoldenTests.Traced Run, int Initial)> Run(RunPlan plan, RunLimits limits, int seed)
    {
        var host = new FakeRunHost();
        var recorder = new ListRecorder(() => host.Calls.Count);
        var runner = new PlanRunner(plan, limits, host, recorder);
        var initial = 0;
        var result = await FakeRunHost.Drive(() =>
        {
            var run = runner.RunAsync(Points(), FakeRunHost.Start);
            initial = host.Calls.Count;
            host.ReleaseInOrder(new Random(seed));
            return run;
        });
        return (new RunGoldenTests.Traced(host, recorder, result), initial);
    }

    /// <summary>The consult draft, and the list input <c>points</c> of ten.</summary>
    private static RunInputs Points()
    {
        var inputs = PlanRunnerLeavesTests.Inputs();
        var points = new JsonArray(Enumerable.Range(1, Ten).Select(i => (JsonNode?)$"Point {i}").ToArray());
        return inputs with
        {
            Inputs = new Dictionary<string, JsonNode?>(inputs.Inputs, StringComparer.Ordinal)
            {
                ["points"] = new JsonObject { ["value"] = points, ["content"] = null, ["title"] = null },
            },
        };
    }

    /// <summary>
    /// <see cref="Minimal"/> with the list input <c>points</c> and, in place of its agent, the loop <c>outer</c> over it
    /// (<c>maxParallel</c> 3) holding the loop <c>middle</c> over it (2) holding the loop <c>inner</c> over it (no cap),
    /// whose body is the call <c>note</c> of the function <c>note_fn</c> (its agent <c>write</c>, reading the point); the
    /// note reads nothing.
    /// </summary>
    private static Dictionary<string, byte[]> Nested()
    {
        var files = Minimal()
            .WithJson("manifest.json", m => m["inputs"]!.AsObject()["points"] = JsonNode.Parse(
                "{ \"label\": \"Points\", \"description\": null, "
                + "\"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }, \"required\": true, \"channel\": null }"))
            .With("nodes/index.json", Index(["outer"]))
            .Without("nodes/plan.md")
            .With("nodes/outer/index.json", Loop("node.outer", "Outer", 3, "middle", ArrayOf(ArrayOf(ArrayOf(PlanRunnerLeavesTests.Plain)))))
            .With("nodes/outer/middle/index.json", Loop("node.outer.middle", "Middle", 2, "inner", ArrayOf(ArrayOf(PlanRunnerLeavesTests.Plain))))
            .With("nodes/outer/middle/inner/index.json", Loop("node.outer.middle.inner", "Inner", null, "note", ArrayOf(PlanRunnerLeavesTests.Plain)))
            .With("nodes/outer/middle/inner/note.md", Md(
                Node("call", "Noting the point", PlanRunnerLeavesTests.Plain, false, true,
                    "\"if\": null, \"function\": \"note_fn\", \"with\": { \"point\": \"item.value\" }"),
                ""))
            .With("functions/index.json", Index(["note_fn"]))
            .With("functions/note_fn/index.json", Json(
                "{ \"label\": \"Note a point\", \"description\": null, "
                + "\"inputs\": { \"point\": { \"label\": \"Point\", \"description\": null, \"format\": \"plaintext\" } }, "
                + "\"uses\": { \"snippets\": [], \"macros\": [], \"formats\": [], \"renderers\": [] }, "
                + "\"nodes\": [\"write\"], \"ret\": \"node.write\", \"format\": \"plaintext\" }"))
            .With("functions/note_fn/write.md", Agent("Writing the point", PlanRunnerLeavesTests.Plain,
                "Write one line on this point for Margaret Okafor's note.\n\n{{ param.point.content }}\n"))
            .With("deliverables/note.md", Md(Deliverable("Consultation note", PlanRunnerLeavesTests.Plain, "null", "hold"), "Dr A. Sample\n"));
        foreach (var folder in new[] { "snippets", "macros", "formats", "renderers" })
            files.With($"functions/note_fn/{folder}/index.json", Index([]));
        return files;
    }

    /// <summary>The parallel loop <paramref name="key"/> over <c>input.points.value</c> with <paramref name="cap"/>, holding <paramref name="body"/> and returning it.</summary>
    private static string Loop(string key, string label, int? cap, string body, string format) =>
        Json(Node("for", label, format, false, true,
            $"\"in\": \"input.points.value\", \"parallel\": true, \"maxParallel\": {(cap is { } c ? Id(c) : "null")}, "
            + $"\"until\": null, \"max\": null, \"itemLabel\": null, \"nodes\": [\"{body}\"], \"ret\": \"{key}.{body}\""));

    private static StubTemplates NestedReads() => new(new Dictionary<string, string[]>
    {
        ["note_fn/node.write"] = ["param.point.content"],
        ["deliverable.note"] = [],
    }, everyBody: false);
}
