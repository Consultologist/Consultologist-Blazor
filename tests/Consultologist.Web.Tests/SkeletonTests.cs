using Bunit;
using Consultologist.UI.Components;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #882: the content-shaped loading placeholders. Each is an announced status
/// region (the shimmer bars have no accessible name) and draws one fluent-skeleton
/// per bar so a load reads as content taking shape rather than a bare spinner.
/// </summary>
public class SkeletonTests : ClientRenderTestContext
{
    [Fact]
    public void SkeletonText_DrawsOneBarPerLine_AsAStatusRegion()
    {
        var cut = Render<SkeletonText>(parameters => parameters.Add(p => p.Lines, 3));

        var region = cut.Find(".skeleton-text");
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal(3, cut.FindAll("fluent-skeleton").Count);
    }

    [Fact]
    public void SkeletonList_DrawsTwoBarsPerRow_AsAStatusRegion()
    {
        var cut = Render<SkeletonList>(parameters => parameters.Add(p => p.Rows, 4));

        var region = cut.Find(".skeleton-list");
        Assert.Equal("status", region.GetAttribute("role"));
        Assert.Equal(4, cut.FindAll(".skeleton-list__row").Count);
        Assert.Equal(8, cut.FindAll("fluent-skeleton").Count);
    }
}
