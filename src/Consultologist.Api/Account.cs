using System.Net;
using System.Text.Json;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Consultologist.Api;

public sealed class Account
{
    private const int MaxSettingKeyLength = 128;
    private const int MaxSettingValueLength = 32_000;
    private const int MaxContentTypeLength = 128;
    private const int DefaultJobsLimit = 20;
    private const int MaxJobsLimit = 50;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly IAccountAuthorizer _authorizer;
    private readonly IAccountSettingsStore _settingsStore;
    private readonly IConsultGenerationJobIndexStore _jobIndexStore;

    public Account(
        IAccountAuthorizer authorizer,
        IAccountSettingsStore settingsStore,
        IConsultGenerationJobIndexStore jobIndexStore)
    {
        _authorizer = authorizer;
        _settingsStore = settingsStore;
        _jobIndexStore = jobIndexStore;
    }

    [Function("AccountMe")]
    public async Task<HttpResponseData> GetMeAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "Account/Me")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        // No IsActive gate: a Pending/Disabled user may still read their own
        // profile so the client can explain why the rest of the API is 403.
        var deliveryPassword = await _settingsStore.GetAsync(
            account.AppUserId,
            AccountSettingKeys.DeliveryPassword,
            cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(
            new AccountMeResponse(
                account.AppUserId,
                account.DisplayName,
                account.Email,
                account.Status,
                account.CurrentIdentity,
                account.LinkedIdentities,
                DocumentPasswordSet: deliveryPassword != null),
            cancellationToken);

        return response;
    }

    // #159: the delivery password is write-only — set/clear through these
    // endpoints, existence surfaced only as Account/Me's DocumentPasswordSet.
    [Function("AccountDeliveryPasswordSave")]
    public async Task<HttpResponseData> SaveDeliveryPasswordAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", "options", Route = "Account/DeliveryPassword")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        SaveDeliveryPasswordRequest? request = null;

        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
            {
                request = JsonSerializer.Deserialize<SaveDeliveryPasswordRequest>(body, JsonOptions);
            }
        }
        catch (JsonException)
        {
            return await CreateErrorResponseAsync(req, "Malformed JSON request body.", cancellationToken);
        }

        var validationError = ValidateDeliveryPassword(request?.Password);

        if (validationError != null)
        {
            return await CreateErrorResponseAsync(req, validationError, cancellationToken);
        }

        await _settingsStore.SaveAsync(
            account.AppUserId,
            AccountSettingKeys.DeliveryPassword,
            request!.Password!,
            "text/plain",
            cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    [Function("AccountDeliveryPasswordDelete")]
    public async Task<HttpResponseData> DeleteDeliveryPasswordAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Account/DeliveryPassword")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        await _settingsStore.DeleteAsync(account.AppUserId, AccountSettingKeys.DeliveryPassword, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    private async Task<HttpResponseData> CreateErrorResponseAsync(
        HttpRequestData req,
        string error,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(HttpStatusCode.BadRequest);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(new { error }, cancellationToken);
        return response;
    }

    // 16-char minimum by decision (#159): an emailed attachment can be
    // brute-forced offline with no rate limiting, so length is the defense.
    internal const int MinDeliveryPasswordLength = 16;
    internal const int MaxDeliveryPasswordLength = 128;

    internal static string? ValidateDeliveryPassword(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return "Password is required.";
        }

        if (password.Length < MinDeliveryPasswordLength)
        {
            return $"Password must be at least {MinDeliveryPasswordLength} characters.";
        }

        if (password.Length > MaxDeliveryPasswordLength)
        {
            return "Password is too long.";
        }

        return null;
    }

    internal static bool IsSecretSettingKey(string key) =>
        string.Equals(key, AccountSettingKeys.DeliveryPassword, StringComparison.Ordinal);

    [Function("AccountJobsList")]
    public async Task<HttpResponseData> GetJobsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Account/Jobs")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var (limit, continuationToken) = ParseJobsQueryParams(req.Url);

        var (jobs, nextToken) = await _jobIndexStore.ListAsync(
            account.AppUserId,
            limit,
            continuationToken,
            cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(
            new AccountJobsResponse(
                jobs.Select(j => new AccountJobSummaryResponse(
                    j.JobId,
                    j.Status,
                    j.CreatedAtUtc,
                    j.StartedAtUtc,
                    j.CompletedAtUtc,
                    j.TotalBlockCount,
                    j.CompletedBlockCount,
                    j.FailedBlockCount,
                    j.Source,
                    j.ScheduledAtUtc)).ToArray(),
                nextToken),
            cancellationToken);

        return response;
    }

    [Function("AccountSettingGet")]
    public async Task<HttpResponseData> GetSettingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "Account/Settings/{key}")] HttpRequestData req,
        string key)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        var validationError = ValidateSettingKey(key);
        if (validationError != null)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.BadRequest, validationError, cancellationToken);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);
        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var setting = await _settingsStore.GetAsync(account.AppUserId, key, cancellationToken);
        if (setting == null)
        {
            return CreateNoContentLikeResponse(req, HttpStatusCode.NotFound);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(
            new AccountSettingResponse(setting.Key, setting.Value, setting.ContentType, setting.UpdatedAtUtc),
            cancellationToken);

        return response;
    }

    [Function("AccountSettingSave")]
    public async Task<HttpResponseData> SaveSettingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "Account/Settings/{key}")] HttpRequestData req,
        string key)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        var validationError = ValidateSettingKey(key);
        if (validationError != null)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.BadRequest, validationError, cancellationToken);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);
        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        SaveAccountSettingRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<SaveAccountSettingRequest>(
                req.Body,
                JsonOptions,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.BadRequest, "Invalid setting payload.", cancellationToken);
        }

        if (request?.Value == null)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.BadRequest, "Setting value is required.", cancellationToken);
        }

        if (request.Value.Length > MaxSettingValueLength)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.RequestEntityTooLarge, "Setting value is too large.", cancellationToken);
        }

        var contentType = string.IsNullOrWhiteSpace(request.ContentType)
            ? "text/plain"
            : request.ContentType.Trim();

        if (contentType.Length > MaxContentTypeLength)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.BadRequest, "Setting content type is too long.", cancellationToken);
        }

        await _settingsStore.SaveAsync(account.AppUserId, key, request.Value, contentType, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    [Function("AccountSettingDelete")]
    public async Task<HttpResponseData> DeleteSettingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Account/Settings/{key}")] HttpRequestData req,
        string key)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        var validationError = ValidateSettingKey(key);
        if (validationError != null)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.BadRequest, validationError, cancellationToken);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);
        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        await _settingsStore.DeleteAsync(account.AppUserId, key, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    [Function("AccountSettingOptions")]
    public HttpResponseData OptionsSettingAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "Account/Settings/{key}")] HttpRequestData req,
        string key)
    {
        return CreateOptionsResponse(req);
    }

    private static (int Limit, string? ContinuationToken) ParseJobsQueryParams(Uri url)
    {
        var query = url.Query.TrimStart('?');
        var limit = DefaultJobsLimit;
        string? continuationToken = null;

        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = segment.IndexOf('=');
            if (eq < 0) continue;

            var key = Uri.UnescapeDataString(segment[..eq]);
            var value = Uri.UnescapeDataString(segment[(eq + 1)..]);

            if (string.Equals(key, "limit", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(value, out var parsed))
            {
                limit = Math.Clamp(parsed, 1, MaxJobsLimit);
            }
            else if (string.Equals(key, "continuationToken", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(value))
            {
                continuationToken = value;
            }
        }

        return (limit, continuationToken);
    }

    private static string? ValidateSettingKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Setting key is required.";
        }

        if (key.Length > MaxSettingKeyLength)
        {
            return "Setting key is too long.";
        }

        // Secret keys never travel the generic settings routes — reads would
        // leak them and writes would bypass strength validation (#159).
        if (IsSecretSettingKey(key))
        {
            return "This setting is managed through its dedicated endpoint.";
        }

        foreach (var character in key)
        {
            if (char.IsLetterOrDigit(character) ||
                character is '.' or '_' or '-' or ':')
            {
                continue;
            }

            return "Setting key contains unsupported characters.";
        }

        return null;
    }

    private static HttpResponseData CreateOptionsResponse(HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        FunctionCors.Apply(req, response);
        return response;
    }

    private static HttpResponseData CreateNoContentLikeResponse(HttpRequestData req, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        FunctionCors.Apply(req, response);
        return response;
    }

    private static async Task<HttpResponseData> CreateTextResponseAsync(
        HttpRequestData req,
        HttpStatusCode statusCode,
        string message,
        CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(statusCode);
        FunctionCors.Apply(req, response);
        await response.WriteStringAsync(message, cancellationToken);
        return response;
    }
}

public sealed record AccountJobSummaryResponse(
    string JobId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int TotalBlockCount,
    int CompletedBlockCount,
    int FailedBlockCount,
    string? Source = null,
    DateTimeOffset? ScheduledAtUtc = null);

public sealed record AccountJobsResponse(
    IReadOnlyList<AccountJobSummaryResponse> Jobs,
    string? ContinuationToken);
