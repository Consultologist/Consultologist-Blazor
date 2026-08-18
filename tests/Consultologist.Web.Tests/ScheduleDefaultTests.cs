using Consultologist.Web.Services.Accounts;

namespace Consultologist.Web.Tests;

/// <summary>
/// #390: the arithmetic behind "run overnight at the time I chose". Worth its
/// own tests because it is the part that fails silently — a consult scheduled
/// for the wrong day still looks scheduled.
/// </summary>
public class ScheduleDefaultTests
{
    [Theory]
    [InlineData("06:30", "06:30")]
    [InlineData("  06:30  ", "06:30")]
    [InlineData("00:00", "00:00")]
    [InlineData("23:59", "23:59")]
    public void AStoredTimeOfDay_ParsesBack(string stored, string expected)
    {
        Assert.Equal(expected, ScheduleDefault.Parse(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("6:30 AM")]
    [InlineData("25:00")]
    [InlineData("nonsense")]
    public void AnUnreadableSetting_ParsesToNull(string? stored)
    {
        // Null so callers fall back. An unreadable preference must not stop
        // someone scheduling a consult.
        Assert.Null(ScheduleDefault.Parse(stored));
    }

    [Fact]
    public void ATimeStillAheadToday_LandsToday()
    {
        var localNow = new DateTime(2026, 8, 18, 20, 0, 0, DateTimeKind.Local);

        var next = ScheduleDefault.NextOccurrenceUtc("23:00", localNow).ToLocalTime();

        Assert.Equal(new DateTime(2026, 8, 18, 23, 0, 0), next.DateTime);
    }

    [Fact]
    public void ATimeAlreadyPastToday_RollsToTomorrow()
    {
        // The rule #157 established, and the reason 2 AM works at all: at 8pm,
        // "2 AM" means tomorrow morning, not sixteen hours ago.
        var localNow = new DateTime(2026, 8, 18, 20, 0, 0, DateTimeKind.Local);

        var next = ScheduleDefault.NextOccurrenceUtc("02:00", localNow).ToLocalTime();

        Assert.Equal(new DateTime(2026, 8, 19, 2, 0, 0), next.DateTime);
    }

    [Fact]
    public void NoPreference_UsesTheDocumentedFallback()
    {
        // Empty until chosen is the rule for the SETTING; the fallback is what
        // scheduling uses meanwhile, and it must stay what #157 shipped.
        var localNow = new DateTime(2026, 8, 18, 20, 0, 0, DateTimeKind.Local);

        var next = ScheduleDefault.NextOccurrenceUtc(null, localNow).ToLocalTime();

        Assert.Equal(new DateTime(2026, 8, 19, 2, 0, 0), next.DateTime);
    }

    [Fact]
    public void ALocalInputValue_RoundTrips()
    {
        var instant = new DateTimeOffset(new DateTime(2026, 8, 19, 6, 30, 0, DateTimeKind.Local));

        var roundTripped = ScheduleDefault.FromLocalInputValue(
            ScheduleDefault.ToLocalInputValue(instant));

        Assert.Equal(instant.ToUniversalTime(), roundTripped);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-08-19T")]
    public void AClearedOrHalfTypedInput_IsNull(string? value)
    {
        Assert.Null(ScheduleDefault.FromLocalInputValue(value));
    }
}
