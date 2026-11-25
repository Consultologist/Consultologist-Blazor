using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 1.1, D2): the stage to 24, moved unchanged from
/// <c>scripts/migrate-workflow-package.cs</c> (which calls it) so that <see cref="WorkflowPackageMigrationChain"/> runs
/// the rungs the script runs. It is the script's port of the Editor's stage (Templates.razor <c>MigratePreludes</c> and
/// <c>ComposeManifest</c>'s bump): below 24 formats are additive, so a package needs only its preludes made macros and
/// its number moved before the 24 → 25 rewriter (<see cref="WorkflowPackageMigration24To25"/>) takes it.
/// </summary>
public static class WorkflowPackageMigrationTo24
{
    /// <summary>
    /// The editor's stage to 24 (Templates.razor MigratePreludes + ComposeManifest):
    /// every prelude becomes a library macro composed before the prompts that
    /// named it (a bare node macro at index 0), and the number moves to 24 with
    /// tags present. Two deliberate differences from the editor: a hyphenated
    /// prelude id is snake_cased here (the 24 → 25 rewriter renames node, prompt,
    /// data and schema ids but not macro ids, and a macro id must be snake_case
    /// at 24), and a dropped prelude's readers lose their `prelude` too, so no
    /// prompt references a prelude that no longer exists.
    /// </summary>
    public static (List<string> Notices, List<string> Refusals) StageTo24(JsonObject root, Dictionary<string, string> files, int from)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(files);

        var notices = new List<string>();
        var refusals = new List<string>();

        if (Get(root, "preludes") is JsonObject preludes && preludes.Count > 0)
        {
            var prompts = Array(root, "prompts").OfType<JsonObject>().ToList();
            var nodes = Array(root, "nodes").OfType<JsonObject>().ToList();
            var macroIds = Array(root, "macros").OfType<JsonObject>().Select(m => Str(m, "id")).Where(id => id != null).ToHashSet(StringComparer.Ordinal)!;
            var renames = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var (id, _) in preludes.ToList())
            {
                var macroId = WorkflowDeclaredIds.IsValid(id) ? id : id.Replace('-', '_');
                if (!WorkflowDeclaredIds.IsValid(macroId))
                {
                    refusals.Add($"Prelude '{id}' needs a snake_case id to become a macro; rename it, then upgrade.");
                }
                else if (macroIds.Contains(macroId) || renames.ContainsValue(macroId))
                {
                    refusals.Add($"Prelude '{id}' has the id of a declared macro; rename one, then upgrade.");
                }
                else if (files.ContainsKey($"macros/{macroId}.md"))
                {
                    refusals.Add($"The package already has a file at 'macros/{macroId}.md'; move it, then upgrade.");
                }

                renames[id] = macroId;
            }

            if (refusals.Count > 0)
            {
                return (notices, refusals);
            }

            foreach (var (id, fileNode) in preludes.ToList())
            {
                var macroId = renames[id];
                var file = fileNode is JsonValue fv && fv.TryGetValue<string>(out var f) ? f : string.Empty;
                var readers = prompts.Where(p => string.Equals(Str(p, "prelude"), id, StringComparison.Ordinal)).ToList();
                var text = file.Length > 0 ? files.GetValueOrDefault(file) : null;

                if (readers.Count == 0)
                {
                    notices.Add($"Prelude '{id}' is used by no prompt and was dropped.");
                }
                else if (string.IsNullOrWhiteSpace(text))
                {
                    notices.Add($"Prelude '{id}' is empty and was dropped.");
                }
                else
                {
                    if (Get(root, "macros") is not JsonArray macros)
                    {
                        macros = new JsonArray();
                        Set(root, "macros", macros);
                    }

                    macros.Add(new JsonObject { ["id"] = macroId, ["label"] = macroId, ["file"] = $"macros/{macroId}.md" });
                    files[$"macros/{macroId}.md"] = text;

                    var homes = new List<string>();
                    foreach (var prompt in readers)
                    {
                        var promptId = Str(prompt, "id");
                        foreach (var node in nodes.Where(n => string.Equals(Str(n, "prompt"), promptId, StringComparison.Ordinal)))
                        {
                            if (Get(node, "macros") is not JsonArray list)
                            {
                                list = new JsonArray();
                                Set(node, "macros", list);
                            }

                            var present = list.Any(entry => entry switch
                            {
                                JsonValue value when value.TryGetValue<string>(out var s) => s == macroId,
                                JsonObject o => Str(o, "id") == macroId,
                                _ => false,
                            });
                            if (!present)
                            {
                                list.Insert(0, JsonValue.Create(macroId));
                            }

                            homes.Add(Str(node, "id") ?? "?");
                        }
                    }

                    notices.Add($"'{id}' → macro '{macroId}' before {(homes.Count == 0 ? "no node (its prompt is unused)" : string.Join(", ", homes.Select(h => $"'{h}'")))}.");
                    if (macroId != id)
                    {
                        notices.Add($"Prelude '{id}' became macro '{macroId}': a macro id is snake_case.");
                    }
                }

                foreach (var prompt in readers)
                {
                    Remove(prompt, "prelude");
                }

                if (file.Length > 0)
                {
                    files.Remove(file);
                }

                preludes.Remove(id);
            }

            if (preludes.Count == 0)
            {
                Remove(root, "preludes");
            }
        }

        if (from < 24)
        {
            Set(root, "specVersion", 24);
            if (Get(root, "tags") is null)
            {
                Set(root, "tags", new JsonArray());
            }

            notices.Add($"Staged specVersion {from} → 24 (the additive bumps: the number, and tags when absent).");
        }

        return (notices, refusals);
    }

    // The rewriter's casing-tolerant helpers: a manifest written by the worker may be PascalCase.
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
