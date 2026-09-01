using System.Globalization;
using System.Net;
using System.Text.Json;
using Consultologist.Api.Auth;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Forms;

/// <summary>
/// #539 (forms-intake-spike.md § 4.1, § 6 E4): the held-responses door — a
/// Power Automate flow pushes a form response and the API holds it for the
/// account the token resolves; the body names no account. Organisation
/// sign-in required (#517's rule); every status answers JSON (E4: the
/// connector reports any non-JSON body as an error, so the shared body-less
/// 401/403 helpers are not used here). Values are kept as the strings sent
/// and never logged — logs carry ids, counts, lengths and outcomes only.
/// </summary>
public sealed class FormsIntake
{
    internal const int MaxIdLength = 128;
    internal const int MaxInputs = 64;
    internal const int MaxValueLength = 256 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IAccountAuthorizer _authorizer;
    private readonly IAccountStore _accounts;
    private readonly IFormResponseBlobStore _blobs;
    private readonly IFormResponseStore _rows;
    private readonly ILogger<FormsIntake> _logger;

    public FormsIntake(
        IAccountAuthorizer authorizer,
        IAccountStore accounts,
        IFormResponseBlobStore blobs,
        IFormResponseStore rows,
        ILogger<FormsIntake> logger)
    {
        _authorizer = authorizer;
        _accounts = accounts;
        _blobs = blobs;
        _rows = rows;
        _logger = logger;
    }

    /// <summary>The wire body. ResponseId rides as JSON so 17 and "17" both land (the flow sends a number).</summary>
    public sealed record FormResponseSubmission(
        string? FormId,
        JsonElement? ResponseId,
        DateTimeOffset? SubmittedAtUtc,
        Dictionary<string, string>? Inputs);

