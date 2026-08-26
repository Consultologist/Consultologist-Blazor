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

    private void WithAccount(string? address = null, string? pending = null)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() },
            DeliveryAddress: address, DeliveryAddressPending: pending));
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
    [InlineData("Address is not a valid email address.", "Address is not a valid email address.")]
    [InlineData(null, "BadGateway")]
    public void EveryNamedReason_HasWords(string? error, string expected)
    {
        Assert.Contains(expected, AccountEndpointService.DeliveryAddressError(error, System.Net.HttpStatusCode.BadGateway));
    }
}
