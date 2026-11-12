using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v30 (#1346 C2a, design § 11; public since the bump, #1347): the Editor reads and keeps a 30
/// package — a while's members are placed and never offered elsewhere, an unrelated edit leaves
/// every 30 member as it was, a 29 package upgrades to 30 with only its version changing, and
/// the Graph pane draws it nested.
/// </summary>
public class TemplatesV30ReadTests : ClientRenderTestContext
{
    internal static readonly IReadOnlyCollection<int> Accepting30 = PF.WorkflowPackageValidator.AcceptedSpecVersions.ToList();

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        WorkflowService.GetCurrentDiagramAsync(Arg.Any<string?>()).Returns("graph TD; engine");
        return Render<Templates>();
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.11.3", "acct-1234567890ab@v2026.11.3"),
                Array.Empty<string>()));

    internal static PF.WorkflowPackageValidator.ValidationResult Validate(WorkflowPackagePublishRequest request)
    {
        var manifest = PF.WorkflowPackageManifestJson.Read(request.Manifest.GetRawText(), "sent", Accepting30.ToList());
        return PF.WorkflowPackageValidator.Validate(manifest, request.Files.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal), null, EditorFormatsCatalog.Catalog);
    }

    [Fact]
    public void TheV30Fixture_IsValidAt30()
    {
        var fixture = EditorFixtures.V30();
        var manifest = PF.WorkflowPackageManifestJson.Read(fixture.Manifest.GetRawText(), "fixture", Accepting30.ToList());
        var result = PF.WorkflowPackageValidator.Validate(manifest, fixture.Files, new Dictionary<string, string>(), null, EditorFormatsCatalog.Catalog);
        Assert.True(result.Errors.Count == 0, string.Join("\n", result.Errors));
    }

    [Fact]
    public void AnUnrelatedInputsEdit_KeepsEvery30Member_AndTheRestOfTheManifestAsLoaded()
    {
        CapturePublish();
        var fixture = EditorFixtures.V30();
        var page = RenderEditor(fixture);
        page.FindAll("button.editor-nav__item").First(button => button.TextContent.Replace("●", string.Empty).Trim() == "Inputs").Click();

        page.FindAll("li.declared-row input")[1].Change("Referral notes");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

        Assert.True(sent is not null, System.Text.RegularExpressions.Regex.Replace(page.Markup, @"\s+", " ") is var m && m.IndexOf("rejected", StringComparison.Ordinal) is var at and >= 0 ? m.Substring(at, Math.Min(900, m.Length - at)) : "no publish, no rejection");
        var expected = JsonNode.Parse(fixture.Manifest.GetRawText())!.AsObject();
        expected["inputs"]![0]!["label"] = "Referral notes";
        var actual = JsonNode.Parse(sent!.Manifest.GetRawText())!.AsObject();
        Assert.True(JsonNode.DeepEquals(expected["nodes"], actual["nodes"]), actual["nodes"]!.ToJsonString());
        Assert.Equal("title", actual["inputs"]!.AsArray().Single(i => (string?)i!["id"] == "cases")!["labelField"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(expected["inputs"], actual["inputs"]), actual["inputs"]!.ToJsonString());
        var result = Validate(sent);
        Assert.True(result.Errors.Count == 0, string.Join("\n", result.Errors));
    }

    [Fact]
    public void AWhilesMembers_ArePlaced_AndNeverOfferedToAnotherBody()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "node:problems");

        var offered = page.FindAll("fluent-select.loop-body-add fluent-option").Select(o => o.GetAttribute("value") ?? string.Empty).ToList();

        Assert.DoesNotContain("draft_problem", offered);
        Assert.DoesNotContain("refine", offered);
        // The loop's body names its while by its kind.
        Assert.Contains("· while pass.number < 2 — per item", page.Find(".loop-body__flow").TextContent);
    }

    [Fact]
    public async Task A29Package_UpgradesTo30_WithOnlyItsVersionChanging()
    {
        var page = RenderEditor(EditorFixtures.V29());

        await page.InvokeAsync(() => page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Upgrade to specVersion 30").Click());
        page.Render();

        var notice = System.Text.RegularExpressions.Regex.Replace(page.Find(".migration-notice").TextContent, @"\s+", " ").Trim();
        Assert.Contains("Migrated to specVersion 30.", notice);
        Assert.Contains("Through 30: nothing changes in this package; loops may now nest, repeat (while) or stop early (until), and a let computes a value.", notice);
        Assert.DoesNotContain("Through 29", notice);
    }

    [Fact]
    public void A30Package_IsOfferedNoUpgrade()
    {
        var page = RenderEditor(EditorFixtures.V30());

        Assert.DoesNotContain(page.FindAll("fluent-button"), b => b.TextContent.Trim().StartsWith("Upgrade to specVersion", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGraphPane_DrawsA30PackageNested()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "graph");

        page.WaitForAssertion(() =>
        {
            var diagram = page.FindComponent<WorkflowDagView>().Instance.Diagram ?? string.Empty;
            var problems = diagram[diagram.IndexOf("subgraph for_problems[", StringComparison.Ordinal)..];
            Assert.Contains("subgraph while_refine[\"refine: while pass.number &lt; 2 — at most 2\"]".Replace("&lt;", "<"), problems);
            Assert.Contains("problem_count[[\"problem_count<br/>Counting the problems<br/>computed · let → number\"]]", diagram);
        });
        WorkflowService.DidNotReceive().GetCurrentDiagramAsync(Arg.Any<string?>());
    }
}
