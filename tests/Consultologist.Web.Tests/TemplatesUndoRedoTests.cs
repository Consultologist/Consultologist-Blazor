using System.Collections;
using System.Reflection;
using Bunit;
using Consultologist.Web.Pages;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #815: Undo/Redo steps through committed pending edits, persisted beside the
/// draft. Uses a title edit (a real pending field that round-trips through
/// ApplyDraft) rather than a synthetic probe, which ApplyDraft would drop.
/// </summary>
public class TemplatesUndoRedoTests : ClientRenderTestContext
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const string HistoryKey = "workflow-editor-history:acct-1234567890ab@v2026.07.1";

    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V6WithValue());
        return Render<Templates>();
    }

    private static int PendingCount(IRenderedComponent<Templates> p) =>
        (int)typeof(Templates).GetProperty("PendingCount", Members)!.GetValue(p.Instance)!;

    private static string? Title(IRenderedComponent<Templates> p) =>
        (string?)typeof(Templates).GetField("titleEdit", Members)!.GetValue(p.Instance);

    private static int Count(IRenderedComponent<Templates> p, string field) =>
        ((ICollection)typeof(Templates).GetField(field, Members)!.GetValue(p.Instance)!).Count;

    private static Task Invoke(IRenderedComponent<Templates> p, string method) =>
        p.InvokeAsync(() => (Task)typeof(Templates).GetMethod(method, Members)!.Invoke(p.Instance, null)!);

    // A real pending edit: set the title field, then run the change choke-point.
    private static async Task Edit(IRenderedComponent<Templates> p, string title)
    {
        typeof(Templates).GetField("titleEdit", Members)!.SetValue(p.Instance, title);
        await Invoke(p, "PendingChangedAsync");
    }

    [Fact]
    public async Task Undo_ThenRedo_RoundTripsAnEdit()
    {
        var page = RenderEditor();
        await Edit(page, "Edited title");
        Assert.Equal(1, PendingCount(page));

        await Invoke(page, "UndoAsync");
        Assert.Equal(0, PendingCount(page));
        Assert.Null(Title(page));

        await Invoke(page, "RedoAsync");
        Assert.Equal(1, PendingCount(page));
        Assert.Equal("Edited title", Title(page));
    }

    [Fact]
    public async Task ANewEdit_ClearsTheRedoStack()
    {
        var page = RenderEditor();
        await Edit(page, "First");
        await Invoke(page, "UndoAsync");
        Assert.Equal(1, Count(page, "redoStack"));

        await Edit(page, "Second");
        Assert.Equal(0, Count(page, "redoStack"));
    }

    [Fact]
    public void Buttons_AreDisabledWithNoHistory()
    {
        var page = RenderEditor();

        Assert.True(page.Find(".undo-button").HasAttribute("disabled"));
        Assert.True(page.Find(".redo-button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task AnEdit_EnablesUndo_AndPersistsTheHistory()
    {
        var page = RenderEditor();
        await Edit(page, "Edited");
        page.Render();

        Assert.False(page.Find(".undo-button").HasAttribute("disabled"));
        Assert.Contains(
            JSInterop.Invocations["localStorage.setItem"],
            invocation => (invocation.Arguments[0] as string) == HistoryKey);
    }

    [Fact]
    public void History_RestoresOnLoad()
    {
        // A saved history with one undo step (back to the clean state).
        JSInterop.Setup<string?>("localStorage.getItem", HistoryKey)
            .SetResult("""{"Version":15,"Undo":[null],"Redo":[],"Current":null}""");

        var page = RenderEditor(); // LoadAsync → RestoreHistoryAsync reads the stub

        Assert.Equal(1, Count(page, "undoStack"));
        Assert.False(page.Find(".undo-button").HasAttribute("disabled"));
    }
}
