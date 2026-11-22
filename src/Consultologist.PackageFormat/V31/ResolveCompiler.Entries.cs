using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the inputs (part 07 § 2.3) and the flat maps (§ 2.5; D6) of <c>resolve.json</c>.
/// Each entry is its header as written, a clone, canonicalised: every <c>format</c> (and a renderer's <c>input</c>)
/// rewritten into the bundle (<see cref="CompiledFormats.Rewrite"/>, in the scope that owns it); every <c>var</c> and
/// <c>missing</c> path of a condition or expression, and every reference string (<c>in</c>, <c>case</c>, <c>with</c>),
/// starting <c>node.&lt;id&gt;</c> or <c>check.&lt;id&gt;</c> written with the key it resolves to where it is read
/// (D3; a macro's or snippet's <c>if</c> is read where it is placed, so it stays as written); a flow node's bodies as
/// keys. Then the derived members: <c>id</c>, <c>parent</c>, <c>reads</c> and the three flags
/// (<see cref="PackageDerived"/>'s); on every node <c>body</c> and <c>bodyHash</c> (null for the kinds with no body),
/// <c>renders</c>, <c>runSpots</c>, <c>formatHash</c> and <c>typeHash</c>; on an agent <c>parts</c> and
/// <c>places</c>; on a flow node <c>nodes</c> (every child, in order) and <c>ret</c>: the returned key of a <c>for</c>
/// and a <c>while</c>, <c>null</c> on an <c>if</c> and a <c>case</c>, whose <c>ret</c>s are their branches' (inside
/// <c>then</c>, <c>else</c> and <c>branches</c>), every member being present (§ 1). A body the seam did not say has no places,
/// renders or run spots (D1). #1503 (specVersion 31, area 06; D3): and on every node (a function's too) and every
/// deliverable, <c>plainConverts</c> (<see cref="Entries"/>' <c>PlainConverts</c>). #1504 (specVersion 31, area 07): on
/// every agent, <c>rendered</c> node and deliverable, <c>placements</c> (D2) and <c>names</c> (D1); each run spot's
/// <c>unit</c> and <c>neutral</c> (D4); and in <c>renderers</c> each built-in renderer the package uses (D6).
/// </summary>
public static partial class ResolveCompiler
{
    /// <summary>As deep as a rule is walked for its paths (<see cref="JsonLogicShape.MaxDepth"/>; the checker refused deeper).</summary>
    private const int RuleDepth = JsonLogicShape.MaxDepth;

    /// <summary>The rule members of an entry <c>plainConverts</c> covers (D3), in this order.</summary>
    private static readonly string[] RuleMembers = ["if", "expression", "while", "until"];

    /// <summary>The six flat maps of the package, as <see cref="Entries.Maps"/> writes them.</summary>
    private sealed record FlatMaps(JsonObject Nodes, JsonObject Checks, JsonObject Renderers, JsonObject Snippets, JsonObject Macros, JsonObject Deliverables);

    /// <summary>The definition maps of the package or of one function.</summary>
    private sealed record DefinitionMaps(JsonObject Renderers, JsonObject Snippets, JsonObject Macros);

    /// <summary>The entries of one compilation.</summary>
    private sealed class Entries(Context context)
    {
        private ScopeTree Tree => context.Tree;

        /// <summary>The manifest's inputs keyed by id, each as written with its format rewritten (§ 2.3).</summary>
        public JsonObject Inputs()
        {
            var inputs = new JsonObject();
            foreach (var input in context.Package.Manifest.Inputs)
            {
                var entry = input.Json.DeepClone().AsObject();
                if (entry.ContainsKey("format"))
                    entry["format"] = CompiledFormats.Rewrite(input.Json["format"], context.Types.Formats, null);
                inputs[input.Id] = entry;
            }

            return inputs;
        }

