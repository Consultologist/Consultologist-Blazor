using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1342 B3b (package-format-v30-design.md § 3.4): a top-level <c>for … until</c> runs its
/// items one at a time and stops after the item where its until holds; a <c>for</c> with
/// <c>max</c> runs its first N. The items it did not run stay rows — "not run" — their
/// blocks skipped with the reason; the count never moves.
/// </summary>
public class EngineRunUntilTests
{
    private const string ThreeParts = """
        { "sections": [ { "title": "History", "kind": "history", "topics": [ { "topic": "onset" } ] },
                        { "title": "Plan", "kind": "plan", "topics": [ { "topic": "follow-up" } ] },
                        { "title": "Course", "kind": "history", "topics": [ { "topic": "course" } ] } ] }
        """;

    private static string? Outputs(string key) => WorkflowItemPath.NodeOf(key) switch
    {
        "plan" => ThreeParts,
        "outline" => $$"""{ "points": [ { "point": "Point of part {{WorkflowItemPath.PathFor(key, "outline")}}." } ] }""",
        "draft_paragraph" => $"Part {WorkflowItemPath.PathFor(key, "draft_paragraph")}.",
        _ => EngineRunNestedTests.Outputs(key),
    };

    /// <summary>The 30 minimal with parts a flat loop (outline, then a draft it returns), changed by <paramref name="parts"/>.</summary>
    private static WorkflowPackage Load(Action<JsonObject> parts, Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null) =>
        EngineRunNestedTests.Load(V30Package.With(
            root =>
            {
                var loop = V30Package.Node(root, "parts");
                loop["nodes"] = new JsonArray("outline", "draft_paragraph");
                loop["ret"] = "node.draft_paragraph";
                var nodes = root["nodes"]!.AsArray();
                foreach (var gone in nodes.Where(node => node!["id"]!.GetValue<string>() is "paragraphs").ToList())
                {
                    nodes.Remove(gone);
                }

                parts(loop);
                change?.Invoke(root);
            },
            bundle =>
            {
                bundle["prompts/draft_paragraph.md"] = "Draft {{ item.value.title }} from {{ node.outline.content }}";
                files?.Invoke(bundle);
            }));

    [Fact]
    public async Task AnUntil_RunsItemsInOrder_AndStopsAfterTheItemWhereItHolds()
    {
        var run = await EngineRun.StartAsync(Load(loop => loop["until"] = "item.value.kind == \"plan\""), EngineRunNestedTests.Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        var outlines = run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("outline:", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "outline:1", "outline:2" }, outlines);

        var notRun = run.State.NodeOutputs!["parts:3"];
        Assert.Equal((ConsultGenerationNodeStatuses.Skipped, "not run: stopped early at item 2 (Parts 2)"), (notRun.Status, notRun.Reason));
        var block = run.State.Blocks["consult:parts:3"];
        Assert.Equal((ConsultGenerationBlockStatuses.Skipped, "not run: stopped early at item 2 (Parts 2)"), (block.Status, block.Reason));

        var row = Assert.Single(run.State.Nodes!, n => n.Id == "parts");
        Assert.Equal((3, "until", "its until held at item 2 (Parts 2)"), (row.Count, row.Stopped, row.StopReason));
        Assert.Single(ConsultGenerationJobs.CreateSemanticEventCandidates(run.State.ToResponse()), c => c.EventKey == "count-resolved:parts");
        Matches("EngineRun30Until.txt", run.Trace());
    }

    [Fact]
    public async Task AnUntilThatNeverHolds_RunsEveryItem_AndEndsAtTheEnd()
    {
        var run = await EngineRun.StartAsync(Load(loop => loop["until"] = "item.value.title == \"Never\""), EngineRunNestedTests.Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "outline:1", "outline:2", "outline:3" }, run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("outline:", StringComparison.Ordinal)));
        var row = Assert.Single(run.State.Nodes!, n => n.Id == "parts");
        Assert.Equal((3, "end"), (row.Count, row.Stopped));
        Assert.DoesNotContain(run.State.Blocks.Values, b => b.Status == ConsultGenerationBlockStatuses.Skipped);
    }

    [Fact]
    public async Task AFailedItem_UnderAnUntil_GoesOnToTheNext()
    {
        var run = await EngineRun.StartAsync(Load(loop => loop["until"] = "item.value.kind == \"plan\""), EngineRunNestedTests.Notes,
            key => key == "outline:1" ? EngineRun.Fails : Outputs(key));

        Assert.Equal(ConsultGenerationBlockStatuses.Failed, run.State.Blocks["consult:parts:1"].Status);
        Assert.Contains(run.Prompts, p => p.NodeId == "outline:2");
        Assert.DoesNotContain(run.Prompts, p => p.NodeId == "outline:3");
        Assert.Equal("not run: stopped early at item 2 (Parts 2)", run.State.Blocks["consult:parts:3"].Reason);
    }

    [Fact]
    public async Task AMax_RunsTheFirstItems_AndTheRestAreNotRun()
    {
        var run = await EngineRun.StartAsync(
            Load(
                loop => loop["max"] = 2,
                root =>
                {
                    root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "assemble", ["label"] = "Assemble", ["format"] = "prose" });
                    root["deliverables"]![0]!["sources"]!.AsArray().Add("node.assemble");
                },
                files => files["prompts/assemble.md"] = "{{ for e in node.parts }}{{ e.item.id }}:{{ if e.skipped }}not run{{ else }}ran{{ end }};{{ end }}{{ if node.parts.stopped == \"bound\" }} (first 2 parts){{ end }}"),
            EngineRunNestedTests.Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "outline:1", "outline:2" }, run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("outline:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(("not run: stopped at its bound of 2"), run.State.Blocks["consult:parts:3"].Reason);
        var row = Assert.Single(run.State.Nodes!, n => n.Id == "parts");
        Assert.Equal((3, "bound"), (row.Count, row.Stopped));
        Assert.Equal("1:ran;2:ran;3:not run; (first 2 parts)", Assert.Single(run.Prompts, p => p.NodeId == "assemble").Prompt);
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
