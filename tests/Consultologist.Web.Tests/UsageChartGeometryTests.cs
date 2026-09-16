using Consultologist.UI.Charts;

namespace Consultologist.Web.Tests;

/// <summary>
/// #732: the usage chart geometry as a pure function — bars scaled to a nice
/// axis, segments stacked bottom-up, empty- and zero-safe, and (the point of
/// inline SVG) carrying no colour of its own.
/// </summary>
public class UsageChartGeometryTests
{
    private static UsageChartGeometry.Category Cat(string label, params (string Role, long Value)[] segments) =>
        new(label, segments.Select(s => new UsageChartGeometry.CategorySegment(s.Role, s.Value)).ToList());

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 5)]
    [InlineData(5, 5)]
    [InlineData(6, 10)]
    [InlineData(3000, 5000)]
    [InlineData(9000, 10000)]
    public void NiceCeil_RoundsToOneTwoOrFive(long value, long expected) =>
        Assert.Equal(expected, UsageChartGeometry.NiceCeil(value));

    [Fact]
    public void Build_ScalesTheTallestBarToTheNiceAxisTop()
    {
        var geometry = UsageChartGeometry.Build(new[]
        {
            Cat("Mon", ("consults", 3)),
            Cat("Tue", ("consults", 1)),
        });

        // Tallest total is 3 → axis top 5.
        Assert.Equal(5, geometry.Max);

        var tallest = geometry.Bars[0].Segments[0];
        var plotHeight = geometry.PlotBottom - geometry.PlotTop;
        Assert.Equal(3.0 / 5.0 * plotHeight, tallest.Height, precision: 6);
        // Its top edge sits height above the baseline.
        Assert.Equal(geometry.PlotBottom - tallest.Height, tallest.Y, precision: 6);
    }

    [Fact]
    public void Build_StacksSegmentsBottomUp_WithoutOverlap()
    {
        var geometry = UsageChartGeometry.Build(new[]
        {
            Cat("Mon", ("tokens-in", 2000), ("tokens-out", 3000)),
        });

        var bar = geometry.Bars[0];
        var inSeg = bar.Segments[0];
        var outSeg = bar.Segments[1];

        // Same column, same width.
        Assert.Equal(inSeg.X, outSeg.X);
        Assert.Equal(inSeg.Width, outSeg.Width);
        // First segment sits on the baseline; the second stacks exactly on top.
        Assert.Equal(geometry.PlotBottom, inSeg.Y + inSeg.Height, precision: 6);
        Assert.Equal(inSeg.Y, outSeg.Y + outSeg.Height, precision: 6);
        Assert.Equal(5000, bar.Total);
    }

    [Fact]
    public void Build_IsEmptySafe()
    {
        var geometry = UsageChartGeometry.Build(Array.Empty<UsageChartGeometry.Category>());

        Assert.Empty(geometry.Bars);
        Assert.Equal(0, geometry.Max);
        // Ticks still exist (0/mid/max all zero) — the axis draws flat, not NaN.
        Assert.All(geometry.Ticks, tick => Assert.False(double.IsNaN(tick.Y)));
    }

    [Fact]
    public void Build_AllZero_DrawsNoHeight_NeverDividesByZero()
    {
        var geometry = UsageChartGeometry.Build(new[] { Cat("Mon", ("consults", 0)) });

        Assert.Equal(0, geometry.Max);
        Assert.Equal(0, geometry.Bars[0].Segments[0].Height);
    }

    [Fact]
    public void Build_BarsStayInsideThePlotArea()
    {
        var geometry = UsageChartGeometry.Build(new[]
        {
            Cat("A", ("consults", 4)),
            Cat("B", ("consults", 2)),
            Cat("C", ("consults", 5)),
        });

        foreach (var segment in geometry.Bars.SelectMany(bar => bar.Segments))
        {
            Assert.True(segment.X >= geometry.PlotLeft);
            Assert.True(segment.X + segment.Width <= geometry.PlotRight + 0.0001);
            Assert.True(segment.Y >= geometry.PlotTop - 0.0001);
        }
    }

    [Fact]
    public void Build_EmitsNoColourLiteral()
    {
        // The whole reason for inline SVG: colour lives in CSS, not here, so a
        // bar recolours with the theme. Roles are class hooks, never colours.
        var geometry = UsageChartGeometry.Build(new[]
        {
            Cat("Mon", ("tokens-in", 2000), ("tokens-out", 3000)),
        });

        var text = System.Text.Json.JsonSerializer.Serialize(geometry);
        Assert.DoesNotContain("#", text);
        Assert.DoesNotContain("rgb", text);
        Assert.DoesNotContain("var(", text);
    }
}
