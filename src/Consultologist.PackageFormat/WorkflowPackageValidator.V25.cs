using System.Text.Json.Nodes;
using Scriban;
using Scriban.Runtime;

namespace Consultologist.PackageFormat;

/// <summary>
/// The validator at specVersion 25 (package-format-v25-design.md; A4, #999).
/// A 25 package is a different grammar — objects in templates, formats on
/// nodes, snippets read and macros placed, deliverables — so it takes its own
/// path after the shared metadata checks, and the v5–v24 path is left
/// byte-for-byte as it was. Every retired key is refused by name with its
/// replacement in the sentence (§ 10); every required member is named; every
/// template is walked (<see cref="TemplateReferenceWalker"/>) and its edges
/// derived; then the deliverables, the reachability and the acyclicity run
/// over those edges.
/// </summary>
public static partial class WorkflowPackageValidator
{
    /// <summary>The replacement sentences of § 10, one per retired key.</summary>
    private static class RetiredAt25
    {
        public const string Objects = "a template reads objects (input.<id>, node.<id>.text, snippet.<id>.text, item.<member>, macro.<id>.text) and its reads are derived";
        public const string Preludes = "read the text inline ({{ snippet.<id>.text }}) — the editor migrated preludes at 23";
        public const string Raw = "escape a literal '{{' as {{ \"{{\" }} or wrap it in {%{ … }%}";
        public const string Data = "declare snippets (read as {{ snippet.<id>.text }})";
        public const string Schemas = "declare formats and name one on each node (nodes[].format)";
        public const string Kind = "the format says what the node is (classification, rendered, terms-subset)";
        public const string Values = "a classification takes its answer set through with: { values: [...] }";
        public const string Check = "a check is a node of format terms-subset with with: { of, in, failWith }";
        public const string Results = "declare deliverables";
        public const string Placement = "place it on the node as a node instance ({ id, at: before|after })";
        public const string Slot = "read {{ macro.<id>.prompt }} in the template where the slot goes";
        public const string ForEach = "fan a snippet folder (snippet:<folder>) or an array input (input:<id>)";
    }

    private const string SnippetNoun = "Snippet";

    /// <summary>A format a node may name: its id, its spec, and whether it is the package's own or the catalogue's.</summary>
    internal sealed record ResolvedFormat(string Id, WorkflowFormatSpec Spec, WorkflowFormatOrigin Origin);

    /// <summary>A node at 25 with its format resolved and its fan settled.</summary>
    private sealed record NodeAt25(WorkflowNodeSpec Node, ResolvedFormat? Format, TemplateFan? Fan, WorkflowDataCollection? Folder)
    {
        public bool IsAggregator => Node.Aggregate is { Count: > 0 };

        public string? FanSource => Node.ForEach;
    }

