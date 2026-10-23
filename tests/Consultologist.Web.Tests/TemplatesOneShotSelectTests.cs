using AngleSharp.Dom;
using Bunit;
using Bunit.Rendering;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1136: a one-shot select (a pick is an action — attach, append, add) runs its
/// handler ONCE per pick and rests on its resting option afterwards. In a browser a
/// FluentSelect with child-content options reaches ValueChanged twice per pick — the
/// select's change and the option's own click, back to back on the SAME elements —
/// and bUnit's .Change() drives only the change. Here the second half is sent to the
/// element the first half was sent to, as the browser does; the pick has recreated
/// the select by then, so that element's handler is gone — the browser drops the
/// event, bUnit says so by name (UnknownEventHandlerIdException) — and one instance
/// results. A pick through a re-queried element is a new gesture, and counts.
/// </summary>
public class TemplatesOneShotSelectTests : ClientRenderTestContext
{
    private const string NodeAttach = "fluent-select[aria-label='Attach a macro to assessment']";
    private const string MacroAttach = "fluent-select[aria-label='Attach macro tool_note to a node']";

    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    /// <summary>The select and one option as rendered NOW — FindAll's elements stay what they were.</summary>
    private static (IElement Select, IElement Option) Rendered(IRenderedComponent<Templates> page, string select, string value)
    {
        var element = page.FindAll(select)[0];
        return (element, element.QuerySelector($"fluent-option[value='{value}']")!);
    }

    private static int NodeRowsOf(IRenderedComponent<Templates> page, string macroId) =>
        page.FindAll(".node-macro-row .binding-row__source").Count(b => b.TextContent.Trim() == macroId);

    // ----- the pair adds once -----

    [Fact]
    public async Task OnTheNodePane_TheChangeThenTheOptionClick_AttachOnce()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");
        var (select, option) = Rendered(page, NodeAttach, "tool_note");

        select.Change("tool_note");
        // The browser's second half: the option's own click, on the element it clicked.
        await Assert.ThrowsAsync<UnknownEventHandlerIdException>(() => option.ClickAsync());

        Assert.Equal(1, NodeRowsOf(page, "tool_note"));
    }

    [Fact]
    public async Task OnTheNodePane_TheOptionClickThenTheChange_AttachOnce()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");
        var (select, option) = Rendered(page, NodeAttach, "tool_note");

        option.Click();
        await Assert.ThrowsAsync<UnknownEventHandlerIdException>(() => select.ChangeAsync("tool_note"));

        Assert.Equal(1, NodeRowsOf(page, "tool_note"));
    }

    [Fact]
    public async Task OnTheMacroPane_TheChangeThenTheOptionClick_AttachOnce()
    {
        var page = RenderEditor();
        Navigate(page, "tool_note");
        var (select, option) = Rendered(page, MacroAttach, "assessment");

        select.Change("assessment");
        await Assert.ThrowsAsync<UnknownEventHandlerIdException>(() => option.ClickAsync());

        Assert.Single(page.FindAll(".macro-where [data-instance-node='assessment']"));
    }

    // ----- it rests -----

    [Fact]
    public void AfterAPick_TheSelectRests_OnItsRestingOption()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");
        var (_, option) = Rendered(page, NodeAttach, "tool_note");

        // The option's click alone toggles the option's own Selected — the half .Change() never drives.
        option.Click();

        var select = page.Find(NodeAttach);
        Assert.Equal("", select.GetAttribute("current-value") ?? "");
        Assert.False(select.QuerySelector("fluent-option[value='tool_note']")!.HasAttribute("selected"));
        Assert.Equal("attach a macro…", select.QuerySelector("fluent-option[value='']")!.TextContent.Trim());
    }

    // ----- a deliberate repeat is not swallowed -----

    [Fact]
    public void PickingTheSameMacroAgain_AttachesASecondInstance()
    {
        // At 25 a macro may be placed before AND after a node: the second pick is a
        // second gesture on the recreated select, never the dropped half of a pair.
        var page = RenderEditor();
        ShowNode(page, "assessment");

        page.Find(NodeAttach).Change("tool_note");
        page.Find(NodeAttach).Change("tool_note");

        Assert.Equal(2, NodeRowsOf(page, "tool_note"));
    }

    // ----- the handler follows the pane's subject -----

    [Fact]
    public void SwitchingMacroPanes_TheOptionClick_AttachesTheShownMacro()
    {
        // The option's click path keeps the list context's FIRST copy of the callback;
        // it points at the component, which reads OnPick live, so closing's select
        // attaches closing even where the instance outlived tool_note's pane.
        var page = RenderEditor();
        Navigate(page, "tool_note");
        Navigate(page, "closing");
        const string attach = "fluent-select[aria-label='Attach macro closing to a node']";

        page.Find($"{attach} fluent-option[value='assessment']").Click();

        // closing already sits after assessment; the pick adds its before instance there.
        Assert.Equal(2, page.FindAll(".macro-where [data-instance-node='assessment']").Count);
        Navigate(page, "tool_note");
        Assert.Empty(page.FindAll(".macro-where [data-instance-node='assessment']"));
    }
}
