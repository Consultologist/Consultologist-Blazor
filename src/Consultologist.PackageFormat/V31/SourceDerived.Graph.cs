using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): one read of an entry. <paramref name="Path"/> is as written; <paramref name="Id"/> the
/// graph vertex it reads (null when it reads none: an input, a run fact, an unknown name); <paramref name="Key"/> the
/// rooted key a reader is told (local to a function inside one; null for <c>item</c>, <c>run</c> and the reads of an
/// earlier pass); <paramref name="Canonical"/> its D4 form, empty for what is no read of the entry's (a <c>ret</c>, a
/// call's function, a folder's items, a renderer piped into, a macro placed); <paramref name="Pass"/> whether it reads through <c>pass</c>
/// (no circle), <paramref name="Earlier"/> whether it reads an earlier pass (<c>pass.previous…</c>, <c>pass.history</c>:
/// never content). <paramref name="Carries"/> is false for a read of what is no value's content (a node's
/// <c>.state</c>, <c>.stopped</c>, <c>.passes</c>, named as <c>node.&lt;id&gt;</c>, through <c>pass</c> or through
/// <c>item.nodes</c>; an item's <c>.id</c> or <c>.state</c>): it carries no flag.
/// </summary>
internal sealed record GraphRead(string Path, string? Id, string? Key, string Canonical, bool Pass, bool Earlier, bool Carries = true);

/// <summary>
/// #1502 (specVersion 31, area 05): one vertex of the derived graph: an entry (a node, a check, a deliverable, a
/// renderer, a snippet or macro and their folders), or what an entry reads that is no entry (a function's parameter, a
/// function's result as its calls see it, a loop's item, a profile field). Inside a function each vertex is there twice:
/// <i>intrinsic</i>, its parameters reading nothing (what a call's result takes from the function itself), and
/// <i>union</i>, each parameter reading its arguments across every call (what the function's own entries are).
/// </summary>
internal sealed class Vertex(string id, string key, SourceFunction? function, bool union, object? declaration, bool entry)
{
    public string Id { get; } = id;

    public string Key { get; } = key;

    public SourceFunction? Function { get; } = function;

    public bool Union { get; } = union;

    public object? Declaration { get; } = declaration;

    public bool Entry { get; } = entry;

    public List<GraphRead> Content { get; } = [];

    public List<GraphRead> Conditions { get; } = [];

    /// <summary>Reads that count among <c>reads</c> but are neither content nor a condition (an earlier pass's read in a body).</summary>
    public List<GraphRead> Other { get; } = [];

    /// <summary>The body nodes of a flow node (for circles: a flow node waits for its body).</summary>
    public List<string> Contains { get; } = [];

    public TemplateReads? Body { get; set; }

    public bool BodyKnown { get; set; } = true;

    public bool StartsModel { get; set; }

    public bool StartsProfile { get; set; }
}

/// <summary>
/// #1502 (specVersion 31, area 05): the derived graph of a package (<see cref="SourceDerived"/>): every vertex, each with
/// its content reads (part 03 § 4.4 "How far it spreads": a computed node's expression, a body's reads, a call's
/// arguments and its function's result, a flow node's <c>ret</c>, a loop's <c>in</c>; never an earlier pass) and its
/// condition reads, each resolved where it is read (<see cref="ScopeTree.Resolve"/>). Built once; never throws on
/// package content.
/// </summary>
internal sealed class DerivedGraph
{
    private readonly SourcePackage package;
    private readonly Resolution resolution;
    private readonly ITemplateReads? templates;
    private readonly Dictionary<object, TemplateReads?> bodies = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, Dictionary<string, Vertex>> byDeclaration = new(ReferenceEqualityComparer.Instance);

    private DerivedGraph(SourcePackage package, Resolution resolution, ITemplateReads? templates)
    {
        this.package = package;
        this.resolution = resolution;
        this.templates = templates;
    }

    private ScopeTree Tree => resolution.Tree;

