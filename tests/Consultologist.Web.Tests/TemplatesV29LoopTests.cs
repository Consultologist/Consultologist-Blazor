using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1306 (package-format-v29-design.md § 8): the editor at 29, behind the test-only
/// seam until the bump (#1307) — a loop's picker offers a node's array fields (a node
/// outside every loop, its format typing the field an array), the pick is written and
/// published, the body's palette offers the element's fields, and a 28 package
/// upgrades to 29 with only its version changing. Without the seam, nothing moves.
/// </summary>
public class TemplatesV29LoopTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture, bool seam = true)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>(p => p.Add(x => x.AuthorsAboveCeiling, seam));
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

    private static readonly IReadOnlyList<int> Accepting29 = PF.WorkflowPackageValidator.AcceptedSpecVersions.Append(29).ToList();

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
            new Dictionary<string, string>(StringComparer.Ordinal), null, EditorFormatsCatalog.Catalog, Accepting29);
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
    public void WithoutTheSeam_29IsNotOffered_AndNoNodeListsAreOffered()
    {
        var page = RenderEditor(EditorFixtures.V28(), seam: false);
        Assert.DoesNotContain(page.FindAll("fluent-button"), b => b.TextContent.Trim() == "Upgrade to specVersion 29");

        Select(page, "node:sections");
        Assert.DoesNotContain(Options(page, "fluent-select.loop-collection"), o => o.StartsWith("node.", StringComparison.Ordinal));
    }
}
