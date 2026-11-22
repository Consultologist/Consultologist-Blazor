using System.Text;

namespace Consultologist.PackageFormat.V31.Templates;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 8.6; part 08 § 4): one thing wrong with a reply's markers.
/// <paramref name="Id"/> is the sentence's row (S05-053 to S05-059), <paramref name="Macro"/> the macro id it is about (for
/// S05-059 the one written too early), <paramref name="Sentence"/> the sentence, and <paramref name="Reason"/> part 08's
/// short form for the row's <c>invalid.reason</c> (<c>macro &lt;id&gt;: missing</c>, <c>repeated</c>, <c>altered</c>,
/// <c>not offered</c>, <c>unknown</c>, <c>out of order</c>).
/// </summary>
public sealed record ReplyFault(string Id, string Macro, string Sentence, string Reason);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 8.6): a reply's markers checked and replaced: the reply with each offered
/// macro's body where its marker stood (<paramref name="Text"/>, <c>null</c> exactly when there is a fault), the faults in
/// the order said, and the sites of the macros placed (their marker found exactly once in its form), in prompt order.
/// </summary>
public sealed record MacroReplyResult(string? Text, IReadOnlyList<ReplyFault> Faults, IReadOnlyList<string> Placed);

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 § 8.6; Review Focus 1): a model's reply processed literally. The reply is never
/// rendered or parsed as a template: it is scanned once for the two exact marker shapes, <c>{{ macro.&lt;ids&gt;.content }}</c>
/// and <c>{{~ macro.&lt;ids&gt;.content }}</c> (one space inside each brace pair, one id or a folder's two by the id pattern),
/// and anything else, a near-miss included, is the reply's own text. With no fault, every marker is replaced in one pass
/// over the original reply into a new string, so what is substituted is never scanned again.
/// </summary>
public static class MacroReply
{
    private const string Open = "{{";
    private const string Close = " }}";
    private const string Root = "macro.";
    private const string Member = "content";

    /// <summary>One marker written in the reply: where it stands, its form, and the macro id it names.</summary>
    private sealed record Occurrence(int Start, int End, bool Block, string Macro, bool TextBefore, bool TextAfter, int LineStart, int AfterSpaces);

    /// <summary>
    /// Checks <paramref name="reply"/> against the macros <paramref name="offered"/> (in prompt order) and replaces their
    /// markers. An occurrence of a macro of the package (<paramref name="packageMacros"/>, ids such as
    /// <c>letrozole</c> or <c>section_notes.hpi</c>) not offered is S05-055; of no macro S05-056. For each offered macro:
    /// none S05-053; more than one S05-054; the other form, a block marker with text before it on its line, or the inline
    /// form where a block was asked, S05-057; a block marker followed on its line by anything but spaces S05-058. With
    /// <paramref name="ordered"/>, the first pair of offered markers out of prompt order is S05-059. A block marker's line
    /// loses its indentation before the marker and the spaces after it and keeps its newline (<c>\n</c>, or <c>\r\n</c>); an
    /// inline marker is replaced where it stands. Never throws on a reply.
    /// </summary>
    public static MacroReplyResult Process(string reply, IReadOnlyList<OfferedMacro> offered, IReadOnlyCollection<string> packageMacros, bool ordered)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(packageMacros);

