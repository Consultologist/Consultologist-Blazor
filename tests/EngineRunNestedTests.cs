using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1341 B2b (package-format-v30-design.md § 1, § 2): a 30 job whose loop runs inside a
/// loop, end to end in the engine harness — the inner loop is scheduled per outer item
/// (a loop instance), each instance resolves its own list and count, and the job runs to
/// completion. Rendering item.parent and s.paragraphs is B2d; these prompts read only
/// the innermost item and the outer body's nodes.
/// </summary>
public class EngineRunNestedTests
{
    private const string Plan = """
        { "sections": [ { "title": "History", "kind": "history", "topics": [ { "topic": "onset" }, { "topic": "course" } ] },
                        { "title": "Plan", "kind": "plan", "topics": [ { "topic": "follow-up" } ] } ] }
        """;

    internal static readonly Dictionary<string, ConsultInputValue> Notes = new(StringComparer.Ordinal)
    {
        ["notes"] = ConsultInputValue.OfText("A fictional referral: a 40-year-old with wheeze and a fever."),
        ["kind"] = ConsultInputValue.OfText("new"),
    };

    /// <summary>The scripted replies: the plan, each part's outline (two points for the first part, one for the second), stand-ins for the rest.</summary>
    internal static string? Outputs(string key) => WorkflowItemPath.NodeOf(key) switch
    {
        "plan" => Plan,
        "outline" => key.EndsWith(":1", StringComparison.Ordinal)
            ? """{ "points": [ { "point": "Onset was sudden." }, { "point": "It has worsened." } ] }"""
            : """{ "points": [ { "point": "Review in a month." } ] }""",
        "find_problems" => """{ "summary": "One problem.", "problems": [ { "name": "Asthma", "severity": "low" } ] }""",
        "extract" => """{ "concepts": [ { "term": "Asthma", "id": "195967001", "type": "disorder", "isActive": true } ] }""",
        "scope" => "in_scope",
        _ => null,
    };

