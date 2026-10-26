using Scriban.Runtime;

namespace Consultologist.PackageFormat;

/// <summary>
/// The validator at specVersion 26 (package-format-v26-design.md; #1150 the
/// path, #1100 the flow nodes). At 26 the manifest is a program over the
/// objects the templates read: a node is a <b>flow node</b> — <c>for</c> over a
/// snippet folder or an array input, <c>if</c> over a Scriban boolean, <c>case</c>
/// over a closed set — with bodies of plain nodes attached by id and a required
/// <c>ret</c> naming the body node the outside reads; or a plain node of a
/// format. <c>forEach</c>, a node's <c>when</c> and <c>aggregate</c> are retired
/// (§ 8). The flow nodes are resolved by <see cref="WorkflowFlowNodes"/>, which
/// the derived-edges walk shares; the pipeline is the 25 one with the node,
/// fan, condition and reachability steps replaced; the steps still the 25
/// methods' are named by the issue that replaces them — #1101 the deliverables
/// and the reachability over <c>sources[]</c>, #1102 the prompt path, #1105 the
/// macro library and the snippets' headers, #1059 the pipe operand.
/// </summary>
public static partial class WorkflowPackageValidator
{
    /// <summary>The replacement sentences of § 8, one per retired key.</summary>
    private static class RetiredAt26
    {
        public const string ForEach = "a for node whose body holds the node (nodes[].for: \"item in snippet.<folder>\" | \"item in input.<id>\")";
        public const string NodeWhen = "an if node whose body holds the node (nodes[].if)";
        public const string Aggregate = "a deliverable names its sources (deliverables[].sources)";
        public const string DeliverableNode = "a deliverable names its sources (deliverables[].sources)";
        public const string Prompts = "a node names its prompt file (nodes[].prompt: \"<path>\")";
        public const string ForItem = "the instance's if on item.id (if: \"item.id == \\\"<item>\\\"\")";
        public const string GateWhen = "a gate is keyed if; when is a case branch's label only";
    }

    private static void ValidateAt26(
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
        ValidateRetiredAt26(manifest, files, errors);
        ValidateInputs(manifest, errors);

        var inputsById = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .Where(input => WorkflowDeclaredIds.IsValid(input.Id))
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var inputShapes = inputsById.ToDictionary(pair => pair.Key, pair => WorkflowDeclarationNode.Of(pair.Value), StringComparer.Ordinal);

        var localFormats = ResolveFormatsAt25(manifest, files, catalog, errors);
        var snippets = ResolveSnippetsAt25(manifest, files, inputsById, errors);
        var flows = WorkflowFlowNodes.Resolve(manifest, snippets, inputsById, errors);
        var nodes = ValidateNodesAt26(manifest, localFormats, catalog, inputsById, flows, errors);
        ValidateMacroLibraryAt25(manifest, files, errors);
        ValidateCaseSubjectsAt26(flows, nodes, inputsById, errors);

        // The plain nodes' shapes first; a flow node's is built over them (§ 1.3).
        var nodeShapes = nodes.Values
            .Where(n => n.Format is not null)
            .ToDictionary(n => n.Node.Id, n => new TemplateNodeShape(n.Format!.Id, n.Format.Spec.Type, n.Format.Spec.Schema, EnumValuesAt25(n, inputsById)), StringComparer.Ordinal);
        var plainShapes = new Dictionary<string, TemplateNodeShape>(nodeShapes, StringComparer.Ordinal);
        foreach (var flow in flows.Flows.Values)
        {
            nodeShapes[flow.Node.Id] = WorkflowFlowNodes.ShapeOf(flow, plainShapes);
        }
        var macroIds = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var bodies = flows.BodyScopes;
        var deliverableIds = (manifest.Deliverables ?? new List<WorkflowDeliverableSpec>()).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);

        TemplateReferenceContext ContextFor(TemplateFan? fan) => new(
            inputShapes, nodeShapes,
            snippets.Scalars.Keys.ToHashSet(StringComparer.Ordinal),
            snippets.Collections.Keys.ToHashSet(StringComparer.Ordinal),
            macroIds, TemplateReferenceContext.NoIds, fan,
            Bodies: bodies, Deliverables: deliverableIds);

        var walk = WorkflowDerivedEdges.WalkAll(
            manifest, files,
            nodes.Values.Select(n => new WorkflowDerivedEdges.WalkNode(
                n.Node, nodeShapes.GetValueOrDefault(n.Node.Id), n.Format?.Spec.Parameters, n.Fan,
                Scope: flows.Owner.GetValueOrDefault(n.Node.Id))).ToList(),
            snippets, ContextFor);
        var walked = walk.Templates;
        TakeSentences(walk, errors, warnings);
        var edges = DeriveEdgesAt26(manifest, nodes, walk, flows, errors);
        ValidateFlowEdgesAt26(flows, edges, errors);

