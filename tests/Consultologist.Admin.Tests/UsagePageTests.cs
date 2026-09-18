using Bunit;
using Consultologist.Admin.Pages;
using Consultologist.Admin.Services.Operators;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Admin.Tests;

/// <summary>
/// #553/#733: the operator usage page (now the admin app's home) — grouped by
/// tenant, sortable, charted, and a signed-in non-operator meets the named
/// state, never a broken page.
/// </summary>
public class UsagePageTests : AdminRenderTestContext
{
    private static OperatorUsageRowResponse Row(
        string id, string name, string? tenantId, int consults, int tokensIn, int tokensOut) =>
        new(id, name, "organisation", tenantId, consults, tokensIn, tokensOut);

    private static OperatorUsageDayResponse Day(string day, int consults, int tokensIn, int tokensOut) =>
        new(day, consults, tokensIn, tokensOut);

    private static readonly IReadOnlyList<OperatorUsageDayResponse> NoDays = Array.Empty<OperatorUsageDayResponse>();

    // ----- the rollup, pure -----

    [Fact]
    public void Groups_SeparateOrganisations_AndLabelTheSpecialTenants()
    {
        var rows = new[]
        {
            Row("u1", "Dr One", "tenant-a", 3, 3000, 900),
            Row("u2", "Dr Two", "tenant-b", 5, 9000, 2500),
            Row("u3", "Dr Personal", OperatorUsageRollup.ConsumersTenantId, 1, 800, 200),
            Row("u4", "Dr Ghost", null, 2, 100, 50)
        };

        var groups = OperatorUsageRollup.Groups(rows, OperatorUsageRollup.SortByName, descending: false);

        Assert.Equal(4, groups.Count);
        // Biggest consult totals first.
        Assert.Equal("Organisation tenant-b", groups[0].Label);
        Assert.Equal((5, 9000L, 2500L), (groups[0].ConsultsCompleted, groups[0].TokensIn, groups[0].TokensOut));
        // The consumers tenant is personal accounts, never an organisation;
        // an unreadable tenant is its own named state.
        Assert.Contains(groups, g => g.Label == OperatorUsageRollup.PersonalAccountsLabel);
        Assert.Contains(groups, g => g.Label == OperatorUsageRollup.TenantNotRecordedLabel);
    }

    [Fact]
    public void SortRows_ByEachKey_AndDirection()
    {
        var rows = new[]
        {
            Row("u1", "Beta", "t", 5, 100, 50),
            Row("u2", "alpha", "t", 3, 9000, 100)
        };

        Assert.Equal(new[] { "u2", "u1" }, OperatorUsageRollup.SortRows(rows, OperatorUsageRollup.SortByName, false).Select(r => r.AppUserId));
        Assert.Equal(new[] { "u1", "u2" }, OperatorUsageRollup.SortRows(rows, OperatorUsageRollup.SortByConsults, true).Select(r => r.AppUserId));
        Assert.Equal(new[] { "u2", "u1" }, OperatorUsageRollup.SortRows(rows, OperatorUsageRollup.SortByTokens, true).Select(r => r.AppUserId));
    }

    // ----- the page -----

    [Fact]
    public void ServedRows_RenderGrouped_WithOrgTotals()
    {
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new OperatorUsageResponse(
            "2026-08-03", "2026-09-01",
            new[]
            {
                Row("u1", "Dr One", "tenant-a", 3, 3000, 900),
                Row("u3", "Dr Personal", OperatorUsageRollup.ConsumersTenantId, 1, 800, 200)
            },
            NoDays));

        var page = Render<Usage>();