    private static void ValidateAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? catalog,
        List<string> errors,
        List<string> warnings)
    {
        ValidateMetadata(manifest, errors);
        ValidateDerivedFrom(manifest, errors);
        ValidateRetiredAt25(manifest, errors);
        ValidateInputs(manifest, errors);

        var inputsById = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .Where(input => WorkflowDeclaredIds.IsValid(input.Id))
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var inputShapes = inputsById.ToDictionary(pair => pair.Key, pair => WorkflowDeclarationNode.Of(pair.Value), StringComparer.Ordinal);

        var localFormats = ResolveFormatsAt25(manifest, files, catalog, errors);
        var snippets = ResolveSnippetsAt25(manifest, files, inputsById, errors);
        var nodes = ValidateNodesAt25(manifest, localFormats, catalog, snippets, inputsById, errors);
        ValidateMacroLibraryAt25(manifest, files, errors);

        var nodeShapes = nodes.Values
            .Where(n => n.Format is not null)
            .ToDictionary(n => n.Node.Id, n => new TemplateNodeShape(n.Format!.Id, n.Format.Spec.Type, n.Format.Spec.Schema), StringComparer.Ordinal);
        var macroIds = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        TemplateReferenceContext ContextFor(TemplateFan? fan) => new(
            inputShapes, nodeShapes,
            snippets.Scalars.Keys.ToHashSet(StringComparer.Ordinal),
            snippets.Collections.Keys.ToHashSet(StringComparer.Ordinal),
            macroIds, TemplateReferenceContext.NoIds, fan);

        // ONE walk of every template — the job starter derives its scheduler
        // edges through the same function (B3 #1002).
        var walk = WorkflowDerivedEdges.WalkAll(
            manifest, files,
            nodes.Values.Select(n => new WorkflowDerivedEdges.WalkNode(
                n.Node, nodeShapes.GetValueOrDefault(n.Node.Id), n.Format?.Spec.Parameters, n.Fan)).ToList(),
            snippets, ContextFor);
        var walked = walk.Templates;
        TakeSentences(walk, errors, warnings);
        var edges = DeriveEdgesAt25(manifest, nodes, walk, errors);
        ValidateFanEdgesAt25(nodes, edges, errors);

        var classifiers = ClassifiersAt25(nodes, inputsById);
        ValidateMacroUseAt25(manifest, nodes, walk, inputsById, classifiers, errors, warnings);
        ValidateReferenceGraphAt25(walked, snippets, errors);
        RenderProbeAt25(walked, inputsById, nodes, snippets, manifest, errors);
        ValidateDeliverablesAt25(manifest, nodes, walked, inputsById, classifiers, errors);

        var specs = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToList();
        ValidateReachabilityAt25(manifest, specs, nodes, edges, errors);
        ValidateAcyclic(specs, edges, errors);
    }

    // ----- § 10: refused by name, with the replacement -----------------------

    private static void ValidateRetiredAt25(WorkflowPackageManifest manifest, List<string> errors)
    {
        if (manifest.Preludes != null)
        {
            errors.Add($"preludes is retired at specVersion 25; {RetiredAt25.Preludes}.");
        }

        if (manifest.Data != null)
        {
            errors.Add($"data is retired at specVersion 25; {RetiredAt25.Data}.");
        }

        foreach (var key in new[] { ("schemas", manifest.Schemas), ("schemaRefs", manifest.SchemaRefs), ("customSchemas", manifest.CustomSchemas) })
        {
            if (key.Item2 != null)
            {
                errors.Add($"{key.Item1} is retired at specVersion 25; {RetiredAt25.Schemas}.");
            }
        }

        if (manifest.Results != null)
        {
            errors.Add($"results is retired at specVersion 25; {RetiredAt25.Results}.");
        }

        if (manifest.Result != null)
        {
            errors.Add($"result is retired at specVersion 25; {RetiredAt25.Results}.");
        }

        foreach (var prompt in manifest.Prompts ?? new List<WorkflowPromptSpec>())
        {
            if (prompt.Prelude != null)
            {
                errors.Add($"Prompt '{prompt.Id}' declares prelude, which is retired at specVersion 25; {RetiredAt25.Preludes}.");
            }

            if (prompt.Variables is { Count: > 0 })
            {
                errors.Add($"Prompt '{prompt.Id}' declares variables, which are retired at specVersion 25; {RetiredAt25.Objects}.");
            }

            if (prompt.Raw != null)
            {
                errors.Add($"Prompt '{prompt.Id}' declares raw, which is retired at specVersion 25; {RetiredAt25.Raw}.");
            }
        }

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (node.Bindings != null)
            {
                errors.Add($"Node '{node.Id}' declares bindings, which are retired at specVersion 25; {RetiredAt25.Objects}.");
            }

            if (node.Output != null)
            {
                errors.Add($"Node '{node.Id}' declares output, which is retired at specVersion 25; name the node's format (nodes[].format).");
            }

            if (node.Kind != null)
            {
                errors.Add($"Node '{node.Id}' declares kind, which is retired at specVersion 25; {RetiredAt25.Kind}.");
            }

            if (node.Values != null)
            {
                errors.Add($"Node '{node.Id}' declares values, which is retired at specVersion 25; {RetiredAt25.Values}.");
            }

            foreach (var (name, value) in new[] { ("op", node.Op), ("of", node.Of), ("in", node.In), ("failWith", node.FailWith) })
            {
                if (value != null)
                {
                    errors.Add($"Node '{node.Id}' declares {name}, which is retired at specVersion 25; {RetiredAt25.Check}.");
                }
            }
        }

        foreach (var deliverable in manifest.EffectiveDeliverables)
        {
            foreach (var entry in deliverable.Macros ?? new List<WorkflowDeliverableMacroSpec>())
            {
                foreach (var (name, value) in new[] { ("before", entry.Before), ("after", entry.After), ("forItem", entry.ForItem) })
                {
                    if (value != null)
                    {
                        errors.Add($"Deliverable '{deliverable.Id}' macro '{entry.Id}' declares {name}, which is retired at specVersion 25; {RetiredAt25.Placement}.");
                    }
                }

                if (entry.Slot != null)
                {
                    errors.Add($"Deliverable '{deliverable.Id}' macro '{entry.Id}' declares slot, which is retired at specVersion 25; {RetiredAt25.Slot}.");
                }
            }
        }
    }

    // ----- § 3: formats ------------------------------------------------------

    private static IReadOnlyDictionary<string, ResolvedFormat> ResolveFormatsAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        WorkflowFormatCatalog? catalog,
        List<string> errors)
    {
        var resolved = new Dictionary<string, ResolvedFormat>(StringComparer.Ordinal);

        foreach (var format in manifest.Formats ?? new List<WorkflowFormatReference>())
        {
            var subject = $"Format '{format.Id}'";

            if (!WorkflowDeclaredIds.IsValid(format.Id))
            {
                errors.Add($"Format id '{format.Id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
                continue;
            }

            if (resolved.ContainsKey(format.Id))
            {
                errors.Add($"{subject} is declared twice.");
                continue;
            }

            if (catalog is not null && catalog.Formats.ContainsKey(format.Id))
            {
                errors.Add($"{subject} is a catalogue id and cannot be declared locally; catalogue ids are reserved.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(format.File))
            {
                errors.Add($"{subject} must declare a file.");
                continue;
            }

            if (!files.TryGetValue(format.File, out var json))
            {
                errors.Add($"{subject} file '{format.File}' is missing from the package.");
                continue;
            }

            if (!WorkflowFormatSpecJson.TryRead(json, subject, errors, out var spec))
            {
                continue;
            }

            var before = errors.Count;
            WorkflowFormatValidator.Validate(subject, spec!, WorkflowFormatOrigin.PackageLocal, errors);

            if (errors.Count == before)
            {
                resolved[format.Id] = new ResolvedFormat(format.Id, spec!, WorkflowFormatOrigin.PackageLocal);
            }
        }

        return resolved;
    }

    // ----- § 4: snippets ----------------------------------------------------------

    /// <summary>
    /// The snippets resolved as data was, with "Snippet" in every sentence; a
    /// folder item's <c>when</c> parsed and held to inputs only; a <c>when</c>
    /// outside a folder has no place — the template's own if does that.
    /// </summary>
    private static WorkflowPackageData ResolveSnippetsAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors)
    {
        var snippets = WorkflowDataResolver.Resolve(manifest.Snippets, SnippetNoun, files, errors);

        foreach (var (id, path) in manifest.Snippets ?? new Dictionary<string, string>())
        {
            if (!WorkflowDeclaredIds.IsValid(id))
            {
                errors.Add($"Snippet id '{id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
            }
        }

        foreach (var (folder, collection) in snippets.Collections)
        {
            foreach (var item in collection.Items.Where(i => i.When != null))
            {
                var prefix = $"Snippet folder '{folder}' item '{item.Id}' condition";

                if (!WorkflowResultConditions.TryParseExpression(item.When!, out var expression, out var syntaxError))
                {
                    errors.Add($"{prefix} {syntaxError}");
                    continue;
                }

                var offending = expression!.Leaves.FirstOrDefault(leaf => leaf.IsNodeValue || leaf.IsItemValue || leaf.IsArithmetic);

                if (offending is not null)
                {
                    errors.Add($"{prefix} reads '{offending.Operand}'; a folder item's when reads inputs only, so the folder's size is known at job start (the template's own if gates anything else).");
                    continue;
                }

                foreach (var condition in expression.Leaves)
                {
                    var before = errors.Count;
                    ValidateV10Clause(prefix, condition, inputsById, new Dictionary<string, WorkflowNodeSpec>(StringComparer.Ordinal), errors);

                    if (errors.Count > before)
                    {
                        break;
                    }
                }
            }
        }

        return snippets;
    }

    // ----- § 3.6: the node -----------------------------------------------------

    private static IReadOnlyDictionary<string, NodeAt25> ValidateNodesAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, ResolvedFormat> localFormats,
        WorkflowFormatCatalog? catalog,
        WorkflowPackageData snippets,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
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
                    ? $"Node id '{node.Id}' must be snake_case at specVersion 25 ({renamed}): a template reads it as a Scriban member."
                    : $"Node id '{node.Id}' must be snake_case at specVersion 25 (a lowercase letter, then lowercase letters, digits, or underscores): a template reads it as a Scriban member.");
            }

            if (string.IsNullOrWhiteSpace(node.Label))
            {
                errors.Add($"Node '{node.Id}' must declare a label.");
            }

            var (fan, folder) = ResolveFanAt25(node, snippets, inputsById, errors);

            if (node.Aggregate is { Count: > 0 })
            {
                foreach (var (name, present) in new[] { ("format", node.Format != null), ("prompt", node.Prompt != null), ("with", node.With != null), ("onInvalid", node.OnInvalid != null) })
                {
                    if (present)
                    {
                        errors.Add($"Node '{node.Id}' aggregates and must not declare {name}; an aggregator is structural.");
                    }
                }

                nodes[node.Id] = new NodeAt25(node, null, fan, folder);
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.Format))
            {
                errors.Add($"Node '{node.Id}' must declare a format at specVersion 25.");
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

            // B5 #1004 (design § 3.6): failIfEmpty is judged on a reply, so a
            // producer with no reply — a render, an operation — takes none.
            if (node.FailIfEmpty != null && string.IsNullOrWhiteSpace(node.FailIfEmpty))
            {
                errors.Add($"Node '{node.Id}' failIfEmpty must not be blank.");
            }
            else if (node.FailIfEmpty != null && spec.Producer is WorkflowFormatProducers.Rendered or WorkflowFormatProducers.Computed)
            {
                errors.Add($"Node '{node.Id}' declares failIfEmpty, but format '{format.Id}' is {spec.Producer} and has no reply to judge.");
            }

            nodes[node.Id] = new NodeAt25(node, format, fan, folder);
        }

        if (unresolvedCatalogueIds.Count > 0)
        {
            errors.Add($"The format catalogue is not available on this engine; catalogue formats ({string.Join(", ", unresolvedCatalogueIds)}) cannot be resolved.");
        }

        return nodes;
    }

    /// <summary><c>forEach: snippet:&lt;folder&gt;</c> or <c>input:&lt;id&gt;</c> (an array); the fan the templates see.</summary>
    private static (TemplateFan? Fan, WorkflowDataCollection? Folder) ResolveFanAt25(
        WorkflowNodeSpec node,
        WorkflowPackageData snippets,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        List<string> errors)
    {
        if (node.ForEach is null)
        {
            return (null, null);
        }

        // snippet:<folder> — the one source the v5 binding grammar does not spell.
        if (node.ForEach.StartsWith(TemplateObjects.Snippet + ":", StringComparison.Ordinal))
        {
            var id = node.ForEach[(TemplateObjects.Snippet.Length + 1)..];

            if (snippets.Collections.TryGetValue(id, out var folder))
            {
                return (new SnippetFolderFan(id), folder);
            }

            errors.Add(snippets.Scalars.ContainsKey(id)
                ? $"Node '{node.Id}' forEach fans snippet '{id}', which is a file, not a folder."
                : $"Node '{node.Id}' forEach fans snippet folder '{id}', which the package does not declare.");
            return (null, null);
        }

        if (!WorkflowNodeBindingSources.TryParse(node.ForEach, out var source, out _))
        {
            errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' must be snippet:<folder> or input:<id>.");
            return (null, null);
        }

        switch (source)
        {
            case WorkflowNodeBindingSource.Data:
                errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' is retired at specVersion 25; {RetiredAt25.ForEach}.");
                return (null, null);

            case WorkflowNodeBindingSource.Input input:
                if (!inputsById.TryGetValue(input.Name, out var declaration))
                {
                    errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' fans undeclared input '{input.Name}'.");
                    return (null, null);
                }

                if (WorkflowInputTypes.Of(declaration) != WorkflowInputTypes.Array)
                {
                    errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' fans input '{input.Name}', which is a {WorkflowInputTypes.Of(declaration)}; only an array can be fanned.");
                    return (null, null);
                }

                var element = WorkflowDeclarationNode.Of(declaration).Items ?? new WorkflowDeclarationNode(WorkflowInputTypes.Text, declaration.Label, false, null, null, null);
                return (new InputArrayFan(element), null);

            default:
                errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' must be snippet:<folder> or input:<id>.");
                return (null, null);
        }
    }

    // ----- § 5: the macro library -----------------------------------------------

    private static void ValidateMacroLibraryAt25(WorkflowPackageManifest manifest, IReadOnlyDictionary<string, string> files, List<string> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var macro in manifest.Macros ?? new List<WorkflowMacroSpec>())
        {
            if (!WorkflowDeclaredIds.IsValid(macro.Id))
            {
                errors.Add($"Macro id '{macro.Id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
                continue;
            }

            if (!ids.Add(macro.Id))
            {
                errors.Add($"Duplicate macro id '{macro.Id}'.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(macro.Label))
            {
                errors.Add($"Macro '{macro.Id}' must declare a label.");
            }

            if (string.IsNullOrWhiteSpace(macro.Instruction))
            {
                errors.Add($"Macro '{macro.Id}' must declare an instruction at specVersion 25; placed text tells the model when to emit its marker.");
            }

            if (string.IsNullOrWhiteSpace(macro.File) || !files.ContainsKey(macro.File))
            {
                errors.Add($"Macro '{macro.Id}' file '{macro.File}' is missing from the package.");
            }
        }
    }

    // ----- §§ 1–2: the templates walked, the edges derived ------------------------

    private static string SnippetItemKey(string folder, string item) => WorkflowDerivedEdges.SnippetItemKey(folder, item);

    /// <summary>The walker's sentences, each once — a prompt walked for several nodes repeats them.</summary>
    private static void TakeSentences(WorkflowDerivedEdges.Walk walk, List<string> errors, List<string> warnings)
    {
        var seenErrors = new HashSet<string>(StringComparer.Ordinal);
        var seenWarnings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var template in walk.Templates)
        {
            errors.AddRange(template.References.Errors.Where(seenErrors.Add));
            warnings.AddRange(template.References.Warnings.Where(seenWarnings.Add));
        }
    }

    /// <summary>
    /// The edges: what each node's prompt reads (node.&lt;id&gt;, closed over
    /// the snippets and macro texts it reads), what its <c>with</c> names,
    /// what it aggregates — one map for the fan rules, the reachability and
    /// the acyclicity.
    /// </summary>
    private static IReadOnlyDictionary<string, HashSet<string>> DeriveEdgesAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        WorkflowDerivedEdges.Walk walk,
        List<string> errors)
    {
        foreach (var node in nodes.Values)
        {
            if (node.Format is not null && node.Node.With != null && (node.Format.Spec.Parameters?.Count ?? 0) == 0)
            {
                errors.Add($"Node '{node.Node.Id}' declares with, but format '{node.Format.Id}' declares no parameters.");
            }

            if (walk.Parameters.TryGetValue(node.Node.Id, out var parameters))
            {
                errors.AddRange(parameters.Errors);
            }
        }

        return WorkflowNodeClosure.Edges(
            (manifest.Nodes ?? new List<WorkflowNodeSpec>()).DistinctBy(n => n.Id, StringComparer.Ordinal).ToList(),
            walk.Edges);
    }

    /// <summary>
    /// The rules bindings carried, over derived edges: a fan reading a fan
    /// over another source is refused; a node that does not fan and reads a
    /// fan is refused — aggregation is explicit.
    /// </summary>
    private static void ValidateFanEdgesAt25(
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        List<string> errors)
    {
        foreach (var (fromId, targets) in edges.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!nodes.TryGetValue(fromId, out var from) || from.IsAggregator)
            {
                continue;
            }

            foreach (var targetId in targets.Order(StringComparer.Ordinal))
            {
                if (!nodes.TryGetValue(targetId, out var target) || target.FanSource is null)
                {
                    continue;
                }

                if (from.FanSource is null)
                {
                    errors.Add($"Node '{fromId}' reads node '{targetId}', which fans; aggregation is explicit at specVersion 25 — collect it through an aggregator node.");
                }
                else if (!string.Equals(from.FanSource, target.FanSource, StringComparison.Ordinal))
                {
                    errors.Add($"Node '{fromId}' reads node '{targetId}' across fans ('{from.FanSource}' vs '{target.FanSource}'); two fans align only over one source.");
                }
            }
        }
    }

    // ----- conditions: the classifier set at 25 -------------------------------------

    /// <summary>
    /// The nodes a <c>node:x == value</c> clause may read: every node of an
    /// enum format, with the answer set it was given — through
    /// <c>with.values</c> (a list, or an enum input's values) or the format's
    /// own <c>values</c>. Shaped as the v10 classifier record so the shared
    /// clause validator reads it unchanged.
    /// </summary>
    private static IReadOnlyDictionary<string, WorkflowNodeSpec> ClassifiersAt25(
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById)
    {
        var classifiers = new Dictionary<string, WorkflowNodeSpec>(StringComparer.Ordinal);

        foreach (var node in nodes.Values.Where(n => n.Format?.Spec.Type == WorkflowInputTypes.Enum))
        {
            List<string>? values = node.Format!.Spec.Values?.ToList();
            var parameter = node.Format.Spec.ValuesParameter;

            if (parameter is not null && node.Node.With is { } with && with.TryGetValue(parameter, out var supplied))
            {
                if (supplied is JsonArray array)
                {
                    values = array.Select(v => v?.GetValue<string>() ?? string.Empty).ToList();
                }
                else if (supplied is JsonValue value && value.TryGetValue(out string? reference)
                    && reference.StartsWith(TemplateObjects.Input + ".", StringComparison.Ordinal)
                    && inputsById.TryGetValue(reference[(TemplateObjects.Input.Length + 1)..], out var input))
                {
                    values = input.Values;
                }
            }

            classifiers[node.Node.Id] = node.Node with { Values = values };
        }

        return classifiers;
    }

    // ----- § 5: macros placed and read ----------------------------------------------

    /// <summary>
    /// Node instances (identity, at, forItem, when), the outputMacros gate on
    /// instances and inline <c>.prompt</c> reads, the unused-macro refusal,
    /// and the folder-items warning of § 7.
    /// </summary>
    private static void ValidateMacroUseAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        WorkflowDerivedEdges.Walk walk,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        IReadOnlyDictionary<string, WorkflowNodeSpec> classifiers,
        List<string> errors,
        List<string> warnings)
    {
        var declared = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var template in walk.Templates)
        {
            used.UnionWith(template.References.MacroTexts);
            used.UnionWith(template.References.MacroPrompts);
        }

        // The .prompt reads a node's template reaches — its prompt, every
        // snippet it reads (transitively), the fan item's own snippet — are the
        // walk's (WorkflowDerivedEdges.Walk.PromptReadsOf): the one closure the
        // engine's completion cross-checks the render against (B5 #1004).
        foreach (var node in nodes.Values.OrderBy(n => n.Node.Id, StringComparer.Ordinal))
        {
            var spec = node.Node;
            var allowsOutputMacros = node.Format?.Spec.AllowsOutputMacros == true;
            var formatName = node.Format?.Id ?? "(unresolved)";

            // Instances.
            if (spec.Macros is { Count: > 0 })
            {
                if (node.IsAggregator)
                {
                    errors.Add($"Node '{spec.Id}' aggregates and must not place macros; place them on the nodes it aggregates or on the deliverable.");
                }

                var fanItemIds = node.Folder?.Items.Select(i => i.Id).ToList();
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var entry in spec.Macros)
                {
                    var where = $"Node '{spec.Id}' macro '{entry.Id}'";

                    if (!declared.Contains(entry.Id))
                    {
                        errors.Add($"{where} is not a declared macro.");
                        continue;
                    }

                    used.Add(entry.Id);

                    if (entry.At != null && entry.At != WorkflowNodeMacroSpec.Before && entry.At != WorkflowNodeMacroSpec.After)
                    {
                        errors.Add($"{where} declares at '{entry.At}'; a node instance is placed 'before' or 'after' the node's output.");
                    }

                    var position = entry.IsAfter ? WorkflowNodeMacroSpec.After : WorkflowNodeMacroSpec.Before;

                    if (!seen.Add($"{entry.Id}\n{position}\n{entry.ForItem}"))
                    {
                        errors.Add($"{where} is placed twice ({position}{(entry.ForItem is null ? string.Empty : $", forItem '{entry.ForItem}'")}); one identity (id, at, forItem) once — two gates to one text is one instance with when: a or b.");
                    }

                    if (!node.IsAggregator && node.Format is not null && !allowsOutputMacros)
                    {
                        errors.Add($"{where} is placed {position} the output, but format '{formatName}' takes no output macros; only a text format with outputMacros may.");
                    }

                    if (entry.ForItem != null)
                    {
                        if (fanItemIds is null)
                        {
                            errors.Add($"{where} anchors to fan item '{entry.ForItem}', but '{spec.Id}' does not fan a snippet folder.");
                        }
                        else if (!fanItemIds.Contains(entry.ForItem, StringComparer.Ordinal))
                        {
                            errors.Add($"{where} anchors to fan item '{entry.ForItem}', which the folder fanned by '{spec.Id}' does not contain (items: {string.Join(", ", fanItemIds)}).");
                        }
                    }

                    if (entry.When != null)
                    {
                        ValidateClauseAt25($"{where} condition", entry.When, inputsById, classifiers, fanItemIds,
                            fanItemIds is null ? $"but '{spec.Id}' does not fan a snippet folder" : string.Empty, errors);
                    }
                }
            }

            // Inline slots.
            if (!walk.HasPrompt(spec.Id))
            {
                continue;
            }

            var inlineSlots = walk.PromptReadsOf(spec.Id);

            if (inlineSlots.Count > 0 && !allowsOutputMacros)
            {
                errors.Add($"Node '{spec.Id}' reads macro.{inlineSlots.Order(StringComparer.Ordinal).First()}.prompt (a slot), but format '{formatName}' takes no output macros; only a text format with outputMacros may.");
            }

            // § 7: items of a fanned folder that lack the read.
            if (node.Folder is { } folder && node.FanSource is not null)
            {
                var folderId = node.FanSource[(node.FanSource.IndexOf(':') + 1)..];
                var perItem = folder.Items.ToDictionary(i => i.Id, i => walk.PromptReadsOf(spec.Id, i.Id), StringComparer.Ordinal);
                var slotsAnywhere = perItem.Values.SelectMany(s => s).ToHashSet(StringComparer.Ordinal);

                foreach (var slot in slotsAnywhere.Order(StringComparer.Ordinal))
                {
                    var lacking = perItem.Where(p => !p.Value.Contains(slot)).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();

                    if (lacking.Count > 0)
                    {
                        warnings.Add($"Node '{spec.Id}' fans snippet folder '{folderId}'; items {string.Join(", ", lacking)} carry no macro.{slot}.prompt read, so no marker is emitted for them.");
                    }
                }
            }
        }

        foreach (var deliverable in manifest.EffectiveDeliverables)
        {
            foreach (var entry in deliverable.Macros ?? new List<WorkflowDeliverableMacroSpec>())
            {
                used.Add(entry.Id);
            }
        }

        foreach (var macro in declared.Where(id => !used.Contains(id)).Order(StringComparer.Ordinal))
        {
            errors.Add($"Macro '{macro}' is declared but never placed or read; place it on a node or a deliverable, or read macro.{macro}.text / macro.{macro}.prompt.");
        }
    }

    private static void ValidateClauseAt25(
        string prefix,
        string when,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        IReadOnlyDictionary<string, WorkflowNodeSpec> classifiers,
        IReadOnlyList<string>? fanItemIds,
        string itemRefusal,
        List<string> errors)
    {
        if (!WorkflowResultConditions.TryParseExpression(when, out var expression, out var syntaxError))
        {
            errors.Add($"{prefix} {syntaxError}");
            return;
        }

        foreach (var condition in expression!.Leaves)
        {
            var before = errors.Count;
            ValidateV10Clause(prefix, condition, inputsById, classifiers, errors, fanItemIds, itemRefusal);

            if (errors.Count > before)
            {
                return;
            }
        }
    }

    // ----- § 5.2: the reference graph ----------------------------------------------

    private static void ValidateReferenceGraphAt25(IReadOnlyList<WorkflowDerivedEdges.WalkedTemplate> walked, WorkflowPackageData snippets, List<string> errors)
    {
        var graph = new TemplateReferenceGraph();

        foreach (var (folder, collection) in snippets.Collections)
        {
            graph.AddSnippetFolder(folder, collection.Items.Select(i => SnippetItemKey(folder, i.Id)).ToList());
        }

        var added = new HashSet<(TemplateKind, string)>();

        foreach (var template in walked)
        {
            // A prompt walked for several nodes is one template; the first
            // walk's references are the same as every other's.
            var key = (template.Kind, template.Kind == TemplateKind.Prompt ? template.Subject : template.Id);

            if (added.Add(key))
            {
                graph.Add(template.Kind, key.Item2, template.References);
            }
        }

        if (graph.TryFindCycle(out var cycle))
        {
            errors.Add($"Templates contain a cycle: {TemplateReferenceGraph.Describe(cycle)}.");
        }

        if (graph.TryFindMacroTextReachingPrompt(out var path, out var promptMacro))
        {
            var through = path.Count > 1 ? $" through {string.Join(" → ", path.Skip(1))}" : string.Empty;
            errors.Add($"{Capitalize(path[0])} text reaches macro.{promptMacro}.prompt{through}; text placed after a reply cannot carry a slot marker.");
        }

        static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    // ----- the strict render probe over the seven objects ----------------------------

    private static void RenderProbeAt25(
        IReadOnlyList<WorkflowDerivedEdges.WalkedTemplate> walked,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        WorkflowPackageData snippets,
        WorkflowPackageManifest manifest,
        List<string> errors)
    {
        var input = new ScriptObject();
        foreach (var (id, spec) in inputsById)
        {
            input.Add(id, ProbeValue(spec));
        }

        var node = new ScriptObject();
        foreach (var n in nodes.Values.Where(n => n.Format is not null))
        {
            var shape = n.Format!.Spec.Type == WorkflowInputTypes.Enum && n.Format.Spec.Schema is null
                ? TemplateValueShape.Text
                : TemplateValueShape.FromSchema(n.Format.Spec.Schema);
            node.Add(n.Node.Id, new ScriptObject { ["text"] = "placeholder", ["value"] = ProbeFromShape(shape) });
        }

        var snippet = new ScriptObject();
        foreach (var id in snippets.Scalars.Keys)
        {
            snippet.Add(id, SnippetProbe(id));
        }

        foreach (var (folder, collection) in snippets.Collections)
        {
            var items = new ScriptArray();
            foreach (var item in collection.Items)
            {
                items.Add(SnippetProbe(item.Id));
            }

            snippet.Add(folder, items);
        }

        var macro = new ScriptObject();
        foreach (var m in manifest.Macros ?? new List<WorkflowMacroSpec>())
        {
            macro.Add(m.Id, new ScriptObject { ["id"] = m.Id, ["name"] = m.Label, ["text"] = "placeholder", ["prompt"] = "placeholder" });
        }

        var run = new ScriptObject();
        foreach (var fact in TemplateMembers.Run)
        {
            run.Add(fact, fact == "date" ? ProbeDate : "placeholder");
        }

        var profile = new ScriptObject();
        foreach (var fact in TemplateMembers.Profile)
        {
            profile.Add(fact, "placeholder");
        }

        foreach (var template in walked.Where(t => t.References.IsValid))
        {
            try
            {
                var parsed = Template.Parse(template.Text);
                var globals = new ScriptObject
                {
                    [TemplateObjects.Input] = input,
                    [TemplateObjects.Node] = node,
                    [TemplateObjects.Snippet] = snippet,
                    [TemplateObjects.Macro] = macro,
                    [TemplateObjects.Run] = run,
                    [TemplateObjects.Profile] = profile
                };

                foreach (var pipe in TemplatePipes.All)
                {
                    globals.Import(pipe, (Func<object?, string>)(_ => "placeholder"));
                }

                if (template.Context.Fan is SnippetFolderFan)
                {
                    globals[TemplateObjects.Item] = SnippetProbe("item");
                }
                else if (template.Context.Fan is InputArrayFan inputFan)
                {
                    globals[TemplateObjects.Item] = new ScriptObject { ["id"] = "0", ["name"] = "placeholder", ["value"] = Probe(inputFan.Element) };
                }

                var context = new PromptTemplateRenderer.RenderingContext();
                context.PushGlobal(globals);
                parsed.Render(context);
            }
            catch (Exception ex)
            {
                errors.Add($"{template.Subject} failed strict rendering: {ex.Message}");
            }
        }

        static ScriptObject SnippetProbe(string id) => new() { ["id"] = id, ["name"] = "placeholder", ["text"] = "placeholder" };
    }

    /// <summary>A probe value for a format's shape — the schema's structure, two elements per array, scalars by kind.</summary>
    private static object ProbeFromShape(TemplateValueShape shape)
    {
        if (shape.IsObject)
        {
            var probe = new ScriptObject();
            foreach (var (name, field) in shape.Fields ?? new Dictionary<string, TemplateValueShape>())
            {
                probe.Add(name, ProbeFromShape(field));
            }

            return probe;
        }

        if (shape.IsArray)
        {
            var element = shape.Items ?? TemplateValueShape.Text;
            return new ScriptArray { ProbeFromShape(element), ProbeFromShape(element) };
        }

        return shape.Kind switch
        {
            WorkflowInputTypes.Boolean => true,
            WorkflowInputTypes.Number => ProbeNumber,
            WorkflowInputTypes.Date => ProbeDate,
            _ => "placeholder"
        };
    }

    // ----- § 11: deliverables ------------------------------------------------------------

    private static void ValidateDeliverablesAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyList<WorkflowDerivedEdges.WalkedTemplate> walked,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        IReadOnlyDictionary<string, WorkflowNodeSpec> classifiers,
        List<string> errors)
    {
        var deliverables = manifest.Deliverables;

        if (deliverables is not { Count: > 0 })
        {
            errors.Add("deliverables must declare at least one deliverable.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
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

            if (deliverable.Node is not { } nodeRef
                || !nodeRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                || nodeRef.Length == WorkflowNodeBindingSources.NodePrefix.Length)
            {
                errors.Add($"{subject} node '{deliverable.Node}' must be 'node:<id>'.");
            }
            else
            {
                var nodeId = nodeRef[WorkflowNodeBindingSources.NodePrefix.Length..];

                if (!nodes.TryGetValue(nodeId, out var node))
                {
                    errors.Add($"{subject} references unknown node '{nodeId}'.");
                }
                else
                {
                    if (!node.IsAggregator)
                    {
                        errors.Add($"{subject} must reference an aggregator node ('{nodeId}' is not one).");
                    }

                    if (!owners.TryAdd(nodeId, deliverable.Id))
                    {
                        errors.Add($"Deliverables '{owners[nodeId]}' and '{deliverable.Id}' share node '{nodeId}': each deliverable needs its own aggregator.");
                    }
                }
            }

            if (deliverable.When != null)
            {
                ValidateClauseAt25($"{subject} condition", deliverable.When, inputsById, classifiers, null, "but only a node instance on a node that fans a snippet folder reads an item", errors);
            }

            if (deliverable.Check != null)
            {
                var prefix = TemplateObjects.Node + ".";

                if (!deliverable.Check.StartsWith(prefix, StringComparison.Ordinal) || deliverable.Check.Length == prefix.Length)
                {
                    errors.Add($"{subject} check '{deliverable.Check}' must be node.<id>, naming a node of a computed format.");
                }
                else
                {
                    var checkId = deliverable.Check[prefix.Length..];

                    if (!nodes.ContainsKey(checkId))
                    {
                        errors.Add($"{subject} check names node '{checkId}', which the package does not declare.");
                    }
                    else if (!checks.Contains(checkId))
                    {
                        errors.Add($"{subject} check names node '{checkId}', whose format is not computed; a check is a node of a computed format (terms-subset).");
                    }
                    else
                    {
                        namedChecks.Add(checkId);
                    }
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

                if (entry.When != null)
                {
                    ValidateClauseAt25($"{where} condition", entry.When, inputsById, classifiers, null, "but only a node instance on a node that fans a snippet folder reads an item", errors);
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

    // ----- reachability at 25 ---------------------------------------------------------------

    private static void ValidateReachabilityAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyList<WorkflowNodeSpec> specs,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        List<string> errors)
    {
        var roots = new List<(string DeliverableId, string NodeId)>();

        foreach (var deliverable in manifest.Deliverables ?? new List<WorkflowDeliverableSpec>())
        {
            if (deliverable.Node is { } nodeRef
                && nodeRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                && nodes.ContainsKey(nodeRef[WorkflowNodeBindingSources.NodePrefix.Length..]))
            {
                roots.Add((deliverable.Id, nodeRef[WorkflowNodeBindingSources.NodePrefix.Length..]));
            }
        }

        if (roots.Count == 0)
        {
            return;
        }

        var reachableFromAny = new HashSet<string>(StringComparer.Ordinal);
        var fans = specs.Where(n => n.ForEach != null).Select(n => n.Id).Order(StringComparer.Ordinal).ToList();
        var remedy = fans.Count > 0
            ? $"Add an aggregator whose sources include a forEach node and name it as this deliverable's node (forEach nodes in this package: {string.Join(", ", fans)})."
            : "This package declares no forEach node — add one over a snippet folder or an array input, then aggregate it into this deliverable.";

        foreach (var (deliverableId, rootId) in roots)
        {
            var reachable = WorkflowNodeClosure.Reachable(new[] { rootId }, edges);
            reachableFromAny.UnionWith(reachable);

            if (!reachable.Any(id => nodes.TryGetValue(id, out var reached) && reached.FanSource != null))
            {
                errors.Add($"Deliverable '{deliverableId}' must transitively include at least one forEach source: a deliverable with no fan has no consult. {remedy}");
            }
        }

        // A check serves a deliverable's existence, not its text: it and its
        // chain are exempt. A node of an enum format feeds the boundary (its
        // value is what conditions read), so it is consumed whether or not a
        // template reads it.
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
                ? $"Node '{spec.Id}' does not feed the deliverable: every node must transitively reach '{roots[0].NodeId}'. Read it from a node that does, or add it to the deliverable's aggregator."
                : $"Node '{spec.Id}' does not feed any deliverable: every node must transitively reach a deliverable's node. Read it from a node that does, or add it to a deliverable's aggregator.");
        }
    }
}
