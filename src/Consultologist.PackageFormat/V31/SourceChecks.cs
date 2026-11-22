namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1505 (specVersion 31, area 08): the publish rules of <c>checks/</c> and of what gates a deliverable (part 06 §§ 1, 3,
/// 4). The shape rules need only the source model, and run where the type rules do (<see cref="SourceCheck.Run"/>):
/// under <c>checks/</c> a leaf is <c>kind: computed</c> (S06-010) with <c>format: check_result</c> (S06-009), and a flow
/// node is a <c>for</c>, an <c>if</c> or a <c>case</c> (a <c>while</c> or a <c>call</c> is S06-011; D17). The rules that
/// read what a reference names run once the resolver has its <see cref="ScopeTree"/>: a <c>for</c> in <c>checks/</c>
/// iterates a loop's <c>.items</c> (S06-014, D17); a deliverable's <c>check</c> names a check at the top of
/// <c>checks/</c> (A08-001); and that check's <c>if</c> reads nothing that can be <c>null</c> (S06-035, D18). Never throws
/// on package content.
/// </summary>
internal static class SourceChecks
{
    /// <summary>
    /// The refusals of <paramref name="package"/>'s checks, ordered by path (ordinal), then in the order found: the shape
    /// rules always, and, with <paramref name="tree"/> (the resolver's, of this package), the rules that read through it.
    /// A null package is the caller's error.
    /// </summary>
    public static IReadOnlyList<SourceRefusal> Check(SourcePackage package, ScopeTree? tree)
    {
        ArgumentNullException.ThrowIfNull(package);

        var refusals = new List<SourceRefusal>();
        foreach (var node in ScopeTree.Walk(package.Checks.Nodes))
            Shape(node, refusals);

        if (tree != null)
        {
            foreach (var loop in ScopeTree.Walk(package.Checks.Nodes).OfType<SourceFor>())
                Loop(tree, loop, refusals);
            Gates(package, tree, refusals);
        }

        return refusals.OrderBy(r => r.Path, StringComparer.Ordinal).ToList();
    }

    /// <summary>D17: a node under <c>checks/</c> is a computed check returning <c>check_result</c>, or a <c>for</c>, an <c>if</c> or a <c>case</c>.</summary>
    private static void Shape(SourceNode node, List<SourceRefusal> refusals)
    {
        switch (node)
        {
            case SourceWhile:
                refusals.Add(CheckSentences.HoldsKind(SourceLayout.Join(node.Path, "index.json"), node.Kind));
                break;
            case SourceLeaf { Kind: "call" } call:
                refusals.Add(CheckSentences.HoldsKind(call.Path, call.Kind));
                break;
            case SourceLeaf { Kind: not "computed" } leaf:
                refusals.Add(CheckSentences.HasKind(leaf.Id, leaf.Kind, leaf.Path));
                break;
            case SourceLeaf leaf when SourceLayout.Text(leaf.Header["format"]) is { } format && format != "check_result":
                refusals.Add(CheckSentences.HasFormat(leaf.Id, format, leaf.Path));
                break;
        }
    }

    /// <summary>
    /// S06-014 (D17): a check loop iterates <c>node.&lt;l&gt;.items</c> or <c>check.&lt;l&gt;.items</c> of a <c>for</c>
    /// (<see cref="LoopSource.OverItems"/>). A source the resolver refused, or could not type, is its own to say.
    /// </summary>
    private static void Loop(ScopeTree tree, SourceFor loop, List<SourceRefusal> refusals)
    {
        var source = tree.Source(loop);
        if (source.Resolved.Kind is ScopeHeadKind.Refused or ScopeHeadKind.Deferred || source.OverItems)
            return;
        refusals.Add(CheckSentences.LoopRunsOver(loop.Id, loop.In, SourceLayout.Join(loop.Path, "index.json")));
    }

    /// <summary>
    /// Each deliverable's <c>check</c>, in folder order: one that is not <c>check.&lt;id&gt;</c> of a check (a leaf) at the
    /// top of <c>checks/</c> is A08-001; for one that is, the first read of the check's <c>if</c> that can be <c>null</c> is
    /// S06-035 (D18), said once per check, naming the first deliverable it gates.
    /// </summary>
    private static void Gates(SourcePackage package, ScopeTree tree, List<SourceRefusal> refusals)
    {
        var judged = new HashSet<SourceLeaf>(ReferenceEqualityComparer.Instance);
        var deliverables = package.Deliverables.Deliverables.Concat(package.Deliverables.Folders.SelectMany(f => f.Deliverables));
        foreach (var deliverable in deliverables)
        {
            if (SourceLayout.Text(deliverable.Header["check"]) is not { } reference)
                continue;
            if (ReferenceString.Parse(reference) is not { Segments: ["check", var id] }
                || tree.ChecksRoot.Visible("check", id) is not SourceLeaf check)
            {
                refusals.Add(CheckSentences.CheckedBy(deliverable.Id, reference, deliverable.Path));
                continue;
            }

            if (judged.Add(check) && NullableRead(tree, check) is { } read)
                refusals.Add(CheckSentences.GateIfReadsNullable(check.Id, deliverable.Id, read, check.Path));
        }
    }

    /// <summary>
    /// The first read of <paramref name="check"/>'s <c>if</c>, in written order, that can be <c>null</c>: its head is
    /// nullable (a node not always valued, an input not required), a step of the read passes through a nullable field, or
    /// the format read is nullable (as a <c>case</c>'s subject is judged). A read that does not resolve or type is the
    /// expression step's to refuse, and is passed over here.
    /// </summary>
    private static string? NullableRead(ScopeTree tree, SourceLeaf check)
    {
        var frame = tree.FrameOf(check);
        var formats = tree.FormatsOf(frame);
        foreach (var path in JsonLogicShape.Paths(check.Header["if"]))
        {
            if (ReferenceString.Parse(path) is not { } reference)
                continue;
            var head = tree.Resolve(reference, frame, $"Check '{check.Id}'", check.Path, condition: true, check);
            if (head.Kind is not (ScopeHeadKind.Format or ScopeHeadKind.Value))
                continue;

            var s = reference.Segments;
            var rest = s.Skip(Math.Clamp(head.Length, 0, s.Count)).ToList();
            List<string> steps = head.Kind == ScopeHeadKind.Value ? ["value", .. rest] : rest;
            var read = FormatReads.Read(head.Format, steps, formats);
            if (read.Fault != ReadFault.None)
                continue;
            if (head.Nullable || read.Nullable || FormatTypes.IsNullable(read.Format, formats))
                return path;
        }

        return null;
    }
}
