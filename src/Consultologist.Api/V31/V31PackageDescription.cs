using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.V31;

/// <summary>
/// #1508 (specVersion 31, area 11, Task 18; part 09 §§ 4.2, 4.3; part 02 § 12; D12, D13): what <c>WorkflowPackages/Current</c>
/// answers for a 31 version, read from its plan (its <c>resolve.json</c>) alone. <c>Blocks</c> are the top-level nodes in
/// <c>graph.order</c>, by key, named by their <c>label</c>; <c>Results</c> the deliverables in the same order, by key and
/// <c>label</c>; <c>Macros</c> always empty. Each input, in <c>inputOrder</c>, fills today's members from its format and
/// adds <c>Format</c> (one compound document, <see cref="PlanFormats.SelfContained"/>) and <c>Channel</c>;
/// <c>ExpectedContent</c> is the channel too, and <c>Types</c> null. A format's <c>Type</c>, <c>Values</c>, <c>Items</c>
/// and <c>Fields</c> are read from its expansion as the run holds it (<see cref="PlanFormats.Expand"/>) by publish's own
/// reading of a format's shape (<see cref="FormatTypes.ExpandedShape"/>), at every depth, the fields in written order
/// (#1508, R10: <c>propertyOrder</c>). Never throws on the plan.
/// </summary>
public static class V31PackageDescription
{
    /// <summary>The response for <paramref name="plan"/>.</summary>
    public static WorkflowPackageResponse Describe(RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new WorkflowPackageResponse(
            plan.Package,
            plan.Version,
            PublishedStamp.SpecVersion,
            plan.TopLevel.Where(key => key.StartsWith("node.", StringComparison.Ordinal))
                .Select(key => new WorkflowPackageBlockResponse(key, Label(plan.Entries[key].Json) ?? key))
                .ToList(),
            plan.InputOrder.Select(id => Input(plan, id, plan.Inputs[id])).ToList(),
            plan.TopLevel.Where(key => key.StartsWith("deliverable.", StringComparison.Ordinal))
                .Select(key => new WorkflowPackageResultResponse(key, Label(plan.Entries[key].Json) ?? key))
                .ToList(),
            FormatTypes.Text(plan.Document["title"]),
            []);
    }

    private static WorkflowPackageInputResponse Input(RunPlan plan, string id, JsonObject input)
    {
        var expanded = plan.Formats.Expand(input["format"]);
        var channel = FormatTypes.Text(input["channel"]);
        return new WorkflowPackageInputResponse(
            id,
            Label(input) ?? id,
            input["required"] is JsonValue required && required.GetValueKind() == JsonValueKind.True,
            TypeOf(expanded),
            Values(expanded),
            Items(expanded),
            Fields(expanded),
            channel,
            Types: null,
            JsonSerializer.SerializeToElement(plan.Formats.SelfContained(input["format"])),
            channel);
    }

    private static string? Label(JsonObject json) => FormatTypes.Text(json["label"]);

    /// <summary>Part 02 § 12's table: the response's <c>Type</c> of a format's shape; null for a shape the table has no row for.</summary>
    private static string? TypeOf(JsonNode? format) => FormatTypes.ExpandedShape(format) switch
    {
        FormatShape.Text or FormatShape.String => WorkflowInputTypes.Text,
        FormatShape.Enum => WorkflowInputTypes.Enum,
        FormatShape.Date => WorkflowInputTypes.Date,
        FormatShape.Number or FormatShape.Integer => WorkflowInputTypes.Number,
        FormatShape.Boolean => WorkflowInputTypes.Boolean,
        FormatShape.Object => WorkflowInputTypes.Object,
        FormatShape.List => WorkflowInputTypes.Array,
        _ => null,
    };

    /// <summary>An enum's values: its <c>enum</c>'s text, or each <c>oneOf</c> entry's <c>const</c>, in order (a null among them is not a value).</summary>
    private static IReadOnlyList<string>? Values(JsonNode? format)
    {
        if (FormatTypes.ExpandedShape(format) != FormatShape.Enum || Schema(format) is not { } schema)
            return null;
        var values = schema["enum"] is JsonArray listed
            ? listed.Select(FormatTypes.Text)
            : (schema["oneOf"] as JsonArray ?? []).Select(entry => FormatTypes.Text((entry as JsonObject)?["const"]));
        return values.OfType<string>().ToList();
    }

