using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Logic;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1507 (specVersion 31, area 10): what the migration tests share. The 30 expressions of
/// <c>conditions/expressions.json</c> (area 01's, read, never rewritten), and <see cref="ScopeOf"/>, the
/// <see cref="ConversionScope"/> an entry's <c>reads</c> describe at 31: each input's 30 type as part 02 § 11 maps
/// it, required when the read says <c>required: true</c> and nullable otherwise (<c>text</c> → <c>markdown_block</c>, <c>enum</c> → a <c>string</c> with a plain <c>enum</c>, <c>date</c> →
/// <c>date</c>, <c>fields</c> → <c>properties</c> with every field in <c>required</c> and an optional one nullable),
/// each 30 node <c>x</c> at the path <c>node.x</c> (<c>text</c> → <c>plaintext</c>, as <c>prose</c> becomes;
/// <c>classification</c> kept; an object schema made strict the same way), always valued when the read is
/// <c>settled</c> and nullable otherwise, the loop's item, and
/// <c>pass.number</c> readable when it is read.
/// </summary>
internal static class MigrationFixture
{
    /// <summary>The entries of <c>conditions/expressions.json</c>, in order.</summary>
    public static IReadOnlyList<JsonObject> Expressions() => ExpressionsFile.Value;

    /// <summary>The entry with <paramref name="id"/>.</summary>
    public static JsonObject Expression(string id) =>
        Expressions().Single(e => (string?)e["id"] == id);

    /// <summary>
    /// The published formats (<see cref="LogicFixture.Formats"/>), and the two package formats the § 2.2 <c>let</c>s
    /// declare (their <c>letType</c>), written strict as part 02 § 3.3 has them: <c>triage_summary</c>, the
    /// <c>priority</c> and <c>age</c> that <c>row.let-object</c> builds, and <c>reasons</c>, the list of plain text
    /// <c>row.let-array</c> builds.
    /// </summary>
    public static FormatSet Formats() => PublishedFormats.Value;

    /// <summary>How an entry is converted: a <c>condition</c> as a condition, a <c>case</c> subject and a <c>let</c> as a value.</summary>
    public static ConversionKind KindOf(JsonObject entry) =>
        (string?)entry["kind"] == "condition" ? ConversionKind.Condition : ConversionKind.Value;

    /// <summary>
    /// A <c>let</c>'s declared format at 31 (part 02 § 11: <c>text</c> is <c>markdown_block</c>, <c>number</c> and
    /// <c>boolean</c> are themselves, a format id is that format); <c>null</c> for a condition and a <c>case</c>.
    /// </summary>
    public static JsonNode? DeclaredOf(JsonObject entry) => (string?)entry["letType"] switch
    {
        null => null,
        "text" => JsonValue.Create("markdown_block"),
        var type => JsonValue.Create(type),
    };

    private static readonly Lazy<IReadOnlyList<JsonObject>> ExpressionsFile = new(() =>
        JsonNode.Parse(ByteEquivalenceFixtures.ReadText(Path.Combine(ByteEquivalenceFixtures.Root, "conditions", "expressions.json")))!
            ["expressions"]!.AsArray().Select(e => e!.AsObject()).ToList());

    private static readonly Lazy<FormatSet> PublishedFormats = new(() => LogicFixture.Formats(
        ("triage_summary", """
            {"type":"object","additionalProperties":false,"required":["priority","age"],
             "properties":{"priority":{"type":["string","null"],"enum":["low","high",null]},"age":{"type":"number"}}}
            """),
        ("reasons", """
            {"type":"object","additionalProperties":false,"required":["reasons"],
             "properties":{"reasons":{"type":"array","items":{"$ref":"plaintext"}}}}
            """)));

    /// <summary>The 31 scope an entry's <c>reads</c> describe (see the class).</summary>
    public static ConversionScope ScopeOf(JsonObject reads)
    {
        var inputs = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new Dictionary<string, ConversionRead>(StringComparer.Ordinal);
        JsonNode? item = null;
        var folderItem = false;
        var inWhile = false;

        foreach (var (read, type) in reads)
        {
            if (read.StartsWith("input.", StringComparison.Ordinal))
            {
                inputs[read["input.".Length..]] = Format(type!, nullable: false);
                if (type is JsonObject { } declared && declared["required"]?.GetValue<bool>() == true)
                {
                    required.Add(read["input.".Length..]);
                }
            }
            else if (read.StartsWith("node.", StringComparison.Ordinal))
            {
                nodes[read["node.".Length..]] = new ConversionRead(read, NodeFormat(type!))
                {
                    AlwaysValued = type is JsonObject { } settled && settled["settled"]?.GetValue<bool>() == true,
                };
            }
            else if (read == "item")
            {
                if (type!["value"] is { } value)
                {
                    item = Format(value, nullable: false);
                }
                else
                {
                    folderItem = true;
                }
            }
            else if (read == "pass.number")
            {
                inWhile = true;
            }
        }

        return new ConversionScope(inputs, nodes, item, folderItem, inWhile, Formats()) { RequiredInputs = required };
    }

