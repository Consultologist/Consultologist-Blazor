using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Stripe;

public interface IStripeSignatureValidator
{
    /// <summary>True iff the Stripe-Signature header is a valid HMAC over the raw body
    /// under the configured signing secret, within the timestamp tolerance.</summary>
    bool Validate(string rawBody, string? stripeSignatureHeader);
}

/// <summary>
/// #725: validates the <c>Stripe-Signature</c> header Stripe sends on every webhook —
/// the analog of <see cref="Consultologist.Api.Marketplace.MarketplaceJwtValidator"/>,
/// but Stripe signs the <b>raw body</b> (HMAC-SHA256 over <c>"{t}.{payload}"</c>) rather
/// than issuing a JWT, so the caller must hand this the exact bytes received before any
/// deserialize. Fails closed when unconfigured. The signing secret is a genuine secret
/// (<c>Stripe__WebhookSigningSecret</c>), the <c>LinkedIn__ClientSecret</c> precedent.
/// A valid signature only proves the caller is Stripe; the webhook still refetches the
/// subscription as the source of truth before acting.
/// </summary>
public sealed class StripeSignatureValidator : IStripeSignatureValidator
{
    // Stripe's default tolerance for replay protection.
    internal static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeSignatureValidator> _logger;

    public StripeSignatureValidator(IConfiguration configuration, ILogger<StripeSignatureValidator> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool Validate(string rawBody, string? stripeSignatureHeader)
    {
        var secret = _configuration["Stripe:WebhookSigningSecret"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Inert until provisioned: no secret means nothing is trusted (#725, Phase A).
            _logger.LogWarning("Stripe webhook rejected: no signing secret configured.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(stripeSignatureHeader))
        {
            return false;
        }

        // Header shape: "t=<unix>,v1=<hex>[,v1=<hex>…]". Parse t and the v1 signatures.
        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in stripeSignatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = part[..eq];
            var value = part[(eq + 1)..];
            if (key == "t")
            {
                timestamp = value;
            }
            else if (key == "v1")
            {
                signatures.Add(value);
            }
        }

        if (timestamp == null || signatures.Count == 0
            || !long.TryParse(timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
        {
            return false;
        }

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(t);
        if (age > Tolerance || age < -Tolerance)
        {
            _logger.LogWarning("Stripe webhook rejected: signature timestamp outside tolerance.");
            return false;
        }

        var expected = ComputeSignature(secret, timestamp, rawBody);
        foreach (var candidate in signatures)
        {
            if (FixedTimeEquals(candidate, expected))
            {
                return true;
            }
        }

        _logger.LogWarning("Stripe webhook rejected: no signature matched.");
        return false;
    }

    private static string ComputeSignature(string secret, string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexStringLower(hash);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
