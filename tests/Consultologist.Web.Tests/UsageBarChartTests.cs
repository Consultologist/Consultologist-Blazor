using Bunit;
using Consultologist.UI.Charts;
using Consultologist.UI.Components;

namespace Consultologist.Web.Tests;

/// <summary>
/// #732: the shared usage chart — an accessible inline SVG (role="img" + a
/// one-line summary, the table beside it carrying the numbers), a bar segment
/// per value, a text legend so it is never colour-only, and a said empty state.
/// </summary>
public class UsageBarChartTests : ClientRenderTestContext
{
    private static UsageChartGeometry.Category Cat(string label, params (string Role, long Value)[] segments) =>
        new(label, segments.Select(s => new UsageChartGeometry.CategorySegment(s.Role, s.Value)).ToList());

    [Fact]
    public void Renders_AnAccessibleSvg_WithARectPerSegment()
    {
        var chart = Render<UsageBarChart>(parameters => parameters
            .Add(p => p.Title, "Tokens per day")
            .Add(p => p.Unit, "tokens")
            .Add(p => p.Categories, new[]
            {
                Cat("2026-09-01", ("tokens-in", 1000), ("tokens-out", 300)),
                Cat("2026-09-02", ("tokens-in", 2000), ("tokens-out", 500)),
            })
            .Add(p => p.Legend, new[] { ("tokens-in", "Tokens in"), ("tokens-out", "Tokens out") }));

        var svg = chart.Find("svg.usage-chart__svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        Assert.Contains("Tokens per day", svg.GetAttribute("aria-label"));
        Assert.Contains("tokens", svg.GetAttribute("aria-label"));

        // Two categories × two segments each.
        Assert.Equal(4, chart.FindAll("rect.usage-chart__seg").Count);

        // The legend carries the role labels as text, not colour alone.
        var legend = chart.Find(".usage-chart__legend").TextContent;
        Assert.Contains("Tokens in", legend);
        Assert.Contains("Tokens out", legend);

        // The end days anchor the x-axis (each <text> also carries a full-label
        // <title> child, so match on Contains, not equality).
        var xlabels = chart.FindAll(".usage-chart__xlabel").Select(l => l.TextContent).ToList();
        Assert.Contains(xlabels, text => text.Contains("2026-09-01"));
        Assert.Contains(xlabels, text => text.Contains("2026-09-02"));
    }

    [Fact]
    public void LineMode_Renders_APolylinePerRole_NotRects()
    {
        // #743: the same categories in line mode — a polyline per role, dot
        // markers, the legend intact, and no bar rects.
        var chart = Render<UsageBarChart>(parameters => parameters
            .Add(p => p.Title, "Tokens per day")
            .Add(p => p.Unit, "tokens")
            .Add(p => p.Mode, UsageChartMode.Line)
            .Add(p => p.Categories, new[]
            {
                Cat("2026-09-01", ("tokens-in", 1000), ("tokens-out", 300)),
                Cat("2026-09-02", ("tokens-in", 2000), ("tokens-out", 500)),
            })
            .Add(p => p.Legend, new[] { ("tokens-in", "Tokens in"), ("tokens-out", "Tokens out") }));

        var svg = chart.Find("svg.usage-chart__svg");
        Assert.Equal("img", svg.GetAttribute("role"));
        // Two roles → two polylines, four point markers; no bars.
        Assert.Equal(2, chart.FindAll("polyline.usage-chart__line").Count);
        Assert.Equal(4, chart.FindAll("circle.usage-chart__dot").Count);
        Assert.Empty(chart.FindAll("rect.usage-chart__seg"));
        // The legend still names both series.
        var legend = chart.Find(".usage-chart__legend").TextContent;
        Assert.Contains("Tokens in", legend);
        Assert.Contains("Tokens out", legend);
    }

    [Fact]
    public void EmptyCategories_SayTheEmptyState_NoSvg()
    {
        var chart = Render<UsageBarChart>(parameters => parameters
            .Add(p => p.Title, "Consults per day")
            .Add(p => p.Categories, Array.Empty<UsageChartGeometry.Category>()));

        Assert.Empty(chart.FindAll("svg"));
        Assert.Contains("No usage in this window", chart.Find(".usage-chart__empty").TextContent);
    }

    [Fact]
    public void AllZeroCategories_AlsoSayTheEmptyState()
    {
        var chart = Render<UsageBarChart>(parameters => parameters
            .Add(p => p.Title, "Consults per day")
            .Add(p => p.Categories, new[] { Cat("2026-09-01", ("consults", 0)), Cat("2026-09-02", ("consults", 0)) }));

        Assert.Empty(chart.FindAll("svg"));
        Assert.Contains("No usage in this window", chart.Find(".usage-chart__empty").TextContent);
    }

    [Fact]
    public void LongCategoryLabels_AreTruncatedButKeepAFullTitle()
    {
        var chart = Render<UsageBarChart>(parameters => parameters
            .Add(p => p.Title, "Consults by organisation")
            .Add(p => p.Categories, new[] { Cat("Organisation contoso-tenant-guid", ("consults", 4)) }));

        var label = chart.Find(".usage-chart__xlabel");
        // The visible text node is the truncated label; its <title> child holds the full one.
        Assert.EndsWith("…", label.FirstChild!.TextContent.Trim());
        Assert.Contains("Organisation contoso-tenant-guid", chart.Find(".usage-chart__xlabel title").TextContent);
    }
}
