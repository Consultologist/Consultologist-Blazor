using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>#1500 (specVersion 31, area 03): what a format is, read through its names and <c>#/$defs</c>.</summary>
public enum FormatShape
{
    /// <summary>Not a format this set can read: an unknown name, a loop, or not a schema.</summary>
    Unknown,

    /// <summary>A <c>string</c> with no annotation, enum or <c>format</c>.</summary>
    String,

    /// <summary>A string with <c>enum</c> or a labelled <c>oneOf</c> (part 02 § 9).</summary>
    Enum,

    /// <summary>A <c>number</c>.</summary>
    Number,

    /// <summary>An <c>integer</c>.</summary>
    Integer,

    /// <summary>A <c>boolean</c>.</summary>
    Boolean,

    /// <summary>A string with <c>format: "date"</c>.</summary>
    Date,

    /// <summary>A string with <c>contentMediaType</c>: a text format.</summary>
    Text,

    /// <summary>An object.</summary>
    Object,

    /// <summary>An array: a list.</summary>
    List,

    /// <summary>An <c>anyOf</c> that is more than one type and <c>null</c>.</summary>
    Union,

    /// <summary><c>{ "type": "null" }</c>.</summary>
    Null,
}

/// <summary>
/// #1500 (specVersion 31, area 03): the two comparisons of part 02 § 2.3 and their hashes (part 07
/// § 2.4). <b>Type equality</b> compares <see cref="TypeForm"/>s (D9): annotations dropped,
/// references by name, unions and <c>required</c> sorted, a built-in's name equal to its schema;
/// <see cref="TypeHash"/> hashes it. <b>Full equality</b> compares <see cref="FullForm"/>s (D8):
/// every reference inlined, every keyword kept; <see cref="FormatHash"/> hashes it without
/// <c>$id</c>. Also one-way fit, and what a format is (<see cref="Shape"/>,
/// <see cref="IsNullable"/>, <see cref="TextKind"/>).
/// <para>
/// Every method takes a format as written (a name or a schema) that may not have passed the
/// dialect checker, and returns for any <see cref="JsonNode"/>: an unknown name, a loop through
/// names or <c>#/$defs</c>, or a value that is no schema at all. No walk goes deeper than
/// <see cref="SourceBounds.JsonDepth"/> schemas or past <see cref="NodeBudget"/> of them; what it
/// cannot read it leaves as written.
/// </para>
/// </summary>
public static class FormatTypes
{
    /// <summary>
    /// The most schemas one call walks. Inlining references copies a format once per place it is
    /// referenced, so a chain of formats each referring to the next twice would otherwise write
    /// 2^n copies; past this, references are left as written.
    /// </summary>
    internal const int NodeBudget = 10_000;

    /// <summary>
    /// The type form (D9): <c>title</c>, <c>description</c>, <c>$schema</c>, <c>$id</c> and
    /// <c>$defs</c> dropped, <c>#/$defs</c> references inlined, a name written <c>{ "$ref": name }</c>,
    /// <c>string</c>, <c>number</c>, <c>boolean</c> and <c>date</c> written as references to the
    /// built-ins, a nullable type array written as an <c>anyOf</c> with <c>{ "type": "null" }</c>,
    /// <c>anyOf</c> members sorted by their canonical JSON and de-duplicated, <c>required</c> sorted.
    /// A name, or a <c>$ref</c> with only annotations, gives its schema's type form; an unknown
    /// name gives <c>{ "$ref": name }</c>; anything that is not a schema is returned as written
    /// (a copy; null for null).
    /// </summary>
    public static JsonNode? TypeForm(JsonNode? format, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        var walk = new Walk(formats, keepId: false);
        if (FormatSet.NameOf(format) is { } name)
        {
            return formats.Find(name)?.Schema is { } schema ? walk.Type(schema, Defs(schema), 0) : Ref(name);
        }

        return walk.Type(format, Defs(format), 0);
    }

