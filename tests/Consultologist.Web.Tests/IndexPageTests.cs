using Bunit;

using Consultologist.Web.Services.Accounts;

using NSubstitute;

using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #679: the Home page's primary card routes a not-yet-active account to
/// activation instead of inviting a consult it cannot run.
/// </summary>
public class IndexPageTests : ClientRenderTestContext
{
    private static AccountMeResponse Me(string status) => new(
        "user-1", "A Clinician", "clinician@example.com", status,
        new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        new[] { new AccountIdentity("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow) });

    [Fact]
    public void PendingAccount_ShowsActivateCta_NotCreateConsult()
    {
        AccountService.GetCurrentAccountAsync().Returns(Me("Pending"));

        var page = Render<Consultologist.Web.Pages.Index>();

        Assert.Contains("Activate your account", page.Markup);
        Assert.Contains("href=\"profile\"", page.Markup);
        Assert.DoesNotContain("Create Consult", page.Markup);
        // The read cards still stand.
        Assert.Contains("Open history", page.Markup);
    }

    [Fact]
    public void ActiveAccount_ShowsCreateConsult()
    {
        AccountService.GetCurrentAccountAsync().Returns(Me("Active"));

        var page = Render<Consultologist.Web.Pages.Index>();

        Assert.Contains("Create Consult", page.Markup);
        Assert.DoesNotContain("Activate your account", page.Markup);
    }
}
