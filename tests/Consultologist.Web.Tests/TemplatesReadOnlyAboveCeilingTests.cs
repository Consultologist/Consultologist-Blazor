using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1209 (interim, Milestone 33): a package ABOVE the editor's authoring ceiling —
/// the engine runs 26 while the panes author up to 25 (#1149's lag) — opens
/// readable and un-editable with a banner; every edit door is refused by name. The
/// seam (<c>AuthorsAboveCeiling</c>) is what the 26-pane tests render with.
/// <b>This whole file is deleted by #1164</b>, with the gate, the banner and the seam,
/// when the ceilings meet again.
/// </summary>
public class TemplatesReadOnlyAboveCeilingTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture, bool withSeam = false)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return withSeam
            ? Render<Templates>(parameters => parameters.Add(p => p.AuthorsAboveCeiling, true))
            : Render<Templates>();
    }

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static IElement Toolbar(IRenderedComponent<Templates> page, string text) =>
        page.FindAll("fluent-button").First(b => b.TextContent.Contains(text));

    private static bool IsDisabled(IElement button) => button.HasAttribute("disabled");

    private bool DraftWritten() =>
        JSInterop.Invocations["localStorage.setItem"]
            .Any(invocation => ((string)invocation.Arguments[0]!).StartsWith("workflow-editor-draft", StringComparison.Ordinal));

    [Fact]
    public void A26Package_OpensWithTheBanner_ThePaneInert_AndEveryEditDoorShut()
    {
        var page = RenderEditor(EditorFixtures.V26());

        var banner = page.Find(".read-only-notice");
        Assert.Contains("This package is at specVersion 26; this editor authors up to 25. Reading only; #1159–#1164 bring the 26 panes.", banner.TextContent);
        Assert.True(page.Find("section.editor-pane").HasAttribute("inert"));

        Assert.True(IsDisabled(Toolbar(page, "Publish")));
        Assert.True(IsDisabled(Toolbar(page, "Undo")));
        Assert.True(IsDisabled(Toolbar(page, "Redo")));
        Assert.True(IsDisabled(Toolbar(page, "Discard")));
        Assert.True(IsDisabled(Toolbar(page, "Revert to default")));
        Assert.True(IsDisabled(Toolbar(page, "Import package")));
        Assert.DoesNotContain(page.FindAll("fluent-button"), b => b.TextContent.Contains("Upgrade to specVersion"));
        // A read stays open: the download is not an edit.
        Assert.False(IsDisabled(Toolbar(page, "Download package")));
    }

    [Fact]
    public void A26Package_IsStillReadable_PaneByPane()
    {
        var page = RenderEditor(EditorFixtures.V26());

        ShowNode(page, "sections");
        Assert.Contains("loop over snippet.sections", page.Find(".pane-identity").TextContent);
        Assert.Single(page.FindAll(".loop-body__row"));

        ShowNode(page, "section_notes");
        Assert.Equal("sections", page.Find("fluent-select.node-in-loop").GetAttribute("current-value"));
        // The nav still nests and still switches.
        Assert.Contains(page.FindAll("button.editor-nav__item--nested"), b => b.TextContent.Contains("Section notes"));
    }

    [Fact]
    public void A26Package_AHandlerFiredPastTheInertPane_WritesNoDraft_AndPublishStaysShut()
    {
        var page = RenderEditor(EditorFixtures.V26());
        ShowNode(page, "sections");

        // bUnit fires what a browser's inert pane cannot: the door behind it is shut too.
        page.Find("fluent-select.loop-ret").Change("");

        Assert.False(DraftWritten());
        Assert.True(IsDisabled(Toolbar(page, "Publish")));
        Assert.True(page.Find("section.editor-pane").HasAttribute("inert"));
    }

    [Fact]
    public void TheSeam_AuthorsA26Package_NoBanner_NoInert_PublishOpensOnAnEdit()
    {
        var page = RenderEditor(EditorFixtures.V26(), withSeam: true);

        Assert.Empty(page.FindAll(".read-only-notice"));
        Assert.False(page.Find("section.editor-pane").HasAttribute("inert"));

        ShowNode(page, "sections");
        page.Find("fluent-select.loop-ret").Change("");

        Assert.True(DraftWritten());
        Assert.False(IsDisabled(Toolbar(page, "Publish")));
    }

    [Fact]
    public void A25Package_IsUntouched()
    {
        var page = RenderEditor(EditorFixtures.V25());

        Assert.Empty(page.FindAll(".read-only-notice"));
        Assert.False(page.Find("section.editor-pane").HasAttribute("inert"));
        Assert.False(IsDisabled(Toolbar(page, "Import package")));
        Assert.False(IsDisabled(Toolbar(page, "Revert to default")));
    }
}
