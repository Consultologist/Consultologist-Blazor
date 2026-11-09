namespace Consultologist.PackageFormat;

/// <summary>
/// #1301 (package-format-v29-design.md § 2, § 3, § 6): what 29 adds to the 28
/// path — a loop over a node's value (the source's place, its type, and that it
/// runs before the loop) and a folder item's <c>if</c> that reads a scalar
/// node's value. Run by <see cref="ValidateAt27"/> with <c>at29</c>; below 29
/// the 26–28 sentences stand unchanged (the published vectors pin them until
/// the bump re-exports them).
/// </summary>
public static partial class WorkflowPackageValidator
{
    /// <summary>
    /// § 2.1: a loop's source is a plain node outside every loop whose format
    /// types the value at the path as an array. Judged once the node shapes
    /// exist; the walker types <c>item.value</c> from the same resolution.
    /// </summary>
    private static void ValidateNodeValueLoopsAt29(
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, TemplateNodeShape> nodeShapes,
        IReadOnlyCollection<string> declaredNodes,
        List<string> errors,
        // v30 (#1339, design § 2.2): a source may sit in an enclosing loop's body.
        bool at30 = false)
    {
        foreach (var flow in flows.Flows.Values.Where(f => f.Kind == WorkflowFlowNodes.Kind.For).OrderBy(f => f.Node.Id, StringComparer.Ordinal))
        {
            if (flow.Fan is not NodeValueFan fan)
            {
                continue;
            }

            var subject = $"Loop '{flow.Node.Id}' iterates {fan.Reference}";

            if (flows.Flows.ContainsKey(fan.NodeId))
            {
                errors.Add($"{subject}; '{fan.NodeId}' is a flow node — a loop iterates a plain node's value.");
                continue;
            }

            if (at30)
            {
                // v30: outside the loop, or in a body that encloses it — a node of
                // the outer body is read per outer item (§ 2.2).
                if (flows.Owner.TryGetValue(fan.NodeId, out var sourceOwner)
                    && !flows.AncestorsOf(flow.Node.Id).Contains(sourceOwner, StringComparer.Ordinal))
                {
                    errors.Add($"{subject}; {fan.NodeId} runs inside {flows.Flows[sourceOwner].Inside} — a loop iterates a node outside it, or in an enclosing loop's body.");
                    continue;
                }
            }
            else if (flows.LoopOf(fan.NodeId) is { } inside)
            {
                errors.Add($"{subject}; {fan.NodeId} runs inside loop '{inside}' — a loop iterates a node outside every loop.");
                continue;
            }

            if (!at30 && flows.Owner.TryGetValue(fan.NodeId, out var owner))
            {
                errors.Add($"{subject}; {fan.NodeId} runs inside node '{owner}' — a loop iterates a node outside every flow node.");
                continue;
            }

            if (!declaredNodes.Contains(fan.NodeId))
            {
                errors.Add($"{subject}; the package declares no node '{fan.NodeId}'.");
                continue;
            }

            if (!nodeShapes.ContainsKey(fan.NodeId))
            {
                // A node whose format is missing or unknown: its own sentence says so.
                continue;
            }

            var at = fan.Resolve(nodeShapes, out var problem);

            if (at is null)
            {
                errors.Add(problem!.EndsWith("returns prose", StringComparison.Ordinal)
                    ? $"{subject}; {problem} — a loop iterates a value its format types as an array."
                    : $"{subject}; {problem}.");
            }
            else if (at.Kind != WorkflowInputTypes.Array)
            {
                errors.Add($"{subject}, which {fan.NodeId}'s format types as {at.Describe()}, not an array.");
            }
        }
    }

    /// <summary>
    /// § 2.1, § 6: a loop's source runs before the loop — a source that reads the
    /// loop, or anything that waits on it, is said by name before the generic
    /// cycle sentence. The edge itself is the loop's (and its body's) on the
    /// source, added in <see cref="DeriveEdgesAt26"/>.
    /// </summary>
    private static void ValidateNodeValueLoopSourcesAt29(
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        List<string> errors)
    {
        foreach (var flow in flows.Flows.Values.Where(f => f.Fan is NodeValueFan).OrderBy(f => f.Node.Id, StringComparer.Ordinal))
        {
            var fan = (NodeValueFan)flow.Fan!;
            var loopSide = flow.BodyNodeIds.Append(flow.Node.Id).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>((edges.GetValueOrDefault(fan.NodeId) ?? new HashSet<string>()).Order(StringComparer.Ordinal));

            while (pending.Count > 0)
            {
                var id = pending.Pop();

                if (!seen.Add(id))
                {
                    continue;
                }

                if (loopSide.Contains(id))
                {
                    errors.Add($"Loop '{flow.Node.Id}' iterates {fan.Reference}, and {fan.NodeId} reads node.{flow.Node.Id} — a loop's source runs before the loop.");
                    break;
                }

                foreach (var next in edges.GetValueOrDefault(id) ?? new HashSet<string>())
                {
                    pending.Push(next);
                }
            }
        }
    }

    /// <summary>
    /// § 3.1: at 29 a folder item's <c>if</c> may also read a scalar node's
    /// <c>.value</c> — a node outside every loop that is not the loop over this
    /// folder. Its other refusals are the 26 ones.
    /// </summary>
    private static void ValidateFolderItemConditionsAt29(WorkflowDerivedEdges.Walk walk, WorkflowFlowNodes.Resolved flows, List<string> errors)
    {
        foreach (var condition in walk.Conditions.Where(c => c.Site == WorkflowDerivedEdges.ConditionSite.FolderItem && c.Walk.IsValid))
        {
            var refs = condition.Walk.References;

            foreach (var (id, read) in refs.Nodes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (flows.IsLoop(id) || flows.LoopOf(id) is not null)
                {
                    errors.Add($"{condition.Subject} reads node.{id}, which loops; a condition decides before the loop runs — read a scalar node.");
                }
                else if (read.ReadsContent)
                {
                    errors.Add($"{condition.Subject} reads node.{id}.content; a folder item's if reads a node's .value (a scalar), inputs and item.id.");
                }
            }

            var offending = refs.Snippets.Select(id => $"{TemplateObjects.Snippet}.{id}")
                .Concat(refs.SnippetFolders.Select(id => $"{TemplateObjects.Snippet}.{id}"))
                .Concat(refs.MacroContents.Concat(refs.MacroPrompts).Concat(refs.MacroFolders).Select(id => $"{TemplateObjects.Macro}.{id}"))
                .Concat(refs.ItemMembers.Where(m => m != TemplateMembers.Id).Select(m => $"{TemplateObjects.Item}.{m}"))
                .Concat(refs.RunFacts.Select(m => $"{TemplateObjects.Run}.{m}"))
                .Concat(refs.ProfileFacts.Select(m => $"{TemplateObjects.Profile}.{m}"))
                .FirstOrDefault();

            if (offending is not null)
            {
                errors.Add($"{condition.Subject} reads {offending}; a folder item's if reads inputs, item.id and a scalar node's .value.");
            }
        }
    }
}
