using System;
using Consultologist.Web.Services;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #684: the elapsed readout for a running generation — m:ss for the common
/// minutes-long case, rolling to h:mm:ss past an hour rather than "60:00".
/// </summary>
public class ElapsedTimeTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(83, "1:23")]
    [InlineData(600, "10:00")]
    [InlineData(720, "12:00")]
    public void Format_UnderAnHour_IsMinutesAndSeconds(int seconds, string expected)
    {
        Assert.Equal(expected, ElapsedTime.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Format_AnHourOrMore_RollsToHours()
    {
        Assert.Equal("1:02:03", ElapsedTime.Format(new TimeSpan(1, 2, 3)));
    }
}
