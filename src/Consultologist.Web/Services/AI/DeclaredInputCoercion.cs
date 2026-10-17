using System.Text.Json;
using Consultologist.Web.Services.Workflow;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Services.AI;

/// <summary>
/// #991: the client's fill is the LIBRARY's coercion, not a hand mirror —
/// Consultologist.PackageFormat.FormResponseCoercion.CoerceDeclared over a
/// WorkflowInputSpec built from the setup form's declaration view, the result
/// crossing into the Web's ConsultInputValue through its wire JSON. The
/// server's start-time verification makes the same call over the same
/// declaration, so the two cannot disagree (the gap design § 13 named as the
/// follow-up under #991: the hand copy knew the v13 string table only, so a
/// held response could not fill an object input in the browser).
/// </summary>
public static class DeclaredInputCoercion
{
    /// <summary>
    /// A held JSON value against a declared input. Null value with null misfit
    /// means "not supplied"; a misfit is the reason phrase, which the caller
    /// prefixes with the input it names.
    /// </summary>
    public static (ConsultInputValue? Value, string? Misfit) Coerce(WorkflowPackageInputResponse input, JsonElement held)
    {
        var (value, misfit) = PF.FormResponseCoercion.CoerceDeclared(SpecOf(input), held);
        return (value is null ? null : ConsultInputValue.FromJson(value.AsJson()), misfit);
    }

    /// <summary>
    /// A held STRING answer — the picker's wire (#540 § 4.2), every value a
    /// string. An OBJECT input whose string is a JSON object is read as the
    /// object it flattened from (the forms endpoint writes a version-2 object
    /// value as its raw JSON), which the server verifies as an object; every
    /// other type goes through the v13 string table unchanged.
    /// </summary>
    public static (ConsultInputValue? Value, string? Misfit) CoerceHeldText(WorkflowPackageInputResponse input, string held)
    {
        if (WorkflowInputTypes.Of(input) == WorkflowInputTypes.Object
            && input.Types is not { Count: > 1 }
            && TryParseObject(held, out var structure))
        {
            return Coerce(input, structure);
        }

        return Coerce(input, JsonSerializer.SerializeToElement(held));
    }

    /// <summary>
    /// #991: a questionnaire file's bytes — UTF-8 JSON, a bare object keyed by
    /// the input's declared field ids. A BOM is tolerated for parsing (the
    /// server hashes the raw bytes). The misfit phrases read after "That
    /// questionnaire file …" here and after "The questionnaire file for input
    /// '…' …" on the server: the same words.
    /// </summary>
    public static (ConsultInputValue? Value, string? Misfit) CoerceFile(WorkflowPackageInputResponse input, byte[] bytes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(StripUtf8Bom(bytes), new JsonDocumentOptions { MaxDepth = ConsultInputValueConverter.MaxDepth + 1 });
        }
        catch (JsonException)
        {
            return (null, "is not JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, $"must be a JSON object; got {KindWord(root.ValueKind)}");
            }

            return Coerce(input, root.Clone());
        }
    }

    /// <summary>The declaration as the library reads it — the setup form's view, member for member.</summary>
    internal static PF.WorkflowInputSpec SpecOf(WorkflowPackageInputResponse input) => new(
        input.Id,
        input.Label,
        input.Required,
        Type: input.Types is { Count: > 1 } types ? new PF.WorkflowTypeSet(types.ToList()) : (PF.WorkflowTypeSet?)input.Type,
        Values: input.Values?.ToList(),
        Items: ElementOf(input.Items),
        Fields: FieldsOf(input.Fields),
        ExpectedContent: input.ExpectedContent);

    private static PF.WorkflowFieldSpec FieldOf(WorkflowPackageFieldResponse field) =>
        new(field.Id, field.Label, field.Required, field.Type, field.Values?.ToList(), ElementOf(field.Items), FieldsOf(field.Fields));

    private static PF.WorkflowElementSpec? ElementOf(WorkflowPackageElementResponse? element) =>
        element is null ? null : new(element.Type, ElementOf(element.Items), FieldsOf(element.Fields), element.Values?.ToList());

    private static List<PF.WorkflowFieldSpec>? FieldsOf(IReadOnlyList<WorkflowPackageFieldResponse>? fields) =>
        fields?.Select(FieldOf).ToList();

    private static bool TryParseObject(string held, out JsonElement element)
    {
        element = default;
        if (!held.TrimStart().StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(held);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ReadOnlyMemory<byte> StripUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? bytes.AsMemory(3) : bytes;

    private static string KindWord(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Array => "a JSON array",
        JsonValueKind.String => "a JSON string",
        JsonValueKind.Number => "a JSON number",
        JsonValueKind.True or JsonValueKind.False => "a JSON boolean",
        _ => "JSON null",
    };
}
