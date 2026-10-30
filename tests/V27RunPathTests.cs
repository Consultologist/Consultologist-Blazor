using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1235 (package-format-v27-design.md § 1.4, § 1.5, § 5): the engine loop's
/// pure halves at 27 — a loop returning an if/case composes per item over the
/// taken branch's returned node (the alias flow:item), skipping the items the
/// flow skipped by condition (the count kept, the hash over the present); the
/// deliverable likewise, an empty composition when every item was skipped; the
/// payload's sparse loop texts and skipped list; a read of a skipped instance
/// omitted; readiness over a skipped instance; the returned node per item.
/// </summary>
public class V27RunPathTests
{
    private static readonly ConsultNodeDescriptor Notes = new("section_notes", "Section notes", ForEach: "snippet.sections", Format: "prose", In: "sections", Reads: null);
    private static readonly ConsultNodeDescriptor Triage = new("triage", "Triage", ForEach: "snippet.sections", Kind: "if", Expression: "item.id == \"hpi\"", Body: new[] { "patient_draft", "table_draft" }, In: "sections");
    private static readonly ConsultNodeDescriptor Patient = new("patient_draft", "Patient draft", ForEach: "snippet.sections", Format: "prose", In: "triage", Reads: new[] { "section_notes" });
    private static readonly ConsultNodeDescriptor Table = new("table_draft", "Table draft", ForEach: "snippet.sections", Format: "concept_list", In: "triage");
    private static readonly ConsultNodeDescriptor Loop = new("sections", "Sections", Kind: "for", Collection: "snippet.sections", Body: new[] { "section_notes", "triage" }, Ret: "triage", Count: 3);
    private static readonly ConsultNodeDescriptor Summary = new("summary", "Summary", ForEach: "snippet.sections", Format: "prose", In: "sections", Reads: new[] { "triage" });
    private static readonly ConsultNodeDescriptor Assessment = new("assessment", "Assessment", Format: "prose", Reads: new[] { "sections" });

    private static readonly IReadOnlyList<IReadOnlyDictionary<string, string>> Items = new[]
    {
        (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = "hpi", ["label"] = "HPI", ["content"] = "History." },
        new Dictionary<string, string> { ["id"] = "plan", ["label"] = "Plan", ["content"] = "Plan." },
        new Dictionary<string, string> { ["id"] = "exam", ["label"] = "Exam", ["content"] = "Exam." },
    };

    private static readonly IReadOnlyDictionary<string, ConsultNodeDescriptor> NodesById =
        new[] { Notes, Triage, Patient, Table, Loop, Summary, Assessment }.ToDictionary(n => n.Id, StringComparer.Ordinal);

    private static NodeRunResult Output(string text) => new(text, null, "in-" + text.Length, "out-" + text.Length, 6);

