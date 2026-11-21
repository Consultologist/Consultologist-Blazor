using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): <c>sourceHash</c> (part 07 § 2.6), the hash of the source as published: the
/// SHA-256 of the RFC 8785 text of a listing, sorted by path (ordinal), of <c>{ "path", "hash" }</c> for every source
/// file, each <c>hash</c> taken over the file's UTF-8 bytes after line-ending normalisation.
/// </summary>
public static class SourceHash
{
    /// <summary>The nine folders whose files are source, every <c>index.json</c> among them.</summary>
    private static readonly string[] Folders =
        ["nodes/", "checks/", "functions/", "renderers/", "snippets/", "macros/", "formats/", "deliverables/", "tests/"];

    /// <summary>
    /// The hash of <paramref name="files"/>, path to text, as <see cref="SourceParseResult.Files"/> gives them. The listing
    /// holds <c>manifest.json</c>, every file under the nine folders and any <c>README.md</c>; anything else is not
    /// source: <c>resolve.json</c>, <c>lock.json</c> and the publish report (evidence), and <c>dag.mmd</c> (a view).
    /// Each text has <c>\r\n</c> made <c>\n</c> again, as intake does, so a listing not taken from intake hashes the same.
    /// </summary>
    public static string Of(IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var listing = new JsonArray();
        foreach (var (path, text) in files.Where(f => IsSource(f.Key)).OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            listing.Add(new JsonObject
            {
                ["path"] = path,
                ["hash"] = CanonicalJson.HashText((text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal)),
            });
        }

        return CanonicalJson.Hash(listing);
    }

    private static bool IsSource(string path) =>
        path == "manifest.json"
        || path == "README.md"
        || path.EndsWith("/README.md", StringComparison.Ordinal)
        || Folders.Any(f => path.StartsWith(f, StringComparison.Ordinal));
}
