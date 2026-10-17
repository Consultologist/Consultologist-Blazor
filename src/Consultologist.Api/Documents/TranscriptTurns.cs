using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Consultologist.Api.Documents;

/// <summary>Which cue container a transcript file announced.</summary>
internal enum TranscriptSyntax
{
    Vtt,
    Srt
}

/// <summary>One cue's speech: who the source said spoke (null when it did not say) and what.</summary>
internal readonly record struct TranscriptCue(string? Speaker, string Text);

/// <summary>
/// #994: the one rule that turns a cue container (WebVTT, SubRip) into the
/// text a transcript input holds — the conversation as TURNS, one line per
/// turn, <c>Speaker: text</c> where the source labels a speaker, consecutive
/// cues of one speaker merged; everything that is container rather than
/// speech removed (the header, NOTE/STYLE/REGION blocks, cue identifiers,
/// timing lines and their settings, inline tags and timestamps), entities
/// decoded. Speakers stay because who said what is clinical content; cue
/// numbers carry nothing the line order does not; timings fragment sentences,
/// cost tokens and survive in the file the origin's <c>fileSha256</c>
/// identifies. One conversation therefore reads — and hashes — the same
/// whichever container it arrived in (provenance@v2026.10.6, the
/// <c>inputOrigins</c> row; docs/DOCUMENT_INPUT.md § 3).
///
/// Pure and deterministic: ordinal comparisons, culture-invariant regexes,
/// no I/O. The output feeds <c>textSha256</c>, so ANY change to this rule is
/// a new parser build — bump BOTH callers' ids (<c>vtt/1</c>, <c>srt/1</c>)
/// to <c>/2</c> in the same commit; the origin's <c>extractor</c> is how a
/// reader explains two readings of one file.
///
/// Plain text is not here on purpose: it has no signature, and a referral
/// letter that mentions a time must not be rewritten.
/// </summary>
internal static class TranscriptTurns
{
    /// <summary>How many bytes a sniffer decodes to decide the format (the HTML sniffer's bound).</summary>
    internal const int SniffBytes = 4096;

    private const RegexOptions Invariant = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>
    /// A cue timing line: optional hours, mm:ss, millis after '.' (VTT) or
    /// ',' (SRT), the arrow, the end time, then optional cue settings.
    /// </summary>
    internal static readonly Regex TimingLine =
        new(@"^\s*(?:\d+:)?\d{2}:\d{2}[.,]\d{3}\s+-->\s+(?:\d+:)?\d{2}:\d{2}[.,]\d{3}(?:\s.*)?$", Invariant);

    // A WebVTT voice span opener: <v Name>, <v.class Name>, <v.a.b Name>. Group 1 is the name.
    private static readonly Regex VoiceOpen = new(@"<v(?:\.[^\s>]*)?\s+([^>]*)>", Invariant);

    // Markup to drop, deliberately narrow: '<' must be followed by '/', an
    // ASCII letter, or a timestamp (two digits and a colon), so "BP < 140"
    // and "HR >90" survive. Covers </v> <c> <c.x> <b> <i> <u> <ruby> <rt>
    // <lang en> <font color=…> </font> and an inline <00:01:02.000>.
    private static readonly Regex Tag = new(@"</?[A-Za-z][^<>]*>|<\d{2}:[^<>]*>", Invariant);

    // ASS/SSA override blocks some SubRip exporters carry: {\an8}, {\i1}.
    private static readonly Regex AssOverride = new(@"\{\\[^{}]*\}", Invariant);

    // A speaker prefix on a cue (Zoom's shape): up to 40 characters holding a
    // letter and no question or exclamation mark ("Dr. Lee" keeps its period),
    // a colon, whitespace, then speech. "12:30 we met" has no letter before
    // its colon; "https://x" has no whitespace after it — neither is a
    // speaker. A false positive ("Plan: start letrozole") renders the same
    // text; only how cues merge into turns differs.
    private static readonly Regex SpeakerPrefix =
        new(@"^(?=[^:\n]*\p{L})([^:\n?!]{1,40}):\s+(.*)$", Invariant | RegexOptions.Singleline);

    private static readonly Regex WhitespaceRun = new(@"[\s ]+", Invariant);

