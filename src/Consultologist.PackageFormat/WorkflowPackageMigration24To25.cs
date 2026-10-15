using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Scriban;
using Scriban.Syntax;

namespace Consultologist.PackageFormat;

/// <summary>What the 24→25 migration produced: the manifest and files at 25, one notice per rewrite, or the refusals that stopped it.</summary>
public sealed record Migration25Result(
    string ManifestJson,
    Dictionary<string, string> Files,
    IReadOnlyList<string> Notices,
    IReadOnlyList<string> Refusals)
{
    public bool Succeeded => Refusals.Count == 0;
}

/// <summary>
/// C6 (#1012, design § 14): the editor's upgrade from 24 to 25 — mechanical, and
/// byte-identical in what the model sees. Every row of the § 14 table is a step
/// here, in an order the rows depend on: ids first (a 25 id is snake_case),
/// then the templates (variables become object reads while the bindings still
/// say what they were), then nodes (kinds and contracts become formats),
/// deliverables (results, with node-anchored macros moved onto their nodes),
/// prompt-side node macros (inline reads; a macro placed on no output becomes a
/// snippet), macro texts (placeholders become objects), data (snippets, files
/// moved and a literal '{{' escaped since a snippet renders as a template), and
/// the version. A refusal — a prompt bound differently by two nodes (the
/// deferred alias map), an id rename that collides, a prelude still present, a
/// template that does not parse — applies nothing.
/// </summary>
public static class WorkflowPackageMigration24To25
{
    private const string NodePrefix = "node:";
    private const string DataPrefix = "data:";
    private const string InputPrefix = "input:";
    private const string ItemPrefix = "item:";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    public static Migration25Result Migrate(string manifestJson, IReadOnlyDictionary<string, string> files, Func<JsonNode, string?>? resolveContract = null)
    {
        var notices = new List<string>();
        var refusals = new List<string>();
        var outFiles = new Dictionary<string, string>(files, StringComparer.Ordinal);

        JsonObject root;
        WorkflowPackageManifest manifest;
        try
        {
            root = JsonNode.Parse(manifestJson)?.AsObject() ?? throw new JsonException("The manifest is not a JSON object.");
            manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(manifestJson, ReadOptions) ?? throw new JsonException("The manifest is empty.");
        }
        catch (JsonException ex)
        {
            return Refused(manifestJson, files, $"The manifest does not read as a package: {ex.Message}");
        }

        // ----- the gate -----
        if (manifest.SpecVersion >= 25)
        {
            return Refused(manifestJson, files, $"The package is already at specVersion {manifest.SpecVersion}.");
        }

        if (manifest.SpecVersion < 23 || (manifest.Result is not null && manifest.Results is null))
        {
            return Refused(manifestJson, files, $"Migrate to specVersion 24 first (the package is at {manifest.SpecVersion}).");
        }

        foreach (var (preludeId, _) in manifest.Preludes ?? new Dictionary<string, string>())
        {
            refusals.Add($"Prelude '{preludeId}' remains; migrate preludes to node macros first (specVersion 23).");
        }

        foreach (var prompt in (manifest.Prompts ?? new List<WorkflowPromptSpec>()).Where(p => p.Prelude is not null))
        {
            refusals.Add($"Prompt '{prompt.Id}' names prelude '{prompt.Prelude}'; migrate preludes to node macros first (specVersion 23).");
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        // ----- row: ids -----
        RenameIds(root, manifest, outFiles, notices, refusals);
        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        manifest = JsonSerializer.Deserialize<WorkflowPackageManifest>(root.ToJsonString(), ReadOptions)!;

        // ----- what each node reads as at 25 (needed by the template rewrite) -----
        var formats = new FormatPlan(manifest, outFiles, resolveContract);

        // ----- row: templates (variables → objects; raw → escaped) -----
        RewriteTemplates(root, manifest, formats, outFiles, notices, refusals);
        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        // The ≤24 node macros are prompt-side compositions; captured before the
        // deliverables row adds output-side instances to the same lists.
        var promptSideMacros = (manifest.Nodes ?? new List<WorkflowNodeSpec>())
            .Where(n => n.Macros is { Count: > 0 })
            .ToDictionary(n => n.Id, n => (IReadOnlyList<WorkflowNodeMacroSpec>)n.Macros!.ToList(), StringComparer.Ordinal);

        // ----- row: nodes (formats, with, onInvalid, failIfEmpty, forEach) -----
        RewriteNodes(root, manifest, formats, outFiles, notices);

        // ----- row: deliverables (results → deliverables; anchored macros → node instances; slots) -----
        var slotMacroIds = RewriteDeliverables(root, manifest, notices);

        // ----- row: prompt-side node macros → inline reads; never-placed macros → snippets -----
        var snippetised = RewritePromptSideMacros(root, manifest, promptSideMacros, slotMacroIds, outFiles, notices, refusals);
        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        // ----- row: data → snippets (files moved, a literal '{{' escaped) -----
        RewriteData(root, manifest, outFiles, notices);

        // ----- row (#1037): every when spelled as a Scriban expression -----
        RewriteConditions(root, manifest, notices, refusals);
        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        // ----- row: macro and snippet texts (placeholders → objects); slot markers → macro.<id>.prompt -----
        RewriteTexts(root, manifest, snippetised, slotMacroIds, outFiles, notices);

        // ----- row: schemas gone; version -----
        Remove(root, "schemas");
        Remove(root, "schemaRefs");
        Remove(root, "customSchemas");
        Set(root, "specVersion", 25);
        if (Get(root, "tags") is null)
        {
            Set(root, "tags", new JsonArray());
        }

        return new Migration25Result(root.ToJsonString(WriteOptions), outFiles, notices, refusals);
    }

    // ===================================================================== ids

    private static void RenameIds(JsonObject root, WorkflowPackageManifest manifest, Dictionary<string, string> files, List<string> notices, List<string> refusals)
    {
        var nodeIds = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).Select(n => n.Id).ToList();
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var id in nodeIds.Where(id => !WorkflowDeclaredIds.IsValid(id)))
        {
            var renamed = id.Replace('-', '_');

            if (!WorkflowDeclaredIds.IsValid(renamed))
            {
                refusals.Add($"Node id '{id}' cannot be made snake_case by replacing hyphens; rename it, then upgrade.");
                continue;
            }

            if (nodeIds.Contains(renamed, StringComparer.Ordinal) || renames.ContainsValue(renamed))
            {
                refusals.Add($"Node '{id}' renamed '{renamed}' collides with another node; rename one, then upgrade.");
                continue;
            }

            renames[id] = renamed;
        }

