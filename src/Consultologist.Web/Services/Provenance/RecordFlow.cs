using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance;

/// <summary>
/// v26 (#1208, record storage 8): the one reading of a record's flow rows — a loop row
/// (<c>kind: for</c>) is the fan its body nodes run under, an if/case row names the branch
/// it took, and a body id of an untaken branch that has neither a descriptor nor an output
/// row is pruned (the engine lists it in the flow row's <c>body</c> alone, and the registry
/// says the same since provenance v2026.10.9, #1223; a listed descriptor with no output row
/// is still read as pruned, for a record that might carry one). Below 8 a node's
/// own <c>forEach</c> is the fan and none of this applies. v27 (#1236, storage 9): an
/// if/case may run inside a loop — its row carries <c>in</c> and a per-item <c>branches</c>
/// map, a branch node's <c>in</c> names it and its loop is two levels up, an item it
/// skipped by condition is its <c>flow:item</c> row Skipped with a reason.
/// </summary>
public static class RecordFlow
{
    public const string Loop = "for";

    public static bool IsFlow(ConsultGenerationNodeDescriptor node) => node.Kind is not null;

    public static bool IsLoop(ConsultGenerationNodeDescriptor node) => node.Kind == Loop;

    public static bool IsBranching(ConsultGenerationNodeDescriptor node) => node.Kind is "if" or "case";

    /// <summary>The fan a node runs under: its own forEach below 8, its loop row's collection at 8 (v27: through its if/case, two levels); null for none.</summary>
    public static string? FanOf(ConsultGenerationNodeDescriptor node, IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes) =>
        node.ForEach ?? LoopOf(node, nodes)?.Collection;

    /// <summary>v27 (#1236, record storage 9): the if/case row a branch node runs inside (its in names it), or null.</summary>
    public static ConsultGenerationNodeDescriptor? FlowOf(ConsultGenerationNodeDescriptor node, IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes) =>
        node.In is { } ownerId && nodes?.FirstOrDefault(n => n.Id == ownerId) is { } owner && IsBranching(owner) ? owner : null;

    /// <summary>The loop row a body node runs inside, or null; v27 (#1236): a branch node's through its if/case — a reader walks in until it meets a for.</summary>
    public static ConsultGenerationNodeDescriptor? LoopOf(ConsultGenerationNodeDescriptor node, IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes)
    {
        if (node.In is not { } ownerId)
        {
            return null;
        }

        var owner = nodes?.FirstOrDefault(n => n.Id == ownerId);
        return owner is { Kind: Loop } ? owner
            : owner is not null && IsBranching(owner) && owner.In is { } loopId ? nodes?.FirstOrDefault(n => n.Id == loopId && n.Kind == Loop)
            : null;
    }

    /// <summary>v27 (#1236, record storage 9): an if/case inside a loop — its in names a for; decided once per item (its branches map), never a branch of its own.</summary>
    public static bool IsInner(ConsultGenerationNodeDescriptor node, IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes) =>
        IsBranching(node) && node.In is { } loopId && nodes?.Any(n => n.Id == loopId && n.Kind == Loop) == true;

    /// <summary>The branch an if/case row took — the record's word, "none" when it took none; v27: "per item" for one inside a loop.</summary>
    public static string BranchText(ConsultGenerationNodeDescriptor node) => BranchText(node, null);

    public static string BranchText(ConsultGenerationNodeDescriptor node, IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes) =>
        node.Branches is not null || IsInner(node, nodes) ? "per item" : node.Branch ?? "none";

    /// <summary>v27: the per-item branches of an if/case inside a loop, in the record's order; empty for a top-level row.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> BranchesOf(ConsultGenerationNodeDescriptor node) =>
        node.Branches?.ToList() ?? (IReadOnlyList<KeyValuePair<string, string>>)Array.Empty<KeyValuePair<string, string>>();

    /// <summary>v27: the items an inner if/case skipped by condition — its &lt;flow&gt;:&lt;item&gt; rows with status Skipped — item id → the reason (the condition's text).</summary>
    public static IReadOnlyDictionary<string, string> SkippedItemsOf(ConsultGenerationNodeDescriptor flow, IReadOnlyDictionary<string, ConsultGenerationNodeStatus>? outputs) =>
        (outputs ?? new Dictionary<string, ConsultGenerationNodeStatus>())
            .Where(pair => pair.Key.StartsWith(flow.Id + ":", StringComparison.Ordinal) && pair.Value.Status == "Skipped")
            .ToDictionary(pair => pair.Key[(flow.Id.Length + 1)..], pair => pair.Value.Reason ?? "took no branch", StringComparer.Ordinal);

    /// <summary>Every id a flow row lists as its body — drawn inside its flow, never at the top level.</summary>
    public static IReadOnlySet<string> BodyIds(IReadOnlyList<ConsultGenerationNodeDescriptor> nodes) =>
        nodes.Where(IsFlow).SelectMany(flow => flow.Body ?? Array.Empty<string>()).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Per if/case row, the body ids pruned with their untaken branch — no descriptor row (the
    /// engine's served record) or no output row (the registry's listed row), in body order.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> PrunedOf(
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus>? outputs)
    {
        var ids = nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var pruned = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        // v27 (#1236): an if/case inside a loop takes a branch per item — a branch node no item took is untaken, not pruned.
        foreach (var flow in nodes.Where(flow => IsBranching(flow) && !IsInner(flow, nodes)))
        {
            var missing = (flow.Body ?? Array.Empty<string>())
                .Where(id => !ids.Contains(id)
                    || (outputs is null || (!outputs.ContainsKey(id) && !outputs.Keys.Any(k => k.StartsWith(id + ":", StringComparison.Ordinal)))))
                .ToList();
            if (missing.Count > 0)
            {
                pruned[flow.Id] = missing;
            }
        }

        return pruned;
    }
}
