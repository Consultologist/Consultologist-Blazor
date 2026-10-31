using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1071: the scope a template is PREVIEWED over, at edit time, when no run
/// exists — every value a run would supply is a token naming its read
/// (<c>⟨input.notes⟩</c>, <c>⟨node.extract.content⟩</c>, <c>⟨profile.name⟩</c>),
/// while the package's own texts — snippets, folder items, macro texts and
/// instructions — render for real over the same scope. Rendering
/// <c>{{ macro.&lt;id&gt;.prompt }}</c> over it through <see cref="TemplateRenderer"/>
/// gives exactly what the engine composes for an inline macro: the instruction
/// rendered, a blank line, the mechanics sentence (<see cref="WorkflowMacroMechanics"/>)
/// — the editor's "what the model is told".
///
/// Not the publish desk's render probe (WorkflowPackageValidator.RenderProbeAt25),
/// which fills every value with the word "placeholder" to prove a template
/// renders strictly and reports only failures; this scope is for reading. A
/// structured value (an object or array input, an object or array format's
/// <c>.value</c>) is a JSON carrier whose leaves are tokens, so a path read
/// renders the path's token; a date, number or boolean input renders as its
/// token too (the renderer types a string that does not parse as the string).
/// </summary>
public static class TemplatePreviewScope
{
    /// <summary>The token for a read: <c>⟨path⟩</c>.</summary>
    public static string Token(string path) => $"⟨{path}⟩";

    /// <summary>
    /// Every renderer pipe: over a token operand (the node's text as this scope makes
    /// it) it renders a token naming the pipe and the operand's token; over real text
    /// (#1138: a preview pick put the sample concept list in the node's place) it renders
    /// the engine's bytes through the spec's own render function, with the concept source
    /// mapped as the engine maps it (<see cref="WorkflowNodeDefaults.WellKnownConceptSources"/>;
    /// the token keeps the raw node id, since it names a path).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Func<TemplateNodeOutput, string>> Pipes =
        TemplatePipes.Specs.ToDictionary(
            spec => spec.Name,
            spec => (Func<TemplateNodeOutput, string>)(output =>
                output.Text.StartsWith('⟨')
                    ? Token($"{spec.Name} of {TemplateObjects.Node}.{output.ConceptSource}.{TemplateMembers.Content}")
                    : spec.Render(output.Text, WorkflowNodeDefaults.WellKnownConceptSources.GetValueOrDefault(output.ConceptSource ?? "node", output.ConceptSource ?? "node"))),
            StringComparer.Ordinal);

