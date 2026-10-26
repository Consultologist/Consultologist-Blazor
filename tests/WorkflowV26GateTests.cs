using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1089 (package-format-v26-design.md § 2.1, § 2.3, § 2.4): a node instance's
/// gate keyed if is walked at its site under the 26 subject — typed against
/// the inputs, refused reading a loop, item.id typed against the loop the
/// node runs in; the 25 when keeps its subject.
/// </summary>
public class WorkflowV26GateTests
{
    private static List<string> Instance(string nodeId, string? @if) =>
        V26Package.FlowErrors(m => V26Package.WithNode(m, nodeId, n => n with
        {
            Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, If: @if) },
        }));

    [Fact]
    public void AnInstancesIf_IsWalked_UnderTheNodesSubject()
    {
        Assert.Contains("Node 'assessment' macro 'closing' if compares input.kind to 'maybe', which it does not declare (values: new, follow_up).",
            Instance("assessment", "input.kind == \"maybe\""));
        Assert.Contains("Node 'assessment' macro 'closing' if reads node.sections, which loops; a condition decides before the loop runs — read a scalar node.",
            Instance("assessment", "node.sections.content != \"\""));
        Assert.Empty(Instance("assessment", "input.kind == \"new\""));
        Assert.Empty(Instance("assessment", "node.scope.value == \"in_scope\""));
    }

    [Fact]
    public void ItemId_IsTypedAgainstTheLoop_TheNodeRunsIn()
    {
        Assert.Contains("Node 'section_notes' macro 'closing' if compares item.id to 'nope', which the folder does not contain (items: hpi, plan).",
            Instance("section_notes", "item.id == \"nope\""));
        Assert.Empty(Instance("section_notes", "item.id == \"hpi\""));
        Assert.Empty(Instance("section_notes", "item.id == \"hpi\" || item.id == \"plan\""));
    }

    [Fact]
    public void ItemId_OnANodeInNoLoop_IsRefused()
    {
        var errors = Instance("assessment", "item.id == \"hpi\"");

        Assert.Contains(errors, e => e.StartsWith("Node 'assessment' macro 'closing' if", StringComparison.Ordinal) && e.Contains("item", StringComparison.Ordinal));
    }

    [Fact]
    public void When_OnAnInstance_IsStillRetired_AndKeepsIts25Subject()
    {
        var errors = V26Package.FlowErrors(m => V26Package.WithNode(m, "assessment", n => n with
        {
            Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, When: "input.kind == \"maybe\"") },
        }));

        Assert.Contains("Node 'assessment' macro 'closing' declares when, which is retired at specVersion 26; a gate is keyed if; when is a case branch's label only.", errors);
        // The 25 key walks under the 25 subject, as the 25 path says it.
        Assert.Contains("Node 'assessment' macro 'closing' condition compares input.kind to 'maybe', which it does not declare (values: new, follow_up).", errors);
    }

    [Fact]
    public void TheWalk_RecordsTheInstanceIf_AtItsSite()
    {
        var (manifest, files) = V26Package.Minimal();
        manifest = V26Package.WithNode(manifest, "assessment", n => n with
        {
            Macros = new List<WorkflowNodeMacroSpec> { new("closing", At: WorkflowNodeMacroSpec.After, If: "node.scope.value == \"in_scope\"") },
        });
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var condition = Assert.Single(walk.Conditions, c => c.Site == WorkflowDerivedEdges.ConditionSite.NodeInstance);
        Assert.Equal(("assessment", "closing", "Node 'assessment' macro 'closing' if"), (condition.OwnerId, condition.MacroId, condition.Subject));
        Assert.True(condition.Walk.IsValid, string.Join(" | ", condition.Walk.References.Errors));
        Assert.Contains("scope", walk.EdgesOf("assessment"));
    }

    // ----- #1090: the macro's own if (design § 2.2) -------------------------------

    private static List<string> MacroIf(string @if) =>
        V26Package.FlowErrors(m => m, files => files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line.", @if: @if) + "Closing: {{ profile.name }}.");

    [Fact]
    public void AMacrosOwnIf_IsWalked_UnderItsSubject()
    {
        Assert.Contains("Macro 'closing' if compares input.kind to 'maybe', which it does not declare (values: new, follow_up).", MacroIf("input.kind == \"maybe\""));
        Assert.Contains("Macro 'closing' if reads node.sections, which loops; a condition decides before the loop runs — read a scalar node.", MacroIf("node.sections.content != \"\""));
        Assert.Contains("Macro 'closing' if reads deliverable.consult; a condition decides before the sections compose — read a scalar node.", MacroIf("deliverable.consult.content != \"\""));
        Assert.Contains(MacroIf("item.id == \"hpi\""), e => e.StartsWith("Macro 'closing' if", StringComparison.Ordinal) && e.Contains("item", StringComparison.Ordinal));
        Assert.Empty(MacroIf("input.kind == \"new\""));
        Assert.Empty(MacroIf("node.scope.value == \"in_scope\" && profile.name != \"\""));
    }

    [Fact]
    public void AMacrosOwnIf_IsRecordedAtItsSite_AndItsReadsReachTheReader()
    {
        var (manifest, files) = V26Package.Minimal();
        files["macros/closing.md"] = V26Package.Header("Closing", instruction: "Add a closing line.", @if: "node.scope.value == \"in_scope\"") + "Closing: {{ profile.name }}.";
        // section_notes reads the macro inline; it does not read scope itself.
        files["prompts/section.md"] = "{{ item.label }}: {{ item.content }} {{ macro.closing.prompt }}";
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        var gate = Assert.Single(walk.Conditions, c => c.Site == WorkflowDerivedEdges.ConditionSite.Macro);
        Assert.Equal(("closing", null, "Macro 'closing' if"), (gate.OwnerId, gate.MacroId, gate.Subject));
        Assert.True(gate.Walk.IsValid, string.Join(" | ", gate.Walk.References.Errors));
        // The reader waits on what the gate reads, as it waits on what the macro's text reads.
        Assert.Contains("scope", walk.EdgesOf("section_notes"));
        // assessment places the macro and reads nothing of it inline: the placement's wait is the engine's (#1177).
        Assert.Contains("scope", walk.MacroEdges("closing").Append("scope"));
    }

    [Fact]
    public void Below26_AMacroHasNoOwnGate()
    {
        var (manifest, files) = V25Package.Minimal();
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, files, V25Package.Catalog(), out var snippets, out var contextFor);
        var walk = WorkflowDerivedEdges.WalkAll(manifest, files, nodes, snippets, contextFor);

        Assert.DoesNotContain(walk.Conditions, c => c.Site == WorkflowDerivedEdges.ConditionSite.Macro);
    }
}
