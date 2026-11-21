namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the scope of one place of a package: every path read there is
/// <see cref="ScopeTree.Resolve"/>d at <paramref name="frame"/>, said under <paramref name="subject"/> on
/// <paramref name="path"/>, <paramref name="reader"/> the node that reads (for S04-050 and S04-051).
/// </summary>
public sealed class ResolvedScope(ScopeTree tree, ScopeFrame frame, string subject, string path, SourceNode? reader) : IExpressionScope
{
    private readonly ScopeTree tree = tree ?? throw new ArgumentNullException(nameof(tree));
    private readonly ScopeFrame frame = frame ?? throw new ArgumentNullException(nameof(frame));
    private readonly string subject = subject ?? throw new ArgumentNullException(nameof(subject));
    private readonly string path = path ?? throw new ArgumentNullException(nameof(path));

    /// <inheritdoc/>
    public ScopeHead Head(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0)
            return new ScopeHead(ScopeHeadKind.Unknown, 0, null, false, null);

        var read = tree.Resolve(new ReferenceString(string.Join('.', segments), segments), frame, subject, path, true, reader);
        return new ScopeHead(read.Kind, read.Length, read.Format, read.Nullable, read.Refusal);
    }
}

/// <summary>
/// #1502 (specVersion 31, area 05): the scope each expression site of a package is read in (part 03 § 4.2), over its
/// <see cref="ScopeTree"/>: a node's rule at the node's place; a <c>while</c>'s <c>while</c> and <c>until</c>, and a
/// <c>for</c>'s <c>until</c>, in the frame of their own body (they read its nodes by bare id); a deliverable's
/// <c>if</c> at the top of <c>nodes/</c>. A macro's or a snippet's <c>if</c> is read where it is placed, which is area
/// 07's: until then its scope stays <see cref="PackageScope"/>'s, deferred.
/// </summary>
public sealed class ResolvedScopes(ScopeTree tree, SourcePackage package) : IExpressionScopes
{
    private readonly ScopeTree tree = tree ?? throw new ArgumentNullException(nameof(tree));
    private readonly SourcePackage package = package ?? throw new ArgumentNullException(nameof(package));

    /// <inheritdoc/>
    public IExpressionScope For(SiteContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var site = context.Site;
        var node = context.Declaration as SourceNode;
        var frame = (site.Kind, node) switch
        {
            (ExpressionSiteKind.MacroIf or ExpressionSiteKind.SnippetIf, _) => null,
            (ExpressionSiteKind.DeliverableIf, _) => tree.PackageRoot,
            (ExpressionSiteKind.While or ExpressionSiteKind.Until, { } loop) => tree.BodyOf(loop),
            (_, { } leaf) => tree.FrameOf(leaf),
            _ => null,
        };

        return frame == null
            ? new PackageScope(package, inFunction: context.Function != null)
            : new ResolvedScope(tree, frame, site.Subject, site.Path, node);
    }
}
