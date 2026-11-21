using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the functions of a package (part 04 §§ 7.3, 7.6, 7.7, 7.8; § 10.1's "Functions
/// and calls" rows, § 10.2's S04-074): each function's <c>uses</c> (S04-052 for a package snippet a header reads
/// unlisted, S04-053, and the S04-074 warning for a format; snippets, macros and renderers read by bodies are the
/// seam's, Task 5), its <c>ret</c> (S04-057 to S04-061), the call graph (S04-054, S04-055) and its depth (S04-056), and
/// the profile a parameter carries into a condition (S04-069).
/// </summary>
public static partial class SourceResolve
{
    /// <summary>The members of <c>uses</c> and the word each kind of definition is called in a sentence.</summary>
    private static readonly (string Member, string Kind)[] UseKinds =
        [("snippets", "snippet"), ("macros", "macro"), ("formats", "format"), ("renderers", "renderer")];

    /// <summary>Every function in listed order: <c>uses</c>, then <c>ret</c>; the unnamed formats to <paramref name="warnings"/>.</summary>
    private static void Functions(SourcePackage package, PackageTypes types, List<SourceRefusal> refusals, List<SourceRefusal> warnings)
    {
        foreach (var function in package.Functions.Items)
        {
            var path = SourceLayout.Join(function.Path, "index.json");
            var formats = types.FunctionFormats.GetValueOrDefault(function.Id) ?? types.Formats;
            foreach (var (member, kind) in UseKinds)
            {
                var own = Own(function, member);
                foreach (var id in Uses(function, member).Distinct(StringComparer.Ordinal).Where(own.Contains))
                    refusals.Add(ResolveSentences.OwnAndUsed(function.Id, kind, id, path));
            }

            Unlisted(package, function, refusals);
            Ret(types, function, formats, path, refusals);
            var named = Reached(package, function, Named(function));
            foreach (var id in Uses(function, "formats").Distinct(StringComparer.Ordinal).Where(id => !named.Contains(id)))
                warnings.Add(ResolveSentences.UsedNever(function.Id, "format", id, path));
        }
    }

    /// <summary>The ids <paramref name="function"/>'s <c>uses</c> lists under <paramref name="member"/>, as written.</summary>
    private static IReadOnlyList<string> Uses(SourceFunction function, string member) =>
        SourceLayout.Lines((function.Index["uses"] as JsonObject)?[member]) ?? [];

    /// <summary>The ids of <paramref name="function"/>'s own definitions of the folder <paramref name="member"/>.</summary>
    private static HashSet<string> Own(SourceFunction function, string member)
    {
        var d = function.Definitions;
        IEnumerable<string> ids = member switch
        {
            "snippets" => d.Snippets.Items.Select(i => i.Id),
            "macros" => d.Macros.Items.Select(i => i.Id),
            "formats" => d.Formats.Items.Select(i => i.Id),
            _ => d.Renderers.Items.Select(i => i.Id),
        };
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The file a node's header is in: a leaf's own, a flow node's <c>index.json</c>.</summary>
    private static string HeaderFile(SourceNode node) => node is SourceLeaf ? node.Path : SourceLayout.Join(node.Path, "index.json");

    /// <summary>
    /// S04-052: a header inside <paramref name="function"/> (a loop's <c>in</c>, a call's <c>with</c>) that names
    /// <c>snippet.&lt;id&gt;</c>, not its own but the package's, which its <c>uses</c> does not list; said once per
    /// snippet, at the first header that reads it.
    /// </summary>
    private static void Unlisted(SourcePackage package, SourceFunction function, List<SourceRefusal> refusals)
    {
        var own = Own(function, "snippets");
        var listed = Uses(function, "snippets").ToHashSet(StringComparer.Ordinal);
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in ScopeTree.Walk(function.Body.Nodes))
        {
            IEnumerable<string?> references = node switch
            {
                SourceFor loop => [loop.In],
                SourceLeaf { Kind: "call" } call when call.Header["with"] is JsonObject with => with.Select(p => SourceLayout.Text(p.Value)),
                _ => [],
            };
            foreach (var reference in references)
            {
                if (ReferenceString.Parse(reference) is { Segments: ["snippet", var id, ..] } && !own.Contains(id) && !listed.Contains(id)
                    && package.Definitions.Snippets.Items.Any(s => s.Id == id) && said.Add(id))
                {
                    refusals.Add(ResolveSentences.UsesUnlisted(function.Id, "snippet", id, HeaderFile(node)));
                }
            }
        }
    }

    /// <summary>
    /// part 04 § 7.6: <paramref name="function"/>'s <c>ret</c> always has a value (S04-057 for an <c>if</c> with no
    /// <c>else</c>, S04-059 for a <c>case</c> with an empty branch, else S04-058 for what may be skipped inside it, or
    /// for a <c>ret</c> skipped by its own <c>if</c>); then its bodies return one format (S04-060); then the function's
    /// <c>format</c> is its <c>ret</c>'s (S04-061), each said only when the one before is not. A <c>ret</c> that names
    /// no top-level node is the reader's.
    /// </summary>
    private static void Ret(PackageTypes types, SourceFunction function, FormatSet formats, string path, List<SourceRefusal> refusals)
    {
        if (function.Body.Nodes.FirstOrDefault(n => n.Key == function.Body.Ret) is not { } ret)
            return;

        SourceRefusal? fault = ret switch
        {
            SourceIf { Else: null } => ResolveSentences.ReturnsAnIfWithNoElse(function.Id, ret.Id, path),
            SourceCase choice when ScopeTree.EmptyValues(choice) is { Count: > 0 } empty =>
                ResolveSentences.ReturnsACaseWithAnEmptyBranch(function.Id, ret.Id, Quoted(empty), path),
            _ when Skippable(ret, null) is { } skipped =>
                ResolveSentences.ReturnsASkippableNode(function.Id, Local(skipped.Node), (skipped.Owner ?? skipped.Node).Id, path),
            _ => null,
        };
        if (fault != null)
        {
            refusals.Add(fault);
            return;
        }

        IEnumerable<SourceBlock> bodies = ret switch
        {
            SourceIf branch => [branch.Then, .. branch.Else is { } otherwise ? [otherwise] : Array.Empty<SourceBlock>()],
            SourceCase choice => choice.Branches.Select(b => b.Body).Concat(choice.Else is { } rest ? [rest] : []),
            _ => [],
        };
        var returned = bodies
            .Select(b => b.Nodes.FirstOrDefault(n => n.Key == b.Ret))
            .OfType<SourceNode>()
            .Select(n => types.NodeFormats.GetValueOrDefault(n.Path))
            .OfType<JsonNode>()
            .ToList();
        if (returned.FirstOrDefault(f => !FormatTypes.Same(f, returned[0], formats)) is { } other)
        {
            refusals.Add(ResolveSentences.ReturnsTwoFormats(function.Id, ret.Id, TypeStrings.Print(returned[0], formats), TypeStrings.Print(other, formats), path));
            return;
        }

        if (function.Index["format"] is { } declared && types.NodeFormats.GetValueOrDefault(ret.Path) is { } format
            && !FormatTypes.Same(declared, format, formats))
        {
            refusals.Add(ResolveSentences.FunctionFormatDisagrees(function.Id, TypeStrings.Print(declared, formats), ret.Id, TypeStrings.Print(format, formats), path));
        }
    }

    /// <summary>
    /// Where <paramref name="node"/>, read as a <c>ret</c>, may have no value: null when it is
    /// <see cref="ScopeTree.AlwaysValued"/>; through an <c>if</c> with an <c>else</c> or a <c>case</c> with no empty branch,
    /// the first of their bodies' <c>ret</c>s that may be skipped, with the flow node whose body it is; otherwise
    /// <paramref name="node"/> itself, with <paramref name="owner"/>. Bounded by the reader's nesting limit.
    /// </summary>
    private static (SourceNode Node, SourceNode? Owner)? Skippable(SourceNode node, SourceNode? owner)
    {
        if (ScopeTree.AlwaysValued(node))
            return null;
        IEnumerable<SourceBlock> blocks = node switch
        {
            SourceIf { Else: { } otherwise } branch => [branch.Then, otherwise],
            SourceCase choice when ScopeTree.EmptyValues(choice).Count == 0 =>
                choice.Branches.Select(b => b.Body).Concat(choice.Else is { } rest ? [rest] : []),
            _ => [],
        };
        foreach (var block in blocks)
        {
            if (block.Nodes.FirstOrDefault(n => n.Key == block.Ret) is { } child && Skippable(child, node) is { } found)
                return found;
        }

        return (node, owner);
    }

    /// <summary>A node's full name inside its function: its key without the root (<c>node.checked.final</c> → <c>checked.final</c>).</summary>
    private static string Local(SourceNode node) => node.Key.StartsWith("node.", StringComparison.Ordinal) ? node.Key["node.".Length..] : node.Key;

    /// <summary>
    /// Every format name <paramref name="function"/> writes (D12): its <c>format</c>, its parameters', its nodes', its
    /// own definitions' (a snippet's or folder's <c>format</c>, a renderer's <c>input</c> and <c>format</c>) and the names
    /// its own format files reference; a name as the member's value or as a <c>$ref</c> inside an inline schema.
    /// </summary>
    private static HashSet<string> Named(SourceFunction function)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        Names(function.Index["format"], named, top: true, 0);
        foreach (var parameter in function.Inputs)
            Names(parameter.Json["format"], named, top: true, 0);
        foreach (var node in ScopeTree.Walk(function.Body.Nodes))
            Names(node.Header["format"], named, top: true, 0);

        var d = function.Definitions;
        foreach (var entry in d.Snippets.Items.Concat(d.Macros.Items))
        {
            switch (entry)
            {
                case SourceSingleText single:
                    Names(single.Text.Header["format"], named, top: true, 0);
                    break;
                case SourceTextFolder folder:
                    Names(folder.Index["format"], named, top: true, 0);
                    foreach (var item in folder.Items)
                        Names(item.Header["format"], named, top: true, 0);
                    break;
            }
        }

        foreach (var renderer in d.Renderers.Items)
        {
            Names(renderer.Header["input"], named, top: true, 0);
            Names(renderer.Header["format"], named, top: true, 0);
        }

        foreach (var format in d.Formats.Items)
            Names(format.Schema, named, top: false, 0);
        return named;
    }

    /// <summary>
    /// #1502 (Task 5): <paramref name="named"/> and every format a named one reaches through its schema's <c>$ref</c>s, at
    /// any depth: the function's own formats first, then the package's (a listed format that only another listed or
    /// named format references is used, S04-074). Each format is read once.
    /// </summary>
    private static HashSet<string> Reached(SourcePackage package, SourceFunction function, HashSet<string> named)
    {
        var schemas = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var format in package.Definitions.Formats.Items)
            schemas[format.Id] = format.Schema;
        foreach (var format in function.Definitions.Formats.Items)
            schemas[format.Id] = format.Schema;

        var queue = new Queue<string>(named.Order(StringComparer.Ordinal));
        var read = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!read.Add(id) || !schemas.TryGetValue(id, out var schema))
                continue;
            var inner = new HashSet<string>(StringComparer.Ordinal);
            Names(schema, inner, top: false, 0);
            foreach (var name in inner.Order(StringComparer.Ordinal))
            {
                if (named.Add(name))
                    queue.Enqueue(name);
            }
        }

        return named;
    }

    /// <summary>The format names in <paramref name="node"/>: itself when it is text at the <paramref name="top"/>, and every <c>$ref</c> inside.</summary>
    private static void Names(JsonNode? node, HashSet<string> named, bool top, int depth)
    {
        if (depth > SourceBounds.JsonDepth)
            return;
        switch (node)
        {
            case JsonValue when top && SourceLayout.Text(node) is { } name:
                named.Add(name);
                break;
            case JsonObject schema:
                foreach (var (key, value) in schema)
                {
                    if (key == "$ref" && SourceLayout.Text(value) is { } reference)
                        named.Add(reference);
                    else if (value is JsonObject or JsonArray)
                        Names(value, named, top: false, depth + 1);
                }

                break;
            case JsonArray members:
                foreach (var member in members)
                    Names(member, named, top: false, depth + 1);
                break;
        }
    }

    // ---- The call graph ------------------------------------------------------------------------

    /// <summary>
    /// The call graph (a function → each function a call anywhere in its body runs, in source order): each ring said
    /// once (S04-055 for a function calling itself, S04-054 naming the ring in full), from its ordinal-first member, as
    /// the shortest ring through it (its callees taken in ordinal order), at the call that starts it; a set of
    /// functions that call each other in several rings is said once. With no ring, every call's depth (§ 7.8), and
    /// S04-056 on each call outside every function whose chain nests deeper than <see cref="FormatBounds.Depth"/>.
    /// Visits each function a bounded number of times: no recursion on the graph.
    /// </summary>
    private static Dictionary<SourceLeaf, int> Graph(ScopeTree tree, SourcePackage package, List<Bound> calls, List<SourceRefusal> refusals)
    {
        var callee = new Dictionary<SourceLeaf, SourceFunction>(ReferenceEqualityComparer.Instance);
        foreach (var bound in calls)
            callee[bound.Call] = bound.Function;

        var ids = new List<string>();
        var edges = new Dictionary<string, List<(SourceLeaf Call, string Callee)>>(StringComparer.Ordinal);
        foreach (var function in package.Functions.Items)
        {
            if (edges.TryAdd(function.Id, CallsIn(ScopeTree.Walk(function.Body.Nodes))
                    .Where(callee.ContainsKey).Select(c => (c, callee[c].Id)).ToList()))
            {
                ids.Add(function.Id);
            }
        }

        var reach = ids.ToDictionary(id => id, id => Reachable(id, edges), StringComparer.Ordinal);
        var ringed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            if (ringed.Contains(id) || !reach[id].Contains(id))
                continue;
            var ring = ids.Where(g => g == id || (reach[id].Contains(g) && reach[g].Contains(id))).ToHashSet(StringComparer.Ordinal);
            ringed.UnionWith(ring);
            var chain = Shortest(id, ring, edges);
            var next = chain.Count > 1 ? chain[1] : id;
            var at = edges[id].First(e => e.Callee == next).Call.Path;
            refusals.Add(chain.Count == 1
                ? ResolveSentences.CallsItself(id, at)
                : ResolveSentences.CallsInARing(id, string.Join("', which calls '", chain.Skip(1)), at));
        }

        var depths = new Dictionary<SourceLeaf, int>(ReferenceEqualityComparer.Instance);
        if (ringed.Count > 0)
            return depths;

        var order = Topological(ids, edges);
        var reached = ids.ToDictionary(id => id, _ => -1, StringComparer.Ordinal);
        var outside = calls.Where(b => tree.FrameOf(b.Call).Function == null).ToList();
        foreach (var bound in outside)
        {
            depths[bound.Call] = 0;
            reached[bound.Function.Id] = Math.Max(reached[bound.Function.Id], 0);
        }

        foreach (var id in order)
        {
            var depth = Math.Max(0, reached[id]) + 1;
            foreach (var (call, next) in edges[id])
            {
                depths[call] = depth;
                reached[next] = Math.Max(reached[next], depth);
            }
        }

        var height = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in Enumerable.Reverse(order))
            height[id] = edges[id].Count == 0 ? 0 : edges[id].Max(e => 1 + height[e.Callee]);
        foreach (var bound in outside)
        {
            if (height[bound.Function.Id] > FormatBounds.Depth)
                refusals.Add(ResolveSentences.CallsTooDeep(bound.Call.Id, height[bound.Function.Id], bound.Call.Path));
        }

        return depths;
    }

    /// <summary>The functions <paramref name="start"/> reaches through one call or more (itself only through a ring).</summary>
    private static HashSet<string> Reachable(string start, Dictionary<string, List<(SourceLeaf Call, string Callee)>> edges)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            foreach (var (_, next) in edges[queue.Dequeue()])
            {
                if (seen.Add(next))
                    queue.Enqueue(next);
            }
        }

        return seen;
    }

    /// <summary>The shortest ring from <paramref name="start"/> back to it inside <paramref name="ring"/>, callees in ordinal order: <c>[start, …, last]</c>, <c>last</c> calling <c>start</c>.</summary>
    private static List<string> Shortest(string start, HashSet<string> ring, Dictionary<string, List<(SourceLeaf Call, string Callee)>> edges)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var next in edges[at].Select(e => e.Callee).Where(ring.Contains).Distinct().Order(StringComparer.Ordinal))
            {
                if (next == start)
                {
                    var chain = new List<string>();
                    for (var x = at; x != start; x = parent[x])
                        chain.Insert(0, x);
                    chain.Insert(0, start);
                    return chain;
                }

                if (parent.TryAdd(next, at))
                    queue.Enqueue(next);
            }
        }

        return [start];
    }

    /// <summary>The functions of a call graph with no ring, each before every function it calls; ties in listed order.</summary>
    private static List<string> Topological(List<string> ids, Dictionary<string, List<(SourceLeaf Call, string Callee)>> edges)
    {
        var incoming = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var id in ids)
            foreach (var next in edges[id].Select(e => e.Callee).Distinct())
                incoming[next]++;

        var order = new List<string>();
        var ready = new Queue<string>(ids.Where(id => incoming[id] == 0));
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            order.Add(id);
            foreach (var next in edges[id].Select(e => e.Callee).Distinct())
            {
                if (--incoming[next] == 0)
                    ready.Enqueue(next);
            }
        }

        return order;
    }

    // ---- The profile ---------------------------------------------------------------------------

    /// <summary>
    /// S04-069: a parameter bound by any call to <c>profile.&lt;field&gt;</c>, or to a parameter that carries one,
    /// carries the profile (part 04 § 7.7, part 06 R-F3); a condition inside its function that reads it (a leaf's
    /// <c>if</c>, an <c>if</c>'s rule, a <c>while</c>'s and an <c>until</c>, a <c>case</c>'s subject) is said once per
    /// node and parameter, and so is an agent's part whose <c>if</c> reads it (<c>a part</c>). A prompt's reads are the
    /// seam's (<see cref="SourceDerived"/>).
    /// </summary>
    private static void Profile(ScopeTree tree, SourcePackage package, List<Bound> calls, List<SourceRefusal> refusals)
    {
        var carries = Carried(tree, calls.Select(b => (b.Call, b.Function)));
        if (carries.Count == 0)
            return;
        foreach (var function in package.Functions.Items)
        {
            foreach (var node in ScopeTree.Walk(function.Body.Nodes))
            {
                foreach (var name in Conditions(node).Select(p => p.Split('.')).Where(s => s is ["param", _, ..]).Select(s => s[1]).Distinct(StringComparer.Ordinal))
                {
                    if (carries.TryGetValue((function.Id, name), out var field))
                        refusals.Add(ResolveSentences.ReadsProfileParameter(function.Id, name, field, "a condition", HeaderFile(node)));
                }

                foreach (var name in PartConditions(node).Select(p => p.Split('.')).Where(s => s is ["param", _, ..]).Select(s => s[1]).Distinct(StringComparer.Ordinal))
                {
                    if (carries.TryGetValue((function.Id, name), out var field))
                        refusals.Add(ResolveSentences.ReadsProfileParameter(function.Id, name, field, "a part", HeaderFile(node)));
                }
            }
        }
    }

    /// <summary>
    /// The parameters that carry the profile (part 04 § 7.7): each one a call binds to <c>profile.&lt;field&gt;</c>, or
    /// to a parameter that carries one, with the field; a fixed point over <paramref name="calls"/> (each call and the
    /// function it runs, in source order).
    /// </summary>
    internal static Dictionary<(string Function, string Param), string> Carried(ScopeTree tree, IEnumerable<(SourceLeaf Call, SourceFunction Function)> calls)
    {
        var bound = calls.ToList();
        var carries = new Dictionary<(string Function, string Param), string>();
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (call, function) in bound)
            {
                var caller = tree.FrameOf(call).Function;
                var with = call.Header["with"] as JsonObject;
                foreach (var parameter in function.Inputs)
                {
                    if (carries.ContainsKey((function.Id, parameter.Id)))
                        continue;
                    var field = ReferenceString.Parse(SourceLayout.Text(with?[parameter.Id]))?.Segments switch
                    {
                        ["profile", var f, ..] when caller == null && ProfileFields.ContainsKey(f) => f,
                        ["param", var p, ..] when caller != null && carries.TryGetValue((caller.Id, p), out var carried) => carried,
                        _ => null,
                    };
                    if (field != null)
                    {
                        carries[(function.Id, parameter.Id)] = field;
                        changed = true;
                    }
                }
            }
        }

        return carries;
    }

    /// <summary>What an agent's parts' <c>if</c>s read, in written order.</summary>
    private static IEnumerable<string> PartConditions(SourceNode node) =>
        node is SourceLeaf { Kind: "agent" } leaf && leaf.Header["parts"] is JsonObject parts
            ? parts.SelectMany(p => JsonLogicShape.Paths((p.Value as JsonObject)?["if"]))
            : [];

    /// <summary>What <paramref name="node"/>'s conditions read, in written order: a leaf's <c>if</c>, an <c>if</c>'s rule, a loop's <c>until</c>, a while's rules, a case's subject.</summary>
    private static IEnumerable<string> Conditions(SourceNode node) => node switch
    {
        SourceLeaf leaf => JsonLogicShape.Paths(leaf.Header["if"]),
        SourceIf branch => JsonLogicShape.Paths(branch.If),
        SourceFor loop => JsonLogicShape.Paths(loop.Until),
        SourceWhile loop => JsonLogicShape.Paths(loop.While).Concat(JsonLogicShape.Paths(loop.Until)),
        SourceCase choice => [choice.Case],
        _ => [],
    };
}