    /// <summary>Every vertex, in the order built: <c>nodes/</c>, <c>checks/</c>, each function (intrinsic, then union), the definitions, the deliverables, then what was read.</summary>
    public List<Vertex> Vertices { get; } = [];

    public Dictionary<string, Vertex> ById { get; } = new(StringComparer.Ordinal);

    /// <summary>The vertex of <paramref name="declaration"/> as its entry is told: the package's, or a function's union one.</summary>
    public Vertex? Of(object declaration, SourceFunction? function) => Of(declaration, function, union: true);

    /// <summary>The vertex of <paramref name="declaration"/> in the scope of <paramref name="function"/>, intrinsic or union.</summary>
    public Vertex? Of(object declaration, SourceFunction? function, bool union) =>
        byDeclaration.TryGetValue(declaration, out var scopes) ? scopes.GetValueOrDefault(Prefix(function, union)) : null;

    /// <summary>The graph prefix of a scope: none at the package, <c>function:&lt;f&gt;:i:</c> or <c>…:u:</c> inside a function.</summary>
    public static string Prefix(SourceFunction? function, bool union) =>
        function == null ? "" : $"function:{function.Id}:{(union ? "u" : "i")}:";

    /// <summary>
    /// The body <paramref name="vertex"/>'s template is read in: a node's own, the top of <c>nodes/</c> for a deliverable
    /// or a package definition, a function's root for its own definitions; null for a pseudo-vertex.
    /// </summary>
    public ScopeFrame? FrameOfBody(Vertex vertex) => vertex.Declaration switch
    {
        SourceNode node => Tree.FrameOf(node),
        SourceDeliverable => Tree.PackageRoot,
        SourceText when vertex.Function != null => Tree.FunctionRoot(vertex.Function),
        SourceText => Tree.PackageRoot,
        _ => null,
    };

    /// <summary>
    /// The declarations whose <c>if</c> a body's read <paramref name="path"/> of <paramref name="root"/> (<c>macro</c> or
    /// <c>snippet</c>) places: <c>macro.&lt;id&gt;</c> or <c>snippet.&lt;id&gt;</c> (the one file), or
    /// <c>macro.&lt;folder&gt;</c> with, for <c>macro.&lt;folder&gt;.&lt;item&gt;</c>, that item; the function's own first,
    /// then the package's.
    /// </summary>
    public IEnumerable<object> PlacedBy(string path, string root, SourceFunction? function)
    {
        if (ReferenceString.Parse(path) is not { } reference || reference.Segments.Count < 2 || reference.Segments[0] != root)
            yield break;
        var s = reference.Segments;
        IReadOnlyList<SourceTextEntry> Of(SourceDefinitions d) => root == "macro" ? d.Macros.Items : d.Snippets.Items;
        var entry = (function == null ? null : Of(function.Definitions).FirstOrDefault(e => e.Id == s[1]))
            ?? Of(package.Definitions).FirstOrDefault(e => e.Id == s[1]);
        switch (entry)
        {
            case SourceSingleText single:
                yield return single.Text;
                break;
            case SourceTextFolder folder:
                yield return folder;
                if (s.Count > 2 && folder.Items.FirstOrDefault(i => i.Id == s[2]) is { } item)
                    yield return item;
                break;
        }
    }

    /// <summary>What <paramref name="declaration"/>'s body reads: none for an empty body, the seam's otherwise (null: not known); asked once.</summary>
    public TemplateReads? BodyOf(object declaration, string body, SourceFunction? function)
    {
        if (string.IsNullOrWhiteSpace(body))
            return TemplateReads.None;
        if (!bodies.TryGetValue(declaration, out var reads))
        {
            reads = templates?.Of(declaration, function);
            bodies[declaration] = reads;
        }

        return reads;
    }

    public static DerivedGraph Build(SourcePackage package, Resolution resolution, ITemplateReads? templates)
    {
        var graph = new DerivedGraph(package, resolution, templates);
        graph.AddAll();
        graph.FillAll();
        return graph;
    }

