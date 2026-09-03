using Consultologist.Web.Services.Locations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Consultologist.Web.Services.Documents;

/// <summary>
/// Success carries what the parser read; refusal carries the sentence the
/// server wrote for it, rendered verbatim.
///
/// A refusal is a result rather than a failure — a scanned PDF is something
/// the clinician needs told, not an exception. Same shape as
/// <c>WorkflowPublishOutcome</c>, for the same reason.
/// </summary>
public sealed record DocumentExtractionOutcome(
    string? Text,
    string? Extractor,
    int? PageCount,
    string? Error)
{
    public bool Succeeded => Text != null;

    public static DocumentExtractionOutcome Refused(string error) => new(null, null, null, error);
}

public interface IDocumentEndpointService
{
    /// <summary>
    /// Reads a document so the clinician can see what the machine got out of
    /// it before it becomes a consult (#236). A preview only: the file itself
    /// is submitted with the job, and the server extracts it again there, so
    /// what runs is what the server read (docs/DOCUMENT_INPUT.md § 5).
    /// </summary>
    Task<DocumentExtractionOutcome> ExtractAsync(byte[] content, string contentType);

    /// <summary>
    /// #615: a OneDrive/SharePoint sharing link becomes the document — the
    /// server retrieves it as the signed-in clinician (on-behalf-of) and
    /// runs the same parser an upload does. The outcome carries the item's
    /// bytes and name so the slot holds exactly what an upload would.
    /// </summary>
    Task<LinkDocumentOutcome> ExtractFromLinkAsync(string url);
}

/// <summary>The link door's outcome: an upload-shaped document, or the sentence for its refusal.</summary>
public sealed record LinkDocumentOutcome(
    string? Text,
    string? Extractor,
    int? PageCount,
    byte[]? Content,
    string? Name,
    string? Error)
{
    public bool Succeeded => Text != null && Content != null;

    public static LinkDocumentOutcome Refused(string error) => new(null, null, null, null, null, error);
}

public sealed class DocumentEndpointService : IDocumentEndpointService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IApiLocations _locations;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ILogger<DocumentEndpointService> _logger;

    public DocumentEndpointService(
        HttpClient httpClient,
        IConfiguration configuration,
        IApiLocations locations,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        ILogger<DocumentEndpointService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _locations = locations;
        _accessTokenProvider = accessTokenProvider;
        _navigation = navigation;
        _logger = logger;
    }

    public async Task<DocumentExtractionOutcome> ExtractAsync(byte[] content, string contentType)
    {
        var url = _locations.Url(ApiRoutes.DocumentExtractions);
        // The only non-JSON request this client makes. Raw bytes rather than
        // multipart, and no filename: one can itself be PHI, the parser
        // dispatches on content anyway, and a filename on the wire would land
        // in Functions request logging.
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new ByteArrayContent(content)
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        await AddAuthorizationAsync(request);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var extraction = await response.Content.ReadFromJsonAsync<DocumentExtractionResponse>();

            return extraction?.Text == null
                ? DocumentExtractionOutcome.Refused("That file could not be read.")
                : new DocumentExtractionOutcome(extraction.Text, extraction.Extractor, extraction.PageCount, null);
        }

        // The body carries the sentence written for this exact cause
        // (DocumentExtractionCopy). Status and outcome are logged; the text is
        // what the clinician sees. Nothing about the document is logged.
        var body = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Document extraction refused with status {StatusCode}.", response.StatusCode);

        return DocumentExtractionOutcome.Refused(
            ExtractError(body) ?? $"That file could not be read ({(int)response.StatusCode}).");
    }

    public async Task<LinkDocumentOutcome> ExtractFromLinkAsync(string url)
    {
        var target = _locations.Url(ApiRoutes.DocumentExtractions) + "/FromLink";
        using var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = JsonContent.Create(new { url })
        };

        await AddAuthorizationAsync(request);

        var response = await _httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var extraction = await response.Content.ReadFromJsonAsync<DocumentExtractionFromLinkResponse>();

            return extraction?.Text == null || extraction.Content == null
                ? LinkDocumentOutcome.Refused("That link could not be read.")
                : new LinkDocumentOutcome(
                    extraction.Text, extraction.Extractor, extraction.PageCount,
                    extraction.Content, extraction.Name, null);
        }

        var body = await response.Content.ReadAsStringAsync();
        _logger.LogWarning("Link document extraction refused with status {StatusCode}.", response.StatusCode);

        var word = ExtractError(body);
        return LinkDocumentOutcome.Refused(
            LinkErrorSentence(word) ?? $"That link could not be read ({(int)response.StatusCode}).");
    }

    /// <summary>
    /// #615: the server's refusal words, given words a clinician can act on.
    /// A word without a sentence falls through to the generic line — and the
    /// test that every named word HAS a sentence keeps this switch honest.
    /// </summary>
    internal static string? LinkErrorSentence(string? word) => word switch
    {
        "personal-account" => "Retrieving from OneDrive signs in as your organisation account; a personal account uploads the file instead.",
        "obo-consent-required" => "Your organisation has not yet allowed this app to read OneDrive files on your behalf — ask your administrator, or upload the file instead.",
        "obo-exchange-failed" => "Signing in to OneDrive on your behalf failed — try again shortly, or upload the file instead.",
        "link-not-onedrive" => "Only OneDrive and SharePoint sharing links can be retrieved; other links need the file uploaded instead.",
        "link-not-found" => "That link does not point at a file any more — it may have been deleted or the sharing may have expired.",
        "link-forbidden" => "Your account cannot open that file; ask the sender to share it with you, or upload it instead.",
        "link-too-large" => "That file is larger than 10 MB, which is the limit either way it arrives.",
        "link-fetch-failed" => "OneDrive did not hand the file over — try again shortly, or upload it instead.",
        _ => word
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

/// <summary>Mirrors Consultologist.Api.Documents.DocumentExtractionResponse.</summary>
public sealed record DocumentExtractionResponse(string Text, string Extractor, int? PageCount);

/// <summary>Mirrors Consultologist.Api.Documents.DocumentExtractionFromLinkResponse.</summary>
public sealed record DocumentExtractionFromLinkResponse(string Text, string Extractor, int? PageCount, byte[] Content, string? Name);
