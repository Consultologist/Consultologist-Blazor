using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1089 (package-format-v26-design.md § 2.3): which inline reads a node's
/// instance gates suppress — one pure judgement over the node's render scope.
/// </summary>
public class WorkflowInlineGatesTests
{
    private static readonly TemplateRunFacts Run = new("2026-10-26", "abcdef12", "test/general@v2026.10.8", "api.example.test");
    private static readonly TemplateProfileFacts Profile = new("Dr Example", "Dr Example, FRCPC");

    private static TemplateRenderScope Scope(IReadOnlyDictionary<string, string>? inputs = null, TemplateFanItem? item = null, IReadOnlyDictionary<string, TemplateNodeOutput>? nodes = null) =>
        new(
            inputs ?? new Dictionary<string, string> { ["kind"] = "new" },
            new Dictionary<string, string> { ["kind"] = "enum" },
            null,
            nodes ?? new Dictionary<string, TemplateNodeOutput>(),
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<TemplateFolderItem>>(),
            item,
            new Dictionary<string, TemplateMacroSource> { ["closing"] = new("closing", "Closing", "Closing.", "Say goodbye.") },
            Run,
            Profile);

    private static IReadOnlySet<string> Suppressed(params WorkflowInlineGate[] instances) =>
        WorkflowInlineGates.Suppressed("assessment", instances, null, Scope());

    [Fact]
    public void NoInstance_IsNeverSuppressed()
    {
        Assert.Empty(Suppressed());
        // Another macro's failing instance names that macro, never this one.
        Assert.Equal(new[] { "other" }, Suppressed(new WorkflowInlineGate("other", "input.kind == \"follow_up\"")));
    }

    [Fact]
    public void OneInstance_WhoseGateFails_Suppresses()
    {
        Assert.Equal(new[] { "closing" }, Suppressed(new WorkflowInlineGate("closing", "input.kind == \"follow_up\"")));
    }

    [Fact]
    public void OneInstance_WhoseGateHolds_OrHasNone_DoesNot()
    {
        Assert.Empty(Suppressed(new WorkflowInlineGate("closing", "input.kind == \"new\"")));
        Assert.Empty(Suppressed(new WorkflowInlineGate("closing", null)));
        Assert.Empty(Suppressed(new WorkflowInlineGate("closing", "  ")));
    }

    [Fact]
    public void TwoInstances_AnyHolding_RendersTheRead()
    {
        // § 2.3: a placement that will hold needs the model's marker.
        Assert.Empty(Suppressed(
            new WorkflowInlineGate("closing", "input.kind == \"follow_up\""),
            new WorkflowInlineGate("closing", "input.kind == \"new\"")));
        Assert.Equal(new[] { "closing" }, Suppressed(
            new WorkflowInlineGate("closing", "input.kind == \"follow_up\""),
            new WorkflowInlineGate("closing", "input.kind != \"new\"")));
    }

    [Fact]
    public void AnInstanceAnchoredToAnotherItem_IsNotThisNodes()
    {
        var item = new TemplateFanItem("hpi", "HPI", "History.", null);
        var scope = Scope(item: item);

        // The one instance anchored elsewhere: no instance of the macro on this run — as today.
        Assert.Empty(WorkflowInlineGates.Suppressed("section_notes", new[] { new WorkflowInlineGate("closing", "input.kind == \"follow_up\"", ForItem: "plan") }, "hpi", scope));
        // Anchored here: judged.
        Assert.Equal(new[] { "closing" }, WorkflowInlineGates.Suppressed("section_notes", new[] { new WorkflowInlineGate("closing", "input.kind == \"follow_up\"", ForItem: "hpi") }, "hpi", scope));
    }

    [Fact]
    public void AGate_OverTheFanItem_AndANodesValue()
    {
        var item = new TemplateFanItem("hpi", "HPI", "History.", null);
        var nodes = new Dictionary<string, TemplateNodeOutput> { ["scope"] = new("in_scope", "enum", "scope", null) };

        Assert.Empty(WorkflowInlineGates.Suppressed("section_notes", new[] { new WorkflowInlineGate("closing", "item.id == \"hpi\"") }, "hpi", Scope(item: item)));
        Assert.Equal(new[] { "closing" }, WorkflowInlineGates.Suppressed("section_notes", new[] { new WorkflowInlineGate("closing", "item.id == \"plan\"") }, "hpi", Scope(item: item)));
        Assert.Empty(WorkflowInlineGates.Suppressed("assessment", new[] { new WorkflowInlineGate("closing", "node.scope.value == \"in_scope\"") }, null, Scope(nodes: nodes)));
        Assert.Equal(new[] { "closing" }, WorkflowInlineGates.Suppressed("assessment", new[] { new WorkflowInlineGate("closing", "node.scope.value == \"out_of_scope\"") }, null, Scope(nodes: nodes)));
    }
}
