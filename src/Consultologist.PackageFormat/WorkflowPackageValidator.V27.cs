using Scriban.Runtime;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1234 (package-format-v27-design.md § 1, § 3, § 4): the specVersion 27 path
/// — the 26 pipeline, with a loop's body allowed an if/case node (resolved by
/// <see cref="WorkflowFlowNodes"/> with two levels of owner), the inner
/// condition's scope (the item, inputs, the loop's earlier body nodes; never
/// the loop, never its own branch nodes), a case switching on an enum field
/// of the item, and a deliverable sourcing a loop that returns a flow node.
/// Every other step is the 26 one, called by name: 27 permits what 26 refused
/// and changes nothing it accepted.
/// </summary>
public static partial class WorkflowPackageValidator
{
    private static void ValidateAt27(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? catalog,
        List<string> errors,
        List<string> warnings)
    {
        ValidateMetadata(manifest, errors);
        ValidateDerivedFrom(manifest, errors);
        // The 25 retirements hold at ≥ 25 — their sentences keep the 25 form.
        ValidateRetiredAt25(manifest, errors);
        ValidateRetiredAt26(manifest, errors);
        ValidateInputs(manifest, errors);

        // #1105 (§ 5): the headers — from here on the manifest carries each
        // macro's header facts and the files are bodies.
        var texts = WorkflowTextHeaders.Resolve(manifest, files, errors);
        manifest = texts.Manifest;
        files = texts.Files;

        var inputsById = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .Where(input => WorkflowDeclaredIds.IsValid(input.Id))
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var inputShapes = inputsById.ToDictionary(pair => pair.Key, pair => WorkflowDeclarationNode.Of(pair.Value), StringComparer.Ordinal);

        var localFormats = ResolveFormatsAt25(manifest, files, catalog, errors);
        var snippets = ResolveSnippetsAt26(manifest, texts, errors);
        // #1099 (§ 5.3): the macro folders through the shared resolver.
        var macroFolders = WorkflowTextHeaders.ResolveMacroFolders(manifest, texts, errors);
        var flows = WorkflowFlowNodes.Resolve(manifest, snippets, inputsById, errors);
        var nodes = ValidateNodesAt26(manifest, files, localFormats, catalog, inputsById, flows, errors);
        ValidateMacroLibraryAt26(manifest, files, macroFolders, errors);
        ValidateMacroPlacementsAt26(manifest, macroFolders, errors);
        ValidateCaseSubjectsAt27(flows, nodes, inputsById, errors);

        // The plain nodes' shapes first; a flow node's is built over them (§ 1.3).
        var nodeShapes = nodes.Values
            .Where(n => n.Format is not null)
            .ToDictionary(n => n.Node.Id, n => new TemplateNodeShape(n.Format!.Id, n.Format.Spec.Type, n.Format.Spec.Schema, EnumValuesAt25(n, inputsById)), StringComparer.Ordinal);
        // #1234: an if/case before a loop, so a loop returning one finds its shape.
        foreach (var flow in flows.InShapeOrder)
        {
            nodeShapes[flow.Node.Id] = WorkflowFlowNodes.ShapeOf(flow, nodeShapes);
        }
        var macroFolderIds = macroFolders?.Collections.Keys.ToHashSet(StringComparer.Ordinal) ?? TemplateReferenceContext.NoIds;
        var macroIds = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).Where(id => !macroFolderIds.Contains(id)).ToHashSet(StringComparer.Ordinal);
        var bodies = flows.BodyScopes;
        var deliverableIds = (manifest.Deliverables ?? new List<WorkflowDeliverableSpec>()).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        // #1116 (§ 5.4): a folder's items by id, so an item is readable directly.
        var snippetFolderItems = snippets.Collections.ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value.Items.Select(i => i.Id).ToList(), StringComparer.Ordinal);
        var macroFolderItems = (macroFolders?.Collections ?? new Dictionary<string, WorkflowDataCollection>()).ToDictionary(e => e.Key, e => (IReadOnlyList<string>)e.Value.Items.Select(i => i.Id).ToList(), StringComparer.Ordinal);

        TemplateReferenceContext ContextFor(TemplateFan? fan) => new(
            inputShapes, nodeShapes,
            snippets.Scalars.Keys.ToHashSet(StringComparer.Ordinal),
            snippets.Collections.Keys.ToHashSet(StringComparer.Ordinal),
            macroIds, macroFolderIds, fan,
            Bodies: bodies, Deliverables: deliverableIds, Labelled: true,
            SnippetFolderItems: snippetFolderItems, MacroFolderItems: macroFolderItems);

        var walk = WorkflowDerivedEdges.WalkAll(
            manifest, files,
            nodes.Values.Select(n => new WorkflowDerivedEdges.WalkNode(
                n.Node, nodeShapes.GetValueOrDefault(n.Node.Id), n.Format?.Spec.Parameters, n.Fan,
                Scope: flows.Owner.GetValueOrDefault(n.Node.Id))).ToList(),
            snippets, ContextFor, texts.Headers, macroFolders);
        var walked = walk.Templates;
        TakeSentences(walk, errors, warnings);
        var edges = DeriveEdgesAt26(manifest, nodes, walk, flows, errors);
        ValidateFlowEdgesAt26(flows, edges, errors);

        ValidateConditionsAt27(walk, flows, errors);
        ValidateMacroUseAt25(manifest, nodes, walk, errors, warnings);
        ValidateInstructionsAt26(manifest, nodes, walk, macroFolders, errors);
        ValidateReferenceGraphAt25(walked, snippets, errors, macroFolders);
        RenderProbeAt25(walk, inputsById, nodes, snippets, manifest, errors, node => ProbeFlowNodesAt26(node, flows, nodeShapes), deliverableIds, labelled: true, macroFolders: macroFolders);
        ValidateDeliverablesAt26(manifest, nodes, walk, flows, nodeShapes, errors);

        var specs = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToList();
        ValidateReachabilityAt26(manifest, specs, nodes, flows, edges, errors);
        ValidateBodyReachabilityAt26(flows, nodes, edges, errors);
        ValidateAcyclic(specs, edges, errors);
    }

    /// <summary>#1234 (v27 design § 1.2): the 26 subjects, and inside a loop over an array input an enum field of the item.</summary>
    private static void ValidateCaseSubjectsAt27(
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors) =>
        ValidateCaseSubjects(flows, nodes, inputsById, errors, "a case switches on an enum input (input.<id>), an enum-format node's value (node.<id>.value) or, inside a loop over an array input, an enum field of the item (item.value.<field>)", itemSubjects: true);

    /// <summary>
    /// #1234 (v27 design § 1.2): a condition reads no loop, as at 26 — an inner
    /// flow node's condition names the loop it runs in by that relation; and it
    /// reads none of its own branch nodes, which run after it decides.
    /// </summary>
    private static void ValidateConditionsAt27(WorkflowDerivedEdges.Walk walk, WorkflowFlowNodes.Resolved flows, List<string> errors)
    {
        foreach (var condition in walk.Conditions.Where(c => c.Site != WorkflowDerivedEdges.ConditionSite.FolderItem && c.Walk.IsValid))
        {
            var inner = condition.Site == WorkflowDerivedEdges.ConditionSite.Flow && flows.IsInner(condition.OwnerId) ? flows.Flows[condition.OwnerId] : null;
            var loopId = inner is null ? null : flows.LoopOf(inner.Node.Id);
            var reads = condition.Walk.References.Nodes.Keys.Order(StringComparer.Ordinal).ToList();

            foreach (var id in reads.Where(flows.IsLoop))
            {
                errors.Add(inner is not null && id == loopId
                    ? $"{condition.Subject} reads node.{id}, the loop it runs in; a condition inside a loop reads the item, inputs and earlier body nodes."
                    : $"{condition.Subject} reads node.{id}, which loops; a condition decides before the loop runs — read a scalar node.");
            }

            if (inner is null)
            {
                continue;
            }

            foreach (var id in reads.Where(id => inner.BodyNodeIds.Contains(id, StringComparer.Ordinal)))
            {
                errors.Add($"{condition.Subject} reads node '{id}', which '{inner.Node.Id}' decides; a condition reads what runs before it.");
            }
        }

        ValidateFolderItemConditionsAt26(walk, errors);
    }
}
