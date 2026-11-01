using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1267: a 26/27 file renamed so its name is its id — the file moved and whichever site names
/// it updated: a node's <c>prompt</c>, a <c>macros</c>/<c>snippets</c> map value, or a folder item's
/// <c>file</c> in its folder's <c>index.json</c> (order kept). No read changes: templates read by id.
/// </summary>
public static class WorkflowFileMove
{
    /// <summary>A file named differently from what its id implies.</summary>
    public sealed record Mismatch(string Kind, string Id, string Path, string Implied);

    /// <summary>What the id implies: prompts/, macros/ or snippets/ + &lt;id&gt;.md, or the folder + &lt;id&gt;.md.</summary>
    public static string ImpliedPath(string kind, string id, string? folderDirectory = null) => kind switch
    {
        "node" => $"prompts/{id}.md",
        "macro" => $"macros/{id}.md",
        "snippet" => $"snippets/{id}.md",
        _ => $"{folderDirectory}{id}.md",
    };

    /// <summary>Every file at 26+ named differently from its id, minus a prompt shared by two nodes.</summary>
    public static IReadOnlyList<Mismatch> Mismatches(JsonObject manifest, IReadOnlyDictionary<string, string> files)
    {
        var found = new List<Mismatch>();
        var prompts = PromptUse(manifest);
        if (manifest["nodes"] is JsonArray nodes)
        {
            foreach (var node in nodes.OfType<JsonObject>())
            {
                if (Str(node, "id") is { } id && Str(node, "prompt") is { } path && prompts[path] == 1 && path != ImpliedPath("node", id))
                {
                    found.Add(new("node", id, path, ImpliedPath("node", id)));
                }
            }
        }

        foreach (var (table, kind) in new[] { ("macros", "macro"), ("snippets", "snippet") })
        {
            if (manifest[table] is not JsonObject map)
            {
                continue;
            }

            foreach (var (id, value) in map)
            {
                if (value is not JsonValue v || !v.TryGetValue<string>(out var path))
                {
                    continue;
                }

                if (path.EndsWith('/'))
                {
                    foreach (var (itemId, file) in FolderItems(files, path))
                    {
                        var itemPath = path + file;
                        var implied = ImpliedPath("item", itemId, path);
                        if (itemPath != implied)
                        {
                            found.Add(new($"{kind} folder item", $"{id}/{itemId}", itemPath, implied));
                        }
                    }
                }
                else if (path != ImpliedPath(kind, id))
                {
                    found.Add(new(kind, id, path, ImpliedPath(kind, id)));
                }
            }
        }

        return found;
    }

    /// <summary>Why <paramref name="from"/> cannot move to <paramref name="to"/>; null when it can.</summary>
    public static string? Refusal(JsonObject manifest, IReadOnlyDictionary<string, string> files, string from, string to)
    {
        if (!files.ContainsKey(from))
        {
            return $"There is no file '{from}'.";
        }

        if (files.ContainsKey(to))
        {
            return $"A file '{to}' already exists.";
        }

        var users = PromptUsers(manifest, from);
        return users.Count > 1 ? $"The prompt is shared by {string.Join(", ", users)} — it keeps its name." : null;
    }

    /// <summary>The file moved and every site naming it updated; false when nothing named it.</summary>
    public static bool Apply(JsonObject manifest, IDictionary<string, string> files, string from, string to)
    {
        if (!files.TryGetValue(from, out var text) || files.ContainsKey(to))
        {
            return false;
        }

        var named = false;
        if (manifest["nodes"] is JsonArray nodes)
        {
            foreach (var node in nodes.OfType<JsonObject>().Where(n => Str(n, "prompt") == from))
            {
                node["prompt"] = to;
                named = true;
            }
        }

        foreach (var table in new[] { "macros", "snippets" })
        {
            if (manifest[table] is not JsonObject map)
            {
                continue;
            }

            foreach (var id in map.Select(p => p.Key).ToList())
            {
                if (map[id] is not JsonValue v || !v.TryGetValue<string>(out var path))
                {
                    continue;
                }

                if (path == from)
                {
                    map[id] = to;
                    named = true;
                }
                else if (path.EndsWith('/') && from.StartsWith(path, StringComparison.Ordinal) && to.StartsWith(path, StringComparison.Ordinal)
                    && files.TryGetValue(path + "index.json", out var indexJson) && JsonNode.Parse(indexJson) is JsonArray index)
                {
                    var changed = false;
                    foreach (var item in index.OfType<JsonObject>().Where(i => path + Str(i, "file") == from))
                    {
                        item["file"] = to[path.Length..];
                        changed = true;
                    }

                    if (changed)
                    {
                        files[path + "index.json"] = index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
                        named = true;
                    }
                }
            }
        }

        if (!named)
        {
            return false;
        }

        files.Remove(from);
        files[to] = text;
        return true;
    }

    private static IEnumerable<(string Id, string File)> FolderItems(IReadOnlyDictionary<string, string> files, string directory)
    {
        if (!files.TryGetValue(directory + "index.json", out var json))
        {
            yield break;
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        if (parsed is not JsonArray index)
        {
            yield break;
        }

        foreach (var item in index.OfType<JsonObject>())
        {
            if (Str(item, "id") is { } id && Str(item, "file") is { } file)
            {
                yield return (id, file);
            }
        }
    }

    private static Dictionary<string, int> PromptUse(JsonObject manifest)
    {
        var use = new Dictionary<string, int>(StringComparer.Ordinal);
        if (manifest["nodes"] is JsonArray nodes)
        {
            foreach (var path in nodes.OfType<JsonObject>().Select(n => Str(n, "prompt")).OfType<string>())
            {
                use[path] = use.GetValueOrDefault(path) + 1;
            }
        }

        return use;
    }

    private static List<string> PromptUsers(JsonObject manifest, string path) =>
        manifest["nodes"] is JsonArray nodes
            ? nodes.OfType<JsonObject>().Where(n => Str(n, "prompt") == path).Select(n => Str(n, "id")).OfType<string>().ToList()
            : new List<string>();

    private static string? Str(JsonObject owner, string key) =>
        owner[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}
