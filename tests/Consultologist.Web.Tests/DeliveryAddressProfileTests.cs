using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>#486: the Delivery address card — its three states and its four actions.</summary>
public class DeliveryAddressProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithAccount(string? address = null, string? pending = null, string? verifiedBy = null, string? signInEmail = null, string? signInKind = null)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() },
            DeliveryAddress: address, DeliveryAddressPending: pending,
            DeliveryAddressVerifiedBy: verifiedBy, SignInEmail: signInEmail, SignInKind: signInKind));
    }

    // ----- #517: the signed-in email, on an organisation's token -----

    [Fact]
    public void AnOrganisationsSignIn_OffersItsOwnEmail_WithoutACode()
    {
        WithAccount(signInEmail: "dr.a@clinic.example", signInKind: "organisation");

        var page = RenderProfile();

        Assert.Equal("Use dr.a@clinic.example", page.Find(".use-signed-in-button").TextContent.Trim());
        Assert.Equal("Send code", page.Find(".send-code-button").TextContent.Trim());
    }

    [Fact]
    public void APersonalSignIn_IsNotOfferedIt()
    {
        // The personal account's address is verified for Microsoft, not for
        // this channel: the code stays the one way.
        WithAccount(signInEmail: "someone@outlook.example", signInKind: "personal");

        var page = RenderProfile();

        Assert.Empty(page.FindAll(".use-signed-in-button"));
        Assert.Equal("Send code", page.Find(".send-code-button").TextContent.Trim());
    }

    [Fact]
    public void OnceItIsTheVerifiedAddress_TheOfferGoes_AndTheStateSaysWhoVouched()
    {
        WithAccount(address: "dr.a@clinic.example", verifiedBy: "tenant", signInEmail: "Dr.A@clinic.example", signInKind: "organisation");

        var page = RenderProfile();

        Assert.Empty(page.FindAll(".use-signed-in-button"));
        Assert.Equal("Verified by your organisation: dr.a@clinic.example", State(page));
        Assert.Equal("Change address", page.Find(".send-code-button").TextContent.Trim());
    }

    [Fact]
    public void ACodeVerifiedAddress_ReadsAsBefore_AndStillOffersTheWorkEmail()
    {
        WithAccount(address: "other@clinic.example", verifiedBy: "code", signInEmail: "dr.a@clinic.example", signInKind: "organisation");

        var page = RenderProfile();

        Assert.Equal("Verified: other@clinic.example", State(page));
        Assert.NotNull(page.Find(".use-signed-in-button"));
    }

    [Fact]
    public void ClickingIt_SetsTheAddress_AndReloadsTheCard()
    {
        WithAccount(signInEmail: "dr.a@clinic.example", signInKind: "organisation");
        AccountService.UseSignedInDeliveryAddressAsync().Returns(Task.CompletedTask);

        var page = RenderProfile();
        page.Find(".use-signed-in-button").Click();

        page.WaitForAssertion(() => Assert.Contains("verified by your organisation", page.Markup));
        AccountService.Received(1).UseSignedInDeliveryAddressAsync();
        AccountService.Received(2).GetCurrentAccountAsync();
    }

    private IRenderedComponent<Profile> RenderProfile() => Render<Profile>();

    private static string State(IRenderedComponent<Profile> page) => page.Find(".delivery-address-state").TextContent.Trim();

    [Fact]
    public void NoAddress_IsNamedAsNotEmailed()
    {
        WithAccount();

        var page = RenderProfile();

        Assert.Equal("Not set — consults are not emailed", State(page));
        Assert.Equal("Send code", page.Find(".send-code-button").TextContent.Trim());
        Assert.Empty(page.FindAll(".confirm-address-button"));
        Assert.Empty(page.FindAll(".remove-address-button"));
    }

    [Fact]
    public void APendingCode_NamesTheAddressAndAsksForTheCode()
    {
        WithAccount(pending: "dr.a@clinic.example");

        var page = RenderProfile();

        Assert.Contains("Code sent to dr.a@clinic.example", State(page));
        Assert.NotEmpty(page.FindAll(".confirm-address-button"));
        Assert.NotEmpty(page.FindAll(".resend-code-button"));
    }

    [Fact]
    public void AVerifiedAddress_IsShown_WithChangeAndRemove()
    {
        WithAccount(address: "dr.a@clinic.example");

        var page = RenderProfile();

        Assert.Equal("Verified: dr.a@clinic.example", State(page));
        Assert.Equal("Change address", page.Find(".send-code-button").TextContent.Trim());
        Assert.NotEmpty(page.FindAll(".remove-address-button"));
        Assert.Empty(page.FindAll(".confirm-address-button"));
    }

    [Fact]
    public void SendCode_CallsTheServiceWithTheAddress()
    {
        WithAccount();
        var page = RenderProfile();

        page.Find("fluent-text-field[aria-label='Delivery address']").Change(" dr.a@clinic.example ");
        page.Find(".send-code-button").Click();

        page.WaitForAssertion(() => AccountService.Received(1).StartDeliveryAddressAsync("dr.a@clinic.example"));
        Assert.Contains("confirmation code has been sent", page.Markup);
    }

    [Fact]
    public void Confirm_CallsTheServiceWithTheCode()
    {
        WithAccount(pending: "dr.a@clinic.example");
        var page = RenderProfile();

        page.Find("fluent-text-field[aria-label='Confirmation code']").Change("123456");
        page.Find(".confirm-address-button").Click();

        page.WaitForAssertion(() => AccountService.Received(1).ConfirmDeliveryAddressAsync("123456"));
    }

    [Fact]
    public void Confirm_IsDisabledUntilSixCharacters()
    {
        WithAccount(pending: "dr.a@clinic.example");
        var page = RenderProfile();

        page.Find("fluent-text-field[aria-label='Confirmation code']").Change("12345");

        Assert.NotNull(page.Find(".confirm-address-button").GetAttribute("disabled"));
    }

    [Fact]
    public void ResendAndRemove_CallTheService()
    {
        WithAccount(address: "old@clinic.example", pending: "dr.a@clinic.example");
        var page = RenderProfile();

        page.Find(".resend-code-button").Click();
        page.WaitForAssertion(() => AccountService.Received(1).StartDeliveryAddressAsync("dr.a@clinic.example"));

        page.Find(".remove-address-button").Click();
        page.WaitForAssertion(() => AccountService.Received(1).ClearDeliveryAddressAsync());
    }

    [Fact]
    public void TheServersReason_IsShownInTheUsersWords()
    {
        WithAccount(pending: "dr.a@clinic.example");
        AccountService.ConfirmDeliveryAddressAsync(Arg.Any<string>())
            .Returns(_ => throw new HttpRequestException(AccountEndpointService.DeliveryAddressError("wrong", System.Net.HttpStatusCode.BadRequest)));
        var page = RenderProfile();

        page.Find("fluent-text-field[aria-label='Confirmation code']").Change("000000");
        page.Find(".confirm-address-button").Click();

        page.WaitForAssertion(() => Assert.Contains("That code is not right", page.Markup));
    }

    [Theory]
    [InlineData("expired", "expired")]
    [InlineData("too-many-attempts", "Too many attempts")]
    [InlineData("code-recently-sent", "a moment ago")]
    [InlineData("delivery-not-configured", "not configured")]
    [InlineData("personal-account", "personal Microsoft account")]
    [InlineData("no-signed-in-email", "no email address")]
    [InlineData("address-in-body", "never from the request")]
    [InlineData("Address is not a valid email address.", "Address is not a valid email address.")]
    [InlineData(null, "BadGateway")]
    public void EveryNamedReason_HasWords(string? error, string expected)
    {
        Assert.Contains(expected, AccountEndpointService.DeliveryAddressError(error, System.Net.HttpStatusCode.BadGateway));
    }
}
