using Consultologist.UI.Charts;

namespace Consultologist.Web.Tests;

/// <summary>
/// #732: the geometry rendered to SVG markup — an accessible role="img" image,
/// class-hooked colours (never a literal), and category labels safely escaped.
/// </summary>
public class UsageChartSvgTests
{
    private static string Render(params UsageChartGeometry.Category[] categories) =>
        UsageChartSvg.Build(
            UsageChartGeometry.Build(categories),
            "N0",
            new[] { ("tokens-in", "Tokens in"), ("tokens-out", "Tokens out") },
            "Tokens per day: 1 bars, peak 5,000 tokens.");

    private static UsageChartGeometry.Category Cat(string label, params (string Role, long Value)[] segments) =>
        new(label, segments.Select(s => new UsageChartGeometry.CategorySegment(s.Role, s.Value)).ToList());

    [Fact]
    public void Build_IsAnAccessibleImage_WithRoleClasses()
    {
        var svg = Render(Cat("2026-09-01", ("tokens-in", 2000), ("tokens-out", 3000)));

        Assert.Contains("role=\"img\"", svg);
        Assert.Contains("aria-label=\"Tokens per day", svg);
        Assert.Contains("usage-chart__seg--tokens-in", svg);
        Assert.Contains("usage-chart__seg--tokens-out", svg);
        // Two segments → two rects.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, "<rect ").Count);
    }

    [Fact]
    public void Build_EmitsNoColourLiteral()
    {
        var svg = Render(Cat("2026-09-01", ("tokens-in", 2000), ("tokens-out", 3000)));

        Assert.DoesNotContain("#", svg);
        Assert.DoesNotContain("rgb", svg);
        Assert.DoesNotContain("fill:", svg);
    }

    [Fact]
    public void Build_EscapesCategoryLabels()
    {
        var svg = Render(Cat("A & B <script>", ("tokens-in", 10)));

        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&amp;", svg);
        Assert.Contains("&lt;script&gt;", svg);
    }

    [Fact]
    public void ShowsLabel_OnlyEndsWhenManyDays_AllWhenFew()
    {
        // 30 days: only the first and last are labelled.
        Assert.True(UsageChartSvg.ShowsLabel(0, 30));
        Assert.True(UsageChartSvg.ShowsLabel(29, 30));
        Assert.False(UsageChartSvg.ShowsLabel(15, 30));
        // A handful of orgs: all labelled.
        Assert.True(UsageChartSvg.ShowsLabel(1, 4));
    }
}
