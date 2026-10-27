using System.Reflection;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1154 (package-format-v26-design.md § 1.3, § 3, § 14), PR 2 — the engine
/// loop's pure halves: a loop's output is the 25 aggregator's bytes and ladder
/// over its returned node's items (the #1065 identity), an if/case's its taken
/// body's returned node, none while unsettled, a failure sentence when an item
/// failed; the deliverable table's completion keys and the block named by the
/// loop; a skipped read omitted from the payload and a loop read as its text;
/// the entity's skipped row.
/// </summary>
public class V26RunPathTests
{
    private static readonly ConsultNodeDescriptor Body = new("section_notes", "Section notes", ForEach: "snippet.sections", Format: "prose", In: "sections");
    private static readonly ConsultNodeDescriptor Loop = new("sections", "Sections", Flow: "for", Collection: "snippet.sections", Body: new[] { "section_notes" }, Ret: "section_notes");
    private static readonly ConsultNodeDescriptor FullWorkup = new("full_workup", "Full work-up", Format: "prose", In: "triage");
    private static readonly ConsultNodeDescriptor Triage = new("triage", "Triage", Flow: "if", Expression: "input.kind == \"new\"", Body: new[] { "full_workup", "brief_update" }, Ret: "full_workup", Branch: "then");
    private static readonly ConsultNodeDescriptor Assessment = new("assessment", "Assessment", Format: "prose");

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Items = new[]
    {
        (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "hpi", ["label"] = "HPI", ["content"] = "History." },
        new Dictionary<string, string> { ["id"] = "plan", ["label"] = "Plan", ["content"] = "Plan." },
    };

    private static IReadOnlyDictionary<string, ConsultNodeDescriptor> NodesById(params ConsultNodeDescriptor[] nodes) =>
        nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

    private static NodeRunResult Output(string text) => new(text, null, "in-" + text.Length, "out-" + text.Length, 6);

    private static ConsultFlowCompletion.Composed? Compose(ConsultNodeDescriptor flow, IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById, IReadOnlyDictionary<string, NodeRunResult> outputs, params string[] failed) =>
        ConsultFlowCompletion.TryCompose(flow, nodesById, outputs, _ => Items, (node, itemId) => failed.Contains($"{node.Id}:{itemId}"));

    [Fact]
    public void TryCompose_ALoopsOutput_IsThe25AggregatorsBytes_AndLadder()
    {
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["section_notes:hpi"] = Output("  The history.\n"),
            ["section_notes:plan"] = Output("The plan."),
        };

        var composed = Compose(Loop, NodesById(Body, Loop), outputs)!;

        var expected = ConsultAggregateRenderer.Render(new[] { new ConsultAggregateRenderer.ForEachPart(new[] { ("hpi", "HPI", "The history."), ("plan", "Plan", "The plan.") }) });
        Assert.Null(composed.Failure);
        Assert.Equal(expected, composed.Result!.RawOutput);
        Assert.Equal("## HPI\n\nThe history.\n\n## Plan\n\nThe plan.", composed.Result.RawOutput);
        Assert.Equal(ConsultGenerationProvenance.ComputeAggregateInputHash(new[] { outputs["section_notes:hpi"].OutputHash, outputs["section_notes:plan"].OutputHash }), composed.Result.InputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(expected), composed.Result.OutputHash);
        Assert.Equal(composed.Result.OutputHash, composed.Result.AgentOutputHash);
        Assert.Equal(ConsultGenerationProvenance.NodeHashVersionAt25, composed.Result.HashVersion);
        Assert.Null(composed.Result.Concepts);
    }

    [Fact]
    public void TryCompose_ALoop_WaitsForEveryItem_AndFailsWithAFailedOne()
    {
        var nodesById = NodesById(Body, Loop);
        var oneItem = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal) { ["section_notes:hpi"] = Output("The history.") };

        Assert.Null(Compose(Loop, nodesById, oneItem));

        var failed = Compose(Loop, nodesById, oneItem, "section_notes:plan")!;
        Assert.Null(failed.Result);
        Assert.Equal("Sections could not assemble: section_notes:plan did not complete.", failed.Failure);
    }

    [Fact]
    public void TryCompose_AnIfNodesOutput_AliasesItsRet_AndNoneNeverComposes()
    {
        var nodesById = NodesById(FullWorkup, Triage);
        var settled = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal) { ["full_workup"] = Output("Worked up.") };

        Assert.Null(Compose(Triage, nodesById, new Dictionary<string, NodeRunResult>()));
        var composed = Compose(Triage, nodesById, settled)!;
        Assert.Same(settled["full_workup"], composed.Result);
        Assert.Null(composed.Failure);

        var none = Triage with { Ret = null, Branch = "none" };
        Assert.Null(Compose(none, nodesById, settled));
    }

    [Fact]
    public void Resolve_At26_SourceIdsAreTheCompletionKeys_AndSourcesTheDeclared()
    {
        var results = new[] { new ConsultResultDescriptor("consult", string.Empty, "Consultation note", Sources: new[] { "assessment", "sections", "triage" }) };

        var deliverable = Assert.Single(ConsultDeliverables.Resolve(results, null, NodesById(Assessment, Body, Loop, FullWorkup, Triage)));

        Assert.Equal(new[] { "assessment", "section_notes", "triage" }, deliverable.SourceIds);
        Assert.Equal(new[] { "assessment", "sections", "triage" }, deliverable.Sources);
        Assert.Equal(("consult:", string.Empty), (deliverable.BlockPrefix, deliverable.NodeId));
    }

    [Fact]
    public void NodeTextsFor_OmitsASkippedRead_AndReadsALoopAsItsComposedText()
    {
        var reader = new ConsultNodeDescriptor("summary", "Summary", Format: "prose", Reads: new[] { "sections", "triage" });
        var nodesById = NodesById(Body, Loop, Triage, reader);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["section_notes:hpi"] = Output("The history."),
            ["sections"] = Output("## HPI\n\nThe history."),
        };

        var texts = ConsultRenderPayload.NodeTextsFor(reader, null, nodesById, outputs, new HashSet<string>(StringComparer.Ordinal) { "triage" });

        Assert.Equal(new[] { "sections" }, texts.Keys);
        Assert.Equal("## HPI\n\nThe history.", texts["sections"]);
        Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.NodeTextsFor(reader, null, nodesById, outputs));
    }

    // ----- the entity's skipped row -----------------------------------------------------

    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    [Fact]
    public async Task MarkNodeSkipped_RecordsTheRow_AndMovesTheCounts()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), "general@v2026.10.1", "hash",
            Nodes: new[] { Triage with { Ret = null, Branch = "none" }, Assessment }));

        entity.MarkNodeSkipped(new ConsultGenerationNodeSkip("triage", "Triage", "took no branch", 1, 2));

        var state = (ConsultGenerationJobState)StateProperty.GetValue(entity)!;
        var row = state.NodeOutputs!["triage"];
        Assert.Equal(ConsultGenerationNodeStatuses.Skipped, row.Status);
        Assert.Null(row.Error);
        Assert.NotNull(row.CompletedAtUtc);
        Assert.Equal((1, 2), (state.CompletedStageCount, state.TotalStageCount));
        Assert.Contains(state.History, e => e.Kind == "skipped" && e.Detail == "took no branch");
        Assert.Equal("none", state.Nodes!.First(n => n.Id == "triage").Branch);
    }
}
