namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): what the resolver derives of one entry (part 07 § 2.5). <paramref name="Key"/> is its
/// rooted key (local to its function inside one); <paramref name="Reads"/> every path it reads, header and body, in D4's
/// canonical form, distinct and ordinal-sorted; <paramref name="ContentReads"/> what its value is made from (part 03
/// § 4.4: never a condition, never an earlier pass; a flow node's <c>ret</c>, a call's <c>function.&lt;id&gt;</c>, a
/// folder's items, a renderer piped into and a macro placed among them), each with the key it reads (null for
/// <c>item</c>, <c>run</c> and an unknown name); <paramref name="ConditionReads"/> its conditions' reads (an <c>if</c>,
/// a <c>while</c>, an <c>until</c>, a <c>case</c> subject, a part's <c>if</c>). <paramref name="BodyKnown"/> is false
/// for a non-empty body the seam did not say (D1). <paramref name="Reproducible"/> is derived for a node or check (null
/// for other entries). The three flags are derived when known; <paramref name="FlagsKnown"/> is false when what the
/// value is made from reaches a body that is not known, and then a flag that is not known to be true is the header's
/// declared value (false on an entry with no header flags). <paramref name="Body"/> is what the seam said its body reads,
/// places, renders and where its run spots are (D1; <see cref="TemplateReads.None"/> for an empty body, null for an entry
/// with no body or a body that is not known), for <c>resolve.json</c>'s <c>places</c>, <c>renders</c> and <c>runSpots</c>.
/// </summary>
public sealed record DerivedEntry(
    string Key,
    IReadOnlyList<string> Reads,
    IReadOnlyList<LineageRead> ContentReads,
    IReadOnlyList<LineageRead> ConditionReads,
    bool BodyKnown,
    bool? Reproducible,
    bool ModelDerived,
    bool RunDependent,
    bool ProfileCarrying,
    bool FlagsKnown,
    TemplateReads? Body = null);

/// <summary>
/// #1502 (specVersion 31, area 05): what the resolver derives of a package. <paramref name="Entries"/> are keyed by rooted
/// key over the nodes, checks, deliverables, and the package's renderers, snippets and macros with their folders
/// (<c>renderer.&lt;id&gt;</c>, <c>snippet.&lt;id&gt;</c>, <c>snippet.&lt;folder&gt;</c>,
/// <c>snippet.&lt;folder&gt;.&lt;item&gt;</c>, <c>macro.&lt;id&gt;</c>, …); <paramref name="FunctionEntries"/> are each
/// function's own, keyed by keys local to it, each parameter standing for its arguments across every call.
/// <paramref name="RunFacts"/> are the <c>run.&lt;fact&gt;</c> read anywhere, <paramref name="ProfileFields"/> the profile
/// fields, both distinct and sorted. <paramref name="Warnings"/> are part 03 § 11.2's (S03-037, S03-038, for every
/// run-dependent decision and computed value) and, with the seam, part 04 § 10.2's S04-074 for snippets, macros and
/// renderers, ordered by path. <paramref name="Complete"/> is true when every body is known.
/// </summary>
public sealed record PackageDerived(
    IReadOnlyDictionary<string, DerivedEntry> Entries,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, DerivedEntry>> FunctionEntries,
    IReadOnlyList<string> RunFacts,
    IReadOnlyList<string> ProfileFields,
    IReadOnlyList<SourceRefusal> Warnings,
    bool Complete)
{
    /// <summary>
    /// What each function's result is of itself, keyed by function id: the three flags of its <c>ret</c> with its
    /// parameters reading nothing (the intrinsic values a call takes from the function, before its arguments'), and
    /// whether they are known. Its <c>Reads</c> are empty: a function reads through its parameters only.
    /// </summary>
    public IReadOnlyDictionary<string, DerivedEntry> Functions { get; init; } = new Dictionary<string, DerivedEntry>(StringComparer.Ordinal);
}

/// <summary>
/// #1502 (specVersion 31, area 05): the derived step of <see cref="SourceCheck.Run"/>, after the expressions: each entry's
/// reads, <c>reproducible</c> and the three contagious flags worked out over the resolved graph
/// (<c>SourceDerived.Graph.cs</c>; part 02 §§ 1.1, 1.2; part 03 §§ 4.4 to 4.6; part 04 § 4, § 7.7) and held against every
/// header (<c>SourceDerived.Flags.cs</c>): nodes reading each other in a circle (A05-003, said first, and alone); a leaf's
/// or check's flag (S03-036); a flow node's or call's flag (S04-016) and <c>reproducible</c> (S04-015); a condition, or a
/// prompt through the seam, reading a profile-carrying value (S03-044, S04-069); and, with the seam (D1), the rules that
/// need what bodies read (S04-035, S04-052, S04-063, the S04-074 warning). A flag that depends on a body the seam does
/// not know is not judged. Never throws on package content.
/// </summary>
public static partial class SourceDerived
{
    /// <summary>
    /// The derived values of <paramref name="package"/>, resolved as <paramref name="resolution"/> says, its sites as
    /// <paramref name="expressions"/> checked them (under <paramref name="types"/>), each body read as
    /// <paramref name="templates"/> says (none: every non-empty body unknown). <c>Derived</c> is null exactly when there is
    /// a refusal; the refusals are ordered by path (ordinal), then in the order found. A null argument other than
    /// <paramref name="templates"/> is the caller's error.
    /// </summary>
    public static (IReadOnlyList<SourceRefusal> Refusals, PackageDerived? Derived) Check(
        SourcePackage package, PackageTypes types, Resolution resolution, PackageExpressions expressions, ITemplateReads? templates = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(expressions);

        var graph = DerivedGraph.Build(package, resolution, templates);
        var refusals = new List<SourceRefusal>();
        Circles(graph, refusals);
        if (refusals.Count > 0)
            return (Ordered(refusals), null);

        var derived = new Derivation(graph, resolution, package);
        var warnings = new List<SourceRefusal>();
        Headers(derived, refusals);
        Decisions(derived, expressions, refusals, warnings);
        Prompts(derived, refusals);
        Values(derived, warnings);
        if (templates != null)
        {
            SourceResolve.UnusedWithBodies(resolution.Tree,
                leaf => graph.BodyOf(leaf, leaf.Body, resolution.Tree.FrameOf(leaf).Function)?.Reads, refusals);
            SourceResolve.FunctionBodies(package, resolution.Tree,
                (declaration, function) => graph.BodyOf(declaration, BodyText(declaration), function), refusals, warnings);
        }

        if (refusals.Count > 0)
            return (Ordered(refusals), null);
        return ([], Package(derived, Ordered(warnings)));
    }

