using Consultologist.Api.Models;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v27 (#1235, package-format-v27-design.md § 1.3, § 1.4, § 5): the per-item
/// calculus of an if/case inside a loop, kept beside the orchestrator's state
/// and free of Durable types so it is unit-testable. An inner flow node is one
/// whose row carries In (the loop) and the loop's collection as its ForEach; its
/// decisions arrive one per (flow, item) from the decide-flow-item activity.
/// A branch node's instance for an item is <em>taken</em> when its flow node's
/// decision for that item lists it, <em>untaken</em> when the decision lists
/// another branch (it never runs and counts as settled), and the item is
/// <em>skipped</em> for the flow when no branch was taken — the loop keeps the
/// count, the item contributes nothing.
/// </summary>
internal sealed class ConsultInnerFlows
{
    private readonly IReadOnlyDictionary<string, ConsultNodeDescriptor> _nodesById;

    public ConsultInnerFlows(IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById)
    {
        _nodesById = nodesById;
    }

    /// <summary>The decisions by instance key (flow:item).</summary>
    public Dictionary<string, ConsultFlowItemDecision> Decisions { get; } = new(StringComparer.Ordinal);

    /// <summary>The instance keys (flow:item) skipped by condition — settled with nothing to read.</summary>
    public HashSet<string> SkippedInstances { get; } = new(StringComparer.Ordinal);

    /// <summary>An if/case row listed in a loop's body — decided once per item.</summary>
    public static bool IsInner(ConsultNodeDescriptor node) =>
        node.Kind is ConsultNodeFlows.If or ConsultNodeFlows.Case && node.In is not null && node.ForEach is not null;

    public bool IsInnerFlow(string nodeId) => _nodesById.TryGetValue(nodeId, out var node) && IsInner(node);

    /// <summary>The inner flow node a branch node runs inside, or null for every other node.</summary>
    public ConsultNodeDescriptor? FlowOf(ConsultNodeDescriptor node) =>
        node.In is { } ownerId && _nodesById.TryGetValue(ownerId, out var owner) && IsInner(owner) ? owner : null;

    public ConsultFlowItemDecision? DecisionOf(string flowId, string itemId) =>
        Decisions.GetValueOrDefault(ConsultNodeScheduler.InstanceKey(flowId, itemId));

    public bool Decided(string flowId, string itemId) => Decisions.ContainsKey(ConsultNodeScheduler.InstanceKey(flowId, itemId));

    /// <summary>Whether this node's instance for the item runs: no inner flow above it, or its flow node took the branch holding it for that item.</summary>
    public bool Taken(ConsultNodeDescriptor node, string itemId) =>
        FlowOf(node) is not { } flow || (DecisionOf(flow.Id, itemId)?.Taken.Contains(node.Id, StringComparer.Ordinal) ?? false);

    /// <summary>Whether this node's instance for the item never runs: its flow node decided the item and took another branch (or none).</summary>
    public bool Untaken(ConsultNodeDescriptor node, string itemId) =>
        FlowOf(node) is { } flow && DecisionOf(flow.Id, itemId) is { } decision && !decision.Taken.Contains(node.Id, StringComparer.Ordinal);

    /// <summary>Whether the node's instance for the item is settled: it has an output, failed, is untaken, or (an inner flow) was skipped for the item.</summary>
    public bool Settled(ConsultNodeDescriptor node, string itemId, IReadOnlyDictionary<string, NodeRunResult> outputs, bool failed)
    {
        var key = ConsultNodeScheduler.InstanceKey(node.Id, itemId);
        return failed || outputs.ContainsKey(key) || SkippedInstances.Contains(key) || Untaken(node, itemId);
    }

    /// <summary>The returned node of the branch taken for the item, or null while undecided or when none.</summary>
    public string? RetOf(string flowId, string itemId) => DecisionOf(flowId, itemId)?.Ret;

    /// <summary>Whether the node is the returned node of the branch its flow node took for the item.</summary>
    public bool IsTakenRet(ConsultNodeDescriptor node, string itemId) =>
        FlowOf(node) is { } flow && string.Equals(RetOf(flow.Id, itemId), node.Id, StringComparison.Ordinal);

    /// <summary>The loop whose ret is this inner flow node, or null when the loop returns a plain node beside it.</summary>
    public ConsultNodeDescriptor? LoopReturning(string flowId) =>
        _nodesById.Values.FirstOrDefault(node => node.Kind == ConsultNodeFlows.For && string.Equals(node.Ret, flowId, StringComparison.Ordinal));

    /// <summary>
    /// #1246: the alias <c>flow:item</c> — an inner if/case's per-item output is its
    /// taken branch's returned node's, once that node has its output for the item
    /// (a skipped item has no alias). Written here, in one place, so that every
    /// reader of the aliases (the fan summary pass that marks the flow's node-level
    /// row, the loop's composition, the deliverables) sees them current: the
    /// smoke's <c>applies</c> row went missing because the summary pass ran
    /// before the aliases landed and only a scalar node followed. Returns whether
    /// an alias was written.
    /// </summary>
    public bool AliasTakenRets(IDictionary<string, NodeRunResult> outputs, Func<ConsultNodeDescriptor, IReadOnlyList<IReadOnlyDictionary<string, string>>> fanItems)
    {
        var progressed = false;

        foreach (var flow in _nodesById.Values.Where(IsInner))
        {
            foreach (var item in fanItems(flow))
            {
                var key = ConsultNodeScheduler.InstanceKey(flow.Id, item["id"]);
                if (outputs.ContainsKey(key) || RetOf(flow.Id, item["id"]) is not { } ret)
                {
                    continue;
                }

                if (outputs.TryGetValue(ConsultNodeScheduler.InstanceKey(ret, item["id"]), out var branchResult))
                {
                    outputs[key] = branchResult;
                    progressed = true;
                }
            }
        }

        return progressed;
    }

    /// <summary>The instances of a branch node that never run — one per item its flow node decided against it.</summary>
    public int UntakenInstances(ConsultNodeDescriptor node, IEnumerable<IReadOnlyDictionary<string, string>> items) =>
        FlowOf(node) is null ? 0 : items.Count(item => Untaken(node, item["id"]));
}
