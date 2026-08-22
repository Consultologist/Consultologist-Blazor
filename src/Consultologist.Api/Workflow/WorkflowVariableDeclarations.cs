namespace Consultologist.Api.Workflow;

/// <summary>
/// Prompt variable → the declared input behind it, for every variable bound
/// to a converted type — date, boolean, number, object, array. Text and enum
/// are strings at runtime and need nothing.
///
/// One rule, two callers (#425): the validator's publish-time probe, which
/// hands Scriban the type the renderer will, and the activity, which has the
/// package in hand and lets an object's fields render as their own types.
/// Built manifest-wide rather than per node on purpose: a prompt may be
/// shared from v6 on, and each using node binds every variable itself with
/// no rule forcing two nodes to agree. So a variable is typed only when every
/// binding that reaches it agrees; anything else stays the string it has
/// always been, and a template that formats it as a date fails — correctly,
/// because it would be wrong for the node passing a string.
///
/// Runs before the declaration is validated, so nothing here may assume the
/// manifest is well formed: an unknown type, a duplicate id or a binding
/// naming no declared input all fall back to a string rather than throwing.
/// </summary>
public static class WorkflowVariableDeclarations
{
    public static IReadOnlyDictionary<string, WorkflowInputSpec> For(WorkflowPackageManifest manifest)
    {
        var declared = new Dictionary<string, WorkflowInputSpec>(StringComparer.Ordinal);

        foreach (var input in manifest.Inputs ?? new List<WorkflowInputSpec>())
        {
            var type = WorkflowInputTypes.Of(input);

            if (type is WorkflowInputTypes.Date or WorkflowInputTypes.Boolean
                or WorkflowInputTypes.Number or WorkflowInputTypes.Object or WorkflowInputTypes.Array)
            {
                declared[input.Id] = input;
            }
        }

        if (declared.Count == 0)
        {
            return declared;
        }

        var typed = new Dictionary<string, WorkflowInputSpec>(StringComparer.Ordinal);
        var conflicted = new HashSet<string>(StringComparer.Ordinal);

        // v9 (#426): an input fan's element reaches its node as item:value, a
        // string in the item map. What it IS — a date, a number, an object
        // with fields — is the array's declaration, one level down. Synthesised
        // once per fanned input so every node fanning it resolves to the same
        // instance, which the agreement rule below compares by reference.
        var elements = new Dictionary<string, WorkflowInputSpec>(StringComparer.Ordinal);

        foreach (var node in manifest.Nodes ?? new List<WorkflowNodeSpec>())
        {
            foreach (var (variable, binding) in node.Bindings ?? new Dictionary<string, WorkflowBindingValue>())
            {
                var declaration = binding.From.StartsWith(WorkflowNodeBindingSources.InputPrefix, StringComparison.Ordinal)
                    ? declared.GetValueOrDefault(binding.From[WorkflowNodeBindingSources.InputPrefix.Length..])
                    : binding.From == "item:value" && WorkflowInputFans.IsInputFan(node.ForEach)
                        ? ElementOf(node.ForEach!, elements, manifest)
                        : null;

                if (typed.TryGetValue(variable, out var seen) && !ReferenceEquals(seen, declaration))
                {
                    conflicted.Add(variable);
                }
                else if (declaration != null)
                {
                    typed[variable] = declaration;
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
    /// The declaration of one element of a fanned array: the array's items
    /// type and values for a scalar, its fields for an object. Null for an
    /// array of text — a string needs no type — and for anything malformed,
    /// which the validator reports on its own.
    /// </summary>
    private static WorkflowInputSpec? ElementOf(
        string forEach,
        Dictionary<string, WorkflowInputSpec> elements,
        WorkflowPackageManifest manifest)
    {
        if (elements.TryGetValue(forEach, out var known))
        {
            return known;
        }

        var id = WorkflowInputFans.InputIdOf(forEach);
        var array = (manifest.Inputs ?? new List<WorkflowInputSpec>())
            .FirstOrDefault(input => input.Id == id && WorkflowInputTypes.Of(input) == WorkflowInputTypes.Array);

        if (array is null || array.Items is null or WorkflowInputTypes.Text)
        {
            return null;
        }

        var element = array.Items == WorkflowInputTypes.Object
            ? new WorkflowInputSpec(id, array.Label, Type: WorkflowInputTypes.Object, Fields: array.Fields)
            : new WorkflowInputSpec(id, array.Label, Type: array.Items, Values: array.Values);

        elements[forEach] = element;
        return element;
    }
}
