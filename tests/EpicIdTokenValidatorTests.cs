using System.Security.Claims;
using Consultologist.Api.Auth;

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
}
