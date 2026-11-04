using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1290: a macro, a snippet, a folder or a folder item renamed by token — every read spelling, the
/// look-alikes left alone (a macro and a snippet of one id, an item id two folders share, a node),
/// every manifest site in the 26/27 map and the 28 list, the index in both shapes, the file or the
/// directory moved to the path the new id implies, the refusals, and the renamed minimal still valid.
/// </summary>
public class WorkflowTextRenameTests
{
    private static readonly IReadOnlySet<string> NoLoops = new HashSet<string>();

    private static string Template(string text, WorkflowRenameKind kind, string from, string to, string? folder = null, IReadOnlySet<string>? itemRoots = null, IReadOnlySet<string>? loops = null) =>
        WorkflowTextRename.RenameInTemplate(text, kind, from, to, folder, itemRoots, loops).Text;

    // ---- the template spellings --------------------------------------------------------

    [Fact]
    public void AMacrosReads_AreRenamed_ASnippetOrNodeOfTheSameId_IsNot()
    {
        var text = "{{ macro.closing.prompt }} {{ macro.closing.content }} {{ snippet.closing.content }} {{ node.closing.content }} closing";
        Assert.Equal("{{ macro.sign_off.prompt }} {{ macro.sign_off.content }} {{ snippet.closing.content }} {{ node.closing.content }} closing",
            Template(text, WorkflowRenameKind.Macro, "closing", "sign_off"));
    }

    [Fact]
    public void ASnippetsReads_AreRenamed_AMacroOfTheSameId_IsNot()
    {
        Assert.Equal("{{ snippet.house.content }} {{ macro.standards.content }}",
            Template("{{ snippet.standards.content }} {{ macro.standards.content }}", WorkflowRenameKind.Snippet, "standards", "house"));
    }

    [Fact]
    public void AFoldersReads_Iterated_ReadDirectly_AndPiped_AreRenamed()
    {
        var text = "{{ for s in snippet.sections }}{{ s.label }}{{ end }} {{ snippet.sections.hpi.content }} {{ snippet.sections | array.size }}";
        Assert.Equal("{{ for s in snippet.chapters }}{{ s.label }}{{ end }} {{ snippet.chapters.hpi.content }} {{ snippet.chapters | array.size }}",
            Template(text, WorkflowRenameKind.SnippetFolder, "sections", "chapters"));
        Assert.Equal("{{ for m in macro.chapters }}{{ m.prompt }}{{ end }}",
            Template("{{ for m in macro.notes }}{{ m.prompt }}{{ end }}", WorkflowRenameKind.MacroFolder, "notes", "chapters"));
    }

    [Fact]
    public void AnItemsDirectRead_IsRenamed_InItsFolderAlone()
    {
        var text = "{{ snippet.sections.hpi.content }} {{ snippet.other.hpi.content }} {{ macro.sections.hpi.content }}";
        Assert.Equal("{{ snippet.sections.history.content }} {{ snippet.other.hpi.content }} {{ macro.sections.hpi.content }}",
            Template(text, WorkflowRenameKind.SnippetFolderItem, "hpi", "history", "sections"));
    }

