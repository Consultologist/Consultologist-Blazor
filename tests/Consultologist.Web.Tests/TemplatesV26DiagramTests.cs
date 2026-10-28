using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Consultologist.Web.Shared.WorkflowEditor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1227: from 25 the Graph pane draws the diagram here, from the editor's own walk —
/// the reads of every template as edges, no round trip — and asks the engine only
/// when it cannot (no format catalogue: a walk without one sees no catalogue-format
/// prompt and would draw a thinner graph than the engine).
/// </summary>
public class TemplatesV26DiagramTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderGraph(WorkflowPackageContentResponse fixture, WorkflowFormatsView? formats)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(formats);
        WorkflowService.GetCurrentDiagramAsync(Arg.Any<string?>()).Returns("graph TD; engine");
        var page = Render<Templates>();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "graph";
        page.Render();
        return page;
    }

    private static string Diagram(IRenderedComponent<Templates> page) =>
        page.FindComponent<WorkflowDagView>().Instance.Diagram ?? string.Empty;

    [Fact]
    public void At26_TheGraphPane_DrawsFromTheLocalWalk_ReadsAsEdges()
    {
        var page = RenderGraph(EditorFixtures.V26(), EditorFormatsCatalog.View());

        page.WaitForAssertion(() =>
        {
            var diagram = Diagram(page);
            Assert.StartsWith("flowchart TD", diagram);
            Assert.Contains("extract -->|\"value\"| assessment", diagram);
            Assert.Contains("input_notes([\"input.notes\"])", diagram);
            Assert.Contains("snippet_standards --> assessment", diagram);
        });
        WorkflowService.DidNotReceive().GetCurrentDiagramAsync(Arg.Any<string?>());
    }

    [Fact]
    public void At25_TheGraphPane_DrawsFromTheLocalWalk_Too()
    {
        var page = RenderGraph(EditorFixtures.V25(), EditorFormatsCatalog.View());

        page.WaitForAssertion(() => Assert.Contains("extract -->|\"value\"| assessment", Diagram(page)));
        WorkflowService.DidNotReceive().GetCurrentDiagramAsync(Arg.Any<string?>());
    }

    [Fact]
    public void At26_WithoutTheCatalogue_TheGraphPane_AsksTheEngine()
    {
        var page = RenderGraph(EditorFixtures.V26(), formats: null);

        page.WaitForAssertion(() => Assert.Equal("graph TD; engine", Diagram(page)));
    }

    [Fact]
    public void Below25_TheGraphPane_AsksTheEngine_AsBefore()
    {
        var page = RenderGraph(EditorFixtures.V24Full(), EditorFormatsCatalog.View());

        page.WaitForAssertion(() => Assert.Equal("graph TD; engine", Diagram(page)));
    }
}
