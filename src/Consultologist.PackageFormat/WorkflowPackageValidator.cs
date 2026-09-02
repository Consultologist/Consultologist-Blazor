using System.Globalization;
using System.Text.RegularExpressions;
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
    public static readonly IReadOnlyList<int> AcceptedSpecVersions = new[] { 5, 6, 7, 8, 9, 10, 11, 12 };

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
            ValidateNodes(manifest, files, catalogSchemas, stampedContracts, errors, warnings);
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
        List<string> errors,
        List<string> warnings)
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

        // v10 (§ 4): the classifiers, for what may read them and what may not.
        var classifierIds = nodes
            .Where(WorkflowNodeKinds.IsClassifier)
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

                    // v10 (§ 4): a classifier runs before anything is produced, so
                    // it may read inputs and other classifiers, never a prompt
                    // node or an aggregator — or the boundary is unreachable.
                    if (WorkflowNodeKinds.IsClassifier(node) && !classifierIds.Contains(target.NodeId))
                    {
                        errors.Add($"Classifier '{node.Id}' binds '{variable}' to 'node:{target.NodeId}', which is not a classifier; a classifier may read inputs and classifiers only.");
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

                // v10 (§ 4): a classification is a symbol, not a document.
                if (classifierIds.Contains(sourceId))
                {
                    errors.Add($"Aggregator node '{node.Id}' aggregates classifier '{sourceId}'; a classifier's value is bindable, never aggregated.");
                }
            }
        }

        // v10 (§ 4): the classifying node — a prompt node whose answer is one
        // of its declared values. What it may not carry, it is told by name.
        void CheckClassifier(WorkflowNodeSpec node)
        {
            var subject = $"Classifier '{node.Id}'";

            if (node.Output != null)
            {
                errors.Add($"{subject} declares output; a classifier's output is the classification contract, implied by its kind.");
            }

            if (node.ForEach != null)
            {
                errors.Add($"{subject} declares forEach; a classification is one answer, so a classifier is never fanned.");
            }

            if (node.Values is not { Count: > 0 })
            {
                errors.Add($"{subject} declares no values; a classifier must declare the values it may answer.");
                return;
            }

            ValidateEnumValues(subject, node.Values, errors, noun: "value");
        }

        // v12 (§ 13): whether a node's declared output is the concept-list
        // contract. For a stamped package the stamp is the authority (#433 —
        // re-matching under a later catalog is what stranded immutable
        // packages); otherwise the canonical match ValidateNodeOutput also
        // runs. Deliberately NOT conceptListNodeIds above, which despite its
        // name means "declares any output".
        bool DeclaresConceptList(WorkflowNodeSpec target)
        {
            if (target.Output is null
                || manifest.Schemas is null
                || !manifest.Schemas.TryGetValue(target.Output.Schema, out var schemaPath))
            {
                return false;
            }

            if (stampedContracts != null && stampedContracts.TryGetValue(target.Output.Schema, out var contractId))
            {
                return contractId == WorkflowNodeDefaults.ConceptListSchemaId;
            }

            if (!files.TryGetValue(schemaPath, out var schemaText)
                || !catalogSchemas.TryGetValue(WorkflowNodeDefaults.ConceptListSchemaId, out var conceptListSchema))
            {
                return false;
            }

            try
            {
                return CanonicalizeSchema(JsonNode.Parse(schemaText)) == CanonicalizeSchema(JsonNode.Parse(conceptListSchema));
            }
            catch (JsonException)
            {
                return false;
            }
        }

        // v12 (§ 13): the check node — the CheckAggregator discipline (the
        // property is the behaviour; the prompt family must be absent), plus
        // typed operands: both must be concept-list nodes, because the check
        // is a pure set operation over two recorded model answers.
        void CheckCheckNode(WorkflowNodeSpec node)
        {
            var subject = $"Check '{node.Id}'";

            if (node.Prompt != null || node.Bindings is { Count: > 0 } || node.Output != null || node.ForEach != null || node.Values != null)
            {
                errors.Add($"{subject} must declare only op, of, in and failWith (no prompt, bindings, output, forEach, or values).");
            }

            if (node.Reproducible != null)
            {
                errors.Add($"{subject} declares reproducible; a check is deterministic by construction, and the claim is not its to make.");
            }

            if (node.Op is null)
            {
                errors.Add($"{subject} declares no op; the operations are {string.Join(", ", WorkflowCheckOps.All)}.");
            }
            else if (!WorkflowCheckOps.All.Contains(node.Op, StringComparer.Ordinal))
            {
                errors.Add($"{subject} declares unknown op '{node.Op}' (accepted: {string.Join(", ", WorkflowCheckOps.All)}).");
            }

            if (string.IsNullOrWhiteSpace(node.FailWith))
            {
                errors.Add($"{subject} declares no failWith; a failed check must speak the package's own sentence.");
            }

            foreach (var (member, operand) in new[] { ("of", node.Of), ("in", node.In) })
            {
                if (operand is null)
                {
                    errors.Add($"{subject} declares no {member}; a check names its two concept-list operands as node:<id> references.");
                    continue;
                }

                if (!operand.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal))
                {
                    errors.Add($"{subject} {member} '{operand}' must be a node:<id> reference.");
                    continue;
                }

                var targetId = operand[WorkflowNodeBindingSources.NodePrefix.Length..];

                if (!nodesById.TryGetValue(targetId, out var target))
                {
                    errors.Add($"{subject} {member} references undeclared node '{targetId}'.");
                }
                else if (!DeclaresConceptList(target))
                {
                    errors.Add($"{subject} {member} names node '{targetId}', which does not declare the concept-list contract.");
                }
            }
        }

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Label))
            {
                errors.Add($"Node '{node.Id}' has no label.");
            }

            // v11 (§ 6): reproducible arrives at 11 — refused by name below it,
            // the posture every gated member has had since v8. No continue: the
            // node's other members are whatever version they are.
            if (manifest.SpecVersion < 11 && node.Reproducible != null)
            {
                errors.Add($"Node '{node.Id}' declares reproducible, which requires specVersion 11.");
            }

            // v12 (§ 13): the check node and its members arrive at 12 — below
            // it each is refused by name, and nothing else about a check is
            // meaningful, so the gate continues (the v10 shape). Sits before
            // the unknown-kind branch so kind 'check' reads as a version
            // requirement, never an unknown word.
            if (manifest.SpecVersion < 12)
            {
                var gatedCheck = false;

                if (WorkflowNodeKinds.IsCheck(node))
                {
                    errors.Add($"Node '{node.Id}' declares kind 'check', which requires specVersion 12.");
                    gatedCheck = true;
                }

                if (node.Op != null)
                {
                    errors.Add($"Node '{node.Id}' declares op, which requires specVersion 12.");
                    gatedCheck = true;
                }

                if (node.Of != null)
                {
                    errors.Add($"Node '{node.Id}' declares of, which requires specVersion 12.");
                    gatedCheck = true;
                }

                if (node.In != null)
                {
                    errors.Add($"Node '{node.Id}' declares in, which requires specVersion 12.");
                    gatedCheck = true;
                }

                if (node.FailWith != null)
                {
                    errors.Add($"Node '{node.Id}' declares failWith, which requires specVersion 12.");
                    gatedCheck = true;
                }

                if (gatedCheck)
                {
                    continue;
                }
            }
            else if (!WorkflowNodeKinds.IsCheck(node))
            {
                // At 12, the check members belong to the check kind alone —
                // the closed-grammar posture.
                foreach (var (member, value) in new[] { ("op", node.Op), ("of", node.Of), ("in", node.In), ("failWith", node.FailWith) })
                {
                    if (value != null)
                    {
                        errors.Add($"Node '{node.Id}' declares {member} but is not a check node; only kind 'check' declares it.");
                    }
                }
            }

            // v10 (§ 4): kind and values arrive at 10 — below it each is refused
            // by name, the posture every gated member has had since v8.
            if (manifest.SpecVersion < 10)
            {
                var gated = false;

                if (node.Kind != null)
                {
                    errors.Add($"Node '{node.Id}' declares kind, which requires specVersion 10.");
                    gated = true;
                }

                if (node.Values != null)
                {
                    errors.Add($"Node '{node.Id}' declares values, which requires specVersion 10.");
                    gated = true;
                }

                if (gated)
                {
                    continue;
                }
            }
            else if (node.Kind != null && !WorkflowNodeKinds.AllFor(manifest.SpecVersion).Contains(node.Kind, StringComparer.Ordinal))
            {
                // Version-keyed (v12 § 13): the sentence names the kinds THIS
                // manifest's version may spell, so a v10 record stays true.
                errors.Add(node.Kind == "aggregator"
                    ? $"Node '{node.Id}' declares kind 'aggregator'; an aggregator is declared by aggregate, not by kind."
                    : $"Node '{node.Id}' declares unknown kind '{node.Kind}' (accepted: {string.Join(", ", WorkflowNodeKinds.AllFor(manifest.SpecVersion))}).");
                continue;
            }
            else if (node.Values != null && !WorkflowNodeKinds.IsClassifier(node))
            {
                errors.Add($"Node '{node.Id}' declares values but is not a classifier; only kind 'classifier' answers from a value set.");
                continue;
            }

            if (node.Aggregate != null)
            {
                if (!v6OrLater)
                {
                    errors.Add($"Node '{node.Id}' declares aggregate, which requires specVersion 6 or later.");
                    continue;
                }

                if (node.Kind != null)
                {
                    errors.Add($"Node '{node.Id}' declares both kind and aggregate; an aggregator is declared by aggregate alone.");
                    continue;
                }

                CheckAggregator(node);
                continue;
            }

            if (WorkflowNodeKinds.IsCheck(node))
            {
                // v12 (§ 13): the second deterministic executor — like the
                // aggregator it continues before the no-prompt rule, because
                // a check by construction has none.
                CheckCheckNode(node);
                continue;
            }

            if (WorkflowNodeKinds.IsClassifier(node))
            {
                CheckClassifier(node);
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
        ValidateMacros(manifest, files, inputsById, data, classifierIds, errors, warnings);

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

        if (manifest.SpecVersion < 10 && input.Items is { IsBare: false })
        {
            // v10's element spec on a v9 manifest: the object form of items is
            // refused by name, never read as an unknown element type.
            errors.Add($"{subject} declares items as a shape, which requires specVersion 10.");
            return;
        }

        ValidateShape(manifest.SpecVersion, subject, type, input.Items, input.Fields, input.Values, errors);
    }

    /// <summary>
    /// The shape rules shared by an input, a field (v10 § 7) and an element
    /// spec: items required for an array and forbidden otherwise, fields
    /// required for an object or an array of objects and forbidden otherwise,
    /// values for an enum or an array of enums and nothing else — recursing
    /// into the element spec and the fields at 10. Below 10 an array may not
    /// hold arrays and a field holds a scalar, refused in v9's own words.
    /// </summary>
    private static void ValidateShape(
        int specVersion,
        string subject,
        string type,
        WorkflowElementSpec? items,
        List<WorkflowFieldSpec>? fields,
        List<string>? values,
        List<string> errors)
    {
        var isArray = type == WorkflowInputTypes.Array;
        var elementTypes = WorkflowInputTypes.ElementTypesFor(specVersion);

        // items: required for an array, one of the element types, forbidden
        // otherwise. Below 10 structure is one level deep, so an array of
        // arrays is refused by name rather than as an unknown element type.
        if (isArray)
        {
            if (items is null)
            {
                errors.Add($"{subject} is type 'array' and must declare items.");
                return;
            }

            if (specVersion < 10 && items.Type == WorkflowInputTypes.Array)
            {
                errors.Add($"{subject} declares items 'array'; structure is one level deep, so an array may not hold arrays.");
                return;
            }

            if (!elementTypes.Contains(items.Type, StringComparer.Ordinal))
            {
                errors.Add($"{subject} declares unknown items type '{items.Type}' (accepted: {string.Join(", ", elementTypes)}).");
                return;
            }

            // v10: an element spec carries the element's own shape; a bare
            // items keeps the v9 reading, where the element's fields and values
            // are the array's.
            if (!items.IsBare)
            {
                if (fields != null || values != null)
                {
                    errors.Add($"{subject} declares items as a shape and also fields or values; an element spec carries its own.");
                    return;
                }

                ValidateShape(specVersion, $"{subject} items", items.Type, items.Items, items.Fields, items.Values, errors);
                return;
            }
        }
        else if (items != null)
        {
            errors.Add($"{subject} is type '{type}' and may not declare items.");
        }

        // fields: required when the declaration is an object or an array of
        // objects, forbidden otherwise.
        var declaresObject = type == WorkflowInputTypes.Object || (isArray && items?.Type == WorkflowInputTypes.Object);

        if (declaresObject)
        {
            if (fields is not { Count: > 0 })
            {
                errors.Add(isArray
                    ? $"{subject} is an array of objects and must declare fields."
                    : $"{subject} is type 'object' and must declare fields.");
            }
            else
            {
                ValidateFields(specVersion, subject, fields, errors);
            }
        }
        else if (fields != null)
        {
            errors.Add($"{subject} is type '{type}' and may not declare fields.");
        }

        // values: an enum's, or an array of enums'. Nothing else has a choice
        // to declare.
        var valuesBelong = type == WorkflowInputTypes.Enum
            || (isArray && items?.Type == WorkflowInputTypes.Enum);

        if (!valuesBelong)
        {
            if (values != null)
            {
                errors.Add($"{subject} is type '{type}' and may not declare values.");
            }

            return;
        }

        ValidateEnumValues(subject, values, errors);
    }

    /// <summary>
    /// The enum rules, for an input, an array's elements or a field alike: at
    /// least two values, unique, each a declared id. Enum values share the
    /// declared-id rule, so they are safe wherever result ids are — authored
    /// package content, never patient data.
    /// </summary>
    private static void ValidateEnumValues(string subject, List<string>? values, List<string> errors, string noun = "enum value")
    {
        if (values is not { Count: > 0 })
        {
            errors.Add($"{subject} is type 'enum' and must declare values.");
            return;
        }

        if (values.Count < 2)
        {
            errors.Add(noun == "enum value"
                ? $"{subject} declares one enum value; an enum with one value is a constant, not a choice."
                : $"{subject} declares one {noun}; a classifier with one value is a constant, not a choice.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (!WorkflowDeclaredIds.IsValid(value))
            {
                errors.Add($"{subject} {noun} '{value}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
                continue;
            }

            if (!seen.Add(value))
            {
                errors.Add($"{subject} declares duplicate {noun} '{value}'.");
            }
        }
    }

    /// <summary>
    /// An object's fields (v9 § 4): ids snake_case and unique, a label each, a
    /// type and values on the usual terms. v9 held a field to a scalar; v10
    /// (§ 7) lets a field carry items and fields of its own and recurses,
    /// while a v9 manifest is refused in v9's words and told the version.
    /// </summary>
    private static void ValidateFields(int specVersion, string subject, List<WorkflowFieldSpec> fields, List<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var scalars = WorkflowInputTypes.ScalarsFor(specVersion);

        foreach (var field in fields)
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

            if (specVersion < 10)
            {
                // The v9 sentence, kept verbatim: the published conformance
                // suite pins it on a v9 manifest. The new members are what
                // name the version.
                if (type is WorkflowInputTypes.Object or WorkflowInputTypes.Array)
                {
                    errors.Add($"{fieldSubject} is type '{type}'; structure is one level deep, so a field holds a scalar.");
                    continue;
                }

                if (field.Items != null)
                {
                    errors.Add($"{fieldSubject} declares items, which requires specVersion 10.");
                    continue;
                }

                if (field.Fields != null)
                {
                    errors.Add($"{fieldSubject} declares fields, which requires specVersion 10.");
                    continue;
                }
            }

            if (!scalars.Contains(type, StringComparer.Ordinal))
            {
                errors.Add($"{fieldSubject} declares unknown type '{type}' (accepted: {string.Join(", ", scalars)}).");
                continue;
            }

            if (specVersion < 10)
            {
                if (type != WorkflowInputTypes.Enum)
                {
                    if (field.Values != null)
                    {
                        errors.Add($"{fieldSubject} is type '{type}' and may not declare values.");
                    }

                    continue;
                }

                ValidateEnumValues(fieldSubject, field.Values, errors);
                continue;
            }

            ValidateShape(specVersion, fieldSubject, type, field.Items, field.Fields, field.Values, errors);
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

        var classifiers = nodesById.Values.Where(WorkflowNodeKinds.IsClassifier).ToDictionary(n => n.Id, StringComparer.Ordinal);

        // v12 (§ 13): the checks, for the gate that names one — and the
        // orphan rule's sibling: a check nobody names gates nothing.
        var checks = nodesById.Values.Where(WorkflowNodeKinds.IsCheck).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var namedChecks = new HashSet<string>(StringComparer.Ordinal);

        foreach (var result in results)
        {
            ValidateResultCondition(manifest, result, declaredInputs, classifiers, errors);
            ValidateResultCheck(manifest, result, checks, namedChecks, errors);
        }

        foreach (var orphan in checks.Keys.Where(id => !namedChecks.Contains(id)).Order(StringComparer.Ordinal))
        {
            errors.Add($"Check '{orphan}' is not named by any result; a check gates a deliverable, or it is dead weight.");
        }
    }

    /// <summary>
    /// The deliverable's gate (v12 § 13): the post-production mirror of when.
    /// The parser has nothing to settle — a check reference is one node: ref.
    /// </summary>
    private static void ValidateResultCheck(
        WorkflowPackageManifest manifest,
        WorkflowResultSpec result,
        IReadOnlyDictionary<string, WorkflowNodeSpec> checks,
        HashSet<string> namedChecks,
        List<string> errors)
    {
        if (result.Check is null)
        {
            return;
        }

        if (manifest.SpecVersion < 12)
        {
            errors.Add($"Result '{result.Id}' declares check, which requires specVersion 12.");
            return;
        }

        if (!result.Check.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal))
        {
            errors.Add($"Result '{result.Id}' check '{result.Check}' must be a node:<id> reference.");
            return;
        }

        var checkId = result.Check[WorkflowNodeBindingSources.NodePrefix.Length..];

        if (!checks.ContainsKey(checkId))
        {
            errors.Add($"Result '{result.Id}' check names '{checkId}', which is not a check node.");
            return;
        }

        namedChecks.Add(checkId);
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
        IReadOnlyDictionary<string, WorkflowNodeSpec> classifiers,
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

        if (!WorkflowResultConditions.TryParseExpression(result.When, out var expression, out var syntaxError))
        {
            errors.Add($"Result '{result.Id}' condition {syntaxError}");
            return;
        }

        // v10 (#494, § 6): every form past one clause is refused below 10 by
        // name, in the shape every version gate has had — the first one found.
        if (manifest.SpecVersion < 10 && expression!.FirstV10Form is { } form)
        {
            errors.Add(form.StartsWith('\'') && !form.StartsWith("'node:", StringComparison.Ordinal)
                ? $"Result '{result.Id}' condition uses {form}, which requires specVersion 10."
                : form == "arithmetic"
                    ? $"Result '{result.Id}' condition uses arithmetic, which requires specVersion 10."
                    : $"Result '{result.Id}' condition reads {form}, which requires specVersion 10.");
            return;
        }

        // One error per result: the first clause that is wrong, so the v9
        // corpus's single-sentence pins hold and an author fixes one thing.
        foreach (var condition in expression!.Leaves)
        {
            var before = errors.Count;

            if (manifest.SpecVersion >= 10)
            {
                ValidateV10Clause(result, condition, declaredInputs, classifiers, errors);
            }
            else
            {
                if (!declaredInputs.TryGetValue(condition.InputId, out var input))
                {
                    errors.Add($"Result '{result.Id}' condition reads undeclared input '{condition.InputId}' (declared: {string.Join(", ", declaredInputs.Keys.Order(StringComparer.Ordinal))}).");
                    return;
                }

                if (manifest.SpecVersion < 9)
                {
                    ValidateV8Condition(result, condition, input, errors);
                }
                else
                {
                    ValidateV9Condition(result, condition, input, errors);
                }
            }

            if (errors.Count > before)
            {
                return;
            }
        }
    }

    /// <summary>
    /// v10 § 6: every operand — a path of any length, count() of a path, a
    /// classifier's value — is resolved to a declaration node, and the
    /// operator, the literal and any arithmetic are held to it. The v9
    /// sentences are produced for the v9 forms; the new forms have their own.
    /// </summary>
    private static void ValidateV10Clause(
        WorkflowResultSpec result,
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, WorkflowInputSpec> declaredInputs,
        IReadOnlyDictionary<string, WorkflowNodeSpec> classifiers,
        List<string> errors)
    {
        var prefix = $"Result '{result.Id}' condition";

        if (condition.IsArithmetic)
        {
            ValidateArithmeticClause(prefix, condition, declaredInputs, classifiers, errors);
            return;
        }

        if (condition.IsNodeValue)
        {
            if (!classifiers.TryGetValue(condition.NodeId!, out var classifier))
            {
                errors.Add($"{prefix} reads 'node:{condition.NodeId}', which is not a classifier (classifiers: {string.Join(", ", classifiers.Keys.Order(StringComparer.Ordinal))}).");
                return;
            }

            if (condition.IsBare)
            {
                errors.Add($"{prefix} '{condition.Operand}' tests a classifier for truth; compare it to one of its values instead.");
                return;
            }

            if (condition.IsOrdered)
            {
                errors.Add($"{prefix} compares '{condition.Operand}' with {condition.Ordering}; a classifier's value is compared with == or != only.");
                return;
            }

            if (classifier.Values?.Contains(condition.Literal!, StringComparer.Ordinal) != true)
            {
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which it does not declare (values: {string.Join(", ", classifier.Values ?? new List<string>())}).");
            }

            return;
        }

        if (!declaredInputs.TryGetValue(condition.InputId, out var input))
        {
            errors.Add($"{prefix} reads undeclared input '{condition.InputId}' (declared: {string.Join(", ", declaredInputs.Keys.Order(StringComparer.Ordinal))}).");
            return;
        }

        if (condition.PathDepth <= 1 && !condition.IsCount || (condition.IsCount && condition.PathDepth == 0))
        {
            // The v9 forms, in v9's words.
            ValidateV9Condition(result, condition, input, errors);
            return;
        }

        // A path past one segment, or count() of a path.
        if (!TryResolvePath(prefix, condition, input, errors, out var node))
        {
            return;
        }

        if (condition.IsCount)
        {
            if (!node!.IsArray)
            {
                errors.Add($"{prefix} counts '{condition.PathText}', which is {node.Describe()}; only an array has a count.");
                return;
            }

            if (condition.IsBare)
            {
                errors.Add($"{prefix} '{condition.Operand}' needs a comparison; write {condition.Operand} > 0.");
                return;
            }

            if (!int.TryParse(condition.Literal, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which is not a whole number.");
            }

            return;
        }

        ValidateTypedComparison(prefix, condition, node!.Type, node.Values, errors);
    }

    /// <summary>The v9 operand rules over a resolved type: bare, text, structure, ordering, literal.</summary>
    private static void ValidateTypedComparison(string prefix, WorkflowResultCondition condition, string operandType, IReadOnlyList<string>? values, List<string> errors)
    {
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
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which it does not declare (values: {string.Join(", ", values ?? Array.Empty<string>())}).");
                break;

            case WorkflowInputTypes.Number when !ConsultInputValue.TryParseNumber(condition.Literal!, out _):
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which is not a plain decimal.");
                break;

            case WorkflowInputTypes.Date when !DateOnly.TryParseExact(condition.Literal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                errors.Add($"{prefix} compares '{condition.Operand}' to '{condition.Literal}', which is not a date written YYYY-MM-DD.");
                break;
        }
    }

    /// <summary>A dotted path folded through the declaration nodes; the sentence names the segment that does not resolve.</summary>
    private static bool TryResolvePath(string prefix, WorkflowResultCondition condition, WorkflowInputSpec input, List<string> errors, out WorkflowDeclarationNode? node)
    {
        node = WorkflowDeclarationNode.Of(input);
        var walked = condition.InputId;

        foreach (var segment in condition.Segments)
        {
            if (!node.IsObject)
            {
                errors.Add($"{prefix} reads field '{segment}' of '{walked}', which is {node.Describe().Replace("an ", string.Empty).Replace("a ", string.Empty) switch { var t => "a " + t }}, not an object.");
                node = null;
                return false;
            }

            var field = node.Fields?.FirstOrDefault(f => string.Equals(f.Id, segment, StringComparison.Ordinal));

            if (field is null)
            {
                errors.Add($"{prefix} reads field '{segment}' of '{walked}', which it does not declare (fields: {string.Join(", ", (node.Fields ?? Array.Empty<WorkflowDeclarationNode>()).Select(f => f.Id))}).");
                node = null;
                return false;
            }

            node = field;
            walked = $"{walked}.{segment}";
        }

        return true;
    }

    /// <summary>
    /// v10 § 6, arithmetic: over numbers and counts, a date ± whole days;
    /// nothing else. Both sides must resolve to the same kind, and a literal
    /// divisor of zero is named here rather than at every start.
    /// </summary>
    private static void ValidateArithmeticClause(
        string prefix,
        WorkflowResultCondition condition,
        IReadOnlyDictionary<string, WorkflowInputSpec> declaredInputs,
        IReadOnlyDictionary<string, WorkflowNodeSpec> classifiers,
        List<string> errors)
    {
        if (condition.Right is null)
        {
            errors.Add($"{prefix} '{condition.Left!.Text}' is arithmetic with no comparison; write {condition.Left.Text} > 0.");
            return;
        }

        var left = TermKind(prefix, condition.Left!, declaredInputs, errors);
        if (left is null) return;
        var right = TermKind(prefix, condition.Right, declaredInputs, errors);
        if (right is null) return;

        if (left != right)
        {
            errors.Add($"{prefix} compares {left} '{condition.Left!.Text}' with {right} '{condition.Right.Text}'; both sides of a comparison must be numbers, or both dates.");
        }
    }

    /// <summary>"a number" or "a date" for a term, or null after an error.</summary>
    private static string? TermKind(string prefix, WorkflowConditionTerm term, IReadOnlyDictionary<string, WorkflowInputSpec> declaredInputs, List<string> errors)
    {
        switch (term)
        {
            case WorkflowLiteralTerm literal:
                if (ConsultInputValue.TryParseNumber(literal.Literal, out _)) return "a number";
                if (DateOnly.TryParseExact(literal.Literal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return "a date";
                errors.Add($"{prefix} uses '{literal.Literal}' in arithmetic, which is neither a plain decimal nor a date written YYYY-MM-DD.");
                return null;

            case WorkflowNegateTerm negate:
                var inner = TermKind(prefix, negate.Inner, declaredInputs, errors);
                if (inner == "a date")
                {
                    errors.Add($"{prefix} negates '{negate.Inner.Text}', which is a date; only a number can be negated.");
                    return null;
                }
                return inner;

            case WorkflowOperandTerm operand:
                if (operand.Operand.IsNodeValue)
                {
                    errors.Add($"{prefix} uses 'node:{operand.Operand.NodeId}' in arithmetic; a classifier's value is a symbol, not a number.");
                    return null;
                }

                if (operand.Operand.IsCount)
                {
                    if (!declaredInputs.TryGetValue(operand.Operand.InputId, out var counted))
                    {
                        errors.Add($"{prefix} reads undeclared input '{operand.Operand.InputId}' (declared: {string.Join(", ", declaredInputs.Keys.Order(StringComparer.Ordinal))}).");
                        return null;
                    }

                    if (!TryResolvePath(prefix, operand.Operand, counted, errors, out var countedNode)) return null;

                    if (!countedNode!.IsArray)
                    {
                        errors.Add($"{prefix} counts '{operand.Operand.PathText}', which is {countedNode.Describe()}; only an array has a count.");
                        return null;
                    }

                    return "a number";
                }

                if (!declaredInputs.TryGetValue(operand.Operand.InputId, out var input))
                {
                    errors.Add($"{prefix} reads undeclared input '{operand.Operand.InputId}' (declared: {string.Join(", ", declaredInputs.Keys.Order(StringComparer.Ordinal))}).");
                    return null;
                }

                if (!TryResolvePath(prefix, operand.Operand, input, errors, out var node)) return null;

                switch (node!.Type)
                {
                    case WorkflowInputTypes.Number: return "a number";
                    case WorkflowInputTypes.Date: return "a date";
                    default:
                        errors.Add($"{prefix} uses '{operand.Operand.Operand}' in arithmetic, which is {node.Describe()}; arithmetic applies to a number, a count or a date.");
                        return null;
                }

            case WorkflowBinaryTerm binary:
                var l = TermKind(prefix, binary.Left, declaredInputs, errors);
                if (l is null) return null;
                var r = TermKind(prefix, binary.Right, declaredInputs, errors);
                if (r is null) return null;

                if (binary.Op == '/' && binary.Right is WorkflowLiteralTerm { Literal: var divisor }
                    && ConsultInputValue.TryParseNumber(divisor, out var d) && d.NumberValue == 0)
                {
                    errors.Add($"{prefix} divides by zero in '{binary.Text}'.");
                    return null;
                }

                if (l == "a number" && r == "a number") return "a number";

                if (l == "a date" && r == "a number" && binary.Op is '+' or '-')
                {
                    return "a date";
                }

                errors.Add($"{prefix} computes '{binary.Text}', which is {l} {binary.Op} {r}; a date admits only ± whole days, and everything else is numbers.");
                return null;

            default:
                return null;
        }
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

        // v12 (§ 13): a check serves a deliverable's existence, not its text —
        // it and every node it depends on are exempt from feeding a result,
        // the classifier's own reasoning widened to a chain (the backward
        // closure from the checks over the same edge map). A node in the
        // chain that ALSO feeds a result was already reachable; the exemption
        // only spares the ones that exist for the check alone.
        var checkChain = WorkflowNodeClosure.Reachable(
            nodes.Where(WorkflowNodeKinds.IsCheck).Select(n => n.Id),
            edges);

        foreach (var node in nodes)
        {
            // v10 (§ 4): a classifier feeds the boundary, not a document — its
            // value is what the fire set reads, so it is consumed by the job
            // whether or not a prompt binds it.
            if (WorkflowNodeKinds.IsClassifier(node))
            {
                continue;
            }

            if (checkChain.Contains(node.Id))
            {
                continue;
            }

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

    /// <summary>
    /// v11 § 4 (#513): the package's macros — template files with placeholders
    /// from closed namespaces, appended verbatim to the deliverables that name
    /// them. Validated whole here: declaration, file, placeholders, and the
    /// result-side references (the string result form has no macros).
    /// </summary>
    private static void ValidateMacros(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, string> files,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        WorkflowPackageData data,
        IReadOnlySet<string> classifierIds,
        List<string> errors,
        List<string> warnings)
    {
        var results = manifest.Results ?? new List<WorkflowResultSpec>();

        // The result-side keys gate below 11 by name, macros declared or not.
        if (manifest.SpecVersion < 11)
        {
            if (manifest.Macros != null)
            {
                errors.Add("macros requires specVersion 11.");
            }

            foreach (var result in results)
            {
                if (result.Macros != null)
                {
                    errors.Add($"Result '{result.Id}' declares macros, which requires specVersion 11.");
                }

                if (result.Signature != null)
                {
                    errors.Add($"Result '{result.Id}' declares signature, which requires specVersion 11.");
                }
            }

            return;
        }

        // v12 (§ 3/§ 4): the optional pair and the placed entry arrive at 12 —
        // below it each is refused by name. The v11 rules still apply, so no
        // return (the ValidateNodes gate shape, not the block above).
        if (manifest.SpecVersion < 12)
        {
            foreach (var macro in manifest.Macros ?? new List<WorkflowMacroSpec>())
            {
                if (macro.Optional != null)
                {
                    errors.Add($"Macro '{macro.Id}' declares optional, which requires specVersion 12.");
                }

                if (macro.Default != null)
                {
                    errors.Add($"Macro '{macro.Id}' declares default, which requires specVersion 12.");
                }
            }

            foreach (var result in results)
            {
                foreach (var entry in (result.Macros ?? new List<WorkflowResultMacroSpec>()).Where(e => !e.IsBare))
                {
                    errors.Add($"Result '{result.Id}' places macro '{entry.Id}', which requires specVersion 12.");
                }
            }
        }

        var macros = manifest.Macros ?? new List<WorkflowMacroSpec>();
        var macroIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var macro in macros)
        {
            if (!WorkflowDeclaredIds.IsValid(macro.Id))
            {
                errors.Add($"Macro id '{macro.Id}' must be snake_case (a lowercase letter, then lowercase letters, digits, or underscores).");
            }
            else if (!macroIds.Add(macro.Id))
            {
                errors.Add($"Duplicate macro id '{macro.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(macro.Label))
            {
                errors.Add($"Macro '{macro.Id}' has no label.");
            }

            // v12 (§ 3): an optional macro must say what a formless run does —
            // the package decides, every door (#516 carried forward).
            if (manifest.SpecVersion >= 12)
            {
                if (macro.Optional == true && macro.Default == null)
                {
                    errors.Add($"Macro '{macro.Id}' is optional and declares no default; an optional macro must say what a run that makes no choice does.");
                }

                if (macro.Default != null && macro.Optional != true)
                {
                    errors.Add($"Macro '{macro.Id}' declares default but is not optional; only optional: true takes a per-run choice.");
                }
            }

            if (!files.TryGetValue(macro.File, out var template))
            {
                errors.Add($"Macro '{macro.Id}' file '{macro.File}' is missing from the package.");
                continue;
            }

            // Unlike a prompt, whose template probe would catch it, an empty
            // macro appends nothing and means an authoring mistake (§ 4).
            if (string.IsNullOrWhiteSpace(template))
            {
                errors.Add($"Macro '{macro.Id}' file '{macro.File}' is empty.");
                continue;
            }

            ValidateMacroPlaceholders(manifest.SpecVersion, macro, template, inputsById, data, classifierIds, errors, warnings);
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var nodesById = (manifest.Nodes ?? new List<WorkflowNodeSpec>())
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // v12 (§ 5): which declared macros carry {{profile:signature}}, and
        // how many times — the signed-once family reads templates, so the
        // count is per macro file, summed per result below.
        var tokenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var macro in macros)
        {
            if (files.TryGetValue(macro.File, out var template))
            {
                tokenCounts[macro.Id] = WorkflowMacroPlaceholders.Pattern.Matches(template)
                    .Count(match => WorkflowMacroPlaceholders.TokenOf(match) == WorkflowMacroPlaceholders.SignatureToken);
            }
        }

        if (manifest.SpecVersion >= 12)
        {
            foreach (var macro in macros)
            {
                if (macro.Optional == true && tokenCounts.GetValueOrDefault(macro.Id) > 0)
                {
                    errors.Add($"Macro '{macro.Id}' is optional and carries {{{{profile:signature}}}}; a per-run signature choice was rejected (#516) and stays rejected.");
                }
            }
        }

        foreach (var result in results)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var entry in result.Macros ?? new List<WorkflowResultMacroSpec>())
            {
                // v12 (§ 4): an entry is a bare id or a placed object; the id
                // rules hold for both — a placed macro counts as referenced.
                var macroId = entry.Id;

                if (!macroIds.Contains(macroId))
                {
                    errors.Add($"Result '{result.Id}' references undeclared macro '{macroId}'.");
                }
                else if (!seen.Add(macroId))
                {
                    errors.Add($"Result '{result.Id}' lists macro '{macroId}' more than once.");
                }

                referenced.Add(macroId);

                // v12 (§ 4): a placement names exactly one anchor, and the
                // anchor must be a section of THIS deliverable's aggregator.
                if (!entry.IsBare && manifest.SpecVersion >= 12)
                {
                    if (entry.Before != null && entry.After != null)
                    {
                        errors.Add($"Result '{result.Id}' places macro '{entry.Id}' with both before and after; a placement names exactly one.");
                    }
                    else if ((entry.Before ?? entry.After) is { } anchor)
                    {
                        var aggregatorId = result.Node.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                            ? result.Node[WorkflowNodeBindingSources.NodePrefix.Length..]
                            : result.Node;

                        if (nodesById.GetValueOrDefault(aggregatorId)?.Aggregate is not { Count: > 0 } aggregate)
                        {
                            errors.Add($"Result '{result.Id}' places macro '{entry.Id}', but its node declares no aggregate; placement is between sections, and there are none.");
                        }
                        else if (!aggregate.Contains(anchor, StringComparer.Ordinal))
                        {
                            errors.Add($"Result '{result.Id}' places macro '{entry.Id}' {(entry.Before != null ? "before" : "after")} '{anchor}', which its aggregator '{aggregatorId}' does not aggregate.");
                        }
                    }
                }
            }

            // v12 (§ 5): a document is signed once — never by the flag AND an
            // embedded token, and never by two tokens.
            if (manifest.SpecVersion >= 12)
            {
                var carrying = (result.Macros ?? new List<WorkflowResultMacroSpec>())
                    .Select(e => e.Id)
                    .Distinct(StringComparer.Ordinal)
                    .Where(id => tokenCounts.GetValueOrDefault(id) > 0)
                    .ToList();
                var tokensOnResult = carrying.Sum(id => tokenCounts[id]);

                if (result.Signature == true && carrying.Count > 0)
                {
                    errors.Add($"Result '{result.Id}' declares signature and references macro '{carrying[0]}', which contains {{{{profile:signature}}}}; a deliverable is signed once.");
                }
                else if (tokensOnResult > 1)
                {
                    errors.Add($"Result '{result.Id}' references {{{{profile:signature}}}} more than once across its macros; a deliverable is signed once.");
                }
            }
        }

        foreach (var orphan in macroIds.Where(id => !referenced.Contains(id)))
        {
            errors.Add($"Macro '{orphan}' is not referenced by any result.");
        }
    }

    /// <summary>
    /// v11 § 4: every placeholder must name a declared input, a data value, a
    /// classifier, or a word from the closed run:/profile: lists — anything
    /// else is refused naming the token. Macro files are never handed to
    /// Scriban; this scanner is the whole of their grammar, and the grammar
    /// itself (pattern, closed word sets) lives in WorkflowMacroPlaceholders,
    /// shared with the run-time expander.
    /// </summary>
    private static void ValidateMacroPlaceholders(
        int specVersion,
        WorkflowMacroSpec macro,
        string template,
        IReadOnlyDictionary<string, WorkflowInputSpec> inputsById,
        WorkflowPackageData data,
        IReadOnlySet<string> classifierIds,
        List<string> errors,
        List<string> warnings)
    {
        foreach (Match match in WorkflowMacroPlaceholders.Pattern.Matches(template))
        {
            var token = WorkflowMacroPlaceholders.TokenOf(match);
            var resolves = WorkflowMacroPlaceholders.TryParse(token, out var ns, out var id) && ns switch
            {
                "input" => inputsById.ContainsKey(id),
                "data" => data.Scalars.ContainsKey(id),
                "classification" => classifierIds.Contains(id),
                "run" => WorkflowMacroPlaceholders.RunFacts.Contains(id),
                // v12 (§ 5): the profile vocabulary is version-keyed — the
                // signature token resolves at 12 and up.
                "profile" => WorkflowMacroPlaceholders.ProfileFactsFor(specVersion).Contains(id),
                _ => false
            };

            if (!resolves)
            {
                // v12 (§ 5): a token the format knows but this version does
                // not is a version requirement, never an unknown word — the
                // three-way the design mandates.
                var resolvesAtNewest = WorkflowMacroPlaceholders.TryParse(token, out var lateNs, out var lateId)
                    && lateNs == "profile"
                    && WorkflowMacroPlaceholders.ProfileFactsFor(AcceptedSpecVersions.Max()).Contains(lateId);

                errors.Add(resolvesAtNewest
                    ? $"Macro '{macro.Id}' placeholder '{{{{{token}}}}}' requires specVersion 12."
                    : $"Macro '{macro.Id}' placeholder '{{{{{token}}}}}' does not resolve.");
                continue;
            }

            // The author chose an optional input knowingly: absent renders empty (§ 4).
            if (ns == "input" && !inputsById[id].Required)
            {
                warnings.Add($"Macro '{macro.Id}' references optional input '{id}', which renders as empty when not supplied.");
            }
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
    internal static object ProbeValue(WorkflowInputSpec input) => Probe(WorkflowDeclarationNode.Of(input));

    /// <summary>v10 (#493): the probe recurses — two elements per array at every level, a field as its structure.</summary>
    private static object Probe(WorkflowDeclarationNode node) => node.Type switch
    {
        WorkflowInputTypes.Object => ObjectProbe(node.Fields),
        WorkflowInputTypes.Array => new ScriptArray { ElementProbe(node), ElementProbe(node) },
        _ => ScalarProbe(node.Type)
    };

    private static object ElementProbe(WorkflowDeclarationNode array) =>
        array.Items is null ? "placeholder" : Probe(array.Items);

    private static ScriptObject ObjectProbe(IEnumerable<WorkflowDeclarationNode>? fields)
    {
        var probe = new ScriptObject();

        // Runs before the declaration is validated, so a malformed field list
        // probes as what it can rather than throwing.
        foreach (var field in fields ?? Array.Empty<WorkflowDeclarationNode>())
        {
            if (!string.IsNullOrEmpty(field.Id) && !probe.ContainsKey(field.Id))
            {
                probe.Add(field.Id, Probe(field));
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

        // v10 (package-format-v10-design.md § 7): the door names the top array
        // only, so a required array with structure below its elements is as
        // unreachable as a number.
        if (manifest.SpecVersion >= 10)
        {
            foreach (var input in inputs.Where(input =>
                input.Required && WorkflowInputTypes.Of(input) == WorkflowInputTypes.Array
                    && WorkflowDeclarationNode.Of(input).IsDeeperThanOneLevel))
            {
                warnings.Add(
                    $"Input '{input.Id}' is a required array with structure deeper than one level. Email can only supply text "
                    + "or a list of documents, so this package cannot be started from the email door.");
            }
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
            WorkflowResultConditions.TryParseExpression(result.When, out var expression, out _)
            && expression!.Leaves.Any()
            && expression.Leaves.All(condition =>
                !condition.IsNodeValue
                && inputsById.TryGetValue(condition.InputId, out var input)
                && WorkflowInputTypes.Of(input) == WorkflowInputTypes.Boolean));

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
