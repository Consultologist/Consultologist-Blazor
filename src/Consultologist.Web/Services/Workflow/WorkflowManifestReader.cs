using System.Text.Json;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// Read-only views over the opaque manifest JsonElement for the editor's
/// display: which prompt texts and data items are editable, and the node
/// summary. Purely presentational — the manifest itself round-trips to the
/// publish endpoint untouched.
/// </summary>
public static class WorkflowManifestReader
{
    public sealed record PromptView(string Id, string File, IReadOnlyList<string> Variables, string? Prelude);

    /// <summary>One binding: a prompt variable, its source, and the optional concept renderer.</summary>
    public sealed record BindingView(string Variable, string From, string? As);

    public sealed record NodeView(
        string Id,
        string Label,
        string? Prompt,
        string? ForEach,
        IReadOnlyList<BindingView> Bindings,
        bool IsResult,
        bool HasOutput,
        IReadOnlyList<string>? Aggregate = null,
        string? OutputSchema = null,
        string? FailIfEmpty = null);

    /// <summary>
    /// One declared field of an object input (v9 § 4). A field holds a scalar,
    /// so it has no items and no fields of its own.
    /// </summary>
    public sealed record FieldView(
        string Id,
        string Label,
        bool Required,
        string? Type = null,
        IReadOnlyList<string>? Values = null);

    /// <summary>
    /// One declared input slot. v8 types it: <c>Type</c> null means text, which
    /// is what keeps a v7 declaration valid. <c>Values</c> belongs to enum.
    ///
    /// v9 (#429): <c>Items</c> is an array's element type and <c>Fields</c> an
    /// object's (or an array-of-object's) fields. Carried here because the
    /// composer rebuilds the whole inputs list from these records — without
    /// them, relabelling one input stripped every other's structure.
    /// </summary>
    public sealed record InputView(
        string Id,
        string Label,
        bool Required,
        string? Type = null,
        IReadOnlyList<string>? Values = null,
        string? Items = null,
        IReadOnlyList<FieldView>? Fields = null);

    /// <summary>
    /// One declared deliverable: authored id and label over an aggregator node.
    ///
    /// v8's <c>When</c> is held as the raw string rather than a parsed
    /// structure, so a row nobody edited round-trips exactly as authored — a
    /// manifest written elsewhere as <c>when: billable</c> is not rewritten to
    /// <c>billable == true</c> by an editor that only passed it through.
    /// </summary>
    public sealed record ResultView(string Id, string Node, string Label, string? When = null);

    public sealed record DataItemView(string Id, string Name, string File);

    public sealed record CollectionView(string Id, string Directory, IReadOnlyList<DataItemView> Items);

