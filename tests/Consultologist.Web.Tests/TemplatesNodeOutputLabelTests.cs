using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #825: the node `output` dropdown's empty (no-contract) option is kind-aware.
/// A template renders its output deterministically — no model — so the empty
/// option reads "— rendered —", not the model-produced "prose (text)". Only the
/// label changes; the option's value stays "" and still composes to no output.
/// </summary>
public class TemplatesNodeOutputLabelTests : ClientRenderTestContext
{
    private const string Manifest = """
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 18,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [ { "id": "consult_draft", "label": "Consult draft", "required": true } ],
          "data": { "standards": "data/standards/" },
          "prompts": [
            { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["consult_draft"] },
            { "id": "render-letter", "file": "prompts/render-letter.md", "variables": ["consult_draft"] }
          ],
          "results": [ { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" } ],
          "nodes": [
            { "id": "draft-section", "label": "Drafting section", "prompt": "draft-section",
              "bindings": { "consult_draft": "input:consult_draft" } },
            { "id": "render-letter", "label": "Rendering letter", "kind": "template", "prompt": "render-letter",
              "bindings": { "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """;

    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(
            EditorFixtures.Package(Manifest, 18,
                ("prompts/draft-section.md", "{{ consult_draft }}"),
                ("prompts/render-letter.md", "Dear colleague — {{ consult_draft }}")));
        return Render<Templates>();
    }

    private void Show(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static string EmptyOutputOptionText(IRenderedComponent<Templates> page) =>
        page.Find("select[aria-label='Node output contract']")
            .QuerySelectorAll("option")
            .First(option => option.GetAttribute("value") == string.Empty)
            .TextContent.Trim();

    // ----- node card -------------------------------------------------------

    [Fact]
    public void ATemplateNodesCard_ReadsRendered()
    {
        var page = RenderEditor();
        Show(page, "node:render-letter");

        Assert.Equal("— rendered —", EmptyOutputOptionText(page));
        Assert.Contains("validates the render", page.Markup);
    }

    [Fact]
    public void APromptNodesCard_ReadsProseText()
    {
        var page = RenderEditor();
        Show(page, "node:draft-section");

        Assert.Equal("prose (text)", EmptyOutputOptionText(page));
        Assert.DoesNotContain("— rendered —", page.Markup);
    }

    // ----- add-node form ---------------------------------------------------

    [Fact]
    public void TheAddNodeForm_TracksTheKind()
    {
        var page = RenderEditor();
        Show(page, "add-node"); // AddNodeKey

        // Default kind is a prompt node.
        Assert.Contains("prose (text)", page.Markup);
        Assert.DoesNotContain("— rendered —", page.Markup);

        // Switch the kind select to a template node.
        var kind = page.FindAll("select")
            .First(select => select.QuerySelectorAll("option").Any(o => o.TextContent.Trim() == "template node"));
        kind.Change("template");

        Assert.Contains("— rendered —", page.Markup);
        Assert.Contains("validates the render", page.Markup);
    }
}
