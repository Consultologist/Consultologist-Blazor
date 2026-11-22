using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Email;

public sealed record GraphMessageRef(
    string Id,
    string? InternetMessageId,
    DateTimeOffset? ReceivedDateTime);

public sealed record GraphInternetMessageHeader(string Name, string Value);

public sealed record GraphMessage(
    string Id,
    string? InternetMessageId,
    string? FromAddress,
    string? BodyText,
    IReadOnlyList<GraphInternetMessageHeader> InternetMessageHeaders,
    // #210: gates the extra attachments call — most messages have none.
    bool HasAttachments = false);

/// <summary>
/// One inbound file attachment. Inline parts (signature logos) are filtered out
/// before this type is constructed: treating them as content would change
/// behaviour for every signed email.
/// </summary>
public sealed record GraphInboundAttachment(
    string Name,
    string ContentType,
    int Size,
    byte[] Content);

/// <summary>
/// What a message's attachment collection actually yielded (#249):
/// <paramref name="Files"/> is what we can read, <paramref name="UnreadableKinds"/>
/// the <c>@odata.type</c> of everything listed that produced no bytes.
///
/// **Kinds, never names.** A filename can itself be PHI
/// ("Smith_John_referral.pdf" — see <see cref="EmailAttachmentInputs"/>), and
/// this list exists to be put in a log line and a reply to the sender. The
/// kind is enough to tell someone what to do differently.
///
/// Inline parts never appear here. A signature logo is deliberately skipped
/// and is not a discrepancy.
/// </summary>
public sealed record GraphAttachmentListing(
    IReadOnlyList<GraphInboundAttachment> Files,
    IReadOnlyList<string> UnreadableKinds);

public interface IGraphMailClient
{
    Task<IReadOnlyList<GraphMessageRef>> ListUnreadInboxMessagesAsync(string mailbox, int top, CancellationToken cancellationToken);

    /// <summary>
    /// Every message in one folder, oldest first — the Queued backlog (#266).
    /// A child folder's messages never appear in the Inbox listing, so this is
    /// a second call rather than a wider one.
    /// </summary>
    Task<IReadOnlyList<GraphMessageRef>> ListFolderMessagesAsync(
        string mailbox,
        string folderId,
        int top,
        CancellationToken cancellationToken);

    /// <summary>Full message with text body and internet headers; null on 404.</summary>
    Task<GraphMessage?> GetMessageAsync(string mailbox, string messageId, CancellationToken cancellationToken);

    /// <summary>
    /// The message's non-inline file attachments (#210), and the kinds of
    /// anything listed that yielded no bytes (#249).
    ///
    /// This used to state as settled fact that "Graph returns contentBytes
    /// base64 in the JSON body, so no raw-bytes path is needed". That holds
    /// for file attachments at the sizes we accept — a 5.7 MB probe inlined
    /// whole — but it was never true of every attachment. An
    /// <c>itemAttachment</c> (a forwarded email) and a
    /// <c>referenceAttachment</c> (a link to a file) carry no
    /// <c>contentBytes</c> at all, and both were being passed over in
    /// silence. The caller decides what to do about that; this only reports
    /// it honestly.
    /// </summary>
    Task<GraphAttachmentListing> ListAttachmentsAsync(string mailbox, string messageId, CancellationToken cancellationToken);

    Task MarkReadAsync(string mailbox, string messageId, CancellationToken cancellationToken);

    /// <summary>Resolves (creating if needed) an Inbox child folder id; memoized per process.</summary>
    Task<string> EnsureInboxChildFolderAsync(string mailbox, string displayName, CancellationToken cancellationToken);

    /// <summary>
    /// The folder id, or null when it does not exist (#266). The read-only
    /// half of <see cref="EnsureInboxChildFolderAsync"/>: polling for a
    /// backlog must not conjure a Queued folder into every mailbox that has
    /// never needed one.
    /// </summary>
    Task<string?> FindInboxChildFolderAsync(string mailbox, string displayName, CancellationToken cancellationToken);

    Task MoveMessageAsync(string mailbox, string messageId, string destinationFolderId, CancellationToken cancellationToken);

    Task SendMailAsync(
        string mailbox,
        string toAddress,
        string subject,
        string textBody,
        CancellationToken cancellationToken,
        IReadOnlyList<GraphMailAttachment>? attachments = null);
}

/// <summary>
/// An inline attachment. #1505 (specVersion 31, area 08; D11): <paramref name="ContentType"/> is the attachment's media
/// type, a PDF's unless said (a 31 bundle's archive is <c>application/zip</c>).
/// </summary>
public sealed record GraphMailAttachment(string Name, byte[] Content, string ContentType = "application/pdf");

