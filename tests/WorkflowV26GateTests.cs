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
}
