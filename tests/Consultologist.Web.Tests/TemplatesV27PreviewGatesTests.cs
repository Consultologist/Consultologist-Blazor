using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v27 (#1236; decided 2026-10-29): the preview's flow gate — a branch node's pane names the
/// branch its if/case takes for the pick and whether this node runs: a top-level if over the
/// input pick (a 26 package, no seam — the 26 gap closed in passing), an if inside a loop over
/// the picked item.
/// </summary>
public class TemplatesV27PreviewGatesTests : ClientRenderTestContext
{
    private const string KindSelect = "fluent-select[aria-label='Preview with input.kind']";
    private const string ItemSelect = "fluent-select.rendered-preview__item-pick";

    private IRenderedComponent<Templates> Show(string key, WorkflowPackageContentResponse fixture, bool seam)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>(parameters => parameters.Add(p => p.AuthorsAboveCeiling, seam));
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
        return page;
    }

    private static IReadOnlyList<(string Kind, string Key, string Text, bool Waiting, string? Branch)> Gates(IRenderedComponent<Templates> page) =>
        page.FindAll(".rendered-preview__gate")
            .Select(li => (li.GetAttribute("data-gate-kind")!, li.GetAttribute("data-gate-key")!, li.TextContent.Trim(), li.ClassList.Contains("rendered-preview__gate--waiting"), li.GetAttribute("data-branch")))
            .ToList();

    [Fact]
    public void ATopLevelIf_IsJudgedOnItsBranchNodesPane_OverTheInputPick_At26()
    {
        var page = Show("node:assessment", EditorFixtures.V26Flow(), seam: false);

        var waiting = Assert.Single(Gates(page), g => g.Kind == "flow");
        Assert.True(waiting.Waiting);
        Assert.Equal("pick input.kind to judge node 'triage''s if (input.kind == \"new\")", waiting.Text);

        page.Find(KindSelect).Change("new");
        var then = Assert.Single(Gates(page), g => g.Kind == "flow");
        Assert.Equal(("then", "node 'triage' takes then for this pick: its if (input.kind == \"new\") — this node runs"), (then.Branch, then.Text));

        page.Find(KindSelect).Change("follow_up");
        var none = Assert.Single(Gates(page), g => g.Kind == "flow");
        Assert.Equal("none", none.Branch);
        Assert.Equal("node 'triage' takes no branch for this pick: its if (input.kind == \"new\") — this node does not run (its text renders all the same)", none.Text);
    }

    [Fact]
    public void AnInnerIf_WaitsOnTheItemPick_ThenNamesTheBranchForThatItem()
    {
        var page = Show("node:patient_draft", EditorFixtures.V27Flow(), seam: true);

        var waiting = Assert.Single(Gates(page), g => g.Kind == "flow");
        Assert.Equal("pick an item to judge node 'triage''s if (per item of loop 'sections') (item.id == \"hpi\")", waiting.Text);

        page.Find(ItemSelect).Change("plan");
        var asPlan = Assert.Single(Gates(page), g => g.Kind == "flow");
        Assert.Equal(("else", "node 'triage' takes else for this pick for this item of loop 'sections': its if (item.id == \"hpi\") — this node does not run (its text renders all the same)"), (asPlan.Branch, asPlan.Text));

        page.Find(ItemSelect).Change("hpi");
        var asHpi = Assert.Single(Gates(page), g => g.Kind == "flow");
        Assert.Equal("then", asHpi.Branch);
        Assert.EndsWith("— this node runs", asHpi.Text);
    }

    [Fact]
    public void ALoopsOwnBodyNode_MeetsNoFlowGate()
    {
        var page = Show("node:section_notes", EditorFixtures.V27Flow(), seam: true);

        Assert.DoesNotContain(Gates(page), g => g.Kind == "flow");
    }
}
