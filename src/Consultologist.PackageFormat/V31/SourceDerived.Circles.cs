namespace Consultologist.PackageFormat.V31;

/// <summary>#1502 (specVersion 31, area 05): nodes that read each other in a circle (A05-003).</summary>
public static partial class SourceDerived
{
    /// <summary>
    /// A05-003: the nodes and checks of the package and of each function (its intrinsic scope; a function's nodes reach
    /// no node outside it), each reading what its content and its conditions read (this pass's node,
    /// <c>pass.node.&lt;id&gt;</c>, as <c>node.&lt;id&gt;</c>: #1504, ruled by the owner, ruling 7, part 04 § 3.3), except an
    /// earlier pass (<c>pass.previous…</c>, <c>pass.history</c>), a loop's <c>item</c> reading the loop's source, and a flow
    /// node waiting for its body. #1504 (area 07): a templated node also reads what each rule of its <c>placements</c>
    /// reads at its place (<see cref="Vertex.Placed"/>), as <c>RunPlan</c> counts them among its dependencies. #1505 (area
    /// 08; D3): the deliverables are vertices too, each reading what its body and its <c>if</c> read, and a check reading
    /// a deliverable reads it; a check outside the deliverable's gate set (<see cref="GateSets"/>) waits for its outcome,
    /// which reads the deliverable and its gate, so the circle names the deliverable it waits through (<c>Node 'gate_a'
    /// reads node 'emr.summary', which reads node 'gate_b', …</c>). Each set of vertices that reach each other is said once, from its
    /// ordinal-first node, as the shortest circle back to it (the vertices read taken in ordinal order), at that node's
    /// file, each named by its bare id (a deliverable by its key without its root: the row's words name nodes, 05-D34).
    /// Before the circles, A08-003 for a check in a deliverable's gate set reading its <c>.state</c>, which it decides.
    /// Each walk visits a vertex once.
    /// </summary>
    private static void Circles(DerivedGraph graph, List<SourceRefusal> refusals)
    {
        var vertices = graph.Vertices
            .Where(v => !v.Union && (v.Declaration is SourceNode or SourceDeliverable || v.Id.StartsWith("item:", StringComparison.Ordinal)))
            .ToList();
        var ids = vertices.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        var edges = vertices.ToDictionary(v => v.Id, v => v.Content.Concat(v.Conditions).Concat(v.Placed)
            .Where(r => !r.Earlier && r.Id != null && ids.Contains(r.Id))
            .Select(r => r.Id!)
            .Concat(v.Contains.Where(ids.Contains))
            .Distinct(StringComparer.Ordinal)
            .ToList(), StringComparer.Ordinal);

        // D3: a reader in the gate set reads the composed deliverable (an ordinary edge, and not its .state, A08-003);
        // every other reader waits for the outcome, and so for the gate: it reads the outcome, a vertex of its own
        // (outcome:<key>) that reads the deliverable and its gate, so a circle through it names the deliverable.
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (deliverable, gate, set) in GateSets(graph, edges).ToList())
        {
            var outcome = $"outcome:{deliverable.Key}";
            edges[outcome] = [deliverable.Key, gate.Id];
            ids.Add(outcome);
            names[outcome] = Bare(deliverable);
            foreach (var reader in vertices.Where(v => v.Declaration is SourceNode))
            {
                var reads = reader.Content.Concat(reader.Conditions).Where(r => !r.Earlier && r.Id == deliverable.Key).ToList();
                if (reads.Count == 0)
                    continue;
                if (!set.Contains(reader.Id))
                {
                    edges[reader.Id].Remove(deliverable.Key);
                    edges[reader.Id].Add(outcome);
                    continue;
                }

                if (reads.Any(r => r.Canonical == $"{deliverable.Key}.state"))
                {
                    var check = (SourceNode)reader.Declaration!;
                    refusals.Add(CheckSentences.ReadsStateItDecides(check.Id, Bare(deliverable), ((SourceNode)gate.Declaration!).Id, HeaderFile(check)));
                }
            }
        }

        foreach (var key in edges.Keys)
            edges[key] = edges[key].Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var reach = edges.Keys.ToDictionary(id => id, id => Reachable(id, edges), StringComparer.Ordinal);
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in vertices.Where(v => v.Declaration is SourceNode).Select(v => v.Id).Order(StringComparer.Ordinal))
        {
            if (said.Contains(start) || !reach[start].Contains(start))
                continue;
            var ring = ids.Where(id => id == start || (reach[start].Contains(id) && reach[id].Contains(start))).ToHashSet(StringComparer.Ordinal);
            said.UnionWith(ring);
            var chain = Shortest(start, ring, edges)
                .Select(id => names.TryGetValue(id, out var name) ? name : graph.ById[id].Declaration switch
                {
                    SourceNode node => node.Id,
                    SourceDeliverable deliverable => Bare(deliverable),
                    _ => null,
                })
                .OfType<string>().ToList();
            var first = (SourceNode)graph.ById[start].Declaration!;
            var after = chain.Count > 1 ? string.Join("', which reads node '", chain.Skip(1)) : first.Id;
            refusals.Add(ResolveSentences.ReadInACircle(first.Id, after, HeaderFile(first)));
        }
    }

    /// <summary>A deliverable's key without its root, as a check reads it (<c>emr.note</c> of <c>deliverable.emr.note</c>).</summary>
    private static string Bare(SourceDeliverable deliverable) =>
        deliverable.Key.StartsWith("deliverable.", StringComparison.Ordinal) ? deliverable.Key["deliverable.".Length..] : deliverable.Key;

    /// <summary>
    /// D3: each gated deliverable, in folder order, with its gate (the check its <c>check</c> names) and its gate set: the
    /// gate and every check the gate depends on, transitively, through checks only (a check's reads and conditions, a
    /// check loop's source and <c>item</c>, its <c>ret</c> and its body), as <c>RunPlan</c> derives it from
    /// <c>Depends</c> over <c>checks</c>. A check reached only through a node or another deliverable is not in it.
    /// </summary>
    private static IEnumerable<(SourceDeliverable Deliverable, Vertex Gate, HashSet<string> Set)> GateSets(
        DerivedGraph graph, Dictionary<string, List<string>> edges)
    {
        static bool InChecks(string id) =>
            id.StartsWith("check.", StringComparison.Ordinal) || id.StartsWith("item:check.", StringComparison.Ordinal);

        foreach (var vertex in graph.Vertices.Where(v => v.Declaration is SourceDeliverable))
        {
            var deliverable = (SourceDeliverable)vertex.Declaration!;
            if (SourceLayout.Text(deliverable.Header["check"]) is not { } gateKey
                || !graph.ById.TryGetValue(gateKey, out var gate) || gate.Declaration is not SourceNode || !edges.ContainsKey(gate.Id))
            {
                continue;
            }

            var set = new HashSet<string>(StringComparer.Ordinal) { gate.Id };
            var queue = new Queue<string>([gate.Id]);
            while (queue.Count > 0)
            {
                foreach (var next in edges[queue.Dequeue()].Where(InChecks))
                {
                    if (set.Add(next))
                        queue.Enqueue(next);
                }
            }

            yield return (deliverable, gate, set);
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
