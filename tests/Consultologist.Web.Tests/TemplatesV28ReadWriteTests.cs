using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v28 (#1285, design § 7): the editor reads, writes and upgrades to 28 — built behind
/// a test-only seam, live since the bump (#1286), which retired it — the id lists and the index
/// object read, each node's prompt implied from its file, an edit published with no
/// path in the manifest, a 27 package upgraded through the library's migration, and
/// #1267's rename-to-match retired at 28.
/// </summary>
public class TemplatesV28ReadWriteTests : ClientRenderTestContext
{

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V28());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static IReadOnlyList<string> NavLabels(IRenderedComponent<Templates> page) =>
        page.FindAll("button.editor-nav__item").Select(b => b.TextContent.Replace("●", string.Empty).Trim()).ToList();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.11.1", "acct-1234567890ab@v2026.11.1"),
                Array.Empty<string>()));

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = PF.WorkflowPackageManifestJson.Read(sent!.Manifest.GetRawText(), "sent", PF.WorkflowPackageValidator.AcceptedSpecVersions);
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    private static string PromptText(IRenderedComponent<Templates> page) =>
        page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text")
            .QuerySelector("fluent-text-area")!.GetAttribute("current-value") ?? string.Empty;

    [Fact]
    public void A28Package_Opens_WithEveryPromptMacroSnippetAndFolderItem()
    {
        var fixture = EditorFixtures.V28();
        var page = RenderEditor(fixture);

        var nav = NavLabels(page);
        foreach (var label in new[] { "closing", "standards", "HPI", "Plan" })
        {
            Assert.Contains(nav, row => row.Contains(label, StringComparison.Ordinal));
        }

        // A 28 node names no prompt; its file is the one its id implies.
        Select(page, "node:assessment");
        Assert.Equal(fixture.Files["prompts/assessment.md"], PromptText(page));
        Select(page, "node:section_notes");
        Assert.Equal(fixture.Files["prompts/section_notes.md"], PromptText(page));
        Assert.Empty(Refusals(page));
    }

    [Fact]
    public void AnEdit_PublishesNoPath_TheIdLists_AndTheIndexObject_AndValidatesAt28()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:assessment");
        var area = page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text").QuerySelector("fluent-text-area")!;
        area.Change(area.GetAttribute("current-value") + "\nOne more line.");
        // #1285 (C2): at 28 the order is the folder pane's.
        Select(page, "folder:snippets/sections/");
        page.Find("fluent-button[aria-label='Move item Plan up']").Click();
        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        var manifest = sent!.Manifest;
        Assert.Equal(28, manifest.GetProperty("specVersion").GetInt32());
        Assert.All(manifest.GetProperty("nodes").EnumerateArray(), node => Assert.False(node.TryGetProperty("prompt", out _)));
        Assert.Equal(JsonValueKind.Array, manifest.GetProperty("macros").ValueKind);
        Assert.Equal(JsonValueKind.Array, manifest.GetProperty("snippets").ValueKind);
        Assert.Contains(manifest.GetProperty("snippets").EnumerateArray(), e => e.GetString() == "sections/");
        Assert.EndsWith("One more line.", sent.Files["prompts/assessment.md"]);
        Assert.Equal("{\n  \"items\": [\n    \"plan\",\n    \"hpi\"\n  ]\n}\n", sent.Files["snippets/sections/index.json"]);
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void AnAddedMacroFolder_IsAnIdListEntry_WithTheIndexObject()
    {
        CapturePublish();
        var page = RenderEditor();
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Replace("●", string.Empty).Trim() == "+ Macro folder").Click();
        page.Find(".new-item-fields fluent-text-field").Change("extras");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create macro folder")).Click();
        var fields = page.FindAll(".new-item-fields fluent-text-field");
        fields[0].Change("caveat");
        fields[1].Change("Caveat");
        page.Find(".pane-editor fluent-text-area").Change("A caveat.");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Add to")).Click();
        page.Find("fluent-text-area[aria-label='Instruction for macro item extras/caveat']").Change("Add the caveat.");
        Select(page, "node:assessment");
        var area = page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text").QuerySelector("fluent-text-area")!;
        area.Change(area.GetAttribute("current-value") + "\n{{ for m in macro.extras }}{{ m.content }}{{ end }}");
        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        var macros = sent!.Manifest.GetProperty("macros").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("extras/", macros);
        Assert.Contains("closing", macros);
        Assert.Equal("{\n  \"items\": [\n    \"caveat\"\n  ]\n}\n", sent.Files["macros/extras/index.json"]);
        Assert.True(sent.Files.ContainsKey("macros/extras/caveat.md"));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void A27Package_UpgradesTo28_ThroughTheMigration_AndPublishesClean()
    {
        CapturePublish();
        var at27 = PF.WorkflowPackageVersionRaise.To(EditorFixtures.V26Gated().Manifest.GetRawText(), EditorFixtures.V26Gated().Files, 27);
        var fixture = new WorkflowPackageContentResponse("acct-1234567890ab", "v2026.07.1", 27, JsonDocument.Parse(at27.ManifestJson).RootElement.Clone(), new Dictionary<string, string>(at27.Files, StringComparer.Ordinal));
        var page = RenderEditor(fixture);

        page.InvokeAsync(() => page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Upgrade to specVersion 30").Click());
        page.Render();

        var notice = page.Markup;
        Assert.Contains("Migrated to specVersion 30.", notice);
        Assert.Contains("prompts/section.md → prompts/section_notes.md", notice);
        Assert.DoesNotContain("Through 27:", notice);

        Publish(page);
        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        Assert.Equal(30, sent!.Manifest.GetProperty("specVersion").GetInt32());
        Assert.True(sent.Files.ContainsKey("prompts/section_notes.md"));
        Assert.False(sent.Files.ContainsKey("prompts/section.md"));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void A26Package_UpgradesTo28_SayingThe27RaiseUnderItsHeading()
    {
        var page = RenderEditor(EditorFixtures.V26Gated());
        page.InvokeAsync(() => page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Upgrade to specVersion 30").Click());
        page.Render();

        Assert.Contains("Migrated to specVersion 30.", page.Markup);
        Assert.Contains("Through 27: Raised to specVersion 27", page.Markup);
    }

    [Fact]
    public void RenameTheFileToMatch_AndTheFileNamesLine_AreRetiredAt28_AndStayAt27()
    {
        var at28 = RenderEditor();
        Select(at28, "package");
        Assert.Empty(at28.FindAll("[data-file-names]"));
        Assert.Empty(at28.FindAll(".file-match__button"));

        var raised = PF.WorkflowPackageVersionRaise.To(EditorFixtures.V26Gated().Manifest.GetRawText(), EditorFixtures.V26Gated().Files, 27);
        var at27 = RenderEditor(new WorkflowPackageContentResponse("acct-1234567890ab", "v2026.07.1", 27, JsonDocument.Parse(raised.ManifestJson).RootElement.Clone(), new Dictionary<string, string>(raised.Files, StringComparer.Ordinal)));
        Select(at27, "package");
        Assert.NotEmpty(at27.FindAll("[data-file-names]"));
    }

    [Fact]
    public void TheVersionAfter_31_IsRefusedAtPublish()
    {
        CapturePublish();
        var fixture = EditorFixtures.V28();
        var root = System.Text.Json.Nodes.JsonNode.Parse(fixture.Manifest.GetRawText())!.AsObject();
        root["specVersion"] = 31;
        var page = RenderEditor(fixture with { SpecVersion = 31, Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() });
        Assert.DoesNotContain(page.FindAll("fluent-button"), b => b.TextContent.Trim().StartsWith("Upgrade to specVersion", StringComparison.Ordinal));

        Select(page, "node:assessment");
        var area = page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text").QuerySelector("fluent-text-area")!;
        area.Change(area.GetAttribute("current-value") + "\nOne more line.");
        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("is specVersion 31; this engine runs specVersion", StringComparison.Ordinal));
    }
}
