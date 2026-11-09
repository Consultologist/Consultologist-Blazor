namespace Consultologist.PackageFormat;

/// <summary>
/// #1339 (package-format-v30-design.md § 2): what 30 adds to the 29 path — a
/// loop inside a loop, to any depth (lifted in <see cref="WorkflowFlowNodes.Resolve"/>),
/// whose source may be the outer item's list field or a node of an enclosing
/// body. Run by <see cref="ValidateAt27"/> with <c>at30</c>; below 30 the 26–29
/// sentences stand unchanged (the published vectors pin them until the bump
/// re-exports them).
/// </summary>
public static partial class WorkflowPackageValidator
{
    /// <summary>
    /// § 2.2: an inner loop over <c>item.value.&lt;field…&gt;</c> iterates a list
    /// field of the enclosing loop's item — typed by that loop's element, so the
    /// field must be declared on it and typed as an array.
    /// </summary>
    private static void ValidateOuterItemLoopsAt30(
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, TemplateNodeShape> nodeShapes,
        List<string> errors)
    {
        foreach (var flow in flows.Flows.Values.Where(f => f.Fan is OuterItemFieldFan).OrderBy(f => f.Node.Id, StringComparer.Ordinal))
        {
            var fan = (OuterItemFieldFan)flow.Fan!;
            var subject = $"Loop '{flow.Node.Id}' iterates {fan.Reference}";

            if (fan.Outer is null)
            {
                // No enclosing loop (said by the resolver), or one whose own source failed.
                continue;
            }

            var at = fan.Resolve(nodeShapes, out var problem);

            if (at is null)
            {
                if (problem is not null && !problem.EndsWith("is not typed", StringComparison.Ordinal))
                {
                    errors.Add($"{subject}; {problem}.");
                }
            }
            else if (at.Kind != WorkflowInputTypes.Array)
            {
                errors.Add($"{subject}, which the outer item types as {at.Describe()}, not an array.");
            }
        }
    }

    /// <summary>
    /// § 3, § 8: what only 30 spells, refused below it by name — a while node,
    /// its bound, and a for's until. (Below 30 the flow resolver does not read
    /// these keys, so this is the one sentence each.)
    /// </summary>
    private static void ValidateV30Members(WorkflowPackageManifest manifest, List<string> errors)
    {
        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            foreach (var (name, present) in new[]
            {
                ("while", node.While != null), ("max", node.Max != null), ("until", node.Until != null), ("checkFirst", node.CheckFirst != null),
                ("let", node.Let != null), ("type", node.Type != null), ("labelField", node.LabelField != null),
            })
            {
                if (present)
                {
                    errors.Add($"Node '{node.Id}' declares {name}, which requires specVersion 30.");
                }
            }
        }

