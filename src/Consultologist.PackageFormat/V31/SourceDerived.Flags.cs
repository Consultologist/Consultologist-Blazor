using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05): the flags of every vertex of a <see cref="DerivedGraph"/>, by
/// <see cref="LineageFlags.Derive"/> over its content reads (an agent starts model-derived, a body placing a
/// profile-reading macro and a profile field start profile-carrying, a <c>run.*</c> read is run-dependent); whether what
/// each is made from reaches a body that is not known (the same spread, started at each unknown body); and each node's
/// derived <c>reproducible</c> (part 02 § 1.1).
/// </summary>
internal sealed class Derivation
{
    private static readonly LineageFlag[] All = [LineageFlag.ModelDerived, LineageFlag.RunDependent, LineageFlag.ProfileCarrying];

    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<LineageFlag, LineageValue>> unknown;
    private readonly Dictionary<SourceNode, bool?> reproducible = new(ReferenceEqualityComparer.Instance);

    public Derivation(DerivedGraph graph, Resolution resolution, SourcePackage package)
    {
        Graph = graph;
        Resolution = resolution;
        Package = package;
        Flags = LineageFlags.Derive(graph.Vertices
            .Select(v => new LineageNode(v.Id, v.StartsModel, v.StartsProfile, Reads(v.Content))).ToList());
        unknown = LineageFlags.Derive(graph.Vertices
            .Select(v => new LineageNode(v.Id, !v.BodyKnown, false, Reads(v.Content))).ToList());
    }

    public DerivedGraph Graph { get; }

    public Resolution Resolution { get; }