    /// <summary>The hash of <see cref="TypeForm"/>: what the record and <c>resolve.json</c> call <c>typeHash</c>.</summary>
    public static string TypeHash(JsonNode? format, FormatSet formats) => CanonicalJson.Hash(TypeForm(format, formats));

    /// <summary>
    /// The full form (D8): every reference inlined, every keyword kept. A <c>$ref</c> site becomes
    /// its target's members without <c>$schema</c>, <c>$id</c> and <c>$defs</c>, with the site's
    /// <c>title</c> in place of the target's and the descriptions joined <c>"&lt;target&gt;\n&lt;site&gt;"</c>;
    /// <c>$defs</c> are dropped once inlined; a nullable type array is written type first. A name
    /// at the root gives its schema with its <c>$schema</c> and <c>$id</c>. A reference that is
    /// unknown, or that reaches a format already being inlined, is left as written.
    /// </summary>
    public static JsonNode? FullForm(JsonNode? format, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        return new Walk(formats, keepId: true).Full(format, Defs(format), 0, root: true);
    }

    /// <summary>The hash of <see cref="FullForm"/> without <c>$id</c>: what the record and <c>resolve.json</c> call <c>formatHash</c>.</summary>
    public static string FormatHash(JsonNode? format, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        return CanonicalJson.Hash(new Walk(formats, keepId: false).Full(format, Defs(format), 0, root: true));
    }

    /// <summary>Type equality: the two <see cref="TypeForm"/>s write the same canonical JSON.</summary>
    public static bool Same(JsonNode? a, JsonNode? b, FormatSet formats) =>
        CanonicalJson.Write(TypeForm(a, formats)) == CanonicalJson.Write(TypeForm(b, formats));

    /// <summary>
    /// Whether a value of format <paramref name="value"/> may stand where <paramref name="expected"/>
    /// is: <see cref="Same"/>; or <c>integer</c> for <c>number</c>; a text format for <c>string</c>;
    /// a format for a union with a member it fits (so <c>T</c> for <c>T | null</c>); a union whose
    /// every member fits; <c>list&lt;A&gt;</c> for <c>list&lt;B&gt;</c> when A fits B. Never the reverse.
    /// </summary>
    public static bool Fits(JsonNode? value, JsonNode? expected, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        return new Fitter(formats).Fit(TypeForm(value, formats), TypeForm(expected, formats), 0);
    }

    /// <summary><c>{ "type": "array", "items": item }</c>, a name written as <c>{ "$ref": name }</c>.</summary>
    public static JsonNode ListOf(JsonNode item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new JsonObject { ["type"] = "array", ["items"] = AsSchema(item) };
    }

    /// <summary>
    /// The union of <paramref name="members"/> (part 02 § 2.2): the first, as written, when all are
    /// <see cref="Same"/>; otherwise <c>{ "anyOf": [...] }</c> of the members that are not the same
    /// as an earlier one, in the order given, a name written as <c>{ "$ref": name }</c>.
    /// </summary>
    public static JsonNode UnionOf(IReadOnlyList<JsonNode> members, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(formats);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var distinct = members
            .Where(m => m is not null && seen.Add(CanonicalJson.Write(TypeForm(m, formats))))
            .ToList();
        return distinct.Count == 1
            ? distinct[0].DeepClone()
            : new JsonObject { ["anyOf"] = new JsonArray(distinct.Select(AsSchema).ToArray()) };
    }

    /// <summary>
    /// What <paramref name="format"/> is, through names and <c>#/$defs</c>: <see cref="FormatShape.Text"/>
    /// for a string with <c>contentMediaType</c>, <see cref="FormatShape.Enum"/> for one with <c>enum</c>
    /// or <c>oneOf</c>, <see cref="FormatShape.Date"/> for <c>format: date</c>. A nullable type array,
    /// or an <c>anyOf</c> of one type and <c>null</c>, is that type's shape (<see cref="IsNullable"/>
    /// says the rest); any other <c>anyOf</c> is a <see cref="FormatShape.Union"/>.
    /// </summary>
    public static FormatShape Shape(JsonNode? format, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        return ShapeOf(format, Defs(format), formats, 0);
    }

