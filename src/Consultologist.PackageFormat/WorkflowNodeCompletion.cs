namespace Consultologist.PackageFormat;

/// <summary>
/// v25 (package-format-v25-design.md §§ 6–7; B5 #1004, #1044): a node's
/// output composed at its completion, stated once. The reply — validated,
/// judged non-empty, then RENDERED as a template over the node's scope (#1044:
/// every <c>{{ macro.&lt;id&gt;.content }}</c> the model wrote is that macro's text,
/// rendered; the render's own trace names each such read) — has its before and
/// after instances composed around it; what comes out is what
/// <c>node.&lt;id&gt;.content</c>, the aggregator and the deliverable read, and
/// what <c>outputHash</c> is over. Pure over rendered texts: the engine renders
/// the reply and each instance's text over the node's own scope; this composes
/// and attributes. A node with no instance returns the rendered reply's bytes
/// unchanged.
/// </summary>
public static class WorkflowNodeCompletion
{
    /// <summary>A before/after instance whose <c>when</c> held, its text rendered.</summary>
    public sealed record Instance(string Id, bool After, string Text);

    /// <summary>One entry of the node's <c>appended[]</c>: what was placed and where it came from, in output order.</summary>
    public sealed record Placed(string Kind, string Id);

    public sealed record Result(string Text, IReadOnlyList<Placed> Appended);

    /// <summary>A before/after instance (the library macro's text placed around the output).</summary>
    public const string MacroKind = "macro";

    /// <summary>An inline macro (#1047, the word): the macro whose text the reply read as <c>{{ macro.&lt;id&gt;.content }}</c> when it rendered (#1044). Below 25 the document-level kind is <c>slot</c>, the marker's fill.</summary>
    public const string InlineKind = "inline";

    /// <summary>
    /// The order: the before instances in their order, the rendered reply, the
    /// after instances in theirs — one blank line between blocks, each block
    /// trailing-trimmed (the convention the prompt-side composition set at v23).
    /// <c>appended[]</c> reads in output order: before macros, the reply's inline
    /// reads as the render met them, after macros.
    /// </summary>
    /// <param name="renderedReply">The reply rendered over the node's scope.</param>
    /// <param name="inlineReads">The macro ids whose text the reply's render read, in order, once per read (<see cref="TemplateRenderer.TemplateRenderTrace.ContentReads"/>).</param>
    public static Result Compose(
        string renderedReply,
        IReadOnlyList<Instance> instances,
        IReadOnlyList<string> inlineReads)
    {
        var inline = inlineReads.Select(id => new Placed(InlineKind, id)).ToList();

        if (instances.Count == 0)
        {
            return new Result(renderedReply, inline);
        }

        var appended = new List<Placed>();
        var parts = new List<string>();

        foreach (var instance in instances.Where(i => !i.After))
        {
            parts.Add(instance.Text.TrimEnd());
            appended.Add(new Placed(MacroKind, instance.Id));
        }

        parts.Add(renderedReply);
        appended.AddRange(inline);

        foreach (var instance in instances.Where(i => i.After))
        {
            parts.Add(instance.Text.TrimEnd());
            appended.Add(new Placed(MacroKind, instance.Id));
        }

        return new Result(string.Join("\n\n", parts), appended);
    }
}
