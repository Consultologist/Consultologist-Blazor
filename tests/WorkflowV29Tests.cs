using System.Text.Json.Nodes;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1301 (package-format-v29-design.md § 2, § 3, § 6): the 29 packages and the
/// seam that lets them through while 29 is wired but not accepted (the bump,
/// #1307, lists it), as <see cref="V28Package.Accepting28"/> did for 28.
/// <see cref="Minimal"/> is the 28 minimal at 29 with a loop over a node's
/// value: <c>problems</c> iterates the problems a local <c>problem_list</c>
/// node returns, its body drafting one part per problem.
/// </summary>
public static class V29Package
{
    public static readonly IReadOnlyCollection<int> Accepting29 = WorkflowPackageValidator.AcceptedSpecVersions.Append(28).Append(29).Distinct().ToList();

    public const string ProblemList = """
        { "type": "object", "producer": "custom", "outputMacros": false,
          "description": "The problems a referral names.",
          "schema": { "type": "object", "additionalProperties": false, "required": ["problems", "summary"],
                      "properties": {
                        "summary": { "type": "string" },
                        "problems": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["name", "severity"],
                                                                  "properties": { "name": { "type": "string" },
                                                                                  "severity": { "type": "string", "enum": ["low", "high"] } } } } } } }
        """;

    /// <summary>The 29 minimal as JSON text and its files.</summary>
    public static (string ManifestJson, Dictionary<string, string> Files) MinimalJson()
    {
        var (json, files) = V28Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        root["specVersion"] = 29;
        root["formats"] = new JsonArray(new JsonObject { ["id"] = "problem_list", ["file"] = "formats/problem_list.json" });
        var nodes = root["nodes"]!.AsArray();
        nodes.Add(new JsonObject { ["id"] = "find_problems", ["label"] = "Find problems", ["format"] = "problem_list", ["onInvalid"] = "fail" });
        nodes.Add(new JsonObject { ["id"] = "draft_problem", ["label"] = "Draft problem", ["format"] = "prose" });
        nodes.Add(new JsonObject
        {
            ["id"] = "problems", ["label"] = "Problems", ["for"] = "item in node.find_problems.value.problems",
            ["nodes"] = new JsonArray("draft_problem"), ["ret"] = "node.draft_problem"
        });
        root["deliverables"]![0]!["sources"]!.AsArray().Add("node.problems");

        files["formats/problem_list.json"] = ProblemList;
        files["prompts/find_problems.md"] = "List the problems this referral names: {{ input.notes }}";
        files["prompts/draft_problem.md"] = "Problem {{ item.id }}: {{ item.value.name }} ({{ item.value.severity }}).";
        return (root.ToJsonString(WorkflowPackageManifestJson.WriteOptions), files);
    }

    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Minimal()
    {
        var (json, files) = MinimalJson();
        return (Read(json), files);
    }

    public static WorkflowPackageManifest Read(string json) => WorkflowPackageManifestJson.Read(json, "test@v29", Accepting29.ToList());

    /// <summary>The minimal with its manifest JSON and files changed, then read.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) With(Action<JsonObject> change, Action<Dictionary<string, string>>? files = null)
    {
        var (json, bundleFiles) = MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        change(root);
        files?.Invoke(bundleFiles);
        return (Read(root.ToJsonString()), bundleFiles);
    }

    public static JsonObject Node(JsonObject root, string id) => V28Package.Node(root, id);

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog(), Accepting29);

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => Validate(bundle).Errors;
}

public class WorkflowV29Tests
{
    private static List<string> Errors(Action<JsonObject> change, Action<Dictionary<string, string>>? files = null) =>
        V29Package.Errors(V29Package.With(change, files));

    private static void Source(JsonObject root, string source) => V29Package.Node(root, "problems")["for"] = "item in " + source;

    // ---- the seam and the boundary -------------------------------------------------------

