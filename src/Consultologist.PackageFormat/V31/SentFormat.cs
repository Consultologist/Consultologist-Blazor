using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03; D3): the bounds of strict mode, the property count and the
/// nesting depth Azure OpenAI documents for structured outputs.
/// </summary>
public static class StrictModeBounds
{
    /// <summary>Object properties in all, a referenced format counted at each place it is referenced.</summary>
    public const int Properties = 100;

    /// <summary>Levels of nested objects, the root object being level 1.</summary>
    public const int Levels = 5;
}

/// <summary>
/// #1500 (specVersion 31, area 03): the copy of a format a model receives. <c>NotSent</c> lists,
/// as pointers into the format's full form, the keywords the agent's strict mode rejects that
/// were removed; <c>Hash</c> is the canonical hash of <c>Schema</c>; <c>Properties</c>,
/// <c>Levels</c> and <c>DeepestPath</c> (the first object at level 6, or null) are counted on it.
/// </summary>
public sealed record SentFormatResult(
    JsonObject Schema,
    IReadOnlyList<string> NotSent,
    string Hash,
    int Properties,
    int Levels,
    string? DeepestPath);

/// <summary>
/// #1500 (specVersion 31, area 03; D8, D22, D23): part 02 § 3.5 steps 1 to 5 on a format's full
/// form: descriptions kept; <c>x-*</c>, <c>contentMediaType</c>, <c>title</c>, <c>$id</c>,
/// <c>$schema</c>, <c>$defs</c> and (#1508, R10) <c>resolve.json</c>'s <c>propertyOrder</c> gone (so a text format is a string with its description); a
/// labelled <c>oneOf</c> an <c>enum</c> of its constants in order; each keyword the agent's strict
/// mode rejects removed and listed. The engine's envelope is not part of it (D22).
/// </summary>
public static class SentFormat
{
    private const int DepthCap = 200;

    /// <summary>
    /// The copy of <paramref name="format"/> a model receives, with <paramref name="strictRejects"/>
    /// removed. Meaningful only for a format that <see cref="FormatTypes.Expands"/>: for one that
    /// does not, the copy keeps what could not be inlined as written, and a part nested past the
    /// walk's cap is <c>{}</c>. Never throws on package content.
    /// </summary>
    public static SentFormatResult Prepare(JsonNode? format, FormatSet formats, IReadOnlyList<string> strictRejects)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(strictRejects);

        var full = FormatTypes.FullForm(format, formats);
        var notSent = new List<string>();
        var schema = full is JsonObject root
            ? Clean(root, "", strictRejects, notSent, 0)
            : new JsonObject();
        notSent.Sort(StringComparer.Ordinal);