        if (refusals.Count > 0 || renames.Count == 0)
        {
            RenameTableKeys(root, manifest, files, notices, refusals);
            return;
        }

        string Node(string reference) =>
            reference.StartsWith(NodePrefix, StringComparison.Ordinal) && renames.TryGetValue(reference[NodePrefix.Length..], out var to)
                ? NodePrefix + to
                : reference;

        string Condition(string? when) => when is null ? when! : Regex.Replace(when, @"node:([A-Za-z0-9_-]+)", m => renames.TryGetValue(m.Groups[1].Value, out var to) ? $"node:{to}" : m.Value);

        foreach (var node in Array(root, "nodes").OfType<JsonObject>())
        {
            var id = Str(node, "id");
            if (id is not null && renames.TryGetValue(id, out var to))
            {
                Set(node, "id", to);
            }

            if (Get(node, "aggregate") is JsonArray aggregate)
            {
                for (var i = 0; i < aggregate.Count; i++)
                {
                    if (aggregate[i]?.GetValue<string>() is { } source)
                    {
                        aggregate[i] = Node(source);
                    }
                }
            }

            foreach (var key in new[] { "of", "in" })
            {
                if (Str(node, key) is { } reference)
                {
                    Set(node, key, Node(reference));
                }
            }

            if (Str(node, "when") is { } when)
            {
                Set(node, "when", Condition(when));
            }

            if (Get(node, "bindings") is JsonObject bindings)
            {
                foreach (var key in bindings.Select(b => b.Key).ToList())
                {
                    switch (bindings[key])
                    {
                        case JsonValue value when value.TryGetValue<string>(out var from):
                            bindings[key] = Node(from);
                            break;
                        case JsonObject placed when Str(placed, "from") is { } from:
                            Set(placed, "from", Node(from));
                            break;
                    }
                }
            }

            foreach (var macro in Array(node, "macros").OfType<JsonObject>())
            {
                if (Str(macro, "when") is { } macroWhen)
                {
                    Set(macro, "when", Condition(macroWhen));
                }
            }
        }

        foreach (var result in Array(root, "results").OfType<JsonObject>())
        {
            foreach (var key in new[] { "node", "check" })
            {
                if (Str(result, key) is { } reference)
                {
                    Set(result, key, Node(reference));
                }
            }

            if (Str(result, "when") is { } when)
            {
                Set(result, "when", Condition(when));
            }

            foreach (var macro in Array(result, "macros").OfType<JsonObject>())
            {
                foreach (var key in new[] { "before", "after" })
                {
                    if (Str(macro, key) is { } anchor)
                    {
                        Set(macro, key, Node(anchor));
                    }
                }

                if (Str(macro, "when") is { } macroWhen)
                {
                    Set(macro, "when", Condition(macroWhen));
                }
            }
        }

        foreach (var (from, to) in renames)
        {
            notices.Add($"Node '{from}' renamed '{to}' (a 25 node id is snake_case), in every reference.");
        }

