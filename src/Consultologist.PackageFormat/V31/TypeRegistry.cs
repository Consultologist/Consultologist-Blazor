using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): the deployment's registry the type system reads: the
/// built-in formats and the agents catalogue's, each as its file's JSON, the catalogue's
/// <c>bindings.json</c> and <c>agents.json</c>, and the built-in renderers (#1504, D6: each with its header and
/// body, as <c>resolve.json</c> embeds them). A package never
/// supplies these, so a file that is missing or malformed is the deployment's fault and throws
/// <see cref="FormatException"/> instead of being refused.
/// </summary>
public sealed record TypeRegistry(
    IReadOnlyDictionary<string, JsonObject> BuiltInFormats,
    IReadOnlyDictionary<string, JsonObject> CatalogueFormats,
    JsonObject Bindings,
    JsonObject Agents,
    IReadOnlyDictionary<string, BuiltInRenderer> BuiltInRenderers)
{
    /// <summary>The ids of the built-in renderers, which a package renderer may not take (S01-035).</summary>
    public IReadOnlySet<string> BuiltInRendererIds { get; } = BuiltInRenderers.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>The three name sets the source reader checks a package's names against.</summary>
    public SourceRegistry Names { get; } = new(
        BuiltInFormats.Keys.ToHashSet(StringComparer.Ordinal),
        CatalogueFormats.Keys.ToHashSet(StringComparer.Ordinal),
        BuiltInRenderers.Keys.ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// Reads the registry from the published files: the two <c>formats/</c> folders (each an
    /// <c>index.json</c> naming its items, one <c>&lt;name&gt;.json</c> per item), the renderers'
    /// <c>index.json</c> and each <c>&lt;id&gt;.md</c> beside it (its JSON header and its body, at the version
    /// <c>lock.json</c>'s <c>builtins</c> writes for it), and the catalogue's <c>bindings.json</c> and <c>agents.json</c>.
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
            ReadRenderers(renderersIndexPath));

    private static Dictionary<string, BuiltInRenderer> ReadRenderers(string indexPath)
    {
        var folder = Path.GetDirectoryName(indexPath) ?? string.Empty;
        var renderers = new Dictionary<string, BuiltInRenderer>(StringComparer.Ordinal);
        foreach (var id in ReadItems(indexPath))
        {
            var path = Path.Combine(folder, id + ".md");
            if (!File.Exists(path))
            {
                throw new FormatException($"Registry file '{path}' is missing.");
            }

            var split = FrontMatter.Split(File.ReadAllText(path).ReplaceLineEndings("\n"))
                ?? throw new FormatException($"Registry file '{path}' has no header.");
            JsonObject header;
            try
            {
                header = JsonNode.Parse(split.Header) as JsonObject
                    ?? throw new FormatException($"Registry file '{path}' has a header that is not a JSON object.");
            }
            catch (System.Text.Json.JsonException e)
            {
                throw new FormatException($"Registry file '{path}' has a header that is not JSON: {e.Message}", e);
            }

            if (!LockCompiler.BuiltinVersions.TryGetValue(id, out var version))
            {
                throw new FormatException($"Registry renderer '{id}' has no version this library writes in lock.json.");
            }

            renderers[id] = new BuiltInRenderer(id, version, header, split.Body);
        }

        return renderers;
    }

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

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 5.3; D6): a built-in renderer of the package-format registry: its id, its
/// version (as <c>lock.json</c>'s <c>builtins</c> writes it), its header (<c>label</c>, <c>kind</c>, <c>description</c>,
/// <c>input</c>, <c>format</c>) and its template body, as a package renderer has them.
/// </summary>
public sealed record BuiltInRenderer(string Id, string Version, JsonObject Header, string Body);
