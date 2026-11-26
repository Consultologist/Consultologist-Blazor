using System.Text.Json;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance.V31;

namespace Consultologist.Web.Tests.V31;

/// <summary>Task 13 (#1509; part 08 §§ 1.2, 4 to 7, 10): the rows of a storage 12 job read into its sections.</summary>
public class Record31Tests
{
    private static readonly Lazy<Record31> Job = new(() =>
    {
        var job = Record31Fixtures.Job();
        return Record31.From(job.Header, job.Rows);
    });

    private static Record31 Read() => Job.Value;

    private static RecordRowRef Raw(string key, string section, string json) =>
        new(key, section, JsonDocument.Parse(json).RootElement.Clone());

    [Fact]
    public void RowsAreSortedIntoSections_ByTheirSection()
    {
        var record = Read();

        Assert.All(record.Nodes, row => Assert.Equal("node", row.Section));
        Assert.All(record.Checks, row => Assert.Equal("check", row.Section));
        Assert.All(record.Deliverables, row => Assert.Equal("deliverable", row.Section));
        Assert.All(record.Decisions, row => Assert.Equal("decision", row.Section));
        Assert.Equal(5, record.Deliverables.Count);
        Assert.Equal(8, record.Checks.Count);
        Assert.Equal(
            ["node.addendum", "node.billing", "node.hpi_section", "node.hpi_section.standard_draft"],
            record.Nodes.Select(n => n.Key).Take(4));
        Assert.Equal("job-1", record.Header.JobId);
    }

    [Fact]
    public void Nodes_AreInKeyOrderAsAPersonCounts()
    {
        var keys = Read().Nodes.Select(n => n.Key).ToList();

        Assert.True(keys.IndexOf("node.sections.draft:2") < keys.IndexOf("node.sections.draft:10"));
        Assert.True(keys.IndexOf("node.refine.judge#2") < keys.IndexOf("node.refine.judge#10"));
        Assert.True(keys.IndexOf("node.refine") < keys.IndexOf("node.refine#1"));
    }

    [Fact]
    public void ARowIsReadByItsMembers_AsStored()
    {
        var row = Read().Find("node.sections:2")!;

        Assert.Equal("NotRun", row.Text("status"));
        Assert.Equal("not run: beyond max 1", row.Text("stateReason"));
        Assert.Equal("node.sections", row.Text("nodeId"));
        Assert.Equal("2", row.Text("itemId"));

        var loop = Read().Find("node.sections")!;
        Assert.Equal(2, loop.Int("count"));
        Assert.Equal("bound", loop.Text("stopped"));

        Assert.Equal(false, Read().Find("node.refine")!.Flag("checkFirst"));
        Assert.Equal(2, Read().Find("node.plan")!.List("renders").Count);
        Assert.Equal("consult-prose", Read().Find("node.plan")!.Object("agent")!.Value.GetProperty("name").GetString());
    }

    [Fact]
    public void FindAnswersAnyRowByKey_AndNullForNoSuchRow()
    {
        var record = Read();

        Assert.Equal("deliverable", record.Find("deliverable.refused")!.Section);
        Assert.Equal("delivery", record.Find("delivery:deliverable.emr.note/email")!.Section);
        Assert.Equal("decision", record.Find("node.route/if:case")!.Section);
        Assert.Null(record.Find("node.nope"));
        Assert.Null(record.Find(null!));
    }

    [Fact]
    public void ACaseDecision_HoldsItsArmAsStoredText()
    {
        var decision = Read().DecisionsOwnedBy("node.route").Single();

        Assert.Equal("2", decision.Text("result"));
        Assert.Equal("case", decision.Text("site"));
    }

    [Fact]
    public void ADecisionWithNoOwnerMember_IsOwnedByTheKeyBeforeItsIfSite()
    {
        var record = Record31.From(Record31Fixtures.Header(),
        [
            Raw("node.old/if:self", "decision", """{ "id": "node.old/if:self", "result": true }"""),
            Raw("node.old:2/if:pass.1", "decision", """{ "id": "node.old:2/if:pass.1", "owner": null }"""),
        ]);

        Assert.Equal(["node.old/if:self"], record.DecisionsOwnedBy("node.old").Select(d => d.Key));
        Assert.Equal(["node.old:2/if:pass.1"], record.DecisionsOwnedBy("node.old:2").Select(d => d.Key));
    }

    [Fact]
    public void ABooleanDecision_HoldsItsResultAsAFlag()
    {
        var decision = Read().DecisionsOwnedBy("node.addendum").Single();

        Assert.Equal(false, decision.Flag("result"));
        Assert.Null(decision.Text("result"));
    }

    [Fact]
    public void DecisionsOwnedBy_FindsThoseOfANodeWithNoRow()
    {
        var record = Read();

        Assert.Null(record.Find("node.never_ran"));
        Assert.Equal(
            ["node.never_ran/if:part.intro", "node.never_ran/if:self"],
            record.DecisionsOwnedBy("node.never_ran").Select(d => d.Key));
        Assert.Empty(record.DecisionsOwnedBy("node.plan:1"));
    }