    /// <summary>Whether <paramref name="format"/> admits <c>null</c>: a type array or <c>type</c> with <c>null</c>, or an <c>anyOf</c> with such a member.</summary>
    public static bool IsNullable(JsonNode? format, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        var (schema, defs) = Follow(format, Defs(format), formats);
        if (schema?["anyOf"] is JsonArray members)
        {
            return members.Any(m => Follow(m, defs, formats).Schema is { } member && AdmitsNull(member));
        }

        return schema != null && AdmitsNull(schema);
    }

    /// <summary><c>plaintext</c>, <c>markdown_block</c> or <c>markdown_inline</c> by the annotations at the root <paramref name="format"/> resolves to; otherwise null.</summary>
    public static string? TextKind(JsonNode? format, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        return KindOf(Follow(format, Defs(format), formats).Schema);
    }

    /// <summary>The text kind <paramref name="schema"/>'s own <c>contentMediaType</c> and <c>x-markdown</c> give.</summary>
    internal static string? KindOf(JsonObject? schema) => Text(schema?["contentMediaType"]) switch
    {
        "text/plain" => "plaintext",
        "text/markdown" => Text(schema!["x-markdown"]) switch
        {
            "block" => "markdown_block",
            "inline" => "markdown_inline",
            _ => null,
        },
        _ => null,
    };

    /// <summary>The <c>$defs</c> object at the root of <paramref name="node"/>, if any.</summary>
    internal static JsonObject? Defs(JsonNode? node) => (node as JsonObject)?["$defs"] as JsonObject;

