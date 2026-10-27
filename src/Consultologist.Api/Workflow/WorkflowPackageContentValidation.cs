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

    // v25 (B2 #1001): formats/<file> (a package-local format) joins the root
    // set; snippets/<file> and snippets/<folder>/<file> mirror data's two shapes.
    // C2 (#1008): a snippet folder's id is snake_case at 25 (the validator's
    // rule), so its directory segment takes the underscore data's never did —
    // the editor names a folder snippets/<id>/, as it names data/<id>/ today.
    private static readonly Regex RootFilePattern = new("^(prompts|schemas|macros|formats)/[A-Za-z0-9._-]+$", RegexOptions.Compiled);
    // v26 (#1182, design § 5.3): a macro folder's files — macros/<folder>/<file>, the
    // folder id snake_case as a snippet folder's is.
    private static readonly Regex MacroFolderFilePattern = new("^macros/[a-z0-9_]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);
    private static readonly Regex DataFilePattern = new("^(data/[a-z0-9-]+|snippets/[a-z0-9_-]+)/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

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
    private static readonly Regex DataValuePattern = new("^(data|snippets)/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

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
            if ((!RootFilePattern.IsMatch(path) && !MacroFolderFilePattern.IsMatch(path) && !DataFilePattern.IsMatch(path) && !DataValuePattern.IsMatch(path))
                || path.Split('/').Any(segment => segment.Trim('.').Length == 0))
            {
                errors.Add($"File path '{path}' is not allowed: expected prompts/<file>, schemas/<file>, macros/<file>, macros/<folder>/<file>, formats/<file>, data/<collection>/<file>, data/<file>, snippets/<folder>/<file>, or snippets/<file>.");
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

        // #1102 (v26 design § 4): at 26 a node names its prompt file itself.
        if (manifest.SpecVersion >= 26)
        {
            foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
            {
                if (node.Prompt is not null)
                {
                    referenced.Add(node.Prompt);
                }
            }
        }

        foreach (var path in (manifest.Preludes ?? new Dictionary<string, string>()).Values)
        {
            referenced.Add(path);
        }

        foreach (var macro in manifest.Macros ?? new List<WorkflowMacroSpec>())
        {
            // #1099 (v26 design § 5.3): a macro folder — its index and the items the index names.
            if (manifest.SpecVersion >= 26 && macro.File is { } dir && dir.EndsWith('/'))
            {
                var macroIndex = dir + WorkflowDataResolver.IndexFileName;
                referenced.Add(macroIndex);

                if (files.TryGetValue(macroIndex, out var macroIndexJson))
                {
                    foreach (var (_, file) in WorkflowTextHeaders.ReadIndexAt26(macroIndexJson, macro.Id, "Macro", new List<string>()) ?? new List<(string, string)>())
                    {
                        referenced.Add(dir + file);
                    }
                }

                continue;
            }

            referenced.Add(macro.File);
        }

        foreach (var path in (manifest.Schemas ?? new Dictionary<string, string>()).Values)
        {
            referenced.Add(path);
        }

        // v22 (#760): a custom schema carries an inline body file, like Schemas —
        // its path is referenced, not a stray. (schemaRefs name a catalog contract,
        // not a file, so they contribute nothing here.)
        foreach (var path in (manifest.CustomSchemas ?? new Dictionary<string, string>()).Values)
        {
            referenced.Add(path);
        }

        // v25 (B2 #1001): the package-local format files, and the snippets —
        // the same table shape as data (a file, or a folder with an index and
        // its items), referenced the same way.
        foreach (var format in manifest.Formats ?? new List<WorkflowFormatReference>())
        {
            referenced.Add(format.File);
        }

        var tables = (manifest.Data ?? new Dictionary<string, string>())
            .Concat(manifest.Snippets ?? new Dictionary<string, string>());

        foreach (var (_, path) in tables)
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

            // #1105 (v26 design § 5.3): at 26 the index is the ordered [{ id, file }]
            // list (the validator says what is wrong with it; here it only names files).
            if (manifest.SpecVersion >= 26)
            {
                foreach (var (_, file) in WorkflowTextHeaders.ReadIndexAt26(indexJson, path, "Snippet", new List<string>()) ?? new List<(string, string)>())
                {
                    referenced.Add(path + file);
                }

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
