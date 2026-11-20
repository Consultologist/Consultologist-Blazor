using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): type strings, how people see a format (part 02 § 2.4, D7).
/// A name or a <c>$ref</c> prints the name, known or not; <c>#/$defs/x</c> prints what it names;
/// a built-in data type prints by name (<c>string</c>, <c>number</c>, <c>integer</c>, <c>boolean</c>,
/// <c>date</c>, <c>null</c>); a text format's own schema prints its kind (<c>plaintext</c>);
/// an object <c>{ a: T, b: U }</c> in written order (<c>{}</c> when empty); an array
/// <c>list&lt;T&gt;</c>; a union its members in written order joined by <c> | </c>; a nullable type
/// array <c>T | null</c>; an enum or labelled <c>oneOf</c> <c>string {a, b}</c> with its non-null
/// values, then <c> | null</c> when nullable. What is not a format prints <c>?</c>.
/// </summary>
public static class TypeStrings
{
    private const string Unknown = "?";

    /// <summary>The type string of <paramref name="format"/>. Never throws on package content; a loop through <c>#/$defs</c> prints <c>?</c> where it closes.</summary>
    public static string Print(JsonNode? format, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        return new Printer().Print(format, FormatTypes.Defs(format), 0);
    }

    /// <summary>One call's printer: the definitions being printed (so a loop stops) and what is left of the budget.</summary>
    private sealed class Printer
    {
        private readonly HashSet<JsonNode> open = new(ReferenceEqualityComparer.Instance);
        private int budget = FormatTypes.NodeBudget;

        public string Print(JsonNode? node, JsonObject? defs, int depth)
        {
            if (FormatSet.NameOf(node) is { } name)
            {
                return name;
            }

            if (node is not JsonObject obj || depth > SourceBounds.JsonDepth || budget-- <= 0)
            {
                return Unknown;
            }

            if (FormatTypes.Text(obj["$ref"]) is { } reference)
            {
                if (FormatDialect.DefinitionName(reference) is { } definition)
                {
                    if (defs?[definition] is not { } target || !open.Add(target))
                    {
                        return Unknown;
                    }

                    var printed = Print(target, defs, depth + 1);
                    open.Remove(target);
                    return printed;
                }

                return reference.Length > 0 && reference[0] != '#' ? reference : Unknown;
            }

            if (obj["anyOf"] is JsonArray members)
            {
                return members.Count == 0 ? Unknown : string.Join(" | ", members.Select(m => Print(m, defs, depth + 1)));
            }

            return obj["type"] switch
            {
                JsonArray pair when FormatTypes.NullablePair(pair) is { } single => Type(obj, single, defs, depth) + " | null",
                JsonValue value when FormatTypes.Text(value) is { } type => Type(obj, type, defs, depth),
                _ => Unknown,
            };
        }

        private string Type(JsonObject obj, string type, JsonObject? defs, int depth) => type switch
        {
            "string" => Text(obj),
            "number" or "integer" or "boolean" or "null" => type,
            "array" => $"list<{Print(obj["items"], defs, depth + 1)}>",
            "object" => obj["properties"] switch
            {
                null => "{}",
                JsonObject { Count: 0 } => "{}",
                JsonObject properties => "{ " + string.Join(", ", properties.Select(p => $"{p.Key}: {Print(p.Value, defs, depth + 1)}")) + " }",
                _ => Unknown,
            },
            _ => Unknown,
        };

        private static string Text(JsonObject obj)
        {
            if (obj["enum"] is JsonArray values)
            {
                return Enum(values);
            }

            if (obj["oneOf"] is JsonArray arms)
            {
                return Enum(arms.OfType<JsonObject>().Where(a => a.ContainsKey("const")).Select(a => a["const"]));
            }

            if (FormatTypes.Text(obj["format"]) == "date")
            {
                return "date";
            }

            return FormatTypes.KindOf(obj) ?? "string";
        }

        private static string Enum(IEnumerable<JsonNode?> values) =>
            "string {" + string.Join(", ", values.Where(v => v is not null).Select(v =>
                v is JsonValue s && s.GetValueKind() == JsonValueKind.String ? s.GetValue<string>() : CanonicalJson.Write(v))) + "}";
    }
}
