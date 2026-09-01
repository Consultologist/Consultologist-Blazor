using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Consultologist.Web.Services.Locations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Consultologist.Web.Services.Operators;

public sealed class OperatorEndpointService : IOperatorEndpointService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IApiLocations _locations;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ILogger<OperatorEndpointService> _logger;

    public OperatorEndpointService(
        HttpClient httpClient,
        IConfiguration configuration,
        IApiLocations locations,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        ILogger<OperatorEndpointService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _locations = locations;
        _accessTokenProvider = accessTokenProvider;
        _navigation = navigation;
        _logger = logger;
    }

    public async Task<OperatorUsageResponse> GetUsageAsync(string from, string to)
    {
        var url = _locations.Url(ApiRoutes.OperatorUsage)
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
