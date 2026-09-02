namespace Consultologist.Web.Tests;

/// <summary>
/// The engine catalog's schema table for the editor round-trip proofs — the
/// same real bundled catalog the API tests validate against, so a composed
/// v12 manifest carrying a check node meets the concept-list rule exactly as
/// it would at the registry.
/// </summary>
public static class EditorCatalogSchemas
{
    public static readonly IReadOnlyDictionary<string, string> CatalogSchemas = Load();

    public static string ConceptListSchema => CatalogSchemas["concept-list"];

    private static IReadOnlyDictionary<string, string> Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        var catalog = Consultologist.Api.Agents.OutputContractCatalog.Load(Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents"));

        return catalog.Entries.Values
            .Where(entry => entry.SchemaJson != null)
            .ToDictionary(entry => entry.ContractId, entry => entry.SchemaJson!, StringComparer.Ordinal);
    }
}