    // Tokens stay readable inside a JSON carrier (no \u27E8 escapes).
    private static readonly JsonSerializerOptions CarrierJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The token scope of a package: its declarations, formats, snippets and macros, with every run value a token.</summary>
    public static TemplateRenderScope Tokens(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files, WorkflowFormatCatalog? formats)
    {
        // Every declared input (WorkflowVariableDeclarations.For keeps only the
        // typed ones — the renderer's conversions; a text input needs none).
        var declared = (manifest.Inputs ?? new List<WorkflowInputSpec>()).DistinctBy(i => i.Id, StringComparer.Ordinal).ToList();
        var declarations = WorkflowVariableDeclarations.For(manifest);
        var inputTypes = declared.ToDictionary(input => input.Id, WorkflowInputTypes.Of, StringComparer.Ordinal);
        var inputs = declared.ToDictionary(
            input => input.Id,
            input => InputCarrier($"{TemplateObjects.Input}.{input.Id}", WorkflowDeclarationNode.Of(input)),
            StringComparer.Ordinal);

        var localSpecs = new Dictionary<string, WorkflowFormatSpec>(StringComparer.Ordinal);
        var discard = new List<string>();
        foreach (var format in manifest.Formats ?? new List<WorkflowFormatReference>())
        {
            if (format.File is not null && files.TryGetValue(format.File, out var json)
                && WorkflowFormatSpecJson.TryRead(json, $"Format '{format.Id}'", discard, out var spec))
            {
                localSpecs[format.Id] = spec!;
            }
        }

        WorkflowFormatSpec? SpecOf(string? formatId) =>
            formatId is null ? null
            : localSpecs.TryGetValue(formatId, out var local) ? local
            : formats is not null && formats.TryGet(formatId, out var catalogue) ? catalogue!.Spec
            : null;

        // .content is its token; .value, on an object or array format, a carrier
        // with token leaves; an enum node's content IS its value, one token for both.
        TemplateNodeOutput NodeToken(string path, WorkflowNodeSpec node)
        {
            var spec = node.Aggregate is { Count: > 0 } ? null : SpecOf(node.Format);
            var type = spec?.Type ?? WorkflowInputTypes.Text;
            var valueJson = type is WorkflowInputTypes.Object or WorkflowInputTypes.Array
                ? ShapeCarrier($"{path}.{TemplateMembers.Value}", TemplateValueShape.FromSchema(spec!.Schema)).ToJsonString(CarrierJson)
                : null;
            var text = Token($"{path}.{(type == WorkflowInputTypes.Enum ? TemplateMembers.Value : TemplateMembers.Content)}");
            return new TemplateNodeOutput(text, type, node.Id, valueJson);
        }

        var nodes = new Dictionary<string, TemplateNodeOutput>(StringComparer.Ordinal);
        foreach (var node in (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal))
        {
            nodes[node.Id] = NodeToken($"{TemplateObjects.Node}.{node.Id}", node);
        }

        // #1105: at 26 the texts carry headers — the scope reads bodies and the
        // header's label (the renderer's own 26 members are #1157's).
        var texts = manifest.SpecVersion >= 26 ? WorkflowTextHeaders.Resolve(manifest, files, discard) : null;
        if (texts is not null)
        {
            manifest = texts.Manifest;
            files = texts.Files;
        }
        var content = texts is null
            ? WorkflowDataResolver.ResolveContent(manifest, files, discard)
            : WorkflowDataResolver.ResolveContent(manifest, texts, discard);
        var folders = content.Collections.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<TemplateFolderItem>)entry.Value.Items
                .Select(item => new TemplateFolderItem(
                    item.Id,
                    item.Fields.GetValueOrDefault(TemplateMembers.Name) ?? item.Fields.GetValueOrDefault(TemplateMembers.Label, item.Id),
                    item.Fields.GetValueOrDefault(TemplateMembers.Content, string.Empty)))
                .ToList(),
            StringComparer.Ordinal);

        // #1243 (decided 2026-10-30): a loop's elements — one per folder item (the folder's
        // real items, their real labels), one placeholder for an array input (its items exist
        // only at run time); each element's body nodes as tokens on the element's path, the
        // renderer's own spelling (node.<loop>[<item>].<body>); the loop's own token kept as
        // its composed text. An inner if/case's per-item output starts as its own token —
        // Judged replaces it with the taken branch's, or marks the item skipped.
        var loops = LoopsOf(manifest, declared, nodes, folders, NodeToken);

        // #1099: a macro folder's items at 26, each with its header's label and instruction.
        var macroFolders = texts is null ? null : WorkflowTextHeaders.ResolveMacroFolders(manifest, texts, discard)?.Collections.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<TemplateMacroSource>)entry.Value.Items
                .Select(item => new TemplateMacroSource(item.Id, item.Header?.Label ?? item.Id, item.Fields.GetValueOrDefault(TemplateMembers.Content, string.Empty), item.Header?.Instruction ?? string.Empty, item.Header?.If))
                .ToList(),
            StringComparer.Ordinal);

        var macros = (manifest.Macros ?? new List<WorkflowMacroSpec>())
            .Where(macro => macro.File is not null && !macro.File.EndsWith('/'))
            .DistinctBy(macro => macro.Id, StringComparer.Ordinal)
            .ToDictionary(
                macro => macro.Id,
                // #1205: the macro's own gate rides the source (the header's), as the engine's scope has it.
                macro => new TemplateMacroSource(macro.Id, macro.Label ?? macro.Id, files.GetValueOrDefault(macro.File!, string.Empty), macro.Instruction ?? string.Empty, texts?.Headers.GetValueOrDefault(macro.File!)?.If),
                StringComparer.Ordinal);

        return new TemplateRenderScope(
            inputs,
            inputTypes,
            declarations,
            nodes,
            content.Scalars,
            folders,
            // #1207: at 26 the fan item's name member is label — the token says so.
            new TemplateFanItem(Token($"{TemplateObjects.Item}.{TemplateMembers.Id}"), Token($"{TemplateObjects.Item}.{(manifest.SpecVersion >= 26 ? TemplateMembers.Label : TemplateMembers.Name)}"), Text: Token($"{TemplateObjects.Item}.{TemplateMembers.Content}")),
            macros,
            new TemplateRunFacts(Token("run.date"), Token("run.job"), Token("run.package"), Token("run.host")),
            new TemplateProfileFacts(Token("profile.name"), Token("profile.signature")),
            MacroFolders: macroFolders,
            Loops: loops,
            // #1207 (v26 design § 5): the preview renders the 26 members — item.label, a folder
            // item's .label, a macro's .label/.instruction — as the engine's render does, and a
            // standalone snippet's label is its header's (the #1179 slice the preview lacked).
            Labelled: manifest.SpecVersion >= 26,
            SnippetLabels: texts is null
                ? null
                : (manifest.Snippets ?? new Dictionary<string, string>())
                    .Where(entry => !entry.Value.EndsWith('/') && texts.Headers.GetValueOrDefault(entry.Value)?.Label is not null)
                    .ToDictionary(entry => entry.Key, entry => texts.Headers[entry.Value].Label!, StringComparer.Ordinal));
    }

    // ----- #1205 (v26 design § 2, § 5.4, § 12): the three gates judged over a preview pick -----

    /// <summary>
    /// The gates a preview may judge, from the walk's conditions: a node instance's `if`
    /// (owner the node, key the macro), a macro's own `if` (key the id or &lt;folder&gt;/&lt;item&gt;),
    /// a standalone snippet's `if` (key the id), a folder item's `if` (owner the folder, key
    /// &lt;folder&gt;/&lt;item&gt;). An instance with no gate is listed with an empty `If` (it holds,
    /// and keeps its macro's read live — the engine's rule) and is never applied. Empty below 26.
    /// </summary>
    public static IReadOnlyList<TemplatePreviewGate> Gates(WorkflowPackageManifest manifest, WorkflowDerivedEdges.Walk walk)
    {
        if (manifest.SpecVersion < 26)
        {
            return Array.Empty<TemplatePreviewGate>();
        }

        var gates = new List<TemplatePreviewGate>();
        var walked = walk.Conditions.Where(c => c.Walk.IsValid).ToList();

        static TemplatePreviewGate Of(TemplatePreviewGateKind kind, string owner, string key, WorkflowDerivedEdges.WalkedCondition condition) =>
            new(kind, owner, key, condition.Text.Trim(), condition.Walk.References.Inputs, condition.Walk.References.Nodes.Keys.ToHashSet(StringComparer.Ordinal), condition.Walk.References.ItemMembers.Count > 0);

        foreach (var condition in walked)
        {
            switch (condition.Site)
            {
                case WorkflowDerivedEdges.ConditionSite.Macro:
                    gates.Add(Of(TemplatePreviewGateKind.Macro, condition.OwnerId, condition.OwnerId, condition));
                    break;
                case WorkflowDerivedEdges.ConditionSite.Snippet:
                    gates.Add(Of(TemplatePreviewGateKind.Snippet, condition.OwnerId, condition.OwnerId, condition));
                    break;
                case WorkflowDerivedEdges.ConditionSite.FolderItem when condition.OwnerId.IndexOf('/') is var slash && slash > 0:
                    gates.Add(Of(TemplatePreviewGateKind.Item, condition.OwnerId[..slash], condition.OwnerId, condition));
                    break;
            }
        }

        // #1236 (v27 design § 6; decided 2026-10-29): a flow node's gate — an if's
        // expression (its walked Flow condition) or a case's subject — judged for
        // a branch node's preview: a top-level flow over the picks, one inside a
        // loop over the picked item too. The gate carries the loop it runs in and
        // its bodies, so the judge names the branch taken and whether the reader runs.
        var inputsById = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .Where(input => WorkflowDeclaredIds.IsValid(input.Id))
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var flows = WorkflowFlowNodes.Resolve(
            manifest,
            new WorkflowPackageData(new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, WorkflowDataCollection>(StringComparer.Ordinal)),
            inputsById,
            new List<string>());

        foreach (var flow in flows.Flows.Values.Where(f => f.Kind != WorkflowFlowNodes.Kind.For))
        {
            var bodies = flow.Bodies.Select(b => new TemplatePreviewBranch(b.Label, b.Nodes, b.RetNodeId, b.When)).ToList();
            var loop = flows.LoopOf(flow.Node.Id);

            if (flow.Kind == WorkflowFlowNodes.Kind.If)
            {
                if (walked.FirstOrDefault(c => c.Site == WorkflowDerivedEdges.ConditionSite.Flow && c.OwnerId == flow.Node.Id) is { } condition)
                {
                    gates.Add(Of(TemplatePreviewGateKind.Flow, flow.Node.Id, flow.Node.Id, condition) with { Loop = loop, Bodies = bodies, IsCase = false });
                }

                continue;
            }

            // A case subject is read structurally: an input, a node's value, or the item's field.
            var subject = (flow.Expression ?? string.Empty).Trim();
            var inputs = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            if (TemplateObjectReference.IsBare(subject, TemplateObjects.Input, out var inputId))
            {
                inputs.Add(inputId);
            }
            else if (WorkflowFlowNodes.CaseNodeSubjectOf(subject) is { } nodeId)
            {
                nodes.Add(nodeId);
            }

            gates.Add(new TemplatePreviewGate(TemplatePreviewGateKind.Flow, flow.Node.Id, flow.Node.Id, subject, inputs, nodes,
                WorkflowFlowNodes.CaseItemSubjectOf(subject) is not null, loop, bodies, IsCase: true));
        }

        // The instances from the manifest: a gated one takes its walked reads, an ungated one holds.
        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            foreach (var instance in node.Macros ?? new List<WorkflowNodeMacroSpec>())
            {
                var gate = (instance.If ?? instance.When)?.Trim();
                if (string.IsNullOrWhiteSpace(gate))
                {
                    gates.Add(new TemplatePreviewGate(TemplatePreviewGateKind.Instance, node.Id, instance.Id, string.Empty,
                        new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), false));
                }
                else if (walked.FirstOrDefault(c => c.Site == WorkflowDerivedEdges.ConditionSite.NodeInstance && c.OwnerId == node.Id && c.MacroId == instance.Id && c.Text.Trim() == gate) is { } condition)
                {
                    gates.Add(Of(TemplatePreviewGateKind.Instance, node.Id, instance.Id, condition));
                }
            }
        }

        return gates;
    }

    /// <summary>
    /// The gates judged over a picked scope, the engine's way (#1175/#1177/#1179): a gate is
    /// judged only once every input, node value and item it reads has a pick (decided
    /// 2026-10-27 — an unchosen token compares false and would gate everything off); the rest
    /// wait, naming the missing picks. Instances are the reader node's alone (no reader, none
    /// judged); a folder item's gate is judged with the item bound. A gate that throws (a stale
    /// text) holds. Without a gate the scope is returned as given — the bytes are unchanged.
    /// </summary>
    public static TemplatePreviewJudgement Judged(
        TemplateRenderScope picked,
        IReadOnlyList<TemplatePreviewGate> gates,
        IReadOnlySet<string> pickedInputs,
        IReadOnlySet<string> pickedNodes,
        bool itemPicked,
        string? readerNodeId,
        string? itemId)
    {
        if (gates.Count == 0)
        {
            return new TemplatePreviewJudgement(picked, Array.Empty<TemplatePreviewGate>(), Array.Empty<TemplatePreviewWaitingGate>());
        }

        var ready = new List<TemplatePreviewGate>();
        var waiting = new List<TemplatePreviewWaitingGate>();
        var perElement = new List<TemplatePreviewGate>();
        foreach (var gate in gates)
        {
            if (gate.Kind == TemplatePreviewGateKind.Instance && gate.Owner != readerNodeId)
            {
                continue;
            }

            // #1236: a flow gate is the branch nodes' — judged on a reader its bodies hold.
            // #1243: and, inside a loop whose elements the scope carries, judged once per
            // element for any reader that iterates the loop — the item is each element's, so
            // no item pick is waited on for that half.
            if (gate.Kind == TemplatePreviewGateKind.Flow)
            {
                var inBodies = readerNodeId is not null && gate.Bodies?.Any(b => b.Nodes.Contains(readerNodeId, StringComparer.Ordinal)) == true;
                var hasElements = gate.Loop is { } loopOf && picked.Loops?.ContainsKey(loopOf) == true;
                if (!inBodies && !hasElements)
                {
                    continue;
                }

                var flowMissing = new List<string>();
                flowMissing.AddRange(gate.Inputs.Where(id => !pickedInputs.Contains(id)).Order(StringComparer.Ordinal).Select(id => $"{TemplateObjects.Input}.{id}"));
                flowMissing.AddRange(gate.Nodes.Where(id => !pickedNodes.Contains(id)).Order(StringComparer.Ordinal).Select(id => $"{TemplateObjects.Node}.{id}"));
                if (flowMissing.Count == 0 && hasElements)
                {
                    perElement.Add(gate);
                }

                if (inBodies && gate.ReadsItem && !itemPicked)
                {
                    flowMissing.Add(TemplateObjects.Item);
                }

                if (!inBodies)
                {
                    if (flowMissing.Count > 0)
                    {
                        waiting.Add(new TemplatePreviewWaitingGate(gate, flowMissing));
                    }

                    continue;
                }

                if (flowMissing.Count > 0)
                {
                    waiting.Add(new TemplatePreviewWaitingGate(gate, flowMissing));
                }
                else
                {
                    ready.Add(gate);
                }

                continue;
            }

            var missing = new List<string>();
            missing.AddRange(gate.Inputs.Where(id => !pickedInputs.Contains(id)).Order(StringComparer.Ordinal).Select(id => $"{TemplateObjects.Input}.{id}"));
            missing.AddRange(gate.Nodes.Where(id => !pickedNodes.Contains(id)).Order(StringComparer.Ordinal).Select(id => $"{TemplateObjects.Node}.{id}"));
            if (gate.ReadsItem && !itemPicked && gate.Kind != TemplatePreviewGateKind.Item)
            {
                missing.Add(TemplateObjects.Item);
            }

            if (missing.Count > 0)
            {
                waiting.Add(new TemplatePreviewWaitingGate(gate, missing));
            }
            else
            {
                ready.Add(gate);
            }
        }

        // The notes read in the engine's order: instances, macros, snippets, items.
        waiting = waiting.OrderBy(w => w.Gate.Kind).ToList();
        var applied = new List<TemplatePreviewGate>();
        IReadOnlySet<string> suppressed = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlySet<string> inertMacros = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlySet<string> inertSnippets = new HashSet<string>(StringComparer.Ordinal);

        static IReadOnlySet<string> Judge(Func<IReadOnlySet<string>> judge)
        {
            try
            {
                return judge();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Scriban.Syntax.ScriptRuntimeException)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        if (readerNodeId is not null)
        {
            var instances = ready.Where(g => g.Kind == TemplatePreviewGateKind.Instance).ToList();
            suppressed = Judge(() => WorkflowInlineGates.Suppressed(readerNodeId, instances.Select(g => new WorkflowInlineGate(g.Key, g.If.Length > 0 ? g.If : null)), itemId, picked, Pipes));
            applied.AddRange(instances.Where(g => g.If.Length > 0 && suppressed.Contains(g.Key)));
        }

        var macros = ready.Where(g => g.Kind == TemplatePreviewGateKind.Macro).ToList();
        inertMacros = Judge(() => WorkflowInlineGates.Inert(macros.Select(g => (g.Key, (string?)g.If)), picked, Pipes));
        applied.AddRange(macros.Where(g => inertMacros.Contains(g.Key)));

        var snippets = ready.Where(g => g.Kind == TemplatePreviewGateKind.Snippet).ToList();
        var inert = new HashSet<string>(Judge(() => WorkflowInlineGates.InertSnippets(snippets.Select(g => (g.Key, (string?)g.If)), picked, Pipes)), StringComparer.Ordinal);
        applied.AddRange(snippets.Where(g => inert.Contains(g.Key)));

        // A folder item's gate is judged with the item bound (the starter's fan filter).
        foreach (var gate in ready.Where(g => g.Kind == TemplatePreviewGateKind.Item))
        {
            var item = picked.Folders.GetValueOrDefault(gate.Owner)?.FirstOrDefault(i => i.Id == gate.Key[(gate.Owner.Length + 1)..]);
            if (item is null)
            {
                continue;
            }

            var bound = picked with { Item = new TemplateFanItem(item.Id, item.Name, Text: item.Text) };
            var holds = Judge(() => WorkflowConditionAt25.Holds($"Snippet folder '{gate.Owner}' item '{item.Id}' if", gate.If, bound, Pipes) ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal) { gate.Key });
            if (holds.Count > 0)
            {
                inert.Add(gate.Key);
                applied.Add(gate);
            }
        }

        inertSnippets = inert;

        // #1236: a flow gate decided over the picked scope (the item bound when the
        // reader's loop has a pick): the branch taken, and whether the reader runs.
        var flowOutcomes = new List<TemplatePreviewFlowOutcome>();
        foreach (var gate in ready.Where(g => g.Kind == TemplatePreviewGateKind.Flow && g.Bodies is not null))
        {
            var bodies = gate.Bodies!.Select(b => new WorkflowFlowNodes.Body(b.Label, b.Nodes, b.Ret is { } ret ? TemplateObjectReference.Node(ret) : null, b.Values)).ToList();
            WorkflowFlowDecision.Outcome? outcome;
            try
            {
                outcome = WorkflowFlowDecision.Decide(gate.Key, gate.IsCase ? WorkflowFlowNodes.Kind.Case : WorkflowFlowNodes.Kind.If, gate.If, bodies, picked, Pipes);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Scriban.Syntax.ScriptRuntimeException)
            {
                outcome = null;
            }

            if (outcome is null)
            {
                continue;
            }

            var runs = outcome.Taken?.Nodes.Contains(readerNodeId!, StringComparer.Ordinal) == true;
            flowOutcomes.Add(new TemplatePreviewFlowOutcome(gate, outcome.Branch, runs));
            if (!runs)
            {
                applied.Add(gate);
            }
        }

        // #1243: the loops' elements judged — an inert folder item leaves the loop's elements
        // as it leaves the run's collection; an inner if/case decided once per element with
        // that element's item bound: a skipped item joins the loop's skipped set (its content
        // and value null in the element), a taken branch's returned node becomes the flow's
        // per-item output (e.content, e.<flow>.content). A placeholder element (an array
        // input's) cannot decide a condition that reads the item — its tokens would compare
        // false and skip everything — so the element stays taken and the pane says undecided.
        var elementOutcomes = new List<TemplatePreviewElementOutcome>();
        if (picked.Loops is { Count: > 0 } loops)
        {
            var judgedLoops = new Dictionary<string, TemplateLoopOutput>(loops, StringComparer.Ordinal);
            foreach (var (loopId, loop) in loops)
            {
                if (loop.Collection is { } collection && collection.StartsWith(TemplateObjectReference.SnippetPrefix, StringComparison.Ordinal))
                {
                    var folderId = collection[TemplateObjectReference.SnippetPrefix.Length..];
                    var present = loop.Items.Where(item => !inertSnippets.Contains($"{folderId}/{item.Id}")).ToList();
                    if (present.Count != loop.Items.Count)
                    {
                        judgedLoops[loopId] = loop with { Items = present };
                    }
                }
            }

            foreach (var gate in perElement)
            {
                var loopId = gate.Loop!;
                var loop = judgedLoops[loopId];
                var inBodies = readerNodeId is not null && gate.Bodies!.Any(b => b.Nodes.Contains(readerNodeId, StringComparer.Ordinal));
                var branches = new List<KeyValuePair<string, string>>();
                var placeholder = loop.Items.All(item => item.Text is null);
                if (!(gate.ReadsItem && placeholder))
                {
                    var bodies = gate.Bodies!.Select(b => new WorkflowFlowNodes.Body(b.Label, b.Nodes, b.Ret is { } ret ? TemplateObjectReference.Node(ret) : null, b.Values)).ToList();
                    var kind = gate.IsCase ? WorkflowFlowNodes.Kind.Case : WorkflowFlowNodes.Kind.If;
                    var skipped = new HashSet<string>(loop.Skipped ?? new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
                    var body = loop.Body.ToDictionary(
                        pair => pair.Key,
                        pair => (IReadOnlyDictionary<string, TemplateNodeOutput>)new Dictionary<string, TemplateNodeOutput>(pair.Value, StringComparer.Ordinal),
                        StringComparer.Ordinal);
                    foreach (var item in loop.Items)
                    {
                        WorkflowFlowDecision.Outcome? outcome;
                        try
                        {
                            outcome = WorkflowFlowDecision.Decide(gate.Key, kind, gate.If, bodies, picked with { Item = item }, Pipes);
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or Scriban.Syntax.ScriptRuntimeException)
                        {
                            outcome = null;
                        }

                        if (outcome is null)
                        {
                            continue;
                        }

                        branches.Add(new KeyValuePair<string, string>(item.Id, outcome.Branch));
                        if (outcome.Taken is null)
                        {
                            skipped.Add(item.Id);
                            continue;
                        }

                        if (outcome.Taken.RetNodeId is { } taken
                            && body.TryGetValue(taken, out var takenRow) && takenRow.TryGetValue(item.Id, out var takenOutput)
                            && body.TryGetValue(gate.Key, out var flowRow))
                        {
                            ((Dictionary<string, TemplateNodeOutput>)flowRow)[item.Id] = takenOutput;
                        }
                    }

                    judgedLoops[loopId] = loop with { Body = body, Skipped = skipped.Count > 0 ? skipped : null };
                }

                if (!inBodies)
                {
                    elementOutcomes.Add(new TemplatePreviewElementOutcome(gate, loopId, branches));
                }
            }

            picked = picked with { Loops = judgedLoops };
        }

        return new TemplatePreviewJudgement(
            picked with { SuppressedPrompts = suppressed, InertMacros = inertMacros, InertSnippets = inertSnippets },
            applied,
            waiting,
            flowOutcomes,
            elementOutcomes);
    }

    /// <summary>#1243: the loops' elements (see <see cref="Tokens"/>); null below 26 or with no loop.</summary>
    private static IReadOnlyDictionary<string, TemplateLoopOutput>? LoopsOf(
        WorkflowPackageManifest manifest,
        IReadOnlyList<WorkflowInputSpec> declared,
        IReadOnlyDictionary<string, TemplateNodeOutput> nodes,
        IReadOnlyDictionary<string, IReadOnlyList<TemplateFolderItem>> folders,
        Func<string, WorkflowNodeSpec, TemplateNodeOutput> nodeToken)
    {
        if (manifest.SpecVersion < 26 || manifest.Nodes is not { Count: > 0 })
        {
            return null;
        }

        var inputsById = declared
            .Where(input => WorkflowDeclaredIds.IsValid(input.Id))
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var nodesById = manifest.Nodes.DistinctBy(n => n.Id, StringComparer.Ordinal).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var flows = WorkflowFlowNodes.Resolve(
            manifest,
            new WorkflowPackageData(new Dictionary<string, string>(StringComparer.Ordinal), new Dictionary<string, WorkflowDataCollection>(StringComparer.Ordinal)),
            inputsById,
            new List<string>());

        var loops = new Dictionary<string, TemplateLoopOutput>(StringComparer.Ordinal);
        foreach (var flow in flows.Flows.Values.Where(f => f.Kind == WorkflowFlowNodes.Kind.For))
        {
            var loopId = flow.Node.Id;
            var body = flow.Bodies.FirstOrDefault();
            var collection = flow.Collection;
            if (body?.RetNodeId is not { } retId || collection is null || !nodes.TryGetValue(loopId, out var output))
            {
                continue;
            }

            IReadOnlyList<TemplateFanItem> items;
            if (collection.StartsWith(TemplateObjectReference.SnippetPrefix, StringComparison.Ordinal))
            {
                var folderId = collection[TemplateObjectReference.SnippetPrefix.Length..];
                items = (folders.GetValueOrDefault(folderId) ?? Array.Empty<TemplateFolderItem>())
                    .Select(item => new TemplateFanItem(item.Id, item.Name, Text: item.Text))
                    .ToList();
            }
            else if (collection.StartsWith(TemplateObjectReference.InputPrefix, StringComparison.Ordinal))
            {
                // The one placeholder: positional id "0" (the run's own), its label a token, its
                // value a carrier with token leaves typed by the input's element declaration.
                var inputId = collection[TemplateObjectReference.InputPrefix.Length..];
                var element = inputsById.TryGetValue(inputId, out var input) ? WorkflowDeclarationNode.Of(input).Items : null;
                var valuePath = $"{TemplateObjects.Item}.{TemplateMembers.Value}";
                items = new[]
                {
                    new TemplateFanItem(
                        "0",
                        Token($"{TemplateObjects.Item}.{TemplateMembers.Label}"),
                        Value: element is null ? Token(valuePath) : DeclarationCarrier(valuePath, element).ToJsonString(CarrierJson),
                        Element: element),
                };
            }
            else
            {
                continue;
            }

            // The element's body nodes: the loop's body, and at 27 each inner if/case's branch
            // nodes (the inner flow itself is in the loop's body list already).
            var bodyIds = new List<string>(body.Nodes);
            foreach (var inner in flows.Flows.Values.Where(f => f.Kind != WorkflowFlowNodes.Kind.For && string.Equals(flows.LoopOf(f.Node.Id), loopId, StringComparison.Ordinal)))
            {
                bodyIds.AddRange(inner.BodyNodeIds);
            }

            var path = $"{TemplateObjects.Node}.{loopId}";
            var bodyOutputs = new Dictionary<string, IReadOnlyDictionary<string, TemplateNodeOutput>>(StringComparer.Ordinal);
            foreach (var bodyId in bodyIds.Distinct(StringComparer.Ordinal))
            {
                if (!nodesById.TryGetValue(bodyId, out var spec))
                {
                    continue;
                }

                bodyOutputs[bodyId] = items.ToDictionary(item => item.Id, item => nodeToken($"{path}[{item.Id}].{bodyId}", spec), StringComparer.Ordinal);
            }

            loops[loopId] = new TemplateLoopOutput(retId, items, bodyOutputs, output, Collection: collection);
        }

        return loops.Count == 0 ? null : loops;
    }

    /// <summary>What the model is told where <c>macro.&lt;id&gt;.prompt</c> is read — the instruction rendered over the scope, then the mechanics sentence; the renderer's own sentence when the instruction does not render.</summary>
    public static string MacroPrompt(string macroId, TemplateRenderScope scope) =>
        TemplateRenderer.Render($"Macro '{macroId}' prompt", "{{ macro." + macroId + "." + TemplateMembers.Prompt + " }}", scope, Pipes);

    // A scalar input is its token; a structured one a carrier whose leaves are tokens.
    private static string InputCarrier(string path, WorkflowDeclarationNode node) =>
        node.Type is WorkflowInputTypes.Object or WorkflowInputTypes.Array
            ? DeclarationCarrier(path, node).ToJsonString(CarrierJson)
            : Token(path);

    private static JsonNode DeclarationCarrier(string path, WorkflowDeclarationNode node) => node.Type switch
    {
        WorkflowInputTypes.Object => new JsonObject((node.Fields ?? new List<WorkflowDeclarationNode>())
            .Select(field => new KeyValuePair<string, JsonNode?>(field.Id, DeclarationCarrier($"{path}.{field.Id}", field)))),
        WorkflowInputTypes.Array => new JsonArray(node.Items is null ? JsonValue.Create(Token($"{path}[0]")) : DeclarationCarrier($"{path}[0]", node.Items)),
        _ => JsonValue.Create(Token(path)),
    };

    private static JsonNode ShapeCarrier(string path, TemplateValueShape shape) =>
        shape.IsObject
            ? new JsonObject((shape.Fields ?? new Dictionary<string, TemplateValueShape>())
                .Select(field => new KeyValuePair<string, JsonNode?>(field.Key, ShapeCarrier($"{path}.{field.Key}", field.Value))))
            : shape.IsArray
                ? new JsonArray(shape.Items is null ? JsonValue.Create(Token($"{path}[0]")) : ShapeCarrier($"{path}[0]", shape.Items))
                : JsonValue.Create(Token(path));
}

