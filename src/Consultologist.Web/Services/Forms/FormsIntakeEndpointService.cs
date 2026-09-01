using System.Net.Http.Headers;
using System.Net.Http.Json;
using Consultologist.Web.Services.Locations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Consultologist.Web.Services.Forms;

/// <summary>One held response as the list knows it — ids and days, never a value (#539).</summary>
public sealed record FormResponseListRow(
    string FormId,
    string ResponseId,
    DateTimeOffset SubmittedAtUtc,
    IReadOnlyList<string> InputIds,
    DateTimeOffset? DeletedAtUtc);

/// <summary>#540: one held response with its values — the picker's read.</summary>
public sealed record FormResponseValues(
    string FormId,
    string ResponseId,
    DateTimeOffset SubmittedAtUtc,
    Dictionary<string, string> Inputs);

public interface IFormsIntakeEndpointService
{
    Task<IReadOnlyList<FormResponseListRow>> ListResponsesAsync();

    Task<FormResponseValues> GetResponseAsync(string formId, string responseId);
}

/// <summary>
/// The intake door's client (#540): the held-responses list and the
/// one-response values read, both organisation-gated server-side. The
/// AccountEndpointService idiom: located URL, bearer, JSON or a named throw.
/// </summary>
public sealed class FormsIntakeEndpointService : IFormsIntakeEndpointService
{
    private sealed record FormResponseListResponse(List<FormResponseListRow> Responses);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IApiLocations _locations;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ILogger<FormsIntakeEndpointService> _logger;

    public FormsIntakeEndpointService(
        HttpClient httpClient,
        IConfiguration configuration,
        IApiLocations locations,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        ILogger<FormsIntakeEndpointService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _locations = locations;
        _accessTokenProvider = accessTokenProvider;
        _navigation = navigation;
        _logger = logger;
    }

    public async Task<IReadOnlyList<FormResponseListRow>> ListResponsesAsync()
    {
        var url = _locations.Url(ApiRoutes.IntakeFormsResponses);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Form responses list failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Form responses list failed: {response.StatusCode}");
        }

        var list = await response.Content.ReadFromJsonAsync<FormResponseListResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize the form responses list.");
        return list.Responses;
    }

    public async Task<FormResponseValues> GetResponseAsync(string formId, string responseId)
    {
        var url = $"{_locations.Url(ApiRoutes.IntakeFormsResponses)}/{Uri.EscapeDataString(formId)}/{Uri.EscapeDataString(responseId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Form response read failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Form response read failed: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<FormResponseValues>()
            ?? throw new InvalidOperationException("Failed to deserialize the form response.");
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
