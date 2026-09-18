using Consultologist.UI.Charts;

namespace Consultologist.Web.Tests;

/// <summary>
/// #743: the line rendering of a per-day series — an accessible role="img"
/// image with a polyline per role, class-hooked colours (never a literal), and
/// escaped category labels, mirroring <see cref="UsageChartSvgTests"/>.
/// </summary>
public class UsageChartLineSvgTests
{
    private static string Render(params UsageChartGeometry.Category[] categories) =>
        UsageChartLineSvg.Build(
            UsageChartGeometry.BuildLines(categories),
            "N0",
            new[] { ("tokens-in", "Tokens in"), ("tokens-out", "Tokens out") },
            "Tokens per day: a line over 2 days, peak 3,000 tokens.");

    private static UsageChartGeometry.Category Cat(string label, params (string Role, long Value)[] segments) =>
        new(label, segments.Select(s => new UsageChartGeometry.CategorySegment(s.Role, s.Value)).ToList());

    [Fact]
    public void Build_IsAnAccessibleImage_WithALinePerRole()
    {
        var svg = Render(
            Cat("2026-09-01", ("tokens-in", 2000), ("tokens-out", 1000)),
            Cat("2026-09-02", ("tokens-in", 3000), ("tokens-out", 1500)));

        Assert.Contains("role=\"img\"", svg);
        Assert.Contains("aria-label=\"Tokens per day", svg);
        Assert.Contains("usage-chart__line--tokens-in", svg);
        Assert.Contains("usage-chart__line--tokens-out", svg);
        // Two roles → two polylines.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(svg, "<polyline ").Count);
        // Two roles × two days → four point markers.
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(svg, "<circle ").Count);
    }

    [Fact]
    public void Build_EmitsNoColourLiteral()
    {
        var svg = Render(
            Cat("2026-09-01", ("tokens-in", 2000), ("tokens-out", 1000)),
            Cat("2026-09-02", ("tokens-in", 3000), ("tokens-out", 1500)));

        Assert.DoesNotContain("#", svg);
        Assert.DoesNotContain("rgb", svg);
        Assert.DoesNotContain("fill:", svg);
        Assert.DoesNotContain("stroke:", svg);
    }

    [Fact]
    public void Build_EscapesCategoryLabels()
    {
        var svg = Render(Cat("A & B <script>", ("tokens-in", 10)));

        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&amp;", svg);
        Assert.Contains("&lt;script&gt;", svg);
    }
}