    // ---- The vertices --------------------------------------------------------------------------

    private void AddAll()
    {
        foreach (var node in Tree.All().Where(n => Tree.FrameOf(n).Function == null))
            AddNode(node, null, false);
        foreach (var function in package.Functions.Items)
        {
            foreach (var union in new[] { false, true })
            {
                foreach (var node in ScopeTree.Walk(function.Body.Nodes))
                    AddNode(node, function, union);
                foreach (var parameter in function.Inputs)
                    Add(new Vertex(Prefix(function, union) + $"param.{parameter.Id}", $"param.{parameter.Id}", function, union, null, false));
                AddDefinitions(function.Definitions, function, union);
            }

            var result = Add(new Vertex($"function.{function.Id}", $"function.{function.Id}", null, false, null, false));
            if (function.Body.Ret is { } ret)
                result.Content.Add(new GraphRead(ret, Prefix(function, false) + ret, ret, "", false, false));
        }

        AddDefinitions(package.Definitions, null, false);
        foreach (var deliverable in Deliverables(package.Deliverables))
            Add(new Vertex(deliverable.Key, deliverable.Key, null, false, deliverable, true), deliverable);
    }

    private Vertex Add(Vertex vertex, object? declaration = null)
    {
        if (ById.TryAdd(vertex.Id, vertex))
        {
            Vertices.Add(vertex);
            if (declaration != null)
            {
                if (!byDeclaration.TryGetValue(declaration, out var scopes))
                    byDeclaration[declaration] = scopes = new Dictionary<string, Vertex>(StringComparer.Ordinal);
                scopes.TryAdd(Prefix(vertex.Function, vertex.Union), vertex);
            }
        }

        return ById[vertex.Id];
    }

    private void AddNode(SourceNode node, SourceFunction? function, bool union)
    {
        var vertex = Add(new Vertex(Prefix(function, union) + node.Key, node.Key, function, union, node, true), node);
        vertex.StartsModel = node is SourceLeaf { Kind: "agent" };
        if (node is SourceFor loop)
            Add(new Vertex(ItemId(function, union, loop), $"item:{loop.Key}", function, union, null, false));
    }

    private static string ItemId(SourceFunction? function, bool union, SourceFor loop) => $"item:{Prefix(function, union)}{loop.Key}";

    private void AddDefinitions(SourceDefinitions definitions, SourceFunction? function, bool union)
    {
        var prefix = Prefix(function, union);
        foreach (var renderer in definitions.Renderers.Items)
            Add(new Vertex(prefix + $"renderer.{renderer.Id}", $"renderer.{renderer.Id}", function, union, renderer, true), renderer);
        foreach (var (root, entries) in new[] { ("snippet", definitions.Snippets.Items), ("macro", definitions.Macros.Items) })
        {
            foreach (var entry in entries)
            {
                switch (entry)
                {
                    case SourceSingleText single:
                        Add(new Vertex(prefix + $"{root}.{single.Id}", $"{root}.{single.Id}", function, union, single.Text, true), single.Text);
                        break;
                    case SourceTextFolder folder:
                        Add(new Vertex(prefix + $"{root}.{folder.Id}", $"{root}.{folder.Id}", function, union, folder, true), folder);
                        foreach (var item in folder.Items)
                        {
                            var key = $"{root}.{folder.Id}.{item.Id}";
                            Add(new Vertex(prefix + key, key, function, union, item, true), item);
                        }

                        break;
                }
            }
        }
    }

    private static IEnumerable<SourceDeliverable> Deliverables(SourceDeliveryFolder folder) =>
        folder.Deliverables.Concat(folder.Folders.SelectMany(Deliverables));

    // ---- The reads -----------------------------------------------------------------------------

