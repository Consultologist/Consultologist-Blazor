using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1164, design § 12, § 13): the one-button upgrade to 26 — a 25 package is offered
/// "Upgrade to specVersion 26" alone, the click runs the library's rewrite and replaces the
/// session's content (the 26 panes render over it, one notice names every rewrite, publish is
/// open as "upgraded 25 → 26"), a refusal is said in the toolbar and applies nothing; and a
/// 26 package opens live — the #1209 interim gate, banner and seam are gone.
/// </summary>
public class TemplatesV26UpgradeTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
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

    private static IReadOnlyList<string> Groups(IRenderedComponent<Templates> page) =>
        page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()).ToList();

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

    /// <summary>The V25 fixture with section_notes fanned but aggregated by nothing — the 26 rewrite refuses it by name.</summary>
    private static WorkflowPackageContentResponse V25WithAnOrphanFan()
    {
        var fixture = EditorFixtures.V25();
        var json = fixture.Manifest.GetRawText().Replace("\"aggregate\": [\"node.assessment\", \"node.section_notes\"]", "\"aggregate\": [\"node.assessment\"]");
        Assert.NotEqual(fixture.Manifest.GetRawText(), json);
        return fixture with { Manifest = JsonDocument.Parse(json).RootElement.Clone() };
    }

    [Fact]
    public void A25Package_IsOfferedTheOneButtonTo26()
    {
        var page = RenderEditor();

        Assert.Equal(new[] { "Upgrade to specVersion 26" }, UpgradeButtons(page));
        Assert.Contains("Prompts", Groups(page));
        Assert.Equal(26, Templates.NewestSpecVersion);
    }

    [Fact]
    public async Task TheUpgrade_ReplacesThePackageAt26_SaysTheRewrites_AndPublishesValid()
    {
        CapturePublish();
        var page = RenderEditor();

        await UpgradeAsync(page);

        // The 26 panes over the migrated package: no Prompts group, the loop in the Nodes nav.
        Assert.DoesNotContain("Prompts", Groups(page));
        Assert.Contains("Sections", NavLabels(page));
        Assert.Empty(UpgradeButtons(page));

        var notice = page.Find(".migration-notice").TextContent;
        Assert.Contains("Migrated to specVersion 26.", notice);
        Assert.Contains("Loop 'sections' over sections returns 'section_notes' with body [section_notes].", notice);
        Assert.DoesNotContain("Through 25:", notice);
        Assert.Contains("macros is the map of id to file", notice);

        var publish = PublishButton(page);
        Assert.Contains("upgraded 25 → 26", publish.TextContent);
        Assert.Null(publish.GetAttribute("disabled"));

        publish.Click();
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var manifest = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;
        Assert.Equal(26, manifest.GetProperty("specVersion").GetInt32());
        Assert.False(manifest.TryGetProperty("prompts", out _));
        Assert.StartsWith("{\n  \"label\": \"Closing\"", sent.Files["macros/closing.md"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public async Task ARefusalAt26_IsSaidInTheToolbar_AndNothingApplies()
    {
        var page = RenderEditor(V25WithAnOrphanFan());

        await UpgradeAsync(page);

        Assert.Contains("At specVersion 26: Node 'section_notes' fans 'sections' but no aggregator sources it or a node that reads it; aggregate it or remove it, then upgrade.", page.Find(".upgrade-refusal").TextContent);
        Assert.Empty(page.FindAll(".migration-notice"));
        Assert.Contains("Prompts", Groups(page));
        Assert.Equal(new[] { "Upgrade to specVersion 26" }, UpgradeButtons(page));
        Assert.DoesNotContain("●", page.Markup);
        Assert.NotNull(PublishButton(page).GetAttribute("disabled"));
    }

    [Fact]
    public async Task AReload_RestoresTheUpgradeTo26()
    {
        var page = RenderEditor();
        await UpgradeAsync(page);

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-upgrade:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        Assert.Contains("\"Version\":2", saved.Json);
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();

        Assert.DoesNotContain("Prompts", Groups(again));
        Assert.Contains("Migrated to specVersion 26.", again.Find(".migration-notice").TextContent);
        Assert.Contains("upgraded 25 → 26", PublishButton(again).TextContent);
    }

    // ---- a 26 package opens live (the #1209 interim retired) ----------------------------

    [Fact]
    public void A26Package_OpensLive_NoBanner_NoInert_NoUpgrade_AndPublishOpensOnAnEdit()
    {
        var page = RenderEditor(EditorFixtures.V26());

        Assert.Empty(page.FindAll(".read-only-notice"));
        Assert.False(page.Find("section.editor-pane").HasAttribute("inert"));
        Assert.Empty(UpgradeButtons(page));
        Assert.NotNull(PublishButton(page).GetAttribute("disabled"));
        Assert.Null(page.Find("#editor-import-trigger").GetAttribute("disabled"));

        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "snippets/standards.md";
        page.Render();
        page.Find(".pane-editor fluent-text-area").Change("Follow the local standards, briefly.");

        Assert.Null(PublishButton(page).GetAttribute("disabled"));
        Assert.Null(page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Undo").GetAttribute("disabled"));
        Assert.Contains("●", page.Markup);
    }
}
