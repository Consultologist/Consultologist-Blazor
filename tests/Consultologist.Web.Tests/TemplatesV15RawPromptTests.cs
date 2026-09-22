using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
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

    // #832: enabled on a variable-free prompt; shown but disabled (with a
    // reason) on one with variables — no longer hidden.
    [Fact]
    public void TheRawToggle_IsEnabled_OnAVariableFreePrompt_AndDisabledOnOneWithVariables()
    {
        var page = RenderEditor(EditorFixtures.V15Raw());

        Navigate(page, "disclaimer");
        Assert.False(page.Find(".prompt-raw input[type=checkbox]").HasAttribute("disabled"));

        Navigate(page, "draft-section");
        var disabled = page.Find(".prompt-raw input[type=checkbox]");
        Assert.True(disabled.HasAttribute("disabled"));
        Assert.Contains("uses variables", page.Find(".prompt-raw-row").TextContent, StringComparison.Ordinal);
    }

    // #829: the control names both sides of the choice, so its effect is legible.
    [Fact]
    public void TheRawToggle_NamesScribanAndPlainText()
    {
        var page = RenderEditor(EditorFixtures.V15Raw());
        Navigate(page, "disclaimer");

        var text = page.Find(".prompt-raw-row").TextContent;
        Assert.Contains("Plain text", text, StringComparison.Ordinal);
        Assert.Contains("Scriban", text, StringComparison.Ordinal);
    }

    // #831: the same plain-text setting, surfaced on the node pane.
    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    [Fact]
    public void TheNodePane_OffersPlainText_ForANodeWithAVariableFreePrompt()
    {
        // disclaimer-block is a template node over the variable-free `disclaimer`.
        var page = RenderEditor(EditorFixtures.V15Raw());
        ShowNode(page, "disclaimer-block");

        var toggle = page.Find(".node-plain-text");
        Assert.Contains("Plain text", toggle.TextContent, StringComparison.Ordinal);
        Assert.Contains("Scriban", toggle.TextContent, StringComparison.Ordinal);
        Assert.Contains("disclaimer", toggle.TextContent, StringComparison.Ordinal); // names the shared prompt
    }

    [Fact]
    public void TheNodePane_ComposesRaw_OnThePromptTheNodeUses()
    {
        var page = RenderEditor(EditorFixtures.V15Raw());
        CapturePublish();
        ShowNode(page, "disclaimer-block");

        page.Find(".node-plain-text input[type=checkbox]").Change(true);
        Publish(page);

        Assert.NotNull(sent);
        Assert.True(Prompt(sent!, "disclaimer").GetProperty("raw").GetBoolean());
    }

    [Fact]
    public void TheNodePane_DisablesPlainText_WhenThePromptHasVariables_AndShowsNothing_WithoutAPrompt()
    {
        var page = RenderEditor(EditorFixtures.V15Raw());

        // #832: draft-section's prompt declares variables — the toggle is shown
        // but disabled, with the reason, rather than hidden.
        ShowNode(page, "draft-section");
        var toggle = page.Find(".node-plain-text input[type=checkbox]");
        Assert.True(toggle.HasAttribute("disabled"));
        Assert.Contains("uses variables", page.Find(".node-plain-text").TextContent, StringComparison.Ordinal);

        // An aggregator has no prompt, so there is nothing to render.
        ShowNode(page, "assemble-note");
        Assert.Empty(page.FindAll(".node-plain-text"));
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
