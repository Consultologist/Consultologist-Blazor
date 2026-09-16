using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v15 (#731): the raw-prompt toggle in the editor — offered on a variable-free
/// prompt, composing `raw: true`, and removed when unchecked. Mirrors the v12
/// macro-optional authoring tests.
/// </summary>
public class TemplatesV15RawPromptTests : ClientRenderTestContext
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
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.09.1", "acct-1234567890ab@v2026.09.1"),
                Array.Empty<string>()));

    private static JsonElement Prompt(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement
            .GetProperty("prompts").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);

    [Fact]
    public void TheRawToggle_IsOffered_OnAVariableFreePrompt_AndNotOnOneWithVariables()
    {
        var page = RenderEditor(EditorFixtures.V15Raw());

        Navigate(page, "disclaimer");
        Assert.NotEmpty(page.FindAll(".prompt-raw input[type=checkbox]"));

        Navigate(page, "draft-section");
        Assert.Empty(page.FindAll(".prompt-raw input[type=checkbox]"));
    }

    [Fact]
    public void CheckingRaw_ComposesRawTrueOnThePrompt()
    {
        var page = RenderEditor(EditorFixtures.V15Raw());
        CapturePublish();
        Navigate(page, "disclaimer");

        page.Find(".prompt-raw input[type=checkbox]").Change(true);
        Publish(page);

        Assert.NotNull(sent);
        Assert.True(Prompt(sent!, "disclaimer").GetProperty("raw").GetBoolean());
        // draft-section, untouched, stays non-raw.
        Assert.False(Prompt(sent!, "draft-section").TryGetProperty("raw", out _));
    }
}
