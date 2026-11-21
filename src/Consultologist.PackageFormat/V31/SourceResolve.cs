namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): what <see cref="SourceResolve.Check"/> learned of a package: its
/// <see cref="ScopeTree"/>, the scopes its expressions are read in (<see cref="ResolvedScopes"/>), and every node's
/// rooted key (part 01 § 9.3; inside a function, local to it), by the node itself.
/// </summary>
public sealed record Resolution(ScopeTree Tree, IExpressionScopes Scopes, IReadOnlyDictionary<SourceNode, string> SourceKeys);

/// <summary>
/// #1502 (specVersion 31, area 05): the resolve step of <see cref="SourceCheck.Run"/>, after the types and before the
/// expressions: every reference of a package resolved to its key and typed. Never throws on package content.
/// </summary>
public static class SourceResolve
{
    /// <summary>
    /// The resolution of <paramref name="package"/> under <paramref name="types"/>; <c>Resolution</c> is null exactly
    /// when there is a refusal, which are ordered by path (ordinal), then in the order found. A null argument is the
    /// caller's error.
    /// </summary>
    public static (IReadOnlyList<SourceRefusal> Refusals, Resolution? Resolution) Check(SourcePackage package, PackageTypes types)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(types);

        var tree = ScopeTree.Build(package, types);
        return ([], new Resolution(tree, new ResolvedScopes(tree, package), tree.Keys));
    }
}
