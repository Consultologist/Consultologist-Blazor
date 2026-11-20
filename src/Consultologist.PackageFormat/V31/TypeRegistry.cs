using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): the deployment's registry the type system reads: the
/// built-in formats and the agents catalogue's, each as its file's JSON, the catalogue's
/// <c>bindings.json</c> and <c>agents.json</c>, and the built-in renderer names. A package never
/// supplies these, so a file that is missing or malformed is the deployment's fault and throws
/// <see cref="FormatException"/> instead of being refused.
/// </summary>
public sealed record TypeRegistry(
    IReadOnlyDictionary<string, JsonObject> BuiltInFormats,
    IReadOnlyDictionary<string, JsonObject> CatalogueFormats,
    JsonObject Bindings,
    JsonObject Agents,
    IReadOnlySet<string> BuiltInRenderers)
{
    /// <summary>The three name sets the source reader checks a package's names against.</summary>
    public SourceRegistry Names { get; } = new(
        BuiltInFormats.Keys.ToHashSet(StringComparer.Ordinal),
        CatalogueFormats.Keys.ToHashSet(StringComparer.Ordinal),
        BuiltInRenderers);

    /// <summary>
    /// Reads the registry from the published files: the two <c>formats/</c> folders (each an
    /// <c>index.json</c> naming its items, one <c>&lt;name&gt;.json</c> per item), the renderers'
    /// <c>index.json</c>, and the catalogue's <c>bindings.json</c> and <c>agents.json</c>.
    /// </summary>
    /// <exception cref="FormatException">A file is missing, is not JSON, or is not what it should be.</exception>
    public static TypeRegistry Read(
        string builtInFormatsFolder,
        string catalogueFormatsFolder,
        string renderersIndexPath,
        string bindingsPath,
        string agentsPath) => new(
            ReadFormats(builtInFormatsFolder),
            ReadFormats(catalogueFormatsFolder),
            ReadObject(bindingsPath),
            ReadObject(agentsPath),
            ReadItems(renderersIndexPath).ToHashSet(StringComparer.Ordinal));

    private static Dictionary<string, JsonObject> ReadFormats(string folder)
    {
        var formats = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var name in ReadItems(Path.Combine(folder, "index.json")))
        {
            formats[name] = ReadObject(Path.Combine(folder, name + ".json"));
        }

        return formats;
    }

    private static List<string> ReadItems(string indexPath)
    {
        if (ReadObject(indexPath)["items"] is not JsonArray items)
        {
            throw new FormatException($"Registry file '{indexPath}' has no items list.");
        }

        return items
            .Select(i => i is JsonValue v && v.TryGetValue<string>(out var s)
                ? s
                : throw new FormatException($"Registry file '{indexPath}' lists an item that is not text."))
            .ToList();
    }

    private static JsonObject ReadObject(string path)
    {
        if (!File.Exists(path))
        {
            throw new FormatException($"Registry file '{path}' is missing.");
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new FormatException($"Registry file '{path}' is not a JSON object.");
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new FormatException($"Registry file '{path}' is not JSON: {e.Message}", e);
        }
    }
}
