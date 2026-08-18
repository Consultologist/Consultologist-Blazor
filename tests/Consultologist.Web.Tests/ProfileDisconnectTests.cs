using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #195: the Disconnect action. These are the first tests of the Profile page —
/// worth having because this is the one destructive control in the app that
/// changes what the account is allowed to do.
/// </summary>
public class ProfileDisconnectTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static AccountIdentity LinkedIn() =>
        new("linkedin", "https://www.linkedin.com/oauth", "sub-li",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DisplayName: "A Clinician");

    private void WithAccount(string status, bool linkedIn)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1",
            "A Clinician",
            "clinician@example.com",
            status,
            Entra(),
            linkedIn ? new[] { Entra(), LinkedIn() } : new[] { Entra() }));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static IReadOnlyList<string> ButtonLabels(IRenderedComponent<Profile> page) =>
        page.FindAll("fluent-button").Select(button => button.TextContent.Trim()).ToList();

    [Fact]
    public void WithNoLinkedInConnected_ThereIsNothingToDisconnect()
    {
        WithAccount("Active", linkedIn: false);

        Assert.DoesNotContain("Disconnect", ButtonLabels(RenderProfile()));
    }

    [Fact]
    public void WithLinkedInConnected_DisconnectIsOffered()
    {
        WithAccount("Active", linkedIn: true);

        Assert.Contains("Disconnect", ButtonLabels(RenderProfile()));
    }

    [Fact]
    public async Task TheFirstClickArmsRatherThanActing()
    {
        // The whole confirmation mechanism: one click must not disconnect. A
        // dialog would say this more conventionally, but nothing renders a
        // FluentDialogProvider in this app.
        WithAccount("Active", linkedIn: true);
        var page = RenderProfile();

        await page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Disconnect").ClickAsync(new());

        await AccountService.DidNotReceive().DisconnectLinkedInAsync();
        Assert.Contains("Confirm disconnect", ButtonLabels(page));
    }

    [Fact]
    public async Task TheSecondClickDisconnects()
    {
        WithAccount("Active", linkedIn: true);
        var page = RenderProfile();

        await page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Disconnect").ClickAsync(new());
        await page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Confirm disconnect").ClickAsync(new());

        await AccountService.Received(1).DisconnectLinkedInAsync();
    }

    [Fact]
    public void AnUnverifiedAccount_IsToldWhatItCannotDoAndHowToFixIt()
    {
        // #195: an account that reads but cannot submit must be told which it
        // is. A silent 403 at the submit button would read as a bug.
        WithAccount("Unverified", linkedIn: false);

        var markup = RenderProfile().Markup;

        Assert.Contains("cannot start new ones", markup, StringComparison.Ordinal);
        Assert.Contains("Reconnect LinkedIn", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AnActiveAccount_IsNotWarned()
    {
        WithAccount("Active", linkedIn: true);

        Assert.DoesNotContain("cannot start new ones", RenderProfile().Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoDefaultSet_TheCardSaysSoAndNamesTheFallback()
    {
        // #390: empty until chosen. A preference nobody set is absent, not
        // 2:00 AM — but the card still has to say what will actually happen.
        WithAccount("Active", linkedIn: false);
        AccountService.GetSettingAsync(ScheduleDefault.SettingKey).Returns((AccountSettingResponse?)null);

        var markup = RenderProfile().Markup;

        Assert.Contains("Not set", markup, StringComparison.Ordinal);
        Assert.Contains("2:00 AM", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredDefault_IsShownAndSeedsTheControl()
    {
        WithAccount("Active", linkedIn: false);
        AccountService.GetSettingAsync(ScheduleDefault.SettingKey)
            .Returns(new AccountSettingResponse(
                ScheduleDefault.SettingKey, "06:30", "text/plain", DateTimeOffset.UtcNow));

        var page = RenderProfile();

        Assert.Equal("06:30", page.Find("input[type=time]").GetAttribute("value"));
    }

    [Fact]
    public async Task SavingTheDefault_WritesTheSettingAsPlainText()
    {
        WithAccount("Active", linkedIn: false);
        AccountService.GetSettingAsync(ScheduleDefault.SettingKey).Returns((AccountSettingResponse?)null);

        var page = RenderProfile();
        page.Find("input[type=time]").Change("07:45");
        await page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Set default").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(
            ScheduleDefault.SettingKey, "07:45", ScheduleDefault.ContentType);
    }

    [Fact]
    public async Task ClearingTheDefault_DeletesTheSetting()
    {
        WithAccount("Active", linkedIn: false);
        AccountService.GetSettingAsync(ScheduleDefault.SettingKey)
            .Returns(new AccountSettingResponse(
                ScheduleDefault.SettingKey, "06:30", "text/plain", DateTimeOffset.UtcNow));

        var page = RenderProfile();
        await page.FindAll("fluent-button").First(b => b.TextContent.Trim() == "Clear").ClickAsync(new());

        await AccountService.Received(1).DeleteSettingAsync(ScheduleDefault.SettingKey);
    }

    [Fact]
    public void AnUnreadableSetting_LeavesTheCardUsable()
    {
        // The tolerant read: a setting nobody can parse must not stop the page
        // rendering or the user setting a new one.
        WithAccount("Active", linkedIn: false);
        AccountService.GetSettingAsync(ScheduleDefault.SettingKey)
            .Returns(new AccountSettingResponse(
                ScheduleDefault.SettingKey, "half past six", "text/plain", DateTimeOffset.UtcNow));

        var page = RenderProfile();

        Assert.Contains("Not set", page.Markup, StringComparison.Ordinal);
        Assert.NotNull(page.Find("input[type=time]"));
    }
}
