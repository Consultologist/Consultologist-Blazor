using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #948: the node pane is redesigned — a single header with the #946 identity
/// strip and labeled sub-sections (Identity / Prompt / Inputs / Runs when /
/// Options). The shared WorkflowNodeSummary card gains opt-in flags; the Graph
/// pane passes neither, so its Node details stays the flat card.
/// </summary>
public class TemplatesNodePaneTests : ClientRenderTestContext
{
    private static string Manifest() => """
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 18,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [ { "id": "consult_draft", "label": "Consult draft", "required": true } ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "classify", "file": "prompts/classify.md", "variables": ["referral"] },
            { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note", "when": "node:scope == in_scope" }
          ],
          "nodes": [
            { "id": "scope", "label": "Is the referral in scope?", "prompt": "classify", "kind": "classifier",
              "values": ["in_scope", "out_of_scope"], "bindings": { "referral": "input:consult_draft" } },
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section", "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """;

    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.Package(Manifest(), 18,
            ("prompts/classify.md", "Is this in scope? {{ referral }}"),
            ("prompts/draft-section.md", "{{ section_name }}: {{ consult_draft }}")));
        return Render<Templates>();
    }

    private IRenderedComponent<Templates> ShowNode(string nodeId)
    {
        var page = RenderEditor();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
        return page;
    }

    [Fact]
    public void TheGraphPane_StaysFlat_NoStripOrSections()
    {
        // Default pane is the Graph's Node details (no SelectedKey): the shared card
        // must render byte-for-byte as before — its own header, no identity strip,
        // no node-sections.
        var page = RenderEditor();

        Assert.Empty(page.FindAll(".pane-identity"));
        Assert.Empty(page.FindAll(".node-section"));
        Assert.NotEmpty(page.FindAll(".template-section h3"));
    }

    [Fact]
    public void TheNodePane_ShowsTheIdentityStrip_AndTheKind()
    {
        var page = ShowNode("draft-section");

        var strip = Assert.Single(page.FindAll(".pane-identity"));
        Assert.Contains("draft-section", strip.TextContent);
        Assert.Contains("prompt node", strip.TextContent);
    }

    [Fact]
    public void TheNodePane_ShowsTheLabelOnce_NoDoubleTitle()
    {
        var page = ShowNode("draft-section");

        // The wrapper owns the one header; the card's header is suppressed.
        Assert.Equal(1, page.FindAll("h3").Count(h => h.TextContent.Trim() == "Drafting section"));
    }

    [Fact]
    public void TheNodePane_RendersTheLabeledSections()
    {
        var page = ShowNode("draft-section");

        var heads = page.FindAll(".node-section__head").Select(h => h.TextContent.Trim()).ToList();
        Assert.Contains("Identity", heads);
        Assert.Contains("Prompt", heads);
        Assert.Contains("Inputs", heads);
        Assert.Contains("Options", heads);
        Assert.Contains("Runs when", heads);
    }

    [Fact]
    public void ThePlainTextToggle_SitsInThePromptTextSection_NotOptions()
    {
        // #949: the plain-text toggle governs the prompt preview, so it moved from
        // Options into the Prompt text editor section.
        var page = ShowNode("draft-section");

        var promptText = page.FindAll(".node-section")
            .Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text");
        Assert.NotNull(promptText.QuerySelector("input[aria-label*='plain text']"));

        var options = page.FindAll(".node-section")
            .Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Options");
        Assert.Null(options.QuerySelector("input[aria-label*='plain text']"));
    }

    [Fact]
    public void TheNodePane_PutsRemoveInTheHeader()
    {
        var page = ShowNode("draft-section");

        var header = page.Find(".pane-header");
        Assert.Contains(header.QuerySelectorAll("fluent-button"), b => b.TextContent.Contains("Remove node"));
    }

    // ----- #949: the inline prompt editor -----

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.09.1", "acct-1234567890ab@v2026.09.1"),
                Array.Empty<string>()));

    private static JsonElement PromptJson(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement
            .GetProperty("prompts").EnumerateArray().Single(p => p.GetProperty("id").GetString() == id);

    [Fact]
    public void TheNodePane_RendersThePromptTextEditorAndPreview()
    {
        var page = ShowNode("draft-section");

        var promptText = page.FindAll(".node-section")
            .Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text");
        Assert.NotNull(promptText.QuerySelector(".markdown-preview"));
        Assert.Contains("{{ section_name }}", promptText.QuerySelector("fluent-text-area")!.GetAttribute("current-value"));
    }

    [Fact]
    public void EditingThePromptText_RoundTripsToThePromptFile()
    {
        CapturePublish();
        var page = ShowNode("draft-section");

        var editor = page.FindAll(".node-section")
            .Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text");
        editor.QuerySelector("fluent-text-area")!.Change("Rewritten from the node pane.");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("Rewritten from the node pane.", sent!.Files["prompts/draft-section.md"]);
    }

    [Fact]
    public void EditingFromTheNodePane_ShowsTheSameTextInThePromptsPane()
    {
        // "copy, not move": the node pane and the Prompts pane write the same edits entry.
        var page = ShowNode("draft-section");
        page.FindAll(".node-section")
            .Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text")
            .QuerySelector("fluent-text-area")!.Change("Shared edit.");

        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "prompts/draft-section.md";
        page.Render();

        Assert.Equal("Shared edit.", page.Find("fluent-text-area").GetAttribute("current-value"));
    }

    [Fact]
    public void AClassifierNode_AlsoGetsThePromptEditor()
    {
        var page = ShowNode("scope");

        Assert.Contains(page.FindAll(".node-section__head").Select(h => h.TextContent.Trim()), h => h == "Prompt text");
    }

    // ----- rich fixture: preludes + a shared, variable-free, raw prompt -----

    private static string RichManifest() => """
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 18,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "preludes": { "guidance": "preludes/guidance.md", "unused": "preludes/unused.md" },
          "inputs": [ { "id": "consult_draft", "label": "Consult draft", "required": true } ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["section_name", "consult_draft"], "prelude": "guidance" },
            { "id": "summary", "file": "prompts/summary.md", "variables": [], "raw": true }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section", "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" }, "output": "text" },
            { "id": "summarize-a", "label": "Summarizing A", "prompt": "summary", "bindings": {}, "output": "text" },
            { "id": "summarize-b", "label": "Summarizing B", "prompt": "summary", "bindings": {}, "output": "text" },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section", "node:summarize-a", "node:summarize-b"] }
          ]
        }
        """;

    private IRenderedComponent<Templates> ShowNodeRich(string nodeId)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.Package(RichManifest(), 18,
            ("prompts/draft-section.md", "{{ section_name }}: {{ consult_draft }}"),
            ("prompts/summary.md", "A fixed summary."),
            ("preludes/guidance.md", "Use short SNOMED search terms."),
            ("preludes/unused.md", "An unused prelude.")));
        var page = Render<Templates>();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
        return page;
    }

    [Fact]
    public void SettingThePrelude_FromTheNodePane_RoundTrips()
    {
        CapturePublish();
        var page = ShowNodeRich("draft-section");

        page.Find("fluent-select[aria-label='Prelude for prompt draft-section']").Change("unused");
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal("unused", PromptJson(sent!, "draft-section").GetProperty("prelude").GetString());
    }

    [Fact]
    public void ASharedPrompt_NamesTheOtherNodes_InTheUsedByNote()
    {
        var page = ShowNodeRich("summarize-a");

        var promptText = page.FindAll(".node-section")
            .Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text");
        Assert.Contains("summarize-b", promptText.TextContent);
    }

    [Fact]
    public void ARawPrompt_PreviewsVerbatim_InTheNodePane()
    {
        var page = ShowNodeRich("summarize-a");

        var promptText = page.FindAll(".node-section")
            .Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Prompt text");
        Assert.NotNull(promptText.QuerySelector(".plain-preview"));
        Assert.Null(promptText.QuerySelector(".markdown-preview"));
    }
}