        /// <summary><c>nodes</c>, <c>checks</c>, <c>renderers</c>, <c>snippets</c>, <c>macros</c> and <c>deliverables</c>, each in source order.</summary>
        public FlatMaps Maps()
        {
            var nodes = new JsonObject();
            Body(context.Package.Nodes.Nodes, null, null, nodes);
            var checks = new JsonObject();
            Body(context.Package.Checks.Nodes, null, null, checks);
            var definitions = Definitions(context.Package.Definitions, null);
            var deliverables = new JsonObject();
            foreach (var folder in DeliveryFolders(context.Package.Deliverables))
            {
                foreach (var deliverable in folder.Deliverables)
                    deliverables[deliverable.Key] = Deliverable(deliverable, folder);
            }

            return new FlatMaps(nodes, checks, definitions.Renderers, definitions.Snippets, definitions.Macros, deliverables);
        }

        /// <summary>
        /// Each function by id (D6, as amended): its index's members as written (<c>inputs</c> and <c>format</c>
        /// rewritten in the function's scope), <c>inputOrder</c>, <c>ret</c>, <c>body</c> (its top-level keys),
        /// <c>nodes</c> (its own flat map, keys local to it), its own <c>renderers</c>, <c>snippets</c> and
        /// <c>macros</c>, the derived members part 07 § 2.5 puts on every entry (<see cref="Function"/>),
        /// <c>formatHash</c> and <c>typeHash</c>.
        /// </summary>
        public JsonObject Functions()
        {
            var functions = new JsonObject();
            foreach (var function in context.Package.Functions.Items)
            {
                var scope = context.Scope(function);
                var entry = function.Index.DeepClone().AsObject();
                if (entry["inputs"] is JsonObject parameters)
                {
                    foreach (var (id, parameter) in parameters.ToList())
                    {
                        if (parameter is JsonObject written && written.ContainsKey("format"))
                            written["format"] = CompiledFormats.Rewrite(function.Index["inputs"]![id]!["format"], scope, function);
                    }
                }

                if (entry.ContainsKey("format"))
                    entry["format"] = CompiledFormats.Rewrite(function.Index["format"], scope, function);
                entry["inputOrder"] = Strings(function.Inputs.Select(p => p.Id));
                entry["ret"] = function.Body.Ret;
                entry["body"] = Strings(function.Body.Nodes.Select(n => n.Key));
                var nodes = new JsonObject();
                Body(function.Body.Nodes, function, null, nodes);
                entry["nodes"] = nodes;
                var definitions = Definitions(function.Definitions, function);
                entry["renderers"] = definitions.Renderers;
                entry["snippets"] = definitions.Snippets;
                entry["macros"] = definitions.Macros;
                Function(entry, function);
                Hashes(entry, function.Index["format"], scope, function);
                functions[function.Id] = entry;
            }

            return functions;
        }

        /// <summary>
        /// The derived members of every entry (part 07 § 2.5) on a function's entry: <c>id</c> (the function's id),
        /// <c>parent</c> (null), <c>reads</c> (what its own entries read from outside its nodes: its <c>param.*</c>
        /// reads and its definitions', distinct and sorted) and the three flags of its <c>ret</c> with its parameters
        /// reading nothing (<see cref="PackageDerived.Functions"/>; derived only, as for every entry that is no node).
        /// </summary>
        private void Function(JsonObject entry, SourceFunction function)
        {
            var reads = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var map in new[] { "nodes", "renderers", "snippets", "macros" })
            {
                foreach (var (_, own) in (JsonObject)entry[map]!)
                {
                    foreach (var read in (own?["reads"] as JsonArray ?? []).Select(SourceLayout.Text))
                    {
                        if (read != null && Outside(read))
                            reads.Add(read);
                    }
                }
            }

            var result = context.Derived.Functions.GetValueOrDefault(function.Id);
            entry["id"] = function.Id;
            entry["parent"] = null;
            entry["reads"] = Strings(reads);
            entry["runDependent"] = result?.RunDependent ?? false;
            entry["profileCarrying"] = result?.ProfileCarrying ?? false;
            entry["modelDerived"] = result?.ModelDerived ?? false;
        }

