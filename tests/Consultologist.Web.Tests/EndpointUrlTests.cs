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

        // #858: the import ingest endpoint (raw .zip POST) resolves to the chosen location.
        try { await service.IngestPackageAsync(new byte[] { 1 }); } catch { }
        Assert.Equal("https://west.ca.api.consultologist.ai/api/WorkflowPackages/Ingest", handler.Requested?.ToString());
    }

    [Fact]
    public async Task TheWorkflowService_ReadsTheFormatsCatalogue_AuthenticatedAtTheChosenLocation()
    {
        // v25 (C1 #1007): GET WorkflowFormats is authorised (CanUseApp), so the request
        // carries the bearer; the view binds the server's PascalCase records.
        var handler = new RecordingHandler("""
            { "Version": "v2026.10.1", "Ref": "formats@v2026.10.1",
              "Formats": [ { "Id": "classification", "Type": "enum", "Producer": "attested", "OutputMacros": false,
                             "Parameters": { "values": { "Kind": "values" } }, "File": "formats/classification.json",
                             "FormatHash": "abc", "AgentName": "classification", "AgentVersion": 1, "Attested": true } ],
              "Custom": { "AgentName": "custom-json", "AgentVersion": 1 } }
            """);
        var service = new WorkflowEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<WorkflowEndpointService>>());

        var formats = await service.GetFormatsAsync();

        Assert.Equal("https://west.ca.api.consultologist.ai/api/WorkflowFormats", handler.Requested?.ToString());
        Assert.Equal("formats@v2026.10.1", formats?.Ref);
        var entry = Assert.Single(formats!.Formats!);
        Assert.Equal("values", entry.Parameters!["values"].Kind);
        Assert.Equal(1, entry.AgentVersion);
        Assert.Equal("custom-json", formats.Custom?.AgentName);

        // Cached: a second read does not go to the network.
        var again = await service.GetFormatsAsync();
        Assert.Same(formats, again);
    }

    [Fact]
    public async Task TheWorkflowService_FormatsCatalogue_IsNullWhenTheEndpointFails()
    {
        var handler = new FailingHandler();
        var service = new WorkflowEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<WorkflowEndpointService>>());

        Assert.Null(await service.GetFormatsAsync());
    }

    [Fact]
    public async Task TheWorkflowService_AsksTheStartChecks_WithTheRefAndThePick()
    {
        var handler = new RecordingHandler("""{"Refusals":["Set your time zone in your account settings before running a workflow."],"NeedsProfile":["signature"]}""");
        var service = new WorkflowEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<WorkflowEndpointService>>());

        var checks = await service.GetStartChecksAsync("example-note@v2026.11.1", "sig-2");

        Assert.Equal(
            "https://west.ca.api.consultologist.ai/api/WorkflowPackages/Current/StartChecks?ref=example-note%40v2026.11.1&signatureBlockId=sig-2",
            handler.Requested?.ToString());
        Assert.Equal(["Set your time zone in your account settings before running a workflow."], checks.Refusals);
        Assert.Equal(["signature"], checks.NeedsProfile);

        await service.GetStartChecksAsync();
        Assert.Equal("https://west.ca.api.consultologist.ai/api/WorkflowPackages/Current/StartChecks", handler.Requested?.ToString());
    }

    [Fact]
    public async Task TheWorkflowService_StartChecks400_ThrowsCarryingTheError()
    {
        var handler = new StatusHandler(HttpStatusCode.BadRequest, """{"error":"This version isn't published."}""");
        var service = new WorkflowEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<WorkflowEndpointService>>());

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetStartChecksAsync("example-note@v2026.11.1"));

        Assert.Equal("This version isn't published.", thrown.Message);
        Assert.Equal(HttpStatusCode.BadRequest, thrown.StatusCode);
    }

    [Fact]
    public async Task TheWorkflowService_Current400_ThrowsCarryingTheError()
    {
        // #1509 (Task 11): a 31 package the engine cannot run is Current's 400 { error }; the Create page says it word for word.
        const string requires = "This workflow (example-note@v2026.11.1) needs Scriban ^7.2; this engine runs Scriban 8.0.1. Publish a new version of the workflow to run it on this engine.";
        var handler = new StatusHandler(HttpStatusCode.BadRequest, "{\"error\":\"" + requires + "\"}");
        var service = new WorkflowEndpointService(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<WorkflowEndpointService>>());

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetCurrentPackageAsync());

        Assert.Equal(requires, thrown.Message);
        Assert.Equal(HttpStatusCode.BadRequest, thrown.StatusCode);
    }

    [Fact]
    public async Task TheWorkflowService_CurrentOtherFailure_KeepsTodaysMessage()
    {
        var service = new WorkflowEndpointService(new HttpClient(new FailingHandler()), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<WorkflowEndpointService>>());

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetCurrentPackageAsync());

        Assert.Equal("Workflow package endpoint failed: ServiceUnavailable", thrown.Message);
    }

    private AIEndpointService Jobs(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Configuration, West, Tokens(), new TestNavigation(), Substitute.For<ILogger<AIEndpointService>>());

    private const string JobId31 = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task TheRowsClient_ListsAJobsRows_WithoutText()
    {
        // #1509 (Task 12): the route writes camelCase ({ rows: [ { key, section, row } ] }), in key order.
        var handler = new RecordingHandler("""
            {"rows":[{"key":"deliverable.note","section":"deliverable","row":{"key":"deliverable.note","state":"released"}},
                     {"key":"node.plan","section":"node","row":{"status":"Completed"}}]}
            """);

        var rows = await Jobs(handler).GetJobRowsAsync(JobId31);

        Assert.Equal($"https://west.ca.api.consultologist.ai/api/ConsultGenerationJobs/{JobId31}/Rows", handler.Requested?.ToString());
        Assert.Equal(["deliverable.note", "node.plan"], rows.Select(row => row.Key));
        Assert.Equal(["deliverable", "node"], rows.Select(row => row.Section));
        Assert.Equal("released", rows[0].Row.GetProperty("state").GetString());
    }

    [Fact]
    public async Task TheRowsClient_FetchesOneRowByItsKey_WithItsText()
    {
        // a deliverable's text is an object of named texts; "content" is the composed text
        var handler = new RecordingHandler("""
            {"key":"node.sections.paragraphs.draft_paragraph:2/1","section":"node","row":{"status":"Completed"},
             "text":{"content":"Dr A. Sample saw Margaret Okafor."},"textDeleted":false}
            """);

        var row = await Jobs(handler).GetJobRowAsync(JobId31, "node.sections.paragraphs.draft_paragraph:2/1");

        Assert.Equal(
            $"https://west.ca.api.consultologist.ai/api/ConsultGenerationJobs/{JobId31}/Rows?key=node.sections.paragraphs.draft_paragraph%3A2%2F1",
            handler.Requested?.AbsoluteUri);
        Assert.NotNull(row);
        Assert.Equal("node", row.Section);
        Assert.Equal("Dr A. Sample saw Margaret Okafor.", row.Text!.Value.GetProperty("content").GetString());
        Assert.False(row.TextDeleted);
    }

    [Fact]
    public async Task TheRowsClient_ReadsADroppedText_AsNull_AndA404AsNoRow()
    {
        var dropped = await Jobs(new RecordingHandler("""{"key":"deliverable.note","section":"deliverable","row":{"state":"released"},"text":null,"textDeleted":true}"""))
            .GetJobRowAsync(JobId31, "deliverable.note");
        Assert.Null(dropped!.Text);
        Assert.True(dropped.TextDeleted);

        Assert.Null(await Jobs(new StatusHandler(HttpStatusCode.NotFound, """{"error":"The job has no row with that key."}"""))
            .GetJobRowAsync(JobId31, "node.nothing"));

        var failed = await Assert.ThrowsAsync<ConsultGenerationRefusedException>(() =>
            Jobs(new StatusHandler(HttpStatusCode.InternalServerError, """{"error":"The record isn't available yet. Try again."}""")).GetJobRowsAsync(JobId31));
        Assert.Equal("The record isn't available yet. Try again.", failed.Message);
    }

    [Fact]
    public async Task TheRecordDownload_CarriesTheBytes_TheHash_AndTheFileName()
    {
        var handler = new RecordHandler();

        var record = await Jobs(handler).DownloadJobRecordAsync(JobId31);

        Assert.Equal($"https://west.ca.api.consultologist.ai/api/ConsultGenerationJobs/{JobId31}/Record", handler.Requested?.ToString());
        Assert.Equal("{\"jobId\":\"j\"}"u8.ToArray(), record.Bytes);
        Assert.Equal("sha256:abc", record.RecordHash);
        Assert.Equal($"record-{JobId31}.json", record.FileName);
    }

    [Fact]
    public async Task TheRecordDownload_Says409And500InTheRoutesWords()
    {
        var unfinished = await Assert.ThrowsAsync<ConsultGenerationRefusedException>(() =>
            Jobs(new StatusHandler(HttpStatusCode.Conflict, """{"error":"The job hasn't finished."}""")).DownloadJobRecordAsync(JobId31));
        Assert.Equal("The job hasn't finished.", unfinished.Message);

        var unreadable = await Assert.ThrowsAsync<ConsultGenerationRefusedException>(() =>
            Jobs(new StatusHandler(HttpStatusCode.InternalServerError, """{"error":"The record isn't available yet. Try again."}""")).DownloadJobRecordAsync(JobId31));
        Assert.Equal("The record isn't available yet. Try again.", unreadable.Message);
    }

    private sealed class RecordHandler : HttpMessageHandler
    {
        public Uri? Requested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("{\"jobId\":\"j\"}"u8.ToArray()) };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            response.Content.Headers.ContentDisposition = System.Net.Http.Headers.ContentDispositionHeaderValue.Parse($"attachment; filename=\"record-{JobId31}.json\"");
            response.Headers.Add("X-Record-Hash", "sha256:abc");
            return Task.FromResult(response);
        }
    }

    private sealed class StatusHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
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
