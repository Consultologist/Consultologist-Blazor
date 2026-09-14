using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

using Consultologist.Api.Auth;
using Consultologist.Api.Marketplace;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api;

/// <summary>The marketplace landing page (SPA) posts the resolve token here.</summary>
public sealed record MarketplaceResolveRequest(string? Token);

public sealed record MarketplaceResolveResponse(string SubscriptionId, string? PlanId, string? OfferId);

/// <summary>The subset of the SaaS webhook payload the engine reads. Tolerant —
/// Microsoft may add fields, and a strict deserialize would drop a real event.</summary>
public sealed record MarketplaceWebhookPayload(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("subscriptionId")] string? SubscriptionId,
    [property: JsonPropertyName("action")] string? Action,
    [property: JsonPropertyName("status")] string? Status);

/// <summary>
/// #669 (docs/MARKETPLACE_ACTIVATION_SPIKE.md): the marketplace activation door.
/// <b>Resolve</b> is the landing endpoint — the SPA (the offer's landing URL) signs
/// the clinician in with Entra and POSTs the marketplace token under their bearer;
/// this resolves it, activates the subscription, and ties it to the account (which
/// activates it, since MicrosoftMarketplace is an activating provider). <b>Webhook</b>
/// is the lifecycle endpoint — anonymous, but every call is authenticated by the
/// marketplace Entra JWT AND confirmed out-of-band via Get-Operation before acting.
/// The engine only ever sees the clinician's delegated bearer on Resolve; the
/// Fulfillment API is called server-side (never from the browser).
/// </summary>
public sealed class AccountMarketplace
{
    /// <summary>A synthetic issuer namespacing the marketplace link's subject
    /// (the subscriptionId) in the identity tables — marketplace has no OIDC issuer.</summary>
    internal const string MarketplaceIssuer = "https://marketplace.microsoft.com";

    // Refusal words on the flat error wire.
    internal const string TokenInvalid = "marketplace-token-invalid";
    internal const string ResolveFailed = "marketplace-resolve-failed";
    internal const string LinkedElsewhere = "marketplace-subscription-linked-elsewhere";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly IAccountAuthorizer _authorizer;
    private readonly IAccountStore _accountStore;
    private readonly IMarketplaceFulfillmentClient _fulfillment;
    private readonly IMarketplaceJwtValidator _jwt;
    private readonly ILogger<AccountMarketplace> _logger;

    public AccountMarketplace(
        IAccountAuthorizer authorizer,
        IAccountStore accountStore,
        IMarketplaceFulfillmentClient fulfillment,
        IMarketplaceJwtValidator jwt,
        ILogger<AccountMarketplace> logger)
    {
        _authorizer = authorizer;
        _accountStore = accountStore;
        _fulfillment = fulfillment;
        _jwt = jwt;
        _logger = logger;
    }