        /// <summary>Whether a read inside a function reads past its own nodes and loops: a parameter or a definition.</summary>
        private static bool Outside(string read) =>
            read.StartsWith("param.", StringComparison.Ordinal) || read.StartsWith("snippet.", StringComparison.Ordinal)
            || read.StartsWith("macro.", StringComparison.Ordinal) || read.StartsWith("renderer.", StringComparison.Ordinal);

        // ---- Nodes -----------------------------------------------------------------------------

        /// <summary>Each node of <paramref name="nodes"/> into <paramref name="map"/>, a flow node before its bodies' nodes.</summary>
        private void Body(IReadOnlyList<SourceNode> nodes, SourceFunction? function, string? parent, JsonObject map)
        {
            foreach (var node in nodes)
            {
                map[node.Key] = Node(node, function, parent);
                foreach (var block in ScopeTree.Blocks(node))
                    Body(block.Nodes, function, node.Key, map);
            }
        }

        private JsonObject Node(SourceNode node, SourceFunction? function, string? parent)
        {
            var frame = Tree.FrameOf(node);
            var scope = context.Scope(function);
            var entry = node.Header.DeepClone().AsObject();
            if (entry.ContainsKey("format"))
                entry["format"] = CompiledFormats.Rewrite(node.Header["format"], scope, function);

            switch (node)
            {
                case SourceLeaf leaf:
                    Leaf(leaf, frame, entry);
                    break;
                case SourceFor loop:
                    entry["in"] = Reference(loop.In, frame, loop);
                    if (entry.ContainsKey("until"))
                        entry["until"] = Logic(loop.Until, Tree.BodyOf(loop), loop);
                    entry["nodes"] = Keys(loop.Body);
                    entry["ret"] = loop.Body.Ret;
                    break;
                case SourceWhile loop:
                    entry["while"] = Logic(loop.While, Tree.BodyOf(loop), loop);
                    if (entry.ContainsKey("until"))
                        entry["until"] = Logic(loop.Until, Tree.BodyOf(loop), loop);
                    entry["nodes"] = Keys(loop.Body);
                    entry["ret"] = loop.Body.Ret;
                    break;
                case SourceIf branch:
                    entry["if"] = Logic(branch.If, frame, branch);
                    entry["then"] = Branch(branch.Then);
                    entry["else"] = branch.Else == null ? null : Branch(branch.Else);
                    entry["nodes"] = Strings(ScopeTree.Blocks(node).SelectMany(b => b.Nodes).Select(n => n.Key));
                    entry["ret"] = null;
                    break;
                case SourceCase choice:
                    entry["case"] = Reference(choice.Case, frame, choice);
                    entry["branches"] = new JsonArray(choice.Branches.Select(b =>
                    {
                        var written = Branch(b.Body);
                        written.Insert(0, "when", Strings(b.When));
                        return (JsonNode?)written;
                    }).ToArray());
                    entry["else"] = choice.Else == null ? null : Branch(choice.Else);
                    entry["nodes"] = Strings(ScopeTree.Blocks(node).SelectMany(b => b.Nodes).Select(n => n.Key));
                    entry["ret"] = null;
                    break;
            }

            var derived = context.Derive(node.Key, function);
            Derived(entry, node.Id, parent, derived);
            PlainConverts(entry, node);
            var body = node is SourceLeaf { Kind: "agent" or "rendered" } templated ? templated.Body : null;
            Body(entry, body, derived);
            if (node is SourceLeaf { Kind: "agent" })
                entry["places"] = Strings(derived?.Body?.Places ?? []);
            if (node is SourceLeaf { Kind: "agent" or "rendered" } leafTemplate)
                Templated(entry, leafTemplate, function, frame, leafTemplate);
            Hashes(entry, node.Header["format"], scope, function);
            return entry;
        }

