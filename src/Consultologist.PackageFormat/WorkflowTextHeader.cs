using System.Text.Json;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1105 (package-format-v26-design.md § 5): what a snippet, folder item or
/// macro file says about itself — the label, the gate, and for a macro the
/// instruction and the option — read from the JSON header the file opens
/// with. The manifest keeps the id, the order, the placements and the nodes.
/// </summary>
public sealed record WorkflowTextHeader(
    string? Label,
    string? If = null,
    string? Instruction = null,
    bool? Optional = null,
    bool? Default = null);

/// <summary>
/// v28 (#1283, design § 3): a folder's index as read — the items in run order,
/// each (id, file relative to the folder), and the folder's display label
/// (28 only; never read by a template).
/// </summary>
public sealed record WorkflowFolderIndex(IReadOnlyList<(string Id, string File)> Items, string? Label);

/// <summary>The kind of file a header is read for — the key set differs.</summary>
public enum WorkflowTextKind
{
    Snippet,
    Macro,
}

/// <summary>
/// A 26 package's texts resolved: the manifest with each macro's header facts
/// filled in (what the shared steps read as <c>macros[].label</c> and
/// <c>.instruction</c>), the files with every header stripped (what Scriban
/// sees), and the headers by file path.
/// </summary>
public sealed record WorkflowResolvedTexts(
    WorkflowPackageManifest Manifest,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyDictionary<string, WorkflowTextHeader> Headers);

/// <summary>
/// The JSON header (§ 5.1): the first line of the file is exactly <c>{</c>,
/// the header ends at the first line that is exactly <c>}</c>, between them is
/// one JSON object; the text is everything after the closing line. A BOM and
/// CRLF are tolerated. A Scriban line opens with <c>{{</c>, so a template body
/// is never mistaken for a header. The key set is closed per kind; the
/// reserved object members are refused by name. Scriban never sees the
/// header; files hash whole, header included.
/// </summary>
public static class WorkflowTextHeaders
{
    private static readonly IReadOnlyList<string> SnippetKeys = new[] { "label", "if" };
    private static readonly IReadOnlyList<string> MacroKeys = new[] { "label", "instruction", "optional", "default", "if" };
    private static readonly IReadOnlySet<string> Reserved = new HashSet<string>(StringComparer.Ordinal) { "id", "file", "content", "value", "prompt" };

    private static readonly System.Text.Json.JsonSerializerOptions HeaderValueJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// The header text a 26 file opens with (§ 5.1): the first line `{`, one key per present
    /// fact in the order label, instruction, optional, default, if, the last line `}` — the
    /// migration's bytes (#1152), composed here so the editor (#1163) writes the same header.
    /// A null label writes none; the validator names the missing label.
    /// </summary>
    public static string Compose(WorkflowTextHeader header)
    {
        var lines = new List<string>();
        if (header.Label is not null)
        {
            lines.Add($"  \"label\": {System.Text.Json.JsonSerializer.Serialize(header.Label, HeaderValueJson)}");
        }

        if (header.Instruction is not null)
        {
            lines.Add($"  \"instruction\": {System.Text.Json.JsonSerializer.Serialize(header.Instruction, HeaderValueJson)}");
        }

        if (header.Optional is not null)
        {
            lines.Add($"  \"optional\": {(header.Optional.Value ? "true" : "false")}");
        }

        if (header.Default is not null)
        {
            lines.Add($"  \"default\": {(header.Default.Value ? "true" : "false")}");
        }

        if (header.If is not null)
        {
            lines.Add($"  \"if\": {System.Text.Json.JsonSerializer.Serialize(header.If, HeaderValueJson)}");
        }

        return "{\n" + string.Join(",\n", lines) + "\n}\n";
    }

    /// <summary>The sentence a snippet or macro file with no header is refused with — the editor shows it on the pane (#1163).</summary>
    public static string MissingHeaderSentence(WorkflowTextKind kind, string path)
    {
        var noun = kind == WorkflowTextKind.Macro ? "macro" : "snippet";
        return $"{path} has no header; a {noun} file opens with {{ \"label\": … }}.";
    }