    internal static string? Text(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    /// <summary>The one type of a type array that is exactly one type and <c>"null"</c>, in either order; otherwise null.</summary>
    internal static string? NullablePair(JsonArray pair) =>
        pair is [var a, var b] && Text(a) is { } x && Text(b) is { } y && (x == "null") != (y == "null")
            ? (x == "null" ? y : x)
            : null;

    /// <summary>Whether <paramref name="obj"/> holds nothing beside its <c>$ref</c> but <c>title</c> and <c>description</c>.</summary>
    internal static bool OnlyAnnotationsBeside(JsonObject obj) =>
        obj.All(p => p.Key is "$ref" or "title" or "description");

    private static JsonObject Ref(string name) => new() { ["$ref"] = name };

    private static JsonNode? AsSchema(JsonNode? node) =>
        node is JsonValue && FormatSet.NameOf(node) is { } name ? Ref(name) : node?.DeepClone();

    private static bool AdmitsNull(JsonObject schema) =>
        Text(schema["type"]) == "null" || (schema["type"] is JsonArray pair && NullablePair(pair) != null);

    /// <summary>
    /// The schema <paramref name="node"/> is, following names and <c>#/$defs</c> sites (with only
    /// annotations beside the <c>$ref</c>), and the <c>$defs</c> its references read; null when a
    /// reference is unknown, or loops, or the node is no schema.
    /// </summary>
    private static (JsonObject? Schema, JsonObject? Defs) Follow(JsonNode? node, JsonObject? defs, FormatSet formats)
    {
        var seen = new HashSet<JsonNode>(ReferenceEqualityComparer.Instance);
        while (true)
        {
            JsonNode? target;
            if (FormatSet.NameOf(node) is { } name)
            {
                target = formats.Find(name)?.Schema;
                defs = Defs(target);
            }
            else if (node is JsonObject site && Text(site["$ref"]) is { } reference && OnlyAnnotationsBeside(site)
                && FormatDialect.DefinitionName(reference) is { } definition)
            {
                target = defs?[definition];
            }
            else
            {
                return (node as JsonObject, defs);
            }

            if (target is null || !seen.Add(target))
            {
                return (null, null);
            }

            node = target;
        }
    }

    private static FormatShape ShapeOf(JsonNode? node, JsonObject? defs, FormatSet formats, int depth)
    {
        var (schema, inner) = Follow(node, defs, formats);
        if (schema is null || depth > SourceBounds.JsonDepth)
        {
            return FormatShape.Unknown;
        }

        if (schema["anyOf"] is JsonArray members)
        {
            var nulls = 0;
            var others = new List<JsonNode?>();
            foreach (var member in members)
            {
                if (Follow(member, inner, formats).Schema is { } m && Text(m["type"]) == "null")
                {
                    nulls++;
                }
                else
                {
                    others.Add(member);
                }
            }

            return (others.Count, nulls) switch
            {
                (0, 0) => FormatShape.Unknown,
                (0, _) => FormatShape.Null,
                (1, > 0) => ShapeOf(others[0], inner, formats, depth + 1),
                _ => FormatShape.Union,
            };
        }

        var type = schema["type"] is JsonArray pair ? NullablePair(pair) : Text(schema["type"]);
        return type switch
        {
            "string" when schema.ContainsKey("contentMediaType") => FormatShape.Text,
            "string" when schema.ContainsKey("enum") || schema.ContainsKey("oneOf") => FormatShape.Enum,
            "string" when Text(schema["format"]) == "date" => FormatShape.Date,
            "string" => FormatShape.String,
            "number" => FormatShape.Number,
            "integer" => FormatShape.Integer,
            "boolean" => FormatShape.Boolean,
            "object" => FormatShape.Object,
            "array" => FormatShape.List,
            "null" => FormatShape.Null,
            _ => FormatShape.Unknown,
        };
    }

    /// <summary>One call's walk: the formats being inlined (so a loop stops) and what is left of the budget.</summary>
    private sealed class Walk(FormatSet formats, bool keepId)
    {
        private readonly HashSet<JsonNode> open = new(ReferenceEqualityComparer.Instance);
        private int budget = NodeBudget;

        private bool Spend() => budget-- > 0;

        /// <summary>The type form of one schema; <paramref name="defs"/> are its file's.</summary>
        public JsonNode? Type(JsonNode? node, JsonObject? defs, int depth)
        {
            if (node is JsonValue && FormatSet.NameOf(node) is { } name)
            {
                return Ref(name);
            }

            if (node is not JsonObject obj || depth > SourceBounds.JsonDepth || !Spend())
            {
                return node?.DeepClone();
            }

            if (Text(obj["$ref"]) is { } reference && OnlyAnnotationsBeside(obj))
            {
                if (FormatSet.NameOf(obj) is { } refName)
                {
                    return Ref(refName);
                }

                if (FormatDialect.DefinitionName(reference) is { } definition && defs?[definition] is { } target && open.Add(target))
                {
                    var inlined = Type(target, defs, depth + 1);
                    open.Remove(target);
                    return inlined;
                }

                return Ref(reference);
            }

            if (obj["type"] is JsonArray pair && NullablePair(pair) is { } single)
            {
                return new JsonObject { ["anyOf"] = Sorted([TypeObject(obj, defs, depth, single), new JsonObject { ["type"] = "null" }]) };
            }

            return TypeObject(obj, defs, depth, null);
        }

        /// <summary>
        /// The type form of an object schema's keywords; with <paramref name="nullableOf"/>, the
        /// non-null half of a nullable type array: that type, its enum without <c>null</c>, its
        /// labelled enum without the <c>{ "const": null }</c> arm.
        /// </summary>
        private JsonNode TypeObject(JsonObject obj, JsonObject? defs, int depth, string? nullableOf)
        {
            var form = new JsonObject();
            foreach (var (key, value) in obj)
            {
                switch (key)
                {
                    case "title" or "description" or "$schema" or "$id" or "$defs":
                        break;
                    case "type" when nullableOf != null:
                        form[key] = nullableOf;
                        break;
                    case "properties" when value is JsonObject properties:
                        var map = new JsonObject();
                        foreach (var (property, schema) in properties)
                        {
                            map[property] = Type(schema, defs, depth + 1);
                        }

                        form[key] = map;
                        break;
                    case "items":
                        form[key] = Type(value, defs, depth + 1);
                        break;
                    case "anyOf" when value is JsonArray members:
                        form[key] = Sorted(members.Select(m => Type(m, defs, depth + 1)));
                        break;
                    case "required" when value is JsonArray names:
                        form[key] = new JsonArray(names
                            .Select(n => n?.DeepClone())
                            .OrderBy(CanonicalJson.Write, StringComparer.Ordinal)
                            .ToArray());
                        break;
                    case "enum" when nullableOf != null && value is JsonArray values:
                        form[key] = new JsonArray(values.Where(v => v is not null).Select(v => v!.DeepClone()).ToArray());
                        break;
                    case "oneOf" when value is JsonArray arms:
                        form[key] = new JsonArray(arms
                            .Where(a => nullableOf == null || !IsNullArm(a))
                            .Select(a => a is JsonObject arm ? WithoutAnnotations(arm) : a?.DeepClone())
                            .ToArray());
                        break;
                    default:
                        form[key] = value?.DeepClone();
                        break;
                }
            }

            // A built-in's name equals its schema (§ 2.3); integer has no built-in.
            if (form.Count == 1 && Text(form["type"]) is { } type && type is "string" or "number" or "boolean")
            {
                return Ref(type);
            }

            if (form.Count == 2 && Text(form["type"]) == "string" && Text(form["format"]) == "date")
            {
                return Ref("date");
            }

            return form;
        }

        /// <summary>
        /// The full form of one schema; <paramref name="root"/> keeps <c>$schema</c> (and <c>$id</c>
        /// when the walk keeps ids), which below the root, and on a referenced format, are dropped.
        /// </summary>
        public JsonNode? Full(JsonNode? node, JsonObject? defs, int depth, bool root)
        {
            if (depth > SourceBounds.JsonDepth || !Spend())
            {
                return node?.DeepClone();
            }

            if (node is JsonValue)
            {
                return FormatSet.NameOf(node) is { } name && formats.Find(name)?.Schema is { } named
                    ? Site(null, named, Defs(named), depth, root) ?? node.DeepClone()
                    : node.DeepClone();
            }

            if (node is not JsonObject obj)
            {
                return node?.DeepClone();
            }

            if (Text(obj["$ref"]) is { } reference && OnlyAnnotationsBeside(obj))
            {
                JsonObject? target = null;
                var targetDefs = defs;
                if (FormatSet.NameOf(obj) is { } refName)
                {
                    target = formats.Find(refName)?.Schema;
                    targetDefs = Defs(target);
                }
                else if (FormatDialect.DefinitionName(reference) is { } definition)
                {
                    target = defs?[definition] as JsonObject;
                }

                return (target is null ? null : Site(obj, target, targetDefs, depth, root)) ?? obj.DeepClone();
            }

            var full = new JsonObject();
            foreach (var (key, value) in obj)
            {
                switch (key)
                {
                    case "$defs":
                    case "$id" when !(root && keepId):
                    case "$schema" when !root:
                        break;
                    case "properties" when value is JsonObject properties:
                        var map = new JsonObject();
                        foreach (var (property, schema) in properties)
                        {
                            map[property] = Full(schema, defs, depth + 1, false);
                        }

                        full[key] = map;
                        break;
                    case "items":
                        full[key] = Full(value, defs, depth + 1, false);
                        break;
                    case "anyOf" when value is JsonArray members:
                        full[key] = new JsonArray(members.Select(m => Full(m, defs, depth + 1, false)).ToArray());
                        break;
                    case "type" when value is JsonArray pair && NullablePair(pair) is { } single:
                        full[key] = new JsonArray(single, "null");
                        break;
                    default:
                        full[key] = value?.DeepClone();
                        break;
                }
            }

            return full;
        }

        /// <summary>A reference site inlined (D8): the target's full form, then the site's title and description; null when the target is already being inlined.</summary>
        private JsonObject? Site(JsonObject? site, JsonObject target, JsonObject? targetDefs, int depth, bool root)
        {
            if (!open.Add(target))
            {
                return null;
            }

            var merged = Full(target, targetDefs, depth + 1, root) as JsonObject;
            open.Remove(target);
            if (merged is null || site is null)
            {
                return merged;
            }

            if (site["title"] is { } title)
            {
                merged["title"] = title.DeepClone();
            }

            if (site["description"] is { } description)
            {
                merged["description"] = Text(merged["description"]) is { } first && Text(description) is { } second
                    ? $"{first}\n{second}"
                    : description.DeepClone();
            }

            return merged;
        }

        private static bool IsNullArm(JsonNode? arm) => arm is JsonObject o && o.ContainsKey("const") && o["const"] is null;

        private static JsonObject WithoutAnnotations(JsonObject obj)
        {
            var copy = new JsonObject();
            foreach (var (key, value) in obj)
            {
                if (key is not ("title" or "description"))
                {
                    copy[key] = value?.DeepClone();
                }
            }

            return copy;
        }

        private static JsonArray Sorted(IEnumerable<JsonNode?> members)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            return new JsonArray(members
                .Select(m => (Node: m, Text: CanonicalJson.Write(m)))
                .Where(m => seen.Add(m.Text))
                .OrderBy(m => m.Text, StringComparer.Ordinal)
                .Select(m => m.Node)
                .ToArray());
        }
    }

