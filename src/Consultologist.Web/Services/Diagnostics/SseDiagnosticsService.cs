using Consultologist.Web.Services.Locations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.JSInterop;

namespace Consultologist.Web.Services.Diagnostics;

public interface ISseDiagnosticsService
{
    Task ReportSseExitAsync(SseExitDiagnostic diagnostic);
}

public sealed class SseDiagnosticsService : ISseDiagnosticsService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IApiLocations _locations;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _jsRuntime;
    private readonly ILogger<SseDiagnosticsService> _logger;

    public SseDiagnosticsService(
        HttpClient httpClient,
        IConfiguration configuration,
        IApiLocations locations,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        IJSRuntime jsRuntime,
        ILogger<SseDiagnosticsService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _locations = locations;
        _accessTokenProvider = accessTokenProvider;
        _navigation = navigation;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    public async Task ReportSseExitAsync(SseExitDiagnostic diagnostic)
    {
        try
        {
            var endpointUrl = GetDiagnosticsEndpointUrl();
            var browserState = await GetBrowserStateAsync();
            var payload = new SseExitDiagnosticPayload(
                diagnostic.JobId,
                diagnostic.AttemptId,
                diagnostic.Reason,
                diagnostic.LatestEventId,
                diagnostic.LatestEventType,
                diagnostic.EventCount,
                diagnostic.ElapsedMs,
                diagnostic.PollingFallbackStarted,
                diagnostic.PollingFallbackCompleted,
                browserState?.VisibilityState,
                browserState?.NavigatorOnLine,
                browserState?.ServiceWorkerControlled);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUrl)
            {
                Content = JsonContent.Create(payload)
            };

            await AddAuthorizationAsync(request);

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "SSE exit diagnostic post was not accepted. JobId={JobId}, AttemptId={AttemptId}, Reason={Reason}, StatusCode={StatusCode}",
                    diagnostic.JobId,
                    diagnostic.AttemptId,
                    diagnostic.Reason,
                    response.StatusCode);
            }
        }
        catch (AccessTokenNotAvailableException)
        {
            _logger.LogDebug(
                "SSE exit diagnostic skipped because an access token was not available. JobId={JobId}, AttemptId={AttemptId}, Reason={Reason}",
                diagnostic.JobId,
                diagnostic.AttemptId,
                diagnostic.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "SSE exit diagnostic post failed. JobId={JobId}, AttemptId={AttemptId}, Reason={Reason}, ExceptionType={ExceptionType}",
                diagnostic.JobId,
                diagnostic.AttemptId,
                diagnostic.Reason,
                ex.GetType().FullName);
        }
    }

    private async Task<BrowserDiagnosticState?> GetBrowserStateAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<BrowserDiagnosticState>("consultologistDiagnostics.getBrowserState");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "Browser diagnostic state could not be read. ExceptionType={ExceptionType}",
                ex.GetType().FullName);
            return null;
        }
    }

    private string GetDiagnosticsEndpointUrl() => _locations.Url(ApiRoutes.DiagnosticsSseExit);

    private async Task AddAuthorizationAsync(HttpRequestMessage request)
    {
        var apiScope = _configuration["AzureFunction:ApiScope"];

        if (string.IsNullOrWhiteSpace(apiScope))
        {
            throw new InvalidOperationException("AzureFunction:ApiScope is not configured.");
        }

        var tokenResult = await _accessTokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = new[] { apiScope }
        });

        if (!tokenResult.TryGetToken(out var token))
        {
            throw new AccessTokenNotAvailableException(_navigation, tokenResult, new[] { apiScope });
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }
}

public sealed record SseExitDiagnostic(
    string JobId,
    string AttemptId,
    string Reason,
    string? LatestEventId,
    string? LatestEventType,
    int EventCount,
    long ElapsedMs,
    bool PollingFallbackStarted,
    bool PollingFallbackCompleted);

public sealed record SseExitDiagnosticPayload(
    string JobId,
    string AttemptId,
    string Reason,
    string? LatestEventId,
    string? LatestEventType,
    int EventCount,
    long ElapsedMs,
    bool PollingFallbackStarted,
    bool PollingFallbackCompleted,
    string? DocumentVisibility,
    bool? NavigatorOnLine,
    bool? ServiceWorkerControlled);

public sealed class BrowserDiagnosticState
{
    public string? VisibilityState { get; set; }
    public bool? NavigatorOnLine { get; set; }
    public bool? ServiceWorkerControlled { get; set; }
}
