using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (B3 #1002, design § 1): the 25 render scope, in two halves. The
/// orchestrator builds what only it holds — the outputs of the nodes an
/// instance reads (item-aligned), the folders as settled at job start, the
/// run and profile facts — onto the activity input; the activity completes
/// the scope from the package it re-resolves (snippet texts, macro texts and
/// instructions, the formats' types) and renders once. Pure functions, so the
/// orchestrator's half is replay-stable and both halves are testable without
/// a Durable context.
/// </summary>
internal static class ConsultRenderPayload
{
    /// <summary>
    /// The outputs of the nodes this instance reads — exactly its descriptor's
    /// Reads, each from the item-aligned instance when the read node fans.
    /// The scheduler started this instance only once every read had settled,
    /// so a missing output is an engine fault, said loud.
    /// </summary>
    public static IReadOnlyDictionary<string, string> NodeTextsFor(
        ConsultNodeDescriptor node,
        IReadOnlyDictionary<string, string>? item,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        // v26 (#1154, design § 1.3): a flow node that took no branch has no
        // text — the read is omitted, and the render reads null (PR 3).
        IReadOnlySet<string>? skipped = null)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var readId in node.Reads ?? Array.Empty<string>())
        {
            if (skipped?.Contains(readId) == true)
            {
                continue;
            }

            var read = nodesById.GetValueOrDefault(readId)
                ?? throw new InvalidOperationException($"Node '{node.Id}' reads node '{readId}', which the job does not carry.");
            var key = ConsultNodeScheduler.InstanceKey(readId, read.ForEach is null ? null : item?["id"]);
            var output = outputs.GetValueOrDefault(key)
                ?? throw new InvalidOperationException($"Node '{node.Id}' reads node '{readId}' ('{key}'), whose output the orchestrator does not hold.");

            texts[readId] = output.Classification ?? output.RawOutput;
        }

        return texts;
    }

    /// <summary>The snippet folders of the snapshot: every collection that is not an input fan, as settled at job start.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> FoldersFor(
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? collections) =>
        (collections ?? new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>())
            .Where(entry => !WorkflowInputFans.IsInputFan(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    /// <summary>The run facts as the v11 tokens gave them: the date as yyyy-MM-dd, the job id's first eight characters, the package ref, the host.</summary>
    public static TemplateRunFacts RunFactsFor(DateTime utcNow, string jobId, string? packageRef, string? apiHost) =>
        new(
            utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            jobId.Length <= 8 ? jobId : jobId[..8],
            packageRef ?? string.Empty,
            apiHost ?? string.Empty);

    /// <summary>The profile facts as the v11/v12 tokens gave them.</summary>
    public static TemplateProfileFacts ProfileFactsFor(string? profileName, ConsultSignatureSnapshot? signature) =>
        new(profileName ?? string.Empty, signature?.Text ?? string.Empty);

    /// <summary>
    /// The activity's half: the scope completed from the package and rendered.
    /// A payload without the orchestrator's half (snapshotted before B3, or a
    /// 25 node started by an engine that did not thread it) is refused — a
    /// render over a partial scope would send a different prompt than the
    /// record says.
    /// </summary>
    /// <param name="trace">B5 #1004: when given, what this render read — its macro.&lt;id&gt;.prompt reads are the inline macros the prompt that ran instructed.</param>
    public static string RenderAt25(ConsultPromptNodeActivityInput input, WorkflowPackage package, string promptText, TemplateRenderer.TemplateRenderTrace? trace = null) =>
        TemplateRenderer.Render($"Prompt '{input.PromptId}'", promptText, ScopeAt25(input, package), TemplatePipes.Engine, trace);

    /// <summary>The 25 scope, completed from the payload and the package — what the prompt renders over, and (B5 #1004) what its instances and fills render over at completion.</summary>
    public static TemplateRenderScope ScopeAt25(ConsultPromptNodeActivityInput input, WorkflowPackage package)
    {
        if (input.Run is null || input.Profile is null || input.NodeTexts is null)
        {
            throw new InvalidOperationException(
                $"Prompt node '{input.NodeId}' declares format '{input.Format}', but its payload carries no 25 render scope (snapshotted before the render shipped).");
        }

        return ScopeFor(input.Variables, input.VariableTypes, input.NodeTexts, input.Folders, input.Item, input.ForEach, input.Run, input.Profile, package,
            input.Loops, input.FlowRets, input.SkippedNodes, input.Deliverables);
    }

    /// <summary>
    /// v26 (#1155, design § 3.1): the composed texts of the deliverables a node
    /// (or a deliverable's macros) reads — id → text; null for one not in the
    /// run (skipped by its if: the read is null). The scheduler started the
    /// reader only once the deliverable's sources settled, and the compose step
    /// runs on that same pass, so a run deliverable not yet composed is an
    /// engine fault, said loud. Null when nothing is read (the 25 bytes).
    /// </summary>
    public static IReadOnlyDictionary<string, string?>? DeliverableTextsFor(
        IReadOnlyList<string>? deliverableReads,
        IReadOnlyDictionary<string, ConsultDeliverableComposition.Composed> composed,
        IReadOnlySet<string> runDeliverables)
    {
        if (deliverableReads is not { Count: > 0 })
        {
            return null;
        }

        return deliverableReads.ToDictionary(
            id => id,
            id => composed.TryGetValue(id, out var c)
                ? c.Rendered
                : runDeliverables.Contains(id)
                    ? throw new InvalidOperationException($"Deliverable '{id}' is read before it composed; the orchestrator does not hold its text.")
                    : null,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// v26 (#1154, design § 1.3): the loops this node reads, as the activity
    /// needs them — every loop among its Reads, with the settled items and each
    /// body node's output per item. The scheduler started the node only once
    /// the loop composed, and a body node completes before the node that
    /// returns it, so every item is in hand; a missing one is an engine fault.
    /// Null when the node reads no loop (the 25 bytes).
    /// </summary>
    public static IReadOnlyDictionary<string, ConsultLoopPayload>? LoopsFor(
        ConsultNodeDescriptor node,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? collections)
    {
        Dictionary<string, ConsultLoopPayload>? loops = null;

        foreach (var readId in node.Reads ?? Array.Empty<string>())
        {
            if (nodesById.GetValueOrDefault(readId) is not { Flow: ConsultNodeFlows.For, Collection: { } collection, Ret: { } ret } loop)
            {
                continue;
            }

            var items = collections?.GetValueOrDefault(WorkflowFanSources.CollectionIdOf(collection))
                ?? throw new InvalidOperationException($"Node '{node.Id}' reads loop '{loop.Id}' over '{collection}', which the job snapshot does not carry.");
            var texts = (loop.Body ?? Array.Empty<string>()).ToDictionary(
                bodyId => bodyId,
                bodyId => (IReadOnlyDictionary<string, string>)items.ToDictionary(
                    item => item["id"],
                    item => outputs.GetValueOrDefault(ConsultNodeScheduler.InstanceKey(bodyId, item["id"])) is { } output
                        ? output.Classification ?? output.RawOutput
                        : throw new InvalidOperationException($"Node '{node.Id}' reads loop '{loop.Id}', whose body node '{bodyId}' has no output for item '{item["id"]}'."),
                    StringComparer.Ordinal),
                StringComparer.Ordinal);

            loops ??= new Dictionary<string, ConsultLoopPayload>(StringComparer.Ordinal);
            loops[readId] = new ConsultLoopPayload(collection, ret, items, texts);
        }

        return loops;
    }

    /// <summary>v26 (#1154): the returned node of every flow node this node reads — the alias types as it. Null when it reads none.</summary>
    public static IReadOnlyDictionary<string, string>? FlowRetsFor(ConsultNodeDescriptor node, IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById)
    {
        var rets = (node.Reads ?? Array.Empty<string>())
            .Select(readId => (readId, nodesById.GetValueOrDefault(readId)))
            .Where(pair => pair.Item2 is { Flow: not null, Ret: not null })
            .ToDictionary(pair => pair.readId, pair => pair.Item2!.Ret!, StringComparer.Ordinal);
        return rets.Count > 0 ? rets : null;
    }

    /// <summary>The 25 scope from its parts — the node's (B3), the deliverable's (B8 #1027: no item) — completed from the package.</summary>
    public static TemplateRenderScope ScopeFor(
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<string, string>? variableTypes,
        IReadOnlyDictionary<string, string> nodeTexts,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? folders,
        IReadOnlyDictionary<string, string>? item,
        string? forEach,
        TemplateRunFacts run,
        TemplateProfileFacts profile,
        WorkflowPackage package,
        // v26 (#1154): the loops read whole, the flow nodes' returned nodes, the skipped flows.
        IReadOnlyDictionary<string, ConsultLoopPayload>? loops = null,
        IReadOnlyDictionary<string, string>? flowRets = null,
        IReadOnlyList<string>? skipped = null,
        // v26 (#1155): the deliverables read, composed (null = skipped).
        IReadOnlyDictionary<string, string?>? deliverables = null)
    {
        var manifest = package.Manifest;
        var nodesById = (package.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var inputsById = WorkflowVariableDeclarations.For(manifest);

        // v26 (#1154): a flow node's output types as its returned node's.
        TemplateNodeOutput OutputOf(string id, string text) =>
            new(
                text,
                FormatTypeOf(package, nodesById.GetValueOrDefault(flowRets?.GetValueOrDefault(id) ?? id)),
                WorkflowNodeDefaults.WellKnownConceptSources.GetValueOrDefault(id, id));

        var nodes = nodeTexts.ToDictionary(pair => pair.Key, pair => OutputOf(pair.Key, pair.Value), StringComparer.Ordinal);

        var folderItems = (folders ?? new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>())
            .ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<TemplateFolderItem>)entry.Value
                    .Select(item => new TemplateFolderItem(
                        item.GetValueOrDefault("id", string.Empty),
                        // v26 (#1154): a 26 item's name is its header's label.
                        item.ContainsKey("id") ? WorkflowPackageBlocks.ItemName(item) : string.Empty,
                        item.GetValueOrDefault("content", string.Empty)))
                    .ToList(),
                StringComparer.Ordinal);

        // v26 (#1154): a 26 folder item carries its header's label where a 25 item carried name.
        TemplateFanItem FanItemOf(IReadOnlyDictionary<string, string> fanned, string fan)
        {
            var id = fanned.GetValueOrDefault("id", string.Empty);
            var name = fanned.ContainsKey("id") ? WorkflowPackageBlocks.ItemName(fanned) : id;

            return WorkflowInputFans.IsInputFan(fan)
                ? new TemplateFanItem(id, name, Value: fanned.GetValueOrDefault("value", string.Empty),
                    Element: inputsById.TryGetValue(WorkflowInputFans.InputIdOf(fan), out var declaration)
                        ? WorkflowDeclarationNode.Of(declaration).Items
                        : null)
                : new TemplateFanItem(id, name, Text: fanned.GetValueOrDefault("content", string.Empty));
        }

        var fanItem = item is not null && forEach is not null ? FanItemOf(item, forEach) : null;

        // v26 (#1154, design § 1.3): each loop read whole — its items, every
        // body node's output per item typed by that node's format, and the
        // loop's own composed text (the aggregator's bytes, already in nodes).
        var loopOutputs = loops?.ToDictionary(
            pair => pair.Key,
            pair => new TemplateLoopOutput(
                pair.Value.RetId,
                pair.Value.Items.Select(fanned => FanItemOf(fanned, pair.Value.Collection)).ToList(),
                pair.Value.Texts.ToDictionary(
                    body => body.Key,
                    body => (IReadOnlyDictionary<string, TemplateNodeOutput>)body.Value.ToDictionary(
                        perItem => perItem.Key,
                        perItem => OutputOf(body.Key, perItem.Value),
                        StringComparer.Ordinal),
                    StringComparer.Ordinal),
                nodes.TryGetValue(pair.Key, out var composed)
                    ? composed
                    : throw new InvalidOperationException($"The payload carries loop '{pair.Key}' but not its composed text.")),
            StringComparer.Ordinal);

        // v26 (#1157, design § 5): a macro's label and instruction are its
        // header's and its text is the body — the store's split, carried on the
        // package; a folder entry (#1182) is no flat macro. The header's `if`
        // rides beside them for the engine's judge (#1177).
        var macros = (package.EffectiveManifest.Macros ?? new List<WorkflowMacroSpec>())
            .Where(macro => macro.File is { } file && !file.EndsWith('/') && package.TextFiles is not null && package.TextFiles.ContainsKey(file))
            .ToDictionary(
                macro => macro.Id,
                macro => new TemplateMacroSource(
                    macro.Id,
                    macro.Label ?? macro.Id,
                    package.TextOf(macro.File!),
                    macro.Instruction ?? string.Empty,
                    package.Texts?.Headers.GetValueOrDefault(macro.File!)?.If),
                StringComparer.Ordinal);

        return new TemplateRenderScope(
            variables,
            variableTypes,
            inputsById,
            nodes,
            package.Data?.Scalars ?? new Dictionary<string, string>(),
            folderItems,
            fanItem,
            macros,
            run,
            profile,
            Loops: loopOutputs,
            SkippedNodes: skipped is { Count: > 0 } ? skipped.ToHashSet(StringComparer.Ordinal) : null,
            Labelled: manifest.SpecVersion >= 26,
            Deliverables: deliverables);
    }

    /// <summary>The type a node's format declares — the stamped catalogue's, or a package-local format file's — so <c>.value</c> parses accordingly; text when unknown.</summary>
    internal static string FormatTypeOf(WorkflowPackage package, WorkflowNodeSpec? node)
    {
        if (node?.Format is not { } formatId)
        {
            return WorkflowInputTypes.Text;
        }

        var local = package.Manifest.Formats?.FirstOrDefault(f => string.Equals(f.Id, formatId, StringComparison.Ordinal));

        if (local?.File is not null
            && package.SourceFiles is not null
            && package.SourceFiles.TryGetValue(local.File, out var json)
            && WorkflowFormatSpecJson.TryRead(json, $"Format '{formatId}'", new List<string>(), out var spec))
        {
            return spec!.Type;
        }

        return package.Formats is not null && package.Formats.TryGet(formatId, out var catalogue)
            ? catalogue!.Spec.Type
            : WorkflowInputTypes.Text;
    }
}
