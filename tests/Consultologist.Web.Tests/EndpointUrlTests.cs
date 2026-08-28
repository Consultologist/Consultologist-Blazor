using System.Net;
using System.Text;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Diagnostics;
using Consultologist.Web.Services.Documents;
using Consultologist.Web.Services.Locations;
using Consultologist.Web.Services.Workflow;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #515: every service calls the chosen location — one representative
/// request per service, its URL read off a stub handler.
/// </summary>
public class EndpointUrlTests
{
    private sealed class RecordingHandler(string body = "{}") : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("https://app.example/", "https://app.example/");

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
        }
    }

    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["AzureFunction:ApiScope"] = "api://consultologist/.default" })
        .Build();

    private static IAccessTokenProvider Tokens()
    {
        var tokens = Substitute.For<IAccessTokenProvider>();
        tokens.RequestAccessToken(Arg.Any<AccessTokenRequestOptions>())
            .Returns(new AccessTokenResult(
                AccessTokenResultStatus.Success,
                new AccessToken { Value = "token", Expires = DateTimeOffset.UtcNow.AddMinutes(5), GrantedScopes = new[] { "api://consultologist/.default" } },
                null,
                null));
        return tokens;
    }

    private static readonly FakeApiLocations West = new(
        new[] { FakeApiLocations.CanadaEast, new ApiLocation("ca-west", "Canada West", "https://west.ca.api.consultologist.ai/api") },
        chosenId: "ca-west");

    [Fact]
    public async Task TheAccountService_CallsTheChosenLocation()
    {
        var handler = new RecordingHandler();
        var service = new AccountEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<AccountEndpointService>>());

        await service.GetSettingAsync("consult.scheduleTime");

        Assert.Equal("https://west.ca.api.consultologist.ai/api/Account/Settings/consult.scheduleTime", handler.Requested?.ToString());
    }

    [Fact]
    public async Task TheJobsService_CallsTheChosenLocation()
    {
        var handler = new RecordingHandler("""{"JobId":"j","Status":"Running"}""");
        var service = new AIEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<AIEndpointService>>());

        try { await service.GetConsultGenerationJobAsync("0123456789abcdef0123456789abcdef"); } catch { /* the body is not the point */ }

        Assert.Equal("https://west.ca.api.consultologist.ai/api/ConsultGenerationJobs/0123456789abcdef0123456789abcdef", handler.Requested?.ToString());
    }

    [Fact]
    public async Task TheWorkflowService_CallsTheChosenLocation_ForPackagesAndTheEngine()
    {
        var handler = new RecordingHandler("""{"Commit":"abc","ApiHost":"west.ca.api.consultologist.ai"}""");
        var service = new WorkflowEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<WorkflowEndpointService>>());

        var engine = await service.GetEngineAsync();

        Assert.Equal("https://west.ca.api.consultologist.ai/api/Public/Engine", handler.Requested?.ToString());
        Assert.Equal("west.ca.api.consultologist.ai", engine?.ApiHost);

        try { await service.GetCurrentPackageAsync(); } catch { }
        Assert.Equal("https://west.ca.api.consultologist.ai/api/WorkflowPackages/Current", handler.Requested?.ToString());
    }

    [Fact]
    public async Task TheDocumentService_CallsTheChosenLocation()
    {
        var handler = new RecordingHandler();
        var service = new DocumentEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<DocumentEndpointService>>());

        try { await service.ExtractAsync(new byte[] { 1 }, "application/pdf"); } catch { }

        Assert.Equal("https://west.ca.api.consultologist.ai/api/DocumentExtractions", handler.Requested?.ToString());
    }

    [Fact]
    public async Task TheDiagnosticsService_CallsTheChosenLocation()
    {
        var handler = new RecordingHandler();
        var service = new SseDiagnosticsService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<IJSRuntime>(), Substitute.For<ILogger<SseDiagnosticsService>>());

        await service.ReportSseExitAsync(new SseExitDiagnostic("j", "a", "closed", null, null, 0, 1, false, false));

        Assert.Equal("https://west.ca.api.consultologist.ai/api/Diagnostics/SseExit", handler.Requested?.ToString());
    }
}
