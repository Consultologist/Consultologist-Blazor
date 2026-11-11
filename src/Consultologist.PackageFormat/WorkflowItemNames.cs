namespace Consultologist.PackageFormat;

/// <summary>
/// v30 (#1344, design § 5, revision 3): a name drawn from an item's own field. It is
/// element text that the record keeps past the retention sweep, so it is bounded:
/// trimmed, then cut to <see cref="MaxLength"/> characters ending in "…". The whole
/// text reaches only the composed note (produced text, deleted with it).
/// </summary>
public static class WorkflowItemNames
{
    /// <summary>The most characters a field-derived name keeps, the ellipsis included.</summary>
    public const int MaxLength = 120;

    public const string Ellipsis = "…";

    /// <summary>The field's text as a name: trimmed, and at most <see cref="MaxLength"/> characters.</summary>
    public static string FromField(string text)
    {
        var name = text.Trim();
        if (name.Length <= MaxLength)
        {
            return name;
        }

        // Never split a surrogate pair at the cut.
        var keep = MaxLength - Ellipsis.Length;
        if (char.IsHighSurrogate(name[keep - 1]))
        {
            keep--;
        }

        return name[..keep].TrimEnd() + Ellipsis;
    }
}
