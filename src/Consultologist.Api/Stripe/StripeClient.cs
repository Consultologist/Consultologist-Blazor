using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Stripe;

/// <summary>A Stripe Checkout Session — its id and the hosted URL the SPA redirects to.</summary>
public sealed record StripeCheckoutSession(string Id, string? Url);

/// <summary>A subscription's current state, refetched as the source of truth on a webhook.</summary>
public sealed record StripeSubscription(string Id, string? Status);

public interface IStripeClient
{
    /// <summary>True iff the secret key and price are configured; the door is inert otherwise (#725, Phase A).</summary>
    bool IsConfigured { get; }

    /// <summary>Create a subscription Checkout Session carrying appUserId as client_reference_id.</summary>
    Task<StripeCheckoutSession?> CreateCheckoutSessionAsync(
        string appUserId, string successUrl, string cancelUrl, CancellationToken cancellationToken);

    /// <summary>Retrieve a subscription — the out-of-band source of truth for a webhook event.</summary>
    Task<StripeSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken);
}

/// <summary>
/// #725: the Stripe REST client for the paid personal-account door. Raw REST in the
/// <see cref="Consultologist.Api.Marketplace.MarketplaceFulfillmentClient"/> idiom —
/// an <c>IHttpClientFactory</c> client, status-only logging (a Stripe body can echo
/// customer detail), throw on non-2xx. Unlike the marketplace client there is no
/// server-to-server token exchange: Stripe authenticates with a static secret API key
/// (a genuine secret, the <c>LinkedIn__ClientSecret</c> precedent). Managed Payments
/// (Stripe as merchant of record) is enabled per session. Backend-only.
/// </summary>
public sealed class StripeClient : IStripeClient
{
    internal const string BaseUrl = "https://api.stripe.com/v1";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeClient> _logger;

    public StripeClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<StripeClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private string? SecretApiKey => Trimmed(_configuration["Stripe:SecretApiKey"]);
    private string? PriceId => Trimmed(_configuration["Stripe:PriceId"]);

    public bool IsConfigured => SecretApiKey != null && PriceId != null;

    public async Task<StripeCheckoutSession?> CreateCheckoutSessionAsync(
        string appUserId, string successUrl, string cancelUrl, CancellationToken cancellationToken)
    {
        var priceId = PriceId ?? throw new InvalidOperationException("Stripe:PriceId is not configured.");

        // Form-urlencoded, as the Stripe API expects. client_reference_id carries our
        // appUserId so the checkout.session.completed webhook can tie the subscription
        // to the account. managed_payments[enabled] asks Stripe to be merchant of
        // record for this session (subject to Stripe's eligibility checks).
        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["line_items[0][price]"] = priceId,
            ["line_items[0][quantity]"] = "1",
            ["client_reference_id"] = appUserId,
            ["success_url"] = successUrl,
            ["cancel_url"] = cancelUrl,
            ["managed_payments[enabled]"] = "true",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/checkout/sessions")
        {
            Content = new FormUrlEncodedContent(form),
        };
        using var document = await SendAsync(request, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var root = document.RootElement;
        var id = FirstString(root, "id");
        return id == null ? null : new StripeCheckoutSession(id, FirstString(root, "url"));
    }

    public async Task<StripeSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{BaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}");
        using var document = await SendAsync(request, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var root = document.RootElement;
        var id = FirstString(root, "id");
        return id == null ? null : new StripeSubscription(id, FirstString(root, "status"));
    }

    private async Task<JsonDocument?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = SecretApiKey
            ?? throw new InvalidOperationException("Stripe:SecretApiKey is not configured.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Stripe API call failed. Method={Method}, Status={Status}",
                request.Method.Method,
                (int)response.StatusCode);
            throw new InvalidOperationException($"Stripe API returned status {(int)response.StatusCode}.");
        }

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body);
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString();
                if (!string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }
        }

        return null;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