        var occurrences = Scan(reply);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < offered.Count; i++)
            index.TryAdd(IdOf(offered[i]), i);

        var faults = new List<ReplyFault>();
        var said = new HashSet<string>(StringComparer.Ordinal);
        foreach (var occurrence in occurrences)
        {
            if (index.ContainsKey(occurrence.Macro) || !said.Add(occurrence.Macro))
                continue;
            faults.Add(packageMacros.Contains(occurrence.Macro)
                ? Fault("S05-055", occurrence.Macro, ReplySentences.NotOffered(occurrence.Macro), "not offered")
                : Fault("S05-056", occurrence.Macro, ReplySentences.NotAMacro(), "unknown"));
        }

        foreach (var macro in offered)
        {
            var id = IdOf(macro);
            var own = occurrences.Where(o => o.Macro == id).ToList();
            if (own.Count == 0)
                faults.Add(Fault("S05-053", id, ReplySentences.NoMarker(id), "missing"));
            else if (own.Count > 1)
                faults.Add(Fault("S05-054", id, ReplySentences.MarkerTimes(id, own.Count), "repeated"));
            else if (macro.Block ? !own[0].Block || own[0].TextBefore : own[0].Block)
                faults.Add(Fault("S05-057", id, ReplySentences.WrongForm(id, macro.Block), "altered"));
            else if (macro.Block && own[0].TextAfter)
                faults.Add(Fault("S05-058", id, ReplySentences.TextAfterMarker(id), "altered"));
        }

        if (ordered && OutOfOrder(occurrences, index) is { } pair)
            faults.Add(Fault("S05-059", pair.Early, ReplySentences.OutOfOrder(pair.Early, pair.Late), "out of order"));

        if (faults.Count > 0)
            return new MacroReplyResult(null, faults, []);
        return new MacroReplyResult(Replace(reply, occurrences, offered, index), [], [.. offered.Select(m => m.Site)]);
    }

    private static ReplyFault Fault(string id, string macro, string sentence, string reason) => new(id, macro, sentence, $"macro {macro}: {reason}");

    /// <summary>The id an offered macro's marker names: its marker without <c>macro.</c> and <c>.content</c>.</summary>
    private static string IdOf(OfferedMacro macro) =>
        macro.Marker.StartsWith(Root, StringComparison.Ordinal) && macro.Marker.EndsWith("." + Member, StringComparison.Ordinal)
            ? macro.Marker[Root.Length..^(Member.Length + 1)]
            : macro.Marker;

    /// <summary>The first two offered markers (each macro's first) that stand in the reply against prompt order: the one written first, then the one the prompt placed first.</summary>
    private static (string Early, string Late)? OutOfOrder(List<Occurrence> occurrences, Dictionary<string, int> index)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? before = null;
        foreach (var occurrence in occurrences)
        {
            if (!index.ContainsKey(occurrence.Macro) || !seen.Add(occurrence.Macro))
                continue;
            if (before != null && index[occurrence.Macro] < index[before])
                return (before, occurrence.Macro);
            before = occurrence.Macro;
        }

        return null;
    }

    /// <summary>The reply rebuilt in one pass, each occurrence (every one an offered macro's, once) replaced by its body.</summary>
    private static string Replace(string reply, List<Occurrence> occurrences, IReadOnlyList<OfferedMacro> offered, Dictionary<string, int> index)
    {
        var text = new StringBuilder(reply.Length + offered.Sum(m => m.Body.Length));
        var at = 0;
        foreach (var occurrence in occurrences)
        {
            var body = offered[index[occurrence.Macro]].Body;
            if (occurrence.Block)
            {
                var start = Math.Max(at, occurrence.LineStart);
                text.Append(reply, at, start - at).Append(body);
                at = occurrence.AfterSpaces;
            }
            else
            {
                text.Append(reply, at, occurrence.Start - at).Append(body);
                at = occurrence.End;
            }
        }

        return text.Append(reply, at, reply.Length - at).ToString();
    }

    /// <summary>
    /// Every marker occurrence in <paramref name="reply"/>, in order, none overlapping. The line each stands on (where it
    /// starts, and whether anything but spaces and tabs stands before the marker on it) is tracked as the scan moves on,
    /// so the scan is linear in the reply however many markers one line holds.
    /// </summary>
    private static List<Occurrence> Scan(string reply)
    {
        var found = new List<Occurrence>();
        var lineStart = 0;
        var lastText = -1;
        var read = 0;
        var at = 0;
        while (at < reply.Length && (at = reply.IndexOf(Open, at, StringComparison.Ordinal)) >= 0)
        {
            for (; read < at; read++)
            {
                if (reply[read] == '\n')
                    lineStart = read + 1;
                else if (reply[read] is not (' ' or '\t'))
                    lastText = read;
            }

            if (Marker(reply, at, lineStart, lastText >= lineStart) is { } occurrence)
            {
                found.Add(occurrence);
                at = occurrence.End;
            }
            else
            {
                at++;
            }
        }

        return found;
    }

    /// <summary>The marker that starts at <paramref name="start"/> (at its <c>{{</c>) on the line starting at <paramref name="lineStart"/>, or <c>null</c> when the text there is none.</summary>
    private static Occurrence? Marker(string reply, int start, int lineStart, bool textBefore)
    {
        var at = start + Open.Length;
        var block = at < reply.Length && reply[at] == '~';
        if (block)
            at++;
        if (at >= reply.Length || reply[at] != ' ' || string.CompareOrdinal(reply, at + 1, Root, 0, Root.Length) != 0)
            return null;
        at += 1 + Root.Length;

        var segments = new List<string>();
        while (true)
        {
            var end = at;
            if (end >= reply.Length || reply[end] is < 'a' or > 'z')
                return null;
            while (end < reply.Length && reply[end] is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
                end++;
            if (end - at > SourceBounds.IdLength)
                return null;
            segments.Add(reply[at..end]);
            at = end;
            if (segments.Count > 3 || at >= reply.Length || reply[at] != '.')
                break;
            at++;
        }

        if (segments.Count is < 2 or > 3 || segments[^1] != Member || string.CompareOrdinal(reply, at, Close, 0, Close.Length) != 0)
            return null;
        var stop = at + Close.Length;

        var afterSpaces = stop;
        while (afterSpaces < reply.Length && reply[afterSpaces] == ' ')
            afterSpaces++;
        var textAfter = afterSpaces < reply.Length && reply[afterSpaces] != '\n'
            && !(reply[afterSpaces] == '\r' && afterSpaces + 1 < reply.Length && reply[afterSpaces + 1] == '\n');

        return new Occurrence(start, stop, block, string.Join('.', segments.Take(segments.Count - 1)), textBefore, textAfter, lineStart, afterSpaces);
    }
}
