using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C6 (#1012, design § 14): the editor's upgrade from 24 to 25 — the library's
/// rewrite applied the import door's way (the migrated package replaces the session's
/// content, is held in this browser, publishes as a new version of the same package,
/// and a discard reloads the registry's), with one notice naming every rewrite. The
/// toolbar still offers no 25 button (A5 moves the ceilings); the tests drive the
/// internal seam.
/// </summary>
public class TemplatesV25MigrationTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V24Full());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>();
    }

    private static async Task UpgradeTo25Async(IRenderedComponent<Templates> page)
    {
        await page.InvokeAsync(() => page.Instance.UpgradeSpecVersionTo(Templates.MigratableSpecVersion));
        page.Render();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static IReadOnlyList<string> Groups(IRenderedComponent<Templates> page) =>
        page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()).ToList();

    private static IReadOnlyList<string> UpgradeButtons(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").Select(b => b.TextContent.Trim()).Where(t => t.StartsWith("Upgrade to specVersion", StringComparison.Ordinal)).ToList();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

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
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog,
            EditorFormatsCatalog.AcceptedWith25);
    }

    [Fact]
    public void TheToolbar_OffersNoButtonTo25_WhileTheCeilingsHoldAt24()
    {
        var page = RenderEditor();

        Assert.DoesNotContain(UpgradeButtons(page), b => b.Contains("25"));
        Assert.Contains("Data", Groups(page));
        Assert.DoesNotContain("Snippets", Groups(page));
    }

    [Fact]
    public async Task TheSeam_ReplacesThePackageAt25_AndSaysEveryRewrite()
    {
        var page = RenderEditor();

        await UpgradeTo25Async(page);

        // The 25 panes render over the migrated package.
        var groups = Groups(page);
        Assert.Contains("Formats", groups);
        Assert.Contains("Snippets", groups);
        Assert.DoesNotContain("Data", groups);
        Assert.DoesNotContain("Schemas", groups);

        var notice = page.Find(".migration-notice").TextContent;
        Assert.Contains("Migrated to specVersion 25.", notice);
        Assert.Contains("Node 'identify-problem' renamed 'identify_problem'", notice);
        Assert.Contains("results is now deliverables", notice);
        Assert.Contains("data is now snippets", notice);
        Assert.Contains("Macro 'closing' needs an instruction before publishing", notice);

        // Said in the publish button, and publish is open.
        var publish = page.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish new version"));
        Assert.Contains("upgraded 24 → 25", publish.TextContent);
        Assert.Null(publish.GetAttribute("disabled"));
        // Still the same package, not an import.
        Assert.DoesNotContain("Imported", page.Markup);
    }

    [Fact]
    public async Task TheDesk_NamesTheExplicitInit_ThenTheWrittenPackagePublishesAccepted()
    {
        var page = RenderEditor();
        CapturePublish();
        await UpgradeTo25Async(page);

        Publish(page);
        Assert.Null(sent);
        var refusals = Refusals(page);
        foreach (var macro in new[] { "closing", "footer", "handoff" })
        {
            Assert.Contains($"Macro '{macro}' must declare an instruction at specVersion 25; placed text tells the model when to emit its marker.", refusals);
        }

        foreach (var macro in new[] { "closing", "footer", "handoff" })
        {
            Navigate(page, macro);
            page.Find($"fluent-text-area[aria-label='Instruction for macro {macro}']").Change("Say when it applies.");
        }

        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var manifest = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement;
        Assert.Equal(25, manifest.GetProperty("specVersion").GetInt32());
        foreach (var key in new[] { "data", "results", "schemas", "schemaRefs", "customSchemas" })
        {
            Assert.False(manifest.TryGetProperty(key, out _), key);
        }

        Assert.Contains("snippets/house_standards.md", sent.Files.Keys);
        Assert.Contains("formats/plan_shape.json", sent.Files.Keys);
        Assert.Equal("Extract the concepts from:\n{{ input.notes }}", sent.Files["prompts/extract.md"]);
        Assert.False(sent.FromUpload);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public async Task APendingEdit_RidesIntoTheMigratedPackage()
    {
        var page = RenderEditor();
        Navigate(page, "extract");
        page.Find(".editor-pane fluent-text-area").Change("Extract EVERY concept from:\n{{ notes }}");

        await UpgradeTo25Async(page);

        Navigate(page, "extract");
        Assert.Equal("Extract EVERY concept from:\n{{ input.notes }}", page.Find(".editor-pane fluent-text-area").GetAttribute("current-value"));
    }

    [Fact]
    public async Task ARefusal_StagesNothing_AndIsSaidInTheToolbar()
    {
        var package = EditorFixtures.V24Full();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        var raw = root["nodes"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>().Single(n => n["id"]!.GetValue<string>() == "raw_node");
        raw["prompt"] = "extract";
        raw["bindings"] = new System.Text.Json.Nodes.JsonObject { ["notes"] = "input:kind" };
        var page = RenderEditor(package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() });

        await UpgradeTo25Async(page);

        Assert.Contains("Prompt 'extract' binds 'notes' differently on nodes", page.Find(".upgrade-refusal").TextContent);
        Assert.Contains("Data", Groups(page));
        Assert.Empty(page.FindAll(".migration-notice"));
    }

    [Fact]
    public async Task Discard_ReturnsThePublished24Package()
    {
        var page = RenderEditor();
        await UpgradeTo25Async(page);
        Assert.Contains("Snippets", Groups(page));

        var discard = page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Discard");
        discard.Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();

        Assert.Contains("Data", Groups(page));
        Assert.DoesNotContain("Snippets", Groups(page));
        Assert.Empty(page.FindAll(".migration-notice"));
        Assert.Contains("Discarded the upgrade from specVersion 24", page.Markup);
    }

    [Fact]
    public async Task AReload_RestoresTheUpgrade_AndItsNotice()
    {
        var page = RenderEditor();
        await UpgradeTo25Async(page);

        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-upgrade:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);

        var again = Render<Templates>();

        Assert.Contains("Snippets", Groups(again));
        Assert.Contains("Migrated to specVersion 25.", again.Find(".migration-notice").TextContent);
        Assert.Contains("upgraded 24 → 25", again.FindAll("fluent-button").First(b => b.TextContent.Contains("Publish new version")).TextContent);
    }
}
