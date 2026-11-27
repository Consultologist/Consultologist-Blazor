using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D8): the line endings of a Markdown template body that 31 does not
/// break and 30 did. 30's pipeline broke every single line ending (<see cref="ConsultMarkdown.Pipeline"/>); 31's reads one
/// inside a paragraph as a space (<see cref="ConsultMarkdown.Pipeline31"/>). The body is parsed with
/// <see cref="ConsultMarkdown.Pipeline31"/> after each tag (<see cref="TemplateTags.Scan"/>) and each comment is masked with
/// <c>x</c> characters of its length, so a tag's text never makes Markdown (a pipe in a filter is no table) and a line ending
/// inside a tag is none of the body's. A line ending is found when it is a soft line break inside a paragraph (in a list
/// item, a quote, an alert or a footnote too): never in a fence, indented code, a table or a heading, at a paragraph's
/// last line or before a list item or a heading. It is then left out when the line ends with a tag closed by <c>~}}</c> or
/// <c>-}}</c> (the template removes that line ending), or when the line before or after it holds only tags and white
/// space (a statement, a placement or an output alone on its line, whose output may be empty or a block, which would leave
/// the break at a paragraph's end). A line ending after a <c>\</c> or two spaces is already a hard break, so
/// <see cref="WithHardBreaks"/> written twice writes nothing the second time.
/// </summary>
public static class MarkdownLineEndings
{
    /// <summary>
    /// The offsets in <paramref name="body"/> of the line endings to break (each a <c>\n</c>, or the <c>\r</c> of a
    /// <c>\r\n</c>), in order; none for a body whose tags do not close.
    /// </summary>
    public static IReadOnlyList<int> Find(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (tags, unclosed) = TemplateTags.Scan(body);
        if (unclosed is not null)
            return [];
        var spans = tags.Select(t => (t.Start, t.End)).Concat(TemplateTags.Comments(body)).OrderBy(s => s.Start).ToList();
        var masked = body.ToCharArray();
        foreach (var (start, end) in spans)
            Array.Fill(masked, 'x', start, end - start);

        // Lines are read in the masked text, where a line ending inside a tag is none.
        var text = new string(masked);
        var found = new List<int>();
        var document = Markdown.Parse(text, ConsultMarkdown.Pipeline31);
        foreach (var lineBreak in document.Descendants<LineBreakInline>())
        {
            if (lineBreak.IsHard || !InParagraph(lineBreak) || LineEndingAt(text, lineBreak.Span.Start) is not { } at)
                continue;
            if (RemovedByATag(text, tags, at) || OnlyTags(text, spans, LineStart(text, at), at) || OnlyTags(text, spans, NextLine(text, at), LineEnd(text, NextLine(text, at))))
                continue;
            found.Add(at);
        }

        return found.Distinct().Order().ToList();
    }

    /// <summary>
    /// <paramref name="body"/> with a <c>\</c> written immediately before each line ending <see cref="Find"/> names (after any
    /// trailing spaces), and how many it wrote. <c>\</c> is not white space, so a <c>~</c> does not strip it.
    /// </summary>
    public static (string Text, int Count) WithHardBreaks(string body)
    {
        var found = Find(body);
        if (found.Count == 0)
            return (body, 0);
        var text = new System.Text.StringBuilder(body.Length + found.Count);
        var from = 0;
        foreach (var at in found)
        {
            text.Append(body, from, at - from).Append('\\');
            from = at;
        }

        text.Append(body, from, body.Length - from);
        return (text.ToString(), found.Count);
    }

    /// <summary>Whether <paramref name="inline"/> sits in a paragraph's inlines (not a heading's, a table cell's or any other leaf's).</summary>
    private static bool InParagraph(Inline inline)
    {
        var root = inline;
        while (root.Parent is { } parent)
            root = parent;
        return root is ContainerInline { ParentBlock: ParagraphBlock };
    }

    /// <summary>The line ending at or after <paramref name="from"/> on its line (a soft break's span may start at the white space before it), or none.</summary>
    private static int? LineEndingAt(string body, int from)
    {
        for (var i = Math.Max(from, 0); i < body.Length; i++)
        {
            if (body[i] is '\n' or '\r')
                return i;
            if (body[i] is not (' ' or '\t'))
                return null;
        }

        return null;
    }

    /// <summary>Whether the line ending at <paramref name="at"/> follows a tag closed by <c>~}}</c> or <c>-}}</c>, or precedes one opened by <c>{{-</c>, with only spaces and tabs between.</summary>
    private static bool RemovedByATag(string body, IReadOnlyList<TemplateTag> tags, int at)
    {
        var before = at;
        while (before > 0 && body[before - 1] is ' ' or '\t')
            before--;
        var after = NextLine(body, at);
        while (after < body.Length && body[after] is ' ' or '\t')
            after++;
        return tags.Any(t => (t.End == before && (t.TildeRight || t.DashRight)) || (t.Start == after && t.DashLeft));
    }

    /// <summary>Whether <paramref name="body"/> from <paramref name="start"/> to <paramref name="end"/> holds a tag or a comment and, outside them, only spaces and tabs.</summary>
    private static bool OnlyTags(string body, IReadOnlyList<(int Start, int End)> spans, int start, int end)
    {
        var any = false;
        for (var i = start; i < end;)
        {
            if (spans.FirstOrDefault(s => s.Start == i) is { End: > 0 } span)
            {
                any = true;
                i = span.End;
            }
            else if (body[i] is ' ' or '\t')
            {
                i++;
            }
            else
            {
                return false;
            }
        }

        return any;
    }

    private static int LineStart(string body, int at)
    {
        var i = at;
        while (i > 0 && body[i - 1] is not '\n')
            i--;
        return i;
    }

    private static int NextLine(string body, int at) =>
        Math.Min(body.Length, at + (body[at] == '\r' && at + 1 < body.Length && body[at + 1] == '\n' ? 2 : 1));

    private static int LineEnd(string body, int start)
    {
        var i = start;
        while (i < body.Length && body[i] is not ('\n' or '\r'))
            i++;
        return i;
    }
}
