using System.Text.Json.Nodes;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// v28 (#1285, package-format-v28-design.md § 1–§ 3): the editor's 28 spelling. The
/// composer edits the macro and snippet tables as the ≤27 map (id → path, a trailing
/// '/' a folder); <see cref="OpenTables"/> turns a loaded id list into that map and
/// <see cref="CloseTables"/> turns it back — and drops every node's prompt, which a 28
/// manifest never names. <see cref="Index"/> is a folder's index object.
/// </summary>
public static class Composer28
{
    private static readonly string[] Tables = { "macros", "snippets" };

    /// <summary>Every 28 id list opened into the map the composer writes, in its order.</summary>
    public static void OpenTables(JsonObject root)
    {
        foreach (var key in Tables)
        {
            if (Get(root, key) is not (string name, JsonArray list))
            {
                continue;
            }

            var map = new JsonObject();
            foreach (var entry in list.OfType<JsonValue>())
            {
                if (entry.TryGetValue<string>(out var id))
                {
                    var (entryId, path) = PF.WorkflowTableIds.Entry(key, id);
                    map[entryId] = path;
                }
            }

            root[name] = map;
        }
    }

    /// <summary>Every table map closed into the 28 id list; every node's prompt dropped.</summary>
    public static void CloseTables(JsonObject root)
    {
        foreach (var key in Tables)
        {
            if (Get(root, key) is not (string name, JsonObject map))
            {
                continue;
            }

            var list = new JsonArray();
            foreach (var (id, value) in map)
            {
                var path = value is JsonValue v && v.TryGetValue<string>(out var p) ? p : string.Empty;
                list.Add(PF.WorkflowTableIds.EntryOf(id, path));
            }

            root[name] = list;
        }

        if (Get(root, "nodes") is (_, JsonArray nodes))
        {
            foreach (var node in nodes.OfType<JsonObject>())
            {
                node.Remove("prompt");
                node.Remove("Prompt");
            }
        }
    }

    /// <summary>A folder's 28 index: <c>{ "label"?, "items": [ids] }</c>, the label written only when set.</summary>
    public static string Index(IEnumerable<string> itemIds, string? label)
    {
        var index = new JsonObject();
        if (!string.IsNullOrWhiteSpace(label))
        {
            index["label"] = label;
        }

        index["items"] = new JsonArray(itemIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
        return index.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) + "\n";
    }

    private static (string Name, JsonNode Node)? Get(JsonObject root, string key)
    {
        var pascal = char.ToUpperInvariant(key[0]) + key[1..];
        return root[key] is { } node ? (key, node) : root[pascal] is { } p ? (pascal, p) : null;
    }
}
