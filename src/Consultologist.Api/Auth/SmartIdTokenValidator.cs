using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Consultologist.Api.Auth;

/// <summary>The EHR-asserted identity a validated SMART id_token carries (#654; generalized across EHRs in #662).</summary>
public sealed record SmartIdentityClaims(string Issuer, string Subject, string? FhirUser, string? Name);

/// <summary>
/// Validates a SMART on FHIR id_token for the identity-link flow. Each EHR's
/// issuer is **per-installation** — a health system's Epic or Cerner is its own
/// issuer — so this trusts a CONFIGURED ALLOWLIST of issuers
/// (<c>{Prefix}:AllowedIssuers</c>) and nothing else. The allowlist is what
/// makes fetching an issuer's OIDC metadata/JWKS safe: an unlisted issuer is
/// refused before any network call, so a forged token cannot steer us to an
/// attacker's key endpoint. Audience is <c>{Prefix}:ClientId</c>. These
/// identities are never accepted as bearer credentials — this proves account
/// control once, at link time.
///
/// A single class, one config prefix per EHR: <see cref="EpicIdTokenValidator"/>
/// (#654) and <see cref="CernerIdTokenValidator"/> (#662) are thin subclasses
/// differing only in the prefix — the SMART/OIDC logic is identical, and
/// <c>fhirUser</c>/<c>iss</c>/<c>sub</c> are standard SMART claims, not one
/// vendor's invention.
/// </summary>
public abstract class SmartIdTokenValidator
{
    private readonly string _prefix;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    // One metadata manager per allowed issuer, built lazily and cached.
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new(StringComparer.Ordinal);

    // The signing keys for an issuer — a seam so a test can observe that it is
    // NEVER reached for an unlisted issuer (the allowlist's whole point is to
    // refuse before any key fetch).
    private readonly Func<string, CancellationToken, Task<ICollection<SecurityKey>>> _signingKeys;

    /// <param name="prefix">The config section for this EHR — "Epic", "Cerner".</param>
    protected SmartIdTokenValidator(
        string prefix,
        IConfiguration configuration,
        ILogger logger,
        Func<string, CancellationToken, Task<ICollection<SecurityKey>>>? signingKeys)
    {
        _prefix = prefix;
        _configuration = configuration;
        _logger = logger;
        _tokenHandler.MapInboundClaims = false;
        _signingKeys = signingKeys ?? DefaultSigningKeysAsync;
    }

    private async Task<ICollection<SecurityKey>> DefaultSigningKeysAsync(string issuer, CancellationToken cancellationToken)
    {
        var manager = _managers.GetOrAdd(issuer, iss => new ConfigurationManager<OpenIdConnectConfiguration>(
            iss.TrimEnd('/') + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever()));
        var oidc = await manager.GetConfigurationAsync(cancellationToken);
        return oidc.SigningKeys;
    }

    /// <summary>
    /// The allowed issuer set from a semicolon/comma/whitespace-separated
    /// setting. Internal so the allowlist decision is testable without a
    /// network round-trip.
    /// </summary>
    internal static IReadOnlySet<string> AllowedIssuers(string? setting) =>
        (setting ?? string.Empty)
            .Split(new[] { ';', ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    public async Task<SmartIdentityClaims?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        var clientId = _configuration[$"{_prefix}:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException($"{_prefix}:ClientId is not configured.");
        }

        var allowedIssuers = AllowedIssuers(_configuration[$"{_prefix}:AllowedIssuers"]);

        // The issuer is read from the (still unverified) token first, only to
        // decide whether it is one we will fetch keys for. Signature, audience
        // and lifetime are all still enforced below against that issuer's own
        // published keys — reading iss early admits nothing on its own.
        string issuer;
        try
        {
            issuer = _tokenHandler.ReadJwtToken(idToken).Issuer;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
        {
            _logger.LogWarning("{Provider} id_token is not a readable JWT.", _prefix);
            return null;
        }

        if (!allowedIssuers.Contains(issuer))
        {
            // Caller data — but the issuer is the operator-controlled decision
            // (which health systems are onboarded), not a secret, and naming it
            // is what makes an onboarding mistake diagnosable.
            _logger.LogWarning("{Provider} id_token issuer is not in the allowed set. Issuer={Issuer}", _prefix, issuer);
            return null;
        }

        try
        {
            var signingKeys = await _signingKeys(issuer, cancellationToken);

            var principal = _tokenHandler.ValidateToken(idToken, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                NameClaimType = "name"
            }, out _);

            return ReadClaims(principal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider} id_token validation failed.", _prefix);
            return null;
        }
    }

    /// <summary>
    /// The claims the link ceremony consumes. Extracted so it can be asserted
    /// directly. `sub` is required; the issuer is carried through so the bind
    /// namespaces the subject per installation (CreateSubjectHash folds issuer
    /// into the hash), and `fhirUser` (the Practitioner ref) is the display
    /// proof.
    /// </summary>
    internal static SmartIdentityClaims? ReadClaims(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirstValue("iss");
        var subject = principal.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        return new SmartIdentityClaims(
            issuer,
            subject,
            principal.FindFirstValue("fhirUser"),
            principal.FindFirstValue("name"));
    }
}
