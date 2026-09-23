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
