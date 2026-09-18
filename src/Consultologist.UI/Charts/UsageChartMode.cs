namespace Consultologist.UI.Charts;

/// <summary>
/// #743: how a <see cref="Components.UsageBarChart"/> renders a per-day series —
/// the shipped stacked <see cref="Bar"/>s, or a <see cref="Line"/> per role
/// (tokens-in / tokens-out drawn as two lines). The per-organisation charts are
/// categorical and always render as bars.
/// </summary>
public enum UsageChartMode
{
    Bar,
    Line,
}
