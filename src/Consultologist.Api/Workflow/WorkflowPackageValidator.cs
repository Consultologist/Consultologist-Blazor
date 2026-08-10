using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Runtime;

namespace Consultologist.Api.Workflow;

/// <summary>
/// Validates specVersion-5, -6 and -7 packages: a manifest declares the rule set
/// it was validated under (package-format-v5.md frozen; package-format-v6.md for
/// the v6 closures — multi-collection, aggregator nodes, reachability;
/// package-format-v7.md for declared inputs and the result set). Used at load
/// time by the store (the engine's enforcement point) and by tests; the same
/// checks apply at publish time. Pre-v5 formats were retired by the v5-only
/// rebase.
/// </summary>
public static class WorkflowPackageValidator
{
    /// <summary>The Scriban version this engine renders with (Major.Minor.Patch).</summary>
    public static readonly Version EngineScribanVersion = GetScribanVersion();

    public sealed record ValidationResult(List<string> Errors, List<string> Warnings)
    {
        public bool IsValid => Errors.Count == 0;
    }

    /// <param name="files">Package-relative path → file content, for every file the manifest references.</param>
    /// <param name="catalogSchemas">
    /// Output-contract id → schema JSON from the engine's catalog: every declared
    /// package schema must canonically match one of these (the closure that welds
    /// package contracts to attested agents, output-contract-catalog.md).
    /// </param>
    public static ValidationResult Validate(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string> catalogSchemas)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (manifest.Templating is null
            || !string.Equals(manifest.Templating.Engine, "scriban", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"templating.engine must be 'scriban' for specVersion {manifest.SpecVersion}.");
        }
        else if (!Version.TryParse(manifest.Templating.EngineVersion, out var engineVersion))
        {
            errors.Add($"templating.engineVersion '{manifest.Templating.EngineVersion}' is not a valid version.");
        }
        else if (engineVersion > EngineScribanVersion)
        {
            errors.Add(
                $"templating.engineVersion {engineVersion} is newer than this engine's Scriban {EngineScribanVersion}.");
        }