    /// <summary>A list's element, mapped by the same rules, at every depth.</summary>
    private static WorkflowPackageElementResponse? Items(JsonNode? format)
    {
        if (FormatTypes.ExpandedShape(format) != FormatShape.List || Schema(format)?["items"] is not { } items)
            return null;
        return new WorkflowPackageElementResponse(TypeOf(items) ?? WorkflowInputTypes.Text, Items(items), Fields(items), Values(items));
    }

    /// <summary>
    /// An object's fields, one per property in written order (D13; #1508, R10: its <c>propertyOrder</c>, then any property
    /// it does not list in the order <c>resolve.json</c> holds them): <c>Label</c> its <c>title</c>, the property name when
    /// it has none; <c>Required</c> when its type is not nullable.
    /// </summary>
    private static IReadOnlyList<WorkflowPackageFieldResponse>? Fields(JsonNode? format)
    {
        if (FormatTypes.ExpandedShape(format) != FormatShape.Object || Schema(format) is not { } schema
            || schema["properties"] is not JsonObject properties)
        {
            return null;
        }

        return Written(schema, properties)
            .Select(property => new WorkflowPackageFieldResponse(
                property.Key,
                Title(property.Value) ?? property.Key,
                !Nullable(property.Value),
                TypeOf(property.Value),
                Values(property.Value),
                Items(property.Value),
                Fields(property.Value)))
            .ToList();
    }

    /// <summary>
    /// <paramref name="properties"/>, <paramref name="schema"/>'s, in written order: each name its <c>propertyOrder</c> lists
    /// (once, and only a property it has), then the rest as held.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, JsonNode?>> Written(JsonObject schema, JsonObject properties)
    {
        var listed = (schema[CompiledFormats.PropertyOrder] as JsonArray ?? [])
            .Select(FormatTypes.Text).OfType<string>().Where(properties.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
        return listed.Select(name => KeyValuePair.Create(name, properties[name]))
            .Concat(properties.Where(property => !listed.Contains(property.Key, StringComparer.Ordinal)));
    }

    /// <summary>A property's <c>title</c>: its own, or the format's it inlines.</summary>
    private static string? Title(JsonNode? property) =>
        FormatTypes.Text((property as JsonObject)?["title"]) ?? FormatTypes.Text(FormatTypes.Inlined(property)?["title"]);

    /// <summary>
    /// The schema whose shape <see cref="FormatTypes.ExpandedShape"/> reads: through an inlined reference's <c>allOf</c>, and
    /// through an <c>anyOf</c> of one type and <c>null</c> to that type.
    /// </summary>
    private static JsonObject? Schema(JsonNode? format)
    {
        for (var depth = 0; depth <= SourceBounds.JsonDepth; depth++)
        {
            var schema = FormatTypes.Inlined(format);
            if (schema?["anyOf"] is not JsonArray members)
                return schema;
            var others = members.Where(member => FormatTypes.Text(FormatTypes.Inlined(member)?["type"]) != "null").ToList();
            if (others.Count != 1)
                return schema;
            format = others[0];
        }

        return null;
    }

    /// <summary>Whether <paramref name="format"/> admits <c>null</c>: a type <c>null</c> or a type list with it, or an <c>anyOf</c> with such a member.</summary>
    private static bool Nullable(JsonNode? format)
    {
        var schema = FormatTypes.Inlined(format);
        return schema?["anyOf"] is JsonArray members
            ? members.Any(member => AdmitsNull(FormatTypes.Inlined(member)))
            : AdmitsNull(schema);
    }

    private static bool AdmitsNull(JsonObject? schema) =>
        schema?["type"] is JsonArray types
            ? types.Any(type => FormatTypes.Text(type) == "null")
            : FormatTypes.Text(schema?["type"]) == "null";
}
