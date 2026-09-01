using Consultologist.Api.Jobs;
using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

/// <summary>
/// #552: the arithmetic, not the plumbing — the table round trip is untested
/// by construction (no Azurite in CI, the rate limiter's own rule), so the
/// pure seams carry the logic.
/// </summary>
public class AccountUsageTests
{
    [Fact]
    public void TheDayKey_IsTheUtcDay_WhateverTheOffset()
    {
        // The WindowKey pin's twin: two offsets naming the same UTC instant
        // land on the same row.
        var lateInHalifax = new DateTimeOffset(2026, 8, 31, 22, 30, 0, TimeSpan.FromHours(-3));
        var earlyInBerlin = new DateTimeOffset(2026, 9, 1, 3, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-09-01", TableAccountUsageStore.DayKey(lateInHalifax));
        Assert.Equal(TableAccountUsageStore.DayKey(lateInHalifax), TableAccountUsageStore.DayKey(earlyInBerlin));
    }

    [Fact]
    public void TheDayKey_IsInvariantAndSortable()
    {
        Assert.Equal("2026-01-05", TableAccountUsageStore.DayKey(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero)));
        // Ordinal order equals chronological order — what the RowKey range read rests on.
        Assert.True(string.CompareOrdinal("2026-01-05", "2026-01-31") < 0);
        Assert.True(string.CompareOrdinal("2026-01-31", "2026-02-01") < 0);
    }
}
