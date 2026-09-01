using Consultologist.Api.Auth;

namespace Consultologist.Tests;

/// <summary>
/// #553: the tenant parse — from what the record already carries, never a
/// guess. Anything not shaped like an Entra issuer is null.
/// </summary>
public class TenantIdsTests
{
    [Theory]
    [InlineData("https://login.microsoftonline.com/11112222-3333-4444-5555-666677778888/v2.0", "11112222-3333-4444-5555-666677778888")]
    // A personal Microsoft account's issuer carries the consumers tenant —
    // personal accounts group under it by construction.
    [InlineData("https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0", "9188040d-6c67-4c5b-b112-36a304b66dad")]
    [InlineData("https://login.microsoftonline.com/11112222-3333-4444-5555-666677778888/", "11112222-3333-4444-5555-666677778888")]
    public void AnEntraIssuer_YieldsItsTenant(string issuer, string expected) =>
        Assert.Equal(expected, TenantIds.TenantIdOf(issuer));

    [Theory]
    [InlineData("https://www.linkedin.com/oauth")] // a verification signal, not a tenant
    [InlineData("https://login.microsoftonline.com/common/v2.0")] // not a concrete tenant
    [InlineData("https://evil.example/11112222-3333-4444-5555-666677778888/v2.0")] // wrong host
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_IsNull_NeverAGuess(string? issuer) =>
        Assert.Null(TenantIds.TenantIdOf(issuer));
}
