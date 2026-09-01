using System.Text.Json;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Services.AI;

/// <summary>
/// Mirrors the Api's Consultologist.PackageFormat.FormResponseCoercion by
/// hand (#540, spike § 4.2) — the client fills the setup form for review,
/// the server re-runs the same table at start to verify the origin, so the
/// two must agree answer for answer. FormResponseCoercionMirrorTests holds
/// them identical over the shared wire table.
/// </summary>
public static class FormResponseCoercion
{
    /// <summary>
    /// Null value with null misfit means not supplied (the flow's "" for an
    /// unanswered question, or an empty JSON array). A misfit is the reason
    /// phrase; the caller prefixes the input it names.
    /// </summary>
    public static (ConsultInputValue? Value, string? Misfit) Coerce(string? type, IReadOnlyList<string>? values, string? itemsType, string held)
    {
        if (string.IsNullOrWhiteSpace(held))
        {
            return (null, null);
        }

        switch (type ?? WorkflowInputTypes.Text)
        {
            case WorkflowInputTypes.Text:
            case WorkflowInputTypes.Date:
                return (ConsultInputValue.OfText(held), null);

            case WorkflowInputTypes.Enum:
                return values is { Count: > 0 } && values.Contains(held, StringComparer.Ordinal)
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
                if (itemsType != null && itemsType != WorkflowInputTypes.Text)
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

            default:
                return (null, "cannot be filled from a form answer");
        }
    }

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