    [Fact]
    public void Decisions_AreInOrdinalOrderOfTheirId()
    {
        var ids = Read().Decisions.Select(d => d.Key).ToList();

        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
    }

    [Fact]
    public void DeliveriesOf_AreInKeyOrder()
    {
        var record = Read();

        Assert.Equal(
            ["delivery:deliverable.emr.note/email", "delivery:deliverable.emr.note/emr", "delivery:deliverable.emr.note/pdf"],
            record.DeliveriesOf("deliverable.emr.note").Select(d => d.Key));
        Assert.Equal(["sent", "not sent", "failed"], record.DeliveriesOf("deliverable.emr.note").Select(d => d.Text("result")));
        Assert.Empty(record.DeliveriesOf("deliverable.refused"));
        Assert.Empty(record.DeliveriesOf("deliverable.nope"));
    }

    [Fact]
    public void DeliveriesAreGroupedByTheDeliverableMember_NotByAKeyPrefix()
    {
        var record = Record31.From(Record31Fixtures.Header(),
        [
            Raw("delivery:deliverable.a/email", "delivery", """{ "key": "delivery:deliverable.a/email", "deliverable": "deliverable.a", "result": "sent" }"""),
            Raw("delivery:deliverable.a.b/email", "delivery", """{ "key": "delivery:deliverable.a.b/email", "deliverable": "deliverable.a.b", "result": "sent" }"""),
        ]);

        Assert.Equal(["delivery:deliverable.a/email"], record.DeliveriesOf("deliverable.a").Select(d => d.Key));
    }

    [Fact]
    public void AParsedRow_ToleratesMissingAndUnknownMembers()
    {
        var record = Record31.From(Record31Fixtures.Header(),
        [
            // members missing, null where an array is expected, a member the Web does not know, wrong kinds
            Raw("node.old", "node", """{ "status": "Completed", "renders": null, "macros": "none", "units": 4, "futureMember": { "x": [1] }, "tokens": [], "itemId": 7, "agent": "consult-prose", "invalid": null }"""),
            Raw("node.scalar", "node", "7"),
            Raw("node.nothing", "node", "null"),
            Raw("check.empty", "check", "{}"),
            Raw("", "node", "{}"),
            Raw("node.weird/render:1", "node", """{ "status": "Failed" }"""),
            Raw("deliverable.x", "deliverable", """{ "state": "released", "delivery": null }"""),
            Raw("delivery:deliverable.x/email", "delivery", "[]"),
            Raw("mystery.row", "future-section", "{}"),
            new RecordRowRef(null!, null!, default),
        ]);

        var old = record.Find("node.old")!;
        Assert.Equal("Completed", old.Text("status"));
        Assert.Empty(old.List("renders"));
        Assert.Empty(old.List("macros"));
        Assert.Empty(old.List("units"));
        Assert.Empty(old.List("absent"));
        Assert.Null(old.Text("absent"));
        Assert.Null(old.Flag("absent"));
        Assert.Null(old.Int("absent"));
        Assert.Null(old.Object("absent"));
        Assert.Null(old.Object("tokens"));
        Assert.Null(old.Object("agent"));
        Assert.Null(old.Object("invalid"));
        Assert.Null(old.Text("itemId"));
        Assert.Null(old.Int("status"));
        Assert.Null(old.Flag("status"));
        Assert.Equal(1, old.Object("futureMember")!.Value.GetProperty("x").GetArrayLength());

        foreach (var key in new[] { "node.scalar", "node.nothing", "check.empty", "delivery:deliverable.x/email" })
        {
            var row = record.Find(key)!;
            Assert.Null(row.Text("status"));
            Assert.Null(row.Flag("x"));
            Assert.Null(row.Int("x"));
            Assert.Null(row.Object("x"));
            Assert.Empty(row.List("x"));
        }

        Assert.Equal("Failed", record.Find("node.weird/render:1")!.Text("status"));
        // a delivery row with no `deliverable` member is still found, by the deliverable its key names
        Assert.Equal(["delivery:deliverable.x/email"], record.DeliveriesOf("deliverable.x").Select(d => d.Key));
        Assert.Empty(record.Deliverables.Single().List("delivery"));
        Assert.DoesNotContain(record.Nodes, n => n.Section != "node");
        Assert.Equal(5, record.Nodes.Count);
        Assert.Single(record.Checks);
        Assert.Null(record.Find("mystery.row"));
    }

    [Fact]
    public void AThousandRows_AreReadInOnePass()
    {
        var rows = Enumerable.Range(1, 1000)
            .Select(i => Raw($"node.sections.draft:{i}", "node", $$"""{ "status": "Completed", "itemId": "{{i}}" }"""))
            .Reverse()
            .ToList();

        var record = Record31.From(Record31Fixtures.Header(), rows);

        Assert.Equal(1000, record.Nodes.Count);
        Assert.Equal("node.sections.draft:1", record.Nodes[0].Key);
        Assert.Equal("node.sections.draft:1000", record.Nodes[^1].Key);
        Assert.Equal("500", record.Find("node.sections.draft:500")!.Text("itemId"));
    }

    private static string? ParentKey(Record31 record, string key) => record.ParentOf(key)?.Key;

