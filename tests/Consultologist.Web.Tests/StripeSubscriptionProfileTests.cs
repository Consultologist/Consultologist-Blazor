using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #725: the Subscription card's paid personal-account door (Stripe). A personal
/// account sees Subscribe only when the deployment has Stripe provisioned; an
/// account with a linked Stripe subscription sees its state; without provisioning
/// it falls back to the LinkedIn pointer.
/// </summary>
public class StripeSubscriptionProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static AccountIdentity StripeLink() =>
        new("stripe", "https://stripe.com", "sub_9", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithPersonalAccount(string status, bool stripeAvailable, bool stripeLinked = false)
    {
        var identities = stripeLinked ? new[] { Entra(), StripeLink() } : new[] { Entra() };
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", status, Entra(), identities,
            AccountKind: "personal", StripeAvailable: stripeAvailable));
    }

    [Fact]
    public void PersonalPending_WithStripe_ShowsSubscribe()
    {
        WithPersonalAccount("Pending", stripeAvailable: true);

        var card = Render<Profile>().Find(".marketplace-card").TextContent;

        Assert.Contains("Subscribe to activate this account", card);
        Assert.Contains("Subscribe", card);
        Assert.DoesNotContain("Not available for personal accounts", card);
    }

    [Fact]
    public void PersonalWithLinkedStripe_ShowsActiveState()
    {
        WithPersonalAccount("Active", stripeAvailable: true, stripeLinked: true);

        var card = Render<Profile>().Find(".marketplace-card").TextContent;

        Assert.Contains("Active subscription", card);
        Assert.DoesNotContain("Subscribe to activate", card);
    }

    [Fact]
    public void PersonalLinkedStripe_PastDue_WarnsPaymentOverdue()
    {
        WithPersonalAccount("Unverified", stripeAvailable: true, stripeLinked: true);

        var card = Render<Profile>().Find(".marketplace-card").TextContent;

        Assert.Contains("payment is overdue", card);
    }

    [Fact]
    public void PersonalPending_WithoutStripe_FallsBackToLinkedInPointer()
    {
        WithPersonalAccount("Pending", stripeAvailable: false);

        var card = Render<Profile>().Find(".marketplace-card").TextContent;

        Assert.Contains("Not available for personal accounts", card);
        Assert.DoesNotContain("Subscribe to activate", card);
    }

    [Fact]
    public void ClickingSubscribe_StartsCheckout()
    {
        WithPersonalAccount("Pending", stripeAvailable: true);
        AccountService.StartStripeCheckoutAsync().Returns("https://checkout.stripe.com/c/pay/cs_1");

        var page = Render<Profile>();
        page.Find(".marketplace-card fluent-button").Click();

        AccountService.Received(1).StartStripeCheckoutAsync();
    }
}
