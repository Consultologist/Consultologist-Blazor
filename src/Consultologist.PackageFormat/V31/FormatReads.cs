using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31;

/// <summary>Why a typed read (part 02 § 2.1) gives no format; the caller words the refusal.</summary>
public enum ReadFault
{
    /// <summary>The read is typed.</summary>
    None,

    /// <summary>No step: the node, input, item or parameter is read bare (S02-018).</summary>
    Bare,

    /// <summary>The first step is not <c>value</c>, <c>content</c> or <c>title</c> (part 04's reads are area 05's), or a step follows <c>content</c> or <c>title</c>, or the format is null.</summary>
    UnknownMember,

    /// <summary><c>.content</c> on a format with no text (<c>number</c>, <c>integer</c>, <c>boolean</c>, <c>date</c>, an enum, <c>null</c>, an unreadable format).</summary>
    NoContent,

    /// <summary><c>.title</c> on a plain <c>enum</c> (S02-019).</summary>
    TitleOfPlainEnum,

    /// <summary><c>.title</c> on a format that is no enum and has no field <c>title</c>.</summary>
    TitleOfNonEnum,

    /// <summary>A field step on an object that does not declare it.</summary>
    NoSuchField,

    /// <summary>A field step through a union whose members are not all objects declaring the field with one format.</summary>
    NotEveryMember,

    /// <summary>A field step on a format that is not an object (or not readable).</summary>
    NotAnObject,
}

/// <summary>
/// What a typed read gives: the <paramref name="Format"/> read, as written at that place (a clone;
/// a name stays a name), the <paramref name="Fault"/> when there is none, whether the value may be
/// <c>null</c>, and, for a <c>.title</c> read of a labelled enum, its <paramref name="Titles"/> in order.
/// </summary>
public sealed record ReadResult(JsonNode? Format, ReadFault Fault, bool Nullable, IReadOnlyList<string>? Titles);

/// <summary>
/// #1500 (specVersion 31, area 03): types a read (part 02 § 2.1, § 2.2 "Reading a union", § 5.3, § 9)
/// against the format being read. Finding the reads is the callers' (JSON Logic, templates,
/// references). Never throws, and never recurses without bound, on any format or step list.
/// </summary>
public static class FormatReads
{
    /// <summary>The most nested <c>anyOf</c> / name hops one step unwraps; a nullable wrapper loop stops here.</summary>
    private const int Hops = 64;

    private readonly record struct View(JsonObject? Schema, JsonObject? Defs, bool Nullable, FormatShape Shape);

    /// <summary>
    /// Types <paramref name="steps"/>, what follows the node, input, item or parameter, read on
    /// <paramref name="format"/>. <c>.value</c> gives the format (Nullable when it is);
    /// <c>.content</c> gives a text format itself and <c>plaintext</c> for an object, a list or a
    /// union (D10), <see cref="ReadFault.NoContent"/> otherwise; <c>.title</c> on a labelled enum gives
    /// <c>string</c> and its titles; after <c>value</c>, each step is a field of an object, a field
    /// called <c>title</c> winning over the label read (§ 5.3). Through a union a field is read when
    /// every member (but a <c>null</c> one) is an object declaring it with the <see cref="FormatTypes.Same"/>
    /// format. A format that cannot be read (unknown name, loop, not a schema) gives
    /// <see cref="ReadFault.NotAnObject"/> to a field step, <see cref="ReadFault.NoContent"/> to
    /// <c>.content</c>, <see cref="ReadFault.TitleOfNonEnum"/> to <c>.title</c>; <c>.value</c> on it
    /// returns the format as written (the unknown name is refused where it is written, S02-017).
    /// </summary>
    public static ReadResult Read(JsonNode? format, IReadOnlyList<string> steps, FormatSet formats)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(formats);
        if (steps.Count == 0)
        {
            return Fault(ReadFault.Bare);
        }

        if (format is null)
        {
            return Fault(ReadFault.UnknownMember);
        }

        var root = Look(format, FormatTypes.Defs(format), formats);
        switch (steps[0])
        {
            case "content":
                return steps.Count > 1 ? Fault(ReadFault.UnknownMember) : Content(format, root);
            case "title":
                return steps.Count > 1 ? Fault(ReadFault.UnknownMember) : Title(root);
            case "value":
                break;
            default:
                return Fault(ReadFault.UnknownMember);
        }

