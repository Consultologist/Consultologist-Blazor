using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1283 (package-format-v28-design.md § 8): a 27 package rewritten to 28 — a
/// file's name becomes its id. Every node prompt, macro, snippet and folder
/// item moves to the path its id implies (a prompt several nodes share is
/// copied to each), the manifest stops spelling paths (no <c>prompt</c>;
/// <c>macros</c> and <c>snippets</c> lists of ids), and every folder index
/// becomes <c>{ "items": [...] }</c> in its 27 order, with no label invented.
/// The targets are computed from the 27 files, never moved one by one, so a
/// swap or a chain of renames cannot collide with itself; a target an unnamed
/// file already holds with different text refuses the migration naming both.
/// Shared by the Editor's upgrade and <c>scripts/migrate-workflow-package.cs --to 28</c>;
/// the caller validates.
/// </summary>
public static class WorkflowPackageMigration27To28
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Migration26Result Migrate(string manifestJson, IReadOnlyDictionary<string, string> files)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(manifestJson)?.AsObject() ?? throw new JsonException("The manifest is not a JSON object.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return Refused(manifestJson, files, $"The manifest does not read as a package: {ex.Message}");
        }

        var version = Get(root, "specVersion") is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
        if (version >= 28)
        {
            return Refused(manifestJson, files, $"The package is already at specVersion {version}.");
        }

        if (version < 27)
        {
            return Refused(manifestJson, files, $"Migrate to specVersion 27 first (the package is at {version}; scripts/migrate-workflow-package.cs --to 27).");
        }

        var refusals = new List<string>();
        var notices = new List<string>();
        // source path → the target paths it moves to (a shared prompt has several).
        var moves = new List<(string From, string To)>();
        var rewrittenIndexes = new Dictionary<string, (string From, string To, List<string> Items)>(StringComparer.Ordinal);

        // ----- the node prompts: each node its own prompts/<id>.md -----
        var prompted = new List<(JsonObject Node, string Id, string Path)>();
        foreach (var node in Array(root, "nodes").OfType<JsonObject>())
        {
            if (Str(node, "id") is not { } id || Str(node, "prompt") is not { } path)
            {
                continue;
            }

            prompted.Add((node, id, path));
            if (!files.ContainsKey(path))
            {
                refusals.Add($"Node '{id}' prompt '{path}' is missing from the package; there is nothing to move.");
                continue;
            }

            moves.Add((path, WorkflowImpliedPaths.PromptOf(id)));
        }

        foreach (var shared in prompted.GroupBy(p => p.Path, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            notices.Add($"{shared.Key} was shared by {string.Join(", ", shared.Select(p => p.Id))}; each node now has its own copy ({string.Join(", ", shared.Select(p => WorkflowImpliedPaths.PromptOf(p.Id)))}).");
        }

        // ----- the tables: a file to <kind>/<id>.md, a folder to <kind>/<id>/ with its items -----
        var lists = new Dictionary<string, JsonArray>(StringComparer.Ordinal);
        foreach (var (table, directory, noun) in new[] { ("macros", "macros", "Macro"), ("snippets", "snippets", "Snippet") })
        {
            var node = Get(root, table);
            if (node is null)
            {
                continue;
            }

            if (node is not JsonObject map)
            {
                refusals.Add($"{table} is not the 27 map of id to path; there is nothing to respell.");
                continue;
            }

            var list = new JsonArray();
            foreach (var (id, value) in map)
            {
                if (value is not JsonValue pathValue || !pathValue.TryGetValue<string>(out var path))
                {
                    refusals.Add($"{table} '{id}' is not a path.");
                    continue;
                }

                if (!path.EndsWith('/'))
                {
                    list.Add(id);
                    if (files.ContainsKey(path))
                    {
                        moves.Add((path, $"{directory}/{id}.md"));
                    }

                    continue;
                }

                list.Add(id + "/");
                var target = $"{directory}/{id}/";
                var indexPath = path + WorkflowDataResolver.IndexFileName;
                if (!files.TryGetValue(indexPath, out var indexJson))
                {
                    continue;
                }

                var errors = new List<string>();
                if (WorkflowTextHeaders.ReadIndex(indexJson, id, noun, 27, errors) is not { } index)
                {
                    refusals.AddRange(errors);
                    continue;
                }

                foreach (var (itemId, file) in index.Items)
                {
                    if (files.ContainsKey(path + file))
                    {
                        moves.Add((path + file, target + itemId + ".md"));
                    }
                }

                rewrittenIndexes[indexPath] = (indexPath, target + WorkflowDataResolver.IndexFileName, index.Items.Select(i => i.Id).ToList());
            }

            lists[table] = list;
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        // ----- the file set: every named source out, every target in -----
        var sources = moves.Select(m => m.From).Concat(rewrittenIndexes.Keys).ToHashSet(StringComparer.Ordinal);
        var outFiles = files.Where(f => !sources.Contains(f.Key)).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal);
        var placed = new Dictionary<string, string>(StringComparer.Ordinal);

        void Place(string from, string to, string text)
        {
            if (placed.TryGetValue(to, out var already))
            {
                if (already != text)
                {
                    refusals.Add($"Both {from} and another file would become {to}, with different text; rename one, then upgrade.");
                }

                return;
            }

            if (outFiles.TryGetValue(to, out var existing) && existing != text)
            {
                refusals.Add($"{from} would become {to}, which the package already holds with different text; rename or remove {to}, then upgrade.");
                return;
            }

            placed[to] = text;
            outFiles[to] = text;
        }

        foreach (var (from, to) in moves)
        {
            Place(from, to, files[from]);
        }

        foreach (var (from, to, items) in rewrittenIndexes.Values)
        {
            Place(from, to, new JsonObject { ["items"] = new JsonArray(items.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()) }.ToJsonString(WriteOptions) + "\n");
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        // ----- the manifest: no prompt, the tables as lists, the version -----
        foreach (var (node, _, _) in prompted)
        {
            Remove(node, "prompt");
        }

        foreach (var (table, list) in lists)
        {
            Set(root, table, list);
        }

        Set(root, "specVersion", 28);

        var moved = moves.Where(m => m.From != m.To).ToList();
        foreach (var (from, to) in moved)
        {
            notices.Add($"{from} → {to}");
        }

        foreach (var (from, to, items) in rewrittenIndexes.Values)
        {
            notices.Add($"{from}{(from == to ? "" : $" → {to}")}: {{ \"items\": [{string.Join(", ", items.Select(i => $"\"{i}\""))}] }} (no label; add one in the Editor).");
        }

        notices.Add($"{moved.Count} file{(moved.Count == 1 ? "" : "s")} renamed to {(moved.Count == 1 ? "its" : "their")} id{(moved.Count == 1 ? "" : "s")}; {prompted.Count} node{(prompted.Count == 1 ? "" : "s")} name no prompt now; macros and snippets are lists of ids.");

        return new Migration26Result(root.ToJsonString(WorkflowPackageManifestJson.WriteOptions), outFiles, notices, System.Array.Empty<string>());
    }

    private static Migration26Result Refused(string manifestJson, IReadOnlyDictionary<string, string> files, string refusal) =>
        Refused(manifestJson, files, new[] { refusal });

    private static Migration26Result Refused(string manifestJson, IReadOnlyDictionary<string, string> files, IReadOnlyList<string> refusals) =>
        new(manifestJson, new Dictionary<string, string>(files, StringComparer.Ordinal), System.Array.Empty<string>(), refusals);

    private static string Pascal(string key) => char.ToUpperInvariant(key[0]) + key[1..];

    private static JsonNode? Get(JsonObject o, string key) => o.TryGetPropertyValue(key, out var v) ? v : o.TryGetPropertyValue(Pascal(key), out var p) ? p : null;

    private static string? Str(JsonObject o, string key) => Get(o, key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static IEnumerable<JsonNode?> Array(JsonObject o, string key) => Get(o, key) is JsonArray a ? a : Enumerable.Empty<JsonNode?>();

    private static void Set(JsonObject o, string key, JsonNode? value)
    {
        var target = o.ContainsKey(key) ? key : o.ContainsKey(Pascal(key)) ? Pascal(key) : key;
        o[target] = value;
    }

    private static void Remove(JsonObject o, string key)
    {
        o.Remove(key);
        o.Remove(Pascal(key));
    }
}
