using System.Globalization;

namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1509 (Task 15; part 09 § 6.4 "Nodes" and "Checks"): one node of the tree History shows. <see cref="Row"/> is null for a
/// node a decision names that never got a row (its <see cref="Status"/> is then <c>NotRun</c>). An item or a pass of a loop is
/// a node of its own (its row), and holds the rows that ran in it.
/// </summary>
public sealed record TreeNode31(
    string Key, string Title, string Kind, string Status, Row31? Row,
    IReadOnlyList<Row31> Decisions, IReadOnlyList<TreeNode31> Children);

/// <summary>
/// #1509 (Task 15): a job's node (or check) rows as a tree: loops expand per item, a <c>while</c> per pass, a <c>call</c> into
/// the rows keyed under it; a decision sits on the node it concerns. Built on <see cref="Record31.ParentOf"/> and
/// <see cref="Record31.ChildrenOf"/>, never by splitting a key. Pure.
/// </summary>
public static class NodeTree31
{
    /// <summary>The Nodes section.</summary>
    public static IReadOnlyList<TreeNode31> Build(Record31 record) => BuildSection(record, "node");

    /// <summary>The Checks section; a check loop's rows carry their <c>alignedWith</c> member.</summary>
    public static IReadOnlyList<TreeNode31> BuildChecks(Record31 record) => BuildSection(record, "check");

    /// <summary>
    /// An item's title: the name its row holds (kept for package text), else the retained name while it is retained, else
    /// <c>&lt;loop label&gt; &lt;position&gt;</c> (<i>Sections 3</i>), the position being the item's number in the key, 1-based as stored.
    /// </summary>
    public static string ItemTitle(Row31 row, string? retainedName) => ItemTitle(row, retainedName, null);

    private static string ItemTitle(Row31 row, string? retainedName, Row31? loop)
    {
        if (!string.IsNullOrWhiteSpace(row.Text("itemName")))
        {
            return row.Text("itemName")!;
        }

        if (!string.IsNullOrWhiteSpace(retainedName))
        {
            return retainedName;
        }

        var steps = RowKey31.Parse(row.Key).Steps;
        var last = steps.Count > 0 ? steps[^1] : null;
        var label = loop?.Text("label") is { Length: > 0 } loopLabel ? loopLabel
            : row.Text("label") is { Length: > 0 } text ? text : last?.Scope ?? row.Key;
        var item = last is { Items.Count: > 0 } ? last.Items[^1] : null;
        return item is null ? label : $"{label} {item}";
    }

    private static IReadOnlyList<TreeNode31> BuildSection(Record31 record, string section)
    {
        var rows = section == "node" ? record.Nodes : record.Checks;
        var ghosts = GhostOwners(record, section, rows);

        var childrenOf = new Dictionary<string, List<(string Key, Row31? Row)>>(StringComparer.Ordinal);
        var top = new List<(string Key, Row31? Row)>();
        foreach (var row in rows)
        {
            Place(childrenOf, top, record.ParentOf(row.Key)?.Key, (row.Key, row));
        }

        foreach (var key in ghosts)
        {
            Place(childrenOf, top, record.ParentOf(key)?.Key, (key, null));
        }

        return top
            .OrderBy(entry => StartedAt(entry.Row) is null ? 1 : 0)
            .ThenBy(entry => StartedAt(entry.Row) ?? DateTime.MinValue)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => Make(record, childrenOf, entry.Key, entry.Row))
            .ToList();
    }

    private static void Place(Dictionary<string, List<(string Key, Row31? Row)>> childrenOf, List<(string Key, Row31? Row)> top,
        string? parent, (string Key, Row31? Row) entry)
    {
        if (parent is null)
        {
            top.Add(entry);
        }
        else if (childrenOf.TryGetValue(parent, out var list))
        {
            list.Add(entry);
        }
        else
        {
            childrenOf[parent] = [entry];
        }
    }

    private static TreeNode31 Make(Record31 record, Dictionary<string, List<(string Key, Row31? Row)>> childrenOf, string key, Row31? row)
    {
        var children = childrenOf.TryGetValue(key, out var list)
            ? list.OrderBy(entry => entry.Key, RowKeyOrder31.Instance).Select(entry => Make(record, childrenOf, entry.Key, entry.Row)).ToList()
            : [];
        return new TreeNode31(key, TitleOf(record, key, row), row?.Text("kind") ?? "", row?.Text("status") ?? "NotRun",
            row, record.DecisionsOwnedBy(key), children);
    }

    private static string TitleOf(Record31 record, string key, Row31? row)
    {
        if (row is null)
        {
            return LastScope(key);
        }

        var parent = record.ParentOf(key);
        var kind = row.Text("kind");
        if (parent is not null && SameEntry(parent.Key, key) && parent.Text("kind") == kind)
        {
            if (kind == "for")
            {
                return ItemTitle(row, null, parent);
            }

            if (kind == "while")
            {
                var pass = row.Int("pass") ?? RowKey31.Parse(key).Steps[^1].Pass;
                return pass is null ? LastScope(key) : $"Pass {pass}";
            }
        }

        return row.Text("label") is { Length: > 0 } label ? label : LastScope(key);
    }

    private static bool SameEntry(string a, string b) =>
        string.Equals(a[..RowKey31.InstanceStart(a)], b[..RowKey31.InstanceStart(b)], StringComparison.Ordinal);

    private static string LastScope(string key)
    {
        var steps = RowKey31.Parse(key).Steps;
        return steps.Count > 0 ? steps[^1].Scope : key;
    }

    private static DateTime? StartedAt(Row31? row) =>
        row?.Text("startedAtUtc") is { } text
        && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;

    private static string OwnerOf(string id)
    {
        var at = id.IndexOf("/if:", StringComparison.Ordinal);
        return at < 0 ? "" : id[..at];
    }

    /// <summary>Owners of this section's decisions that have no row, once each, in key order.</summary>
    private static List<string> GhostOwners(Record31 record, string section, IReadOnlyList<Row31> rows)
    {
        var have = rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        var prefix = section + ".";
        return record.Decisions
            .Select(decision => decision.Text("owner") ?? OwnerOf(decision.Key))
                        .Where(owner => owner.StartsWith(prefix, StringComparison.Ordinal) && !have.Contains(owner))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(owner => owner, RowKeyOrder31.Instance)
            .ToList();
    }
}
