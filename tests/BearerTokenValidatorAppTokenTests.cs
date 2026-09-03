using System.Security.Claims;
using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

/// <summary>
/// #610: only delegated user tokens enter. Before this gate the rule was an
/// accident of configuration — production's RequiredScope caught app-only
/// tokens because they carry no scp, and the log said "missing required
/// scope" about a caller that was never a user at all. The seam is asserted
/// directly (the RefusalWordFor precedent).
/// </summary>
public class BearerTokenValidatorAppTokenTests
{
    private static ClaimsPrincipal CreatePrincipal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value))));

    [Fact]
    public void AnApplicationToken_IsRefusedByName_EvenWithScopes()
    {
        // idtyp wins over everything: a token stamped app is refused as an
        // application token no matter what else it carries — never as a
        // scope problem.
        var principal = CreatePrincipal(("idtyp", "app"), ("scp", "access_as_user"));

        Assert.Equal(
            "Bearer token is an application token; only delegated user tokens are accepted.",
            BearerTokenValidator.AppTokenRefusal(principal, new[] { "access_as_user" }));
    }

    [Fact]
    public void ATokenWithNoDelegatedScopes_IsRefused_WithOrWithoutTheOptionalClaim()
    {
        // The load-bearing branch: idtyp is an optional claim the
        // registration must request, but app-only tokens never carry scp —
        // so a scopeless token is refused even when RequiredScope is unset
        // and the optional claim never arrived.
        var principal = CreatePrincipal(("oid", "service-principal-object-id"));

        Assert.Equal(
            "Bearer token carried no delegated scopes; only delegated user tokens are accepted.",
            BearerTokenValidator.AppTokenRefusal(principal, Array.Empty<string>()));
    }

    [Fact]
    public void ADelegatedToken_PassesTheGate()
    {
        var principal = CreatePrincipal(("scp", "access_as_user"), ("oid", "user-object-id"));

        Assert.Null(BearerTokenValidator.AppTokenRefusal(principal, new[] { "access_as_user" }));
    }

    [Fact]
    public void TheGateReadsTheSameScopesAsTheScopeCheck()
    {
        // The scopes parameter is GetScopes' output — whichever claim name
        // carried them (scp, scope, or the long form), the gate sees what
        // the RequiredScope check below it sees. A non-app idtyp value is
        // not the refusal's business.
        var principal = CreatePrincipal(("idtyp", "user"));

        Assert.Null(BearerTokenValidator.AppTokenRefusal(principal, new[] { "Files.Read", "access_as_user" }));
    }
}
