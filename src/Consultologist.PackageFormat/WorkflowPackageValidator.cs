using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Runtime;


namespace Consultologist.PackageFormat;

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
    public static readonly IReadOnlyList<int> AcceptedSpecVersions = new[] { 5, 6, 7, 8, 9 };

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

    // v9: a number probes as a decimal, which is what the renderer hands
    // Scriban — with a fraction, so a template that formats it meets one.
    private const decimal ProbeNumber = 1.5m;

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
    /// <param name="stampedContracts">
    /// #433: schema id → contract id as the publication stamp recorded it. A
    /// schema the stamp covers was matched once, at publish, under the catalog
    /// the stamp names; it is not re-matched here. Null — every unstamped
    /// version, and every caller but the store — keeps the closure as it was.
    /// </param>
    public static ValidationResult Validate(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, string> catalogSchemas,
        IReadOnlyDictionary<string, string>? stampedContracts = null)
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
            ValidateMetadata(manifest, errors);
            ValidateDerivedFrom(manifest, errors);
            ValidateNodes(manifest, files, catalogSchemas, stampedContracts, errors);
            WarnUnreachableByEmail(manifest, warnings);
            WarnFannedOptionalInputs(manifest, warnings);
        }

        return new ValidationResult(errors, warnings);
    }

    /// <summary>
    /// v9 § 4 (#432): a title and a description, both optional, both arriving
    /// at 9 — below it each is refused by name, as items and fields are. A
    /// whitespace-only value is empty and says only that; a non-empty title may
    /// be told both that it spans lines and that it is too long. Lengths are
    /// UTF-16 code units (WorkflowPackageMetadata).
    /// </summary>
    private static void ValidateMetadata(WorkflowPackageManifest manifest, List<string> errors)
    {
        if (manifest.SpecVersion < 9)
        {
            if (manifest.Title != null)
            {
                errors.Add("title requires specVersion 9.");
            }

            if (manifest.Description != null)
            {
                errors.Add("description requires specVersion 9.");
            }

            if (manifest.Tags != null)
            {
                errors.Add("tags requires specVersion 9.");
            }

            return;
        }

        if (manifest.Title != null)
        {
            if (string.IsNullOrWhiteSpace(manifest.Title))
            {
                errors.Add("title must not be empty.");
            }
            else
            {
                if (manifest.Title.Contains('\r') || manifest.Title.Contains('\n'))
                {
                    errors.Add("title must be a single line.");
                }

                if (manifest.Title.Length > WorkflowPackageMetadata.MaxTitleLength)
                {
                    errors.Add($"title must be at most {WorkflowPackageMetadata.MaxTitleLength} characters.");
                }
            }
        }

        if (manifest.Description != null)
        {
            if (string.IsNullOrWhiteSpace(manifest.Description))
            {
                errors.Add("description must not be empty.");
            }
            else if (manifest.Description.Length > WorkflowPackageMetadata.MaxDescriptionLength)
            {
                errors.Add($"description must be at most {WorkflowPackageMetadata.MaxDescriptionLength} characters.");
            }
        }

        ValidateTags(manifest.Tags, errors);
    }

    /// <summary>
    /// #453: tags are required at 9 — an empty array is the spelling of
    /// "none" — and each one is held to a label's rules. Sentences name the
    /// POSITION, never the text: a tag is authored content, but the offending
    /// one is the one whose text is the problem, and the author can see it.
    /// Every rule reports, so a tag that is both too long and a repeat is
    /// told both.
    /// </summary>
    private static void ValidateTags(List<string>? tags, List<string> errors)
    {
        if (tags is null)
        {
            errors.Add("tags is required in specVersion 9 (an empty array when the package has none).");
            return;
        }

        if (tags.Count > WorkflowPackageMetadata.MaxTags)
        {
            errors.Add($"tags must declare at most {WorkflowPackageMetadata.MaxTags} tags.");
        }

        for (var i = 0; i < tags.Count; i++)
        {
            var tag = tags[i];

            if (string.IsNullOrWhiteSpace(tag))
            {
                errors.Add($"tags[{i}] must not be empty.");
                continue;
            }

            if (tag.Contains('\r') || tag.Contains('\n'))
            {
                errors.Add($"tags[{i}] must be a single line.");
            }

            if (!string.Equals(tag, tag.Trim(), StringComparison.Ordinal))
            {
                errors.Add($"tags[{i}] must not begin or end with whitespace.");
            }

            if (tag.Length > WorkflowPackageMetadata.MaxTagLength)
            {
                errors.Add($"tags[{i}] must be at most {WorkflowPackageMetadata.MaxTagLength} characters.");
            }

            // Distinct ignoring case: a filter treats Oncology and oncology as
            // one, so the manifest may not declare them as two. The earlier
            // position is the one kept; the later one is the repeat.
            for (var j = 0; j < i; j++)
            {
                if (!string.IsNullOrWhiteSpace(tags[j]) && string.Equals(tags[j].Trim(), tag.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"tags[{i}] repeats tags[{j}]; tags are distinct ignoring case.");
                    break;
                }
            }
        }
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
        IReadOnlyDictionary<string, string>? stampedContracts,
        List<string> errors)
    {
        var v6OrLater = manifest.SpecVersion >= 6;
        var declaredInputs = ValidateInputs(manifest, errors);
        // The declarations themselves, for what a fan needs to know (v9): the
        // id set above closes bindings; an input fan needs the type.
        var inputsById = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .Where(input => WorkflowDeclaredIds.IsValid(input.Id))
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
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
                    if (!TryResolveForEachSource(manifest, node, data, inputsById, out _, out var collection, out var inputFan))
                    {
                        return; // The forEach itself is refused beside this; one complaint is enough.
                    }

                    if (collection != null && !collection.Fields.Contains(item.Field))
                    {
                        errors.Add($"Node '{node.Id}' binds '{variable}' to unknown item field '{item.Field}' (the collection declares: {string.Join(", ", collection.Fields)}).");
                    }
                    else if (inputFan != null && !WorkflowInputFans.ItemFields.Contains(item.Field, StringComparer.Ordinal))
                    {
                        // One shape for every caller element (v9 § 5): the element
                        // itself is item:value, whatever its kind.
                        errors.Add($"Node '{node.Id}' binds '{variable}' to unknown item field '{item.Field}' (an input fan's items carry: {string.Join(", ", WorkflowInputFans.ItemFields)}).");
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

            if (node.ForEach != null && !TryResolveForEachSource(manifest, node, data, inputsById, out var forEachError, out _, out _))
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

            ValidateNodeOutput(node, manifest, files, catalogSchemas, stampedContracts, errors);
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
    ///
    /// v9 adds number, object and array, with `items` for an array and
    /// `fields` for an object (package-format-v9-design.md § 4). The type set
    /// is keyed by version: a v8 manifest is held to v8's four names, and its
    /// refusals read exactly as the published conformance suite recorded them.
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

            if (input.Items != null)
            {
                errors.Add($"Input '{input.Id}' declares items, which requires specVersion 9.");
            }

            if (input.Fields != null)
            {
                errors.Add($"Input '{input.Id}' declares fields, which requires specVersion 9.");
            }

            return;
        }

        var accepted = WorkflowInputTypes.ForSpecVersion(manifest.SpecVersion);
        var type = WorkflowInputTypes.Of(input);

        if (manifest.SpecVersion < 9)
        {
            // v9's vocabulary on a v8 manifest: the same posture as the gate
            // above, so an author learns which version to move to rather than
            // that a name is unknown.
            var gated = false;

            if (input.Type != null && !accepted.Contains(type, StringComparer.Ordinal)
                && WorkflowInputTypes.All.Contains(type, StringComparer.Ordinal))
            {
                errors.Add($"Input '{input.Id}' declares type '{type}', which requires specVersion 9.");
                gated = true;
            }

            if (input.Items != null)
            {
                errors.Add($"Input '{input.Id}' declares items, which requires specVersion 9.");
                gated = true;
            }

            if (input.Fields != null)
            {
                errors.Add($"Input '{input.Id}' declares fields, which requires specVersion 9.");
                gated = true;
            }

            if (gated)
            {
                return;
            }
        }

        if (!accepted.Contains(type, StringComparer.Ordinal))
        {
            errors.Add($"Input '{input.Id}' declares unknown type '{type}' (accepted: {string.Join(", ", accepted)}).");
            return; // An unknown type says nothing about whether values belong.
        }

        var subject = $"Input '{input.Id}'";
        var isArray = type == WorkflowInputTypes.Array;

        // items: required for an array, one of the element types, forbidden
        // otherwise. Structure is one level deep, so an array of arrays is
        // refused by name rather than as an unknown element type.
        if (isArray)
        {
            if (input.Items is null)
            {
                errors.Add($"{subject} is type 'array' and must declare items.");
                return;
            }

            if (input.Items.Type == WorkflowInputTypes.Array)
            {
                errors.Add($"{subject} declares items 'array'; structure is one level deep, so an array may not hold arrays.");
                return;
            }

            if (!WorkflowInputTypes.ElementTypes.Contains(input.Items.Type, StringComparer.Ordinal))
            {
                errors.Add($"{subject} declares unknown items type '{input.Items.Type}' (accepted: {string.Join(", ", WorkflowInputTypes.ElementTypes)}).");
                return;
            }
        }
        else if (input.Items != null)
        {
            errors.Add($"{subject} is type '{type}' and may not declare items.");
        }

        // fields: required when the declaration is an object or an array of
        // objects, forbidden otherwise.
        if (WorkflowInputTypes.DeclaresObject(input))
        {
            if (input.Fields is not { Count: > 0 })
            {
                errors.Add(isArray
                    ? $"{subject} is an array of objects and must declare fields."
                    : $"{subject} is type 'object' and must declare fields.");
            }
            else
            {
                ValidateFields(input, errors);
            }
        }
        else if (input.Fields != null)
        {
            errors.Add($"{subject} is type '{type}' and may not declare fields.");
        }

        // values: an enum's, or an array of enums'. Nothing else has a choice
        // to declare.
        var valuesBelong = type == WorkflowInputTypes.Enum
            || (isArray && input.Items?.Type == WorkflowInputTypes.Enum);

        if (!valuesBelong)
        {
            if (input.Values != null)
            {
                errors.Add($"{subject} is type '{type}' and may not declare values.");
            }

            return;
        }

        ValidateEnumValues(subject, input.Values, errors);
    }

    /// <summary>
    /// The enum rules, for an input, an array's elements or a field alike: at
    /// least two values, unique, each a declared id. Enum values share the
    /// declared-id rule, so they are safe wherever result ids are — authored
    /// package content, never patient data.
    /// </summary>
    private static void ValidateEnumValues(string subject, List<string>? values, List<string> errors)
    {
        if (values is not { Count: > 0 })
        {
            errors.Add($"{subject} is type 'enum' and must declare values.");
            return;
        }

        if (values.Count < 2)
        {
            errors.Add($"{subject} declares one enum value; an enum with one value is a constant, not a choice.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (!WorkflowDeclaredIds.IsValid(value))
            {
                errors.Add($"{subject} enum value '{value}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
                continue;
            }

            if (!seen.Add(value))
            {
                errors.Add($"{subject} declares duplicate enum value '{value}'.");
            }
        }
    }

    /// <summary>
    /// An object's fields (v9 § 4): ids snake_case and unique, a label each, a
    /// scalar type — structure is one level deep — and values for an enum
    /// field on the usual terms.
    /// </summary>
    private static void ValidateFields(WorkflowInputSpec input, List<string> errors)
    {
        var subject = $"Input '{input.Id}'";
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var field in input.Fields!)
        {
            if (!WorkflowDeclaredIds.IsValid(field.Id))
            {
                errors.Add($"{subject} field id '{field.Id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
                continue;
            }

            if (!seen.Add(field.Id))
            {
                errors.Add($"{subject} declares duplicate field id '{field.Id}'.");
            }

            var fieldSubject = $"{subject} field '{field.Id}'";

            if (string.IsNullOrWhiteSpace(field.Label))
            {
                errors.Add($"{fieldSubject} has no label.");
            }

            var type = WorkflowInputTypes.Of(field);

            if (type is WorkflowInputTypes.Object or WorkflowInputTypes.Array)
            {
                errors.Add($"{fieldSubject} is type '{type}'; structure is one level deep, so a field holds a scalar.");
                continue;
            }

            if (!WorkflowInputTypes.Scalars.Contains(type, StringComparer.Ordinal))
            {
                errors.Add($"{fieldSubject} declares unknown type '{type}' (accepted: {string.Join(", ", WorkflowInputTypes.Scalars)}).");
                continue;
            }

            if (type != WorkflowInputTypes.Enum)
            {
                if (field.Values != null)
                {
                    errors.Add($"{fieldSubject} is type '{type}' and may not declare values.");
                }

                continue;
            }

            ValidateEnumValues(fieldSubject, field.Values, errors);
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
    /// (package-format-v8-design.md § 5, v9 § 6). The parser has already
    /// settled the syntax; what is checked here needs the declaration.
    ///
    /// v8 conditions read <c>enum</c> and <c>boolean</c> inputs only, and a
    /// v8 manifest is still held to that — its refusals read as they always
    /// did. v9 widens once: ordering for a number or a date, a path into one
    /// field of an object, count() of an array, the bare form on an array.
    /// Text stays incomparable: a referral byte for byte is not a choice.
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

        if (manifest.SpecVersion < 9)
        {
            ValidateV8Condition(result, condition, input, errors);
            return;
        }

        ValidateV9Condition(result, condition, input, errors);
    }

    /// <summary>The v8 rules, verbatim: the conformance suite and the editor quote these sentences.</summary>
    private static void ValidateV8Condition(
        WorkflowResultSpec result,
        WorkflowResultCondition condition,
        WorkflowInputSpec input,
        List<string> errors)
    {
        // v9's forms on a v8 manifest: named, the way every version gate is.
        if (condition.Field != null)
        {
            errors.Add($"Result '{result.Id}' condition reads a field of '{condition.InputId}', which requires specVersion 9.");
            return;
        }

        if (condition.IsCount)
        {
            errors.Add($"Result '{result.Id}' condition counts '{condition.InputId}', which requires specVersion 9.");
            return;
        }

        if (condition.IsOrdered)
        {
            errors.Add($"Result '{result.Id}' condition compares '{condition.InputId}' with {condition.Ordering}, which requires specVersion 9.");
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
    /// v9 § 6. The operand is resolved to a type first — the input's, one
    /// field's, or a count — and the operator and literal are held to it.
    /// Every message names the type, so an author learns why rather than
    /// hunting a syntax error.
    /// </summary>
    private static void ValidateV9Condition(
        WorkflowResultSpec result,
        WorkflowResultCondition condition,
        WorkflowInputSpec input,
        List<string> errors)
    {
        var prefix = $"Result '{result.Id}' condition";
        var inputType = WorkflowInputTypes.Of(input);
        string operandType;
        List<string>? values;

        if (condition.IsCount)
        {
            if (inputType != WorkflowInputTypes.Array)
            {
                errors.Add($"{prefix} counts '{condition.InputId}', which is a {inputType}; only an array has a count.");
                return;
            }

            if (condition.IsBare)
            {
                errors.Add($"{prefix} '{condition.Operand}' needs a comparison; write count({condition.InputId}) > 0.");
                return;
            }

            if (!int.TryParse(condition.Literal, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which is not a whole number.");
            }

            return;
        }

        if (condition.Field != null)
        {
            if (inputType != WorkflowInputTypes.Object)
            {
                errors.Add($"{prefix} reads field '{condition.Field}' of '{condition.InputId}', which is a {inputType}, not an object.");
                return;
            }

            var field = input.Fields?.FirstOrDefault(f => string.Equals(f.Id, condition.Field, StringComparison.Ordinal));

            if (field is null)
            {
                errors.Add($"{prefix} reads field '{condition.Field}' of '{condition.InputId}', which it does not declare (fields: {string.Join(", ", (input.Fields ?? new List<WorkflowFieldSpec>()).Select(f => f.Id))}).");
                return;
            }

            operandType = WorkflowInputTypes.Of(field);
            values = field.Values;
        }
        else
        {
            operandType = inputType;
            values = input.Values;
        }

        if (condition.IsBare)
        {
            if (operandType is WorkflowInputTypes.Boolean or WorkflowInputTypes.Array)
            {
                return;
            }

            errors.Add(operandType == WorkflowInputTypes.Enum
                ? $"{prefix} '{condition.Operand}' tests an enum for truth; compare it to one of its values instead."
                : $"{prefix} '{condition.Operand}' tests a {operandType} for truth; only a boolean or an array can be tested bare.");
            return;
        }

        if (operandType == WorkflowInputTypes.Text)
        {
            errors.Add($"{prefix} reads '{condition.Operand}', which is a text: a text input cannot be tested.");
            return;
        }

        if (operandType is WorkflowInputTypes.Object or WorkflowInputTypes.Array)
        {
            errors.Add($"{prefix} compares '{condition.Operand}', which is an {operandType}; compare one of its fields, or its count, instead.");
            return;
        }

        if (condition.IsOrdered && operandType is not (WorkflowInputTypes.Number or WorkflowInputTypes.Date))
        {
            errors.Add($"{prefix} compares '{condition.Operand}' with {condition.Ordering}, which is a {operandType}; ordering operators apply to a number or a date.");
            return;
        }

        switch (operandType)
        {
            case WorkflowInputTypes.Boolean when condition.Literal is not ("true" or "false"):
                errors.Add($"{prefix} compares boolean '{condition.Operand}' to '{condition.Literal}'; use true or false.");
                break;

            case WorkflowInputTypes.Enum when values?.Contains(condition.Literal!, StringComparer.Ordinal) != true:
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which it does not declare (values: {string.Join(", ", values ?? new List<string>())}).");
                break;

            case WorkflowInputTypes.Number when !ConsultInputValue.TryParseNumber(condition.Literal!, out _):
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which is not a plain decimal.");
                break;

            case WorkflowInputTypes.Date when !DateOnly.TryParseExact(condition.Literal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which is not a date written YYYY-MM-DD.");
                break;
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

    /// <summary>
    /// What a node fans: a data collection, as always — or, from v9, an array
    /// input (package-format-v9-design.md § 5, #426). Below 9 the sentence is
    /// the one it always was; an input fan on an older manifest reads as a
    /// source the format does not know, which is true of that version.
    /// </summary>
    private static bool TryResolveForEachSource(
        WorkflowPackageManifest manifest,
        WorkflowNodeSpec node,
        WorkflowPackageData data,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        out string? error,
        out WorkflowDataCollection? collection,
        out WorkflowInputSpec? inputFan)
    {
        error = null;
        collection = null;
        inputFan = null;

        if (!WorkflowNodeBindingSources.TryParse(node.ForEach!, out var source, out _))
        {
            error = $"forEach '{node.ForEach}' must be a data: collection reference.";
            return false;
        }

        switch (source)
        {
            case WorkflowNodeBindingSource.Data dataSource:
                if (data.Collections.TryGetValue(dataSource.Id, out var resolved))
                {
                    collection = resolved;
                    return true;
                }

                error = data.Scalars.ContainsKey(dataSource.Id)
                    ? $"forEach references scalar data entry '{dataSource.Id}' (forEach requires a collection)."
                    : $"forEach references unknown data entry '{dataSource.Id}'.";
                return false;

            case WorkflowNodeBindingSource.Input input when manifest.SpecVersion >= 9:
                if (!inputsById.TryGetValue(input.Name, out var declaration))
                {
                    error = $"forEach '{node.ForEach}' fans undeclared input '{input.Name}'.";
                    return false;
                }

                if (WorkflowInputTypes.Of(declaration) != WorkflowInputTypes.Array)
                {
                    error = $"forEach '{node.ForEach}' fans input '{input.Name}', which is a {WorkflowInputTypes.Of(declaration)}; only an array can be fanned.";
                    return false;
                }

                inputFan = declaration;
                return true;

            default:
                error = $"forEach '{node.ForEach}' must be a data: collection reference.";
                return false;
        }
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
        IReadOnlyDictionary<string, string>? stampedContracts,
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
        //
        // #433: a schema the publication stamp covers was matched once, at
        // publish, under the catalog the stamp names. Re-matching it here against
        // whatever catalog is running is what stranded immutable packages; the
        // store checks the stamped contract still exists instead.
        if (stampedContracts is null || !stampedContracts.ContainsKey(node.Output.Schema))
        {
            var canonicalSchema = CanonicalizeSchema(schema);
            if (!catalogSchemas.Values.Any(catalogSchema => CanonicalizeSchema(JsonNode.Parse(catalogSchema)) == canonicalSchema))
            {
                errors.Add($"Schema '{node.Output.Schema}' must canonically match a catalog output contract (modulo title/description).");
            }
        }

        if (node.Output.FailIfEmpty != null && string.IsNullOrWhiteSpace(node.Output.FailIfEmpty))
        {
            errors.Add($"Node '{node.Id}' failIfEmpty must not be blank.");
        }
    }

    /// <summary>Sorted-key serialization with title/description stripped recursively.</summary>
    public static string CanonicalizeSchema(JsonNode? node)
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
    /// What the probe hands Scriban for a declared input — the type the
    /// renderer will (v9 § 4, *The publish-time probe*): a date, a boolean, a
    /// decimal, an object carrying its declared fields, or an array of
    /// <b>two</b> element probes. Two rather than one, so a template that
    /// assumes a singleton fails the probe rather than the job.
    /// </summary>
    internal static object ProbeValue(WorkflowInputSpec input)
    {
        var type = WorkflowInputTypes.Of(input);

        return type switch
        {
            WorkflowInputTypes.Object => ObjectProbe(input.Fields),
            WorkflowInputTypes.Array => new ScriptArray { ElementProbe(input), ElementProbe(input) },
            _ => ScalarProbe(type)
        };
    }

    private static object ElementProbe(WorkflowInputSpec input) =>
        input.Items?.Type == WorkflowInputTypes.Object
            ? ObjectProbe(input.Fields)
            : ScalarProbe(WorkflowInputTypes.ElementTypeOf(input));

    private static ScriptObject ObjectProbe(IEnumerable<WorkflowFieldSpec>? fields)
    {
        var probe = new ScriptObject();

        // Runs before the declaration is validated, so a malformed field list
        // probes as what it can rather than throwing.
        foreach (var field in fields ?? Array.Empty<WorkflowFieldSpec>())
        {
            if (!string.IsNullOrEmpty(field.Id) && !probe.ContainsKey(field.Id))
            {
                probe.Add(field.Id, ScalarProbe(WorkflowInputTypes.Of(field)));
            }
        }

        return probe;
    }

    private static object ScalarProbe(string type) => type switch
    {
        WorkflowInputTypes.Date => ProbeDate,
        WorkflowInputTypes.Boolean => true,
        WorkflowInputTypes.Number => ProbeNumber,
        _ => "placeholder"
    };

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
    private static IReadOnlyDictionary<string, WorkflowInputSpec> ProbeTypes(WorkflowPackageManifest manifest) =>
        // One rule with the renderer's activity (#425): the same map that types
        // a probe is the map that types a job, so what publishes is what runs.
        WorkflowVariableDeclarations.For(manifest);

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
    /// <summary>
    /// v9 (#426): a fan over an array the caller may leave out. An absent or
    /// empty fanned input produces no items, therefore no blocks, therefore no
    /// document — the job is refused at start by name. That is correct, and
    /// it is worth the author hearing at publish rather than discovering on
    /// every consult that skipped the slot. A warning: this validator runs at
    /// load.
    /// </summary>
    private static void WarnFannedOptionalInputs(WorkflowPackageManifest manifest, List<string> warnings)
    {
        if (manifest.SpecVersion < 9)
        {
            return;
        }

        var inputsById = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .GroupBy(input => input.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var node in (manifest.Nodes ?? new List<WorkflowNodeSpec>()).Where(node => WorkflowInputFans.IsInputFan(node.ForEach)))
        {
            var id = WorkflowInputFans.InputIdOf(node.ForEach!);

            if (inputsById.TryGetValue(id, out var input) && !input.Required)
            {
                warnings.Add(
                    $"Input '{id}' is optional but node '{node.Id}' fans it: a consult that leaves it empty is refused at start, "
                    + "because every document written from it would have nothing to say. Declare it required, or accept the refusal.");
            }
        }
    }

    private static void WarnUnreachableByEmail(WorkflowPackageManifest manifest, List<string> warnings)
    {
        // Booleans arrive with v8; before it there is nothing to say.
        if (manifest.SpecVersion < 8)
        {
            return;
        }

        var inputs = manifest.Inputs ?? new List<WorkflowInputSpec>();

        // v9 (package-format-v9-design.md § 4, Intake): a number and an object
        // are as unreachable by email as a boolean — the door supplies text. An
        // array is not listed: attachments fill an array of text (§ 7, #428).
        foreach (var input in inputs.Where(input =>
            input.Required && WorkflowInputTypes.Of(input) is WorkflowInputTypes.Boolean
                or WorkflowInputTypes.Number or WorkflowInputTypes.Object))
        {
            warnings.Add(
                $"Input '{input.Id}' is a required {WorkflowInputTypes.Of(input)}. Email can only supply text, so this package "
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
        IReadOnlyDictionary<string, WorkflowInputSpec> probeTypes,
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
                probe.Add(variable, probeTypes.TryGetValue(variable, out var declaration)
                    ? ProbeValue(declaration)
                    : "placeholder");
            }

            // The renderer's own context, so what publishes is what runs —
            // including v9's rule that an empty array is falsy (#425).
            var context = new PromptTemplateRenderer.RenderingContext();
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
