using Bunit;

using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Shared;

using NSubstitute;

using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #679: every non-Active state carries a next step — the awaiting-activation
/// banner links to Profile, and a Disabled account gets a support mailto.
/// </summary>
public class AccountStatusBannerTests : ClientRenderTestContext
{
    private static AccountMeResponse Me(string status) => new(
        "user-1", "A Clinician", "clinician@example.com", status,
        new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        new[] { new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) });

    [Fact]
    public void Pending_WarnsAndLinksToProfile()
    {
        AccountService.GetCurrentAccountAsync().Returns(Me("Pending"));

        var page = Render<AccountStatusBanner>();

        Assert.Contains("awaiting activation", page.Markup);
        Assert.Contains("href=\"profile\"", page.Markup);
        Assert.Contains("Go to your profile", page.Markup);
    }

    [Fact]
    public void Disabled_IsAnErrorWithASupportMailto()
    {
        AccountService.GetCurrentAccountAsync().Returns(Me("Disabled"));

        var page = Render<AccountStatusBanner>();

        Assert.Contains("has been disabled", page.Markup);
        Assert.Contains("mailto:hello@consultologist.ai", page.Markup);
    }

    [Fact]
    public void Active_RendersNothing()
    {
        AccountService.GetCurrentAccountAsync().Returns(Me("Active"));

        var page = Render<AccountStatusBanner>();

        Assert.Empty(page.FindAll(".account-status-banner"));
    }
}