    [Fact]
    public void TheMinimal_IsValidAt29_ThroughTheSeam()
    {
        var result = V29Package.Validate(V29Package.Minimal());
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ThePublicEntry_Accepts29_SinceTheBump_AndRefuses30()
    {
        // #1307: both sets carry 29; the seam is a harmless duplicate since.
        Assert.Contains(29, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Contains(29, WorkflowPackageStore.SupportedSpecVersions);
        var (manifest, files) = V29Package.Minimal();
        Assert.Empty(WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog()).Errors);

        var next = WorkflowPackageValidator.Validate(manifest with { SpecVersion = 30 }, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());
        Assert.Contains(next.Errors, e => e.StartsWith("specVersion 30 is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void At28_ALoopOverANodesValue_IsTheGate()
    {
        // #1307 (design § 6): below 29 a loop over a node's value is the version gate.
        var (json, files) = V29Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        root["specVersion"] = 28;
        var errors = V28Package.Errors((V28Package.Read(root.ToJsonString()), files));

        Assert.Contains("Loop 'problems' iterates node.find_problems.value.problems, which requires specVersion 29.", errors);
    }

    // ---- the source: § 2.1 -------------------------------------------------------------

    [Fact]
    public void ALoop_IteratesAnArrayOfStrings_ThroughAConceptListField()
    {
        // Decided 2026-11-07: concept_list's concepts field iterates as any array field does.
        var errors = Errors(
            root =>
            {
                Source(root, "node.extract.value.concepts");
                // find_problems feeds nothing once the loop reads extract instead.
                var nodes = root["nodes"]!.AsArray();
                nodes.Remove(nodes.Single(n => (string?)n!["id"] == "find_problems"));
            },
            files => files["prompts/draft_problem.md"] = "Concept {{ item.id }}: {{ item.value }}.");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("node.find_problems.value", "Loop 'problems' iterates node.find_problems.value, which find_problems's format types as an object, not an array.")]
    [InlineData("node.find_problems.value.summary", "Loop 'problems' iterates node.find_problems.value.summary, which find_problems's format types as a text, not an array.")]
    [InlineData("node.find_problems.value.issues", "Loop 'problems' iterates node.find_problems.value.issues; find_problems's format does not declare 'issues' under node.find_problems.value (fields: problems, summary).")]
    [InlineData("node.find_problems.value.summary.x", "Loop 'problems' iterates node.find_problems.value.summary.x; node.find_problems.value.summary is a text, not an object.")]
    [InlineData("node.assessment.value.x", "Loop 'problems' iterates node.assessment.value.x; assessment returns prose — a loop iterates a value its format types as an array.")]
    [InlineData("node.find_problems.content", "Loop 'problems' iterates node.find_problems.content; a loop iterates a node's value — node.find_problems.value.<field>.")]
    [InlineData("node.nowhere.value.x", "Loop 'problems' iterates node.nowhere.value.x; the package declares no node 'nowhere'.")]
    [InlineData("node.sections.value", "Loop 'problems' iterates node.sections.value; 'sections' is a flow node — a loop iterates a plain node's value.")]
    [InlineData("node.section_notes.value.x", "Loop 'problems' iterates node.section_notes.value.x; section_notes runs inside loop 'sections' — a loop iterates a node outside every loop.")]
    public void ALoopsSource_IsJudged_ByName(string source, string sentence)
    {
        var errors = Errors(root => Source(root, source));

        Assert.Contains(sentence, errors);
    }

    [Fact]
    public void ASourceThatReadsTheLoop_IsSaidByName_BeforeTheCycle()
    {
        var errors = Errors(
            _ => { },
            files => files["prompts/find_problems.md"] = "Problems again: {{ node.problems.content }}");

        Assert.Contains("Loop 'problems' iterates node.find_problems.value.problems, and find_problems reads node.problems — a loop's source runs before the loop.", errors);
    }

    // ---- the items: § 2.2 --------------------------------------------------------------

    [Fact]
    public void TheItem_IsTypedFromTheFormat()
    {
        var errors = Errors(_ => { }, files => files["prompts/draft_problem.md"] = "{{ item.value.stage }}");

        Assert.Contains(errors, e => e.StartsWith("Node 'draft_problem' prompt reads field 'stage' of", StringComparison.Ordinal) && e.Contains("name, severity", StringComparison.Ordinal));
    }

    [Fact]
    public void ACase_OnAnEnumFieldOfTheItem_IsAClosedSet()
    {
        var errors = Errors(
            root =>
            {
                var nodes = root["nodes"]!.AsArray();
                nodes.Add(new JsonObject { ["id"] = "urgent", ["label"] = "Urgent", ["format"] = "prose" });
                nodes.Add(new JsonObject
                {
                    ["id"] = "triage", ["label"] = "Triage", ["case"] = "item.value.severity",
                    ["branches"] = new JsonArray(
                        new JsonObject { ["when"] = new JsonArray("high"), ["nodes"] = new JsonArray("urgent"), ["ret"] = "node.urgent" },
                        new JsonObject { ["when"] = new JsonArray("low"), ["nodes"] = new JsonArray("draft_problem"), ["ret"] = "node.draft_problem" })
                });
                var loop = V29Package.Node(root, "problems");
                loop["nodes"] = new JsonArray("triage");
                loop["ret"] = "node.triage";
            },
            files => files["prompts/urgent.md"] = "Urgent: {{ item.value.name }}.");

        Assert.Empty(errors);
    }

    [Fact]
    public void ACase_OnATextFieldOfTheItem_IsRefused()
    {
        var errors = Errors(root =>
        {
            V29Package.Node(root, "problems")["nodes"] = new JsonArray("triage");
            V29Package.Node(root, "problems")["ret"] = "node.triage";
            root["nodes"]!.AsArray().Add(new JsonObject
            {
                ["id"] = "triage", ["label"] = "Triage", ["case"] = "item.value.name",
                ["branches"] = new JsonArray(new JsonObject { ["when"] = new JsonArray("asthma"), ["nodes"] = new JsonArray("draft_problem"), ["ret"] = "node.draft_problem" })
            });
        });

        Assert.Contains(errors, e => e.StartsWith("Node 'triage' switches on item.value.name, which is a text;", StringComparison.Ordinal));
    }

    // ---- a folder item's if: § 3.1 ----------------------------------------------------

    private static Action<Dictionary<string, string>> ItemIf(string @if) =>
        files => files["snippets/sections/plan.md"] = V26Package.Header("Plan", @if: @if) + "Plan.";

    [Fact]
    public void AFolderItemsIf_MayReadAScalarNodesValue()
    {
        Assert.Empty(Errors(_ => { }, ItemIf("node.scope.value == \"in_scope\"")));
    }

    [Theory]
    [InlineData("node.scope.content != \"\"", "Snippet folder 'sections' item 'plan' if reads node.scope.content; a folder item's if reads a node's .value (a scalar), inputs and item.id.")]
    [InlineData("node.sections.content != \"\"", "Snippet folder 'sections' item 'plan' if reads node.sections, which loops; a condition decides before the loop runs — read a scalar node.")]
    [InlineData("run.date != \"\"", "Snippet folder 'sections' item 'plan' if reads run.date; a folder item's if reads inputs, item.id and a scalar node's .value.")]
    public void AFolderItemsIf_IsStillRefused_ForEverythingElse(string @if, string sentence)
    {
        Assert.Contains(sentence, Errors(_ => { }, ItemIf(@if)));
    }

    [Fact]
    public void At28_AFolderItemsIf_ReadingANodesValue_IsTheGate()
    {
        var (json, files) = V28Package.MinimalJson();
        ItemIf("node.scope.value == \"in_scope\"")(files);

        var errors = V28Package.Errors((V28Package.Read(json), files));

        Assert.Contains("Snippet folder 'sections' item 'plan' if reads node.scope.value, which requires specVersion 29.", errors);
    }

    // ---- the edges: § 2.1, § 3.3 --------------------------------------------------------

    [Fact]
    public void TheLoop_WaitsOnItsSource_InTheDerivedEdges()
    {
        var walk = Walk(V29Package.Minimal());

        Assert.Contains("find_problems", walk.EdgesOf("problems"));
        Assert.DoesNotContain("find_problems", walk.GatingReads());
    }

    [Fact]
    public void AReaderOfAFolder_WaitsOnANodeGatingOneOfItsItems()
    {
        var bundle = V29Package.With(_ => { }, ItemIf("node.scope.value == \"in_scope\""));
        var walk = Walk(bundle);

        // section_notes runs per folder item (the loop over sections): it waits on the gate's node.
        Assert.Contains("scope", walk.EdgesOf("section_notes"));
        Assert.DoesNotContain("scope", walk.GatingReads());
    }

    [Fact]
    public void Below29_AnItemGateReadsNoNode_SoTheWalkIsUnchanged()
    {
        var (json, files) = V28Package.MinimalJson();
        var walk = Walk((V28Package.Read(json), files));

        Assert.DoesNotContain("scope", walk.EdgesOf("section_notes"));
    }

    private static WorkflowDerivedEdges.Walk Walk((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
    {
        // As the engine reads a 28+ package: the prompts implied, then walked.
        var manifest = WorkflowImpliedPaths.WithImpliedPrompts(bundle.Manifest, bundle.Files);
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, bundle.Files, V25Package.Catalog(), out var snippets, out var contextFor);
        return WorkflowDerivedEdges.WalkAll(manifest, bundle.Files, nodes, snippets, contextFor);
    }
}
