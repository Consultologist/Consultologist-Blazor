namespace Consultologist.Api.Documents;

/// <summary>
/// #1063: recognises a recording (WAV, MP3, OGG/Opus, FLAC, M4A/MP4) by its
/// magic bytes. Like <see cref="ImageDocumentExtractor"/> it has no local
/// parse — audio carries no text — so <see cref="Extract"/> returns
/// <c>audio-needs-transcription</c> and the impure edge
/// (<see cref="DocumentExtraction.ExtractAsync"/>) routes it to speech-to-text,
/// or refuses it as <c>audio-not-enabled</c> when none is configured. The
/// format sniff is content-only, as every format's is: a recording dropped on
/// a plain text input transcribes the same way and keeps that input's origin
/// kind, as a VTT does (#994).
/// </summary>
internal static class AudioDocumentExtractor
{
    internal static bool Matches(byte[] bytes) =>
        IsWav(bytes) || IsMp3(bytes) || IsOgg(bytes) || IsFlac(bytes) || IsMp4(bytes);

    internal static DocumentExtractionResult Extract(byte[] bytes) =>
        DocumentExtractionResult.Refused(DocumentExtractionOutcomes.AudioNeedsTranscription);

    // RIFF....WAVE
    private static bool IsWav(byte[] b) =>
        b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
        && b[8] == (byte)'W' && b[9] == (byte)'A' && b[10] == (byte)'V' && b[11] == (byte)'E';

    // An ID3v2 tag, or a bare MPEG Layer III frame sync — 11 set bits, a valid
    // version, and the layer bits 01 (FF FB/FA/F3/F2/E3/E2, what every MP3
    // encoder emits first). Layer III only: a UTF-16 text file opens with the
    // byte-order mark FF FE, which reads as a Layer I sync and must stay text
    // (TextDocumentDecoder's BOM rule).
    private static bool IsMp3(byte[] b) =>
        (b.Length >= 3 && b[0] == (byte)'I' && b[1] == (byte)'D' && b[2] == (byte)'3')
        || (b.Length >= 2 && b[0] == 0xFF && (b[1] & 0xE0) == 0xE0 && (b[1] & 0x18) != 0x08 && (b[1] & 0x06) == 0x02);

    private static bool IsOgg(byte[] b) =>
        b.Length >= 4 && b[0] == (byte)'O' && b[1] == (byte)'g' && b[2] == (byte)'g' && b[3] == (byte)'S';

    private static bool IsFlac(byte[] b) =>
        b.Length >= 4 && b[0] == (byte)'f' && b[1] == (byte)'L' && b[2] == (byte)'a' && b[3] == (byte)'C';

    // An ISO base media file (M4A, MP4): the ftyp box at offset 4.
    private static bool IsMp4(byte[] b) =>
        b.Length >= 8 && b[4] == (byte)'f' && b[5] == (byte)'t' && b[6] == (byte)'y' && b[7] == (byte)'p';
}
