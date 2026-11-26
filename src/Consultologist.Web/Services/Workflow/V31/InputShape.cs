using System.Text.Json;

namespace Consultologist.Web.Services.Workflow.V31;

/// <summary>The control the Create page draws for an input (part 02 § 8.1, part 09 § 5.1).</summary>
public enum InputWidget { Line, Area, Markdown, Select, Date, Boolean, Number, Integer, Group, Rows, Unsupported }

/// <summary>One choice of a select; <see cref="Title"/> equals <see cref="Value"/> for a plain enum.</summary>
public sealed record InputChoice(string Value, string Title);

/// <summary>An input's (or a field's, or a row's) shape: what widget, and what lies under it.</summary>
public sealed record InputShape(
    InputWidget Widget,
    bool Nullable,
    string? Title,
    IReadOnlyList<InputChoice> Choices,
    IReadOnlyList<InputField> Fields,
    InputShape? Items);

/// <summary>A field of a group, in written order.</summary>
public sealed record InputField(string Id, string Label, bool Required, InputShape Shape);

/// <summary>
/// Reads an input's <c>Format</c> (one compound document: the schema at the root, the formats it reaches under the root's
/// <c>$defs</c>, references written <c>{ "$ref": "&lt;$id URN&gt;" }</c>) into an <see cref="InputShape"/>. It never throws:
/// what it cannot read is <see cref="InputWidget.Unsupported"/>.
/// </summary>
public static class InputShapes
{
    private const int MaxDepth = 32;
    private const string SpecFormat = "urn:consultologist:spec:";

    private static readonly IReadOnlyList<InputChoice> NoChoices = [];
    private static readonly IReadOnlyList<InputField> NoFields = [];

    /// <param name="format"><c>WorkflowPackageInputResponse.Format</c>.</param>
    /// <param name="fields">The response's own <c>Fields</c>: written order and labels.</param>
    /// <param name="items">The response's own <c>Items</c>.</param>
    public static InputShape Of(JsonElement format, IReadOnlyList<WorkflowPackageFieldResponse>? fields, WorkflowPackageElementResponse? items)
    {
        try
        {
            return Read(format, DefsOf(format), fields, items, 0);
        }
        catch (Exception ex) when (ex is InvalidOperationException or JsonException or FormatException or KeyNotFoundException)
        {
            return Unsupported(null);
        }
    }

    private static InputShape Unsupported(string? title) => new(InputWidget.Unsupported, false, title, NoChoices, NoFields, null);

