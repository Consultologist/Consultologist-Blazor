using System.Net;
using System.Text;
using Consultologist.Web.Services.AI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #348: a refusal's reason has to survive the trip from the transport to the
/// screen. Every start refusal answers with <c>{ "error": "…" }</c> naming the
/// input, the document or the wait, and the client threw that away — six
/// distinct refusals arrived as the single word "UnprocessableEntity".
///
/// These go through the real <see cref="AIEndpointService"/> against a stubbed
/// handler rather than testing the parse in isolation, because the discarding
/// happened in the plumbing around the parse, not in it.
/// </summary>
public class AIEndpointRefusalTests
{
    /// <summary>What the server actually composes for an empty fire set.</summary>
    private const string Refusal =
        "No document applies to these inputs. 'Consultation note' needs billable to be 'true'; it is 'false'.";

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://app.example/", "https://app.example/consults");

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }

    private static AIEndpointService Service(HttpStatusCode status, string body, string mediaType = "application/json")
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureFunction:ApiScope"] = "api://consultologist/.default"
            })
            .Build();

        var tokens = Substitute.For<IAccessTokenProvider>();
        tokens.RequestAccessToken(Arg.Any<AccessTokenRequestOptions>())
            .Returns(new AccessTokenResult(
                AccessTokenResultStatus.Success,
                new AccessToken
                {
                    Value = "token",
                    Expires = DateTimeOffset.UtcNow.AddMinutes(5),
                    GrantedScopes = new[] { "api://consultologist/.default" }
                },
                null,
                null));

        return new AIEndpointService(
            new HttpClient(new StubHandler(response)),
            configuration,
            new FakeApiLocations(),
            tokens,
            new TestNavigation(),
            Substitute.For<ILogger<AIEndpointService>>());
    }

    private static Task<ConsultGenerationJobStartResponse> StartAsync(AIEndpointService service) =>
        service.StartConsultGenerationJobAsync(
            new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = ConsultInputValue.OfText("Chest pain, rule out ACS."),
                ["billable"] = ConsultInputValue.OfBoolean(false)
            },
            "acct-1234567890ab@v2026.08.8");

    [Fact]
    public async Task AStartRefusal_ArrivesAsTheServerWroteIt()
    {
        var service = Service(HttpStatusCode.UnprocessableEntity, $$"""{"error":"{{Refusal}}"}""");

        var refusal = await Assert.ThrowsAsync<ConsultGenerationRefusedException>(() => StartAsync(service));

        Assert.Equal(Refusal, refusal.Message);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refusal.Status);
    }

    [Fact]
    public async Task ARefusalThatLeftARow_SaysWhereItIs()
    {
        // #434: the same status and sentence, plus the id of the job born
        // Failed. Read the same tolerant way as the sentence.
        var service = Service(HttpStatusCode.UnprocessableEntity, $$"""{"error":"{{Refusal}}","jobId":"bb4df62f2bea4dd193a92ae0e6798370"}""");

        var refusal = await Assert.ThrowsAsync<ConsultGenerationRefusedException>(() => StartAsync(service));

        Assert.Equal(Refusal, refusal.Message);
        Assert.Equal("bb4df62f2bea4dd193a92ae0e6798370", refusal.JobId);
    }

    [Theory]
    [InlineData("""{"error":"no","jobId":null}""")]
    [InlineData("""{"error":"no"}""")]
    public async Task ARefusalThatLeftNothing_HasNoJobId(string body)
    {
        var service = Service(HttpStatusCode.UnprocessableEntity, body);

        var refusal = await Assert.ThrowsAsync<ConsultGenerationRefusedException>(() => StartAsync(service));

        Assert.Null(refusal.JobId);
    }

    [Fact]
    public async Task APollRefusal_ArrivesTheSameWay()
    {
        // The poll path discarded the body with its own copy of the same
        // throw, so fixing only the start path would have left half of it.
        var service = Service(HttpStatusCode.Forbidden, """{"error":"That job belongs to another account."}""");

        var refusal = await Assert.ThrowsAsync<ConsultGenerationRefusedException>(
            () => service.GetConsultGenerationJobAsync("bb4df62f2bea4dd193a92ae0e6798370"));

        Assert.Equal("That job belongs to another account.", refusal.Message);
        Assert.Equal(HttpStatusCode.Forbidden, refusal.Status);
    }

    [Fact]
    public async Task AnInfrastructureFailure_StaysATransportError()
    {
        // A 502 from the platform carries an HTML page, not an answer. Showing
        // it would be worse than the status code, so it must NOT become a
        // refusal — this is the half of the change that has to stay narrow.
        var service = Service(
            HttpStatusCode.BadGateway,
            "<html><head><title>502 Bad Gateway</title></head><body>The service is unavailable.</body></html>",
            "text/html");

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => StartAsync(service));

        Assert.Contains("BadGateway", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("<html>", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Internal server error")]
    [InlineData("""{"detail":"something else"}""")]
    [InlineData("""{"error":"   "}""")]
    [InlineData("""["error"]""")]
    public async Task AnythingWithoutAWrittenReason_StaysATransportError(string body)
    {
        // Blank, unparseable, the wrong property, whitespace, and the right
        // word in the wrong shape. Each would otherwise surface an empty
        // message bar, which reads as a UI fault rather than a refusal.
        var service = Service(HttpStatusCode.InternalServerError, body);

        var failure = await Assert.ThrowsAsync<HttpRequestException>(() => StartAsync(service));

        Assert.Contains("InternalServerError", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReasonIsReadWhateverTheSerializerCapitalised()
    {
        // The transport writes it lowercase, but the property crosses
        // middleware whose casing is not ours to assume.
        var service = Service(HttpStatusCode.UnprocessableEntity, """{"Error":"Referral text carries no content."}""");

        var refusal = await Assert.ThrowsAsync<ConsultGenerationRefusedException>(() => StartAsync(service));

        Assert.Equal("Referral text carries no content.", refusal.Message);
    }
}