    private void FillAll()
    {
        foreach (var vertex in Vertices.ToList())
        {
            switch (vertex.Declaration)
            {
                case SourceNode node:
                    FillNode(vertex, node);
                    break;
                case SourceDeliverable deliverable:
                    Conditions(vertex, JsonLogicShape.Paths(deliverable.Header["if"]), Tree.PackageRoot, null);
                    FillBody(vertex, deliverable, deliverable.Body, Tree.PackageRoot, null);
                    break;
                case SourceText text:
                    var frame = vertex.Function == null ? Tree.PackageRoot : Tree.FunctionRoot(vertex.Function);
                    Conditions(vertex, JsonLogicShape.Paths(text.Header["if"]), null, null);
                    FillBody(vertex, text, text.Body, frame, null);
                    break;
                case SourceTextFolder folder:
                    Conditions(vertex, JsonLogicShape.Paths(folder.Index["if"]), null, null);
                    foreach (var item in folder.Items)
                    {
                        var key = $"{vertex.Key}.{item.Id}";
                        vertex.Content.Add(new GraphRead(key, Prefix(vertex.Function, vertex.Union) + key, key, "", false, false));
                    }

                    break;
            }
        }

        foreach (var function in package.Functions.Items)
            Arguments(function);
    }

    private void FillNode(Vertex vertex, SourceNode node)
    {
        var frame = Tree.FrameOf(node);
        var prefix = Prefix(vertex.Function, vertex.Union);
        switch (node)
        {
            case SourceLeaf leaf:
                Conditions(vertex, JsonLogicShape.Paths(leaf.Header["if"]), frame, leaf);
                if (leaf.Kind == "agent" && leaf.Header["parts"] is JsonObject parts)
                {
                    foreach (var (_, part) in parts)
                        Conditions(vertex, JsonLogicShape.Paths((part as JsonObject)?["if"]), frame, leaf);
                }

                switch (leaf.Kind)
                {
                    case "computed":
                        foreach (var path in JsonLogicShape.Paths(leaf.Header["expression"]))
                            Content(vertex, Map(path, frame, vertex, leaf));
                        break;
                    case "call":
                        if (leaf.Header["with"] is JsonObject with)
                        {
                            foreach (var (_, value) in with)
                            {
                                if (SourceLayout.Text(value) is { } text)
                                    Content(vertex, Map(text, frame, vertex, leaf));
                            }
                        }

                        if (resolution.Calls.TryGetValue(leaf, out var binding))
                        {
                            var result = $"function.{binding.Function.Id}";
                            vertex.Content.Add(new GraphRead(result, result, result, "", false, false));
                        }

                        break;
                    default:
                        FillBody(vertex, leaf, leaf.Body, frame, leaf);
                        break;
                }

                break;
            case SourceFor loop:
                var source = Map(loop.In, frame, vertex, loop);
                Content(vertex, source);
                ById[ItemId(vertex.Function, vertex.Union, loop)].Content.Add(source);
                Returns(vertex, prefix, [loop.Body]);
                Conditions(vertex, JsonLogicShape.Paths(loop.Until), Tree.BodyOf(loop), loop);
                break;
            case SourceWhile loop:
                Returns(vertex, prefix, [loop.Body]);
                Conditions(vertex, JsonLogicShape.Paths(loop.While).Concat(JsonLogicShape.Paths(loop.Until)), Tree.BodyOf(loop), loop);
                break;
            case SourceIf branch:
                Returns(vertex, prefix, ScopeTree.Blocks(branch));
                Conditions(vertex, JsonLogicShape.Paths(branch.If), frame, branch);
                break;
            case SourceCase choice:
                Returns(vertex, prefix, ScopeTree.Blocks(choice));
                Conditions(vertex, [choice.Case], frame, choice);
                break;
        }
    }

    /// <summary>A flow node's content: each body's <c>ret</c>; and, for circles, every node of its bodies.</summary>
    private static void Returns(Vertex vertex, string prefix, IEnumerable<SourceBlock> blocks)
    {
        foreach (var block in blocks)
        {
            if (block.Ret is { } ret)
                vertex.Content.Add(new GraphRead(ret, prefix + ret, ret, "", false, false));
            vertex.Contains.AddRange(block.Nodes.Select(n => prefix + n.Key));
        }
    }

