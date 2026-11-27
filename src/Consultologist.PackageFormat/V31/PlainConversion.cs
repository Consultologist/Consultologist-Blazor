using Markdig;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): the one Markdown-to-plain-text conversion (decision D13): Markdig's
/// <c>Markdown.ToPlainText</c> over the specVersion 31 pipeline, <see cref="ConsultMarkdown.Pipeline31"/> (part 02 § 5.2; #1510, G-D6), <c>\r\n</c> and <c>\r</c>
/// turned to <c>\n</c>, spaces, tabs and line breaks at the very end removed (everything else is Markdig's own output, a table's rows included, which run together). It is <c>plain_conversion</c> 1.0.0, pinned by goldens; the
/// <c>plain</c> operation and plain-text output both use it. Markdown Markdig
/// cannot convert gives <c>null</c>; <c>plain</c> then fails the rule (<see cref="JsonLogicFailureKind.PlainNotConverted"/>).
/// </summary>
public static class PlainConversion
{
    /// <summary>The conversion's version, as in <c>constants.json</c>.</summary>
    public const string Version = "1.0.0";

    /// <summary>
    /// Plain text for <paramref name="markdown"/>; <c>""</c> for null or empty text; <c>null</c> when Markdig cannot convert
    /// the text (it refuses Markdown nested too deeply, such as 200 nested quotes or 50,000 <c>[</c>), never the source
    /// in its place. Never throws.
    /// </summary>
    public static string? ToPlain(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return "";
        string plain;
        try
        {
            plain = Markdown.ToPlainText(markdown, ConsultMarkdown.Pipeline31);
        }
        catch (Exception)
        {
            return null;
        }
        return plain.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n', ' ', '\t');
    }
}
