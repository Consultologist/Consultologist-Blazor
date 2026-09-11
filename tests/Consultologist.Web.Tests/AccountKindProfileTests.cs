using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #556: the account's stored kind on the signed-in details card — the
/// account's, where the delivery card reads the token's. A pre-#556 account
/// not yet back-filled shows a dash, never a guess. #680: the kind also steers
/// the Subscription (Marketplace) card, which is org-only.
/// </summary>
public class AccountKindProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithAccount(string? accountKind, string status = "Active")
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", status, Entra(), new[] { Entra() },
            AccountKind: accountKind));
    }

    [Fact]
    public void TheStoredKind_IsShown_Humanised()
    {
        WithAccount("organisation");
        Assert.Equal("Work or school", Render<Profile>().Find(".account-kind").TextContent.Trim());

        WithAccount("personal");
        Assert.Equal("Personal", Render<Profile>().Find(".account-kind").TextContent.Trim());
    }

    [Fact]
    public void APreBackfillAccount_ShowsADash_NeverAGuess()
    {
        WithAccount(null);

        Assert.Equal("—", Render<Profile>().Find(".account-kind").TextContent.Trim());
    }

    // ----- #680: the Subscription card steers by AccountKind -----

    [Fact]
    public void PersonalAccount_SubscriptionCard_IsMarkedNotApplicable()
    {
        WithAccount("personal");

        var card = Render<Profile>().Find(".marketplace-card").TextContent;

        Assert.Contains("for work or school accounts", card);
        Assert.Contains("Not available for personal accounts", card);
        // No purchase framing for a personal account.
        Assert.DoesNotContain("Configure account", card);
    }

    [Fact]
    public void OrganisationPending_ShowsStartASubscriptionCue()
    {
        WithAccount("organisation", status: "Pending");

        var card = Render<Profile>().Find(".marketplace-card").TextContent;

        Assert.Contains("Start a subscription to activate", card);
        Assert.Contains("Configure account", card);
    }
}
