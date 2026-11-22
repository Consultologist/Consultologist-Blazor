using System.Text;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 06 § 6.4; part 05 § 8.3; D7, D14): plain text written into Markdown so that it
/// displays as exactly that text. CommonMark's backslash escapes for ASCII punctuation: a backslash before each of the 32
/// ASCII punctuation characters. #1505 (area 08; the owner's ruling R9): the first space or tab leading each line is
/// written as its character reference (<c>&amp;#32;</c>, <c>&amp;#9;</c>), so a line led by four spaces or a tab is not
/// an indented code block. Nothing else changes (letters, digits, other white space, line breaks, which the pipeline makes
/// hard breaks, and any non-ASCII text stay as they are). A line starts at the start of the text and after each
/// <c>\n</c> or <c>\r</c>. The lock names it <c>markdown_escape</c> at <see cref="Version"/>.
/// </summary>
public static class MarkdownEscape
{
    /// <summary>The version of the escape, as <c>lock.json</c>'s <c>builtins</c> names it.</summary>
    public const string Version = "1.0.0";

    /// <summary><paramref name="plain"/> escaped, its start the start of a line.</summary>
    public static string Escape(string plain) => Escape(plain, startsLine: true);

    /// <summary>
    /// <paramref name="plain"/> with a backslash before each ASCII punctuation character and the first space or tab of each
    /// line as its character reference. With <paramref name="startsLine"/> <c>false</c> the text is a piece of a longer one
    /// that continues a line (the text after a run spot), so its start is not a line start.
    /// </summary>
    public static string Escape(string plain, bool startsLine)
    {
        ArgumentNullException.ThrowIfNull(plain);
        var escaped = new StringBuilder(plain.Length + (plain.Length / 8));
        for (var i = 0; i < plain.Length; i++)
        {
            var c = plain[i];
            var lineStart = i == 0 ? startsLine : plain[i - 1] is '\n' or '\r';
            if (lineStart && c is ' ')
                escaped.Append("&#32;");
            else if (lineStart && c is '\t')
                escaped.Append("&#9;");
            else if (IsAsciiPunctuation(c))
                escaped.Append('\\').Append(c);
            else
                escaped.Append(c);
        }

        return escaped.ToString();
    }

    /// <summary>
    /// Whether Markdown <paramref name="text"/> the escape may have written into is blank: only white space and the
    /// escape's own references <c>&amp;#32;</c> and <c>&amp;#9;</c>, which display as a space and a tab (Task 15's review:
    /// R9 writes a blank line's leading space or tab as its reference, and an empty value stays empty for
    /// <c>failIfEmpty</c> and <c>whenEmpty</c>). For plain text, which the escape never writes, use
    /// <see cref="string.IsNullOrWhiteSpace"/>: there a reference is the characters written.
    /// </summary>
    public static bool IsBlank(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        for (var i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i]))
                i++;
            else if (string.CompareOrdinal(text, i, "&#32;", 0, 5) == 0)
                i += 5;
            else if (string.CompareOrdinal(text, i, "&#9;", 0, 4) == 0)
                i += 4;
            else
                return false;
        }

        return true;
    }

    /// <summary>Whether position <paramref name="at"/> of <paramref name="text"/> starts a line: it is the text's start, or follows a <c>\n</c> or <c>\r</c>.</summary>
    public static bool StartsLine(string text, int at) => at == 0 || text[at - 1] is '\n' or '\r';

    /// <summary>Whether <paramref name="c"/> is one of CommonMark's 32 ASCII punctuation characters (<c>!</c> to <c>/</c>, <c>:</c> to <c>@</c>, <c>[</c> to <c>`</c>, <c>{</c> to <c>~</c>).</summary>
    private static bool IsAsciiPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';
}
