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

        return ScopeFor(input.Variables, input.VariableTypes, input.NodeTexts, input.Folders, input.Item, input.ForEach, input.Run, input.Profile, package);
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
        WorkflowPackage package)
    {
        var manifest = package.Manifest;
        var nodesById = (package.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var inputsById = WorkflowVariableDeclarations.For(manifest);

        var nodes = nodeTexts.ToDictionary(
            pair => pair.Key,
            pair => new TemplateNodeOutput(
                pair.Value,
                FormatTypeOf(package, nodesById.GetValueOrDefault(pair.Key)),
                WorkflowNodeDefaults.WellKnownConceptSources.GetValueOrDefault(pair.Key, pair.Key)),
            StringComparer.Ordinal);

        var folderItems = (folders ?? new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>())
            .ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<TemplateFolderItem>)entry.Value
                    .Select(item => new TemplateFolderItem(
                        item.GetValueOrDefault("id", string.Empty),
                        item.GetValueOrDefault("name", item.GetValueOrDefault("id", string.Empty)),
                        item.GetValueOrDefault("content", string.Empty)))
                    .ToList(),
                StringComparer.Ordinal);

        TemplateFanItem? fanItem = null;

        if (item is not null && forEach is not null)
        {
            var id = item.GetValueOrDefault("id", string.Empty);
            var name = item.GetValueOrDefault("name", id);

            fanItem = WorkflowInputFans.IsInputFan(forEach)
                ? new TemplateFanItem(id, name, Value: item.GetValueOrDefault("value", string.Empty),
                    Element: inputsById.TryGetValue(WorkflowInputFans.InputIdOf(forEach), out var declaration)
                        ? WorkflowDeclarationNode.Of(declaration).Items
                        : null)
                : new TemplateFanItem(id, name, Text: item.GetValueOrDefault("content", string.Empty));
        }

        var macros = (manifest.Macros ?? new List<WorkflowMacroSpec>())
            .Where(macro => macro.File is not null && package.SourceFiles is not null && package.SourceFiles.ContainsKey(macro.File))
            .ToDictionary(
                macro => macro.Id,
                macro => new TemplateMacroSource(macro.Id, macro.Label, package.SourceFiles![macro.File!], macro.Instruction ?? string.Empty),
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
            profile);
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
