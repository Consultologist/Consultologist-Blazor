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
}
