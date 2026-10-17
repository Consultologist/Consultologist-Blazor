using System.Text.Json;

namespace Consultologist.PackageFormat;

/// <summary>
/// #540 (forms-intake-spike.md § 4.2): a held form answer — always a string,
/// the flow sends nothing else — coerced by the input's declaration into the
/// typed value the setup form would have produced, or refused by name
/// ("named, not filled"). E2's lesson is the enum rule: an *Other* answer
/// arrives as free text indistinguishable from a declared option, so
/// membership (ordinal — the starter's own comparison) is the only guarantee.
/// The Web hand-mirrors this table (the client fills the form for review;
/// the server re-runs it at start to verify the origin); the shared wire
/// table in FormResponseCoercionMirrorTests holds the two identical. v25
/// (B6 #1005) adds the JSON overloads below for a payload v2 answer; the
/// string table is theirs for a string value and does not move.
/// </summary>
public static class FormResponseCoercion
{
    /// <summary>
    /// Null value with null misfit means not supplied — the flow's "" for an
    /// unanswered question (and an empty JSON array likewise). A misfit is
    /// the reason phrase; the caller prefixes the input it names.
    /// </summary>
    public static (ConsultInputValue? Value, string? Misfit) Coerce(WorkflowDeclarationNode declaration, string held)
    {
        if (string.IsNullOrWhiteSpace(held))
        {
            return (null, null);
        }

        switch (declaration.Type)
        {
            case WorkflowInputTypes.Text:
            case WorkflowInputTypes.Date:
                // As the string; a malformed date is the same start-time
                // refusal a typed one gets — the declaration check owns it.
                return (ConsultInputValue.OfText(held), null);

            case WorkflowInputTypes.Enum:
                return declaration.Values is { Count: > 0 } values && values.Contains(held, StringComparer.Ordinal)
                    ? (ConsultInputValue.OfText(held), null)
                    : (null, "is not one of the declared values");

            case WorkflowInputTypes.Boolean:
                return held.Trim().ToLowerInvariant() switch
                {
                    "yes" or "true" => (ConsultInputValue.OfBoolean(true), null),
                    "no" or "false" => (ConsultInputValue.OfBoolean(false), null),
                    _ => (null, "is not Yes, No, true or false"),
                };

            case WorkflowInputTypes.Number:
                return ConsultInputValue.TryParseNumber(held.Trim(), out var number)
                    ? (number, null)
                    : (null, "is not a plain decimal number");

            case WorkflowInputTypes.Array:
                return CoerceArray(declaration, held);

            default:
                // object (and anything the table does not know) is absent
                // from § 4.2 by design.
                return (null, "cannot be filled from a form answer");
        }
    }

    /// <summary>
    /// v25 (package-format-v25-design.md § 9, B6 #1005): a held answer that is
    /// a JSON value — the payload v2 shape. A string goes through the table
    /// above, unchanged (one table, one Web mirror); a number, a boolean, an
    /// array and an object fill the declaration they fit, shape for shape: an
    /// array's elements against its items, an object's members against its
    /// declared fields — an undeclared member, a null member and a missing
    /// required field are named. Null or absent is not supplied.
    /// </summary>
    public static (ConsultInputValue? Value, string? Misfit) Coerce(WorkflowDeclarationNode declaration, JsonElement held)
    {
        switch (held.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                return (null, null);

            case JsonValueKind.String:
                return Coerce(declaration, held.GetString()!);

            case JsonValueKind.Number:
                return declaration.Type switch
                {
                    WorkflowInputTypes.Number => ConsultInputValue.TryParseNumber(held.GetRawText(), out var number)
                        ? (number, null)
                        : (null, "is not a plain decimal number"),
                    WorkflowInputTypes.Text or WorkflowInputTypes.Date => (ConsultInputValue.OfText(held.GetRawText()), null),
                    WorkflowInputTypes.Enum => Coerce(declaration, held.GetRawText()),
                    WorkflowInputTypes.Boolean => (null, "is not Yes, No, true or false"),
                    _ => (null, $"is a JSON number; the input is {An(declaration.Type)}"),
                };

            case JsonValueKind.True:
            case JsonValueKind.False:
                var flag = held.ValueKind == JsonValueKind.True;
                return declaration.Type switch
                {
                    WorkflowInputTypes.Boolean => (ConsultInputValue.OfBoolean(flag), null),
                    WorkflowInputTypes.Text => (ConsultInputValue.OfText(flag ? "true" : "false"), null),
                    WorkflowInputTypes.Enum => Coerce(declaration, flag ? "true" : "false"),
                    WorkflowInputTypes.Number => (null, "is not a plain decimal number"),
                    _ => (null, $"is a JSON boolean; the input is {An(declaration.Type)}"),
                };

            case JsonValueKind.Array:
                return declaration.Type == WorkflowInputTypes.Array
                    ? CoerceElements(declaration, held)
                    : (null, $"is a JSON array; the input is {An(declaration.Type)}");

            case JsonValueKind.Object:
                return declaration.Type == WorkflowInputTypes.Object
                    ? CoerceObject(declaration, held)
                    : (null, $"is a JSON object; the input is {An(declaration.Type)}");

            default:
                return (null, "cannot be filled from a form answer");
        }
    }

