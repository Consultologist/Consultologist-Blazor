using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v28 (#1285, design § 3, § 7): a folder's own pane at 28 — opened from its nav row, it
/// holds the folder's display label (written to index.json's label, removed when cleared)
/// and the item order (the arrows and reset order, moved there from the nav). At 27 a
/// folder stays a nav row with its arrows.
/// </summary>
public class TemplatesV28FolderPaneTests : ClientRenderTestContext
{
    private const string Sections = "folder:snippets/sections/";
    private const string SectionsIndex = "snippets/sections/index.json";

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V28());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>();
    }

    private string? SelectedKey => Services.GetRequiredService<WorkflowEditorSession>().SelectedKey;

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> PaneOrder(IRenderedComponent<Templates> page) =>
        page.FindAll(".folder-pane__item").Select(li => li.GetAttribute("data-folder-item")!).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.11.1", "acct-1234567890ab@v2026.11.1"),
                Array.Empty<string>()));

    private PF.WorkflowPackageValidator.ValidationResult Validated() =>
        PF.WorkflowPackageValidator.Validate(
            PF.WorkflowPackageManifestJson.Read(sent!.Manifest.GetRawText(), "sent", PF.WorkflowPackageValidator.AcceptedSpecVersions),
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);

    private static IRenderedComponent<Templates> Open(IRenderedComponent<Templates> page)
    {
        page.Find("button.editor-nav__folder-select[aria-label='Open folder sections']").Click();
        return page;
    }

    [Fact]
    public void TheFoldersNavRow_OpensItsPane_WithTheLabelAndTheOrder()
    {
        var page = Open(RenderEditor());

        Assert.Equal(Sections, SelectedKey);
        Assert.NotNull(page.Find("[data-folder-pane='snippets/sections/']"));
        var label = page.Find("fluent-text-field[aria-label='Label of folder sections']");
        Assert.Equal(string.Empty, label.GetAttribute("value") ?? string.Empty);
        Assert.Equal("sections", label.GetAttribute("placeholder"));
        Assert.Equal(new[] { "hpi", "plan" }, PaneOrder(page));
        // The nav keeps its rows and "+ Add item"; the arrows are the pane's.
        Assert.Empty(page.FindAll(".editor-nav__move"));
    }

    [Fact]
    public void ALabel_PublishesIntoTheIndex_AndNamesTheFolderInTheNav()
    {
        CapturePublish();
        var page = Open(RenderEditor());
        page.Find("fluent-text-field[aria-label='Label of folder sections']").Change("Sections of the note");

        Assert.Contains("Sections of the note", page.Find("button.editor-nav__folder-select[aria-label='Open folder sections']").TextContent);
        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        Assert.Equal("{\n  \"label\": \"Sections of the note\",\n  \"items\": [\n    \"hpi\",\n    \"plan\"\n  ]\n}\n", sent!.Files[SectionsIndex]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void ClearingALoadedLabel_RemovesTheKey()
    {
        CapturePublish();
        var fixture = EditorFixtures.V28();
        var files = new Dictionary<string, string>(fixture.Files, StringComparer.Ordinal)
        {
            [SectionsIndex] = "{\n  \"label\": \"Sections\",\n  \"items\": [\n    \"hpi\",\n    \"plan\"\n  ]\n}\n",
        };
        var page = Open(RenderEditor(fixture with { Files = files }));
        Assert.Equal("Sections", page.Find("fluent-text-field[aria-label='Label of folder sections']").GetAttribute("value"));

        page.Find("fluent-text-field[aria-label='Label of folder sections']").Change("");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("{\n  \"items\": [\n    \"hpi\",\n    \"plan\"\n  ]\n}\n", sent!.Files[SectionsIndex]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void TheOrder_MovesInThePane_ResetsThere_AndPublishes()
    {
        CapturePublish();
        var page = Open(RenderEditor());
        page.Find("fluent-button[aria-label='Move item Plan up']").Click();
        Assert.Equal(new[] { "plan", "hpi" }, PaneOrder(page));

        page.Find("button[aria-label='Reset order of sections']").Click();
        Assert.Equal(new[] { "hpi", "plan" }, PaneOrder(page));

        page.Find("fluent-button[aria-label='Move item Plan up']").Click();
        Publish(page);
        Assert.NotNull(sent);
        Assert.Equal("{\n  \"items\": [\n    \"plan\",\n    \"hpi\"\n  ]\n}\n", sent!.Files[SectionsIndex]);
    }

    [Fact]
    public void TheLabel_SurvivesAReload_WithItsDraft()
    {
        var page = Open(RenderEditor());
        page.Find("fluent-text-field[aria-label='Label of folder sections']").Change("Sections of the note");

        // The draft the editor saved, read back as a reload reads it.
        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);
        var reloaded = Open(Render<Templates>());

        Assert.Equal("Sections of the note", reloaded.Find("fluent-text-field[aria-label='Label of folder sections']").GetAttribute("value"));
        Assert.Contains("1 folder label", reloaded.Markup);
    }

    [Fact]
    public void At27_AFolder_IsANavRow_WithItsArrows()
    {
        var raised = PF.WorkflowPackageVersionRaise.To(EditorFixtures.V26Gated().Manifest.GetRawText(), EditorFixtures.V26Gated().Files, 27);
        var page = RenderEditor(new WorkflowPackageContentResponse("acct-1234567890ab", "v2026.07.1", 27,
            JsonDocument.Parse(raised.ManifestJson).RootElement.Clone(), new Dictionary<string, string>(raised.Files, StringComparer.Ordinal)));

        Assert.Empty(page.FindAll("button.editor-nav__folder-select"));
        Assert.NotEmpty(page.FindAll(".editor-nav__move"));
    }
}
