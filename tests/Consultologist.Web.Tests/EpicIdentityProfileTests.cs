using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #654: the Profile's Epic identity card — display + disconnect only
/// (linking happens in the SMART panel). Distinct from LinkedIn: no
/// Connect button here.
/// </summary>
public class EpicIdentityProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static AccountIdentity Epic() =>
        new("epic", "https://fhir.epic.com/interconnect-fhir-oauth/oauth2", "e6aw6-RJuKO2mbqjleKvgVQ3",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DisplayName: "Practitioner/e6aw6");

    private void WithIdentities(params AccountIdentity[] linked)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), linked));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    [Fact]
    public void NoEpicIdentity_InvitesLinkingFromEpic_AndOffersNoDisconnect()
    {
        WithIdentities(Entra());

        var page = RenderProfile();

        Assert.Contains("No Epic identity linked", page.Find(".epic-card").TextContent);
        Assert.Empty(page.FindAll(".epic-card fluent-button"));
    }

    [Fact]
    public void ALinkedEpicIdentity_IsShown_AndDisconnectIsArmedInTwoClicks()
    {
        WithIdentities(Entra(), Epic());

        var page = RenderProfile();
        Assert.Contains("Practitioner/e6aw6", page.Find(".epic-card").TextContent);

        var button = page.Find(".epic-card fluent-button");
        button.Click();
        Assert.Contains("Confirm disconnect", page.Find(".epic-card fluent-button").TextContent);
        AccountService.DidNotReceive().DisconnectEpicLinkAsync();

        page.Find(".epic-card fluent-button").Click();
        page.WaitForAssertion(() => AccountService.Received(1).DisconnectEpicLinkAsync());
    }
}
