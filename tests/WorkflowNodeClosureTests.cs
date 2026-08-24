using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// The reachability primitive both the validator and the job starter's fire-set
/// prune (#355) are built on. The geometry mirrors DagInterpreterTests' two-result
/// fixture: one shared fan, one node private to the second deliverable — which is
/// exactly the shape that made a skipped deliverable's nodes run anyway.
/// </summary>
public class WorkflowNodeClosureTests
{
    private static WorkflowBindingValue From(string source, string? renderer = null) => new(source, renderer);

    private static readonly List<WorkflowNodeSpec> Nodes = new()
    {
        new("upstream", "Upstream", Prompt: "p",
            Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
            {
                ["draft"] = From("input:consult_draft")
            }),
        new("fan", "Fan", Prompt: "p",
            Bindings: new Dictionary<string, WorkflowBindingValue>(StringComparer.Ordinal)
            {
                ["prior"] = From("node:upstream", "concept-context"),
                ["text"] = From("item:content")
            },
            ForEach: "data:standards"),
        new("extra", "Extra", Prompt: "p"),
        new("note", "Note", Aggregate: new List<string> { "node:fan" }),
        new("letter", "Letter", Aggregate: new List<string> { "node:fan", "node:extra" })
    };

    private static HashSet<string> Closure(IReadOnlyList<WorkflowNodeSpec> nodes, params string[] roots) =>
        WorkflowNodeClosure.Reachable(roots, WorkflowNodeClosure.Edges(nodes));

    private static string[] Sorted(IEnumerable<string> ids) =>
        ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();

    [Fact]
    public void OneRoot_ReachesItsOwnBranchOnly()
    {
        // 'extra' feeds the letter alone, so pruning to the note must drop it —
        // the whole point of the exercise.
        Assert.Equal(new[] { "fan", "note", "upstream" }, Sorted(Closure(Nodes, "note")));
    }

    [Fact]
    public void ASharedSource_IsReachedFromEitherRoot()
    {
        Assert.Contains("fan", Closure(Nodes, "note"));
        Assert.Contains("fan", Closure(Nodes, "letter"));
    }

    [Fact]
    public void EveryRoot_TogetherReachesEveryNode()
    {
        // The identity case the starter's gate relies on: with nothing skipped,
        // pruning would remove nothing.
        Assert.Equal(Sorted(Nodes.Select(node => node.Id)), Sorted(Closure(Nodes, "note", "letter")));
    }

    [Fact]
    public void ARendererOnABinding_DoesNotChangeReachability()
    {
        // 'as' is presentation; the edge is the same edge.
        Assert.Contains("upstream", Closure(Nodes, "note"));
    }

    [Fact]
    public void OnlyNodeSources_AreEdges()
    {
        var edges = WorkflowNodeClosure.Edges(Nodes);

        // 'upstream' binds input: only, so it has no outgoing edge at all, and
        // 'fan' has exactly one despite also carrying item: and forEach.
        Assert.False(edges.ContainsKey("upstream"));
        Assert.Equal(new[] { "upstream" }, edges["fan"].ToArray());
    }

    [Fact]
    public void ABareAggregateSource_IsStillAnEdge()
    {
        // ExpandAggregator and the engine both accept a bare id. If the closure
        // did not, a prune would drop precisely the node they then look up —
        // and both index without a guard.
        var bare = Nodes.Select(node => node.Id == "note"
            ? node with { Aggregate = new List<string> { "fan" } }
            : node).ToList();

        Assert.Contains("fan", Closure(bare, "note"));
    }

    [Fact]
    public void AnEdgeToAnUndeclaredNode_IsDropped()
    {
        // Load-bearing rather than tidy: the validator runs Kahn's algorithm
        // over this same map, and a dependency that is not a node can never be
        // resolved — so a dangling reference would leave its node unresolved
        // and be reported as a cycle it is not part of.
        var dangling = new List<WorkflowNodeSpec>(Nodes)
        {
            new("orphan", "Orphan", Aggregate: new List<string> { "node:nowhere" })
        };

        var edges = WorkflowNodeClosure.Edges(dangling);

        Assert.False(edges.ContainsKey("orphan"));
        Assert.Equal(new[] { "orphan" }, Closure(dangling, "orphan").ToArray());
    }

    [Fact]
    public void ACycle_Terminates()
    {
        // Invalid packages still reach the validator's walk, so the BFS must not
        // hang on one.
        var cyclic = new List<WorkflowNodeSpec>
        {
            new("a", "A", Aggregate: new List<string> { "node:b" }),
            new("b", "B", Aggregate: new List<string> { "node:a" })
        };

        Assert.Equal(new[] { "a", "b" }, Sorted(Closure(cyclic, "a")));
    }
}
