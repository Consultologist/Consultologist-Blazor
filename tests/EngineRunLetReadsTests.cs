using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1343 B4b (package-format-v30-design.md § 4.1, § 6): what a <c>let</c> reads and what
/// reads it — a reduce over a nested loop's list (per instance, once it composed), a
/// loop over a let's value (at the top and per instance), a let in a while (per pass),
/// a deliverable sourcing a let (composed and attested by its hash).
/// </summary>
public class EngineRunLetReadsTests
{
    private const string TwoProblems = """
        { "summary": "Two problems.", "problems": [ { "name": "Anaemia", "severity": "high" }, { "name": "Gout", "severity": "low" } ] }
        """;

    private static string? Outputs(string key) => WorkflowItemPath.NodeOf(key) switch
    {
        "find_problems" => TwoProblems,
        _ => EngineRunNestedTests.Outputs(key),
    };

    private static IEnumerable<string> PromptsOf(EngineRun run, string nodeId) =>
        run.Prompts.Where(p => WorkflowItemPath.NodeOf(p.NodeId) == nodeId).OrderBy(p => p.NodeId, StringComparer.Ordinal).Select(p => p.Prompt);

    /// <summary>The job completed — or the rows that failed, by their sentences.</summary>
    private static void Completed(EngineRun run) =>
        Assert.True(run.State.Status == ConsultGenerationJobStatuses.Completed,
            string.Join("\n", run.State.NodeOutputs!.Where(pair => pair.Value.Status == ConsultGenerationNodeStatuses.Failed).Select(pair => $"{pair.Key}: {pair.Value.Error}")));

    private static void AddNode(JsonObject root, JsonObject node) => root["nodes"]!.AsArray().Add(node);

    // ---- a reduce over a nested loop's list ------------------------------------------------

    /// <summary>The two-level minimal with parts returning an assembly that reads a count of its paragraphs.</summary>
    private static WorkflowPackage Reducing(string let = "node.paragraphs | array.size") =>
        EngineRunNestedTests.Load(EngineRunNestedTests.TwoLevels(
            root =>
            {
                AddNode(root, new JsonObject { ["id"] = "paragraph_count", ["label"] = "Counting the paragraphs", ["let"] = let, ["type"] = "number", ["onInvalid"] = "fail" });
                AddNode(root, new JsonObject { ["id"] = "assemble", ["label"] = "Assemble the part", ["format"] = "prose" });
                var parts = V30Package.Node(root, "parts");
                parts["nodes"] = new JsonArray("outline", "paragraphs", "paragraph_count", "assemble");
                parts["ret"] = "node.assemble";
            },
            files => files["prompts/assemble.md"] =
                "{{ node.paragraph_count.value }} paragraph(s): {{ for p in node.paragraphs }}[{{ p.content }}]{{ end }}"));

    [Fact]
    public async Task AReduceOverANestedLoopsList_IsComputedPerInstance_OnceItComposed()
    {
        var run = await EngineRun.StartAsync(Reducing(), EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);

        Completed(run);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("2"), run.State.NodeOutputs!["paragraph_count:1"].OutputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("1"), run.State.NodeOutputs["paragraph_count:2"].OutputHash);
        Assert.Equal(
            new[] { "2 paragraph(s): [[draft_paragraph:1/1]][[draft_paragraph:1/2]]", "1 paragraph(s): [[draft_paragraph:2/1]]" },
            PromptsOf(run, "assemble"));

