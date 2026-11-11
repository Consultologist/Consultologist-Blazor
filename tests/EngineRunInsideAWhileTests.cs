using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1342 B3d (package-format-v30-design.md § 3.2): what repeats inside a <c>while</c> — a
/// <c>while</c> in a <c>while</c> (its passes keyed round/pass, <c>pass.parent</c> the
/// enclosing pass) and a <c>for</c> re-run each pass, read whole by its sibling.
/// </summary>
public class EngineRunInsideAWhileTests
{
    private static string Verdict(string verdict, string feedback) =>
        $$"""{ "verdict": "{{verdict}}", "feedback": "{{feedback}}" }""";

    /// <summary>The review while (at most 2 rounds) over the refine while and a critic, as the v30-while-in-a-while fixture.</summary>
    private static WorkflowPackage Reviewing(Action<Dictionary<string, string>>? files = null) =>
        EngineRunNestedTests.Load(WorkflowV30WhileTests.Refining(
            root =>
            {
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "critic", ["label"] = "Critic", ["format"] = "verdict", ["onInvalid"] = "fail" });
                root["nodes"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = "review", ["label"] = "Review", ["while"] = "node.critic.value.verdict != \"good\"", ["max"] = 2,
                    ["nodes"] = new JsonArray("refine", "critic"), ["ret"] = "node.refine"
                });
                var sources = root["deliverables"]![0]!["sources"]!.AsArray();
                sources.Remove(sources.First(s => s!.GetValue<string>() == "node.refine"));
                sources.Add("node.review");
            },
            bundle =>
            {
                bundle["prompts/critic.md"] = "Critique {{ node.refine.content }} after {{ node.refine.passes }} passes ({{ node.refine.stopped }}).";
                bundle["prompts/draft.md"] = "Round {{ pass.parent.number }}, pass {{ pass.number }}.{{ if pass.parent.number > 1 }} Critic: {{ pass.parent.previous.node.critic.value.feedback }}{{ end }}{{ if pass.number > 1 }} Judge: {{ pass.previous.node.judge.value.feedback }}{{ end }}";
                files?.Invoke(bundle);
            }));

    /// <summary>Round 1: the judge says good at pass 2, the critic redo; round 2: the judge good at pass 1, the critic good.</summary>
    private static string? Rounds(string key) => WorkflowItemPath.NodeOf(key) switch
    {
        "judge" => WorkflowItemPath.PathFor(key, "judge") is "1/1" ? Verdict("redo", "Tighter.") : Verdict("good", "Fine."),
        "critic" => WorkflowItemPath.PathFor(key, "critic") == "1" ? Verdict("redo", "Name the plan.") : Verdict("good", "Done."),
        "draft" => $"Draft {WorkflowItemPath.PathFor(key, "draft")}.",
        _ => EngineRunNestedTests.Outputs(key),
    };

    [Fact]
    public async Task AWhileInAWhile_RunsRoundByRound_ReadingThePassAbove()
    {
        var run = await EngineRun.StartAsync(Reviewing(), EngineRunNestedTests.Notes, Rounds);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "draft:1/1", "draft:1/2", "draft:2/1" },
            run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("draft:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal("Round 1, pass 2. Judge: Tighter.", Assert.Single(run.Prompts, p => p.NodeId == "draft:1/2").Prompt);
        Assert.Equal("Round 2, pass 1. Critic: Name the plan.", Assert.Single(run.Prompts, p => p.NodeId == "draft:2/1").Prompt);
        Assert.Equal("Critique Draft 1/2. after 2 passes (check).", Assert.Single(run.Prompts, p => p.NodeId == "critic:1").Prompt);

        Assert.Equal(new[] { ("1", (int?)2, "check"), ("2", 1, "check") },
            run.State.LoopInstances!.Where(r => r.LoopId == "refine").OrderBy(r => r.Path, StringComparer.Ordinal).Select(r => (r.Path, r.Passes, r.Stopped)));
        var review = Assert.Single(run.State.Nodes!, n => n.Id == "review");
        Assert.Equal((2, "check"), (review.Passes, review.Stopped));
        Assert.Equal("Draft 2/1.", run.State.Blocks["consult:review"].GeneratedText);
        Matches("EngineRun30WhileInAWhile.txt", run.Trace());
    }

    /// <summary>refine's body is a sketch (a list of points), a points loop drafting each, a draft reading the loop whole, and the judge.</summary>
    private static WorkflowPackage PointsInRefine() =>
        EngineRunNestedTests.Load(WorkflowV30WhileTests.Refining(
            root =>
            {
                var nodes = root["nodes"]!.AsArray();
                nodes.Add(new JsonObject { ["id"] = "sketch", ["label"] = "Sketch", ["format"] = "outline", ["onInvalid"] = "fail" });
                nodes.Add(new JsonObject { ["id"] = "draft_point", ["label"] = "Draft a point", ["format"] = "prose" });
                nodes.Add(new JsonObject
                {
                    ["id"] = "points", ["label"] = "Points", ["for"] = "item in node.sketch.value.points",
                    ["nodes"] = new JsonArray("draft_point"), ["ret"] = "node.draft_point"
                });
                V30Package.Node(root, "refine")["nodes"] = new JsonArray("sketch", "points", "draft", "judge");
            },
            bundle =>
            {
                bundle["prompts/sketch.md"] = "Sketch pass {{ pass.number }}.";
                bundle["prompts/draft_point.md"] = "Point {{ item.value.point }} in pass {{ pass.number }}.";
                bundle["prompts/draft.md"] = "Draft from{{ for p in node.points }} [{{ p.item.id }}: {{ p.content }}]{{ end }}{{ if pass.number > 1 }} after {{ pass.previous.node.judge.value.feedback }}{{ end }}";
            }));

    [Fact]
    public async Task AForInAWhile_RunsEachPass_AndItsSiblingReadsThatPassesList()
    {
        var run = await EngineRun.StartAsync(PointsInRefine(), EngineRunNestedTests.Notes, key => WorkflowItemPath.NodeOf(key) switch
        {
            "sketch" => WorkflowItemPath.PathFor(key, "sketch") == "1"
                ? """{ "points": [ { "point": "Wheeze" }, { "point": "Fever" } ] }"""
                : """{ "points": [ { "point": "Wheeze and fever" } ] }""",
            "draft_point" => $"P{WorkflowItemPath.PathFor(key, "draft_point")}",
            "judge" => WorkflowItemPath.PathFor(key, "judge") == "1" ? Verdict("redo", "Merge them.") : Verdict("good", "Fine."),
            "draft" => $"Draft {WorkflowItemPath.PathFor(key, "draft")}.",
            _ => EngineRunNestedTests.Outputs(key),
        });

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "draft_point:1/1", "draft_point:1/2", "draft_point:2/1" },
            run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("draft_point:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal("Point Fever in pass 1.", Assert.Single(run.Prompts, p => p.NodeId == "draft_point:1/2").Prompt);
        Assert.Equal("Draft from [1: P1/1] [2: P1/2]", Assert.Single(run.Prompts, p => p.NodeId == "draft:1").Prompt);
        Assert.Equal("Draft from [1: P2/1] after Merge them.", Assert.Single(run.Prompts, p => p.NodeId == "draft:2").Prompt);
        Assert.Equal(new[] { ("1", (int?)2), ("2", 1) },
            run.State.LoopInstances!.Where(r => r.LoopId == "points").OrderBy(r => r.Path, StringComparer.Ordinal).Select(r => (r.Path, r.Count)));
        Assert.Equal("Draft 2.", run.State.Blocks["consult:refine"].GeneratedText);
        Matches("EngineRun30ForInAWhile.txt", run.Trace());
    }

    [Fact]
    public async Task ANodeBesideANestedLoop_ReadsItsItemsList()
    {
        // assemble runs in parts' body after paragraphs, reading that part's paragraphs whole; parts returns it.
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(EngineRunNestedTests.TwoLevels(
                root =>
                {
                    root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "assemble", ["label"] = "Assemble", ["format"] = "prose" });
                    var parts = V30Package.Node(root, "parts");
                    parts["nodes"] = new JsonArray("outline", "paragraphs", "assemble");
                    parts["ret"] = "node.assemble";
                },
                files => files["prompts/assemble.md"] = "{{ item.value.title }}:{{ for p in node.paragraphs }} [{{ p.item.id }}] {{ p.content }}{{ end }}")),
            EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal("History: [1] [draft_paragraph:1/1] [2] [draft_paragraph:1/2]", Assert.Single(run.Prompts, p => p.NodeId == "assemble:1").Prompt);
        Assert.Equal("Plan: [1] [draft_paragraph:2/1]", Assert.Single(run.Prompts, p => p.NodeId == "assemble:2").Prompt);
    }

    [Fact]
    public async Task AnInnerPassThatCannotFinish_FailsTheOuterWhilesBlock()
    {
        var run = await EngineRun.StartAsync(Reviewing(), EngineRunNestedTests.Notes, key => key == "judge:1/1" ? EngineRun.Fails : Rounds(key));

        var block = run.State.Blocks["consult:review"];
        Assert.Equal(ConsultGenerationBlockStatuses.Failed, block.Status);
        Assert.StartsWith("Refining the summary could not finish pass 1: Judge the draft failed:", block.Error);
        Assert.DoesNotContain(run.Prompts, p => p.NodeId.StartsWith("critic:", StringComparison.Ordinal));
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
