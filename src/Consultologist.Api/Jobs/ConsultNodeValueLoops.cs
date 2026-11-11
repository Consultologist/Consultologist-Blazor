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
    public static Resolution ItemsOf(NodeRunResult output, string collection, string loopLabel, string? labelField = null)
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

        return ItemsFrom(value, $"{TemplateObjects.Node}.{source.NodeId}.{TemplateMembers.Value}", source.Fields, loopLabel, labelField);
    }

    /// <summary>
    /// #1341 (v30 design § 2.2): an inner loop over a list field of its OUTER item
    /// (<c>item.value.&lt;field…&gt;</c>) — read from the outer item's value as a node's
    /// value is, with the same sentences, its walk spelled from <c>item.value</c>.
    /// </summary>
    public static Resolution ItemsOfOuterItem(IReadOnlyDictionary<string, string> outer, string collection, string loopLabel, string? labelField = null)
    {
        const string prefix = "item.value.";
        if (!collection.StartsWith(prefix, StringComparison.Ordinal) || collection.Length == prefix.Length)
        {
            return new Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), null, $"{loopLabel} iterates {collection}, which is not a field of the outer item.");
        }

        JsonNode? value;
        try
        {
            value = JsonNode.Parse(outer.GetValueOrDefault("value") ?? string.Empty);
        }
        catch (JsonException)
        {
            return new Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), null, $"{loopLabel} could not read its list: the outer item's value is not JSON.");
        }

        return ItemsFrom(value, "item.value", collection[prefix.Length..].Split('.'), loopLabel, labelField);
    }

    /// <summary>#1341: a nested instance's items, each carrying its path (<c>outer/id</c>) beside its own id.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> WithPath(IEnumerable<IReadOnlyDictionary<string, string>> items, string outerPath) =>
        items.Select(item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(item, StringComparer.Ordinal)
        {
            [WorkflowItemPath.PathMember] = WorkflowItemPath.Of(new[] { outerPath, item["id"] }),
        }).ToList();

    private static Resolution ItemsFrom(JsonNode? value, string walked, IReadOnlyList<string> fields, string loopLabel, string? labelField)
    {
        foreach (var field in fields)
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

        // The same ceiling as an array input's elements: each item runs the body's
        // model calls, so a reply listing thousands would fan without bound. Too
        // many fails the job closed, by name — never a silent truncation.
        if (list.Count > ConsultGenerationJobs.MaxArrayElements)
        {
            return new Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), null, $"{loopLabel} could not run: {walked} has {list.Count} items, more than {ConsultGenerationJobs.MaxArrayElements}.");
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
                    ["name"] = NameOf(element, labelField) ?? $"{loopLabel} {position}",
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
    /// v30 (#1344, design § 5): an element named by its loop's <c>labelField</c> — the
    /// field's text, trimmed; null (label plus position) when the field is blank,
    /// absent or not text, or the loop declares none (always, below 30).
    /// </summary>
    internal static string? NameOf(JsonNode? element, string? labelField) =>
        labelField is not null
            && element is JsonObject obj
            && obj.TryGetPropertyValue(labelField, out var field)
            && field is JsonValue value
            && value.TryGetValue(out string? text)
            && !string.IsNullOrWhiteSpace(text)
                ? WorkflowItemNames.FromField(text)
                : null;

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
