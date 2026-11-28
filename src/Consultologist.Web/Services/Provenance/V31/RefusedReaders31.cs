namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1543 (ruling 4, D12): which node rows a blocked deliverable read. A blocked deliverable's reads are the paths its
/// substitutions and sites name (<see cref="TemplateReads31.Group"/>, never parsed a second way). A read belongs to the node
/// row whose key it starts with (the longest such key, the next character ending the key: <c>.</c>, <c>:</c> or <c>/</c>);
/// that row and every row <see cref="Record31.ChildrenOf"/> reaches from it (its item, pass and call rows) are marked.
/// Membership is by the record's own parent and child relations, not by key prefix alone; nodes further upstream are not
/// marked. Pure.
/// </summary>
public static class RefusedReaders31
{
    /// <summary>Node row key to the ids of the blocked deliverables that read it, each id once, in ordinal order.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Of(Record31 record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var marked = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var deliverable in record.Deliverables.Where(row => string.Equals(row.Text("state"), "blocked", StringComparison.Ordinal)))
        {
            var id = IdOf(deliverable.Key);
            var reads = TemplateReads31.Group(deliverable)
                .SelectMany(site => site.Passes.Select(pass => pass.Reads).Append(site.Expr))
                .Where(read => read.Length > 0)
                .Distinct(StringComparer.Ordinal);
            foreach (var read in reads)
            {
                if (NodeNamedBy(record, read) is { } node)
                {
                    Mark(record, node, id, marked);
                }
            }
        }

        return marked.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToList(), StringComparer.Ordinal);
    }

    private static void Mark(Record31 record, Row31 node, string id, SortedDictionary<string, SortedSet<string>> marked)
    {
        if (!marked.TryGetValue(node.Key, out var ids))
        {
            marked[node.Key] = ids = new SortedSet<string>(StringComparer.Ordinal);
        }

        if (ids.Add(id))
        {
            foreach (var child in record.ChildrenOf(node.Key))
            {
                Mark(record, child, id, marked);
            }
        }
    }

    private static Row31? NodeNamedBy(Record31 record, string read)
    {
        Row31? best = null;
        foreach (var node in record.Nodes)
        {
            if (read.StartsWith(node.Key, StringComparison.Ordinal)
                && (read.Length == node.Key.Length || read[node.Key.Length] is '.' or ':' or '/')
                && (best is null || node.Key.Length > best.Key.Length))
            {
                best = node;
            }
        }

        return best;
    }

    /// <summary>A deliverable's id as the sentences name it: its key without the root (<c>deliverable.note</c> is <c>note</c>).</summary>
    private static string IdOf(string key) =>
        RowKey31.Parse(key) is { Root: "deliverable", Path: { Length: > 0 } path } ? path : key;
}
