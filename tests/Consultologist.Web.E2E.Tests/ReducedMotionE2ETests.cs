using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #775: with the OS "reduce motion" preference set, the app's transitions must
/// collapse to ~0 — a computed-style behaviour bUnit can't observe. Uses the
/// public /help page (renders the shell, never redirects) and the skip-link,
/// whose 0.15s slide lives in the global app.css.
/// </summary>
[Collection(BrowserCollection.Name)]
public class ReducedMotionE2ETests
{
    private readonly BrowserFixture _fixture;

    public ReducedMotionE2ETests(BrowserFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ReduceMotion_ZeroesTheSkipLinkTransition()
    {
        var page = await _fixture.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ReducedMotion = ReducedMotion.Reduce,
        });
        await page.GotoAsync($"{_fixture.BaseUrl}/help", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("a.skip-link", new() { Timeout = 30_000 });

        var duration = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('a.skip-link')).transitionDuration");

        // Without the guard this is "0.15s"; the guard collapses it to ~0.
        Assert.True(
            float.Parse(duration.Replace("s", string.Empty), CultureInfo.InvariantCulture) < 0.05f,
            $"skip-link transition should be ~0 under reduced motion; was {duration}");
    }
}
