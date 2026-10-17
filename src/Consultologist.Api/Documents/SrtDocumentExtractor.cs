namespace Consultologist.Api.Documents;

/// <summary>
/// #994: SubRip as a first-class format, read as turns by
/// <see cref="TranscriptTurns"/> — the other container a recorded encounter's
/// transcript arrives in. Sits BEFORE the HTML entry in the format list: the
/// HTML sniffer's close-tag rule would claim an italic span's <c>&lt;/i&gt;</c>.
/// </summary>
internal static class SrtDocumentExtractor
{
    // Hand-versioned like text/1. Bump with any change to TranscriptTurns.
    internal const string ExtractorId = "srt/1";

    /// <summary>
    /// SubRip has no magic number; its signature is structural: the first
    /// non-blank line is a cue index (ASCII digits only) and the next is a
    /// timing line. Prose that happens to start with "1" fails the second
    /// test; HTML fails the first.
    /// </summary>
    internal static bool Matches(byte[] bytes)
    {
        if (!TextDocumentDecoder.LooksLikeText(bytes))
        {
            return false;
        }

        var lines = TranscriptTurns.SniffPrefix(bytes)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var first = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (first < 0 || first + 1 >= lines.Length)
        {
            return false;
        }

        var index = lines[first].Trim();
        return index.Length > 0
            && index.All(char.IsAsciiDigit)
            && TranscriptTurns.TimingLine.IsMatch(lines[first + 1]);
    }

    internal static DocumentExtractionResult Extract(byte[] bytes) =>
        TranscriptTurns.Extract(bytes, TranscriptSyntax.Srt, ExtractorId);
}
