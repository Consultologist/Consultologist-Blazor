namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1543 (ruling 4, D12): which node rows a blocked deliverable read. A blocked deliverable's reads are the paths its
/// substitutions and sites name (<see cref="TemplateReads31.Group"/>, never parsed a second way) and, for a site inside a
/// template <c>for</c>, the source its <c>loop</c> names (the final review, finding 2: the engine records
/// <c>{{ for part in node.sections.items }}{{ part.value }}</c>'s site as <c>part.value</c>, with
/// <c>part in node.sections.items</c> in its <c>loop</c>), each followed through the loop variables the deliverable's sites
/// record to the outermost source (<c>for b in a.value.parts</c> inside <c>for a in node.x.items</c> reads
/// <c>node.x</c>). A read belongs to the node
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
            var sites = TemplateReads31.Group(deliverable);
            var loops = sites.Select(site => LoopOf(site.Loop)).OfType<(string Variable, string Source)>().Distinct().ToList();
            var reads = sites
                .SelectMany(site => site.Passes.Select(pass => pass.Reads).Append(site.Expr).Append(LoopOf(site.Loop)?.Source ?? ""))
                .Where(read => read.Length > 0)
                .SelectMany(read => Outermost(read, loops, 0))
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

    /// <summary>
    /// A site's <c>loop</c> as the engine writes it (part 08 § 10: the nearest <c>for</c> around the site as written,
    /// <c>&lt;variable&gt; in &lt;source&gt;</c>): the variable and the source's path (its leading dotted name; a pipe or
    /// argument after it is not a read of a node); none for a site in no loop or a loop not in that form.
    /// </summary>
    private static (string Variable, string Source)? LoopOf(string? loop)
    {
        if (loop is null || loop.IndexOf(" in ", StringComparison.Ordinal) is not (> 0 and var at))
        {
            return null;
        }

        var variable = loop[..at].Trim();
        var source = loop[(at + 4)..].TrimStart(' ', '(');
        var end = 0;
        while (end < source.Length && (char.IsLetterOrDigit(source[end]) || source[end] is '_' or '.' or ':'))
        {
            end++;
        }

        return variable.Length > 0 && end > 0 ? (variable, source[..end]) : null;
    }

    /// <summary>
    /// <paramref name="read"/> followed through the deliverable's loop variables to the outermost sources: a read whose head
    /// is a variable some site's loop binds (<c>line</c> of <c>line in sec.value.lines</c>, <c>sec</c> of
    /// <c>sec in node.sections.items</c>) is the source of that loop, followed again; a variable two loops bind is followed
    /// through both (each is marked: an extra mark keeps text from being copied, a missing one would not). A head no
    /// recorded loop binds is the read itself.
    /// </summary>
    private static IEnumerable<string> Outermost(string read, IReadOnlyList<(string Variable, string Source)> loops, int depth)
    {
        var head = read.Split('.', 2)[0];
        var sources = loops.Where(loop => loop.Variable == head && loop.Source != read).Select(loop => loop.Source).ToList();
        if (sources.Count == 0 || depth > loops.Count)
        {
            return [read];
        }

        return sources.SelectMany(source => Outermost(source, loops, depth + 1));
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
