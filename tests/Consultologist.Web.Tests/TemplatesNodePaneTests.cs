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
    public void ThePlainTextToggle_SitsInTheOptionsSection()
    {
        var page = ShowNode("draft-section");

        var options = page.FindAll(".node-section")
            .Single(s => s.QuerySelector(".node-section__head")?.TextContent.Trim() == "Options");
        Assert.NotNull(options.QuerySelector("input[aria-label*='plain text']"));
    }

    [Fact]
    public void TheNodePane_PutsRemoveInTheHeader()
    {
        var page = ShowNode("draft-section");

        var header = page.Find(".pane-header");
        Assert.Contains(header.QuerySelectorAll("fluent-button"), b => b.TextContent.Contains("Remove node"));
    }
}
