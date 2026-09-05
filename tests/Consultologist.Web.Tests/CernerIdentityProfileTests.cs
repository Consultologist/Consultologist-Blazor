using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #662: the Profile's Cerner (Oracle Health) identity card — the Epic card for
/// a second EHR: display + disconnect only (linking happens in the SMART panel),
/// no Connect button.
/// </summary>
public class CernerIdentityProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static AccountIdentity Cerner() =>
        new("cerner",
            "https://authorization.cerner.com/tenants/ec2458f2-1e24-41c8-b71b-0e701af7583d/oidc/idsps/ec2458f2-1e24-41c8-b71b-0e701af7583d/",
            "12742069", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DisplayName: "Practitioner/12742069");

    private void WithIdentities(params AccountIdentity[] linked)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), linked));
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    [Fact]
    public void NoCernerIdentity_InvitesLinkingFromCerner_AndOffersNoDisconnect()
    {
        WithIdentities(Entra());

        var page = RenderProfile();

        Assert.Contains("No Cerner identity linked", page.Find(".cerner-card").TextContent);
        Assert.Empty(page.FindAll(".cerner-card fluent-button"));
    }

    [Fact]
    public void ALinkedCernerIdentity_IsShown_AndDisconnectIsArmedInTwoClicks()
    {
        WithIdentities(Entra(), Cerner());

        var page = RenderProfile();
        Assert.Contains("Practitioner/12742069", page.Find(".cerner-card").TextContent);

        var button = page.Find(".cerner-card fluent-button");
        button.Click();
        Assert.Contains("Confirm disconnect", page.Find(".cerner-card fluent-button").TextContent);
        AccountService.DidNotReceive().DisconnectCernerLinkAsync();

        page.Find(".cerner-card fluent-button").Click();
        page.WaitForAssertion(() => AccountService.Received(1).DisconnectCernerLinkAsync());
    }
}
