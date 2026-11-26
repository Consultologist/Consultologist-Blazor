using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1508 (ruled by the owner, 2026-11-26, item 1; F-D1 to F-D5; part 10 § 1.2): the nodes only deliverables with a
/// condition need. 30 ran only what an applicable deliverable needed (<c>ConsultGenerationJobStarter.DecideFireSet</c>: the
/// firing deliverables' closure, classifiers and gating reads always); 31 runs every node, so the migration writes the
/// condition on each leaf of such a node (<see cref="Migrated.DeliverableGates"/>), which <see cref="WriteNode"/> puts in
/// the leaf's <c>if</c> with A10-087.
/// </summary>
internal static partial class Migrate
{
    /// <summary>
    /// Fills <see cref="Migrated.DeliverableGates"/> and <see cref="Migrated.GatingDeliverables"/> (F-D2). K, what 30 ran
    /// whatever the deliverables, is the closure over 30's edges (and each flow's edge to its children) of every
    /// classifier, every node a deliverable's condition reads, every top-level <c>if</c>/<c>case</c> flow's decision reads,
    /// and the roots of every deliverable with no condition or one the converter refused. A top-level node outside K, not a
    /// check, whose subtree holds no <c>while</c> (F-D3), that a deliverable with a converted condition reaches, is gated by
    /// those deliverables' conditions in 30's order (one, or joined by <c>or</c>), written for each leaf of it (F-D1) that is
    /// not a check and not in K.
    /// </summary>
    private static void DeliverableGates(Migrated m, NodeTree tree)
    {
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (id, targets) in WorkflowNodeClosure.Edges(m.Manifest30.Nodes ?? [], m.Walk30.Edges))
            edges[id] = new HashSet<string>(targets, StringComparer.Ordinal);
        foreach (var (id, flow) in tree.ById.Where(n => WorkflowImpliedPaths.IsFlow(n.Value)))
        {
            if (!edges.TryGetValue(id, out var targets))
                edges[id] = targets = new HashSet<string>(StringComparer.Ordinal);
            targets.UnionWith(NodeTree.Children(flow));
        }

        var always = new List<string>();
        foreach (var (id, node) in tree.ById)
        {
            if (!WorkflowImpliedPaths.IsFlow(node) && node.Let is null && node.Format == "classification")
                always.Add(id);
        }

        foreach (var id in tree.Top)
        {
            if (tree.ById[id] is { If: not null } or { Case: not null })
                always.AddRange(m.Walk30.EdgesOf(id));
        }

        var gating = new List<(string Id, JsonNode Rule, HashSet<string> Reach)>();
        var scope = m.Scope((string?)null);
        foreach (var deliverable in m.Manifest30.EffectiveDeliverables)
        {
            var roots = new List<string>();
            foreach (var source in (deliverable.Sources ?? []).Where(s => s is not null))
                roots.Add(source.StartsWith("node.", StringComparison.Ordinal) ? source["node.".Length..] : source);
            foreach (var macro in deliverable.Macros ?? [])
                roots.AddRange(m.Walk30.MacroEdges(macro.Id));
            if (deliverable.Check is { } check)
                roots.Add(check.StartsWith("node.", StringComparison.Ordinal) ? check["node.".Length..] : check);

            var condition = deliverable.If ?? deliverable.When;
            var rule = condition is null ? null : WorkflowConditionConverter.FromScriban(condition, scope, ConversionKind.Condition).Logic;
            if (rule is null)
            {
                always.AddRange(roots);
                continue;
            }

            foreach (var path in JsonLogicShape.Paths(rule))
            {
                var segments = path.Split('.');
                if (segments.Length > 1 && segments[0] is "node" or "check")
                    always.Add(segments[1]);
            }

            gating.Add((deliverable.Id, rule, WorkflowNodeClosure.Reachable(roots, edges)));
        }

        var ran = WorkflowNodeClosure.Reachable(always, edges);
        foreach (var id in tree.Top)
        {
            if (IsCheck(m, tree.ById[id]) || ran.Contains(id) || HoldsAWhile(tree, id, 0))
                continue;
            var by = gating.Where(g => g.Reach.Contains(id)).ToList();
            if (by.Count == 0)
                continue;

            var ids = by.Select(g => g.Id).ToList();
            foreach (var leaf in Leaves(m, tree, id, 0).Where(l => !ran.Contains(l)))
            {
                m.DeliverableGates[leaf] = by.Count == 1
                    ? by[0].Rule.DeepClone()
                    : new JsonObject { ["or"] = new JsonArray([.. by.Select(g => g.Rule.DeepClone())]) };
                m.GatingDeliverables[leaf] = ids;
            }
        }
    }

    /// <summary>Whether node <paramref name="id"/> is a <c>while</c> or holds one in its bodies, at any depth.</summary>
    private static bool HoldsAWhile(NodeTree tree, string id, int depth) =>
        depth <= SourceBounds.JsonDepth && tree.ById.TryGetValue(id, out var node)
        && (node.While is not null
            || (WorkflowImpliedPaths.IsFlow(node) && NodeTree.Children(node).Any(c => HoldsAWhile(tree, c, depth + 1))));

    /// <summary>The leaves of node <paramref name="id"/>: itself when it is a leaf, else every leaf of its bodies, at any depth; never a check.</summary>
    private static IEnumerable<string> Leaves(Migrated m, NodeTree tree, string id, int depth)
    {
        if (depth > SourceBounds.JsonDepth || !tree.ById.TryGetValue(id, out var node) || IsCheck(m, node))
            return [];
        return WorkflowImpliedPaths.IsFlow(node)
            ? NodeTree.Children(node).SelectMany(c => Leaves(m, tree, c, depth + 1))
            : [id];
    }
}
