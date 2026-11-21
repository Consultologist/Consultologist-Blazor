using System.Text;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 06 § 6.4; part 05 § 8.3; D7, D14): plain text written into Markdown so that it
/// displays as exactly that text. CommonMark's backslash escapes for ASCII punctuation: a backslash before each of the 32
/// ASCII punctuation characters, nothing else changed (letters, digits, white space, line breaks and any non-ASCII text
/// stay as they are). A leading run of four spaces or a tab is not protected (D7). The lock names it
/// <c>markdown_escape</c> at <see cref="Version"/>.
/// </summary>
public static class MarkdownEscape
{
    /// <summary>The version of the escape, as <c>lock.json</c>'s <c>builtins</c> names it.</summary>
    public const string Version = "1.0.0";

    /// <summary><paramref name="plain"/> with a backslash before each ASCII punctuation character.</summary>
    public static string Escape(string plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        var escaped = new StringBuilder(plain.Length + (plain.Length / 8));
        foreach (var c in plain)
        {
            if (IsAsciiPunctuation(c))
                escaped.Append('\\');
            escaped.Append(c);
        }

        return escaped.ToString();
    }

    /// <summary>Whether <paramref name="c"/> is one of CommonMark's 32 ASCII punctuation characters (<c>!</c> to <c>/</c>, <c>:</c> to <c>@</c>, <c>[</c> to <c>`</c>, <c>{</c> to <c>~</c>).</summary>
    private static bool IsAsciiPunctuation(char c) =>
        c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';
}