    [Fact]
    public void AnItemIdLiteral_IsRenamed_OnlyWhereTheItemRangesOverTheFolder()
    {
        // for s in snippet.<folder>: s is an item of the folder; another folder's loop is left alone.
        Assert.Equal("{{ for s in snippet.sections }}{{ if s.id == \"history\" }}x{{ end }}{{ end }}{{ for o in snippet.other }}{{ if o.id == \"hpi\" }}y{{ end }}{{ end }}",
            Template("{{ for s in snippet.sections }}{{ if s.id == \"hpi\" }}x{{ end }}{{ end }}{{ for o in snippet.other }}{{ if o.id == \"hpi\" }}y{{ end }}{{ end }}",
                WorkflowRenameKind.SnippetFolderItem, "hpi", "history", "sections"));

        // item ranges over the folder only where the caller says so (a loop body's prompt, its gates).
        Assert.Equal("{{ if item.id == 'history' || \"plan\" == item.id }}x{{ end }}", Template("{{ if item.id == 'hpi' || \"plan\" == item.id }}x{{ end }}",
            WorkflowRenameKind.SnippetFolderItem, "hpi", "history", "sections", new HashSet<string> { "item" }));
        Assert.Equal("{{ if item.id == \"hpi\" }}x{{ end }}", Template("{{ if item.id == \"hpi\" }}x{{ end }}", WorkflowRenameKind.SnippetFolderItem, "hpi", "history", "sections"));

        // A loop element's item, and an indexed one, for a loop over the folder.
        var loops = new HashSet<string> { "notes_loop" };
        Assert.Equal("{{ for e in node.notes_loop }}{{ if e.item.id != \"history\" }}{{ e.content }}{{ end }}{{ end }} {{ node.notes_loop[0].item.id == \"history\" }}",
            Template("{{ for e in node.notes_loop }}{{ if e.item.id != \"hpi\" }}{{ e.content }}{{ end }}{{ end }} {{ node.notes_loop[0].item.id == \"hpi\" }}",
                WorkflowRenameKind.SnippetFolderItem, "hpi", "history", "sections", loops: loops));

        // A string that is not compared with an item id is prose.
        Assert.Equal("{{ \"hpi\" }} {{ input.kind == \"hpi\" }}", Template("{{ \"hpi\" }} {{ input.kind == \"hpi\" }}",
            WorkflowRenameKind.SnippetFolderItem, "hpi", "history", "sections", new HashSet<string> { "item" }));
    }

    [Fact]
    public void AnUnparsedTemplate_IsLeftAlone()
    {
        var (text, changed, parsed) = WorkflowTextRename.RenameInTemplate("{{ macro.closing.prompt ", WorkflowRenameKind.Macro, "closing", "sign_off");
        Assert.Equal(("{{ macro.closing.prompt ", false, false), (text, changed, parsed));
    }

    // ---- the package at 26 ------------------------------------------------------------

