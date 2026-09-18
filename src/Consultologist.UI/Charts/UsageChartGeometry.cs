using System.Globalization;

namespace Consultologist.UI.Charts;

/// <summary>
/// #732: the usage bar chart as a pure function — categories in (a day or an
/// organisation), SVG geometry out. Deterministic and unit-tested, but with one
/// difference that is the whole reason this is inline SVG rather than a canvas
/// library: it emits <em>geometry and segment roles only, never colours</em>.
/// The colours live in the component's CSS as <c>var(--consultologist-*)</c>
/// tokens, so a bar recolours with the theme automatically. A test asserts this
/// output carries no colour literal.
/// </summary>
public static class UsageChartGeometry
{
    // A fixed coordinate space; the SVG scales to its container via width=100%.
    public const double Width = 640;
    public const double Height = 200;

    // Insets: room for the y-axis tick labels on the left and the category
    // labels along the bottom.
    private const double PadLeft = 56;
    private const double PadRight = 12;
    private const double PadTop = 12;
    private const double PadBottom = 30;

    // The share of each category's slot left empty, so bars do not touch.
    private const double BarGapFraction = 0.32;

    /// <summary>One stacked segment of a bar (e.g. tokens-in below tokens-out).</summary>
    public sealed record Segment(string Role, double X, double Y, double Width, double Height, long Value);

    /// <summary>One category's bar — its label and its stacked segments, bottom-up.</summary>
    public sealed record Bar(string Label, IReadOnlyList<Segment> Segments)
    {
        public long Total => Segments.Sum(segment => segment.Value);
    }

    /// <summary>A y-axis gridline: its line position and the value it marks.</summary>
    public sealed record Tick(double Y, double Value);

    public sealed record Geometry(
        double Width,
        double Height,
        double PlotLeft,
        double PlotRight,
        double PlotTop,
        double PlotBottom,
        long Max,
        IReadOnlyList<Bar> Bars,
        IReadOnlyList<Tick> Ticks);

    /// <summary>The caller's input: a labelled category and its role/value segments.</summary>
    public sealed record Category(string Label, IReadOnlyList<CategorySegment> Segments);

    public sealed record CategorySegment(string Role, long Value);

    public static Geometry Build(IReadOnlyList<Category> categories)
    {
        var plotLeft = PadLeft;
        var plotRight = Width - PadRight;
        var plotTop = PadTop;
        var plotBottom = Height - PadBottom;
        var plotWidth = plotRight - plotLeft;
        var plotHeight = plotBottom - plotTop;

        var rawMax = categories.Count == 0
            ? 0L
            : categories.Max(category => category.Segments.Sum(segment => segment.Value));
        var max = NiceCeil(rawMax);

        var count = categories.Count;
        var step = count > 0 ? plotWidth / count : plotWidth;
        var barWidth = step * (1 - BarGapFraction);

        var bars = new List<Bar>(count);
        for (var index = 0; index < count; index++)
        {
            var category = categories[index];
            var x = plotLeft + (index * step) + ((step - barWidth) / 2);

            var segments = new List<Segment>(category.Segments.Count);
            double stacked = 0;
            foreach (var segment in category.Segments)
            {
                var height = max > 0 ? (double)segment.Value / max * plotHeight : 0;
                var y = plotBottom - stacked - height;
                segments.Add(new Segment(segment.Role, x, y, barWidth, height, segment.Value));
                stacked += height;
            }

            bars.Add(new Bar(category.Label, segments));
        }

        var ticks = new List<Tick>
        {
            new(plotBottom, 0),
            new(plotBottom - (plotHeight / 2), max / 2.0),
            new(plotTop, max),
        };

        return new Geometry(Width, Height, plotLeft, plotRight, plotTop, plotBottom, max, bars, ticks);
    }

    /// <summary>
    /// Round a scale top up to 1/2/5 × 10ⁿ so the axis and its midpoint read
    /// cleanly (3 → 5, 3,000 → 5,000, 9,000 → 10,000). Zero or negative → 0,
    /// the flat "no usage" axis.
    /// </summary>
    internal static long NiceCeil(long value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var magnitude = (long)Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = (double)value / magnitude;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    /// <summary>A coordinate as invariant text for an SVG attribute (never the current culture's comma).</summary>
    public static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // ----- #743: the line rendering of the same categories -----

    /// <summary>One plotted point of a line — its position, the value, and the category label (for the tooltip).</summary>
    public sealed record LinePoint(double X, double Y, long Value, string Label);

    /// <summary>One role's line across the categories (e.g. the tokens-in line), left to right.</summary>
    public sealed record Line(string Role, IReadOnlyList<LinePoint> Points);

    public sealed record LineGeometry(
        double Width,
        double Height,
        double PlotLeft,
        double PlotRight,
        double PlotTop,
        double PlotBottom,
        long Max,
        IReadOnlyList<Line> Lines,
        IReadOnlyList<Tick> Ticks);

    /// <summary>
    /// The same categories as one line per role rather than stacked bars.
    /// Reuses the bar chart's plot rect, ticks and nice-rounding; the one
    /// difference is the axis top: lines are <em>unstacked</em>, so the scale
    /// reaches the largest single role value (not the summed bar height), and
    /// each line uses the full plot. Emits roles and coordinates only — never a
    /// colour — so the theme keeps the colours in CSS, exactly as the bars do.
    /// </summary>
    public static LineGeometry BuildLines(IReadOnlyList<Category> categories)
    {
        var plotLeft = PadLeft;
        var plotRight = Width - PadRight;
        var plotTop = PadTop;
        var plotBottom = Height - PadBottom;
        var plotWidth = plotRight - plotLeft;
        var plotHeight = plotBottom - plotTop;

        var rawMax = categories.Count == 0
            ? 0L
            : categories.SelectMany(category => category.Segments).Select(segment => segment.Value).DefaultIfEmpty(0L).Max();
        var max = NiceCeil(rawMax);

        var count = categories.Count;
        var step = count > 0 ? plotWidth / count : plotWidth;

        // Roles in first-seen order (the legend's order), one line each.
        var roles = categories.SelectMany(category => category.Segments.Select(segment => segment.Role))
            .Distinct(StringComparer.Ordinal).ToList();

        var lines = new List<Line>(roles.Count);
        foreach (var role in roles)
        {
            var points = new List<LinePoint>(count);
            for (var index = 0; index < count; index++)
            {
                var category = categories[index];
                var value = category.Segments.FirstOrDefault(segment => segment.Role == role)?.Value ?? 0L;
                var x = plotLeft + (index * step) + (step / 2);
                var y = max > 0 ? plotBottom - ((double)value / max * plotHeight) : plotBottom;
                points.Add(new LinePoint(x, y, value, category.Label));
            }

            lines.Add(new Line(role, points));
        }

        var ticks = new List<Tick>
        {
            new(plotBottom, 0),
            new(plotBottom - (plotHeight / 2), max / 2.0),
            new(plotTop, max),
        };

        return new LineGeometry(Width, Height, plotLeft, plotRight, plotTop, plotBottom, max, lines, ticks);
    }
}
