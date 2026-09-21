using System.Collections;
using System.Reflection;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #812: each editable pane's "Discard changes in this pane" reverts only that
/// pane's pending edits, arm→confirm, leaving every other pane's edits intact.
/// The V6WithValue fixture has a `draft-section` node/prompt and a scalar value.
/// </summary>
public class TemplatesPaneDiscardTests : ClientRenderTestContext
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithValue());
        return Render<Templates>();
    }

    private static IDictionary Dict(IRenderedComponent<Templates> page, string field) =>
        (IDictionary)typeof(Templates).GetField(field, Members)!.GetValue(page.Instance)!;

    private static T Get<T>(IRenderedComponent<Templates> page, string field) =>
        (T)typeof(Templates).GetField(field, Members)!.GetValue(page.Instance)!;

    private static void Set(IRenderedComponent<Templates> page, string field, object? value) =>
        typeof(Templates).GetField(field, Members)!.SetValue(page.Instance, value);

    // Put the editor on a pane without going through Select (which would clear
    // the arm) — set the shared session key and re-render.
    private void ShowPane(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    [Fact]
    public async Task DiscardingANode_RevertsOnlyThatNode_KeepingOtherEdits()
    {
        var page = RenderEditor();

        // A node edit (aggregateEdits is part of NodeHasPending), a Package title
        // edit (a different pane), and a spec bump (no pane at all).
        await page.InvokeAsync(() =>
        {
            Dict(page, "aggregateEdits")["draft-section"] = new List<string> { "node:draft-section" };
            Set(page, "titleEdit", "Edited title");
            Set(page, "specVersionBump", 7);
        });
        ShowPane(page, "node:draft-section");

        // Arm, then confirm.
        await page.Find(".pane-discard-button").ClickAsync(new());
        await page.Find(".pane-discard-button").ClickAsync(new());

        Assert.Empty(Dict(page, "aggregateEdits"));            // the node's edit is gone
        Assert.Equal("Edited title", Get<string?>(page, "titleEdit")); // the Package edit survives
        Assert.Equal(7, Get<int?>(page, "specVersionBump"));   // the pane-less spec bump survives
    }

    [Fact]
    public async Task OneClickArms_WithoutReverting()
    {
        var page = RenderEditor();
        await page.InvokeAsync(() => Dict(page, "aggregateEdits")["draft-section"] = new List<string>());
        ShowPane(page, "node:draft-section");

        await page.Find(".pane-discard-button").ClickAsync(new());

        Assert.NotEmpty(Dict(page, "aggregateEdits"));          // not yet reverted
        Assert.Contains("Confirm discard", page.Find(".pane-discard-button").TextContent);
    }

    [Fact]
    public async Task DiscardingASingletonPane_RevertsOnlyIt()
    {
        var page = RenderEditor();
        await page.InvokeAsync(() =>
        {
            Set(page, "titleEdit", "Edited title");
            Dict(page, "aggregateEdits")["draft-section"] = new List<string>();
        });
        ShowPane(page, "package"); // PackageKey

        await page.Find(".pane-discard-button").ClickAsync(new());
        await page.Find(".pane-discard-button").ClickAsync(new());

        Assert.Null(Get<string?>(page, "titleEdit"));           // Package edit reverted
        Assert.NotEmpty(Dict(page, "aggregateEdits"));          // the node's edit survives
    }

    [Fact]
    public void WithNoPendingInThePane_NoDiscardButtonShows()
    {
        var page = RenderEditor();
        ShowPane(page, "node:draft-section");

        Assert.Empty(page.FindAll(".pane-discard-button"));
    }
}
