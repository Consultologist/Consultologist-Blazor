using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #682: the profile cards are grouped under four titled sections with the
/// identity/activation cluster first — a scannable page, not one flat scroll.
/// </summary>
public class ProfileSectioningTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithAccount() =>
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() },
            AccountKind: "organisation"));

    [Fact]
    public void ProfileIsGroupedIntoFourTitledSections_ActivationFirst()
    {
        WithAccount();

        var page = Render<Profile>();

        var titles = page.FindAll(".profile-group__title").Select(t => t.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Identity & activation", "Delivery", "Workflow preferences", "Account" }, titles);

        // A representative card from each group is still rendered (classes preserved).
        Assert.NotEmpty(page.FindAll(".marketplace-card"));      // Identity & activation
        Assert.NotEmpty(page.FindAll(".delivery-address-card")); // Delivery
        Assert.NotEmpty(page.FindAll(".signature-card"));        // Workflow preferences
        Assert.NotEmpty(page.FindAll(".usage-card"));            // Account
    }
}
