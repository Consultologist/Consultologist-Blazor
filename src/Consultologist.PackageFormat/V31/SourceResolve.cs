namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): what <see cref="SourceResolve.Check"/> learned of a package: its
/// <see cref="ScopeTree"/>, the scopes its expressions are read in (<see cref="ResolvedScopes"/>), every node's
/// rooted key (part 01 § 9.3; inside a function, local to it), by the node itself; every <c>call</c> bound to its
/// function (<see cref="CallBinding"/>, by the call node); and the warnings the step gives (S04-074 for a format a
/// function lists in <c>uses</c> and never names), ordered by path.
/// </summary>
public sealed record Resolution(
    ScopeTree Tree,
    IExpressionScopes Scopes,
    IReadOnlyDictionary<SourceNode, string> SourceKeys,
    IReadOnlyDictionary<SourceLeaf, CallBinding> Calls,
    IReadOnlyList<SourceRefusal> Warnings);

/// <summary>
/// #1502 (specVersion 31, area 05): a <c>call</c> bound (part 04 § 7.7): the <paramref name="Function"/> it runs, its
/// <paramref name="Arguments"/> by parameter id (each the reference resolved where the call is; <c>null</c> for the
/// literal <c>null</c>; iterate the function's <see cref="SourceFunction.Inputs"/> for their declared order), and its
/// <paramref name="Depth"/> (§ 7.8: 0 outside every function, <i>d</i>+1 inside a function reached at depth <i>d</i>,
/// the deepest way it is reached; a function no call outside every function reaches is counted as reached at 0).
/// </summary>
public sealed record CallBinding(SourceFunction Function, IReadOnlyDictionary<string, Resolved?> Arguments, int Depth);

/// <summary>
/// #1502 (specVersion 31, area 05): the resolve step of <see cref="SourceCheck.Run"/>, after the types and before the
/// expressions: every reference of a package resolved to its key and typed, and part 04's rules of what reads it (a
/// loop's source and <c>itemLabel</c>, a while's unused body nodes: <c>SourceResolve.Loops.cs</c>; a case's subject
/// and values: <c>SourceResolve.Branches.cs</c>; each call's function and arguments: <c>SourceResolve.Calls.cs</c>; each
/// function's <c>ret</c> and <c>uses</c>, the call graph, depth and the profile a parameter carries:
/// <c>SourceResolve.Functions.cs</c>; a function's own format whose bundle key the package already uses, A05-004:
/// <see cref="CompiledFormats.Collisions"/>). Never throws on package content.
/// </summary>
public static partial class SourceResolve
{
    /// <summary>
    /// The profile's fields and their formats, as <c>v31/profile.json</c> lists them (part 06 § 10), in its order: what
    /// <c>profile.&lt;field&gt;</c> passed to a <c>with</c> is typed by. Held to the file by a test.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> ProfileFields = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["name"] = "string",
        ["credentials"] = "string",
        ["organisation"] = "string",
        ["signature"] = "plaintext",
        ["closing"] = "plaintext",
    };

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
        var refusals = new List<SourceRefusal>();
        var warnings = new List<SourceRefusal>();
        Loops(tree, refusals);
        Branches(tree, refusals);
        var calls = Calls(tree, package, types, refusals);
        Functions(package, types, refusals, warnings);
        var depths = Graph(tree, package, calls, refusals);
        Profile(tree, package, calls, refusals);
        refusals.AddRange(CompiledFormats.Collisions(package, types));
        if (refusals.Count > 0)
            return (refusals.OrderBy(r => r.Path, StringComparer.Ordinal).ToList(), null);

        var bindings = new Dictionary<SourceLeaf, CallBinding>(ReferenceEqualityComparer.Instance);
        foreach (var bound in calls)
            bindings[bound.Call] = new CallBinding(bound.Function, bound.Arguments, depths.GetValueOrDefault(bound.Call));
        return ([], new Resolution(tree, new ResolvedScopes(tree, package), tree.Keys, bindings,
            warnings.OrderBy(w => w.Path, StringComparer.Ordinal).ToList()));
    }
}
