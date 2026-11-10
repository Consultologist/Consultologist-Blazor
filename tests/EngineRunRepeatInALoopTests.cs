using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1342 B3c (package-format-v30-design.md § 2.3, § 3): a <c>while</c> inside a <c>for</c>
/// runs per item (keys item/pass), and a nested loop's <c>until</c> and <c>max</c> apply
/// per instance — each recorded on its loop-instance row.
/// </summary>
public class EngineRunRepeatInALoopTests
{
    private static string Verdict(string verdict, string feedback) =>
        $$"""{ "verdict": "{{verdict}}", "feedback": "{{feedback}}" }""";

    /// <summary>Part 1's judge says good at pass <paramref name="goodAtInPart1"/>, part 2's at pass 1; drafts name their path.</summary>
    private static Func<string, string?> Judging(int? goodAtInPart1) => key =>
    {
        switch (WorkflowItemPath.NodeOf(key))
        {
            case "judge":
                var path = WorkflowItemPath.PathFor(key, "judge")!;
                var (part, pass) = (path.Split('/')[0], int.Parse(path.Split('/')[1]));
                return (part == "1" ? pass == goodAtInPart1 : pass == 1) ? Verdict("good", "Fine.") : Verdict("redo", $"Shorter ({path}).");
            case "draft":
                return $"Draft {WorkflowItemPath.PathFor(key, "draft")}.";
            default:
                return EngineRunNestedTests.Outputs(key);
        }
    };

    /// <summary>The refine while moved into parts, which returns it; the inner paragraphs loop dropped.</summary>
    private static WorkflowPackage WhileInParts(Action<JsonObject>? change = null) =>
        EngineRunNestedTests.Load(WorkflowV30WhileTests.Refining(
            root =>
            {
                var parts = V30Package.Node(root, "parts");
                parts["nodes"] = new JsonArray("outline", "refine");
                parts["ret"] = "node.refine";
                var nodes = root["nodes"]!.AsArray();
                foreach (var gone in nodes.Where(node => node!["id"]!.GetValue<string>() is "paragraphs" or "draft_paragraph").ToList())
                {
                    nodes.Remove(gone);
                }

                var sources = root["deliverables"]![0]!["sources"]!.AsArray();
                sources.Remove(sources.First(source => source!.GetValue<string>() == "node.refine"));
                change?.Invoke(root);
            },
            files => files["prompts/draft.md"] = "Part {{ item.value.title }} ({{ node.outline.value.points | array.size }} points), pass {{ pass.number }}.{{ if pass.number > 1 }} Feedback: {{ pass.previous.node.judge.value.feedback }}{{ end }}"));

    [Fact]
    public async Task AWhileInsideAFor_RunsPerItem_StoppingOnItsCheck()
    {
        var run = await EngineRun.StartAsync(WhileInParts(), EngineRunNestedTests.Notes, Judging(goodAtInPart1: 2));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "draft:1/1", "draft:1/2", "draft:2/1" },
            run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("draft:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal("Part History (2 points), pass 2. Feedback: Shorter (1/1).", Assert.Single(run.Prompts, p => p.NodeId == "draft:1/2").Prompt);

        Assert.Equal(
            new[] { ("refine", "1", (int?)2, "check"), ("refine", "2", 1, "check") },
            run.State.LoopInstances!.Where(r => r.LoopId == "refine").OrderBy(r => r.Path, StringComparer.Ordinal).Select(r => (r.LoopId, r.Path, r.Passes, r.Stopped)));
        Assert.Equal("Draft 1/2.", run.State.Blocks["consult:parts:1"].GeneratedText);
        Assert.Equal("Draft 2/1.", run.State.Blocks["consult:parts:2"].GeneratedText);
        Matches("EngineRun30WhileInALoop.txt", run.Trace());
    }

    [Fact]
    public async Task AWhileInsideAFor_AtItsBoundInOneItem_NamesItOnThatInstance()
    {
        var run = await EngineRun.StartAsync(WhileInParts(), EngineRunNestedTests.Notes, Judging(goodAtInPart1: null));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        var part1 = Assert.Single(run.State.LoopInstances!, r => r.LoopId == "refine" && r.Path == "1");
        Assert.Equal((3, "bound", "stopped at its bound of 3; the check never stopped holding"), (part1.Passes, part1.Stopped, part1.StopReason));
        Assert.Equal((1, "check"), Assert.Single(run.State.LoopInstances!, r => r.LoopId == "refine" && r.Path == "2") is var part2 ? (part2.Passes, part2.Stopped) : default);
        Assert.Equal("Draft 1/3.", run.State.Blocks["consult:parts:1"].GeneratedText);
    }

