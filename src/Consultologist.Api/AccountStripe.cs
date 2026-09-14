using System.Net;
using System.Text.Json;

using Consultologist.Api.Auth;
using Consultologist.Api.Stripe;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api;

/// <summary>The SPA redirects the clinician to this hosted Checkout URL.</summary>
public sealed record StripeCheckoutResponse(string CheckoutUrl);

/// <summary>The subset of a Stripe webhook event the engine reads (tolerant — Stripe
/// adds fields, and a strict deserialize would drop a real event).</summary>
public sealed record StripeWebhookEvent(
    string? EventId, string? Type, string? ObjectId, string? ClientReferenceId, string? Subscription, string? Status);

/// <summary>
/// #725 (docs/PERSONAL_ACTIVATION_SPIKE.md): the paid personal-account activation door,
/// via Stripe (Managed Payments — Stripe as merchant of record). <b>Checkout</b> returns
/// a hosted Checkout Session URL under the caller's Entra bearer, carrying appUserId as
/// <c>client_reference_id</c>. <b>Webhook</b> is anonymous; every call is authenticated by
/// the <c>Stripe-Signature</c> HMAC over the raw body, and — Stripe being the source of
/// truth — a subscription lifecycle event refetches the subscription before acting. The
/// tie is created webhook-side on <c>checkout.session.completed</c> (the browser return can
/// be lost). Stripe is a billing signal, never a bearer credential (#610). The door is
/// inert until the Stripe secret + price + signing secret are provisioned.
/// </summary>
public sealed class AccountStripe
{
    /// <summary>A synthetic issuer namespacing the Stripe link's subject (the subscription
    /// id) in the identity tables — Stripe has no OIDC issuer.</summary>
    internal const string StripeIssuer = "https://stripe.com";

    internal const string NotConfigured = "stripe-not-configured";
    internal const string CheckoutFailed = "stripe-checkout-failed";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private readonly IAccountAuthorizer _authorizer;
    private readonly IAccountStore _accountStore;
    private readonly IStripeClient _stripe;
    private readonly IStripeSignatureValidator _signatures;
    private readonly ILogger<AccountStripe> _logger;

    public AccountStripe(
        IAccountAuthorizer authorizer,
        IAccountStore accountStore,
        IStripeClient stripe,
        IStripeSignatureValidator signatures,
        ILogger<AccountStripe> logger)
    {
        _authorizer = authorizer;
        _accountStore = accountStore;
        _stripe = stripe;
        _signatures = signatures;
        _logger = logger;
    }