    [Theory]
    [InlineData("node.refine.judge#2", "node.refine#2")]
    [InlineData("node.refine.sections#2:3", "node.refine.sections#2")]
    [InlineData("node.refine.sections#2", "node.refine#2")]
    [InlineData("node.refine#2", "node.refine")]
    [InlineData("node.sections:2", "node.sections")]
    [InlineData("node.sections.draft:2", "node.sections:2")]
    [InlineData("node.sections.paragraphs:1", "node.sections:1")]
    [InlineData("node.sections.paragraphs:1/1", "node.sections.paragraphs:1")]
    [InlineData("node.sections.paragraphs.draft_paragraph:1/1", "node.sections.paragraphs:1/1")]
    [InlineData("node.hpi_section.standard_draft", "node.hpi_section")]
    [InlineData("check.part_coverage:2", "check.part_coverage")]
    [InlineData("check.part_coverage.coverage:2", "check.part_coverage:2")]
    [InlineData("node.refine", null)]
    [InlineData("node.plan", null)]
    [InlineData("check.gate", null)]
    [InlineData("deliverable.emr.note", null)]
    [InlineData("node.nope", null)]
    [InlineData("node.plan/render:1", null)]
    public void ParentOf_IsTheLoopPassOrCallRowTheRowRunsIn(string key, string? parent)
    {
        Assert.Equal(parent, ParentKey(Read(), key));
    }

    [Fact]
    public void AnItemRowWhoseLoopRowIsMissing_FallsToTheEnclosingPass()
    {
        var record = Record31.From(Record31Fixtures.Header(),
        [
            Raw("node.refine#2", "node", """{ "kind": "while", "status": "Completed" }"""),
            Raw("node.refine", "node", """{ "kind": "while", "status": "Completed" }"""),
            Raw("node.refine.sections#2:3", "node", """{ "kind": "for", "status": "Completed" }"""),
        ]);

        Assert.Equal("node.refine#2", ParentKey(record, "node.refine.sections#2:3"));
    }

    [Fact]
    public void AMissingParentRow_ResolvesToTheNearestAncestorPresent_OrNull()
    {
        var record = Record31.From(Record31Fixtures.Header(),
        [
            Raw("node.a", "node", """{ "kind": "for" }"""),
            Raw("node.a.b.c:1", "node", """{ "kind": "agent" }"""),
            Raw("node.x.y.z", "node", """{ "kind": "agent" }"""),
        ]);

        Assert.Equal("node.a", ParentKey(record, "node.a.b.c:1"));
        Assert.Null(ParentKey(record, "node.x.y.z"));
    }

    [Fact]
    public void AnAgentRowIsNeverTakenForAnItemRowOfItsOwnEntry()
    {
        var record = Record31.From(Record31Fixtures.Header(),
        [
            Raw("node.draft", "node", """{ "kind": "agent" }"""),
            Raw("node.draft:1", "node", """{ "kind": "agent" }"""),
        ]);

        Assert.Null(ParentKey(record, "node.draft:1"));
    }

    [Fact]
    public void AnUnparseableKey_HasNoParent()
    {
        Assert.Null(Read().ParentOf("plan"));
        Assert.Null(Read().ParentOf(null!));
        Assert.Null(Read().ParentOf(""));
    }

    [Fact]
    public void ChildrenOf_AreTheRowsWhoseParentItIs_InKeyOrder()
    {
        var record = Read();

        Assert.Equal(["node.refine#1", "node.refine#2", "node.refine.judge#10"], record.ChildrenOf("node.refine").Select(r => r.Key));
        Assert.Equal(["node.refine.judge#2", "node.refine.sections#2"], record.ChildrenOf("node.refine#2").Select(r => r.Key));
        Assert.Equal(["node.sections:1", "node.sections:2", "node.sections.draft:10"], record.ChildrenOf("node.sections").Select(r => r.Key));
        Assert.Equal(["node.sections.draft:1", "node.sections.paragraphs:1"], record.ChildrenOf("node.sections:1").Select(r => r.Key));
        Assert.Equal(["node.hpi_section.standard_draft"], record.ChildrenOf("node.hpi_section").Select(r => r.Key));
        Assert.Equal(["check.part_coverage:1", "check.part_coverage:2"], record.ChildrenOf("check.part_coverage").Select(r => r.Key));
        Assert.Empty(record.ChildrenOf("node.plan"));
        Assert.Empty(record.ChildrenOf("node.nope"));
    }

    [Fact]
    public void EveryChild_IsUnderExactlyOneParent_AndTopLevelRowsAreNoOnesChild()
    {
        var record = Read();
        var rows = record.Nodes.Concat(record.Checks).ToList();
        var children = rows.SelectMany(r => record.ChildrenOf(r.Key)).ToList();

        Assert.Equal(children.Count, children.Select(c => c.Key).Distinct().Count());
        Assert.Equal(rows.Count(r => record.ParentOf(r.Key) != null), children.Count);
        Assert.DoesNotContain(children, c => c.Key is "node.plan" or "node.refine" or "node.sections" or "node.hpi_section");
    }
}
