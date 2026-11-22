using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Migration;

/// <summary>
/// #1507 (specVersion 31, area 10; part 02 § 11, § 8; part 10 § 1.2's input rows, § 1.8): the inputs pass. 30's inputs
/// array becomes the manifest's object keyed by id, in the array's order (the Create page's), each input written with
/// every member (<see cref="SourceShapes.Input"/>): its <c>label</c>, <c>description: null</c>, its 31 <c>format</c>,
/// <c>required</c> as it was, and <c>channel</c> from <c>expectedContent</c>. <c>labelField</c> is the loops pass's
/// (<c>itemLabel</c>, part 04); a per-run macro choice is the macros pass's (§ 10).
/// </summary>
internal static partial class Migrate
{
    /// <summary>Writes each 30 input into the 31 manifest's <c>inputs</c> (which <see cref="Layout"/> wrote).</summary>
    public static void Inputs(Migrated m)
    {
        ArgumentNullException.ThrowIfNull(m);
        var inputs = m.Manifest["inputs"]!.AsObject();
        foreach (var input in m.Manifest30.Inputs ?? [])
        {
            inputs[input.Id] = new JsonObject
            {
                ["label"] = input.Label,
                ["description"] = null,
                ["format"] = InputFormat(m, input),
                ["required"] = input.Required,
                ["channel"] = input.ExpectedContent,
            };
        }
    }

    /// <summary>
    /// An input's 31 format (part 02 § 11): <c>text</c> and no type are <c>markdown_block</c>; <c>date</c> is
    /// <c>"date"</c>; <c>boolean</c> and <c>number</c> keep their names; <c>enum</c> + <c>values</c> is a <c>string</c>
    /// with a plain <c>enum</c> and no titles; <c>object</c> + <c>fields</c> is <see cref="ObjectOf"/>; <c>array</c> +
    /// <c>items</c> an array of the element; a "one or many" union <c>[x, array]</c> its array form. A union with no
    /// array arm (or more than one other) has no 31 input format: it is written as an <c>anyOf</c> of its arms, which the
    /// checker refuses at an input's root, and is a hand edit (A10-024).
    /// </summary>
    private static JsonNode InputFormat(Migrated m, WorkflowInputSpec input)
    {
        var types = WorkflowInputTypes.TypesOf(input);
        if (types.Count > 1)
        {
            var others = types.Where(t => t != WorkflowInputTypes.Array).ToList();
            if (others.Count == 1 && others.Count < types.Count)
            {
                var element = input.Items ?? new WorkflowElementSpec(others[0]);
                return new JsonObject { ["type"] = "array", ["items"] = Element(element, input.Fields, input.Values) };
            }

            m.HandEdit("manifest.json", $"inputs.{input.Id}.format", MigrationSentences.UnionInput);
            return new JsonObject
            {
                ["anyOf"] = new JsonArray([.. types.Select(t => (JsonNode?)Element(new WorkflowElementSpec(t), input.Fields, input.Values))]),
            };
        }

        return types[0] switch
        {
            WorkflowInputTypes.Text => "markdown_block",
            WorkflowInputTypes.Date => "date",
            WorkflowInputTypes.Boolean => "boolean",
            WorkflowInputTypes.Number => "number",
            WorkflowInputTypes.Enum => EnumOf(input.Values),
            WorkflowInputTypes.Object => ObjectOf(input.Fields),
            WorkflowInputTypes.Array => new JsonObject
            {
                ["type"] = "array",
                ["items"] = Element(input.Items ?? new WorkflowElementSpec(WorkflowInputTypes.Text), input.Fields, input.Values),
            },
            // Unreachable for a package 30 validated (its types are closed); written as named, which 31 refuses.
            var other => other,
        };
    }

    /// <summary>
    /// A nested type (an array's element, an object's field, part 02 § 5.3): <c>text</c> references
    /// <c>markdown_block</c> and <c>date</c> the built-in <c>date</c>; <c>boolean</c> and <c>number</c> are their JSON
    /// types; the rest as for an input. An element's <c>fields</c> and <c>values</c> are its own, or the declaration's
    /// (30's one <c>fields</c> serves an array of objects).
    /// </summary>
    private static JsonObject Element(WorkflowElementSpec element, List<WorkflowFieldSpec>? fields, List<string>? values) =>
        Nested(element.Type, element.Items, element.Fields ?? fields, element.Values ?? values);

    private static JsonObject Nested(string type, WorkflowElementSpec? items, List<WorkflowFieldSpec>? fields, List<string>? values) => type switch
    {
        WorkflowInputTypes.Text => new JsonObject { ["$ref"] = "markdown_block" },
        WorkflowInputTypes.Date => new JsonObject { ["$ref"] = "date" },
        WorkflowInputTypes.Boolean or WorkflowInputTypes.Number => new JsonObject { ["type"] = type },
        WorkflowInputTypes.Enum => EnumOf(values),
        WorkflowInputTypes.Object => ObjectOf(fields),
        WorkflowInputTypes.Array => new JsonObject
        {
            ["type"] = "array",
            ["items"] = Element(items ?? new WorkflowElementSpec(WorkflowInputTypes.Text), fields, values),
        },
        var other => new JsonObject { ["type"] = other },
    };

    /// <summary>Part 02 § 9: a plain enum, the 30 values as they were, with no invented titles.</summary>
    private static JsonObject EnumOf(List<string>? values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray([.. (values ?? []).Select(v => (JsonNode?)JsonValue.Create(v))]),
    };

    /// <summary>
    /// Part 02 § 8.1, § 3.3: 30's <c>fields</c> as <c>properties</c>, every field listed in <c>required</c> in 30's order,
    /// <c>additionalProperties: false</c>; each field typed as <see cref="Nested"/> says, made nullable when it is optional
    /// (<see cref="Nullable"/>), and titled by its <c>label</c>.
    /// </summary>
    private static JsonObject ObjectOf(List<WorkflowFieldSpec>? fields)
    {
        var properties = new JsonObject();
        foreach (var field in fields ?? [])
        {
            var schema = Nested(WorkflowInputTypes.Of(field), field.Items, field.Fields, field.Values);
            if (!field.Required)
                schema = Nullable(schema);
            schema["title"] = field.Label;
            properties[field.Id] = schema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray([.. properties.Select(p => (JsonNode?)JsonValue.Create(p.Key))]),
            ["properties"] = properties,
        };
    }
}
