namespace Consultologist.PackageFormat;

/// <summary>
/// Reachability over the DAG's node→node edges. There are exactly two kinds: a
/// binding's <c>node:</c> source and an aggregator's source list
/// (package-format-v5.md, package-format-v6-design.md § 2). <c>input:</c>,
/// <c>data:</c> and <c>item:</c> are node→data rather than node→node,
/// <c>forEach</c> names a collection, and a binding's renderer (<c>as</c>) is
/// presentation — none of them is an edge.
///
/// Two callers, one definition of "what depends on what": the validator's
/// reachability and cycle rules, and the job starter's prune of the node set to
/// the fire set (#355).
/// </summary>
public static class WorkflowNodeClosure
{
    /// <summary>
    /// Every node→node edge, as node id → the ids it depends on.
    ///
    /// Edges naming an **undeclared** node are dropped, and that is load-bearing
    /// rather than tidy: <see cref="WorkflowPackageValidator"/> runs Kahn's
    /// algorithm over this same map, and a dependency that is not itself a node
    /// can never be resolved — so a dangling reference would leave its node
    /// permanently unresolved and be reported as a cycle. The dangling
    /// reference has its own error; it must not also invent a second, wrong one.
    /// </summary>
    public static IReadOnlyDictionary<string, HashSet<string>> Edges(IReadOnlyList<WorkflowNodeSpec> nodes)
    {
        var declared = nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Add(string fromId, string targetId)
        {
            if (!declared.Contains(targetId))
            {
                return;
            }

            edges.TryAdd(fromId, new HashSet<string>(StringComparer.Ordinal));
            edges[fromId].Add(targetId);
        }

        foreach (var node in nodes)
        {
            foreach (var binding in (node.Bindings ?? new Dictionary<string, WorkflowBindingValue>()).Values)
            {
                if (binding.From.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal))
                {
                    Add(node.Id, binding.From[WorkflowNodeBindingSources.NodePrefix.Length..]);
                }
            }

            foreach (var sourceRef in node.Aggregate ?? new List<string>())
            {
                // Prefix-tolerant, matching WorkflowPackageBlocks.ExpandAggregator
                // and the engine's aggregator loop: a bare id those two would
                // resolve has to be an edge here, or a prune built from this map
                // drops exactly the node they then look up.
                Add(node.Id, sourceRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                    ? sourceRef[WorkflowNodeBindingSources.NodePrefix.Length..]
                    : sourceRef);
            }

            // v12 (§ 13): a check node's operands are its dependencies — the
            // check settles only after both, so acyclicity, reachability and
            // the starter's prune all see the same third edge kind.
            foreach (var operand in new[] { node.Of, node.In })
            {
                if (operand != null && operand.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal))
                {
                    Add(node.Id, operand[WorkflowNodeBindingSources.NodePrefix.Length..]);
                }
            }
        }

        return edges;
    }

    /// <summary>
    /// Every node transitively reachable from the roots, the roots included.
    /// </summary>
    public static HashSet<string> Reachable(
        IEnumerable<string> roots,
        IReadOnlyDictionary<string, HashSet<string>> edges)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>();

        foreach (var root in roots)
        {
            if (reachable.Add(root))
            {
                frontier.Enqueue(root);
            }
        }

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            if (!edges.TryGetValue(current, out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                if (reachable.Add(dependency))
                {
                    frontier.Enqueue(dependency);
                }
            }
        }

        return reachable;
    }
}