    private static Dictionary<string, JsonElement> DefsOf(JsonElement root)
    {
        var defs = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("$defs", out var table) && table.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in table.EnumerateObject())
            {
                defs[$"#/$defs/{entry.Name}"] = entry.Value;
                if (entry.Value.ValueKind == JsonValueKind.Object && Text(entry.Value, "$id") is { } id)
                    defs[id] = entry.Value;
            }
        }
        return defs;
    }

    private static string? Text(JsonElement schema, string name) =>
        schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static InputShape Read(
        JsonElement schema, Dictionary<string, JsonElement> defs,
        IReadOnlyList<WorkflowPackageFieldResponse>? fields, WorkflowPackageElementResponse? items, int depth)
    {
        if (depth > MaxDepth || schema.ValueKind != JsonValueKind.Object)
            return Unsupported(null);

        var title = Text(schema, "title");

        if (schema.TryGetProperty("$ref", out var reference))
        {
            if (reference.ValueKind != JsonValueKind.String || !defs.TryGetValue(reference.GetString()!, out var target))
                return Unsupported(title);
            var resolved = Read(target, defs, fields, items, depth + 1);
            return title is null ? resolved : resolved with { Title = title };
        }

        // Part 02 § 3.2: a reference or an object is made nullable as anyOf it and { "type": "null" }; no other anyOf has
        // a widget (§ 8.1).
        if (schema.TryGetProperty("anyOf", out var anyOf))
        {
            if (anyOf.ValueKind != JsonValueKind.Array || anyOf.GetArrayLength() != 2)
                return Unsupported(title);
            var arms = anyOf.EnumerateArray().ToList();
            var others = arms.Where(arm => !IsNullArm(arm)).ToList();
            if (others.Count != 1)
                return Unsupported(title);
            var inner = Read(others[0], defs, fields, items, depth + 1);
            return inner.Widget == InputWidget.Unsupported
                ? inner
                : inner with { Nullable = true, Title = title ?? inner.Title };
        }

        if (Text(schema, "$id") is { } id && id.StartsWith(SpecFormat, StringComparison.Ordinal))
        {
            var widget = id switch
            {
                _ when id.EndsWith(":format:plaintext", StringComparison.Ordinal) => InputWidget.Area,
                _ when id.EndsWith(":format:markdown_block", StringComparison.Ordinal) => InputWidget.Markdown,
                _ when id.EndsWith(":format:date", StringComparison.Ordinal) => InputWidget.Date,
                _ => (InputWidget?)null,
            };
            if (widget is { } named)
                return new InputShape(named, false, title, NoChoices, NoFields, null);
        }

        if (!TypeOf(schema, out var type, out var nullable))
            return Unsupported(title);

        switch (type)
        {
            case "string":
                return ReadString(schema, nullable, title);
            case "boolean":
                return Scalar(InputWidget.Boolean, nullable, title);
            case "number":
                return Scalar(InputWidget.Number, nullable, title);
            case "integer":
                return Scalar(InputWidget.Integer, nullable, title);
            case "object":
                return ReadGroup(schema, defs, fields, nullable, title, depth);
            case "array":
                if (!schema.TryGetProperty("items", out var element))
                    return Unsupported(title);
                return new InputShape(
                    InputWidget.Rows, nullable, title, NoChoices, NoFields,
                    Read(element, defs, items?.Fields, items?.Items, depth + 1));
            default:
                return Unsupported(title);
        }
    }

    private static bool IsNullArm(JsonElement arm) =>
        arm.ValueKind == JsonValueKind.Object && Text(arm, "type") == "null" && arm.EnumerateObject().Count() == 1;

    private static InputShape Scalar(InputWidget widget, bool nullable, string? title) =>
        new(widget, nullable, title, NoChoices, NoFields, null);

    /// <summary>The one non-null type, and whether the schema also admits null (part 02 § 3.2: <c>type</c> as an array).</summary>
    private static bool TypeOf(JsonElement schema, out string type, out bool nullable)
    {
        type = "";
        nullable = false;
        if (!schema.TryGetProperty("type", out var declared))
            return false;
        if (declared.ValueKind == JsonValueKind.String)
        {
            type = declared.GetString()!;
            return true;
        }
        if (declared.ValueKind != JsonValueKind.Array)
            return false;
        var others = new List<string>();
        foreach (var entry in declared.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
                return false;
            if (entry.GetString() == "null")
                nullable = true;
            else
                others.Add(entry.GetString()!);
        }
        if (others.Count != 1 || !nullable)
            return false;
        type = others[0];
        return true;
    }

    private static InputShape ReadString(JsonElement schema, bool nullable, string? title)
    {
        var choices = new List<InputChoice>();
        if (schema.TryGetProperty("oneOf", out var arms) && arms.ValueKind == JsonValueKind.Array)
        {
            foreach (var arm in arms.EnumerateArray())
            {
                // A nullable labelled enum's { "const": null } arm (part 02 § 3.2) is the blank choice, not an option.
                if (arm.ValueKind == JsonValueKind.Object && arm.TryGetProperty("const", out var constant) && constant.ValueKind == JsonValueKind.Null)
                    continue;
                if (arm.ValueKind != JsonValueKind.Object || Text(arm, "const") is not { } value)
                    return Unsupported(title);
                choices.Add(new InputChoice(value, Text(arm, "title") ?? value));
            }
        }
        else if (schema.TryGetProperty("enum", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in values.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Null)
                    continue;
                if (entry.ValueKind != JsonValueKind.String)
                    return Unsupported(title);
                choices.Add(new InputChoice(entry.GetString()!, entry.GetString()!));
            }
        }
        else
        {
            return Scalar(InputWidget.Line, nullable, title);
        }
        return new InputShape(InputWidget.Select, nullable, title, choices, NoFields, null);
    }

    private static InputShape ReadGroup(
        JsonElement schema, Dictionary<string, JsonElement> defs,
        IReadOnlyList<WorkflowPackageFieldResponse>? fields, bool nullable, string? title, int depth)
    {
        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return Unsupported(title);

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array)
            foreach (var name in names.EnumerateArray())
                if (name.ValueKind == JsonValueKind.String)
                    required.Add(name.GetString()!);

        // The order and labels come from the response's fields; with none, the properties as written.
        var declared = fields ?? properties.EnumerateObject()
            .Select(p => new WorkflowPackageFieldResponse(p.Name, Text(p.Value, "title") ?? p.Name, required.Contains(p.Name)))
            .ToList();

        var result = new List<InputField>();
        foreach (var field in declared)
        {
            var shape = properties.TryGetProperty(field.Id, out var property)
                ? Read(property, defs, field.Fields, field.Items, depth + 1)
                : Unsupported(null);
            result.Add(new InputField(field.Id, field.Label, field.Required && !shape.Nullable, shape));
        }
        return new InputShape(InputWidget.Group, nullable, title, NoChoices, result, null);
    }
}
