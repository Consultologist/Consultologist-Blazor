using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1283 (package-format-v28-design.md): the 28 packages and the seam that lets
/// them through while 28 is wired but not yet accepted (the bump, #1286, lists
/// it), as <see cref="V27Package.Accepting27"/> did for 27. <see cref="Minimal"/>
/// is the 27 minimal spelled at 28 — no prompt keys, the tables as lists of ids,
/// the folder index the object, the one mismatched prompt (section_notes) at
/// prompts/section_notes.md — and is read through the manifest reader, so the
/// converters' shapes are the wire's.
/// </summary>
public static class V28Package
{
    public static readonly IReadOnlyCollection<int> Accepting28 = WorkflowPackageValidator.AcceptedSpecVersions.Append(28).ToList();

    /// <summary>The 28 minimal as JSON text and its files.</summary>
    public static (string ManifestJson, Dictionary<string, string> Files) MinimalJson()
    {
        var (manifest, files27) = V27Package.Minimal();
        var root = JsonNode.Parse(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions))!.AsObject();
        root["specVersion"] = 28;
        foreach (var node in root["nodes"]!.AsArray())
        {
            node!.AsObject().Remove("prompt");
        }

        root["macros"] = new JsonArray("closing");
        root["snippets"] = new JsonArray("standards", "sections/");

