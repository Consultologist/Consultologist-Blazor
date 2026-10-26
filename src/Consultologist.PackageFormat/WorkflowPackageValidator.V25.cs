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
        public const string Objects = "a template reads objects (input.<id>, node.<id>.content, snippet.<id>.content, item.<member>, macro.<id>.content) and its reads are derived";
        public const string Preludes = "read the text inline ({{ snippet.<id>.content }}) — the editor migrated preludes at 23";
        public const string Raw = "escape a literal '{{' as {{ \"{{\" }} or wrap it in {%{ … }%}";
        public const string Data = "declare snippets (read as {{ snippet.<id>.content }})";
        public const string Schemas = "declare formats and name one on each node (nodes[].format)";
        public const string Kind = "the format says what the node is (classification, rendered, terms_subset)";
        public const string Values = "a classification takes its answer set through with: { values: [...] }";
        public const string Check = "a check is a node of format terms_subset with with: { of, in, failWith }";
        public const string Results = "declare deliverables";
        public const string Placement = "place it on the node as a node instance ({ id, at: before|after })";
        public const string Inline = "read {{ macro.<id>.prompt }} in the template where the inline macro goes";
        public const string ForEach = "fan a snippet folder (snippet.<folder>) or an array input (input.<id>)";
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
        ValidateV26Members(manifest, files, errors);
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

        // #1065: an aggregator is a node a template may read — its composed text
        // (node.<id>.content), no value, no renderer — so it joins the map beside
        // the formatted nodes; its own declarations stay structural.
        var nodeShapes = nodes.Values
            .Where(n => n.Format is not null)
            .ToDictionary(n => n.Node.Id, n => new TemplateNodeShape(n.Format!.Id, n.Format.Spec.Type, n.Format.Spec.Schema, EnumValuesAt25(n, inputsById)), StringComparer.Ordinal);
        foreach (var aggregator in nodes.Values.Where(n => n.IsAggregator))
        {
            nodeShapes[aggregator.Node.Id] = TemplateNodeShape.Aggregator();
        }
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

        ValidateConditionsAt25(walk, nodes.Values.Where(n => n.IsAggregator).Select(n => n.Node.Id).ToHashSet(StringComparer.Ordinal), errors);
        ValidateMacroUseAt25(manifest, nodes, walk, errors, warnings);
        ValidateReferenceGraphAt25(walked, snippets, errors);
        RenderProbeAt25(walk, inputsById, nodes, snippets, manifest, errors);
        ValidateDeliverablesAt25(manifest, nodes, walk, errors);

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
            // #1037 (design § 16): every declared id is snake_case at 25 — a prompt's
            // too, so one id rule reads the same for every object of the package.
            if (!WorkflowDeclaredIds.IsValid(prompt.Id))
            {
                var renamed = prompt.Id.Replace('-', '_');
                errors.Add(WorkflowDeclaredIds.IsValid(renamed)
                    ? $"Prompt id '{prompt.Id}' must be snake_case at specVersion 25 ({renamed}); every declared id is."
                    : $"Prompt id '{prompt.Id}' must be snake_case at specVersion 25 (a lowercase letter, then lowercase letters, digits, or underscores); every declared id is.");
            }

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
                    errors.Add($"Deliverable '{deliverable.Id}' macro '{entry.Id}' declares slot, which is retired at specVersion 25; {RetiredAt25.Inline}.");
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

        // A folder item's when is walked with every other condition (#1037); the
        // inputs-only rule is ValidateConditionsAt25's.

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
        var declaredIds = (manifest.Nodes ?? new List<WorkflowNodeSpec>()).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
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
                foreach (var (name, present) in new[] { ("format", node.Format != null), ("prompt", node.Prompt != null), ("with", node.With != null), ("onInvalid", node.OnInvalid != null), ("forEach", node.ForEach != null) })
                {
                    if (present)
                    {
                        errors.Add($"Node '{node.Id}' aggregates and must not declare {name}; an aggregator is structural.");
                    }
                }

                // #1037 (design § 16): a source is node.<id> — declared, and not the aggregator itself.
                foreach (var sourceRef in node.Aggregate)
                {
                    if (TemplateObjectReference.IsColonSpelling(sourceRef))
                    {
                        errors.Add($"Aggregator node '{node.Id}' source '{sourceRef}' is the ≤24 spelling; at specVersion 25 a source is node.<id>.");
                    }
                    else if (!TemplateObjectReference.IsBare(sourceRef, TemplateObjects.Node, out var sourceId))
                    {
                        errors.Add($"Aggregator node '{node.Id}' source '{sourceRef}' must be node.<id>.");
                    }
                    else if (sourceId == node.Id)
                    {
                        errors.Add($"Aggregator node '{node.Id}' aggregates itself.");
                    }
                    else if (!declaredIds.Contains(sourceId))
                    {
                        errors.Add($"Aggregator node '{node.Id}' references unknown node '{sourceId}'.");
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

            // A5 #1018: the ≤24 twins (a template, a check) in the format's
            // words — a render and an operation are deterministic by
            // construction, so the claim is not theirs to make.
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

        // #1037 (design § 16): the object spelling — snippet.<folder> or input.<id>;
        // the ≤24 binding grammar (data:, input:, and 25's own snippet: before it) is
        // refused by name with the spelling in the sentence.
        if (TemplateObjectReference.IsColonSpelling(node.ForEach))
        {
            errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' is the ≤24 spelling; at specVersion 25 forEach fans snippet.<folder> or input.<id>.");
            return (null, null);
        }

        if (!TemplateObjectReference.TryParse(node.ForEach, out var obj, out var id, out var rest) || rest.Length > 0
            || (obj != TemplateObjects.Snippet && obj != TemplateObjects.Input))
        {
            errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' must be snippet.<folder> or input.<id>.");
            return (null, null);
        }

        if (obj == TemplateObjects.Snippet)
        {
            if (snippets.Collections.TryGetValue(id, out var folder))
            {
                return (new SnippetFolderFan(id), folder);
            }

            errors.Add(snippets.Scalars.ContainsKey(id)
                ? $"Node '{node.Id}' forEach fans snippet '{id}', which is a file, not a folder."
                : $"Node '{node.Id}' forEach fans snippet folder '{id}', which the package does not declare.");
            return (null, null);
        }

        if (!inputsById.TryGetValue(id, out var declaration))
        {
            errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' fans undeclared input '{id}'.");
            return (null, null);
        }

        if (WorkflowInputTypes.Of(declaration) != WorkflowInputTypes.Array)
        {
            errors.Add($"Node '{node.Id}' forEach '{node.ForEach}' fans input '{id}', which is a {WorkflowInputTypes.Of(declaration)}; only an array can be fanned.");
            return (null, null);
        }

        var element = WorkflowDeclarationNode.Of(declaration).Items ?? new WorkflowDeclarationNode(WorkflowInputTypes.Text, declaration.Label, false, null, null, null);
        return (new InputArrayFan(element), null);
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
                errors.Add($"Macro '{macro.Id}' must declare an instruction at specVersion 25; placed text tells the model when to write its text read.");
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

        // #1037: a condition's sentences ride with the templates'.
        foreach (var condition in walk.Conditions)
        {
            errors.AddRange(condition.Walk.References.Errors.Where(seenErrors.Add));
            warnings.AddRange(condition.Walk.References.Warnings.Where(seenWarnings.Add));
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
    private static IReadOnlyList<string>? EnumValuesAt25(NodeAt25 node, IReadOnlyDictionary<string, WorkflowInputSpec> inputsById)
    {
        if (node.Format?.Spec.Type != WorkflowInputTypes.Enum)
        {
            return null;
        }

        List<string>? values = node.Format.Spec.Values?.ToList();
        var parameter = node.Format.Spec.ValuesParameter;

        if (parameter is not null && node.Node.With is { } with && with.TryGetValue(parameter, out var supplied))
        {
            if (supplied is JsonArray array)
            {
                values = array.Select(v => v?.GetValue<string>() ?? string.Empty).ToList();
            }
            else if (supplied is JsonValue value && value.TryGetValue(out string? reference)
                && TemplateObjectReference.IsBare(reference, TemplateObjects.Input, out var inputId)
                && inputsById.TryGetValue(inputId, out var input))
            {
                values = input.Values;
            }
        }

        return values;
    }

    /// <summary>
    /// #1037 (design § 16): the site rules over the walked conditions — the walk
    /// itself refused what does not parse, reads what is not declared, or compares
    /// against the wrong value. A folder item's when reads inputs only, so the
    /// folder's size is known at job start.
    /// </summary>
    private static void ValidateConditionsAt25(WorkflowDerivedEdges.Walk walk, IReadOnlySet<string> aggregatorIds, List<string> errors)
    {
        // #1065: a template may read an aggregator's composed text; a when may not —
        // the deciding stage runs the whens' closure before the sections compose,
        // and that closure does not pull an aggregator's sources along.
        foreach (var condition in walk.Conditions.Where(c => c.Site != WorkflowDerivedEdges.ConditionSite.FolderItem && c.Walk.IsValid))
        {
            foreach (var id in condition.Walk.References.Nodes.Keys.Where(aggregatorIds.Contains).Order(StringComparer.Ordinal))
            {
                errors.Add($"{condition.Subject} reads node.{id}, which aggregates; a condition decides before the sections compose — read a scalar node.");
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
                errors.Add($"{condition.Subject} reads {offending}; a folder item's when reads inputs only, so the folder's size is known at job start (the template's own if gates anything else).");
            }
        }
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
        List<string> errors,
        List<string> warnings)
    {
        var declared = (manifest.Macros ?? new List<WorkflowMacroSpec>()).Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        // #1067: a use is a read from a template the run renders, or a placement
        // (Walk.MacrosReached) — not a read from any walked template. An unplaced
        // macro's own instruction reads its content in the ordinary way of saying
        // where it goes, and counting that read made the refusal below unreachable.
        var used = new HashSet<string>(
            walk.MacrosReached(manifest.EffectiveDeliverables.SelectMany(d => d.Macros ?? new List<WorkflowDeliverableMacroSpec>()).Select(e => e.Id)),
            StringComparer.Ordinal);

        // #1067: an instruction that reads its own content. The instruction is a
        // template rendered where macro.<id>.prompt is read, so the read puts the
        // macro's TEXT in the prompt — right before the mechanics sentence that
        // names the read the model is to write. Warned, not refused: the text still
        // runs, and three published packages carried it when this landed.
        foreach (var instruction in walk.Templates
            .Where(t => t.Kind == TemplateKind.Instruction && t.References.MacroContents.Contains(t.Id))
            .OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            warnings.Add($"Macro '{instruction.Id}' instruction reads macro.{instruction.Id}.content; an instruction renders where macro.{instruction.Id}.prompt is read, so that read puts the macro's text in the prompt — say where the content goes, the mechanics sentence names the read.");
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

                    // C6 (#1012): a macro text runs under the node's fan — none here means no item.
                    if (node.Fan is null && walk.TextItemReadOf(TemplateKind.Macro, entry.Id) is { } itemRead)
                    {
                        errors.Add($"{where} reads item.{itemRead.Member}{(itemRead.Subject == $"Macro '{entry.Id}'" ? string.Empty : $" (through {itemRead.Subject})")}, but '{spec.Id}' does not fan.");
                    }
                }
            }

            // Inline macros (#1047: the word).
            if (!walk.HasPrompt(spec.Id))
            {
                continue;
            }

            var inlineMacros = walk.PromptReadsOf(spec.Id);

            if (inlineMacros.Count > 0 && !allowsOutputMacros)
            {
                errors.Add($"Node '{spec.Id}' reads macro.{inlineMacros.Order(StringComparer.Ordinal).First()}.prompt (an inline macro), but format '{formatName}' takes no output macros; only a text format with outputMacros may.");
            }

            // C6 (#1012): what the prompt reads — a snippet, a macro text, an inline macro's fill — may read the fan item only where the node fans.
            if (node.Fan is null)
            {
                if (walk.PromptItemReadThroughOf(spec.Id) is { } through)
                {
                    errors.Add($"Node '{spec.Id}' reaches item.{through.Member} through {through.Subject}, but '{spec.Id}' does not fan.");
                }

                foreach (var slot in inlineMacros.Order(StringComparer.Ordinal))
                {
                    if (walk.TextItemReadOf(TemplateKind.Macro, slot) is { } fillRead)
                    {
                        errors.Add($"Node '{spec.Id}' reads macro.{slot}.prompt (an inline macro), but its fill reads item.{fillRead.Member} and '{spec.Id}' does not fan.");
                    }
                }
            }

            // § 7: items of a fanned folder that lack the read.
            if (node.Folder is { } folder && node.FanSource is not null)
            {
                var folderId = WorkflowFanSources.CollectionIdOf(node.FanSource);
                var perItem = folder.Items.ToDictionary(i => i.Id, i => walk.PromptReadsOf(spec.Id, i.Id), StringComparer.Ordinal);
                var inlineAnywhere = perItem.Values.SelectMany(s => s).ToHashSet(StringComparer.Ordinal);

                foreach (var slot in inlineAnywhere.Order(StringComparer.Ordinal))
                {
                    var lacking = perItem.Where(p => !p.Value.Contains(slot)).Select(p => p.Key).Order(StringComparer.Ordinal).ToList();

                    if (lacking.Count > 0)
                    {
                        warnings.Add($"Node '{spec.Id}' fans snippet folder '{folderId}'; items {string.Join(", ", lacking)} carry no macro.{slot}.prompt read, so nothing tells the model to write macro.{slot}.content for them.");
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
            errors.Add($"Macro '{macro}' is declared but never placed or read; place it on a node or a deliverable, or read macro.{macro}.content / macro.{macro}.prompt.");
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
            errors.Add($"{Capitalize(path[0])} text reaches macro.{promptMacro}.prompt{through}; text placed after a reply instructs no model.");
        }

        static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    // ----- the strict render probe over the seven objects ----------------------------

    private static void RenderProbeAt25(
        WorkflowDerivedEdges.Walk walk,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        IReadOnlyDictionary<string, NodeAt25> nodes,
        WorkflowPackageData snippets,
        WorkflowPackageManifest manifest,
        List<string> errors,
        // #1100: the 26 path lends the flow nodes' probes (a loop's text, a
        // branch's returned shape) — null on the 25 path, which has none.
        Action<ScriptObject>? probeFlowNodes = null,
        // #1101: the 26 path's deliverables, read as deliverable.<id>.content — null on the 25 path.
        IReadOnlyCollection<string>? deliverableIds = null)
    {
        var walked = walk.Templates;
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
            node.Add(n.Node.Id, new ScriptObject { ["content"] = "placeholder", ["value"] = ProbeFromShape(shape) });
        }

        // #1065: an aggregator probes as text alone — the walker refused .value already.
        foreach (var n in nodes.Values.Where(n => n.IsAggregator))
        {
            node.Add(n.Node.Id, new ScriptObject { ["content"] = "placeholder" });
        }

        probeFlowNodes?.Invoke(node);

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
            // A duplicate id is refused by sentence above; the probe must not
            // throw on it (A5 #1018 — found by the conformance export).
            macro[m.Id] = new ScriptObject { ["id"] = m.Id, ["name"] = m.Label, ["content"] = "placeholder", ["prompt"] = "placeholder" };
        }

        ScriptObject? deliverable = null;
        if (deliverableIds is not null)
        {
            deliverable = new ScriptObject();
            foreach (var id in deliverableIds)
            {
                deliverable[id] = new ScriptObject { ["content"] = "placeholder" };
            }
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

        PromptTemplateRenderer.RenderingContext ContextFor(TemplateReferenceContext templateContext)
        {
            var globals = new ScriptObject
            {
                [TemplateObjects.Input] = input,
                [TemplateObjects.Node] = node,
                [TemplateObjects.Snippet] = snippet,
                [TemplateObjects.Macro] = macro,
                [TemplateObjects.Run] = run,
                [TemplateObjects.Profile] = profile
            };

            if (deliverable is not null)
            {
                globals[TemplateObjects.Deliverable] = deliverable;
            }

            foreach (var pipe in TemplatePipes.All)
            {
                globals.Import(pipe, (Func<object?, string>)(_ => "placeholder"));
            }

            if (templateContext.Fan is SnippetFolderFan)
            {
                globals[TemplateObjects.Item] = SnippetProbe("item");
            }
            else if (templateContext.Fan is InputArrayFan inputFan)
            {
                globals[TemplateObjects.Item] = new ScriptObject { ["id"] = "0", ["name"] = "placeholder", ["value"] = Probe(inputFan.Element) };
            }
            else if (templateContext.ItemDeferred)
            {
                // C6 (#1012): a snippet or macro text runs under its reader's fan; the probe lends every member.
                globals[TemplateObjects.Item] = new ScriptObject { ["id"] = "item", ["name"] = "placeholder", ["content"] = "placeholder", ["value"] = "placeholder" };
            }

            var context = new PromptTemplateRenderer.RenderingContext();
            context.PushGlobal(globals);
            return context;
        }

        foreach (var template in walked.Where(t => t.References.IsValid))
        {
            try
            {
                Template.Parse(template.Text).Render(ContextFor(template.Context));
            }
            catch (Exception ex)
            {
                errors.Add($"{template.Subject} failed strict rendering: {ex.Message}");
            }
        }

        // #1037: a condition evaluates over the same probe, and must yield a boolean.
        foreach (var condition in walk.Conditions.Where(c => c.Walk.IsValid))
        {
            try
            {
                var context = ContextFor(condition.Context);
                var value = context.Evaluate(condition.Walk.Expression!);

                if (value is not bool)
                {
                    errors.Add($"{condition.Subject} must be a boolean expression; it yields {WorkflowConditionAt25.DescribeValue(value)}.");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{condition.Subject} failed strict evaluation: {ex.Message}");
            }
        }

        static ScriptObject SnippetProbe(string id) => new() { ["id"] = id, ["name"] = "placeholder", ["content"] = "placeholder" };
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
        WorkflowDerivedEdges.Walk walk,
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

            // #1037 (design § 16): node.<id>, the object spelling.
            if (TemplateObjectReference.IsColonSpelling(deliverable.Node))
            {
                errors.Add($"{subject} node '{deliverable.Node}' is the ≤24 spelling; at specVersion 25 a deliverable names node.<id>.");
            }
            else if (!TemplateObjectReference.IsBare(deliverable.Node, TemplateObjects.Node, out var nodeId))
            {
                errors.Add($"{subject} node '{deliverable.Node}' must be node.<id>.");
            }
            else
            {
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

            if (deliverable.Check != null)
            {
                if (!TemplateObjectReference.IsBare(deliverable.Check, TemplateObjects.Node, out var checkId))
                {
                    errors.Add($"{subject} check '{deliverable.Check}' must be node.<id>, naming a node of a computed format.");
                }
                else
                {
                    if (!nodes.ContainsKey(checkId))
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

                // B8 #1027 (design § 2): a deliverable instance's reads are the
                // deliverable's edges — scalar nodes and aggregators; a fanned
                // node has no item at a deliverable, so the read is refused.
                foreach (var read in walk.MacroEdges(entry.Id).Order(StringComparer.Ordinal))
                {
                    if (nodes.TryGetValue(read, out var readNode) && readNode.Fan is not null)
                    {
                        errors.Add($"{where} reads node '{read}', which fans; a deliverable macro reads scalar nodes and aggregators — place the macro on the node to read its items.");
                    }
                }

                // C6 (#1012): a deliverable composes no fan item.
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
            if (TemplateObjectReference.IsBare(deliverable.Node, TemplateObjects.Node, out var rootId) && nodes.ContainsKey(rootId))
            {
                roots.Add((deliverable.Id, rootId));
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