        var current = format.DeepClone();
        var view = root;
        var nullable = root.Nullable;
        for (var i = 1; i < steps.Count; i++)
        {
            var step = steps[i];
            var last = i == steps.Count - 1;
            if (step is null)
            {
                return Fault(ReadFault.NoSuchField);
            }

            var field = Field(view, step, formats, out var fault, out var viaNull);
            nullable |= viaNull;
            if (field is null)
            {
                if (last && step == "title" && fault is ReadFault.NoSuchField or ReadFault.NotAnObject)
                {
                    var title = Title(view);
                    return title with { Nullable = title.Nullable || nullable };
                }

                return Fault(fault);
            }

            current = field;
            view = Look(field, FormatTypes.Defs(field), formats);
            nullable |= view.Nullable;
        }

        return new ReadResult(current, ReadFault.None, nullable, null);
    }

    /// <summary>S02-018 for <c>input.&lt;id&gt;</c> read bare; <paramref name="path"/> is the file.</summary>
    public static SourceRefusal BareInput(string id, string path)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(path);
        return FormatSentences.BareInput(id, path);
    }

    /// <summary>S02-019: <paramref name="readPath"/> (<c>item.value.kind</c>, without <c>.title</c>) is a plain enum.</summary>
    public static SourceRefusal TitleOfPlainEnum(string readPath, string path)
    {
        ArgumentNullException.ThrowIfNull(readPath);
        ArgumentNullException.ThrowIfNull(path);
        return FormatSentences.TitleOfPlainEnum(readPath, path);
    }

    /// <summary>
    /// S02-020 when <paramref name="literal"/> is not one of <paramref name="read"/>'s titles; null
    /// when it is, or when the read has no titles. <paramref name="condition"/> is the caller's site
    /// phrase (<c>Node 'route' if</c>); <paramref name="readPath"/> is the read without <c>.title</c>.
    /// </summary>
    public static SourceRefusal? TitleLiteral(string condition, string readPath, string literal, ReadResult read, string path)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(readPath);
        ArgumentNullException.ThrowIfNull(literal);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(path);
        if (read.Titles is null || read.Titles.Count == 0 || read.Titles.Contains(literal, StringComparer.Ordinal))
        {
            return null;
        }

        return FormatSentences.TitleLiteralNotATitle(condition, readPath, literal, string.Join(", ", read.Titles), path);
    }

    private static ReadResult Fault(ReadFault fault) => new(null, fault, false, null);

    private static ReadResult Content(JsonNode format, View view) => view.Shape switch
    {
        FormatShape.Text => new ReadResult(format.DeepClone(), ReadFault.None, view.Nullable, null),
        FormatShape.Object or FormatShape.List or FormatShape.Union =>
            new ReadResult(new JsonObject { ["$ref"] = "plaintext" }, ReadFault.None, view.Nullable, null),
        _ => Fault(ReadFault.NoContent),
    };

    private static ReadResult Title(View view)
    {
        if (view.Shape != FormatShape.Enum || view.Schema is null)
        {
            return Fault(ReadFault.TitleOfNonEnum);
        }

        if (view.Schema["oneOf"] is not JsonArray arms)
        {
            return Fault(ReadFault.TitleOfPlainEnum);
        }

        var titles = new List<string>();
        foreach (var arm in arms)
        {
            if (arm is JsonObject o && FormatTypes.Text(o["title"]) is { } t && FormatTypes.Text(o["const"]) is not null)
            {
                titles.Add(t);
            }
        }

        return new ReadResult(new JsonObject { ["type"] = "string" }, ReadFault.None, view.Nullable, titles);
    }

    /// <summary>The field <paramref name="name"/> of what <paramref name="view"/> is, as written there; null with the fault in <paramref name="fault"/>.</summary>
    private static JsonNode? Field(View view, string name, FormatSet formats, out ReadFault fault, out bool nullable)
    {
        fault = ReadFault.None;
        nullable = false;
        if (view.Schema is null)
        {
            fault = ReadFault.NotAnObject;
            return null;
        }

        if (view.Shape == FormatShape.Object)
        {
            var found = FieldOf(view.Schema, view.Defs, name);
            fault = found is null ? ReadFault.NoSuchField : ReadFault.None;
            return found;
        }

        if (view.Shape != FormatShape.Union || view.Schema["anyOf"] is not JsonArray members)
        {
            fault = ReadFault.NotAnObject;
            return null;
        }

        JsonNode? first = null;
        foreach (var member in members)
        {
            var m = Look(member, view.Defs, formats);
            if (m.Schema is not null && m.Shape == FormatShape.Null)
            {
                nullable = true;
                continue;
            }

            nullable |= m.Nullable;

            var found = m.Shape == FormatShape.Object && m.Schema is not null ? FieldOf(m.Schema, m.Defs, name) : null;
            if (found is null || (first is not null && !FormatTypes.Same(first, found, formats)))
            {
                fault = ReadFault.NotEveryMember;
                return null;
            }

            first ??= found;
        }

        if (first is null)
        {
            fault = ReadFault.NotEveryMember;
        }

        return first;
    }

    /// <summary>The clone of <c>properties[name]</c> of <paramref name="schema"/>, carrying the <c>$defs</c> its <c>#/$defs</c> references read.</summary>
    private static JsonNode? FieldOf(JsonObject schema, JsonObject? defs, string name)
    {
        if (schema["properties"] is not JsonObject properties || !properties.TryGetPropertyValue(name, out var field) || field is null)
        {
            return null;
        }

        var clone = field.DeepClone();
        for (var hop = 0; hop < Hops && clone is JsonObject site && defs is not null
            && FormatTypes.Text(site["$ref"]) is { } reference && FormatTypes.OnlyAnnotationsBeside(site)
            && FormatDialect.DefinitionName(reference) is { } definition && defs[definition] is JsonObject target; hop++)
        {
            // A #/$defs site cannot travel without its file's $defs: give the definition, with the site's annotations.
            var resolved = (JsonObject)target.DeepClone();
            foreach (var key in new[] { "title", "description" })
            {
                if (site[key] is { } annotation)
                {
                    resolved[key] = annotation.DeepClone();
                }
            }

            clone = resolved;
        }

        if (clone is JsonObject obj && defs is not null && !obj.ContainsKey("$defs") && HasLocalReference(obj, 0))
        {
            obj["$defs"] = defs.DeepClone();
        }

        return clone;
    }

    /// <summary>Whether <paramref name="node"/> holds a <c>$ref</c> into <c>#/$defs</c> (so its file's <c>$defs</c> must travel with it); true past the depth bound.</summary>
    private static bool HasLocalReference(JsonNode? node, int depth)
    {
        if (depth > SourceBounds.JsonDepth)
        {
            return true;
        }

        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, member) in obj)
                {
                    if (key == "$ref" && FormatTypes.Text(member) is { } r && r.StartsWith('#'))
                    {
                        return true;
                    }

                    if (HasLocalReference(member, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            case JsonArray array:
                return array.Any(m => HasLocalReference(m, depth + 1));
            default:
                return false;
        }
    }

    /// <summary>
    /// What <paramref name="node"/> is, through names, <c>#/$defs</c> and an <c>anyOf</c> of one
    /// type and <c>null</c> (read as that type, nullable); a union of more is a union.
    /// </summary>
    private static View Look(JsonNode? node, JsonObject? defs, FormatSet formats)
    {
        var nullable = false;
        for (var hop = 0; hop < Hops; hop++)
        {
            var (schema, inner) = FormatTypes.Follow(node, defs, formats);
            if (schema is null)
            {
                return new View(null, null, nullable, FormatShape.Unknown);
            }

            if (schema["anyOf"] is not JsonArray members)
            {
                nullable |= schema["type"] is JsonArray pair && FormatTypes.NullablePair(pair) != null;
                nullable |= FormatTypes.Text(schema["type"]) == "null";
                return new View(schema, inner, nullable, FormatTypes.Shape(schema, formats));
            }

            JsonNode? only = null;
            var others = 0;
            var nulls = 0;
            foreach (var member in members)
            {
                if (FormatTypes.Follow(member, inner, formats).Schema is { } m && FormatTypes.Text(m["type"]) == "null")
                {
                    nulls++;
                }
                else
                {
                    others++;
                    only = member;
                }
            }

            nullable |= nulls > 0;
            if (others == 1 && nulls > 0)
            {
                node = only;
                defs = inner;
                continue;
            }

            return new View(schema, inner, nullable, others == 0 ? FormatShape.Null : FormatShape.Union);
        }

        return new View(null, null, nullable, FormatShape.Unknown);
    }
}
