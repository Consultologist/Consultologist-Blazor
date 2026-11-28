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
/// last line or before a list item or a heading. It is then left out when the line ends with a tag (a comment too) closed by <c>~}}</c> or
/// <c>-}}</c> (the template removes that line ending). #1543 (ruling 2, D2; the G-D8 gap): a line of only tags and white
/// space is judged by its tags' roles (<see cref="TagRole"/>, which the caller gives from the checker's typing;
/// <see cref="TagRoles"/>), a comment's being a statement's. As the owner amended D2 on 2026-11-27 (item 3), a line ending
/// between a line of text (literal text, or an <see cref="TagRole.InlineRead"/>) and a line whose only reads are
/// <see cref="TagRole.BlockRead"/>s, on either side of it, is found as <see cref="LineEndingKind.BesideABlockRead"/>: a
/// blank line goes there, never a <c>\</c>. Any other is a <see cref="LineEndingKind.Break"/>, left out when the line it
/// ends holds only tags and any of them is not an <see cref="TagRole.InlineRead"/> (after a statement, a placement or a
/// renderer the <c>\</c> could end a paragraph), or when the line after it holds only tags and any of them is a
/// <see cref="TagRole.Statement"/>, a <see cref="TagRole.Placement"/> or a <see cref="TagRole.Renderer"/> (its output may
/// be nothing, which would leave the <c>\</c> at a paragraph's end). So the line above an inline read alone on its line
/// (<c>Sincerely,</c> above <c>{{ profile.name }}</c>) is a break, and each line of a letterhead of single reads. A line
/// ending after a <c>\</c> or two spaces is already a hard break, and a blank line ends the paragraph, so
/// <see cref="WithHardBreaks"/> and <see cref="WithBlockReadsSeparated"/> written twice write nothing the second time.
/// </summary>
public static class MarkdownLineEndings
{
    /// <summary>
    /// The line endings in <paramref name="body"/> 31 would join where 30 broke them (each a <c>\n</c>, or the <c>\r</c> of a
    /// <c>\r\n</c>), in order, each with its kind (<see cref="LineEndingKind"/>); none for a body whose tags do not close,
    /// or that Markdig refuses to parse (nested too deeply). <paramref name="role"/> gives each tag's role
    /// (<see cref="TemplateTags.Scan"/>'s tags; a comment is not asked).
    /// </summary>
    public static IReadOnlyList<LineEndingHit> Find(string body, Func<TemplateTag, TagRole> role)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(role);
        var (scanned, unclosed) = TemplateTags.Scan(body);
        if (unclosed is not null)
            return [];
        // A comment renders nothing, but a ~}} on it removes the line ending after it as a tag's does.
        var comments = TemplateTags.Comments(body);
        var tags = scanned.Concat(comments).OrderBy(t => t.Start).ToList();
        var roles = scanned.ToDictionary(t => t.Start, role);
        foreach (var comment in comments)
            roles[comment.Start] = TagRole.Statement;
        var byStart = tags.ToDictionary(t => t.Start);
        var masked = body.ToCharArray();
        foreach (var tag in tags)
            Array.Fill(masked, 'x', tag.Start, tag.End - tag.Start);

        // Lines are read in the masked text, where a line ending inside a tag is none.
        var text = new string(masked);
        var found = new Dictionary<int, LineEndingKind>();
        MarkdownDocument document;
        try
        {
            document = Markdown.Parse(text, ConsultMarkdown.Pipeline31);
        }
        catch (Exception)
        {
            // Markdig refuses text nested too deeply (as PlainConversion guards it): no hit, so a warning pass and a
            // migration pass never turn into a failure.
            return [];
        }

        // #1543 (the owner's ruling of 2026-11-27, item 3; D2's amendment): a line of text (literal text, or an inline
        // read) next to a line whose only reads are block reads, on either side of it, is separated by a blank line.
        bool TextLine(List<TemplateTag>? only) => only is null || only.Any(t => roles[t.Start] == TagRole.InlineRead);
        bool BlockReadLine(List<TemplateTag>? only) =>
            only is not null && only.Any(t => roles[t.Start] == TagRole.BlockRead) && !only.Any(t => roles[t.Start] == TagRole.InlineRead);