        foreach (var input in manifest.Inputs ?? new List<WorkflowInputSpec>())
        {
            if (input.LabelField != null)
            {
                errors.Add($"Input '{input.Id}' declares labelField, which requires specVersion 30.");
            }
        }
    }

    /// <summary>
    /// § 4.1: a let — its expression is walked with the templates
    /// (<see cref="WorkflowLetAt30.Walk"/>); here its shape. It carries no format,
    /// prompt, with, failIfEmpty, reproducible, macros or aggregate; its type is
    /// text, number, boolean or an object format; its onInvalid is fail or flag
    /// (required, no default; retry needs a model). The resolved format is the
    /// type's — synthesized for a scalar — so the shapes and probes read it as any
    /// node's.
    /// </summary>
    private static ResolvedFormat? ResolveLetAt30(
        WorkflowNodeSpec node,
        WorkflowPackageManifest manifest,
        IReadOnlyDictionary<string, ResolvedFormat> localFormats,
        WorkflowFormatCatalog? catalog,
        List<string> errors)
    {
        var subject = $"Let '{node.Id}'";

        foreach (var (name, present) in new[]
        {
            ("format", node.Format != null), ("prompt", node.Prompt != null), ("with", node.With != null), ("failIfEmpty", node.FailIfEmpty != null),
            ("reproducible", node.Reproducible != null), ("macros", node.Macros != null), ("aggregate", node.Aggregate != null), ("forEach", node.ForEach != null),
        })
        {
            if (present)
            {
                errors.Add($"{subject} declares {name}, which a let does not carry; a let computes its value from its expression and names it with type.");
            }
        }

        if (string.IsNullOrWhiteSpace(node.OnInvalid))
        {
            errors.Add($"{subject} must declare onInvalid (fail | flag); its value is checked against its type.");
        }
        else if (node.OnInvalid == WorkflowOnInvalid.Retry)
        {
            errors.Add($"{subject} declares onInvalid retry; retry needs a model — use fail or flag.");
        }
        else if (!WorkflowOnInvalid.IsKnown(node.OnInvalid))
        {
            errors.Add($"{subject} onInvalid '{node.OnInvalid}' is not one of fail, flag.");
        }

        if (string.IsNullOrWhiteSpace(node.Type))
        {
            errors.Add($"{subject} declares no type; a let names its value's type — text, number, boolean or an object format.");
            return null;
        }

        if (WorkflowLetAt30.ScalarSpecOf(node.Type) is { } scalar)
        {
            return new ResolvedFormat(node.Type, scalar, WorkflowFormatOrigin.PackageLocal);
        }

        ResolvedFormat? format = localFormats.TryGetValue(node.Type, out var local) ? local
            : catalog is not null && catalog.TryGet(node.Type, out var entry) ? new ResolvedFormat(node.Type, entry!.Spec, WorkflowFormatOrigin.Catalogue)
            : null;

        if (format is null)
        {
            errors.Add($"{subject} type '{node.Type}' is neither text, number, boolean nor a package or catalogue format.");
            return null;
        }

        if (format.Spec.Type != WorkflowInputTypes.Object)
        {
            errors.Add($"{subject} type '{node.Type}' is a{(format.Spec.Type is WorkflowInputTypes.Enum ? "n" : string.Empty)} {format.Spec.Type} format; a let's format types an object — or write type text, number or boolean.");
            return null;
        }

        return format;
    }

    /// <summary>
    /// § 5: an array of objects names each item by one of its fields — a required
    /// text field of the element (a blank value falls back to the label and the
    /// position, at the run).
    /// </summary>
    private static void ValidateInputLabelFieldAt30(WorkflowInputSpec input, List<string> errors)
    {
        var subject = $"Input '{input.Id}'";
        var element = WorkflowInputTypes.Of(input) == WorkflowInputTypes.Array ? WorkflowDeclarationNode.Of(input).Items : null;

        if (element is not { Type: WorkflowInputTypes.Object })
        {
            errors.Add($"{subject} declares labelField, but it is not an array of objects; labelField names a field of each item.");
            return;
        }

        LabelFieldSentence($"{subject} labelField '{input.LabelField}'", input.LabelField!, TemplateValueShape.FromDeclaration(element), "of its items", errors);
    }

    /// <summary>
    /// § 5: a loop names each item by a field of its element — over a node's value
    /// or an outer item's field, a required text field the element's format (or
    /// declaration) types. A loop over an input takes the input's labelField; a
    /// folder's items are named by their label header.
    /// </summary>
    private static void ValidateLoopLabelFieldsAt30(
        WorkflowFlowNodes.Resolved flows,
        IReadOnlyDictionary<string, TemplateNodeShape> nodeShapes,
        List<string> errors)
    {
        foreach (var flow in flows.Flows.Values.Where(f => f.Kind == WorkflowFlowNodes.Kind.For && f.Node.LabelField is not null).OrderBy(f => f.Node.Id, StringComparer.Ordinal))
        {
            var subject = $"Loop '{flow.Node.Id}'";

            switch (flow.Fan)
            {
                case InputArrayFan:
                    errors.Add($"{subject} declares labelField, but it iterates {flow.Collection} — the input names its items ({flow.Collection}'s labelField).");
                    continue;

                case SnippetFolderFan:
                    errors.Add($"{subject} declares labelField, but it iterates {flow.Collection} — a folder's items are named by their label header.");
                    continue;

                case NodeValueFan or OuterItemFieldFan:
                    break;

                default:
                    // The source did not resolve; its own sentence says why.
                    continue;
            }

            var reference = flow.Fan switch { NodeValueFan n => n.Reference, OuterItemFieldFan o => o.Reference, _ => string.Empty };

            if (TemplateFan.ElementOf(flow.Fan, nodeShapes) is not { } element || element.IsUnknown)
            {
                continue;
            }

            if (!element.IsObject)
            {
                errors.Add($"{subject} labelField '{flow.Node.LabelField}': each element of {reference} is {element.Describe()}, not an object; labelField names a field of each item.");
                continue;
            }

            LabelFieldSentence($"{subject} labelField '{flow.Node.LabelField}'", flow.Node.LabelField!, element, $"of the elements of {reference}", errors);
        }
    }

    private static void LabelFieldSentence(string subject, string field, TemplateValueShape element, string of, List<string> errors)
    {
        if (element.Fields is not { } fields || !fields.TryGetValue(field, out var shape))
        {
            errors.Add($"{subject} is not a field {of} (fields: {string.Join(", ", (element.Fields?.Keys ?? Enumerable.Empty<string>()).Order(StringComparer.Ordinal))}).");
            return;
        }

        if (shape.Kind != WorkflowInputTypes.Text)
        {
            errors.Add($"{subject} names {(shape.Kind is WorkflowInputTypes.Enum or WorkflowInputTypes.Object or WorkflowInputTypes.Array ? "an" : "a")} {shape.Kind} field; labelField names a required text field.");
            return;
        }

        if (element.Required is { } required && !required.Contains(field))
        {
            errors.Add($"{subject} is optional; labelField names a required text field.");
        }
    }
}
