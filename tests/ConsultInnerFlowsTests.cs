using Consultologist.Api.Jobs;
using Consultologist.Api.Models;

namespace Consultologist.Api.Tests;

/// <summary>#1235 (design § 1.3, § 1.4, § 5): the per-item calculus of an if/case inside a loop — taken, untaken, settled, the taken ret, the loop returning the flow.</summary>
public class ConsultInnerFlowsTests
{
    private static readonly ConsultNodeDescriptor Loop = new("sections", "Sections", Kind: ConsultNodeFlows.For, Collection: "snippet.sections", Body: new[] { "section_notes", "triage" }, Ret: "triage");
    private static readonly ConsultNodeDescriptor Notes = new("section_notes", "Notes", ForEach: "snippet.sections", In: "sections", Format: "prose");
    private static readonly ConsultNodeDescriptor Triage = new("triage", "Triage", ForEach: "snippet.sections", Kind: ConsultNodeFlows.If, Expression: "item.id == \"hpi\"", Body: new[] { "patient_draft", "table_draft" }, In: "sections");
    private static readonly ConsultNodeDescriptor Patient = new("patient_draft", "Patient", ForEach: "snippet.sections", In: "triage", Format: "prose");
    private static readonly ConsultNodeDescriptor Table = new("table_draft", "Table", ForEach: "snippet.sections", In: "triage", Format: "prose");
    private static readonly ConsultNodeDescriptor TopIf = new("gate", "Gate", Kind: ConsultNodeFlows.If, Expression: "input.kind == \"new\"", Body: new[] { "x" }, Branch: "then", Ret: "x");

    private static readonly Dictionary<string, ConsultNodeDescriptor> NodesById = new[] { Loop, Notes, Triage, Patient, Table, TopIf }.ToDictionary(n => n.Id, StringComparer.Ordinal);

    private static ConsultFlowItemDecision Then(string item) => new("triage", item, "then", new[] { "patient_draft" }, "patient_draft", null);
    private static ConsultFlowItemDecision None(string item) => new("triage", item, "none", Array.Empty<string>(), null, "needs `item.id == \"hpi\"`; it does not hold");

    [Fact]
    public void AnInnerFlow_IsAnIfOrCaseRow_WithInAndAFan()
    {
        Assert.True(ConsultInnerFlows.IsInner(Triage));
        Assert.False(ConsultInnerFlows.IsInner(TopIf));
        Assert.False(ConsultInnerFlows.IsInner(Loop));
        var inner = new ConsultInnerFlows(NodesById);
        Assert.Same(Triage, inner.FlowOf(Patient));
        Assert.Null(inner.FlowOf(Notes));
        Assert.Same(Loop, inner.LoopReturning("triage"));
        Assert.Null(inner.LoopReturning("gate"));
    }

    [Fact]
    public void TakenUntakenAndSettled_FollowTheItemsDecision()
    {
        var inner = new ConsultInnerFlows(NodesById);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal);

        // Undecided: a branch node is neither taken nor untaken nor settled; a loop body node is taken.
        Assert.False(inner.Taken(Patient, "hpi"));
        Assert.False(inner.Untaken(Patient, "hpi"));
        Assert.False(inner.Settled(Patient, "hpi", outputs, failed: false));
        Assert.True(inner.Taken(Notes, "hpi"));

        inner.Decisions["triage:hpi"] = Then("hpi");
        Assert.True(inner.Taken(Patient, "hpi"));
        Assert.False(inner.Taken(Table, "hpi"));
        Assert.True(inner.Untaken(Table, "hpi"));
        Assert.True(inner.Settled(Table, "hpi", outputs, failed: false));
        Assert.False(inner.Settled(Patient, "hpi", outputs, failed: false));
        Assert.True(inner.IsTakenRet(Patient, "hpi"));
        Assert.Equal("patient_draft", inner.RetOf("triage", "hpi"));

        inner.Decisions["triage:plan"] = None("plan");
        inner.SkippedInstances.Add("triage:plan");
        Assert.True(inner.Untaken(Patient, "plan"));
        Assert.True(inner.Untaken(Table, "plan"));
        Assert.True(inner.Settled(Triage, "plan", outputs, failed: false));
        Assert.Null(inner.RetOf("triage", "plan"));

        var items = new[] { "hpi", "plan", "exam" }.Select(id => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { ["id"] = id }).ToList();
        Assert.Equal(2, inner.UntakenInstances(Table, items));
        Assert.Equal(1, inner.UntakenInstances(Patient, items));
        Assert.Equal(0, inner.UntakenInstances(Notes, items));
    }
}
