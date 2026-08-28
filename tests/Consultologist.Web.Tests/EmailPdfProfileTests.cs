using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #518: the profile card for whether app-initiated runs are emailed. Empty
/// until chosen, and the unset line says what unset means.
/// </summary>
public class EmailPdfProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithChoice(string? stored)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetSettingAsync(EmailPdfPreference.SettingKey).Returns(stored == null
            ? null
            : new AccountSettingResponse(EmailPdfPreference.SettingKey, stored, "text/plain", DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string State(IRenderedComponent<Profile> page) => page.Find(".email-pdf-state").TextContent.Trim();

    private static string[] Buttons(IRenderedComponent<Profile> page) =>
        page.FindAll(".email-pdf-card fluent-button").Select(b => b.ClassName ?? "").ToArray();

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData(" FALSE ", false)]
    [InlineData("maybe", null)]
    public void Parse_OnlyFalseSilences(string? stored, bool? expected)
    {
        Assert.Equal(expected, EmailPdfPreference.Parse(stored));
    }

    [Fact]
    public void NotChosen_SaysSo_AndWhatThatMeans_WithBothChoicesOffered()
    {
        WithChoice(null);

        var page = RenderProfile();

        Assert.Equal("Not chosen — PDFs are sent, as today", State(page));
        Assert.Contains(Buttons(page), c => c.Contains("email-pdf-yes"));
        Assert.Contains(Buttons(page), c => c.Contains("email-pdf-no"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("email-pdf-clear"));
    }

    [Fact]
    public void No_IsShown_WithYesAndBackOffered()
    {
        WithChoice("false");

        var page = RenderProfile();

        Assert.StartsWith("No — runs started from the app are not emailed", State(page));
        Assert.Contains(Buttons(page), c => c.Contains("email-pdf-yes"));
        Assert.DoesNotContain(Buttons(page), c => c.Contains("email-pdf-no"));
        Assert.Contains(Buttons(page), c => c.Contains("email-pdf-clear"));
    }

    [Fact]
    public void Yes_IsShown()
    {
        WithChoice("true");

        Assert.StartsWith("Yes — each run started from the app is emailed", State(RenderProfile()));
    }

    [Fact]
    public async Task ChoosingNo_WritesFalse_AsPlainText()
    {
        WithChoice(null);
        var page = RenderProfile();

        await page.Find(".email-pdf-no").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(EmailPdfPreference.SettingKey, "false", "text/plain");
        Assert.StartsWith("No — ", State(page));
        Assert.Contains("will not be emailed", page.Find(".email-pdf-message").TextContent);
    }

    [Fact]
    public async Task ChoosingYes_WritesTrue()
    {
        WithChoice("false");
        var page = RenderProfile();

        await page.Find(".email-pdf-yes").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(EmailPdfPreference.SettingKey, "true", "text/plain");
        Assert.StartsWith("Yes — ", State(page));
    }

    [Fact]
    public async Task BackToNotChosen_DeletesTheSetting()
    {
        WithChoice("true");
        var page = RenderProfile();

        await page.Find(".email-pdf-clear").ClickAsync(new());

        await AccountService.Received(1).DeleteSettingAsync(EmailPdfPreference.SettingKey);
        Assert.Equal("Not chosen — PDFs are sent, as today", State(page));
    }
}
