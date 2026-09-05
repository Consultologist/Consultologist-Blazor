using System.Globalization;
using System.Net;
using Consultologist.Api.Auth;
using Consultologist.Api.RateLimiting;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Documents;

/// <summary>
/// Reads a document and hands the text back for the clinician to look at
/// before it becomes a consult (#235, docs/DOCUMENT_INPUT.md § 5).
///
/// A preview, and only a preview: it persists nothing and creates nothing.
/// The authoritative extraction happens again at job start (#238) over the
/// file the request carries, so what the server runs on is what the server
/// read — never what a client asserted. Extraction is deterministic for the
/// same bytes and the same pinned extractor, which is what makes this
/// preview honest rather than indicative.
///
/// The filename never arrives here. The client keeps it for its own label;
/// a filename can itself be PHI ("Smith_John_referral.pdf") and a
/// request-scoped one would land in Functions request logging.
/// </summary>
public sealed class DocumentExtractions
{
    private readonly ILogger<DocumentExtractions> _logger;
    private readonly IAccountAuthorizer _authorizer;
    private readonly IAccountRateLimiter _rateLimiter;
    private readonly IOnBehalfOfTokenClient _onBehalfOf;
    private readonly IGraphDocumentFetcher _graphFetcher;
    private readonly IDocumentOcr _ocr;
    private readonly IAccountSettingsStore _settingsStore;

    public DocumentExtractions(
        ILogger<DocumentExtractions> logger,
        IAccountAuthorizer authorizer,
        IAccountRateLimiter rateLimiter,
        IOnBehalfOfTokenClient onBehalfOf,
        IGraphDocumentFetcher graphFetcher,
        IDocumentOcr ocr,
        IAccountSettingsStore settingsStore)
    {
        _logger = logger;
        _authorizer = authorizer;
        _rateLimiter = rateLimiter;
        _onBehalfOf = onBehalfOf;
        _graphFetcher = graphFetcher;
        _ocr = ocr;
        _settingsStore = settingsStore;
    }

    // #239: the caller's OCR confidence policy — the minimum a scan's read must
    // clear, or null when the gate is off. Read per request so the preview
    // matches what job start will do for the same account.
    private async Task<double?> OcrMinConfidenceAsync(string appUserId, CancellationToken cancellationToken)
    {
        var gate = await _settingsStore.GetAsync(appUserId, AccountSettingKeys.OcrConfidenceGate, cancellationToken);
        var min = await _settingsStore.GetAsync(appUserId, AccountSettingKeys.OcrMinConfidence, cancellationToken);
        return OcrConfidenceSettings.EffectiveMinConfidence(gate?.Value, min?.Value);
    }

