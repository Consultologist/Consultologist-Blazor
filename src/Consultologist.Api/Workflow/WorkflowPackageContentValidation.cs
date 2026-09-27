using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Workflow;

/// <summary>
/// The content-only half of package validation: the file-path allowlist + byte
/// caps, and the reverse manifest↔files closure. Shared by
/// <see cref="WorkflowPackagePublisher"/> (publish) and
/// <see cref="WorkflowPackageIngestor"/> (#858 import ingest), so both gates run
/// exactly the same checks. The forward closure half (every referenced file
/// present) and the schema/graph checks live in
/// <see cref="WorkflowPackageValidator"/>.
/// </summary>
internal static class WorkflowPackageContentValidation
{
    public const int MaxFileBytes = 256 * 1024;
    public const int MaxTotalBytes = 2 * 1024 * 1024;

    private static readonly Regex RootFilePattern = new("^(prompts|schemas|macros)/[A-Za-z0-9._-]+$", RegexOptions.Compiled);
    private static readonly Regex DataFilePattern = new("^data/[a-z0-9-]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    /// <summary>
    /// A data path with no directory segment is a **single-value** entry —
    /// <c>data/specialty.txt</c> — which <c>WorkflowDataResolver</c> reads as a
    /// scalar and nodes bind as <c>data:&lt;id&gt;</c>. One segment, so it matches
    /// neither pattern above and this door refused it (#318) while the content
    /// repo's publish script, which has no allowlist, let repo packages through.
    ///
    /// Underscores are allowed deliberately: <c>note_type</c> is published and
    /// running. The no-underscore rule belongs to the *directory* segment above,
    /// and a value is a file.
    ///
    /// This pattern matches <c>data/..</c> — the traversal guard below is what
    /// rejects that, and it has to keep doing so.
    /// </summary>
    private static readonly Regex DataValuePattern = new("^data/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions IndexJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void ValidateFilePaths(IReadOnlyDictionary<string, string> files, List<string> errors)
    {
        long totalBytes = 0;

        foreach (var (path, content) in files)
        {
            // Blob names are a flat namespace, so dot segments carry no traversal
            // semantics there — this is defense in depth for any future
            // filesystem-backed consumer of registry paths.
            if ((!RootFilePattern.IsMatch(path) && !DataFilePattern.IsMatch(path) && !DataValuePattern.IsMatch(path))
                || path.Split('/').Any(segment => segment.Trim('.').Length == 0))
            {
                errors.Add($"File path '{path}' is not allowed: expected prompts/<file>, schemas/<file>, macros/<file>, data/<collection>/<file>, or data/<file>.");
                continue;
            }

            var bytes = Encoding.UTF8.GetByteCount(content ?? string.Empty);

            if (bytes > MaxFileBytes)
            {
                errors.Add($"File '{path}' exceeds the {MaxFileBytes / 1024} KB per-file limit.");
            }

            totalBytes += bytes;
        }

        if (totalBytes > MaxTotalBytes)
        {
            errors.Add($"Package content exceeds the {MaxTotalBytes / 1024} KB total limit.");
        }
    }

    /// <summary>
    /// The reverse half of the manifest↔files closure: every uploaded file must be
    /// referenced by the manifest (prompts, preludes, schemas, data scalars, or a
    /// collection's index.json and its item files). The forward half — every
    /// referenced file present — is the validator's, with its own messages.
    /// </summary>
    public static void ValidateFileClosure(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        List<string> errors)
    {
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prompt in manifest.Prompts ?? new List<WorkflowPromptSpec>())
        {
            referenced.Add(prompt.File);
        }

        foreach (var path in (manifest.Preludes ?? new Dictionary<string, string>()).Values)
        {
            referenced.Add(path);
        }

        foreach (var macro in manifest.Macros ?? new List<WorkflowMacroSpec>())
        {
            referenced.Add(macro.File);
        }

        foreach (var path in (manifest.Schemas ?? new Dictionary<string, string>()).Values)
        {
            referenced.Add(path);
        }

        foreach (var (_, path) in manifest.Data ?? new Dictionary<string, string>())
        {
            if (!path.EndsWith('/'))
            {
                referenced.Add(path);
                continue;
            }

            var indexPath = path + WorkflowDataResolver.IndexFileName;
            referenced.Add(indexPath);

            if (!files.TryGetValue(indexPath, out var indexJson))
            {
                continue;
            }

            WorkflowDataIndexFile? index;
            try
            {
                index = JsonSerializer.Deserialize<WorkflowDataIndexFile>(indexJson, IndexJsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            foreach (var item in index?.Items ?? new List<WorkflowDataIndexItem>())
            {
                if (!string.IsNullOrWhiteSpace(item.File))
                {
                    referenced.Add(path + item.File);
                }
            }
        }

        foreach (var stray in files.Keys.Where(path => !referenced.Contains(path)).Order(StringComparer.Ordinal))
        {
            errors.Add($"File '{stray}' is not referenced by the manifest.");
        }
    }
}
