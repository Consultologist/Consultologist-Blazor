using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1342 B3a (package-format-v30-design.md § 3.1–§ 3.3): a 30 job with a top-level
/// <c>while</c>, end to end in the engine harness — passes keyed by number, the check
/// decided after each, the bound a named state, one block from the last pass.
/// </summary>
public class EngineRunWhileTests
{
    private static string Verdict(string verdict, string feedback) =>
        $$"""{ "verdict": "{{verdict}}", "feedback": "{{feedback}}" }""";

    /// <summary>The judge says redo until pass <paramref name="goodAt"/> (never, when null); every draft names its pass.</summary>
    internal static Func<string, string?> Judging(int? goodAt) => key => WorkflowItemPath.NodeOf(key) switch
    {
        "judge" => int.Parse(WorkflowItemPath.PathFor(key, "judge")!) == goodAt
            ? Verdict("good", "Fine.")
            : Verdict("redo", $"Shorter than pass {WorkflowItemPath.PathFor(key, "judge")}."),
        "draft" => $"Draft {WorkflowItemPath.PathFor(key, "draft")}.",
        _ => EngineRunNestedTests.Outputs(key),
    };

    private static WorkflowPackage Load(Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null) =>
        EngineRunNestedTests.Load(WorkflowV30WhileTests.Refining(change, files));

    [Fact]
    public async Task AWhile_StopsWhenItsCheckStopsHolding()
    {
        var run = await EngineRun.StartAsync(Load(), EngineRunNestedTests.Notes, Judging(goodAt: 2));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "draft:1", "draft:2", "judge:1", "judge:2" },
            run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("draft:", StringComparison.Ordinal) || id.StartsWith("judge:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal("Pass 2. Summarise: A fictional referral: a 40-year-old with wheeze and a fever. Feedback: Shorter than pass 1.",
            Assert.Single(run.Prompts, p => p.NodeId == "draft:2").Prompt);
        Assert.Equal("Judge this draft: Draft 2.", Assert.Single(run.Prompts, p => p.NodeId == "judge:2").Prompt);

        var row = Assert.Single(run.State.Nodes!, n => n.Id == "refine");
        Assert.Equal((2, "check"), (row.Passes, row.Stopped));
        Assert.Equal("Draft 2.", run.State.Blocks["consult:refine"].GeneratedText);
        Assert.All(new[] { "draft:1", "draft:2", "judge:1", "judge:2" }, key => Assert.NotNull(run.State.NodeOutputs![key].OutputHash));
        Matches("EngineRun30While.txt", run.Trace());
    }

    [Fact]
    public async Task AWhile_AtItsBound_StopsThere_AsANamedState()
    {
        var run = await EngineRun.StartAsync(Load(), EngineRunNestedTests.Notes, Judging(goodAt: null));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.DoesNotContain(run.Prompts, p => p.NodeId == "draft:4");
        var row = Assert.Single(run.State.Nodes!, n => n.Id == "refine");
        Assert.Equal((3, "bound", "stopped at its bound of 3; the check never stopped holding"), (row.Passes, row.Stopped, row.StopReason));
        Assert.Contains(run.State.History, e => e.Kind == "while-stopped" && e.Label == "Refining the summary: 3 passes, stopped at its bound of 3; the check never stopped holding");
        Assert.Equal("Draft 3.", run.State.Blocks["consult:refine"].GeneratedText);
    }

    [Fact]
    public async Task AnUntilBesideTheCheck_StopsTheWhile_WhenItHolds()
    {
        var run = await EngineRun.StartAsync(
            Load(root => V30Package.Node(root, "refine")["until"] = "pass.number >= 2"), EngineRunNestedTests.Notes, Judging(goodAt: null));

        var row = Assert.Single(run.State.Nodes!, n => n.Id == "refine");
        Assert.Equal((2, "until"), (row.Passes, row.Stopped));
        Assert.Equal("Draft 2.", run.State.Blocks["consult:refine"].GeneratedText);
    }

    [Fact]
    public async Task ACheckFirstWhile_ThatNeverHolds_RunsNoPass_AndSkipsItsBlock()
    {
        // A check on an input that does not hold: under checkFirst no pass runs.
        var run = await EngineRun.StartAsync(
            Load(root =>
            {
                var refine = V30Package.Node(root, "refine");
                refine["checkFirst"] = true;
                refine["while"] = "input.kind == \"follow_up\"";
            }),
            EngineRunNestedTests.Notes, Judging(goodAt: 1));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.DoesNotContain(run.Prompts, p => p.NodeId.StartsWith("draft:", StringComparison.Ordinal));
        var row = Assert.Single(run.State.Nodes!, n => n.Id == "refine");
        Assert.Equal((0, "check"), (row.Passes, row.Stopped));
        var block = run.State.Blocks["consult:refine"];
        Assert.Equal((ConsultGenerationBlockStatuses.Skipped, "its check did not hold before the first pass"), (block.Status, block.Reason));
    }

    [Fact]
    public async Task ReadsOfEarlierPasses_AndOfTheWhileFromOutside_Render()
    {
        var run = await EngineRun.StartAsync(
            Load(
                root =>
                {
                    root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "trail", ["label"] = "Revision trail", ["format"] = "prose" });
                    root["deliverables"]![0]!["sources"]!.AsArray().Add("node.trail");
                },
                files =>
                {
                    files["prompts/draft.md"] = "Pass {{ pass.number }}:{{ for p in pass.history }} [{{ p.number }}] {{ p.node.judge.value.feedback }}{{ end }}";
                    files["prompts/trail.md"] = "{{ node.refine.passes }} {{ node.refine.stopped }}:{{ for p in node.refine.history }} {{ p.number }}={{ p.node.draft.content }}{{ end }} Last: {{ node.refine.history[-1].node.judge.value.verdict }}";
                }),
            EngineRunNestedTests.Notes, Judging(goodAt: 3));

        Assert.Equal("Pass 3: [1] Shorter than pass 1. [2] Shorter than pass 2.", Assert.Single(run.Prompts, p => p.NodeId == "draft:3").Prompt);
        Assert.Equal("3 check: 1=Draft 1. 2=Draft 2. 3=Draft 3. Last: good", Assert.Single(run.Prompts, p => p.NodeId == "trail").Prompt);
    }

    [Fact]
    public async Task APassThatCannotFinish_FailsTheWhilesBlock_AndTheJobEnds()
    {
        var run = await EngineRun.StartAsync(Load(), EngineRunNestedTests.Notes,
            key => key == "judge:2" ? EngineRun.Fails : Judging(goodAt: null)(key));

        Assert.DoesNotContain(run.Prompts, p => p.NodeId == "draft:3");
        var block = run.State.Blocks["consult:refine"];
        Assert.Equal(ConsultGenerationBlockStatuses.Failed, block.Status);
        Assert.StartsWith("Refining the summary could not finish pass 2: Judge the draft failed:", block.Error);
        Assert.NotEqual(ConsultGenerationJobStatuses.Running, run.State.Status);
    }

    private static void Matches(string name, string actual)
    {
        var golden = Path.Combine(Path.GetDirectoryName(SourceFile())!, "Golden", name);

        if (!File.Exists(golden))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, actual);
            Assert.Fail($"Wrote {golden}; review it and commit it.");
        }

        Assert.Equal(File.ReadAllText(golden).Replace("\r\n", "\n"), actual);
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;
}
