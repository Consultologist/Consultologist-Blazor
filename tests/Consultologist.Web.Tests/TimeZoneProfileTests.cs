using Bunit;
using ApiKeys = Consultologist.Api.Auth.AccountSettingKeys;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1509 (Task 22; part 09 § 7.2; D12): the account's time zone on the Profile page, a select of IANA names that starts
/// empty, a "Use this browser's zone" button that fills it only when clicked, and S06-008 until a zone is set.
/// </summary>
public class TimeZoneProfileTests : ClientRenderTestContext
{
    private const string BrowserZoneFunction = "consultologistBrowserTimeZone";
    private const string S06008 = "Set your time zone in your account settings before running a workflow.";

    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithZone(string? zone)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetSettingAsync(TimeZones.SettingKey).Returns(zone == null
            ? null
            : new AccountSettingResponse(TimeZones.SettingKey, zone, "text/plain", DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string State(IRenderedComponent<Profile> page) => page.Find(".time-zone-state").TextContent.Trim();

    private static string? Selected(IRenderedComponent<Profile> page) =>
        page.FindAll(".time-zone-select option").FirstOrDefault(o => o.HasAttribute("selected"))?.GetAttribute("value");

    [Fact]
    public void TheSelect_StartsEmpty_WhenNoZoneIsSet()
    {
        WithZone(null);

        var page = RenderProfile();

        Assert.Equal("", Selected(page) ?? "");
        Assert.Equal("", page.Find(".time-zone-select").GetAttribute("value") ?? "");
        AccountService.DidNotReceive().SaveSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        AccountService.DidNotReceive().DeleteSettingAsync(Arg.Any<string>());
    }

    [Fact]
    public void ASavedZone_IsSelected_AndShown()
    {
        WithZone("America/Toronto");

        var page = RenderProfile();

        Assert.Equal("America/Toronto", Selected(page));
        Assert.Equal("America/Toronto", State(page));
    }

    [Fact]
    public void ItListsIanaNames_InOrder()
    {
        WithZone(null);

        var page = RenderProfile();

        var values = page.FindAll(".time-zone-select option").Select(o => o.GetAttribute("value")!).ToList();
        Assert.Equal("", values[0]);
        var ids = values.Skip(1).ToList();
        Assert.Contains("America/Toronto", ids);
        Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal).ToList(), ids);
        Assert.Equal(TimeZones.Iana(), ids);
    }

    [Fact]
    public async Task PickingAZone_SavesIt()
    {
        WithZone(null);
        var page = RenderProfile();

        await page.Find(".time-zone-select").ChangeAsync(new() { Value = "America/Toronto" });
        await page.Find(".time-zone-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(TimeZones.SettingKey, "America/Toronto", "text/plain");
        Assert.Equal("America/Toronto", State(page));
    }

    [Fact]
    public void UseThisBrowsersZone_FillsTheSelect_OnlyWhenClicked()
    {
        WithZone(null);
        JSInterop.Setup<string?>(BrowserZoneFunction).SetResult("America/Toronto");

        var page = RenderProfile();

        // Nothing is read or filled on render.
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier == BrowserZoneFunction);
        Assert.Equal("", Selected(page) ?? "");

        page.Find(".time-zone-browser").Click();

        Assert.Single(JSInterop.Invocations, i => i.Identifier == BrowserZoneFunction);
        Assert.Equal("America/Toronto", Selected(page));
    }

    [Fact]
    public void UseThisBrowsersZone_FillsTheSelect_AndSavesNothingUntilSaveIsPressed()
    {
        WithZone(null);
        JSInterop.Setup<string?>(BrowserZoneFunction).SetResult("America/Toronto");
        var page = RenderProfile();

        page.Find(".time-zone-browser").Click();

        AccountService.DidNotReceive().SaveSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        Assert.Equal(S06008, State(page));
    }

    [Fact]
    public void ABrowserZoneTheListDoesNotHold_IsNotSelected_AndSaysSo()
    {
        WithZone(null);
        JSInterop.Setup<string?>(BrowserZoneFunction).SetResult("Mars/Olympus_Mons");
        var page = RenderProfile();

        page.Find(".time-zone-browser").Click();

        Assert.Equal("", Selected(page) ?? "");
        var message = page.Find(".time-zone-message").TextContent;
        Assert.Contains("isn't one we can offer", message);
        Assert.DoesNotContain("Mars", message);
    }

    [Fact]
    public async Task ClearingTheSelect_DeletesTheSetting()
    {
        WithZone("America/Toronto");
        var page = RenderProfile();

        await page.Find(".time-zone-select").ChangeAsync(new() { Value = "" });
        await page.Find(".time-zone-save").ClickAsync(new());

        await AccountService.Received(1).DeleteSettingAsync(TimeZones.SettingKey);
        await AccountService.DidNotReceive().SaveSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        Assert.Equal(S06008, State(page));
    }

    [Fact]
    public void UntilItIsSet_TheCardSaysS06008()
    {
        WithZone(null);

        var page = RenderProfile();

        Assert.Equal(S06008, State(page));
        Assert.Equal(S06008, Consultologist.Api.V31.V31StartChecks.NoTimeZone);
    }

    [Fact]
    public async Task TheEnginesRefusalOfAZone_IsShown()
    {
        WithZone(null);
        const string refusal = "account.timeZone must be an IANA time zone name, such as America/Toronto.";
        AccountService.SaveSettingAsync(TimeZones.SettingKey, Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromException(new HttpRequestException(refusal, null, System.Net.HttpStatusCode.BadRequest)));
        var page = RenderProfile();

        await page.Find(".time-zone-select").ChangeAsync(new() { Value = "America/Toronto" });
        await page.Find(".time-zone-save").ClickAsync(new());

        Assert.Contains(refusal, page.Find(".time-zone-message").TextContent);
        Assert.Equal(S06008, State(page));
    }

    [Fact]
    public async Task AFailedLoad_ShowsNoState_AndASaveOfNothingSaysNothingAboutWhatIsSet()
    {
        // Final review C3: the read failed, so the card does not know whether a zone is set.
        WithZone(null);
        AccountService.GetSettingAsync(TimeZones.SettingKey)
            .Returns<Task<AccountSettingResponse?>>(_ => throw new HttpRequestException("down"));
        var page = RenderProfile();

        Assert.Empty(page.FindAll(".time-zone-state"));
        Assert.DoesNotContain(S06008, page.Markup, StringComparison.Ordinal);

        await page.Find(".time-zone-save").ClickAsync(new());

        Assert.DoesNotContain("Your time zone is not set.", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll(".time-zone-state"));
        await AccountService.DidNotReceive().DeleteSettingAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task AFailedLoad_ThenSavingAZone_SavesIt_AndShowsIt()
    {
        WithZone(null);
        AccountService.GetSettingAsync(TimeZones.SettingKey)
            .Returns<Task<AccountSettingResponse?>>(_ => throw new HttpRequestException("down"));
        var page = RenderProfile();

        await page.Find(".time-zone-select").ChangeAsync(new() { Value = "America/Toronto" });
        await page.Find(".time-zone-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(TimeZones.SettingKey, "America/Toronto", Arg.Any<string>());
        Assert.Equal("America/Toronto", State(page));
    }

    [Fact]
    public void TheKey_MirrorsTheApis()
    {
        Assert.Equal(ApiKeys.TimeZone, TimeZones.SettingKey);
    }

    [Fact]
    public void EveryZoneTheCardOffers_IsOneTheApiAccepts()
    {
        Assert.All(TimeZones.Iana(), id => Assert.True(Consultologist.Api.V31.V31StartChecks.IsZone(id), id));
    }
}
