using Bunit;
using Consultologist.UI.Components;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #882: the shared empty-state block — an icon, a title, a line and an optional
/// action, so an empty list reads as a defined state. Compact drops the card for
/// inline use, and a passthrough Class preserves existing CSS/test hooks.
/// </summary>
public class EmptyStateTests : ClientRenderTestContext
{
    [Fact]
    public void RendersTitleDescriptionAndAction()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Title, "No consults yet")
            .Add(p => p.Description, "Draft your first one to get started.")
            .Add(p => p.Action, "<a href=\"create\">Draft a consult</a>"));

        var region = cut.Find(".empty-state");
        Assert.Contains("No consults yet", region.TextContent);
        Assert.Contains("Draft your first one", region.TextContent);
        Assert.NotNull(cut.Find(".empty-state__action a"));
    }

    [Fact]
    public void Compact_AddsModifier_AndKeepsPassthroughClass()
    {
        var cut = Render<EmptyState>(parameters => parameters
            .Add(p => p.Compact, true)
            .Add(p => p.Class, "usage-empty")
            .Add(p => p.Description, "No usage yet."));

        var region = cut.Find(".empty-state");
        Assert.Contains("empty-state--compact", region.ClassList);
        Assert.Contains("usage-empty", region.ClassList);
        Assert.Contains("No usage yet.", region.TextContent);
    }
}
