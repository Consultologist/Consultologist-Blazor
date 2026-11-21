using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 07 § 2.4, 05-D47(1)): values held to <c>resolve.json</c>'s format bundle,
/// <c>{ "$defs": { "&lt;key&gt;": &lt;schema&gt;, … } }</c>. A bundle entry keeps its own <c>$id</c> while the references in
/// it are <c>#/$defs/&lt;key&gt;</c> of the bundle, so a standard validator given an entry resolves them against the entry;
/// <see cref="Expand"/> inlines every reference instead, and a value is validated against what it gives. The bundle is
/// read, never changed. Never throws on a bundle, a format or a value.
/// </summary>
public sealed class PlanFormats
{
    /// <summary>The deepest nesting <see cref="Expand"/> writes; past it the format is <c>false</c>.</summary>
    public const int MaxDepth = 64;

    /// <summary>The deepest JSON <see cref="Read"/> parses a reply to (the evaluator's own limit).</summary>
    public const int MaxReplyDepth = 512;

    private const string DefsPrefix = "#/$defs/";

    /// <summary>The most schemas <see cref="Expand"/> writes for one format (<see cref="FormatTypes.SchemaBudget"/>); past it, <c>false</c>.</summary>
    private const int Budget = FormatTypes.SchemaBudget;

    /// <summary>The <c>$id</c>s of the text formats (part 02): their reply is the text itself.</summary>
    private static readonly HashSet<string> TextIds = new(StringComparer.Ordinal)
    {
        "urn:consultologist:spec:31:format:plaintext",
        "urn:consultologist:spec:31:format:markdown_block",
        "urn:consultologist:spec:31:format:markdown_inline",
    };

    private static readonly CorvusFormatValueValidator Validator = new();

    private readonly JsonObject defs;

    /// <summary>The formats of <paramref name="bundle"/> (<c>resolve.json</c>'s <c>formats</c>); anything that is not a bundle has none.</summary>
    public PlanFormats(JsonNode? bundle)
    {
        defs = bundle is JsonObject { } root && root["$defs"] is JsonObject found ? found : new JsonObject();
    }

    /// <summary>
    /// <paramref name="format"/> with every <c>{ "$ref": "#/$defs/&lt;key&gt;" }</c> replaced by that entry of the bundle,
    /// <c>$id</c> and <c>$schema</c> dropped from each inlined entry (members beside a <c>$ref</c> are kept, the entry
    /// joining them through <c>allOf</c>), to <see cref="MaxDepth"/> levels. A reference to a key the bundle does not have,
    /// any other reference, a reference reached again inside itself (a cycle), or an expansion too deep or too large gives
    /// <c>false</c>, the schema nothing fits. A new tree: the bundle is not changed.
    /// </summary>
    public JsonNode Expand(JsonNode? format)
    {
        var walk = new Walk(defs);
        var expanded = walk.Schema(format, 0);
        return walk.Failed || expanded is null ? JsonValue.Create(false) : expanded;
    }

    /// <summary><paramref name="value"/> validated against <see cref="Expand"/> of <paramref name="format"/>.</summary>
    public FormatValueResult Validate(JsonNode? value, JsonNode? format) =>
        Validator.ValidateExpanded(value, Expand(format));

    /// <summary>
    /// Whether <paramref name="format"/> is a text format: a reference to a bundle entry whose <c>$id</c> is that of
    /// <c>plaintext</c>, <c>markdown_block</c> or <c>markdown_inline</c>.
    /// </summary>
    public bool IsText(JsonNode? format) =>
        Target(format) is { } entry && SourceLayout.Text(entry["$id"]) is { } id && TextIds.Contains(id);

    /// <summary>
    /// A model's <paramref name="reply"/> as a value of <paramref name="format"/>: for a text format the reply itself, as a
    /// string; otherwise the reply parsed as JSON (to <see cref="MaxReplyDepth"/> levels, a repeated member refused), with
    /// <c>Parsed</c> false when it is not.
    /// </summary>
    public (JsonNode? Value, bool Parsed) Read(string reply, JsonNode? format)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (IsText(format))
            return (JsonValue.Create(reply), true);

        try
        {
            var value = JsonNode.Parse(reply, documentOptions: new JsonDocumentOptions { MaxDepth = MaxReplyDepth });

            // A repeated member surfaces when an object is first read: read them all here.
            Touch(value);
            return (value, true);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            return (null, false);
        }
    }

    /// <summary>The bundle entry <paramref name="format"/> refers to, when it is exactly one <c>$ref</c> to a key there.</summary>
    private JsonObject? Target(JsonNode? format) =>
        format is JsonObject { Count: 1 } site && SourceLayout.Text(site["$ref"]) is { } reference
        && reference.StartsWith(DefsPrefix, StringComparison.Ordinal)
        && defs[reference[DefsPrefix.Length..]] is JsonObject entry
            ? entry
            : null;

    /// <summary>Reads every object of <paramref name="node"/>, iteratively, so that a repeated member throws here.</summary>
    private static void Touch(JsonNode? node)
    {
        var pending = new Stack<JsonNode?>();
        pending.Push(node);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case JsonObject obj:
                    foreach (var (_, child) in obj)
                        pending.Push(child);
                    break;
                case JsonArray array:
                    foreach (var child in array)
                        pending.Push(child);
                    break;
            }
        }
    }

    /// <summary>One expansion: the keys being inlined (to find a cycle), the schemas written, and whether it failed.</summary>
    private sealed class Walk(JsonObject defs)
    {
        private readonly HashSet<string> inlining = new(StringComparer.Ordinal);
        private int written;

        public bool Failed { get; private set; }

        public JsonNode? Schema(JsonNode? node, int depth)
        {
            if (Failed)
                return null;
            if (depth > MaxDepth || ++written > Budget)
                return Fail();

            switch (node)
            {
                case JsonObject obj:
                    return Object(obj, depth);
                case JsonArray array:
                {
                    var copy = new JsonArray();
                    foreach (var item in array)
                        copy.Add(Schema(item, depth + 1));
                    return Failed ? null : copy;
                }

                default:
                    return node?.DeepClone();
            }
        }

        private JsonNode? Object(JsonObject obj, int depth)
        {
            JsonNode? inlined = null;
            if (obj.ContainsKey("$ref"))
            {
                var reference = SourceLayout.Text(obj["$ref"]);
                if (reference is null || !reference.StartsWith(DefsPrefix, StringComparison.Ordinal))
                    return Fail();
                var key = reference[DefsPrefix.Length..];
                if (defs[key] is not JsonObject entry || !inlining.Add(key))
                    return Fail();

                var target = new JsonObject();
                foreach (var (name, value) in entry)
                {
                    if (name is "$id" or "$schema")
                        continue;
                    target[name] = Schema(value, depth + 1);
                }

                inlining.Remove(key);
                if (Failed)
                    return null;
                if (obj.Count == 1)
                    return target;
                inlined = target;
            }

            var copy = new JsonObject();
            foreach (var (name, value) in obj)
            {
                if (name == "$ref")
                    continue;

                // A member's value is a schema, a list or map of schemas, or an annotation: each is copied, its references inlined.
                copy[name] = Schema(value, depth + 1);
            }

            if (Failed)
                return null;
            if (inlined != null)
            {
                if (copy["allOf"] is JsonArray all)
                    all.Add(inlined);
                else
                    copy["allOf"] = new JsonArray(inlined);
            }

            return copy;
        }

        private JsonNode? Fail()
        {
            Failed = true;
            return null;
        }
    }
}
