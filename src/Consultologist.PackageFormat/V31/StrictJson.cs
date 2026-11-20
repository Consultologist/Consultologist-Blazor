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
/// #1499: where in a JSON text a fault lies, as <c>System.Text.Json</c> counts it: the zero-based
/// line and the zero-based byte offset in that line (UTF-8 bytes). <see cref="StrictJson.Locate"/>
/// turns it into a line and a column a reader of the text can find.
/// </summary>
public sealed record StrictJsonPosition(long Line, long BytePositionInLine);

/// <summary>
/// #1499: the outcome of <see cref="StrictJson.Parse"/>. For <c>NotJson</c>, <see cref="Position"/>
/// is where the text stops being JSON (where it ends, for an empty or truncated text) and
/// <see cref="Detail"/> is null: no parser's own wording reaches a sentence. For
/// <c>DuplicateMember</c>, <see cref="Detail"/> is the repeated member's name; for <c>TooDeep</c>,
/// the JSON pointer of the container that opened too deep. <see cref="IsNull"/> tells a valid
/// <c>null</c> document (whose <see cref="Value"/> is null) from a fault.
/// </summary>
public sealed record StrictJsonResult(
    JsonNode? Value, bool IsNull, StrictJsonFault? Fault, string? Detail, StrictJsonPosition? Position = null);

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
        var bytes = new UTF8Encoding(false, false).GetBytes(text);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = ReaderMaxDepth,
        });
        try
        {
            var pointer = new List<string>();
            if (!reader.Read())
                return NotJson(At(bytes, bytes.Length));
            var value = ReadValue(ref reader, pointer, 0);
            if (reader.Read())
                return NotJson(At(bytes, reader.TokenStartIndex));
            return new StrictJsonResult(value, value is null, null, null);
        }
        catch (FaultException fault)
        {
            return fault.Fault == StrictJsonFault.NotJson
                ? NotJson(At(bytes, fault.Offset ?? reader.TokenStartIndex))
                : new StrictJsonResult(null, false, fault.Fault, fault.Detail);
        }
        catch (JsonException e)
        {
            return NotJson(e.LineNumber is { } line && e.BytePositionInLine is { } column
                ? new StrictJsonPosition(line, column)
                : At(bytes, reader.TokenStartIndex));
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            // A string the reader cannot read back (a lone surrogate escape): the token's start.
            return NotJson(At(bytes, reader.TokenStartIndex));
        }
    }

    /// <summary>
    /// The 1-based line and column of <paramref name="position"/> in <paramref name="text"/>: the
    /// column counts characters (Unicode code points, as the description bound does), not bytes. A
    /// position past the end of its line is the column after the line's last character.
    /// </summary>
    public static (int Line, int Column) Locate(string text, StrictJsonPosition position)
    {
        var lines = text.Split('\n');
        var index = (int)Math.Clamp(position.Line, 0, lines.Length - 1);
        var column = 1;
        long bytes = 0;
        foreach (var rune in lines[index].EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > position.BytePositionInLine)
                break;
            column++;
        }

        return (index + 1, column);
    }

    private static StrictJsonResult NotJson(StrictJsonPosition position) =>
        new(null, false, StrictJsonFault.NotJson, null, position);

    /// <summary>The position of the byte at <paramref name="offset"/>, counted as <c>System.Text.Json</c> counts it (lines end at <c>\n</c>).</summary>
    private static StrictJsonPosition At(byte[] bytes, long offset)
    {
        var end = (int)Math.Clamp(offset, 0, bytes.Length);
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < end; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new StrictJsonPosition(line, end - lineStart);
    }

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
                throw new FaultException(StrictJsonFault.NotJson, $"Unexpected token {reader.TokenType}.", reader.TokenStartIndex);
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
                throw new FaultException(StrictJsonFault.NotJson, "The text ends inside an object.", reader.BytesConsumed);
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

    private sealed class FaultException(StrictJsonFault fault, string detail, long? offset = null) : Exception(detail)
    {
        public StrictJsonFault Fault { get; } = fault;

        public string Detail { get; } = detail;

        /// <summary>For <c>NotJson</c>: the byte offset of the fault in the text.</summary>
        public long? Offset { get; } = offset;
    }
}
