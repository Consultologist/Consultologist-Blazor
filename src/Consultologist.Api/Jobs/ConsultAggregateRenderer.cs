namespace Consultologist.Api.Jobs;

/// <summary>
/// The aggregator's normative rendering (package-format-v6-design.md § 3):
/// sources in declared order joined by blank lines; a forEach source renders as
/// labeled blocks ("## {item name}", blank line, instance output) in collection
/// index order; a scalar source renders verbatim. No prologue, no epilogue, no
/// trailing newline — the bytes feed hashes and downstream prompt inputs, so the
/// spec pins them exactly (see the pinning tests).
/// </summary>
internal static class ConsultAggregateRenderer
{
    public abstract record Part;

    /// <summary>A scalar source's output (prompt node or upstream aggregator).</summary>
    public sealed record ScalarPart(string Text) : Part;

    /// <summary>A forEach source's instances, already in collection index order.</summary>
    public sealed record ForEachPart(IReadOnlyList<(string Name, string Text)> Blocks) : Part;

    public static string Render(IReadOnlyList<Part> parts)
    {
        return string.Join("\n\n", parts.Select(RenderPart));
    }

    /// <summary>
    /// One source's bytes (v12 #619): the per-part half of Render, so the
    /// placement composer interleaves macros between sources while joining
    /// with the same separator — the join is associative, so a composition
    /// with nothing placed is byte-identical to Render (pinned). A
    /// ForEachPart renders as ONE string: an anchor on a fanned source
    /// places around the whole block, never between items (design § 4).
    /// </summary>
    public static string RenderPart(Part part) => part switch
    {
        ScalarPart scalar => scalar.Text,
        ForEachPart forEach => string.Join(
            "\n\n",
            forEach.Blocks.Select(block => $"## {block.Name}\n\n{block.Text}")),
        _ => throw new InvalidOperationException($"Unknown aggregate part '{part.GetType().Name}'.")
    };
}
