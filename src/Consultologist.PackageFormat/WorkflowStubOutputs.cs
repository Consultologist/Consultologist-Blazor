using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// The deterministic stubs of part 10 § 3.1 (#1498): a value for a node's output made from its
/// format alone, seeded by the instance's key (the node's key, then ':' and its item path and
/// pass), so the same seed always gives the same stub and nothing is random or clocked.
/// <c>Pick(seed, path, n)</c> is the first eight bytes of SHA-256 over the UTF-8 of
/// <c>seed + "\n" + path</c>, read as a big-endian unsigned 64-bit integer, modulo <c>n</c>;
/// <c>Hex8(seed, path)</c> is the first eight lowercase hex characters of the same digest.
/// <c>Text</c> is <c>"Stub reply for " + seed + " (" + Hex8(seed, "$") + ")."</c>; <c>Member</c> is
/// <c>values[Pick(seed, "$", values.Count)]</c>. <c>FromSchema</c> walks from path <c>$</c>: an
/// <c>enum</c> gives the member at <c>Pick(seed, path, count)</c>, cloned; a <c>const</c> its
/// value; a <c>type</c> array its first member that is not <c>"null"</c>; no <c>type</c> is
/// <c>object</c> when <c>properties</c> is present and <c>string</c> otherwise. An <c>object</c>
/// gets every property in declared order at <c>&lt;path&gt;.&lt;name&gt;</c>; an <c>array</c>
/// gets <c>1 + Pick(seed, path, 3)</c> items, raised to <c>minItems</c> and lowered to
/// <c>maxItems</c>, item <c>i</c> at <c>&lt;path&gt;[&lt;i&gt;]</c>; a <c>boolean</c> is
/// <c>Pick(seed, path, 2) == 0</c>; an <c>integer</c> or <c>number</c> is <c>Pick(seed, path, 10)</c>,
/// raised to <c>minimum</c> and lowered to <c>maximum</c>; a <c>string</c> is
/// <c>"stub " + Hex8(seed, path)</c>, cut to <c>maxLength</c>; <c>null</c> is <c>null</c>.
/// </summary>
public static class WorkflowStubOutputs
{
    /// <summary>A text format's reply.</summary>
    public static string Text(string seed) =>
        "Stub reply for " + seed + " (" + Hex8(seed, "$") + ").";

    /// <summary>An enum's or classifier's member.</summary>
    public static string Member(IReadOnlyList<string> values, string seed) =>
        values[(int)Pick(seed, "$", values.Count)];

    /// <summary>A structured format's value, made from its schema alone.</summary>
    public static JsonNode? FromSchema(JsonNode? schema, string seed) => Walk(schema, seed, "$");

    private static JsonNode? Walk(JsonNode? schema, string seed, string path)
    {
        var obj = schema as JsonObject;

        if (obj?["enum"] is JsonArray members && members.Count > 0)
        {
            return members[(int)Pick(seed, path, members.Count)]?.DeepClone();
        }

        if (obj is not null && obj.TryGetPropertyValue("const", out var constant))
        {
            return constant?.DeepClone();
        }

        switch (TypeOf(obj))
        {
            case "object":
                var result = new JsonObject();

                if (obj?["properties"] is JsonObject properties)
                {
                    foreach (var (name, property) in properties)
                    {
                        result[name] = Walk(property, seed, $"{path}.{name}");
                    }
                }

                return result;

            case "array":
                var count = 1 + (int)Pick(seed, path, 3);

                if (Bound(obj, "minItems") is { } minItems)
                {
                    count = Math.Max(count, (int)minItems);
                }

                if (Bound(obj, "maxItems") is { } maxItems)
                {
                    count = Math.Min(count, (int)maxItems);
                }

                var items = new JsonArray();

                for (var i = 0; i < count; i++)
                {
                    items.Add(Walk(obj?["items"], seed, $"{path}[{i.ToString(CultureInfo.InvariantCulture)}]"));
                }

                return items;

            case "boolean":
                return JsonValue.Create(Pick(seed, path, 2) == 0);

            case "integer":
            case "number":
                var number = (double)Pick(seed, path, 10);

                if (Bound(obj, "minimum") is { } minimum)
                {
                    number = Math.Max(number, minimum);
                }

                if (Bound(obj, "maximum") is { } maximum)
                {
                    number = Math.Min(number, maximum);
                }

                return number == Math.Truncate(number) ? JsonValue.Create((long)number) : JsonValue.Create(number);

            case "null":
                return null;

            default:
                var text = "stub " + Hex8(seed, path);

                return JsonValue.Create(Bound(obj, "maxLength") is { } maxLength && maxLength < text.Length ? text[..Math.Max(0, (int)maxLength)] : text);
        }
    }

    private static string TypeOf(JsonObject? obj)
    {
        switch (obj?["type"])
        {
            case JsonArray union:
                return union
                    .Select(member => member is JsonValue v && v.TryGetValue(out string? name) ? name : null)
                    .FirstOrDefault(name => name is not null and not "null") ?? "null";
            case JsonValue single when single.TryGetValue(out string? one):
                return one!;
            default:
                return obj?["properties"] is not null ? "object" : "string";
        }
    }

    private static double? Bound(JsonObject? obj, string keyword) =>
        obj?[keyword] is JsonValue value && value.TryGetValue(out double bound) ? bound : null;

    private static byte[] Digest(string seed, string path) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(seed + "\n" + path));

    private static ulong Pick(string seed, string path, int n) =>
        BinaryPrimitives.ReadUInt64BigEndian(Digest(seed, path)) % (ulong)n;

    private static string Hex8(string seed, string path) =>
        Convert.ToHexStringLower(Digest(seed, path))[..8];
}
