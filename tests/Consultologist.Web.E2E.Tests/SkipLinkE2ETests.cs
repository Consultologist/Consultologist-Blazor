using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #707/#688: the skip-link reveals on focus and moves focus into the main
/// landmark when activated — real focus movement bUnit can't assert. Runs
/// against the served app at BaseUrl; the home page renders its shell
/// anonymously, so no sign-in is needed. (Focus is driven directly rather than
/// by Tab count: App.razor's FocusOnNavigate lands initial focus on the h1, so
/// the raw tab order from load isn't a stable thing to assert.)
/// </summary>
[Collection(BrowserCollection.Name)]
public class SkipLinkE2ETests
{
    private readonly BrowserFixture _fixture;

    public SkipLinkE2ETests(BrowserFixture fixture) => _fixture = fixture;

    private async Task<IPage> HomeAsync()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        // The app boots and renders MainLayout (skip-link + main) even signed out.
        await page.WaitForSelectorAsync("a.skip-link", new() { Timeout = 30_000 });
        return page;
    }

    [Fact]
    public async Task SkipLink_IsFirstInTheDom_AndRevealsOnFocus()
    {
        var page = await HomeAsync();

        // It is the first focusable element in the document (before the header nav).
        var isFirst = await page.EvaluateAsync<bool>(
            "() => { const f = document.querySelector('a[href],button,input,select,textarea,[tabindex]:not([tabindex=\"-1\"])'); return !!f && f.classList.contains('skip-link'); }");
        Assert.True(isFirst, "the skip-link should be the first focusable element in the DOM");

        await page.FocusAsync("a.skip-link");
        var activeClass = await page.EvaluateAsync<string>("() => document.activeElement?.className ?? ''");
        Assert.Contains("skip-link", activeClass);

        // Off-screen (top:-3rem) until focused, then it slides into view (top:.5rem).
        // Wait out the 0.15s transition before measuring.
        await page.WaitForTimeoutAsync(300);
        var box = await page.Locator("a.skip-link").BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box!.Y >= 0, $"skip-link should be on-screen when focused; y={box.Y}");
    }

    [Fact]
    public async Task ActivatingTheSkipLink_MovesFocusIntoMain()
    {
        var page = await HomeAsync();

        await page.FocusAsync("a.skip-link");
        await page.Keyboard.PressAsync("Enter");   // activate the in-page anchor

        // Focus must land IN main, not stay on the link — Blazor intercepts the
        // fragment nav, so MainLayout moves focus explicitly (#707 fix).
        await page.WaitForFunctionAsync("() => document.activeElement?.id === 'main-content'");
        var activeId = await page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''");
        Assert.Equal("main-content", activeId);
    }
}
