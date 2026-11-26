namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// One scope of a row key (<paramref name="Scope"/>, an id between dots), and, on the key's last step, the instance it
/// names: the 1-based pass (<paramref name="Pass"/>, the innermost <c>#n</c> the key writes; null for none) and the
/// item ids (<paramref name="Items"/>, outermost loop first, as the key writes them; empty for none).
/// </summary>
public sealed record KeyStep31(string Scope, int? Pass, IReadOnlyList<string> Items);

/// <summary>
/// #1509 (Task 13; part 08 § 2.1, § 2.3): a stored row's key read into its parts. The root is <c>node</c>, <c>check</c>
/// or <c>deliverable</c>, then the scope ids joined by <c>.</c>, then <c>:</c> and the item ids joined by <c>/</c> and
/// <c>#&lt;n&gt;</c> for a pass of a <c>while</c> where it sits (<c>node.refine.sections#2:3</c>); a delivery row's key is
/// <c>delivery:&lt;deliverable&gt;/&lt;destination&gt;</c> (root <c>delivery</c>, no steps). A sub-row id
/// (<c>node.a/render:1</c>, <c>node.a/macro:x</c>, <c>node.a/if:self</c>) is never a row key: its entry holds a <c>/</c>,
/// and an item path never holds <c>:</c>. <see cref="Parse"/> never throws; what it cannot read has root <c>""</c> and
/// <paramref name="Path"/> equal to <paramref name="Raw"/>. Pure.
/// </summary>
/// <param name="Root"><c>node</c>, <c>check</c>, <c>deliverable</c> or <c>delivery</c>; <c>""</c> when unparseable.</param>
/// <param name="Path">The key after the root and its separator (<c>refine.sections#2:3</c>); the key itself when unparseable.</param>
/// <param name="Steps">One step per scope id, in order; empty for a delivery key or an unparseable one.</param>
/// <param name="Raw">The key as given.</param>
public sealed record RowKey31(string Root, string Path, IReadOnlyList<KeyStep31> Steps, string Raw)
{
    private const string DeliveryPrefix = "delivery:";

    /// <summary>Reads <paramref name="key"/>; never throws.</summary>
    public static RowKey31 Parse(string key)
    {
        var raw = key ?? "";
        if (raw.StartsWith(DeliveryPrefix, StringComparison.Ordinal) && raw.Length > DeliveryPrefix.Length)
        {
            return new RowKey31("delivery", raw[DeliveryPrefix.Length..], [], raw);
        }

        var entryEnd = InstanceStart(raw);
        var entry = raw[..entryEnd];
        var dot = entry.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || !IsRoot(entry[..dot]) || entry.Contains('/', StringComparison.Ordinal))
        {
            return Unparseable(raw);
        }

        var scopes = entry[(dot + 1)..].Split('.');
        if (scopes.Any(scope => scope.Length == 0))
        {
            return Unparseable(raw);
        }

        if (!TryReadInstance(raw, entryEnd, out var pass, out var items))
        {
            return Unparseable(raw);
        }

        var steps = new List<KeyStep31>(scopes.Length);
        for (var i = 0; i < scopes.Length; i++)
        {
            var last = i == scopes.Length - 1;
            steps.Add(new KeyStep31(scopes[i], last ? pass : null, last ? items : []));
        }

        return new RowKey31(entry[..dot], raw[(dot + 1)..], steps, raw);
    }

    /// <summary>
    /// The row key of the item, pass or call the row sits in: the key without its last scope, its items and passes kept
    /// (<c>node.sections.paragraphs.draft_paragraph:2/1</c> is in <c>node.sections.paragraphs:2/1</c>;
    /// <c>node.hpi_section.standard_draft</c> is in the call <c>node.hpi_section</c>); null for a key with one scope, a
    /// deliverable, a delivery or an unparseable key. A loop's own item row is keyed as its loop is, so the key alone
    /// cannot tell it from a node in that item: a consumer looks the answer up with <c>Record31.Find</c>.
    /// </summary>
    public string? ParentInstance
    {
        get
        {
            if (Steps.Count < 2 || !string.Equals(Root, "node", StringComparison.Ordinal)
                && !string.Equals(Root, "check", StringComparison.Ordinal))
            {
                return null;
            }

            var entryEnd = InstanceStart(Raw);
            var scopes = string.Join('.', Steps.Take(Steps.Count - 1).Select(step => step.Scope));
            return $"{Root}.{scopes}{Raw[entryEnd..]}";
        }
    }

    /// <summary>Where the instance part of <paramref name="key"/> starts (its first <c>:</c> or <c>#</c>), or its length.</summary>
    internal static int InstanceStart(string key)
    {
        var at = key.IndexOfAny([':', '#']);
        return at < 0 ? key.Length : at;
    }

    private static bool IsRoot(string root) => root is "node" or "check" or "deliverable";

    private static RowKey31 Unparseable(string raw) => new("", raw, [], raw);

    /// <summary>
    /// <c>(#n)* ( : item ( / item | #n )* )?</c>: passes at the head, then the items from the outermost loop inward, each pass
    /// where its while sits. An item holding <c>:</c>, an empty item or a pass that is not digits fails.
    /// </summary>
    private static bool TryReadInstance(string key, int at, out int? pass, out List<string> items)
    {
        pass = null;
        items = [];
        var i = at;
        if (!ReadPasses(key, ref i, ref pass))
        {
            return false;
        }

        if (i == key.Length)
        {
            return true;
        }

        if (key[i] != ':')
        {
            return false;
        }

        i++;
        while (true)
        {
            var start = i;
            while (i < key.Length && key[i] is not ('/' or '#' or ':'))
            {
                i++;
            }

            if (i == start)
            {
                return false;
            }

            items.Add(key[start..i]);
            if (!ReadPasses(key, ref i, ref pass))
            {
                return false;
            }

            if (i == key.Length)
            {
                return true;
            }

            if (key[i] != '/')
            {
                return false;
            }

            i++;
        }
    }

    private static bool ReadPasses(string key, ref int i, ref int? pass)
    {
        while (i < key.Length && key[i] == '#')
        {
            i++;
            var start = i;
            while (i < key.Length && char.IsAsciiDigit(key[i]))
            {
                i++;
            }

            if (i == start || !int.TryParse(key.AsSpan(start, i - start), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            pass = number;
        }

        return true;
    }
}
