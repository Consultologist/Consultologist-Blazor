using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1162, design § 4 and § 12): a prompt is its node's file — the Prompts nav goes at 26,
/// the node pane holds the prompt editor, its Reads and Read by rows and its rendered preview
/// with one owner (the node). The prompt model is derived from the nodes, keyed by file.
/// Rendered through the #1209 seam; the 25 nav and pane are pinned by the ≤25 suites.
/// </summary>
public class TemplatesV26PromptPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>(parameters => parameters.Add(p => p.AuthorsAboveCeiling, true));
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private string SelectedKey => Services.GetRequiredService<WorkflowEditorSession>().SelectedKey!;

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

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

    private static JsonElement Node(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == id);

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(sent!.Manifest.GetRawText(), PF.WorkflowPackageManifestJson.ReadOptions)!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    private static AngleSharp.Dom.IElement PromptTextSection(IRenderedComponent<Templates> page) =>
        page.FindAll(".node-section").Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text");

    private static IReadOnlyList<string> ReadersOf(IRenderedComponent<Templates> page, string nodeId) =>
        page.Find($".template-read-rows[aria-label='Readers of {nodeId}']").QuerySelectorAll(".template-read-row").Select(r => r.GetAttribute("data-read-reader")!).ToList();

    // ---- the nav ------------------------------------------------------------------

    [Fact]
    public void TheNav_HasNoPromptsGroup_At26_AndKeepsIt_At25()
    {
        var page = RenderEditor();
        Assert.DoesNotContain("Prompts", page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()));
        Assert.DoesNotContain(page.FindAll("button.editor-nav__item"), b => b.TextContent.Contains("+ Prompt"));

        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        var at25 = Render<Templates>();
        Assert.Contains("Prompts", at25.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()));
        Assert.Contains(at25.FindAll("button.editor-nav__item"), b => b.TextContent.Contains("+ Prompt"));
    }

    // ---- the node pane holds the editor ------------------------------------------------

    [Fact]
    public void TheNodePane_ShowsThePromptText_EditsItsFile_AndTheNavDotLights()
    {
        CapturePublish();
        var page = RenderEditor();
        Select(page, "node:section_notes");

        var section = PromptTextSection(page);
        Assert.Equal("{{ item.label }}: {{ item.content }}", section.QuerySelector("fluent-text-area")!.GetAttribute("current-value"));
        // One owner: the node — the preview resolves through it: the loop's items are the picks.
        Assert.NotNull(page.Find(".rendered-preview__item-pick"));
        // The palette never offers the node its own output.
        var offered = page.Find("fluent-select.insert-reference__pick").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value") ?? string.Empty).ToList();
        Assert.DoesNotContain(offered, v => v.Contains("node.section_notes"));
        Assert.Contains(offered, v => v.Contains("item.content"));

        section.QuerySelector("fluent-text-area")!.Change("{{ item.label }}: {{ item.content }}\nKeep it brief.");
        Assert.Contains("●", page.FindAll("button.editor-nav__item").First(b => b.TextContent.Contains("Section notes")).TextContent);
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("{{ item.label }}: {{ item.content }}\nKeep it brief.", sent!.Files["prompts/section.md"]);
        Assert.Equal("prompts/section.md", Node(sent, "section_notes").GetProperty("prompt").GetString());
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void TheNodePanesDiscard_RevertsThePromptText()
    {
        var page = RenderEditor();
        Select(page, "node:section_notes");

        PromptTextSection(page).QuerySelector("fluent-text-area")!.Change("changed");
        page.Find(".pane-discard-button").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();

        Assert.Equal("{{ item.label }}: {{ item.content }}", PromptTextSection(page).QuerySelector("fluent-text-area")!.GetAttribute("current-value"));
        Assert.DoesNotContain("●", page.FindAll("button.editor-nav__item").First(b => b.TextContent.Contains("Section notes")).TextContent);
    }

    [Fact]
    public void ALoopPane_StillHasNoPromptText_AndTheNodeCardHasNoPromptSelect()
    {
        var page = RenderEditor();
        Select(page, "node:sections");
        Assert.DoesNotContain("Prompt text", page.Markup);

        Select(page, "node:assessment");
        Assert.NotNull(PromptTextSection(page));
        Assert.Empty(page.FindAll("fluent-select[aria-label='Node prompt']"));
        // The preview's subject is the node's prompt — the walk's one owner — not a prompt id.
        Assert.Equal("Rendered preview of Node 'assessment' prompt as", page.Find(".rendered-preview__mode").GetAttribute("aria-label"));
    }

    // ---- Read by ----------------------------------------------------------------------

    [Fact]
    public void TheNodePane_ListsWhoReadsIt_OrRestsEmpty()
    {
        var page = RenderEditor();

        Select(page, "node:extract");
        Assert.Equal(new[] { "Node 'Assessment'" }, ReadersOf(page, "extract"));
        page.Find(".template-read-rows[aria-label='Readers of extract'] fluent-button").Click();
        Assert.Equal("node:assessment", SelectedKey);

        Select(page, "node:meds");
        Assert.Equal(new[] { "Node 'Assessment'" }, ReadersOf(page, "meds"));

        Select(page, "node:scope");
        Assert.Contains("Read by nothing yet", page.Find(".node-read-by__empty").TextContent);
    }

    [Fact]
    public void ASnippetsReadByRow_OpensTheNodePane()
    {
        // The standards snippet is read by assessment's prompt; the row carries the walk's
        // subject ("Node 'assessment' prompt") and opens the node's pane — the prompt's only home.
        var page = RenderEditor();
        Select(page, "snippets/standards.md");

        var row = page.FindAll(".template-read-row").First(r => r.GetAttribute("data-read-reader")!.Contains("assessment"));
        Assert.Equal("Node 'assessment' prompt", row.GetAttribute("data-read-reader"));
        row.QuerySelector("fluent-button")!.Click();
        Assert.Equal("node:assessment", SelectedKey);
    }

    // ---- creating a node -----------------------------------------------------------------

    [Fact]
    public void PlusNode_LandsOnTheNodePane_WithAnEmptyPromptText_ThenComposesTheFile()
    {
        CapturePublish();
        var page = RenderEditor();
        Navigate(page, "+ Node");

        Assert.DoesNotContain("new prompt (named after the node)", page.Markup);
        page.Find("fluent-select.new-node-format").Change("prose");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("plan");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Plan");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();

        Assert.Equal("node:plan", SelectedKey);
        var section = PromptTextSection(page);
        Assert.Equal("", section.QuerySelector("fluent-text-area")!.GetAttribute("current-value") ?? "");
        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Node 'plan' prompt has no text yet — write it on the node's pane."));

        section.QuerySelector("fluent-text-area")!.Change("Plan from {{ input.notes }}.");
        // Make it feed the deliverable: a source on the card.
        Select(page, "results:consult");
        page.Find("fluent-select.deliverable-source-add").Change("plan");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("Plan from {{ input.notes }}.", sent!.Files["prompts/plan.md"]);
        Assert.Equal("prompts/plan.md", Node(sent, "plan").GetProperty("prompt").GetString());
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void PlusNode_RefusesAnIdWhosePromptFileExists()
    {
        var page = RenderEditor();
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("prose");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("section");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Section");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();

        Assert.Contains(page.FindAll(".fluent-messagebar-message"), bar => bar.TextContent.Contains("A prompt file 'prompts/section.md' already exists"));
    }
}
