namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1502 (specVersion 31, area 05, D11): the engine's numbers that part 04 § 10.2's notes are measured against, which are
/// not in the format (part 04 § 8): <paramref name="Ceiling"/>, the agent calls one job may have in flight;
/// <paramref name="ItemsPerLoop"/>, the items one loop instance may be given; <paramref name="CallsPerJob"/>, the agent
/// calls one job may make.
/// </summary>
public sealed record PublishLimits(int Ceiling, int ItemsPerLoop, int CallsPerJob);

/// <summary>
/// #1502 (specVersion 31, area 05): part 04 § 10.2's notes at publish (§ 8; D11): never refusals, written among
/// <c>resolve.json</c>'s warnings. Every agent leaf is reached along its nesting path of loops, whiles, branches and calls
/// (a call's function body is walked where the call is, at any depth). Only agent calls count; checks and check loops
/// count as production does.
/// <list type="bullet">
/// <item>S04-072: the largest product of the caps along a path passes the ceiling. A loop with <c>parallel: false</c>, a
/// while and a branch cap at 1; a parallel loop at its <c>maxParallel</c>, or, with none, at its list's length when it is
/// known at publish (a snippet folder; the smaller of the two when both are), else it is unbounded.</item>
/// <item>S04-073, instead, for each unbounded loop with an agent beneath it, in the order found.</item>
/// <item>S04-079: a loop whose list could hold more than the items limit: unless its length is known at publish and
/// within it (a snippet folder). The formats dialect has no <c>maxItems</c> (S02-021), so no other list's length is
/// known. A loop over a loop's <c>.items</c> has at most what the production loop ran, which the limit already
/// bounds.</item>
/// <item>S04-080: the agent calls of the worst case pass the calls limit: each agent counted once for every run of the
/// bodies around it (a loop's items: its known length, the items limit, and its <c>max</c>, whichever is least; a
/// while's <c>max</c> passes; the larger branch of an <c>if</c> or a <c>case</c>).</item>
/// </list>
/// Never throws on package content: the call graph is walked with the functions on the path set aside, and the
/// counts saturate.
/// </summary>
public static class PublishNotes
{
    /// <summary>
    /// The notes of <paramref name="package"/>, resolved as <paramref name="resolution"/> says, against
    /// <paramref name="limits"/>: S04-072 or S04-073, then each S04-079 in source order, then S04-080. A package-wide
    /// note's path is <c>""</c>; S04-079's is the loop's <c>index.json</c>. A null argument is the caller's error.
    /// </summary>
    public static IReadOnlyList<SourceRefusal> Of(SourcePackage package, Resolution resolution, PublishLimits limits)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(limits);

        var walk = new Walk(resolution, limits);
        walk.Body(package.Nodes.Nodes);
        walk.Body(package.Checks.Nodes);
        var total = Add(walk.Total(package.Nodes.Nodes), walk.Total(package.Checks.Nodes));

        var notes = new List<SourceRefusal>();
        if (walk.Unbounded.Count > 0)
            notes.AddRange(walk.Unbounded.Select(loop => ResolveSentences.CallsAtOnceUnbounded(loop.Id, limits.Ceiling)));
        else if (walk.AtOnce > limits.Ceiling)
            notes.Add(ResolveSentences.CallsAtOnce(walk.AtOnce, limits.Ceiling));

        foreach (var loop in resolution.Tree.All().OfType<SourceFor>())
        {
            if (walk.CouldPassLimit(loop))
                notes.Add(ResolveSentences.ItemsPastLimit(loop.Id, loop.In, limits.ItemsPerLoop, SourceLayout.Join(loop.Path, "index.json")));
        }

