using System.Text.Json.Nodes;
using Corvus.Text.Json;
using Corvus.Text.Json.Validator;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): one place a value does not follow its format (part 02 § 3.4).
/// <c>Pointer</c> is a JSON pointer into the value (<c>/concepts/0/term</c>, <c>""</c> for the
/// root; for <c>required</c> and <c>additionalProperties</c> the object that is missing or has
/// too many members) and <c>Keyword</c> the schema keyword that failed. Neither carries any part
/// of the value: a pointer names only the format's own properties and array positions.
/// </summary>
public sealed record FormatValueFault(string Pointer, string Keyword);

/// <summary>The outcome of <see cref="IFormatValueValidator.Validate"/>: an invalid value always has at least one fault, sorted by pointer then keyword, without repeats.</summary>
public sealed record FormatValueResult(bool Valid, IReadOnlyList<FormatValueFault> Faults);

/// <summary>
/// #1500 (specVersion 31, area 03; D1): is a value one the format describes. The engine, the
/// model-reply checks and test stubs go through this interface; Corvus is behind it. A
/// <c>JsonNode?</c> that is C# null is the JSON value <c>null</c>. Never throws on a value or a format.
/// </summary>
public interface IFormatValueValidator
{
    /// <summary>Validates <paramref name="value"/> against the full form of <paramref name="format"/> (a name or an inline schema) in <paramref name="formats"/>.</summary>
    FormatValueResult Validate(JsonNode? value, JsonNode? format, FormatSet formats);
}

/// <summary>
/// #1500 (specVersion 31, area 03; D1): <see cref="IFormatValueValidator"/> on
/// <c>Corvus.Text.Json.Validator</c>, 2020-12, <c>format</c> asserted, no file or HTTP
/// resolution. The schema is the format's full form (references inlined, as the model's reply is
/// held to what the author wrote, not to the sent copy), compiled once per process under
/// <c>urn:consultologist:validate:&lt;hash of the full form&gt;</c>, so two different formats never
/// share a compiled schema. A format that cannot be compiled (unknown name, cycle, too large)
/// gives one fault <c>("", "$ref")</c>; a value that cannot be read, <c>("", "type")</c>.
/// </summary>
public sealed class CorvusFormatValueValidator : IFormatValueValidator
{
    private static readonly FormatValueFault[] BadFormat = [new("", "$ref")];
    private static readonly FormatValueFault[] BadValue = [new("", "type")];

    private static readonly JsonSchema.Options Options = new(
        additionalSchemaFiles: null,
        allowFileSystemAndHttpResolution: false,
        defaultDialect: Corvus.Text.Json.RuntimeEvaluator.JsonSchemaDialect.Draft202012,
        alwaysAssertFormat: true,
        additionalDocumentResolver: null);

    /// <inheritdoc />
    public FormatValueResult Validate(JsonNode? value, JsonNode? format, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);

        JsonSchema schema;
        try
        {
            if (!FormatTypes.Expands(format, formats) || FormatTypes.FullForm(format, formats) is not JsonObject full)
            {
                return new FormatValueResult(false, BadFormat);
            }

            var uri = "urn:consultologist:validate:" + CanonicalJson.Hash(full).Replace("sha256:", "", StringComparison.Ordinal);
            var copy = (JsonObject)full.DeepClone();
            copy.Remove("$id");
            copy["$schema"] = "https://json-schema.org/draft/2020-12/schema";
            schema = JsonSchema.FromText(copy.ToJsonString(), uri, Options, false);
        }
        catch (Exception)
        {
            return new FormatValueResult(false, BadFormat);
        }

        try
        {
            var json = value?.ToJsonString() ?? "null";
            if (schema.Validate(json, null))
            {
                return new FormatValueResult(true, []);
            }

            using var collector = JsonSchemaResultsCollector.Create(JsonSchemaResultsLevel.Detailed, 0);
            schema.Validate(json, collector);
            var faults = Faults(collector);
            return new FormatValueResult(false, faults.Count > 0 ? faults : BadValue);
        }
        catch (Exception)
        {
            return new FormatValueResult(false, BadValue);
        }
    }

    /// <summary>The failures that are leaves: a failure under another failure is its reason, and under an <c>anyOf</c> or <c>oneOf</c> the union is the fault.</summary>
    private static List<FormatValueFault> Faults(JsonSchemaResultsCollector collector)
    {
        var failed = new List<(string Schema, string Document)>();
        var results = collector.EnumerateResults();
        while (results.MoveNext())
        {
            var result = results.Current;
            if (result.IsMatch)
            {
                continue;
            }

            var schema = result.GetSchemaEvaluationLocationText().TrimStart('#');
            if (schema.Length > 0)
            {
                failed.Add((schema, result.GetDocumentEvaluationLocationText().TrimStart('#')));
            }
        }

        static bool Under(string child, string parent) => child.Length > parent.Length && child.StartsWith(parent + "/", StringComparison.Ordinal);
        static string Keyword(string schema) => schema[(schema.LastIndexOf('/') + 1)..];

        var unions = failed.Where(f => Keyword(f.Schema) is "anyOf" or "oneOf").Select(f => f.Schema).ToList();
        var kept = failed.Where(f => !unions.Any(u => Under(f.Schema, u))).ToList();
        var leaves = kept.Where(f => !kept.Any(o => Under(o.Schema, f.Schema)));

        return leaves
            .Select(f =>
            {
                var keyword = Keyword(f.Schema);
                var pointer = keyword is "required" or "additionalProperties" ? f.Document[..Math.Max(0, f.Document.LastIndexOf('/'))] : f.Document;
                return new FormatValueFault(pointer, keyword);
            })
            .Distinct()
            .OrderBy(f => f.Pointer, StringComparer.Ordinal)
            .ThenBy(f => f.Keyword, StringComparer.Ordinal)
            .ToList();
    }
}
