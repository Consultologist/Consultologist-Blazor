using System.Net;

using Consultologist.Api;
using Consultologist.Api.Marketplace;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace Consultologist.Api.Tests;

public class MarketplaceTests
{
    // ----- #669: the S2S token response reader -----

    [Fact]
    public void ReadTokenResponse_ReturnsTheAccessToken_On200()
    {
        Assert.Equal("abc.def", MarketplaceTokenClient.ReadTokenResponse(200, "{\"access_token\":\"abc.def\",\"expires_in\":3600}"));
    }

    [Theory]
    [InlineData(401, "{\"error\":\"invalid_client\"}")]
    [InlineData(200, "{\"access_token\":\"\"}")]
    [InlineData(200, "not json")]
    [InlineData(200, "{}")]
    public void ReadTokenResponse_IsNull_OnFailureOrEmpty(int status, string body)
    {
        Assert.Null(MarketplaceTokenClient.ReadTokenResponse(status, body));
    }

    // ----- #669: the webhook action → effect mapping (§5 of the spike) -----

    [Theory]
    [InlineData("Subscribe", "Activate")]
    [InlineData("Activate", "Activate")]
    [InlineData("Reinstate", "Activate")]
    [InlineData("Suspend", "Suspend")]
    [InlineData("Unsubscribe", "Unsubscribe")]
    [InlineData("ChangePlan", "AckOnly")]
    [InlineData("ChangeQuantity", "AckOnly")]
    [InlineData("Renew", "Ignore")]
    [InlineData("SomethingNew", "Ignore")]
    [InlineData(null, "Ignore")]
    public void EffectFor_MapsTheLifecycle(string? action, string expected)
    {
        Assert.Equal(expected, AccountMarketplace.EffectFor(action).ToString());
    }

    // ----- #669: the Fulfillment API client (raw REST) -----

    [Fact]
    public async Task Resolve_PostsTheTokenHeader_AndParsesTheSubscription()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"id\":\"sub-1\",\"offerId\":\"off-1\",\"planId\":\"trial\",\"quantity\":3,\"subscriptionName\":\"Clinic\"}");
        var client = ClientFor(handler);

        var resolved = await client.ResolveAsync("the-token", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal("sub-1", resolved!.SubscriptionId);
        Assert.Equal("off-1", resolved.OfferId);
        Assert.Equal("trial", resolved.PlanId);
        Assert.Equal(3, resolved.Quantity);
        Assert.Equal("Clinic", resolved.SubscriptionName);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("/subscriptions/resolve?api-version=2018-08-31", handler.LastRequest.RequestUri!.ToString());
        Assert.Equal("the-token", handler.LastRequest.Headers.GetValues("x-ms-marketplace-token").Single());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("tkn", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Resolve_IsNull_WhenNoSubscriptionId()
    {
        var resolved = await ClientFor(new StubHandler(HttpStatusCode.OK, "{\"offerId\":\"off\"}"))
            .ResolveAsync("t", CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task GetOperation_ParsesTheOperation()
    {
        var op = await ClientFor(new StubHandler(HttpStatusCode.OK,
                "{\"id\":\"op-1\",\"status\":\"InProgress\",\"action\":\"ChangePlan\",\"subscriptionId\":\"sub-1\"}"))
            .GetOperationAsync("sub-1", "op-1", CancellationToken.None);

        Assert.NotNull(op);
        Assert.Equal("op-1", op!.Id);
        Assert.Equal("ChangePlan", op.Action);
        Assert.Equal("sub-1", op.SubscriptionId);
    }

    [Fact]
    public async Task AckOperation_Patches_WithTheSuccessStatus()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        await ClientFor(handler).AckOperationAsync("sub-1", "op-1", success: true, CancellationToken.None);

        Assert.Equal(HttpMethod.Patch, handler.LastRequest!.Method);
        Assert.Contains("/subscriptions/sub-1/operations/op-1?api-version=2018-08-31", handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("\"status\":\"Success\"", handler.LastBody);
    }

    private static MarketplaceFulfillmentClient ClientFor(HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        var tokens = Substitute.For<IMarketplaceTokenClient>();
        tokens.GetTokenAsync(Arg.Any<CancellationToken>()).Returns("tkn");

        return new MarketplaceFulfillmentClient(factory, tokens, NullLogger<MarketplaceFulfillmentClient>.Instance);
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
