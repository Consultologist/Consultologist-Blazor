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
/// completion. B2d: an item reads its enclosing loops' items as item.parent, and a
/// template iterates the nested lists through the outer element (s.paragraphs).
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

    /// <summary>The 30 minimal with a draft that reads its own item and its part's outline.</summary>
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

    // ---- B2d: item.parent and nested lists (design § 2.4, § 6) ----------------------------

    /// <summary>The v30-three-levels fixture, plus an assemble node iterating the nested lists down to the sentences.</summary>
    internal static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) ThreeLevels() =>
        V30Package.With(
            root =>
            {
                root["inputs"]!.AsArray().Add(new JsonObject { ["id"] = "questions", ["label"] = "Questions", ["type"] = "array", ["items"] = "text" });
                var paragraphs = V30Package.Node(root, "paragraphs");
                paragraphs["nodes"] = new JsonArray("draft_paragraph", "sentences");
                paragraphs["ret"] = "node.sentences";
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "draft_sentence", ["label"] = "Draft sentence", ["format"] = "prose" });
                root["nodes"]!.AsArray().Add(new JsonObject
                {
                    ["id"] = "sentences", ["label"] = "Sentences", ["for"] = "item in input.questions",
                    ["nodes"] = new JsonArray("draft_sentence"), ["ret"] = "node.draft_sentence"
                });
                root["nodes"]!.AsArray().Add(new JsonObject { ["id"] = "assemble", ["label"] = "Assemble", ["format"] = "prose" });
                root["deliverables"]![0]!["sources"]!.AsArray().Add("node.assemble");
            },
            files =>
            {
                files["prompts/draft_paragraph.md"] = "Point {{ item.value.point }} of {{ item.parent.value.title }}.";
                files["prompts/draft_sentence.md"] =
                    "Part {{ item.parent.parent.value.title }}, point {{ item.parent.value.point }}, question {{ item.id }}: {{ item.value }}. {{ node.draft_paragraph.content }}";
                files["prompts/assemble.md"] =
                    "{{ for s in node.parts }}## {{ s.item.value.title }}\n{{ for p in s.paragraphs }}- {{ p.item.value.point }}:{{ for q in p.sentences }} [{{ q.item.id }}] {{ q.content }}{{ end }}\n{{ end }}{{ end }}";
            });

    internal static readonly Dictionary<string, ConsultInputValue> NotesAndQuestions = new(Notes, StringComparer.Ordinal)
    {
        ["questions"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("Does anything at home trigger the wheeze?"), ConsultInputValue.OfText("Is there a cough that wakes them at night?") }),
    };

    [Fact]
    public async Task AnInnerItem_ReadsItsPartAsItemParent()
    {
        var run = await EngineRun.StartAsync(Load(TwoLevels(files: files =>
            files["prompts/draft_paragraph.md"] = "Part {{ item.parent.value.title }} ({{ item.parent.id }}), point {{ item.value.point }}")), Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(
            new[] { ("draft_paragraph:1/1", "Part History (1), point Onset was sudden."), ("draft_paragraph:1/2", "Part History (1), point It has worsened."), ("draft_paragraph:2/1", "Part Plan (2), point Review in a month.") },
            run.Prompts.Where(p => p.NodeId.StartsWith("draft_paragraph:", StringComparison.Ordinal)).OrderBy(p => p.NodeId, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task ThreeLevels_RunToCompletion_ReadingBothParents_AndTheNestedLists()
    {
        var run = await EngineRun.StartAsync(Load(ThreeLevels()), NotesAndQuestions,
            key => WorkflowItemPath.NodeOf(key) switch
            {
                "draft_sentence" => $"Sentence {WorkflowItemPath.PathFor(key, "draft_sentence")}.",
                "draft_paragraph" => $"Paragraph {WorkflowItemPath.PathFor(key, "draft_paragraph")}.",
                _ => Outputs(key),
            });

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        // The blocks are the innermost loop's — a middle level lays out none of its own.
        var served = run.State.ToResponse();
        Assert.Equal((11, 11), (served.TotalBlockCount, served.CompletedBlockCount));
        Assert.DoesNotContain(run.State.Blocks.Keys, id => id.StartsWith("consult:paragraphs:", StringComparison.Ordinal));
        var sentences = run.Prompts.Where(p => p.NodeId.StartsWith("draft_sentence:", StringComparison.Ordinal)).OrderBy(p => p.NodeId, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "draft_sentence:1/1/0", "draft_sentence:1/1/1", "draft_sentence:1/2/0", "draft_sentence:1/2/1", "draft_sentence:2/1/0", "draft_sentence:2/1/1" },
            sentences.Select(p => p.NodeId));
        Assert.Equal("Part History, point It has worsened., question 0: Does anything at home trigger the wheeze?. Paragraph 1/2.", sentences[2].Prompt);
        Assert.Equal("Part Plan, point Review in a month., question 1: Is there a cough that wakes them at night?. Paragraph 2/1.", sentences[5].Prompt);

        // A count per instance at each level: paragraphs under each part, sentences under each paragraph.
        Assert.Equal(
            new[] { ("paragraphs", "1", (int?)2), ("paragraphs", "2", 1), ("sentences", "1/1", 2), ("sentences", "1/2", 2), ("sentences", "2/1", 2) },
            run.State.LoopInstances!.Select(r => (r.LoopId, r.Path, r.Count)).Order());

        Assert.Equal(
            "## History\n- Onset was sudden.: [0] Sentence 1/1/0. [1] Sentence 1/1/1.\n- It has worsened.: [0] Sentence 1/2/0. [1] Sentence 1/2/1.\n"
            + "## Plan\n- Review in a month.: [0] Sentence 2/1/0. [1] Sentence 2/1/1.\n",
            Assert.Single(run.Prompts, p => p.NodeId == "assemble").Prompt);
        Matches("EngineRun30ThreeLevels.txt", run.Trace());
    }

    [Fact]
    public async Task AnInnerIf_InANestedLoop_DecidesOnItemParent()
    {
        // paragraphs' body: an if on the part's kind — only a history part's points are drafted.
        var run = await EngineRun.StartAsync(Load(TwoLevels(root =>
        {
            var paragraphs = V30Package.Node(root, "paragraphs");
            paragraphs["nodes"] = new JsonArray("history_only");
            paragraphs["ret"] = "node.history_only";
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "history_only", ["label"] = "History only", ["if"] = "item.parent.value.kind == \"history\"",
                ["then"] = new JsonObject { ["nodes"] = new JsonArray("draft_paragraph"), ["ret"] = "node.draft_paragraph" },
            });
        })), Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "draft_paragraph:1/1", "draft_paragraph:1/2" },
            run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("draft_paragraph:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
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
