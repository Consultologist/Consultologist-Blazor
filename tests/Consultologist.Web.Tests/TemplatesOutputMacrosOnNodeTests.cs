using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #956 (view-only): the node pane surfaces the OUTPUT macros anchored around a
/// node's output — a deliverable macro whose before/after names this node. It is
/// read-only here (editing is #965 in place / #966 full parity): a list with
/// jumps to the macro and the deliverable, no placement select and no condition
/// builder.
/// </summary>
public class TemplatesOutputMacrosOnNodeTests : ClientRenderTestContext
{
    // v12 (placement arrives at 12): consult_note aggregates draft-section, and
    // the `closing` macro is placed BEFORE draft-section, gated on an enum input.
    private const string ManifestJson = """
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 12,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "encounter_kind", "label": "Encounter kind", "required": false, "type": "enum", "values": ["new_patient", "follow_up"] }
          ],
          "macros": [ { "id": "closing", "label": "Closing", "file": "macros/closing.md" } ],
          "prompts": [ { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["consult_draft"] } ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note",
              "macros": [ { "id": "closing", "before": "node:draft-section", "when": "encounter_kind == follow_up" } ] }
          ],
          "nodes": [
            { "id": "draft-section", "label": "Drafting section", "prompt": "draft-section",
              "bindings": { "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """;

    private WorkflowPackageContentResponse Fixture() =>
        EditorFixtures.Package(ManifestJson, 12,
            ("prompts/draft-section.md", "{{ consult_draft }}"),
            ("macros/closing.md", "Thank you for this referral."));

    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(Fixture());
        return Render<Templates>();
    }

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    [Fact]
    public void ASourceNode_SurfacesItsAnchoredOutputMacros()
    {
        var page = RenderEditor();
        ShowNode(page, "draft-section");

        Assert.Contains("Output macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        var row = Assert.Single(page.FindAll(".node-output-macro-row"));
        var text = row.TextContent;
        Assert.Contains("closing", text);
        Assert.Contains("before", text);
        Assert.Contains("consult_note", text);
        // The gated-when shows read-only.
        Assert.Contains("encounter_kind == follow_up", text);
    }

    [Fact]
    public void ANodeWithNoAnchoredOutputMacro_HasNoSection()
    {
        var page = RenderEditor();
        // assemble-note is the aggregator — nothing is anchored before/after it.
        ShowNode(page, "assemble-note");

        Assert.DoesNotContain("Output macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        Assert.Empty(page.FindAll(".node-output-macro-row"));
    }

    [Fact]
    public void TheSection_IsReadOnly()
    {
        var page = RenderEditor();
        ShowNode(page, "draft-section");

        var row = Assert.Single(page.FindAll(".node-output-macro-row"));
        // No placement select, no fan-item select, no attach picker, no condition builder.
        Assert.Empty(row.QuerySelectorAll("fluent-select"));
        Assert.Empty(row.QuerySelectorAll(".result-macro-placement"));
        Assert.Empty(page.FindAll("[aria-label^='Add a condition']"));
        Assert.Empty(page.FindAll("[aria-label^='Append macro']"));
    }
}
