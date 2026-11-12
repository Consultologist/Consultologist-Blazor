using System.Text.Json.Nodes;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1339 (package-format-v30-design.md § 2): the 30 packages and the seam that
/// lets them through while 30 is wired but not accepted (the bump, #1347, lists
/// it), as <see cref="V29Package.Accepting29"/> did for 29. <see cref="Minimal"/>
/// is the 29 minimal at 30 with a loop inside a loop: <c>parts</c> iterates the
/// sections a local <c>plan</c> node returns; its body outlines each section, then
/// <c>paragraphs</c> iterates that outline's points, drafting one paragraph per
/// point with the section's title read as <c>item.parent.value.title</c>.
/// </summary>
public static class V30Package
{
    public static readonly IReadOnlyCollection<int> Accepting30 = WorkflowPackageValidator.AcceptedSpecVersions.Append(29).Append(30).Distinct().ToList();

    public const string SectionPlan = """
        { "type": "object", "producer": "custom", "outputMacros": false,
          "description": "The sections a note needs.",
          "schema": { "type": "object", "additionalProperties": false, "required": ["sections"],
                      "properties": {
                        "sections": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["title", "kind", "topics"],
                                                                  "properties": { "title": { "type": "string" },
                                                                                  "kind": { "type": "string", "enum": ["history", "plan"] },
                                                                                  "topics": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["topic"],
                                                                                                                          "properties": { "topic": { "type": "string" } } } } } } } } } }
        """;

    public const string Outline = """
        { "type": "object", "producer": "custom", "outputMacros": false,
          "description": "One section's outline.",
          "schema": { "type": "object", "additionalProperties": false, "required": ["points"],
                      "properties": {
                        "points": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["point"],
                                                                "properties": { "point": { "type": "string" } } } } } } }
        """;

    /// <summary>The 30 minimal as JSON text and its files.</summary>
    public static (string ManifestJson, Dictionary<string, string> Files) MinimalJson()
    {
        var (json, files) = V29Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        root["specVersion"] = 30;
        var formats = root["formats"]!.AsArray();
        formats.Add(new JsonObject { ["id"] = "section_plan", ["file"] = "formats/section_plan.json" });
        formats.Add(new JsonObject { ["id"] = "outline", ["file"] = "formats/outline.json" });
        var nodes = root["nodes"]!.AsArray();
        nodes.Add(new JsonObject { ["id"] = "plan", ["label"] = "Plan the sections", ["format"] = "section_plan", ["onInvalid"] = "fail" });
        nodes.Add(new JsonObject { ["id"] = "outline", ["label"] = "Outline the section", ["format"] = "outline", ["onInvalid"] = "fail" });
        nodes.Add(new JsonObject { ["id"] = "draft_paragraph", ["label"] = "Draft paragraph", ["format"] = "prose" });
        nodes.Add(new JsonObject
        {
            ["id"] = "parts", ["label"] = "Parts", ["for"] = "item in node.plan.value.sections",
            ["nodes"] = new JsonArray("outline", "paragraphs"), ["ret"] = "node.paragraphs"
        });
        nodes.Add(new JsonObject
        {
            ["id"] = "paragraphs", ["label"] = "Paragraphs", ["for"] = "item in node.outline.value.points",
            ["nodes"] = new JsonArray("draft_paragraph"), ["ret"] = "node.draft_paragraph"
        });
        root["deliverables"]![0]!["sources"]!.AsArray().Add("node.parts");

        files["formats/section_plan.json"] = SectionPlan;
        files["formats/outline.json"] = Outline;
        files["prompts/plan.md"] = "Plan the sections this referral needs: {{ input.notes }}";
        files["prompts/outline.md"] = "Outline the section {{ item.value.title }}.";
        files["prompts/draft_paragraph.md"] = "Section {{ item.parent.value.title }}, point {{ item.id }}: {{ item.value.point }}. Of {{ node.outline.value.points | array.size }} points.";
        return (root.ToJsonString(WorkflowPackageManifestJson.WriteOptions), files);
    }

    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Minimal()
    {
        var (json, files) = MinimalJson();
        return (Read(json), files);
    }

