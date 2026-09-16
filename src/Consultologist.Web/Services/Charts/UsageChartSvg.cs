using System.Text;
using System.Web;

namespace Consultologist.Web.Services.Charts;

/// <summary>
/// #732: the geometry rendered to an SVG markup string — a pure function, the
/// way <see cref="Provenance.RunDagDiagram"/> builds Mermaid text. Building the
/// string (rather than authoring the SVG in the .razor) is also the only way to
/// emit an SVG <c>&lt;text&gt;</c> element, which Razor reserves as a control
/// keyword. Colours are class names only — never a literal — so the component's
/// CSS keeps them on the theme tokens; a test asserts the absence of any colour.
/// </summary>
public static class UsageChartSvg
{
    public static string Build(
        UsageChartGeometry.Geometry geometry,
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

        // y-axis gridlines and their value labels.
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

        // Bars: one column per category, segments stacked bottom-up.
        var count = geometry.Bars.Count;
        for (var index = 0; index < count; index++)
        {
            var bar = geometry.Bars[index];
            foreach (var segment in bar.Segments)
            {
                svg.Append("<rect class=\"usage-chart__seg usage-chart__seg--").Append(Attr(segment.Role))
                   .Append("\" x=\"").Append(UsageChartGeometry.N(segment.X))
                   .Append("\" y=\"").Append(UsageChartGeometry.N(segment.Y))
                   .Append("\" width=\"").Append(UsageChartGeometry.N(segment.Width))
                   .Append("\" height=\"").Append(UsageChartGeometry.N(segment.Height)).Append("\">")
                   .Append("<title>").Append(Text($"{bar.Label} — {segment.Value.ToString(valueFormat)} {RoleLabel(segment.Role)}")).Append("</title>")
                   .Append("</rect>");
            }

            if (ShowsLabel(index, count))
            {
                var centre = bar.Segments.Count > 0 ? bar.Segments[0].X + (bar.Segments[0].Width / 2) : geometry.PlotLeft;
                svg.Append("<text class=\"usage-chart__xlabel\" x=\"").Append(UsageChartGeometry.N(centre))
                   .Append("\" y=\"").Append(UsageChartGeometry.N(geometry.PlotBottom + 16))
                   .Append("\" text-anchor=\"middle\">").Append(Text(Short(bar.Label)))
                   .Append("<title>").Append(Text(bar.Label)).Append("</title></text>");
            }
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    // Show the first and last category label always; every one when few enough
    // not to overlap. Middle days stay unlabelled — the ends anchor the axis.
    internal static bool ShowsLabel(int index, int count) =>
        count <= 12 || index == 0 || index == count - 1;

    internal static string Short(string label) =>
        label.Length <= 14 ? label : label[..13] + "…";

    private static string Text(string value) => HttpUtility.HtmlEncode(value);

    private static string Attr(string value) => HttpUtility.HtmlAttributeEncode(value);
}
