using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v27 (#1237, design § 7, § 9), moved by v28 (#1286): the one-button upgrade to the newest —
/// a 26 package is offered "Upgrade to specVersion 28" alone; the click raises it to 27 (said
/// under "Through 27:") and the 28 rewrite renames every file to its id; publish is open as
/// "upgraded 26 → 28" and the sent package validates at 28; a reload restores it; and a 28
/// package opens live with no upgrade offered — the #1285 seam is gone. The raise and the
/// rewrite are the library's (WorkflowPackageVersionRaiseTests, WorkflowPackageMigration27To28Tests).
/// </summary>
public class TemplatesV27UpgradeTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>();
    }

    private static async Task UpgradeAsync(IRenderedComponent<Templates> page)
    {
        await page.InvokeAsync(() => page.FindAll("fluent-button")
            .First(button => button.TextContent.Trim() == $"Upgrade to specVersion {Templates.NewestSpecVersion}")
            .Click());
        page.Render();
    }

    private static IReadOnlyList<string> NavLabels(IRenderedComponent<Templates> page) =>
        page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()).ToList();

    private static IReadOnlyList<string> UpgradeButtons(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").Select(b => b.TextContent.Trim()).Where(t => t.StartsWith("Upgrade to specVersion", StringComparison.Ordinal)).ToList();

    private static AngleSharp.Dom.IElement PublishButton(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish new version"));

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

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

    [Fact]
    public void A26Package_IsOfferedTheOneButtonToTheNewest()
    {
        var page = RenderEditor();

        // #1286: the newest is 28 — one button, both ceilings together.
        Assert.Equal(new[] { "Upgrade to specVersion 28" }, UpgradeButtons(page));
        Assert.Equal(28, Templates.NewestSpecVersion);
        Assert.Equal(28, Templates.RunnableSpecVersion);
    }

    [Fact]
    public async Task TheChain_RaisesTo27_ThenRenamesEveryFileTo28_AndPublishesValid()
    {
        CapturePublish();
        var fixture = EditorFixtures.V26();
        var page = RenderEditor(fixture);
        var navBefore = NavLabels(page);

        await UpgradeAsync(page);

        Assert.Equal(navBefore, NavLabels(page));
        Assert.Empty(UpgradeButtons(page));
        var notice = System.Text.RegularExpressions.Regex.Replace(page.Find(".migration-notice").TextContent, @"\s+", " ").Trim();
        // #1286: the 27 raise is a rung, said under its heading; the 28 rewrite's notices end it.
        Assert.Contains("Migrated to specVersion 28. Through 27: Raised to specVersion 27; nothing rewritten — 27 permits what 26 refused.", notice);
        Assert.Contains("prompts/section.md → prompts/section_notes.md", notice);
        Assert.EndsWith("macros and snippets are lists of ids.", notice);

        var publish = PublishButton(page);
        Assert.Contains("upgraded 26 → 28", publish.TextContent);
        Assert.Null(publish.GetAttribute("disabled"));

        publish.Click();
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var manifest = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;
        Assert.Equal(28, manifest.GetProperty("specVersion").GetInt32());
        // Every file named after its id: the one mismatched prompt moved, nothing else lost.
        Assert.True(sent.Files.ContainsKey("prompts/section_notes.md"));
        Assert.False(sent.Files.ContainsKey("prompts/section.md"));
        Assert.Equal(fixture.Files.Count, sent.Files.Count);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public async Task AReload_RestoresTheRaise()
    {
        var page = RenderEditor();
        await UpgradeAsync(page);

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-upgrade:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        Assert.Contains("\"Version\":4", saved.Json);
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();

        Assert.Contains("Migrated to specVersion 28.", again.Find(".migration-notice").TextContent);
        Assert.Contains("upgraded 26 → 28", PublishButton(again).TextContent);
        Assert.Empty(UpgradeButtons(again));
    }

    [Fact]
    public void ANewestPackage_OpensLive_NoUpgrade_AndPublishOpensOnAnEdit()
    {
        // #1286: the newest is 28 — a 28 package is offered nothing.
        var page = RenderEditor(EditorFixtures.V28());

        Assert.Empty(page.FindAll(".read-only-notice"));
        Assert.False(page.Find("section.editor-pane").HasAttribute("inert"));
        Assert.Empty(UpgradeButtons(page));
        Assert.NotNull(PublishButton(page).GetAttribute("disabled"));

        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "snippets/standards.md";
        page.Render();
        page.Find(".pane-editor fluent-text-area").Change("Follow the local standards, briefly.");

        Assert.Null(PublishButton(page).GetAttribute("disabled"));
        Assert.Contains("●", page.Markup);
    }
}
