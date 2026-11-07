using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v29 (#1303, package-format-v29-design.md § 1.2, § 2.2–§ 2.3): a loop over a
/// node's value, settled from the source node's RECORDED output — pure, so the
/// orchestrator replays it to the same items. The items are the list at the
/// field path under the value, in order: id the 1-based position, name the
/// loop's label and the position, value the element (its JSON for an object or
/// array, its text for a string). An empty list is a loop with no items and a
/// reason; a value that is not a list there is a failure sentence.
/// </summary>
internal static class ConsultNodeValueLoops
{
    /// <summary>The item key that says how <c>value</c> is spelled: <see cref="Json"/> or text.</summary>
    public const string ValueKind = "valueKind";

    public const string Json = "json";

    public sealed record Resolution(
        IReadOnlyList<IReadOnlyDictionary<string, string>> Items,
        string? Reason,
        string? Failure);

    /// <summary>The source node and the field path a loop collection names (<c>node.&lt;id&gt;.value.f…</c>), or null.</summary>
    public static (string NodeId, IReadOnlyList<string> Fields)? SourceOf(string? collection)
    {
        if (!TemplateObjectReference.TryParse(collection, out var obj, out var id, out var rest) || obj != TemplateObjects.Node)
        {
            return null;
        }

        var valuePrefix = "." + TemplateMembers.Value;
        if (rest != valuePrefix && !rest.StartsWith(valuePrefix + ".", StringComparison.Ordinal))
        {
            return null;
        }

        return (id, rest.Length == valuePrefix.Length ? Array.Empty<string>() : rest[(valuePrefix.Length + 1)..].Split('.'));
    }

    /// <summary>The loop's items from its source's recorded output (the bytes the renderer reads as <c>.value</c>).</summary>
    public static Resolution ItemsOf(NodeRunResult output, string collection, string loopLabel)
    {
        if (SourceOf(collection) is not { } source)
        {
            return new Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), null, $"{loopLabel} iterates {collection}, which is not a node's value.");
        }

        JsonNode? value;
        try
        {
            value = JsonNode.Parse(output.Classification ?? output.RawOutput);
        }
        catch (JsonException)
        {
            return new Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), null, $"{loopLabel} could not read its list: {source.NodeId}'s value is not JSON.");
        }

        var walked = $"{TemplateObjects.Node}.{source.NodeId}.{TemplateMembers.Value}";
        foreach (var field in source.Fields)
        {
            if (value is not JsonObject obj || !obj.TryGetPropertyValue(field, out var next))
            {
                return new Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), null, $"{loopLabel} could not read its list: {walked} has no field '{field}'.");
            }

            value = next;
            walked += "." + field;
        }

        if (value is not JsonArray list)
        {
            return new Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), null, $"{loopLabel} could not read its list: {walked} is not a list.");
        }

        if (list.Count == 0)
        {
            return new Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), $"no items: {walked} was empty", null);
        }

        var items = list
            .Select((element, index) =>
            {
                var position = (index + 1).ToString(CultureInfo.InvariantCulture);
                var item = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["id"] = position,
                    ["name"] = $"{loopLabel} {position}",
                };

                if (element is JsonValue scalar && scalar.TryGetValue(out string? text))
                {
                    item["value"] = text;
                }
                else
                {
                    item["value"] = element?.ToJsonString() ?? string.Empty;
                    item[ValueKind] = Json;
                }

                return (IReadOnlyDictionary<string, string>)item;
            })
            .ToList();

        return new Resolution(items, null, null);
    }

    /// <summary>
    /// How a template reads an item's value: an object or array element as
    /// structured (its fields by name, as the format declared them), a string
    /// as text — the declaration the renderer materialises the value by.
    /// </summary>
    public static WorkflowDeclarationNode? ElementOf(IReadOnlyDictionary<string, string> item) =>
        item.GetValueOrDefault(ValueKind) == Json && item.GetValueOrDefault("value") is { } json
            ? new WorkflowDeclarationNode(json.TrimStart().StartsWith('[') ? WorkflowInputTypes.Array : WorkflowInputTypes.Object, string.Empty, false, null, null, null)
            : null;
}
