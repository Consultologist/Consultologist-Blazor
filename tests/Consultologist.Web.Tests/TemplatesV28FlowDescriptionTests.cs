using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v28 (#1270, design § 6a): a flow node's display-only description — a text area beside
/// the label in the loop, if and case panes at 28, published beside the label (removed when
/// cleared), counted against the 500-character limit, and drawn under the label in the
/// Graph box. At 27 the field is not offered.
/// </summary>
public class TemplatesV28FlowDescriptionTests : ClientRenderTestContext
{
    private static readonly IReadOnlyCollection<int> Accepting28 = PF.WorkflowPackageValidator.AcceptedSpecVersions.Append(28).ToList();
    private const string Field = "fluent-text-area[aria-label='Description of sections']";

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V28());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>(p => p.Add(x => x.AuthorsAboveCeiling, true));
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

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

    private static JsonElement Node(WorkflowPackagePublishRequest request, string id) =>
        request.Manifest.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("id").GetString() == id);

    private PF.WorkflowPackageValidator.ValidationResult Validated() =>
        PF.WorkflowPackageValidator.Validate(
            PF.WorkflowPackageManifestJson.Read(sent!.Manifest.GetRawText(), "sent", Accepting28.ToList()),
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog,
            Accepting28);

    [Fact]
    public void ALoopsDescription_PublishesBesideItsLabel_AndValidates()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:sections");
        page.Find(Field).Change("Drafts one note per section.");
        Assert.Equal("28 / 500", page.Find("[data-description-count]").TextContent.Trim());
        Publish(page);

        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);
        Assert.Equal("Drafts one note per section.", Node(sent!, "sections").GetProperty("description").GetString());
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void ClearingALoadedDescription_RemovesIt()
    {
        CapturePublish();
        var fixture = EditorFixtures.V28();
        var root = System.Text.Json.Nodes.JsonNode.Parse(fixture.Manifest.GetRawText())!.AsObject();
        root["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "sections")!["description"] = "Drafts one note per section.";
        var page = RenderEditor(fixture with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() });
        Select(page, "node:sections");
        Assert.Equal("Drafts one note per section.", page.Find(Field).GetAttribute("value"));

        page.Find(Field).Change("");
        Publish(page);

        Assert.NotNull(sent);
        Assert.False(Node(sent!, "sections").TryGetProperty("description", out _));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void ADescriptionOverTheLimit_IsCounted_AndRefusedAtPublish()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:sections");
        page.Find(Field).Change(new string('a', 501));
        Assert.Contains("field-error", page.Find("[data-description-count]").ClassName);
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("description must be at most 500 characters", StringComparison.Ordinal));
    }

    [Fact]
    public void At27_TheFieldIsNotOffered()
    {
        var raised = PF.WorkflowPackageVersionRaise.To(EditorFixtures.V26Gated().Manifest.GetRawText(), EditorFixtures.V26Gated().Files, 27);
        var page = RenderEditor(new WorkflowPackageContentResponse("acct-1234567890ab", "v2026.07.1", 27,
            JsonDocument.Parse(raised.ManifestJson).RootElement.Clone(), new Dictionary<string, string>(raised.Files, StringComparer.Ordinal)));
        Select(page, "node:sections");
        Assert.Empty(page.FindAll(Field));
    }
}
