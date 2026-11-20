namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1499: a file split at its front matter fences, as part 01 § 4 states them.
/// </summary>
/// <param name="Header">The text between the fences, without the line ends next to them.</param>
/// <param name="Body">Everything after the closing fence's line end; empty when the file ends there.</param>
public sealed record FrontMatterSplit(string Header, string Body);

/// <summary>
/// #1499: the split of a file with a JSON header (<c>---</c>, the header, <c>---</c>, the body).
/// The text is already <c>\n</c>-normalised; a fence is a whole line of exactly <c>---</c>.
/// </summary>
public static class FrontMatter
{
    private const string Fence = "---";

    /// <summary>Splits <paramref name="text"/> at its first two fence lines; null when either is missing.</summary>
    public static FrontMatterSplit? Split(string text)
    {
        if (!text.StartsWith(Fence + "\n", StringComparison.Ordinal))
            return null;

        var headerStart = Fence.Length + 1;
        var lineStart = headerStart;
        while (lineStart <= text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            var end = lineEnd < 0 ? text.Length : lineEnd;
            if (end - lineStart == Fence.Length
                && string.CompareOrdinal(text, lineStart, Fence, 0, Fence.Length) == 0)
            {
                var header = lineStart == headerStart ? "" : text[headerStart..(lineStart - 1)];
                var body = lineEnd < 0 ? "" : text[(lineEnd + 1)..];
                return new FrontMatterSplit(header, body);
            }

            if (lineEnd < 0)
                return null;
            lineStart = lineEnd + 1;
        }

        return null;
    }

    /// <summary>True when <paramref name="body"/> holds nothing but whitespace.</summary>
    public static bool IsEmptyBody(string body) => string.IsNullOrWhiteSpace(body);
}
