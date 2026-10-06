using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #946: every entity pane names itself the same way — a read-only identity strip
/// (id · file), with the id suppressed when it is already the title. The macro's
/// label and a standard item's name are editable from their panes.
/// </summary>
public class TemplatesIdentityStripTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    private Consultologist.PackageFormat.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<Consultologist.PackageFormat.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return Consultologist.PackageFormat.WorkflowPackageValidator.Validate(
            manifest, sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
    }

    [Fact]
    public void TheMacroPane_ShowsItsIdAndFileInTheIdentityStrip()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        Navigate(page, "disclaimer");

        var strip = page.Find(".pane-identity");
        Assert.Contains("disclaimer", strip.TextContent);
        Assert.Contains("macros/disclaimer.md", strip.TextContent);
        // the human label is the title, not the strip.
        Assert.Contains("Standing disclaimer", page.Find(".panel-title").TextContent);
    }

    [Fact]
    public void ThePromptPane_SuppressesTheIdInTheStrip_WhenItIsAlreadyTheTitle()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        Navigate(page, "classify");

        // Title == id, so the strip carries only the file — no "id" term.
        var strip = page.Find(".pane-identity");
        Assert.Contains("prompts/classify.md", strip.TextContent);
        Assert.DoesNotContain(
            strip.QuerySelectorAll(".pane-identity__item").Select(item => item.TextContent.Trim()),
            text => text.StartsWith("id ", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStandardPane_SurfacesItsIdInTheStrip()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());
        Navigate(page, "History");

        var strip = page.Find(".pane-identity");
        Assert.Contains("hpi", strip.TextContent);
        Assert.Contains("data/standards/hpi.md", strip.TextContent);
    }

    [Fact]
    public void EditingAMacroLabel_RoundTripsToTheManifest()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V11Macro());
        Navigate(page, "disclaimer");

        page.Find("input[aria-label='Label for macro disclaimer']").Change("Safety disclaimer");
        Publish(page);

        Assert.NotNull(sent);
        var macro = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement
            .GetProperty("macros").EnumerateArray().Single(m => m.GetProperty("id").GetString() == "disclaimer");
        Assert.Equal("Safety disclaimer", macro.GetProperty("label").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }

    [Fact]
    public void EditingAStandardName_RoundTripsToTheCollectionIndex()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V11Macro());
        Navigate(page, "History");

        page.Find("input[aria-label='Name for standard hpi']").Change("Presenting illness");
        Publish(page);

        Assert.NotNull(sent);
        // The name lives in the collection's index.json, not the manifest — the
        // publish gate must recompose it.
        Assert.True(sent!.Files.ContainsKey("data/standards/index.json"),
            "index.json was not recomposed — the publish gate did not fire for a name edit.");
        var item = JsonDocument.Parse(sent.Files["data/standards/index.json"]).RootElement
            .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "hpi");
        Assert.Equal("Presenting illness", item.GetProperty("name").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join(" | ", validated.Errors));
    }
}
