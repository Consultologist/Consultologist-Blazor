using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1267: at 26+ a pane header names a file only when it is not the path its id implies, and offers
/// "rename the file to match"; the Package pane lists every mismatch and brings them in step at once.
/// </summary>
public class TemplatesV26FileNamesTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private string SelectedKey => Services.GetRequiredService<WorkflowEditorSession>().SelectedKey!;

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private JsonElement Manifest => JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(sent!.Manifest.GetRawText(), PF.WorkflowPackageManifestJson.ReadOptions)!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    private static string? IdentityFile(IRenderedComponent<Templates> page) =>
        page.FindAll(".pane-identity__item").Select(i => i.TextContent.Trim()).FirstOrDefault(t => t.StartsWith("file ", StringComparison.Ordinal));

    /// <summary>V26 with a kebab-case folder item (hpi → history.md) and a mismatched macro (tool_note → macros/tool-note.md).</summary>
    private static WorkflowPackageContentResponse Mismatched()
    {
        var at26 = EditorFixtures.V26();
        var root = JsonNode.Parse(at26.Manifest.GetRawText())!.AsObject();
        root["macros"]!["tool_note"] = "macros/tool-note.md";
        var files = new Dictionary<string, string>(at26.Files, StringComparer.Ordinal);
        files["macros/tool-note.md"] = files["macros/tool_note.md"];
        files.Remove("macros/tool_note.md");
        files["snippets/sections/history.md"] = files["snippets/sections/hpi.md"];
        files.Remove("snippets/sections/hpi.md");
        files["snippets/sections/index.json"] = files["snippets/sections/index.json"].Replace("\"hpi.md\"", "\"history.md\"");
        return new(at26.Name, at26.Version, 26, JsonDocument.Parse(root.ToJsonString()).RootElement.Clone(), files);
    }

    [Fact]
    public void AFileNamedAfterItsId_IsNotNamedInTheHeader()
    {
        var page = RenderEditor();

        Select(page, "macros/closing.md");
        Assert.Null(IdentityFile(page));

        Select(page, "snippets/sections/hpi.md");
        Assert.Null(IdentityFile(page));
        Assert.Empty(page.FindAll(".file-match__button"));
    }

    [Fact]
    public void ANodesMismatchedPrompt_IsNamed_AndRenamedToMatch()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:section_notes");

        // The 25→26 migration kept prompts/section.md.
        Assert.Single(page.FindAll("[data-node-file='prompts/section.md']"));
        page.Find("fluent-button[aria-label='Rename prompts/section.md to prompts/section_notes.md']").Click();
        Assert.Empty(page.FindAll("[data-node-file]"));

        Publish(page);
        var node = Manifest.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == "section_notes");
        Assert.Equal("prompts/section_notes.md", node.GetProperty("prompt").GetString());
        Assert.True(sent!.Files.ContainsKey("prompts/section_notes.md"));
        Assert.False(sent.Files.ContainsKey("prompts/section.md"));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void AMismatchedMacro_IsRenamedToMatch_AndThePaneFollows()
    {
        CapturePublish();
        var page = RenderEditor(Mismatched());
        Select(page, "macros/tool-note.md");

        Assert.Equal("file macros/tool-note.md", IdentityFile(page));
        page.Find("fluent-button[aria-label='Rename macros/tool-note.md to macros/tool_note.md']").Click();

        Assert.Equal("macros/tool_note.md", SelectedKey);
        Assert.Null(IdentityFile(page));
        Publish(page);
        Assert.Equal("macros/tool_note.md", Manifest.GetProperty("macros").GetProperty("tool_note").GetString());
    }

    [Fact]
    public void AMismatchedFolderItem_IsRenamedToMatch_InItsIndex()
    {
        CapturePublish();
        var page = RenderEditor(Mismatched());
        Select(page, "snippets/sections/history.md");

        page.Find("fluent-button[aria-label='Rename snippets/sections/history.md to snippets/sections/hpi.md']").Click();
        Publish(page);

        var index = JsonNode.Parse(sent!.Files["snippets/sections/index.json"])!.AsArray();
        Assert.Equal(new[] { "hpi.md", "plan.md" }, index.Select(i => i!["file"]!.GetValue<string>()));
        Assert.True(sent.Files.ContainsKey("snippets/sections/hpi.md"));
    }

    [Fact]
    public void ThePackagePane_ListsEveryMismatch_BringsThemInStep_AndUndoReturnsThem()
    {
        CapturePublish();
        var page = RenderEditor(Mismatched());
        Select(page, "package");

        Assert.Equal("3", page.Find("[data-file-names]").GetAttribute("data-file-names"));
        page.Find("fluent-button.package-file-names__apply").Click();
        Select(page, "package");
        Assert.Equal("0", page.Find("[data-file-names]").GetAttribute("data-file-names"));
        Assert.Contains("Every file is named after its id.", page.Find("[data-file-names]").TextContent);

        Publish(page);
        Assert.Empty(Validated().Errors);

        page.Find(".undo-button").Click();
        Select(page, "package");
        Assert.Equal("3", page.Find("[data-file-names]").GetAttribute("data-file-names"));
    }

    [Fact]
    public void APendingEdit_FollowsTheMovedFile()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:section_notes");
        page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text")
            .QuerySelector("fluent-text-area")!.Change("{{ item.label }} — {{ item.content }}");

        page.Find("fluent-button[aria-label='Rename prompts/section.md to prompts/section_notes.md']").Click();
        Publish(page);

        Assert.Equal("{{ item.label }} — {{ item.content }}", sent!.Files["prompts/section_notes.md"]);
    }

    [Fact]
    public void AMove_OntoAnExistingFile_IsRefused_WithItsReason()
    {
        var at26 = EditorFixtures.V26();
        var files = new Dictionary<string, string>(at26.Files, StringComparer.Ordinal) { ["prompts/section_notes.md"] = "Something else." };
        var page = RenderEditor(new WorkflowPackageContentResponse(at26.Name, at26.Version, 26, at26.Manifest, files));
        Select(page, "node:section_notes");

        Assert.Empty(page.FindAll(".file-match__button"));
        Assert.Contains("A file 'prompts/section_notes.md' already exists.", page.Find("[data-file-refusal]").TextContent);
    }
}