        var count = new Counter();
        count.Walk(schema, "", 0, 0);
        return new SentFormatResult(schema, notSent, CanonicalJson.Hash(schema), count.Properties, count.Levels, count.DeepestPath);
    }

    /// <summary>S02-055 when the sent copy declares more than <see cref="StrictModeBounds.Properties"/> properties, then S02-056 when it nests deeper than <see cref="StrictModeBounds.Levels"/>.</summary>
    public static IReadOnlyList<SourceRefusal> CheckLimits(string formatName, SentFormatResult sent, string path)
    {
        ArgumentNullException.ThrowIfNull(formatName);
        ArgumentNullException.ThrowIfNull(sent);
        ArgumentNullException.ThrowIfNull(path);
        var refusals = new List<SourceRefusal>();
        if (sent.Properties > StrictModeBounds.Properties)
        {
            refusals.Add(FormatSentences.TooManyProperties(formatName, sent.Properties, StrictModeBounds.Properties, path));
        }

        if (sent.Levels > StrictModeBounds.Levels && sent.DeepestPath is { } deepest)
        {
            refusals.Add(FormatSentences.TooDeep(formatName, sent.Levels, deepest, StrictModeBounds.Levels, path));
        }

        return refusals;
    }

    private static JsonObject Clean(JsonObject schema, string pointer, IReadOnlyList<string> strictRejects, List<string> notSent, int depth)
    {
        // Past the cap a schema is never copied as written (it would keep what the model must not
        // see): the over-deep part is sent as {}.
        if (depth > DepthCap)
        {
            return new JsonObject();
        }

        var result = new JsonObject();
        foreach (var (key, value) in schema)
        {
            // #1508 (R10): propertyOrder is resolve.json's alone, never sent, so sentFormatHash does not move.
            if (key is "title" or "$id" or "$schema" or "$defs" or "contentMediaType" or CompiledFormats.PropertyOrder
                || key.StartsWith("x-", StringComparison.Ordinal))
            {
                continue;
            }

            if (strictRejects.Contains(key))
            {
                notSent.Add(pointer + "/" + FormatDialect.Escape(key));
                continue;
            }

            switch (key)
            {
                case "properties" when value is JsonObject properties:
                    var map = new JsonObject();
                    foreach (var (name, child) in properties)
                    {
                        map[name] = child is JsonObject c
                            ? Clean(c, $"{pointer}/properties/{FormatDialect.Escape(name)}", strictRejects, notSent, depth + 1)
                            : child?.DeepClone();
                    }

                    result[key] = map;
                    break;
                case "items" when value is JsonObject item:
                    result[key] = Clean(item, pointer + "/items", strictRejects, notSent, depth + 1);
                    break;
                case "anyOf" when value is JsonArray members:
                    result[key] = new JsonArray(members.Select((m, i) => m is JsonObject o
                        ? Clean(o, $"{pointer}/anyOf/{i}", strictRejects, notSent, depth + 1)
                        : m?.DeepClone()).ToArray());
                    break;
                case "oneOf" when value is JsonArray arms && arms.Count > 0 && arms.All(a => a is JsonObject o && o.ContainsKey("const")):
                    result["enum"] = new JsonArray(arms.Select(a => a!["const"]?.DeepClone()).ToArray());
                    break;
                default:
                    result[key] = value?.DeepClone();
                    break;
            }
        }

        return result;
    }

    /// <summary>D3's counts on the sent copy, walked in document order.</summary>
    private sealed class Counter
    {
        public int Properties { get; private set; }

        public int Levels { get; private set; }

        public string? DeepestPath { get; private set; }

        public void Walk(JsonObject schema, string pointer, int level, int depth)
        {
            if (depth > DepthCap)
            {
                return;
            }

            if (IsObject(schema))
            {
                level++;
                Levels = Math.Max(Levels, level);
                if (level == StrictModeBounds.Levels + 1)
                {
                    DeepestPath ??= pointer;
                }
            }

            foreach (var (key, value) in schema)
            {
                switch (key)
                {
                    case "properties" when value is JsonObject properties:
                        foreach (var (name, child) in properties)
                        {
                            Properties++;
                            if (child is JsonObject c)
                            {
                                Walk(c, $"{pointer}/properties/{FormatDialect.Escape(name)}", level, depth + 1);
                            }
                        }

                        break;
                    case "items" when value is JsonObject item:
                        Walk(item, pointer + "/items", level, depth + 1);
                        break;
                    case "anyOf" when value is JsonArray members:
                        for (var i = 0; i < members.Count; i++)
                        {
                            if (members[i] is JsonObject m)
                            {
                                Walk(m, $"{pointer}/anyOf/{i}", level, depth + 1);
                            }
                        }

                        break;
                }
            }
        }

        private static bool IsObject(JsonObject schema) =>
            schema.ContainsKey("properties")
            || schema["type"] switch
            {
                JsonValue v => v.TryGetValue<string>(out var s) && s == "object",
                JsonArray a => a.Any(t => t is JsonValue v && v.TryGetValue<string>(out var s) && s == "object"),
                _ => false,
            };
    }
}
