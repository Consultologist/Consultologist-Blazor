using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; D1): the "published" stamp of a 31 version (part 09 § 1.2 step 3), <see cref="FileName"/>
/// in the version's folder: <c>{ "specVersion": 31, "resolveHash": "sha256:…", "lockHash": "sha256:…" }</c>, the hashes
/// <c>resolve.json</c> and <c>lock.json</c> must have. Publish writes it last, so a folder without one is not a version
/// the engine loads. The load path is chosen by it: a stamp with <c>specVersion</c> 31 takes the 31 path, anything else
/// today's. Area 11 writes it and may add members; <see cref="TryRead"/> ignores them.
/// </summary>
public sealed record PublishedStamp(string ResolveHash, string LockHash)
{
    /// <summary>The stamp's file name in a version's folder (the name the 5–30 stamp has too).</summary>
    public const string FileName = "publish.json";

    /// <summary>The spec version a stamp of this shape names.</summary>
    public const int SpecVersion = 31;

    /// <summary>The deepest JSON <see cref="TryRead"/> reads: a stamp is one level.</summary>
    private const int MaxDepth = 64;

    /// <summary>The stamp's text: canonical (RFC 8785), with <c>specVersion</c>.</summary>
    public string ToJson() => CanonicalJson.Write(new JsonObject
    {
        ["specVersion"] = SpecVersion,
        ["resolveHash"] = ResolveHash,
        ["lockHash"] = LockHash,
    });

    /// <summary>
    /// The stamp <paramref name="json"/> holds, or null unless it is a JSON object with <c>specVersion</c> 31 and a
    /// <c>resolveHash</c> and a <c>lockHash</c> that are each <c>sha256:</c> and 64 lowercase hex digits. Other members are
    /// ignored. Never throws.
    /// </summary>
    public static PublishedStamp? TryRead(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = MaxDepth }) is not JsonObject stamp)
                return null;

            // A repeated member surfaces when the object is first read.
            if (stamp["specVersion"] is not JsonValue version
                || version.GetValueKind() != JsonValueKind.Number
                || !version.TryGetValue<int>(out var number)
                || number != SpecVersion)
            {
                return null;
            }

            var resolveHash = SourceLayout.Text(stamp["resolveHash"]);
            var lockHash = SourceLayout.Text(stamp["lockHash"]);
            return IsHash(resolveHash) && IsHash(lockHash) ? new PublishedStamp(resolveHash!, lockHash!) : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Whether <paramref name="text"/> is <c>sha256:</c> and 64 lowercase hex digits.</summary>
    internal static bool IsHash(string? text)
    {
        const string prefix = "sha256:";
        if (text is null || text.Length != prefix.Length + 64 || !text.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        for (var i = prefix.Length; i < text.Length; i++)
        {
            if (text[i] is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                return false;
        }

        return true;
    }
}
