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
}
