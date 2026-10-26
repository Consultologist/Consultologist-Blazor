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
/// (§ 8). The pipeline is the 25 one with the node, fan, condition and
/// reachability steps replaced; the steps still the 25 methods' are named by the
/// issue that replaces them — #1101 the deliverables and the reachability over
/// <c>sources[]</c>, #1102 the prompt path, #1105 the macro library and the
/// snippets' headers, #1059 the pipe operand. The reads a template makes of a
/// flow node (<c>node.&lt;loop&gt;</c> iterable, <c>.value</c> across branches) are
/// #1100's second PR; here a loop is read as text (the aggregator's shape) and a
/// branch node as its returned node's format when every branch returns one.
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

    private enum FlowKind { For, If, Case }

    /// <summary>One body of a flow node: the loop's, an if's then/else, a case's branch (with its values) or else.</summary>
    private sealed record BodyAt26(string Label, IReadOnlyList<string> Nodes, string? Ret, IReadOnlyList<string>? When)
    {
        /// <summary>The returned node's id, when <c>ret</c> is spelled <c>node.&lt;id&gt;</c>.</summary>
        public string? RetNodeId => TemplateObjectReference.IsBare(Ret, TemplateObjects.Node, out var id) ? id : null;
    }

    /// <summary>A flow node resolved: its kind, bodies, and — for a loop — the fan its body runs under.</summary>
    private sealed record FlowAt26(
        WorkflowNodeSpec Node,
        FlowKind Kind,
        IReadOnlyList<BodyAt26> Bodies,
        TemplateFan? Fan,
        WorkflowDataCollection? Folder,
        string? Expression)
    {
        /// <summary>"Loop 'l'" for a for node, "Node 'x'" for an if or case node — the sentences' subject.</summary>
        public string Subject => Kind == FlowKind.For ? $"Loop '{Node.Id}'" : $"Node '{Node.Id}'";

        /// <summary>"loop 'l'" / "node 'x'" — where a body node runs.</summary>
        public string Inside => Kind == FlowKind.For ? $"loop '{Node.Id}'" : $"node '{Node.Id}'";

        public IEnumerable<string> BodyNodeIds => Bodies.SelectMany(b => b.Nodes);

        public string BodySubject(BodyAt26 body) => Kind == FlowKind.For ? Subject : $"{Subject} {body.Label}";
    }

    /// <summary>The flow nodes by id, and each body node's owner (the flow node it runs inside).</summary>
    private sealed record FlowsAt26(IReadOnlyDictionary<string, FlowAt26> Flows, IReadOnlyDictionary<string, string> Owner)
    {
        public bool IsLoop(string id) => Flows.TryGetValue(id, out var flow) && flow.Kind == FlowKind.For;
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
        var flows = ResolveFlowNodesAt26(manifest, snippets, inputsById, errors);
        var nodes = ValidateNodesAt26(manifest, localFormats, catalog, inputsById, flows, errors);
        ValidateMacroLibraryAt25(manifest, files, errors);
        ValidateCaseSubjectsAt26(flows, nodes, inputsById, errors);

        var nodeShapes = nodes.Values
            .Where(n => n.Format is not null)
            .ToDictionary(n => n.Node.Id, n => new TemplateNodeShape(n.Format!.Id, n.Format.Spec.Type, n.Format.Spec.Schema, EnumValuesAt25(n, inputsById)), StringComparer.Ordinal);
        foreach (var flow in flows.Flows.Values)
        {
            nodeShapes[flow.Node.Id] = FlowShapeAt26(flow, nodes, inputsById);
        }
        var macroIds = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        TemplateReferenceContext ContextFor(TemplateFan? fan) => new(
            inputShapes, nodeShapes,
            snippets.Scalars.Keys.ToHashSet(StringComparer.Ordinal),
            snippets.Collections.Keys.ToHashSet(StringComparer.Ordinal),
            macroIds, TemplateReferenceContext.NoIds, fan);

        var walk = WorkflowDerivedEdges.WalkAll(
            manifest, files,
            nodes.Values.Select(n => new WorkflowDerivedEdges.WalkNode(
                n.Node, nodeShapes.GetValueOrDefault(n.Node.Id), n.Format?.Spec.Parameters, n.Fan)).ToList(),
            snippets, ContextFor);
        var walked = walk.Templates;
        TakeSentences(walk, errors, warnings);
        var edges = DeriveEdgesAt26(manifest, nodes, walk, flows, errors);
        ValidateFlowEdgesAt26(flows, edges, errors);

        ValidateConditionsAt26(walk, flows, errors);
        ValidateMacroUseAt25(manifest, nodes, walk, errors, warnings);
        ValidateReferenceGraphAt25(walked, snippets, errors);
        RenderProbeAt25(walk, inputsById, nodes, snippets, manifest, errors, node => ProbeFlowNodesAt26(node, flows, nodes, inputsById));
        // #1101: the deliverables name their sources; until then the 25 step
        // speaks of the aggregator a deliverable can no longer name.
        ValidateDeliverablesAt25(manifest, nodes, walk, errors);

        var specs = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToList();
        // #1101: the reachability rooted at the deliverables (every node feeds
        // one; every deliverable includes a loop) is rewritten over sources[];
        // the 25 step keys on forEach and is skipped here. Inside a body the
        // #1067 rule holds already.
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

    // ----- § 1: the flow nodes --------------------------------------------------

    /// <summary>
    /// Every node carrying <c>for</c>, <c>if</c> or <c>case</c>: one flow key, no
    /// format or prompt (a flow node is structural), the keys its kind carries,
    /// a loop's collection resolved to the fan its body runs under, each body a
    /// set of declared plain nodes attached to exactly one body, and <c>ret</c>
    /// required on every body and naming one of its nodes (§ 1.1–1.2).
    /// </summary>
    private static FlowsAt26 ResolveFlowNodesAt26(
        WorkflowPackageManifest manifest,
        WorkflowPackageData snippets,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors)
    {
        var declared = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var flows = new Dictionary<string, FlowAt26>(StringComparer.Ordinal);

        foreach (var node in declared.Values)
        {
            var keys = new List<string>();
            if (node.For != null) keys.Add("for");
            if (node.If != null) keys.Add("if");
            if (node.Case != null) keys.Add("case");

            if (keys.Count == 0)
            {
                foreach (var (name, present) in new[] { ("nodes", node.Nodes != null), ("ret", node.Ret != null), ("then", node.Then != null), ("else", node.Else != null), ("branches", node.Branches != null) })
                {
                    if (present)
                    {
                        errors.Add($"Node '{node.Id}' declares {name}, which belongs to a flow node; declare for, if or case.");
                    }
                }

                continue;
            }

            if (keys.Count > 1)
            {
                errors.Add($"Node '{node.Id}' declares {string.Join(", ", keys)}; a node is one flow node — for, if or case.");
                continue;
            }

            var kind = keys[0] switch { "for" => FlowKind.For, "if" => FlowKind.If, _ => FlowKind.Case };

            foreach (var (name, present) in new[]
            {
                ("format", node.Format != null), ("prompt", node.Prompt != null), ("with", node.With != null), ("onInvalid", node.OnInvalid != null),
                ("failIfEmpty", node.FailIfEmpty != null), ("reproducible", node.Reproducible != null), ("macros", node.Macros != null),
            })
            {
                if (present)
                {
                    errors.Add($"Node '{node.Id}' declares {keys[0]} and {name}; a flow node is structural.");
                }
            }

            var foreign = kind switch
            {
                FlowKind.For => new[] { ("then", node.Then != null), ("else", node.Else != null), ("branches", node.Branches != null) },
                FlowKind.If => new[] { ("nodes", node.Nodes != null), ("ret", node.Ret != null), ("branches", node.Branches != null) },
                _ => new[] { ("nodes", node.Nodes != null), ("ret", node.Ret != null), ("then", node.Then != null) },
            };
            var kindWord = kind switch { FlowKind.For => "a for", FlowKind.If => "an if", _ => "a case" };

            foreach (var (name, present) in foreign)
            {
                if (present)
                {
                    errors.Add($"Node '{node.Id}' declares {name}, which {kindWord} node does not carry.");
                }
            }

            var bodies = new List<BodyAt26>();
            TemplateFan? fan = null;
            WorkflowDataCollection? folder = null;
            string? expression = null;

            switch (kind)
            {
                case FlowKind.For:
                    (fan, folder) = ResolveLoopCollectionAt26(node.Id, node.For!, snippets, inputsById, errors);
                    if (node.Nodes is not { Count: > 0 })
                    {
                        errors.Add($"Loop '{node.Id}' has no body; name the nodes that run per item in nodes.");
                    }

                    bodies.Add(new BodyAt26("body", node.Nodes ?? new List<string>(), node.Ret, null));
                    break;

                case FlowKind.If:
                    expression = node.If;
                    if (node.Then is null)
                    {
                        errors.Add($"Node '{node.Id}' declares if without then; an if node runs a then body.");
                    }
                    else
                    {
                        bodies.Add(new BodyAt26("then", node.Then.Nodes ?? new List<string>(), node.Then.Ret, null));
                    }

                    if (node.Else is not null)
                    {
                        bodies.Add(new BodyAt26("else", node.Else.Nodes ?? new List<string>(), node.Else.Ret, null));
                    }

                    break;

                default:
                    expression = node.Case;
                    if (node.Branches is not { Count: > 0 })
                    {
                        errors.Add($"Node '{node.Id}' declares case without branches; a case node runs one branch per set of values.");
                    }

                    var index = 0;
                    foreach (var branch in node.Branches ?? new List<WorkflowCaseBranchSpec>())
                    {
                        index++;
                        if (branch.When is not { Count: > 0 })
                        {
                            errors.Add($"Node '{node.Id}' branch {index} lists no value; a branch's when names one or more values.");
                        }

                        bodies.Add(new BodyAt26($"branch {index}", branch.Nodes ?? new List<string>(), branch.Ret, branch.When));
                    }

                    if (node.Else is not null)
                    {
                        bodies.Add(new BodyAt26("else", node.Else.Nodes ?? new List<string>(), node.Else.Ret, null));
                    }

                    break;
            }

            flows[node.Id] = new FlowAt26(node, kind, bodies, fan, folder, expression);
        }

        // The bodies: declared plain nodes, each attached once, a loop's body
        // never a flow node (nesting waits, § 6); ret required and a member.
        var owner = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var flow in flows.Values)
        {
            foreach (var body in flow.Bodies)
            {
                var subject = flow.BodySubject(body);
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var id in body.Nodes)
                {
                    if (!seen.Add(id))
                    {
                        errors.Add($"{subject} lists node '{id}' twice; a body is a set.");
                        continue;
                    }

                    if (!declared.ContainsKey(id))
                    {
                        errors.Add($"{subject} names node '{id}', which the package does not declare.");
                        continue;
                    }

                    if (flows.TryGetValue(id, out var inner))
                    {
                        errors.Add(inner.Kind == FlowKind.For
                            ? $"Loop '{id}' runs inside {flow.Inside}; nesting waits for the scheduler redesign."
                            : $"Node '{id}' runs inside {flow.Inside}; at specVersion 26 a body holds plain nodes — a flow node inside a body is a later issue.");
                        continue;
                    }

                    if (owner.TryGetValue(id, out var first))
                    {
                        errors.Add($"Node '{id}' runs inside {flows[first].Inside} and inside {flow.Inside}; a node runs in one body.");
                        continue;
                    }

                    owner[id] = flow.Node.Id;
                }

                if (body.Ret is null)
                {
                    errors.Add($"{subject} returns nothing; name the body node the outside reads with ret.");
                }
                else if (body.RetNodeId is null)
                {
                    errors.Add($"{subject} ret '{body.Ret}' must be node.<id>.");
                }
                else if (!body.Nodes.Contains(body.RetNodeId, StringComparer.Ordinal))
                {
                    errors.Add($"{subject} returns node.{body.RetNodeId}, which is not in its body (nodes: {string.Join(", ", body.Nodes)}).");
                }
            }
        }

        return new FlowsAt26(flows, owner);
    }

    /// <summary><c>for: "item in snippet.&lt;folder&gt;"</c> or <c>"item in input.&lt;id&gt;"</c> (an array); the fan the loop's body runs under.</summary>
    private static (TemplateFan? Fan, WorkflowDataCollection? Folder) ResolveLoopCollectionAt26(
        string loopId,
        string text,
        WorkflowPackageData snippets,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors)
    {
        const string prefix = "item in ";
        var grammar = $"Loop '{loopId}' iterates '{text}'; a loop iterates \"item in snippet.<folder>\" or \"item in input.<id>\".";

        if (!text.StartsWith(prefix, StringComparison.Ordinal) || text.Length == prefix.Length)
        {
            errors.Add(grammar);
            return (null, null);
        }

        var reference = text[prefix.Length..];

        if (reference.Contains(' ') || TemplateObjectReference.IsColonSpelling(reference)
            || !TemplateObjectReference.TryParse(reference, out var obj, out var id, out var rest))
        {
            errors.Add(grammar);
            return (null, null);
        }

        if (obj == TemplateObjects.Node)
        {
            // § 6: a count the run discovers is the next bump's.
            errors.Add($"Loop '{loopId}' iterates {reference}; a loop over a node's value waits for the scheduler redesign — iterate an input or a snippet folder.");
            return (null, null);
        }

        if (rest.Length > 0 || (obj != TemplateObjects.Snippet && obj != TemplateObjects.Input))
        {
            errors.Add(grammar);
            return (null, null);
        }

        if (obj == TemplateObjects.Snippet)
        {
            if (snippets.Collections.TryGetValue(id, out var folder))
            {
                return (new SnippetFolderFan(id), folder);
            }

            errors.Add(snippets.Scalars.ContainsKey(id)
                ? $"Loop '{loopId}' iterates snippet '{id}', which is a file, not a folder."
                : $"Loop '{loopId}' iterates snippet folder '{id}', which the package does not declare.");
            return (null, null);
        }

        if (!inputsById.TryGetValue(id, out var declaration))
        {
            errors.Add($"Loop '{loopId}' iterates undeclared input '{id}'.");
            return (null, null);
        }

        if (WorkflowInputTypes.Of(declaration) != WorkflowInputTypes.Array)
        {
            errors.Add($"Loop '{loopId}' iterates input '{id}', which is a {WorkflowInputTypes.Of(declaration)}; only an array can be iterated.");
            return (null, null);
        }

        var element = WorkflowDeclarationNode.Of(declaration).Items ?? new WorkflowDeclarationNode(WorkflowInputTypes.Text, declaration.Label, false, null, null, null);
        return (new InputArrayFan(element), null);
    }

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
        FlowsAt26 flows,
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

            var (fan, folder) = flows.Owner.TryGetValue(node.Id, out var ownerId) && flows.Flows.TryGetValue(ownerId, out var loop)
                ? (loop.Fan, loop.Folder)
                : (null, null);

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
        FlowsAt26 flows,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors)
    {
        const string rule = "a case switches on an enum input (input.<id>) or an enum-format node's value (node.<id>.value)";

        foreach (var flow in flows.Flows.Values.Where(f => f.Kind == FlowKind.Case && f.Expression is not null))
        {
            var subject = flow.Expression!;
            IReadOnlyList<string>? values = null;

            if (TemplateObjectReference.IsBare(subject, TemplateObjects.Input, out var inputId))
            {
                // An undeclared input is the walker's sentence when the branches' bodies read it; here only the type.
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

    /// <summary>
    /// What a template reads of a flow node, until the second PR gives the
    /// walker the loop's shape: a loop is its returned node's text, concatenated
    /// (the aggregator's shape — <c>.content</c> reads, <c>.value</c> refused); an
    /// if/case is its returned node's format when every branch returns one.
    /// </summary>
    private static TemplateNodeShape FlowShapeAt26(FlowAt26 flow, IReadOnlyDictionary<string, NodeAt25> nodes, IReadOnlyDictionary<string, WorkflowInputSpec> inputsById)
    {
        if (flow.Kind == FlowKind.For)
        {
            return TemplateNodeShape.Aggregator();
        }

        var returned = flow.Bodies
            .Select(b => b.RetNodeId is { } id && nodes.TryGetValue(id, out var node) ? node : null)
            .ToList();

        if (returned.Count > 0 && returned.All(n => n?.Format is not null)
            && returned.Select(n => n!.Format!.Id).Distinct(StringComparer.Ordinal).Count() == 1)
        {
            var node = returned[0]!;
            return new TemplateNodeShape(node.Format!.Id, node.Format.Spec.Type, node.Format.Spec.Schema, EnumValuesAt25(node, inputsById));
        }

        return TemplateNodeShape.Aggregator();
    }

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
        FlowsAt26 flows,
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

            if (flow.Kind == FlowKind.Case && flow.Expression is { } subject && CaseNodeSubjectOf(subject) is { } nodeId && nodes.ContainsKey(nodeId))
            {
                targets.Add(nodeId);
            }
        }

        return edges;
    }

    /// <summary>
    /// § 1.2: a body node is read by bare id only from inside its body — the
    /// flow node is the only name the outside sees; two loops' bodies do not read
    /// each other; a body node does not read the loop it runs in.
    /// </summary>
    private static void ValidateFlowEdgesAt26(FlowsAt26 flows, IReadOnlyDictionary<string, HashSet<string>> edges, List<string> errors)
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
    private static void ValidateConditionsAt26(WorkflowDerivedEdges.Walk walk, FlowsAt26 flows, List<string> errors)
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

    /// <summary>§ 1.2 (#1067 inside a body): a body node is returned or read by a sibling, or it runs for nothing.</summary>
    private static void ValidateBodyReachabilityAt26(
        FlowsAt26 flows,
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
                        errors.Add(flow.Kind == FlowKind.For
                            ? $"Node '{id}' in loop '{flow.Node.Id}' is neither returned nor read; name it with ret or read it from a body node."
                            : $"Node '{id}' in node '{flow.Node.Id}' {body.Label} is neither returned nor read; name it with ret or read it from a body node.");
                    }
                }
            }
        }
    }

    /// <summary>The flow nodes' probes: a loop as text (the aggregator's), an if/case as its returned node's shape when the branches agree.</summary>
    private static void ProbeFlowNodesAt26(
        ScriptObject node,
        FlowsAt26 flows,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById)
    {
        foreach (var flow in flows.Flows.Values)
        {
            var shape = FlowShapeAt26(flow, nodes, inputsById);

            if (shape.Aggregates)
            {
                node[flow.Node.Id] = new ScriptObject { ["content"] = "placeholder" };
                continue;
            }

            var value = shape.Type == WorkflowInputTypes.Enum && shape.Schema is null
                ? TemplateValueShape.Text
                : TemplateValueShape.FromSchema(shape.Schema);
            node[flow.Node.Id] = new ScriptObject { ["content"] = "placeholder", ["value"] = ProbeFromShape(value) };
        }
    }
}