/// <summary>
/// Raw Microsoft Graph REST (repo idiom — no Graph SDK) authenticated as the
/// user-assigned managed identity, whose Mail roles are restricted to the
/// consults mailbox by an Exchange application access policy (docs/ACCOUNTS.md).
/// </summary>
public sealed class GraphMailClient : IGraphMailClient
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private static readonly string[] GraphScope = { "https://graph.microsoft.com/.default" };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenCredential _credential;
    private readonly ILogger<GraphMailClient> _logger;
    private readonly Dictionary<string, string> _folderIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _folderLock = new(1, 1);

    public GraphMailClient(
        IHttpClientFactory httpClientFactory,
        TokenCredential credential,
        ILogger<GraphMailClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credential = credential;
        _logger = logger;
    }

    private const string MessageRefSelect = "$select=id,internetMessageId,receivedDateTime";

    public async Task<IReadOnlyList<GraphMessageRef>> ListUnreadInboxMessagesAsync(string mailbox, int top, CancellationToken cancellationToken)
    {
        // Deliberately unordered. Graph requires every property named in
        // $orderby to also appear in $filter for messages, so adding
        // "$orderby=receivedDateTime" beside "isRead eq false" returns
        // InefficientFilter ("the restriction or sort order is too complex")
        // rather than sorted results. Ordering that matters is on the Queued
        // listing, which needs no filter and can therefore ask for it.
        var url = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/mailFolders/inbox/messages"
            + $"?$filter=isRead eq false&$top={top}&{MessageRefSelect}";

        return await ListMessageRefsAsync(url, cancellationToken);
    }

    /// <summary>
    /// Every message in a folder, oldest first (#266). No <c>isRead</c>
    /// filter: for the Queued folder, membership is the state, and read
    /// status says nothing about whether a message still needs processing.
    /// That absence is also what makes the sort legal — see the note above.
    /// </summary>
    public async Task<IReadOnlyList<GraphMessageRef>> ListFolderMessagesAsync(
        string mailbox,
        string folderId,
        int top,
        CancellationToken cancellationToken)
    {
        var url = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/mailFolders/{Uri.EscapeDataString(folderId)}/messages"
            + $"?$top={top}&$orderby=receivedDateTime asc&{MessageRefSelect}";

        return await ListMessageRefsAsync(url, cancellationToken);
    }

    private async Task<IReadOnlyList<GraphMessageRef>> ListMessageRefsAsync(string url, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken)
            ?? throw new InvalidOperationException("Graph message listing failed.");

        var refs = new List<GraphMessageRef>();

        foreach (var element in document.RootElement.GetProperty("value").EnumerateArray())
        {
            refs.Add(new GraphMessageRef(
                element.GetProperty("id").GetString()!,
                element.TryGetProperty("internetMessageId", out var imid) ? imid.GetString() : null,
                element.TryGetProperty("receivedDateTime", out var received) && received.ValueKind == JsonValueKind.String
                    ? received.GetDateTimeOffset()
                    : null));
        }

        return refs;
    }

    public async Task<GraphMessage?> GetMessageAsync(string mailbox, string messageId, CancellationToken cancellationToken)
    {
        var url = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/messages/{messageId}"
            + "?$select=id,internetMessageId,from,body,internetMessageHeaders,hasAttachments";

        using var document = await SendAsync(
            HttpMethod.Get,
            url,
            body: null,
            cancellationToken,
            preferTextBody: true,
            tolerateNotFound: true);

        if (document == null)
        {
            return null;
        }

        var root = document.RootElement;

        var headers = new List<GraphInternetMessageHeader>();
        if (root.TryGetProperty("internetMessageHeaders", out var headerArray) && headerArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in headerArray.EnumerateArray())
            {
                var name = header.TryGetProperty("name", out var n) ? n.GetString() : null;
                var value = header.TryGetProperty("value", out var v) ? v.GetString() : null;
                if (name != null && value != null)
                {
                    headers.Add(new GraphInternetMessageHeader(name, value));
                }
            }
        }

        string? fromAddress = null;
        if (root.TryGetProperty("from", out var from)
            && from.TryGetProperty("emailAddress", out var emailAddress)
            && emailAddress.TryGetProperty("address", out var address))
        {
            fromAddress = address.GetString();
        }

        string? bodyText = null;
        if (root.TryGetProperty("body", out var bodyElement)
            && bodyElement.TryGetProperty("content", out var content))
        {
            bodyText = content.GetString();
        }

        return new GraphMessage(
            root.GetProperty("id").GetString()!,
            root.TryGetProperty("internetMessageId", out var messageInternetId) ? messageInternetId.GetString() : null,
            fromAddress,
            bodyText,
            headers,
            root.TryGetProperty("hasAttachments", out var hasAttachments) && hasAttachments.ValueKind == JsonValueKind.True);
    }

    public async Task<GraphAttachmentListing> ListAttachmentsAsync(
        string mailbox,
        string messageId,
        CancellationToken cancellationToken)
    {
        var attachments = new List<GraphInboundAttachment>();
        var unreadable = new List<string>();
        var url = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/messages/{messageId}/attachments";

        // Graph's contract is explicit: "To read all results, you must
        // continue to call Microsoft Graph with the @odata.nextLink property
        // returned in each response until the @odata.nextLink property is no
        // longer returned." We never did, so a second page would have been
        // invisible — including to the reconciliation below, which would
        // cheerfully report that everything listed had been read.
        //
        // Bounded rather than while(true): a nextLink that never terminates
        // would otherwise hang the whole poll on one message.
        for (var page = 0; page < MaxAttachmentPages && url != null; page++)
        {
            using var document = await SendAsync(HttpMethod.Get, url, body: null, cancellationToken, tolerateNotFound: true);

            if (document == null
                || !document.RootElement.TryGetProperty("value", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var item in items.EnumerateArray())
            {
                var classified = Classify(item);

                if (classified.IsInline)
                {
                    continue;
                }

                if (classified.UnreadableKind != null)
                {
                    unreadable.Add(classified.UnreadableKind);
                    continue;
                }

                attachments.Add(new GraphInboundAttachment(
                    item.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    item.TryGetProperty("contentType", out var contentType) ? contentType.GetString() ?? string.Empty : string.Empty,
                    item.TryGetProperty("size", out var size) && size.TryGetInt32(out var sizeValue) ? sizeValue : 0,
                    Convert.FromBase64String(classified.ContentBytes!)));
            }

            url = document.RootElement.TryGetProperty("@odata.nextLink", out var next)
                ? next.GetString()
                : null;
        }

        return new GraphAttachmentListing(attachments, unreadable);
    }

    internal const string UnknownAttachmentKind = "unknown";

    /// <summary>
    /// What one entry in the attachments collection is (#249). Pulled out of
    /// the loop so the rule can be tested: every test substitutes
    /// <see cref="IGraphMailClient"/> wholesale, so anything left inside the
    /// request loop ships unverified — and the inline rule is the one guard
    /// here that must not be got wrong, since treating a signature logo as a
    /// discrepancy would bounce every signed email.
    /// </summary>
    internal sealed record GraphAttachmentClassification(
        bool IsInline,
        string? UnreadableKind,
        string? ContentBytes);

    internal static GraphAttachmentClassification Classify(JsonElement item)
    {
        // Inline parts are signature logos and the like. Deliberately skipped
        // since #210 — treating them as content would change behaviour for
        // every signed email — and so NOT a discrepancy.
        if (item.TryGetProperty("isInline", out var inline) && inline.ValueKind == JsonValueKind.True)
        {
            return new GraphAttachmentClassification(true, null, null);
        }

        var contentBytes = item.TryGetProperty("contentBytes", out var bytes)
            && bytes.ValueKind == JsonValueKind.String
                ? bytes.GetString()
                : null;

        if (contentBytes != null)
        {
            return new GraphAttachmentClassification(false, null, contentBytes);
        }

        // An itemAttachment (forwarded mail) or referenceAttachment (a link),
        // or a fileAttachment that unexpectedly arrived without bytes. The
        // kind is reportable; the name never is — it can itself be PHI.
        return new GraphAttachmentClassification(
            false,
            item.TryGetProperty("@odata.type", out var kind) && kind.ValueKind == JsonValueKind.String
                ? kind.GetString() ?? UnknownAttachmentKind
                : UnknownAttachmentKind,
            null);
    }

    // Generous: the message caps are 10 MB per attachment and 20 MB in total,
    // so a message reaching this many pages is pathological rather than
    // clinical.
    private const int MaxAttachmentPages = 20;

    public async Task MarkReadAsync(string mailbox, string messageId, CancellationToken cancellationToken)
    {
        var url = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/messages/{messageId}";
        using var _ = await SendAsync(new HttpMethod("PATCH"), url, "{\"isRead\":true}", cancellationToken, tolerateNotFound: true);
    }

    public async Task<string?> FindInboxChildFolderAsync(string mailbox, string displayName, CancellationToken cancellationToken)
    {
        var cacheKey = $"{mailbox}|{displayName}";

        await _folderLock.WaitAsync(cancellationToken);
        try
        {
            return _folderIds.TryGetValue(cacheKey, out var cached)
                ? cached
                : await FindUncachedAsync(mailbox, displayName, cacheKey, cancellationToken);
        }
        finally
        {
            _folderLock.Release();
        }
    }

    private async Task<string?> FindUncachedAsync(
        string mailbox,
        string displayName,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var listUrl = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/mailFolders/inbox/childFolders"
            + $"?$filter=displayName eq '{displayName.Replace("'", "''")}'";

        using var listDocument = await SendAsync(HttpMethod.Get, listUrl, body: null, cancellationToken);
        var existing = listDocument?.RootElement.GetProperty("value").EnumerateArray().FirstOrDefault();

        if (existing is not { ValueKind: JsonValueKind.Object })
        {
            return null;
        }

        var id = existing.Value.GetProperty("id").GetString()!;
        _folderIds[cacheKey] = id;
        return id;
    }

    public async Task<string> EnsureInboxChildFolderAsync(string mailbox, string displayName, CancellationToken cancellationToken)
    {
        var cacheKey = $"{mailbox}|{displayName}";

        await _folderLock.WaitAsync(cancellationToken);
        try
        {
            if (_folderIds.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            if (await FindUncachedAsync(mailbox, displayName, cacheKey, cancellationToken) is { } found)
            {
                return found;
            }

            var createUrl = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/mailFolders/inbox/childFolders";
            var createBody = JsonSerializer.Serialize(new { displayName });

            using var created = await SendAsync(HttpMethod.Post, createUrl, createBody, cancellationToken)
                ?? throw new InvalidOperationException($"Could not create mail folder '{displayName}'.");

            var createdId = created.RootElement.GetProperty("id").GetString()!;
            _folderIds[cacheKey] = createdId;
            return createdId;
        }
        finally
        {
            _folderLock.Release();
        }
    }

    public async Task MoveMessageAsync(string mailbox, string messageId, string destinationFolderId, CancellationToken cancellationToken)
    {
        var url = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/messages/{messageId}/move";
        var body = JsonSerializer.Serialize(new { destinationId = destinationFolderId });
        using var _ = await SendAsync(HttpMethod.Post, url, body, cancellationToken, tolerateNotFound: true);
    }

    public async Task SendMailAsync(
        string mailbox,
        string toAddress,
        string subject,
        string textBody,
        CancellationToken cancellationToken,
        IReadOnlyList<GraphMailAttachment>? attachments = null)
    {
        var url = $"{GraphBase}/users/{Uri.EscapeDataString(mailbox)}/sendMail";

        // Dictionaries rather than anonymous types: the attachment's mandatory
        // "@odata.type" is not a legal anonymous-property name. Inline
        // attachments cap around 3 MB of REQUEST — a whole-message budget the
        // caller shares across the set (see the reply activity's cap).
        var message = new Dictionary<string, object?>
        {
            ["subject"] = subject,
            ["body"] = new { contentType = "Text", content = textBody },
            ["toRecipients"] = new[] { new { emailAddress = new { address = toAddress } } }
        };

        if (attachments is { Count: > 0 })
        {
            message["attachments"] = attachments
                .Select(attachment => new Dictionary<string, object?>
                {
                    ["@odata.type"] = "#microsoft.graph.fileAttachment",
                    ["name"] = attachment.Name,
                    ["contentType"] = attachment.ContentType,
                    ["contentBytes"] = Convert.ToBase64String(attachment.Content)
                })
                .ToList();
        }

        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["message"] = message,
            ["saveToSentItems"] = true
        });

        var document = await SendAsync(HttpMethod.Post, url, body, cancellationToken);
        document?.Dispose();
    }

    private async Task<JsonDocument?> SendAsync(
        HttpMethod method,
        string url,
        string? body,
        CancellationToken cancellationToken,
        bool preferTextBody = false,
        bool tolerateNotFound = false)
    {
        var token = await _credential.GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        if (preferTextBody)
        {
            request.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
        }

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        using var response = await client.SendAsync(request, cancellationToken);

        if (tolerateNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            // Status only — Graph error bodies can echo message metadata.
            _logger.LogWarning(
                "Graph mail request failed. Method={Method}, StatusCode={StatusCode}",
                method.Method,
                (int)response.StatusCode);
            throw new InvalidOperationException($"Graph mail request failed with status {(int)response.StatusCode}.");
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
