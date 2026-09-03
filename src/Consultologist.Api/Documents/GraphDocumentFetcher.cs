using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Documents;

/// <summary>
/// One fetch attempt's outcome: bytes, or a kebab-case refusal word for the
/// flat error wire. Never both.
/// </summary>
public sealed record GraphDocumentFetchOutcome(byte[]? Content, string? Name, string? Refusal)
{
    public static GraphDocumentFetchOutcome Fetched(byte[] content, string? name) => new(content, name, null);
    public static GraphDocumentFetchOutcome Refused(string refusal) => new(null, null, refusal);
}

public static class GraphDocumentRefusals
{
    /// <summary>The URL is not a OneDrive/SharePoint link Graph can resolve.</summary>
    public const string NotOneDrive = "link-not-onedrive";

    /// <summary>Graph answered 404/410 — deleted, expired, or never shared.</summary>
    public const string NotFound = "link-not-found";

    /// <summary>Graph answered 401/403 — the signed-in clinician cannot read that item.</summary>
    public const string Forbidden = "link-forbidden";

    /// <summary>The item's declared size exceeds the parser's cap — refused before download.</summary>
    public const string TooLarge = "link-too-large";

    /// <summary>Anything else on the Graph side — its own word, never a generic 500.</summary>
    public const string FetchFailed = "link-fetch-failed";
}

public interface IGraphDocumentFetcher
{
    /// <summary>
    /// Fetch a shared item's bytes as the signed-in clinician: the caller
    /// hands in the OBO-exchanged Graph token (#615); this class never
    /// exchanges, stores, or logs it.
    /// </summary>
    Task<GraphDocumentFetchOutcome> FetchAsync(string accessToken, string url, CancellationToken cancellationToken);
}

/// <summary>
/// #615's proving capability: a OneDrive/SharePoint sharing link becomes a
/// document. Raw Graph REST (repo idiom — no SDK): the sharing URL becomes
/// a share id, the driveItem's declared size gates BEFORE the download
/// (the parser's own 10 MB cap, enforced where it is cheapest), and the
/// bytes go to the same parser an upload does — the #234 criterion holds:
/// the parser learns nothing.
/// </summary>
public sealed class GraphDocumentFetcher : IGraphDocumentFetcher
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";

    /// <summary>
    /// The hosts Graph's /shares can resolve — the Microsoft members of
    /// InputContent's cloud-link list (#291). Google Drive, Dropbox and the
    /// rest stay what they always were at intake: a refusal naming a file
    /// we cannot open — this capability opens exactly the Microsoft ones.
    /// Host-suffix match with a dot boundary (the allow-list lesson: a
    /// suffix without the boundary admits evil.example-sharepoint.com).
    /// </summary>
    private static readonly string[] GraphShareHosts =
    {
        "sharepoint.com",
        "1drv.ms",
        "onedrive.live.com"
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphDocumentFetcher> _logger;

    public GraphDocumentFetcher(IHttpClientFactory httpClientFactory, ILogger<GraphDocumentFetcher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>#615: is this a link the capability can open at all? Extracted so it can be asserted directly.</summary>
    internal static bool IsGraphShareLink(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return GraphShareHosts.Any(host =>
            string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The sharing-URL-to-share-id encoding Graph defines: "u!" + unpadded base64url of the URL.</summary>
    internal static string ShareIdOf(string url) =>
        "u!" + Convert.ToBase64String(Encoding.UTF8.GetBytes(url))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>The size gate, judged from the driveItem metadata. Extracted so it can be asserted directly.</summary>
    internal static string? SizeRefusalOf(long? declaredSize) =>
        declaredSize > DocumentExtraction.MaxBytes ? GraphDocumentRefusals.TooLarge : null;

    public async Task<GraphDocumentFetchOutcome> FetchAsync(string accessToken, string url, CancellationToken cancellationToken)
    {
        if (!IsGraphShareLink(url))
        {
            return GraphDocumentFetchOutcome.Refused(GraphDocumentRefusals.NotOneDrive);
        }

        var shareId = ShareIdOf(url);
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        // Metadata first: the declared size gates before any content moves.
        using var metadataRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{GraphBase}/shares/{shareId}/driveItem?$select=size,name");
        metadataRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var metadata = await client.SendAsync(metadataRequest, cancellationToken);

        if (RefusalFor(metadata.StatusCode) is { } metadataRefusal)
        {
            _logger.LogWarning(
                "Graph share fetch refused at metadata. Refusal={Refusal}, Status={Status}",
                metadataRefusal, (int)metadata.StatusCode);
            return GraphDocumentFetchOutcome.Refused(metadataRefusal);
        }

        long? size = null;
        string? name = null;
        try
        {
            using var document = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("size", out var s) && s.TryGetInt64(out var parsed))
            {
                size = parsed;
            }

            // The item's name goes back to the CLIENT for its label — the
            // same place an upload's filename lives; it is never logged here.
            if (document.RootElement.TryGetProperty("name", out var n))
            {
                name = n.GetString();
            }
        }
        catch (JsonException)
        {
            return GraphDocumentFetchOutcome.Refused(GraphDocumentRefusals.FetchFailed);
        }

        if (SizeRefusalOf(size) is { } sizeRefusal)
        {
            _logger.LogWarning("Graph share fetch refused: item exceeds the parser's cap. Size={Size}", size);
            return GraphDocumentFetchOutcome.Refused(sizeRefusal);
        }

        using var contentRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{GraphBase}/shares/{shareId}/driveItem/content");
        contentRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var content = await client.SendAsync(contentRequest, cancellationToken);

        if (RefusalFor(content.StatusCode) is { } contentRefusal)
        {
            _logger.LogWarning(
                "Graph share fetch refused at content. Refusal={Refusal}, Status={Status}",
                contentRefusal, (int)content.StatusCode);
            return GraphDocumentFetchOutcome.Refused(contentRefusal);
        }

        var bytes = await content.Content.ReadAsByteArrayAsync(cancellationToken);

        // The declared size is the item's claim; the bytes are the fact.
        // Both gates hold — a mis-declared item cannot smuggle past the cap.
        if (bytes.Length > DocumentExtraction.MaxBytes)
        {
            return GraphDocumentFetchOutcome.Refused(GraphDocumentRefusals.TooLarge);
        }

        return GraphDocumentFetchOutcome.Fetched(bytes, name);
    }

    /// <summary>Graph statuses, judged. Extracted so it can be asserted directly.</summary>
    internal static string? RefusalFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => null,
        HttpStatusCode.NotFound or HttpStatusCode.Gone => GraphDocumentRefusals.NotFound,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => GraphDocumentRefusals.Forbidden,
        _ => GraphDocumentRefusals.FetchFailed
    };
}
