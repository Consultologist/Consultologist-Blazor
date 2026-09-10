using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Marketplace;

public interface IMarketplaceTokenClient
{
    /// <summary>A client-credentials token for the SaaS Fulfillment API, or null
    /// when the exchange failed (a named log line, never the body).</summary>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// #669: the server-to-server token for the Microsoft commercial-marketplace
/// SaaS Fulfillment API (§7 of docs/MARKETPLACE_ACTIVATION_SPIKE.md). A standard
/// Entra <b>client-credentials</b> token to <c>20e940b3-…/.default</c> — but the
/// Fulfillment API requires the token's <c>appid</c> to be the registered offer
/// app, so a plain managed-identity token (appid = the MI) will not do. The
/// identity-only posture is preserved by <b>workload identity federation</b>: the
/// offer app registration trusts the user-assigned managed identity, and the MI
/// mints a client assertion the token endpoint accepts — the same mechanics as
/// <see cref="Consultologist.Api.Auth.OnBehalfOfTokenClient"/>, minus the OBO
/// parameters. If WIF is rejected by the Fulfillment API (to be verified in Phase
/// B), setting <c>Marketplace:ClientSecret</c> switches to the documented
/// client-secret grant (the LinkedIn:ClientSecret precedent). No token cache.
/// </summary>
public sealed class MarketplaceTokenClient : IMarketplaceTokenClient
{
    /// <summary>The SaaS Fulfillment API's resource — the fixed first-party app id.</summary>
    internal const string FulfillmentScope = "20e940b3-4c77-4b0b-9a53-9e16a1b010a7/.default";

    /// <summary>The audience workload identity federation exchanges against.</summary>
    internal const string FederationScope = "api://AzureADTokenExchange/.default";

    private const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MarketplaceTokenClient> _logger;
    private readonly string? _tokenEndpointOverride;

    public MarketplaceTokenClient(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IConfiguration configuration,
        ILogger<MarketplaceTokenClient> logger)
        : this(httpClientFactory, credential, configuration, logger, tokenEndpointOverride: null)
    {
    }

    /// <summary>Test seam: a fixed token endpoint.</summary>
    internal MarketplaceTokenClient(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IConfiguration configuration,
        ILogger<MarketplaceTokenClient> logger,
        string? tokenEndpointOverride)
    {
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _configuration = configuration;
        _logger = logger;
        _tokenEndpointOverride = tokenEndpointOverride;
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var clientId = _configuration["Marketplace:ClientId"]
            ?? throw new InvalidOperationException("Marketplace:ClientId is not configured.");
        var tenantId = _configuration["Marketplace:TenantId"]
            ?? throw new InvalidOperationException("Marketplace:TenantId is not configured.");
        var clientSecret = _configuration["Marketplace:ClientSecret"];

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["scope"] = FulfillmentScope,
        };

        if (!string.IsNullOrWhiteSpace(clientSecret))
        {
            // The documented fallback (LinkedIn:ClientSecret precedent).
            form["client_secret"] = clientSecret;
        }
        else
        {
            // WIF: the MI mints the assertion the offer app is federated to trust.
            var assertion = await _credential.GetTokenAsync(
                new TokenRequestContext(new[] { FederationScope }), cancellationToken);
            form["client_assertion_type"] = ClientAssertionType;
            form["client_assertion"] = assertion.Token;
        }

        var endpoint = _tokenEndpointOverride
            ?? $"https://login.microsoftonline.com/{tenantId.Trim()}/oauth2/v2.0/token";

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(form), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = ReadTokenResponse((int)response.StatusCode, body);

        if (token == null)
        {
            // The status and whether a secret was used — never the body, which
            // quotes the token on success and can quote the assertion on failure.
            _logger.LogWarning(
                "Marketplace token exchange failed. Status={Status}, Mode={Mode}",
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(clientSecret) ? "wif" : "secret");
        }

        return token;
    }

    /// <summary>The token response, judged — extracted so it can be asserted directly.</summary>
    internal static string? ReadTokenResponse(int statusCode, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (statusCode == 200 && document.RootElement.TryGetProperty("access_token", out var token))
            {
                var value = token.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