        if (total > limits.CallsPerJob)
            notes.Add(ResolveSentences.CallsPastLimit(total, limits.CallsPerJob));
        return notes;
    }

    /// <summary>
    /// #1507 (area 10, Task 15; part 10 § 3.2, D14): the agent calls of <paramref name="package"/>'s worst case, as S04-080
    /// counts them against <paramref name="limits"/> (each agent once for every run of the bodies around it, saturating).
    /// </summary>
    internal static long WorstCaseCalls(SourcePackage package, Resolution resolution, PublishLimits limits)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(limits);
        var walk = new Walk(resolution, limits);
        return Add(walk.Total(package.Nodes.Nodes), walk.Total(package.Checks.Nodes));
    }

    private static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;

    private static long Times(long a, long b) => a != 0 && b > long.MaxValue / a ? long.MaxValue : a * b;

    /// <summary>
    /// One walk of the nesting paths: the largest product of caps, the unbounded loops, and each body's calls. Each
    /// function is worked out once (<see cref="Facts"/>) and applied at every call of it, so the walk is linear in the
    /// package however wide the calls fan out; a function already on the path (a ring, refused before) adds nothing.
    /// </summary>
    private sealed class Walk(Resolution resolution, PublishLimits limits)
    {
        private readonly HashSet<SourceFunction> path = new(ReferenceEqualityComparer.Instance);

        private readonly Dictionary<SourceFunction, Facts> facts = new(ReferenceEqualityComparer.Instance);

        private readonly HashSet<SourceFor> bounding = new(ReferenceEqualityComparer.Instance);

        private readonly Scan top = new();

        public long AtOnce => top.AtOnce;

        public List<SourceFor> Unbounded => top.Unbounded;

        private ScopeTree Tree => resolution.Tree;

        /// <summary>The caps along every path through <paramref name="nodes"/> at the top of the package.</summary>
        public void Body(IReadOnlyList<SourceNode> nodes) => Body(nodes, 1, null, top);

        /// <summary>
        /// The caps along every path through <paramref name="nodes"/>, at <paramref name="caps"/> so far, into
        /// <paramref name="scan"/>; <paramref name="open"/> are the unbounded loops around them.
        /// </summary>
        private void Body(IReadOnlyList<SourceNode> nodes, long caps, List<SourceFor>? open, Scan scan)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case SourceLeaf { Kind: "agent" }:
                        scan.Agent(caps, open ?? []);
                        break;
                    case SourceLeaf { Kind: "call" } call:
                        if (Function(call) is { } function && Of(function) is { Agent: true } inside)
                        {
                            scan.Agent(Times(caps, inside.Caps), open ?? []);
                            scan.Agent(Times(caps, inside.Caps), inside.Unbounded);
                        }

                        break;
                    case SourceFor loop:
                        var cap = Cap(loop);
                        var inner = open;
                        if (cap == null)
                            inner = [.. open ?? [], loop];
                        Body(loop.Body.Nodes, Times(caps, cap ?? 1), inner, scan);
                        break;
                    default:
                        foreach (var block in ScopeTree.Blocks(node))
                            Body(block.Nodes, caps, open, scan);
                        break;
                }
            }
        }

        /// <summary>The agent calls one run of <paramref name="nodes"/> could make.</summary>
        public long Total(IReadOnlyList<SourceNode> nodes)
        {
            long total = 0;
            foreach (var node in nodes)
            {
                total = Add(total, node switch
                {
                    SourceLeaf { Kind: "agent" } => 1,
                    SourceLeaf { Kind: "call" } call => Function(call) is { } function && Of(function) is { } inside ? inside.Total : 0,
                    SourceFor loop => Times(Bound(loop), Total(loop.Body.Nodes)),
                    SourceWhile loop => Times(Math.Max(loop.Max, 0), Total(loop.Body.Nodes)),
                    SourceIf or SourceCase => ScopeTree.Blocks(node).Select(b => Total(b.Nodes)).DefaultIfEmpty(0).Max(),
                    _ => 0,
                });
            }

            return total;
        }

        /// <summary><paramref name="function"/>'s facts, worked out the first time it is asked for; null while it is on the path.</summary>
        private Facts? Of(SourceFunction function)
        {
            if (facts.TryGetValue(function, out var known))
                return known;
            if (!path.Add(function))
                return null;
            var scan = new Scan();
            Body(function.Body.Nodes, 1, null, scan);
            var total = Total(function.Body.Nodes);
            path.Remove(function);
            return facts[function] = new Facts(scan.Holds, scan.AtOnce, scan.Unbounded, total);
        }

        /// <summary>S04-079's rule: the loop's list could hold more than the limit.</summary>
        public bool CouldPassLimit(SourceFor loop)
        {
            var source = Tree.Source(loop);
            if (source.OverItems || source.Element == null)
                return false;
            return source.KnownLength is not { } length || length > limits.ItemsPerLoop;
        }

        /// <summary>How many items a loop runs at most: its known length, the items limit and its max, whichever is least.</summary>
        private long Bound(SourceFor loop)
        {
            var source = Tree.Source(loop);
            long bound = limits.ItemsPerLoop;
            if (source.OverItems && source.Production is { } production && bounding.Add(loop))
            {
                bound = Math.Min(bound, Bound(production));
                bounding.Remove(loop);
            }

            if (source.KnownLength is { } length)
                bound = Math.Min(bound, length);
            if (loop.Max is { } most)
                bound = Math.Min(bound, most);
            return Math.Max(bound, 0);
        }

        /// <summary>A parallel loop's cap: its maxParallel, or its known length, the smaller when both; null when unbounded. 1 when sequential.</summary>
        private long? Cap(SourceFor loop)
        {
            if (!loop.Parallel)
                return 1;
            var known = Tree.Source(loop).KnownLength;
            return (loop.MaxParallel, known) switch
            {
                ({ } max, { } length) => Math.Min(max, length),
                ({ } max, null) => max,
                (null, { } length) => length,
                _ => null,
            };
        }

        private SourceFunction? Function(SourceLeaf call) =>
            resolution.Calls.TryGetValue(call, out var binding) ? binding.Function : null;
    }

    /// <summary>
    /// What one function holds, its body walked from a product of 1: whether an agent is beneath it; the largest
    /// product of caps to one; its unbounded loops with an agent beneath them, in the order found; its agent calls.
    /// </summary>
    private sealed record Facts(bool Agent, long Caps, IReadOnlyList<SourceFor> Unbounded, long Total);

    /// <summary>What one walk of a body has found: whether it reached an agent, the largest product of caps, the unbounded loops.</summary>
    private sealed class Scan
    {
        private readonly HashSet<SourceFor> seen = new(ReferenceEqualityComparer.Instance);

        public bool Holds { get; private set; }

        public long AtOnce { get; private set; }

        public List<SourceFor> Unbounded { get; } = [];

        /// <summary>An agent reached at <paramref name="caps"/>, beneath <paramref name="open"/>.</summary>
        public void Agent(long caps, IEnumerable<SourceFor> open)
        {
            Holds = true;
            AtOnce = Math.Max(AtOnce, caps);
            foreach (var loop in open)
            {
                if (seen.Add(loop))
                    Unbounded.Add(loop);
            }
        }
    }
}
