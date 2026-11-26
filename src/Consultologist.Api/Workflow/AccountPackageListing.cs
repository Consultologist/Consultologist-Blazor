using System.Text.Json;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Workflow;

/// <summary>
/// Parses one account fork's private-registry listing into the summary the
/// package selector consumes (#134): {name}/{version}/manifest.json blobs plus
/// the {name}/latest.json pointer — the same layout the public chain view's
/// package branch reads on the public side. #1508: a version whose manifest
/// declares specVersion 31 is a version only once its stamp
/// ({name}/{version}/publish.json) is in the same listing
/// (<see cref="IsUnstamped31"/>).
/// </summary>
public static class AccountPackageListing
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static PublicPackageSummary Build(
        string name,
        IReadOnlyCollection<string> blobNames,
        string? latestPointerJson,
        IReadOnlyDictionary<string, int>? specVersions = null,
        IReadOnlyDictionary<string, string>? titles = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? tags = null)
    {
        var listed = blobNames as IReadOnlySet<string> ?? new HashSet<string>(blobNames, StringComparer.Ordinal);
        var unstamped = new HashSet<string>(StringComparer.Ordinal);
        var versions = PublicRegistryReader.SortVersions(blobNames
            .Select(blobName => WorkflowPackageRef.TryParseManifestPath(blobName, out var owner, out var version) && string.Equals(owner, name, StringComparison.Ordinal) ? version : null)
            .Where(version => version != null)
            .Where(version =>
            {
                if (!IsUnstamped31($"{name}/{version}", specVersions != null && specVersions.TryGetValue(version!, out var spec) ? spec : null, listed))
                {
                    return true;
                }

                unstamped.Add(version!);
                return false;
            })!);

        if (unstamped.Count > 0)
        {
            specVersions = Without(specVersions, unstamped);
            titles = Without(titles, unstamped);
            tags = Without(tags, unstamped);
        }

        string? latest = null;

        if (!string.IsNullOrWhiteSpace(latestPointerJson))
        {
            try
            {
                latest = JsonSerializer.Deserialize<LatestPointer>(latestPointerJson, JsonOptions)?.Version;
            }
            catch (JsonException)
            {
                // A malformed pointer degrades to "no latest"; the versions list stands.
            }
        }

        return new PublicPackageSummary(name, latest, versions, specVersions, titles, tags);
    }

    /// <summary>
    /// #1508: the summary of <paramref name="name"/> in an account's listing
    /// (<paramref name="blobNames"/>, every blob under the account's root):
    /// each version's manifest is read once (<paramref name="readManifest"/>)
    /// for its spec version, title and tags, and kept in
    /// <paramref name="cache"/> for the process lifetime (a stamped version is
    /// immutable). A version whose manifest declares 31 and whose stamp is not
    /// in the listing is not listed and not cached (its folder may still be
    /// written, or rewritten by a retry), so it is read again until it is
    /// stamped, and listed from then on. Null when no version is listed.
    /// </summary>
    public static async Task<PublicPackageSummary?> SummarizeAsync(
        string name,
        IReadOnlySet<string> blobNames,
        string? latestPointerJson,
        Func<string, Task<string>> readManifest,
        IDictionary<string, (int? SpecVersion, string? Title, IReadOnlyList<string>? Tags)> cache)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(blobNames);
        ArgumentNullException.ThrowIfNull(readManifest);
        ArgumentNullException.ThrowIfNull(cache);

        var specVersions = new Dictionary<string, int>(StringComparer.Ordinal);
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var tags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var manifestPath in blobNames.Where(n => WorkflowPackageRef.TryParseManifestPath(n, out var owner, out _) && owner == name))
        {
            WorkflowPackageRef.TryParseManifestPath(manifestPath, out _, out var version);

            if (!cache.TryGetValue(manifestPath, out var listing))
            {
                var manifestJson = await readManifest(manifestPath);
                listing = (ReadSpecVersion(manifestJson), ReadTitle(manifestJson), ReadTags(manifestJson));

                if (!IsUnstamped31($"{name}/{version}", listing.SpecVersion, blobNames))
                {
                    cache[manifestPath] = listing;
                }
            }

            if (listing.SpecVersion is int value)
            {
                specVersions[version] = value;
            }

            if (listing.Title is { } title)
            {
                titles[version] = title;
            }

            if (listing.Tags is { } declared)
            {
                tags[version] = declared;
            }
        }

        var summary = Build(
            name, blobNames, latestPointerJson,
            specVersions.Count > 0 ? specVersions : null,
            titles.Count > 0 ? titles : null,
            tags.Count > 0 ? tags : null);

        return summary.Versions.Count > 0 ? summary : null;
    }

    /// <summary>
    /// #1508 (Global Constraints: an unstamped folder does not exist for any
    /// reader): true when the version folder <paramref name="folder"/>
    /// (<c>{name}/{version}</c>), whose manifest declares
    /// <paramref name="specVersion"/>, is a 31 version whose stamp is not in
    /// the listing <paramref name="listed"/>. The 31 writer writes the source
    /// first and the stamp last, so its manifest is listed long before the
    /// version exists. A 5–30 version (the manifest written last) is never
    /// unstamped here, whatever its <c>publish.json</c>; an unknown spec
    /// version (null) is not either.
    /// </summary>
    public static bool IsUnstamped31(string folder, int? specVersion, IReadOnlySet<string> listed) =>
        specVersion == PublishedStamp.SpecVersion && !listed.Contains($"{folder}/{PublishedStamp.FileName}");

    private static IReadOnlyDictionary<string, T>? Without<T>(IReadOnlyDictionary<string, T>? byVersion, IReadOnlySet<string> versions) =>
        byVersion?.Where(pair => !versions.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>
    /// Reads just the title out of a manifest document (v9 § 4, #432); null
    /// when absent, blank, not a string, or the document is unreadable. The
    /// registry writes camelCase, as ReadSpecVersion assumes.
    /// </summary>
    public static string? ReadTitle(string manifestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestJson);

            return document.RootElement.TryGetProperty("title", out var title)
                && title.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(title.GetString())
                    ? title.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The distinct package names a blob listing holds a manifest for —
    /// {name}/{version}/manifest.json, the name read from the right so a
    /// nested one (#448) is itself — in ordinal order.
    /// </summary>
    public static IReadOnlyList<string> NamesIn(IEnumerable<string> blobNames) =>
        blobNames
            .Select(blobName => WorkflowPackageRef.TryParseManifestPath(blobName, out var name, out _) ? name : null)
            .Where(name => name != null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Reads just the tags out of a manifest document (#453): the array's
    /// string entries in authored order; null when absent, not an array, or
    /// the document is unreadable; an empty list when the array is empty.
    /// Non-string entries are skipped — this is a listing, not the validator,
    /// which refused them at publish.
    /// </summary>
    public static IReadOnlyList<string>? ReadTags(string manifestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestJson);

            if (!document.RootElement.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return tags.EnumerateArray()
                .Where(tag => tag.ValueKind == JsonValueKind.String)
                .Select(tag => tag.GetString()!)
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads just the specVersion out of a manifest document; null when the
    /// document is unreadable (callers treat unknown as selectable — the
    /// resolver stays the runtime authority).
    /// </summary>
    public static int? ReadSpecVersion(string manifestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(manifestJson);

            return document.RootElement.TryGetProperty("specVersion", out var spec)
                && spec.ValueKind == JsonValueKind.Number
                && spec.TryGetInt32(out var value)
                    ? value
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record LatestPointer(string? Version);
}