        foreach (var lineBreak in document.Descendants<LineBreakInline>())
        {
            if (lineBreak.IsHard || !InParagraph(lineBreak) || LineEndingAt(text, lineBreak.Span.Start) is not { } at)
                continue;
            if (RemovedByATag(text, tags, at))
                continue;
            var before = OnlyTags(text, byStart, LineStart(text, at), at);
            var next = NextLine(text, at);
            var after = OnlyTags(text, byStart, next, LineEnd(text, next));
            if ((TextLine(before) && BlockReadLine(after)) || (BlockReadLine(before) && TextLine(after)))
            {
                found.TryAdd(at, LineEndingKind.BesideABlockRead);
                continue;
            }

            if (before is not null && before.Any(t => roles[t.Start] != TagRole.InlineRead))
                continue;
            if (after is not null && after.Any(t => roles[t.Start] is TagRole.Statement or TagRole.Placement or TagRole.Renderer))
                continue;

            found.TryAdd(at, LineEndingKind.Break);
        }

        return found.OrderBy(f => f.Key).Select(f => new LineEndingHit(f.Key, f.Value)).ToList();
    }

    /// <summary>
    /// <paramref name="body"/> with a <c>\</c> written immediately before each line ending <see cref="Find"/> names a
    /// <see cref="LineEndingKind.Break"/> (after any trailing spaces), and how many it wrote, each tag's role given by
    /// <paramref name="role"/>. <c>\</c> is not white space, so a <c>~</c> does not strip it. A line ending beside a block
    /// read's line gets none: its blank line is the block-reads pass's (<see cref="WithBlockReadsSeparated"/>).
    /// </summary>
    public static (string Text, int Count) WithHardBreaks(string body, Func<TemplateTag, TagRole> role)
    {
        var found = Find(body, role).Where(h => h.Kind == LineEndingKind.Break).Select(h => h.At).ToList();
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

    /// <summary>
    /// #1543 (ruling 2, D5): whether <paramref name="tag"/> (one of <see cref="TemplateTags.Scan"/>'s tags of
    /// <paramref name="body"/>) has text before it and after it on its own line. Text is any character but a space or a
    /// tab outside every tag and comment, or a tag whose role (<paramref name="role"/>) is <see cref="TagRole.InlineRead"/>;
    /// a statement, a comment, a placement, a renderer or a block read beside it is not, and the scan steps over it. A tag
    /// beside it that spans lines ends the scan there: what stands past its line ending is another line's. Neither, for a
    /// body whose tags do not close.
    /// </summary>
    public static (bool Before, bool After) TextBeside(string body, TemplateTag tag, Func<TemplateTag, TagRole> role)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(tag);
        ArgumentNullException.ThrowIfNull(role);
        if (Neighbours.Of(body) is not { } neighbours)
            return (false, false);
        var beside = Beside(body, tag, role, neighbours);
        return (beside.Before, beside.After);
    }

    /// <summary>
    /// #1543 (ruling 2, D5): the finder of publish's S05-066 and S05-067 and of the migration's separation (D7): each tag of
    /// <paramref name="body"/> whose role is <see cref="TagRole.BlockRead"/> and that has text before or after it on its
    /// line (<see cref="TextBeside"/>), in order; none for a body whose tags do not close.
    /// </summary>
    public static IReadOnlyList<(TemplateTag Tag, bool Before, bool After)> BlockReadsBeside(string body, Func<TemplateTag, TagRole> role)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(role);
        if (Neighbours.Of(body) is not { } neighbours)
            return [];
        return neighbours.Scanned.Where(t => role(t) == TagRole.BlockRead)
            .Select(t => (Tag: t, Side: Beside(body, t, role, neighbours)))
            .Where(t => t.Side.Before || t.Side.After)
            .Select(t => (t.Tag, t.Side.Before, t.Side.After))
            .ToList();
    }

    /// <summary>
    /// #1543 (ruling 2, D7, as the owner amended it on 2026-11-27): <paramref name="body"/> with each block read separated
    /// from the text beside it by a blank line, and nothing dropped. Where <see cref="BlockReadsBeside"/> finds text before
    /// the read, two line endings are written directly before it, the tags the scan steps over (a statement, a comment, a
    /// placement, a renderer, another block read) kept on the read's line, so <c>{{ if x }}{{ r }}</c> moves whole; where it
    /// finds text after it, two line endings directly after it and the tags that stay with it. Then each single line ending
    /// between a line of text and a line whose only reads are block reads (<see cref="LineEndingKind.BesideABlockRead"/>,
    /// either side) gets one more, so a blank line stands between them. A read on a line that a blank line would break out
    /// of its Markdown block is not moved, and neither is anything else on that line: it is in <c>Marked</c> (#1543, the
    /// owner's item 4 as the controller extended it in Task A5's review, and the final review's finding 1, for the owner).
    /// That is a line the parse of the masked body places inside a container other than the document's top level
    /// (<see cref="InAContainer"/>: a table row, a list item, a quote, an alert, a footnote, a definition item), and, the
    /// markers' test standing beside it (<see cref="BreaksItsBlock"/>), a line whose text before the read starts a list
    /// item, a quote or a heading (<c>- Seen: {{ r }}</c>) or whose text after it would start one on its new line, or
    /// indented code. A <see cref="LineEndingKind.BesideABlockRead"/> line ending inside a container's paragraph (a list
    /// item's lazy line above the read's) gets no blank line either: the block reads on the read's line are marked, as
    /// publish still warns there (S05-065). <c>Separated</c> is each block read with a blank line written
    /// beside it, in order; both lists hold the tags of <paramref name="body"/> as given. The line endings written are
    /// <c>\n</c> (the migration's bodies are normalised) but where one is doubled, which keeps its own. Written again, it
    /// writes nothing: a blank line already there is left.
    /// </summary>
    public static (string Text, IReadOnlyList<TemplateTag> Separated, IReadOnlyList<TemplateTag> Marked) WithBlockReadsSeparated(
        string body, Func<TemplateTag, TagRole> role)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(role);
        if (Neighbours.Of(body) is not { } neighbours)
            return (body, [], []);
        var separated = new HashSet<TemplateTag>();
        var marked = new List<TemplateTag>();
        var writes = new SortedDictionary<int, string>();
        var besides = neighbours.Scanned.Where(t => role(t) == TagRole.BlockRead)
            .Select(t => (Tag: t, Side: Beside(body, t, role, neighbours)))
            .Where(t => t.Side.Before || t.Side.After)
            .ToList();
        // Nothing on a line that a blank line would break out of its block is moved, another read on it included: a line
        // the parse places inside a container (a table, a list item, a quote, ...), or whose markers say it would.
        var parsed = Parsed(body);
        var left = besides.Where(t => InAContainer(parsed, t.Tag.Start) == true || BreaksItsBlock(body, t.Side))
            .Select(t => LineStart(body, t.Tag.Start)).ToHashSet();
        foreach (var (tag, beside) in besides)
        {
            if (left.Contains(LineStart(body, tag.Start)))
            {
                marked.Add(tag);
                continue;
            }

            separated.Add(tag);
            // Two reads separated as one (a block read stepped over beside another) write their blank line once.
            if (beside.Before)
                writes[beside.From] = "\n\n";
            if (beside.After)
                writes[beside.To] = "\n\n";
        }

        var moved = Written(body, writes);

        // The line beside a block read's line, read in the moved text, whose tags are the body's in the same order.
        var given = neighbours.Scanned;
        var (now, _) = TemplateTags.Scan(moved);
        var index = now.Select((t, i) => (t.Start, i)).ToDictionary(p => p.Start, p => p.i);
        TagRole Role(TemplateTag t) => index.TryGetValue(t.Start, out var i) && i < given.Count ? role(given[i]) : TagRole.Statement;
        var blankLines = new SortedDictionary<int, string>();
        var byStart = now.Concat(TemplateTags.Comments(moved)).GroupBy(t => t.Start).ToDictionary(g => g.Key, g => g.First());
        var movedParsed = Parsed(moved);
        foreach (var hit in Find(moved, Role).Where(h => h.Kind == LineEndingKind.BesideABlockRead))
        {
            var next = NextLine(moved, hit.At);
            var line = OnlyTags(moved, byStart, LineStart(moved, hit.At), hit.At) is { } above && above.Any(t => Role(t) == TagRole.BlockRead) && !above.Any(t => Role(t) == TagRole.InlineRead)
                ? above
                : OnlyTags(moved, byStart, next, LineEnd(moved, next)) ?? [];
            var reads = line.Where(t => Role(t) == TagRole.BlockRead).Select(t => given[index[t.Start]]).ToList();
            // A blank line inside a container's paragraph (a list item's lazy line, a quote's) would end the container:
            // none is written, and the read is marked, as publish still warns there (S05-065).
            if (InAContainer(movedParsed, hit.At) == true)
            {
                marked.AddRange(reads);
                continue;
            }

            blankLines[hit.At] = moved[hit.At] == '\r' && hit.At + 1 < moved.Length && moved[hit.At + 1] == '\n' ? "\r\n" : moved[hit.At].ToString();
            foreach (var read in reads)
                separated.Add(read);
        }

        return (Written(moved, blankLines), separated.OrderBy(t => t.Start).ToList(), marked.Distinct().OrderBy(t => t.Start).ToList());
    }

    /// <summary>
    /// <paramref name="body"/> parsed with <see cref="ConsultMarkdown.Pipeline31"/> after each tag and comment is masked
    /// with <c>x</c> characters of its length, as <see cref="Find"/> reads it; none for a body whose tags do not close, or
    /// that Markdig refuses to parse.
    /// </summary>
    private static MarkdownDocument? Parsed(string body)
    {
        var (scanned, unclosed) = TemplateTags.Scan(body);
        if (unclosed is not null)
            return null;
        var masked = body.ToCharArray();
        foreach (var tag in scanned.Concat(TemplateTags.Comments(body)))
            Array.Fill(masked, 'x', tag.Start, tag.End - tag.Start);
        try
        {
            return Markdown.Parse(new string(masked), ConsultMarkdown.Pipeline31);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// #1543 (the final review, finding 1): whether the offset <paramref name="at"/> of the parsed body lies inside a
    /// container other than the document's top level: a table, a list item, a quote, an alert, a footnote, a definition
    /// item (any <see cref="ContainerBlock"/> but the <see cref="MarkdownDocument"/>) between it and the document. A blank
    /// line written there would end that container (a table at that row, a list item before its value) or split it. None
    /// when the parse does not place the offset in any block (the caller then has only the block markers to go by).
    /// </summary>
    private static bool? InAContainer(MarkdownDocument? document, int at)
    {
        if (document is null)
            return null;
        Block? deepest = null;
        foreach (var block in document.Descendants<Block>())
        {
            // Pre-order: siblings do not overlap, so the last block whose span holds the offset is the deepest.
            if (block.Span.Start <= at && at <= block.Span.End)
                deepest = block;
        }

        if (deepest is null)
            return null;
        for (Block? block = deepest; block is not null and not MarkdownDocument; block = block.Parent)
        {
            if (block is ContainerBlock)
                return true;
        }

        return false;
    }

    /// <summary><paramref name="body"/> with each of <paramref name="writes"/> inserted at its offset.</summary>
    private static string Written(string body, SortedDictionary<int, string> writes)
    {
        if (writes.Count == 0)
            return body;
        var text = new System.Text.StringBuilder(body.Length + writes.Values.Sum(w => w.Length));
        var from = 0;
        foreach (var (at, written) in writes)
        {
            text.Append(body, from, at - from).Append(written);
            from = at;
        }

        text.Append(body, from, body.Length - from);
        return text.ToString();
    }

    /// <summary>
    /// #1543 (the owner's item 4, as the controller extended it in Task A5's review, for the owner): by the block markers
    /// alone, beside <see cref="InAContainer"/>'s parse (and all there is when the parse does not place the line), whether
    /// separating the read <paramref name="beside"/> describes would change the block structure of its line. Either the text before it,
    /// trimmed at its start, starts with a Markdown block marker (<see cref="StartsABlock"/>), whether or not more text
    /// follows the marker (<c>- Seen: {{ r }}</c>, <c>&gt; {{ r }}</c>): a blank line would move the read out of the list
    /// item, the quote or the heading. Or the text after it would itself start a block on its new line: after its leading
    /// white space it starts with such a marker, or it is led by a tab or by four or more spaces (an indented code block).
    /// </summary>
    private static bool BreaksItsBlock(string body, (bool Before, bool After, int From, int To) beside)
    {
        if (beside.Before && StartsABlock.IsMatch(body[LineStart(body, beside.From)..beside.From].TrimStart(' ', '\t')))
            return true;
        if (!beside.After)
            return false;
        var after = body[beside.To..LineEnd(body, beside.To)];
        var text = after.TrimStart(' ', '\t');
        var lead = after[..(after.Length - text.Length)];
        return lead.Contains('\t', StringComparison.Ordinal) || lead.Length >= 4 || StartsABlock.IsMatch(text);
    }

    /// <summary>
    /// A Markdown block marker at a text's start: <c>-</c>, <c>*</c> or <c>+</c> followed by white space or nothing (the
    /// tag), a list number (<c>1.</c>, <c>1)</c>) likewise, <c>&gt;</c>, <c>#</c> to <c>######</c> followed by white space
    /// or nothing.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex StartsABlock = new(
        @"^(?:[-*+](?:[ \t]|$)|[0-9]{1,9}[.)](?:[ \t]|$)|>|#{1,6}(?:[ \t]|$))",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// #1543 (ruling 2, D8): where each of <paramref name="reads"/> (a tag's text as written) stands separated by a blank
    /// line from a line of text in <paramref name="body"/>, as <see cref="WithBlockReadsSeparated"/> writes it: the read's
    /// line holds only tags and white space, the line next to it (above or below) is blank, and the line beyond that is not.
    /// Each with the tag's start, the span of the line endings between the text's line and the read's (<c>From</c> to
    /// <c>To</c>), whether the text stands above, and the text's line. In order; none for a body whose tags do not close.
    /// </summary>
    internal static IReadOnlyList<Separation> Separations(string body, IReadOnlyCollection<string> reads)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(reads);
        var (scanned, unclosed) = TemplateTags.Scan(body);
        if (unclosed is not null || reads.Count == 0)
            return [];
        var byStart = scanned.Concat(TemplateTags.Comments(body)).GroupBy(t => t.Start).ToDictionary(g => g.Key, g => g.First());
        bool Blank(int start, int end) => body.AsSpan(start, end - start).Trim(" \t").IsEmpty;

        var found = new List<Separation>();
        foreach (var tag in scanned.Where(t => reads.Contains(body[t.Start..t.End])))
        {
            var start = LineStart(body, tag.Start);
            var end = LineEnd(body, tag.End);
            if (OnlyTags(body, byStart, start, end) is null)
                continue;

            // Above: the line before the read's is blank, the one before that is not.
            if (start > 0 && PreviousLine(body, start) is var blank && blank < start && Blank(blank, LineEndBefore(body, start))
                && blank > 0 && PreviousLine(body, blank) is var text && LineEndBefore(body, blank) is var textEnd && !Blank(text, textEnd))
            {
                found.Add(new Separation(tag.Start, textEnd, start, true, body[text..textEnd]));
            }

            // Below: the line after the read's is blank, the one after that is not.
            if (end < body.Length && NextLine(body, end) is var below && below < body.Length && LineEnd(body, below) is var blankEnd && Blank(below, blankEnd)
                && blankEnd < body.Length && NextLine(body, blankEnd) is var after && after < body.Length && LineEnd(body, after) is var afterEnd && !Blank(after, afterEnd))
            {
                found.Add(new Separation(tag.Start, end, after, false, body[after..afterEnd]));
            }
        }

        return found;
    }

    /// <summary>The start of the line before the one that starts at <paramref name="lineStart"/> (which is not the first).</summary>
    private static int PreviousLine(string body, int lineStart) => LineStart(body, LineEndBefore(body, lineStart));

    /// <summary>Where the line before the one that starts at <paramref name="lineStart"/> ends (its <c>\r\n</c>'s <c>\r</c>, or its <c>\n</c>).</summary>
    private static int LineEndBefore(string body, int lineStart) =>
        lineStart >= 2 && body[lineStart - 1] == '\n' && body[lineStart - 2] == '\r' ? lineStart - 2 : lineStart - 1;

    /// <summary>A body's tags (<see cref="TemplateTags.Scan"/>'s, asked their role) and comments (never text), by where each starts and ends.</summary>
    private sealed record Neighbours(
        IReadOnlyList<TemplateTag> Scanned,
        IReadOnlyDictionary<int, (TemplateTag Tag, bool Asked)> ByStart,
        IReadOnlyDictionary<int, (TemplateTag Tag, bool Asked)> ByEnd)
    {
        /// <summary>The neighbours of <paramref name="body"/>; none for a body whose tags do not close.</summary>
        public static Neighbours? Of(string body)
        {
            var (scanned, unclosed) = TemplateTags.Scan(body);
            if (unclosed is not null)
                return null;
            var tags = scanned.Select(t => (Tag: t, Asked: true)).Concat(TemplateTags.Comments(body).Select(c => (Tag: c, Asked: false))).ToList();
            return new(
                scanned,
                tags.GroupBy(t => t.Tag.Start).ToDictionary(g => g.Key, g => g.First()),
                tags.GroupBy(t => t.Tag.End).ToDictionary(g => g.Key, g => g.First()));
        }
    }

    /// <summary>
    /// Whether <paramref name="tag"/> has text before and after it on its line (<see cref="TextBeside"/>), and where the
    /// tags the scan stepped over begin before it (<c>From</c>: its own start when none) and end after it (<c>To</c>).
    /// </summary>
    private static (bool Before, bool After, int From, int To) Beside(string body, TemplateTag tag, Func<TemplateTag, TagRole> role, Neighbours neighbours)
    {
        bool IsText((TemplateTag Tag, bool Asked) other) => other.Asked && role(other.Tag) == TagRole.InlineRead;
        bool SpansLines(TemplateTag other) => body.AsSpan(other.Start, other.End - other.Start).IndexOfAny('\n', '\r') >= 0;

        var before = false;
        var from = tag.Start;
        for (var i = tag.Start; i > 0 && !before;)
        {
            if (neighbours.ByEnd.TryGetValue(i, out var other) && other.Tag.Start < i)
            {
                before = IsText(other);
                if (before)
                    break;
                from = other.Tag.Start;
                if (SpansLines(other.Tag))
                    break;
                i = other.Tag.Start;
            }
            else if (body[i - 1] is '\n' or '\r')
            {
                break;
            }
            else
            {
                before = body[i - 1] is not (' ' or '\t');
                i--;
            }
        }

        var after = false;
        var to = tag.End;
        for (var i = tag.End; i < body.Length && !after;)
        {
            if (neighbours.ByStart.TryGetValue(i, out var other) && other.Tag.End > i)
            {
                after = IsText(other);
                if (after)
                    break;
                to = other.Tag.End;
                if (SpansLines(other.Tag))
                    break;
                i = other.Tag.End;
            }
            else if (body[i] is '\n' or '\r')
            {
                break;
            }
            else
            {
                after = body[i] is not (' ' or '\t');
                i++;
            }
        }

        return (before, after, from, to);
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

    /// <summary>
    /// The tags and comments of <paramref name="body"/> from <paramref name="start"/> to <paramref name="end"/> when that
    /// span holds at least one and, outside them, only spaces and tabs; otherwise null.
    /// </summary>
    private static List<TemplateTag>? OnlyTags(string body, IReadOnlyDictionary<int, TemplateTag> tags, int start, int end)
    {
        var found = new List<TemplateTag>();
        for (var i = start; i < end;)
        {
            if (tags.TryGetValue(i, out var tag))
            {
                found.Add(tag);
                i = tag.End;
            }
            else if (body[i] is ' ' or '\t')
            {
                i++;
            }
            else
            {
                return null;
            }
        }

        return found.Count > 0 ? found : null;
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

/// <summary>
/// #1543 (D2, as the owner amended it on 2026-11-27): what a line ending <see cref="MarkdownLineEndings.Find"/> names
/// wants: a <c>\</c> (ruling 1), or a blank line beside a line whose only reads are block reads.
/// </summary>
public enum LineEndingKind
{
    /// <summary>A single line ending inside a paragraph: the migration ends the line with <c>\</c>.</summary>
    Break,

    /// <summary>A single line ending between a line of text and a line whose only reads are block reads: the migration writes a blank line.</summary>
    BesideABlockRead,
}

/// <summary>A line ending <see cref="MarkdownLineEndings.Find"/> names: its offset (a <c>\n</c>, or the <c>\r</c> of a <c>\r\n</c>) and its kind.</summary>
public readonly record struct LineEndingHit(int At, LineEndingKind Kind);

/// <summary>
/// #1543 (D8): a read separated from a line of text by a blank line (<see cref="MarkdownLineEndings.Separations"/>): the
/// read's tag's start, the line endings between the text's line and the read's (<paramref name="From"/> to
/// <paramref name="To"/>), whether the text stands <paramref name="Above"/> the read, and the text's line.
/// </summary>
internal sealed record Separation(int Tag, int From, int To, bool Above, string Line);
