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
/// table in FormResponseCoercionMirrorTests holds the two identical.
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