    [Fact]
    public async Task AWhileInsideAFor_WithNoPassInOneItem_SkipsThatItemsBlock()
    {
        var run = await EngineRun.StartAsync(
            WhileInParts(root =>
            {
                var refine = V30Package.Node(root, "refine");
                refine["checkFirst"] = true;
                refine["while"] = "item.value.kind == \"plan\"";
            }),
            EngineRunNestedTests.Notes, Judging(goodAtInPart1: 1));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.DoesNotContain(run.Prompts, p => p.NodeId.StartsWith("draft:1/", StringComparison.Ordinal));
        var block = run.State.Blocks["consult:parts:1"];
        Assert.Equal((ConsultGenerationBlockStatuses.Skipped, "its check did not hold before the first pass"), (block.Status, block.Reason));
        Assert.Equal((0, "check"), Assert.Single(run.State.LoopInstances!, r => r.LoopId == "refine" && r.Path == "1") is var row ? (row.Passes, row.Stopped) : default);
        Assert.Equal((3, "bound"), Assert.Single(run.State.LoopInstances!, r => r.LoopId == "refine" && r.Path == "2") is var other ? (other.Passes, other.Stopped) : default);
    }

    [Fact]
    public async Task AWhileInsideAFor_WhosePassCannotFinish_FailsThatItemOnly()
    {
        var run = await EngineRun.StartAsync(WhileInParts(), EngineRunNestedTests.Notes,
            key => key == "judge:1/1" ? EngineRun.Fails : Judging(goodAtInPart1: 1)(key));

        var failed = run.State.Blocks["consult:parts:1"];
        Assert.Equal(ConsultGenerationBlockStatuses.Failed, failed.Status);
        Assert.StartsWith("Refining the summary could not finish pass 1: Judge the draft failed:", failed.Error);
        Assert.Equal("Draft 2/1.", run.State.Blocks["consult:parts:2"].GeneratedText);
        Assert.NotEqual(ConsultGenerationJobStatuses.Running, run.State.Status);
    }

    [Fact]
    public async Task AnUntilInsideANestedLoop_StopsEachInstanceOnItsOwn()
    {
        // paragraphs stops after a point about onset: part 1 (two points) stops at its first; part 2 runs its one.
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(EngineRunNestedTests.TwoLevels(root => V30Package.Node(root, "paragraphs")["until"] = "item.value.point == \"Onset was sudden.\"")),
            EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "draft_paragraph:1/1", "draft_paragraph:2/1" },
            run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("draft_paragraph:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        var notRun = run.State.Blocks["consult:paragraphs:1/2"];
        Assert.Equal((ConsultGenerationBlockStatuses.Skipped, "not run: stopped early at item 1 (Paragraphs 1)"), (notRun.Status, notRun.Reason));
        Assert.Equal(("until", "its until held at item 1 (Paragraphs 1)"),
            Assert.Single(run.State.LoopInstances!, r => r.LoopId == "paragraphs" && r.Path == "1") is var part1 ? (part1.Stopped, part1.StopReason) : default);
        Assert.Equal("end", Assert.Single(run.State.LoopInstances!, r => r.LoopId == "paragraphs" && r.Path == "2").Stopped);
        Assert.Equal(3, Assert.Single(run.State.Nodes!, n => n.Id == "paragraphs").Count);
        Assert.Null(Assert.Single(run.State.Nodes!, n => n.Id == "paragraphs").Stopped);
        Matches("EngineRun30UntilInANestedLoop.txt", run.Trace());
    }

    [Fact]
    public async Task AnUntilThatNeverHolds_InsideANestedLoop_EndsEveryInstance()
    {
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(EngineRunNestedTests.TwoLevels(root => V30Package.Node(root, "paragraphs")["until"] = "item.value.point == \"Never.\"")),
            EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(3, run.Prompts.Count(p => p.NodeId.StartsWith("draft_paragraph:", StringComparison.Ordinal)));
        Assert.All(run.State.LoopInstances!.Where(r => r.LoopId == "paragraphs"), row => Assert.Equal("end", row.Stopped));
    }

    [Fact]
    public async Task AMaxInsideANestedLoop_AppliesPerInstance_AndTheCountStands()
    {
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(EngineRunNestedTests.TwoLevels(root => V30Package.Node(root, "paragraphs")["max"] = 1)),
            EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.DoesNotContain(run.Prompts, p => p.NodeId == "draft_paragraph:1/2");
        Assert.Equal("not run: stopped at its bound of 1", run.State.Blocks["consult:paragraphs:1/2"].Reason);
        Assert.Equal("bound", Assert.Single(run.State.LoopInstances!, r => r.LoopId == "paragraphs" && r.Path == "1").Stopped);
        Assert.Equal("end", Assert.Single(run.State.LoopInstances!, r => r.LoopId == "paragraphs" && r.Path == "2").Stopped);
        Assert.Equal(3, Assert.Single(run.State.Nodes!, n => n.Id == "paragraphs").Count);
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