    private static (string Json, Dictionary<string, string> Files) Package26(Action<JsonObject>? reshape = null, Action<Dictionary<string, string>>? files = null)
    {
        var (manifest, bundleFiles) = V26Package.Minimal();
        var root = JsonNode.Parse(JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions))!.AsObject();
        reshape?.Invoke(root);
        var copy = new Dictionary<string, string>(bundleFiles, StringComparer.Ordinal);
        files?.Invoke(copy);
        return (root.ToJsonString(), copy);
    }

    private static WorkflowPackageValidator.ValidationResult Validate26(Migration26Result result) =>
        V26Package.Validate((WorkflowPackageManifestJson.Read(result.ManifestJson, "renamed", V26Package.Accepting26.ToList()), result.Files));

    private static JsonObject Root(Migration26Result result) => JsonNode.Parse(result.ManifestJson)!.AsObject();

    private static JsonObject NodeOf(JsonObject root, string id) =>
        root["nodes"]!.AsArray().Select(n => n!.AsObject()).Single(n => (string?)n["id"] == id);

    [Fact]
    public void At26_AMacroRename_RewritesTheMap_TheInstance_AndMovesTheFile()
    {
        var (json, files) = Package26();
        var result = WorkflowTextRename.Rename(json, files, WorkflowRenameKind.Macro, "closing", "sign_off");

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var root = Root(result);
        Assert.Equal("macros/sign_off.md", (string?)root["macros"]!["sign_off"]);
        Assert.Null(root["macros"]!["closing"]);
        Assert.Equal("sign_off", (string?)NodeOf(root, "assessment")["macros"]![0]!["id"]);
        Assert.True(result.Files.ContainsKey("macros/sign_off.md"));
        Assert.False(result.Files.ContainsKey("macros/closing.md"));
        Assert.Equal(new[] { "Renamed macro 'closing' to 'sign_off'.", "macros/closing.md → macros/sign_off.md." }, result.Notices);
        Assert.Empty(Validate26(result).Errors);
    }

    [Fact]
    public void At26_ASnippetRename_RewritesItsReaders_AndMovesTheFile()
    {
        var (json, files) = Package26();
        var result = WorkflowTextRename.Rename(json, files, WorkflowRenameKind.Snippet, "standards", "house_standards");

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        Assert.Equal("snippets/house_standards.md", (string?)Root(result)["snippets"]!["house_standards"]);
        Assert.Contains("{{ snippet.house_standards.content }}", result.Files["prompts/assessment.md"]);
        Assert.True(result.Files.ContainsKey("snippets/house_standards.md"));
        Assert.Empty(Validate26(result).Errors);
    }

    [Fact]
    public void At26_ASnippetFolderRename_RewritesTheLoop_AndMovesTheDirectory()
    {
        var (json, files) = Package26(files: f => f["prompts/assessment.md"] += "\n{{ for s in snippet.sections }}{{ s.label }}{{ end }}");
        var result = WorkflowTextRename.Rename(json, files, WorkflowRenameKind.SnippetFolder, "sections", "chapters");

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var root = Root(result);
        Assert.Equal("snippets/chapters/", (string?)root["snippets"]!["chapters"]);
        Assert.Equal("item in snippet.chapters", (string?)NodeOf(root, "sections")["for"]);
        Assert.Contains("{{ for s in snippet.chapters }}", result.Files["prompts/assessment.md"]);
        Assert.Equal(new[] { "snippets/chapters/hpi.md", "snippets/chapters/index.json", "snippets/chapters/plan.md" },
            result.Files.Keys.Where(k => k.StartsWith("snippets/chapters/", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(result.Files.Keys, k => k.StartsWith("snippets/sections/", StringComparison.Ordinal));
        Assert.Contains("snippets/sections/ → snippets/chapters/ (3 files).", result.Notices);
        Assert.Empty(Validate26(result).Errors);
    }

    [Fact]
    public void At26_AnItemRename_RewritesTheIndex_TheGate_AndMovesTheFile_KeepingTheOrder()
    {
        // The closing macro placed on the loop's body node for the hpi item alone (the #1266 chip gate).
        var (json, files) = Package26(root => NodeOf(root, "section_notes")["macros"] = new JsonArray(new JsonObject { ["id"] = "closing", ["at"] = "after", ["if"] = "item.id == \"hpi\"" }));
        var result = WorkflowTextRename.Rename(json, files, WorkflowRenameKind.SnippetFolderItem, "hpi", "history", "sections");

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        var index = JsonNode.Parse(result.Files["snippets/sections/index.json"])!.AsArray();
        Assert.Equal(new[] { ("history", "history.md"), ("plan", "plan.md") }, index.Select(i => ((string)i!["id"]!, (string)i["file"]!)));
        Assert.Equal("item.id == \"history\"", (string?)NodeOf(Root(result), "section_notes")["macros"]![0]!["if"]);
        Assert.True(result.Files.ContainsKey("snippets/sections/history.md"));
        Assert.False(result.Files.ContainsKey("snippets/sections/hpi.md"));
        Assert.Empty(Validate26(result).Errors);
    }

    [Fact]
    public void At26_AMacroFolderRename_RewritesItsLoopVariableReads_AndMovesTheDirectory()
    {
        var (manifest, bundleFiles) = WorkflowV26MacroFolderTests.WithFolder();
        var json = JsonSerializer.Serialize(manifest, WorkflowPackageManifestJson.WriteOptions);
        var result = WorkflowTextRename.Rename(json, bundleFiles, WorkflowRenameKind.MacroFolder, "section_notes", "notes_kept");

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        Assert.Equal("macros/notes_kept/", (string?)Root(result)["macros"]!["notes_kept"]);
        Assert.Contains("{{ for m in macro.notes_kept }}", result.Files["prompts/assessment.md"]);
        // The node of the same id is not the folder.
        Assert.NotNull(NodeOf(Root(result), "section_notes"));
        Assert.True(result.Files.ContainsKey("macros/notes_kept/index.json"));
        Assert.Empty(Validate26(result).Errors);
    }

    // ---- the package at 28 ------------------------------------------------------------

    private static WorkflowPackageValidator.ValidationResult Validate28(Migration26Result result) =>
        V28Package.Validate((V28Package.Read(result.ManifestJson), result.Files));

    [Theory]
    [InlineData(WorkflowRenameKind.Macro, "closing", "sign_off", null, "macros/sign_off.md")]
    [InlineData(WorkflowRenameKind.Snippet, "standards", "house_standards", null, "snippets/house_standards.md")]
    [InlineData(WorkflowRenameKind.SnippetFolder, "sections", "chapters", null, "snippets/chapters/index.json")]
    [InlineData(WorkflowRenameKind.SnippetFolderItem, "plan", "care_plan", "sections", "snippets/sections/care_plan.md")]
    public void At28_EachKind_RenamesTheIdList_OrTheIndexObject_AndStaysValid(WorkflowRenameKind kind, string from, string to, string? folder, string movedTo)
    {
        var (json, files) = V28Package.MinimalJson();
        var result = WorkflowTextRename.Rename(json, files, kind, from, to, folder);

        Assert.True(result.Succeeded, string.Join(" | ", result.Refusals));
        Assert.True(result.Files.ContainsKey(movedTo), movedTo);
        var root = Root(result);
        Assert.All(root["nodes"]!.AsArray(), n => Assert.Null(n!["prompt"]));
        if (kind == WorkflowRenameKind.SnippetFolderItem)
        {
            Assert.Equal("{\n  \"items\": [\n    \"hpi\",\n    \"care_plan\"\n  ]\n}\n", result.Files["snippets/sections/index.json"]);
        }
        else
        {
            var table = (kind == WorkflowRenameKind.Macro ? root["macros"] : root["snippets"])!.AsArray().Select(e => (string)e!).ToList();
            Assert.Contains(kind == WorkflowRenameKind.SnippetFolder ? to + "/" : to, table);
            Assert.DoesNotContain(kind == WorkflowRenameKind.SnippetFolder ? from + "/" : from, table);
        }

        var validated = Validate28(result);
        Assert.Empty(validated.Errors);
        Assert.Empty(validated.Warnings);
    }

    // ---- the refusals ------------------------------------------------------------------

    [Theory]
    [InlineData(WorkflowRenameKind.Macro, "closing", "Sign-Off", null, "'Sign-Off' is not a valid id — a lowercase letter, then lowercase letters, digits or underscores (a template reads it as a Scriban member).")]
    [InlineData(WorkflowRenameKind.Macro, "farewell", "sign_off", null, "There is no macro 'farewell'.")]
    [InlineData(WorkflowRenameKind.Snippet, "standards", "sections", null, "A snippet or snippet folder 'sections' already exists.")]
    [InlineData(WorkflowRenameKind.SnippetFolderItem, "hpi", "plan", "sections", "Folder 'sections' already has an item 'plan'.")]
    [InlineData(WorkflowRenameKind.SnippetFolderItem, "hpi", "history", "chapters", "There is no snippet folder 'chapters'.")]
    [InlineData(WorkflowRenameKind.Macro, "closing", "closing", null, "The macro is already called 'closing'.")]
    public void ARename_IsRefused_NamingWhy_AndNothingApplies(WorkflowRenameKind kind, string from, string to, string? folder, string refusal)
    {
        var (json, files) = Package26();
        var result = WorkflowTextRename.Rename(json, files, kind, from, to, folder);

        Assert.Equal(new[] { refusal }, result.Refusals);
        Assert.Equal(json, result.ManifestJson);
        Assert.Equal(files, result.Files);
    }

    [Fact]
    public void ARename_OntoAFileThatExists_IsRefused()
    {
        var (json, files) = Package26(files: f => f["macros/sign_off.md"] = "Already here.");
        var result = WorkflowTextRename.Rename(json, files, WorkflowRenameKind.Macro, "closing", "sign_off");
        Assert.Equal(new[] { "A file 'macros/sign_off.md' already exists; rename or remove it first." }, result.Refusals);
    }
}
