using Scriban.Parsing;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 2.6): one <c>{{ … }}</c> tag of a body's source. <paramref name="Line"/>
/// and <paramref name="Column"/> are 1-based and name the <c>{{</c>; <paramref name="Start"/> and <paramref name="End"/>
/// are the character offsets of the <c>{{</c> and of the character after the <c>}}</c>. The four marks say whether
/// <c>~</c> or <c>-</c> stands inside each brace pair. <paramref name="TextBefore"/> and <paramref name="TextAfter"/>
/// say whether anything but spaces and tabs is on the tag's line before or after it (another tag included), and
/// <paramref name="AloneOnLine"/> is neither. Comments (<c>{{# … }}</c>) and raw blocks (<c>{%{ … }%}</c>) are no tags.
/// </summary>
public sealed record TemplateTag(
    int Line,
    int Column,
    int Start,
    int End,
    bool TildeLeft,
    bool TildeRight,
    bool DashLeft,
    bool DashRight,
    bool AloneOnLine,
    bool TextBefore,
    bool TextAfter);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 2.6): the tag scanner. It reads a body with Scriban's own lexer, so a
/// tag is exactly what Scriban reads as code, and a raw block or a comment is skipped as Scriban skips it.
/// </summary>
internal static class TemplateTags
{
    /// <summary>
    /// The tags of <paramref name="text"/> in source order, or the 1-based line of a <c>{{</c> that is never closed
    /// (Scriban then reads the rest of the body as code and may say nothing).
    /// </summary>
    public static (IReadOnlyList<TemplateTag> Tags, int? UnclosedLine) Scan(string text)
    {
        var tags = new List<TemplateTag>();
        Token? enter = null;
        var comment = false;
        var code = false;

        foreach (var token in new Lexer(text))
        {
            switch (token.Type)
            {
                case TokenType.CodeEnter:
                    enter = token;
                    comment = false;
                    code = false;
                    break;
                case TokenType.CodeExit when enter is { } open:
                    // A tag holding only a comment renders nothing and is no tag, unless a `-` makes it remove text.
                    var tag = Tag(text, open, token);
                    if (code || !comment || tag.DashLeft || tag.DashRight)
                    {
                        tags.Add(tag);
                    }

                    enter = null;
                    break;
                case TokenType.Eof:
                    return (tags, enter is { } unclosed ? Math.Max(unclosed.Start.Line, 0) + 1 : null);
                case TokenType.Comment or TokenType.CommentMulti:
                    comment = true;
                    break;
                case TokenType.Whitespace or TokenType.WhitespaceFull or TokenType.NewLine:
                    break;
                default:
                    code = true;
                    break;
            }
        }

        return (tags, enter is { } last ? Math.Max(last.Start.Line, 0) + 1 : null);
    }

    /// <summary>
    /// #1510 (G-D8): the spans of <paramref name="text"/>'s tags that hold only a comment (<c>{{# … }}</c>), which
    /// <see cref="Scan"/> does not list (they render nothing), each from its <c>{{</c> to the character after its <c>}}</c>.
    /// </summary>
    public static IReadOnlyList<(int Start, int End)> Comments(string text)
    {
        var comments = new List<(int Start, int End)>();
        Token? enter = null;
        var comment = false;
        var code = false;
        foreach (var token in new Lexer(text))
        {
            switch (token.Type)
            {
                case TokenType.CodeEnter:
                    (enter, comment, code) = (token, false, false);
                    break;
                case TokenType.CodeExit when enter is { } open:
                    var tag = Tag(text, open, token);
                    if (comment && !code && !tag.DashLeft && !tag.DashRight)
                        comments.Add((tag.Start, tag.End));
                    enter = null;
                    break;
                case TokenType.Eof:
                    return comments;
                case TokenType.Comment or TokenType.CommentMulti:
                    comment = true;
                    break;
                case TokenType.Whitespace or TokenType.WhitespaceFull or TokenType.NewLine:
                    break;
                default:
                    code = true;
                    break;
            }
        }

        return comments;
    }

    private static TemplateTag Tag(string text, Token enter, Token exit)
    {
        var start = Math.Clamp(enter.Start.Offset, 0, text.Length);
        var end = Math.Clamp(exit.End.Offset + 1, start, text.Length);
        var left = text[start..Math.Clamp(enter.End.Offset + 1, start, end)];
        var right = text[Math.Clamp(exit.Start.Offset, start, end)..end];
        var before = TextBefore(text, start);
        var after = TextAfter(text, end);

        return new TemplateTag(
            enter.Start.Line + 1,
            enter.Start.Column + 1,
            start,
            end,
            left.EndsWith('~'),
            right.StartsWith('~'),
            left.EndsWith('-'),
            right.StartsWith('-'),
            !before && !after,
            before,
            after);
    }

    private static bool TextBefore(string text, int start)
    {
        for (var i = start - 1; i >= 0 && text[i] != '\n'; i--)
        {
            if (text[i] is not (' ' or '\t'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TextAfter(string text, int end)
    {
        for (var i = end; i < text.Length && text[i] != '\n'; i++)
        {
            if (text[i] is not (' ' or '\t') && !(text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n'))
            {
                return true;
            }
        }

        return false;
    }
}
