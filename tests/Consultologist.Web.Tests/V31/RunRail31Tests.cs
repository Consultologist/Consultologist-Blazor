using Consultologist.Web.Services.Provenance.V31;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 12 (#1509, D15): the Create page's rail for a 31 job. Its blocks are the package's <c>Blocks</c> (the top-level node
/// keys); each takes the stored status of its own row from <c>row-finished</c>, and its item and pass rows appear under it as
/// they finish, labelled by the path or pass the key writes. Statuses are the stored words (one vocabulary).
/// </summary>
public class RunRail31Tests
{
    private static readonly WorkflowPackageBlockResponse[] Blocks =
    [
        new("node.plan", "Planning the sections"),
        new("node.sections", "Sections"),
        new("node.refine", "Refining"),
    ];

    private static RowFinished Node(string key, string status) => new(key, "node", status);

    [Fact]
    public void ABlock_TakesItsOwnRowsStoredStatus()
    {
        var rail = RunRail31.Build(Blocks, [Node("node.plan", "Completed"), Node("node.sections", "Failed")], currentKey: null);

        Assert.Equal(["node.plan", "node.sections", "node.refine"], rail.Select(block => block.Key));
        Assert.Equal(["Planning the sections", "Sections", "Refining"], rail.Select(block => block.Name));
        Assert.Equal("Completed", rail[0].Status);
        Assert.Equal("Failed", rail[1].Status);
        Assert.Null(rail[2].Status);
        Assert.All(rail, block => Assert.Empty(block.Children));
    }

    [Fact]
    public void ItemAndPassRows_AppearUnderTheirTopLevelBlock_InKeyOrder()
    {
        var rail = RunRail31.Build(
            Blocks,
            [
                Node("node.sections.paragraphs.draft_paragraph:2/1", "Completed"),
                Node("node.refine.judge#3", "NotRun"),
                Node("node.sections.paragraphs.draft_paragraph:1/2", "Failed"),
                Node("node.refine.judge#1", "Completed"),
                Node("node.refine.sections#2:3", "Completed"),
            ],
            currentKey: null);

        var sections = rail.Single(block => block.Key == "node.sections");
        Assert.Equal(
            [
                new RailChild31("node.sections.paragraphs.draft_paragraph:1/2", "1/2", "Failed"),
                new RailChild31("node.sections.paragraphs.draft_paragraph:2/1", "2/1", "Completed"),
            ],
            sections.Children);
        Assert.Null(sections.Status);

        var refine = rail.Single(block => block.Key == "node.refine");
        // key order (ordinal, as the Rows listing gives it): judge#1, judge#3, then sections#2:3
        Assert.Equal(["#1", "#3", "#2:3"], refine.Children.Select(child => child.Label));
        Assert.Equal(["Completed", "NotRun", "Completed"], refine.Children.Select(child => child.Status));
        Assert.Empty(rail.Single(block => block.Key == "node.plan").Children);
    }

    [Fact]
    public void ARowOfAnotherBlockWithALongerSharedPrefix_IsNotMisfiled()
    {
        WorkflowPackageBlockResponse[] blocks = [new("node.plan", "Plan"), new("node.planning", "Planning")];

        var rail = RunRail31.Build(
            blocks,
            [Node("node.planning", "Completed"), Node("node.planning.draft:1", "Completed"), Node("node.plan", "Running")],
            currentKey: "node.planning.draft:1");

        Assert.Equal("Running", rail[0].Status);
        Assert.Empty(rail[0].Children);
        Assert.False(rail[0].Current);
        Assert.Equal("Completed", rail[1].Status);
        Assert.Equal("1", Assert.Single(rail[1].Children).Label);
        Assert.True(rail[1].Current);
    }

    [Fact]
    public void TheCurrentKey_MarksItsBlock()
    {
        var own = RunRail31.Build(Blocks, [Node("node.plan", "Completed")], currentKey: "node.plan");
        Assert.Equal([true, false, false], own.Select(block => block.Current));

        var child = RunRail31.Build(Blocks, [], currentKey: "node.refine.judge#2");
        Assert.Equal([false, false, true], child.Select(block => block.Current));

        var none = RunRail31.Build(Blocks, [], currentKey: "deliverable.note");
        Assert.All(none, block => Assert.False(block.Current));
    }

    [Fact]
    public void CheckAndDeliverableRows_AreNotRailBlocks()
    {
        var rail = RunRail31.Build(
            Blocks,
            [
                new RowFinished("check.part_coverage.coverage:1", "check", "Completed"),
                new RowFinished("deliverable.note", "deliverable", "released"),
                new RowFinished("delivery:deliverable.note/email", "delivery", "sent"),
                Node("node.plan", "Completed"),
            ],
            currentKey: null);

        Assert.Equal(["node.plan", "node.sections", "node.refine"], rail.Select(block => block.Key));
        Assert.All(rail, block => Assert.Empty(block.Children));
    }

    [Fact]
    public void TheJobsStatus_IsShownAsStored_ButDeliveringReadsDelivering()
    {
        Assert.Equal("Delivering…", RunRail31.StatusText("delivering"));
        foreach (var stored in new[] { "Queued", "Scheduled", "Running", "Completed", "Failed", "Cancelled", "Waiting for an update to finish" })
        {
            Assert.Equal(stored, RunRail31.StatusText(stored));
        }
    }

    [Fact]
    public void ARowsStatus_IsTheMemberItsSectionWrites()
    {
        Assert.Equal("Completed", RunRail31.StatusOf("node", System.Text.Json.JsonDocument.Parse("""{"status":"Completed"}""").RootElement));
        Assert.Equal("blocked", RunRail31.StatusOf("deliverable", System.Text.Json.JsonDocument.Parse("""{"state":"blocked","status":"x"}""").RootElement));
        Assert.Equal("not sent", RunRail31.StatusOf("delivery", System.Text.Json.JsonDocument.Parse("""{"result":"not sent"}""").RootElement));
        Assert.Null(RunRail31.StatusOf("node", System.Text.Json.JsonDocument.Parse("""{"status":null}""").RootElement));
    }
}
