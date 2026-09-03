using System.Net;
using System.Text;
using Consultologist.Api.Auth;
using Consultologist.Api.Documents;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.Tests;

/// <summary>
/// #615: the sharing link's road to bytes — the statics asserted directly,
/// the wire behaviour on a stub handler.
/// </summary>
public class GraphDocumentFetcherTests
{
    [Theory]
    [InlineData("https://contoso.sharepoint.com/:w:/g/x/abc", true)]
    [InlineData("https://1drv.ms/w/s!abc", true)]
    [InlineData("https://onedrive.live.com/redir?resid=x", true)]
    [InlineData("https://drive.google.com/file/d/x", false)]
    [InlineData("https://dropbox.com/s/x", false)]
    [InlineData("https://evil.example-sharepoint.com/x", false)]
    [InlineData("https://sharepoint.com.evil.example/x", false)]
    [InlineData("http://contoso.sharepoint.com/x", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsGraphShareLink_IsTheMicrosoftTrio_WithDotBoundaries(string? url, bool expected)
    {
        Assert.Equal(expected, GraphDocumentFetcher.IsGraphShareLink(url));
    }

    [Fact]
    public void ShareIdOf_IsGraphsUnpaddedBase64UrlForm()
    {
        // The documented encoding: "u!" + base64url(url), padding trimmed,
        // +/ swapped for -_ .
        Assert.Equal(
            "u!" + Convert.ToBase64String(Encoding.UTF8.GetBytes("https://1drv.ms/x"))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            GraphDocumentFetcher.ShareIdOf("https://1drv.ms/x"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(1024L, null)]
    [InlineData(10L * 1024 * 1024, null)]
    [InlineData(10L * 1024 * 1024 + 1, GraphDocumentRefusals.TooLarge)]
    public void SizeRefusalOf_GatesOnTheParsersOwnCap(long? size, string? expected)
    {
        Assert.Equal(expected, GraphDocumentFetcher.SizeRefusalOf(size));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.NotFound, GraphDocumentRefusals.NotFound)]
    [InlineData(HttpStatusCode.Gone, GraphDocumentRefusals.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, GraphDocumentRefusals.Forbidden)]
    [InlineData(HttpStatusCode.Forbidden, GraphDocumentRefusals.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError, GraphDocumentRefusals.FetchFailed)]
    public void RefusalFor_JudgesGraphStatuses(HttpStatusCode status, string? expected)
    {
        Assert.Equal(expected, GraphDocumentFetcher.RefusalFor(status));
    }

    [Theory]
    [InlineData(OnBehalfOfRefusals.PersonalAccount, HttpStatusCode.Forbidden)]
    [InlineData(OnBehalfOfRefusals.ConsentRequired, HttpStatusCode.Forbidden)]
    [InlineData(OnBehalfOfRefusals.ExchangeFailed, HttpStatusCode.BadGateway)]
    [InlineData(GraphDocumentRefusals.NotOneDrive, HttpStatusCode.UnprocessableEntity)]
    [InlineData(GraphDocumentRefusals.NotFound, HttpStatusCode.UnprocessableEntity)]
    [InlineData(GraphDocumentRefusals.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData(GraphDocumentRefusals.TooLarge, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(GraphDocumentRefusals.FetchFailed, HttpStatusCode.BadGateway)]
    // The default is 502, not 500: an unmapped word is a downstream fact,
    // never an apparent server fault.
    [InlineData("some-future-word", HttpStatusCode.BadGateway)]
    public void StatusForLink_MapsEveryWord_AndDefaultsDownstream(string word, HttpStatusCode expected)
    {
        Assert.Equal(expected, DocumentExtractions.StatusForLink(word));
    }

    // The wire, on a stub: the size gate refuses BEFORE the content call.

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;
        public int Calls;

        public SequenceHandler(params HttpResponseMessage[] responses) => _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ATooLargeItem_IsRefusedAtMetadata_NoContentCall()
    {
        var handler = new SequenceHandler(Json(HttpStatusCode.OK, "{\"size\":999999999,\"name\":\"x.docx\"}"));
        var fetcher = new GraphDocumentFetcher(new StubFactory(handler), NullLogger<GraphDocumentFetcher>.Instance);

        var outcome = await fetcher.FetchAsync("token", "https://1drv.ms/x", CancellationToken.None);

        Assert.Equal(GraphDocumentRefusals.TooLarge, outcome.Refusal);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AReadableItem_FetchesItsBytes()
    {
        var handler = new SequenceHandler(
            Json(HttpStatusCode.OK, "{\"size\":11,\"name\":\"x.txt\"}"),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("hello bytes")) });
        var fetcher = new GraphDocumentFetcher(new StubFactory(handler), NullLogger<GraphDocumentFetcher>.Instance);

        var outcome = await fetcher.FetchAsync("token", "https://1drv.ms/x", CancellationToken.None);

        Assert.Null(outcome.Refusal);
        Assert.Equal("hello bytes", Encoding.UTF8.GetString(outcome.Content!));
        Assert.Equal("x.txt", outcome.Name);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task AnUnreadableItem_CarriesItsWord()
    {
        var handler = new SequenceHandler(Json(HttpStatusCode.Forbidden, "{\"error\":{\"code\":\"accessDenied\"}}"));
        var fetcher = new GraphDocumentFetcher(new StubFactory(handler), NullLogger<GraphDocumentFetcher>.Instance);

        var outcome = await fetcher.FetchAsync("token", "https://contoso.sharepoint.com/:w:/g/x", CancellationToken.None);

        Assert.Equal(GraphDocumentRefusals.Forbidden, outcome.Refusal);
    }
}
