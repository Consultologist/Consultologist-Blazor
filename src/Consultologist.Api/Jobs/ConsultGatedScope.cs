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

        return JudgeSnippets(scope with { InertMacros = inert, SuppressedPrompts = suppressed }, package, input.Folders);
    }

    /// <summary>
    /// v26 (#1179, design § 2.1, § 5.4): the snippet gates on a scope, at 26 only.
    /// A standalone snippet's header `if` is judged here over the scope
    /// (<see cref="WorkflowInlineGates.InertSnippets"/>) — inert, its content is
    /// empty where read. A folder item's `if` was judged ONCE, at job start, with
    /// the item bound (the starter's fan filter, <c>SettledFolders</c>): the
    /// settled snapshot the orchestrator threads holds the live items. The render
    /// sees the FULL declared folder (bodies, labels) with every item the
    /// snapshot lacks keyed inert — a direct read of a gated-off item renders
    /// empty (the strict context would otherwise refuse the member), and the
    /// renderer leaves an inert item out of every loop. Below 26 the scope is
    /// the one it was: the settled folders, no key.
    /// </summary>
    public static TemplateRenderScope JudgeSnippets(
        TemplateRenderScope scope,
        WorkflowPackage package,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? settledFolders)
    {
        if (package.Manifest.SpecVersion < 26)
        {
            return scope;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var snippets = package.EffectiveManifest.Snippets ?? new Dictionary<string, string>();

        keys.UnionWith(WorkflowInlineGates.InertSnippets(
            scope.Snippets.Keys.Select(id => (id, package.Texts?.Headers.GetValueOrDefault(snippets.GetValueOrDefault(id) ?? string.Empty)?.If)),
            scope,
            TemplatePipes.Engine));

        var folders = new Dictionary<string, IReadOnlyList<TemplateFolderItem>>(StringComparer.Ordinal);
        foreach (var (folder, collection) in package.Data?.Collections ?? new Dictionary<string, WorkflowDataCollection>())
        {
            var live = (settledFolders?.GetValueOrDefault(folder) ?? Array.Empty<IReadOnlyDictionary<string, string>>())
                .Select(item => item.GetValueOrDefault("id", string.Empty))
                .ToHashSet(StringComparer.Ordinal);
            folders[folder] = collection.Items
                .Select(item => new TemplateFolderItem(item.Id, WorkflowPackageBlocks.ItemName(new Dictionary<string, string>(item.Fields, StringComparer.Ordinal) { ["id"] = item.Id }), item.Fields.GetValueOrDefault("content", string.Empty)))
                .ToList();
            keys.UnionWith(collection.Items.Where(item => !live.Contains(item.Id)).Select(item => $"{folder}/{item.Id}"));
        }

        return scope with { Folders = folders, InertSnippets = keys };
    }

    /// <summary>The node id of an instance key ("nodeId" or "nodeId:itemId").</summary>
    internal static string NodeIdOf(string instanceKey)
    {
        var colon = instanceKey.IndexOf(':');
        return colon < 0 ? instanceKey : instanceKey[..colon];
    }
}