    /// <summary>hpi took then (patient_draft), plan was skipped, exam took else (table_draft) — the aliases in place.</summary>
    private static Dictionary<string, NodeRunResult> Outputs()
    {
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["section_notes:hpi"] = Output("Notes h."),
            ["section_notes:plan"] = Output("Notes p."),
            ["section_notes:exam"] = Output("Notes e."),
            ["patient_draft:hpi"] = Output("  Drafted h.\n"),
            ["table_draft:exam"] = Output("Drafted e."),
        };
        outputs["triage:hpi"] = outputs["patient_draft:hpi"];
        outputs["triage:exam"] = outputs["table_draft:exam"];
        return outputs;
    }

    private static readonly IReadOnlySet<string> Skipped = new HashSet<string>(StringComparer.Ordinal) { "triage:plan" };

    private static ConsultFlowCompletion.Composed? Compose(IReadOnlyDictionary<string, NodeRunResult> outputs, IReadOnlySet<string>? skipped) =>
        ConsultFlowCompletion.TryCompose(Loop, NodesById, outputs, _ => Items, (_, _) => false, skipped);

    [Fact]
    public void ALoopReturningAnIf_ComposesThePresentItemsAliases_InItemOrder_AndHashesOverThem()
    {
        var outputs = Outputs();

        // Not every item settled: the skipped one has no alias, so without the skipped set the loop waits.
        Assert.Null(Compose(outputs, null));

        var composed = Compose(outputs, Skipped)!;

        var expected = ConsultAggregateRenderer.Render(new[] { new ConsultAggregateRenderer.ForEachPart(new[] { ("hpi", "HPI", "Drafted h."), ("exam", "Exam", "Drafted e.") }) });
        Assert.Null(composed.Failure);
        Assert.Equal("## HPI\n\nDrafted h.\n\n## Exam\n\nDrafted e.", composed.Result!.RawOutput);
        Assert.Equal(ConsultGenerationProvenance.ComputeAggregateInputHash(new[] { outputs["patient_draft:hpi"].OutputHash, outputs["table_draft:exam"].OutputHash }), composed.Result.InputHash);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(expected), composed.Result.OutputHash);
        Assert.Equal(ConsultGenerationProvenance.NodeHashVersionAt25, composed.Result.HashVersion);
    }

    [Fact]
    public void ALoop_WhoseEveryItemWasSkipped_ComposesNothing_NotAFailure()
    {
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal) { ["section_notes:hpi"] = Output("n") };
        var all = new HashSet<string>(StringComparer.Ordinal) { "triage:hpi", "triage:plan", "triage:exam" };

        var composed = Compose(outputs, all)!;

        Assert.Null(composed.Failure);
        Assert.Equal(string.Empty, composed.Result!.RawOutput);
        Assert.Equal(ConsultGenerationProvenance.ComputeAggregateInputHash(Array.Empty<string>()), composed.Result.InputHash);
    }

    [Fact]
    public void A26Loop_WithNoSkippedSet_ComposesAsItDid()
    {
        var loop26 = Loop with { Ret = "section_notes", Body = new[] { "section_notes" } };
        var outputs = Outputs();

        var without = ConsultFlowCompletion.TryCompose(loop26, NodesById, outputs, _ => Items, (_, _) => false)!;
        var withEmpty = ConsultFlowCompletion.TryCompose(loop26, NodesById, outputs, _ => Items, (_, _) => false, new HashSet<string>())!;

        Assert.Equal(without.Result!.RawOutput, withEmpty.Result!.RawOutput);
        Assert.Equal(without.Result.InputHash, withEmpty.Result.InputHash);
        Assert.Equal("## HPI\n\nNotes h.\n\n## Plan\n\nNotes p.\n\n## Exam\n\nNotes e.", without.Result.RawOutput);
    }

    [Fact]
    public void ADeliverable_SourcingTheLoop_ComposesThePresentItems_AndAnEmptyPartWhenAllWereSkipped()
    {
        var deliverable = new ConsultDeliverables.Deliverable("consult", "Consultation note", "consult:", string.Empty, new[] { "triage" }, 0, Sources: new[] { "sections" });
        var outputs = Outputs();
        outputs["sections"] = Compose(outputs, Skipped)!.Result!;

        var composed = ConsultDeliverableComposition.TryCompose(deliverable, NodesById, outputs, _ => Items, (_, _) => false, Skipped)!;
        Assert.Null(composed.Failure);
        Assert.Equal("## HPI\n\nDrafted h.\n\n## Exam\n\nDrafted e.", composed.Rendered);
        Assert.Equal(ConsultGenerationProvenance.ComputeAggregateInputHash(new[] { outputs["patient_draft:hpi"].OutputHash, outputs["table_draft:exam"].OutputHash }), composed.InputHash);

        var all = new HashSet<string>(StringComparer.Ordinal) { "triage:hpi", "triage:plan", "triage:exam" };
        var empty = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal) { ["sections"] = Compose(outputs, all)!.Result! };
        var document = ConsultDeliverableComposition.TryCompose(deliverable, NodesById, empty, _ => Items, (_, _) => false, all)!;
        Assert.Null(document.Failure);
        Assert.Equal(string.Empty, document.Rendered);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(string.Empty), document.ComposedHash);
    }

    [Fact]
    public void LoopsFor_IsSparseForTheInnerFlowAndItsBranchNodes_AndNamesTheSkippedItems()
    {
        var outputs = Outputs();
        var collections = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> { ["sections"] = Items };

        var loops = ConsultRenderPayload.LoopsFor(Assessment, NodesById, outputs, collections, Skipped)!;

        var payload = loops["sections"];
        Assert.Equal(new[] { "plan" }, payload.Skipped);
        Assert.Equal(new[] { "exam", "hpi", "plan" }, payload.Texts["section_notes"].Keys.Order());
        Assert.Equal(new[] { "exam", "hpi" }, payload.Texts["triage"].Keys.Order());
        Assert.Equal("Drafted e.", payload.Texts["triage"]["exam"]);

        // A loop body node (not a branch node) missing an item that was not skipped is still an engine fault.
        outputs.Remove("section_notes:exam");
        Assert.Contains("has no output for item 'exam'", Assert.Throws<InvalidOperationException>(() => ConsultRenderPayload.LoopsFor(Assessment, NodesById, outputs, collections, Skipped)).Message);
    }

    [Fact]
    public void NodeTextsFor_OmitsASkippedInstance_AndInstanceReady_SettlesOnIt()
    {
        var outputs = Outputs();
        var plan = Items[1];

        // summary reads triage item-aligned: for plan the instance was skipped — omitted, and the reader is ready.
        Assert.Empty(ConsultRenderPayload.NodeTextsFor(Summary, plan, NodesById, outputs, Skipped));
        Assert.True(ConsultNodeScheduler.InstanceReady(Summary, "plan", NodesById, outputs, Skipped));
        Assert.False(ConsultNodeScheduler.InstanceReady(Summary, "plan", NodesById, outputs, null));

        // For hpi the alias is the text, typed by the taken branch's returned node.
        Assert.Equal(new Dictionary<string, string> { ["triage"] = "  Drafted h.\n" }, ConsultRenderPayload.NodeTextsFor(Summary, Items[0], NodesById, outputs, Skipped));
        Assert.True(ConsultNodeScheduler.InstanceReady(Summary, "hpi", NodesById, outputs, Skipped));
    }

    [Fact]
    public void FlowRetsFor_NamesTheReturnedNodePerItem_ForAnInnerFlowReadDirectlyOrThroughItsLoop()
    {
        var itemRets = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["triage"] = new Dictionary<string, string> { ["hpi"] = "patient_draft", ["exam"] = "table_draft" },
        };

        var direct = ConsultRenderPayload.FlowRetsFor(Summary, NodesById, itemRets)!;
        Assert.Equal(new Dictionary<string, string> { ["triage:hpi"] = "patient_draft", ["triage:exam"] = "table_draft" }, direct);

        var throughLoop = ConsultRenderPayload.FlowRetsFor(Assessment, NodesById, itemRets)!;
        Assert.Equal("triage", throughLoop["sections"]);
        Assert.Equal("table_draft", throughLoop["triage:exam"]);

        Assert.Null(ConsultRenderPayload.FlowRetsFor(Notes, NodesById, itemRets));
    }

    // ----- #1246: the alias lands before the summary pass judges the flow settled -----

    private static ConsultInnerFlows DecidedInner()
    {
        var inner = new ConsultInnerFlows(NodesById);
        inner.Decisions["triage:hpi"] = new ConsultFlowItemDecision("triage", "hpi", "then", new[] { "patient_draft" }, "patient_draft", null);
        inner.Decisions["triage:plan"] = new ConsultFlowItemDecision("triage", "plan", "none", Array.Empty<string>(), null, "needs `item.id == \"hpi\"`; it does not hold");
        inner.Decisions["triage:exam"] = new ConsultFlowItemDecision("triage", "exam", "else", new[] { "table_draft" }, "table_draft", null);
        inner.SkippedInstances.Add("triage:plan");
        return inner;
    }

    [Fact]
    public void TheInnerFlowsItems_SettleOnlyThroughTheAlias_WhichTheSummaryPassNowWritesFirst()
    {
        // #1246: the smoke's `applies` row went missing — every item decided, the
        // taken branches' outputs in place, and still the flow was not "settled"
        // for the summary pass, because the alias flow:item had not been written
        // yet (the aggregator pass wrote it, after). With the outputs of the taken
        // returned nodes present and no alias, the flow's taken items are unsettled;
        // one alias pass settles them, and the pass is idempotent.
        var inner = DecidedInner();
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal)
        {
            ["patient_draft:hpi"] = Output("Drafted h."),
            ["table_draft:exam"] = Output("Drafted e."),
        };

        Assert.False(inner.Settled(Triage, "hpi", outputs, failed: false));
        Assert.True(inner.Settled(Triage, "plan", outputs, failed: false)); // skipped by condition: settled with nothing to read
        Assert.False(inner.Settled(Triage, "exam", outputs, failed: false));

        Assert.True(inner.AliasTakenRets(outputs, _ => Items));

        Assert.Same(outputs["patient_draft:hpi"], outputs["triage:hpi"]);
        Assert.Same(outputs["table_draft:exam"], outputs["triage:exam"]);
        Assert.False(outputs.ContainsKey("triage:plan"));
        Assert.True(Items.All(item => inner.Settled(Triage, item["id"], outputs, failed: false)));
        Assert.False(inner.AliasTakenRets(outputs, _ => Items));
    }

    [Fact]
    public void TheAlias_WaitsForTheTakenReturnedNodesOutput_AndNeverAliasesAnUndecidedItem()
    {
        var inner = new ConsultInnerFlows(NodesById);
        inner.Decisions["triage:hpi"] = new ConsultFlowItemDecision("triage", "hpi", "then", new[] { "patient_draft" }, "patient_draft", null);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal) { ["table_draft:exam"] = Output("Drafted e.") };

        // hpi decided, its returned node not yet done: no alias; exam undecided though its would-be branch has output: no alias.
        Assert.False(inner.AliasTakenRets(outputs, _ => Items));
        Assert.False(outputs.ContainsKey("triage:hpi"));
        Assert.False(outputs.ContainsKey("triage:exam"));

        outputs["patient_draft:hpi"] = Output("Drafted h.");
        Assert.True(inner.AliasTakenRets(outputs, _ => Items));
        Assert.Same(outputs["patient_draft:hpi"], outputs["triage:hpi"]);
    }
}
