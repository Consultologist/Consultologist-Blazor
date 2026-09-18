using System.Text;
using System.Web;

namespace Consultologist.UI.Charts;

/// <summary>
/// #743: the line rendering of a per-day series — the same pure-function,
/// theme-class-only approach as <see cref="UsageChartSvg"/>, but a
/// <c>&lt;polyline&gt;</c> per role plus <c>&lt;circle&gt;</c> markers instead of
/// stacked <c>&lt;rect&gt;</c>s. Colours are class names only (never a literal),
/// so the component's CSS keeps them on the theme tokens; a test asserts the
/// absence of any colour. The gridlines, axis labels and x-axis category labels
/// are drawn identically to the bar chart.
/// </summary>
public static class UsageChartLineSvg
{
    public static string Build(
        UsageChartGeometry.LineGeometry geometry,
        string valueFormat,
        IReadOnlyList<(string Role, string Label)> legend,
        string ariaLabel)
    {
        string RoleLabel(string role) =>
            legend.FirstOrDefault(item => item.Role == role).Label ?? role;

        var svg = new StringBuilder();
        svg.Append("<svg class=\"usage-chart__svg\" viewBox=\"0 0 ")
           .Append(UsageChartGeometry.N(geometry.Width)).Append(' ').Append(UsageChartGeometry.N(geometry.Height))
           .Append("\" preserveAspectRatio=\"xMidYMid meet\" role=\"img\" aria-label=\"")
           .Append(Attr(ariaLabel)).Append("\">");

        // y-axis gridlines and their value labels (identical to the bar chart).
        foreach (var tick in geometry.Ticks)
        {
            svg.Append("<line class=\"usage-chart__grid\" x1=\"").Append(UsageChartGeometry.N(geometry.PlotLeft))
               .Append("\" y1=\"").Append(UsageChartGeometry.N(tick.Y))
               .Append("\" x2=\"").Append(UsageChartGeometry.N(geometry.PlotRight))
               .Append("\" y2=\"").Append(UsageChartGeometry.N(tick.Y)).Append("\" />");
            svg.Append("<text class=\"usage-chart__axis-label\" x=\"").Append(UsageChartGeometry.N(geometry.PlotLeft - 6))
               .Append("\" y=\"").Append(UsageChartGeometry.N(tick.Y + 3))
               .Append("\" text-anchor=\"end\">").Append(Text(tick.Value.ToString(valueFormat))).Append("</text>");
        }

        // x-axis category labels, positioned from the first line's points.
        var labelPoints = geometry.Lines.Count > 0
            ? geometry.Lines[0].Points
            : (IReadOnlyList<UsageChartGeometry.LinePoint>)Array.Empty<UsageChartGeometry.LinePoint>();
        var count = labelPoints.Count;
        for (var index = 0; index < count; index++)
        {
            if (!UsageChartSvg.ShowsLabel(index, count))
            {
                continue;
            }

            var point = labelPoints[index];
            svg.Append("<text class=\"usage-chart__xlabel\" x=\"").Append(UsageChartGeometry.N(point.X))
               .Append("\" y=\"").Append(UsageChartGeometry.N(geometry.PlotBottom + 16))
               .Append("\" text-anchor=\"middle\">").Append(Text(UsageChartSvg.Short(point.Label)))
               .Append("<title>").Append(Text(point.Label)).Append("</title></text>");
        }

        // One polyline per role, then its point markers (the last one emphasised).
        foreach (var line in geometry.Lines)
        {
            svg.Append("<polyline class=\"usage-chart__line usage-chart__line--").Append(Attr(line.Role)).Append("\" points=\"");
            for (var i = 0; i < line.Points.Count; i++)
            {
                if (i > 0)
                {
                    svg.Append(' ');
                }

                svg.Append(UsageChartGeometry.N(line.Points[i].X)).Append(',').Append(UsageChartGeometry.N(line.Points[i].Y));
            }

            svg.Append("\" />");

            for (var i = 0; i < line.Points.Count; i++)
            {
                var point = line.Points[i];
                var radius = i == line.Points.Count - 1 ? "3.5" : "2.5";
                svg.Append("<circle class=\"usage-chart__dot usage-chart__dot--").Append(Attr(line.Role))
                   .Append("\" cx=\"").Append(UsageChartGeometry.N(point.X))
                   .Append("\" cy=\"").Append(UsageChartGeometry.N(point.Y))
                   .Append("\" r=\"").Append(radius).Append("\">")
                   .Append("<title>").Append(Text($"{point.Label} — {point.Value.ToString(valueFormat)} {RoleLabel(line.Role)}")).Append("</title>")
                   .Append("</circle>");
            }
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string Text(string value) => HttpUtility.HtmlEncode(value);

    private static string Attr(string value) => HttpUtility.HtmlAttributeEncode(value);
}
