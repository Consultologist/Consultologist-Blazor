using System.Globalization;
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
    /// <summary>
    /// The formats a package may be PUBLISHED against, which is deliberately not
    /// the set WorkflowPackageStore will RUN. The two move independently so a
    /// format can be published and validated against before the engine executes
    /// it — that is how v8 shipped (package-format-v8-design.md § 8). The
    /// invariant is Supported ⊆ Accepted, held by SpecVersionSetTests, and both
    /// are checked against the published spec-versions.json there too.
    /// </summary>
    public static readonly IReadOnlyList<int> AcceptedSpecVersions = new[] { 5, 6, 7, 8 };

    /// <summary>
    /// "5, 6, 7 or 8" — the order a sentence reads in, which is not what
    /// string.Join produces and not the shape WorkflowPackageSpecVersionException
    /// uses. Two different sentences about the same set, both asserted verbatim
    /// by tests, so neither may drift into the other's wording by accident.
    /// </summary>
    internal static string DescribeAcceptedSpecVersions()
    {
        var accepted = AcceptedSpecVersions.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToList();
        return accepted.Count == 1
            ? accepted[0]
            : $"{string.Join(", ", accepted.Take(accepted.Count - 1))} or {accepted[^1]}";
    }

    /// <summary>The Scriban version this engine renders with (Major.Minor.Patch).</summary>
    public static readonly Version EngineScribanVersion = GetScribanVersion();

    /// <summary>
    /// #357: the probe's stand-in for a date-typed variable. Any real date does;
    /// what matters is that it is a DateTime, so a template may format it.
    /// </summary>
    private static readonly DateTime ProbeDate = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Scriban globals a prompt variable would shadow. Only the ones whose loss
    /// is silent rather than loud are worth warning about — a shadowed `date`
    /// makes every date in the template render as a .NET default instead of the
    /// format the package declares.
    /// </summary>
    private static readonly HashSet<string> ScribanBuiltinNames =
        new(StringComparer.Ordinal) { "date", "string", "array", "math", "object", "regex", "timespan", "html" };

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

            ValidateTemplate(prompt, templateText, ProbeTypes(manifest), errors, warnings);
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
        if (!AcceptedSpecVersions.Contains(manifest.SpecVersion))
        {
            errors.Add($"specVersion {manifest.SpecVersion} is not supported: this engine accepts specVersion {DescribeAcceptedSpecVersions()} (pre-v5 packages are archived; see registry-operations.md).");
        }
        else
        {
            ValidateDerivedFrom(manifest, errors);
            ValidateNodes(manifest, files, catalogSchemas, errors);
            WarnUnreachableByEmail(manifest, warnings);
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

        // #355: one definition of the node→node edges, shared with the job
        // starter's prune. Built up front from the declaration rather than
        // accumulated as validation proceeds, so reachability and the cycle
        // check see the graph the engine will walk — not the subset that
        // happened to pass the checks above them.
        var edges = WorkflowNodeClosure.Edges(nodes);

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
        WorkflowPackageManifest manifest,
        List<WorkflowResultSpec> results,
        IReadOnlyDictionary<string, WorkflowNodeSpec> nodesById,
        IReadOnlyDictionary<string, WorkflowInputSpec> declaredInputs,
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

        foreach (var result in results)
        {
            ValidateResultCondition(manifest, result, declaredInputs, errors);
        }
    }

    /// <summary>
    /// The vocabulary closure over a deliverable's condition
    /// (package-format-v8-design.md § 5). The parser has already settled the
    /// syntax; what is checked here needs the declaration.
    ///
    /// Conditions read <c>enum</c> and <c>boolean</c> inputs only. Date
    /// equality asks merely "was it exactly this day" until ordering exists
    /// (#338), and text equality compares a referral byte for byte — neither
    /// is a choice, which is what a condition is for. Widening this is
    /// additive and safe; narrowing it later would strand published packages,
    /// so the narrow rule comes first.
    /// </summary>
    private static void ValidateResultCondition(
        WorkflowPackageManifest manifest,
        WorkflowResultSpec result,
        IReadOnlyDictionary<string, WorkflowInputSpec> declaredInputs,
        List<string> errors)
    {
        if (result.When is null)
        {
            return;
        }

        if (manifest.SpecVersion < 8)
        {
            errors.Add($"Result '{result.Id}' declares when, which requires specVersion 8.");
            return;
        }

        if (!WorkflowResultConditions.TryParse(result.When, out var condition, out var syntaxError))
        {
            errors.Add($"Result '{result.Id}' condition {syntaxError}");
            return;
        }

        if (!declaredInputs.TryGetValue(condition!.InputId, out var input))
        {
            errors.Add($"Result '{result.Id}' condition reads undeclared input '{condition.InputId}' (declared: {string.Join(", ", declaredInputs.Keys.Order(StringComparer.Ordinal))}).");
            return;
        }

        var type = WorkflowInputTypes.Of(input);

        if (type is not (WorkflowInputTypes.Enum or WorkflowInputTypes.Boolean))
        {
            errors.Add($"Result '{result.Id}' condition reads input '{condition.InputId}', which is a {type}: only enum and boolean inputs can be tested.");
            return;
        }

        if (condition.Literal is null)
        {
            // The bare form asks "is this true", which only a boolean answers.
            if (type != WorkflowInputTypes.Boolean)
            {
                errors.Add($"Result '{result.Id}' condition '{condition.InputId}' tests an enum for truth; compare it to one of its values instead.");
            }

            return;
        }

        if (type == WorkflowInputTypes.Boolean && condition.Literal is not ("true" or "false"))
        {
            errors.Add($"Result '{result.Id}' condition compares boolean '{condition.InputId}' to '{condition.Literal}'; use true or false.");
            return;
        }

        if (type == WorkflowInputTypes.Enum && input.Values?.Contains(condition.Literal, StringComparer.Ordinal) != true)
        {
            // An undeclared value is an authoring error, not a condition that
            // silently never holds.
            errors.Add($"Result '{result.Id}' condition compares '{condition.InputId}' to '{condition.Literal}', which it does not declare (values: {string.Join(", ", input.Values ?? new List<string>())}).");
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
            var reachable = WorkflowNodeClosure.Reachable(new[] { rootId }, edges);
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

            // Built here rather than threaded from ValidateInputs, which
            // returns the id set the binding checks close over. Conditions
            // need the declaration itself — the type and, for an enum, its
            // values.
            var declaredInputs = (manifest.Inputs ?? new List<WorkflowInputSpec>())
                .Where(input => WorkflowDeclaredIds.IsValid(input.Id))
                .GroupBy(input => input.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            ValidateResultSet(manifest, manifest.Results, nodesById, declaredInputs, errors);
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

    /// <summary>
    /// Variable name → the declared input type the probe should render it as
    /// (#357).
    ///
    /// The runtime types per NODE — a node's bindings say which of its
    /// variables read a typed input. A prompt has no such environment of its
    /// own: from v6 a prompt may be shared, and each using node binds every
    /// variable itself, with no rule forcing two nodes to agree. So a variable
    /// is typed here only when every binding that reaches it agrees; anything
    /// else stays the string it has always been, and a template that formats it
    /// as a date fails — correctly, because it would be wrong for the node
    /// passing a string.
    ///
    /// This runs before ValidateNodes and before the specVersion gate, so
    /// nothing here may assume the declaration is well formed: an unknown type
    /// string, a duplicate id or a binding naming no declared input all fall
    /// back to a string rather than throwing.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ProbeTypes(WorkflowPackageManifest manifest)
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var input in manifest.Inputs ?? new List<WorkflowInputSpec>())
        {
            var type = WorkflowInputTypes.Of(input);

            // Only the two converted types change what the probe hands Scriban;
            // text and enum are strings at runtime too.
            if (type is WorkflowInputTypes.Date or WorkflowInputTypes.Boolean)
            {
                declared[input.Id] = type;
            }
        }

        if (declared.Count == 0)
        {
            return declared;
        }

        var typed = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflicted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            foreach (var (variable, binding) in node.Bindings ?? new Dictionary<string, WorkflowBindingValue>())
            {
                var type = binding.From.StartsWith(WorkflowNodeBindingSources.InputPrefix, StringComparison.Ordinal)
                    ? declared.GetValueOrDefault(binding.From[WorkflowNodeBindingSources.InputPrefix.Length..])
                    : null;

                if (typed.TryGetValue(variable, out var seen) && seen != type)
                {
                    conflicted.Add(variable);
                }
                else if (type != null)
                {
                    typed[variable] = type;
                }
                else if (typed.ContainsKey(variable))
                {
                    conflicted.Add(variable);
                }
            }
        }

        foreach (var variable in conflicted)
        {
            typed.Remove(variable);
        }

        return typed;
    }

    /// <summary>
    /// #370: a package the email door can never start, said at publish rather
    /// than left for a sender to discover.
    ///
    /// An emailed value is always text — a body or a .txt attachment — and a
    /// string in a boolean slot is a 422 by design (package-format-v8.md § wire
    /// form), deliberately, since the alternative is guessing that "yes" in a
    /// body means true. So a boolean slot cannot be filled through that door at
    /// all, and two declarations make a package unreachable by construction:
    /// a REQUIRED boolean (inputs never resolve), and every deliverable
    /// conditioned on a boolean (inputs resolve, but no condition can hold —
    /// absence satisfies nothing, including the negated form — so the fire set
    /// is always empty and the job is refused at start, #315).
    ///
    /// Neither is caught by testing in the app, where both work perfectly.
    /// That is what makes this worth saying: the author sees it run, publishes,
    /// and has silently produced something one of the two doors cannot accept.
    ///
    /// A date or an enum gets no warning. Both are JSON strings on the wire and
    /// email supplies them fine — a seen_on.txt holding 2026-08-10 is a
    /// verified path. The boolean is the only type the door cannot express.
    ///
    /// Warnings rather than errors, for #357's reason: this validator also runs
    /// at LOAD, so a new error would make an already-published package that
    /// trips it unresolvable, and published versions are immutable. Not
    /// hypothetical — acct-* versions declaring a required boolean are live.
    /// </summary>
    private static void WarnUnreachableByEmail(WorkflowPackageManifest manifest, List<string> warnings)
    {
        // Booleans arrive with v8; before it there is nothing to say.
        if (manifest.SpecVersion < 8)
        {
            return;
        }

        var inputs = manifest.Inputs ?? new List<WorkflowInputSpec>();

        foreach (var input in inputs.Where(input =>
            input.Required && WorkflowInputTypes.Of(input) == WorkflowInputTypes.Boolean))
        {
            warnings.Add(
                $"Input '{input.Id}' is a required boolean. Email can only supply text, so this package "
                + "cannot be started from the email door.");
        }

        var results = manifest.Results ?? new List<WorkflowResultSpec>();

        if (results.Count == 0)
        {
            return;
        }

        var inputsById = inputs
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        // Reachable the moment ONE deliverable can fire: an unconditional one
        // always does, and an enum condition is answerable in text. Only when
        // every deliverable is gated on a boolean is the fire set always empty.
        //
        // Both a MISSING condition and a MALFORMED one fail TryParse — it
        // answers false with "is blank." for the first — so either makes All
        // false and warns nothing. That is right for both: an unconditional
        // deliverable is genuinely reachable, and a malformed one is an error
        // being reported elsewhere, which must not also be read as unreachable.
        var everyResultNeedsABoolean = results.All(result =>
            WorkflowResultConditions.TryParse(result.When, out var condition, out _)
            && inputsById.TryGetValue(condition!.InputId, out var input)
            && WorkflowInputTypes.Of(input) == WorkflowInputTypes.Boolean);

        if (everyResultNeedsABoolean)
        {
            warnings.Add(
                "Every deliverable's condition reads a boolean, which email cannot supply, so no document "
                + "would ever apply to an emailed consult and this package cannot be started from the email door.");
        }
    }

    private static void ValidateTemplate(
        WorkflowPromptSpec prompt,
        string templateText,
        IReadOnlyDictionary<string, string> probeTypes,
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
                // #357: typed where the bindings agree, so a template may format
                // a date it was given as a date. Before this every variable was
                // the string "placeholder", and the format's own documented
                // idiom — {{ seen_on | date.to_string "%d %B %Y" }} — could not
                // publish, because Scriban refuses string → DateTime.
                probe.Add(variable, probeTypes.GetValueOrDefault(variable) switch
                {
                    WorkflowInputTypes.Date => ProbeDate,
                    WorkflowInputTypes.Boolean => true,
                    _ => "placeholder"
                });
            }

            var context = new TemplateContext { StrictVariables = true };
            context.PushGlobal(probe);
            template.Render(context);
        }
        catch (Exception ex)
        {
            errors.Add($"Prompt '{prompt.Id}' failed strict rendering with its declared variables: {ex.Message}");
        }

        // #357: a variable named for a Scriban builtin shadows it. `date` is the
        // one that bites: with it shadowed, Scriban finds no date functions and
        // EVERY date in the template silently renders as 08/12/2026 00:00:00 —
        // including ones the shadowing variable has nothing to do with.
        //
        // A warning rather than an error because this validator also runs at
        // load: a new error would make an already-published package that trips
        // it unresolvable, and published versions are immutable.
        foreach (var variable in prompt.Variables.Where(v => ScribanBuiltinNames.Contains(v)))
        {
            warnings.Add(
                $"Prompt '{prompt.Id}' declares variable '{variable}', which shadows Scriban's built-in "
                    + $"'{variable}' object — dates in this template will not render as the format specifies.");
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
