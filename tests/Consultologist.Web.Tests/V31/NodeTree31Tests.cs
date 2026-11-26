using System.Text.Json;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>Task 15 (#1509; part 09 § 6.4): a job's rows as the tree History shows.</summary>
public class NodeTree31Tests
{
    private static Record31 Read()
    {
        var job = Record31Fixtures.Job();
        return Record31.From(job.Header, job.Rows);
    }

    private static RecordRowRef Raw(string key, string section, string json) =>
        new(key, section, JsonDocument.Parse(json).RootElement.Clone());

    private static Row31 RowOf(string key, string json) => new(key, "node", JsonDocument.Parse(json).RootElement.Clone());

    private static Record31 Of(params RecordRowRef[] rows) => Record31.From(Record31Fixtures.Header(), rows);

    private static TreeNode31 Node(IReadOnlyList<TreeNode31> nodes, string key) =>
        nodes.Select(n => Find(n, key)).First(n => n is not null)!;

    private static TreeNode31? Find(TreeNode31 node, string key) =>
        node.Key == key ? node : node.Children.Select(c => Find(c, key)).FirstOrDefault(c => c is not null);

    private static string[] Keys(TreeNode31 node) => node.Children.Select(c => c.Key).ToArray();

    [Fact]
    public void ALoop_ExpandsPerItem_InNumericOrder()
    {
        var record = Of(
            Raw("node.loop", "node", """{ "kind": "for", "status": "Completed", "label": "Parts" }"""),
            Raw("node.loop:10", "node", """{ "kind": "for", "status": "Completed" }"""),
            Raw("node.loop:9", "node", """{ "kind": "for", "status": "Completed" }"""),
            Raw("node.loop:2", "node", """{ "kind": "for", "status": "Completed" }"""),
            Raw("node.loop.draft:10", "node", """{ "kind": "agent", "status": "Completed" }"""));

        var loop = Node(NodeTree31.Build(record), "node.loop");

        Assert.Equal(["node.loop:2", "node.loop:9", "node.loop:10"], Keys(loop));
        Assert.Equal(["node.loop.draft:10"], Keys(loop.Children[2]));
    }

    [Fact]
    public void AWhile_ExpandsPerPass()
    {
        var refine = Node(NodeTree31.Build(Read()), "node.refine");

        // the fixture has a judge row for pass 10 but no pass-10 row: it sits on the nearest row present, the while
        Assert.Equal(["node.refine#1", "node.refine#2", "node.refine.judge#10"], Keys(refine));
        Assert.Equal("Pass 2", refine.Children[1].Title);
        Assert.Equal(["node.refine.judge#1"], Keys(refine.Children[0]));
        Assert.Equal(["node.refine.judge#2", "node.refine.sections#2"], Keys(refine.Children[1]));
        Assert.Equal(["node.refine.sections#2:3"], Keys(refine.Children[1].Children[1]));
    }

    [Fact]
    public void ACall_ExpandsToItsOwnRows()
    {
        var call = Node(NodeTree31.Build(Read()), "node.hpi_section");

        Assert.Equal("call", call.Kind);
        Assert.Equal(["node.hpi_section.standard_draft"], Keys(call));
    }

    [Fact]
    public void ANestedLoopsItems_AreUnderTheirOuterItem()
    {
        var nodes = NodeTree31.Build(Read());
        var outer = Node(nodes, "node.sections");

        // draft:10 has no item-10 row in the fixture: it sits on the loop
        Assert.Equal(["node.sections:1", "node.sections:2", "node.sections.draft:10"], Keys(outer));
        var item1 = outer.Children[0];
        Assert.Equal(["node.sections.draft:1", "node.sections.paragraphs:1"], Keys(item1));
        var inner = Node(nodes, "node.sections.paragraphs:1");
        Assert.Equal(["node.sections.paragraphs:1/1"], Keys(inner));
        Assert.Equal(["node.sections.paragraphs.draft_paragraph:1/1"], Keys(inner.Children[0]));
        Assert.DoesNotContain(nodes, n => n.Key.StartsWith("node.sections."));
    }

    [Fact]
    public void ADecision_SitsOnItsOwner()
    {
        var nodes = NodeTree31.Build(Read());

        Assert.Equal(["node.plan/if:macro.follow_up_text", "node.plan/if:macro.letrozole_text"],
            Node(nodes, "node.plan").Decisions.Select(d => d.Key).Order(StringComparer.Ordinal));
        Assert.Single(Node(nodes, "node.addendum").Decisions);
        Assert.Equal("Skipped", Node(nodes, "node.addendum").Status);
        Assert.Equal(2, Node(nodes, "node.refine").Decisions.Count);
    }

    [Fact]
    public void ADecisionsOwnerWithNoRow_IsShownNotRun()
    {
        var nodes = NodeTree31.Build(Read());
        var ghost = Node(nodes, "node.never_ran");

        Assert.Null(ghost.Row);
        Assert.Equal("NotRun", ghost.Status);
        Assert.Equal("never_ran", ghost.Title);
        Assert.Equal(2, ghost.Decisions.Count);
        Assert.Contains(nodes, n => n.Key == "node.never_ran");
    }

    [Fact]
    public void ADecisionsOwnerWithNoRow_InsideAnItem_IsPlacedUnderThatItemAmongItsSiblings()
    {
        var record = Of(
            Raw("node.sections", "node", """{ "kind": "for", "status": "Completed" }"""),
            Raw("node.sections:3", "node", """{ "kind": "for", "status": "Completed" }"""),
            Raw("node.sections.draft:3", "node", """{ "kind": "agent", "status": "Completed" }"""),
            Raw("node.sections.zzz:3", "node", """{ "kind": "agent", "status": "Completed" }"""),
            Raw("node.sections.review:3/if:self", "decision", """{ "owner": "node.sections.review:3", "site": "self", "result": false }"""));

        var item = Node(NodeTree31.Build(record), "node.sections:3");

        Assert.Equal(["node.sections.draft:3", "node.sections.review:3", "node.sections.zzz:3"], Keys(item));
        Assert.Null(item.Children[1].Row);
        Assert.Equal("review", item.Children[1].Title);
    }

    [Fact]
    public void AnItemThatDidNotRun_KeepsItsStatusAndReason()
    {
        var item = Node(NodeTree31.Build(Read()), "node.sections:2");

        Assert.Equal("NotRun", item.Status);
        Assert.Equal("not run: beyond max 1", item.Row!.Text("stateReason"));
        Assert.Equal("Sections 2", item.Title);
        Assert.True(item.IsItem);
        Assert.Equal("Sections", item.LoopLabel);
    }

    [Fact]
    public void TopLevelRows_AreInTheOrderTheyStarted_NeverStartedAfter_TiesByKey()
    {
        var record = Of(
            Raw("node.a", "node", """{ "kind": "agent", "status": "NotRun" }"""),
            Raw("node.b", "node", """{ "kind": "agent", "status": "Completed", "startedAtUtc": "2026-11-16T14:00:05Z" }"""),
            Raw("node.c", "node", """{ "kind": "agent", "status": "Completed", "startedAtUtc": "2026-11-16T14:00:01Z" }"""),
            Raw("node.d", "node", """{ "kind": "agent", "status": "Completed", "startedAtUtc": "2026-11-16T14:00:05Z" }"""),
            Raw("node.e/if:self", "decision", """{ "owner": "node.e", "site": "self", "result": false }"""));

        Assert.Equal(["node.c", "node.b", "node.d", "node.a", "node.e"], NodeTree31.Build(record).Select(n => n.Key));
    }

    [Fact]
    public void ItemTitle_UsesTheRowsNameWhenItHoldsOne_ThenTheRetainedName_ThenPosition()
    {
        var kept = RowOf("node.sections:3", """{ "label": "Sections", "itemName": "Assessment and plan", "itemNameHash": null }""");
        var dropped = RowOf("node.sections:3", """{ "label": "Sections", "itemName": null, "itemNameHash": "sha256:aa" }""");

        Assert.Equal("Assessment and plan", NodeTree31.ItemTitle(kept, "Retained"));
        Assert.Equal("Retained", NodeTree31.ItemTitle(dropped, "Retained"));
        Assert.Equal("Sections 3", NodeTree31.ItemTitle(dropped, null));
    }

    [Fact]
    public void PositionsAreOneBased()
    {
        var first = RowOf("node.sections:1", """{ "label": "Sections" }""");
        var nested = RowOf("node.sections.paragraphs:2/7", """{ "label": "Paragraphs" }""");

        Assert.Equal("Sections 1", NodeTree31.ItemTitle(first, null));
        Assert.Equal("Paragraphs 7", NodeTree31.ItemTitle(nested, null));
    }

    [Fact]
    public void CheckLoops_ExpandPerItem_AndCarryAlignedWith()
    {
        var checks = NodeTree31.BuildChecks(Read());
        var loop = Node(checks, "check.part_coverage");

        Assert.Equal(["check.part_coverage:1", "check.part_coverage:2"], Keys(loop));
        Assert.Equal("node.sections:2", loop.Children[1].Row!.Text("alignedWith"));
        Assert.Equal(["check.part_coverage.coverage:2"], Keys(loop.Children[1]));
        Assert.Equal("node.sections:2", loop.Children[1].Children[0].Row!.Text("alignedWith"));
        Assert.DoesNotContain(checks, c => c.Key.StartsWith("node."));
        Assert.Contains(checks, c => c.Key == "check.gate");
    }

    [Fact]
    public void ARowWithNoLabel_IsTitledByItsLastScope_AndItsItemByThatScopeAndPosition()
    {
        var record = Of(
            Raw("node.loop", "node", """{ "kind": "for", "status": "Completed" }"""),
            Raw("node.loop:4", "node", """{ "kind": "for", "status": "Completed" }"""));

        var loop = Node(NodeTree31.Build(record), "node.loop");

        Assert.Equal("loop", loop.Title);
        Assert.Equal("loop 4", loop.Children[0].Title);
    }

    [Fact]
    public void EveryRowIsInTheTreeExactlyOnce()
    {
        var record = Read();

        static int Count(IReadOnlyList<TreeNode31> nodes) => nodes.Sum(n => 1 + Count(n.Children));
        var nodeGhosts = record.Decisions.Select(Record31.OwnerOf).Where(o => o.StartsWith("node.") && record.Find(o) is null).Distinct().Count();
        var checkGhosts = record.Decisions.Select(Record31.OwnerOf).Where(o => o.StartsWith("check.") && record.Find(o) is null).Distinct().Count();

        Assert.Equal(record.Nodes.Count + nodeGhosts, Count(NodeTree31.Build(record)));
        Assert.Equal(record.Checks.Count + checkGhosts, Count(NodeTree31.BuildChecks(record)));
        Assert.True(nodeGhosts > 0);
    }

    [Fact]
    public void ARowWithNoStatus_IsShownAsStored_NotAsNotRun()
    {
        var record = Of(Raw("node.a", "node", """{ "kind": "agent" }"""));

        Assert.Equal("", Node(NodeTree31.Build(record), "node.a").Status);
    }

    [Fact]
    public void AnItemCarriesWhatRetitlingNeeds()
    {
        var nodes = NodeTree31.Build(Read());
        var item = Node(nodes, "node.sections.paragraphs:1/1");

        Assert.True(item.IsItem);
        Assert.Equal("Paragraphs", item.LoopLabel);
        Assert.Equal("Paragraphs 1", item.Title);
        Assert.Equal("Intro", NodeTree31.ItemTitle(item.Row!, "Intro", item.LoopLabel));
        Assert.False(Node(nodes, "node.sections").IsItem);
        Assert.False(Node(nodes, "node.refine#2").IsItem);
        Assert.False(Node(nodes, "node.never_ran").IsItem);
    }
}
