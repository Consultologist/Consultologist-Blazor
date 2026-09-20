using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #710/#687: the run rail's live-region aria is present in the REAL served,
/// authed app. (The live "updates as sections complete" runs over SSE and isn't
/// practically mockable; this confirms the polite role="status" summary and the
/// node grid's aria-busy render.)
/// </summary>
[Collection(BrowserCollection.Name)]
public class RunRailAriaE2ETests
{
    private readonly BrowserFixture _fixture;

    public RunRailAriaE2ETests(BrowserFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TheRunRail_CarriesItsLiveRegionAria()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await ApiMock.InstallAsync(page);

        // The re-attach route renders the run rail from the (mocked) job.
        await page.GotoAsync($"{_fixture.BaseUrl}/create/{ApiMock.JobId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        await page.WaitForSelectorAsync(".node-grid", new() { Timeout = 30_000 });

        Assert.NotNull(await page.Locator(".node-grid").GetAttributeAsync("aria-busy"));
        // The polite status summary is present (visually-hidden, role=status).
        Assert.NotEmpty(await page.Locator("[role=status].visually-hidden").AllAsync());
    }
}
