using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Auth;

/// <summary>
/// The outcome of one exchange attempt. Exactly one of the two shapes:
/// <see cref="AccessToken"/> set (Exchanged), or <see cref="Refusal"/> set —
/// a kebab-case word the door returns on the flat error wire. The token is
/// per-request, in memory only, never persisted and never logged.
/// </summary>
public sealed record OnBehalfOfOutcome(string? AccessToken, string? Refusal)
{
    public static OnBehalfOfOutcome Exchanged(string accessToken) => new(accessToken, null);
    public static OnBehalfOfOutcome Refused(string refusal) => new(null, refusal);
}

public static class OnBehalfOfRefusals
{
    /// <summary>OBO cannot act for a consumer Microsoft account — refused before the wire.</summary>
    public const string PersonalAccount = "personal-account";

    /// <summary>The tenant has not consented the requested downstream scope.</summary>
    public const string ConsentRequired = "obo-consent-required";

    /// <summary>The exchange itself failed — its own word, never a generic 500.</summary>
    public const string ExchangeFailed = "obo-exchange-failed";
}

public interface IOnBehalfOfTokenClient
{
    /// <summary>
    /// Exchange the caller's own delegated bearer for a downstream token,
    /// acting as the signed-in clinician (#615). <paramref name="assertion"/>
    /// is the raw incoming access token (the endpoint re-reads its own
    /// Authorization header — the authorizer's carrier holds claims only).
    /// </summary>
    Task<OnBehalfOfOutcome> ExchangeAsync(
        AuthenticatedUser user,
        string assertion,
        string scope,
        CancellationToken cancellationToken);
}

/// <summary>
/// The On-Behalf-Of foundation (#615): the engine reaches outward as the
/// user. Raw token-endpoint REST (repo idiom — no Graph SDK, no server
/// MSAL); the client credential is a federated identity credential on the
/// API registration trusting the user-assigned managed identity, so nothing
/// exchangeable is stored anywhere — the assertion is minted per request
/// from the same TokenCredential every storage client uses. No token cache:
/// a document fetch is one-shot, and no-cache is strictly safer than any.
/// Logs carry ids and scopes, never tokens (#245's discipline).
/// </summary>
public sealed class OnBehalfOfTokenClient : IOnBehalfOfTokenClient
{
    /// <summary>The audience workload identity federation exchanges against.</summary>
    internal const string FederationScope = "api://AzureADTokenExchange/.default";
    private const string ClientAssertionType = "urn:ietf:params:oauth:grant-type:jwt-bearer";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OnBehalfOfTokenClient> _logger;
    private readonly string? _tokenEndpointOverride;

    public OnBehalfOfTokenClient(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IConfiguration configuration,
        ILogger<OnBehalfOfTokenClient> logger)
        : this(httpClientFactory, credential, configuration, logger, tokenEndpointOverride: null)
    {
    }

    /// <summary>Test seam: a fixed token endpoint instead of the caller's tenant.</summary>
    internal OnBehalfOfTokenClient(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        IConfiguration configuration,
        ILogger<OnBehalfOfTokenClient> logger,
        string? tokenEndpointOverride)
    {
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _configuration = configuration;
        _logger = logger;
        _tokenEndpointOverride = tokenEndpointOverride;
    }

    public async Task<OnBehalfOfOutcome> ExchangeAsync(
        AuthenticatedUser user,
        string assertion,
        string scope,
        CancellationToken cancellationToken)
    {
        // Refused before the wire: OBO does not exist for consumer accounts,
        // and the named refusal beats a confusing AADSTS sentence.
        if (!DeliveryAddress.IsOrganisation(user))
        {
            return OnBehalfOfOutcome.Refused(OnBehalfOfRefusals.PersonalAccount);
        }

        var clientId = _configuration["Auth:Audience"]
            ?? throw new InvalidOperationException("Auth:Audience is not configured.");

        var clientAssertion = await _credential.GetTokenAsync(
            new TokenRequestContext(new[] { FederationScope }), cancellationToken);

        // OBO is tenanted: the exchange happens in the caller's own tenant,
        // never /common.
        var endpoint = _tokenEndpointOverride
            ?? $"https://login.microsoftonline.com/{user.TenantId!.Trim()}/oauth2/v2.0/token";

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var response = await client.PostAsync(endpoint, new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = ClientAssertionType,
                ["client_id"] = clientId,
                ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                ["client_assertion"] = clientAssertion.Token,
                ["assertion"] = assertion,
                ["requested_token_use"] = "on_behalf_of",
                ["scope"] = scope
            }), cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var outcome = ReadExchangeResponse((int)response.StatusCode, body);

        if (outcome.Refusal != null)
        {
            // The word, the caller, the scope — never the body, which quotes
            // tokens on success and can quote the assertion on failure.
            _logger.LogWarning(
                "On-behalf-of exchange refused. Refusal={Refusal}, Scope={Scope}, Status={Status}",
                outcome.Refusal, scope, (int)response.StatusCode);
        }

        return outcome;
    }

    /// <summary>
    /// #615: the token response, judged. Consent problems are the caller's
    /// tenant saying no — their own word, distinct from an exchange fault —
    /// detected from invalid_grant/interaction_required and the
    /// consent_required suberror. Extracted so it can be asserted directly.
    /// </summary>
    internal static OnBehalfOfOutcome ReadExchangeResponse(int statusCode, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (statusCode == 200 && root.TryGetProperty("access_token", out var token))
            {
                var value = token.GetString();
                return string.IsNullOrWhiteSpace(value)
                    ? OnBehalfOfOutcome.Refused(OnBehalfOfRefusals.ExchangeFailed)
                    : OnBehalfOfOutcome.Exchanged(value);
            }

            var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
            var suberror = root.TryGetProperty("suberror", out var s) ? s.GetString() : null;

            if (string.Equals(error, "interaction_required", StringComparison.Ordinal)
                || string.Equals(suberror, "consent_required", StringComparison.Ordinal)
                || (string.Equals(error, "invalid_grant", StringComparison.Ordinal)
                    && root.TryGetProperty("error_description", out var d)
                    && d.GetString()?.Contains("AADSTS65001", StringComparison.Ordinal) == true))
            {
                return OnBehalfOfOutcome.Refused(OnBehalfOfRefusals.ConsentRequired);
            }

            return OnBehalfOfOutcome.Refused(OnBehalfOfRefusals.ExchangeFailed);
        }
        catch (JsonException)
        {
            return OnBehalfOfOutcome.Refused(OnBehalfOfRefusals.ExchangeFailed);
        }
    }
}
