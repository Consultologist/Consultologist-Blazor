namespace Consultologist.PackageFormat;

/// <summary>One instance of a macro on the node a text runs for: the macro, its gate (<c>if</c> at 26, <c>when</c> below), and the 25 item anchor.</summary>
public sealed record WorkflowInlineGate(string MacroId, string? If, string? ForItem = null);

/// <summary>
/// #1089 (package-format-v26-design.md § 2.3): the instance gate governs the
/// inline read. A node that runs a text reading <c>macro.&lt;id&gt;.prompt</c>
/// and holds an instance of that macro renders the read only where the
/// instance's gate holds. The three answers the design settled: a node with
/// <b>no instance</b> of the macro renders the read as ever (the read is the
/// ask; a placement is not required); a node with <b>two instances</b>
/// renders the read when <b>any</b> instance's gate holds (a placement that
/// will hold needs the model's marker); a read in a prompt is one node's at
/// 26, a snippet read by several nodes is judged per running node. The
/// judgement is one pure function over the node's render scope — the engine
/// calls it before the render and hands the set to
/// <see cref="TemplateRenderScope.SuppressedPrompts"/>; the placement's own
/// skip into <c>excluded[]</c> stays completion's.
/// </summary>
public static class WorkflowInlineGates
{
    /// <summary>
    /// The macros whose inline read the node must leave empty: every instance
    /// of the macro on this node (an instance anchored by <c>forItem</c> to
    /// another item is not this node's) carries a gate, and none holds. An
    /// instance with no gate holds; a macro with no instance is never named.
    /// </summary>
    public static IReadOnlySet<string> Suppressed(
        string nodeId,
        IEnumerable<WorkflowInlineGate> instances,
        string? itemId,
        TemplateRenderScope scope,
        IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null)
    {
        var suppressed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var group in instances
            .Where(i => i.ForItem is null || string.Equals(i.ForItem, itemId, StringComparison.Ordinal))
            .GroupBy(i => i.MacroId, StringComparer.Ordinal))
        {
            var holds = group.Any(instance =>
                string.IsNullOrWhiteSpace(instance.If)
                || WorkflowConditionAt25.Holds($"Node '{nodeId}' macro '{group.Key}' if", instance.If, scope, pipes));

            if (!holds)
            {
                suppressed.Add(group.Key);
            }
        }

        return suppressed;
    }

    /// <summary>
    /// #1090 (§ 2.2): the macros whose own gate — the file header's <c>if</c> —
    /// does not hold: inert for the run, <c>.prompt</c> and <c>.content</c> empty
    /// where read, every placement skipped (the engine's). A macro with no gate
    /// holds. Composes by AND with <see cref="Suppressed"/>: the engine hands
    /// both sets to the scope; the inert set empties the content too.
    /// </summary>
    public static IReadOnlySet<string> Inert(
        IEnumerable<(string MacroId, string? If)> macros,
        TemplateRenderScope scope,
        IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null)
    {
        var inert = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (macroId, gate) in macros)
        {
            if (!string.IsNullOrWhiteSpace(gate) && !WorkflowConditionAt25.Holds($"Macro '{macroId}' if", gate, scope, pipes))
            {
                inert.Add(macroId);
            }
        }

        return inert;
    }

    /// <summary>
    /// #1094 (§ 2.1, § 5.4): the standalone snippets whose own gate — the file
    /// header's <c>if</c> — does not hold: <c>snippet.&lt;id&gt;.content</c> empty
    /// where read. A snippet with no gate holds. A folder item's gate is the fan
    /// filter (the engine's) and, with #1116's direct read, the item's.
    /// </summary>
    public static IReadOnlySet<string> InertSnippets(
        IEnumerable<(string SnippetId, string? If)> snippets,
        TemplateRenderScope scope,
        IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>>? pipes = null)
    {
        var inert = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (snippetId, gate) in snippets)
        {
            if (!string.IsNullOrWhiteSpace(gate) && !WorkflowConditionAt25.Holds($"Snippet '{snippetId}' if", gate, scope, pipes))
            {
                inert.Add(snippetId);
            }
        }

        return inert;
    }
}