    /// <summary>A 30 node's format at 31: <c>text</c> is <c>plaintext</c>, a classifier keeps <c>classification</c>, an object its schema made strict.</summary>
    private static JsonNode NodeFormat(JsonNode type)
    {
        if (type is JsonValue)
        {
            return (string)type! == "text"
                ? JsonValue.Create("plaintext")
                : throw new InvalidOperationException($"A node read of type '{type}' has no 31 format here.");
        }

        return (string?)type["type"] switch
        {
            "text" => JsonValue.Create("plaintext"),
            "classification" => JsonValue.Create("classification"),
            "object" when type["schema"] is JsonObject schema => Strict(schema, nullable: false),
            var other => throw new InvalidOperationException($"A node read of type '{other}' has no 31 format here."),
        };
    }

    /// <summary>A 30 read type (<c>text</c>, <c>enum</c>, <c>number</c>, <c>boolean</c>, <c>date</c>, <c>array</c>, <c>object</c>) as its 31 format.</summary>
    private static JsonNode Format(JsonNode type, bool nullable)
    {
        var name = type is JsonValue ? (string)type! : (string)type["type"]!;
        switch (name)
        {
            case "text":
                return nullable ? NullableReference("markdown_block") : JsonValue.Create("markdown_block");
            case "number":
            case "boolean":
                return nullable ? new JsonObject { ["type"] = new JsonArray(name, "null") } : JsonValue.Create(name);
            case "date":
                return nullable
                    ? new JsonObject { ["type"] = new JsonArray("string", "null"), ["format"] = "date" }
                    : JsonValue.Create("date");
            case "enum":
                var values = new JsonArray(type["values"]!.AsArray().Select(v => (JsonNode?)JsonValue.Create((string)v!)).ToArray());
                if (nullable)
                {
                    values.Add(null);
                }

                return new JsonObject
                {
                    ["type"] = nullable ? new JsonArray("string", "null") : "string",
                    ["enum"] = values,
                };
            case "array":
                var list = new JsonObject { ["type"] = "array", ["items"] = Format(type["items"]!, nullable: false) };
                return nullable ? NullableSchema(list) : list;
            case "object":
                var properties = new JsonObject();
                var required = new JsonArray();
                foreach (var (field, fieldType) in type["fields"]!.AsObject())
                {
                    var fieldRequired = fieldType is JsonObject declared && declared["required"]?.GetValue<bool>() == true;
                    properties[field] = Format(fieldType!, nullable: !fieldRequired);
                    required.Add(field);
                }

                var obj = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = required,
                    ["properties"] = properties,
                };
                return nullable ? NullableSchema(obj) : obj;
            default:
                throw new InvalidOperationException($"A read type '{name}' has no 31 format here.");
        }
    }

    /// <summary>A 30 JSON Schema made strict (part 02 § 3.3): every property in <c>required</c>, an optional one nullable.</summary>
    private static JsonNode Strict(JsonObject schema, bool nullable)
    {
        var type = (string?)schema["type"];
        switch (type)
        {
            case "object":
                var listed = schema["required"]?.AsArray().Select(r => (string)r!).ToHashSet(StringComparer.Ordinal) ?? [];
                var properties = new JsonObject();
                var required = new JsonArray();
                foreach (var (name, property) in schema["properties"]?.AsObject() ?? new JsonObject())
                {
                    properties[name] = Strict(property!.AsObject(), nullable: !listed.Contains(name));
                    required.Add(name);
                }

                var obj = new JsonObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["required"] = required,
                    ["properties"] = properties,
                };
                return nullable ? NullableSchema(obj) : obj;
            case "array":
                var list = new JsonObject { ["type"] = "array", ["items"] = Strict(schema["items"]!.AsObject(), nullable: false) };
                return nullable ? NullableSchema(list) : list;
            case "string" when schema["enum"] is JsonArray values:
                var enumValues = values.DeepClone().AsArray();
                if (nullable)
                {
                    enumValues.Add(null);
                }

                return new JsonObject { ["type"] = nullable ? new JsonArray("string", "null") : "string", ["enum"] = enumValues };
            case "string" or "number" or "integer" or "boolean":
                return new JsonObject { ["type"] = nullable ? new JsonArray(type, "null") : type };
            default:
                throw new InvalidOperationException($"A node schema of type '{type}' has no 31 format here.");
        }
    }

    private static JsonObject NullableReference(string name) =>
        new() { ["anyOf"] = new JsonArray(new JsonObject { ["$ref"] = name }, new JsonObject { ["type"] = "null" }) };

    private static JsonObject NullableSchema(JsonObject schema) =>
        new() { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) };
}
