using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>What was wrong with a JSON text.</summary>
public enum StrictJsonFault
{
    /// <summary>Not one RFC 8259 value (comments, trailing commas, extra values and the like).</summary>
    NotJson,

    /// <summary>An object names one member twice.</summary>
    DuplicateMember,

    /// <summary>A container opens deeper than <see cref="SourceBounds.JsonDepth"/>.</summary>
    TooDeep,
}

/// <summary>
/// #1499: the outcome of <see cref="StrictJson.Parse"/>. <see cref="Detail"/> is the parser's
/// message (<c>NotJson</c>), the repeated member's name (<c>DuplicateMember</c>) or the JSON
/// pointer of the container that opened too deep (<c>TooDeep</c>). <see cref="IsNull"/> tells a
/// valid <c>null</c> document (whose <see cref="Value"/> is null) from a fault.
/// </summary>
public sealed record StrictJsonResult(JsonNode? Value, bool IsNull, StrictJsonFault? Fault, string? Detail);

/// <summary>
/// #1499: RFC 8259 JSON and nothing more (no comments, no trailing commas, one value then only
/// whitespace), with a refusal for a repeated member name at any depth and for nesting past
/// <see cref="SourceBounds.JsonDepth"/> (decision D2: the outermost container is level 1). It
/// never throws.
/// </summary>
public static class StrictJson
{
    // Above the bound, so the walk reports depth itself, with a pointer, instead of the reader throwing.
    private const int ReaderMaxDepth = SourceBounds.JsonDepth + 8;

    /// <summary>Parses <paramref name="text"/>; the fault, if any, is in the result.</summary>
    public static StrictJsonResult Parse(string text)
    {
        try
        {
            var bytes = new UTF8Encoding(false, false).GetBytes(text);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = ReaderMaxDepth,
            });
            var pointer = new List<string>();
            if (!reader.Read())
                return Fail(StrictJsonFault.NotJson, "The text holds no JSON value.");
            var value = ReadValue(ref reader, pointer, 0);
            if (reader.Read())
                return Fail(StrictJsonFault.NotJson, "The text holds more than one JSON value.");
            return new StrictJsonResult(value, value is null, null, null);
        }
        catch (FaultException fault)
        {
            return Fail(fault.Fault, fault.Detail);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return Fail(StrictJsonFault.NotJson, e.Message);
        }
    }

    private static StrictJsonResult Fail(StrictJsonFault fault, string detail) => new(null, false, fault, detail);

    private static JsonNode? ReadValue(ref Utf8JsonReader reader, List<string> pointer, int level)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                return ReadObject(ref reader, pointer, level + 1);
            case JsonTokenType.StartArray:
                return ReadArray(ref reader, pointer, level + 1);
            case JsonTokenType.String:
                return JsonValue.Create(reader.GetString());
            case JsonTokenType.True:
                return JsonValue.Create(true);
            case JsonTokenType.False:
                return JsonValue.Create(false);
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return JsonValue.Create(JsonElement.ParseValue(ref reader));
            default:
                throw new FaultException(StrictJsonFault.NotJson, $"Unexpected token {reader.TokenType}.");
        }
    }

    private static void CheckDepth(List<string> pointer, int level)
    {
        if (level > SourceBounds.JsonDepth)
            throw new FaultException(StrictJsonFault.TooDeep, Pointer(pointer));
    }

    private static JsonObject ReadObject(ref Utf8JsonReader reader, List<string> pointer, int level)
    {
        CheckDepth(pointer, level);
        var result = new JsonObject();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var name = reader.GetString()!;
            if (!seen.Add(name))
                throw new FaultException(StrictJsonFault.DuplicateMember, name);
            if (!reader.Read())
                throw new FaultException(StrictJsonFault.NotJson, "The text ends inside an object.");
            pointer.Add(name);
            result.Add(name, ReadValue(ref reader, pointer, level));
            pointer.RemoveAt(pointer.Count - 1);
        }

        return result;
    }

    private static JsonArray ReadArray(ref Utf8JsonReader reader, List<string> pointer, int level)
    {
        CheckDepth(pointer, level);
        var result = new JsonArray();
        var index = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            pointer.Add(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            result.Add(ReadValue(ref reader, pointer, level));
            pointer.RemoveAt(pointer.Count - 1);
            index++;
        }

        return result;
    }

    private static string Pointer(List<string> pointer) =>
        string.Concat(pointer.Select(p => "/" + p.Replace("~", "~0").Replace("/", "~1")));

    private sealed class FaultException(StrictJsonFault fault, string detail) : Exception(detail)
    {
        public StrictJsonFault Fault { get; } = fault;

        public string Detail { get; } = detail;
    }
}