    /// <summary>A body's reads (the seam's), the renderers it pipes into and the macros it places, where it is.</summary>
    private void FillBody(Vertex vertex, object declaration, string body, ScopeFrame frame, SourceNode? reader)
    {
        var reads = BodyOf(declaration, body, vertex.Function);
        vertex.Body = reads;
        vertex.BodyKnown = reads != null;
        if (reads == null)
            return;
        vertex.StartsProfile |= reads.PlacesProfileMacro;
        foreach (var path in reads.Reads)
            Content(vertex, Map(path, frame, vertex, reader));
        foreach (var render in reads.Renders)
        {
            Content(vertex, Map(render.Source, frame, vertex, reader));
            vertex.Content.Add(Definition($"renderer.{render.Renderer}", vertex) with { Canonical = "" });
        }

        foreach (var place in reads.Places)
            vertex.Content.Add(Definition(place, vertex) with { Canonical = "" });
    }

    /// <summary>A content read, or, when it reads an earlier pass, one that only counts among the entry's reads.</summary>
    private static void Content(Vertex vertex, GraphRead read) => (read.Earlier ? vertex.Other : vertex.Content).Add(read);

    private void Conditions(Vertex vertex, IEnumerable<string> paths, ScopeFrame? frame, SourceNode? reader)
    {
        foreach (var path in paths)
            vertex.Conditions.Add(Map(path, frame, vertex, reader));
    }

    /// <summary>Union: each parameter of <paramref name="function"/> reads its argument of every call, where the call is.</summary>
    private void Arguments(SourceFunction function)
    {
        foreach (var call in Tree.All().OfType<SourceLeaf>().Where(c => c.Kind == "call"))
        {
            if (!resolution.Calls.TryGetValue(call, out var binding) || !ReferenceEquals(binding.Function, function)
                || call.Header["with"] is not JsonObject with)
            {
                continue;
            }

            var caller = Tree.FrameOf(call).Function;
            var at = Of(call, caller, union: true) ?? Of(call, caller, union: false);
            if (at == null)
                continue;
            foreach (var parameter in function.Inputs)
            {
                if (SourceLayout.Text(with[parameter.Id]) is { } text
                    && ById.TryGetValue(Prefix(function, true) + $"param.{parameter.Id}", out var param))
                {
                    var read = Map(text, Tree.FrameOf(call), at, call);
                    param.Content.Add(read);
                    if (read.Id != null && read.Id.StartsWith("profile.", StringComparison.Ordinal))
                        param.StartsProfile = true;
                }
            }
        }
    }

    // ---- One read ------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="path"/> read at <paramref name="frame"/> (null for a macro's or snippet's <c>if</c>, read where it is
    /// placed) by <paramref name="vertex"/>: the vertex it reads, its key and its D4 form.
    /// </summary>
    public GraphRead Map(string path, ScopeFrame? frame, Vertex vertex, SourceNode? reader)
    {
        var plain = new GraphRead(path, null, null, path, false, false);
        if (ReferenceString.Parse(path) is not { } reference)
            return plain;
        var s = reference.Segments;
        var prefix = Prefix(vertex.Function, vertex.Union);
        switch (s[0])
        {
            case "node" or "check" or "input" or "param" when frame != null:
                var resolved = Tree.Resolve(reference, frame, "", "", condition: true, reader);
                if (resolved.Key is not { } key)
                    return plain;
                var member = s[0] is "node" or "check" && s.Count > 2 ? s[2] : null;
                return new GraphRead(path, s[0] == "input" ? key : prefix + key, key, s.Count > 2 ? $"{key}.{s[2]}" : key, false, false,
                    Carries: !RunFact(member));
            case "pass" when frame != null:
                return Pass(path, s, frame, prefix);
            case "pass":
                return plain with { Pass = true, Earlier = s.Contains("previous") || s.Contains("history") };
            case "item" when frame != null:
                var up = 0;
                while (1 + up < s.Count && s[1 + up] == "parent")
                    up++;
                var itemMember = 1 + up < s.Count ? s[1 + up] : null;
                if (frame.Loop(up) is not { } loop)
                    return plain;
                // Over a loop's .items, item.nodes.<id>.<member> is a body node's member, a fact of the run as node.<id>'s is.
                var nodeMember = itemMember == "nodes" && 3 + up < s.Count && Tree.Source(loop).OverItems ? s[3 + up] : null;
                return plain with
                {
                    Id = ItemId(vertex.Function, vertex.Union, loop),
                    Carries = itemMember is not ("id" or "state") && !RunFact(nodeMember),
                };
            case "profile" when s.Count >= 2:
                var field = $"profile.{s[1]}";
                var profile = Add(new Vertex(field, field, null, false, null, false));
                profile.StartsProfile = true;
                return plain with { Id = field, Key = field };
            case "snippet" or "macro" or "renderer":
                return Definition(path, vertex);
            default:
                return plain;
        }
    }