        /// <summary>A leaf's <c>if</c>, a computed node's <c>expression</c>, a call's arguments and an agent's parts, at the leaf's body.</summary>
        private void Leaf(SourceLeaf leaf, ScopeFrame frame, JsonObject entry)
        {
            if (entry.ContainsKey("if"))
                entry["if"] = Logic(leaf.Header["if"], frame, leaf);
            switch (leaf.Kind)
            {
                case "computed":
                    if (entry.ContainsKey("expression"))
                        entry["expression"] = Logic(leaf.Header["expression"], frame, leaf);
                    break;
                case "call" when entry["with"] is JsonObject with:
                    foreach (var (parameter, value) in with.ToList())
                    {
                        if (SourceLayout.Text(value) is { } text)
                            with[parameter] = Reference(text, frame, leaf);
                    }

                    break;
                case "agent":
                    var parts = new JsonObject();
                    if (leaf.Header["parts"] is JsonObject written)
                    {
                        foreach (var (id, part) in written)
                        {
                            var copy = part?.DeepClone();
                            if (copy is JsonObject rewritten && rewritten.ContainsKey("if"))
                                rewritten["if"] = Logic(part!["if"], frame, leaf);
                            parts[id] = copy;
                        }
                    }

                    entry["parts"] = parts;
                    break;
            }
        }

        /// <summary>A body of an <c>if</c> or a <c>case</c> as written, with keys: <c>{ nodes, ret }</c>.</summary>
        private static JsonObject Branch(SourceBlock block) => new() { ["nodes"] = Keys(block), ["ret"] = block.Ret };

        private static JsonArray Keys(SourceBlock block) => Strings(block.Nodes.Select(n => n.Key));

        // ---- Definitions and deliverables ------------------------------------------------------

        /// <summary>The renderers, snippets (and folders, and their items) and macros (likewise) of the package or of <paramref name="function"/>.</summary>
        private DefinitionMaps Definitions(SourceDefinitions definitions, SourceFunction? function)
        {
            var scope = context.Scope(function);
            var renderers = new JsonObject();
            foreach (var renderer in definitions.Renderers.Items)
            {
                var key = $"renderer.{renderer.Id}";
                var entry = Text(renderer.Header, renderer.Id, key, function, renderer.Body);
                if (entry.ContainsKey("input"))
                    entry["input"] = CompiledFormats.Rewrite(renderer.Header["input"], scope, function);
                renderers[key] = entry;
            }

            // #1504 (D6; part 05 § 5.3): each built-in renderer the package uses, embedded as a package renderer is, after them.
            foreach (var builtin in function == null ? context.BuiltIns : [])
            {
                var key = $"renderer.{builtin.Id}";
                var entry = Text(builtin.Header, builtin.Id, key, null, builtin.Body);
                if (entry.ContainsKey("input"))
                    entry["input"] = CompiledFormats.Rewrite(builtin.Header["input"], scope, null);
                renderers[key] = entry;
            }

            var snippets = new JsonObject();
            var macros = new JsonObject();
            foreach (var (root, entries, map) in new[] { ("snippet", definitions.Snippets.Items, snippets), ("macro", definitions.Macros.Items, macros) })
            {
                foreach (var written in entries)
                {
                    switch (written)
                    {
                        case SourceSingleText { Text: var text }:
                            map[$"{root}.{text.Id}"] = Text(text.Header, text.Id, $"{root}.{text.Id}", function, text.Body);
                            break;
                        case SourceTextFolder folder:
                            var key = $"{root}.{folder.Id}";
                            map[key] = Text(folder.Index, folder.Id, key, function, null);
                            foreach (var item in folder.Items)
                                map[$"{key}.{item.Id}"] = Text(item.Header, item.Id, $"{key}.{item.Id}", function, item.Body);
                            break;
                    }
                }
            }

            return new DefinitionMaps(renderers, snippets, macros);
        }

        /// <summary>A renderer, snippet, macro, folder or folder item: its header as written, <c>format</c> rewritten, and the derived members.</summary>
        private JsonObject Text(JsonObject header, string id, string key, SourceFunction? function, string? body)
        {
            var scope = context.Scope(function);
            var entry = header.DeepClone().AsObject();
            if (entry.ContainsKey("format"))
                entry["format"] = CompiledFormats.Rewrite(header["format"], scope, function);
            var derived = context.Derive(key, function);
            Derived(entry, id, null, derived);
            Body(entry, body, derived);
            if (header.ContainsKey("format"))
                Hashes(entry, header["format"], scope, function);
            return entry;
        }

