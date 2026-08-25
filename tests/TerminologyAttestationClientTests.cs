using System.Net;
using Consultologist.Api.Workflow;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.Tests;

/// <summary>#403: what the terminology server says, read once per cache window, kept through an outage.</summary>
public class TerminologyAttestationClientTests
{
    private const string Document = """{"Edition":"SNOMEDCT 20251130 import.","Version":"2025-11-30","ImportDate":"2025-12-21T22:39:16.944Z","ServerVersion":"1.0.0","Commit":"0fff939d4a5c3a6e7b8c9d0e1f2a3b4c5d6e7f80","GeneratedAtUtc":"2026-08-25T20:00:00Z"}""";

    [Fact]
    public void Describe_TakesTheEditionAsReported_AndTheServerFromItsCommit()
    {
        var attestation = TerminologyAttestationClient.Describe(
            new TerminologyAttestationClient.TerminologyInfoDocument("SNOMEDCT 20251130 import.", "2025-11-30", "2025-12-21T22:39:16.944Z", "1.0.0", "0fff939d4a5c3a6e7b8c9d0e1f2a3b4c5d6e7f80"),
            DateTimeOffset.UnixEpoch)!;

        Assert.Equal(new TerminologySnapshot("SNOMEDCT 20251130 import.", "2025-11-30", "2025-12-21T22:39:16.944Z"), attestation.Terminology);
        Assert.Equal("snomed-snowstorm-mcp@0fff939d4a5c3a6e7b8c9d0e1f2a3b4c5d6e7f80", attestation.ServerRef);

        // A hand-deployed server carries no commit: its version, said as such.
        var unstamped = TerminologyAttestationClient.Describe(
            new TerminologyAttestationClient.TerminologyInfoDocument("e", "v", "d", "1.0.0", null), DateTimeOffset.UnixEpoch)!;
        Assert.Equal("snomed-snowstorm-mcp@1.0.0", unstamped.ServerRef);

        Assert.Null(TerminologyAttestationClient.Describe(null, DateTimeOffset.UnixEpoch));
        Assert.Null(TerminologyAttestationClient.Describe(new TerminologyAttestationClient.TerminologyInfoDocument(null, null, null, null, null), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task Unconfigured_SaysNothing_AndNeverCalls()
    {
        var handler = new StubHandler(Document);
        var client = new TerminologyAttestationClient(new StubFactory(handler), NullLogger<TerminologyAttestationClient>.Instance, null, 60);

        Assert.Null(await client.GetAsync(CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ReadsOncePerCacheWindow_AndKeepsTheLastAnswerThroughAnOutage()
    {
        var handler = new StubHandler(Document);
        var client = new TerminologyAttestationClient(new StubFactory(handler), NullLogger<TerminologyAttestationClient>.Instance, "https://mcp.example/api/Public/Terminology", 60);

        var first = await client.GetAsync(CancellationToken.None);
        var second = await client.GetAsync(CancellationToken.None);
        Assert.Equal("2025-11-30", first!.Terminology!.Version);
        Assert.Same(first, second);
        Assert.Equal(1, handler.Calls);

        // The window expires and the server is down: the last answer stands.
        var expiring = new TerminologyAttestationClient(new StubFactory(handler), NullLogger<TerminologyAttestationClient>.Instance, "https://mcp.example/api/Public/Terminology", 0);
        var good = await expiring.GetAsync(CancellationToken.None);
        handler.Fail = true;
        var kept = await expiring.GetAsync(CancellationToken.None);
        Assert.Same(good, kept);

        // Down from the start: nothing, not a guess.
        var down = new TerminologyAttestationClient(new StubFactory(new StubHandler(Document) { Fail = true }), NullLogger<TerminologyAttestationClient>.Instance, "https://mcp.example/api/Public/Terminology", 60);
        Assert.Null(await down.GetAsync(CancellationToken.None));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                throw new HttpRequestException("down");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
