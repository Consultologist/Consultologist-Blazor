using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v26 (#1175, #1177; design § 2.2–2.4): the node's render scope with the two
/// macro gates judged — the macro's own `if` (its header's, carried on the
/// macro source by #1157) into <see cref="TemplateRenderScope.InertMacros"/>,
/// the instance's `if` into <see cref="TemplateRenderScope.SuppressedPrompts"/>
/// — by the library's one judgement (<see cref="WorkflowInlineGates"/>). Pure
/// over the activity's input and the package, so the render and the completion
/// judge alike; judged before the model call, so a gate that is no boolean
/// fails before anything is spent. **At 26 only**: a 25 instance's `when` never
/// suppressed the inline read (the text guard was the author's tool), so below
/// 26 the scope is the one it was — the sets null, the bytes pinned.
/// </summary>
internal static class ConsultGatedScope
{
    public static TemplateRenderScope Judge(ConsultPromptNodeActivityInput input, WorkflowPackage package)
    {
        var scope = ConsultRenderPayload.ScopeAt25(input, package);

        if (package.Manifest.SpecVersion < 26)
        {
            return scope;
        }

        var nodeId = NodeIdOf(input.NodeId);
        var itemId = input.Item?.GetValueOrDefault("id");
        var inert = WorkflowInlineGates.Inert(scope.Macros.Values.Select(macro => (macro.Id, macro.If)), scope, TemplatePipes.Engine);
        var suppressed = WorkflowInlineGates.Suppressed(
            nodeId,
            (input.Instances ?? Array.Empty<ConsultNodeMacroDescriptor>()).Select(instance => new WorkflowInlineGate(instance.Id, instance.If, instance.ForItem)),
            itemId,
            scope,
            TemplatePipes.Engine);

        return scope with { InertMacros = inert, SuppressedPrompts = suppressed };
    }

    /// <summary>The node id of an instance key ("nodeId" or "nodeId:itemId").</summary>
    internal static string NodeIdOf(string instanceKey)
    {
        var colon = instanceKey.IndexOf(':');
        return colon < 0 ? instanceKey : instanceKey[..colon];
    }
}
