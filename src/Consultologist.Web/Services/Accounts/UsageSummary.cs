using System.Globalization;

namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #552: the window's figures, computed from the served day rows — over the
/// WINDOW length, not the row count (days without activity are absent from
/// the response by design). Numbers only; an average over zero consults is
/// "—", never a divide-by-zero zero.
/// </summary>
public static class UsageSummary
{
    public sealed record Figures(
        int Consults,
        long TokensIn,
        long TokensOut,
        long TokensTotal,
        double ConsultsPerDay,
        double TokensPerDay,
        long? AverageTokensPerConsult);

    public static Figures Of(IReadOnlyList<AccountUsageDayResponse> days, int windowDays)
    {
        var consults = days.Sum(day => day.ConsultsCompleted);
        var tokensIn = days.Sum(day => (long)day.TokensIn);
        var tokensOut = days.Sum(day => (long)day.TokensOut);
        var total = tokensIn + tokensOut;

        return new Figures(
            consults,
            tokensIn,
            tokensOut,
            total,
            windowDays > 0 ? (double)consults / windowDays : 0,
            windowDays > 0 ? (double)total / windowDays : 0,
            consults > 0 ? total / consults : null);
    }

    /// <summary>The inclusive day count of a served window.</summary>
    public static int WindowDaysOf(string from, string to) =>
        DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDay)
        && DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDay)
        && toDay >= fromDay
            ? toDay.DayNumber - fromDay.DayNumber + 1
            : 0;

    public static string DescribeAverage(long? averageTokensPerConsult) =>
        averageTokensPerConsult is { } average ? average.ToString("N0", CultureInfo.CurrentCulture) : "—";
}
