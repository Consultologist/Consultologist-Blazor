namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// One package found inside an uploaded archive: the <see cref="Prefix"/> it sat
/// under (empty for a single-package zip whose manifest.json is at the root, or
/// the export's per-package subfolder like <c>acct-root/test/general/</c>), its
/// raw <c>manifest.json</c> text, and its files with the prefix stripped and
/// manifest.json removed — i.e. exactly the shape a single-package import expects.
/// </summary>
public sealed record WorkflowPackageBundleEntry(string Prefix, string ManifestJson, Dictionary<string, string> Files);

/// <summary>
/// Splits the flat path→text map of an uploaded archive (#859 Profile bulk import)
/// into its constituent packages — the reverse of the Profile multi-package export
/// (<c>Profile.razor</c>'s DownloadMyPackagesAsync), which writes each package under
/// <c>&lt;package.Name&gt;/</c>. A package name can contain '/', so the split anchors on
/// the unambiguous <c>manifest.json</c> marker rather than the first path segment.
/// </summary>
public static class WorkflowPackageBundle
{
    private const string ManifestName = "manifest.json";

    /// <summary>
    /// Groups <paramref name="entries"/> by package. Each entry whose path ends in
    /// <c>manifest.json</c> is a package root; its prefix is the text before it, and
    /// the package's files are the other entries under that prefix (prefix stripped).
    /// Roots are claimed longest-prefix-first, so a root-level manifest (a
    /// single-package zip) only takes what no subfolder package claimed. Entries
    /// under no manifest are returned in <paramref name="strays"/>.
    /// </summary>
    public static IReadOnlyList<WorkflowPackageBundleEntry> Split(
        IReadOnlyDictionary<string, string> entries,
        out IReadOnlyList<string> strays)
    {
        var prefixes = entries.Keys
            .Where(path => path == ManifestName || path.EndsWith("/" + ManifestName, StringComparison.Ordinal))
            .Select(path => path.Length == ManifestName.Length ? string.Empty : path[..^ManifestName.Length])
            .OrderByDescending(prefix => prefix.Length)
            .ToList();

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var packages = new List<WorkflowPackageBundleEntry>();

        foreach (var prefix in prefixes)
        {
            var manifestPath = prefix + ManifestName;
            var files = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (path, text) in entries)
            {
                if (claimed.Contains(path) || path == manifestPath)
                {
                    continue;
                }

                if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                files[path[prefix.Length..]] = text;
                claimed.Add(path);
            }

            claimed.Add(manifestPath);
            packages.Add(new WorkflowPackageBundleEntry(prefix, entries[manifestPath], files));
        }

        strays = entries.Keys
            .Where(path => !claimed.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // Stable, predictable order for the staging UI.
        return packages.OrderBy(package => package.Prefix, StringComparer.Ordinal).ToList();
    }
}