    /// <summary>One <see cref="Fits"/> call: type forms compared, names opened one level at a time, each pair judged once.</summary>
    private sealed class Fitter(FormatSet formats)
    {
        private readonly Dictionary<(string, string), bool> judged = [];

        public bool Fit(JsonNode? value, JsonNode? expected, int depth)
        {
            var key = (CanonicalJson.Write(value), CanonicalJson.Write(expected));
            if (key.Item1 == key.Item2)
            {
                return true;
            }

            if (depth > SourceBounds.JsonDepth)
            {
                return false;
            }

            if (judged.TryGetValue(key, out var known))
            {
                return known;
            }

            // While a pair is being judged it does not fit, so a loop ends.
            judged[key] = false;
            var fits = Judge(Open(value), Open(expected), depth);
            judged[key] = fits;
            return fits;
        }

        private bool Judge(JsonNode? value, JsonNode? expected, int depth)
        {
            if (CanonicalJson.Write(value) == CanonicalJson.Write(expected))
            {
                return true;
            }

            if (Members(expected) is { } wanted)
            {
                return Members(value) is { } given
                    ? given.All(g => wanted.Any(w => Fit(g, w, depth + 1)))
                    : wanted.Any(w => Fit(value, w, depth + 1));
            }

            if (Members(value) is { } members)
            {
                return members.All(m => Fit(m, expected, depth + 1));
            }

            switch (RefName(expected))
            {
                case "number":
                    return value is JsonObject { Count: 1 } integer && Text(integer["type"]) == "integer";
                case "string":
                    return KindOf(value as JsonObject) != null;
            }

            return Items(value) is { } given2 && Items(expected) is { } wanted2 && Fit(given2, wanted2, depth + 1);
        }

        /// <summary>A type form's <c>{ "$ref": name }</c> opened to its schema's type form, one level.</summary>
        private JsonNode? Open(JsonNode? form) =>
            RefName(form) is { } name && formats.Find(name) != null ? TypeForm(JsonValue.Create(name), formats) : form;

        private static string? RefName(JsonNode? form) =>
            form is JsonObject { Count: 1 } obj ? FormatSet.NameOf(obj) : null;

        private static JsonArray? Members(JsonNode? form) =>
            form is JsonObject { Count: 1 } obj && obj["anyOf"] is JsonArray { Count: > 0 } members ? members : null;

        private static JsonNode? Items(JsonNode? form) =>
            form is JsonObject { Count: 2 } obj && Text(obj["type"]) == "array" && obj.ContainsKey("items") ? obj["items"] : null;
    }
}