    internal static WorkflowPackage Load((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowPackageStore.PackageOf(WorkflowPackageStore.Materialise("test@v30", bundle.Manifest,
            new Dictionary<string, string>(bundle.Files, StringComparer.Ordinal), null, V29Catalog.Catalog, V25Package.Catalog(), V30Package.Accepting30));

    /// <summary>The 30 minimal with a draft that reads its own item and its part's outline — no item.parent (B2d).</summary>
    internal static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) TwoLevels(Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null) =>
        V30Package.With(root => change?.Invoke(root), bundle =>
        {
            bundle["prompts/draft_paragraph.md"] = "Point {{ item.id }}: {{ item.value.point }} (one of {{ node.outline.value.points | array.size }}).";
            files?.Invoke(bundle);
        });

    [Fact]
    public async Task ALoopInsideALoop_RunsEachInnerInstance_ToCompletion()
    {
        var run = await EngineRun.StartAsync(Load(TwoLevels()), Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(
            new[] { "draft_paragraph:1/1", "draft_paragraph:1/2", "draft_paragraph:2/1" },
            run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("draft_paragraph:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Contains(run.Prompts, p => p.NodeId == "draft_paragraph:1/2" && p.Prompt == "Point 2: It has worsened. (one of 2).");
        Assert.Contains(run.Prompts, p => p.NodeId == "draft_paragraph:2/1" && p.Prompt == "Point 1: Review in a month. (one of 1).");
        Assert.Equal(
            new[] { "consult:paragraphs:1/1", "consult:paragraphs:1/2", "consult:paragraphs:2/1" },
            run.State.Blocks.Keys.Where(id => id.StartsWith("consult:paragraphs:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Matches("EngineRun30TwoLevels.txt", run.Trace());
    }

    [Fact]
    public async Task AnInnerLoopOverTheOuterItemsField_ResolvesFromTheItem_AtOnce()
    {
        // paragraphs iterates each part's own topics (item.value.topics): two for the first part, one for the second.
        var run = await EngineRun.StartAsync(Load(TwoLevels(
            root => V30Package.Node(root, "paragraphs")["for"] = "item in item.value.topics",
            files => files["prompts/draft_paragraph.md"] = "Topic {{ item.id }}: {{ item.value.topic }} (outline of {{ node.outline.value.points | array.size }}).")), Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(
            new[] { ("draft_paragraph:1/1", "Topic 1: onset (outline of 2)."), ("draft_paragraph:1/2", "Topic 2: course (outline of 2)."), ("draft_paragraph:2/1", "Topic 1: follow-up (outline of 1).") },
            run.Prompts.Where(p => p.NodeId.StartsWith("draft_paragraph:", StringComparison.Ordinal)).OrderBy(p => p.NodeId, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task AnInnerListThatCannotBeRead_FailsItsOuterItemOnly()
    {
        // The second part's outline is not a list: that part fails with the sentence; the first part's paragraphs run.
        var run = await EngineRun.StartAsync(Load(TwoLevels()), Notes,
            key => key == "outline:2" ? """{ "points": "none" }""" : Outputs(key));

        Assert.Contains(run.Prompts, p => p.NodeId == "draft_paragraph:1/2");
        Assert.DoesNotContain(run.Prompts, p => p.NodeId.StartsWith("draft_paragraph:2/", StringComparison.Ordinal));
        var failed = run.State.Blocks["consult:paragraphs:2"];
        Assert.Equal(ConsultGenerationBlockStatuses.Failed, failed.Status);
        Assert.Equal("Paragraphs could not read its list: node.outline.value.points is not a list.", failed.Error);
        Assert.Equal(ConsultGenerationBlockStatuses.Completed, run.State.Blocks["consult:paragraphs:1/1"].Status);
        Assert.NotEqual(ConsultGenerationJobStatuses.Running, run.State.Status);

        // Storage 11 (#1341): the failed instance has its row, with its sentence and no count.
        var row = Assert.Single(run.State.LoopInstances!, r => r.Path == "2");
        Assert.Equal(("paragraphs", (int?)null, "Paragraphs could not read its list: node.outline.value.points is not a list."), (row.LoopId, row.Count, row.Failure));
        Assert.Equal(2, Assert.Single(run.State.LoopInstances!, r => r.Path == "1").Count);
        Assert.DoesNotContain(ConsultGenerationJobs.CreateSemanticEventCandidates(run.State.ToResponse()), c => c.EventKey == "count-resolved:paragraphs:2");
    }

    [Fact]
    public async Task AnInnerItemThatFails_FailsItsOwnBlock_ByItsPath()
    {
        var run = await EngineRun.StartAsync(Load(TwoLevels()), Notes,
            key => key == "draft_paragraph:1/2" ? EngineRun.Fails : Outputs(key));

        Assert.Equal(ConsultGenerationBlockStatuses.Failed, run.State.Blocks["consult:paragraphs:1/2"].Status);
        Assert.Equal(ConsultGenerationBlockStatuses.Completed, run.State.Blocks["consult:paragraphs:1/1"].Status);
        Assert.Equal(ConsultGenerationBlockStatuses.Completed, run.State.Blocks["consult:paragraphs:2/1"].Status);
    }

    [Fact]
    public async Task AnEmptyInnerList_SettlesAtZero_WithItsReason()
    {
        var run = await EngineRun.StartAsync(Load(TwoLevels()), Notes,
            key => key == "outline:2" ? """{ "points": [] }""" : Outputs(key));

        Assert.Contains(run.State.History, e => e.Kind == "loop-resolved" && e.Label == "Paragraphs (2): no items: node.outline.value.points was empty");
        Assert.Equal((0, "no items: node.outline.value.points was empty"), (run.State.LoopInstances!.Single(r => r.Path == "2").Count, run.State.LoopInstances!.Single(r => r.Path == "2").Reason));
        Assert.DoesNotContain(run.Prompts, p => p.NodeId.StartsWith("draft_paragraph:2/", StringComparison.Ordinal));
        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
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
