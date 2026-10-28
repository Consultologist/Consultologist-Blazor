using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance;

/// <summary>
/// v26 (#1208, record storage 8): the one reading of a record's flow rows — a loop row
/// (<c>kind: for</c>) is the fan its body nodes run under, an if/case row names the branch
/// it took, and a body id of an untaken branch that has neither a descriptor nor an output
/// row is pruned (the engine lists it in the flow row's <c>body</c> alone; the registry's
/// sentence lists it in <c>nodes</c> too — either way it is drawn here). Below 8 a node's
/// own <c>forEach</c> is the fan and none of this applies.
/// </summary>
public static class RecordFlow
{
    public const string Loop = "for";

    public static bool IsFlow(ConsultGenerationNodeDescriptor node) => node.Kind is not null;

    public static bool IsLoop(ConsultGenerationNodeDescriptor node) => node.Kind == Loop;

    public static bool IsBranching(ConsultGenerationNodeDescriptor node) => node.Kind is "if" or "case";

    /// <summary>The fan a node runs under: its own forEach below 8, its loop row's collection at 8; null for none.</summary>
    public static string? FanOf(ConsultGenerationNodeDescriptor node, IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes) =>
        node.ForEach
        ?? (node.In is { } loopId && nodes?.FirstOrDefault(n => n.Id == loopId) is { Kind: Loop } loop ? loop.Collection : null);

    /// <summary>The loop row a body node runs inside, or null.</summary>
    public static ConsultGenerationNodeDescriptor? LoopOf(ConsultGenerationNodeDescriptor node, IReadOnlyList<ConsultGenerationNodeDescriptor>? nodes) =>
        node.In is { } loopId ? nodes?.FirstOrDefault(n => n.Id == loopId && n.Kind == Loop) : null;

    /// <summary>The branch an if/case row took — the record's word, "none" when it took none.</summary>
    public static string BranchText(ConsultGenerationNodeDescriptor node) => node.Branch ?? "none";

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
        foreach (var flow in nodes.Where(IsBranching))
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
