using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Consultologist.Web.Tests;

/// <summary>
/// #448: the picker is a Fluent tree. These drive it the way a person does —
/// open the trigger, read the nodes, select a version — through the DOM,
/// since bUnit can raise the tree item's selected-change event.
/// </summary>
internal static class PickerTree
{
    public static void Open<T>(IRenderedComponent<T> cut) where T : class, IComponent =>
        cut.Find("button[aria-label='Workflow package']").Click();

    public static string Shown<T>(IRenderedComponent<T> cut) where T : class, IComponent =>
        cut.Find(".package-picker__ref").TextContent.Trim();

    /// <summary>Every tree item, in document order, as (id, label).</summary>
    public static IReadOnlyList<(string Id, string Label)> Nodes<T>(IRenderedComponent<T> cut) where T : class, IComponent =>
        cut.FindAll("fluent-tree-item").Select(item => (item.GetAttribute("id")!, item.GetAttribute("aria-label") ?? string.Empty)).ToList();

    /// <summary>The refs offered — version nodes only.</summary>
    public static IReadOnlyList<string> Refs<T>(IRenderedComponent<T> cut) where T : class, IComponent =>
        Nodes(cut).Where(node => node.Id.Contains('@')).Select(node => node.Id).ToList();

    /// <summary>The package leaves' labels, in document order.</summary>
    public static IReadOnlyList<string> Packages<T>(IRenderedComponent<T> cut) where T : class, IComponent =>
        Nodes(cut).Where(node => node.Id.StartsWith("package:", StringComparison.Ordinal)).Select(node => node.Label).ToList();

    public static IReadOnlyList<string> Folders<T>(IRenderedComponent<T> cut) where T : class, IComponent =>
        Nodes(cut).Where(node => node.Id.StartsWith("folder:", StringComparison.Ordinal)).Select(node => node.Label).ToList();

    public static string LabelOf<T>(IRenderedComponent<T> cut, string id) where T : class, IComponent =>
        Nodes(cut).Single(node => node.Id == id).Label;

    /// <summary>
    /// Fluent marks selection on the web component through JS, not an
    /// attribute, so the observable fact is the item's Selected state.
    /// </summary>
    public static IReadOnlyList<string> SelectedRefs<T>(IRenderedComponent<T> cut) where T : class, IComponent =>
        cut.FindComponents<FluentTreeItem>()
            .Where(item => item.Instance.Selected && item.Instance.Id is { } id && id.Contains('@'))
            .Select(item => item.Instance.Id!)
            .ToList();

    public static Task SelectAsync<T>(IRenderedComponent<T> cut, string packageRef) where T : class, IComponent =>
        cut.FindAll("fluent-tree-item").Single(item => item.GetAttribute("id") == packageRef)
            .TriggerEventAsync("onselectedchange", new TreeChangeEventArgs { AffectedId = packageRef, Selected = true });
}
