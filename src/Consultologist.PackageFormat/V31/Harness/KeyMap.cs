using System.Globalization;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.PackageFormat.V31.Harness;

/// <summary>
/// #1507 (specVersion 31, area 10; part 10 § 3.1; D11, D12; the fixture README's "What area 10 does with the captures"):
/// 30's instance key beside 31's row key. A 30 key is <c>&lt;node id&gt;</c> or <c>&lt;node id&gt;:&lt;path&gt;</c>, the path
/// one segment per enclosing <c>for</c> or <c>while</c>, outermost first, joined by <c>/</c>: a loop item's id (an array
/// input's counted from 0, a node's list from 1, a snippet folder's by item id) or a while's pass. A 31 key is
/// <see cref="RunKeys.Of"/>'s: the rooted entry key, the items after <c>:</c> joined by <c>/</c> (every list's counted from
/// 1, #1506 R3; a folder's by item id), each pass written <c>#&lt;n&gt;</c> where its while sits. The node is found through
/// <see cref="Migration31Result.Keys"/>, its loops through the plan's <see cref="PlanEntry.Parent"/> chain, and a 30 item of
/// an array input counts one more at 31. A deliverable's key is <c>deliverable.&lt;id&gt;</c> on both sides, through
/// <see cref="Migration31Result.Keys"/>. A key that cannot be mapped gives <c>null</c> (Review Focus 5: the caller reports it).
/// </summary>
public static class KeyMap
{
    private const string DeliverablePrefix = "deliverable.";

    /// <summary>The 31 row key of the 30 instance key <paramref name="key30"/>, or <c>null</c> when it cannot be mapped.</summary>
    public static string? To31(string key30, Migration31Result migrated, RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(key30);
        ArgumentNullException.ThrowIfNull(migrated);
        ArgumentNullException.ThrowIfNull(plan);

        if (key30.StartsWith(DeliverablePrefix, StringComparison.Ordinal))
            return migrated.Keys.TryGetValue(key30, out var deliverable) && plan.Entries.ContainsKey(deliverable) ? deliverable : null;

        var colon = key30.IndexOf(':', StringComparison.Ordinal);
        var id = colon < 0 ? key30 : key30[..colon];
        var segments = colon < 0 ? [] : key30[(colon + 1)..].Split('/');
        if (!migrated.Keys.TryGetValue(id, out var entryKey) || !plan.Entries.TryGetValue(entryKey, out var entry))
            return null;

        var loops = Loops(entry, plan);
        if (segments.Length != loops.Count)
            return null;

        var items = new List<string>();
        var passes = new List<(int Position, int Pass)>();
        for (var i = 0; i < loops.Count; i++)
        {
            var segment = segments[i];
            if (loops[i].Kind == PlanKind.While)
            {
                if (Whole(segment) is not { } pass || pass < 1)
                    return null;
                passes.Add((items.Count, pass));
            }
            else if (IsArrayInput(loops[i]))
            {
                if (Whole(segment) is not { } position)
                    return null;
                items.Add((position + 1).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                if (segment.Length == 0)
                    return null;
                items.Add(segment);
            }
        }

        return RunKeys.Of(entry.Key, items, passes);
    }

    /// <summary>The 30 instance key of the 31 row key <paramref name="key31"/>, or <c>null</c> when it cannot be mapped.</summary>
    public static string? To30(string key31, Migration31Result migrated, RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(key31);
        ArgumentNullException.ThrowIfNull(migrated);
        ArgumentNullException.ThrowIfNull(plan);

        var end = key31.IndexOfAny([':', '#']);
        var entryKey = end < 0 ? key31 : key31[..end];
        var owners = migrated.Keys.Where(k => k.Value == entryKey).Select(k => k.Key).ToList();

        if (entryKey.StartsWith(DeliverablePrefix, StringComparison.Ordinal))
        {
            var deliverable = owners.Where(k => k.StartsWith(DeliverablePrefix, StringComparison.Ordinal)).ToList();
            return end < 0 && deliverable is [var only] ? only : null;
        }

        owners = owners.Where(k => !k.StartsWith(DeliverablePrefix, StringComparison.Ordinal)).ToList();
        if (owners is not [var id] || !plan.Entries.TryGetValue(entryKey, out var entry))
            return null;

        var tokens = Tokens(end < 0 ? "" : key31[end..]);
        if (tokens == null)
            return null;

        var loops = Loops(entry, plan);
        if (tokens.Count != loops.Count)
            return null;

        var segments = new List<string>();
        for (var i = 0; i < loops.Count; i++)
        {
            var (pass, value) = tokens[i];
            if (pass != (loops[i].Kind == PlanKind.While))
                return null;
            if (pass)
            {
                if (Whole(value) is not { } n || n < 1)
                    return null;
                segments.Add(value);
            }
            else if (IsArrayInput(loops[i]))
            {
                if (Whole(value) is not { } position || position < 1)
                    return null;
                segments.Add((position - 1).ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                segments.Add(value);
            }
        }

        return segments.Count == 0 ? id : $"{id}:{string.Join('/', segments)}";
    }

    /// <summary>The <c>for</c> and <c>while</c> entries around <paramref name="entry"/>, outermost first.</summary>
    private static List<PlanEntry> Loops(PlanEntry entry, RunPlan plan)
    {
        var loops = new List<PlanEntry>();
        var parent = entry.Parent;
        while (parent != null && plan.Entries.TryGetValue(parent, out var flow))
        {
            if (flow.Kind is PlanKind.For or PlanKind.While)
                loops.Insert(0, flow);
            parent = flow.Parent;
        }

        return loops;
    }

    /// <summary>Whether a <c>for</c> iterates an input's list, whose items 30 counted from 0.</summary>
    private static bool IsArrayInput(PlanEntry loop) =>
        SourceLayout.Text(loop.Json["in"]) is { } source && source.StartsWith("input.", StringComparison.Ordinal);

    /// <summary>
    /// The suffix of a 31 key (after the entry key) as its items and passes in order, a pass <c>(true, n)</c>: <c>#2:3</c> is
    /// <c>[(true, 2), (false, 3)]</c>; null when it is not <see cref="RunKeys.Of"/>'s form.
    /// </summary>
    private static List<(bool Pass, string Value)>? Tokens(string suffix)
    {
        var tokens = new List<(bool, string)>();
        var at = 0;
        var items = 0;
        while (at < suffix.Length)
        {
            var mark = suffix[at];
            var next = suffix.IndexOfAny([':', '/', '#'], at + 1);
            var value = next < 0 ? suffix[(at + 1)..] : suffix[(at + 1)..next];
            if (value.Length == 0)
                return null;
            switch (mark)
            {
                case '#':
                    tokens.Add((true, value));
                    break;
                case ':' when items == 0:
                case '/' when items > 0:
                    tokens.Add((false, value));
                    items++;
                    break;
                default:
                    return null;
            }

            at = next < 0 ? suffix.Length : next;
        }

        return tokens;
    }

    /// <summary>A whole number written in digits alone, without a sign or a leading zero; else null.</summary>
    private static int? Whole(string text) =>
        text.Length > 0 && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0')
        && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
