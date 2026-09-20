using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #773: at phone width the signed-in workspace nav must wrap, not scroll behind
/// a suppressed scrollbar — otherwise trailing links (incl. Profile) clip off
/// the right edge. A layout-overflow regression bUnit can't see; the E2E harness
/// is authenticated, so the full five-link nav renders. Uses the public /help
/// page, which renders the same shell and never redirects.
/// </summary>
[Collection(BrowserCollection.Name)]
public class NavResponsiveE2ETests
{
    private readonly BrowserFixture _fixture;

    public NavResponsiveE2ETests(BrowserFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task TheNav_WrapsRatherThanClipping_AtPhoneWidth()
    {
        var page = await _fixture.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = 320, Height = 800 },
        });
        await page.GotoAsync($"{_fixture.BaseUrl}/help", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync(".top-nav", new() { Timeout = 30_000 });

        // No horizontal overflow: the links wrap to fit the width. A scrolling
        // strip would have scrollWidth > clientWidth.
        var fits = await page.EvaluateAsync<bool>(
            "() => { const n = document.querySelector('.top-nav'); return n.scrollWidth <= n.clientWidth + 1; }");
        Assert.True(fits, "the mobile nav should wrap to fit the viewport, not scroll behind a hidden scrollbar");

        // Every signed-in link is present (wrapping shows them all, hides none).
        var labels = (await page.Locator(".top-nav a").AllTextContentsAsync())
            .Select(text => text.Trim())
            .ToList();
        foreach (var expected in new[] { "Create", "History", "Editor", "Help", "Profile" })
        {
            Assert.Contains(expected, labels);
        }
    }
}
