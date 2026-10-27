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
    private static readonly ConsultNodeDescriptor Loop = new("sections", "Sections", Kind: "for", Collection: "snippet.sections", Body: new[] { "section_notes" }, Ret: "section_notes");
    private static readonly ConsultNodeDescriptor FullWorkup = new("full_workup", "Full work-up", Format: "prose", In: "triage");
    private static readonly ConsultNodeDescriptor Triage = new("triage", "Triage", Kind: "if", Expression: "input.kind == \"new\"", Body: new[] { "full_workup", "brief_update" }, Ret: "full_workup", Branch: "then");
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

    // ----- #1155: the deliverable composes from its sources ---------------------------------

    private static readonly ConsultNodeDescriptor Header = new("patient_header", "Patient header", Format: "rendered");

    private static ConsultDeliverables.Deliverable Consult(params string[] sources) =>
        new("consult", "Consultation note", "consult:", string.Empty, sources, 0, Sources: sources);

    private static ConsultDeliverableComposition.Composed? ComposeDeliverable(ConsultDeliverables.Deliverable deliverable, IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById, IReadOnlyDictionary<string, NodeRunResult> outputs, params string[] failed) =>
        ConsultDeliverableComposition.TryCompose(deliverable, nodesById, outputs, _ => Items, (node, itemId) => failed.Contains($"{node.Id}:{itemId}"));

    [Fact]
    public void TryCompose_ADeliverable_ComposesItsSourcesInOrder_AsThe25AggregatorDid()
    {
        var nodesById = NodesById(Body, Loop, Header);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["section_notes:hpi"] = Output("The history."),
            ["section_notes:plan"] = Output("The plan."),
            ["patient_header"] = Output("  Dr Example, Clinic.\n"),
        };
        outputs["sections"] = Compose(Loop, nodesById, outputs)!.Result!;

        var composed = ComposeDeliverable(Consult("sections", "patient_header"), nodesById, outputs)!;

        var parts = new ConsultAggregateRenderer.Part[]
        {
            new ConsultAggregateRenderer.ForEachPart(new[] { ("hpi", "HPI", "The history."), ("plan", "Plan", "The plan.") }),
            new ConsultAggregateRenderer.ScalarPart("Dr Example, Clinic."),
        };
        var expected = ConsultAggregateRenderer.Render(parts);
        Assert.Null(composed.Failure);
        Assert.Equal(expected, composed.Rendered);
        Assert.Equal("## HPI\n\nThe history.\n\n## Plan\n\nThe plan.\n\nDr Example, Clinic.", composed.Rendered);
        Assert.Equal(new[] { "ForEachPart", "ScalarPart" }, composed.Parts!.Select(part => part.GetType().Name));
        // The aggregator's ladder: the items' output hashes then the scalar's; the composed hash over the bytes.
        Assert.Equal(
            ConsultGenerationProvenance.ComputeAggregateInputHash(new[] { outputs["section_notes:hpi"].OutputHash, outputs["section_notes:plan"].OutputHash, outputs["patient_header"].OutputHash }),
            composed.InputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(expected), composed.ComposedHash);
    }

    [Fact]
    public void TryCompose_ADeliverable_WaitsOnTheLoopsRow_AndFailsWithAFailedItem()
    {
        var nodesById = NodesById(Body, Loop, Header);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["section_notes:hpi"] = Output("The history."),
            ["section_notes:plan"] = Output("The plan."),
            ["patient_header"] = Output("Header."),
        };

        // Every item done but the loop's own row not yet written: not settled.
        Assert.Null(ComposeDeliverable(Consult("sections", "patient_header"), nodesById, outputs));
        // The scalar missing: not settled.
        outputs["sections"] = Compose(Loop, nodesById, outputs)!.Result!;
        var scalar = outputs["patient_header"];
        outputs.Remove("patient_header");
        Assert.Null(ComposeDeliverable(Consult("sections", "patient_header"), nodesById, outputs));
        outputs["patient_header"] = scalar;

        var failed = ComposeDeliverable(Consult("sections", "patient_header"), nodesById, outputs, "section_notes:plan")!;
        Assert.Null(failed.Rendered);
        Assert.Equal("Consultation note could not assemble: section_notes:plan did not complete.", failed.Failure);
    }

    [Fact]
    public void TryCompose_AnIfSource_IsOneScalarPart_WithTheReturnedNodesHash()
    {
        var nodesById = NodesById(FullWorkup, Triage, Header);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["full_workup"] = Output("Worked up."),
            ["patient_header"] = Output("Header."),
        };
        outputs["triage"] = outputs["full_workup"];

        var composed = ComposeDeliverable(Consult("patient_header", "triage"), nodesById, outputs)!;

        Assert.Equal("Header.\n\nWorked up.", composed.Rendered);
        Assert.Equal(ConsultGenerationProvenance.ComputeAggregateInputHash(new[] { outputs["patient_header"].OutputHash, outputs["full_workup"].OutputHash }), composed.InputHash);
    }

    [Fact]
    public void IsReleaseReady_TreatsASkippedFlowReadAsSettled_AndHoldsUntilAReadDeliverableComposes()
    {
        var deliverable = Consult("sections") with { Reads = new[] { "triage" }, DeliverableReads = new[] { "letter" } };
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal);
        var settled = new Dictionary<string, ConsultCheckOutcome>(StringComparer.Ordinal);
        var skipped = new HashSet<string>(StringComparer.Ordinal) { "triage" };
        var run = new HashSet<string>(StringComparer.Ordinal) { "consult", "letter" };
        var composed = new HashSet<string>(StringComparer.Ordinal);

        // The read names a flow that took no branch: unsettled without the set, settled with it.
        Assert.False(ConsultCheckSettlement.IsReleaseReady(deliverable, outputs, settled, out _));
        Assert.False(ConsultCheckSettlement.IsReleaseReady(deliverable, outputs, settled, out _, skipped, composed, run));
        // The read deliverable in the run but not composed holds; composed releases.
        composed.Add("letter");
        Assert.True(ConsultCheckSettlement.IsReleaseReady(deliverable, outputs, settled, out _, skipped, composed, run));
        // A read deliverable not in the run (skipped by its if) does not hold.
        Assert.True(ConsultCheckSettlement.IsReleaseReady(deliverable, outputs, settled, out _, skipped, new HashSet<string>(), new HashSet<string> { "consult" }));
    }

    [Fact]
    public void FinalOutcome_At26_ProducedIsComposed_AndTheErrorIsTheDeliverables()
    {
        var deliverables = new[] { Consult("sections"), Consult("patient_header") with { ResultId = "letter", Label = "Letter", BlockPrefix = "letter:" } };
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal);

        Assert.Equal((ConsultGenerationJobStatuses.Completed, (string?)null),
            ConsultDeliverables.FinalOutcome(deliverables, outputs, new Dictionary<string, string>(), null, new HashSet<string> { "consult", "letter" }, new Dictionary<string, string>()));

        var (status, error) = ConsultDeliverables.FinalOutcome(deliverables, outputs, new Dictionary<string, string>(), null,
            new HashSet<string> { "letter" }, new Dictionary<string, string> { ["consult"] = "Consultation note could not assemble: section_notes:plan did not complete." });
        Assert.Equal(ConsultGenerationJobStatuses.Failed, status);
        Assert.Equal("Consultation note could not assemble: section_notes:plan did not complete.", error);

        // Without the composed set a 26 deliverable is missing (the 25 test reads an empty NodeId).
        Assert.Equal(ConsultGenerationJobStatuses.Failed, ConsultDeliverables.FinalOutcome(deliverables, outputs, new Dictionary<string, string>()).Status);
    }

    [Fact]
    public void DeliverableTextsFor_ThreadsTheText_NullForASkippedOne_AndFailsLoudOnOneNotYetComposed()
    {
        var composed = new Dictionary<string, ConsultDeliverableComposition.Composed>(StringComparer.Ordinal)
        {
            ["consult"] = new(new List<ConsultAggregateRenderer.Part>(), "The note.", "i", "c", null),
        };
        var run = new HashSet<string>(StringComparer.Ordinal) { "consult", "letter" };

        Assert.Null(ConsultRenderPayload.DeliverableTextsFor(null, composed, run));
        Assert.Null(ConsultRenderPayload.DeliverableTextsFor(Array.Empty<string>(), composed, run));
        var texts = ConsultRenderPayload.DeliverableTextsFor(new[] { "consult", "summary" }, composed, run)!;
        Assert.Equal(new Dictionary<string, string?> { ["consult"] = "The note.", ["summary"] = null }, texts);
        var ex = Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.DeliverableTextsFor(new[] { "letter" }, composed, run));
        Assert.Equal("Deliverable 'letter' is read before it composed; the orchestrator does not hold its text.", ex.Message);
    }

    [Fact]
    public async Task CompleteResultDocument_StampsTheComposedHashAndSources_AndTheResponseCarriesThem()
    {
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), "general@v2026.10.1", "hash"));

        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("consult", "Consultation note", "The note.", 0, ComposedHash: "c0ffee", Sources: new[] { "sections", "patient_header" }));

        var state = (ConsultGenerationJobState)StateProperty.GetValue(entity)!;
        var row = Assert.Single(state.AssembledDocuments!);
        Assert.Equal("c0ffee", row.ComposedHash);
        Assert.Equal(new[] { "sections", "patient_header" }, row.Sources);
        state.Status = ConsultGenerationJobStatuses.Completed;
        var response = Assert.Single(state.ToResponse().AssembledDocuments!);
        Assert.Equal("c0ffee", response.ComposedHash);
        Assert.Equal(new[] { "sections", "patient_header" }, response.Sources);

        // Below 26 the row stores the bytes it always stored.
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("letter", "Letter", "Dear.", 1));
        var legacy = state.AssembledDocuments!.First(d => d.ResultId == "letter");
        Assert.Null(legacy.ComposedHash);
        Assert.Null(legacy.Sources);
    }

    [Fact]
    public void Resolve_At26_CarriesTheDeliverableReads()
    {
        var results = new[] { new ConsultResultDescriptor("consult", string.Empty, "Consultation note", Sources: new[] { "sections" }, DeliverableReads: new[] { "letter" }) };

        var deliverable = Assert.Single(ConsultDeliverables.Resolve(results, null, NodesById(Body, Loop)));

        Assert.Equal(new[] { "letter" }, deliverable.DeliverableReads);
    }
}