    private static List<SourceRefusal> Ordered(List<SourceRefusal> said) =>
        said.Distinct().OrderBy(r => r.Path, StringComparer.Ordinal).ToList();

    private static string BodyText(object declaration) => declaration switch
    {
        SourceLeaf leaf => leaf.Body,
        SourceText text => text.Body,
        SourceDeliverable deliverable => deliverable.Body,
        _ => "",
    };

    /// <summary>The entries: the package's vertices that are entries, and each function's union ones, in the order built.</summary>
    private static PackageDerived Package(Derivation derived, List<SourceRefusal> warnings)
    {
        var graph = derived.Graph;
        var entries = new Dictionary<string, DerivedEntry>(StringComparer.Ordinal);
        var functions = new Dictionary<string, IReadOnlyDictionary<string, DerivedEntry>>(StringComparer.Ordinal);
        foreach (var function in derived.Package.Functions.Items)
            functions.TryAdd(function.Id, new Dictionary<string, DerivedEntry>(StringComparer.Ordinal));

        foreach (var vertex in graph.Vertices.Where(v => v.Entry && (v.Function == null || v.Union)))
        {
            var entry = Entry(derived, vertex);
            if (vertex.Function == null)
                entries.TryAdd(vertex.Key, entry);
            else
            {
                if (!functions.TryGetValue(vertex.Function.Id, out var own))
                    functions[vertex.Function.Id] = own = new Dictionary<string, DerivedEntry>(StringComparer.Ordinal);
                ((Dictionary<string, DerivedEntry>)own).TryAdd(vertex.Key, entry);
            }
        }

        var reads = graph.Vertices.SelectMany(v => v.Content.Concat(v.Conditions).Concat(v.Other)).ToList();
        var facts = reads.Select(r => r.Path.Split('.')).Where(s => s.Length >= 2 && s[0] == "run")
            .Select(s => $"run.{s[1]}").Distinct().Order(StringComparer.Ordinal).ToList();
        var fields = reads.Select(r => r.Path.Split('.')).Where(s => s.Length >= 2 && s[0] == "profile")
            .Select(s => s[1]).Distinct().Order(StringComparer.Ordinal).ToList();
        var results = new Dictionary<string, DerivedEntry>(StringComparer.Ordinal);
        foreach (var vertex in graph.Vertices.Where(v => v.Declaration == null && v.Function == null && v.Key.StartsWith("function.", StringComparison.Ordinal)))
            results.TryAdd(vertex.Key["function.".Length..], Entry(derived, vertex));
        return new PackageDerived(entries, functions, facts, fields, warnings, graph.Vertices.All(v => v.BodyKnown)) { Functions = results };
    }

    private static DerivedEntry Entry(Derivation derived, Vertex vertex)
    {
        var known = derived.Known(vertex);
        var header = (vertex.Declaration as SourceNode)?.Header;
        bool Flag(LineageFlag flag) => derived.Flag(vertex, flag) || (!known && Declared(header, flag) == true);

        var reads = vertex.Content.Concat(vertex.Conditions).Concat(vertex.Other)
            .Select(r => r.Canonical).Where(c => c.Length > 0).Distinct().Order(StringComparer.Ordinal).ToList();
        return new DerivedEntry(vertex.Key, reads,
            vertex.Content.Select(r => new LineageRead(r.Path, r.Key)).ToList(),
            vertex.Conditions.Select(r => new LineageRead(r.Path, r.Key)).ToList(),
            vertex.BodyKnown,
            vertex.Declaration is SourceNode node ? derived.Reproducible(node) : null,
            Flag(LineageFlag.ModelDerived), Flag(LineageFlag.RunDependent), Flag(LineageFlag.ProfileCarrying), known, vertex.Body);
    }

    /// <summary>The flag as a node header declares it; null when it declares none.</summary>
    private static bool? Declared(System.Text.Json.Nodes.JsonObject? header, LineageFlag flag) =>
        header?[LineageFlags.Member(flag)] is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<bool>(out var declared) ? declared : null;
}
