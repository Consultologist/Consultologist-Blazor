using System.Globalization;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1503 (specVersion 31, area 06): what a job starts with: each input by id as part 02 reads it
/// (<c>{ value, content, title }</c>; an input not supplied is absent), the run facts (<c>run.*</c>), and the profile's fields.
/// </summary>
public sealed record RunInputs(IReadOnlyDictionary<string, JsonNode?> Inputs, JsonObject RunFacts, IReadOnlyDictionary<string, string?> Profile);

/// <summary>
/// #1503 (specVersion 31, area 06; part 03 § 6.1): the data a rule or a body is given: one object whose members are the
/// roots, holding exactly the paths it reads and nothing else. A path's value is placed at the path itself, as nested objects
/// along its dots (<c>node.sections.outline.value</c> under <c>node</c>, <c>sections</c>, <c>outline</c>). A node, check or
/// deliverable is read through the instance its key names as seen from the reader's frame (<see cref="RunFrame.Find"/>);
/// one skipped, not run or absent reads <c>null</c> at <c>.value</c> and <c>.content</c>, and its <c>.state</c> says which.
/// An input not supplied reads <c>null</c> throughout. Never throws on a path; a path that names nothing reads <c>null</c>.
/// </summary>
internal sealed class RunScope(RunInputs inputs)
{
    /// <summary>The state an absent thing reads (part 03 § 6.1).</summary>
    public const string NotRun = "not_run";

    /// <summary>The read form of something with no value: <c>{ value: null, content: null, state }</c>.</summary>
    public static JsonObject Absent(string state) => new() { ["value"] = null, ["content"] = null, ["state"] = state };

    /// <summary>
    /// The data for <paramref name="entry"/> running in <paramref name="frame"/>, holding <paramref name="paths"/> (its
    /// <see cref="PlanEntry.Reads"/>, or some of them). A path under one already placed is in it already.
    /// </summary>
    public JsonObject Data(PlanEntry entry, RunFrame frame, IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(paths);

        var data = new JsonObject();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var segments = path.Split('.');
            if (segments.Any(s => s.Length == 0) || Covered(segments, placed))
                continue;
            Place(data, segments, Value(segments, frame));
            placed.Add(path);
        }

        return data;
    }

    /// <summary>Whether a proper prefix of <paramref name="segments"/> (at a dot) is already placed whole.</summary>
    private static bool Covered(string[] segments, HashSet<string> placed)
    {
        for (var length = 1; length < segments.Length; length++)
        {
            if (placed.Contains(string.Join('.', segments, 0, length)))
                return true;
        }

        return false;
    }

    /// <summary>Sets <paramref name="value"/> at <paramref name="segments"/> under <paramref name="data"/>, making the objects on the way.</summary>
    private static void Place(JsonObject data, string[] segments, JsonNode? value)
    {
        var at = data;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (!at.TryGetPropertyValue(segments[i], out var next))
            {
                next = new JsonObject();
                at[segments[i]] = next;
            }

            if (next is not JsonObject inner)
                return; // a value already placed there that is not an object: the path is under it
            at = inner;
        }

        at[segments[^1]] = value?.DeepClone();
    }

    /// <summary>What the path <paramref name="segments"/> reads from <paramref name="frame"/>: a node in the plan's JSON or a run's, never cloned.</summary>
    private JsonNode? Value(string[] segments, RunFrame frame)
    {
        switch (segments[0])
        {
            case "input":
                if (segments.Length < 2)
                    return null;
                return Walk(inputs.Inputs.GetValueOrDefault(segments[1]), segments, 2);
            case "run":
                return Walk(inputs.RunFacts, segments, 1);
            case "profile":
                if (segments.Length < 2)
                    return null;
                return segments.Length == 2 && inputs.Profile.GetValueOrDefault(segments[1]) is { } field ? JsonValue.Create(field) : null;
            case "item":
                return Walk(frame.Binding(f => f.Item != null)?.Item, segments, 1);
            case "pass":
                return Walk(frame.Binding(f => f.Pass != null)?.Pass, segments, 1);
            case "param":
                if (segments.Length < 2)
                    return null;
                return Walk(frame.Binding(f => f.Parameters != null)?.Parameters?.GetValueOrDefault(segments[1]), segments, 2);
            case "node" or "check" or "deliverable":
                return Keyed(segments, frame);
            default:
                return null;
        }
    }

    /// <summary>A read under <c>node.</c>, <c>check.</c> or <c>deliverable.</c>: the longest key of the frame's map it names, read through its instance.</summary>
    private static JsonNode? Keyed(string[] segments, RunFrame frame)
    {
        for (var length = segments.Length; length >= 2; length--)
        {
            var key = string.Join('.', segments, 0, length);
            if (!frame.Map.ContainsKey(key))
                continue;
            var instance = frame.Find(key);
            var read = instance is { IsSettled: true, Read: { } settled } ? settled : Absent(NotRun);
            return Walk(read, segments, length);
        }

        return null;
    }

    /// <summary><paramref name="node"/> walked by <paramref name="segments"/> from <paramref name="from"/>: members of objects, positions in lists.</summary>
    private static JsonNode? Walk(JsonNode? node, string[] segments, int from)
    {
        var at = node;
        for (var i = from; i < segments.Length && at != null; i++)
        {
            at = at switch
            {
                JsonObject obj => obj.TryGetPropertyValue(segments[i], out var member) ? member : null,
                JsonArray list when int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < list.Count => list[index],
                _ => null,
            };
        }

        return at;
    }
}
