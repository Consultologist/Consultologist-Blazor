using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using NSubstitute.Extensions;

namespace Consultologist.Web.Tests;

/// <summary>
/// #356: the run rail's glyphs. It used to mark a row complete by comparing its
/// POSITION against a count, so a job that completed two nodes turned the first
/// two rows green whatever actually ran — observed in production ticking a node
/// the fire set had excluded, and leaving unticked the aggregator that produced
/// the document.
///
/// The count could not have carried the information: the node-completed event's
/// CompletedNodeCount is a rank among completed nodes in manifest order,
/// recomputed at synthesis, so a DAG finishing its third node first reports 1.
/// Identity was on the wire the whole time.
///
/// These are the first tests of the rail. They reach it by re-attaching to a
/// terminal job, the only way into the run phase without executing one.
/// </summary>
public class ConsultsRunRailTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";

    /// <summary>
    /// The manifest the rail draws rows from: a forEach node over data:standards
    /// and the aggregator it feeds, in that order. The order is what matters —
    /// every test here completes the LATER node and leaves the earlier one
    /// unrun, which no positional implementation can render correctly.
    /// </summary>
    private void WithTwoNodePackage()
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });
        // Configure() rather than a bare call: WithPinnedPackage stubs this to
        // throw, and invoking the member to re-stub it would run that throw
        // during setup.
        WorkflowService.Configure().GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());
    }

    private void WithJobReporting(
        params (string NodeId, string Label, string Status)[] nodes)
    {
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId,
            "user-1",
            "Completed",
            TotalBlockCount: 1,
            CompletedBlockCount: 1,
            FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(),
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            // One node completed out of two, so a count-driven rail marks the
            // FIRST row — which is the one that did not run.
            CompletedStageCount: nodes.Count(node => node.Status == "Completed"),
            TotalStageCount: nodes.Length,
            NodeOutputs: nodes.ToDictionary(
                node => node.NodeId,
                node => new ConsultGenerationNodeStatus(node.NodeId, node.Label, node.Status, null, null, null, null),
                StringComparer.Ordinal),
            AssembledDocument: "The note."));
    }

    private IRenderedComponent<Consults> RenderRail() =>
        Render<Consults>(parameters => parameters.Add(page => page.JobId, JobId));

    /// <summary>The glyph on the rail row whose label matches.</summary>
    private static string GlyphFor(IRenderedComponent<Consults> page, string label) =>
        page.FindAll(".node-row")
            .First(row => row.QuerySelector(".node-row__label")?.TextContent.Trim() == label)
            .QuerySelector(".node-row__status")!
            .TextContent.Trim();

    private static IElement RowFor(IRenderedComponent<Consults> page, string label) =>
        page.FindAll(".node-row")
            .First(row => row.QuerySelector(".node-row__label")?.TextContent.Trim() == label);

    [Fact]
    public void ANodeTheJobCompleted_IsTickedAndOneItDidNotIsNot()
    {
        // The production case. 'Assembling note' is second in the manifest and
        // is the one that ran; 'Drafting section' is first and did not. A rail
        // marking by position ticks 'Drafting section' and nothing else.
        WithTwoNodePackage();
        WithJobReporting(
            ("draft-section", "Drafting section", "Running"),
            ("assemble-note", "Assembling note", "Completed"));

        var page = RenderRail();

        Assert.Equal("✓", GlyphFor(page, "Assembling note"));
        Assert.NotEqual("✓", GlyphFor(page, "Drafting section"));
    }

    [Fact]
    public void ANodeTheJobNeverReported_IsNotTicked()
    {
        // Absence of a node output is not completion. After #355 a node outside
        // the fire set is not in the job at all, which is exactly this shape.
        WithTwoNodePackage();
        WithJobReporting(("assemble-note", "Assembling note", "Completed"));

        var page = RenderRail();

        Assert.Equal("✓", GlyphFor(page, "Assembling note"));
        Assert.Equal("○", GlyphFor(page, "Drafting section"));
    }

    [Fact]
    public void AFailedNode_RendersAsFailed()
    {
        // Before this, a failed node rendered identically to one that never
        // started — the Done row was the only place a failure showed at all.
        WithTwoNodePackage();
        WithJobReporting(
            ("draft-section", "Drafting section", "Failed"),
            ("assemble-note", "Assembling note", "Skipped"));

        var page = RenderRail();

        Assert.Equal("✗", GlyphFor(page, "Drafting section"));
    }

    [Fact]
    public void ASkippedNode_IsDistinguishableFromOneNotYetReached()
    {
        // Two different facts: the job decided against this node, versus it has
        // not got there. They shared the ○ glyph until #356.
        WithTwoNodePackage();
        WithJobReporting(("assemble-note", "Assembling note", "Skipped"));

        var page = RenderRail();

        Assert.Equal("⊘", GlyphFor(page, "Assembling note"));
        Assert.Equal("○", GlyphFor(page, "Drafting section"));
        Assert.Contains("node-status--skipped", RowFor(page, "Assembling note").QuerySelector(".node-row__status")!.ClassName);
    }

    [Fact]
    public void ItemsOfANodeThatNeverRan_AreNotTicked()
    {
        // The node-level verdict used to leak down: a falsely-green node greened
        // every item under it. The items belong to the forEach node, which here
        // did not run.
        WithTwoNodePackage();
        WithJobReporting(("assemble-note", "Assembling note", "Completed"));

        var page = RenderRail();

        Assert.All(
            page.FindAll(".node-row--item"),
            item => Assert.Equal("○", item.QuerySelector(".node-row__status")!.TextContent.Trim()));
    }
}
