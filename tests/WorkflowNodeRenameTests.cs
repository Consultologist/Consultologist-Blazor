using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1225: a node id renamed by token — every spelling that names the node as a node, and nothing
/// else (look-alike ids, prose, string literals, another loop's members).
/// </summary>
public class WorkflowNodeRenameTests
{
    private static readonly IReadOnlySet<string> NoLoops = new HashSet<string>();
    private static readonly IReadOnlySet<string> InSections = new HashSet<string> { "sections" };

    private static string Rename(string text, IReadOnlySet<string>? loops = null) =>
        WorkflowNodeRename.RenameInTemplate(text, "draft", "first_draft", loops ?? NoLoops).Text;

    [Theory]
    [InlineData("{{ node.draft.content }}", "{{ node.first_draft.content }}")]
    [InlineData("{{ node.draft.value.title }}", "{{ node.first_draft.value.title }}")]
    [InlineData("{{ node[\"draft\"].content }}", "{{ node[\"first_draft\"].content }}")]
    [InlineData("{{ node.draft | concept_context }}", "{{ node.first_draft | concept_context }}")]
    [InlineData("{{ if node.draft.value == \"x\" }}y{{ end }}", "{{ if node.first_draft.value == \"x\" }}y{{ end }}")]
    [InlineData("A {{ node.draft.content }} and {{ node.draft.value }}.", "A {{ node.first_draft.content }} and {{ node.first_draft.value }}.")]
    public void EverySpellingOfTheNode_IsRenamed(string before, string after) =>
        Assert.Equal(after, Rename(before));

    [Theory]
    [InlineData("{{ node.draft_two.content }}")]
    [InlineData("The draft node.draft reads here.")]
    [InlineData("{{ \"node.draft\" }}")]
    [InlineData("{{ input.draft }}")]
    [InlineData("{{ snippet.draft.content }}")]
    public void LookAlikes_AreLeftAlone(string text) =>
        Assert.Equal(text, Rename(text));

    [Fact]
    public void ALoopElementsRead_IsRenamed_OnlyForALoopHoldingTheNode()
    {
        const string text = "{{ for e in node.sections }}{{ e.draft.content }}{{ end }}";
        Assert.Equal("{{ for e in node.sections }}{{ e.first_draft.content }}{{ end }}", Rename(text, InSections));

        // Another loop's element: its member is not this node.
        const string other = "{{ for e in node.others }}{{ e.draft.content }}{{ end }}";
        Assert.Equal(other, Rename(other, InSections));
    }

    [Fact]
    public void AnIndexedLoopRead_IsRenamed() =>
        Assert.Equal("{{ node.sections[0].first_draft.content }}", Rename("{{ node.sections[0].draft.content }}", InSections));

    [Fact]
    public void AText_ThatDoesNotParse_IsReturnedUnchanged()
    {
        var (text, changed, parsed) = WorkflowNodeRename.RenameInTemplate("{{ node.draft.content ", "draft", "first_draft", NoLoops);
        Assert.Equal("{{ node.draft.content ", text);
        Assert.False(changed);
        Assert.False(parsed);
    }

    [Fact]
    public void AGate_IsRenamed_AsABareExpression()
    {
        Assert.Equal(("node.first_draft.value == \"x\" || input.kind == \"new\"", true),
            WorkflowNodeRename.RenameInCondition("node.draft.value == \"x\" || input.kind == \"new\"", "draft", "first_draft", NoLoops));
        Assert.Equal(("input.kind == \"new\"", false), WorkflowNodeRename.RenameInCondition("input.kind == \"new\"", "draft", "first_draft", NoLoops));
    }

    [Fact]
    public void AFilesHeaderIf_AndBody_AreRenamed()
    {
        var file = WorkflowTextHeaders.Compose(new WorkflowTextHeader("Closing", "node.draft.value == \"yes\"")) + "See {{ node.draft.content }}.";
        var (text, changed) = WorkflowNodeRename.RenameInFile(file, "draft", "first_draft", NoLoops);

        Assert.True(changed);
        Assert.Contains("node.first_draft.value", text);
        Assert.EndsWith("See {{ node.first_draft.content }}.", text);
        Assert.DoesNotContain("node.draft", text);
    }

    [Fact]
    public void EveryManifestSite_IsRenamed()
    {
        var manifest = JsonNode.Parse("""
            {
              "nodes": [
                { "id": "draft", "prompt": "prompts/draft.md", "format": "prose",
                  "macros": [ { "id": "m", "at": "after", "if": "node.draft.value == \"x\"" } ] },
                { "id": "sections", "for": "item in snippet.sections", "nodes": ["route"], "ret": "node.route" },
                { "id": "route", "case": "node.draft.value", "branches": [ { "when": ["a"], "nodes": ["draft2"], "ret": "node.draft2" } ] },
                { "id": "gate", "if": "node.draft.value == \"x\"", "then": { "nodes": ["draft"], "ret": "node.draft" } },
                { "id": "coverage", "format": "terms_subset", "with": { "of": "node.draft", "in": "node.draft" } }
              ],
              "deliverables": [
                { "id": "note", "sources": ["node.draft", "node.sections"], "check": "node.draft", "if": "node.draft.value == \"x\"",
                  "macros": [ { "id": "m", "at": "append", "if": "node.draft.value == \"x\"" } ] }
              ]
            }
            """)!.AsObject();

        Assert.True(WorkflowNodeRename.RenameInManifest(manifest, "draft", "first_draft", "prompts/first_draft.md"));

        var json = manifest.ToJsonString();
        Assert.DoesNotContain("node.draft\"", json);
        Assert.DoesNotContain("node.draft.", json);
        Assert.DoesNotContain("\"draft\"", json);
        Assert.Contains("\"id\":\"first_draft\"", json);
        Assert.Contains("\"prompt\":\"prompts/first_draft.md\"", json);
        // draft2 is another node and keeps its id.
        Assert.Contains("node.draft2", json);
    }

    [Fact]
    public void ARenamedMinimal_StillValidates()
    {
        var (manifest, files) = V27Package.Minimal();
        var root = JsonNode.Parse(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions))!.AsObject();
        var target = root["nodes"]!.AsArray().OfType<JsonObject>().First(n => n["prompt"] is not null);
        var oldId = target["id"]!.GetValue<string>();
        var oldPrompt = target["prompt"]!.GetValue<string>();
        const string newId = "renamed_node";
        var loops = WorkflowNodeRename.LoopsHolding(root, oldId);

        Assert.True(WorkflowNodeRename.RenameInManifest(root, oldId, newId, "prompts/renamed_node.md"));
        var renamedFiles = files.ToDictionary(
            f => f.Key == oldPrompt ? "prompts/renamed_node.md" : f.Key,
            f => WorkflowNodeRename.RenameInFile(f.Value, oldId, newId, loops).Text,
            StringComparer.Ordinal);

        var renamed = WorkflowPackageManifestJson.Read(root.ToJsonString(), "renamed", WorkflowPackageValidator.AcceptedSpecVersions);
        Assert.Empty(WorkflowPackageValidator.Validate(renamed, renamedFiles, TestOutputContracts.CatalogSchemas, null, V25Package.Catalog()).Errors);
        Assert.DoesNotContain(renamed.Nodes, n => n.Id == oldId);
    }
}
