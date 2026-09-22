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

    // #835: the toggle was gated on IsDeclaredPrompt, so a prompt still pending
    // in the editor (a standalone added prompt, or the fresh prompt a node
    // mints) never offered it. These cover both pending sources.

    private IRenderedComponent<Templates> RenderWithDraft(WorkflowPackageContentResponse fixture, string draftJson)
    {
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{fixture.Ref}").SetResult(draftJson);
        return RenderEditor(fixture);
    }

    [Fact]
    public void TheNodePane_OffersAndComposesPlainText_ForAnAddedNodesOwnPrompt()
    {
        // The reported case (#835): a template node added in the editor mints
        // its own variable-free prompt, wired into the deliverable.
        var page = RenderWithDraft(EditorFixtures.V15Raw(), """
            {
              "Version": 15,
              "AddedNodes": [ { "Id": "footer", "Label": "Footer", "PromptText": "Standard footer.", "Kind": "template" } ],
              "AggregateEdits": { "assemble-note": ["node:draft-section", "node:disclaimer-block", "node:footer"] }
            }
            """);
        CapturePublish();

        ShowNode(page, "footer");
        var toggle = page.Find(".node-plain-text input[type=checkbox]");
        Assert.False(toggle.HasAttribute("disabled"));
        toggle.Change(true);
        Publish(page);

        Assert.NotNull(sent);
        Assert.True(Prompt(sent!, "footer").GetProperty("raw").GetBoolean());
    }

    [Fact]
    public void ThePromptPane_OffersAndComposesPlainText_ForAStandaloneAddedPrompt()
    {
        // A standalone prompt added via "+ Prompt", pointed at by a template node.
        var page = RenderWithDraft(EditorFixtures.V15Raw(), """
            {
              "Version": 15,
              "AddedPrompts": [ { "Id": "footer-text", "Text": "Standard footer." } ],
              "AddedNodes": [ { "Id": "footer", "Label": "Footer", "PromptText": "", "PromptRef": "footer-text", "Kind": "template" } ],
              "AggregateEdits": { "assemble-note": ["node:draft-section", "node:disclaimer-block", "node:footer"] }
            }
            """);
        CapturePublish();

        Navigate(page, "footer-text");
        var toggle = page.Find(".prompt-raw input[type=checkbox]");
        Assert.False(toggle.HasAttribute("disabled"));
        toggle.Change(true);
        Publish(page);

        Assert.NotNull(sent);
        Assert.True(Prompt(sent!, "footer-text").GetProperty("raw").GetBoolean());
    }

    [Fact]
    public void ThePendingPromptRawChoice_SurvivesADraftRoundTrip()
    {
        // Raw ripples through DraftAddedNode/DraftAddedPrompt, so a reload
        // (draft restore) keeps the plain-text choice and composes it.
        var page = RenderWithDraft(EditorFixtures.V15Raw(), """
            {
              "Version": 15,
              "AddedNodes": [ { "Id": "footer", "Label": "Footer", "PromptText": "Standard footer.", "Kind": "template", "Raw": true } ],
              "AggregateEdits": { "assemble-note": ["node:draft-section", "node:disclaimer-block", "node:footer"] }
            }
            """);
        CapturePublish();

        ShowNode(page, "footer");
        Assert.True(page.Find(".node-plain-text input[type=checkbox]").HasAttribute("checked"));

        Publish(page);
        Assert.NotNull(sent);
        Assert.True(Prompt(sent!, "footer").GetProperty("raw").GetBoolean());
    }

    [Fact]
    public void TheToggle_IsDisabled_WhenAPendingPromptGainsAVariable()
    {
        // #832's variable rule holds for pending prompts too — plain text
        // interpolates nothing. (The variable is added through the UI: draft
        // restore applies variable adds before the pending prompt exists.)
        var page = RenderWithDraft(EditorFixtures.V15Raw(), """
            {
              "Version": 15,
              "AddedPrompts": [ { "Id": "greeting", "Text": "Hello." } ],
              "AddedNodes": [ { "Id": "greeter", "Label": "Greeter", "PromptText": "", "PromptRef": "greeting", "Kind": "template" } ],
              "AggregateEdits": { "assemble-note": ["node:draft-section", "node:disclaimer-block", "node:greeter"] }
            }
            """);

        Navigate(page, "greeting");
        Assert.False(page.Find(".prompt-raw input[type=checkbox]").HasAttribute("disabled"));

        page.Find("input[aria-label='New variable name']").Change("consult_draft");
        page.Find("select[aria-label='New variable source']").Change("input:consult_draft");
        page.FindAll(".prompt-variables button").First(b => b.TextContent.Trim() == "Add").Click();

        Assert.True(page.Find(".prompt-raw input[type=checkbox]").HasAttribute("disabled"));
        Assert.Contains("uses variables", page.Find(".prompt-raw-row").TextContent, StringComparison.Ordinal);
    }
}
