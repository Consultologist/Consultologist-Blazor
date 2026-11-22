using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): <c>graph</c> (part 07 § 2.7; D7). <c>edges</c>: from what is read to the entry that
/// reads it, keys without members, distinct, in the order found (the nodes of <c>nodes/</c> then <c>checks/</c>, a flow
/// node before its bodies, then the package's renderers, snippets and macros, then the deliverables): <c>reads</c> (a
/// content read of a node, check, input, <c>run.&lt;fact&gt;</c> or <c>profile.&lt;field&gt;</c>), <c>if</c> (a
/// condition's read, a <c>case</c> subject's), <c>for</c> (a loop's source), <c>aligned</c> (a production loop to the
/// loop over its <c>.items</c>, beside its <c>for</c>), <c>ret</c> (a body's <c>ret</c> to its flow node),
/// <c>calls</c> (<c>function.&lt;id&gt;</c> to the call), <c>placed</c> (a macro to the body that places it, through
/// the seam) and <c>gates</c> (a deliverable's <c>check</c>); #1505 (area 08): a check's read of a deliverable is a
/// <c>reads</c> (or <c>if</c>) edge from the deliverable. A function is a sealed scope with keys of its own, so what
/// its body reads is in its entries, not here. <c>order</c>: every key of <c>inputs</c>, <c>nodes</c>, <c>checks</c>
/// and <c>deliverables</c>, a flow node with its bodies after it, each level in a topological order of the edges
/// between what it holds but <c>gates</c> (#1505: a deliverable composes before its gate runs), ties by source order
/// (A05-003 refused a circle; were one left, the earliest in source order goes first).
/// </summary>
public static partial class ResolveCompiler
{
    private sealed record Edge(string From, string To, string Kind);

    private static JsonObject Graph(Context context)
    {
        var edges = Edges(context);
        return new JsonObject
        {
            ["edges"] = new JsonArray(edges.Select(e => (JsonNode?)new JsonObject
            {
                ["from"] = e.From,
                ["to"] = e.To,
                ["kind"] = e.Kind,
            }).ToArray()),
            ["order"] = Strings(new Ordering(context, edges).All()),
        };
    }

    private static List<Edge> Edges(Context context)
    {
        var found = new List<Edge>();
        var seen = new HashSet<Edge>();
        void Add(string from, string to, string kind)
        {
            var edge = new Edge(from, to, kind);
            if (seen.Add(edge))
                found.Add(edge);
        }

        var tree = context.Tree;
        foreach (var node in tree.All().Where(n => tree.FrameOf(n).Function == null))
            EdgesOf(context, node.Key, node, Add);
        foreach (var key in DefinitionKeys(context.Package.Definitions))
            EdgesOf(context, key, null, Add);
        foreach (var deliverable in Folders(context.Package.Deliverables).SelectMany(f => f.Deliverables))
        {
            EdgesOf(context, deliverable.Key, null, Add);
            // #1505 (area 08): publish refuses a check that names no check at the top of checks/ (A08-001), so none is passed over here.
            if (SourceLayout.Text(deliverable.Header["check"]) is { } gate && ReferenceString.Parse(gate) is { Segments: ["check", var id] }
                && tree.ChecksRoot.Visible("check", id) is { } check)
            {
                Add(check.Key, deliverable.Key, "gates");
            }
        }

        return found;
    }

    /// <summary>The edges into the entry <paramref name="key"/> (<paramref name="node"/> when it is one), from its derived reads.</summary>
    private static void EdgesOf(Context context, string key, SourceNode? node, Action<string, string, string> add)
    {
        if (context.Derive(key, null) is not { } entry)
            return;
        var rets = node == null ? [] : ScopeTree.Blocks(node).Select(b => b.Ret).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var loop = node as SourceFor;
        foreach (var read in entry.ContentReads)
        {
            if (read.Key is not { } from)
            {
                if (Fact(read.Path) is { } fact)
                    add(fact, key, "reads");
                continue;
            }

            switch (Root(from))
            {
                case "function":
                    add(from, key, "calls");
                    break;
                case "macro":
                    add(from, key, "placed");
                    break;
                case "node" or "check" when rets.Contains(read.Path) && read.Path == from:
                    add(from, key, "ret");
                    break;
                case "node" or "check" or "input" when loop != null && read.Path == loop.In:
                    add(from, key, "for");
                    if (context.Tree.Source(loop).OverItems)
                        add(from, key, "aligned");
                    break;
                case "node" or "check" or "input" or "profile" or "deliverable":
                    // #1505 (area 08; D1): a check's read of a deliverable is a reads edge from it.
                    add(from, key, "reads");
                    break;
            }
        }

        foreach (var read in entry.ConditionReads)
        {
            if (read.Key is { } from && Root(from) is "node" or "check" or "input" or "profile" or "deliverable")
                add(from, key, "if");
            else if (read.Key == null && Fact(read.Path) is { } fact)
                add(fact, key, "if");
        }
    }