        var labels = page.FindAll(".operators-org__label").Select(l => l.TextContent.Trim()).ToList();
        Assert.Contains("Organisation tenant-a", labels);
        Assert.Contains("Personal accounts", labels);
        Assert.Contains("3 consults · 3,000 in · 900 out tokens", page.FindAll(".operators-org__totals").Select(t => t.TextContent.Trim()));
        Assert.Contains("Dr One", page.Find(".operators__table").TextContent);
    }

    [Fact]
    public void ServedUsage_DrawsTheChartsAboveTheTables()
    {
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new OperatorUsageResponse(
            "2026-09-01", "2026-09-03",
            new[]
            {
                Row("u1", "Dr One", "tenant-a", 3, 3000, 900),
                Row("u2", "Dr Two", "tenant-b", 5, 9000, 2500)
            },
            new[]
            {
                Day("2026-09-01", 4, 6000, 1600),
                Day("2026-09-03", 4, 6000, 1800)
            }));

        var page = Render<Usage>();

        // Four charts: consults/day, tokens/day, consults by org, tokens by org.
        var titles = page.FindAll(".usage-chart__title").Select(t => t.TextContent).ToList();
        Assert.Contains("Consults per day", titles);
        Assert.Contains("Tokens per day", titles);
        Assert.Contains("Consults by organisation", titles);
        Assert.Contains("Tokens by organisation", titles);
        Assert.NotEmpty(page.FindAll("svg.usage-chart__svg"));
        // The tables stay as the exact-numbers source.
        Assert.NotEmpty(page.FindAll(".operators__table"));
    }

    [Fact]
    public async Task TogglingToLines_DrawsThePerDayChartsAsLines_PerOrgStaysBars()
    {
        // #743: the bars↔lines toggle flips the per-day charts; the per-org
        // comparison is categorical and stays bars.
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new OperatorUsageResponse(
            "2026-09-01", "2026-09-03",
            new[] { Row("u1", "Dr One", "tenant-a", 3, 3000, 900) },
            new[]
            {
                Day("2026-09-01", 4, 6000, 1600),
                Day("2026-09-03", 4, 6000, 1800)
            }));

        var page = Render<Usage>();

        // Default is bars — no lines yet.
        Assert.Empty(page.FindAll("polyline.usage-chart__line"));

        await page.Find(".usage-chart-toggle fluent-button:last-child").ClickAsync(new()); // "Lines"

        // The two per-day charts now draw lines; the per-org charts keep their bars.
        Assert.NotEmpty(page.FindAll("polyline.usage-chart__line"));
        Assert.NotEmpty(page.FindAll("rect.usage-chart__seg"));
    }

    [Fact]
    public void AnOlderApiWithoutDays_ShowsOnlyThePerOrgCharts_NeverCrashes()
    {
        // The frontend and API deploy independently: a new client can meet an
        // API build that predates the Days field. The page must not crash, and
        // simply omits the daily series until the API catches up.
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new OperatorUsageResponse(
            "2026-09-01", "2026-09-03",
            new[] { Row("u1", "Dr One", "tenant-a", 3, 3000, 900) },
            Days: null));

        var page = Render<Usage>();

        var titles = page.FindAll(".usage-chart__title").Select(t => t.TextContent).ToList();
        Assert.DoesNotContain("Consults per day", titles);
        Assert.DoesNotContain("Tokens per day", titles);
        Assert.Contains("Consults by organisation", titles);
        Assert.NotEmpty(page.FindAll(".operators__table"));
    }

    [Fact]
    public void WhileLoading_ShowsASpinner_ThenTheResults()
    {
        // #692: the fetch is in flight until we release the gate — the page
        // shows the shared loading ring and no table; once it completes, the
        // ring gives way to the rollup.
        var gate = new TaskCompletionSource<OperatorUsageResponse>();
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(gate.Task);

        var page = Render<Usage>();

        Assert.NotEmpty(page.FindAll(".loading-state"));
        Assert.Empty(page.FindAll(".operators__table"));

        gate.SetResult(new OperatorUsageResponse(
            "2026-08-03", "2026-09-01", new[] { Row("u1", "Dr One", "tenant-a", 3, 3000, 900) }, NoDays));

        // #691: an explicit timeout — the default 1s flaked under CI load.
        page.WaitForState(() => page.FindAll(".loading-state").Count == 0, TimeSpan.FromSeconds(5));
        Assert.Contains("Dr One", page.Find(".operators__table").TextContent);
    }

    [Fact]
    public void ANonOperator_MeetsTheNamedState_NeverABrokenPage()
    {
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new OperatorAccessException());

        var page = Render<Usage>();

        Assert.Contains("your account is not on the allowlist", page.Find(".operators-denied").TextContent);
        Assert.Empty(page.FindAll(".operators-window"));
    }

    [Fact]
    public void AnEmptyWindow_SaysSo()
    {
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new OperatorUsageResponse("2026-08-03", "2026-09-01", Array.Empty<OperatorUsageRowResponse>(), NoDays));

        var page = Render<Usage>();

        Assert.Contains("No usage in this window", page.Find(".operators-empty").TextContent);
    }

    [Fact]
    public async Task SortToggles_ReorderTheRows()
    {
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(new OperatorUsageResponse(
            "2026-08-03", "2026-09-01",
            new[]
            {
                Row("u1", "Beta", "t", 5, 100, 50),
                Row("u2", "Alpha", "t", 3, 9000, 100)
            },
            NoDays));
        var page = Render<Usage>();

        // Default: consults descending — u1 first.
        Assert.StartsWith("Beta", page.FindAll(".operators__row")[0].TextContent.Trim());

        await page.Find(".operators-sort--name").ClickAsync(new());

        Assert.StartsWith("Alpha", page.FindAll(".operators__row")[0].TextContent.Trim());
    }
}
