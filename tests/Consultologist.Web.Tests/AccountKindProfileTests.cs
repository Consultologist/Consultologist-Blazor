using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #556: the account's stored kind on the signed-in details card — the
/// account's, where the delivery card reads the token's. A pre-#556 account
/// not yet back-filled shows a dash, never a guess.
/// </summary>
public class AccountKindProfileTests : ClientRenderTestContext
{
    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithKind(string? accountKind)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() },
            AccountKind: accountKind));
    }

    [Fact]
    public void TheStoredKind_IsShown()
    {
        WithKind("organisation");

        Assert.Equal("organisation", Render<Profile>().Find(".account-kind").TextContent.Trim());
    }

    [Fact]
    public void APreBackfillAccount_ShowsADash_NeverAGuess()
    {
        WithKind(null);

        Assert.Equal("—", Render<Profile>().Find(".account-kind").TextContent.Trim());
    }
}
