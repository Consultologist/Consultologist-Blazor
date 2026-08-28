using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Consultologist.Api.Auth;

public interface IBearerTokenValidator
{
    Task<AuthenticatedUser?> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken);
}

public sealed class BearerTokenValidator : IBearerTokenValidator
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BearerTokenValidator> _logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public BearerTokenValidator(IConfiguration configuration, ILogger<BearerTokenValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _tokenHandler.MapInboundClaims = false;

        var authority = GetRequiredConfiguration("Auth:Authority").TrimEnd('/');
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());
    }

    public async Task<AuthenticatedUser?> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var authority = GetRequiredConfiguration("Auth:Authority").TrimEnd('/');
            var audience = GetRequiredConfiguration("Auth:Audience");
            var requiredScope = _configuration["Auth:RequiredScope"];
            var oidcConfiguration = await _configurationManager.GetConfigurationAsync(cancellationToken);

            var principal = _tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                IssuerValidator = (issuer, _, _) => ValidateIssuer(authority, oidcConfiguration.Issuer, issuer),
                ValidateAudience = true,
                ValidAudiences = GetValidAudiences(audience),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = oidcConfiguration.SigningKeys,
                NameClaimType = "name"
            }, out _);

            var scopes = GetScopes(principal).ToArray();

            if (!string.IsNullOrWhiteSpace(requiredScope)
                && !scopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Bearer token missing required scope. RequiredScope={RequiredScope}", requiredScope);
                return null;
            }

            var issuer = principal.FindFirstValue("iss") ?? authority;
            var subject = principal.FindFirstValue("oid")
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? principal.FindFirstValue("sub");

            if (string.IsNullOrWhiteSpace(subject))
            {
                _logger.LogWarning("Bearer token did not contain a stable subject claim.");
                return null;
            }

            var displayName = principal.FindFirstValue("name")
                ?? principal.FindFirstValue("preferred_username")
                ?? principal.FindFirstValue("email")
                ?? "Clinician";

            var email = principal.FindFirstValue("email")
                ?? principal.FindFirstValue("preferred_username")
                ?? principal.FindFirstValue("upn");

            return new AuthenticatedUser(
                IdentityProviders.EntraExternalId,
                issuer,
                subject,
                displayName,
                email,
                scopes,
                // #517: the tenant, read from this token and nowhere else.
                TenantId: principal.FindFirstValue("tid"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bearer token validation failed.");
            return null;
        }
    }

    private string GetRequiredConfiguration(string key)
    {
        return string.IsNullOrWhiteSpace(_configuration[key])
            ? throw new InvalidOperationException($"{key} is not configured.")
            : _configuration[key]!;
    }

    internal static string ValidateIssuer(string authority, string metadataIssuer, string issuer)
    {
        var validIssuers = GetValidIssuers(authority, metadataIssuer).ToArray();

        if (validIssuers.Contains(issuer, StringComparer.OrdinalIgnoreCase)
            || MatchesTenantIssuerTemplate(metadataIssuer, issuer)
            || MatchesAuthorityTenantIssuer(authority, issuer))
        {
            return issuer;
        }

        throw new SecurityTokenInvalidIssuerException($"Issuer '{issuer}' is not valid for authority '{authority}'.");
    }

    private static IEnumerable<string> GetValidIssuers(string authority, string metadataIssuer)
    {
        yield return authority.TrimEnd('/');
        yield return metadataIssuer.TrimEnd('/');

        var normalizedAuthority = authority.TrimEnd('/');

        if (normalizedAuthority.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase))
        {
            yield return normalizedAuthority[..^"/v2.0".Length];
        }
        else
        {
            yield return $"{normalizedAuthority}/v2.0";
        }
    }

    internal static bool MatchesTenantIssuerTemplate(string metadataIssuer, string issuer)
    {
        const string tenantPlaceholder = "{tenantid}";

        if (!metadataIssuer.Contains(tenantPlaceholder, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var escapedTemplate = Uri.EscapeDataString(tenantPlaceholder);
        var template = metadataIssuer.Replace(escapedTemplate, tenantPlaceholder, StringComparison.OrdinalIgnoreCase);
        var parts = template.Split(tenantPlaceholder, StringSplitOptions.None);

        return parts.Length == 2
            && issuer.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase)
            && issuer.EndsWith(parts[1], StringComparison.OrdinalIgnoreCase);
    }

    internal static bool MatchesAuthorityTenantIssuer(string authority, string issuer)
    {
        var normalizedAuthority = authority.TrimEnd('/');

        foreach (var segment in new[] { "/organizations", "/common" })
        {
            var markerIndex = normalizedAuthority.IndexOf(segment, StringComparison.OrdinalIgnoreCase);

            if (markerIndex < 0)
            {
                continue;
            }

            var prefix = normalizedAuthority[..markerIndex];
            var suffix = normalizedAuthority.EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase)
                ? "/v2.0"
                : string.Empty;

            if (issuer.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase)
                && issuer.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> GetValidAudiences(string audience)
    {
        yield return audience;

        const string apiPrefix = "api://";

        if (audience.StartsWith(apiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            yield return audience[apiPrefix.Length..];
        }
    }

    private static IEnumerable<string> GetScopes(ClaimsPrincipal principal)
    {
        foreach (var claimType in new[] { "scp", "scope", "http://schemas.microsoft.com/identity/claims/scope" })
        {
            var claim = principal.FindFirstValue(claimType);

            if (string.IsNullOrWhiteSpace(claim))
            {
                continue;
            }

            foreach (var scope in claim.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return scope;
            }
        }
    }
}
