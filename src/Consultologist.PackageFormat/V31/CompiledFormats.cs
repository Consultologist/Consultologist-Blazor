using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the formats part of <c>resolve.json</c> (part 07 § 2.4; part 02 §§ 2.3, 6.2, 6.3,
/// 7; D5), built once for <c>ResolveCompiler</c> to place. <see cref="Bundle"/> is <c>{ "$defs": { key: schema } }</c>:
/// every format the package uses, from its three homes, each with its <c>$id</c>, its <c>$schema</c> and its
/// annotations, every name inside rewritten to <c>#/$defs/&lt;key&gt;</c> (<see cref="Rewrite"/>).
/// <see cref="FormatHashes"/> and <see cref="TypeHashes"/> hold each bundled format's two hashes, as
/// <see cref="FormatTypes.FormatHash"/> and <see cref="FormatTypes.TypeHash"/> give them in the format's own scope
/// (the rewrite changes neither). <see cref="Bindings"/> is format key, then producer, then
/// <c>{ agentName, agentVersion, via }</c>, one for each format and producer an agent node is bound by.
/// <see cref="Refusals"/> holds A05-004, a function's own format whose key another used format has.
/// Every map is keyed by bundle key: a format's id, or <c>&lt;function&gt;__&lt;id&gt;</c> for a function's own (D5).
/// </summary>
public sealed record CompiledFormats(
    JsonObject Bundle,
    JsonObject FormatHashes,
    JsonObject TypeHashes,
    JsonObject Bindings,
    IReadOnlyList<SourceRefusal> Refusals)
{
    /// <summary>The producer every agent binding was found for (part 02 § 1.3: at 31 only <c>generated</c> has bindings).</summary>
    private const string Producer = "generated";

    /// <summary>As deep as <see cref="FormatSet"/> looks for names, so the rewrite reaches every name the bundle followed.</summary>
    private const int WalkDepth = 64;

    /// <summary>
    /// The bundle, its hashes and the bindings of <paramref name="package"/>. A format is <i>used</i> when a
    /// <c>format</c> member names it (an input's, a node's or check's at any depth, a function's and its parameters',
    /// a renderer's <c>input</c> and <c>format</c>, a snippet's, a snippet folder's, a macro's, a deliverable's), in
    /// the scope that owns the member, or when a used format reaches it through its names: a function's own format in
    /// the function's scope, any other in the package's. Returns for any package: a name no scope knows is left out,
    /// a format that reaches itself is bundled once. A null argument is the caller's error.
    /// </summary>
    public static CompiledFormats Build(SourcePackage package, PackageTypes types, TypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(registry);

        var used = new Used(types);
        used.Members(package);
        var refusals = new List<SourceRefusal>();
        var defs = new JsonObject();
        var formatHashes = new JsonObject();
        var typeHashes = new JsonObject();

        // The package's scope takes its keys first; a function's own format then needs a key no other format has.
        var claimed = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var entry in used.Entries.Where(e => e.Function == null))
            claimed.Add(entry.Key, entry);
        foreach (var entry in used.Entries.Where(e => e.Function != null))
        {
            if (!claimed.TryAdd(entry.Key, entry))
            {
                var file = entry.Function!.Definitions.Formats.Items.FirstOrDefault(f => f.Id == entry.Format.Id);
                refusals.Add(ResolveSentences.BundleKeyTaken(entry.Function.Id, entry.Format.Id, file?.Path ?? entry.Function.Path));
            }
        }

        foreach (var (key, entry) in claimed.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            defs[key] = Bundled(package.Manifest, entry);
            var name = JsonValue.Create(entry.Format.Id);
            formatHashes[key] = FormatTypes.FormatHash(name, entry.Scope);
            typeHashes[key] = FormatTypes.TypeHash(name, entry.Scope);
        }

        return new CompiledFormats(
            new JsonObject { ["$defs"] = defs },
            formatHashes,
            typeHashes,
            BindingsOf(package, types),
            refusals);
    }

    /// <summary>
    /// <paramref name="format"/> as <c>resolve.json</c> writes it, a clone: a name <paramref name="scope"/> knows becomes
    /// <c>{ "$ref": "#/$defs/&lt;key&gt;" }</c>, and in a schema every name it holds (a <c>$ref</c>, or a name written as
    /// a schema, under <c>properties</c>, <c>items</c>, <c>anyOf</c> and <c>$defs</c>, as <see cref="FormatSet"/> finds
    /// them) likewise. The key is the name, or for a format of <paramref name="function"/>'s own
    /// <c>&lt;function&gt;__&lt;name&gt;</c> (D5). A name the scope does not know, a <c>#</c> reference and anything that
    /// is not a schema are copied as written. Never throws on package content.
    /// </summary>
    public static JsonNode? Rewrite(JsonNode? format, FormatSet scope, SourceFunction? function)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (format is JsonValue value)
            return Reference(value, scope, function) ?? value.DeepClone();

        var copy = format?.DeepClone();
        if (copy is JsonObject schema)
            Walk(schema, scope, function, 0);
        return copy;
    }

    /// <summary>The bundle key of a format a scope found: its id, or <c>&lt;function&gt;__&lt;id&gt;</c> for the function's own.</summary>
    private static string KeyOf(NamedFormat found, SourceFunction? function) =>
        found.Origin == FormatOrigin.Function && function != null ? $"{function.Id}__{found.Id}" : found.Id;

    /// <summary>
    /// A bundle entry: the schema with its names rewritten; a package format's <c>$id</c> derived (part 02 § 6.2), a
    /// function's own with the function in it (D5), and either given the dialect's <c>$schema</c> when it has none; a
    /// built-in or catalogue format keeps its <c>$id</c> and <c>$schema</c> as published.
    /// </summary>
    private static JsonObject Bundled(SourceManifest manifest, Entry entry)
    {
        var schema = (JsonObject)Rewrite(entry.Format.Schema, entry.Scope, entry.Function)!;
        var package = $"urn:consultologist:package:{manifest.Name}@{manifest.Version}";
        switch (entry.Format.Origin)
        {
            case FormatOrigin.Package:
                schema["$id"] = $"{package}:format:{entry.Format.Id}";
                break;
            case FormatOrigin.Function:
                schema["$id"] = $"{package}:function:{entry.Function!.Id}:format:{entry.Format.Id}";
                break;
            default:
                return schema;
        }

        if (!schema.ContainsKey("$schema"))
            schema["$schema"] = FormatDialect.DialectId;
        return schema;
    }

    private static JsonObject? Reference(JsonValue value, FormatSet scope, SourceFunction? function) =>
        BareName(value) is { } name && scope.Find(name) is { } found
            ? new JsonObject { ["$ref"] = $"#/$defs/{KeyOf(found, function)}" }
            : null;

    private static void Walk(JsonObject schema, FormatSet scope, SourceFunction? function, int depth)
    {
        if (depth > WalkDepth)
            return;

        if (schema["$ref"] is JsonValue target && BareName(target) is { } name && scope.Find(name) is { } found)
            schema["$ref"] = $"#/$defs/{KeyOf(found, function)}";

        foreach (var member in new[] { "properties", "$defs" })
        {
            if (schema[member] is JsonObject map)
            {
                foreach (var key in map.Select(p => p.Key).ToList())
                {
                    if (Member(map[key], scope, function, depth) is { } reference)
                        map[key] = reference;
                }
            }
        }

        if (Member(schema["items"], scope, function, depth) is { } items)
            schema["items"] = items;
        if (schema["anyOf"] is JsonArray members)
        {
            for (var i = 0; i < members.Count; i++)
            {
                if (Member(members[i], scope, function, depth) is { } reference)
                    members[i] = reference;
            }
        }
    }

    /// <summary>A schema position's value: a name gives the <c>$ref</c> to put in its place; an object is walked in place (null).</summary>
    private static JsonObject? Member(JsonNode? node, FormatSet scope, SourceFunction? function, int depth)
    {
        if (node is JsonValue value)
            return Reference(value, scope, function);
        if (node is JsonObject schema)
            Walk(schema, scope, function, depth + 1);
        return null;
    }

    private static string? BareName(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 && s[0] != '#' ? s : null;

    /// <summary>
    /// Each agent node's binding (<see cref="PackageTypes.Bindings"/>, by path), under its format's key and the producer,
    /// in walk order, the first kept. <c>via</c> is <see cref="AgentBindings"/>'s, <c>format</c> or <c>custom</c>; the
    /// reserved <c>producer</c> route has no row in <c>sentences.json</c> and <see cref="AgentBindings"/> never gives it.
    /// </summary>
    private static JsonObject BindingsOf(SourcePackage package, PackageTypes types)
    {
        var bindings = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);

        void Body(IReadOnlyList<SourceNode> nodes, FormatSet scope, SourceFunction? function)
        {
            foreach (var node in Walk(nodes))
            {
                if (!types.Bindings.TryGetValue(node.Path, out var binding))
                    continue;
                var format = types.NodeFormats.TryGetValue(node.Path, out var declared) ? declared : node.Header["format"];
                if (FormatSet.NameOf(format) is not { } name || scope.Find(name) is not { } found)
                    continue;

                var key = KeyOf(found, function);
                if (!bindings.ContainsKey(key))
                {
                    bindings[key] = new JsonObject
                    {
                        [Producer] = new JsonObject
                        {
                            ["agentName"] = binding.AgentName,
                            ["agentVersion"] = binding.AgentVersion,
                            ["via"] = binding.Via,
                        },
                    };
                }
            }
        }

        Body(package.Nodes.Nodes, types.Formats, null);
        Body(package.Checks.Nodes, types.Formats, null);
        foreach (var function in package.Functions.Items)
            Body(function.Body.Nodes, ScopeOf(types, function), function);

        var result = new JsonObject();
        foreach (var (key, value) in bindings)
            result[key] = value;
        return result;
    }

    /// <summary>The function's scope as the type rules built it; for a package they never accepted, built the same way.</summary>
    private static FormatSet ScopeOf(PackageTypes types, SourceFunction function) =>
        types.FunctionFormats.TryGetValue(function.Id, out var scope)
            ? scope
            : types.Formats.ForFunction(
                function.Definitions.Formats.Items,
                SourceLayout.Lines((function.Index["uses"] as JsonObject)?["formats"]) ?? []);

    /// <summary>Every node under <paramref name="nodes"/>, a flow node before its bodies' nodes.</summary>
    private static IEnumerable<SourceNode> Walk(IReadOnlyList<SourceNode> nodes)
    {
        var pending = new Stack<SourceNode>(nodes.Reverse());
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            yield return node;
            foreach (var block in Blocks(node).Reverse())
            {
                for (var i = block.Nodes.Count - 1; i >= 0; i--)
                    pending.Push(block.Nodes[i]);
            }
        }
    }

    private static IEnumerable<SourceBlock> Blocks(SourceNode node) => node switch
    {
        SourceFor loop => [loop.Body],
        SourceWhile loop => [loop.Body],
        SourceIf branch => branch.Else == null ? [branch.Then] : [branch.Then, branch.Else],
        SourceCase choice => choice.Branches.Select(b => b.Body).Concat(choice.Else == null ? [] : [choice.Else]),
        _ => [],
    };

    /// <summary>One used format: its bundle key, the format, the scope its names resolve in, and its function if it is one's own.</summary>
    private sealed record Entry(string Key, NamedFormat Format, FormatSet Scope, SourceFunction? Function);

    /// <summary>The used formats, each once, in the order first named; then every format they reach.</summary>
    private sealed class Used(PackageTypes types)
    {
        private readonly FormatSet package = types.Formats;

        private readonly HashSet<(string? Function, string Id)> seen = [];

        public List<Entry> Entries { get; } = [];

        public void Members(SourcePackage source)
        {
            foreach (var input in source.Manifest.Inputs)
                Name(input.Json["format"], package, null);
            Nodes(source.Nodes.Nodes, package, null);
            Nodes(source.Checks.Nodes, package, null);
            Definitions(source.Definitions, package, null);
            Deliverables(source.Deliverables);

            foreach (var function in source.Functions.Items)
            {
                var scope = ScopeOf(types, function);
                Name(function.Index["format"], scope, function);
                foreach (var parameter in function.Inputs)
                    Name(parameter.Json["format"], scope, function);
                Nodes(function.Body.Nodes, scope, function);
                Definitions(function.Definitions, scope, function);
            }
        }

        private void Nodes(IReadOnlyList<SourceNode> nodes, FormatSet scope, SourceFunction? function)
        {
            foreach (var node in Walk(nodes))
                Name(node.Header["format"], scope, function);
        }

        private void Definitions(SourceDefinitions definitions, FormatSet scope, SourceFunction? function)
        {
            foreach (var renderer in definitions.Renderers.Items)
            {
                Name(renderer.Header["input"], scope, function);
                Name(renderer.Header["format"], scope, function);
            }

            foreach (var entry in definitions.Snippets.Items)
            {
                if (entry is SourceSingleText { Text: var snippet })
                    Name(snippet.Header["format"], scope, function);
                else if (entry is SourceTextFolder folder)
                    Name(folder.Index["format"], scope, function);
            }

            foreach (var entry in definitions.Macros.Items)
            {
                IEnumerable<SourceText> macros = entry switch
                {
                    SourceSingleText single => [single.Text],
                    SourceTextFolder folder => folder.Items,
                    _ => [],
                };
                foreach (var macro in macros)
                    Name(macro.Header["format"], scope, function);
            }
        }

        private void Deliverables(SourceDeliveryFolder folder)
        {
            foreach (var deliverable in folder.Deliverables)
                Name(deliverable.Header["format"], package, null);
            foreach (var inner in folder.Folders)
                Deliverables(inner);
        }

        /// <summary>Every name <paramref name="format"/> holds, found in <paramref name="scope"/>, and what each reaches, breadth first.</summary>
        private void Name(JsonNode? format, FormatSet scope, SourceFunction? function)
        {
            var pending = new Queue<(JsonNode? Format, FormatSet Scope, SourceFunction? Function)>();
            pending.Enqueue((format, scope, function));
            while (pending.Count > 0)
            {
                var (next, set, owner) = pending.Dequeue();
                foreach (var (name, _) in FormatSet.References(next))
                {
                    if (set.Find(name) is not { } found)
                        continue;

                    // A function's own format belongs to it; a name written in any other format is the package's.
                    var own = found.Origin == FormatOrigin.Function ? owner : null;
                    var home = own == null ? package : set;
                    if (!seen.Add((own?.Id, found.Id)))
                        continue;

                    Entries.Add(new Entry(KeyOf(found, own), found, home, own));
                    pending.Enqueue((found.Schema, home, own));
                }
            }
        }
    }
}
