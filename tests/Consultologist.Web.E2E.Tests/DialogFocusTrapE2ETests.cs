using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #707/#686: the run-diagram modal's focus trap and restoration — behaviour
/// bUnit can't assert (no real document.activeElement). This drives the ACTUAL
/// shipped wwwroot/js/dialog-focus.js against a minimal fixture DOM, so a
/// regression in the trap logic fails the test.
/// </summary>
[Collection(BrowserCollection.Name)]
public class DialogFocusTrapE2ETests
{
    private readonly BrowserFixture _fixture;

    public DialogFocusTrapE2ETests(BrowserFixture fixture) => _fixture = fixture;

    private static string DialogFocusJsPath() =>
        Path.Combine(BrowserFixture.RepoRoot(), "src", "Consultologist.Web", "wwwroot", "js", "dialog-focus.js");

    private const string Fixture = """
        <button id="trigger">open</button>
        <div class="run-dag-panel" tabindex="-1">
            <button class="run-dag-close">close</button>
            <button id="mid">mid</button>
        </div>
        """;

    private async Task<IPage> OpenAsync()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await page.SetContentAsync($"<!doctype html><html><body>{Fixture}</body></html>");
        await page.AddScriptTagAsync(new() { Path = DialogFocusJsPath() });
        await page.FocusAsync("#trigger");
        await page.EvaluateAsync("() => window.consultologistDialog.activate(document.querySelector('.run-dag-panel'))");
        return page;
    }

    private static Task<string> ActiveClassAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.activeElement?.className ?? ''");

    private static Task<string> ActiveIdAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.activeElement?.id ?? ''");

    [Fact]
    public async Task Activate_MovesFocusToTheFirstControlInThePanel()
    {
        var page = await OpenAsync();
        Assert.Equal("run-dag-close", await ActiveClassAsync(page));
    }

    [Fact]
    public async Task Tab_AtTheLastControl_WrapsToTheFirst()
    {
        var page = await OpenAsync();

        await page.FocusAsync("#mid");
        await page.Keyboard.PressAsync("Tab");

        Assert.Equal("run-dag-close", await ActiveClassAsync(page));
    }

    [Fact]
    public async Task ShiftTab_AtTheFirstControl_WrapsToTheLast()
    {
        var page = await OpenAsync();

        await page.FocusAsync(".run-dag-close");
        await page.Keyboard.PressAsync("Shift+Tab");

        Assert.Equal("mid", await ActiveIdAsync(page));
    }

    [Fact]
    public async Task Deactivate_RestoresFocusToTheTrigger()
    {
        var page = await OpenAsync();

        await page.EvaluateAsync("() => window.consultologistDialog.deactivate()");

        Assert.Equal("trigger", await ActiveIdAsync(page));
    }
}
