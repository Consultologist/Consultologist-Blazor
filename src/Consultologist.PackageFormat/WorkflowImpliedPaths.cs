namespace Consultologist.PackageFormat;

/// <summary>
/// v28 (#1283, package-format-v28-design.md § 1, § 6): the paths a 28 manifest
/// no longer spells. The macro and snippet tables imply theirs in their
/// converters; a node's prompt is implied here, from the files, because whether
/// a node reads one depends on its format (a computed node reads none) and the
/// catalogue that says so is the validator's. Below 28 nothing is implied.
/// </summary>
public static class WorkflowImpliedPaths
{
    private const string PromptsDirectory = "prompts/";
    private const string MacrosDirectory = "macros/";
    private const string SnippetsDirectory = "snippets/";

    /// <summary>
    /// The manifest with every non-flow node's prompt set to <c>prompts/&lt;id&gt;.md</c>
    /// when that file is in the package and the node names none. A node whose
    /// file is absent keeps no prompt; the validator names it, or accepts it
    /// when its format is computed. Returned unchanged below 28.
    /// </summary>
    public static WorkflowPackageManifest WithImpliedPrompts(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files)
    {
        if (manifest.SpecVersion < 28 || manifest.Nodes is null)
        {
            return manifest;
        }

        var nodes = manifest.Nodes
            .Select(node => node.Prompt is null && !NamesNoPrompt(node) && files.ContainsKey(PromptOf(node.Id))
                ? node with { Prompt = PromptOf(node.Id) }
                : node)
            .ToList();
        return manifest with { Nodes = nodes };
    }

    /// <summary>
    /// v28 (#1284, design § 6): the prompt files a loader may fetch before it holds
    /// the package's files — <c>prompts/&lt;id&gt;.md</c> of every non-flow node that names
    /// none. Fetched optionally: a computed node has no file, and a missing one is
    /// the validator's to name. Empty below 28.
    /// </summary>
    public static IReadOnlyList<string> CandidatePrompts(WorkflowPackageManifest manifest) =>
        manifest.SpecVersion < 28
            ? Array.Empty<string>()
            : (manifest.Nodes ?? new List<WorkflowNodeSpec>())
                .Where(node => node.Prompt is null && !NamesNoPrompt(node))
                .Select(node => PromptOf(node.Id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

    /// <summary>The prompt file a node's id implies (§ 1).</summary>
    public static string PromptOf(string nodeId) => WorkflowFileMove.ImpliedPath("node", nodeId);

    /// <summary>True for a for, if or case node — it runs a body and reads no prompt.</summary>
    public static bool IsFlow(WorkflowNodeSpec node) => node.For is not null || node.If is not null || node.Case is not null || node.While is not null;

    /// <summary>v30 (#1339 A3): a node that reads no prompt file — a flow node, or a let (computed from its expression).</summary>
    public static bool NamesNoPrompt(WorkflowNodeSpec node) => IsFlow(node) || node.Let is not null;

    /// <summary>
    /// The § 2 warnings: a file under <c>prompts/</c> that is no node's prompt,
    /// a file under <c>macros/</c> or <c>snippets/</c> that is no entry, no
    /// folder's index and no listed item. The manifest given has its prompts
    /// implied; a folder's items are read by <see cref="WorkflowTextHeaders.ReadIndex"/>.
    /// Each sentence says what the file would have to be called.
    /// </summary>
    public static IReadOnlyList<string> StrayFiles(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.Prompt is { } prompt)
            {
                named.Add(prompt);
            }
        }

        var folders = new List<(string Kind, string Id, string Directory)>();
        foreach (var macro in manifest.Macros ?? new List<WorkflowMacroSpec>())
        {
            if (macro.File.EndsWith('/'))
            {
                folders.Add(("Macro", macro.Id, macro.File));
            }
            else
            {
                named.Add(macro.File);
            }
        }

        foreach (var (id, path) in manifest.Snippets ?? new Dictionary<string, string>())
        {
            if (path.EndsWith('/'))
            {
                folders.Add(("Snippet", id, path));
            }
            else
            {
                named.Add(path);
            }
        }

        var folderDirectories = new List<string>();
        foreach (var (kind, id, directory) in folders)
        {
            folderDirectories.Add(directory);
            var indexPath = directory + WorkflowDataResolver.IndexFileName;
            named.Add(indexPath);
            if (files.TryGetValue(indexPath, out var indexJson)
                && WorkflowTextHeaders.ReadIndex(indexJson, id, kind, manifest.SpecVersion, new List<string>()) is { } index)
            {
                foreach (var (_, file) in index.Items)
                {
                    named.Add(directory + file);
                }
            }
        }

        var warnings = new List<string>();
        foreach (var path in files.Keys.Order(StringComparer.Ordinal))
        {
            if (named.Contains(path))
            {
                continue;
            }

            if (path.StartsWith(PromptsDirectory, StringComparison.Ordinal))
            {
                warnings.Add($"{path} names no node; at specVersion 28 a prompt file is prompts/<node id>.md.");
            }
            else if (folderDirectories.FirstOrDefault(d => path.StartsWith(d, StringComparison.Ordinal)) is { } folder)
            {
                warnings.Add($"{path} is not listed in {folder}{WorkflowDataResolver.IndexFileName}; at specVersion 28 an item is <id>.md beside the index, listed in its items.");
            }
            else if (path.StartsWith(MacrosDirectory, StringComparison.Ordinal))
            {
                warnings.Add($"{path} names no macro; at specVersion 28 a macro file is macros/<id>.md, listed in macros.");
            }
            else if (path.StartsWith(SnippetsDirectory, StringComparison.Ordinal))
            {
                warnings.Add($"{path} names no snippet; at specVersion 28 a snippet file is snippets/<id>.md, listed in snippets.");
            }
        }

        return warnings;
    }
}
