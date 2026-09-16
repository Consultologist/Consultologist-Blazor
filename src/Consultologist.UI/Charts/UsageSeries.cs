using System.Globalization;

namespace Consultologist.UI.Charts;

/// <summary>
/// #732: the served usage rows are <em>sparse</em> — a day with no activity is
/// absent from the response by design. A time series must not draw a 30-day
/// window as however-many bars happened to have activity, so this fills the
/// gaps: one point per calendar day in [from..to] inclusive, zero where the day
/// is absent. Both the profile's own days and the operators' aggregate daily
/// series flow through here after mapping their DTO to <see cref="DayPoint"/>.
/// </summary>
public static class UsageSeries
{
    /// <summary>A single day's counts — the currency both usage DTOs map to.</summary>
    public sealed record DayPoint(string Day, int ConsultsCompleted, int TokensIn, int TokensOut);

    public static IReadOnlyList<DayPoint> DenseDays(string from, string to, IEnumerable<DayPoint> days)
    {
        if (!TryParse(from, out var fromDay) || !TryParse(to, out var toDay) || toDay < fromDay)
        {
            return Array.Empty<DayPoint>();
        }

        var byDay = new Dictionary<string, DayPoint>(StringComparer.Ordinal);
        foreach (var day in days)
        {
            byDay[day.Day] = day;
        }

        var dense = new List<DayPoint>(toDay.DayNumber - fromDay.DayNumber + 1);
        for (var day = fromDay; day <= toDay; day = day.AddDays(1))
        {
            var key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            dense.Add(byDay.TryGetValue(key, out var point) ? point : new DayPoint(key, 0, 0, 0));
        }

        return dense;
    }

    private static bool TryParse(string value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