        // Each part's count ran after that part's paragraphs composed.
        var calls = run.Context.Calls.Select(call => call.InputJson).ToList();
        int IndexOf(string marker) => calls.FindIndex(json => json.Contains(marker, StringComparison.Ordinal));
        Assert.True(IndexOf("\"NodeId\":\"paragraph_count:1\"") > IndexOf("\"NodeId\":\"draft_paragraph:1/2\""));
        Matches("EngineRun30LetReduce.txt", run.Trace());
    }

    // ---- a loop over a let's value -----------------------------------------------------------

    [Fact]
    public async Task ALoopOverALetsValue_ResolvesFromTheLet()
    {
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(WorkflowV30LetTests.Counting(
                root =>
                {
                    (V30Package.Node(root, "problem_count")["let"], V30Package.Node(root, "problem_count")["type"]) = ("node.find_problems.value", "problem_list");
                    V30Package.Node(root, "problems")["for"] = "item in node.problem_count.value.problems";
                },
                files => files["prompts/draft_problem.md"] = "{{ item.value.name }}: {{ node.problem_count.value.summary }}")),
            EngineRunNestedTests.Notes, Outputs);

        Completed(run);
        Assert.Equal(new[] { "Anaemia: Two problems.", "Gout: Two problems." }, PromptsOf(run, "draft_problem"));
        Assert.Equal(2, Assert.Single(run.State.Nodes!, n => n.Id == "problems").Count);

        var let = Assert.Single(run.State.Nodes!, n => n.Id == "problem_count");
        Assert.Equal(("problem_list", "problem_list"), (let.Format, let.LetType));
        Assert.NotNull(let.FormatHash);
        Assert.Equal(
            ConsultGenerationProvenance.Sha256Hex("""{"problems":[{"name":"Anaemia","severity":"high"},{"name":"Gout","severity":"low"}],"summary":"Two problems."}"""),
            run.State.NodeOutputs!["problem_count"].OutputHash);
        Matches("EngineRun30LetFeedsALoop.txt", run.Trace());
    }

    [Fact]
    public async Task APerItemLet_FeedsAnInnerLoop_PerInstance()
    {
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(EngineRunNestedTests.TwoLevels(root =>
            {
                AddNode(root, new JsonObject { ["id"] = "points_of", ["label"] = "The points", ["let"] = "node.outline.value", ["type"] = "outline", ["onInvalid"] = "fail" });
                V30Package.Node(root, "parts")["nodes"] = new JsonArray("outline", "points_of", "paragraphs");
                V30Package.Node(root, "paragraphs")["for"] = "item in node.points_of.value.points";
            })),
            EngineRunNestedTests.Notes, EngineRunNestedTests.Outputs);

        Completed(run);
        Assert.Equal(new[] { "draft_paragraph:1/1", "draft_paragraph:1/2", "draft_paragraph:2/1" },
            run.Prompts.Select(p => p.NodeId).Where(id => id.StartsWith("draft_paragraph:", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal("computed", run.State.NodeOutputs!["points_of:1"].Producer);
    }

    // ---- a let in a while --------------------------------------------------------------------

    [Fact]
    public async Task ALetInAWhile_IsComputedPerPass_AndReadInItsPass()
    {
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(WorkflowV30WhileTests.Refining(
                root =>
                {
                    AddNode(root, new JsonObject { ["id"] = "pass_mark", ["label"] = "The pass's mark", ["let"] = "pass.number * 10", ["type"] = "number", ["onInvalid"] = "fail" });
                    V30Package.Node(root, "refine")["nodes"] = new JsonArray("draft", "pass_mark", "judge");
                },
                files => files["prompts/judge.md"] = "Judge this draft ({{ node.pass_mark.value }}): {{ node.draft.content }}")),
            EngineRunNestedTests.Notes, EngineRunWhileTests.Judging(goodAt: 2));

        Completed(run);
        Assert.Equal(new[] { "Judge this draft (10): Draft 1.", "Judge this draft (20): Draft 2." }, PromptsOf(run, "judge"));

        var first = run.State.NodeOutputs!["pass_mark:1"];
        var second = run.State.NodeOutputs["pass_mark:2"];
        Assert.Equal((ConsultGenerationProvenance.Sha256Hex("10"), ConsultGenerationProvenance.Sha256Hex("20")), (first.OutputHash, second.OutputHash));
        Assert.NotEqual(first.InputHash, second.InputHash);
        Assert.Equal(("computed", "computed"), (first.Producer, second.Producer));
    }

    // ---- a deliverable sourcing a let --------------------------------------------------------

    [Fact]
    public async Task ADeliverableSourcingALet_ComposesItsValue_AndAttestsItsHash()
    {
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(WorkflowV30LetTests.Counting(
                root =>
                {
                    (V30Package.Node(root, "problem_count")["let"], V30Package.Node(root, "problem_count")["type"]) =
                        ("node.find_problems.value.problems | array.map \"name\" | array.join \", \"", "text");
                    root["deliverables"]![0]!["sources"]!.AsArray().Add("node.problem_count");
                },
                files => files["prompts/draft_problem.md"] = "{{ item.value.name }} of {{ node.problem_count.content }}.")),
            EngineRunNestedTests.Notes, Outputs);

        Completed(run);
        var block = run.State.Blocks["consult:problem_count"];
        Assert.Equal((ConsultGenerationBlockStatuses.Completed, "Anaemia, Gout"), (block.Status, block.GeneratedText));
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("Anaemia, Gout"), run.State.NodeOutputs!["problem_count"].OutputHash);

        // Attested as any source is (v26 § 10): the document row names the let among its
        // sources, and its composed hash is over the text the let's value is part of.
        var document = Assert.Single(run.State.AssembledDocuments!, d => d.ResultId == "consult");
        Assert.Contains("problem_count", document.Sources!);
        Assert.Contains("Anaemia, Gout", document.Text);
        Assert.NotNull(document.ComposedHash);
    }

    // ---- a let that fails inside a loop -----------------------------------------------------

    [Fact]
    public async Task ALetThatFailsInALoop_FailsItsItemsBlocks_WithItsSentence_AndTheRestCompletes()
    {
        var run = await EngineRun.StartAsync(
            EngineRunNestedTests.Load(WorkflowV30LetTests.Counting(root =>
            {
                (V30Package.Node(root, "problem_count")["let"], V30Package.Node(root, "problem_count")["type"]) =
                    ("[item.value.name] | array.last", "number");
                V30Package.Node(root, "problems")["nodes"]!.AsArray().Insert(0, "problem_count");
            })),
            EngineRunNestedTests.Notes, Outputs);

        // As any body node's failure: the item's block fails with the sentence; no draft runs for it.
        foreach (var block in new[] { "consult:problems:1", "consult:problems:2" })
        {
            Assert.Equal(ConsultGenerationBlockStatuses.Failed, run.State.Blocks[block].Status);
        }

        Assert.Contains(run.State.History, e => e.Label == "Section failed: Problems 1"
            && e.Detail == "Counting the problems failed: Let 'problem_count' yielded a text; it declares type number.");
        Assert.DoesNotContain(run.Prompts, p => p.NodeId.StartsWith("draft_problem:", StringComparison.Ordinal));
        Assert.Equal(ConsultGenerationBlockStatuses.Completed, run.State.Blocks["consult:sections:hpi"].Status);
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
