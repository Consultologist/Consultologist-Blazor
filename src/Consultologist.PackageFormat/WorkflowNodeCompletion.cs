namespace Consultologist.PackageFormat;

/// <summary>
/// v25 (package-format-v25-design.md §§ 6–7; B5 #1004): a node's output
/// composed at its completion, stated once. The reply — validated, judged
/// non-empty — has its sanctioned inline slots filled where the model placed
/// their markers, then its before and after instances composed around it; what
/// comes out is what <c>node.&lt;id&gt;.text</c>, the aggregator and the
/// deliverable read, and what <c>outputHash</c> is over. Pure over rendered
/// texts: the engine renders each instance's and fill's text over the node's
/// own scope and decides which markers the prompt that ran sanctioned; this
/// composes and attributes. A node with nothing to compose and no marker
/// returns the reply's bytes unchanged.
/// </summary>
public static class WorkflowNodeCompletion
{
    /// <summary>A before/after instance whose <c>when</c> held, its text rendered.</summary>
    public sealed record Instance(string Id, bool After, string Text);

    /// <summary>One entry of the node's <c>appended[]</c>: what was placed and where it came from, in output order.</summary>
    public sealed record Placed(string Kind, string Id);

    /// <summary>One thing removed or withheld, by name, with the sentence that says why.</summary>
    public sealed record Removed(string Kind, string Id, string Reason);

    public sealed record Result(string Text, IReadOnlyList<Placed> Appended, IReadOnlyList<Removed> Removals);

    /// <summary>A before/after instance (the library macro's text placed around the output).</summary>
    public const string MacroKind = "macro";

    /// <summary>A slot: the macro's text filled where the model emitted its marker.</summary>
    public const string SlotKind = "slot";

    /// <summary>
    /// The order: the reply's markers filled (a sanctioned one with its text, any
    /// other removed and named), the before instances in their order, the filled
    /// reply, the after instances in theirs — one blank line between blocks,
    /// each block trailing-trimmed (the convention the prompt-side composition
    /// set at v23). <c>appended[]</c> reads in output order: before macros,
    /// slots as their markers appear, after macros.
    /// </summary>
    /// <param name="fills">The sanctioned slots: macro id to its rendered text.</param>
    /// <param name="removalReason">The sentence for a marker no fill sanctions, by macro id.</param>
    public static Result Compose(
        string reply,
        IReadOnlyList<Instance> instances,
        IReadOnlyDictionary<string, string> fills,
        Func<string, string> removalReason)
    {
        var appended = new List<Placed>();
        var removals = new List<Removed>();
        var slots = new List<Placed>();

        var filled = WorkflowSlotMarker.Pattern.IsMatch(reply)
            ? WorkflowSlotMarker.Pattern.Replace(reply, match =>
            {
                var id = match.Groups[1].Value;

                if (fills.TryGetValue(id, out var text))
                {
                    slots.Add(new Placed(SlotKind, id));
                    return text;
                }

                removals.Add(new Removed(SlotKind, id, removalReason(id)));
                return string.Empty;
            })
            : reply;

        if (instances.Count == 0)
        {
            return new Result(filled, slots, removals);
        }

        var parts = new List<string>();

        foreach (var instance in instances.Where(i => !i.After))
        {
            parts.Add(instance.Text.TrimEnd());
            appended.Add(new Placed(MacroKind, instance.Id));
        }

        parts.Add(filled);
        appended.AddRange(slots);

        foreach (var instance in instances.Where(i => i.After))
        {
            parts.Add(instance.Text.TrimEnd());
            appended.Add(new Placed(MacroKind, instance.Id));
        }

        return new Result(string.Join("\n\n", parts), appended, removals);
    }
}
