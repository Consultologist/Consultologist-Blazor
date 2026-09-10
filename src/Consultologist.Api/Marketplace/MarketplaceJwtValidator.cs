using System.IdentityModel.Tokens.Jwt;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Consultologist.Api.Marketplace;

public interface IMarketplaceJwtValidator
{
    /// <summary>True iff the Authorization header carries a valid Entra bearer for
    /// the marketplace webhook (issuer/audience/lifetime/signature).</summary>
    Task<bool> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken);
}

/// <summary>
/// #669: validates the Entra JWT Microsoft sends on the SaaS webhook (§5 of the
/// spike). The technique is <see cref="Consultologist.Api.Auth.BearerTokenValidator"/>'s
/// (an OIDC <see cref="ConfigurationManager{T}"/> feeding <see cref="TokenValidationParameters"/>),
/// but this is a <b>separate</b> validator with two deliberate differences: it is
/// bound to the marketplace app's own authority/audience (<c>Marketplace:Authority</c>
/// / <c>Marketplace:WebhookAudience</c>), and it <b>accepts an application token</b> —
/// the webhook JWT is app-only (no delegated <c>scp</c>), exactly the shape
/// <c>BearerTokenValidator</c> refuses. The webhook additionally confirms the payload
/// out-of-band via Get-Operation before acting — this only proves the caller is
/// Microsoft.
/// </summary>
public sealed class MarketplaceJwtValidator : IMarketplaceJwtValidator
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MarketplaceJwtValidator> _logger;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly object _lock = new();
    private ConfigurationManager<OpenIdConnectConfiguration>? _configManager;
    private string? _configuredAuthority;

    public MarketplaceJwtValidator(IConfiguration configuration, ILogger<MarketplaceJwtValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        var authority = _configuration["Marketplace:Authority"]
            ?? throw new InvalidOperationException("Marketplace:Authority is not configured.");
        var audience = _configuration["Marketplace:WebhookAudience"]
            ?? throw new InvalidOperationException("Marketplace:WebhookAudience is not configured.");

        try
        {
            var configuration = await GetConfigManager(authority).GetConfigurationAsync(cancellationToken);

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = configuration.Issuer,
                ValidateAudience = true,
                ValidAudiences = ValidAudiences(audience),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = configuration.SigningKeys,
                // Deliberately no scope/idtyp check: the webhook token is app-only.
            };

            _handler.ValidateToken(token, parameters, out _);
            return true;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            // The reason type only — never the token or claims.
            _logger.LogWarning("Marketplace webhook JWT rejected. Reason={Reason}", ex.GetType().Name);
            return false;
        }
    }

    private static string[] ValidAudiences(string audience) =>
        audience.StartsWith("api://", StringComparison.OrdinalIgnoreCase)
            ? new[] { audience, audience["api://".Length..] }
            : new[] { audience, $"api://{audience}" };

    private ConfigurationManager<OpenIdConnectConfiguration> GetConfigManager(string authority)
    {
        lock (_lock)
        {
            if (_configManager == null || !string.Equals(_configuredAuthority, authority, StringComparison.Ordinal))
            {
                var metadata = $"{authority.TrimEnd('/')}/.well-known/openid-configuration";
                _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    metadata, new OpenIdConnectConfigurationRetriever());
                _configuredAuthority = authority;
            }

            return _configManager;
        }
    }
}