    /// <summary>
    /// D33: whether <paramref name="member"/>, read after a node's id (<c>node.&lt;id&gt;</c>, <c>pass…node.&lt;id&gt;</c>,
    /// <c>item.nodes.&lt;id&gt;</c>), is the engine's fact about the run rather than a value's content: <c>.state</c>,
    /// <c>.stopped</c>, <c>.passes</c>. Such a read carries no flag, whichever form names the node.
    /// </summary>
    private static bool RunFact(string? member) => member is "state" or "stopped" or "passes";

    /// <summary>
    /// <c>pass.(parent)*.(previous.)?node.&lt;id&gt;</c>: the node, an earlier pass's marked <c>Earlier</c> (and told no
    /// key); <c>pass.history</c>: an earlier pass, naming nothing; a pass's own member: nothing.
    /// </summary>
    private static GraphRead Pass(string path, IReadOnlyList<string> s, ScopeFrame frame, string prefix)
    {
        var at = 1;
        var up = 0;
        while (at < s.Count && s[at] == "parent")
        {
            at++;
            up++;
        }

        var plain = new GraphRead(path, null, null, path, true, false);
        if (at < s.Count && s[at] == "history")
            return plain with { Earlier = true };
        var earlier = at < s.Count && s[at] == "previous";
        if (earlier)
            at++;
        if (at + 1 < s.Count && s[at] == "node" && frame.While(up) is { } loop
            && ScopeTree.Walk(loop.Body.Nodes).FirstOrDefault(n => n.Id == s[at + 1]) is { } node)
        {
            // An earlier pass's node is named (reproducible and the decisions follow it), but is never content.
            var member = at + 2 < s.Count ? s[at + 2] : null;
            return plain with { Id = prefix + node.Key, Key = earlier ? null : node.Key, Earlier = earlier, Carries = !RunFact(member) };
        }

        return plain with { Earlier = earlier };
    }

    /// <summary>A read of a renderer, snippet or macro: the longest key it starts with, the function's own first, then the package's.</summary>
    private GraphRead Definition(string path, Vertex vertex)
    {
        var plain = new GraphRead(path, null, null, path, false, false);
        if (ReferenceString.Parse(path) is not { } reference)
            return plain;
        var s = reference.Segments;
        foreach (var length in new[] { 3, 2 })
        {
            if (s.Count < length)
                continue;
            var key = string.Join('.', s.Take(length));
            if (vertex.Function != null && ById.ContainsKey(Prefix(vertex.Function, vertex.Union) + key))
                return plain with { Id = Prefix(vertex.Function, vertex.Union) + key, Key = key };
            if (ById.ContainsKey(key) && ById[key].Function == null && ById[key].Entry)
                return plain with { Id = key, Key = key };
        }

        return plain;
    }
}
