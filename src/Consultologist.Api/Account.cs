using System.Net;
using System.Text.Json;
using Consultologist.Api.Auth;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
    private readonly IGraphMailClient _mail;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<Account> _logger;

    public Account(
        IAccountAuthorizer authorizer,
        IAccountSettingsStore settingsStore,
        IConsultGenerationJobIndexStore jobIndexStore,
        IGraphMailClient mail,
        IConfiguration configuration,
        TimeProvider time,
        ILogger<Account> logger)
    {
        _authorizer = authorizer;
        _settingsStore = settingsStore;
        _jobIndexStore = jobIndexStore;
        _mail = mail;
        _configuration = configuration;
        _time = time;
        _logger = logger;
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

        var authorized = await _authorizer.AuthorizeWithUserAsync(req, cancellationToken);

        if (authorized == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        var account = authorized.Account;

        // No IsActive gate: a Pending/Disabled user may still read their own
        // profile so the client can explain why the rest of the API is 403.
        var deliveryPassword = await _settingsStore.GetAsync(
            account.AppUserId,
            AccountSettingKeys.DeliveryPassword,
            cancellationToken);
        var deliveryAddress = await _settingsStore.GetAsync(
            account.AppUserId,
            AccountSettingKeys.DeliveryAddress,
            cancellationToken);
        var pending = DeliveryAddress.Deserialize(
            (await _settingsStore.GetAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressPending, cancellationToken))?.Value);
        var verifiedBy = await _settingsStore.GetAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressVerifiedBy, cancellationToken);
        // #517: what this token says, never stored — the card offers the
        // one-click choice on an organisation's sign-in and not otherwise.
        var signedIn = DeliveryAddress.SignedInEligibility(authorized.User);

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
                DocumentPasswordSet: deliveryPassword != null,
                DeliveryAddress: deliveryAddress?.Value,
                // An expired code is not "pending" — the profile would tell
                // the user to enter a code that can no longer work.
                DeliveryAddressPending: pending != null && _time.GetUtcNow() < pending.ExpiresAtUtc ? pending.Address : null,
                DeliveryAddressVerifiedBy: deliveryAddress != null ? verifiedBy?.Value : null,
                SignInEmail: signedIn.Address,
                SignInKind: DeliveryAddress.SignInKindOf(authorized.User)),
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

    // #486: the verified delivery address. Set → a code goes to the address;
    // Confirm → the address becomes the account's; Delete → gone. The confirmed
    // address is the only one app-submitted jobs are ever sent to.
    [Function("AccountDeliveryAddressStart")]
    public async Task<HttpResponseData> StartDeliveryAddressAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "Account/DeliveryAddress")] HttpRequestData req)
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

        var mailbox = _configuration["EmailIntake:MailboxAddress"];

        if (string.IsNullOrWhiteSpace(mailbox))
        {
            return await CreateErrorResponseAsync(req, "delivery-not-configured", cancellationToken, HttpStatusCode.ServiceUnavailable);
        }

        SaveDeliveryAddressRequest? request;

        try
        {
            request = await ReadJsonAsync<SaveDeliveryAddressRequest>(req, cancellationToken);
        }
        catch (JsonException)
        {
            return await CreateErrorResponseAsync(req, "Malformed JSON request body.", cancellationToken);
        }

        var validationError = DeliveryAddress.Validate(request?.Address);

        if (validationError != null)
        {
            return await CreateErrorResponseAsync(req, validationError, cancellationToken);
        }

        var now = _time.GetUtcNow();
        var existing = DeliveryAddress.Deserialize(
            (await _settingsStore.GetAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressPending, cancellationToken))?.Value);

        if (existing != null && now - existing.SentAtUtc < DeliveryAddress.ResendInterval)
        {
            return await CreateErrorResponseAsync(req, "code-recently-sent", cancellationToken, HttpStatusCode.TooManyRequests);
        }

        var code = DeliveryAddress.CreateCode();
        var pending = DeliveryAddress.CreatePending(account.AppUserId, request!.Address!, code, now);

        // Row first, mail second: a code that was sent but not recorded can
        // never be confirmed; one recorded but not sent just expires.
        await _settingsStore.SaveAsync(
            account.AppUserId,
            AccountSettingKeys.DeliveryAddressPending,
            DeliveryAddress.Serialize(pending),
            "application/json",
            cancellationToken);

        var (subject, body) = DeliveryAddress.ComposeCodeMessage(code);

        try
        {
            await _mail.SendMailAsync(mailbox, pending.Address, subject, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Status only — the address is the user's, not the log's.
            _logger.LogWarning(ex, "Delivery address code could not be sent. AppUserId={AppUserId}", account.AppUserId);
            await _settingsStore.DeleteAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressPending, cancellationToken);
            return await CreateErrorResponseAsync(req, "code-not-sent", cancellationToken, HttpStatusCode.BadGateway);
        }

        var response = req.CreateResponse(HttpStatusCode.Accepted);
        FunctionCors.Apply(req, response);
        return response;
    }

    [Function("AccountDeliveryAddressConfirm")]
    public async Task<HttpResponseData> ConfirmDeliveryAddressAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "Account/DeliveryAddress/Confirm")] HttpRequestData req)
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

        ConfirmDeliveryAddressRequest? request;

        try
        {
            request = await ReadJsonAsync<ConfirmDeliveryAddressRequest>(req, cancellationToken);
        }
        catch (JsonException)
        {
            return await CreateErrorResponseAsync(req, "Malformed JSON request body.", cancellationToken);
        }

        var pending = DeliveryAddress.Deserialize(
            (await _settingsStore.GetAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressPending, cancellationToken))?.Value);
        var decision = DeliveryAddress.Decide(pending, account.AppUserId, request?.Code, _time.GetUtcNow());

        switch (decision.Outcome)
        {
            case ConfirmOutcome.Confirmed:
                await _settingsStore.SaveAsync(
                    account.AppUserId,
                    AccountSettingKeys.DeliveryAddress,
                    decision.Pending!.Address,
                    "text/plain",
                    cancellationToken);
                // #517: how it was verified — the code, here.
                await _settingsStore.SaveAsync(
                    account.AppUserId,
                    AccountSettingKeys.DeliveryAddressVerifiedBy,
                    DeliveryAddressVerifiedBy.Code,
                    "text/plain",
                    cancellationToken);
                await _settingsStore.DeleteAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressPending, cancellationToken);
                var ok = req.CreateResponse(HttpStatusCode.NoContent);
                FunctionCors.Apply(req, ok);
                return ok;

            case ConfirmOutcome.Wrong:
                await _settingsStore.SaveAsync(
                    account.AppUserId,
                    AccountSettingKeys.DeliveryAddressPending,
                    DeliveryAddress.Serialize(decision.Pending!),
                    "application/json",
                    cancellationToken);
                return await CreateErrorResponseAsync(req, "wrong", cancellationToken);

            case ConfirmOutcome.Expired:
            case ConfirmOutcome.TooManyAttempts:
                await _settingsStore.DeleteAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressPending, cancellationToken);
                return await CreateErrorResponseAsync(
                    req,
                    decision.Outcome == ConfirmOutcome.Expired ? "expired" : "too-many-attempts",
                    cancellationToken);

            default:
                return await CreateErrorResponseAsync(req, "none", cancellationToken);
        }
    }

    [Function("AccountDeliveryAddressDelete")]
    public async Task<HttpResponseData> DeleteDeliveryAddressAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "Account/DeliveryAddress")] HttpRequestData req)
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

        await _settingsStore.DeleteAsync(account.AppUserId, AccountSettingKeys.DeliveryAddress, cancellationToken);
        await _settingsStore.DeleteAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressPending, cancellationToken);
        await _settingsStore.DeleteAsync(account.AppUserId, AccountSettingKeys.DeliveryAddressVerifiedBy, cancellationToken);

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    /// <summary>
    /// #517: a work account takes its own signed-in email as the delivery
    /// address without a code — the organisation's sign-in already verified
    /// that the mailbox is this person's. The token decides, not the account:
    /// the tenant must be an organisation's (a personal Microsoft account is
    /// refused by name and keeps the code), the address is the token's own
    /// email claim, and a body carrying anything is refused so a client cannot
    /// name a different one. The Do-Not stands: the claim becomes a target
    /// only because the user chose it here, and it is written as the verified
    /// address like any other.
    /// </summary>
    [Function("AccountDeliveryAddressUseSignedIn")]
    public async Task<HttpResponseData> UseSignedInDeliveryAddressAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "Account/DeliveryAddress/UseSignedIn")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var optionsResponse = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, optionsResponse);
            return optionsResponse;
        }

        var authorized = await _authorizer.AuthorizeWithUserAsync(req, cancellationToken);

        if (authorized == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(authorized.Account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var body = await new StreamReader(req.Body).ReadToEndAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(body))
        {
            return await CreateErrorResponseAsync(req, "address-in-body", cancellationToken);
        }

        var decision = DeliveryAddress.SignedInEligibility(authorized.User);

        switch (decision.Outcome)
        {
            case SignedInOutcome.PersonalAccount:
                return await CreateErrorResponseAsync(req, "personal-account", cancellationToken, HttpStatusCode.Forbidden);
            case SignedInOutcome.NoEmailClaim:
                return await CreateErrorResponseAsync(req, "no-signed-in-email", cancellationToken);
        }

        var appUserId = authorized.Account.AppUserId;
        await _settingsStore.SaveAsync(appUserId, AccountSettingKeys.DeliveryAddress, decision.Address!, "text/plain", cancellationToken);
        await _settingsStore.SaveAsync(appUserId, AccountSettingKeys.DeliveryAddressVerifiedBy, DeliveryAddressVerifiedBy.Tenant, "text/plain", cancellationToken);
        await _settingsStore.DeleteAsync(appUserId, AccountSettingKeys.DeliveryAddressPending, cancellationToken);

        _logger.LogInformation("Delivery address set from the organisation sign-in. AppUserId={AppUserId}", appUserId);

        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpRequestData req, CancellationToken cancellationToken) where T : class
    {
        var body = await new StreamReader(req.Body).ReadToEndAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private async Task<HttpResponseData> CreateErrorResponseAsync(
        HttpRequestData req,
        string error,
        CancellationToken cancellationToken,
        HttpStatusCode statusCode = HttpStatusCode.BadRequest)
    {
        var response = req.CreateResponse(statusCode);
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

    // #486 widened this from the one secret to every key the generic routes
    // must not touch: the address is not secret, but a route that could write
    // it would be a way to plant an address nobody confirmed.
    internal static bool IsSecretSettingKey(string key) =>
        string.Equals(key, AccountSettingKeys.DeliveryPassword, StringComparison.Ordinal)
        || string.Equals(key, AccountSettingKeys.DeliveryAddress, StringComparison.Ordinal)
        || string.Equals(key, AccountSettingKeys.DeliveryAddressPending, StringComparison.Ordinal)
        // #517: how the address was verified is a claim of trust, not a preference.
        || string.Equals(key, AccountSettingKeys.DeliveryAddressVerifiedBy, StringComparison.Ordinal);

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
                    j.ScheduledAtUtc,
                    j.FailedAtStart,
                    j.TextDroppedAtUtc,
                    j.DeliveryOutcome,
                    j.DeliveredAtUtc,
                    j.Deciding,
                    j.DecisionFailureKind)).ToArray(),
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
    DateTimeOffset? ScheduledAtUtc = null,
    // #434: see ConsultGenerationJobIndexEntry.FailedAtStart.
    bool FailedAtStart = false,
    // #368: when the retention sweep deleted the produced text; null while present.
    DateTimeOffset? TextDroppedAtUtc = null,
    // #486: the completion email's outcome (DeliveryOutcomes) and time.
    string? DeliveryOutcome = null,
    DateTimeOffset? DeliveredAtUtc = null,
    // v10 (#496): still deciding what to produce; and why a job ended in that stage.
    bool Deciding = false,
    string? DecisionFailureKind = null);

public sealed record AccountJobsResponse(
    IReadOnlyList<AccountJobSummaryResponse> Jobs,
    string? ContinuationToken);