        RenameTableKeys(root, manifest, files, notices, refusals);
    }

    /// <summary>Data keys (snippet ids at 25) and schema keys (format ids at 25) must be snake_case too.</summary>
    private static void RenameTableKeys(JsonObject root, WorkflowPackageManifest manifest, Dictionary<string, string> files, List<string> notices, List<string> refusals)
    {
        if (Get(root, "data") is JsonObject data)
        {
            var dataRenames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var key in data.Select(d => d.Key).Where(k => !WorkflowDeclaredIds.IsValid(k)).ToList())
            {
                var renamed = key.Replace('-', '_');
                if (!WorkflowDeclaredIds.IsValid(renamed) || data.ContainsKey(renamed))
                {
                    refusals.Add($"Data entry '{key}' cannot become a snake_case snippet id; rename it, then upgrade.");
                    continue;
                }

                dataRenames[key] = renamed;
            }

            foreach (var (from, to) in dataRenames)
            {
                var path = data[from];
                data.Remove(from);
                data[to] = path?.DeepClone();
                notices.Add($"Data entry '{from}' renamed '{to}' (a 25 snippet id is snake_case).");

                foreach (var node in Array(root, "nodes").OfType<JsonObject>())
                {
                    if (Str(node, "forEach") == DataPrefix + from)
                    {
                        Set(node, "forEach", DataPrefix + to);
                    }

                    if (Get(node, "bindings") is JsonObject bindings)
                    {
                        foreach (var key in bindings.Select(b => b.Key).ToList())
                        {
                            if (bindings[key] is JsonValue value && value.TryGetValue<string>(out var source) && source == DataPrefix + from)
                            {
                                bindings[key] = DataPrefix + to;
                            }
                        }
                    }
                }
            }
        }

        foreach (var table in new[] { "schemas", "schemaRefs", "customSchemas" })
        {
            if (Get(root, table) is not JsonObject map)
            {
                continue;
            }

            foreach (var key in map.Select(m => m.Key).Where(k => !WorkflowDeclaredIds.IsValid(k)).ToList())
            {
                var renamed = key.Replace('-', '_');
                if (!WorkflowDeclaredIds.IsValid(renamed) || map.ContainsKey(renamed))
                {
                    refusals.Add($"Schema '{key}' cannot become a snake_case format id; rename it, then upgrade.");
                    continue;
                }

                var value = map[key];
                map.Remove(key);
                map[renamed] = value?.DeepClone();
                notices.Add($"Schema '{key}' renamed '{renamed}' (a 25 format id is snake_case).");

                foreach (var node in Array(root, "nodes").OfType<JsonObject>())
                {
                    if (Get(node, "output") is JsonObject output && Str(output, "schema") == key)
                    {
                        Set(output, "schema", renamed);
                    }
                }
            }
        }
    }

    // ================================================================= formats

    /// <summary>What a ≤24 node is at 25: its format id, and how a binding to it reads (text, text with a pipe, or value).</summary>
    private sealed class FormatPlan
    {
        private readonly Dictionary<string, string> _formatOf = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _readOf = new(StringComparer.Ordinal);
        public readonly Dictionary<string, (string Id, JsonNode Schema)> CustomFormats = new(StringComparer.Ordinal);

        public FormatPlan(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files, Func<JsonNode, string?>? resolveContract)
        {
            foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
            {
                if (node.Aggregate is not null)
                {
                    _readOf[node.Id] = "text";
                    continue;
                }

                if (WorkflowNodeKinds.IsClassifier(node))
                {
                    _formatOf[node.Id] = "classification";
                    _readOf[node.Id] = "value";
                    continue;
                }

                if (node.Kind == WorkflowNodeKinds.Template)
                {
                    _formatOf[node.Id] = "rendered";
                    _readOf[node.Id] = "text";
                    continue;
                }

                if (node.Kind == WorkflowNodeKinds.Check)
                {
                    _formatOf[node.Id] = WorkflowFormatIds.TermsSubset;
                    _readOf[node.Id] = "value";
                    continue;
                }

                if (node.Output?.Schema is { } schemaId)
                {
                    var contract = ContractOf(manifest, files, schemaId, resolveContract);
                    if (contract == WorkflowNodeDefaults.ConceptListSchemaId)
                    {
                        _formatOf[node.Id] = WorkflowFormatIds.ConceptList;
                        _readOf[node.Id] = "concepts";
                    }
                    else if (contract == WorkflowNodeDefaults.ClassificationSchemaId)
                    {
                        _formatOf[node.Id] = "classification";
                        _readOf[node.Id] = "value";
                    }
                    else if (contract == "text")
                    {
                        _formatOf[node.Id] = "prose";
                        _readOf[node.Id] = "text";
                    }
                    else
                    {
                        // A custom shape (customSchemas, or a body matching no contract): a package format.
                        var body = (manifest.CustomSchemas ?? new Dictionary<string, string>()).TryGetValue(schemaId, out var customFile)
                            ? files.GetValueOrDefault(customFile)
                            : (manifest.Schemas ?? new Dictionary<string, string>()).TryGetValue(schemaId, out var schemaFile) ? files.GetValueOrDefault(schemaFile) : null;
                        var schema = body is null ? new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = new JsonObject() } : JsonNode.Parse(body) ?? new JsonObject();
                        CustomFormats[schemaId] = (schemaId, schema);
                        _formatOf[node.Id] = schemaId;
                        _readOf[node.Id] = "text";
                    }

                    continue;
                }

                _formatOf[node.Id] = "prose";
                _readOf[node.Id] = "text";
            }
        }

        public string? FormatOf(string nodeId) => _formatOf.GetValueOrDefault(nodeId);

        /// <summary>The 25 read of a ≤24 binding to this node — the engine's ≤24 render: a classifier's value, a concept node through its renderer (bullets when none was named), anything else its text.</summary>
        public string ReadOf(string nodeId, string? renderer) => _readOf.GetValueOrDefault(nodeId) switch
        {
            "value" => $"{TemplateObjects.Node}.{nodeId}.{TemplateMembers.Value}",
            "concepts" => $"{TemplateObjects.Node}.{nodeId}.{TemplateMembers.Text} | {(renderer == WorkflowConceptRenderers.ConceptContext ? TemplatePipes.ConceptContext : TemplatePipes.ConceptBullets)}",
            _ => $"{TemplateObjects.Node}.{nodeId}.{TemplateMembers.Text}"
        };

        private static string? ContractOf(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files, string schemaId, Func<JsonNode, string?>? resolveContract)
        {
            if ((manifest.SchemaRefs ?? new Dictionary<string, string>()).TryGetValue(schemaId, out var referenced))
            {
                return referenced;
            }

            if ((manifest.CustomSchemas ?? new Dictionary<string, string>()).ContainsKey(schemaId))
            {
                return null;
            }

            if ((manifest.Schemas ?? new Dictionary<string, string>()).TryGetValue(schemaId, out var file)
                && files.TryGetValue(file, out var body)
                && resolveContract is not null)
            {
                try
                {
                    return resolveContract(JsonNode.Parse(body) ?? new JsonObject());
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return null;
        }
    }

    // =============================================================== templates

    private static void RewriteTemplates(JsonObject root, WorkflowPackageManifest manifest, FormatPlan formats, Dictionary<string, string> files, List<string> notices, List<string> refusals)
    {
        var nodes = manifest.Nodes ?? new List<WorkflowNodeSpec>();
        var rewrittenPrompts = 0;

        foreach (var prompt in manifest.Prompts ?? new List<WorkflowPromptSpec>())
        {
            if (!files.TryGetValue(prompt.File, out var text))
            {
                continue;
            }

            if (prompt.Raw == true)
            {
                files[prompt.File] = EscapeBraces(text);
                notices.Add($"Prompt '{prompt.Id}' was raw; its literal '{{{{' are escaped and it renders as a template.");
            }
            else if (prompt.Variables.Count > 0)
            {
                var readers = nodes.Where(n => n.Prompt == prompt.Id).ToList();
                var replacements = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var variable in prompt.Variables)
                {
                    var sources = readers
                        .Select(n => (Node: n, Binding: n.Bindings is { } b && b.TryGetValue(variable, out var v) ? v : null))
                        .Where(pair => pair.Binding is not null)
                        .ToList();
                    var distinct = sources.Select(s => $"{s.Binding!.From}\n{s.Binding.As}").Distinct(StringComparer.Ordinal).ToList();

                    if (distinct.Count > 1)
                    {
                        var byNode = string.Join(" and ", sources.Select(s => $"'{s.Node.Id}'"));
                        refusals.Add($"Prompt '{prompt.Id}' binds '{variable}' differently on nodes {byNode}; a per-node alias map is deferred — split the prompt first, then upgrade.");
                        continue;
                    }

                    var binding = sources.FirstOrDefault().Binding;
                    if (binding is null)
                    {
                        continue;
                    }

                    replacements[variable] = ObjectRead(binding, formats);
                }

                if (refusals.Count > 0)
                {
                    continue;
                }

                var rewritten = RewriteVariables(prompt.Id, text, replacements, refusals);
                if (rewritten is not null)
                {
                    files[prompt.File] = rewritten;
                    rewrittenPrompts++;
                }
            }
        }

        foreach (var prompt in Array(root, "prompts").OfType<JsonObject>())
        {
            Remove(prompt, "variables");
            Remove(prompt, "raw");
        }

        foreach (var node in Array(root, "nodes").OfType<JsonObject>())
        {
            Remove(node, "bindings");
        }

        if (rewrittenPrompts > 0)
        {
            notices.Add($"{rewrittenPrompts} prompt{(rewrittenPrompts == 1 ? "" : "s")} now read objects where variables were bound (bindings and variables dropped).");
        }
    }

    /// <summary>The ≤24 binding source as a 25 object read.</summary>
    private static string ObjectRead(WorkflowBindingValue binding, FormatPlan formats)
    {
        var from = binding.From;

        if (from.StartsWith(InputPrefix, StringComparison.Ordinal))
        {
            return $"{TemplateObjects.Input}.{from[InputPrefix.Length..]}";
        }

        if (from.StartsWith(DataPrefix, StringComparison.Ordinal))
        {
            return $"{TemplateObjects.Snippet}.{from[DataPrefix.Length..]}.{TemplateMembers.Text}";
        }

        if (from.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            var member = from[ItemPrefix.Length..];
            return $"{TemplateObjects.Item}.{(member == "content" ? TemplateMembers.Text : member)}";
        }

        if (from.StartsWith(NodePrefix, StringComparison.Ordinal))
        {
            return formats.ReadOf(from[NodePrefix.Length..], binding.As);
        }

        return from;
    }

    /// <summary>Every global variable in the template that is one of the prompt's, replaced at its span — a piped read parenthesised unless it is the whole expression.</summary>
    private static string? RewriteVariables(string promptId, string text, IReadOnlyDictionary<string, string> replacements, List<string> refusals)
    {
        // The walker's own first net: a '{{' never closed, which Scriban may lex without a word.
        if (TemplateReferenceWalker.TryFindUnclosedCode(text, out var unclosedLine))
        {
            refusals.Add($"Prompt '{promptId}' does not parse at line {unclosedLine}: '{{{{' is not closed; escape a literal '{{{{' as {{{{ \"{{{{\" }}}} or wrap it in {{%{{ … }}%}}.");
            return null;
        }

        var template = Template.Parse(text);

        if (template.HasErrors)
        {
            foreach (var message in template.Messages)
            {
                var at = message.Span.Start.Line >= 0 ? $" at line {message.Span.Start.Line + 1}" : string.Empty;
                refusals.Add($"Prompt '{promptId}' does not parse{at}: {message.Message}");
            }

            return null;
        }

        var collector = new VariableCollector(replacements.Keys.ToHashSet(StringComparer.Ordinal));
        collector.Visit(template.Page);

        var result = text;
        foreach (var variable in collector.Found.OrderByDescending(v => v.Start))
        {
            var replacement = replacements[variable.Name];
            if (replacement.Contains(" | ", StringComparison.Ordinal) && !IsWholeExpression(text, variable.Start, variable.End))
            {
                replacement = $"({replacement})";
            }

            result = result[..variable.Start] + replacement + result[(variable.End + 1)..];
        }

        return result;
    }

    private static bool IsWholeExpression(string text, int start, int end)
    {
        var before = text[..start].TrimEnd();
        var after = text[(end + 1)..].TrimStart();
        return before.EndsWith("{{", StringComparison.Ordinal) && after.StartsWith("}}", StringComparison.Ordinal);
    }

    private sealed class VariableCollector : ScriptVisitor
    {
        private readonly IReadOnlySet<string> _names;
        public readonly List<(string Name, int Start, int End)> Found = new();

        public VariableCollector(IReadOnlySet<string> names) => _names = names;

        public override void Visit(ScriptVariableGlobal node)
        {
            if (_names.Contains(node.Name))
            {
                Found.Add((node.Name, node.Span.Start.Offset, node.Span.End.Offset));
            }

            base.Visit(node);
        }
    }

    /// <summary>A literal '{{' spelled the Scriban way — the only characters a verbatim text must escape to render as a template.</summary>
    public static string EscapeBraces(string text) => text.Replace("{{", "{{ \"{{\" }}", StringComparison.Ordinal);

    // =================================================================== nodes

    private static void RewriteNodes(JsonObject root, WorkflowPackageManifest manifest, FormatPlan formats, Dictionary<string, string> files, List<string> notices)
    {
        var typedNodes = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in Array(root, "nodes").OfType<JsonObject>())
        {
            var id = Str(node, "id");
            if (id is null || !typedNodes.TryGetValue(id, out var typed))
            {
                continue;
            }

            if (typed.Aggregate is not null)
            {
                // #1037: an aggregator's sources read node.<id>.
                if (Get(node, "aggregate") is JsonArray aggregate)
                {
                    for (var i = 0; i < aggregate.Count; i++)
                    {
                        if (aggregate[i]?.GetValue<string>() is { } source)
                        {
                            aggregate[i] = NodeRef(source);
                        }
                    }
                }

                continue;
            }

            var format = formats.FormatOf(id) ?? "prose";
            var with = new JsonObject();
            string? onInvalid = null;

            if (WorkflowNodeKinds.IsClassifier(typed))
            {
                with["values"] = new JsonArray((typed.Values ?? new List<string>()).Select(v => (JsonNode?)v).ToArray());
                onInvalid = WorkflowOnInvalid.Fail;
            }
            else if (typed.Kind == WorkflowNodeKinds.Check)
            {
                with["of"] = NodeRef(typed.Of);
                with["in"] = NodeRef(typed.In);
                if (typed.FailWith is not null) { with["failWith"] = typed.FailWith; }
                onInvalid = WorkflowOnInvalid.Fail;
            }
            else if (format == WorkflowFormatIds.ConceptList || formats.CustomFormats.ContainsKey(format) || format == WorkflowFormatIds.Classification)
            {
                onInvalid = WorkflowOnInvalid.Fail;
            }

            // Written in the 25 node's order after id/label/prompt: format, with, onInvalid, failIfEmpty.
            var failIfEmpty = typed.Output?.FailIfEmpty;
            Remove(node, "kind"); Remove(node, "values"); Remove(node, "op"); Remove(node, "of"); Remove(node, "in"); Remove(node, "failWith"); Remove(node, "output");
            Set(node, "format", format);
            if (with.Count > 0) { Set(node, "with", with); }
            if (onInvalid is not null) { Set(node, "onInvalid", onInvalid); }
            if (!string.IsNullOrWhiteSpace(failIfEmpty) && format != WorkflowFormatIds.Rendered && format != WorkflowFormatIds.TermsSubset) { Set(node, "failIfEmpty", failIfEmpty); }

            // #1037: the object spelling — data:x → snippet.x, input:x → input.x.
            if (Str(node, "forEach") is { } forEach)
            {
                if (forEach.StartsWith(DataPrefix, StringComparison.Ordinal))
                {
                    Set(node, "forEach", TemplateObjectReference.Snippet(forEach[DataPrefix.Length..]));
                }
                else if (forEach.StartsWith(InputPrefix, StringComparison.Ordinal))
                {
                    Set(node, "forEach", TemplateObjectReference.Input(forEach[InputPrefix.Length..]));
                }
            }

            counts[format] = counts.GetValueOrDefault(format) + 1;
        }

        if (formats.CustomFormats.Count > 0)
        {
            var list = Get(root, "formats") as JsonArray ?? new JsonArray();
            foreach (var (schemaId, (formatId, schema)) in formats.CustomFormats)
            {
                var file = $"formats/{formatId}.json";
                // The schema's own description or title is the format's description; without
                // one it is blank — explicit init: the desk refuses it until written.
                var description = schema is JsonObject schemaObject
                    ? Str(schemaObject, "description") ?? Str(schemaObject, "title") ?? string.Empty
                    : string.Empty;
                var spec = new JsonObject
                {
                    ["type"] = "object",
                    ["producer"] = WorkflowFormatProducers.Custom,
                    ["description"] = description,
                    ["schema"] = schema.DeepClone()
                };
                files[file] = spec.ToJsonString(WriteOptions);
                if (!list.OfType<JsonObject>().Any(f => Str(f, "id") == formatId))
                {
                    list.Add(new JsonObject { ["id"] = formatId, ["file"] = file });
                }

                notices.Add($"Schema '{schemaId}' is now the package format '{formatId}' ({file}) with onInvalid: fail{(description.Length == 0 ? " — write its description before publishing" : string.Empty)}.");
            }

            Set(root, "formats", list);
        }

        foreach (var (file, _) in (manifest.Schemas ?? new Dictionary<string, string>()).Concat(manifest.CustomSchemas ?? new Dictionary<string, string>()).Select(p => (p.Value, p.Key)))
        {
            files.Remove(file);
        }

        if (counts.Count > 0)
        {
            notices.Add($"Nodes now name their format: {string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Value} {c.Key}"))} (kind, output and schemas dropped).");
        }
    }

    // ============================================================ conditions (#1037)

    /// <summary>
    /// Every <c>when</c> — a node's own, a node instance's, a deliverable's, a
    /// deliverable instance's — spelled through <see cref="ConditionScriban.Spell"/>;
    /// one that the ≤24 grammar refuses is a refusal naming the site (nothing
    /// applied). A 24 folder item carries no when (it is a 25 key), so none is moved.
    /// </summary>
    private static void RewriteConditions(JsonObject root, WorkflowPackageManifest manifest, List<string> notices, List<string> refusals)
    {
        var inputTypes = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => WorkflowInputTypes.Of(g.First()), StringComparer.Ordinal);
        var count = 0;

        string? Spelled(string site, string? when)
        {
            if (when is null)
            {
                return null;
            }

            if (!WorkflowResultConditions.TryParseLegacy(when, out var expression, out var error) || expression is null)
            {
                refusals.Add($"{site} condition '{when}' {error} Fix it, then upgrade.");
                return when;
            }

            count++;
            return ConditionScriban.Spell(expression, id => inputTypes.GetValueOrDefault(id));
        }

        void SpellOn(JsonObject owner, string site)
        {
            if (Str(owner, "when") is { } when)
            {
                Set(owner, "when", Spelled(site, when));
            }
        }

        foreach (var node in Array(root, "nodes").OfType<JsonObject>())
        {
            var nodeId = Str(node, "id") ?? "?";
            SpellOn(node, $"Node '{nodeId}'");

            foreach (var instance in (Get(node, "macros") as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                SpellOn(instance, $"Node '{nodeId}' macro '{Str(instance, "id") ?? "?"}'");
            }
        }

        foreach (var deliverable in Array(root, "deliverables").OfType<JsonObject>())
        {
            var deliverableId = Str(deliverable, "id") ?? "?";
            SpellOn(deliverable, $"Deliverable '{deliverableId}'");

            foreach (var instance in (Get(deliverable, "macros") as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                SpellOn(instance, $"Deliverable '{deliverableId}' macro '{Str(instance, "id") ?? "?"}'");
            }
        }

        if (count > 0)
        {
            notices.Add($"{count} condition{(count == 1 ? string.Empty : "s")} spelled as Scriban expression{(count == 1 ? string.Empty : "s")} (node.<id>.value, input.<id>, item.id).");
        }
    }

    private static string? NodeRef(string? reference) =>
        reference is null ? null : reference.StartsWith(NodePrefix, StringComparison.Ordinal) ? $"{TemplateObjects.Node}.{reference[NodePrefix.Length..]}" : reference;

    // ============================================================ deliverables

    /// <summary>results → deliverables; a node-anchored macro becomes an instance on its node; a slot entry leaves (the template reads macro.&lt;id&gt;.prompt). Returns the slot macro ids.</summary>
    private static IReadOnlySet<string> RewriteDeliverables(JsonObject root, WorkflowPackageManifest manifest, List<string> notices)
    {
        var slots = new HashSet<string>(StringComparer.Ordinal);
        var results = Get(root, "results") as JsonArray;
        if (results is null)
        {
            return slots;
        }

        var nodesById = Array(root, "nodes").OfType<JsonObject>().Where(n => Str(n, "id") is not null).ToDictionary(n => Str(n, "id")!, StringComparer.Ordinal);

        foreach (var result in results.OfType<JsonObject>())
        {
            var resultId = Str(result, "id") ?? "?";

            if (Str(result, "check") is { } check)
            {
                Set(result, "check", NodeRef(check));
            }

            // #1037: the deliverable names node.<id>.
            if (Str(result, "node") is { } resultNode)
            {
                Set(result, "node", NodeRef(resultNode));
            }

            if (Get(result, "macros") is not JsonArray macros)
            {
                continue;
            }

            var kept = new JsonArray();
            foreach (var entry in macros.ToList())
            {
                if (entry is JsonValue value && value.TryGetValue<string>(out var bareId))
                {
                    kept.Add(new JsonObject { ["id"] = bareId, ["at"] = WorkflowDeliverableMacroSpec.Append });
                    continue;
                }

                if (entry is not JsonObject placed || Str(placed, "id") is not { } macroId)
                {
                    continue;
                }

                if (Get(placed, "slot") is JsonValue slot && slot.TryGetValue<bool>(out var isSlot) && isSlot)
                {
                    slots.Add(macroId);
                    notices.Add($"Macro '{macroId}' was a slot on deliverable '{resultId}'; the template that carried [[slot:{macroId}]] now reads macro.{macroId}.prompt.");
                    continue;
                }

                var anchor = Str(placed, "before") ?? Str(placed, "after");
                if (anchor is not null)
                {
                    var side = Str(placed, "before") is not null ? WorkflowNodeMacroSpec.Before : WorkflowNodeMacroSpec.After;
                    var nodeId = anchor.StartsWith(NodePrefix, StringComparison.Ordinal) ? anchor[NodePrefix.Length..] : anchor;
                    var instance = new JsonObject { ["id"] = macroId, ["at"] = side };
                    if (Str(placed, "when") is { } when) { instance["when"] = when; }
                    if (Str(placed, "forItem") is { } forItem) { instance["forItem"] = forItem; }

                    if (nodesById.TryGetValue(nodeId, out var node))
                    {
                        var list = Get(node, "macros") as JsonArray ?? new JsonArray();
                        list.Add(instance);
                        Set(node, "macros", list);
                        notices.Add($"Macro '{macroId}' moved from deliverable '{resultId}' onto node '{nodeId}' ({side}); the node's outputHash now covers it.");
                    }

                    continue;
                }

                var appended = new JsonObject { ["id"] = macroId, ["at"] = WorkflowDeliverableMacroSpec.Append };
                if (Str(placed, "when") is { } gate) { appended["when"] = gate; }
                kept.Add(appended);
            }

            if (kept.Count > 0)
            {
                Set(result, "macros", kept);
            }
            else
            {
                Remove(result, "macros");
            }
        }

        var deliverables = results.DeepClone();
        Remove(root, "results");
        Remove(root, "result");
        Set(root, "deliverables", deliverables);
        notices.Add("results is now deliverables; a deliverable's node and check read node.<id>; appended macros say at: append.");
        return slots;
    }

    // ==================================================== prompt-side macros

    private static IReadOnlySet<string> RewritePromptSideMacros(
        JsonObject root,
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, IReadOnlyList<WorkflowNodeMacroSpec>> promptSideMacros,
        IReadOnlySet<string> slotMacroIds,
        Dictionary<string, string> files,
        List<string> notices,
        List<string> refusals)
    {
        var snippetised = new HashSet<string>(StringComparer.Ordinal);
        if (promptSideMacros.Count == 0)
        {
            return snippetised;
        }

        var nodes = manifest.Nodes ?? new List<WorkflowNodeSpec>();
        var promptsById = (manifest.Prompts ?? new List<WorkflowPromptSpec>()).ToDictionary(p => p.Id, StringComparer.Ordinal);
        var inputTypes = (manifest.Inputs ?? new List<WorkflowInputSpec>()).ToDictionary(i => i.Id, i => WorkflowInputTypes.Of(i), StringComparer.Ordinal);

        // The output-side instances the deliverables row added: everything in a node's list that the ≤24 list did not hold.
        var promptSideIds = promptSideMacros.Values.SelectMany(list => list.Select(m => m.Id)).ToHashSet(StringComparer.Ordinal);
        // A slot macro is read as macro.<id>.prompt — a use that keeps it a macro.
        var outputPlaced = new HashSet<string>(slotMacroIds, StringComparer.Ordinal);
        foreach (var node in Array(root, "nodes").OfType<JsonObject>())
        {
            var id = Str(node, "id")!;
            var before = promptSideMacros.GetValueOrDefault(id)?.Count ?? 0;
            var list = Array(node, "macros").ToList();
            foreach (var extra in list.Skip(before).OfType<JsonObject>())
            {
                if (Str(extra, "id") is { } extraId) { outputPlaced.Add(extraId); }
            }
        }

        foreach (var deliverable in Array(root, "deliverables").OfType<JsonObject>())
        {
            foreach (var macro in Array(deliverable, "macros").OfType<JsonObject>())
            {
                if (Str(macro, "id") is { } id) { outputPlaced.Add(id); }
            }
        }

        // A macro read prompt-side only becomes a snippet.
        foreach (var macroId in promptSideIds.Where(id => !outputPlaced.Contains(id)))
        {
            snippetised.Add(macroId);
        }

        // Shared prompts must agree on their macro lists.
        foreach (var prompt in promptsById.Values)
        {
            var readers = nodes.Where(n => n.Prompt == prompt.Id).ToList();
            var lists = readers.Select(n => string.Join("|", (promptSideMacros.GetValueOrDefault(n.Id) ?? System.Array.Empty<WorkflowNodeMacroSpec>()).Select(m => $"{m.Id}/{m.At}/{m.When}/{m.ForItem}"))).Distinct(StringComparer.Ordinal).ToList();
            if (lists.Count > 1)
            {
                refusals.Add($"Prompt '{prompt.Id}' is composed with different macros on nodes {string.Join(" and ", readers.Select(n => $"'{n.Id}'"))}; a per-node alias map is deferred — split the prompt first, then upgrade.");
            }
        }

        if (refusals.Count > 0)
        {
            return snippetised;
        }

        var rewrittenPrompts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (nodeId, macros) in promptSideMacros)
        {
            var node = nodes.FirstOrDefault(n => n.Id == nodeId);
            if (node?.Prompt is null || !promptsById.TryGetValue(node.Prompt, out var prompt) || !files.TryGetValue(prompt.File, out var text) || rewrittenPrompts.Contains(prompt.Id))
            {
                continue;
            }

            var befores = new List<string>();
            var afters = new List<string>();
            foreach (var macro in macros)
            {
                var read = snippetised.Contains(macro.Id)
                    ? $"{{{{ {TemplateObjects.Snippet}.{macro.Id}.{TemplateMembers.Text} }}}}"
                    : $"{{{{ {TemplateObjects.Macro}.{macro.Id}.{TemplateMembers.Text} }}}}";
                var conditions = new List<string>();
                if (macro.When is { } when && WorkflowResultConditions.TryParseExpression(when, out var expression, out _) && expression is not null)
                {
                    conditions.Add(ConditionScriban.Spell(expression, id => inputTypes.GetValueOrDefault(id)));
                }

                if (macro.ForItem is { } forItem)
                {
                    conditions.Add($"{TemplateObjects.Item}.{TemplateMembers.Id} == \"{forItem}\"");
                }

                var block = conditions.Count == 0 ? read : $"{{{{ if {string.Join(" && ", conditions)} }}}}{read}{{{{ end }}}}";
                (macro.IsAfter ? afters : befores).Add(block);
            }

            var composed = text;
            if (befores.Count > 0)
            {
                composed = string.Join("\n\n", befores) + "\n\n" + composed;
            }

            if (afters.Count > 0)
            {
                composed = composed + "\n\n" + string.Join("\n\n", afters);
            }

            files[prompt.File] = composed;
            rewrittenPrompts.Add(prompt.Id);
            notices.Add($"Node '{nodeId}' composed {macros.Count} macro{(macros.Count == 1 ? "" : "s")} around its prompt; prompt '{prompt.Id}' now reads {string.Join(", ", macros.Select(m => snippetised.Contains(m.Id) ? $"snippet.{m.Id}.text" : $"macro.{m.Id}.text"))} inline{(macros.Any(m => m.When is not null || m.ForItem is not null) ? " under its gates" : "")}.");
        }

        // Drop the ≤24 prompt-side entries from every node's list (the output-side instances stay).
        foreach (var node in Array(root, "nodes").OfType<JsonObject>())
        {
            var id = Str(node, "id")!;
            var promptSideCount = promptSideMacros.GetValueOrDefault(id)?.Count ?? 0;
            if (promptSideCount == 0)
            {
                continue;
            }

            var remaining = new JsonArray(Array(node, "macros").Skip(promptSideCount).Select(m => m?.DeepClone()).ToArray());
            if (remaining.Count > 0) { Set(node, "macros", remaining); } else { Remove(node, "macros"); }
        }

        // Never-placed macros become snippets: the library entry leaves, the file moves.
        if (snippetised.Count > 0)
        {
            var macrosArray = Get(root, "macros") as JsonArray;
            var snippets = Get(root, "snippets") as JsonObject ?? new JsonObject();
            foreach (var macroId in snippetised)
            {
                var declaration = macrosArray?.OfType<JsonObject>().FirstOrDefault(m => Str(m, "id") == macroId);
                var file = declaration is null ? $"macros/{macroId}.md" : Str(declaration, "file") ?? $"macros/{macroId}.md";
                if (declaration is not null) { macrosArray!.Remove(declaration); }
                var target = $"snippets/{macroId}.md";
                if (files.TryGetValue(file, out var body))
                {
                    files.Remove(file);
                    files[target] = body;
                }

                snippets[macroId] = target;
                notices.Add($"Macro '{macroId}' was placed on no output and read prompt-side only; it is now the snippet '{macroId}' ({target}).");
            }

            Set(root, "snippets", snippets);
            if (macrosArray is { Count: 0 }) { Remove(root, "macros"); }
        }

        return snippetised;
    }

    // ==================================================================== data

    private static void RewriteData(JsonObject root, WorkflowPackageManifest manifest, Dictionary<string, string> files, List<string> notices)
    {
        if (Get(root, "data") is not JsonObject data)
        {
            return;
        }

        var snippets = Get(root, "snippets") as JsonObject ?? new JsonObject();
        var escaped = 0;

        foreach (var (id, pathNode) in data.ToList())
        {
            var path = pathNode?.GetValue<string>();
            if (path is null)
            {
                continue;
            }

            if (path.EndsWith('/'))
            {
                var directory = $"snippets/{id}/";
                foreach (var file in files.Keys.Where(f => f.StartsWith(path, StringComparison.Ordinal)).ToList())
                {
                    var body = files[file];
                    files.Remove(file);
                    var relative = file[path.Length..];
                    files[directory + relative] = relative == "index.json" ? body : Escape(body, ref escaped);
                }

                snippets[id] = directory;
            }
            else
            {
                var target = $"snippets/{id}.md";
                if (files.TryGetValue(path, out var body))
                {
                    files.Remove(path);
                    files[target] = Escape(body, ref escaped);
                }

                snippets[id] = target;
            }
        }

        Remove(root, "data");
        Set(root, "snippets", snippets);
        notices.Add($"data is now snippets ({data.Count} entr{(data.Count == 1 ? "y" : "ies")} moved under snippets/){(escaped > 0 ? $"; a literal '{{{{' was escaped in {escaped} file{(escaped == 1 ? "" : "s")}, since a snippet renders as a template" : "")}.");

        static string Escape(string body, ref int escaped)
        {
            if (!body.Contains("{{", StringComparison.Ordinal))
            {
                return body;
            }

            escaped++;
            return EscapeBraces(body);
        }
    }

    // =================================================================== texts

    private static void RewriteTexts(JsonObject root, WorkflowPackageManifest manifest, IReadOnlySet<string> snippetised, IReadOnlySet<string> slotMacroIds, Dictionary<string, string> files, List<string> notices)
    {
        // ≤24 macro placeholders → 25 objects, in every remaining macro text and every macro turned snippet.
        var macroFiles = Array(root, "macros").OfType<JsonObject>().Select(m => Str(m, "file")).Where(f => f is not null).Select(f => f!)
            .Concat(snippetised.Select(id => $"snippets/{id}.md"))
            .ToList();
        var rewritten = 0;

        foreach (var file in macroFiles)
        {
            if (!files.TryGetValue(file, out var text))
            {
                continue;
            }

            var replaced = WorkflowMacroPlaceholders.Pattern.Replace(text, match =>
            {
                var token = WorkflowMacroPlaceholders.TokenOf(match);
                if (!WorkflowMacroPlaceholders.TryParse(token, out var ns, out var id))
                {
                    return match.Value;
                }

                return ns switch
                {
                    "input" => $"{{{{ {TemplateObjects.Input}.{id} }}}}",
                    "data" => $"{{{{ {TemplateObjects.Snippet}.{id}.{TemplateMembers.Text} }}}}",
                    "classification" => $"{{{{ {TemplateObjects.Node}.{id}.{TemplateMembers.Value} }}}}",
                    "item" => $"{{{{ {TemplateObjects.Item}.{(id == "content" ? TemplateMembers.Text : id)} }}}}",
                    "run" => $"{{{{ {TemplateObjects.Run}.{id} }}}}",
                    "profile" => $"{{{{ {TemplateObjects.Profile}.{id} }}}}",
                    _ => match.Value
                };
            });

            if (replaced != text)
            {
                files[file] = replaced;
                rewritten++;
            }
        }

        if (rewritten > 0)
        {
            notices.Add($"{rewritten} macro text{(rewritten == 1 ? "" : "s")} now read objects where placeholders stood.");
        }

        // Slot markers → the slot read, in every template file.
        if (slotMacroIds.Count > 0)
        {
            foreach (var file in files.Keys.Where(f => f.StartsWith("prompts/", StringComparison.Ordinal) || f.StartsWith("snippets/", StringComparison.Ordinal) || f.StartsWith("macros/", StringComparison.Ordinal)).ToList())
            {
                var text = files[file];
                var replaced = WorkflowSlotMarker.Pattern.Replace(text, m => slotMacroIds.Contains(m.Groups[1].Value) ? $"{{{{ {TemplateObjects.Macro}.{m.Groups[1].Value}.{TemplateMembers.Prompt} }}}}" : m.Value);
                if (replaced != text)
                {
                    files[file] = replaced;
                }
            }
        }

        // Every remaining library macro needs an instruction at 25 — said once each.
        foreach (var macro in Array(root, "macros").OfType<JsonObject>())
        {
            if (Str(macro, "id") is { } id && string.IsNullOrWhiteSpace(Str(macro, "instruction")))
            {
                notices.Add($"Macro '{id}' needs an instruction before publishing (specVersion 25): what the model is told where its slot is read.");
            }
        }
    }

    // ================================================================= helpers

    private static Migration25Result Refused(string manifestJson, IReadOnlyDictionary<string, string> files, string refusal) =>
        Refused(manifestJson, files, new[] { refusal });

    private static Migration25Result Refused(string manifestJson, IReadOnlyDictionary<string, string> files, IReadOnlyList<string> refusals) =>
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
