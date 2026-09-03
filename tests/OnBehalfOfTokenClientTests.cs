using System.Net;
using System.Text;
using Azure.Core;
using Consultologist.Api.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.Tests;

/// <summary>
/// #615: the On-Behalf-Of exchange. The judgment over the token response is
/// a pure static (the RefusalWordFor family); the personal-account refusal
/// happens before any wire; the wire behaviour rides a stub handler (the
/// TerminologyAttestationClient pattern).
/// </summary>
public class OnBehalfOfTokenClientTests
{
    private static AuthenticatedUser User(string? tenantId) =>
        new("entra-external-id", "https://login.microsoftonline.com/tenant/v2.0",
            "subject-1", "Test User", "user@example.com",
            new[] { "access_as_user" }, TenantId: tenantId);

    private sealed class StubHandler : HttpMessageHandler
    {
        public int Calls;
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class StubCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("uami-assertion", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    private static OnBehalfOfTokenClient Client(StubHandler handler) =>
        new(new StubFactory(handler), new StubCredential(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Audience"] = "api-client-id"
            }).Build(),
            NullLogger<OnBehalfOfTokenClient>.Instance,
            tokenEndpointOverride: "https://login.example/token");

    [Fact]
    public async Task APersonalAccount_IsRefusedBeforeTheWire()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");

        var outcome = await Client(handler).ExchangeAsync(
            User(DeliveryAddress.ConsumersTenantId), "incoming-token",
            "https://graph.microsoft.com/Files.Read", CancellationToken.None);

        Assert.Equal(OnBehalfOfRefusals.PersonalAccount, outcome.Refusal);
        Assert.Null(outcome.AccessToken);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AnOrganisationToken_Exchanges()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            "{\"access_token\":\"downstream-token\",\"token_type\":\"Bearer\"}");

        var outcome = await Client(handler).ExchangeAsync(
            User("4258958f-9334-4a8c-af82-d7cddc47ae50"), "incoming-token",
            "https://graph.microsoft.com/Files.Read", CancellationToken.None);

        Assert.Equal("downstream-token", outcome.AccessToken);
        Assert.Null(outcome.Refusal);
        Assert.Equal(1, handler.Calls);
    }

    // The judgment, asserted directly.

    [Fact]
    public void ASuccessBody_IsExchanged()
    {
        var outcome = OnBehalfOfTokenClient.ReadExchangeResponse(200, "{\"access_token\":\"t\"}");

        Assert.Equal("t", outcome.AccessToken);
    }

    [Theory]
    [InlineData("{\"error\":\"interaction_required\",\"error_description\":\"AADSTS65001: consent.\"}")]
    [InlineData("{\"error\":\"invalid_grant\",\"suberror\":\"consent_required\",\"error_description\":\"x\"}")]
    [InlineData("{\"error\":\"invalid_grant\",\"error_description\":\"AADSTS65001: The user or administrator has not consented.\"}")]
    public void AConsentProblem_IsItsOwnWord(string body)
    {
        // The tenant saying no is not an exchange fault — a caller can act
        // on obo-consent-required (ask the admin); obo-exchange-failed only
        // says try later or file it.
        Assert.Equal(OnBehalfOfRefusals.ConsentRequired,
            OnBehalfOfTokenClient.ReadExchangeResponse(400, body).Refusal);
    }

    [Theory]
    [InlineData(400, "{\"error\":\"invalid_client\",\"error_description\":\"AADSTS700027: bad assertion.\"}")]
    [InlineData(500, "{\"error\":\"server_error\"}")]
    [InlineData(200, "{\"token_type\":\"Bearer\"}")]
    [InlineData(200, "not-json")]
    public void AnythingElse_IsAnExchangeFailure_NeverAThrow(int status, string body)
    {
        Assert.Equal(OnBehalfOfRefusals.ExchangeFailed,
            OnBehalfOfTokenClient.ReadExchangeResponse(status, body).Refusal);
    }
}
