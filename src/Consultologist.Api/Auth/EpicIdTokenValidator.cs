using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Consultologist.Api.Auth;

/// <summary>The Epic-asserted identity a validated SMART id_token carries (#654).</summary>
public sealed record EpicIdentityClaims(string Issuer, string Subject, string? FhirUser, string? Name);

public interface IEpicIdTokenValidator
{
    Task<EpicIdentityClaims?> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

/// <summary>
/// Validates Epic SMART id_tokens for the identity-link flow (#654). A
/// separate class from BearerTokenValidator (bound to the Entra authority)
/// and from LinkedInIdTokenValidator (bound to LinkedIn's single issuer):
/// Epic's issuer is per-installation — each health system's Epic is its own
/// issuer — so this trusts a CONFIGURED ALLOWLIST of issuers
/// (Epic:AllowedIssuers) and nothing else. The allowlist is what makes
/// fetching an issuer's OIDC metadata/JWKS safe: an unlisted issuer is
/// refused before any network call, so a forged token cannot steer us to an
/// attacker's key endpoint. Epic identities are never accepted as bearer
/// credentials — this proves account control once, at link time.
/// </summary>
public sealed class EpicIdTokenValidator : IEpicIdTokenValidator
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EpicIdTokenValidator> _logger;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    // One metadata manager per allowed issuer, built lazily and cached — the
    // per-instance analogue of the LinkedIn validator's single manager.
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers = new(StringComparer.Ordinal);

    // The signing keys for an issuer — a seam so a test can observe that it
    // is NEVER reached for an unlisted issuer (the allowlist's whole point is
    // to refuse before any key fetch). Production fetches the issuer's OIDC
    // metadata; the allowlist has already gated which issuers get here.
    private readonly Func<string, CancellationToken, Task<ICollection<SecurityKey>>> _signingKeys;

    public EpicIdTokenValidator(IConfiguration configuration, ILogger<EpicIdTokenValidator> logger)
        : this(configuration, logger, signingKeys: null)
    {
    }

    internal EpicIdTokenValidator(
        IConfiguration configuration,
        ILogger<EpicIdTokenValidator> logger,
        Func<string, CancellationToken, Task<ICollection<SecurityKey>>>? signingKeys)
    {
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
    /// The configured audience (our Epic app's client id) and the allowed
    /// issuer set. Kept internal so the allowlist decision is testable
    /// without a network round-trip.
    /// </summary>
    internal static IReadOnlySet<string> AllowedIssuers(string? setting) =>
        (setting ?? string.Empty)
            .Split(new[] { ';', ',', ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

    public async Task<EpicIdentityClaims?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        var clientId = _configuration["Epic:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Epic:ClientId is not configured.");
        }

        var allowedIssuers = AllowedIssuers(_configuration["Epic:AllowedIssuers"]);

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
            _logger.LogWarning("Epic id_token is not a readable JWT.");
            return null;
        }

        if (!allowedIssuers.Contains(issuer))
        {
            // Caller data — but the issuer is the operator-controlled decision
            // (which health systems are onboarded), not a secret, and naming
            // it is what makes an onboarding mistake diagnosable.
            _logger.LogWarning("Epic id_token issuer is not in the allowed set. Issuer={Issuer}", issuer);
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
            _logger.LogWarning(ex, "Epic id_token validation failed.");
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
    internal static EpicIdentityClaims? ReadClaims(ClaimsPrincipal principal)
    {
        var issuer = principal.FindFirstValue("iss");
        var subject = principal.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        return new EpicIdentityClaims(
            issuer,
            subject,
            principal.FindFirstValue("fhirUser"),
            principal.FindFirstValue("name"));
    }
}