    public static IReadOnlyList<PromptView> ReadPrompts(JsonElement manifest)
    {
        var prompts = new List<PromptView>();

        if (!TryGetProperty(manifest, "prompts", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return prompts;
        }

        foreach (var prompt in array.EnumerateArray())
        {
            var variables = new List<string>();

            if (TryGetProperty(prompt, "variables", out var vars) && vars.ValueKind == JsonValueKind.Array)
            {
                variables.AddRange(vars.EnumerateArray().Select(v => v.GetString() ?? string.Empty));
            }

            prompts.Add(new PromptView(
                ReadString(prompt, "id") ?? string.Empty,
                ReadString(prompt, "file") ?? string.Empty,
                variables,
                ReadString(prompt, "prelude")));
        }

        return prompts;
    }

    public static IReadOnlyList<NodeView> ReadNodes(JsonElement manifest)
    {
        var nodes = new List<NodeView>();
        var resultNodeIds = ReadResultNodeIds(manifest);

        if (!TryGetProperty(manifest, "nodes", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return nodes;
        }

        foreach (var node in array.EnumerateArray())
        {
            var bindings = new List<BindingView>();

            if (TryGetProperty(node, "bindings", out var bindingsElement) && bindingsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var binding in bindingsElement.EnumerateObject())
                {
                    bindings.Add(ReadBinding(binding.Name, binding.Value));
                }
            }

            List<string>? aggregate = null;

            if (TryGetProperty(node, "aggregate", out var aggregateElement) && aggregateElement.ValueKind == JsonValueKind.Array)
            {
                aggregate = aggregateElement.EnumerateArray()
                    .Select(entry => entry.GetString() ?? string.Empty)
                    .Where(entry => entry.Length > 0)
                    .ToList();
            }

            var id = ReadString(node, "id") ?? string.Empty;
            var hasOutput = TryGetProperty(node, "output", out var output) && output.ValueKind == JsonValueKind.Object;
            nodes.Add(new NodeView(
                id,
                ReadString(node, "label") ?? id,
                ReadString(node, "prompt"),
                ReadString(node, "forEach"),
                bindings,
                resultNodeIds.Contains(id),
                hasOutput,
                aggregate,
                hasOutput ? ReadString(output, "schema") : null,
                hasOutput ? ReadString(output, "failIfEmpty") : null));
        }

        return nodes;
    }

    /// <summary>The manifest's schema ids — the package's output-contract choices.</summary>
    public static IReadOnlyList<string> ReadSchemaIds(JsonElement manifest)
    {
        if (!TryGetProperty(manifest, "schemas", out var schemas) || schemas.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return schemas.EnumerateObject().Select(entry => entry.Name).ToList();
    }

    /// <summary>
    /// Scalar data entries: values of the data map that are not directories.
    /// Ids only — what the binding dropdown needs.
    /// </summary>
    public static IReadOnlyList<string> ReadScalars(JsonElement manifest) =>
        ReadScalarEntries(manifest).Select(entry => entry.Id).ToList();

    /// <summary>
    /// The same entries with their file paths, which editing needs: a scalar's
    /// value *is* the file at that path, so the path is how the editor loads
    /// and writes it.
    ///
    /// The trailing slash is the whole discriminator — `WorkflowDataResolver`
    /// reads a path ending in '/' as a collection and anything else as a
    /// value. Anything writing these paths has to get that one character
    /// right, in both directions.
    /// </summary>
    public static IReadOnlyList<ScalarView> ReadScalarEntries(JsonElement manifest)
    {
        var scalars = new List<ScalarView>();

        if (!TryGetProperty(manifest, "data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return scalars;
        }

        foreach (var entry in data.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String && entry.Value.GetString() is { } value && !value.EndsWith('/'))
            {
                scalars.Add(new ScalarView(entry.Name, value));
            }
        }

        return scalars;
    }

    /// <summary>One single-value data entry: the bindable id and the file holding its text.</summary>
    public sealed record ScalarView(string Id, string Path);

    /// <summary>
    /// The manifest's collections joined with each directory's index.json (from
    /// the files dict) so the editor can render one card per item.
    /// </summary>
    public static IReadOnlyList<CollectionView> ReadCollections(JsonElement manifest, IReadOnlyDictionary<string, string> files)
    {
        var collections = new List<CollectionView>();

        if (!TryGetProperty(manifest, "data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return collections;
        }

        foreach (var entry in data.EnumerateObject())
        {
            var directory = entry.Value.GetString();

            if (string.IsNullOrWhiteSpace(directory) || !directory.EndsWith('/'))
            {
                continue;
            }

            var items = new List<DataItemView>();

            if (files.TryGetValue(directory + "index.json", out var indexJson))
            {
                try
                {
                    using var index = JsonDocument.Parse(indexJson);

                    if (index.RootElement.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsElement.EnumerateArray())
                        {
                            var file = ReadString(item, "file");

                            if (string.IsNullOrWhiteSpace(file))
                            {
                                continue;
                            }

                            var id = ReadString(item, "id") ?? file;
                            items.Add(new DataItemView(id, ReadString(item, "name") ?? id, directory + file));
                        }
                    }
                }
                catch (JsonException)
                {
                    // An unparseable index renders as an empty collection; the
                    // publish validator is the authority on integrity.
                }
            }

            collections.Add(new CollectionView(entry.Name, directory, items));
        }

        return collections;
    }

    /// <summary>The fork's parent ref, or null for root packages.</summary>
    public static string? ReadDerivedFrom(JsonElement manifest) => ReadString(manifest, "derivedFrom");

    /// <summary>The raw result reference ("node:x"), for the deliverable selector.</summary>
    public static string? ReadResultRef(JsonElement manifest) => ReadString(manifest, "result");

    /// <summary>
    /// The declared input slots (specVersion 7). Empty for v5/v6, whose single
    /// slot is the frozen consult_draft convention rather than a declaration.
    /// `required` defaults true when absent, matching the server's spec record.
    /// </summary>
    public static IReadOnlyList<InputView> ReadInputs(JsonElement manifest)
    {
        var inputs = new List<InputView>();

        if (!TryGetProperty(manifest, "inputs", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return inputs;
        }

        foreach (var input in array.EnumerateArray())
        {
            var id = ReadString(input, "id") ?? string.Empty;
            var required = !TryGetProperty(input, "required", out var requiredElement)
                || requiredElement.ValueKind != JsonValueKind.False;

            inputs.Add(new InputView(
                id,
                ReadString(input, "label") ?? id,
                required,
                ReadString(input, "type"),
                ReadStringArray(input, "values"),
                ReadString(input, "items"),
                ReadFields(input)));
        }

        return inputs;
    }

    /// <summary>An object input's declared fields (v9); null when the property is absent.</summary>
    private static IReadOnlyList<FieldView>? ReadFields(JsonElement input)
    {
        if (!TryGetProperty(input, "fields", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var fields = new List<FieldView>();

        foreach (var field in array.EnumerateArray())
        {
            var id = ReadString(field, "id") ?? string.Empty;
            var required = !TryGetProperty(field, "required", out var requiredElement)
                || requiredElement.ValueKind != JsonValueKind.False;

            fields.Add(new FieldView(
                id,
                ReadString(field, "label") ?? id,
                required,
                ReadString(field, "type"),
                ReadStringArray(field, "values")));
        }

        return fields;
    }

    /// <summary>
    /// The declared deliverables (specVersion 7). Empty when the package uses
    /// the string result form, which stays valid as one-entry sugar.
    /// </summary>
    public static IReadOnlyList<ResultView> ReadResults(JsonElement manifest)
    {
        var results = new List<ResultView>();

        if (!TryGetProperty(manifest, "results", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var result in array.EnumerateArray())
        {
            var id = ReadString(result, "id") ?? string.Empty;
            results.Add(new ResultView(
                id,
                ReadString(result, "node") ?? string.Empty,
                ReadString(result, "label") ?? id,
                ReadString(result, "when")));
        }

        return results;
    }

    /// <summary>
    /// The nodes a package names as deliverables: the v7 results list, or the
    /// v5/v6 single result string. A v7 package declaring results carries no
    /// result string, so reading only the latter would mark no node at all.
    /// </summary>
    private static IReadOnlySet<string> ReadResultNodeIds(JsonElement manifest)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (TryGetProperty(manifest, "results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                if (StripNodePrefix(ReadString(result, "node")) is { } nodeId)
                {
                    ids.Add(nodeId);
                }
            }

            return ids;
        }

        if (StripNodePrefix(ReadString(manifest, "result")) is { } singleNodeId)
        {
            ids.Add(singleNodeId);
        }

        return ids;
    }

    private static string? StripNodePrefix(string? reference) =>
        reference != null && reference.StartsWith("node:", StringComparison.Ordinal)
            ? reference["node:".Length..]
            : reference;

    private static BindingView ReadBinding(string variable, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new BindingView(variable, value.GetString() ?? string.Empty, null),
        JsonValueKind.Object => new BindingView(variable, ReadString(value, "from") ?? string.Empty, ReadString(value, "as")),
        _ => new BindingView(variable, value.ToString(), null)
    };

    /// <summary>
    /// The manifest element's property casing depends on the server's response
    /// serializer (the Functions worker default writes PascalCase; repo manifest
    /// sources are camelCase) — the reader accepts either, like the server's own
    /// case-insensitive parsing does.
    /// </summary>
    private static bool TryGetProperty(JsonElement element, string camelName, out JsonElement value)
    {
        if (element.TryGetProperty(camelName, out value))
        {
            return true;
        }

        var pascalName = char.ToUpperInvariant(camelName[0]) + camelName[1..];
        return element.TryGetProperty(pascalName, out value);
    }

    private static string? ReadString(JsonElement element, string property) =>
        TryGetProperty(element, property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>An enum input's declared values; null when the property is absent.</summary>
    private static IReadOnlyList<string>? ReadStringArray(JsonElement element, string property)
    {
        if (!TryGetProperty(element, property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }
}
