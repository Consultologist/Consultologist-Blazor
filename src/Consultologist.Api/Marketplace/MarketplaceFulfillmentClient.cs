using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Marketplace;

/// <summary>The resolved subscription a marketplace token names.</summary>
public sealed record ResolvedSubscription(
    string SubscriptionId, string? OfferId, string? PlanId, int? Quantity, string? SubscriptionName);

/// <summary>A subscription's current state.</summary>
public sealed record MarketplaceSubscription(string Id, string? Status, string? PlanId, string? OfferId);

/// <summary>One asynchronous operation on a subscription (webhook confirmation).</summary>
public sealed record MarketplaceOperation(string Id, string? Status, string? Action, string? SubscriptionId);

public interface IMarketplaceFulfillmentClient
{
    Task<ResolvedSubscription?> ResolveAsync(string marketplaceToken, CancellationToken cancellationToken);

    Task ActivateAsync(string subscriptionId, string? planId, int? quantity, CancellationToken cancellationToken);

    Task<MarketplaceSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken);

    Task<MarketplaceOperation?> GetOperationAsync(string subscriptionId, string operationId, CancellationToken cancellationToken);

    Task AckOperationAsync(string subscriptionId, string operationId, bool success, CancellationToken cancellationToken);
}

/// <summary>
/// #669: the SaaS Fulfillment API v2 client (§4 of the spike). Raw REST in the
/// <see cref="Consultologist.Api.Email.GraphMailClient"/> idiom — per-call bearer
/// (from <see cref="IMarketplaceTokenClient"/>, not a plain MI token), an
/// <c>IHttpClientFactory</c> client, status-only logging (a marketplace body can
/// echo purchaser detail). Backend-only; never called from the browser.
/// </summary>
public sealed class MarketplaceFulfillmentClient : IMarketplaceFulfillmentClient
{
    internal const string BaseUrl = "https://marketplaceapi.microsoft.com/api/saas";
    internal const string ApiVersion = "2018-08-31";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMarketplaceTokenClient _tokenClient;
    private readonly ILogger<MarketplaceFulfillmentClient> _logger;

    public MarketplaceFulfillmentClient(
        IHttpClientFactory httpClientFactory,
        IMarketplaceTokenClient tokenClient,
        ILogger<MarketplaceFulfillmentClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenClient = tokenClient;
        _logger = logger;
    }

    public async Task<ResolvedSubscription?> ResolveAsync(string marketplaceToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/subscriptions/resolve?api-version={ApiVersion}");
        request.Headers.TryAddWithoutValidation("x-ms-marketplace-token", marketplaceToken);
        using var document = await SendAsync(request, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var root = document.RootElement;
        var subscription = root.TryGetProperty("subscription", out var sub) ? sub : default;

        var subscriptionId = FirstString(root, "id", "subscriptionId")
            ?? (subscription.ValueKind == JsonValueKind.Object ? FirstString(subscription, "id", "subscriptionId") : null);
        if (string.IsNullOrEmpty(subscriptionId))
        {
            return null;
        }

        return new ResolvedSubscription(
            subscriptionId,
            FirstString(root, "offerId") ?? FirstString(subscription, "offerId"),
            FirstString(root, "planId") ?? FirstString(subscription, "planId"),
            FirstInt(root, "quantity") ?? FirstInt(subscription, "quantity"),
            FirstString(root, "subscriptionName") ?? FirstString(subscription, "name"));
    }

    public async Task ActivateAsync(string subscriptionId, string? planId, int? quantity, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?> { ["planId"] = planId };
        if (quantity is { } q)
        {
            payload["quantity"] = q;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{BaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/activate?api-version={ApiVersion}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        using var _ = await SendAsync(request, cancellationToken);
    }

    public async Task<MarketplaceSubscription?> GetSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"{BaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}?api-version={ApiVersion}");
        using var document = await SendAsync(request, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var root = document.RootElement;
        var id = FirstString(root, "id", "subscriptionId");
        return id == null ? null : new MarketplaceSubscription(
            id, FirstString(root, "saasSubscriptionStatus", "status"), FirstString(root, "planId"), FirstString(root, "offerId"));
    }

    public async Task<MarketplaceOperation?> GetOperationAsync(string subscriptionId, string operationId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{BaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/operations/{Uri.EscapeDataString(operationId)}?api-version={ApiVersion}");
        using var document = await SendAsync(request, cancellationToken);
        if (document == null)
        {
            return null;
        }

        var root = document.RootElement;
        var id = FirstString(root, "id");
        return id == null ? null : new MarketplaceOperation(
            id, FirstString(root, "status"), FirstString(root, "action"), FirstString(root, "subscriptionId"));
    }

    public async Task AckOperationAsync(string subscriptionId, string operationId, bool success, CancellationToken cancellationToken)
    {
        var payload = new { status = success ? "Success" : "Failure" };
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{BaseUrl}/subscriptions/{Uri.EscapeDataString(subscriptionId)}/operations/{Uri.EscapeDataString(operationId)}?api-version={ApiVersion}")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        using var _ = await SendAsync(request, cancellationToken);
    }

    private async Task<JsonDocument?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokenClient.GetTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("Could not acquire a marketplace Fulfillment API token.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("x-ms-requestid", Guid.NewGuid().ToString());

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Marketplace Fulfillment call failed. Method={Method}, Status={Status}",
                request.Method.Method,
                (int)response.StatusCode);
            throw new InvalidOperationException($"Marketplace Fulfillment returned status {(int)response.StatusCode}.");
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

    private static int? FirstInt(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n))
                {
                    return n;
                }

                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
                {
                    return parsed;
                }
            }
        }

        return null;
    }
}
