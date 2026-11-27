using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #1509 (Task 25; part 09 §§ 7.2, 8.1; D12, D14): the Profile page's time zone card and the Editor's read-only view of
/// a 31 package, in the served app. The zone list is the WASM runtime's own, which no bUnit test can see.
/// </summary>
[Collection(BrowserCollection.Name)]
public class Profile31E2ETests
{
    private const string BrowserZone = "America/Toronto";

    private readonly BrowserFixture _fixture;

    public Profile31E2ETests(BrowserFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Profile31_TheZoneList_IsNotEmpty_AndTheBrowserZoneButtonFillsIt()
    {
        await using var context = await _fixture.Browser.NewContextAsync(new() { TimezoneId = BrowserZone });
        var page = await context.NewPageAsync();
        var api = new Api31();
        await ApiMock.Install31Async(page, api);
        await page.GotoAsync($"{_fixture.BaseUrl}/profile", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync(".time-zone-card", new() { Timeout = 30_000 });

        var select = page.Locator(".time-zone-select");
        Assert.Equal("", await select.InputValueAsync());
        // The placeholder and the zones: hundreds in a real runtime, and the browser's own among them.
        var zones = await select.Locator("option").CountAsync();
        Assert.True(zones > 100, $"the zone list holds {zones - 1} zones");
        await Assertions.Expect(select.Locator($"option[value='{BrowserZone}']")).ToHaveCountAsync(1);
        await Assertions.Expect(select.Locator("option[value='Europe/London']")).ToHaveCountAsync(1);

        await page.Locator("fluent-button.time-zone-browser").ClickAsync();
        await Assertions.Expect(select).ToHaveValueAsync(BrowserZone);
        await Assertions.Expect(page.Locator(".time-zone-message")).ToHaveCountAsync(0);
        // Filled, not saved: Save writes it.
        Assert.Equal(0, api.HitsWhere(name => name.StartsWith("PUT ", System.StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Editor31_OpensReadOnly_WithTheBar()
    {
        var page = await _fixture.Browser.NewPageAsync();
        await ApiMock.Install31Async(page, new Api31());
        await page.GotoAsync($"{_fixture.BaseUrl}/editor", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync(".v31-package", new() { Timeout = 30_000 });

        await Assertions.Expect(page.Locator(".v31-package__bar")).ToHaveTextAsync(
            "This package uses the new format. You can view and download it; editing returns in a later release.");

        var files = page.Locator(".v31-package__file");
        await Assertions.Expect(files.First).ToHaveTextAsync("manifest.json");
        await Assertions.Expect(files.Filter(new() { HasTextString = "nodes/plan.md" })).ToHaveCountAsync(1);

        // Opening a source file shows it as stored, and nothing on the page edits it.
        await files.Filter(new() { HasTextString = "nodes/plan.md" }).ClickAsync();
        await Assertions.Expect(page.Locator(".v31-package__view pre")).ToHaveTextAsync("Plan the sections of the letter for Margaret Okafor.");
        await Assertions.Expect(page.Locator("textarea, fluent-text-area, [contenteditable='true']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".editor-layout")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("fluent-button, button", new() { HasTextString = "Publish" })).ToHaveCountAsync(0);
    }
}
