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
                var noun = kind == WorkflowTextKind.Macro ? "macro" : "snippet";
                errors.Add($"{path} has no header; a {noun} file opens with {{ \"label\": … }}.");
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
                    && ReadIndexAt26(indexJson, id, "Snippet", new List<string>()) is { } items)
                {
                    foreach (var (_, file) in items)
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
}
