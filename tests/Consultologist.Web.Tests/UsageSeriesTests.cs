using Consultologist.UI.Charts;

namespace Consultologist.Web.Tests;

/// <summary>
/// #732: the sparse served rows become one point per calendar day, so a
/// window draws its full width with gaps at zero — never however-many days
/// happened to have activity.
/// </summary>
public class UsageSeriesTests
{
    private static UsageSeries.DayPoint Day(string day, int consults, int tokensIn, int tokensOut) =>
        new(day, consults, tokensIn, tokensOut);

    [Fact]
    public void DenseDays_FillsGapsWithZero_AcrossTheInclusiveWindow()
    {
        var dense = UsageSeries.DenseDays("2026-09-01", "2026-09-05", new[]
        {
            Day("2026-09-02", 3, 1000, 200),
            Day("2026-09-05", 1, 400, 50),
        });

        Assert.Equal(5, dense.Count);
        Assert.Equal(new[] { "2026-09-01", "2026-09-02", "2026-09-03", "2026-09-04", "2026-09-05" },
            dense.Select(point => point.Day));

        // The absent days are present as zeros; the served days keep their counts.
        Assert.Equal((0, 0, 0), (dense[0].ConsultsCompleted, dense[0].TokensIn, dense[0].TokensOut));
        Assert.Equal((3, 1000, 200), (dense[1].ConsultsCompleted, dense[1].TokensIn, dense[1].TokensOut));
        Assert.Equal(0, dense[2].ConsultsCompleted);
        Assert.Equal((1, 400, 50), (dense[4].ConsultsCompleted, dense[4].TokensIn, dense[4].TokensOut));
    }

    [Fact]
    public void DenseDays_SingleDayWindow_IsOnePoint()
    {
        var dense = UsageSeries.DenseDays("2026-09-01", "2026-09-01", Array.Empty<UsageSeries.DayPoint>());

        Assert.Single(dense);
        Assert.Equal("2026-09-01", dense[0].Day);
        Assert.Equal(0, dense[0].ConsultsCompleted);
    }

    [Theory]
    [InlineData("2026-09-05", "2026-09-01")] // reversed
    [InlineData("not-a-date", "2026-09-05")] // unparseable from
    [InlineData("2026-09-01", "")]           // blank to
    public void DenseDays_BadRange_IsEmpty(string from, string to) =>
        Assert.Empty(UsageSeries.DenseDays(from, to, Array.Empty<UsageSeries.DayPoint>()));

    [Fact]
    public void DenseDays_SpansMonthBoundary()
    {
        var dense = UsageSeries.DenseDays("2026-08-30", "2026-09-02", Array.Empty<UsageSeries.DayPoint>());

        Assert.Equal(new[] { "2026-08-30", "2026-08-31", "2026-09-01", "2026-09-02" },
            dense.Select(point => point.Day));
    }
}
