using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #561: the profile's snippet card — the signature card's shape without a
/// chosen one. Explicit initialisation, one row written per mutation, the
/// key deleted when the last snippet goes, remove armed in two clicks.
/// </summary>
public class SnippetProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithSnippets(string? value)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        AccountService.GetSettingAsync(Snippets.SettingKey).Returns(
            value == null
                ? (AccountSettingResponse?)null
                : new AccountSettingResponse(Snippets.SettingKey, value, Snippets.ContentType, DateTimeOffset.UtcNow));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    [Fact]
    public void NoSnippets_SaysSo_AndOffersTheForm()
    {
        WithSnippets(null);

        var page = RenderProfile();

        Assert.Contains("No snippets yet", page.Find(".snippet-state").TextContent);
        Assert.NotNull(page.Find(".snippet-name-input"));
        Assert.NotNull(page.Find(".snippet-text-input"));
    }

    [Fact]
    public async Task AddingASnippet_WritesTheOneRow()
    {
        WithSnippets(null);
        var page = RenderProfile();

        await page.Find(".snippet-name-input").ChangeAsync(new() { Value = "Normal exam" });
        await page.Find(".snippet-text-input").ChangeAsync(new() { Value = "Exam unremarkable." });
        await page.Find(".snippet-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(
            Snippets.SettingKey,
            Arg.Is<string>(v => v.Contains("\"Id\":\"normal-exam\"") && v.Contains("Exam unremarkable.")),
            Snippets.ContentType);
        Assert.Contains("1 snippet", page.Find(".snippet-state").TextContent);
    }

    [Fact]
    public async Task Editing_KeepsTheId_AndSavesTheNewText()
    {
        WithSnippets(SnippetsModelTests.Wire);
        var page = RenderProfile();

        await page.Find(".snippet-edit").ClickAsync(new());
        await page.Find(".snippet-text-input").ChangeAsync(new() { Value = "Updated text." });
        await page.Find(".snippet-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(
            Snippets.SettingKey,
            Arg.Is<string>(v => v.Contains("\"Id\":\"normal-exam\"") && v.Contains("Updated text.")),
            Snippets.ContentType);
    }

    [Fact]
    public async Task Remove_IsArmedInTwoClicks_AndTheLastRemovalDeletesTheKey()
    {
        WithSnippets(SnippetsModelTests.Wire);
        var page = RenderProfile();

        await page.Find(".snippet-remove").ClickAsync(new());

        // Armed, not removed: the label flips, nothing is written.
        Assert.Contains("Confirm remove", page.Find(".snippet-remove").TextContent);
        await AccountService.DidNotReceiveWithAnyArgs().DeleteSettingAsync(default!);

        await page.Find(".snippet-remove").ClickAsync(new());

        // The last snippet going deletes the row rather than writing an
        // empty set — the signature card's rule.
        await AccountService.Received(1).DeleteSettingAsync(Snippets.SettingKey);
        await AccountService.DidNotReceiveWithAnyArgs().SaveSettingAsync(default!, default!, default);
        Assert.Contains("No snippets yet", page.Find(".snippet-state").TextContent);
    }
}
