using Consultologist.Api.RateLimiting;

namespace Consultologist.Api.Tests;

/// <summary>
/// The arithmetic, not the plumbing (#266). There is no Azurite in CI, so the
/// table round trip is untested by construction — which is exactly why the
/// decision it wraps is pulled out and pinned here, the way
/// AccountStore.DecideLinkOutcome is.
/// </summary>
public class AccountRateLimiterTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-01T14:23:07Z");

    [Fact]
    public void WindowKey_IsTheUtcHour()
    {
        Assert.Equal("2026-08-01T14", TableAccountRateLimiter.WindowKey(Now));
    }

    [Fact]
    public void WindowKey_IsStableAcrossTheWholeHour()
    {
        Assert.Equal(
            TableAccountRateLimiter.WindowKey(DateTimeOffset.Parse("2026-08-01T14:00:00Z")),
            TableAccountRateLimiter.WindowKey(DateTimeOffset.Parse("2026-08-01T14:59:59Z")));
    }

    [Fact]
    public void WindowKey_ChangesAtTheHourBoundary()
    {
        Assert.NotEqual(
            TableAccountRateLimiter.WindowKey(DateTimeOffset.Parse("2026-08-01T14:59:59Z")),
            TableAccountRateLimiter.WindowKey(DateTimeOffset.Parse("2026-08-01T15:00:00Z")));
    }

    [Fact]
    public void WindowKey_NormalizesToUtcRatherThanTheOffsetItWasGiven()
    {
        // 2026-08-01T09:23-05:00 is 14:23 UTC. An account submitting from two
        // time zones must share one window, or the limit is per-offset.
        Assert.Equal(
            TableAccountRateLimiter.WindowKey(Now),
            TableAccountRateLimiter.WindowKey(DateTimeOffset.Parse("2026-08-01T09:23:07-05:00")));
    }

    // ----- #558: the cleanup cutoff -----

    [Fact]
    public void TheCleanupHorizon_IsTheReadClamp()
    {
        // Derived FROM Account.MaxUsageWindowDays, so read and cleanup can
        // never drift; the figure the docs state.
        Assert.Equal(Consultologist.Api.Account.MaxUsageWindowDays, TableAccountRateLimiter.CleanupHorizonDays);
        Assert.Equal(92, TableAccountRateLimiter.CleanupHorizonDays);
    }

    [Fact]
    public void TheCleanupCutoff_IsTheHorizonAgo_AsAWindowKey()
    {
        // 92 days before 2026-08-01 is 2026-05-01 — and it is in the PAST: a
        // sign slip would delete every live window and none of the old ones.
        Assert.Equal("2026-05-01T14", TableAccountRateLimiter.CleanupCutoffWindow(Now));
    }

    [Fact]
    public void RowKeyLtTheCutoff_KeepsTheBoundaryWindow_AndDropsTheOneBefore()
    {
        var cutoff = TableAccountRateLimiter.CleanupCutoffWindow(Now);
        var boundary = TableAccountRateLimiter.WindowKey(Now.AddDays(-TableAccountRateLimiter.CleanupHorizonDays));
        var older = TableAccountRateLimiter.WindowKey(Now.AddDays(-TableAccountRateLimiter.CleanupHorizonDays).AddHours(-1));

        // `RowKey lt cutoff` is the whole date comparison, so the boundary
        // window survives (not lt itself) and anything older goes.
        Assert.False(string.CompareOrdinal(boundary, cutoff) < 0);
        Assert.True(string.CompareOrdinal(older, cutoff) < 0);
    }

    [Fact]
    public void Decide_AllowsUpToTheLimit()
    {
        var decision = TableAccountRateLimiter.Decide(used: 59, limit: 60, Now);

        Assert.True(decision.Allowed);
        Assert.Equal(1, decision.Remaining);
    }

    [Fact]
    public void Decide_RefusesAtTheLimit()
    {
        var decision = TableAccountRateLimiter.Decide(used: 60, limit: 60, Now);

        Assert.False(decision.Allowed);
        Assert.Equal(0, decision.Remaining);
    }

    [Fact]
    public void Decide_RefusesPastTheLimitWithoutReportingNegativeHeadroom()
    {
        // Reachable: a boundary race can commit two increments against one
        // read. Remaining is what a caller may be told, so it floors at zero.
        var decision = TableAccountRateLimiter.Decide(used: 61, limit: 60, Now);

        Assert.False(decision.Allowed);
        Assert.Equal(0, decision.Remaining);
    }

    [Fact]
    public void Decide_ReportsTheLimitItEnforced()
    {
        Assert.Equal(60, TableAccountRateLimiter.Decide(used: 0, limit: 60, Now).Limit);
    }

    [Fact]
    public void RetryAfter_IsTheDistanceToTheNextWindow()
    {
        // 14:23:07 to 15:00:00.
        Assert.Equal(
            TimeSpan.FromSeconds((36 * 60) + 53),
            TableAccountRateLimiter.RetryAfter(Now));
    }

    [Fact]
    public void RetryAfter_IsNeverZeroAtTheTopOfTheHour()
    {
        // A Retry-After of 0 invites an immediate retry, and at :00 exactly
        // the naive answer is a full hour — but one second before the
        // boundary it is under a second, which rounds to "try again now".
        var atTheBoundary = TableAccountRateLimiter.RetryAfter(DateTimeOffset.Parse("2026-08-01T14:59:59.5Z"));

        Assert.True(atTheBoundary >= TimeSpan.FromSeconds(1), $"Retry-After was {atTheBoundary}.");
    }

    [Fact]
    public void RetryAfter_IsAFullHourImmediatelyAfterAReset()
    {
        Assert.Equal(
            TimeSpan.FromHours(1),
            TableAccountRateLimiter.RetryAfter(DateTimeOffset.Parse("2026-08-01T14:00:00Z")));
    }

    [Fact]
    public void Decide_ANewWindowStartsFromZero()
    {
        // The window reset is expressed by the row key, not by the arithmetic:
        // a new hour reads no row, so `used` is 0. This pins that a zero-used
        // decision is allowed regardless of how saturated the last one was.
        Assert.False(TableAccountRateLimiter.Decide(used: 60, limit: 60, Now).Allowed);
        Assert.True(TableAccountRateLimiter.Decide(used: 0, limit: 60, Now).Allowed);
    }
}