        /// <summary>
        /// A deliverable: its header as written (<c>format</c> rewritten, the <c>if</c>'s paths as keys), then
        /// <c>delivery</c> and <c>bundle</c> as the <c>index.json</c> of <paramref name="folder"/>, the folder that holds
        /// it, writes them (part 06 § 8: delivery is declared on folders only, with no inheritance; the engine loads only
        /// <c>resolve.json</c>, part 07 § 1), and the derived members, <c>escapes</c> among them (D7).
        /// </summary>
        private JsonObject Deliverable(SourceDeliverable deliverable, SourceDeliveryFolder folder)
        {
            var entry = deliverable.Header.DeepClone().AsObject();
            entry["delivery"] = folder.Index["delivery"]?.DeepClone() ?? new JsonArray();
            entry["bundle"] = folder.Index["bundle"]?.DeepClone();
            if (entry.ContainsKey("format"))
                entry["format"] = CompiledFormats.Rewrite(deliverable.Header["format"], context.Types.Formats, null);
            if (entry.ContainsKey("if"))
                entry["if"] = Logic(deliverable.Header["if"], Tree.PackageRoot, null);
            var derived = context.Derive(deliverable.Key, null);
            Derived(entry, deliverable.Id, null, derived);
            PlainConverts(entry, deliverable);
            Body(entry, deliverable.Body, derived);
            // #1505 (area 08; D7): the outputs composition escapes, in the shape of runSpots' unit and at.
            entry["escapes"] = new JsonArray((context.Check.Templates?.Escapes(deliverable.Key) ?? []).Select(e => (JsonNode?)new JsonObject
            {
                ["unit"] = e.Unit,
                ["at"] = e.At,
            }).ToArray());
            Templated(entry, deliverable, null, Tree.PackageRoot, null);
            Hashes(entry, deliverable.Header["format"], context.Types.Formats, null);
            return entry;
        }

        // ---- The derived members ---------------------------------------------------------------

        private static void Derived(JsonObject entry, string id, string? parent, DerivedEntry? derived)
        {
            entry["id"] = id;
            entry["parent"] = parent;
            entry["reads"] = Strings(derived?.Reads ?? []);
            entry["runDependent"] = derived?.RunDependent ?? false;
            entry["profileCarrying"] = derived?.ProfileCarrying ?? false;
            entry["modelDerived"] = derived?.ModelDerived ?? false;
        }

        /// <summary>
        /// #1503 (D3, 04-D14): <c>plainConverts</c>, from each of <c>if</c>, <c>expression</c>, <c>while</c> and
        /// <c>until</c> that <paramref name="entry"/> has non-null to its site's list (whether each <c>plain</c> of the rule
        /// converts, in the evaluator's order; <c>[]</c> for a rule with none); <c>{}</c> for an entry with none of them.
        /// </summary>
        private void PlainConverts(JsonObject entry, object declaration)
        {
            var sites = context.Plains.GetValueOrDefault(declaration);
            var plains = new JsonObject();
            foreach (var member in RuleMembers)
            {
                if (entry[member] is null)
                    continue;
                var list = sites?.GetValueOrDefault(member) ?? [];
                plains[member] = new JsonArray(list.Select(converts => (JsonNode?)converts).ToArray());
            }

            entry["plainConverts"] = plains;
        }

        /// <summary><c>body</c>, <c>bodyHash</c>, and what the seam said of it: <c>renders</c> and <c>runSpots</c>.</summary>
        private static void Body(JsonObject entry, string? body, DerivedEntry? derived)
        {
            entry["body"] = body;
            entry["bodyHash"] = body == null ? null : CanonicalJson.HashText(body);
            var said = body == null ? null : derived?.Body;
            entry["renders"] = new JsonArray((said?.Renders ?? []).Select(r => (JsonNode?)new JsonObject
            {
                ["n"] = r.N,
                ["source"] = r.Source,
                ["renderer"] = r.Renderer,
            }).ToArray());
            entry["runSpots"] = new JsonArray((said?.RunSpots ?? []).Select(s => (JsonNode?)new JsonObject
            {
                ["site"] = s.Site,
                ["at"] = s.At,
                ["reads"] = s.Reads,
                ["unit"] = s.Unit,
                ["neutral"] = s.Neutral,
            }).ToArray());
        }

