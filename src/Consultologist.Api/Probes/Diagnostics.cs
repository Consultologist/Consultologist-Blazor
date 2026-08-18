using System.Net;
using System.Text.Json;
using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Probes;

public sealed class Diagnostics
{
    private const int MaxRequestBodyLength = 4096;
    private const int MaxJobIdLength = 128;
    private const int MaxReasonLength = 64;
    private const int MaxEventIdLength = 256;
    private const int MaxEventTypeLength = 128;
    private const int MaxVisibilityStateLength = 32;
    private const int MaxEventCount = 1_000_000;
    private const long MaxElapsedMs = 24 * 60 * 60 * 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> AllowedReasons = new(StringComparer.Ordinal)
    {
        "completed-via-sse",
        "server-error-event",
        "ended-before-done",
        "timeout",
        "exception",
        "manual-cancel",
        "component-disposed",
        "navigation",
        "polling-fallback-started",
        "polling-fallback-completed"
    };

    private readonly IAccountAuthorizer _authorizer;
    private readonly ILogger<Diagnostics> _logger;

    public Diagnostics(IAccountAuthorizer authorizer, ILogger<Diagnostics> logger)
    {
        _authorizer = authorizer;
        _logger = logger;
    }

    [Function("DiagnosticsSseExit")]
    public async Task<HttpResponseData> SseExitAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "Diagnostics/SseExit")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return CreateEmptyResponse(req, HttpStatusCode.OK);
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

        var requestBody = await new StreamReader(req.Body).ReadToEndAsync(cancellationToken);

        if (requestBody.Length > MaxRequestBodyLength)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.RequestEntityTooLarge, "Diagnostic payload is too large.", cancellationToken);
        }

        SseExitDiagnosticRequest? diagnostic;

        try
        {
            diagnostic = JsonSerializer.Deserialize<SseExitDiagnosticRequest>(requestBody, JsonOptions);
        }
        catch (JsonException)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.BadRequest, "Invalid diagnostic payload.", cancellationToken);
        }

        var validationError = Validate(diagnostic);

        if (validationError != null)
        {
            return await CreateTextResponseAsync(req, HttpStatusCode.BadRequest, validationError, cancellationToken);
        }

        var payload = diagnostic!;

        _logger.LogInformation(
            "Consult generation SSE client exit diagnostic received. JobId={JobId}, AppUserId={AppUserId}, AttemptId={AttemptId}, Reason={Reason}, LatestEventId={LatestEventId}, LatestEventType={LatestEventType}, EventCount={EventCount}, ElapsedMs={ElapsedMs}, PollingFallbackStarted={PollingFallbackStarted}, PollingFallbackCompleted={PollingFallbackCompleted}, DocumentVisibility={DocumentVisibility}, NavigatorOnLine={NavigatorOnLine}, ServiceWorkerControlled={ServiceWorkerControlled}",
            payload.JobId,
            account.AppUserId,
            payload.AttemptId,
            payload.Reason,
            payload.LatestEventId,
            payload.LatestEventType,
            payload.EventCount,
            payload.ElapsedMs,
            payload.PollingFallbackStarted,
            payload.PollingFallbackCompleted,
            payload.DocumentVisibility,
            payload.NavigatorOnLine,
            payload.ServiceWorkerControlled);

        return CreateEmptyResponse(req, HttpStatusCode.Accepted);
    }

    private static string? Validate(SseExitDiagnosticRequest? request)
    {
        if (request == null)
        {
            return "Diagnostic payload is required.";
        }

        if (!IsValidBoundedValue(request.JobId, MaxJobIdLength) || !ContainsOnlySafeIdentifierCharacters(request.JobId))
        {
            return "JobId is invalid.";
        }

        if (!Guid.TryParse(request.AttemptId, out _))
        {
            return "AttemptId is invalid.";
        }

        if (!IsValidBoundedValue(request.Reason, MaxReasonLength) || !AllowedReasons.Contains(request.Reason))
        {
            return "Reason is invalid.";
        }

        if (request.LatestEventId != null
            && (!IsValidBoundedValue(request.LatestEventId, MaxEventIdLength) || !ContainsOnlySafeIdentifierCharacters(request.LatestEventId)))
        {
            return "LatestEventId is invalid.";
        }

        if (request.LatestEventType != null
            && (!IsValidBoundedValue(request.LatestEventType, MaxEventTypeLength) || !ContainsOnlySafeEventTypeCharacters(request.LatestEventType)))
        {
            return "LatestEventType is invalid.";
        }

        if (request.EventCount is < 0 or > MaxEventCount)
        {
            return "EventCount is invalid.";
        }

        if (request.ElapsedMs is < 0 or > MaxElapsedMs)
        {
            return "ElapsedMs is invalid.";
        }

        if (request.DocumentVisibility != null
            && (!IsValidBoundedValue(request.DocumentVisibility, MaxVisibilityStateLength) || !ContainsOnlySafeEventTypeCharacters(request.DocumentVisibility)))
        {
            return "DocumentVisibility is invalid.";
        }

        return null;
    }

    private static bool IsValidBoundedValue(string? value, int maxLength)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;
    }

    private static bool ContainsOnlySafeIdentifierCharacters(string value)
    {
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or ':' or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool ContainsOnlySafeEventTypeCharacters(string value)
    {
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static HttpResponseData CreateEmptyResponse(HttpRequestData req, HttpStatusCode statusCode)
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

public sealed record SseExitDiagnosticRequest(
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
