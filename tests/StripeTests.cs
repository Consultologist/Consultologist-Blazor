using System.Net;
using System.Security.Cryptography;
using System.Text;

using Consultologist.Api.Stripe;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace Consultologist.Api.Tests;

public class StripeTests
{
    // ----- #725: the Stripe REST client (raw REST, against a stub handler) -----

    [Fact]
    public async Task CreateCheckoutSession_PostsTheForm_WithClientReferenceIdAndBearer()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"id\":\"cs_test_1\",\"url\":\"https://checkout.stripe.com/c/pay/cs_test_1\"}");
        var client = ClientFor(handler, secret: "sk_test_abc", priceId: "price_123");

        var session = await client.CreateCheckoutSessionAsync("user-1", "https://app/subscription?stripe=success", "https://app/subscription?stripe=cancel", CancellationToken.None);

        Assert.Equal("cs_test_1", session!.Id);
        Assert.Equal("https://checkout.stripe.com/c/pay/cs_test_1", session.Url);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/v1/checkout/sessions", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("Bearer sk_test_abc", handler.LastRequest.Headers.Authorization!.ToString());
        // Form-urlencoded: client_reference_id carries appUserId; the configured price rides.
        Assert.Contains("client_reference_id=user-1", handler.LastBody);
        Assert.Contains("mode=subscription", handler.LastBody);
        Assert.Contains("price_123", handler.LastBody);
        Assert.Contains("managed_payments", handler.LastBody);
    }

    [Fact]
    public async Task GetSubscription_ParsesTheStatus()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"id\":\"sub_1\",\"status\":\"active\"}");
        var client = ClientFor(handler, secret: "sk_test_abc", priceId: "price_123");

        var subscription = await client.GetSubscriptionAsync("sub_1", CancellationToken.None);

        Assert.Equal("sub_1", subscription!.Id);
        Assert.Equal("active", subscription.Status);
        Assert.Contains("/v1/subscriptions/sub_1", handler.LastRequest!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("sk_test_abc", null, false)]
    [InlineData(null, "price_123", false)]
    [InlineData("sk_test_abc", "price_123", true)]
    public void IsConfigured_NeedsBothSecretAndPrice(string? secret, string? priceId, bool expected)
    {
        Assert.Equal(expected, ClientFor(new StubHandler(HttpStatusCode.OK, "{}"), secret, priceId).IsConfigured);
    }

    // ----- #725: the webhook signature validator (HMAC over the raw body) -----

    [Fact]
    public void Validate_AcceptsAGenuineSignature_OverTheRawBody()
    {
        const string secret = "whsec_test";
        const string body = "{\"id\":\"evt_1\",\"type\":\"checkout.session.completed\"}";
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var header = $"t={t},v1={Sign(secret, t, body)}";

        Assert.True(ValidatorFor(secret).Validate(body, header));
    }

    [Fact]
    public void Validate_RejectsAWrongSecret()
    {
        const string body = "{\"id\":\"evt_1\"}";
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var header = $"t={t},v1={Sign("whsec_other", t, body)}";

        Assert.False(ValidatorFor("whsec_test").Validate(body, header));
    }

    [Fact]
    public void Validate_RejectsAnExpiredTimestamp()
    {
        const string secret = "whsec_test";
        const string body = "{\"id\":\"evt_1\"}";
        var t = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var header = $"t={t},v1={Sign(secret, t, body)}";

        Assert.False(ValidatorFor(secret).Validate(body, header));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-signature")]
    [InlineData("t=123")]
    public void Validate_RejectsAMalformedHeader(string? header)
    {
        Assert.False(ValidatorFor("whsec_test").Validate("{}", header));
    }

    [Fact]
    public void Validate_FailsClosed_WhenUnconfigured()
    {
        const string body = "{}";
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var header = $"t={t},v1={Sign("whsec_test", t, body)}";

        Assert.False(ValidatorFor(secret: null).Validate(body, header));
    }

    private static string Sign(string secret, string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}")));
    }

    private static StripeClient ClientFor(HttpMessageHandler handler, string? secret, string? priceId)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        return new StripeClient(factory, ConfigWith(("Stripe:SecretApiKey", secret), ("Stripe:PriceId", priceId)), NullLogger<StripeClient>.Instance);
    }

    private static StripeSignatureValidator ValidatorFor(string? secret) =>
        new(ConfigWith(("Stripe:WebhookSigningSecret", secret)), NullLogger<StripeSignatureValidator>.Instance);

    private static IConfiguration ConfigWith(params (string Key, string? Value)[] entries)
    {
        var dict = new Dictionary<string, string?>();
        foreach (var (key, value) in entries)
        {
            dict[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _json;

        public StubHandler(HttpStatusCode status, string json)
        {
            _status = status;
            _json = json;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(_status) { Content = new StringContent(_json) };
        }
    }
}