/// <summary>#1205: the kind of gate a preview judges — a node instance's `if`, a macro's own, a standalone snippet's, a folder item's; #1236: a flow node's (an if's expression, a case's subject), judged for its branch nodes.</summary>
public enum TemplatePreviewGateKind { Instance, Macro, Snippet, Item, Flow }

/// <summary>
/// #1205: one gate a preview may judge — its kind, its owner (the node of an instance, the folder of
/// an item, else the key), its key (a macro id, a snippet id, &lt;folder&gt;/&lt;item&gt;), its text (empty for
/// an ungated instance, which holds), and what it reads: the inputs, the nodes, whether it reads the
/// fan item. A gate is judged only once every read has a pick. #1236: a flow gate (owner and key the
/// flow node) carries the loop it runs inside (null at the top level) and its bodies, so the judge
/// names the branch taken and whether the reader runs; <c>IsCase</c> says the text is a case subject.
/// </summary>
public sealed record TemplatePreviewGate(
    TemplatePreviewGateKind Kind, string Owner, string Key, string If, IReadOnlySet<string> Inputs, IReadOnlySet<string> Nodes, bool ReadsItem,
    string? Loop = null, IReadOnlyList<TemplatePreviewBranch>? Bodies = null, bool IsCase = false);

/// <summary>#1236: one body of a flow gate — its label (then, else, branch n), its nodes, its returned node and (a case branch) the values it lists.</summary>
public sealed record TemplatePreviewBranch(string Label, IReadOnlyList<string> Nodes, string? Ret, IReadOnlyList<string>? Values);

