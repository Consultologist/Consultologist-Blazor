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
public sealed record FormatValueResult(bool Valid, IReadOnlyList<FormatValueFault> Faults)
{
    /// <summary>
    /// #1507 (area 10; part 02 § 5; R10, D21): the keyword of the fault a value has when it fits its format as written but a
    /// <c>markdown_inline</c> text in it holds a line ending: the annotation that makes the text inline.
    /// </summary>
    public const string LineEndingKeyword = "x-markdown";

    /// <summary>Whether the value fits its format but for a line ending in a <c>markdown_inline</c> text (<see cref="LineEndingKeyword"/>).</summary>
    public bool BreaksALine => !Valid && Faults.Count > 0 && Faults.All(f => f.Keyword == LineEndingKeyword);
}

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

        JsonNode full;
        try
        {
            if (!FormatTypes.Expands(format, formats) || FormatTypes.FullForm(format, formats) is not JsonObject form)
            {
                return new FormatValueResult(false, BadFormat);
            }

            full = form;
        }
        catch (Exception)
        {
            return new FormatValueResult(false, BadFormat);
        }

        return ValidateExpanded(value, full);
    }

    /// <summary>
    /// #1503 (area 06): validates <paramref name="value"/> against <paramref name="schema"/>, a self-contained schema (no
    /// reference left to resolve; what <see cref="Validate"/> holds a value to once it has expanded the format): an object,
    /// compiled once per process as <see cref="Validate"/> compiles one, or a boolean schema (<c>true</c> fits every value,
    /// <c>false</c> none, with the fault <c>("", "$ref")</c> of a format that cannot be compiled). Never throws on either.
    /// #1507 (area 10; part 02 § 5: a <c>markdown_inline</c> text "sits inside a line"; R10, D21): a value that fits the
    /// schema as written is held too to every <c>markdown_inline</c> text in it holding no line ending (<c>\n</c> or
    /// <c>\r</c>), at any depth; one that holds one has a fault <see cref="FormatValueResult.LineEndingKeyword"/> at each
    /// place it fails (<see cref="FormatValueResult.BreaksALine"/>).
    /// </summary>
    public FormatValueResult ValidateExpanded(JsonNode? value, JsonNode schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        var result = ValidateAsWritten(value, schema);
        if (!result.Valid || schema is not JsonObject written)
        {
            return result;
        }

        var oneLine = (JsonObject)written.DeepClone();
        if (!OneLine(oneLine, 0))
        {
            return result;
        }

        // A held copy that cannot be compiled says so as any format does, never as a line ending.
        var held = ValidateAsWritten(value, oneLine);
        return held.Valid || ReferenceEquals(held.Faults, BadFormat)
            ? held
            : new FormatValueResult(false, held.Faults
                .Select(f => f with { Keyword = FormatValueResult.LineEndingKeyword })
                .Distinct()
                .ToList());
    }

    /// <summary>A schema that a text holding a line ending fits: what a <c>markdown_inline</c> text is held not to fit.</summary>
    private static JsonObject LineEnding() => new() { ["pattern"] = "[\\r\\n]" };

    /// <summary>
    /// D21: gives each <c>markdown_inline</c> schema in <paramref name="node"/> (the text kind its own annotations give,
    /// <see cref="FormatTypes.KindOf"/>), where a schema stands (the root, a property's, <c>items</c>, <c>prefixItems</c>,
    /// <c>additionalProperties</c>, <c>not</c>, a member of <c>anyOf</c>, <c>oneOf</c> or <c>allOf</c>), the rule that it
    /// holds no line ending: in its own <c>not</c>, or in its <c>allOf</c> when it has a <c>not</c>. Whether it found one.
    /// Bounded by <see cref="SourceBounds.JsonDepth"/>.
    /// </summary>
    private static bool OneLine(JsonNode? node, int depth)
    {
        if (node is not JsonObject schema || depth > SourceBounds.JsonDepth)
        {
            return false;
        }

        var found = false;
        foreach (var (name, member) in schema.ToList())
        {
            switch (name)
            {
                case "properties" when member is JsonObject properties:
                    foreach (var (_, property) in properties.ToList())
                    {
                        found |= OneLine(property, depth + 1);
                    }

                    break;
                case "items" or "additionalProperties" or "not":
                    found |= OneLine(member, depth + 1);
                    break;
                case "anyOf" or "oneOf" or "allOf" or "prefixItems" when member is JsonArray members:
                    foreach (var item in members.ToList())
                    {
                        found |= OneLine(item, depth + 1);
                    }

                    break;
            }
        }

        if (FormatTypes.KindOf(schema) != "markdown_inline")
        {
            return found;
        }

        if (!schema.ContainsKey("not"))
        {
            schema["not"] = LineEnding();
        }
        else if (schema["allOf"] is JsonArray all)
        {
            all.Add(new JsonObject { ["not"] = LineEnding() });
        }
        else
        {
            schema["allOf"] = new JsonArray(new JsonObject { ["not"] = LineEnding() });
        }

        return true;
    }

    /// <summary><see cref="ValidateExpanded"/> against <paramref name="schema"/> as written, compiled once per process.</summary>
    private static FormatValueResult ValidateAsWritten(JsonNode? value, JsonNode schema)
    {
        if (schema is JsonValue boolean && boolean.GetValueKind() is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
        {
            return boolean.GetValueKind() == System.Text.Json.JsonValueKind.True ? new FormatValueResult(true, []) : new FormatValueResult(false, BadFormat);
        }

        JsonSchema compiled;
        try
        {
            if (schema is not JsonObject full)
            {
                return new FormatValueResult(false, BadFormat);
            }

            var uri = "urn:consultologist:validate:" + CanonicalJson.Hash(full).Replace("sha256:", "", StringComparison.Ordinal);
            var copy = (JsonObject)full.DeepClone();
            copy.Remove("$id");
            copy["$schema"] = "https://json-schema.org/draft/2020-12/schema";
            compiled = JsonSchema.FromText(copy.ToJsonString(), uri, Options, false);
        }
        catch (Exception)
        {
            return new FormatValueResult(false, BadFormat);
        }

        try
        {
            var json = value?.ToJsonString() ?? "null";
            if (compiled.Validate(json, null))
            {
                return new FormatValueResult(true, []);
            }

            using var collector = JsonSchemaResultsCollector.Create(JsonSchemaResultsLevel.Detailed, 0);
            compiled.Validate(json, collector);
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

        var unions = failed.Where(f => EndsOnKeyword(f.Schema) is "anyOf" or "oneOf").Select(f => f.Schema).ToList();
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

    /// <summary>
    /// The keyword a schema location (<c>/properties/a/anyOf/0/type</c>) ends on, or null when it
    /// ends on a name or a position: a segment after <c>properties</c> or <c>$defs</c> is a
    /// property's or a definition's name (a field may be called <c>anyOf</c>), one after
    /// <c>anyOf</c>, <c>oneOf</c>, <c>allOf</c> or <c>prefixItems</c> a position.
    /// </summary>
    private static string? EndsOnKeyword(string schema)
    {
        string? keyword = null;
        var nameOrPosition = false;
        foreach (var segment in schema.Split('/').Skip(1))
        {
            keyword = nameOrPosition ? null : segment;
            nameOrPosition = keyword is "properties" or "$defs" or "patternProperties" or "dependentSchemas"
                or "anyOf" or "oneOf" or "allOf" or "prefixItems";
        }

        return keyword;
    }
}
