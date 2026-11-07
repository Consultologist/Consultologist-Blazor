namespace Consultologist.PackageFormat;

/// <summary>
/// #1283 (package-format-v28-design.md § 2, § 3, § 6, § 6a): the specVersion 28
/// path — a file's name is its id. The manifest is judged as received first (a
/// retired prompt key, a table's shape, a duplicate entry, a description), then
/// its prompts are implied from the files (<see cref="WorkflowImpliedPaths"/>)
/// and the 27 pipeline runs over it, with the sentences that name an implied
/// file, the snippet folders' item ids checked, and the stray files warned.
/// </summary>
public static partial class WorkflowPackageValidator
{
    private const string SnakeCaseRule = "a lowercase letter, then lowercase letters, digits, or underscores";

    private static void ValidateAt28(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? catalog,
        List<string> errors,
        List<string> warnings,
        // v29 (#1301): run for a 29 package, which adds to 28 and retires nothing.
        bool at29 = false)
    {
        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.Prompt is not null)
            {
                errors.Add($"Node '{node.Id}' declares prompt, which is retired at specVersion 28; a node's prompt is {WorkflowImpliedPaths.PromptOf(node.Id)}.");
            }
        }

        ValidateTableAt28("macros", manifest.Macros as WorkflowMacroTable is { } macros ? (macros.Shape, macros.Select(m => WorkflowTableIds.EntryOf(m.Id, m.File)).ToList(), macros.Duplicates) : null, errors);
        ValidateTableAt28("snippets", manifest.Snippets as WorkflowSnippetTable is { } snippets ? (snippets.Shape, snippets.Select(s => WorkflowTableIds.EntryOf(s.Key, s.Value)).ToList(), snippets.Duplicates) : null, errors);
        ValidateDescriptionsAt28(manifest, errors);

        // An explicit prompt was refused above; it is not implied over.
        var implied = WorkflowImpliedPaths.WithImpliedPrompts(manifest with { Nodes = manifest.Nodes?.Select(n => n with { Prompt = null }).ToList() }, files);
        ValidateAt27(implied, files, catalog, errors, warnings, at28: true, at29: at29);
        warnings.AddRange(WorkflowImpliedPaths.StrayFiles(implied, files));
    }

    /// <summary>
    /// § 2: at 28 a macro or snippet table is a list of ids — a map (or the 25
    /// list of objects) is refused with the package's own ids in the list
    /// spelling; an id the list names twice is named once.
    /// </summary>
    private static void ValidateTableAt28(string name, (WorkflowTableShape Shape, List<string> Entries, IReadOnlyList<string> Duplicates)? table, List<string> errors)
    {
        if (table is not { } read)
        {
            return;
        }

        if (read.Shape != WorkflowTableShape.IdList && read.Entries.Count > 0)
        {
            var kind = read.Shape == WorkflowTableShape.Map ? "a map" : "a list of entries";
            errors.Add($"{name} is {kind}; at specVersion 28 it is a list of ids — [{string.Join(", ", read.Entries.Select(e => $"\"{e}\""))}].");
        }

        foreach (var id in read.Duplicates)
        {
            errors.Add($"{name} lists '{id}' twice.");
        }
    }

    /// <summary>§ 6a (#1270): a description is a flow node's, non-empty and at most the package description's length.</summary>
    private static void ValidateDescriptionsAt28(WorkflowPackageManifest manifest, List<string> errors)
    {
        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.Description is not { } description)
            {
                continue;
            }

            if (!WorkflowImpliedPaths.IsFlow(node))
            {
                errors.Add($"Node '{node.Id}' declares description; only a for, if or case node carries one — a node's prompt file says what it does.");
            }
            else if (string.IsNullOrWhiteSpace(description))
            {
                errors.Add($"Node '{node.Id}' description must not be empty.");
            }
            else if (description.Length > WorkflowPackageMetadata.MaxDescriptionLength)
            {
                errors.Add($"Node '{node.Id}' description must be at most {WorkflowPackageMetadata.MaxDescriptionLength} characters.");
            }
        }
    }

    /// <summary>§ 3: a snippet folder's item ids are snake_case — the 26 gap closed (a macro folder's were checked already).</summary>
    private static void ValidateSnippetFolderItemsAt28(WorkflowPackageData snippets, List<string> errors)
    {
        foreach (var (folder, collection) in snippets.Collections)
        {
            foreach (var item in collection.Items)
            {
                if (!WorkflowDeclaredIds.IsValid(item.Id))
                {
                    errors.Add($"Snippet folder '{folder}' item id '{item.Id}' must be snake_case ({SnakeCaseRule}).");
                }
            }
        }
    }

    /// <summary>
    /// § 6, § 6a: what only 28 spells, refused below it by name — a list of ids
    /// for macros or snippets, and a node's description. (An index in the 28
    /// object shape is refused below 28 where the index is read.)
    /// </summary>
    private static void ValidateV28Members(WorkflowPackageManifest manifest, List<string> errors)
    {
        if (manifest.Macros is WorkflowMacroTable { Shape: WorkflowTableShape.IdList })
        {
            errors.Add("macros is a list of ids, which requires specVersion 28.");
        }

        if (manifest.Snippets is WorkflowSnippetTable { Shape: WorkflowTableShape.IdList } && manifest.SpecVersion >= 25)
        {
            errors.Add("snippets is a list of ids, which requires specVersion 28.");
        }

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.Description is not null)
            {
                errors.Add($"Node '{node.Id}' declares description, which requires specVersion 28.");
            }
        }
    }
}
