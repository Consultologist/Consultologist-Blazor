using System.Linq;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #795: the editor's left-hand nav groups read Workflow first, then Prompts,
/// Macros, Preludes, Schemas, Data — and within Data, values sit above folders.
/// </summary>
public class TemplatesNavOrderTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        return Render<Templates>();
    }

    [Fact]
    public void TheNavGroups_ReadWorkflowFirst()
    {
        // V11 so the Macros group shows (v11+ or when macros exist).
        var page = RenderEditor(EditorFixtures.V11Macro());

        var groups = page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Workflow", "Nodes", "Prompts", "Macros", "Preludes", "Schemas", "Data" }, groups);
    }

    // #838: nodes are their own section (a peer to Prompts/Macros), while Graph
    // stays its own Workflow item — not the parent the nodes nest under.
    [Fact]
    public void Nodes_AreTheirOwnSection_BelowGraph()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());

        var groups = page.FindAll(".editor-nav__group").Select(g => g.TextContent.Trim()).ToArray();
        Assert.Contains("Nodes", groups);

        var items = page.FindAll(".editor-nav__item").Select(i => i.TextContent.Trim()).ToArray();
        Assert.Contains(items, i => i == "Graph"); // Graph stays a Workflow item

        // + Node now sits flat in the Nodes section, not nested under Graph.
        var addNode = page.FindAll(".editor-nav__item").Single(i => i.TextContent.Trim() == "+ Node");
        Assert.DoesNotContain("editor-nav__item--nested", addNode.ClassList);
    }

    [Fact]
    public void WithinData_ValuesPrecedeFolders()
    {
        var page = RenderEditor(EditorFixtures.V11Macro());

        // Both add buttons always render; the value one comes first now.
        var addButtons = page.FindAll(".editor-nav__add")
            .Select(b => b.TextContent.Trim())
            .Where(t => t is "+ Data value" or "+ Data folder")
            .ToArray();
        Assert.Equal(new[] { "+ Data value", "+ Data folder" }, addButtons);
    }
}
