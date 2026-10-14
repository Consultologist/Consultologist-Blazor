using System.Text.Json;
using Consultologist.PackageFormat;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 (C1 #1007): the engine's real formats catalogue, as the editor would read it
/// from GET WorkflowFormats — built from the bundled agents catalogue (the same
/// files the API serves), described by the API's own Describe, and bound into the
/// client's view through the wire's JSON. So the picker tests see the catalogue the
/// production endpoint answers, parameters and all.
/// </summary>
public static class EditorFormatsCatalog
{
    public static readonly WorkflowFormatCatalog Catalog = Load();

    public static WorkflowFormatsView View() =>
        JsonSerializer.Deserialize<WorkflowFormatsView>(
            JsonSerializer.Serialize(Consultologist.Api.Workflow.WorkflowFormats.Describe(Catalog)),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    /// <summary>The accepted set with 25 — the A4 seam the round-trip proofs validate through until A5.</summary>
    public static IReadOnlyCollection<int> AcceptedWith25 => WorkflowPackageValidator.AcceptedSpecVersions.Append(25).ToList();

    private static WorkflowFormatCatalog Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        var agents = Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents");
        var errors = new List<string>();
        if (!WorkflowFormatCatalogIndexJson.TryRead(File.ReadAllText(Path.Combine(agents, "formats.json")), "Format catalogue", errors, out var index))
        {
            throw new InvalidOperationException(string.Join(" | ", errors));
        }

        var catalog = WorkflowFormatCatalog.Build(index!, path => File.Exists(Path.Combine(agents, path)) ? File.ReadAllText(Path.Combine(agents, path)) : null, errors);
        if (catalog is null || errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" | ", errors));
        }

        return catalog;
    }
}
