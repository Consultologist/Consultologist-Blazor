using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1343 B4 (package-format-v30-design.md § 4.2): a <c>let</c> runs as a computed node —
/// its expression evaluated over the scope a template at its place reads, its value
/// typed, stored (a text bare, the rest canonical JSON) and hashed, its row naming its
/// producer. The 30 minimal's <c>problem_count</c> counts the problems a referral names;
/// each drafted problem reads it.
/// </summary>
public class EngineRunLetTests
{
    private const string TwoProblems = """
        { "summary": "Two problems.", "problems": [ { "name": "Anaemia", "severity": "high" }, { "name": "Gout", "severity": "low" } ] }
        """;

    private static string? Outputs(string key) => WorkflowItemPath.NodeOf(key) switch
    {
        "find_problems" => TwoProblems,
        _ => EngineRunNestedTests.Outputs(key),
    };

    private static WorkflowPackage Load(Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null) =>
        EngineRunNestedTests.Load(WorkflowV30LetTests.Counting(change, files));

    private static void Let(JsonObject root, string expression, string type) =>
        (V30Package.Node(root, "problem_count")["let"], V30Package.Node(root, "problem_count")["type"]) = (expression, type);

    private static IEnumerable<string> PromptsOf(EngineRun run, string nodeId) =>
        run.Prompts.Where(p => WorkflowItemPath.NodeOf(p.NodeId) == nodeId).OrderBy(p => p.NodeId, StringComparer.Ordinal).Select(p => p.Prompt);

    [Fact]
    public async Task AScalarLet_IsComputedOnce_RecordedAndHashed_AndReadAsANumber()
    {
        var run = await EngineRun.StartAsync(
            Load(files: bundle => bundle["prompts/draft_problem.md"] =
                "Problem {{ item.id }} of {{ node.problem_count.value }}{{ if node.problem_count.value > 1 }} (one of several){{ end }}: {{ item.value.name }}."),
            EngineRunNestedTests.Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(
            new[] { "Problem 1 of 2 (one of several): Anaemia.", "Problem 2 of 2 (one of several): Gout." },
            PromptsOf(run, "draft_problem"));

        // No model call: the let renders no prompt; its row is computed, valid and hashed.
        Assert.DoesNotContain(run.Prompts, p => p.NodeId == "problem_count");
        var row = run.State.NodeOutputs!["problem_count"];
        Assert.Equal((ConsultGenerationNodeStatuses.Completed, "computed", true), (row.Status, row.Producer, row.Validation?.Valid));
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("2"), row.OutputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex($"node.find_problems.value.problems | array.size\nfind_problems={TwoProblems}"), row.InputHash);

        // Every other row names no producer — its bytes are as before.
        Assert.All(run.State.NodeOutputs.Where(pair => pair.Key != "problem_count"), pair => Assert.Null(pair.Value.Producer));

        var descriptor = Assert.Single(run.State.Nodes!, n => n.Id == "problem_count");
        Assert.Equal(("computed", "let", "number", (string?)null), (descriptor.Producer, descriptor.Op, descriptor.LetType, descriptor.Format));
        Matches("EngineRun30Let.txt", run.Trace());
    }

    [Fact]
    public async Task ALetInALoop_IsComputedPerItem_AndReadByItsItem()
    {
        var run = await EngineRun.StartAsync(
            Load(root =>
            {
                Let(root, "item.value.name | string.size", "number");
                V30Package.Node(root, "problems")["nodes"]!.AsArray().Insert(0, "problem_count");
            }),
            EngineRunNestedTests.Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "Problem 1 of 7: Anaemia (high).", "Problem 2 of 4: Gout (low)." }, PromptsOf(run, "draft_problem"));

        var first = run.State.NodeOutputs!["problem_count:1"];
        Assert.Equal(("computed", ConsultGenerationProvenance.Sha256Hex("7")), (first.Producer, first.OutputHash));
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("4"), run.State.NodeOutputs["problem_count:2"].OutputHash);
    }

    [Fact]
    public async Task ATextLet_IsStoredBare()
    {
        var run = await EngineRun.StartAsync(
            Load(root => Let(root, "node.find_problems.value.problems | array.map \"name\" | array.join \", \"", "text"),
                bundle => bundle["prompts/draft_problem.md"] = "{{ item.value.name }}, among {{ node.problem_count.content }}."),
            EngineRunNestedTests.Notes, Outputs);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        Assert.Equal(new[] { "Anaemia, among Anaemia, Gout.", "Gout, among Anaemia, Gout." }, PromptsOf(run, "draft_problem"));
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("Anaemia, Gout"), run.State.NodeOutputs!["problem_count"].OutputHash);
    }

    [Fact]
    public async Task ABooleanLet_IsReadAsABoolean()
    {
        var run = await EngineRun.StartAsync(
            Load(root => Let(root, "(node.find_problems.value.problems | array.size) > 1", "boolean"),
                bundle => bundle["prompts/draft_problem.md"] = "{{ item.value.name }}{{ if node.problem_count.value }}, one of several{{ end }}."),
            EngineRunNestedTests.Notes, Outputs);

        Assert.Equal(new[] { "Anaemia, one of several.", "Gout, one of several." }, PromptsOf(run, "draft_problem"));
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("true"), run.State.NodeOutputs!["problem_count"].OutputHash);
    }

    // The walk does not type what array.map yields, so the mismatch is the run's
    // (design § 4.1: "the rest is the run's onInvalid").
    private const string AName = "node.find_problems.value.problems | array.map \"name\" | array.last";

    [Fact]
    public async Task ATypeMismatch_UnderFail_FailsTheNode_NamingIt()
    {
        var run = await EngineRun.StartAsync(Load(root => Let(root, AName, "number")), EngineRunNestedTests.Notes, Outputs);

        var row = run.State.NodeOutputs!["problem_count"];
        Assert.Equal(ConsultGenerationNodeStatuses.Failed, row.Status);
        Assert.Contains("Let 'problem_count' yielded a text; it declares type number.", row.Error);
        Assert.Empty(PromptsOf(run, "draft_problem"));
    }

    [Fact]
    public async Task ATypeMismatch_UnderFlag_KeepsTheValue_Flagged()
    {
        var run = await EngineRun.StartAsync(
            Load(root =>
            {
                Let(root, AName, "number");
                V30Package.Node(root, "problem_count")["onInvalid"] = "flag";
            }),
            EngineRunNestedTests.Notes, Outputs);

        var row = run.State.NodeOutputs!["problem_count"];
        Assert.Equal((ConsultGenerationNodeStatuses.Completed, false), (row.Status, row.Validation?.Valid));
        Assert.Equal(new[] { "Let 'problem_count' yielded a text; it declares type number." }, row.Validation!.Errors);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex("Gout"), row.OutputHash);
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
