using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #543: the profile card for how a pushed form response becomes a consult.
/// Empty until chosen; the unset line says what unset means (hold); only the
/// account's own word runs anything.
/// </summary>
public class FormResponseProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithChoice(string? stored)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetSettingAsync(FormResponsePreference.SettingKey).Returns(stored == null
            ? null
            : new AccountSettingResponse(FormResponsePreference.SettingKey, stored, "text/plain", DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string State(IRenderedComponent<Profile> page) => page.Find(".forms-mode-state").TextContent.Trim();

    private static string[] Buttons(IRenderedComponent<Profile> page) =>
        page.FindAll(".forms-mode-card fluent-button").Select(b => b.ClassName ?? "").ToArray();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("hold", "hold")]
    [InlineData(" RUNATONCE ", "runAtOnce")]
    [InlineData("maybe", null)]
    public void Parse_OnlyTheTwoWordsAct(string? stored, string? expected)
    {
        Assert.Equal(expected, FormResponsePreference.Parse(stored));
    }

    [Fact]
    public void NotChosen_SaysSo_AndWhatThatMeans_WithBothChoicesOffered()
    {
        WithChoice(null);

        var page = RenderProfile();

        Assert.Equal("Not chosen — responses are held for review, as today", State(page));
        Assert.Contains(Buttons(page), c => c.Contains("forms-mode-run"));
        Assert.Contains(Buttons(page), c => c.Contains("forms-mode-hold"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("forms-mode-clear"));
    }

    [Fact]
    public void RunAtOnce_IsShown_WithHoldAndBackOffered()
    {
        WithChoice("runAtOnce");

        var page = RenderProfile();

        Assert.StartsWith("Run at once — each pushed response starts a consult", State(page));
        Assert.Contains(Buttons(page), c => c.Contains("forms-mode-hold"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("forms-mode-run"));
        Assert.Contains(Buttons(page), c => c.Contains("forms-mode-clear"));
    }

    [Fact]
    public async Task ChoosingRunAtOnce_WritesTheExactWord_AndTheNoticeNamesTheRefusalRule()
    {
        WithChoice(null);
        var page = RenderProfile();

        await page.Find(".forms-mode-run").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(FormResponsePreference.SettingKey, "runAtOnce", "text/plain");
        Assert.StartsWith("Run at once — ", State(page));
        Assert.Contains("refused by name", page.Find(".forms-mode-message").TextContent);
        Assert.Contains("stays held", page.Find(".forms-mode-message").TextContent);
    }

    [Fact]
    public async Task ChoosingHold_WritesTheWord()
    {
        WithChoice("runAtOnce");
        var page = RenderProfile();

        await page.Find(".forms-mode-hold").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(FormResponsePreference.SettingKey, "hold", "text/plain");
        Assert.StartsWith("Hold for review — ", State(page));
    }

    [Fact]
    public async Task BackToNotChosen_DeletesTheSetting()
    {
        WithChoice("hold");
        var page = RenderProfile();

        await page.Find(".forms-mode-clear").ClickAsync(new());

        await AccountService.Received(1).DeleteSettingAsync(FormResponsePreference.SettingKey);
        Assert.Equal("Not chosen — responses are held for review, as today", State(page));
    }
}
