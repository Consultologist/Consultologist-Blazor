using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1500 (specVersion 31, area 03): where a schema the dialect checker reads is written, which
/// decides the rules of its root and whose names it holds to the id pattern (part 02 § 3.1, § 5.3).
/// </summary>
public enum SchemaSite
{
    /// <summary>A file of a package's (or a function's) <c>formats/</c>.</summary>
    PackageFormat,

    /// <summary>A built-in or catalogue format file: the deployment's, not an author's.</summary>
    RegistryFormat,

    /// <summary>A schema written inside a declaration (an input's, a node's, a parameter's): part of its file, with no root keywords of its own.</summary>
    Inline,
}

/// <summary>
/// #1500 (specVersion 31, area 03): one schema against the formats dialect (part 02 § 3.1–3.3,
/// § 5.3): the keywords it may use, their JSON types, where each sits, and the shape of objects,
/// lists and unions, at every depth, in document order, every refusal reported. It reads one
/// schema document on its own: a named <c>$ref</c> is never looked up (that is
/// <see cref="FormatSet"/>'s), while <c>#/$defs/&lt;name&gt;</c> is resolved against the file's
/// root <c>$defs</c>, each definition walked as a schema. The root's own keywords (<c>$schema</c>,
/// <c>$id</c>, <c>$defs</c>, <c>contentMediaType</c>, <c>x-markdown</c>) and the contents of
/// <c>enum</c> and <c>oneOf</c> are judged by the enum and root rules (<c>FormatDialect.Enums.cs</c>).
/// </summary>
public static partial class FormatDialect
{
    /// <summary>The dialect's id, the <c>$schema</c> of every format file (part 02 § 3).</summary>
    internal const string DialectId = "urn:consultologist:spec:31:dialect";

    private const string DefsPrefix = "#/$defs/";

