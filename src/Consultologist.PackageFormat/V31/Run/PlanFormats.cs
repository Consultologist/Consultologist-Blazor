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

    /// <summary>
    /// The most JSON nodes (objects, lists and values) <see cref="Expand"/> writes for one format; past it, <c>false</c>.
    /// The schema budget counts schemas, not what their keywords hold, so a forged document can repeat one very large
    /// <c>enum</c> from thousands of places within it. The bound is half of <see cref="SourceBounds.TotalBytes"/>: a
    /// JSON node takes two bytes of a file at least, so this is as many nodes as a whole package source can hold, and
    /// an expansion that writes every node of every format of the largest source once stays within it.
    /// </summary>
    public const int NodeBudget = SourceBounds.TotalBytes / 2;

    private const string DefsPrefix = "#/$defs/";

    /// <summary>
    /// The most schemas <see cref="Expand"/> writes from the bundle's entries for one format, and the most of the format's
    /// own (<see cref="FormatTypes.SchemaBudget"/>, counted as publish counts them); past either, <c>false</c>.
    /// </summary>
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
    /// <c>$id</c>, <c>$schema</c> and <c>$defs</c> dropped from each inlined entry (members beside a <c>$ref</c> are kept,
    /// the entry joining them through <c>allOf</c>), to <see cref="MaxDepth"/> levels. Inside an inlined entry a reference
    /// is first one of the entry's own <c>$defs</c> (its format file's definitions, which the bundle keeps as written,
    /// part 02 § 3.1), then the bundle's entry of that key. A reference to a key neither has,
    /// any other reference, a reference reached again inside itself (a cycle), or an expansion too deep or too large gives
    /// <c>false</c>, the schema nothing fits. Too large is past <see cref="FormatTypes.SchemaBudget"/> schemas, counted as
    /// publish counts them (A03-011), so a format file publish accepted is not too large here, or past
    /// <see cref="NodeBudget"/> nodes written in all. A new tree: the bundle is not changed.
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

    /// <summary>
    /// One expansion: the entries and definitions being inlined (to find a cycle), the schemas written, and whether it failed. The schemas
    /// are counted as publish counts them for A03-011 (<see cref="FormatTypes.Expands"/>), so a format publish accepted
    /// expands here: a schema is the format itself, a value of <c>properties</c>, <c>items</c>, a member of
    /// <c>anyOf</c>, and each bundle entry inlined (the reference written in its place is not one). Any other member's
    /// value (a keyword's, an annotation's, an <c>enum</c>'s list) is copied uncounted, as publish copies it; a reference
    /// met there is inlined all the same, and its entry counted, so every inlining spends the budget. The schemas the
    /// format writes itself, around the entries it refers to, are counted apart from the entries': publish holds each
    /// format file to the budget, not the place that refers to it. What the uncounted values hold is bounded by
    /// <see cref="NodeBudget"/>, which counts every node written.
    /// </summary>
    private sealed class Walk(JsonObject defs)
    {
        private readonly HashSet<JsonObject> inlining = new(ReferenceEqualityComparer.Instance);

        /// <summary>The <c>$defs</c> of the bundle entry being inlined, its file's own definitions; null outside one, or when it has none.</summary>
        private JsonObject? local;
        private int written;
        private int own;
        private int nodes;

        public bool Failed { get; private set; }

        /// <summary><paramref name="node"/> where a schema is: counted when it is one written here (an object that is not a reference).</summary>
        public JsonNode? Schema(JsonNode? node, int depth)
        {
            if (Failed)
                return null;
            if (node is JsonObject schema && !schema.ContainsKey("$ref") && !Counted())
                return Fail();
            return Copy(node, depth);
        }

        /// <summary>One more schema, within the budget: an entry's, or one of the format's own.</summary>
        private bool Counted() => (inlining.Count > 0 ? ++written : ++own) <= Budget;

        /// <summary>One more node written, within <see cref="NodeBudget"/>.</summary>
        private bool Node() => ++nodes <= NodeBudget;

        /// <summary><paramref name="node"/> copied, the references in it inlined, to <see cref="MaxDepth"/> and <see cref="NodeBudget"/>.</summary>
        private JsonNode? Copy(JsonNode? node, int depth)
        {
            if (Failed)
                return null;
            if (depth > MaxDepth || !Node())
                return Fail();

            switch (node)
            {
                case JsonObject obj:
                    return Object(obj, depth);
                case JsonArray array:
                {
                    var copy = new JsonArray();
                    foreach (var item in array)
                        copy.Add(Copy(item, depth + 1));
                    return Failed ? null : copy;
                }

                default:
                    return node?.DeepClone();
            }
        }

        /// <summary>The value of an object's member <paramref name="name"/>, at <paramref name="depth"/>: the schemas publish counts, counted.</summary>
        private JsonNode? Member(string name, JsonNode? value, int depth)
        {
            switch (name)
            {
                case "properties" when value is JsonObject properties && depth <= MaxDepth:
                {
                    if (!Node())
                        return Fail();
                    var map = new JsonObject();
                    foreach (var (property, schema) in properties)
                        map[property] = Schema(schema, depth + 1);
                    return Failed ? null : map;
                }

                case "items":
                    return Schema(value, depth);
                case "anyOf" when value is JsonArray members && depth <= MaxDepth:
                {
                    if (!Node())
                        return Fail();
                    var list = new JsonArray();
                    foreach (var member in members)
                        list.Add(Schema(member, depth + 1));
                    return Failed ? null : list;
                }

                default:
                    return Copy(value, depth);
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

                // The dialect resolves a reference against the file it is written in (part 02 § 3.1), and the bundle keeps a
                // file's own definitions as written: the entry's own first, then the bundle's entry of that key.
                var inFile = local != null && local.ContainsKey(key);
                if ((inFile ? local![key] : defs[key]) is not JsonObject entry || !inlining.Add(entry) || !Counted())
                    return Fail();

                // A bundle entry's references are its own file's; a definition of the file stays in the file.
                var outer = local;
                if (!inFile)
                    local = entry["$defs"] as JsonObject;
                var target = new JsonObject();
                foreach (var (name, value) in entry)
                {
                    if (name is "$id" or "$schema" or "$defs")
                        continue;
                    target[name] = Member(name, value, depth + 1);
                }

                local = outer;
                inlining.Remove(entry);
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
                copy[name] = Member(name, value, depth + 1);
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
