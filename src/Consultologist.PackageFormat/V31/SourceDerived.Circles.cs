namespace Consultologist.PackageFormat.V31;

/// <summary>#1502 (specVersion 31, area 05): nodes that read each other in a circle (A05-003).</summary>
public static partial class SourceDerived
{
    /// <summary>
    /// A05-003: the nodes and checks of the package and of each function (its intrinsic scope; a function's nodes reach
    /// no node outside it), each reading what its content and its conditions read, except through <c>pass</c> (a while's
    /// own passes, the earlier ones among them), a loop's <c>item</c> reading the loop's source, and a flow node waiting
    /// for its body. Each set of nodes that reach each other is said once, from its ordinal-first node, as the shortest
    /// circle back to it (the nodes read taken in ordinal order), at that node's file. Each walk visits a vertex once.
    /// </summary>
    private static void Circles(DerivedGraph graph, List<SourceRefusal> refusals)
    {
        var vertices = graph.Vertices
            .Where(v => !v.Union && (v.Declaration is SourceNode || v.Id.StartsWith("item:", StringComparison.Ordinal)))
            .ToList();
        var ids = vertices.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var edges = vertices.ToDictionary(v => v.Id, v => v.Content.Concat(v.Conditions)
            .Where(r => !r.Pass && r.Id != null && ids.Contains(r.Id))
            .Select(r => r.Id!)
            .Concat(v.Contains.Where(ids.Contains))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList(), StringComparer.Ordinal);

        var reach = vertices.ToDictionary(v => v.Id, v => Reachable(v.Id, edges), StringComparer.Ordinal);
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in vertices.Where(v => v.Declaration is SourceNode).Select(v => v.Id).Order(StringComparer.Ordinal))
        {
            if (said.Contains(start) || !reach[start].Contains(start))
                continue;
            var ring = ids.Where(id => id == start || (reach[start].Contains(id) && reach[id].Contains(start))).ToHashSet(StringComparer.Ordinal);
            said.UnionWith(ring);
            var chain = Shortest(start, ring, edges)
                .Select(id => graph.ById[id].Declaration).OfType<SourceNode>().Select(n => n.Id).ToList();
            var first = (SourceNode)graph.ById[start].Declaration!;
            var after = chain.Count > 1 ? string.Join("', which reads node '", chain.Skip(1)) : first.Id;
            refusals.Add(ResolveSentences.ReadInACircle(first.Id, after, HeaderFile(first)));
        }
    }

    /// <summary>The vertices <paramref name="start"/> reaches through one edge or more (itself only through a circle).</summary>
    private static HashSet<string> Reachable(string start, Dictionary<string, List<string>> edges)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            foreach (var next in edges[queue.Dequeue()])
            {
                if (seen.Add(next))
                    queue.Enqueue(next);
            }
        }

        return seen;
    }

    /// <summary>The shortest circle from <paramref name="start"/> back to it inside <paramref name="ring"/>: <c>[start, …, last]</c>, <c>last</c> reading <c>start</c>.</summary>
    private static List<string> Shortest(string start, HashSet<string> ring, Dictionary<string, List<string>> edges)
    {
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<string>([start]);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var next in edges[at].Where(ring.Contains))
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
}