    [Function("AccountMarketplaceResolve")]
    public async Task<HttpResponseData> ResolveAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "Account/Marketplace/Resolve")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var authorized = await _authorizer.AuthorizeWithUserAsync(req, cancellationToken);
        if (authorized == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        // No CanUseApp gate: resolving a purchase is how a Pending account
        // activates — the LinkedIn/Epic-link posture.
        MarketplaceResolveRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<MarketplaceResolveRequest>(cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            request = null;
        }

        if (string.IsNullOrWhiteSpace(request?.Token))
        {
            return await Error(req, HttpStatusCode.BadRequest, TokenInvalid, cancellationToken);
        }

        var resolved = await _fulfillment.ResolveAsync(request.Token, cancellationToken);
        if (resolved == null)
        {
            return await Error(req, HttpStatusCode.UnprocessableEntity, ResolveFailed, cancellationToken);
        }

        // Activate the subscription (billing starts here), then tie it to the
        // account — the link flips a Pending/Unverified account to Active.
        await _fulfillment.ActivateAsync(resolved.SubscriptionId, resolved.PlanId, resolved.Quantity, cancellationToken);

        var outcome = await _accountStore.LinkIdentityAsync(
            authorized.Account.AppUserId,
            IdentityProviders.MicrosoftMarketplace,
            MarketplaceIssuer,
            resolved.SubscriptionId,
            displayName: resolved.PlanId,
            email: null,
            pictureUrl: null,
            verifiedCategories: null,
            cancellationToken);

        if (outcome == IdentityLinkOutcome.ConflictOtherUser)
        {
            return await Error(req, HttpStatusCode.Conflict, LinkedElsewhere, cancellationToken);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(
            new MarketplaceResolveResponse(resolved.SubscriptionId, resolved.PlanId, resolved.OfferId), cancellationToken);
        return response;
    }

    [Function("AccountMarketplaceWebhook")]
    public async Task<HttpResponseData> WebhookAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Account/Marketplace/Webhook")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        var authorizationHeader = req.Headers.TryGetValues("Authorization", out var values)
            ? values.FirstOrDefault()
            : null;

        // Authenticate every call — the marketplace Entra JWT. (Get-Operation
        // below is the second, independent confirmation before we act.)
        if (!await _jwt.ValidateAsync(authorizationHeader, cancellationToken))
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        MarketplaceWebhookPayload? payload;
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync(cancellationToken);
            payload = JsonSerializer.Deserialize<MarketplaceWebhookPayload>(body, WebJson);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            payload = null;
        }

        // Nothing to act on is still a success — 200 stops Microsoft's retries.
        if (string.IsNullOrEmpty(payload?.SubscriptionId))
        {
            return req.CreateResponse(HttpStatusCode.OK);
        }

        var effect = EffectFor(payload.Action);
        if (effect == WebhookEffect.Ignore)
        {
            return req.CreateResponse(HttpStatusCode.OK);
        }

        // Confirm the operation out-of-band before acting — never trust the body.
        if (!string.IsNullOrEmpty(payload.Id))
        {
            var operation = await _fulfillment.GetOperationAsync(payload.SubscriptionId, payload.Id, cancellationToken);
            if (operation == null)
            {
                _logger.LogWarning("Marketplace webhook could not confirm an operation; ignoring. Action={Action}", payload.Action);
                return req.CreateResponse(HttpStatusCode.OK);
            }
        }

        if (effect == WebhookEffect.AckOnly)
        {
            // Plan/quantity change: PATCH-ACK the operation within 10s; no status change.
            if (!string.IsNullOrEmpty(payload.Id))
            {
                await _fulfillment.AckOperationAsync(payload.SubscriptionId, payload.Id, success: true, cancellationToken);
            }

            return req.CreateResponse(HttpStatusCode.OK);
        }

        var appUserId = await _accountStore.FindAppUserByLinkAsync(
            IdentityProviders.MicrosoftMarketplace, MarketplaceIssuer, payload.SubscriptionId, cancellationToken);

        // An event for a subscription we never tied (no landing/resolve yet) is a no-op.
        if (appUserId != null)
        {
            switch (effect)
            {
                case WebhookEffect.Activate:
                    await _accountStore.ApplyActivationStatusAsync(appUserId, IdentityProviders.MicrosoftMarketplace, ActivationStatusEffect.Activate, cancellationToken);
                    break;
                case WebhookEffect.Suspend:
                    await _accountStore.ApplyActivationStatusAsync(appUserId, IdentityProviders.MicrosoftMarketplace, ActivationStatusEffect.Suspend, cancellationToken);
                    break;
                case WebhookEffect.Unsubscribe:
                    await _accountStore.UnlinkIdentityAsync(appUserId, IdentityProviders.MicrosoftMarketplace, cancellationToken);
                    break;
            }
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }

    internal enum WebhookEffect
    {
        Activate,
        Suspend,
        Unsubscribe,
        AckOnly,
        Ignore,
    }

    /// <summary>
    /// The subscription-lifecycle action → its effect on the engine account
    /// (§5 of the spike). Extracted so the mapping can be asserted directly.
    /// Renew and any unrecognised action are Ignore (notify-only).
    /// </summary>
    internal static WebhookEffect EffectFor(string? action) => (action ?? string.Empty).ToLowerInvariant() switch
    {
        "subscribe" or "activate" or "reinstate" => WebhookEffect.Activate,
        "suspend" => WebhookEffect.Suspend,
        "unsubscribe" => WebhookEffect.Unsubscribe,
        "changeplan" or "changequantity" => WebhookEffect.AckOnly,
        _ => WebhookEffect.Ignore,
    };

    private static async Task<HttpResponseData> Error(
        HttpRequestData req, HttpStatusCode status, string word, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(status);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(new { error = word }, cancellationToken);
        return response;
    }
}
