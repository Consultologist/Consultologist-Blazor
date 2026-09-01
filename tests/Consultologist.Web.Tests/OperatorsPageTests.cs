using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.Operators;
using Consultologist.Web.Shared;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Web.Tests;

/// <summary>
/// #553: the operator panel — grouped by tenant, sortable, and a signed-in
/// non-operator meets the named state, never a broken page.
/// </summary>
public class OperatorsPageTests : ClientRenderTestContext
{
    private static OperatorUsageRowResponse Row(
        string id, string name, string? tenantId, int consults, int tokensIn, int tokensOut) =>
        new(id, name, "organisation", tenantId, consults, tokensIn, tokensOut);

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
            }));

        var page = Render<OperatorsPage>();

        var labels = page.FindAll(".operators-org__label").Select(l => l.TextContent.Trim()).ToList();
        Assert.Contains("Organisation tenant-a", labels);
        Assert.Contains("Personal accounts", labels);
        Assert.Contains("3 consults · 3,000 in · 900 out tokens", page.FindAll(".operators-org__totals").Select(t => t.TextContent.Trim()));
        Assert.Contains("Dr One", page.Find(".operators__table").TextContent);
    }

    [Fact]
    public void ANonOperator_MeetsTheNamedState_NeverABrokenPage()
    {
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new OperatorAccessException());

        var page = Render<OperatorsPage>();

        Assert.Contains("your account is not on the allowlist", page.Find(".operators-denied").TextContent);
        Assert.Empty(page.FindAll(".operators-window"));
    }

    [Fact]
    public void AnEmptyWindow_SaysSo()
    {
        OperatorService.GetUsageAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new OperatorUsageResponse("2026-08-03", "2026-09-01", Array.Empty<OperatorUsageRowResponse>()));

        var page = Render<OperatorsPage>();

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
            }));
        var page = Render<OperatorsPage>();

        // Default: consults descending — u1 first.
        Assert.StartsWith("Beta", page.FindAll(".operators__row")[0].TextContent.Trim());

        await page.Find(".operators-sort--name").ClickAsync(new());

        Assert.StartsWith("Alpha", page.FindAll(".operators__row")[0].TextContent.Trim());
    }

    // ----- the nav link -----

    private void WithMe(bool isOperator)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active",
            new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new[] { new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) },
            IsOperator: isOperator));
    }

    [Fact]
    public void AnOperator_SeesTheNavLink()
    {
        WithMe(isOperator: true);

        var header = Render<Header>();

        Assert.Contains(header.FindAll("fluent-nav-link, a").Select(a => a.TextContent.Trim()), t => t == "Operators");
    }

    [Fact]
    public void AnOrdinaryAccount_SeesNoNavLink()
    {
        WithMe(isOperator: false);

        var header = Render<Header>();

        Assert.DoesNotContain(header.FindAll("fluent-nav-link, a").Select(a => a.TextContent.Trim()), t => t == "Operators");
    }
}
