using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #552: the profile's Usage card — the served rows summarized over the
/// window, the empty state said in words (never zeros), the window selector
/// driving the fetch.
/// </summary>
public class UsageProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithUsage(AccountUsageResponse response)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(response);
    }

    // ----- the pure summary -----

    [Fact]
    public void TheFigures_SumRows_AndAverageOverTheWindowLength()
    {
        var days = new[]
        {
            new AccountUsageDayResponse("2026-09-01", 2, 2000, 600),
            new AccountUsageDayResponse("2026-09-03", 1, 1000, 300)
        };

        // A 10-day window with two active days: per-day figures divide by 10.
        var figures = UsageSummary.Of(days, windowDays: 10);

        Assert.Equal(3, figures.Consults);
        Assert.Equal((3000, 900, 3900), ((int)figures.TokensIn, (int)figures.TokensOut, (int)figures.TokensTotal));
        Assert.Equal(0.3, figures.ConsultsPerDay, precision: 5);
        Assert.Equal(390.0, figures.TokensPerDay, precision: 5);
        Assert.Equal(1300, figures.AverageTokensPerConsult);
    }

    [Fact]
    public void NoConsults_MeansNoAverage_NeverZero()
    {
        var figures = UsageSummary.Of(Array.Empty<AccountUsageDayResponse>(), windowDays: 7);

        Assert.Null(figures.AverageTokensPerConsult);
        Assert.Equal("—", UsageSummary.DescribeAverage(figures.AverageTokensPerConsult));
    }

    [Theory]
    [InlineData("2026-09-01", "2026-09-07", 7)]
    [InlineData("2026-09-01", "2026-09-01", 1)]
    [InlineData("junk", "2026-09-01", 0)]
    public void TheWindowLength_IsInclusive(string from, string to, int expected) =>
        Assert.Equal(expected, UsageSummary.WindowDaysOf(from, to));

    // ----- the card -----

    [Fact]
    public void ServedRows_RenderTheFiguresAndThePerDayTable()
    {
        WithUsage(new AccountUsageResponse("2026-08-17", "2026-09-15", new[]
        {
            new AccountUsageDayResponse("2026-09-01", 2, 2000, 600),
            new AccountUsageDayResponse("2026-09-03", 1, 1000, 300)
        }));

        var page = Render<Profile>();

        Assert.Equal("3", page.Find(".usage-consults").TextContent.Trim());
        Assert.Contains("3,000 in · 900 out · 3,900 total", page.Find(".usage-tokens").TextContent);
        Assert.Equal("1,300", page.Find(".usage-average").TextContent.Trim());
        var rows = page.FindAll(".usage__row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("2026-09-01", rows[0].TextContent);
        // The History link line closes the card.
        Assert.Contains("History", page.Find(".usage-history-line").TextContent);
    }

    [Fact]
    public void NoRows_SaysTheEmptySentence_NeverZeros()
    {
        WithUsage(new AccountUsageResponse("2026-08-17", "2026-09-15", Array.Empty<AccountUsageDayResponse>()));

        var page = Render<Profile>();

        Assert.Contains("No usage yet — counts begin with the first completed run", page.Find(".usage-empty").TextContent);
        Assert.Empty(page.FindAll(".usage-figures"));
        Assert.Empty(page.FindAll(".usage__table"));
    }

    [Fact]
    public async Task SwitchingTheWindow_Refetches_WithTheRightSpan()
    {
        WithUsage(new AccountUsageResponse("2026-08-17", "2026-09-15", Array.Empty<AccountUsageDayResponse>()));
        var page = Render<Profile>();
        AccountService.ClearReceivedCalls();

        await page.Find(".usage-window").ChangeAsync(new() { Value = "7" });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await AccountService.Received(1).GetUsageAsync(
            today.AddDays(-6).ToString("yyyy-MM-dd"),
            today.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task ACustomRange_DrivesTheFetchFromItsInputs()
    {
        WithUsage(new AccountUsageResponse("2026-08-17", "2026-09-15", Array.Empty<AccountUsageDayResponse>()));
        var page = Render<Profile>();

        await page.Find(".usage-window").ChangeAsync(new() { Value = "custom" });
        page.Find(".usage-from").Change("2026-09-01");
        page.Find(".usage-to").Change("2026-09-07");
        AccountService.ClearReceivedCalls();
        await page.Find(".usage-apply").ClickAsync(new());

        await AccountService.Received(1).GetUsageAsync("2026-09-01", "2026-09-07");
    }
}