        // ---- #1504 (area 07): what a template places, and the keys its heads name ---------------

        /// <summary>
        /// D2 and D1 on a templated entry (an agent's, a <c>rendered</c> node's, a deliverable's): <c>placements</c>, every
        /// unit its body places in order of appearance, placed units expanded in place (a macro's body's snippets right
        /// after the macro's own entry), a folder macro preceded by its folder once per unit that places one of its macros (so
        /// the folder is decided wherever one of its macros may be, and once for the row: the runner decides a site once);
        /// each with the site that places it (<c>within</c>), its rule with keys rooted for this entry's
        /// place (<paramref name="frame"/>, read by <paramref name="reader"/>) and that rule's <c>plainConverts</c>, and a
        /// macro's marker form. Then <c>names</c>, each head the body writes to the rooted key it names, where they differ.
        /// </summary>
        private void Templated(JsonObject entry, object declaration, SourceFunction? function, ScopeFrame frame, SourceNode? reader)
        {
            var templates = context.Check.Templates;
            var placements = new JsonArray();
            var folders = new HashSet<(string Id, string? Within)>();
            void Add(PlacedUnit unit)
            {
                var s = unit.Site.Split('.');
                if (unit.Instruction is not null && s is ["macro", var id, _] && folders.Add((id, unit.Within)) && MacroFolder(id, function) is { } folder)
                    placements.Add(Placement($"macro-folder.{id}", unit.Within, folder.Index["if"], folder, "if", null, frame, reader));

                var (holder, member) = s[0] switch
                {
                    "part" => (unit.Body.Host.Declaration, Context.PartMember(s[1])),
                    "macro" => (unit.Instruction?.Host.Declaration ?? unit.Body.Host.Declaration, "if"),
                    _ => (unit.Body.Host.Declaration, "if"),
                };
                var form = unit.Marker is { } marker ? (marker.Block ? "block" : "inline") : null;
                placements.Add(Placement(unit.Site, unit.Within, unit.If, holder, member, form, frame, reader));
            }

            foreach (var unit in templates?.Units(declaration, function) ?? [])
            {
                Add(unit);

                // #1504 (part 05 § 6.3): the snippets a macro's body places (and those they place), after the macro's own
                // entry, within the macro, so that each is decided as a prompt's snippet is.
                foreach (var placed in unit.MacroBody?.Units ?? [])
                    Add(placed);
            }

            entry["placements"] = placements;
            var names = new JsonObject();
            foreach (var (head, key) in templates?.Names(declaration, function) ?? new Dictionary<string, string>())
                names[head] = key;
            entry["names"] = names;
        }

        /// <summary>One <c>placements</c> entry (D2): <paramref name="rule"/> rooted at <paramref name="frame"/>, its <c>plainConverts</c> those of <paramref name="holder"/>'s <paramref name="member"/>.</summary>
        private JsonObject Placement(
            string site, string? within, JsonNode? rule, object holder, string member, string? form, ScopeFrame frame, SourceNode? reader)
        {
            var written = rule is null || rule.GetValueKind() == System.Text.Json.JsonValueKind.Null ? null : Logic(rule, frame, reader);
            var plains = written is null ? [] : context.Plains.GetValueOrDefault(holder)?.GetValueOrDefault(member) ?? [];
            return new JsonObject
            {
                ["site"] = site,
                ["within"] = within,
                ["if"] = written,
                ["plainConverts"] = new JsonArray(plains.Select(p => (JsonNode?)p).ToArray()),
                ["form"] = form,
            };
        }

