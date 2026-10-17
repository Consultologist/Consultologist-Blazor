namespace Consultologist.Api.Documents;

/// <summary>
/// #994: WebVTT as a first-class format — a meeting transcript as Teams or
/// Zoom export it, read as turns by <see cref="TranscriptTurns"/>. Before
/// this a VTT fell through the text fallback verbatim: header, cue numbers
/// and timings became the "text" the model read and the record hashed.
///
/// Sits BEFORE the HTML entry in the format list: the HTML sniffer's
/// close-tag rule would claim a voice span's <c>&lt;/v&gt;</c>.
/// </summary>
internal static class VttDocumentExtractor
{
    // Hand-versioned like text/1: there is no library to name. Bump with any
    // change to TranscriptTurns (see its summary).
    internal const string ExtractorId = "vtt/1";

    /// <summary>
    /// The WebVTT file signature: the decoded prefix (BOM already dropped)
    /// starts with <c>WEBVTT</c> followed by the end of the file, a space, a
    /// tab or a line break. <c>WEBVTTX</c> is not a match.
    /// </summary>
    internal static bool Matches(byte[] bytes)
    {
        if (!TextDocumentDecoder.LooksLikeText(bytes))
        {
            return false;
        }

        var prefix = TranscriptTurns.SniffPrefix(bytes);
        return prefix.StartsWith("WEBVTT", StringComparison.Ordinal)
            && (prefix.Length == 6 || prefix[6] is ' ' or '\t' or '\r' or '\n');
    }

    internal static DocumentExtractionResult Extract(byte[] bytes) =>
        TranscriptTurns.Extract(bytes, TranscriptSyntax.Vtt, ExtractorId);
}
