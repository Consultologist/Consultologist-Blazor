using System.Text;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1503 (specVersion 31, area 06; D13; part 04 §§ 2.6, 7.9): the one writer of an instance's row key. Area 09 owns the
/// final form; everything that names an instance goes through <see cref="Of"/>.
/// </summary>
public static class RunKeys
{
    /// <summary>
    /// The key of the instance of <paramref name="entryKey"/> at <paramref name="itemPath"/> (item ids, outermost loop first)
    /// and <paramref name="passes"/> (each while's pass and its position in the item path: the number of items outside it):
    /// the entry's key, then <c>:</c> and the item ids joined by <c>/</c>, each pass written <c>#&lt;n&gt;</c> where its
    /// while sits (<c>node.refine.judge#2</c>, <c>node.sections.paragraphs.draft_paragraph:2/1</c>,
    /// <c>node.x.y#2:3</c>). With no items and no passes it is the entry's key.
    /// </summary>
    public static string Of(string entryKey, IReadOnlyList<string> itemPath, IReadOnlyList<(int Position, int Pass)> passes)
    {
        ArgumentNullException.ThrowIfNull(entryKey);
        ArgumentNullException.ThrowIfNull(itemPath);
        ArgumentNullException.ThrowIfNull(passes);

        var key = new StringBuilder(entryKey);
        AppendPasses(key, passes, 0);
        for (var i = 0; i < itemPath.Count; i++)
        {
            key.Append(i == 0 ? ':' : '/').Append(itemPath[i]);
            AppendPasses(key, passes, i + 1);
        }

        return key.ToString();
    }

    private static void AppendPasses(StringBuilder key, IReadOnlyList<(int Position, int Pass)> passes, int position)
    {
        foreach (var (at, pass) in passes)
        {
            if (at == position)
                key.Append('#').Append(pass.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