    public static WorkflowPackageManifest Read(string json) => WorkflowPackageManifestJson.Read(json, "test@v30", Accepting30.ToList());

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
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog(), Accepting30);

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => Validate(bundle).Errors;
}

public class WorkflowV30Tests
{
    private static List<string> Errors(Action<JsonObject> change, Action<Dictionary<string, string>>? files = null) =>
        V30Package.Errors(V30Package.With(change, files));

    private static void AddNode(JsonObject root, JsonObject node) => root["nodes"]!.AsArray().Add(node);

    private static void NoErrors(List<string> errors) => Assert.True(errors.Count == 0, string.Join("\n", errors));

    private static void Says(string sentence, List<string> errors) =>
        Assert.True(errors.Contains(sentence), $"Expected: {sentence}\nGot:\n{string.Join("\n", errors)}");

    // ---- the seam and the boundary -------------------------------------------------------

    [Fact]
    public void TheMinimal_IsValidAt30_ThroughTheSeam()
    {
        var result = V30Package.Validate(V30Package.Minimal());
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ThePublicEntry_Accepts30_SinceTheBump_AndRefuses31()
    {
        // #1347: both sets carry 30; the seam is a harmless duplicate since.
        Assert.Contains(30, WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Contains(30, WorkflowPackageStore.SupportedSpecVersions);
        var (manifest, files) = V30Package.Minimal();

        var result = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());
        Assert.True(result.Errors.Count == 0, string.Join("\n", result.Errors));
        var next = WorkflowPackageValidator.Validate(manifest with { SpecVersion = 31 }, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());
        Assert.Contains(next.Errors, e => e.StartsWith("specVersion 31 is not supported", StringComparison.Ordinal));
    }

    [Fact]
    public void At29_ALoopInsideALoop_IsTheVersionGate()
    {
        // #1347: below 30, nesting is the version gate (the v26 vectors re-exported with it).
        var (json, files) = V30Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        root["specVersion"] = 29;
        var errors = V29Package.Errors((V29Package.Read(root.ToJsonString()), files));

        Says("Loop 'paragraphs' runs inside loop 'parts', which requires specVersion 30.", errors);
    }

    [Fact]
    public void At29_ALoopInABranch_IsTheVersionGate()
    {
        var (json, files) = V30Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        root["specVersion"] = 29;
        GateParagraphsOnAnIf(root);
        var errors = V29Package.Errors((V29Package.Read(root.ToJsonString()), files));

        Says("Loop 'paragraphs' runs inside node 'gate', which requires specVersion 30.", errors);
    }

    /// <summary>The parts' body runs its paragraphs only for a history section — a loop inside a branch inside a loop.</summary>
    private static void GateParagraphsOnAnIf(JsonObject root)
    {
        var sections = V30Package.Node(root, "parts");
        sections["nodes"] = new JsonArray("outline", "gate");
        sections["ret"] = "node.gate";
        AddNode(root, new JsonObject
        {
            ["id"] = "gate", ["label"] = "History only", ["if"] = "item.value.kind == \"history\"",
            ["then"] = new JsonObject { ["nodes"] = new JsonArray("paragraphs"), ["ret"] = "node.paragraphs" },
        });
    }

    // ---- nesting: § 2.1 ---------------------------------------------------------------

    [Fact]
    public void ALoop_RunsInsideABranch_InsideALoop()
    {
        NoErrors(Errors(GateParagraphsOnAnIf));
    }

    [Fact]
    public void ThreeLevels_Nest_AndItemParentChains()
    {
        var errors = Errors(
            root =>
            {
                root["inputs"]!.AsArray().Add(new JsonObject { ["id"] = "questions", ["label"] = "Questions", ["type"] = "array", ["items"] = "text" });
                V30Package.Node(root, "paragraphs")["nodes"] = new JsonArray("draft_paragraph", "sentences");
                V30Package.Node(root, "paragraphs")["ret"] = "node.sentences";
                AddNode(root, new JsonObject { ["id"] = "draft_sentence", ["label"] = "Draft sentence", ["format"] = "prose" });
                AddNode(root, new JsonObject
                {
                    ["id"] = "sentences", ["label"] = "Sentences", ["for"] = "item in input.questions",
                    ["nodes"] = new JsonArray("draft_sentence"), ["ret"] = "node.draft_sentence"
                });
            },
            files => files["prompts/draft_sentence.md"] =
                "Section {{ item.parent.parent.value.title }}, point {{ item.parent.value.point }}, question {{ item.id }}: {{ item.value }}. {{ node.draft_paragraph.content }}");

        NoErrors(errors);
    }

    [Fact]
    public void ABodyHoldingItsOwnHolder_IsRefused()
    {
        var errors = Errors(root => V30Package.Node(root, "paragraphs")["nodes"] = new JsonArray("draft_paragraph", "parts"));

        Assert.Contains(errors, e => e.StartsWith("Node '", StringComparison.Ordinal) && e.EndsWith("; a body cannot hold the node that holds it.", StringComparison.Ordinal));
    }

    [Fact]
    public void ALoopListingItself_IsRefused()
    {
        var errors = Errors(root => V30Package.Node(root, "problems")["nodes"] = new JsonArray("draft_problem", "problems"));

        Says("Node 'problems' lists itself in its body; a body cannot hold the node that holds it.", errors);
    }

    // ---- what an inner loop iterates: § 2.2 ------------------------------------------------

    [Fact]
    public void AnInnerLoop_IteratesTheOuterItemsListField()
    {
        var errors = Errors(
            root => V30Package.Node(root, "paragraphs")["for"] = "item in item.value.topics",
            files => files["prompts/draft_paragraph.md"] = "Section {{ item.parent.value.title }}, topic {{ item.value.topic }}, of {{ node.outline.value.points | array.size }} points.");

        NoErrors(errors);
    }

    [Theory]
    [InlineData("item.value.title", "Loop 'paragraphs' iterates item.value.title, which the outer item types as a text, not an array.")]
    [InlineData("item.value.missing", "Loop 'paragraphs' iterates item.value.missing; the outer item does not declare 'missing' under item.value (fields: kind, title, topics).")]
    [InlineData("item.id", "Loop 'paragraphs' iterates item.id; an inner loop iterates a list field of the outer item — item.value.<field>.")]
    [InlineData("item.value", "Loop 'paragraphs' iterates item.value; an inner loop iterates a list field of the outer item — item.value.<field>.")]
    public void AnOuterItemSource_IsJudged_ByName(string source, string sentence)
    {
        Says(sentence, Errors(root => V30Package.Node(root, "paragraphs")["for"] = "item in " + source));
    }

    [Fact]
    public void ATopLevelLoop_OverItemValue_RunsInsideNoLoop()
    {
        var errors = Errors(root => V30Package.Node(root, "parts")["for"] = "item in item.value.sections");

        Says("Loop 'parts' iterates item.value.sections, but it runs inside no loop; item.value names the enclosing loop's item.", errors);
    }

    [Fact]
    public void AnInnerLoop_OverANodeInAnUnrelatedLoop_IsRefused()
    {
        // problems' body drafts per problem; paragraphs may not iterate a node of that loop.
        var errors = Errors(root => V30Package.Node(root, "paragraphs")["for"] = "item in node.draft_problem.value.points");

        Says("Loop 'paragraphs' iterates node.draft_problem.value.points; draft_problem runs inside loop 'problems' — a loop iterates a node outside it, or in an enclosing loop's body.", errors);
    }

    [Fact]
    public void AnInnerLoop_OverATopLevelNode_IsValid()
    {
        var errors = Errors(
            root => V30Package.Node(root, "paragraphs")["for"] = "item in node.find_problems.value.problems",
            files => files["prompts/draft_paragraph.md"] = "Section {{ item.parent.value.title }}, problem {{ item.value.name }}, of {{ node.outline.value.points | array.size }} points.");

        NoErrors(errors);
    }

    // ---- reads across levels: § 2.4 ----------------------------------------------------------

    [Fact]
    public void ItemParent_InATopLevelLoop_IsRefused()
    {
        var errors = Errors(root => { }, files => files["prompts/outline.md"] = "Outline {{ item.parent.value.title }}.");

        Assert.Contains(errors, e => e.EndsWith("reads item.parent, but its loop runs inside no loop; item.parent is the enclosing loop's item.", StringComparison.Ordinal));
    }

    [Fact]
    public void ItemParent_IsTypedByTheOuterElement()
    {
        var errors = Errors(root => { }, files => files["prompts/draft_paragraph.md"] = "{{ item.parent.value.heading }}");

        Assert.Contains(errors, e => e.StartsWith("Node 'draft_paragraph' prompt reads field 'heading'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnInnerBodyNode_IsNotReadFromTheOuterBody()
    {
        var errors = Errors(root => { }, files => files["prompts/outline.md"] = "Outline {{ item.value.title }} after {{ node.draft_paragraph.content }}.");

        Assert.Contains(errors, e => e.EndsWith("reads node 'draft_paragraph', which runs inside loop 'paragraphs'; read node.paragraphs.", StringComparison.Ordinal));
    }

    [Fact]
    public void AnInnerBodyNode_ReadingTheLoopItRunsIn_IsRefused()
    {
        var errors = Errors(root => { }, files => files["prompts/draft_paragraph.md"] = "{{ item.value.point }} {{ node.parts.content }}");

        Assert.Contains(errors, e => e.Contains("node.parts", StringComparison.Ordinal));
    }

    // ---- the deliverable: § 6 ------------------------------------------------------------------

    [Fact]
    public void ATemplate_IteratesANestedList_ThroughTheOuterElement()
    {
        var errors = Errors(root =>
        {
            AddNode(root, new JsonObject { ["id"] = "assemble", ["label"] = "Assemble", ["format"] = "prose" });
            root["deliverables"]![0]!["sources"]!.AsArray().Add("node.assemble");
        }, files => files["prompts/assemble.md"] =
            "{{ for s in node.parts }}## {{ s.item.value.title }}\n{{ for p in s.paragraphs }}{{ p.content }} ({{ p.item.value.point }})\n{{ end }}{{ end }}");

        NoErrors(errors);
    }

    // ---- edges ---------------------------------------------------------------------------------

    [Fact]
    public void ASourceInTheOuterBody_ThatReadsTheInnerLoop_IsTheSourceSentence()
    {
        var errors = Errors(root => { }, files => files["prompts/outline.md"] = "Outline {{ item.value.title }} after {{ node.paragraphs.content }}.");

        Says("Loop 'paragraphs' iterates node.outline.value.points, and outline reads node.paragraphs — a loop's source runs before the loop.", errors);
    }

    [Fact]
    public void TheWalk_ReadsAnOuterBodyNode_FromAnInnerBody_AndAnInnerListThroughItsOuterLoop()
    {
        var walk = Walk(V30Package.With(root =>
        {
            AddNode(root, new JsonObject { ["id"] = "assemble", ["label"] = "Assemble", ["format"] = "prose" });
            root["deliverables"]![0]!["sources"]!.AsArray().Add("node.assemble");
        }, files => files["prompts/assemble.md"] = "{{ for s in node.parts }}{{ for p in s.paragraphs }}{{ p.content }}{{ end }}{{ end }}"));

        Assert.Contains("outline", walk.EdgesOf("draft_paragraph"));
        Assert.Contains("parts", walk.EdgesOf("assemble"));
        Assert.DoesNotContain("paragraphs", walk.EdgesOf("assemble"));
    }

    private static WorkflowDerivedEdges.Walk Walk((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
    {
        var manifest = WorkflowImpliedPaths.WithImpliedPrompts(bundle.Manifest, bundle.Files);
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, bundle.Files, V25Package.Catalog(), out var snippets, out var contextFor);
        return WorkflowDerivedEdges.WalkAll(manifest, bundle.Files, nodes, snippets, contextFor);
    }
}