    /// <summary>The seven JSON types a <c>type</c> may name.</summary>
    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "string", "number", "integer", "boolean", "object", "array", "null",
    };

    /// <summary>§ 3.1's keywords any schema may write. <c>const</c> lives only in a labelled enum's branches.</summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "type", "properties", "required", "additionalProperties", "items", "enum", "oneOf", "anyOf",
        "$ref", "title", "description", "format",
    };

    /// <summary>The keywords a format file writes at its root alone; below the root they are S02-021.</summary>
    private static readonly HashSet<string> RootKeywords = new(StringComparer.Ordinal)
    {
        "$schema", "$id", "$defs", "contentMediaType", "x-markdown",
    };

    private static readonly HashSet<string> Scalars = new(StringComparer.Ordinal) { "string", "number", "integer", "boolean" };

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>How deep <see cref="Json"/> prints before writing a container as <c>…</c>.</summary>
    private const int PrintDepth = 32;

    /// <summary>
    /// The refusals of <paramref name="schema"/>, empty when it is in the dialect. <paramref name="subject"/>
    /// is D11's <c>&lt;Format&gt;</c> (<c>Format 'verdict'</c>, <c>Input 'sections'</c>); <paramref name="id"/>
    /// the bare id the root's sentences print; <paramref name="path"/> the file the refusals carry.
    /// A schema written as text is a format name and is left to the lookup; any other value that is
    /// not an object is refused as the <c>format</c> it was written as.
    /// </summary>
    public static IReadOnlyList<SourceRefusal> Check(JsonNode? schema, SchemaSite site, string subject, string id, string path)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(path);

        if (schema is JsonValue name && name.GetValueKind() == JsonValueKind.String)
        {
            return [];
        }

        if (schema is not JsonObject root)
        {
            return [FormatSentences.KeywordWrongType(subject, "", "format", Describe(schema), "an object", path)];
        }

        var defs = site == SchemaSite.Inline ? null : root["$defs"] as JsonObject;
        var walk = new Walk(site, subject, id, path, defs);
        walk.Schema(root, "", 1, isRoot: true);
        return walk.Refusals;
    }

    /// <summary>A03-003's <c>&lt;what it is&gt;</c>, in A02-001's words.</summary>
    private static string Describe(JsonNode? value) => value switch
    {
        null => "null",
        JsonArray => "a list",
        JsonObject => "an object",
        _ => value.GetValueKind() switch
        {
            JsonValueKind.String => "text",
            JsonValueKind.True or JsonValueKind.False => "true or false",
            JsonValueKind.Number => "a number",
            _ => "null",
        },
    };

    /// <summary>
    /// <paramref name="value"/> as compact JSON in written order; with <paramref name="hideText"/> every
    /// string value is <c>"…"</c> (S02-051). It never throws: a container deeper than
    /// <see cref="PrintDepth"/>, or a value no writer can write, prints as <c>…</c>.
    /// </summary>
    private static string Json(JsonNode? value, bool hideText = false)
    {
        var text = new System.Text.StringBuilder();
        Print(value, 0);
        return text.ToString();

        void Print(JsonNode? node, int depth)
        {
            switch (node)
            {
                case null:
                    text.Append("null");
                    break;
                case JsonObject or JsonArray when depth >= PrintDepth:
                    text.Append('…');
                    break;
                case JsonObject members:
                    text.Append('{');
                    var first = true;
                    foreach (var (key, member) in members)
                    {
                        text.Append(first ? "" : ",").Append(JsonSerializer.Serialize(key, Compact)).Append(':');
                        first = false;
                        Print(member, depth + 1);
                    }

                    text.Append('}');
                    break;
                case JsonArray items:
                    text.Append('[');
                    for (var i = 0; i < items.Count; i++)
                    {
                        text.Append(i == 0 ? "" : ",");
                        Print(items[i], depth + 1);
                    }

                    text.Append(']');
                    break;
                default:
                    text.Append(Scalar(node));
                    break;
            }
        }

        string Scalar(JsonNode node)
        {
            try
            {
                return hideText && node.GetValueKind() == JsonValueKind.String ? "\"…\"" : node.ToJsonString(Compact);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or NotSupportedException or FormatException)
            {
                return "…";
            }
        }
    }

    private static string? Text(JsonNode? value) =>
        value is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    internal static string Escape(string name) => name.Replace("~", "~0").Replace("/", "~1");

    /// <summary>
    /// What a <c>type</c> declares: one of the seven, or one of them plus <c>"null"</c> in either
    /// order (<see cref="Nullable"/>). <see cref="Valid"/> is false when it is neither; an absent
    /// <c>type</c> is valid and declares nothing.
    /// </summary>
    private readonly record struct Declared(string? Type, bool Nullable, bool Valid)
    {
        public static Declared Of(JsonNode? type)
        {
            if (type is null)
                return new(null, false, true);
            if (Text(type) is { } one)
                return Types.Contains(one) ? new(one, false, true) : new(null, false, false);
            if (type is JsonArray { Count: 2 } pair && Text(pair[0]) is { } a && Text(pair[1]) is { } b
                && Types.Contains(a) && Types.Contains(b) && (a == "null") != (b == "null"))
                return new(a == "null" ? b : a, true, true);
            return new(null, false, false);
        }
    }

    /// <summary>One walk over one schema document: the refusals so far and what the root gives (its definitions).</summary>
    private sealed partial class Walk(SchemaSite site, string format, string id, string path, JsonObject? defs)
    {
        private readonly HashSet<string> looping = Loops(defs);

        /// <summary>#1508 (A11-002): the names a file both refers to and defines, each said once.</summary>
        private readonly HashSet<string> alsoDefined = new(StringComparer.Ordinal);
        private bool tooDeep;

        public List<SourceRefusal> Refusals { get; } = [];

        /// <summary><paramref name="level"/> is the JSON level of <paramref name="schema"/>, the root being 1, as S01-041 counts.</summary>
        public void Schema(JsonObject schema, string pointer, int level, bool isRoot)
        {
            if (level > SourceBounds.JsonDepth)
            {
                // Said once, as the reader says it of a file: the first container past the depth.
                if (!tooDeep)
                    Refusals.Add(SourceSentences.JsonTooDeep(path, pointer));
                tooDeep = true;
                return;
            }

            var hasRef = schema.ContainsKey("$ref");
            var declared = Declared.Of(schema["type"]);
            if (isRoot)
                Root(schema, declared);
            if (!hasRef)
                Shape(schema, pointer, declared);

            foreach (var (keyword, value) in schema)
            {
                if (keyword is "contentMediaType" or "x-markdown")
                {
                    Annotation(schema, keyword, value, pointer, isRoot, declared);
                    continue;
                }

                if (isRoot && RootKeywords.Contains(keyword))
                {
                    RootKeyword(keyword, value);
                    continue;
                }

                if (!Keywords.Contains(keyword))
                {
                    Refusals.Add(FormatSentences.OutsideDialect(format, pointer, keyword, path));
                    continue;
                }

                if (hasRef && keyword is not ("$ref" or "title" or "description"))
                {
                    Refusals.Add(FormatSentences.KeywordBesideRef(format, pointer, keyword, path));
                    continue;
                }

                Keyword(schema, keyword, value, pointer, level, declared);
            }
        }

        /// <summary>The rules of the schema as a whole: a type at all, an object's three keywords and its names, a list's items.</summary>
        private void Shape(JsonObject schema, string pointer, Declared declared)
        {
            if (!schema.ContainsKey("type") && !schema.ContainsKey("anyOf"))
                Refusals.Add(FormatSentences.NoType(format, pointer, path));

            if (declared.Type == "object")
            {
                foreach (var keyword in (string[])["properties", "required", "additionalProperties"])
                {
                    if (!schema.ContainsKey(keyword))
                        Refusals.Add(FormatSentences.ObjectWithout(format, pointer, keyword, path));
                }

                if (schema["properties"] is JsonObject properties && RequiredNames(schema["required"]) is { } required)
                {
                    foreach (var name in required.Where(n => !properties.ContainsKey(n)))
                        Refusals.Add(FormatSentences.RequiredNotAProperty(format, pointer, name, path));
                    var listed = required.ToHashSet(StringComparer.Ordinal);
                    foreach (var (name, _) in properties.Where(p => !listed.Contains(p.Key)))
                        Refusals.Add(FormatSentences.PropertyNotRequired(format, pointer, name, path));
                }
            }

            if (declared.Type == "array" && !schema.ContainsKey("items"))
                Refusals.Add(FormatSentences.ListWithoutItems(format, pointer, path));

            if (schema.ContainsKey("enum") && schema.ContainsKey("oneOf"))
                Refusals.Add(FormatSentences.EnumAndOneOf(format, pointer, path));
        }

        private void Keyword(JsonObject schema, string keyword, JsonNode? value, string pointer, int level, Declared declared)
        {
            switch (keyword)
            {
                case "type":
                    if (value is not JsonArray && Text(value) is null)
                        WrongType(pointer, keyword, value, "text or a list");
                    else if (!declared.Valid)
                        Refusals.Add(FormatSentences.TypeNotInDialect(format, pointer, Json(value), path));
                    break;

                case "properties":
                    if (value is not JsonObject properties)
                    {
                        WrongType(pointer, keyword, value, "an object");
                        break;
                    }

                    Place(pointer, keyword, declared, "object", "an object");
                    foreach (var (name, property) in properties)
                    {
                        if (site != SchemaSite.RegistryFormat && SourceIds.CheckPatternAndLength(name, path) is { } refusal)
                            Refusals.Add(refusal);
                        Member(property, $"{pointer}/properties/{Escape(name)}", level + 2, name);
                    }

                    break;

                case "required":
                    if (RequiredNames(value) is null)
                        WrongType(pointer, keyword, value, "a list of text");
                    else
                        Place(pointer, keyword, declared, "object", "an object");
                    break;

                case "additionalProperties":
                    if (Place(pointer, keyword, declared, "object", "an object")
                        && !(value is JsonValue v && v.GetValueKind() == JsonValueKind.False))
                        Refusals.Add(FormatSentences.AdditionalPropertiesNotFalse(format, pointer, Json(value), path));
                    break;

                case "items":
                    if (value is not JsonObject items)
                    {
                        WrongType($"{pointer}/items", keyword, value, "an object");
                        break;
                    }

                    Place(pointer, keyword, declared, "array", "a list");
                    Schema(items, $"{pointer}/items", level + 1, isRoot: false);
                    break;

                case "enum" or "oneOf":
                    if (value is not JsonArray values)
                        WrongType(pointer, keyword, value, "a list");
                    else if (IsNumericEnum(schema, keyword, values, declared))
                        NumericValues(values, pointer, declared);
                    else if (Place(pointer, keyword, declared, "string", "a string")
                        && !(schema.ContainsKey("enum") && schema.ContainsKey("oneOf")))
                        Values(keyword, values, pointer, declared);
                    break;

                case "anyOf":
                    if (value is not JsonArray members)
                    {
                        WrongType(pointer, keyword, value, "a list");
                        break;
                    }

                    if (members.Count < 2)
                        Refusals.Add(FormatSentences.UnionTooSmall(format, pointer, members.Count, path));
                    else if (NullableScalar(members) is { } scalar)
                        Refusals.Add(FormatSentences.NullableScalarWithAnyOf(format, pointer, scalar, path));
                    for (var i = 0; i < members.Count; i++)
                        Member(members[i], $"{pointer}/anyOf/{i}", level + 2, keyword);
                    break;

                case "$ref":
                    if (Text(value) is not { } reference)
                        WrongType(pointer, keyword, value, "text");
                    else
                        Reference(reference, pointer);
                    break;

                case "format":
                    if (Text(value) is not { } written)
                        WrongType(pointer, keyword, value, "text");
                    else if (written != "date" || Text(schema["type"]) != "string")
                        Refusals.Add(FormatSentences.FormatNotDate(format, pointer, written, path));
                    break;

                default: // title, description
                    if (Text(value) is null)
                        WrongType(pointer, keyword, value, "text");
                    break;
            }
        }

        /// <summary>A schema held by <paramref name="holder"/> (a property's name, <c>anyOf</c>, a definition's name): an object, walked.</summary>
        private void Member(JsonNode? value, string pointer, int level, string holder)
        {
            if (value is JsonObject schema)
                Schema(schema, pointer, level, isRoot: false);
            else
                WrongType(pointer, holder, value, "an object");
        }

        /// <summary>The root's <c>$defs</c>: an object of schemas, each refused first when its references reach it.</summary>
        private void Definitions(JsonNode? value)
        {
            if (value is not JsonObject definitions)
            {
                WrongType("", "$defs", value, "an object");
                return;
            }

            foreach (var (name, definition) in definitions)
            {
                var pointer = $"/$defs/{Escape(name)}";
                if (looping.Contains(name))
                    Refusals.Add(FormatSentences.RefersToItself(format, pointer, path));
                Member(definition, pointer, 3, name);
            }
        }

        /// <summary>
        /// A <c>$ref</c>: a format name (looked up elsewhere), or <c>#/$defs/&lt;name&gt;</c> naming one of the root's
        /// definitions. #1508 (area 11; R4, ruled by the owner on #1508): a package's format file that names a format and
        /// also defines that name in its own <c>$defs</c> is A11-002, once for each name, so no reader takes one for the other.
        /// </summary>
        private void Reference(string reference, string pointer)
        {
            if (SourceIds.IsPattern(reference))
            {
                if (site == SchemaSite.PackageFormat && defs != null && defs.ContainsKey(reference) && alsoDefined.Add(reference))
                    Refusals.Add(ResolveSentences.NameAlsoDefined(id, reference, path));
                return;
            }

            var name = DefinitionName(reference);
            if (name is null || defs is null || !defs.ContainsKey(name))
                Refusals.Add(FormatSentences.DefinitionMissing(format, pointer, name ?? reference, path));
        }

        /// <summary>Whether the keyword sits on its own type; A03-004 when not. A <c>type</c> that is itself refused leaves placement unjudged.</summary>
        private bool Place(string pointer, string keyword, Declared declared, string type, string belongsOn)
        {
            if (!declared.Valid || declared.Type == type)
                return true;
            Refusals.Add(FormatSentences.KeywordMisplaced(format, pointer, keyword, belongsOn, path));
            return false;
        }

        private void WrongType(string pointer, string keyword, JsonNode? value, string mustBe) =>
            Refusals.Add(FormatSentences.KeywordWrongType(format, pointer, keyword, Describe(value), mustBe, path));
    }

    /// <summary>The names of a <c>required</c> that is a list of text; null when it is anything else.</summary>
    private static List<string>? RequiredNames(JsonNode? value)
    {
        if (value is not JsonArray list)
            return null;
        var names = new List<string>(list.Count);
        foreach (var entry in list)
        {
            if (Text(entry) is not { } name)
                return null;
            names.Add(name);
        }

        return names;
    }

    /// <summary>The <c>&lt;name&gt;</c> of <c>#/$defs/&lt;name&gt;</c>: not empty, no <c>/</c>; null for any other reference.</summary>
    internal static string? DefinitionName(string reference) =>
        reference.StartsWith(DefsPrefix, StringComparison.Ordinal)
        && reference.Length > DefsPrefix.Length
        && reference.IndexOf('/', DefsPrefix.Length) < 0
            ? reference[DefsPrefix.Length..]
            : null;

    /// <summary>S02-028: the scalar type of an <c>anyOf</c> that is exactly a bare scalar and <c>null</c>, each with only annotations beside its type.</summary>
    private static string? NullableScalar(JsonArray members)
    {
        if (members is not [JsonObject a, JsonObject b])
            return null;
        if (Bare(a) == "null")
            return Bare(b) is { } t && Scalars.Contains(t) ? t : null;
        if (Bare(b) == "null")
            return Bare(a) is { } u && Scalars.Contains(u) ? u : null;
        return null;

        static string? Bare(JsonObject schema) =>
            schema.All(p => p.Key is "type" or "title" or "description") ? Text(schema["type"]) : null;
    }

    /// <summary>
    /// The definitions whose <c>#/$defs</c> references reach themselves (S02-032): those in a strongly
    /// connected component of more than one, or referring to themselves. Iterative, so a long chain
    /// cannot exhaust the stack.
    /// </summary>
    private static HashSet<string> Loops(JsonObject? defs)
    {
        var looping = new HashSet<string>(StringComparer.Ordinal);
        if (defs is null)
            return looping;

        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (name, definition) in defs)
            edges[name] = References(definition, defs);

        // Tarjan's algorithm, with an explicit stack of (definition, next edge).
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var low = new Dictionary<string, int>(StringComparer.Ordinal);
        var open = new Stack<string>();
        var onOpen = new HashSet<string>(StringComparer.Ordinal);
        var next = 0;
        foreach (var start in edges.Keys)
        {
            if (index.ContainsKey(start))
                continue;
            var work = new Stack<(string Node, int Edge)>();
            Visit(start);
            while (work.Count > 0)
            {
                var (node, edge) = work.Pop();
                var targets = edges[node];
                if (edge < targets.Count)
                {
                    work.Push((node, edge + 1));
                    var target = targets[edge];
                    if (!index.ContainsKey(target))
                        Visit(target);
                    else if (onOpen.Contains(target))
                        low[node] = Math.Min(low[node], index[target]);
                    continue;
                }

                if (low[node] == index[node])
                {
                    var component = new List<string>();
                    string member;
                    do
                    {
                        member = open.Pop();
                        onOpen.Remove(member);
                        component.Add(member);
                    }
                    while (member != node);
                    if (component.Count > 1 || targets.Contains(node))
                        looping.UnionWith(component);
                }

                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[node]);
                }
            }

            void Visit(string node)
            {
                index[node] = low[node] = next++;
                open.Push(node);
                onOpen.Add(node);
                work.Push((node, 0));
            }
        }

        return looping;
    }

    /// <summary>The definitions <paramref name="definition"/> refers to, through properties, items and anyOf, to the JSON depth.</summary>
    private static List<string> References(JsonNode? definition, JsonObject defs)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<(JsonNode? Node, int Level)>();
        pending.Push((definition, 3));
        while (pending.Count > 0)
        {
            var (node, level) = pending.Pop();
            if (node is not JsonObject schema || level > SourceBounds.JsonDepth)
                continue;
            if (Text(schema["$ref"]) is { } reference && DefinitionName(reference) is { } name
                && defs.ContainsKey(name) && seen.Add(name))
                found.Add(name);
            if (schema["properties"] is JsonObject properties)
            {
                foreach (var (_, property) in properties)
                    pending.Push((property, level + 2));
            }

            pending.Push((schema["items"], level + 1));
            if (schema["anyOf"] is JsonArray members)
            {
                foreach (var member in members)
                    pending.Push((member, level + 2));
            }
        }

        return found;
    }
}
