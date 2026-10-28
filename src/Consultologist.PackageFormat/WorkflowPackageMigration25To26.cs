using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat;

/// <summary>What the 25→26 migration produced: the manifest and files at 26, one notice per rewrite, or the refusals that stopped it.</summary>
public sealed record Migration26Result(
    string ManifestJson,
    Dictionary<string, string> Files,
    IReadOnlyList<string> Notices,
    IReadOnlyList<string> Refusals)
{
    public bool Succeeded => Refusals.Count == 0;
}

/// <summary>
/// #1152 (package-format-v26-design.md § 13): the upgrade from 25 to 26 —
/// mechanical, and byte-identical in what the model sees. Every row of the
/// § 13 table is a step here, in the order the rows depend on: the texts
/// checked for a header already (a 25 text has none); the fans folded into
/// loops — <b>a loop per aggregated chain</b> (decided 2026-10-26): for each
/// body node an aggregator sources, one <c>for</c> over the collection whose
/// body is that node and the body nodes it reads inside the collection,
/// <c>ret</c> that node, the loop taking the collection's id when it is the
/// collection's only loop and <c>&lt;collection&gt;_&lt;ret&gt;</c> otherwise; a
/// node's <c>when</c> wrapped in an <c>if</c> node; the aggregators dissolved
/// into the deliverables' <c>sources[]</c> and their reads rewritten to
/// <c>deliverable.&lt;id&gt;.content</c> or the loop's; every gate keyed
/// <c>if</c>; the prompts by path; the renderer pipes taking the node; the
/// folder indexes as lists and every text's facts in its header; <c>.name</c>
/// to <c>.label</c>; the version. A refusal — a text with a header already, a
/// fanned node no aggregator reaches, a node read by two chains, a loop id
/// taken, a prompt bound by two nodes, an aggregator read nothing names —
/// applies nothing.
/// </summary>
public static class WorkflowPackageMigration25To26
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex NodeReference = new(@"\bnode\.([a-z][a-z0-9_]*)\b", RegexOptions.Compiled);
    private static readonly Regex PipeOnContent = new(@"\bnode\.([a-z][a-z0-9_]*)\.content\s*\|\s*(concept_bullets|concept_context)\b", RegexOptions.Compiled);
    private static readonly Regex ForVariable = new(@"\{\{-?\s*for\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+(?:snippet|macro)\.[a-z][a-z0-9_]*\s*-?\}\}", RegexOptions.Compiled);

    public static Migration26Result Migrate(string manifestJson, IReadOnlyDictionary<string, string> files)
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
        if (manifest.SpecVersion >= 26)
        {
            return Refused(manifestJson, files, $"The package is already at specVersion {manifest.SpecVersion}.");
        }

        if (manifest.SpecVersion < 25)
        {
            return Refused(manifestJson, files, $"Migrate to specVersion 25 first (the package is at {manifest.SpecVersion}; scripts/migrate-workflow-package.cs).");
        }

        var nodes = manifest.Nodes ?? new List<WorkflowNodeSpec>();
        var prompts = (manifest.Prompts ?? new List<WorkflowPromptSpec>()).GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().File, StringComparer.Ordinal);
        string? PromptTextOf(WorkflowNodeSpec node) =>
            node.Prompt is { } id && prompts.TryGetValue(id, out var file) && files.TryGetValue(file, out var text) ? text : null;

        // ----- step 1: a 25 text has no header -----
        var snippetScalars = new List<(string Id, string Path)>();
        var snippetFolders = new List<(string Id, string Dir, List<(string Id, string Name, string File, string? When)> Items)>();
        foreach (var (id, path) in manifest.Snippets ?? new Dictionary<string, string>())
        {
            if (path.EndsWith('/'))
            {
                if (files.TryGetValue(path + WorkflowDataResolver.IndexFileName, out var indexJson))
                {
                    var items = ReadIndex25(indexJson, id, refusals);
                    snippetFolders.Add((id, path, items));
                    foreach (var item in items)
                    {
                        OpensAlready(path + item.File);
                    }
                }
            }
            else
            {
                snippetScalars.Add((id, path));
                OpensAlready(path);
            }
        }

        foreach (var macro in manifest.Macros ?? new List<WorkflowMacroSpec>())
        {
            OpensAlready(macro.File);
        }

        void OpensAlready(string path)
        {
            if (files.TryGetValue(path, out var text) && WorkflowTextHeaders.Opens(text))
            {
                refusals.Add($"'{path}' opens with a header already; a 25 text has none — remove it, then upgrade.");
            }
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        // ----- step 2: the fans fold into loops, a loop per aggregated chain -----
        var fanned = nodes.Where(n => n.ForEach is not null).ToList();
        var collectionOf = fanned.ToDictionary(n => n.Id, n => CollectionOf(n.ForEach!), StringComparer.Ordinal);
        var aggregators = nodes.Where(n => n.Aggregate is { Count: > 0 }).ToList();
        var heads = new List<string>();
        foreach (var aggregator in aggregators)
        {
            foreach (var source in aggregator.Aggregate!)
            {
                var id = NodeIdOf(source);
                if (id is not null && collectionOf.ContainsKey(id) && !heads.Contains(id, StringComparer.Ordinal))
                {
                    heads.Add(id);
                }
            }
        }

        // A deliverable naming a fanned node directly is a chain head too.
        foreach (var deliverable in manifest.EffectiveDeliverables)
        {
            var id = NodeIdOf(deliverable.Node);
            if (id is not null && collectionOf.ContainsKey(id) && !heads.Contains(id, StringComparer.Ordinal))
            {
                heads.Add(id);
            }
        }

        var bodyOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var chainOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var head in heads)
        {
            var collection = collectionOf[head];
            var body = new List<string>();
            var pending = new Queue<string>();
            pending.Enqueue(head);
            while (pending.TryDequeue(out var id))
            {
                if (body.Contains(id, StringComparer.Ordinal))
                {
                    continue;
                }

                body.Add(id);
                var node = nodes.First(n => n.Id == id);
                foreach (Match match in NodeReference.Matches(PromptTextOf(node) ?? string.Empty))
                {
                    var read = match.Groups[1].Value;
                    if (collectionOf.TryGetValue(read, out var readCollection) && readCollection == collection && !body.Contains(read, StringComparer.Ordinal))
                    {
                        pending.Enqueue(read);
                    }
                }
            }

            foreach (var id in body)
            {
                if (chainOf.TryGetValue(id, out var other) && other != head)
                {
                    refusals.Add($"Node '{id}' is read by two aggregated chains ('{other}' and '{head}'); a 26 body node runs in one loop — split it, then upgrade.");
                }

                chainOf[id] = head;
            }

            bodyOf[head] = body;
        }

        foreach (var node in fanned)
        {
            if (!chainOf.ContainsKey(node.Id))
            {
                refusals.Add($"Node '{node.Id}' fans '{collectionOf[node.Id]}' but no aggregator sources it or a node that reads it; aggregate it or remove it, then upgrade.");
            }
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        var loopOf = new Dictionary<string, string>(StringComparer.Ordinal); // head → loop id
        var nodeIds = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var group in heads.GroupBy(h => collectionOf[h], StringComparer.Ordinal))
        {
            var collection = group.Key;
            var sole = group.Count() == 1 && !nodeIds.Contains(collection);
            foreach (var head in group)
            {
                var loopId = sole ? collection : $"{collection}_{head}";
                if (nodeIds.Contains(loopId) || loopOf.ContainsValue(loopId))
                {
                    refusals.Add($"Loop id '{loopId}' (for the chain returning '{head}' over {collection}) is taken by a node; rename that node, then upgrade.");
                }

                loopOf[head] = loopId;
            }
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        string LoopOfBody(string bodyNodeId) => loopOf[chainOf[bodyNodeId]];

        var rootNodes = Get(root, "nodes") as JsonArray ?? new JsonArray();
        foreach (var head in heads)
        {
            var loopId = loopOf[head];
            var body = bodyOf[head];
            var collection = collectionOf[head];
            var fanNode = nodes.First(n => n.Id == head);
            var label = Humanise(collection) + (loopId == collection ? string.Empty : " — " + fanNode.Label.ToLowerInvariant());
            var loop = new JsonObject
            {
                ["id"] = loopId,
                ["label"] = label,
                ["for"] = $"item in {fanNode.ForEach!.Replace(':', '.')}",
                ["nodes"] = new JsonArray(body.Select(id => (JsonNode)id).ToArray()),
                ["ret"] = $"node.{head}",
            };

            var first = IndexOfNode(rootNodes, body.First());
            rootNodes.Insert(first < 0 ? rootNodes.Count : first, loop);
            notices.Add($"Loop '{loopId}' over {collection} returns '{head}' with body [{string.Join(", ", body)}]{(loopId == collection ? string.Empty : " (named after its returned node: the collection has more than one chain)")}.");
        }

        foreach (var node in rootNodes.OfType<JsonObject>())
        {
            Remove(node, "forEach");
        }

        // ----- step 4 (before the aggregators resolve their sources): a node's when wraps in an if node -----
        var ifOf = new Dictionary<string, string>(StringComparer.Ordinal); // wrapped node → if node id
        foreach (var group in nodes.Where(n => n.When is not null && n.Aggregate is null).GroupBy(n => n.When!, StringComparer.Ordinal))
        {
            var wrapped = group.Select(n => n.Id).ToList();
            if (wrapped.Count > 1)
            {
                refusals.Add($"Nodes {string.Join(", ", wrapped.Select(id => $"'{id}'"))} share the gate '{group.Key}'; an if node returns one node — gate them separately, then upgrade.");
                continue;
            }

            var id = wrapped[0];
            var ifId = $"{id}_if";
            if (nodeIds.Contains(ifId))
            {
                refusals.Add($"If node id '{ifId}' (for node '{id}') is taken; rename that node, then upgrade.");
                continue;
            }

            var ifNode = new JsonObject
            {
                ["id"] = ifId,
                ["label"] = nodes.First(n => n.Id == id).Label,
                ["if"] = group.Key,
                ["then"] = new JsonObject { ["nodes"] = new JsonArray(id), ["ret"] = $"node.{id}" },
            };
            var at = IndexOfNode(rootNodes, id);
            rootNodes.Insert(at < 0 ? rootNodes.Count : at, ifNode);
            Remove(rootNodes.OfType<JsonObject>().First(n => Str(n, "id") == id), "when");
            ifOf[id] = ifId;
            notices.Add($"Node '{id}' gated by '{group.Key}' now runs inside if node '{ifId}', which returns it; readers read node.{ifId}.");
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        string Reach(string nodeId) =>
            chainOf.ContainsKey(nodeId) ? LoopOfBody(nodeId)
            : ifOf.TryGetValue(nodeId, out var ifId) ? ifId
            : nodeId;

        // ----- step 3: the aggregators dissolve into sources -----
        var aggregatorIds = aggregators.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var namedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var deliverable in manifest.EffectiveDeliverables)
        {
            var target = NodeIdOf(deliverable.Node);
            if (target is not null && aggregatorIds.Contains(target))
            {
                namedBy.TryAdd(target, new List<string>());
                namedBy[target].Add(deliverable.Id);
            }
        }

        IReadOnlyList<string> SourcesOf(WorkflowNodeSpec aggregator) =>
            aggregator.Aggregate!.Select(s => NodeIdOf(s) is { } id ? $"node.{Reach(id)}" : s).Distinct(StringComparer.Ordinal).ToList();

        var rootDeliverables = Get(root, "deliverables") as JsonArray ?? new JsonArray();
        foreach (var deliverable in rootDeliverables.OfType<JsonObject>())
        {
            var target = NodeIdOf(Str(deliverable, "node"));
            if (target is null)
            {
                continue;
            }

            var sources = aggregatorIds.Contains(target)
                ? SourcesOf(aggregators.First(a => a.Id == target))
                : new[] { $"node.{Reach(target)}" };
            Remove(deliverable, "node");
            Set(deliverable, "sources", new JsonArray(sources.Select(s => (JsonNode)s).ToArray()));
            notices.Add($"Deliverable '{Str(deliverable, "id")}' composes sources [{string.Join(", ", sources)}]{(aggregatorIds.Contains(target) ? $" (aggregator '{target}' dissolved)" : string.Empty)}.");
        }

        foreach (var aggregator in aggregators)
        {
            var at = IndexOfNode(rootNodes, aggregator.Id);
            if (at >= 0)
            {
                rootNodes.RemoveAt(at);
            }
        }

        // The reads of an aggregator's text.
        foreach (var aggregator in aggregators)
        {
            var read = $"node.{aggregator.Id}.content";
            var readers = outFiles.Where(pair => pair.Value.Contains(read, StringComparison.Ordinal)).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToList();
            if (readers.Count == 0)
            {
                continue;
            }

            string replacement;
            if (namedBy.TryGetValue(aggregator.Id, out var deliverables))
            {
                if (deliverables.Count > 1)
                {
                    refusals.Add($"'{readers[0]}' reads {read}, which deliverables {string.Join(", ", deliverables.Select(d => $"'{d}'"))} both name; read one deliverable, then upgrade.");
                    continue;
                }

                replacement = $"deliverable.{deliverables[0]}.content";
            }
            else
            {
                var sources = SourcesOf(aggregator);
                var sole = sources.Count == 1 ? NodeIdOf(sources[0]) : null;
                if (sole is null || !loopOf.ContainsValue(sole))
                {
                    refusals.Add($"'{readers[0]}' reads {read}, which no deliverable names and which composes {sources.Count} source{(sources.Count == 1 ? string.Empty : "s")}; read a deliverable or one loop, then upgrade.");
                    continue;
                }

                replacement = $"node.{sole}.content";
            }

            foreach (var path in readers)
            {
                outFiles[path] = outFiles[path].Replace(read, replacement, StringComparison.Ordinal);
            }

            notices.Add($"{read} now reads {replacement} in {string.Join(", ", readers)}.");
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        // Readers of a node now inside an if node read the if node.
        foreach (var (wrapped, ifId) in ifOf)
        {
            var ownPrompt = prompts.GetValueOrDefault(nodes.First(n => n.Id == wrapped).Prompt ?? string.Empty);
            foreach (var path in outFiles.Keys.Where(p => p != ownPrompt).ToList())
            {
                outFiles[path] = Regex.Replace(outFiles[path], $@"\bnode\.{Regex.Escape(wrapped)}\.", $"node.{ifId}.");
            }
        }

        // ----- step 5: every gate keyed if -----
        var gates = 0;
        foreach (var node in rootNodes.OfType<JsonObject>())
        {
            foreach (var instance in Array(node, "macros").OfType<JsonObject>())
            {
                gates += KeyIf(instance);
            }
        }

        foreach (var deliverable in rootDeliverables.OfType<JsonObject>())
        {
            gates += KeyIf(deliverable);
            foreach (var instance in Array(deliverable, "macros").OfType<JsonObject>())
            {
                gates += KeyIf(instance);
            }
        }

        if (gates > 0)
        {
            notices.Add($"{gates} gate{(gates == 1 ? string.Empty : "s")} now keyed if (when, and forItem as item.id == …).");
        }

        // ----- step 6: a node names its prompt file -----
        var bound = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var node in rootNodes.OfType<JsonObject>())
        {
            if (Str(node, "prompt") is { } promptId && prompts.TryGetValue(promptId, out var file))
            {
                bound.TryAdd(promptId, new List<string>());
                bound[promptId].Add(Str(node, "id") ?? "?");
                Set(node, "prompt", file);
            }
        }

        foreach (var (promptId, readers) in bound.Where(pair => pair.Value.Count > 1))
        {
            refusals.Add($"Prompt '{promptId}' is bound by nodes {string.Join(", ", readers.Select(r => $"'{r}'"))}; at 26 a node names its own file — copy the file per node, then upgrade.");
        }

        if (refusals.Count > 0)
        {
            return Refused(manifestJson, files, refusals);
        }

        if (Get(root, "prompts") is not null)
        {
            Remove(root, "prompts");
            notices.Add($"{bound.Count} node{(bound.Count == 1 ? string.Empty : "s")} now name{(bound.Count == 1 ? "s" : string.Empty)} the prompt file; prompts[] removed.");
        }

        // ----- step 7: a renderer pipe takes the node -----
        var pipes = 0;
        foreach (var path in outFiles.Keys.ToList())
        {
            outFiles[path] = PipeOnContent.Replace(outFiles[path], match => { pipes++; return $"node.{match.Groups[1].Value} | {match.Groups[2].Value}"; });
        }

        if (pipes > 0)
        {
            notices.Add($"{pipes} renderer pipe{(pipes == 1 ? string.Empty : "s")} now take{(pipes == 1 ? "s" : string.Empty)} the node (node.<id> | pipe).");
        }

        // ----- step 8: the headers, the folder lists, the macros map -----
        foreach (var (id, path) in snippetScalars)
        {
            var label = Humanise(id);
            outFiles[path] = HeaderOf(label) + outFiles[path];
            notices.Add($"Snippet '{id}' labelled '{label}' — invented from the id; review it.");
        }

        foreach (var (id, dir, items) in snippetFolders)
        {
            var list = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["id"] = i.Id, ["file"] = i.File }).ToArray());
            outFiles[dir + WorkflowDataResolver.IndexFileName] = list.ToJsonString(WriteOptions) + "\n";
            foreach (var item in items)
            {
                outFiles[dir + item.File] = HeaderOf(item.Name, @if: item.When) + outFiles[dir + item.File];
            }

            notices.Add($"Snippet folder '{id}' index is the ordered list; each item file opens with its header (label{(items.Any(i => i.When is not null) ? ", if" : string.Empty)}).");
        }

        if (manifest.Macros is { Count: > 0 } macros)
        {
            var map = new JsonObject();
            foreach (var macro in macros)
            {
                map[macro.Id] = macro.File;
                if (outFiles.TryGetValue(macro.File, out var text))
                {
                    outFiles[macro.File] = HeaderOf(macro.Label ?? Humanise(macro.Id), instruction: macro.Instruction, optional: macro.Optional, @default: macro.Default) + text;
                }
            }

            Set(root, "macros", map);
            notices.Add($"macros is the map of id to file; each macro file opens with its header (label, instruction, optional, default).");
        }

        // ----- step 9: .name reads .label -----
        var names = 0;
        foreach (var path in outFiles.Keys.ToList())
        {
            var text = outFiles[path];
            var variables = ForVariable.Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
            var pattern = @"\b(item|snippet\.[a-z][a-z0-9_]*|macro\.[a-z][a-z0-9_]*" + string.Concat(variables.Select(v => "|" + Regex.Escape(v))) + @")\.name\b";
            outFiles[path] = Regex.Replace(text, pattern, match => { names++; return match.Groups[1].Value + ".label"; });
        }

        if (names > 0)
        {
            notices.Add($"{names} read{(names == 1 ? string.Empty : "s")} of .name now read{(names == 1 ? "s" : string.Empty)} .label.");
        }

        // ----- step 10: the version -----
        Set(root, "specVersion", 26);

        return new Migration26Result(root.ToJsonString(WriteOptions), outFiles, notices, refusals);
    }

    // ================================================================= helpers

    private static int KeyIf(JsonObject gated)
    {
        var count = 0;
        var when = Str(gated, "when");
        var forItem = Str(gated, "forItem");
        if (when is null && forItem is null)
        {
            return 0;
        }

        var parts = new List<string>();
        if (when is not null)
        {
            parts.Add(forItem is null ? when : $"({when})");
            Remove(gated, "when");
            count++;
        }

        if (forItem is not null)
        {
            parts.Add($"item.id == \"{forItem}\"");
            Remove(gated, "forItem");
            count++;
        }

        Set(gated, "if", string.Join(" && ", parts));
        return count;
    }

    private static List<(string Id, string Name, string File, string? When)> ReadIndex25(string indexJson, string folder, List<string> refusals)
    {
        var items = new List<(string, string, string, string?)>();
        try
        {
            var index = JsonNode.Parse(indexJson)?.AsObject();
            foreach (var item in (index is null ? null : Get(index, "items") as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                items.Add((Str(item, "id") ?? string.Empty, Str(item, "name") ?? string.Empty, Str(item, "file") ?? string.Empty, Str(item, "when")));
            }
        }
        catch (JsonException ex)
        {
            refusals.Add($"Snippet folder '{folder}' index.json does not parse: {ex.Message}");
        }

        return items;
    }

    /// <summary>The JSON header a 26 text opens with (§ 5.1) — <see cref="WorkflowTextHeaders.Compose"/>'s bytes.</summary>
    public static string HeaderOf(string label, string? @if = null, string? instruction = null, bool? optional = null, bool? @default = null) =>
        WorkflowTextHeaders.Compose(new WorkflowTextHeader(label, @if, instruction, optional, @default));

    /// <summary>A label invented from an id: `snomed_tool_guidance` → `Snomed tool guidance`.</summary>
    public static string Humanise(string id)
    {
        var words = id.Replace('_', ' ').Trim();
        return words.Length == 0 ? id : char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string CollectionOf(string forEach) =>
        forEach.Replace(':', '.') is var spelled && spelled.IndexOf('.') is > 0 and var dot ? spelled[(dot + 1)..] : forEach;

    private static string? NodeIdOf(string? reference) =>
        reference is not null && reference.StartsWith("node.", StringComparison.Ordinal) ? reference[5..]
        : reference is not null && reference.StartsWith("node:", StringComparison.Ordinal) ? reference[5..]
        : null;

    private static int IndexOfNode(JsonArray nodes, string id)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is JsonObject node && Str(node, "id") == id)
            {
                return i;
            }
        }

        return -1;
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
