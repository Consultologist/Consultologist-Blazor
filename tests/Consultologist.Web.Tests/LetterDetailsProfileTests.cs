using Bunit;
using ApiKeys = Consultologist.Api.Auth.AccountSettingKeys;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1509 (Task 21; part 09 § 7.1; D13): the Letter details card, a profile card for the three fields a 31 package reads
/// as <c>profile.*</c>. A blank value is not set, so saving one deletes the setting.
/// </summary>
public class LetterDetailsProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithSaved(string? credentials = null, string? organisation = null, string? closing = null)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() }));
        Set(LetterDetails.CredentialsKey, credentials);
        Set(LetterDetails.OrganisationKey, organisation);
        Set(LetterDetails.ClosingKey, closing);
    }

    private void Set(string key, string? value) =>
        AccountService.GetSettingAsync(key).Returns(value == null
            ? null
            : new AccountSettingResponse(key, value, "text/plain", DateTimeOffset.UtcNow));

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string State(IRenderedComponent<Profile> page, string field) =>
        page.Find($".letter-{field}-state").TextContent.Trim();

    [Fact]
    public void TheCard_HasCredentials_Organisation_AndClosingLine_WithTheirControls()
    {
        WithSaved();

        var page = RenderProfile();

        var card = page.Find(".letter-details-card");
        Assert.Contains("Letter details", card.TextContent);
        Assert.Equal("input", page.Find(".letter-credentials-input").TagName.ToLowerInvariant());
        Assert.Equal("input", page.Find(".letter-organisation-input").TagName.ToLowerInvariant());
        Assert.Equal("textarea", page.Find(".letter-closing-input").TagName.ToLowerInvariant());
        Assert.Equal("Credentials", page.Find(".letter-credentials-input").GetAttribute("aria-label"));
        Assert.Equal("Organisation", page.Find(".letter-organisation-input").GetAttribute("aria-label"));
        Assert.Equal("Closing line", page.Find(".letter-closing-input").GetAttribute("aria-label"));
        Assert.Single(page.FindAll(".letter-details-card textarea"));
        Assert.Equal(2, page.FindAll(".letter-details-card input").Count);
    }

    [Fact]
    public void ItLoadsWhatIsSaved()
    {
        WithSaved("MD, FRCPC", "Sample Oncology Clinic", "Kind regards,\nDr A. Sample");

        var page = RenderProfile();

        Assert.Equal("MD, FRCPC", State(page, "credentials"));
        Assert.Equal("Sample Oncology Clinic", State(page, "organisation"));
        Assert.Equal("MD, FRCPC", page.Find(".letter-credentials-input").GetAttribute("value"));
        Assert.Equal("Sample Oncology Clinic", page.Find(".letter-organisation-input").GetAttribute("value"));
        Assert.Equal("Kind regards,\nDr A. Sample", page.Find(".letter-closing-input").GetAttribute("value")?.Replace("\r", ""));
        Assert.Contains("Dr A. Sample", State(page, "closing"));
    }

    [Fact]
    public void AnUnsetField_ReadsNotSet()
    {
        WithSaved(credentials: "MD");

        var page = RenderProfile();

        Assert.Equal("MD", State(page, "credentials"));
        Assert.Equal("Not set", State(page, "organisation"));
        Assert.Equal("Not set", State(page, "closing"));
    }

    [Fact]
    public async Task Saving_WritesEachChangedField_Trimmed_AsTextPlain()
    {
        WithSaved(organisation: "Sample Oncology Clinic");
        var page = RenderProfile();

        await page.Find(".letter-credentials-input").ChangeAsync(new() { Value = "  MD, FRCPC  " });
        await page.Find(".letter-closing-input").ChangeAsync(new() { Value = "\nKind regards,\nDr A. Sample  \n" });
        await page.Find(".letter-details-save").ClickAsync(new());

        await AccountService.Received(1).SaveSettingAsync(LetterDetails.CredentialsKey, "MD, FRCPC", "text/plain");
        await AccountService.Received(1).SaveSettingAsync(LetterDetails.ClosingKey, "Kind regards,\nDr A. Sample", "text/plain");
        // The organisation did not change: it is not written.
        await AccountService.DidNotReceive().SaveSettingAsync(LetterDetails.OrganisationKey, Arg.Any<string>(), Arg.Any<string>());
        await AccountService.DidNotReceive().DeleteSettingAsync(Arg.Any<string>());
        Assert.Equal("MD, FRCPC", State(page, "credentials"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \t ")]
    public async Task SavingABlankOrWhitespaceValue_DeletesTheSetting(string blank)
    {
        WithSaved(credentials: "MD", organisation: "Sample Oncology Clinic", closing: "Regards");
        var page = RenderProfile();

        await page.Find(".letter-organisation-input").ChangeAsync(new() { Value = blank });
        await page.Find(".letter-details-save").ClickAsync(new());

        await AccountService.Received(1).DeleteSettingAsync(LetterDetails.OrganisationKey);
        await AccountService.DidNotReceive().SaveSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
        Assert.Equal("Not set", State(page, "organisation"));
        Assert.Equal("MD", State(page, "credentials"));
    }

    [Fact]
    public async Task SavingABlankValueOfAnUnsetField_WritesNothing()
    {
        WithSaved();
        var page = RenderProfile();

        await page.Find(".letter-credentials-input").ChangeAsync(new() { Value = "  " });
        await page.Find(".letter-details-save").ClickAsync(new());

        await AccountService.DidNotReceive().DeleteSettingAsync(Arg.Any<string>());
        await AccountService.DidNotReceive().SaveSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void EachField_StopsAtItsLimit()
    {
        WithSaved();

        var page = RenderProfile();

        Assert.Equal("120", page.Find(".letter-credentials-input").GetAttribute("maxlength"));
        Assert.Equal("200", page.Find(".letter-organisation-input").GetAttribute("maxlength"));
        Assert.Equal("4000", page.Find(".letter-closing-input").GetAttribute("maxlength"));
    }

    [Fact]
    public async Task EachField_StopsAtItsLimit_AndShowsTheEnginesRefusalIfOneArrives()
    {
        WithSaved();
        const string refusal = "profile.credentials must be at most 120 characters.";
        AccountService.SaveSettingAsync(LetterDetails.CredentialsKey, Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromException(new HttpRequestException(refusal, null, System.Net.HttpStatusCode.BadRequest)));
        var page = RenderProfile();

        await page.Find(".letter-credentials-input").ChangeAsync(new() { Value = "MD" });
        await page.Find(".letter-details-save").ClickAsync(new());

        Assert.Contains(refusal, page.Find(".letter-details-message").TextContent);
        Assert.Equal("Not set", State(page, "credentials"));
    }

    [Fact]
    public async Task ATransportFailure_SaysItCouldNotSave_NotTheRawMessage()
    {
        WithSaved();
        AccountService.SaveSettingAsync(LetterDetails.ClosingKey, Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromException(new HttpRequestException("Account setting save endpoint failed: ServiceUnavailable")));
        var page = RenderProfile();

        await page.Find(".letter-closing-input").ChangeAsync(new() { Value = "Regards" });
        await page.Find(".letter-details-save").ClickAsync(new());

        Assert.Contains("We couldn't save your letter details", page.Find(".letter-details-message").TextContent);
    }

    [Fact]
    public void TheLimits_MirrorTheApis()
    {
        Assert.Equal(ApiKeys.ProfileCredentials, LetterDetails.CredentialsKey);
        Assert.Equal(ApiKeys.ProfileOrganisation, LetterDetails.OrganisationKey);
        Assert.Equal(ApiKeys.ProfileClosing, LetterDetails.ClosingKey);

        // The Api's limits are internal consts of Account; its validator is the check: a value at the limit passes and one
        // character over is refused, for each key.
        foreach (var (key, limit) in new[]
        {
            (LetterDetails.CredentialsKey, LetterDetails.MaxCredentialsLength),
            (LetterDetails.OrganisationKey, LetterDetails.MaxOrganisationLength),
            (LetterDetails.ClosingKey, LetterDetails.MaxClosingLength),
        })
        {
            Assert.Null(Consultologist.Api.Account.ValidateProfileSetting(key, new string('x', limit)));
            Assert.NotNull(Consultologist.Api.Account.ValidateProfileSetting(key, new string('x', limit + 1)));
        }

        Assert.Equal(Consultologist.Api.Account.MaxCredentialsLength, LetterDetails.MaxCredentialsLength);
        Assert.Equal(Consultologist.Api.Account.MaxOrganisationLength, LetterDetails.MaxOrganisationLength);
        Assert.Equal(Consultologist.Api.Account.MaxClosingLength, LetterDetails.MaxClosingLength);
    }
}
