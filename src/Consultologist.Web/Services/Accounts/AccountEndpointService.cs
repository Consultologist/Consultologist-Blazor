using Consultologist.Web.Services.Locations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;

namespace Consultologist.Web.Services.Accounts;

public sealed class AccountEndpointService : IAccountEndpointService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IApiLocations _locations;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ILogger<AccountEndpointService> _logger;

    public AccountEndpointService(
        HttpClient httpClient,
        IConfiguration configuration,
        IApiLocations locations,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        ILogger<AccountEndpointService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _locations = locations;
        _accessTokenProvider = accessTokenProvider;
        _navigation = navigation;
        _logger = logger;
    }

    public async Task<AccountMeResponse> GetCurrentAccountAsync()
    {
        var accountUrl = _locations.Url(ApiRoutes.AccountMe);
        using var request = new HttpRequestMessage(HttpMethod.Get, accountUrl);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Account endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Account endpoint failed: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<AccountMeResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize account response.");
    }

    public async Task<string> StartLinkedInLinkAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GetAccountBaseUrl() + "/LinkedIn/Start");
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "LinkedIn link start failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"LinkedIn link start failed: {response.StatusCode}");
        }

        var startResponse = await response.Content.ReadFromJsonAsync<LinkedInStartResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize LinkedIn start response.");

        return startResponse.AuthorizationUrl;
    }

    public async Task SetDeliveryPasswordAsync(string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, GetAccountBaseUrl() + "/DeliveryPassword")
        {
            Content = JsonContent.Create(new SaveDeliveryPasswordRequest(password))
        };
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            // The 400 body carries the validation message (e.g. length) —
            // surface it; the password itself is never logged.
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Delivery password save failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException(ExtractError(error) ?? $"Delivery password save failed: {response.StatusCode}");
        }
    }

    public async Task DisconnectLinkedInAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, GetAccountBaseUrl() + "/LinkedIn");
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("LinkedIn disconnect failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException(ExtractError(error) ?? $"LinkedIn disconnect failed: {response.StatusCode}");
        }
    }

    public async Task ClearDeliveryPasswordAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, GetAccountBaseUrl() + "/DeliveryPassword");
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Delivery password clear failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Delivery password clear failed: {response.StatusCode}");
        }
    }

    public async Task StartDeliveryAddressAsync(string address)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GetAccountBaseUrl() + "/DeliveryAddress")
        {
            Content = JsonContent.Create(new SaveDeliveryAddressRequest(address))
        };
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Delivery address start failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException(DeliveryAddressError(ExtractError(error), response.StatusCode));
        }
    }

    public async Task ConfirmDeliveryAddressAsync(string code)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GetAccountBaseUrl() + "/DeliveryAddress/Confirm")
        {
            Content = JsonContent.Create(new ConfirmDeliveryAddressRequest(code))
        };
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Delivery address confirm failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException(DeliveryAddressError(ExtractError(error), response.StatusCode));
        }
    }

    public async Task UseSignedInDeliveryAddressAsync()
    {
        // No body: the address is the token's own, and the server refuses a
        // request that names one.
        using var request = new HttpRequestMessage(HttpMethod.Post, GetAccountBaseUrl() + "/DeliveryAddress/UseSignedIn");
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Delivery address from sign-in failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException(DeliveryAddressError(ExtractError(error), response.StatusCode));
        }
    }

    public async Task ClearDeliveryAddressAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, GetAccountBaseUrl() + "/DeliveryAddress");
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Delivery address clear failed with status {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Delivery address clear failed: {response.StatusCode}");
        }
    }

    /// <summary>#486: the server's named reasons, in the user's words.</summary>
    internal static string DeliveryAddressError(string? error, System.Net.HttpStatusCode status) => error switch
    {
        "wrong" => "That code is not right. Check the email and try again.",
        "expired" => "That code has expired. Send a new one.",
        "too-many-attempts" => "Too many attempts. Send a new code.",
        "none" => "No code is pending. Send one first.",
        "code-recently-sent" => "A code was sent a moment ago. Check your email, or wait a minute to send another.",
        "code-not-sent" => "The code could not be sent. Try again in a moment.",
        "delivery-not-configured" => "Email delivery is not configured on this deployment.",
        // #517
        "personal-account" => "A personal Microsoft account confirms its address with a code; send one to the address above.",
        "no-signed-in-email" => "Your sign-in carries no email address the app can deliver to; send a code to an address instead.",
        "address-in-body" => "The signed-in address is taken from your sign-in, never from the request.",
        null => $"The request failed: {status}",
        _ => error
    };

    private static string? ExtractError(string body)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) ? error.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public async Task<AccountSettingResponse?> GetSettingAsync(string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GetSettingUrl(key));
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Account setting endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Account setting endpoint failed: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<AccountSettingResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize account setting response.");
    }

    public async Task SaveSettingAsync(string key, string value, string contentType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, GetSettingUrl(key))
        {
            Content = JsonContent.Create(new SaveAccountSettingRequest(value, contentType))
        };
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Account setting save endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Account setting save endpoint failed: {response.StatusCode}");
        }
    }

    public async Task DeleteSettingAsync(string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, GetSettingUrl(key));
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Account setting delete endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Account setting delete endpoint failed: {response.StatusCode}");
        }
    }

    public async Task<AccountJobsResponse> GetJobsAsync(int limit = 20, string? continuationToken = null)
    {
        var jobsUrl = GetJobsUrl();
        var query = $"?limit={limit}";

        if (!string.IsNullOrEmpty(continuationToken))
        {
            query += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, jobsUrl + query);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Account jobs endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Account jobs endpoint failed: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<AccountJobsResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize account jobs response.");
    }

    public async Task<AccountUsageResponse> GetUsageAsync(string from, string to)
    {
        var usageUrl = GetAccountBaseUrl() + "/Usage"
            + $"?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, usageUrl);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError(
                "Account usage endpoint failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Account usage endpoint failed: {response.StatusCode}");
        }

        return await response.Content.ReadFromJsonAsync<AccountUsageResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize account usage response.");
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

    private string GetSettingUrl(string key)
    {
        var accountUrl = GetAccountBaseUrl();
        return accountUrl + "/Settings/" + Uri.EscapeDataString(key);
    }

    private string GetJobsUrl()
    {
        var accountUrl = GetAccountBaseUrl();
        return accountUrl + "/Jobs";
    }

    private string GetAccountBaseUrl() => _locations.Url(ApiRoutes.Account);
}