        /// <summary>The macro folder <paramref name="id"/>, <paramref name="function"/>'s own before the package's.</summary>
        private SourceTextFolder? MacroFolder(string id, SourceFunction? function) =>
            (function?.Definitions.Macros.Items.FirstOrDefault(m => m.Id == id) ?? context.Package.Definitions.Macros.Items.FirstOrDefault(m => m.Id == id))
                as SourceTextFolder;

        /// <summary>
        /// <c>formatHash</c> and <c>typeHash</c> of <paramref name="format"/>: a bundled name's, as in <c>formatHashes</c> and
        /// <c>typeHashes</c>; an inline schema's, as the type services give them in its scope; null for none.
        /// </summary>
        private void Hashes(JsonObject entry, JsonNode? format, FormatSet scope, SourceFunction? function)
        {
            if (format == null || format.GetValueKind() == System.Text.Json.JsonValueKind.Null)
            {
                entry["formatHash"] = null;
                entry["typeHash"] = null;
                return;
            }

            if (FormatSet.NameOf(format) is { } name && scope.Find(name) is { } found
                && context.Formats.FormatHashes[CompiledFormats.KeyOf(found, function)] is { } full
                && context.Formats.TypeHashes[CompiledFormats.KeyOf(found, function)] is { } type)
            {
                entry["formatHash"] = full.DeepClone();
                entry["typeHash"] = type.DeepClone();
                return;
            }

            entry["formatHash"] = FormatTypes.FormatHash(format, scope);
            entry["typeHash"] = FormatTypes.TypeHash(format, scope);
        }

        // ---- D3: references written with their keys --------------------------------------------

        /// <summary>A clone of <paramref name="rule"/>, each <c>var</c> and <c>missing</c> path rewritten where it is read (D3); as written when <paramref name="frame"/> is null.</summary>
        private JsonNode? Logic(JsonNode? rule, ScopeFrame? frame, SourceNode? reader)
        {
            var copy = rule?.DeepClone();
            if (frame != null)
                Paths(copy, frame, reader, 0);
            return copy;
        }

        private void Paths(JsonNode? node, ScopeFrame frame, SourceNode? reader, int depth)
        {
            if (depth > RuleDepth)
                return;
            switch (node)
            {
                case JsonArray array:
                    foreach (var element in array)
                        Paths(element, frame, reader, depth + 1);
                    break;
                case JsonObject { Count: 1 } operation:
                    var (name, operand) = operation.First();
                    if (name == "var" && SourceLayout.Text(operand) is { } path)
                        operation["var"] = Reference(path, frame, reader);
                    else if (name == "missing" && operand is JsonArray paths)
                    {
                        for (var i = 0; i < paths.Count; i++)
                        {
                            if (SourceLayout.Text(paths[i]) is { } missing)
                                paths[i] = Reference(missing, frame, reader);
                        }
                    }
                    else if (name == "object" && operand is JsonObject fields)
                    {
                        foreach (var (_, field) in fields)
                            Paths(field, frame, reader, depth + 1);
                    }
                    else
                        Paths(operand, frame, reader, depth + 1);
                    break;
            }
        }

        /// <summary>
        /// <paramref name="text"/> read at <paramref name="frame"/>: a path starting <c>node.&lt;id&gt;</c> or
        /// <c>check.&lt;id&gt;</c> written with the key it resolves to, the rest as written (D3); anything else as written.
        /// </summary>
        private string Reference(string text, ScopeFrame? frame, SourceNode? reader)
        {
            if (frame == null || ReferenceString.Parse(text) is not { } reference || reference.Segments.Count < 2
                || reference.Segments[0] is not ("node" or "check"))
            {
                return text;
            }

            var resolved = Tree.Resolve(reference, frame, "", "", condition: false, reader);
            if (resolved.Key is not { } key)
                return text;
            return reference.Segments.Count == 2 ? key : $"{key}.{string.Join('.', reference.Segments.Skip(2))}";
        }

        /// <summary><paramref name="folder"/>, then each delivery folder under it, in source order.</summary>
        private static IEnumerable<SourceDeliveryFolder> DeliveryFolders(SourceDeliveryFolder folder) =>
            folder.Folders.SelectMany(DeliveryFolders).Prepend(folder);
    }
}
