using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Consultologist.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.Tests;

/// <summary>
/// #654: the Epic SMART id_token validator. The signature/JWKS edge is the
/// same ConfigurationManager machinery the LinkedIn validator uses; the
/// decisions this pins are the issuer allowlist and the claim read, both
/// asserted directly (no network).
/// </summary>
public class EpicIdTokenValidatorTests
{
    private static ClaimsPrincipal CreatePrincipal(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value))));

    [Theory]
    [InlineData("https://fhir.epic.com/interconnect-fhir-oauth/oauth2;https://other.example/oauth2",
        "https://fhir.epic.com/interconnect-fhir-oauth/oauth2", true)]
    [InlineData("https://fhir.epic.com/interconnect-fhir-oauth/oauth2",
        "https://evil.example/oauth2", false)]
    [InlineData("", "https://fhir.epic.com/interconnect-fhir-oauth/oauth2", false)]
    [InlineData(null, "https://fhir.epic.com/interconnect-fhir-oauth/oauth2", false)]
    public void AllowedIssuers_IsAConfiguredSet(string? setting, string issuer, bool allowed)
    {
        Assert.Equal(allowed, EpicIdTokenValidator.AllowedIssuers(setting).Contains(issuer));
    }

    [Fact]
    public void AllowedIssuers_SplitsOnTheUsualSeparators()
    {
        var set = EpicIdTokenValidator.AllowedIssuers("https://a.example/o , https://b.example/o\nhttps://c.example/o");

        Assert.Equal(3, set.Count);
        Assert.Contains("https://a.example/o", set);
        Assert.Contains("https://c.example/o", set);
    }

    [Fact]
    public void ReadClaims_CarriesIssuerSubjectFhirUser()
    {
        var principal = CreatePrincipal(
            ("iss", "https://fhir.epic.com/interconnect-fhir-oauth/oauth2"),
            ("sub", "e6aw6-RJuKO2mbqjleKvgVQ3"),
            ("fhirUser", "https://fhir.epic.com/interconnect-fhir-oauth/api/FHIR/R4/Practitioner/e6aw6-RJuKO2mbqjleKvgVQ3"),
            ("name", "Dr Test"));

        var claims = EpicIdTokenValidator.ReadClaims(principal);

        Assert.NotNull(claims);
        Assert.Equal("https://fhir.epic.com/interconnect-fhir-oauth/oauth2", claims!.Issuer);
        Assert.Equal("e6aw6-RJuKO2mbqjleKvgVQ3", claims.Subject);
        Assert.Contains("Practitioner/", claims.FhirUser);
        Assert.Equal("Dr Test", claims.Name);
    }

    [Theory]
    [InlineData("iss-only")]
    [InlineData("sub-only")]
    public void ReadClaims_RequiresBothIssuerAndSubject(string which)
    {
        var principal = which == "iss-only"
            ? CreatePrincipal(("iss", "https://fhir.epic.com/interconnect-fhir-oauth/oauth2"))
            : CreatePrincipal(("sub", "s1"));

        Assert.Null(EpicIdTokenValidator.ReadClaims(principal));
    }

    [Fact]
    public async Task ValidateAsync_ForeignIssuer_IsRefusedBeforeAnyNetwork()
    {
        // The allowlist enforcement, not just the set: a token whose issuer
        // is not listed is refused after ReadJwtToken and BEFORE any JWKS
        // fetch — so this needs no network and no valid signature. A forged
        // token cannot steer key resolution to an attacker's endpoint.
        var foreign = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "https://evil.example/oauth2",
            claims: new[] { new Claim("sub", "s1") }));

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Epic:ClientId"] = "our-epic-client-id",
            ["Epic:AllowedIssuers"] = "https://fhir.epic.com/interconnect-fhir-oauth/oauth2"
        }).Build();

        var keyFetches = new List<string>();
        var validator = new EpicIdTokenValidator(config, NullLogger<EpicIdTokenValidator>.Instance,
            signingKeys: (issuer, _) =>
            {
                keyFetches.Add(issuer);
                return Task.FromResult<ICollection<Microsoft.IdentityModel.Tokens.SecurityKey>>(
                    new List<Microsoft.IdentityModel.Tokens.SecurityKey>());
            });

        Assert.Null(await validator.ValidateAsync(foreign, CancellationToken.None));
        // The security property: the allowlist refuses BEFORE any key fetch,
        // so no metadata resolution was even attempted for the foreign issuer.
        Assert.Empty(keyFetches);
    }

    [Fact]
    public async Task ValidateAsync_MissingClientId_IsAConfigurationError()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Epic:AllowedIssuers"] = "https://fhir.epic.com/interconnect-fhir-oauth/oauth2"
        }).Build();

        var validator = new EpicIdTokenValidator(config, NullLogger<EpicIdTokenValidator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.ValidateAsync("any.token.here", CancellationToken.None));
    }
}
