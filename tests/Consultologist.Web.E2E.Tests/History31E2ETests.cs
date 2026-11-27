using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #1509 (Task 25; part 09 §§ 6.3, 6.4; R1): History with a job at record storage 12, in the served app. A blocked
/// deliverable is bannered and its text is shown only under "Refused, not delivered", with no copy control, at a desktop
/// and a phone width; a row's text is fetched once, when it is first opened; a banner's link opens its row in place.
/// </summary>
[Collection(BrowserCollection.Name)]
public class History31E2ETests
{
    private const string Refused = "deliverable.refused";
    private const string Note = "deliverable.note";

    private readonly BrowserFixture _fixture;

    public History31E2ETests(BrowserFixture fixture) => _fixture = fixture;

    private async Task<IPage> OpenAsync(Api31 api, int width = 1280)
    {
        var page = await _fixture.Browser.NewPageAsync(new() { ViewportSize = new() { Width = width, Height = 900 } });
        await ApiMock.Install31Async(page, api);
        await page.GotoAsync($"{_fixture.BaseUrl}/history/{Fixtures31.JobId}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("#history31-deliverables", new() { Timeout = 30_000 });
        return page;
    }

    private static ILocator Row(IPage page, string key) => page.Locator($"#history31-deliverables [id=\"row-{key}\"]");

    private static ILocator Toggle(IPage page, string key) => Row(page, key).Locator(".history31-deliverable__toggle");

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task History31_ABlockedDeliverable_IsBanneredAndMarkedRefused_WithNoCopyButton(int width)
    {
        var page = await OpenAsync(new Api31(), width);

        var banner = page.Locator(".history31-banner--blocked");
        await Assertions.Expect(banner).ToHaveTextAsync("Refused note was blocked: check 'gate' failed (The plan is not complete.).");

        await Toggle(page, Refused).ClickAsync();
        var panel = Row(page, Refused).Locator(".history31-refused");
        var heading = panel.Locator(".history31-refused__heading");
        await Assertions.Expect(heading).ToHaveTextAsync("Refused, not delivered");
        await Assertions.Expect(heading).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator(".history31-refused__text")).ToHaveTextAsync(Fixtures31.RefusedText);
        Assert.Equal("none", await panel.EvaluateAsync<string>("panel => getComputedStyle(panel).userSelect"));

        // The heading and the text fit the width: nothing of them is off the side of a phone.
        foreach (var part in new[] { heading, panel.Locator(".history31-refused__text") })
        {
            var box = await part.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.X >= 0 && box.X + box.Width <= width + 0.5, $"{box.X} + {box.Width} is outside {width}");
        }

        // No copy control in the refused row, while a released row has one.
        await Assertions.Expect(Row(page, Refused).Locator(".history31-deliverable__copy")).ToHaveCountAsync(0);
        await Assertions.Expect(Row(page, Refused).Locator("fluent-button, button", new() { HasTextString = "Copy" })).ToHaveCountAsync(0);
        await Toggle(page, Note).ClickAsync();
        await Assertions.Expect(Row(page, Note).Locator(".history31-deliverable__copy")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task History31_OpeningARow_FetchesItsTextOnce()
    {
        var api = new Api31();
        var page = await OpenAsync(api);
        Assert.Equal(0, api.HitsOf("Rows?key=" + Note));

        await Toggle(page, Note).ClickAsync();
        await Assertions.Expect(Toggle(page, Note)).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(Row(page, Note)).ToContainTextAsync(Fixtures31.ReleasedText);

        await Toggle(page, Note).ClickAsync();
        await Assertions.Expect(Toggle(page, Note)).ToHaveAttributeAsync("aria-expanded", "false");
        await Toggle(page, Note).ClickAsync();
        await Assertions.Expect(Row(page, Note)).ToContainTextAsync(Fixtures31.ReleasedText);
        await page.WaitForTimeoutAsync(300);

        Assert.Equal(1, api.HitsOf("Rows?key=" + Note));
        Assert.Equal(1, api.HitsOf("Rows"));
    }

    [Fact]
    public async Task History31_ABannersRowLink_OpensTheRow_AndKeepsThePageOnTheJob()
    {
        var page = await OpenAsync(new Api31());
        await Assertions.Expect(Toggle(page, Refused)).ToHaveAttributeAsync("aria-expanded", "false");

        var link = page.Locator(".history31-banner--blocked a");
        Assert.Equal($"/history/{Fixtures31.JobId}#row-{Refused}", await link.GetAttributeAsync("href"));
        await link.ClickAsync();

        await Assertions.Expect(Toggle(page, Refused)).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(Row(page, Refused).Locator(".history31-refused__heading")).ToBeVisibleAsync();
        await Assertions.Expect(Row(page, Refused)).ToBeFocusedAsync();
        Assert.Equal($"/history/{Fixtures31.JobId}", new Uri(page.Url).AbsolutePath);
    }
}
