using System.Text.RegularExpressions;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499 (specVersion 31, area 02): the id rules of part 01 § 6, in the order the reader
/// applies them: the pattern (S01-030), the length (S01-031), the names Windows reserves
/// (S01-032), then the words references read (S01-033).
/// </summary>
public static class SourceIds
{
    private static readonly Regex Pattern = new("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The plural reserved words and what they are read as: <c>value(s)</c>, <c>content(s)</c>,
    /// <c>items</c> and <c>passes</c> are members; the rest are roots. The word named is the
    /// form read in references: the singular, except <c>items</c> and <c>passes</c> (D10).
    /// </summary>
    private static readonly Dictionary<string, (string Kind, string Word)> Members = new()
    {
        ["value"] = ("member", "value"),
        ["values"] = ("member", "value"),
        ["content"] = ("member", "content"),
        ["contents"] = ("member", "content"),
        ["items"] = ("member", "items"),
        ["passes"] = ("member", "passes"),
    };

    /// <summary>
    /// The first rule <paramref name="id"/> fails, or null when it is a good id. <paramref name="path"/>
    /// is the file the id belongs to and is carried on the refusal.
    /// </summary>
    public static SourceRefusal? Check(string id, string path)
    {
        if (CheckPatternAndLength(id, path) is { } refusal)
        {
            return refusal;
        }

        if (ReservedIds.Windows.Contains(id))
        {
            return SourceSentences.IdReservedOnWindows(id, path);
        }

        if (ReservedIds.Words.Contains(id))
        {
            if (Members.TryGetValue(id, out var member))
            {
                return SourceSentences.IdReserved(id, member.Kind, member.Word, path);
            }

            // Every other reserved word is a root; a plural is read as its singular.
            var word = id.EndsWith('s') && ReservedIds.Words.Contains(id[..^1]) ? id[..^1] : id;
            return SourceSentences.IdReserved(id, "root", word, path);
        }

        return null;
    }

    /// <summary>
    /// The pattern (S01-030) and the length (S01-031) alone, without the reserved names: the rule
    /// a format's property names follow (#1500, part 02 § 5.3). Null when <paramref name="id"/> passes both.
    /// </summary>
    internal static SourceRefusal? CheckPatternAndLength(string id, string path)
    {
        // The pattern's $ would also accept a trailing newline; ordinal checks keep it exact.
        if (!IsPattern(id))
        {
            return SourceSentences.IdNotValid(id, path);
        }

        if (id.Length > SourceBounds.IdLength)
        {
            return SourceSentences.IdTooLong(id, path);
        }

        return null;
    }

    /// <summary>Whether <paramref name="id"/> matches <c>^[a-z][a-z0-9_]*$</c> exactly.</summary>
    internal static bool IsPattern(string id) => Pattern.IsMatch(id) && !id.EndsWith('\n');
}
