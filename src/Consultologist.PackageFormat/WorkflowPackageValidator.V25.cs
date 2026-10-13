namespace Consultologist.PackageFormat;

/// <summary>
/// The validator at specVersion 25 (package-format-v25-design.md; A4, #999).
/// A 25 package is a different grammar — objects in templates, formats on
/// nodes, snippets read and macros placed, deliverables — so it takes its own
/// path after the shared metadata checks, and the v5–v24 path is left
/// byte-for-byte as it was. Every retired key is refused by name with its
/// replacement in the sentence (§ 10); every required member is named.
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

        var formats = ResolveFormatsAt25(manifest, files, catalog, errors);
        var shapes = ValidateNodesAt25(manifest, formats, catalog, errors);
        ValidateMacroLibraryAt25(manifest, files, errors);

        // The templates, the derived edges, the macros' placement, the
        // deliverables and the strict render follow in the second wiring PR.
        _ = shapes;
        _ = warnings;
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

    /// <summary>A format a node may name: its id, its spec, and whether it is the package's own or the catalogue's.</summary>
    internal sealed record ResolvedFormat(string Id, WorkflowFormatSpec Spec, WorkflowFormatOrigin Origin);

    /// <summary>
    /// The package-local formats of <c>manifest.formats[]</c>, each file read
    /// and validated as a local (custom) format. Catalogue ids are resolved per
    /// node against <paramref name="catalog"/>; the map returned holds only the
    /// local ones.
    /// </summary>
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

    // ----- § 3.6: the node -----------------------------------------------------

    /// <summary>
    /// Node ids, formats, prompts and policies at 25. Returns each node's
    /// shape as a template sees it (format id, type, schema) for the walk.
    /// </summary>
    private static IReadOnlyDictionary<string, TemplateNodeShape> ValidateNodesAt25(
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, ResolvedFormat> localFormats,
        WorkflowFormatCatalog? catalog,
        List<string> errors)
    {
        var shapes = new Dictionary<string, TemplateNodeShape>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var prompts = (manifest.Prompts ?? new List<WorkflowPromptSpec>()).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var unresolvedCatalogueIds = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            if (!ids.Add(node.Id))
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

            if (node.Aggregate is { Count: > 0 })
            {
                foreach (var (name, present) in new[] { ("format", node.Format != null), ("prompt", node.Prompt != null), ("with", node.With != null), ("onInvalid", node.OnInvalid != null) })
                {
                    if (present)
                    {
                        errors.Add($"Node '{node.Id}' aggregates and must not declare {name}; an aggregator is structural.");
                    }
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(node.Format))
            {
                errors.Add($"Node '{node.Id}' must declare a format at specVersion 25.");
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
                continue;
            }
            else
            {
                errors.Add($"Node '{node.Id}' declares format '{node.Format}', which is neither a package format nor a catalogue format.");
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

            shapes[node.Id] = new TemplateNodeShape(format.Id, spec.Type, spec.Schema);
        }

        if (unresolvedCatalogueIds.Count > 0)
        {
            errors.Add($"The format catalogue is not available on this engine; catalogue formats ({string.Join(", ", unresolvedCatalogueIds)}) cannot be resolved.");
        }

        return shapes;
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
}
