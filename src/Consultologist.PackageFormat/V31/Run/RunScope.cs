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
/// one skipped, not run or absent reads <c>null</c> at <c>.value</c> and <c>.content</c>, and its <c>.state</c> says which
/// (a flow node in a branch not taken reads <c>skipped</c>, part 04 § 2.5). #1505 (area 08; D3): a deliverable reads by
/// reader: to an entry that reads it composed (<see cref="PlanEntry.ReadsComposed"/>), its composed form once composed;
/// to any other, its outcome's form (R1, R2: <c>null</c> at <c>.value</c> and <c>.content</c> when skipped or blocked).
/// <c>pass</c> reads the pass the reader runs in (part 04 § 3.3): <c>pass.node.&lt;id&gt;</c> (after any <c>parent</c>s)
/// this pass's node, read as <c>node.&lt;id&gt;</c> is; the rest (<c>number</c>, <c>previous</c>, <c>history</c>) the
/// pass's binding.
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
            Place(data, segments, Value(segments, frame, entry));
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
    private JsonNode? Value(string[] segments, RunFrame frame, PlanEntry reader)
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
                return Pass(segments, frame, reader);
            case "param":
                if (segments.Length < 2)
                    return null;
                return Walk(frame.Binding(f => f.Parameters != null)?.Parameters?.GetValueOrDefault(segments[1]), segments, 2);
            case "node" or "check" or "deliverable":
                return Keyed(segments, frame, reader);
            default:
                return null;
        }
    }

    /// <summary>A read under <c>node.</c>, <c>check.</c> or <c>deliverable.</c>: the longest key of the frame's map it names, read through its instance.</summary>
    private static JsonNode? Keyed(string[] segments, RunFrame frame, PlanEntry reader)
    {
        for (var length = segments.Length; length >= 2; length--)
        {
            var key = string.Join('.', segments, 0, length);
            if (!frame.Map.ContainsKey(key))
                continue;
            return Walk(Read(key, frame, reader), segments, length);
        }

        return null;
    }

    /// <summary>
    /// What <paramref name="key"/> reads from <paramref name="frame"/>: its settled instance's read form; with none, absent:
    /// <c>skipped</c> for a flow node whose nearest visible ancestor is a settled <c>if</c> or <c>case</c> (it sits in a
    /// branch not taken, part 04 § 2.5), else <c>not_run</c>. #1505 (D3): to a <paramref name="reader"/> that reads
    /// <paramref name="key"/> composed, a composed deliverable reads its composed form.
    /// </summary>
    internal static JsonObject Read(string key, RunFrame frame, PlanEntry? reader = null)
    {
        var instance = frame.Find(key);
        if (instance is { Composed: { } composed } && reader != null && reader.ReadsComposed.Contains(key))
            return composed;
        if (instance is { IsSettled: true, Read: { } settled })
            return settled;
        if (instance != null || frame.Map.GetValueOrDefault(key) is not { Kind: PlanKind.For or PlanKind.While or PlanKind.If or PlanKind.Case } entry)
            return Absent(NotRun);

        var steps = 0;
        for (var at = entry.Parent; at != null && steps++ <= frame.Map.Count; at = frame.Map.GetValueOrDefault(at)?.Parent)
        {
            if (frame.Find(at) is { } holder)
                return Absent(holder is { IsSettled: true, Entry.Kind: PlanKind.If or PlanKind.Case } ? "skipped" : NotRun);
        }

        return Absent(NotRun);
    }

    /// <summary>
    /// A read under <c>pass</c>: past its <c>parent</c>s, the pass of that while (the nearest frame binding one, then the
    /// next outward, within the map); <c>node.&lt;id&gt;</c> there is that pass's node of that id, read through
    /// <see cref="Read"/>; anything else walks the pass's binding.
    /// </summary>
    private static JsonNode? Pass(string[] segments, RunFrame frame, PlanEntry reader)
    {
        var at = 1;
        var pass = frame.Binding(f => f.Pass != null);
        while (at < segments.Length && segments[at] == "parent" && pass != null)
        {
            at++;
            pass = pass.Parent?.Binding(f => f.Pass != null);
        }

        if (pass == null)
            return null;
        if (at + 1 < segments.Length && segments[at] == "node" && pass.Owner is { } loop
            && PassNode(frame.Map, e => e.Id, e => e.Parent, loop.Entry.Key, segments[at + 1]) is { } key)
        {
            return Walk(Read(key, frame, reader), segments, at + 2);
        }

        return Walk(pass.Pass, segments, at);
    }

    /// <summary>
    /// The key <c>pass….node.&lt;id&gt;</c> names in the while <paramref name="loop"/>: the entry of <paramref name="map"/>
    /// with that <paramref name="id"/> at any depth inside it (part 01: ids are unique within a while at any depth; the
    /// first in ordinal order should a document repeat one); null for none. The one resolution both
    /// <see cref="RunPlan"/>'s dependency edge and the run's live read use.
    /// </summary>
    internal static string? PassNode<T>(IReadOnlyDictionary<string, T> map, Func<T, string> idOf, Func<T, string?> parentOf, string loop, string id)
    {
        string? found = null;
        foreach (var (key, entry) in map)
        {
            if (idOf(entry) == id && (found == null || string.CompareOrdinal(key, found) < 0) && Inside(map, parentOf, key, loop))
                found = key;
        }

        return found;
    }

    /// <summary>Whether <paramref name="key"/>'s chain of parents in <paramref name="map"/> reaches <paramref name="ancestor"/> (bounded by the map's size).</summary>
    internal static bool Inside<T>(IReadOnlyDictionary<string, T> map, Func<T, string?> parentOf, string key, string ancestor)
    {
        var steps = 0;
        for (var at = map.TryGetValue(key, out var entry) ? parentOf(entry) : null; at != null && steps++ <= map.Count;
             at = map.TryGetValue(at, out var up) ? parentOf(up) : null)
        {
            if (at == ancestor)
                return true;
        }

        return false;
    }

    /// <summary>
    /// #1506 (area 09; D4): the value at <paramref name="path"/> in <paramref name="data"/>, data <see cref="Data"/> gave (a
    /// path under one placed whole walks into it as the read does); null when nothing is there. Never cloned.
    /// </summary>
    public static JsonNode? At(JsonObject data, string path)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(path);
        return Walk(data, path.Split('.'), 0);
    }

    /// <summary>The flags of a read the runner cannot trace (D4): both, so model output is never marked not model-derived.</summary>
    private static readonly (bool RunDependent, bool ModelDerived) Untraced = (true, true);

    /// <summary>The flags of a read of nothing run-dependent or model-derived (a typed input, package text, a count).</summary>
    private static readonly (bool RunDependent, bool ModelDerived) Clean = (false, false);

    /// <summary>
    /// #1506 (area 09; part 08 § 5.1; D4, R5): whether <paramref name="path"/>, read by <paramref name="entry"/>'s rule in
    /// <paramref name="frame"/>, is run-dependent and model-derived: <c>run.*</c> is run-dependent; an input (typed or
    /// uploaded) and a snippet folder (package text) are neither; <c>node.*</c>, <c>check.*</c> and <c>deliverable.*</c> take
    /// the flags of the entry they name (its <c>runDependent</c> and <c>modelDerived</c> as <c>resolve.json</c> writes them);
    /// <c>item</c> (past its <c>parent</c>s) the flags of its loop's source, read where the loop runs; <c>pass</c> (past its
    /// <c>parent</c>s, through <c>previous</c> and <c>history</c>) the flags of the body node it names, a pass's
    /// <c>number</c> neither, and a whole pass every node of its while; <c>param.&lt;id&gt;</c> the flags of the call's
    /// argument, read where the call runs (the literal <c>null</c> neither). Anything else, or a read that names nothing, is
    /// both. Bounded by the nesting of loops and calls.
    /// </summary>
    public static (bool RunDependent, bool ModelDerived) Lineage(PlanEntry entry, RunFrame frame, string path)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(path);
        return Lineage(entry, frame, path, 0);
    }

    private static (bool RunDependent, bool ModelDerived) Lineage(PlanEntry entry, RunFrame frame, string path, int depth)
    {
        var segments = path.Split('.');
        if (depth > MaxLineageDepth || segments.Any(s => s.Length == 0))
            return Untraced;
        switch (segments[0])
        {
            case "run":
                return (true, false);
            case "input" or "snippet":
                return Clean;
            case "node" or "check" or "deliverable":
                for (var length = segments.Length; length >= 2; length--)
                {
                    if (frame.Map.GetValueOrDefault(string.Join('.', segments, 0, length)) is { } named)
                        return Flags(named);
                }

                return Untraced;
            case "item":
            {
                var at = 1;
                var binding = frame.Binding(IsItemFrame);
                while (at < segments.Length && segments[at] == "parent" && binding != null)
                {
                    at++;
                    binding = binding.Parent?.Binding(IsItemFrame);
                }

                return binding?.Owner is { } loop && SourceLayout.Text(loop.Entry.Json["in"]) is { } source
                    ? Lineage(loop.Entry, loop.Frame, source, depth + 1)
                    : Untraced;
            }

            case "pass":
                return PassLineage(entry, frame, segments);
            case "param":
            {
                if (segments.Length < 2 || frame.Binding(f => f.Parameters != null)?.Owner is not { } call
                    || call.Entry.Json["with"] is not JsonObject arguments || !arguments.TryGetPropertyValue(segments[1], out var argument))
                {
                    return Untraced;
                }

                if (argument is null)
                    return Clean;
                return SourceLayout.Text(argument) is { } reference ? Lineage(call.Entry, call.Frame, reference, depth + 1) : Untraced;
            }

            default:
                return Untraced;
        }
    }

    /// <summary>More nested loops and calls than any plan the engine runs: a chain of sources past it is not traced.</summary>
    private const int MaxLineageDepth = 256;

    /// <summary>Whether <paramref name="frame"/> is a loop item's (opened by a <c>for</c>).</summary>
    private static bool IsItemFrame(RunFrame frame) => frame.Owner is { Entry.Kind: PlanKind.For };

    /// <summary>
    /// A read under <c>pass</c> (D4, R5): the while is found as <see cref="RunPlan"/>'s dependency edge finds it (the
    /// reader's own when it is a while, else its enclosing one; each <c>parent</c> the next outward); past
    /// <c>previous</c>s and <c>history.&lt;n&gt;</c>s, <c>node.&lt;id&gt;</c> is that while's node of that id,
    /// <c>number</c> is a count, and nothing more is the whole pass: every node of the while.
    /// </summary>
    private static (bool RunDependent, bool ModelDerived) PassLineage(PlanEntry reader, RunFrame frame, string[] segments)
    {
        var map = frame.Map;
        var at = 1;
        while (at < segments.Length && segments[at] == "parent")
            at++;

        var up = at - 1;
        string? loop = null;
        var steps = 0;
        for (var key = reader.Kind == PlanKind.While ? reader.Key : reader.Parent; key != null && steps++ <= map.Count; key = map.GetValueOrDefault(key)?.Parent)
        {
            if (map.GetValueOrDefault(key)?.Kind == PlanKind.While && up-- == 0)
            {
                loop = key;
                break;
            }
        }

        if (loop == null)
            return Untraced;

        while (at < segments.Length)
        {
            switch (segments[at])
            {
                case "previous":
                    at++;
                    continue;
                case "history":
                    at += 2;
                    continue;
                case "number":
                    return Clean;
                case "node" when at + 1 < segments.Length:
                    return PassNode(map, e => e.Id, e => e.Parent, loop, segments[at + 1]) is { } key ? Flags(map[key]) : Untraced;
                default:
                    return Untraced;
            }
        }

        var whole = Clean;
        foreach (var (key, inside) in map)
        {
            if (Inside(map, e => e.Parent, key, loop))
                whole = (whole.RunDependent || Flags(inside).RunDependent, whole.ModelDerived || Flags(inside).ModelDerived);
        }

        return whole;
    }

    /// <summary>An entry's flags as <c>resolve.json</c> writes them; a flag not written as a boolean is taken as true (D4).</summary>
    private static (bool RunDependent, bool ModelDerived) Flags(PlanEntry entry) => (Flag(entry, "runDependent"), Flag(entry, "modelDerived"));

    private static bool Flag(PlanEntry entry, string member) =>
        entry.Json[member] is not JsonValue value || !value.TryGetValue<bool>(out var flag) || flag;

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