    /// <summary>The decoded prefix a sniffer looks at; the BOM is already gone.</summary>
    internal static string SniffPrefix(byte[] bytes) =>
        TextDocumentDecoder.Decode(bytes.Length <= SniffBytes ? bytes : bytes[..SniffBytes]);

    /// <summary>
    /// The shared extractor body: decode, parse to turns, bound the OUTPUT
    /// (the input is already bounded by MaxBytes; what matters is whether the
    /// speech fits an input), name every failure.
    /// </summary>
    internal static DocumentExtractionResult Extract(byte[] bytes, TranscriptSyntax syntax, string extractorId)
    {
        try
        {
            var text = TextDocumentDecoder.Decode(bytes);
            var (turns, malformed) = Parse(text, syntax);

            // A file that passed the sniff announced a format; a block with
            // text but no timing line did not keep that promise. Named, not
            // dropped — this project does not silently lose speech — and not
            // read as plain text either, which would hand back the cue soup
            // the old text fallback produced (the HTML extractor's
            // PDF-in-spans `corrupt` is the precedent).
            if (malformed)
            {
                return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Corrupt);
            }

            if (turns.Length > DocumentExtraction.MaxCharacters)
            {
                return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.TooMuchText);
            }

            // Whitespace-only (a lone header, a NOTE-only file) falls to the
            // shared Normalize gate, which downgrades it to `empty`.
            return DocumentExtractionResult.Extracted(turns, extractorId, pageCount: null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Corrupt);
        }
    }

    /// <summary>Cue container text to turns. <c>Malformed</c> is true when a block carried text but no timing line.</summary>
    internal static (string Turns, bool Malformed) Parse(string text, TranscriptSyntax syntax)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var cues = new List<TranscriptCue>();
        var header = syntax == TranscriptSyntax.Vtt;

        foreach (var block in Blocks(lines))
        {
            if (header)
            {
                header = false;

                // The signature block. Anything after its timing line, if it
                // has one (a file that forgot the blank line), is a cue.
                var timing = block.FindIndex(line => line.Contains("-->", StringComparison.Ordinal));
                if (timing < 0)
                {
                    continue;
                }

                AddCues(cues, block.GetRange(timing + 1, block.Count - timing - 1));
                continue;
            }

            if (syntax == TranscriptSyntax.Vtt && IsVttMetaBlock(block))
            {
                continue;
            }

            var payload = PayloadOf(block);
            if (payload == null)
            {
                return (string.Empty, true);
            }

            AddCues(cues, payload);
        }

        return (Render(cues), false);
    }

    /// <summary>Maximal runs of non-blank lines.</summary>
    private static IEnumerable<List<string>> Blocks(string[] lines)
    {
        var block = new List<string>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (block.Count > 0)
                {
                    yield return block;
                    block = [];
                }

                continue;
            }

            block.Add(line);
        }

        if (block.Count > 0)
        {
            yield return block;
        }
    }

    private static bool IsVttMetaBlock(List<string> block)
    {
        var first = block[0].TrimStart();
        return StartsWithKeyword(first, "NOTE") || first.Trim() == "STYLE" || StartsWithKeyword(first, "REGION");
    }

    private static bool StartsWithKeyword(string line, string keyword) =>
        line.StartsWith(keyword, StringComparison.Ordinal)
        && (line.Length == keyword.Length || line[keyword.Length] is ' ' or '\t');

    /// <summary>
    /// The cue's payload lines: after a timing line that is first, or after an
    /// identifier line (any text — a SubRip index, a WebVTT id) followed by a
    /// timing line. Null when the block has neither: malformed.
    /// </summary>
    private static List<string>? PayloadOf(List<string> block)
    {
        if (block[0].Contains("-->", StringComparison.Ordinal))
        {
            return block.GetRange(1, block.Count - 1);
        }

        if (block.Count >= 2 && block[1].Contains("-->", StringComparison.Ordinal))
        {
            return block.GetRange(2, block.Count - 2);
        }

        return null;
    }

    /// <summary>
    /// A payload to cues. One tolerance for exporters that omit the blank
    /// line between cues: a timing line inside a payload (optionally preceded
    /// by an index line) starts a new cue.
    /// </summary>
    private static void AddCues(List<TranscriptCue> cues, List<string> payload)
    {
        var segment = new List<string>();
        for (var i = 0; i < payload.Count; i++)
        {
            var isIndexBeforeTiming = i + 1 < payload.Count
                && payload[i].Trim().Length > 0
                && payload[i].Trim().All(char.IsAsciiDigit)
                && TimingLine.IsMatch(payload[i + 1]);
            if (isIndexBeforeTiming || TimingLine.IsMatch(payload[i]))
            {
                AddCuesFromSegment(cues, segment);
                segment = [];
                i += isIndexBeforeTiming ? 1 : 0;
                continue;
            }

            segment.Add(payload[i]);
        }

        AddCuesFromSegment(cues, segment);
    }

    private static void AddCuesFromSegment(List<TranscriptCue> cues, List<string> payloadLines)
    {
        if (payloadLines.Count == 0)
        {
            return;
        }

        // A payload's internal line breaks are layout, not speech.
        var raw = string.Join(' ', payloadLines);
        var voices = VoiceOpen.Matches(raw);

        if (voices.Count == 0)
        {
            AddCue(cues, null, raw);
            return;
        }

        AddCue(cues, null, raw[..voices[0].Index]);
        for (var i = 0; i < voices.Count; i++)
        {
            var speaker = voices[i].Groups[1].Value.Trim().TrimEnd(':').Trim();
            var start = voices[i].Index + voices[i].Length;
            var end = i + 1 < voices.Count ? voices[i + 1].Index : raw.Length;
            AddCue(cues, speaker.Length == 0 ? null : speaker, raw[start..end]);
        }
    }

    private static void AddCue(List<TranscriptCue> cues, string? speaker, string segment)
    {
        var text = Clean(segment);

        if (speaker == null)
        {
            var prefix = SpeakerPrefix.Match(text);
            if (prefix.Success)
            {
                speaker = prefix.Groups[1].Value.Trim();
                text = prefix.Groups[2].Value.Trim();
            }
        }

        if (text.Length == 0)
        {
            return;
        }

        cues.Add(new TranscriptCue(speaker, text));
    }

    /// <summary>
    /// Strip, THEN decode: an escaped <c>&amp;lt;b&amp;gt;</c> is speech about
    /// a tag, not a tag, and must come through as the literal characters.
    /// </summary>
    private static string Clean(string segment)
    {
        var text = AssOverride.Replace(segment, string.Empty);
        text = Tag.Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        return WhitespaceRun.Replace(text, " ").Trim();
    }

    /// <summary>
    /// Cues to turns. Consecutive cues of one speaker are one turn (a cue
    /// boundary is a timing artefact). Unlabelled cues merge too, but break
    /// after a cue that ends a sentence — otherwise an auto-caption file with
    /// no speakers reads as one paragraph the preview cannot be checked
    /// against. A labelled turn never breaks on a sentence: one line per turn
    /// is the rule the record states.
    /// </summary>
    internal static string Render(IReadOnlyList<TranscriptCue> cues)
    {
        var turns = new List<string>();
        StringBuilder? current = null;
        string? currentSpeaker = null;

        foreach (var cue in cues)
        {
            var continues = current != null
                && (cue.Speaker != null
                    ? string.Equals(cue.Speaker, currentSpeaker, StringComparison.Ordinal)
                    : currentSpeaker == null && !EndsSentence(current));

            if (continues)
            {
                current!.Append(' ').Append(cue.Text);
                continue;
            }

            if (current != null)
            {
                turns.Add(current.ToString());
            }

            currentSpeaker = cue.Speaker;
            current = cue.Speaker == null
                ? new StringBuilder(cue.Text)
                : new StringBuilder(cue.Speaker).Append(": ").Append(cue.Text);
        }

        if (current != null)
        {
            turns.Add(current.ToString());
        }

        return string.Join('\n', turns);
    }

    private static bool EndsSentence(StringBuilder turn)
    {
        var end = turn.Length - 1;
        while (end >= 0 && turn[end] is '"' or '\'' or '”' or '’' or ')' or ']')
        {
            end--;
        }

        return end >= 0 && turn[end] is '.' or '?' or '!';
    }
}
