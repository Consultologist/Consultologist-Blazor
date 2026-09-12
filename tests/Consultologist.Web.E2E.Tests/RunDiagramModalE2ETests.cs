using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #710/#686: the run-diagram modal, opened from the REAL authed History page,
/// traps and restores focus — the behaviour bUnit can't assert and the #707
/// fixture only proves for the JS in isolation. Reaches the page via the
/// compile-time E2E fake-auth shim (the app is served with -p:E2E=true) and
/// Playwright-mocked API data.
/// </summary>
[Collection(BrowserCollection.Name)]
public class RunDiagramModalE2ETests
{
    private readonly BrowserFixture _fixture;

    public RunDiagramModalE2ETests(BrowserFixture fixture) => _fixture = fixture;

    private async Task<IPage> OpenModalAsync()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await ApiMock.InstallAsync(page);

        await page.GotoAsync($"{_fixture.BaseUrl}/history", new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        // Expand the job row → detail loads → the "Run diagram" trigger appears.
        await page.ClickAsync(".job-item__summary", new() { Timeout = 30_000 });
        await page.ClickAsync(".run-dag-button", new() { Timeout = 30_000 });
        await page.WaitForSelectorAsync(".run-dag-panel", new() { Timeout = 30_000 });
        return page;
    }

    private static Task<bool> FocusInsidePanelAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => { const p = document.querySelector('.run-dag-panel'); return !!p && p.contains(document.activeElement); }");

    [Fact]
    public async Task Opening_IsADialog_ThatTakesFocus()
    {
        var page = await OpenModalAsync();

        var panel = page.Locator(".run-dag-panel");
        Assert.Equal("dialog", await panel.GetAttributeAsync("role"));
        Assert.Equal("true", await panel.GetAttributeAsync("aria-modal"));
        // The shipped consultologistDialog.activate moved focus into the panel.
        Assert.True(await FocusInsidePanelAsync(page));
    }

    [Fact]
    public async Task Tab_KeepsFocusInsideTheDialog()
    {
        var page = await OpenModalAsync();

        await page.Keyboard.PressAsync("Tab");
        Assert.True(await FocusInsidePanelAsync(page));

        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await FocusInsidePanelAsync(page));
    }

    [Fact]
    public async Task Escape_Closes_AndRestoresFocusToTheTrigger()
    {
        var page = await OpenModalAsync();

        await page.Keyboard.PressAsync("Escape");

        await page.WaitForSelectorAsync(".run-dag-panel", new() { State = WaitForSelectorState.Detached, Timeout = 30_000 });
        var activeClass = await page.EvaluateAsync<string>("() => document.activeElement?.className ?? ''");
        Assert.Contains("run-dag-button", activeClass);
    }
}
