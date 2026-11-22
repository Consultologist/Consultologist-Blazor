namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1507 (specVersion 31, area 10; D15, part 02 §§ 1.1, 1.2): the values a node's or check's header writes, as the
/// derivation gives them: <paramref name="Reproducible"/> (null for a kind with no rule), and the three contagious flags.
/// <paramref name="Known"/> is false when what the value is made from reaches a body the seam did not say; a flag that is
/// then false is not known to be false (a true one is: the flags only grow along what a value is made from).
/// </summary>
internal sealed record DerivedFlags(bool? Reproducible, bool ModelDerived, bool RunDependent, bool ProfileCarrying, bool Known);

/// <summary>#1507 (specVersion 31, area 10; D15): the derived flags without the header checks, for the migration.</summary>
public static partial class SourceDerived
{
    /// <summary>
    /// The derived <c>reproducible</c>, <c>modelDerived</c>, <c>runDependent</c> and <c>profileCarrying</c> of every node
    /// and check header of <paramref name="package"/> outside a function (keyed by the path of the header's file: a leaf's
    /// or a check's <c>.md</c>, a flow node's <c>index.json</c>), worked out as <see cref="Check"/> works them out, each body
    /// read as <paramref name="templates"/> says (none: every non-empty body unknown), and never held against what the
    /// headers declare: a declared flag is never read. The flags follow content only, never a condition (part 02 § 1.2), so
    /// no expression site is needed. Empty when nodes read each other in a circle (A05-003, which <see cref="Check"/>
    /// says). Never throws on package content; a null argument other than <paramref name="templates"/> and
    /// <paramref name="dialect"/> is the caller's error.
    /// </summary>
    internal static IReadOnlyDictionary<string, DerivedFlags> Values(
        SourcePackage package, Resolution resolution, ITemplateReads? templates = null, Templates.PackageTemplates? dialect = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(resolution);

        var values = new Dictionary<string, DerivedFlags>(StringComparer.Ordinal);
        var graph = DerivedGraph.Build(package, resolution, templates, dialect ?? templates as Templates.PackageTemplates);
        var circles = new List<SourceRefusal>();
        Circles(graph, circles);
        if (circles.Count > 0)
            return values;

        var derived = new Derivation(graph, resolution, package);
        foreach (var vertex in graph.Vertices.Where(v => v.Function == null && !v.Union))
        {
            if (vertex.Declaration is not SourceNode node)
                continue;
            values.TryAdd(HeaderFile(node), new DerivedFlags(
                derived.Reproducible(node),
                derived.Flag(vertex, LineageFlag.ModelDerived),
                derived.Flag(vertex, LineageFlag.RunDependent),
                derived.Flag(vertex, LineageFlag.ProfileCarrying),
                derived.Known(vertex)));
        }

        return values;
    }
}