/// <summary>#1236: a flow gate decided for the reader's pick — the branch taken (the record's word, "none" when none) and whether the reader node runs in it.</summary>
public sealed record TemplatePreviewFlowOutcome(TemplatePreviewGate Gate, string Branch, bool RunsReader);

/// <summary>#1243: a flow gate inside a loop decided once per element for a reader that iterates the loop — the branch each item takes ("none" when skipped); empty when the picks cannot decide it (a placeholder element), the elements then taken.</summary>
public sealed record TemplatePreviewElementOutcome(TemplatePreviewGate Gate, string Loop, IReadOnlyList<KeyValuePair<string, string>> Branches);

/// <summary>#1205: a gate waiting on picks — the reads it needs, spelled `input.&lt;id&gt;`, `node.&lt;id&gt;`, `item`.</summary>
public sealed record TemplatePreviewWaitingGate(TemplatePreviewGate Gate, IReadOnlyList<string> Missing);

/// <summary>#1205: the picked scope with the judged sets, the gates applied (they did not hold) and the gates waiting on picks; #1236: the flow gates decided, branch by branch.</summary>
public sealed record TemplatePreviewJudgement(TemplateRenderScope Scope, IReadOnlyList<TemplatePreviewGate> Applied, IReadOnlyList<TemplatePreviewWaitingGate> Waiting, IReadOnlyList<TemplatePreviewFlowOutcome>? Flows = null, IReadOnlyList<TemplatePreviewElementOutcome>? Elements = null);