    public SourcePackage Package { get; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<LineageFlag, LineageValue>> Flags { get; }

    public static IReadOnlyList<LineageFlag> Each => All;

    /// <summary>The reads as <see cref="LineageFlags"/> takes them: a read that carries no flag names no vertex.</summary>
    public static List<LineageRead> Reads(IEnumerable<GraphRead> reads) => reads.Select(r => new LineageRead(r.Path, r.Carries ? r.Id : null)).ToList();

    public LineageValue Value(Vertex vertex, LineageFlag flag) => Flags[vertex.Id][flag];

    public bool Flag(Vertex vertex, LineageFlag flag) => Flags[vertex.Id][flag].Value;

    /// <summary>Whether nothing <paramref name="vertex"/>'s value is made from is a body the seam did not say.</summary>
    public bool Known(Vertex vertex) => !unknown[vertex.Id][LineageFlag.ModelDerived].Value;

    /// <summary>
    /// part 02 § 1.1, part 04 § 1: an agent is not reproducible, a rendered or computed node (a check included) is, whatever
    /// it reads; a call is its function's <c>ret</c>'s value; a flow node is reproducible only if everything in its body
    /// that its result depends on is: each node its bodies' <c>ret</c>s are made from, through content reads and reads of
    /// an earlier pass that stay inside its body (a loop's <c>item</c> followed to the loop's source), is reproducible. Null for a kind with no such
    /// rule. Bounded: each node is worked out once, and calls nest in no ring.
    /// </summary>
    public bool? Reproducible(SourceNode node)
    {
        if (reproducible.TryGetValue(node, out var known))
            return known;
        reproducible[node] = true;
        bool? value = node switch
        {
            SourceLeaf { Kind: "agent" } => false,
            SourceLeaf { Kind: "rendered" or "computed" } => true,
            SourceLeaf { Kind: "call" } call => Resolution.Calls.TryGetValue(call, out var binding)
                ? binding.Function.Body.Nodes.Where(n => n.Key == binding.Function.Body.Ret).Select(Reproducible).FirstOrDefault() ?? true
                : null,
            SourceFor or SourceWhile or SourceIf or SourceCase => Body(node),
            _ => null,
        };
        reproducible[node] = value;
        return value;
    }

    /// <summary>Whether every leaf of <paramref name="flow"/>'s body that its <c>ret</c>s are made from is reproducible.</summary>
    private bool Body(SourceNode flow)
    {
        var function = Resolution.Tree.FrameOf(flow).Function;
        if (Graph.Of(flow, function, union: false) is not { } start)
            return true;
        var inside = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in Blocks(flow))
        {
            foreach (var node in ScopeTree.Walk(block.Nodes))
            {
                if (Graph.Of(node, function, union: false) is { } vertex)
                    inside.Add(vertex.Id);
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<Vertex>();
        foreach (var read in start.Content.Where(r => r.Id != null && inside.Contains(r.Id) && r.Canonical.Length == 0))
        {
            if (seen.Add(read.Id!))
                queue.Enqueue(Graph.ById[read.Id!]);
        }

        while (queue.Count > 0)
        {
            var vertex = queue.Dequeue();
            if (vertex.Declaration is SourceLeaf leaf && Reproducible(leaf) == false)
                return false;
            foreach (var read in vertex.Content.Concat(vertex.Other).Where(r => r.Id != null))
            {
                var next = Graph.ById.GetValueOrDefault(read.Id!);
                if (next != null && (inside.Contains(next.Id) || next.Id.StartsWith("item:", StringComparison.Ordinal)) && seen.Add(next.Id))
                    queue.Enqueue(next);
            }
        }

        return true;
    }

    private static IEnumerable<SourceBlock> Blocks(SourceNode node) => node switch
    {
        SourceFor loop => [loop.Body],
        SourceWhile loop => [loop.Body],
        SourceIf branch => branch.Else == null ? [branch.Then] : [branch.Then, branch.Else],
        SourceCase choice => choice.Branches.Select(b => b.Body).Concat(choice.Else == null ? [] : [choice.Else]),
        _ => [],
    };
}

/// <summary>#1502 (specVersion 31, area 05): the header checks, the profile's rules and the warnings of the derived step.</summary>
public static partial class SourceDerived
{
    /// <summary>
    /// Every node and check (each function's in its union scope): each flag it declares against the derived one, when
    /// known (true, or what it is made from is known): S03-036 for a leaf or a check, S04-016 for a flow node or a call;
    /// a flow node's or call's <c>reproducible</c>: S04-015.
    /// </summary>
    private static void Headers(Derivation derived, List<SourceRefusal> refusals)
    {
        foreach (var vertex in derived.Graph.Vertices.Where(v => v.Function == null || v.Union))
        {
            if (vertex.Declaration is not SourceNode node)
                continue;
            var known = derived.Known(vertex);
            foreach (var flag in Derivation.Each)
            {
                var value = derived.Value(vertex, flag);
                if (Declared(node.Header, flag) is not { } declared || declared == value.Value || (!value.Value && !known))
                    continue;
                refusals.Add(node is SourceLeaf { Kind: not "call" }
                    ? LineageFlags.Disagreement(node.Id, flag, declared, value, node.Path)!
                    : ResolveSentences.FlagNotTheBodys(Word(node), node.Id, LineageFlags.Member(flag), declared, HeaderFile(node)));
            }

            if (node is not SourceLeaf { Kind: not "call" } && derived.Reproducible(node) is { } reproducible
                && node.Header["reproducible"] is JsonValue written && written.TryGetValue<bool>(out var declaredReproducible)
                && declaredReproducible != reproducible)
            {
                refusals.Add(ResolveSentences.ReproducibleNotTheBodys(Word(node), node.Id, declaredReproducible, HeaderFile(node)));
            }
        }
    }

    private static string Word(SourceNode node) => node switch
    {
        SourceFor => "Loop",
        SourceWhile => "While",
        SourceIf => "If",
        SourceCase => "Case",
        _ => "Call",
    };

    private static string HeaderFile(SourceNode node) => node is SourceLeaf ? node.Path : SourceLayout.Join(node.Path, "index.json");

    /// <summary>One decision: a condition site and what it reads, in the scope its entry is told.</summary>
    private sealed record Decision(ExpressionSite Site, IReadOnlyList<GraphRead> Reads);

    /// <summary>
    /// Every decision: each condition site the expressions checked (not a computed node's expression), each
    /// <c>case</c>'s subject (<c>Case 'x' case</c>), and, through the seam, each macro's or snippet's <c>if</c> (a macro
    /// folder's shared one included) read again at every body that places it; without the seam a placed <c>if</c>'s
    /// reads of <c>node</c> and <c>check</c> name nothing and are not judged. S03-044 for each read of a profile-carrying value; S03-037 once for a
    /// decision that is run-dependent (part 03 § 4.4), naming the run fact it comes from, though it reads it through a value.
    /// </summary>
    private static void Decisions(Derivation derived, PackageExpressions expressions, List<SourceRefusal> refusals, List<SourceRefusal> warnings)
    {
        foreach (var decision in DecisionsOf(derived, expressions))
        {
            foreach (var read in decision.Reads.Where(r => r.Id != null && r.Carries && derived.Graph.ById.TryGetValue(r.Id, out var v)
                         && derived.Flag(v, LineageFlag.ProfileCarrying)).Select(r => r.Path).Distinct(StringComparer.Ordinal))
            {
                refusals.Add(ExpressionSentences.ReadsProfileText(decision.Site, read));
            }

            var run = LineageFlags.OfReads(Derivation.Reads(decision.Reads), derived.Flags)[LineageFlag.RunDependent];
            if (run.Value && Fact(derived, run.Because, decision.Reads) is { } fact)
                warnings.Add(ExpressionSentences.RunDependentDecision(decision.Site, fact));
        }
    }

    private static IEnumerable<Decision> DecisionsOf(Derivation derived, PackageExpressions expressions)
    {
        var tree = derived.Resolution.Tree;
        foreach (var site in expressions.Sites)
        {
            var context = site.Context;
            if (context.Site.Kind == ExpressionSiteKind.Expression || derived.Graph.Of(context.Declaration, context.Function) is not { } vertex)
                continue;
            var frame = (context.Declaration, context.Site.Kind) switch
            {
                (SourceWhile loop, _) => tree.BodyOf(loop),
                (SourceFor loop, ExpressionSiteKind.Until) => tree.BodyOf(loop),
                (SourceNode node, _) => tree.FrameOf(node),
                (SourceDeliverable, _) => tree.PackageRoot,
                _ => null,
            };
            var reader = context.Declaration as SourceNode;
            yield return new Decision(context.Site, site.Check.Reads.Select(p => derived.Graph.Map(p, frame, vertex, reader)).ToList());
        }

        // A macro's or snippet's if is read where it is placed: at each body the seam says places it (a macro in
        // Places, a snippet named by a read). Without the seam nothing says where, and only its run facts are judged.
        var placeable = expressions.Sites
            .Where(s => s.Context.Site.Kind is ExpressionSiteKind.MacroIf or ExpressionSiteKind.SnippetIf)
            .ToLookup(s => s.Context.Declaration, ReferenceEqualityComparer.Instance);
        foreach (var vertex in derived.Graph.Vertices.Where(v => (v.Function == null || v.Union) && v.Body != null))
        {
            if (derived.Graph.FrameOfBody(vertex) is not { } frame)
                continue;
            var reader = vertex.Declaration as SourceNode;
            var placed = vertex.Body!.Places.SelectMany(p => derived.Graph.PlacedBy(p, "macro", vertex.Function))
                .Concat(vertex.Body.Reads.SelectMany(p => derived.Graph.PlacedBy(p, "snippet", vertex.Function)))
                .Distinct(ReferenceEqualityComparer.Instance);
            foreach (var declaration in placed)
            {
                foreach (var site in placeable[declaration!])
                    yield return new Decision(site.Context.Site, site.Check.Reads.Select(p => derived.Graph.Map(p, frame, vertex, reader)).ToList());
            }
        }

        foreach (var vertex in derived.Graph.Vertices.Where(v => v.Function == null || v.Union))
        {
            if (vertex.Declaration is not SourceCase choice)
                continue;
            var suffix = vertex.Function == null ? "" : $" in function '{vertex.Function.Id}'";
            var site = new ExpressionSite(ExpressionSiteKind.FlowIf, $"Case '{choice.Id}'{suffix}", "case", HeaderFile(choice), null, null);
            yield return new Decision(site, [derived.Graph.Map(choice.Case, tree.FrameOf(choice), vertex, choice)]);
        }
    }

    /// <summary>
    /// Through the seam: an agent's prompt (its parts' text included) reading a profile-carrying value: S04-069 when it
    /// is a parameter that carries a profile field (part 04 § 7.7), else S03-044.
    /// </summary>
    private static void Prompts(Derivation derived, List<SourceRefusal> refusals)
    {
        var carried = SourceResolve.Carried(derived.Resolution.Tree,
            derived.Resolution.Calls.Select(c => (c.Key, c.Value.Function)));
        foreach (var vertex in derived.Graph.Vertices.Where(v => v.Function == null || v.Union))
        {
            if (vertex.Declaration is not SourceLeaf { Kind: "agent" } agent || vertex.Body == null)
                continue;
            var said = new HashSet<string>(StringComparer.Ordinal);
            foreach (var read in vertex.Content.Where(r => r.Canonical.Length > 0 && r.Id != null && r.Carries))
            {
                if (!derived.Graph.ById.TryGetValue(read.Id!, out var target) || !derived.Flag(target, LineageFlag.ProfileCarrying)
                    || !said.Add(read.Path))
                {
                    continue;
                }

                if (vertex.Function != null && read.Path.Split('.') is ["param", var name, ..]
                    && carried.TryGetValue((vertex.Function.Id, name), out var field))
                {
                    refusals.Add(ResolveSentences.ReadsProfileParameter(vertex.Function.Id, name, field, "a prompt", agent.Path));
                    continue;
                }

                var suffix = vertex.Function == null ? "" : $" in function '{vertex.Function.Id}'";
                var site = new ExpressionSite(ExpressionSiteKind.NodeIf, $"Node '{agent.Id}'{suffix}", "if", agent.Path, null, null);
                refusals.Add(ExpressionSentences.ReadsProfileText(site, read.Path));
            }
        }
    }

    /// <summary>S03-038 once for each computed node (a check included; each function's in its union scope) that is run-dependent, naming the read that makes it so.</summary>
    private static void Values(Derivation derived, List<SourceRefusal> warnings)
    {
        foreach (var vertex in derived.Graph.Vertices.Where(v => v.Function == null || v.Union))
        {
            if (vertex.Declaration is SourceLeaf { Kind: "computed" } computed
                && derived.Value(vertex, LineageFlag.RunDependent) is { Value: true, Because: { } because })
            {
                warnings.Add(ExpressionSentences.RunDependentValue(computed.Id, because, computed.Path));
            }
        }
    }

    /// <summary>
    /// The run fact a run-dependent read comes from: <paramref name="because"/> itself when it is <c>run.&lt;fact&gt;</c>,
    /// else the fact of the vertex it reads, followed through each vertex's own cause; null when none is found. Visits
    /// each vertex once.
    /// </summary>
    private static string? Fact(Derivation derived, string? because, IReadOnlyList<GraphRead> reads)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (because != null)
        {
            if (because.Split('.') is ["run", var fact, ..])
                return fact;
            var read = reads.FirstOrDefault(r => r.Path == because && r.Id != null);
            if (read == null || !seen.Add(read.Id!) || !derived.Graph.ById.TryGetValue(read.Id!, out var vertex))
                return null;
            because = derived.Value(vertex, LineageFlag.RunDependent).Because;
            reads = vertex.Content;
        }

        return null;
    }
}