    [Function("AccountStripeCheckout")]
    public async Task<HttpResponseData> CheckoutAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "Account/Stripe/Checkout")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);
        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        // No IsActive gate: subscribing is an input to activation, so a Pending
        // account must be able to start checkout.

        // The return target comes from the browser-set Origin checked against the
        // allow-list — never a client-supplied value.
        var origin = req.Headers.TryGetValues("Origin", out var originValues) ? originValues.FirstOrDefault() : null;
        if (!FunctionCors.IsAllowedOrigin(origin))
        {
            var badRequest = req.CreateResponse(HttpStatusCode.BadRequest);
            FunctionCors.Apply(req, badRequest);
            await badRequest.WriteStringAsync("Origin not allowed.", cancellationToken);
            return badRequest;
        }

        // Inert until provisioned: no key/price → the door reports not-configured.
        if (!_stripe.IsConfigured)
        {
            return await Error(req, HttpStatusCode.ServiceUnavailable, NotConfigured, cancellationToken);
        }

        var session = await _stripe.CreateCheckoutSessionAsync(
            account.AppUserId,
            successUrl: $"{origin}/subscription?stripe=success",
            cancelUrl: $"{origin}/subscription?stripe=cancel",
            cancellationToken);

        if (session?.Url is not { Length: > 0 } url)
        {
            return await Error(req, HttpStatusCode.BadGateway, CheckoutFailed, cancellationToken);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(new StripeCheckoutResponse(url), cancellationToken);
        return response;
    }

    [Function("AccountStripeWebhook")]
    public async Task<HttpResponseData> WebhookAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "Account/Stripe/Webhook")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        // Read the RAW body first — the signature is an HMAC over these exact bytes,
        // so it must be hashed before any deserialize.
        var rawBody = await new StreamReader(req.Body).ReadToEndAsync(cancellationToken);

        var signature = req.Headers.TryGetValues("Stripe-Signature", out var sigValues) ? sigValues.FirstOrDefault() : null;
        if (!_signatures.Validate(rawBody, signature))
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var evt = ParseEvent(rawBody);

        // Re-delivery is safe: link / status / unlink are idempotent, so a retried
        // event (Stripe retries on non-2xx) converges to the same state. Every
        // non-auth path returns 200 to stop the retries.
        var effect = EffectFor(evt?.Type, evt?.Status);

        if (effect == WebhookEffect.Link)
        {
            // checkout.session.completed: the session carries our appUserId
            // (client_reference_id) and the new subscription id — make the tie,
            // which activates the account (Stripe is an activating provider).
            if (evt is { ClientReferenceId: { Length: > 0 } appUserId, Subscription: { Length: > 0 } subscriptionId })
            {
                var outcome = await _accountStore.LinkIdentityAsync(
                    appUserId, IdentityProviders.Stripe, StripeIssuer, subscriptionId,
                    displayName: null, email: null, pictureUrl: null, verifiedCategories: null, cancellationToken);
                if (outcome == IdentityLinkOutcome.ConflictOtherUser)
                {
                    _logger.LogWarning("Stripe checkout tied a subscription already linked to another account; ignoring.");
                }
            }

            return req.CreateResponse(HttpStatusCode.OK);
        }

        if (effect == WebhookEffect.Ignore || evt?.ObjectId is not { Length: > 0 } subId)
        {
            return req.CreateResponse(HttpStatusCode.OK);
        }

        var tiedUser = await _accountStore.FindAppUserByLinkAsync(
            IdentityProviders.Stripe, StripeIssuer, subId, cancellationToken);

        // An event for a subscription we never tied is a no-op.
        if (tiedUser != null)
        {
            // Refetch the subscription as the source of truth before acting — never
            // trust the event body (events can arrive out of order).
            var subscription = await _stripe.GetSubscriptionAsync(subId, cancellationToken);
            var authoritative = subscription == null ? effect : EffectFor(evt!.Type, subscription.Status);

            switch (authoritative)
            {
                case WebhookEffect.Activate:
                    await _accountStore.ApplyActivationStatusAsync(tiedUser, IdentityProviders.Stripe, ActivationStatusEffect.Activate, cancellationToken);
                    break;
                case WebhookEffect.Suspend:
                    await _accountStore.ApplyActivationStatusAsync(tiedUser, IdentityProviders.Stripe, ActivationStatusEffect.Suspend, cancellationToken);
                    break;
                case WebhookEffect.Unsubscribe:
                    await _accountStore.UnlinkIdentityAsync(tiedUser, IdentityProviders.Stripe, cancellationToken);
                    break;
            }
        }

        return req.CreateResponse(HttpStatusCode.OK);
    }

    internal enum WebhookEffect
    {
        Link,
        Activate,
        Suspend,
        Unsubscribe,
        Ignore,
    }

    /// <summary>
    /// The Stripe event (and, for a subscription event, its status) → the effect on the
    /// engine account. Extracted so the mapping can be asserted directly. A subscription
    /// event's effect is re-derived from the refetched status; unknown types/statuses are
    /// Ignore (notify-only).
    /// </summary>
    internal static WebhookEffect EffectFor(string? eventType, string? subscriptionStatus) => (eventType ?? string.Empty).ToLowerInvariant() switch
    {
        "checkout.session.completed" => WebhookEffect.Link,
        "customer.subscription.deleted" => WebhookEffect.Unsubscribe,
        "customer.subscription.created" or "customer.subscription.updated" => StatusEffect(subscriptionStatus),
        _ => WebhookEffect.Ignore,
    };

    private static WebhookEffect StatusEffect(string? status) => (status ?? string.Empty).ToLowerInvariant() switch
    {
        "active" or "trialing" => WebhookEffect.Activate,
        "past_due" or "unpaid" => WebhookEffect.Suspend,
        "canceled" or "incomplete_expired" => WebhookEffect.Unsubscribe,
        _ => WebhookEffect.Ignore,
    };

    internal static StripeWebhookEvent? ParseEvent(string rawBody)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var root = document.RootElement;
            var obj = root.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var o) ? o : default;
            return new StripeWebhookEvent(
                Str(root, "id"),
                Str(root, "type"),
                Str(obj, "id"),
                Str(obj, "client_reference_id"),
                Str(obj, "subscription"),
                Str(obj, "status"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static async Task<HttpResponseData> Error(
        HttpRequestData req, HttpStatusCode status, string word, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(status);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(new { error = word }, cancellationToken);
        return response;
    }
}
