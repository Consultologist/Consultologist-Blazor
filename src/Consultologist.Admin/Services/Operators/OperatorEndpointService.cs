using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Consultologist.Admin.Services.Operators;

/// <summary>
/// #733: the admin app's operator client. Unlike the clinician SPA's version it
/// resolves a single API base from <c>Api:BaseUrl</c> (this is a one-deployment
/// ops tool, not the multi-region clinician app) and keeps the same per-call
/// bearer against <c>AzureFunction:ApiScope</c>. The server gate is the real
/// boundary; a 403 for a signed-in non-operator surfaces as the named state.
/// </summary>
public sealed class OperatorEndpointService : IOperatorEndpointService
{
    public const string ApiBaseKey = "Api:BaseUrl";
    public const string ApiScopeKey = "AzureFunction:ApiScope";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ILogger<OperatorEndpointService> _logger;

    public OperatorEndpointService(
        HttpClient httpClient,
        IConfiguration configuration,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        ILogger<OperatorEndpointService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _accessTokenProvider = accessTokenProvider;
        _navigation = navigation;
        _logger = logger;
    }

    public async Task<OperatorUsageResponse> GetUsageAsync(string from, string to)
    {
        var apiBase = _configuration[ApiBaseKey];
        if (string.IsNullOrWhiteSpace(apiBase))
        {
            throw new InvalidOperationException($"{ApiBaseKey} is not configured.");
        }

        var url = $"{apiBase.TrimEnd('/')}/Operator/Usage"
            + $"?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            // The allowlist's 403 carries no body by design; the named
            // sentence is the client's.
            throw new OperatorAccessException();
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Operator usage endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Operator usage endpoint failed: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<OperatorUsageResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize operator usage response.");
    }

    private async Task AddAuthorizationAsync(HttpRequestMessage request)
    {
        var apiScope = _configuration[ApiScopeKey];

        if (string.IsNullOrWhiteSpace(apiScope))
        {
            throw new InvalidOperationException($"{ApiScopeKey} is not configured.");
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