        var prompts = manifest.Prompts ?? new List<WorkflowPromptSpec>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var prompt in prompts)
        {
            if (!ids.Add(prompt.Id))
            {
                errors.Add($"Duplicate prompt id '{prompt.Id}'.");
            }

            if (prompt.Prelude != null && (manifest.Preludes is null || !manifest.Preludes.ContainsKey(prompt.Prelude)))
            {
                errors.Add($"Prompt '{prompt.Id}' references undefined prelude '{prompt.Prelude}'.");
            }

            if (!files.TryGetValue(prompt.File, out var templateText))
            {
                errors.Add($"Prompt '{prompt.Id}' file '{prompt.File}' is missing from the package.");
                continue;
            }

            ValidateTemplate(prompt, templateText, errors, warnings);
        }

        foreach (var (preludeId, preludePath) in manifest.Preludes ?? new Dictionary<string, string>())
        {
            if (!files.ContainsKey(preludePath))
            {
                errors.Add($"Prelude '{preludeId}' file '{preludePath}' is missing from the package.");
            }
        }

        // v8 validates and publishes here before it executes: the engine's own
        // gate (WorkflowPackageStore.SupportedSpecVersions) moves last, so a v8
        // package is well-formedness-checked while running it still refuses
        // with SpecVersionNotYetExecutable (package-format-v8-design.md § 8).
        if (manifest.SpecVersion is not (5 or 6 or 7 or 8))
        {
            errors.Add($"specVersion {manifest.SpecVersion} is not supported: this engine accepts specVersion 5, 6, 7 or 8 (pre-v5 packages are archived; see registry-operations.md).");
        }
        else
        {
            ValidateDerivedFrom(manifest, errors);
            ValidateNodes(manifest, files, catalogSchemas, errors);
        }

        return new ValidationResult(errors, warnings);
    }

    private static void ValidateDerivedFrom(WorkflowPackageManifest manifest, List<string> errors)
    {
        // Absent and null are equivalent: a root package. When set, the fork origin
        // must be a concrete immutable version — @latest would make lineage mutable.
        if (manifest.DerivedFrom is null)
        {
            return;
        }

        if (!WorkflowPackageRef.TryParse(manifest.DerivedFrom, out var origin))
        {
            errors.Add($"derivedFrom '{manifest.DerivedFrom}' is not a valid package reference.");
            return;
        }

        if (origin!.IsLatest)
        {
            errors.Add("derivedFrom must be a concrete version (name@vYYYY.MM.N), never @latest.");
        }
    }

    /// <summary>
    /// The node rules, selected by declared specVersion. v5: one kind, forEach
    /// multiplicity, item-aligned/broadcast edges only, one shared collection,
    /// result on a forEach node (package-format-v5.md, frozen). v6 additionally:
    /// multiple collections, aggregator nodes (the only aggregation), result on
    /// an aggregator, and reachability (package-format-v6-design.md § 7) —
    /// closures that carry to every later version. v7 additionally: declared
    /// inputs (the input: vocabulary closure) and an optional results set
    /// (package-format-v7.md).
    /// </summary>
    private static void ValidateNodes(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string> catalogSchemas,
        List<string> errors)
    {
        var v6OrLater = manifest.SpecVersion >= 6;
        var declaredInputs = ValidateInputs(manifest, errors);
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        var nodes = manifest.Nodes ?? new List<WorkflowNodeSpec>();

        if (nodes.Count == 0)
        {
            errors.Add($"nodes is required and must not be empty in specVersion {manifest.SpecVersion}.");
            return;
        }

        var promptsById = (manifest.Prompts ?? new List<WorkflowPromptSpec>())
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nodesById = new Dictionary<string, WorkflowNodeSpec>(StringComparer.Ordinal);
        var promptReferenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var edges = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (!nodesById.TryAdd(node.Id, node))
            {
                errors.Add($"Duplicate node id '{node.Id}'.");
            }
        }

        var conceptListNodeIds = nodes
            .Where(n => n.Output != null)
            .Select(n => n.Id)
            .ToHashSet(StringComparer.Ordinal);

        void CheckBinding(WorkflowNodeSpec node, string variable, WorkflowBindingValue binding)
        {
            if (!WorkflowNodeBindingSources.TryParse(binding.From, out var source, out var parseError))
            {
                errors.Add($"Node '{node.Id}' binds '{variable}' to {parseError}.");
                return;
            }

            switch (source)
            {
                case WorkflowNodeBindingSource.Input input:
                    // The input vocabulary closure: parsing is structural, the
                    // declaration (v7) or the fixed v5/v6 slot closes the set.
                    if (!declaredInputs.Contains(input.Name))
                    {
                        errors.Add(manifest.SpecVersion >= 7
                            ? $"Node '{node.Id}' binds '{variable}' to undeclared input '{input.Name}' (declared: {string.Join(", ", declaredInputs.Order(StringComparer.Ordinal))})."
                            : $"Node '{node.Id}' binds '{variable}' to unknown input '{input.Name}' (expected consult_draft).");
                    }
                    else if (binding.As != null)
                    {
                        errors.Add($"Node '{node.Id}' binding '{variable}' declares renderer '{binding.As}' on non-node source '{binding.From}'.");
                    }

                    return;
                case WorkflowNodeBindingSource.Item item when node.ForEach is null:
                    errors.Add($"Node '{node.Id}' binds '{variable}' to 'item:{item.Field}' but declares no forEach.");
                    return;
                case WorkflowNodeBindingSource.Item item:
                    if (TryResolveForEachCollection(node, data, out _, out var collection)
                        && !collection!.Fields.Contains(item.Field))
                    {
                        errors.Add($"Node '{node.Id}' binds '{variable}' to unknown item field '{item.Field}' (the collection declares: {string.Join(", ", collection.Fields)}).");
                    }

                    return;
                case WorkflowNodeBindingSource.Data dataSource:
                    if (data.Collections.ContainsKey(dataSource.Id))
                    {
                        errors.Add($"Node '{node.Id}' binds '{variable}' to data collection '{dataSource.Id}', which is only iterable via forEach.");
                    }
                    else if (!data.Scalars.ContainsKey(dataSource.Id))
                    {
                        errors.Add($"Node '{node.Id}' binds '{variable}' to unknown data entry '{dataSource.Id}'.");
                    }

                    return;
                case WorkflowNodeBindingSource.NodeOutput target:
                    if (!nodesById.TryGetValue(target.NodeId, out var targetNode))
                    {
                        errors.Add($"Node '{node.Id}' binds '{variable}' to unknown node '{target.NodeId}'.");
                        return;
                    }

                    // The edge-semantics table: cross-collection closed everywhere;
                    // aggregation closed in v5 and explicit-only (aggregator nodes,
                    // never bindings) in v6.
                    if (targetNode.ForEach != null && node.ForEach is null)
                    {
                        errors.Add(v6OrLater
                            ? $"Node '{node.Id}' binds '{variable}' to forEach node '{target.NodeId}': aggregation is explicit in specVersion {manifest.SpecVersion} — collect it through an aggregator node."
                            : $"Node '{node.Id}' binds '{variable}' to forEach node '{target.NodeId}', whose aggregate output is not bindable in specVersion 5.0.");
                        return;
                    }

                    if (targetNode.ForEach != null && !string.Equals(targetNode.ForEach, node.ForEach, StringComparison.Ordinal))
                    {
                        errors.Add($"Node '{node.Id}' binds '{variable}' to '{target.NodeId}' across collections ('{node.ForEach}' vs '{targetNode.ForEach}'), which is not supported in specVersion 5.0.");
                        return;
                    }

                    edges.TryAdd(node.Id, new HashSet<string>(StringComparer.Ordinal));
                    edges[node.Id].Add(target.NodeId);

                    var rendersConcepts = binding.As != null || conceptListNodeIds.Contains(target.NodeId);
                    if (binding.As != null && !WorkflowConceptRenderers.All.Contains(binding.As))
                    {
                        errors.Add($"Node '{node.Id}' binding '{variable}' uses unknown renderer '{binding.As}' (expected 'concept-bullets' or 'concept-context').");
                    }
                    else if (rendersConcepts && !conceptListNodeIds.Contains(target.NodeId))
                    {
                        errors.Add($"Node '{node.Id}' binding '{variable}' renders node '{target.NodeId}' with '{binding.As}' but '{target.NodeId}' declares no concept-list output.");
                    }

                    return;
                default:
                    if (binding.As != null)
                    {
                        errors.Add($"Node '{node.Id}' binding '{variable}' declares renderer '{binding.As}' on non-node source '{binding.From}'.");
                    }

                    return;
            }
        }

        void CheckAggregator(WorkflowNodeSpec node)
        {
            // Aggregators are deterministic: the property is the behavior, and the
            // prompt-family fields must be absent (package-format-v6-design.md § 2).
            if (node.Prompt != null || node.Bindings is { Count: > 0 } || node.Output != null || node.ForEach != null)
            {
                errors.Add($"Aggregator node '{node.Id}' must declare only aggregate (no prompt, bindings, output, or forEach).");
            }

            if (node.Aggregate!.Count == 0)
            {
                errors.Add($"Aggregator node '{node.Id}' declares an empty aggregate list.");
                return;
            }

            foreach (var sourceRef in node.Aggregate)
            {
                if (!sourceRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                    || sourceRef.Length == WorkflowNodeBindingSources.NodePrefix.Length)
                {
                    errors.Add($"Aggregator node '{node.Id}' source '{sourceRef}' must be 'node:<id>'.");
                    continue;
                }

                var sourceId = sourceRef[WorkflowNodeBindingSources.NodePrefix.Length..];

                if (!nodesById.ContainsKey(sourceId))
                {
                    errors.Add($"Aggregator node '{node.Id}' references unknown node '{sourceId}'.");
                    continue;
                }

                edges.TryAdd(node.Id, new HashSet<string>(StringComparer.Ordinal));
                edges[node.Id].Add(sourceId);
            }
        }

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Label))
            {
                errors.Add($"Node '{node.Id}' has no label.");
            }

            if (node.Aggregate != null)
            {
                if (!v6OrLater)
                {
                    errors.Add($"Node '{node.Id}' declares aggregate, which requires specVersion 6 or later.");
                    continue;
                }

                CheckAggregator(node);
                continue;
            }

            if (node.ForEach != null && !TryResolveForEachCollection(node, data, out var forEachError, out _))
            {
                errors.Add($"Node '{node.Id}' {forEachError}");
            }

            if (node.Prompt is null)
            {
                errors.Add($"Node '{node.Id}' declares no prompt.");
            }
            else
            {
                var bindings = node.Bindings ?? new Dictionary<string, WorkflowBindingValue>();

                if (!promptsById.TryGetValue(node.Prompt, out var prompt))
                {
                    errors.Add($"Node '{node.Id}' references undeclared prompt '{node.Prompt}'.");
                }
                else
                {
                    promptReferenceCounts[node.Prompt] = promptReferenceCounts.GetValueOrDefault(node.Prompt) + 1;

                    var declared = new HashSet<string>(prompt.Variables, StringComparer.Ordinal);
                    if (!declared.SetEquals(bindings.Keys))
                    {
                        errors.Add(
                            $"Node '{node.Id}' bindings [{string.Join(", ", bindings.Keys.Order(StringComparer.Ordinal))}] " +
                            $"must exactly match prompt '{node.Prompt}' variables [{string.Join(", ", prompt.Variables.Order(StringComparer.Ordinal))}].");
                    }
                }

                foreach (var (variable, binding) in bindings)
                {
                    CheckBinding(node, variable, binding);
                }
            }

            ValidateNodeOutput(node, manifest, files, catalogSchemas, errors);
        }

        foreach (var orphan in promptsById.Keys.Where(id => promptReferenceCounts.GetValueOrDefault(id) == 0))
        {
            errors.Add($"Prompt '{orphan}' is not referenced by any node.");
        }

        // specVersion 6+ allows prompt sharing — each using node binds the
        // prompt's variables itself. v5's published 1:1 rule stays frozen.
        if (!v6OrLater)
        {
            foreach (var overused in promptReferenceCounts.Where(pair => pair.Value > 1).Select(pair => pair.Key))
            {
                errors.Add($"Prompt '{overused}' is referenced by more than one node.");
            }
        }

        if (!v6OrLater)
        {
            // v5.0 closure: the engine fans one item set per job, so every forEach
            // node shares one collection (disconnected parallel chains would have no
            // consumer anyway). Relaxed in v6, where aggregator nodes are the
            // consumer and reachability replaces this rule.
            var forEachCollections = nodes
                .Where(node => node.ForEach != null)
                .Select(node => node.ForEach!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (forEachCollections.Count > 1)
            {
                errors.Add($"All forEach nodes must share one collection in specVersion 5.0 (found {string.Join(", ", forEachCollections.Select(c => $"'{c}'"))}).");
            }
        }

        ValidateResult(manifest, nodesById, v6OrLater, errors);

        if (v6OrLater)
        {
            ValidateReachability(manifest, nodes, nodesById, edges, errors);
        }

        ValidateAcyclic(nodes, edges, errors);
    }

    /// <summary>
    /// The input vocabulary: v5/v6 packages bind exactly consult_draft (the
    /// declaration-free closure, frozen); v7 packages declare their slots —
    /// required section, at least one slot, snake_case unique ids, non-blank
    /// labels (package-format-v7.md § 2). Returns the closed set the binding
    /// checks validate against.
    /// </summary>
    private static IReadOnlySet<string> ValidateInputs(WorkflowPackageManifest manifest, List<string> errors)
    {
        if (manifest.SpecVersion < 7)
        {
            if (manifest.Inputs != null)
            {
                errors.Add("inputs requires specVersion 7.");
            }

            return new HashSet<string>(StringComparer.Ordinal) { "consult_draft" };
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);

        if (manifest.Inputs is not { Count: > 0 })
        {
            errors.Add(manifest.Inputs is null
                ? "inputs is required in specVersion 7."
                : "inputs must declare at least one input slot in specVersion 7.");
            return declared;
        }

        foreach (var input in manifest.Inputs)
        {
            if (!WorkflowDeclaredIds.IsValid(input.Id))
            {
                errors.Add($"Input id '{input.Id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
                continue; // An ill-formed id joins no vocabulary.
            }

            if (!declared.Add(input.Id))
            {
                errors.Add($"Duplicate input id '{input.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(input.Label))
            {
                errors.Add($"Input '{input.Id}' has no label.");
            }

            ValidateInputType(manifest, input, errors);
        }

        return declared;
    }

    /// <summary>
    /// v8 types an input slot (package-format-v8-design.md § 4). An absent type
    /// is text, which is what keeps every v7 declaration valid — so the minimal
    /// v8 migration is the specVersion line and nothing else.
    /// </summary>
    private static void ValidateInputType(WorkflowPackageManifest manifest, WorkflowInputSpec input, List<string> errors)
    {
        if (manifest.SpecVersion < 8)
        {
            // Mirrors "inputs requires specVersion 7": a section the version
            // does not have is an error, never a silently ignored field.
            if (input.Type != null)
            {
                errors.Add($"Input '{input.Id}' declares a type, which requires specVersion 8.");
            }

            if (input.Values != null)
            {
                errors.Add($"Input '{input.Id}' declares values, which requires specVersion 8.");
            }

            return;
        }

        var type = WorkflowInputTypes.Of(input);

        if (!WorkflowInputTypes.All.Contains(type, StringComparer.Ordinal))
        {
            errors.Add($"Input '{input.Id}' declares unknown type '{type}' (accepted: {string.Join(", ", WorkflowInputTypes.All)}).");
            return; // An unknown type says nothing about whether values belong.
        }

        if (type != WorkflowInputTypes.Enum)
        {
            if (input.Values != null)
            {
                errors.Add($"Input '{input.Id}' is type '{type}' and may not declare values.");
            }

            return;
        }

        if (input.Values is not { Count: > 0 })
        {
            errors.Add($"Input '{input.Id}' is type 'enum' and must declare values.");
            return;
        }

        if (input.Values.Count < 2)
        {
            errors.Add($"Input '{input.Id}' declares one enum value; an enum with one value is a constant, not a choice.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in input.Values)
        {
            // Enum values share the declared-id rule, so they are safe wherever
            // result ids are: authored package content, never patient data.
            if (!WorkflowDeclaredIds.IsValid(value))
            {
                errors.Add($"Input '{input.Id}' enum value '{value}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
                continue;
            }

            if (!seen.Add(value))
            {
                errors.Add($"Input '{input.Id}' declares duplicate enum value '{value}'.");
            }
        }
    }

    /// <summary>The v7 result set: authored ids and labels over distinct aggregator nodes (package-format-v7.md § 3).</summary>
    private static void ValidateResultSet(
        List<WorkflowResultSpec> results,
        IReadOnlyDictionary<string, WorkflowNodeSpec> nodesById,
        List<string> errors)
    {
        if (results.Count == 0)
        {
            errors.Add("results must declare at least one deliverable.");
            return;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nodeOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var result in results)
        {
            if (!WorkflowDeclaredIds.IsValid(result.Id))
            {
                errors.Add($"Result id '{result.Id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
            }
            else if (!ids.Add(result.Id))
            {
                errors.Add($"Duplicate result id '{result.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(result.Label))
            {
                errors.Add($"Result '{result.Id}' has no label.");
            }

            if (result.Node is not { } nodeRef
                || !nodeRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                || nodeRef.Length == WorkflowNodeBindingSources.NodePrefix.Length)
            {
                errors.Add($"Result '{result.Id}' node '{result.Node}' must be 'node:<id>'.");
                continue;
            }

            var nodeId = nodeRef[WorkflowNodeBindingSources.NodePrefix.Length..];

            if (!nodesById.TryGetValue(nodeId, out var node))
            {
                errors.Add($"Result '{result.Id}' references unknown node '{nodeId}'.");
                continue;
            }

            if (node.Aggregate is null)
            {
                errors.Add($"Result '{result.Id}' must reference an aggregator node ('{nodeId}' is not one).");
            }

            if (!nodeOwners.TryAdd(nodeId, result.Id))
            {
                errors.Add($"Results '{nodeOwners[nodeId]}' and '{result.Id}' share node '{nodeId}': each deliverable needs its own aggregator.");
            }
        }
    }

    /// <summary>
    /// The reachability closure (v6, union-rooted since v7): every node must
    /// transitively feed a result through binding or aggregate edges — the
    /// orphan-prompt philosophy applied to execution (package-format-v6-design.md
    /// § 7). Each result must also transitively include at least one forEach
    /// source: a deliverable with no fan has no consult.
    ///
    /// #227: both errors name the fix, not only the rule. Whether the
    /// per-result scope is right at all is a separate question, recorded in
    /// package-format-v7-design.md § 11 — the justification is about the
    /// package, the enforcement is per deliverable.
    /// </summary>
    private static void ValidateReachability(
        WorkflowPackageManifest manifest,
        IReadOnlyList<WorkflowNodeSpec> nodes,
        IReadOnlyDictionary<string, WorkflowNodeSpec> nodesById,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        List<string> errors)
    {
        // (result id, node id) roots; the string form contributes one unnamed
        // root. Ill-formed declarations already reported their own errors.
        var roots = new List<(string? ResultId, string NodeId)>();

        if (manifest.SpecVersion >= 7 && manifest.Results != null)
        {
            foreach (var result in manifest.Results)
            {
                if (result.Node is { } nodeRef
                    && nodeRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                    && nodesById.ContainsKey(nodeRef[WorkflowNodeBindingSources.NodePrefix.Length..]))
                {
                    roots.Add((result.Id, nodeRef[WorkflowNodeBindingSources.NodePrefix.Length..]));
                }
            }
        }
        else if (manifest.Result != null
            && manifest.Result.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
            && nodesById.ContainsKey(manifest.Result[WorkflowNodeBindingSources.NodePrefix.Length..]))
        {
            roots.Add((null, manifest.Result[WorkflowNodeBindingSources.NodePrefix.Length..]));
        }

        if (roots.Count == 0)
        {
            return;
        }

        var reachableFromAny = new HashSet<string>(StringComparer.Ordinal);

        // #227: the two shapes of this failure need different advice. An
        // author whose package fans somewhere has a routing problem and wants
        // to know what to route; an author whose package fans nowhere would be
        // told to aggregate over something that does not exist.
        var forEachNodeIds = nodes
            .Where(node => node.ForEach != null)
            .Select(node => node.Id)
            .Order(StringComparer.Ordinal)
            .ToList();

        var forEachRemedy = forEachNodeIds.Count > 0
            ? $"Add an aggregator whose sources include a forEach node and bind it into this result (forEach nodes in this package: {string.Join(", ", forEachNodeIds)})."
            : "This package declares no forEach node — add one over a data collection, then aggregate it into this result.";

        foreach (var (resultId, rootId) in roots)
        {
            var reachable = WalkDependencies(rootId, edges);
            reachableFromAny.UnionWith(reachable);

            if (!reachable.Any(id => nodesById.TryGetValue(id, out var reached) && reached.ForEach != null))
            {
                // The rule first, unchanged: existing callers and tests match
                // on it, and the guidance is additive.
                errors.Add(resultId is null
                    ? $"The result must transitively include at least one forEach source in specVersion {manifest.SpecVersion}: a deliverable with no fan has no consult. {forEachRemedy}"
                    : $"Result '{resultId}' must transitively include at least one forEach source: a deliverable with no fan has no consult. {forEachRemedy}");
            }
        }

        foreach (var node in nodes)
        {
            if (!reachableFromAny.Contains(node.Id))
            {
                errors.Add(roots.Count == 1
                    ? $"Node '{node.Id}' does not feed the result: every node must transitively reach '{roots[0].NodeId}' in specVersion {manifest.SpecVersion}. Bind it into a node that does, or add it to the result's aggregator."
                    : $"Node '{node.Id}' does not feed any result: every node must transitively reach a result node in specVersion {manifest.SpecVersion}. Bind it into a node that does, or add it to a result's aggregator.");
            }
        }
    }

    private static HashSet<string> WalkDependencies(string rootId, IReadOnlyDictionary<string, HashSet<string>> edges)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal) { rootId };
        var frontier = new Queue<string>();
        frontier.Enqueue(rootId);

        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();

            if (!edges.TryGetValue(current, out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                if (reachable.Add(dependency))
                {
                    frontier.Enqueue(dependency);
                }
            }
        }

        return reachable;
    }

    private static bool TryResolveForEachCollection(
        WorkflowNodeSpec node,
        WorkflowPackageData data,
        out string? error,
        out WorkflowDataCollection? collection)
    {
        error = null;
        collection = null;

        if (!WorkflowNodeBindingSources.TryParse(node.ForEach!, out var source, out _)
            || source is not WorkflowNodeBindingSource.Data dataSource)
        {
            error = $"forEach '{node.ForEach}' must be a data: collection reference.";
            return false;
        }

        if (data.Collections.TryGetValue(dataSource.Id, out var resolved))
        {
            collection = resolved;
            return true;
        }

        error = data.Scalars.ContainsKey(dataSource.Id)
            ? $"forEach references scalar data entry '{dataSource.Id}' (forEach requires a collection)."
            : $"forEach references unknown data entry '{dataSource.Id}'.";
        return false;
    }

    private static void ValidateResult(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, WorkflowNodeSpec> nodesById,
        bool v6OrLater,
        List<string> errors)
    {
        if (manifest.SpecVersion >= 7 && manifest.Results != null)
        {
            // The list form owns the declaration; the string form stays valid
            // only as one-entry sugar (package-format-v7.md § 3).
            if (manifest.Result != null)
            {
                errors.Add("Declare result or results, not both.");
                return;
            }

            ValidateResultSet(manifest.Results, nodesById, errors);
            return;
        }

        if (manifest.Results != null)
        {
            errors.Add("results requires specVersion 7.");
            // The string result still validates below.
        }

        if (manifest.Result is null)
        {
            errors.Add(manifest.SpecVersion >= 7
                ? "A result or results declaration is required in specVersion 7."
                : $"result is required in specVersion {manifest.SpecVersion}.");
            return;
        }

        if (!manifest.Result.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
            || manifest.Result.Length == WorkflowNodeBindingSources.NodePrefix.Length)
        {
            errors.Add($"result '{manifest.Result}' must be 'node:<id>'.");
            return;
        }

        var resultNodeId = manifest.Result[WorkflowNodeBindingSources.NodePrefix.Length..];

        if (!nodesById.TryGetValue(resultNodeId, out var resultNode))
        {
            errors.Add($"result references unknown node '{resultNodeId}'.");
            return;
        }

        if (v6OrLater)
        {
            // The deliverable is the assembled document: v6+ results always name
            // an aggregator (package-format-v6-design.md § 4).
            if (resultNode.Aggregate is null)
            {
                errors.Add($"result must reference an aggregator node in specVersion {manifest.SpecVersion} ('{resultNodeId}' is not one).");
            }
        }
        else if (resultNode.ForEach is null)
        {
            errors.Add($"result must reference a forEach node ('{resultNodeId}' runs once).");
        }
    }

    private static void ValidateNodeOutput(
        WorkflowNodeSpec node,
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string> catalogSchemas,
        List<string> errors)
    {
        if (node.Output is null)
        {
            return;
        }

        if (manifest.Schemas is null || !manifest.Schemas.TryGetValue(node.Output.Schema, out var schemaPath))
        {
            errors.Add($"Node '{node.Id}' output schema '{node.Output.Schema}' is not declared in schemas.");
            return;
        }

        if (!files.TryGetValue(schemaPath, out var schemaText))
        {
            errors.Add($"Schema '{node.Output.Schema}' file '{schemaPath}' is missing from the package.");
            return;
        }

        JsonNode? schema;
        try
        {
            schema = JsonNode.Parse(schemaText);
        }
        catch (JsonException ex)
        {
            errors.Add($"Schema '{node.Output.Schema}' does not parse as JSON: {ex.Message}");
            return;
        }

        // The closure, catalog-shaped since milestone 5: every declared schema must
        // canonically match some catalog output contract (modulo title/description) —
        // schemas are welded to attested agents, and the catalog names them
        // (output-contract-catalog.md). This subsumes the structured-outputs-subset check.
        var canonicalSchema = CanonicalizeSchema(schema);
        if (!catalogSchemas.Values.Any(catalogSchema => CanonicalizeSchema(JsonNode.Parse(catalogSchema)) == canonicalSchema))
        {
            errors.Add($"Schema '{node.Output.Schema}' must canonically match a catalog output contract (modulo title/description).");
        }

        if (node.Output.FailIfEmpty != null && string.IsNullOrWhiteSpace(node.Output.FailIfEmpty))
        {
            errors.Add($"Node '{node.Id}' failIfEmpty must not be blank.");
        }
    }

    /// <summary>Sorted-key serialization with title/description stripped recursively.</summary>
    internal static string CanonicalizeSchema(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var parts = obj
                .Where(pair => pair.Key is not ("title" or "description"))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{JsonSerializer.Serialize(pair.Key)}:{CanonicalizeSchema(pair.Value)}");
            return "{" + string.Join(",", parts) + "}";
        }

        if (node is JsonArray array)
        {
            return "[" + string.Join(",", array.Select(CanonicalizeSchema)) + "]";
        }

        return node?.ToJsonString() ?? "null";
    }

    private static void ValidateAcyclic(
        IReadOnlyList<WorkflowNodeSpec> nodes,
        IReadOnlyDictionary<string, HashSet<string>> edges,
        List<string> errors)
    {
        // Kahn's algorithm, seeded in manifest order for deterministic error text.
        // Duplicate ids are already reported; deduplicate so the walk still runs.
        nodes = nodes.DistinctBy(n => n.Id, StringComparer.Ordinal).ToList();
        var remainingDeps = nodes.ToDictionary(
            n => n.Id,
            n => new HashSet<string>(edges.GetValueOrDefault(n.Id) ?? new HashSet<string>(), StringComparer.Ordinal),
            StringComparer.Ordinal);
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        var progressed = true;

        while (progressed)
        {
            progressed = false;
            foreach (var node in nodes)
            {
                if (resolved.Contains(node.Id) || remainingDeps[node.Id].Except(resolved).Any())
                {
                    continue;
                }

                resolved.Add(node.Id);
                progressed = true;
            }
        }

        if (resolved.Count < nodes.Count)
        {
            var cyclic = nodes.Where(n => !resolved.Contains(n.Id)).Select(n => n.Id);
            errors.Add($"nodes contain a cycle involving: {string.Join(", ", cyclic)}.");
        }
    }

    private static void ValidateTemplate(
        WorkflowPromptSpec prompt,
        string templateText,
        List<string> errors,
        List<string> warnings)
    {
        var template = Template.Parse(templateText);
        if (template.HasErrors)
        {
            errors.Add($"Prompt '{prompt.Id}' template does not parse: {string.Join("; ", template.Messages)}");
            return;
        }

        // Undeclared-variable use: render in strict mode against exactly the declared
        // variables with placeholder values; any other access throws.
        try
        {
            var probe = new ScriptObject();
            foreach (var variable in prompt.Variables)
            {
                probe.Add(variable, "placeholder");
            }

            var context = new TemplateContext { StrictVariables = true };
            context.PushGlobal(probe);
            template.Render(context);
        }
        catch (Exception ex)
        {
            errors.Add($"Prompt '{prompt.Id}' failed strict rendering with its declared variables: {ex.Message}");
        }

        // Unused-declaration heuristic (warning only): the variable name never appears
        // in the template text.
        foreach (var variable in prompt.Variables.Where(v => !templateText.Contains(v, StringComparison.Ordinal)))
        {
            warnings.Add($"Prompt '{prompt.Id}' declares variable '{variable}' but the template never mentions it.");
        }
    }

    private static Version GetScribanVersion()
    {
        var assembly = typeof(Template).Assembly;

        // NuGet packages commonly pin AssemblyVersion to Major.0.0; the real package
        // version is in the informational version (possibly with +metadata/-prerelease).
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational != null
            && Version.TryParse(informational.Split('+', '-')[0], out var packageVersion))
        {
            return new Version(packageVersion.Major, packageVersion.Minor, Math.Max(packageVersion.Build, 0));
        }

        var version = assembly.GetName().Version ?? new Version(0, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }
}
