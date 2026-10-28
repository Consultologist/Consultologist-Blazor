using System.Text.Json;
using System.Text.Json.Nodes;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// v25 C2 (#1008): the engine's own template walk, run in the editor over the
/// effective package — the composed manifest, the assembled files and the
/// catalogue the API served, bound back into the format library's
/// <see cref="PF.WorkflowFormatCatalog"/>. The Snippets pane lists where a
/// snippet is read from it; C4's Reads list and "every reference resolves"
/// desk read the same walk. Null when the package does not parse — the desk
/// says why in its own sentences; this never does.
/// </summary>
public static class EditorPackageWalk
{
    /// <summary>
    /// A permissive read: the editor composes keys the registry accepts, and a
    /// key the library's record lacks must not make the whole walk vanish.
    /// </summary>
    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The catalogue as the API describes it (GET WorkflowFormats), bound back
    /// into the library's catalogue: each entry's spec from its described
    /// fields, its index entry from the agent or op it names. The canonical
    /// JSON (the hash's bytes) is not served and not needed for a walk — it is
    /// left empty. Null for no catalogue.
    /// </summary>
    public static PF.WorkflowFormatCatalog? CatalogFrom(WorkflowFormatsView? view)
    {
        if (view?.Formats is null)
        {
            return null;
        }

        var formats = new Dictionary<string, PF.WorkflowFormatCatalogFormat>(StringComparer.Ordinal);

        foreach (var entry in view.Formats)
        {
            if (string.IsNullOrWhiteSpace(entry.Id) || entry.Type is null || entry.Producer is null)
            {
                continue;
            }

            var parameters = entry.Parameters is { Count: > 0 }
                ? entry.Parameters
                    .Where(pair => pair.Value.Kind is not null)
                    .ToDictionary(pair => pair.Key, pair => new PF.WorkflowFormatParameterSpec(pair.Value.Kind!, pair.Value.Format), StringComparer.Ordinal)
                : null;

            var spec = new PF.WorkflowFormatSpec(
                entry.Type,
                entry.Producer,
                entry.OutputMacros ? true : null,
                entry.Description,
                entry.Schema is { } schema ? JsonNode.Parse(schema.GetRawText()) : null,
                entry.Values?.ToList(),
                parameters is { Count: > 0 } ? parameters : null);

            formats[entry.Id] = new PF.WorkflowFormatCatalogFormat(
                entry.Id,
                spec,
                string.Empty,
                new PF.WorkflowFormatCatalogEntry(entry.File ?? string.Empty, entry.AgentName, entry.AgentVersion, entry.Op));
        }

        return new PF.WorkflowFormatCatalog(
            view.Version ?? string.Empty,
            formats,
            new PF.WorkflowFormatCatalogAgentBinding(view.Custom?.AgentName ?? string.Empty, view.Custom?.AgentVersion ?? 0, string.Empty));
    }

    /// <summary>
    /// The walk of a package: every node's prompt under its fan, every snippet
    /// and folder item, every macro text (WorkflowDerivedEdges.WalkAll). Null
    /// when the manifest does not read as one.
    /// </summary>
    public static PF.WorkflowDerivedEdges.Walk? Walk(string manifestJson, IReadOnlyDictionary<string, string> files, PF.WorkflowFormatCatalog? catalogue)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(manifestJson, ManifestOptions);

            if (manifest is null)
            {
                return null;
            }

            // v26 (#1207; a #1099 leftover): the macro folders resolved ride the walk, so a macro
            // folder's items are walked as macro texts keyed <folder>/<item> in the editor too —
            // the item pane's reads, its readers and the direct read's target.
            var nodes = PF.WorkflowDerivedEdges.PrepareNodes(manifest, files, catalogue, out var snippets, out var contextFor, out var macroFolders);
            return PF.WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor, macros: macroFolders);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