    /// <summary>
    /// One function for both verbs: the Functions host does not route two
    /// functions sharing a template by method — the second silently loses
    /// and answers the host's bare 404, the exact E4 failure. Found live.
    /// </summary>
    [Function("FormsIntakeResponses")]
    public async Task<HttpResponseData> ResponsesAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "get", "options", Route = "Intake/Forms/Responses")] HttpRequestData req)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var options = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, options);
            return options;
        }

        var (account, refusal) = await AuthorizeOrganisationAsync(req, cancellationToken);
        if (refusal != null)
        {
            return refusal;
        }

        if (string.Equals(req.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            var rows = await _rows.ListAsync(account!.AppUserId, cancellationToken);
            var list = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, list);
            await list.WriteAsJsonAsync(new { responses = rows.Select(ResponseOf).ToList() }, cancellationToken);
            return list;
        }

        FormResponseSubmission? submission;
        try
        {
            submission = JsonSerializer.Deserialize<FormResponseSubmission>(
                await new StreamReader(req.Body).ReadToEndAsync(cancellationToken), JsonOptions);
        }
        catch (JsonException)
        {
            return await ErrorAsync(req, HttpStatusCode.BadRequest, "Request body is not valid JSON.", cancellationToken);
        }

        var responseId = NormalizeResponseId(submission?.ResponseId);
        if (ValidateSubmission(submission, responseId) is { } validationError)
        {
            return await ErrorAsync(req, HttpStatusCode.BadRequest, validationError, cancellationToken);
        }

        var formId = submission!.FormId!;

        // Idempotent: the flow may retry, and a row — held or already
        // swept — means this response was taken. Swept values are never
        // resurrected by a late retry.
        var existing = await _rows.TryGetAsync(account!.AppUserId, formId, responseId!, cancellationToken);
        if (existing != null)
        {
            _logger.LogInformation(
                "Form response already held. FormId={FormId}, ResponseId={ResponseId}, Deleted={Deleted}",
                formId, responseId, existing.DeletedAtUtc != null);
            return NoContent(req);
        }

        var payload = new FormResponsePayload(
            FormResponsePayload.CurrentVersion, formId, responseId!, submission.SubmittedAtUtc!.Value,
            submission.Inputs!);

        // The write is the point: a blob failure refuses (JSON, per E4) and
        // writes no row, so the flow's retry is clean.
        FormResponseBlobPointer pointer;
        try
        {
            var accountKind = await _accounts.GetAccountKindAsync(account.AppUserId, cancellationToken);
            pointer = await _blobs.WriteAsync(accountKind, account.AppUserId, payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Form response could not be held. FormId={FormId}, ResponseId={ResponseId}", formId, responseId);
            return await ErrorAsync(req, HttpStatusCode.InternalServerError, "The response could not be held; please retry.", cancellationToken);
        }

        await _rows.UpsertAsync(new FormResponseRow(
            account.AppUserId, formId, responseId!, submission.SubmittedAtUtc.Value,
            submission.Inputs!.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList(),
            pointer.Container, pointer.Name, DeletedAtUtc: null), cancellationToken);

        _logger.LogInformation(
            "Form response held. FormId={FormId}, ResponseId={ResponseId}, InputCount={InputCount}, TotalLength={TotalLength}",
            formId, responseId, submission.Inputs!.Count, submission.Inputs.Values.Sum(v => (long)v.Length));

        var created = req.CreateResponse(HttpStatusCode.Created);
        FunctionCors.Apply(req, created);
        await created.WriteAsJsonAsync(new { formId, responseId, submittedAtUtc = submission.SubmittedAtUtc.Value }, cancellationToken);
        return created;
    }

    [Function("FormsIntakeDiscard")]
    public async Task<HttpResponseData> DiscardAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", "options", Route = "Intake/Forms/Responses/{formId}/{responseId}")] HttpRequestData req,
        string formId,
        string responseId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var options = req.CreateResponse(HttpStatusCode.OK);
            FunctionCors.Apply(req, options);
            return options;
        }

        var (account, refusal) = await AuthorizeOrganisationAsync(req, cancellationToken);
        if (refusal != null)
        {
            return refusal;
        }

        var row = await _rows.TryGetAsync(account!.AppUserId, formId, responseId, cancellationToken);
        if (row == null)
        {
            return await ErrorAsync(req, HttpStatusCode.NotFound, "No such held response.", cancellationToken);
        }

        if (row.DeletedAtUtc != null)
        {
            return NoContent(req);
        }

        // The sweep's own order: blob first, then the stamp — a failure
        // persists nothing and the discard can be retried.
        await _blobs.DeleteAsync(new FormResponseBlobPointer(row.BlobContainer, row.BlobName), cancellationToken);
        await _rows.MarkDeletedAsync(account.AppUserId, formId, responseId, DateTimeOffset.UtcNow, cancellationToken);

        _logger.LogInformation("Form response discarded. FormId={FormId}, ResponseId={ResponseId}", formId, responseId);
        return NoContent(req);
    }

    /// <summary>The list row on the wire — ids and days, never a value.</summary>
    internal static object ResponseOf(FormResponseRow row) => new
    {
        formId = row.FormId,
        responseId = row.ResponseId,
        submittedAtUtc = row.SubmittedAtUtc,
        inputIds = row.InputIds,
        deletedAtUtc = row.DeletedAtUtc
    };

    /// <summary>17 and "17" both land; anything else is null and refused by name.</summary>
    internal static string? NormalizeResponseId(JsonElement? responseId) => responseId switch
    {
        { ValueKind: JsonValueKind.String } element => element.GetString(),
        { ValueKind: JsonValueKind.Number } element => element.GetRawText(),
        _ => null
    };

    /// <summary>Every refusal names the field and the rule. Extracted so it can be asserted directly.</summary>
    internal static string? ValidateSubmission(FormResponseSubmission? submission, string? responseId)
    {
        if (submission == null)
        {
            return "Request body is required.";
        }

        if (IdError("formId", submission.FormId) is { } formIdError)
        {
            return formIdError;
        }

        if (IdError("responseId", responseId) is { } responseIdError)
        {
            return responseIdError;
        }

        if (submission.SubmittedAtUtc == null)
        {
            return "submittedAtUtc is required, as an ISO-8601 instant.";
        }

        if (submission.Inputs is not { Count: > 0 })
        {
            return "inputs is required and must carry at least one value.";
        }

        if (submission.Inputs.Count > MaxInputs)
        {
            return $"inputs may carry at most {MaxInputs} values.";
        }

        foreach (var (id, value) in submission.Inputs)
        {
            if (IdError("an input id", id) is { } inputIdError)
            {
                return inputIdError;
            }

            if (value == null)
            {
                return $"input '{id}' carries no value — values are strings.";
            }

            if (value.Length > MaxValueLength)
            {
                // Refused, never truncated — the #428 rule.
                return $"input '{id}' is longer than {MaxValueLength} characters.";
            }
        }

        return null;
    }

    private static string? IdError(string field, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return $"{field} is required.";
        }

        if (id.Length > MaxIdLength)
        {
            return $"{field} is longer than {MaxIdLength} characters.";
        }

        // Row-key- and blob-name-safe; anything else is refused by name.
        return id.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-')
            ? null
            : $"{field} may carry letters, digits, '.', '_' and '-' only.";
    }

    private async Task<(AppAccount? Account, HttpResponseData? Refusal)> AuthorizeOrganisationAsync(
        HttpRequestData req, CancellationToken cancellationToken)
    {
        var authorized = await _authorizer.AuthorizeWithUserAsync(req, cancellationToken);

        if (authorized == null)
        {
            // E4: JSON on every status — the shared 401 helper writes no body.
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            FunctionCors.Apply(req, unauthorized);
            unauthorized.Headers.Add("WWW-Authenticate", "Bearer");
            await unauthorized.WriteAsJsonAsync(new { error = "unauthorized" }, cancellationToken);
            return (null, unauthorized);
        }

        if (RefusalWordFor(authorized.Account, authorized.User) is { } word)
        {
            return (null, await ErrorAsync(req, HttpStatusCode.Forbidden, word, cancellationToken));
        }

        return (authorized.Account, null);
    }

    /// <summary>
    /// The door's 403 word, or null when the caller may hold. #517's rule:
    /// an organisation's sign-in vouches for the mailbox the flow runs as;
    /// a personal token is refused by its word. Extracted so it can be
    /// asserted directly.
    /// </summary>
    internal static string? RefusalWordFor(AppAccount account, AuthenticatedUser user) =>
        !AccountAuthorizer.CanUseApp(account) ? "account-not-active"
        : !DeliveryAddress.IsOrganisation(user) ? "personal-account"
        : null;

    private static HttpResponseData NoContent(HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.NoContent);
        FunctionCors.Apply(req, response);
        return response;
    }

    private static async Task<HttpResponseData> ErrorAsync(
        HttpRequestData req, HttpStatusCode statusCode, string error, CancellationToken cancellationToken)
    {
        var response = req.CreateResponse(statusCode);
        FunctionCors.Apply(req, response);
        await response.WriteAsJsonAsync(new { error }, cancellationToken);
        return response;
    }
}