    /// <summary>
    /// A held JSON answer against a declared input, union-aware (#729): a single
    /// type defers to <see cref="Coerce(WorkflowDeclarationNode, JsonElement)"/>;
    /// a union tries each arm in declared order and takes the first that
    /// coerces. The one call both the run-at-once door and the starter's
    /// verification make, so the two cannot disagree.
    /// </summary>
    public static (ConsultInputValue? Value, string? Misfit) CoerceDeclared(WorkflowInputSpec declaration, JsonElement held)
    {
        var node = WorkflowDeclarationNode.Of(declaration);

        if (declaration.Type is not { IsUnion: true })
        {
            return Coerce(node, held);
        }

        string? lastMisfit = null;
        foreach (var arm in WorkflowInputTypes.TypesOf(declaration))
        {
            var (value, misfit) = Coerce(node with { Type = arm }, held);
            if (misfit == null)
            {
                return (value, null); // a match, or the blank (null, null) case
            }

            lastMisfit = misfit;
        }

        return (null, lastMisfit);
    }

    /// <summary>An array's elements, each against the items; an empty array is nothing supplied.</summary>
    private static (ConsultInputValue? Value, string? Misfit) CoerceElements(WorkflowDeclarationNode declaration, JsonElement held)
    {
        var items = declaration.Items ?? new WorkflowDeclarationNode(WorkflowInputTypes.Text, declaration.Label, false, null, null, null);
        var elements = new List<ConsultInputValue>();
        var index = 0;

        foreach (var element in held.EnumerateArray())
        {
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return (null, $"element {index} is null");
            }

            var (value, misfit) = Coerce(items, element);
            if (misfit != null)
            {
                return (null, $"element {index} {misfit}");
            }

            if (value != null)
            {
                elements.Add(value);
            }

            index++;
        }

        return elements.Count == 0 ? (null, null) : (ConsultInputValue.OfArray(elements), null);
    }

    /// <summary>
    /// The object fill of design § 9: the response's members fill the declared
    /// fields, each coerced to its field type; a member the input does not
    /// declare, a null member and a missing required field are named; a blank
    /// optional field is omitted. Fields land in declared order.
    /// </summary>
    private static (ConsultInputValue? Value, string? Misfit) CoerceObject(WorkflowDeclarationNode declaration, JsonElement held)
    {
        var fields = declaration.Fields ?? Array.Empty<WorkflowDeclarationNode>();
        var declared = fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
        var filled = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal);

        foreach (var member in held.EnumerateObject())
        {
            if (!declared.TryGetValue(member.Name, out var field))
            {
                return (null, $"has a field '{member.Name}' it does not declare (fields: {string.Join(", ", fields.Select(f => f.Id))})");
            }

            if (member.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return (null, $"field '{member.Name}' is null; omit an optional field instead");
            }

            var (value, misfit) = Coerce(field, member.Value);
            if (misfit != null)
            {
                return (null, $"field '{member.Name}' {misfit}");
            }

            if (value != null)
            {
                filled[member.Name] = value;
            }
        }

        var missing = fields.FirstOrDefault(field => field.Required && !filled.ContainsKey(field.Id));
        if (missing != null)
        {
            return (null, $"is missing required field '{missing.Id}'");
        }

        return filled.Count == 0
            ? (null, null)
            : (ConsultInputValue.OfObject(fields.Where(f => filled.ContainsKey(f.Id)).Select(f => new ConsultInputEntry(f.Id, filled[f.Id]))), null);
    }

    private static string An(string type) => type switch
    {
        WorkflowInputTypes.Array => "an array",
        WorkflowInputTypes.Object => "an object",
        WorkflowInputTypes.Enum => "an enum",
        _ => $"a {type}",
    };

    /// <summary>
    /// An array of text only: a JSON-array string (E2's multiple-choice wire
    /// form, every element a string) or a single string as one element.
    /// </summary>
    private static (ConsultInputValue? Value, string? Misfit) CoerceArray(WorkflowDeclarationNode declaration, string held)
    {
        if (declaration.Items is { } items && items.Type != WorkflowInputTypes.Text)
        {
            return (null, "cannot be filled from a form answer");
        }

        var elements = TryParseJsonStringArray(held);
        if (elements == null)
        {
            return (ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText(held) }), null);
        }

        return elements.Count == 0
            ? (null, null)
            : (ConsultInputValue.OfArray(elements.Select(ConsultInputValue.OfText)), null);
    }

    /// <summary>
    /// The elements when the answer is a JSON array of strings; null when it
    /// is not JSON, not an array, or carries a non-string element — those
    /// fall back to the whole answer as one element.
    /// </summary>
    private static IReadOnlyList<string>? TryParseJsonStringArray(string held)
    {
        var trimmed = held.TrimStart();
        if (!trimmed.StartsWith('['))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(held);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var elements = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                elements.Add(element.GetString()!);
            }

            return elements;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
