using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// The engine's instance validator for the strict structured-output subset
/// (package-format-v25-design.md § 3.5; B4 #1003): a reply parsed as JSON is
/// checked against a format's schema after parsing and before anything is
/// composed. The subset is exactly the keywords the catalogue's and a package's
/// formats may use — <c>type</c> (a name or a union, <c>null</c> among them),
/// <c>properties</c> + <c>required</c> + <c>additionalProperties:false</c>,
/// <c>items</c>, <c>enum</c>, and the annotations <c>title</c>,
/// <c>description</c>, <c>$schema</c>. A keyword outside the subset is an
/// error, never a silent "valid": a constraint the engine does not enforce
/// must not be published as if it were.
/// </summary>
public static class JsonSchemaSubsetValidator
{
    public static readonly IReadOnlySet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "items", "enum", "title", "description", "$schema",
    };

    private static readonly IReadOnlySet<string> TypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "string", "number", "integer", "boolean", "object", "array", "null",
    };

    /// <summary>Every keyword outside the subset, anywhere in the schema, each with its path.</summary>
    public static IReadOnlyList<string> UnsupportedKeywords(JsonNode? schema)
    {
        var errors = new List<string>();
        Walk(schema, "");
        return errors;

        void Walk(JsonNode? node, string path)
        {
            if (node is not JsonObject obj)
            {
                return;
            }

            foreach (var (keyword, value) in obj)
            {
                if (!Keywords.Contains(keyword))
                {
                    errors.Add($"keyword '{keyword}'{At(path)} is outside the strict structured-output subset ({string.Join(", ", Keywords.Order(StringComparer.Ordinal))}); the engine would not enforce it.");
                    continue;
                }

                switch (keyword)
                {
                    case "properties" when value is JsonObject properties:
                        foreach (var (name, property) in properties)
                        {
                            Walk(property, Join(path, name));
                        }

                        break;
                    case "items":
                        Walk(value, $"{path}[]");
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Every way the instance misses the schema, each with its path; empty when
    /// it conforms. Unsupported keywords in the schema are reported too, so a
    /// reply is never called valid against a constraint that was not checked.
    /// </summary>
    public static IReadOnlyList<string> Validate(JsonNode? instance, JsonNode schema)
    {
        var errors = new List<string>(UnsupportedKeywords(schema));
        Check(instance, schema, "");
        return errors;

        void Check(JsonNode? value, JsonNode? schemaNode, string path)
        {
            if (schemaNode is not JsonObject obj)
            {
                return;
            }

            if (obj["type"] is { } type && !TypeMatches(value, type, out var expected))
            {
                errors.Add($"'{Root(path)}' is {Describe(value)}, not {expected}.");
                return;
            }

            if (obj["enum"] is JsonArray allowed && !allowed.Any(member => JsonNode.DeepEquals(member, value)))
            {
                errors.Add($"'{Root(path)}' is not one of the declared values.");
                return;
            }

            if (value is JsonObject instanceObject)
            {
                var properties = obj["properties"] as JsonObject;

                foreach (var required in (obj["required"] as JsonArray ?? new JsonArray()).Select(r => r?.GetValue<string>()).Where(r => r is not null))
                {
                    if (!instanceObject.ContainsKey(required!))
                    {
                        errors.Add($"'{Root(path)}' lacks required property '{required}'.");
                    }
                }

                var closed = obj["additionalProperties"] is JsonValue addl && addl.TryGetValue(out bool allowsExtra) && !allowsExtra;

                foreach (var (name, member) in instanceObject)
                {
                    if (properties is not null && properties.TryGetPropertyValue(name, out var propertySchema))
                    {
                        Check(member, propertySchema, Join(path, name));
                    }
                    else if (closed)
                    {
                        errors.Add($"'{Root(path)}' has undeclared property '{name}'.");
                    }
                }
            }

            if (value is JsonArray instanceArray && obj["items"] is { } items)
            {
                for (var i = 0; i < instanceArray.Count; i++)
                {
                    Check(instanceArray[i], items, $"{path}[{i.ToString(CultureInfo.InvariantCulture)}]");
                }
            }
        }
    }

    private static bool TypeMatches(JsonNode? value, JsonNode type, out string expected)
    {
        var names = type switch
        {
            JsonValue single when single.TryGetValue(out string? one) => new[] { one! },
            JsonArray union => union.Select(member => member is JsonValue v && v.TryGetValue(out string? name) ? name! : "").Where(n => n.Length > 0).ToArray(),
            _ => Array.Empty<string>(),
        };
        expected = string.Join(" or ", names);
        var actual = KindOf(value);

        return names.Any(name => TypeNames.Contains(name) && (name == actual || (name == "number" && actual == "integer")));
    }

    private static string KindOf(JsonNode? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case JsonObject:
                return "object";
            case JsonArray:
                return "array";
            case JsonValue jsonValue:
                var element = jsonValue.GetValue<JsonElement>();
                return element.ValueKind switch
                {
                    JsonValueKind.String => "string",
                    JsonValueKind.True or JsonValueKind.False => "boolean",
                    JsonValueKind.Number => element.TryGetInt64(out _) || (element.TryGetDecimal(out var d) && d == decimal.Truncate(d)) ? "integer" : "number",
                    JsonValueKind.Null => "null",
                    _ => element.ValueKind.ToString().ToLowerInvariant(),
                };
            default:
                return "unknown";
        }
    }

    private static string Describe(JsonNode? value) => KindOf(value) switch
    {
        "null" => "null",
        "integer" => "a number",
        var kind when "aeiou".Contains(kind[0]) => $"an {kind}",
        var kind => $"a {kind}",
    };

    private static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    private static string Root(string path) => path.Length == 0 ? "$" : path;

    private static string At(string path) => path.Length == 0 ? "" : $" at '{path}'";
}
