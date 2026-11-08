using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1306 (package-format-v29-design.md § 8): the editor at 29 (behind a test-only
/// seam until the bump, #1307, retired it) — a loop's picker offers a node's array fields (a node
/// outside every loop, its format typing the field an array), the pick is written and
/// published, the body's palette offers the element's fields, and a 28 package
/// upgrades to 29 with only its version changing. Without the seam, nothing moves.
/// </summary>
public class TemplatesV29LoopTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static IReadOnlyList<string> Options(IRenderedComponent<Templates> page, string select) =>
        page.Find(select).QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value") ?? string.Empty).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.11.2", "acct-1234567890ab@v2026.11.2"),
                Array.Empty<string>()));

    private static readonly IReadOnlyList<int> Accepting29 = PF.WorkflowPackageValidator.AcceptedSpecVersions;

    [Fact]
    public void TheLoopPicker_OffersANodesArrayFields_AndNotAProseOrABodyNode()
    {
        var page = RenderEditor(EditorFixtures.V29());
        Select(page, "node:problems");

        var options = Options(page, "fluent-select.loop-collection");

        Assert.Contains("node.find_problems.value.problems", options);
        Assert.Contains("node.find_problems.value.risks", options);
        Assert.DoesNotContain("node.find_problems.value.summary", options);
        Assert.DoesNotContain(options, o => o.StartsWith("node.draft_problem", StringComparison.Ordinal) || o.StartsWith("node.assessment", StringComparison.Ordinal));
        Assert.Contains("snippet.sections", options);
        Assert.Equal("node.find_problems.value.problems", page.Find("fluent-select.loop-collection").GetAttribute("current-value"));
    }

    [Fact]
    public void APickedNodeList_IsWritten_AndPublishesValidAt29()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V29());
        Select(page, "node:problems");

        page.Find("fluent-select.loop-collection").Change("node.find_problems.value.risks");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

        Assert.True(sent is not null, string.Join(" | ", page.FindAll(".fluent-messagebar-message li, .desk-error, .field-error").Select(e => e.TextContent.Trim())));
        var problems = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == "problems");
        Assert.Equal("item in node.find_problems.value.risks", problems.GetProperty("for").GetString());
        var manifest = PF.WorkflowPackageManifestJson.Read(sent.Manifest.GetRawText(), "sent", Accepting29);
        var result = PF.WorkflowPackageValidator.Validate(manifest, sent.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal), null, EditorFormatsCatalog.Catalog);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void TheBodysPalette_OffersTheElementsFields()
    {
        var page = RenderEditor(EditorFixtures.V29());
        Select(page, "node:draft_problem");

        var offered = Options(page, "fluent-select.insert-reference__pick");

        Assert.Contains(offered, v => v.Contains("item.value.name", StringComparison.Ordinal));
        Assert.Contains(offered, v => v.Contains("item.value.severity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A28Package_UpgradesTo29_WithOnlyItsVersionChanging()
    {
        var page = RenderEditor(EditorFixtures.V28());

        await page.InvokeAsync(() => page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Upgrade to specVersion 29").Click());
        page.Render();

        var notice = System.Text.RegularExpressions.Regex.Replace(page.Find(".migration-notice").TextContent, @"\s+", " ").Trim();
        Assert.Contains("Migrated to specVersion 29.", notice);
        Assert.Contains("Through 29: nothing changes in this package; loops may now iterate a node's value.", notice);
        Assert.DoesNotContain("Through 28", notice);
    }

    [Fact]
    public void At28_NoNodeListsAreOffered()
    {
        var page = RenderEditor(EditorFixtures.V28());

        Select(page, "node:sections");
        Assert.DoesNotContain(Options(page, "fluent-select.loop-collection"), o => o.StartsWith("node.", StringComparison.Ordinal));
    }

    // ---- C2: a folder item's if reading a node (design § 3.1) -----------------------------

    private static IReadOnlyList<string> ItemOperands(IRenderedComponent<Templates> page)
    {
        page.Find(".condition-field__resting").Click();
        return page.Find("fluent-select[aria-label='Condition operand for sections item hpi']")
            .QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value") ?? string.Empty).ToList();
    }

    [Fact]
    public void At29_AFolderItemsIf_OffersAScalarNode_AndWritesItsValueRead()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V29());
        Select(page, "snippets/sections/hpi.md");

        Assert.Contains("node:scope", ItemOperands(page));
        page.Find("fluent-select[aria-label='Condition operand for sections item hpi']").Change("node:scope");
        page.Find("fluent-select[aria-label='Condition value for sections item hpi']").Change("in_scope");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

        Assert.True(sent is not null, string.Join(" | ", page.FindAll(".fluent-messagebar-message li").Select(e => e.TextContent.Trim())));
        Assert.Contains("\"if\": \"node.scope.value == \\\"in_scope\\\"\"", sent!.Files["snippets/sections/hpi.md"]);
        var manifest = PF.WorkflowPackageManifestJson.Read(sent.Manifest.GetRawText(), "sent", Accepting29);
        Assert.Empty(PF.WorkflowPackageValidator.Validate(manifest, sent.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal), null, EditorFormatsCatalog.Catalog).Errors);
    }

    [Fact]
    public void At28_AFolderItemsIf_StillReadsInputsOnly()
    {
        var page = RenderEditor(EditorFixtures.V28());
        Select(page, "snippets/sections/hpi.md");

        Assert.DoesNotContain(ItemOperands(page), o => o.StartsWith("node:", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGatesNode_EarnsAPreviewPick_AndJudgesTheItem()
    {
        var fixture = EditorFixtures.V29();
        var files = new Dictionary<string, string>(fixture.Files, StringComparer.Ordinal)
        {
            ["snippets/sections/hpi.md"] = "{\n  \"label\": \"HPI\",\n  \"if\": \"node.scope.value == \\\"in_scope\\\"\"\n}\nHistory: {{ input.notes }}",
        };
        var page = RenderEditor(fixture with { Files = files });
        Select(page, "node:assessment");

        // The gate reads node.scope, which the text itself never reads: it still earns a pick.
        var pick = page.Find("fluent-select[aria-label='Preview with node.scope.value']");
        var hpiGate = () => page.FindAll(".rendered-preview__gate").Single(g => g.GetAttribute("data-gate-key") == "sections/hpi");
        Assert.Contains("rendered-preview__gate--waiting", hpiGate().ClassList);

        pick.Change("out_of_scope");

        Assert.DoesNotContain("rendered-preview__gate--waiting", hpiGate().ClassList);
        Assert.Contains("node.scope.value == \"in_scope\"", hpiGate().TextContent);
    }
}
