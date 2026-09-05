using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Consultologist.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;

namespace Consultologist.Api.Tests;

/// <summary>
/// #662: the Cerner binding of the shared SMART validator. The SMART/OIDC logic
/// is exercised through the Epic tests (same base); these pin that the Cerner
/// subclass reads the <c>Cerner:</c> config prefix and enforces the allowlist
/// before any network — the security property, for the second EHR.
/// </summary>
public class CernerIdTokenValidatorTests
{
    private const string SandboxIssuer =
        "https://authorization.cerner.com/tenants/ec2458f2-1e24-41c8-b71b-0e701af7583d/oidc/idsps/ec2458f2-1e24-41c8-b71b-0e701af7583d/";

    [Fact]
    public async Task ValidateAsync_ForeignIssuer_IsRefusedBeforeAnyNetwork_UsingTheCernerPrefix()
    {
        var foreign = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "https://evil.example/oauth2",
            claims: new[] { new Claim("sub", "s1") }));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Cerner:ClientId"] = "our-cerner-client-id",
            ["Cerner:AllowedIssuers"] = SandboxIssuer
        }).Build();

        var keyFetches = new List<string>();
        var validator = new CernerIdTokenValidator(config, NullLogger<CernerIdTokenValidator>.Instance,
            signingKeys: (issuer, _) =>
            {
                keyFetches.Add(issuer);
                return Task.FromResult<ICollection<SecurityKey>>(new List<SecurityKey>());
            });

        Assert.Null(await validator.ValidateAsync(foreign, CancellationToken.None));
        Assert.Empty(keyFetches);
    }

    [Fact]
    public async Task ValidateAsync_ReadsTheCernerClientId_NotEpics()
    {
        // Only Epic:ClientId is set; the Cerner validator must still treat its
        // own ClientId as missing (a configuration error), proving it reads the
        // Cerner prefix rather than sharing Epic's.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Epic:ClientId"] = "an-epic-client-id",
            ["Cerner:AllowedIssuers"] = SandboxIssuer
        }).Build();

        var validator = new CernerIdTokenValidator(config, NullLogger<CernerIdTokenValidator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("any.token.here", CancellationToken.None));
    }
}