        ValidateConditionsAt26(walk, flows, errors);
        ValidateMacroUseAt25(manifest, nodes, walk, errors, warnings);
        ValidateReferenceGraphAt25(walked, snippets, errors);
        RenderProbeAt25(walk, inputsById, nodes, snippets, manifest, errors, node => ProbeFlowNodesAt26(node, flows, nodeShapes), deliverableIds);
        ValidateDeliverablesAt26(manifest, nodes, walk, flows, nodeShapes, errors);

        var specs = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToList();
        ValidateReachabilityAt26(manifest, specs, nodes, flows, edges, errors);
        ValidateBodyReachabilityAt26(flows, nodes, edges, errors);
        ValidateAcyclic(specs, edges, errors);
    }

    // ----- § 8: refused by name, with the replacement -------------------------

    private static void ValidateRetiredAt26(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files, List<string> errors)
    {
        if (manifest.Prompts != null)
        {
            errors.Add($"prompts is retired at specVersion 26; {RetiredAt26.Prompts}.");
        }

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.ForEach != null)
            {
                errors.Add($"Node '{node.Id}' declares forEach, which is retired at specVersion 26; {RetiredAt26.ForEach}.");
            }

            if (node.When != null)
            {
                errors.Add($"Node '{node.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.NodeWhen}.");
            }

            if (node.Aggregate != null)
            {
                errors.Add($"Node '{node.Id}' declares aggregate, which is retired at specVersion 26; {RetiredAt26.Aggregate}.");
            }

            foreach (var instance in node.Macros ?? new List<WorkflowNodeMacroSpec>())
            {
                if (instance.ForItem != null)
                {
                    errors.Add($"Node '{node.Id}' macro '{instance.Id}' declares forItem, which is retired at specVersion 26; {RetiredAt26.ForItem}.");
                }

                if (instance.When != null)
                {
                    errors.Add($"Node '{node.Id}' macro '{instance.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.GateWhen}.");
                }
            }
        }

        foreach (var deliverable in manifest.Deliverables ?? new List<WorkflowDeliverableSpec>())
        {
            if (deliverable.Node != null)
            {
                errors.Add($"Deliverable '{deliverable.Id}' declares node, which is retired at specVersion 26; {RetiredAt26.DeliverableNode}.");
            }

            if (deliverable.When != null)
            {
                errors.Add($"Deliverable '{deliverable.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.GateWhen}.");
            }

            foreach (var instance in deliverable.Macros ?? new List<WorkflowDeliverableMacroSpec>())
            {
                if (instance.When != null)
                {
                    errors.Add($"Deliverable '{deliverable.Id}' macro '{instance.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.GateWhen}.");
                }
            }
        }

        // A folder item's when sits in the folder's index.json, not the manifest:
        // resolve the snippet table as ResolveSnippetsAt25 will (its own sentences
        // are that step's — a folder that does not resolve is simply absent here).
        var snippets = WorkflowDataResolver.Resolve(manifest.Snippets, SnippetNoun, files, new List<string>());

        foreach (var (folder, collection) in snippets.Collections)
        {
            foreach (var item in collection.Items)
            {
                if (item.When != null)
                {
                    errors.Add($"Snippet folder '{folder}' item '{item.Id}' declares when, which is retired at specVersion 26; {RetiredAt26.GateWhen}.");
                }
            }
        }
    }

    // ----- § 1: the nodes at 26 ---------------------------------------------------

    /// <summary>
    /// The 25 node rules with the aggregator gone and the fan the loop's: a plain
    /// node resolves its format, prompt and policies as at 25 and runs under its
    /// owner loop's fan (none under an if/case); a flow node is structural and
    /// carries its own fan for the walk.
    /// </summary>
    private static IReadOnlyDictionary<string, NodeAt25> ValidateNodesAt26(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, ResolvedFormat> localFormats,
        WorkflowFormatCatalog? catalog,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        WorkflowFlowNodes.Resolved flows,
        List<string> errors)
    {
        var nodes = new Dictionary<string, NodeAt25>(StringComparer.Ordinal);
        var prompts = (manifest.Prompts ?? new List<WorkflowPromptSpec>()).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var unresolvedCatalogueIds = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (nodes.ContainsKey(node.Id))
            {
                errors.Add($"Duplicate node id '{node.Id}'.");
                continue;
            }

            if (!WorkflowDeclaredIds.IsValid(node.Id))
            {
                var renamed = node.Id.Replace('-', '_');
                errors.Add(WorkflowDeclaredIds.IsValid(renamed)
                    ? $"Node id '{node.Id}' must be snake_case at specVersion 26 ({renamed}): a template reads it as a Scriban member."
                    : $"Node id '{node.Id}' must be snake_case at specVersion 26 (a lowercase letter, then lowercase letters, digits, or underscores): a template reads it as a Scriban member.");
            }

            if (string.IsNullOrWhiteSpace(node.Label))
            {
                errors.Add($"Node '{node.Id}' must declare a label.");
            }

            if (flows.Flows.TryGetValue(node.Id, out var flow))
            {
                nodes[node.Id] = new NodeAt25(node, null, flow.Fan, flow.Folder);
                continue;
            }

            var (fan, folder) = flows.FanOf(node.Id);

            if (string.IsNullOrWhiteSpace(node.Format))
            {
                errors.Add($"Node '{node.Id}' must declare a format at specVersion 26.");
                nodes[node.Id] = new NodeAt25(node, null, fan, folder);
                continue;
            }

            ResolvedFormat? format = null;

            if (localFormats.TryGetValue(node.Format, out var local))
            {
                format = local;
            }
            else if (catalog is not null && catalog.TryGet(node.Format, out var entry))
            {
                format = new ResolvedFormat(node.Format, entry!.Spec, WorkflowFormatOrigin.Catalogue);
            }
            else if (catalog is null && (manifest.Formats ?? new List<WorkflowFormatReference>()).All(f => f.Id != node.Format))
            {
                unresolvedCatalogueIds.Add(node.Format);
                nodes[node.Id] = new NodeAt25(node, null, fan, folder);
                continue;
            }
            else
            {
                errors.Add($"Node '{node.Id}' declares format '{node.Format}', which is neither a package format nor a catalogue format.");
                nodes[node.Id] = new NodeAt25(node, null, fan, folder);
                continue;
            }

            var spec = format.Spec;
            var computed = spec.Producer == WorkflowFormatProducers.Computed;

            if (computed && node.Prompt != null)
            {
                errors.Add($"Node '{node.Id}' declares prompt, but format '{format.Id}' is computed and takes none.");
            }
            else if (!computed && string.IsNullOrWhiteSpace(node.Prompt))
            {
                errors.Add($"Node '{node.Id}' must declare a prompt; format '{format.Id}' is {spec.Producer}.");
            }
            else if (node.Prompt != null && !prompts.Contains(node.Prompt))
            {
                // #1102: the prompt becomes a file path; until then the 25 declaration.
                errors.Add($"Node '{node.Id}' names prompt '{node.Prompt}', which the package does not declare.");
            }

            var validatable = spec.Type != WorkflowInputTypes.Text;

            if (!validatable && node.OnInvalid != null)
            {
                errors.Add($"Node '{node.Id}' declares onInvalid, but format '{format.Id}' is text and validates nothing.");
            }
            else if (validatable && string.IsNullOrWhiteSpace(node.OnInvalid))
            {
                errors.Add($"Node '{node.Id}' must declare onInvalid ({string.Join(" | ", WorkflowOnInvalid.All)}); format '{format.Id}' is validated.");
            }
            else if (node.OnInvalid != null && !WorkflowOnInvalid.IsKnown(node.OnInvalid))
            {
                errors.Add($"Node '{node.Id}' onInvalid '{node.OnInvalid}' is not one of {string.Join(", ", WorkflowOnInvalid.All)}.");
            }

            if (node.FailIfEmpty != null && string.IsNullOrWhiteSpace(node.FailIfEmpty))
            {
                errors.Add($"Node '{node.Id}' failIfEmpty must not be blank.");
            }
            else if (node.FailIfEmpty != null && spec.Producer is WorkflowFormatProducers.Rendered or WorkflowFormatProducers.Computed)
            {
                errors.Add($"Node '{node.Id}' declares failIfEmpty, but format '{format.Id}' is {spec.Producer} and has no reply to judge.");
            }

            if (node.Reproducible != null && spec.Producer is WorkflowFormatProducers.Rendered or WorkflowFormatProducers.Computed)
            {
                errors.Add($"Node '{node.Id}' declares reproducible, but format '{format.Id}' is {spec.Producer} and deterministic by construction; the claim is not its to make.");
            }

            nodes[node.Id] = new NodeAt25(node, format, fan, folder);
        }

        if (unresolvedCatalogueIds.Count > 0)
        {
            errors.Add($"The format catalogue is not available on this engine; catalogue formats ({string.Join(", ", unresolvedCatalogueIds)}) cannot be resolved.");
        }

        return nodes;
    }

    /// <summary>
    /// A case switches on a closed set — an enum input (<c>input.&lt;id&gt;</c>) or an
    /// enum-format node's value (<c>node.&lt;id&gt;.value</c>); every branch value is
    /// one of the set and takes one branch (§ 1.1).
    /// </summary>
    private static void ValidateCaseSubjectsAt26(
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors)
    {
        const string rule = "a case switches on an enum input (input.<id>) or an enum-format node's value (node.<id>.value)";

        foreach (var flow in flows.Flows.Values.Where(f => f.Kind == WorkflowFlowNodes.Kind.Case && f.Expression is not null))
        {
            var subject = flow.Expression!;
            IReadOnlyList<string>? values = null;

            if (TemplateObjectReference.IsBare(subject, TemplateObjects.Input, out var inputId))
            {
                if (inputsById.TryGetValue(inputId, out var input))
                {
                    if (WorkflowInputTypes.Of(input) == WorkflowInputTypes.Enum)
                    {
                        values = input.Values;
                    }
                    else
                    {
                        errors.Add($"Node '{flow.Node.Id}' switches on {subject}, which is a {WorkflowInputTypes.Of(input)}; {rule}.");
                    }
                }
                else
                {
                    errors.Add($"Node '{flow.Node.Id}' switches on {subject}, which the package does not declare; {rule}.");
                }
            }
            else if (CaseNodeSubjectOf(subject) is { } nodeId)
            {
                if (flows.Flows.ContainsKey(nodeId))
                {
                    errors.Add($"Node '{flow.Node.Id}' switches on {subject}, which is a flow node; {rule}.");
                }
                else if (!nodes.TryGetValue(nodeId, out var node))
                {
                    errors.Add($"Node '{flow.Node.Id}' switches on {subject}, which the package does not declare; {rule}.");
                }
                else if (node.Format is null)
                {
                    // The node's own sentence names the missing format.
                }
                else if (node.Format.Spec.Type != WorkflowInputTypes.Enum)
                {
                    errors.Add($"Node '{flow.Node.Id}' switches on {subject}, but format '{node.Format.Id}' is {node.Format.Spec.Type}; {rule}.");
                }
                else
                {
                    values = EnumValuesAt25(node, inputsById);
                }
            }
            else
            {
                errors.Add($"Node '{flow.Node.Id}' switches on '{subject}'; {rule}.");
            }

            if (values is null)
            {
                continue;
            }

            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            var index = 0;

            foreach (var body in flow.Bodies.Where(b => b.When is not null))
            {
                index++;

                foreach (var value in body.When!)
                {
                    if (!values.Contains(value, StringComparer.Ordinal))
                    {
                        errors.Add($"Node '{flow.Node.Id}' branch {index} when '{value}' is a value {subject} does not declare (values: {string.Join(", ", values)}).");
                    }
                    else if (taken.TryGetValue(value, out var first))
                    {
                        errors.Add($"Node '{flow.Node.Id}' lists '{value}' twice (branches {first} and {index}); each value takes one branch.");
                    }
                    else
                    {
                        taken[value] = index;
                    }
                }
            }
        }
    }

    /// <summary><c>node.&lt;id&gt;.value</c> → the id; anything else → null.</summary>
    private static string? CaseNodeSubjectOf(string subject) =>
        TemplateObjectReference.TryParse(subject, out var obj, out var id, out var rest)
            && obj == TemplateObjects.Node
            && rest.TrimStart('.') == TemplateMembers.Value
            ? id
            : null;

    // ----- the edges, the bodies' scope, the conditions -------------------------

    /// <summary>
    /// The 25 edges (what each prompt reads, closed over the texts; <c>with</c>)
    /// plus the flow: a flow node depends on every node of its bodies (it
    /// completes after them; its output is theirs), and a case on the node it
    /// switches on.
    /// </summary>
    private static IReadOnlyDictionary<string, HashSet<string>> DeriveEdgesAt26(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        WorkflowDerivedEdges.Walk walk,
        WorkflowFlowNodes.Resolved flows,
        List<string> errors)
    {
        var edges = DeriveEdgesAt25(manifest, nodes, walk, errors)
            .ToDictionary(pair => pair.Key, pair => new HashSet<string>(pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (var flow in flows.Flows.Values)
        {
            if (!edges.TryGetValue(flow.Node.Id, out var targets))
            {
                targets = new HashSet<string>(StringComparer.Ordinal);
                edges[flow.Node.Id] = targets;
            }

            targets.UnionWith(flow.BodyNodeIds.Where(nodes.ContainsKey));

            if (flow.Kind == WorkflowFlowNodes.Kind.Case && flow.Expression is { } subject && CaseNodeSubjectOf(subject) is { } nodeId && nodes.ContainsKey(nodeId))
            {
                targets.Add(nodeId);
            }
        }

        return edges;
    }

    /// <summary>
    /// § 1.2: a body node is read by bare id only from inside its body — the
    /// walker refuses a prompt's or a condition's read naming the flow node; a
    /// deferred text's (a snippet's, a macro's) read is judged here at its reader.
    /// Two loops' bodies do not read each other; a body node does not read the
    /// loop it runs in.
    /// </summary>
    private static void ValidateFlowEdgesAt26(WorkflowFlowNodes.Resolved flows, IReadOnlyDictionary<string, HashSet<string>> edges, List<string> errors)
    {
        foreach (var (fromId, targets) in edges.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            flows.Flows.TryGetValue(fromId, out var fromFlow);
            flows.Owner.TryGetValue(fromId, out var fromOwner);

            foreach (var targetId in targets.Order(StringComparer.Ordinal))
            {
                if (fromFlow is not null && fromFlow.BodyNodeIds.Contains(targetId, StringComparer.Ordinal))
                {
                    continue; // its own body
                }

                if (flows.Owner.TryGetValue(targetId, out var targetOwner))
                {
                    if (fromOwner == targetOwner)
                    {
                        continue; // siblings of one body
                    }

                    if (fromOwner is not null)
                    {
                        errors.Add(flows.IsLoop(fromOwner) && flows.IsLoop(targetOwner)
                            ? $"Node '{fromId}' reads node '{targetId}' across loops ('{fromOwner}' vs '{targetOwner}'); body nodes of one loop read one another."
                            : $"Node '{fromId}' reads node '{targetId}' across bodies ({flows.Flows[fromOwner].Inside} vs {flows.Flows[targetOwner].Inside}); the nodes of one body read one another.");
                    }
                    else
                    {
                        errors.Add($"Node '{fromId}' reads node '{targetId}', which runs inside {flows.Flows[targetOwner].Inside}; read node.{targetOwner}.");
                    }
                }
                else if (fromOwner is not null && fromOwner == targetId)
                {
                    errors.Add(flows.IsLoop(targetId)
                        ? $"Node '{fromId}' reads node.{targetId}, the loop it runs in; a body node reads its siblings, the outside reads the loop."
                        : $"Node '{fromId}' reads node.{targetId}, the branch it runs in; a body node reads its siblings, the outside reads node.{targetId}.");
                }
            }
        }
    }

    /// <summary>
    /// § 1.3, § 2.1: a condition decides before a loop runs, so it reads no loop
    /// (the 25 aggregator rule, carried); a folder item's gate reads inputs only.
    /// </summary>
    private static void ValidateConditionsAt26(WorkflowDerivedEdges.Walk walk, WorkflowFlowNodes.Resolved flows, List<string> errors)
    {
        foreach (var condition in walk.Conditions.Where(c => c.Site != WorkflowDerivedEdges.ConditionSite.FolderItem && c.Walk.IsValid))
        {
            foreach (var id in condition.Walk.References.Nodes.Keys.Where(flows.IsLoop).Order(StringComparer.Ordinal))
            {
                errors.Add($"{condition.Subject} reads node.{id}, which loops; a condition decides before the loop runs — read a scalar node.");
            }
        }

        foreach (var condition in walk.Conditions.Where(c => c.Site == WorkflowDerivedEdges.ConditionSite.FolderItem && c.Walk.IsValid))
        {
            var refs = condition.Walk.References;
            var offending = refs.Nodes.Keys.Select(id => $"{TemplateObjects.Node}.{id}")
                .Concat(refs.Snippets.Select(id => $"{TemplateObjects.Snippet}.{id}"))
                .Concat(refs.SnippetFolders.Select(id => $"{TemplateObjects.Snippet}.{id}"))
                .Concat(refs.MacroContents.Concat(refs.MacroPrompts).Select(id => $"{TemplateObjects.Macro}.{id}"))
                .Concat(refs.ItemMembers.Select(m => $"{TemplateObjects.Item}.{m}"))
                .Concat(refs.RunFacts.Select(m => $"{TemplateObjects.Run}.{m}"))
                .Concat(refs.ProfileFacts.Select(m => $"{TemplateObjects.Profile}.{m}"))
                .FirstOrDefault();

            if (offending is not null)
            {
                errors.Add($"{condition.Subject} reads {offending}; a folder item's if reads inputs only, so the folder's size is known at job start (the template's own if gates anything else).");
            }
        }
    }

    // ----- § 3: the deliverable names what it composes ---------------------------

    /// <summary>
    /// § 3 (#1101): a deliverable composes its <c>sources</c> — ordered, each a
    /// prose node (a plain node of a text format, a loop whose returned node is
    /// prose, an if/case whose every branch returns prose), never a body node by
    /// its own name; two deliverables may share a source. The check, the
    /// instances and the signature keep their 25 rules; a deliverable macro reads
    /// no body node by name (the loop is what it reads).
    /// </summary>
    private static void ValidateDeliverablesAt26(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        WorkflowDerivedEdges.Walk walk,
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, TemplateNodeShape> shapes,
        List<string> errors)
    {
        var walked = walk.Templates;
        var deliverables = manifest.Deliverables;

        if (deliverables is not { Count: > 0 })
        {
            errors.Add("deliverables must declare at least one deliverable.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var declaredMacros = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var checks = nodes.Values.Where(n => n.Format?.Spec.Producer == WorkflowFormatProducers.Computed).Select(n => n.Node.Id).ToHashSet(StringComparer.Ordinal);
        var namedChecks = new HashSet<string>(StringComparer.Ordinal);
        var signatureReaders = walked.Where(w => w.Kind == TemplateKind.Macro && w.References.ProfileFacts.Contains("signature")).Select(w => w.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var deliverable in deliverables)
        {
            var subject = $"Deliverable '{deliverable.Id}'";

            if (!WorkflowDeclaredIds.IsValid(deliverable.Id))
            {
                errors.Add($"Deliverable id '{deliverable.Id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
            }
            else if (!ids.Add(deliverable.Id))
            {
                errors.Add($"Duplicate deliverable id '{deliverable.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(deliverable.Label))
            {
                errors.Add($"{subject} has no label.");
            }

            if (deliverable.Sources is not { Count: > 0 })
            {
                errors.Add($"{subject} names no sources; a deliverable composes one or more nodes (sources: [\"node.<id>\", …]).");
            }
            else
            {
                var seenSources = new HashSet<string>(StringComparer.Ordinal);

                foreach (var source in deliverable.Sources)
                {
                    if (!TemplateObjectReference.IsBare(source, TemplateObjects.Node, out var sourceId))
                    {
                        errors.Add($"{subject} source '{source}' must be node.<id>.");
                        continue;
                    }

                    if (!seenSources.Add(sourceId))
                    {
                        errors.Add($"{subject} source node.{sourceId} twice; a source composes once.");
                        continue;
                    }

                    if (!nodes.TryGetValue(sourceId, out var node))
                    {
                        errors.Add($"{subject} source names unknown node '{sourceId}'.");
                        continue;
                    }

                    if (flows.Owner.TryGetValue(sourceId, out var owner))
                    {
                        errors.Add($"{subject} source node.{sourceId} runs inside {flows.Flows[owner].Inside}; name node.{owner}.");
                        continue;
                    }

                    if (flows.Flows.TryGetValue(sourceId, out var flow))
                    {
                        var ret = shapes.TryGetValue(sourceId, out var shape) ? shape.Flow?.Ret : null;

                        if (flow.Kind == WorkflowFlowNodes.Kind.For)
                        {
                            if (ret is null || ret.Type != WorkflowInputTypes.Text || ret.Aggregates)
                            {
                                errors.Add($"{subject} source node.{sourceId} loops over '{shape?.Flow?.RetId}' of format '{ret?.Format}'; a deliverable composes prose.");
                            }
                        }
                        else if (ret is null)
                        {
                            errors.Add($"{subject} source node.{sourceId} returns {shape?.Flow?.DescribeDisagreement()}; a deliverable composes prose.");
                        }
                        else if (ret.Type != WorkflowInputTypes.Text)
                        {
                            errors.Add($"{subject} source node.{sourceId} returns '{shape?.Flow?.RetId}' of format '{ret.Format}'; a deliverable composes prose.");
                        }

                        continue;
                    }

                    if (node.Format is null)
                    {
                        continue; // the node's own sentence names the missing format
                    }

                    if (node.Format.Spec.Producer == WorkflowFormatProducers.Computed)
                    {
                        errors.Add($"{subject} source node.{sourceId} is a check; a check gates a deliverable (check: node.{sourceId}), it is no text.");
                    }
                    else if (node.Format.Spec.Type != WorkflowInputTypes.Text)
                    {
                        errors.Add($"{subject} source node.{sourceId} is a {node.Format.Id}; a deliverable composes prose — read its .value from a prose node.");
                    }
                }
            }

            if (deliverable.Check != null)
            {
                if (!TemplateObjectReference.IsBare(deliverable.Check, TemplateObjects.Node, out var checkId))
                {
                    errors.Add($"{subject} check '{deliverable.Check}' must be node.<id>, naming a node of a computed format.");
                }
                else if (!nodes.ContainsKey(checkId))
                {
                    errors.Add($"{subject} check names node '{checkId}', which the package does not declare.");
                }
                else if (!checks.Contains(checkId))
                {
                    errors.Add($"{subject} check names node '{checkId}', whose format is not computed; a check is a node of a computed format (terms_subset).");
                }
                else
                {
                    namedChecks.Add(checkId);
                }
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var signatures = 0;

            foreach (var entry in deliverable.Macros ?? new List<WorkflowDeliverableMacroSpec>())
            {
                var where = $"{subject} macro '{entry.Id}'";

                if (!declaredMacros.Contains(entry.Id))
                {
                    errors.Add($"{where} is not a declared macro.");
                    continue;
                }

                if (entry.At is null)
                {
                    errors.Add($"{where} must declare at (prepend | append).");
                }
                else if (entry.At != WorkflowDeliverableMacroSpec.Prepend && entry.At != WorkflowDeliverableMacroSpec.Append)
                {
                    errors.Add($"{where} declares at '{entry.At}'; a deliverable instance is placed 'prepend' or 'append'.");
                }

                if (!seen.Add($"{entry.Id}\n{entry.At}"))
                {
                    errors.Add($"{where} is placed twice ({entry.At}); one identity (id, at) once.");
                }

                // § 1.2: a deliverable composes no item — a body node is read through its loop.
                foreach (var read in walk.MacroEdges(entry.Id).Order(StringComparer.Ordinal))
                {
                    if (flows.Owner.TryGetValue(read, out var readOwner))
                    {
                        errors.Add($"{where} reads node '{read}', which runs inside {flows.Flows[readOwner].Inside}; a deliverable macro reads scalar nodes, flow nodes and deliverable.<id>.content — place the macro on a body node to read its items.");
                    }
                }

                if (walk.TextItemReadOf(TemplateKind.Macro, entry.Id) is { } itemRead)
                {
                    errors.Add($"{where} reads item.{itemRead.Member}; a deliverable composes no fan item — place the macro on the node to read its items.");
                }

                if (signatureReaders.Contains(entry.Id))
                {
                    signatures++;
                }
            }

            if (deliverable.Signature == true && signatures > 0)
            {
                errors.Add($"{subject} declares signature and places a macro that reads profile.signature; a deliverable is signed once.");
            }
            else if (signatures > 1)
            {
                errors.Add($"{subject} places more than one macro that reads profile.signature; a deliverable is signed once.");
            }
        }

        foreach (var orphan in checks.Where(id => !namedChecks.Contains(id)).Order(StringComparer.Ordinal))
        {
            errors.Add($"Check '{orphan}' is not named by any deliverable; a check gates a deliverable, or it is dead weight.");
        }
    }

    /// <summary>
    /// § 3.2 (#1067's two rules over sources): every deliverable's closure holds a
    /// loop — a deliverable with no loop has no consult; every node reaches some
    /// deliverable's closure, but for an enum node (read by conditions) and a
    /// check's chain. A flow node's bodies are reached when it is.
    /// </summary>
    private static void ValidateReachabilityAt26(
        WorkflowPackageManifest manifest,
        IReadOnlyList<WorkflowNodeSpec> specs,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        List<string> errors)
    {
        var roots = new List<(string DeliverableId, IReadOnlyList<string> Sources)>();

        foreach (var deliverable in manifest.Deliverables ?? new List<WorkflowDeliverableSpec>())
        {
            var sources = (deliverable.Sources ?? new List<string>())
                .Select(source => TemplateObjectReference.IsBare(source, TemplateObjects.Node, out var id) ? id : null)
                .OfType<string>()
                .Where(id => nodes.ContainsKey(id) && !flows.Owner.ContainsKey(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (sources.Count > 0)
            {
                roots.Add((deliverable.Id, sources));
            }
        }

        if (roots.Count == 0)
        {
            return;
        }

        var reachableFromAny = new HashSet<string>(StringComparer.Ordinal);
        var loops = flows.Flows.Values.Where(f => f.Kind == WorkflowFlowNodes.Kind.For).Select(f => f.Node.Id).Order(StringComparer.Ordinal).ToList();
        var remedy = loops.Count > 0
            ? $"Name a for node among its sources or read one from a source (loops in this package: {string.Join(", ", loops)})."
            : "This package declares no loop — add a for node over a snippet folder or an array input and name it among the deliverable's sources.";

        foreach (var (deliverableId, sources) in roots)
        {
            var reachable = WorkflowNodeClosure.Reachable(sources, edges);
            reachableFromAny.UnionWith(reachable);

            if (!reachable.Any(flows.IsLoop))
            {
                errors.Add($"Deliverable '{deliverableId}' must transitively include at least one loop (a for node): a deliverable with no loop has no consult. {remedy}");
            }
        }

        var checkChain = WorkflowNodeClosure.Reachable(
            nodes.Values.Where(n => n.Format?.Spec.Producer == WorkflowFormatProducers.Computed).Select(n => n.Node.Id),
            edges);

        foreach (var spec in specs)
        {
            if (nodes.TryGetValue(spec.Id, out var node) && node.Format?.Spec.Type == WorkflowInputTypes.Enum)
            {
                continue;
            }

            if (checkChain.Contains(spec.Id) || reachableFromAny.Contains(spec.Id))
            {
                continue;
            }

            errors.Add(roots.Count == 1
                ? $"Node '{spec.Id}' does not feed the deliverable: every node must transitively reach '{roots[0].DeliverableId}'s sources. Read it from a node that does, or name it among the deliverable's sources."
                : $"Node '{spec.Id}' does not feed any deliverable: every node must transitively reach a deliverable's sources. Read it from a node that does, or name it among a deliverable's sources.");
        }
    }

    /// <summary>§ 1.2 (#1067 inside a body): a body node is returned or read by a sibling, or it runs for nothing.</summary>
    private static void ValidateBodyReachabilityAt26(
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        List<string> errors)
    {
        foreach (var flow in flows.Flows.Values)
        {
            foreach (var body in flow.Bodies)
            {
                var members = body.Nodes.Where(id => nodes.ContainsKey(id) && flows.Owner.TryGetValue(id, out var o) && o == flow.Node.Id).Distinct(StringComparer.Ordinal).ToList();

                foreach (var id in members)
                {
                    if (id == body.RetNodeId)
                    {
                        continue;
                    }

                    var read = members.Any(m => m != id && edges.TryGetValue(m, out var targets) && targets.Contains(id));

                    if (!read)
                    {
                        errors.Add(flow.Kind == WorkflowFlowNodes.Kind.For
                            ? $"Node '{id}' in loop '{flow.Node.Id}' is neither returned nor read; name it with ret or read it from a body node."
                            : $"Node '{id}' in node '{flow.Node.Id}' {body.Label} is neither returned nor read; name it with ret or read it from a body node.");
                    }
                }
            }
        }
    }

    /// <summary>
    /// § 1.3: the flow nodes' probes — a loop as a list of two elements (each
    /// carrying item, the returned node's content and value, and the body nodes)
    /// that also reads as text and as the array of values; an if/case as its
    /// returned node's shape when the branches agree, else as text.
    /// </summary>
    private static void ProbeFlowNodesAt26(ScriptObject node, WorkflowFlowNodes.Resolved flows, IReadOnlyDictionary<string, TemplateNodeShape> shapes)
    {
        foreach (var flow in flows.Flows.Values)
        {
            var shape = shapes[flow.Node.Id];

            if (shape.Flow?.Kind == TemplateFlowKind.For)
            {
                var ret = shape.Flow.Ret ?? TemplateNodeShape.Aggregator();
                var list = new ScriptArray();

                for (var i = 0; i < 2; i++)
                {
                    var element = new ScriptObject
                    {
                        [TemplateMembers.Item] = shape.Flow.Fan is InputArrayFan input
                            ? new ScriptObject { ["id"] = i.ToString(), ["name"] = "placeholder", ["value"] = Probe(input.Element) }
                            : new ScriptObject { ["id"] = "item", ["name"] = "placeholder", ["content"] = "placeholder" },
                        [TemplateMembers.Content] = "placeholder",
                    };

                    if (ret.HasValue)
                    {
                        element[TemplateMembers.Value] = ProbeFromShape(ValueShapeOf(ret));
                    }

                    foreach (var (id, bodyShape) in shape.Flow.Body ?? new Dictionary<string, TemplateNodeShape>())
                    {
                        var member = new ScriptObject { [TemplateMembers.Content] = "placeholder" };
                        if (bodyShape.HasValue)
                        {
                            member[TemplateMembers.Value] = ProbeFromShape(ValueShapeOf(bodyShape));
                        }

                        element[id] = member;
                    }

                    list.Add(element);
                }

                list.ScriptObject[TemplateMembers.Content] = "placeholder";
                if (ret.HasValue)
                {
                    list.ScriptObject[TemplateMembers.Value] = new ScriptArray { ProbeFromShape(ValueShapeOf(ret)), ProbeFromShape(ValueShapeOf(ret)) };
                }

                node[flow.Node.Id] = list;
                continue;
            }

            node[flow.Node.Id] = shape.HasValue
                ? new ScriptObject { ["content"] = "placeholder", ["value"] = ProbeFromShape(ValueShapeOf(shape)) }
                : new ScriptObject { ["content"] = "placeholder" };
        }

        static TemplateValueShape ValueShapeOf(TemplateNodeShape shape) =>
            shape.Type == WorkflowInputTypes.Enum && shape.Schema is null
                ? TemplateValueShape.Text
                : TemplateValueShape.FromSchema(shape.Schema);
    }
}