    /// <summary>The header's JSON and the body, or (null, text) when the file opens with no header.</summary>
    public static (string? HeaderJson, string Body) Split(string text)
    {
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var lines = text.Split('\n');
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != "{")
        {
            return (null, text);
        }

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd('\r') == "}")
            {
                var header = string.Join("\n", lines.Take(i + 1).Select(l => l.TrimEnd('\r')));
                var body = string.Join("\n", lines.Skip(i + 1));
                return (header, body);
            }
        }

        // Opened, never closed: the whole text is the header, which Parse refuses.
        return (text, string.Empty);
    }

    /// <summary>True when the file opens with a header line (the strip would apply).</summary>
    public static bool Opens(string text) => Split(text).HeaderJson is not null;

    /// <summary>
    /// The header parsed for its kind; every refusal names the file. Null when
    /// the JSON is not one object (the sentences say why); otherwise the record,
    /// its keys checked — a missing label is said and the record still returned.
    /// </summary>
    public static WorkflowTextHeader? Parse(WorkflowTextKind kind, string path, string headerJson, List<string> errors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(headerJson);
        }
        catch (JsonException ex)
        {
            errors.Add($"{path} header is not a JSON object: {ex.Message}");
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{path} header is not a JSON object.");
                return null;
            }

            var keys = kind == WorkflowTextKind.Macro ? MacroKeys : SnippetKeys;
            var noun = kind == WorkflowTextKind.Macro ? "macro" : "snippet";
            string? label = null, @if = null, instruction = null;
            bool? optional = null, @default = null;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                var key = property.Name;

                if (Reserved.Contains(key))
                {
                    errors.Add($"{path} header declares '{key}', a reserved name; a header carries label, if, instruction, optional and default.");
                    continue;
                }

                if (!keys.Contains(key, StringComparer.Ordinal))
                {
                    errors.Add($"{path} header declares '{key}', which is not a header key (a {noun} file's header carries {string.Join(", ", keys)}).");
                    continue;
                }

                switch (key)
                {
                    case "label":
                        label = String(property, "label");
                        break;
                    case "if":
                        @if = String(property, "if");
                        break;
                    case "instruction":
                        instruction = String(property, "instruction");
                        break;
                    case "optional":
                        optional = Boolean(property, "optional");
                        break;
                    case "default":
                        @default = Boolean(property, "default");
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                errors.Add($"{path} header declares no label; a {noun} file's header names its label.");
            }

            return new WorkflowTextHeader(label, @if, instruction, optional, @default);

            string? String(JsonProperty property, string key)
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                errors.Add($"{path} header '{key}' must be a string.");
                return null;
            }

            bool? Boolean(JsonProperty property, string key)
            {
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return property.Value.GetBoolean();
                }

                errors.Add($"{path} header '{key}' must be a boolean.");
                return null;
            }
        }
    }

    /// <summary>
    /// v28 (#1283, design § 3, § 6): a folder's index read for the version —
    /// below 28 the ordered list <c>[{ id, file }]</c> of <see cref="ReadIndexAt26"/>,
    /// at 28 the object <c>{ "label"?, "items": [ids] }</c>, each item's file
    /// <c>&lt;id&gt;.md</c> beside the index. Null when the index cannot be read.
    /// The sentences name the folder; duplicates and an empty list are the
    /// resolver's, as at 26.
    /// </summary>
    public static WorkflowFolderIndex? ReadIndex(string indexJson, string folder, string noun, int specVersion, List<string> errors)
    {
        if (specVersion < 28)
        {
            return ReadIndexAt26(indexJson, folder, noun, errors) is { } listed ? new WorkflowFolderIndex(listed, null) : null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(indexJson);
        }
        catch (JsonException ex)
        {
            errors.Add($"{noun} collection '{folder}' index.json is not valid JSON: {ex.Message}");
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                var ids = root.ValueKind == JsonValueKind.Array
                    ? root.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind == JsonValueKind.Object && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null).OfType<string>().ToList()
                    : new List<string>();
                errors.Add($"{noun} folder '{folder}' index.json at specVersion 28 is an object {{ \"label\"?, \"items\": [ids] }} — {{ \"items\": [{string.Join(", ", ids.Select(i => $"\"{i}\""))}] }}; each item's file is <id>.md beside the index.");
                return null;
            }

            string? label = null;
            JsonElement? itemsElement = null;
            var refused = false;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "label":
                        if (property.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.Value.GetString()))
                        {
                            errors.Add($"{noun} folder '{folder}' index.json label must be a non-empty string.");
                            refused = true;
                        }
                        else
                        {
                            label = property.Value.GetString();
                        }

                        break;
                    case "items":
                        itemsElement = property.Value.Clone();
                        break;
                    default:
                        errors.Add($"{noun} folder '{folder}' index.json declares '{property.Name}', which is not an index key at specVersion 28 (the keys are label and items).");
                        refused = true;
                        break;
                }
            }

            if (itemsElement is not { ValueKind: JsonValueKind.Array } array)
            {
                errors.Add($"{noun} folder '{folder}' index.json has no items; at specVersion 28 items lists the folder's item ids in order.");
                return null;
            }

            var items = new List<(string Id, string File)>();
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()))
                {
                    var id = element.GetString()!;
                    items.Add((id, id + ".md"));
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Object)
                {
                    var id = element.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String ? idElement.GetString() : null;
                    errors.Add($"{noun} folder '{folder}' index.json item {{ id, file }}{(id is null ? "" : $" '{id}'")} — at specVersion 28 an item is its id: \"items\": [\"{id ?? "hpi"}\"].");
                }
                else
                {
                    errors.Add($"{noun} folder '{folder}' index.json items must be ids (non-empty strings).");
                }

                refused = true;
            }

            return refused ? null : new WorkflowFolderIndex(items, label);
        }
    }

    /// <summary>
    /// A folder's index at 26 (§ 5.3): the ordered list <c>[{ id, file }]</c>.
    /// The 25 object shape, an item's <c>name</c> or <c>when</c>, and any other
    /// key are refused naming the header; null when the index is not a list.
    /// </summary>
    public static IReadOnlyList<(string Id, string File)>? ReadIndexAt26(string indexJson, string folder, string noun, List<string> errors)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(indexJson);
        }
        catch (JsonException ex)
        {
            errors.Add($"{noun} collection '{folder}' index.json is not valid JSON: {ex.Message}");
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{noun} folder '{folder}' index.json at specVersion 26 is an ordered list [{{ id, file }}]; name and when live in the item file's header.");
                return null;
            }

            var items = new List<(string Id, string File)>();

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"{noun} folder '{folder}' index.json has an item that is not an object ({{ id, file }}).");
                    continue;
                }

                string? id = null, file = null;
                var refused = false;

                foreach (var property in element.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "id":
                            id = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                            break;
                        case "file":
                            file = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
                            break;
                        case "name":
                            errors.Add($"{noun} folder '{folder}' item '{IdOf(element)}' declares name, which is retired at specVersion 26; the item file's header carries label.");
                            refused = true;
                            break;
                        case "when":
                            errors.Add($"{noun} folder '{folder}' item '{IdOf(element)}' declares when, which is retired at specVersion 26; a gate is keyed if in the item file's header; when is a case branch's label only.");
                            refused = true;
                            break;
                        default:
                            errors.Add($"{noun} folder '{folder}' item '{IdOf(element)}' declares '{property.Name}', which is not an index key at specVersion 26 (an item is {{ id, file }}; the rest is the item file's header).");
                            refused = true;
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(file))
                {
                    if (!refused)
                    {
                        errors.Add($"{noun} folder '{folder}' index.json has an item without an id or file.");
                    }

                    continue;
                }

                items.Add((id, file));
            }

            return items;
        }

        static string IdOf(JsonElement element) =>
            element.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "" : "";
    }

    /// <summary>
    /// The package's texts at 26: every snippet, folder item and macro file the
    /// manifest names is split and its header parsed for its kind (a file with
    /// no header is refused naming the kind; a missing file is left to the
    /// resolver and the macro library, which say so as at 25); a node's prompt
    /// file that opens with a header is refused. The manifest returned carries
    /// each macro's header facts; the files returned hold bodies.
    /// </summary>
    public static WorkflowResolvedTexts Resolve(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files, List<string> errors)
    {
        var bodies = new Dictionary<string, string>(files, StringComparer.Ordinal);
        var headers = new Dictionary<string, WorkflowTextHeader>(StringComparer.Ordinal);

        void Take(WorkflowTextKind kind, string path)
        {
            if (headers.ContainsKey(path) || !files.TryGetValue(path, out var text))
            {
                return;
            }

            var (headerJson, body) = Split(text);
            if (headerJson is null)
            {
                errors.Add(MissingHeaderSentence(kind, path));
                return;
            }

            var header = Parse(kind, path, headerJson, errors);
            bodies[path] = body;
            headers[path] = header ?? new WorkflowTextHeader(null);
        }

        foreach (var (id, path) in manifest.Snippets ?? new Dictionary<string, string>())
        {
            if (path.EndsWith('/'))
            {
                if (files.TryGetValue(path + WorkflowDataResolver.IndexFileName, out var indexJson)
                    && ReadIndex(indexJson, id, "Snippet", manifest.SpecVersion, new List<string>()) is { } index)
                {
                    foreach (var (_, file) in index.Items)
                    {
                        Take(WorkflowTextKind.Snippet, path + file);
                    }
                }
            }
            else
            {
                Take(WorkflowTextKind.Snippet, path);
            }
        }

        var macros = new List<WorkflowMacroSpec>();
        foreach (var macro in manifest.Macros ?? new List<WorkflowMacroSpec>())
        {
            // #1099 (§ 5.3): a macro folder — the index an ordered list, each item a
            // macro file with the macro key set (the kind follows the declaration).
            if (macro.File is { } dir && dir.EndsWith('/'))
            {
                if (files.TryGetValue(dir + WorkflowDataResolver.IndexFileName, out var indexJson)
                    && ReadIndex(indexJson, macro.Id, "Macro", manifest.SpecVersion, new List<string>()) is { } index)
                {
                    foreach (var (_, file) in index.Items)
                    {
                        Take(WorkflowTextKind.Macro, dir + file);
                    }
                }

                macros.Add(macro);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(macro.File))
            {
                Take(WorkflowTextKind.Macro, macro.File);
            }

            macros.Add(headers.TryGetValue(macro.File ?? "", out var header)
                ? macro with { Label = header.Label, Instruction = header.Instruction, Optional = header.Optional, Default = header.Default }
                : macro);
        }

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.Prompt is { } prompt && files.TryGetValue(prompt, out var text) && Opens(text))
            {
                errors.Add($"Node '{node.Id}' prompt '{prompt}' carries a header; a prompt file has no label, instruction or gate — the node's fields are in the manifest.");
            }
        }

        var effective = manifest.Macros is null ? manifest : manifest with { Macros = macros };
        return new WorkflowResolvedTexts(effective, bodies, headers);
    }

    /// <summary>
    /// #1099 (§ 5.3): the macro folders resolved through the shared resolver —
    /// the entries whose file ends with '/', as a table folder id → directory,
    /// with the noun "Macro" in every sentence; each item carries its header.
    /// Null when the package declares no folder.
    /// </summary>
    public static WorkflowPackageData? ResolveMacroFolders(WorkflowPackageManifest manifest, WorkflowResolvedTexts texts, List<string> errors)
    {
        var folders = (manifest.Macros ?? new List<WorkflowMacroSpec>())
            .Where(m => m.File is { } file && file.EndsWith('/'))
            .GroupBy(m => m.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().File, StringComparer.Ordinal);

        return folders.Count == 0 ? null : WorkflowDataResolver.Resolve(folders, "Macro", texts.Files, errors, texts.Headers, manifest.SpecVersion);
    }
}
