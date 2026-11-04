using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1290: renaming a folder or a folder item in the editor — a snippet folder's loop and reads follow
/// and its directory moves (from the nav at 26, the folder pane at 28), a macro folder's loop-variable
/// reads follow, an item's index entry, file and the instance gate comparing its id follow while an
/// item of the same id in another folder is left alone, a pending label and order follow the folder,
/// a refusal is said, and undo takes it back.
/// </summary>
public class TemplatesRenameFolderItemTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26Gated());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private string SelectedKey => Services.GetRequiredService<WorkflowEditorSession>().SelectedKey!;

    private static void Commit(IRenderedComponent<Templates> page, string noun, string from, string to)
    {
        page.Find($"fluent-text-field[aria-label='New id for {noun} {from}']").Change(to);
        page.Find("fluent-button.text-rename__ok").Click();
    }

    private static string? RenameError(IRenderedComponent<Templates> page) =>
        page.FindAll(".text-rename .field-error").SingleOrDefault()?.TextContent.Trim();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.11.2", "acct-1234567890ab@v2026.11.2"),
                Array.Empty<string>()));

    private JsonElement Manifest => JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;

    private JsonElement NodeOf(string id) => Manifest.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == id);

    private PF.WorkflowPackageValidator.ValidationResult Validated() =>
        PF.WorkflowPackageValidator.Validate(
            PF.WorkflowPackageManifestJson.Read(sent!.Manifest.GetRawText(), "sent", PF.WorkflowPackageValidator.AcceptedSpecVersions),
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);

    [Fact]
    public void At26_ASnippetFolder_IsRenamedFromTheNav_ItsLoopAndReadsFollow_AndItsDirectoryMoves()
    {
        CapturePublish();
        var page = RenderEditor();

        page.Find("button[aria-label='Rename folder sections']").Click();
        Commit(page, "snippet folder", "sections", "chapters");

        Assert.Null(RenameError(page));
        Publish(page);
        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        Assert.Equal("snippets/chapters/", Manifest.GetProperty("snippets").GetProperty("chapters").GetString());
        Assert.Equal("item in snippet.chapters", NodeOf("sections").GetProperty("for").GetString());
        Assert.Contains("for s in snippet.chapters", sent.Files["prompts/assessment.md"]);
        Assert.Contains("snippets/chapters/index.json", sent.Files.Keys);
        Assert.DoesNotContain(sent.Files.Keys, k => k.StartsWith("snippets/sections/", StringComparison.Ordinal));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void At26_AMacroFolder_IsRenamed_ItsLoopVariableReadsFollow()
    {
        CapturePublish();
        var page = RenderEditor();

        page.Find("button[aria-label='Rename folder notes']").Click();
        Commit(page, "macro folder", "notes", "notes_kept");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("macros/notes_kept/", Manifest.GetProperty("macros").GetProperty("notes_kept").GetString());
        Assert.Contains("for m in macro.notes_kept", sent.Files["prompts/assessment.md"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void At26_AnItem_IsRenamed_ItsIndexFileAndGateFollow_AndTheSameIdElsewhereIsLeftAlone()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "snippets/sections/hpi.md");

        page.Find("fluent-button[aria-label='Rename item hpi']").Click();
        Commit(page, "item", "hpi", "history");

        Assert.Equal("snippets/sections/history.md", SelectedKey);
        Publish(page);
        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        var index = JsonDocument.Parse(sent!.Files["snippets/sections/index.json"]).RootElement.EnumerateArray().Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Equal(new[] { "history", "plan" }, index);
        Assert.True(sent.Files.ContainsKey("snippets/sections/history.md"));
        // The migrated forItem gate on the loop body's instance compares the item's id.
        Assert.Equal("item.id == \"history\"", NodeOf("section_notes").GetProperty("macros")[0].GetProperty("if").GetString());
        // The macro folder's item of the same id is another item.
        Assert.Contains("hpi", sent.Files["macros/notes/index.json"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void At28_ASnippetFolder_IsRenamedFromItsPane_ItsPendingLabelAndOrderFollow()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V28());
        Select(page, "folder:snippets/sections/");
        page.Find("fluent-text-field[aria-label='Label of folder sections']").Change("Sections");
        page.Find("fluent-button[aria-label='Move item Plan up']").Click();

        page.Find("fluent-button[aria-label='Rename snippet folder sections']").Click();
        Commit(page, "snippet folder", "sections", "chapters");

        Assert.Equal("folder:snippets/chapters/", SelectedKey);
        Publish(page);
        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        Assert.Contains("chapters/", Manifest.GetProperty("snippets").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("{\n  \"label\": \"Sections\",\n  \"items\": [\n    \"plan\",\n    \"hpi\"\n  ]\n}\n", sent.Files["snippets/chapters/index.json"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void ARename_OntoAnItemTheFolderHas_IsRefused()
    {
        var page = RenderEditor();
        Select(page, "snippets/sections/hpi.md");
        page.Find("fluent-button[aria-label='Rename item hpi']").Click();
        Commit(page, "item", "hpi", "plan");

        Assert.Equal("Folder 'sections' already has an item 'plan'.", RenameError(page));
        Assert.Equal("snippets/sections/hpi.md", SelectedKey);
    }

    [Fact]
    public void Undo_TakesAFolderRenameBack()
    {
        var page = RenderEditor();
        page.Find("button[aria-label='Rename folder sections']").Click();
        Commit(page, "snippet folder", "sections", "chapters");

        page.Find(".undo-button").Click();

        Assert.NotNull(page.Find("button[aria-label='Rename folder sections']"));
        Assert.Empty(page.FindAll("[data-text-renamed]"));
    }
}