        var files = new Dictionary<string, string>(files27, StringComparer.Ordinal);
        files["prompts/section_notes.md"] = files["prompts/section.md"];
        files.Remove("prompts/section.md");
        files["snippets/sections/index.json"] = """{ "items": ["hpi", "plan"] }""";
        return (root.ToJsonString(WorkflowPackageManifestJson.WriteOptions), files);
    }

    /// <summary>The 28 minimal, read as a package is.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Minimal()
    {
        var (json, files) = MinimalJson();
        return (Read(json), files);
    }

    public static WorkflowPackageManifest Read(string json) => WorkflowPackageManifestJson.Read(json, "test@v28", Accepting28.ToList());

    /// <summary>The minimal with its manifest JSON changed, then read.</summary>
    public static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) WithJson(Action<JsonObject> change, Action<Dictionary<string, string>>? files = null)
    {
        var (json, bundleFiles) = MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        change(root);
        files?.Invoke(bundleFiles);
        return (Read(root.ToJsonString()), bundleFiles);
    }

    public static JsonObject Node(JsonObject root, string id) =>
        root["nodes"]!.AsArray().Select(n => n!.AsObject()).Single(n => (string?)n["id"] == id);

    public static WorkflowPackageValidator.ValidationResult Validate((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) =>
        WorkflowPackageValidator.Validate(bundle.Manifest, bundle.Files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog(), Accepting28);

    public static List<string> Errors((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => Validate(bundle).Errors;

    public static List<string> Warnings((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle) => Validate(bundle).Warnings;
}

public class WorkflowV28Tests
{
    private const string SnakeCase = "(a lowercase letter, then lowercase letters, digits, or underscores)";

    // ---- the seam and the boundary ---------------------------------------------------

    [Fact]
    public void TheMinimal_IsValidAt28_ThroughTheSeam_WithNoWarnings()
    {
        var result = V28Package.Validate(V28Package.Minimal());
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void ThePublicEntry_StillRefuses28_UntilTheBump()
    {
        Assert.DoesNotContain(28, WorkflowPackageValidator.AcceptedSpecVersions);
        var (manifest, files) = V28Package.Minimal();
        var result = WorkflowPackageValidator.Validate(manifest, files, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog());
        Assert.Contains(result.Errors, e => e.StartsWith("specVersion 28 is not supported", StringComparison.Ordinal));
    }

    // ---- the wire shapes ---------------------------------------------------------------

    [Fact]
    public void TheIdLists_ReadIntoTheImpliedPaths_AndMarkTheirShape()
    {
        var (manifest, _) = V28Package.Minimal();

        var macros = Assert.IsType<WorkflowMacroTable>(manifest.Macros);
        Assert.Equal(WorkflowTableShape.IdList, macros.Shape);
        Assert.False(macros.IsMap);
        Assert.Equal(("closing", "macros/closing.md"), (macros[0].Id, macros[0].File));

        var snippets = Assert.IsType<WorkflowSnippetTable>(manifest.Snippets);
        Assert.Equal(WorkflowTableShape.IdList, snippets.Shape);
        Assert.Equal(new[] { ("standards", "snippets/standards.md"), ("sections", "snippets/sections/") }, snippets.Select(p => (p.Key, p.Value)));

        Assert.All(manifest.Nodes!, node => Assert.Null(node.Prompt));
    }

    [Fact]
    public void TheIdLists_WriteBackAsLists_AndA27Map_AsAMap()
    {
        var (manifest, _) = V28Package.Minimal();
        var written = JsonNode.Parse(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions))!;
        Assert.Equal("""["closing"]""", written["macros"]!.ToJsonString());
        Assert.Equal("""["standards","sections/"]""", written["snippets"]!.ToJsonString());

        var json27 = JsonSerializer.Serialize(V27Package.Minimal().Manifest, WorkflowPackageManifestJson.WriteOptions);
        var read27 = WorkflowPackageManifestJson.Read(json27, "test@v27", V27Package.Accepting27.ToList());
        Assert.Equal(WorkflowTableShape.Map, Assert.IsType<WorkflowSnippetTable>(read27.Snippets).Shape);
        Assert.Equal(json27, JsonSerializer.Serialize(read27, WorkflowPackageManifestJson.WriteOptions));
    }

    [Fact]
    public void AnIdTwice_IsReadOnce_AndNamedByTheValidator()
    {
        var bundle = V28Package.WithJson(root =>
        {
            root["macros"] = new JsonArray("closing", "closing");
            root["snippets"] = new JsonArray("standards", "sections/", "standards");
        });

        Assert.Single(bundle.Manifest.Macros!);
        var errors = V28Package.Errors(bundle);
        Assert.Contains("macros lists 'closing' twice.", errors);
        Assert.Contains("snippets lists 'standards' twice.", errors);
    }

    // ---- § 2: the manifest ---------------------------------------------------------------

    [Fact]
    public void APromptKey_IsRetired_NamingTheImpliedFile()
    {
        var errors = V28Package.Errors(V28Package.WithJson(root => V28Package.Node(root, "extract")["prompt"] = "prompts/extract.md"));
        Assert.Contains("Node 'extract' declares prompt, which is retired at specVersion 28; a node's prompt is prompts/extract.md.", errors);
    }

    [Fact]
    public void AMap_IsRefused_WithTheListSpelling()
    {
        var errors = V28Package.Errors(V28Package.WithJson(root =>
        {
            root["macros"] = new JsonObject { ["closing"] = "macros/closing.md" };
            root["snippets"] = new JsonObject { ["standards"] = "snippets/standards.md", ["sections"] = "snippets/sections/" };
        }));

        Assert.Contains("macros is a map; at specVersion 28 it is a list of ids — [\"closing\"].", errors);
        Assert.Contains("snippets is a map; at specVersion 28 it is a list of ids — [\"standards\", \"sections/\"].", errors);
        Assert.DoesNotContain(errors, e => e.StartsWith("macros is a list;", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingImpliedPrompt_IsNamed()
    {
        var errors = V28Package.Errors(V28Package.WithJson(_ => { }, files => files.Remove("prompts/scope.md")));
        Assert.Contains("Node 'scope' has no prompt: prompts/scope.md is missing from the package.", errors);
    }

    [Fact]
    public void AComputedNodesFile_IsRefused()
    {
        var errors = V28Package.Errors(V28Package.WithJson(_ => { }, files => files["prompts/coverage.md"] = "Compare."));
        Assert.Contains("Node 'coverage' is computed (format 'terms_subset') and reads no prompt; remove prompts/coverage.md.", errors);
    }

    [Fact]
    public void AMissingMacroOrSnippet_IsNamed()
    {
        var errors = V28Package.Errors(V28Package.WithJson(root =>
        {
            root["macros"] = new JsonArray("closing", "farewell");
            root["snippets"] = new JsonArray("standards", "sections/", "notes/");
        }));

        Assert.Contains("Macro 'farewell' file 'macros/farewell.md' is missing from the package.", errors);
        Assert.Contains("Snippet collection 'notes' is missing 'snippets/notes/index.json'.", errors);
    }

    [Fact]
    public void AnIdThatIsNotSnakeCase_IsRefused()
    {
        var errors = V28Package.Errors(V28Package.WithJson(root => root["macros"] = new JsonArray("Closing"),
            files => files["macros/Closing.md"] = files["macros/closing.md"]));
        Assert.Contains($"Macro id 'Closing' must be snake_case {SnakeCase}.", errors);
    }

    [Fact]
    public void StrayFiles_AreWarnings_NamingWhatTheyWouldBeCalled()
    {
        var bundle = V28Package.WithJson(_ => { }, files =>
        {
            files["prompts/old-draft.md"] = "An old draft.";
            files["macros/spare.md"] = "Spare.";
            files["snippets/loose.md"] = "Loose.";
            files["snippets/sections/extra.md"] = "Extra.";
        });

        var result = V28Package.Validate(bundle);
        Assert.Empty(result.Errors);
        Assert.Equal(new[]
        {
            "macros/spare.md names no macro; at specVersion 28 a macro file is macros/<id>.md, listed in macros.",
            "prompts/old-draft.md names no node; at specVersion 28 a prompt file is prompts/<node id>.md.",
            "snippets/loose.md names no snippet; at specVersion 28 a snippet file is snippets/<id>.md, listed in snippets.",
            "snippets/sections/extra.md is not listed in snippets/sections/index.json; at specVersion 28 an item is <id>.md beside the index, listed in its items.",
        }, result.Warnings);
    }

    // ---- § 3: the folder index -------------------------------------------------------------

    [Fact]
    public void AFolderLabel_IsAccepted_AndRead()
    {
        var bundle = V28Package.WithJson(_ => { }, files => files["snippets/sections/index.json"] = """{ "label": "Sections", "items": ["hpi", "plan"] }""");
        Assert.Empty(V28Package.Errors(bundle));

        var index = WorkflowTextHeaders.ReadIndex(bundle.Files["snippets/sections/index.json"], "sections", "Snippet", 28, new List<string>())!;
        Assert.Equal("Sections", index.Label);
        Assert.Equal(new[] { ("hpi", "hpi.md"), ("plan", "plan.md") }, index.Items);
    }

    [Theory]
    [InlineData("""[ { "id": "hpi", "file": "hpi.md" }, { "id": "plan", "file": "plan.md" } ]""",
        "Snippet folder 'sections' index.json at specVersion 28 is an object { \"label\"?, \"items\": [ids] } — { \"items\": [\"hpi\", \"plan\"] }; each item's file is <id>.md beside the index.")]
    [InlineData("""{ "items": ["hpi", "plan"], "order": "run" }""",
        "Snippet folder 'sections' index.json declares 'order', which is not an index key at specVersion 28 (the keys are label and items).")]
    [InlineData("""{ "label": "Sections" }""",
        "Snippet folder 'sections' index.json has no items; at specVersion 28 items lists the folder's item ids in order.")]
    [InlineData("""{ "label": "", "items": ["hpi", "plan"] }""",
        "Snippet folder 'sections' index.json label must be a non-empty string.")]
    [InlineData("""{ "items": [ { "id": "hpi", "file": "hpi.md" }, "plan" ] }""",
        "Snippet folder 'sections' index.json item { id, file } 'hpi' — at specVersion 28 an item is its id: \"items\": [\"hpi\"].")]
    [InlineData("""{ "items": [] }""",
        "Snippet collection 'sections' declares no items.")]
    [InlineData("""{ "items": ["hpi", "plan", "hpi"] }""",
        "Snippet collection 'sections' has duplicate item id 'hpi'.")]
    [InlineData("""{ "items": ["hpi", "plan", "follow_up"] }""",
        "Snippet collection 'sections' item 'follow_up' file 'snippets/sections/follow_up.md' is missing from the package.")]
    public void AnIndexAt28_IsTheObject_EachRefusalNamed(string index, string sentence)
    {
        var errors = V28Package.Errors(V28Package.WithJson(_ => { }, files => files["snippets/sections/index.json"] = index));
        Assert.Contains(sentence, errors);
    }

    [Fact]
    public void ASnippetFolderItemId_IsSnakeCase_At28()
    {
        var errors = V28Package.Errors(V28Package.WithJson(_ => { }, files =>
        {
            files["snippets/sections/index.json"] = """{ "items": ["hpi", "Plan"] }""";
            files["snippets/sections/Plan.md"] = files["snippets/sections/plan.md"];
            files.Remove("snippets/sections/plan.md");
        }));

        Assert.Contains($"Snippet folder 'sections' item id 'Plan' must be snake_case {SnakeCase}.", errors);
    }

    [Fact]
    public void BelowTwentyEight_TheIndexReader_IsThe26One()
    {
        var errors = new List<string>();
        Assert.Null(WorkflowTextHeaders.ReadIndex("""{ "items": ["hpi"] }""", "sections", "Snippet", 27, errors));
        Assert.Equal(new[] { "Snippet folder 'sections' index.json at specVersion 26 is an ordered list [{ id, file }]; name and when live in the item file's header." }, errors);
    }

    // ---- § 6a: a flow node's description ------------------------------------------------------

    [Fact]
    public void AFlowNodesDescription_IsAccepted()
    {
        var bundle = V28Package.WithJson(root => V28Package.Node(root, "sections")["description"] = "Drafts one note per section.");
        Assert.Empty(V28Package.Errors(bundle));
        Assert.Equal("Drafts one note per section.", bundle.Manifest.Nodes!.Single(n => n.Id == "sections").Description);
    }

    [Fact]
    public void ADescription_OnAPlainNode_EmptyOrTooLong_IsRefused()
    {
        var errors = V28Package.Errors(V28Package.WithJson(root =>
        {
            V28Package.Node(root, "extract")["description"] = "Extracts.";
            V28Package.Node(root, "sections")["description"] = " ";
        }));
        Assert.Contains("Node 'extract' declares description; only a for, if or case node carries one — a node's prompt file says what it does.", errors);
        Assert.Contains("Node 'sections' description must not be empty.", errors);

        var long_ = V28Package.Errors(V28Package.WithJson(root => V28Package.Node(root, "sections")["description"] = new string('a', 501)));
        Assert.Contains("Node 'sections' description must be at most 500 characters.", long_);
    }

    // ---- what only 28 spells, below it ---------------------------------------------------------

    [Fact]
    public void BelowTwentyEight_TheListsAndADescription_RequireIt()
    {
        var (json, files) = V28Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        root["specVersion"] = 27;
        V28Package.Node(root, "sections")["description"] = "Drafts one note per section.";
        var manifest = WorkflowPackageManifestJson.Read(root.ToJsonString(), "test@v27", V27Package.Accepting27.ToList());

        var errors = V27Package.Errors((manifest, files));
        Assert.Contains("macros is a list of ids, which requires specVersion 28.", errors);
        Assert.Contains("snippets is a list of ids, which requires specVersion 28.", errors);
        Assert.Contains("Node 'sections' declares description, which requires specVersion 28.", errors);
        Assert.DoesNotContain(errors, e => e.StartsWith("macros is a list;", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDiagram_DrawsAFlowNodesDescription_UnderItsLabel_Escaped()
    {
        var bundle = V28Package.WithJson(root => V28Package.Node(root, "sections")["description"] = "Drafts one note\nper <section> \"here\".");
        var diagram = WorkflowDagDiagram.Generate(bundle.Manifest);

        Assert.Contains("sections<br/>Sections<br/><i>Drafts one note per &lt;section&gt; #quot;here#quot;.</i><br/>item in snippet.sections", diagram);
        Assert.DoesNotContain("<i>", WorkflowDagDiagram.Generate(V28Package.Minimal().Manifest));
    }

    // ---- the implied prompts ------------------------------------------------------------------

    [Fact]
    public void ThePrompts_AreImpliedFromTheFiles_NeverForAFlowNode_NeverBelow28()
    {
        var (manifest, files) = V28Package.Minimal();
        files["prompts/sections.md"] = "A flow node reads no prompt.";

        var implied = WorkflowImpliedPaths.WithImpliedPrompts(manifest, files);
        var prompts = implied.Nodes!.ToDictionary(n => n.Id, n => n.Prompt);
        Assert.Equal("prompts/extract.md", prompts["extract"]);
        Assert.Equal("prompts/section_notes.md", prompts["section_notes"]);
        Assert.Null(prompts["coverage"]);
        Assert.Null(prompts["sections"]);

        var at27 = manifest with { SpecVersion = 27 };
        Assert.Same(at27, WorkflowImpliedPaths.WithImpliedPrompts(at27, files));
    }
}
