using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace Consultologist.Web.E2E.Tests;

/// <summary>
/// #1509 (Task 25; part 09 §§ 5.1 to 5.3; D1, D2): the Create page with a specVersion 31 package, in the served app.
/// The inputs drawn by <c>Input31</c>, the start checks' refusals above Start, a Remove inside a row (AGENTS.md's
/// label trap, which only a browser shows), and the checks asked again on a window focus.
/// </summary>
[Collection(BrowserCollection.Name)]
public class Create31E2ETests
{
    private readonly BrowserFixture _fixture;

    public Create31E2ETests(BrowserFixture fixture) => _fixture = fixture;

    private async Task<IPage> OpenAsync(Api31 api)
    {
        var page = await _fixture.Browser.NewPageAsync();
        await ApiMock.Install31Async(page, api);
        await page.GotoAsync($"{_fixture.BaseUrl}/create", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("[data-input-id='medicines']", new() { Timeout = 30_000 });
        return page;
    }

    private static ILocator Slot(IPage page, string id) => page.Locator($"[data-input-id='{id}']");

    private static ILocator Start(IPage page) => page.Locator("fluent-button", new() { HasTextString = "Create consult" });

    [Fact]
    public async Task Create31_ABooleanStartsUnset_AndALabelledChoiceShowsTitles()
    {
        var page = await OpenAsync(new Api31());

        // D2: three states, unset first, and the select rests on unset.
        var boolean = Slot(page, "include_billing").Locator("fluent-select");
        Assert.Equal(["choose…", "Yes", "No"], (await boolean.Locator("fluent-option").AllTextContentsAsync()).Select(t => t.Trim()));
        Assert.Equal("", await boolean.EvaluateAsync<string>("select => select.value"));
        Assert.Equal("choose…", (await boolean.Locator("fluent-option[aria-selected='true']").TextContentAsync())?.Trim());

        // A labelled choice shows its titles; each option's value is its const.
        var choice = Slot(page, "visit_type").Locator("fluent-select");
        Assert.Equal(["choose…", "New patient", "Follow-up"], (await choice.Locator("fluent-option").AllTextContentsAsync()).Select(t => t.Trim()));
        Assert.Equal("follow_up", await choice.Locator("fluent-option", new() { HasTextString = "Follow-up" }).EvaluateAsync<string>("option => option.value"));

        // D4: the help text from the content's resolve.json.
        Assert.Equal("Adds the billing section to the letter.", (await Slot(page, "include_billing").Locator(".input-field__help").TextContentAsync())?.Trim());

        // Nothing holds Start: no refusal, every input optional.
        await Assertions.Expect(Start(page)).Not.ToHaveAttributeAsync("disabled", "");
    }

    [Fact]
    public async Task Create31_ARefusalDisablesStart_AndReadsWordForWord()
    {
        var page = await OpenAsync(new Api31 { StartChecks = Fixtures31.TwoRefusals });

        var refusals = page.Locator(".start-checks31__refusal");
        await Assertions.Expect(refusals).ToHaveCountAsync(2);
        Assert.Equal([Fixtures31.TimeZoneRefusal, Fixtures31.SignatureRefusal], (await refusals.AllTextContentsAsync()).Select(t => t.Trim()));
        Assert.Equal("/profile", await page.Locator(".start-checks31__profile").GetAttributeAsync("href"));
        await Assertions.Expect(Start(page)).ToHaveAttributeAsync("disabled", "");
    }

    [Fact]
    public async Task Create31_TheStartChecks_AreAskedAgain_OnARealWindowFocus()
    {
        var api = new Api31 { StartChecks = Fixtures31.TwoRefusals };
        var page = await OpenAsync(api);
        await Assertions.Expect(page.Locator(".start-checks31__refusal")).ToHaveCountAsync(2);
        var before = api.HitsOf("StartChecks");
        Assert.True(before >= 1);

        // The account was fixed in another tab: the checks now pass. Headless Chromium keeps every tab focused, so a tab
        // switch (BringToFrontAsync, with or without CDP's focus emulation) fires no focus event: the window is sent the
        // browser's own focus event, which reaches the published windowFocus.js listener and the page through interop.
        api.StartChecks = Fixtures31.NoRefusals;
        await page.WaitForTimeoutAsync(200); // the listener is registered after the render that drew the refusals
        await page.EvaluateAsync("() => window.dispatchEvent(new FocusEvent('focus'))");

        Assert.True(await AskedAgainAsync(api, before), "the start checks were not asked again after a window focus");
        Assert.Equal(before + 1, api.HitsOf("StartChecks"));
        await Assertions.Expect(page.Locator(".start-checks31__refusal")).ToHaveCountAsync(0);
        await Assertions.Expect(Start(page)).Not.ToHaveAttributeAsync("disabled", "");
    }

    private static async Task<bool> AskedAgainAsync(Api31 api, int before)
    {
        for (var i = 0; i < 20 && api.HitsOf("StartChecks") == before; i++)
        {
            await Task.Delay(100);
        }

        return api.HitsOf("StartChecks") > before;
    }

    [Theory]
    [InlineData("medicines")]
    // A form-channel group is drawn inside the page's own <label> slot: the case AGENTS.md's trap is about.
    [InlineData("intake")]
    public async Task Create31_RemovingARow_LeavesNoEmptyRowBehind(string input)
    {
        var page = await OpenAsync(new Api31());
        var slot = Slot(page, input);
        var rows = slot.Locator(".input31__row");
        var add = slot.Locator("fluent-button.input31__add");
        // Every click that reaches the document: a <label>'s activation is one more than the clicks made here.
        await page.EvaluateAsync("() => { window.__clicks31 = 0; document.addEventListener('click', () => window.__clicks31++, true); }");

        await add.ClickAsync();
        await add.ClickAsync();
        await Assertions.Expect(rows).ToHaveCountAsync(2);

        await slot.Locator("fluent-button[aria-label='Remove entry 2']").ClickAsync();
        await Assertions.Expect(rows).ToHaveCountAsync(1);

        // The last row's Remove: the re-render drops the button under the click, and nothing puts a row back.
        await slot.Locator("fluent-button[aria-label='Remove entry 1']").ClickAsync();
        await Assertions.Expect(rows).ToHaveCountAsync(0);
        await page.WaitForTimeoutAsync(300);
        await Assertions.Expect(rows).ToHaveCountAsync(0);
        Assert.Equal(4, await page.EvaluateAsync<int>("() => window.__clicks31"));
    }
}