    [Function("CreateDocumentExtraction")]
    public async Task<HttpResponseData> CreateAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "DocumentExtractions")] HttpRequestData req)
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

        // #266: before the body is read, not after. Buffering up to 10 MB is
        // itself a cost this bounds, and there is nothing to learn from the
        // bytes of a request we have already decided to refuse.
        var decision = await _rateLimiter.AcquireOrAllowAsync(account.AppUserId, _logger, cancellationToken);

        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "Document extraction refused: the account is over its submission limit. Limit={Limit}",
                decision.Limit);

            // No `outcome` field. Every value in that vocabulary describes
            // what became of an attempt to read a document, and no attempt
            // was made — claiming one here would be a lie in the one place
            // the client reads to explain itself to a clinician.
            return await CreateJsonResponseAsync(
                req,
                HttpStatusCode.TooManyRequests,
                new
                {
                    error = $"This account has read {decision.Limit} documents in the past hour, which is its limit. "
                        + "Nothing is wrong with this file — please try again shortly."
                },
                cancellationToken,
                decision.RetryAfter);
        }

        using var buffer = new MemoryStream();
        await req.Body.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        var result = await DocumentExtraction.ExtractAsync(
            bytes,
            DocumentExtraction.InteractiveGateWait,
            cancellationToken,
            _ocr,
            await OcrMinConfidenceAsync(account.AppUserId, cancellationToken));

        // Lengths and dispositions only: no bytes, no extracted text, no
        // filename — there is no filename to log.
        _logger.LogInformation(
            "Document extraction {Outcome}. Bytes={Bytes}, Pages={Pages}, Characters={Characters}",
            result.Outcome,
            bytes.Length,
            result.PageCount,
            result.Text?.Length ?? 0);

        if (!DocumentExtraction.Succeeded(result))
        {
            return await CreateJsonResponseAsync(
                req,
                StatusFor(result.Outcome),
                new { error = DocumentExtractionCopy.For(result.Outcome), outcome = result.Outcome },
                cancellationToken);
        }

        return await CreateJsonResponseAsync(
            req,
            HttpStatusCode.OK,
            new DocumentExtractionResponse(result.Text!, result.ExtractorId!, result.PageCount),
            cancellationToken);
    }

    /// <summary>
    /// Refusals are not transport errors. The precedent is
    /// ConsultGenerationJobStartError.InputsMismatch, which returns 422 with
    /// the note that the request was well-formed but unsatisfiable — a
    /// scanned PDF is exactly that shape, and so is a corrupt one.
    /// </summary>
    private static HttpStatusCode StatusFor(string outcome) => outcome switch
    {
        DocumentExtractionOutcomes.UnsupportedType => HttpStatusCode.UnsupportedMediaType,
        DocumentExtractionOutcomes.TooLarge => HttpStatusCode.RequestEntityTooLarge,
        // #241: not 422. The request was well-formed AND satisfiable — we
        // simply had no capacity, and 503 is the one status that says
        // "try the same thing again". #239: an OCR outage is the same shape.
        DocumentExtractionOutcomes.Busy or DocumentExtractionOutcomes.OcrUnavailable =>
            HttpStatusCode.ServiceUnavailable,
        _ => HttpStatusCode.UnprocessableEntity
    };

    /// <summary>
    /// #615's proving capability: a OneDrive/SharePoint sharing link becomes
    /// the document. The server exchanges the caller's own bearer on-behalf-of
    /// (org accounts only — refused by name before any wire), fetches the
    /// bytes via Graph /shares as the clinician, and runs the SAME parser an
    /// upload does; the SPA treats the result exactly like an uploaded file
    /// (origin kind document at job start). A preview, and only a preview —
    /// persists nothing, like its sibling above.
    /// </summary>
    [Function("CreateDocumentExtractionFromLink")]
    public async Task<HttpResponseData> CreateFromLinkAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "DocumentExtractions/FromLink")] HttpRequestData req)
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

        // The same submission budget as an upload: retrieving a document IS
        // reading one, whichever door the bytes arrive through (#266).
        var decision = await _rateLimiter.AcquireOrAllowAsync(authorized.Account.AppUserId, _logger, cancellationToken);

        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "Link document extraction refused: the account is over its submission limit. Limit={Limit}",
                decision.Limit);

            return await CreateJsonResponseAsync(
                req,
                HttpStatusCode.TooManyRequests,
                new
                {
                    error = $"This account has read {decision.Limit} documents in the past hour, which is its limit. "
                        + "Nothing is wrong with this link — please try again shortly."
                },
                cancellationToken,
                decision.RetryAfter);
        }

        DocumentExtractionFromLinkRequest? request;
        try
        {
            request = await req.ReadFromJsonAsync<DocumentExtractionFromLinkRequest>(cancellationToken);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            request = null;
        }

        if (string.IsNullOrWhiteSpace(request?.Url))
        {
            return await CreateJsonResponseAsync(
                req, HttpStatusCode.BadRequest,
                new { error = "The request carries no link." }, cancellationToken);
        }

        // The host gate runs before any token moves: a Google Drive or
        // Dropbox link stays what it always was — a file we cannot open.
        if (!GraphDocumentFetcher.IsGraphShareLink(request.Url))
        {
            return await CreateJsonResponseAsync(
                req, StatusForLink(GraphDocumentRefusals.NotOneDrive),
                new { error = GraphDocumentRefusals.NotOneDrive }, cancellationToken);
        }

        // The raw incoming bearer is the OBO assertion; the authorizer's
        // carrier holds claims only, so the door re-reads its own header.
        var assertion = RawBearerOf(req);

        if (assertion == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        var exchange = await _onBehalfOf.ExchangeAsync(
            authorized.User, assertion, "https://graph.microsoft.com/Files.Read", cancellationToken);

        if (exchange.Refusal != null)
        {
            return await CreateJsonResponseAsync(
                req, StatusForLink(exchange.Refusal),
                new { error = exchange.Refusal }, cancellationToken);
        }

        var fetch = await _graphFetcher.FetchAsync(exchange.AccessToken!, request.Url, cancellationToken);

        if (fetch.Refusal != null)
        {
            return await CreateJsonResponseAsync(
                req, StatusForLink(fetch.Refusal),
                new { error = fetch.Refusal }, cancellationToken);
        }

        var result = await DocumentExtraction.ExtractAsync(
            fetch.Content!,
            DocumentExtraction.InteractiveGateWait,
            cancellationToken,
            _ocr,
            await OcrMinConfidenceAsync(authorized.Account.AppUserId, cancellationToken));

        // Lengths and dispositions only — no bytes, no text, no URL: a
        // sharing link names a file, and a filename can itself be PHI.
        _logger.LogInformation(
            "Link document extraction {Outcome}. Bytes={Bytes}, Pages={Pages}, Characters={Characters}",
            result.Outcome,
            fetch.Content!.Length,
            result.PageCount,
            result.Text?.Length ?? 0);

        if (!DocumentExtraction.Succeeded(result))
        {
            return await CreateJsonResponseAsync(
                req,
                StatusFor(result.Outcome),
                new { error = DocumentExtractionCopy.For(result.Outcome), outcome = result.Outcome },
                cancellationToken);
        }

        // The bytes ride back: the job submits document CONTENT and the
        // server extracts again at start (#238) — a link-fetched document
        // must reach that authoritative pass the same way an upload does.
        return await CreateJsonResponseAsync(
            req,
            HttpStatusCode.OK,
            new DocumentExtractionFromLinkResponse(
                result.Text!, result.ExtractorId!, result.PageCount, fetch.Content!, fetch.Name),
            cancellationToken);
    }

    private static string? RawBearerOf(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("Authorization", out var values))
        {
            return null;
        }

        var header = values.FirstOrDefault();
        return header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    /// <summary>
    /// #615: the link door's refusal words, mapped. The default is 502, not
    /// 500 — every unmapped word is a downstream fact, and a missing arm
    /// must not turn a named refusal into an apparent server fault.
    /// Extracted so it can be asserted directly.
    /// </summary>
    internal static HttpStatusCode StatusForLink(string refusal) => refusal switch
    {
        OnBehalfOfRefusals.PersonalAccount => HttpStatusCode.Forbidden,
        OnBehalfOfRefusals.ConsentRequired => HttpStatusCode.Forbidden,
        GraphDocumentRefusals.NotOneDrive => HttpStatusCode.UnprocessableEntity,
        GraphDocumentRefusals.NotFound => HttpStatusCode.UnprocessableEntity,
        GraphDocumentRefusals.Forbidden => HttpStatusCode.Forbidden,
        GraphDocumentRefusals.TooLarge => HttpStatusCode.RequestEntityTooLarge,
        _ => HttpStatusCode.BadGateway
    };

    private static async Task<HttpResponseData> CreateJsonResponseAsync<T>(
        HttpRequestData req,
        HttpStatusCode statusCode,
        T payload,
        CancellationToken cancellationToken,
        TimeSpan? retryAfter = null)
    {
        var response = req.CreateResponse(statusCode);
        FunctionCors.Apply(req, response);

        if (retryAfter is { } wait)
        {
            response.Headers.Add(
                "Retry-After",
                Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture));
        }

        await response.WriteAsJsonAsync(payload, cancellationToken);
        return response;
    }
}

public sealed record DocumentExtractionResponse(string Text, string Extractor, int? PageCount);

public sealed record DocumentExtractionFromLinkRequest(string? Url);

public sealed record DocumentExtractionFromLinkResponse(string Text, string Extractor, int? PageCount, byte[] Content, string? Name);