    private static string Root(string key) => key.Split('.')[0];

    /// <summary><c>run.&lt;fact&gt;</c> for a read of a run fact; null otherwise.</summary>
    private static string? Fact(string path) =>
        ReferenceString.Parse(path) is { Segments: ["run", var fact, ..] } ? $"run.{fact}" : null;

    /// <summary>The keys of the package's renderers, snippets and macros (folders, then their items), in source order.</summary>
    private static IEnumerable<string> DefinitionKeys(SourceDefinitions definitions)
    {
        foreach (var renderer in definitions.Renderers.Items)
            yield return $"renderer.{renderer.Id}";
        foreach (var (root, entries) in new[] { ("snippet", definitions.Snippets.Items), ("macro", definitions.Macros.Items) })
        {
            foreach (var entry in entries)
            {
                yield return $"{root}.{entry.Id}";
                if (entry is SourceTextFolder folder)
                {
                    foreach (var item in folder.Items)
                        yield return $"{root}.{folder.Id}.{item.Id}";
                }
            }
        }
    }

    /// <summary><c>graph.order</c>: each level of the package in a topological order of the edges lifted to it, a flow node's bodies after it.</summary>
    private sealed class Ordering
    {
        private readonly Dictionary<string, string?> parents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> children = new(StringComparer.Ordinal);
        private readonly List<Edge> edges;
        private readonly List<string> top = [];

        public Ordering(Context context, List<Edge> edges)
        {
            // #1505 (area 08, D19; part 06 §§ 6.7, 7): the order is topological over every edge but gates. A gate decides a
            // deliverable's release, not its composition, so a gate that reads its deliverable comes after it, by the
            // reads edge, and a gate that reads only nodes keeps its place.
            this.edges = edges.Where(e => e.Kind != "gates").ToList();
            var package = context.Package;
            foreach (var input in package.Manifest.Inputs)
                Place($"input.{input.Id}", null);
            foreach (var node in package.Nodes.Nodes.Concat(package.Checks.Nodes))
                Nodes(node, null);
            foreach (var deliverable in Folders(package.Deliverables).SelectMany(f => f.Deliverables))
                Place(deliverable.Key, null);
        }

        public List<string> All()
        {
            var order = new List<string>();
            Level(top, null, order, 0);
            return order;
        }

        private void Nodes(SourceNode node, string? parent)
        {
            Place(node.Key, parent);
            foreach (var block in ScopeTree.Blocks(node))
                foreach (var inner in block.Nodes)
                    Nodes(inner, node.Key);
        }

        private void Place(string key, string? parent)
        {
            if (!parents.TryAdd(key, parent))
                return;
            if (parent == null)
                top.Add(key);
            else
            {
                if (!children.TryGetValue(parent, out var list))
                    children[parent] = list = [];
                list.Add(key);
            }
        }

        private void Level(List<string> level, string? parent, List<string> order, int depth)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < level.Count; i++)
                index[level[i]] = i;

            var after = level.Select(_ => new HashSet<int>()).ToList();
            var waiting = new int[level.Count];
            foreach (var edge in edges)
            {
                if (Lift(edge.From, parent) is { } from && Lift(edge.To, parent) is { } to && from != to
                    && index.TryGetValue(from, out var a) && index.TryGetValue(to, out var b) && after[a].Add(b))
                {
                    waiting[b]++;
                }
            }

            var done = new bool[level.Count];
            for (var placed = 0; placed < level.Count; placed++)
            {
                var next = Enumerable.Range(0, level.Count).FirstOrDefault(i => !done[i] && waiting[i] == 0, -1);
                if (next < 0)
                    next = Enumerable.Range(0, level.Count).First(i => !done[i]);
                done[next] = true;
                foreach (var b in after[next])
                    waiting[b]--;

                order.Add(level[next]);
                if (depth < SourceBounds.JsonDepth && children.TryGetValue(level[next], out var inner))
                    Level(inner, level[next], order, depth + 1);
            }
        }

        /// <summary>The key of <paramref name="key"/>'s enclosing node (or itself) that is a member of the level under <paramref name="parent"/>; null when outside it.</summary>
        private string? Lift(string key, string? parent)
        {
            var current = key;
            for (var steps = 0; steps <= parents.Count && parents.TryGetValue(current, out var up); steps++)
            {
                if (up == parent)
                    return current;
                if (up == null)
                    return null;
                current = up;
            }

            return null;
        }
    }
}
