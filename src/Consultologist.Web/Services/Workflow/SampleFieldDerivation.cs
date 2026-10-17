using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Consultologist.Web.Services.Workflow;

/// <summary>
/// #991 (B): an object input's fields derived from a pasted sample response —
/// the inverse of FormResponseCoercion's table, so what the helper declares is
/// what a response of that shape fills. Merge rule: add what the owner lacks,
/// never retype what it has. One sample proves no vocabulary and no
/// required-ness, so a derived field is optional and never an enum. Refusals
/// name keys (the author's own field ids), never values; one refusal derives
/// nothing.
/// </summary>
public static class SampleFieldDerivation
{
    public sealed record Outcome(
        IReadOnlyList<WorkflowManifestReader.FieldView> Fields,
        IReadOnlyList<string> Added,
        IReadOnlyList<string> Refusals);

    private static readonly Regex SnakeCase = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    /// <param name="allowStructure">False below specVersion 10, where a field holds a scalar (the validator's rule).</param>
    public static Outcome DeriveFields(JsonElement sample, IReadOnlyList<WorkflowManifestReader.FieldView> existing, bool allowStructure = true)
    {
        var refusals = new List<string>();

        if (sample.ValueKind != JsonValueKind.Object)
        {
            refusals.Add("The sample must be a JSON object.");
            return new Outcome(existing.ToList(), Array.Empty<string>(), refusals);
        }

        var derived = Derive(sample, string.Empty, allowStructure, refusals);
        if (refusals.Count > 0)
        {
            return new Outcome(existing.ToList(), Array.Empty<string>(), refusals);
        }

        var known = existing.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
        var added = derived.Where(field => !known.Contains(field.Id)).ToList();
        return new Outcome(existing.Concat(added).ToList(), added.Select(field => field.Id).ToList(), refusals);
    }

    private static List<WorkflowManifestReader.FieldView> Derive(JsonElement sample, string prefix, bool allowStructure, List<string> refusals)
    {
        var fields = new List<WorkflowManifestReader.FieldView>();
        foreach (var member in sample.EnumerateObject())
        {
            var path = prefix.Length == 0 ? member.Name : $"{prefix}.{member.Name}";
            if (!SnakeCase.IsMatch(member.Name))
            {
                refusals.Add($"Key '{path}' is not snake_case (a lowercase letter, then lowercase letters, digits, or underscores); rename it in the sample.");
                continue;
            }

            var field = Infer(member.Name, member.Value, path, allowStructure, refusals);
            if (field is not null)
            {
                fields.Add(field);
            }
        }

        return fields;
    }

    private static WorkflowManifestReader.FieldView? Infer(string id, JsonElement value, string path, bool allowStructure, List<string> refusals)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return new WorkflowManifestReader.FieldView(id, id, false, IsIsoDate(value.GetString()!) ? WorkflowInputTypes.Date : null);

            case JsonValueKind.Number:
                return new WorkflowManifestReader.FieldView(id, id, false, WorkflowInputTypes.Number);

            case JsonValueKind.True:
            case JsonValueKind.False:
                return new WorkflowManifestReader.FieldView(id, id, false, WorkflowInputTypes.Boolean);

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                refusals.Add($"Key '{path}' is null; give it a sample value.");
                return null;

            case JsonValueKind.Object:
                if (!allowStructure)
                {
                    refusals.Add($"Key '{path}' is structure (an object or an array); fields below specVersion 10 are scalars.");
                    return null;
                }

                if (!value.EnumerateObject().Any())
                {
                    refusals.Add($"Key '{path}' is an empty object; give it one sample member.");
                    return null;
                }

                var before = refusals.Count;
                var nested = Derive(value, path, allowStructure, refusals);
                return refusals.Count > before ? null : new WorkflowManifestReader.FieldView(id, id, false, WorkflowInputTypes.Object, Fields: nested);

            case JsonValueKind.Array:
                if (!allowStructure)
                {
                    refusals.Add($"Key '{path}' is structure (an object or an array); fields below specVersion 10 are scalars.");
                    return null;
                }

                return InferArray(id, value, path, refusals);

            default:
                refusals.Add($"Key '{path}' has a value no field can hold.");
                return null;
        }
    }

    /// <summary>
    /// The element is read from the first sample element. An array of objects
    /// takes the v9 form the editor writes: items "object", the fields ON the
    /// field; an array of arrays nests through the element.
    /// </summary>
    private static WorkflowManifestReader.FieldView? InferArray(string id, JsonElement array, string path, List<string> refusals)
    {
        if (array.GetArrayLength() == 0)
        {
            refusals.Add($"Key '{path}' is an empty array; give it one sample element.");
            return null;
        }

        var element = ElementOf(array[0], path, refusals, out var elementFields);
        if (element is null)
        {
            return null;
        }

        return new WorkflowManifestReader.FieldView(id, id, false, WorkflowInputTypes.Array, Items: element, Fields: elementFields);
    }

    private static WorkflowManifestReader.ElementView? ElementOf(JsonElement first, string path, List<string> refusals, out IReadOnlyList<WorkflowManifestReader.FieldView>? fieldsOnTheOwner)
    {
        fieldsOnTheOwner = null;
        switch (first.ValueKind)
        {
            case JsonValueKind.String:
                return new WorkflowManifestReader.ElementView(IsIsoDate(first.GetString()!) ? WorkflowInputTypes.Date : WorkflowInputTypes.Text);
            case JsonValueKind.Number:
                return new WorkflowManifestReader.ElementView(WorkflowInputTypes.Number);
            case JsonValueKind.True:
            case JsonValueKind.False:
                return new WorkflowManifestReader.ElementView(WorkflowInputTypes.Boolean);
            case JsonValueKind.Object:
                if (!first.EnumerateObject().Any())
                {
                    refusals.Add($"Key '{path}' has an empty object as its first element; give it one sample member.");
                    return null;
                }

                var before = refusals.Count;
                var nested = Derive(first, $"{path}[]", true, refusals);
                if (refusals.Count > before)
                {
                    return null;
                }

                fieldsOnTheOwner = nested;
                return new WorkflowManifestReader.ElementView(WorkflowInputTypes.Object);
            case JsonValueKind.Array:
                if (first.GetArrayLength() == 0)
                {
                    refusals.Add($"Key '{path}' has an empty array as its first element; give it one sample element.");
                    return null;
                }

                var inner = ElementOf(first[0], $"{path}[]", refusals, out var innerFields);
                return inner is null ? null : new WorkflowManifestReader.ElementView(WorkflowInputTypes.Array, Items: inner, Fields: innerFields);
            default:
                refusals.Add($"Key '{path}' has a null first element; give it a sample value.");
                return null;
        }
    }

    private static bool IsIsoDate(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
