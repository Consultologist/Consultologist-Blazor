namespace Consultologist.Api.Documents;

/// <summary>
/// #730: recognizes a raw image (PNG/JPEG/TIFF) by its magic bytes. Unlike the
/// other extractors it has no local parse — an image carries no text layer —
/// so <see cref="Extract"/> returns <c>image-needs-ocr</c> and the impure edge
/// (<see cref="DocumentExtraction.ExtractAsync"/>) routes it to Azure Document
/// Intelligence, exactly as an image-only PDF's <c>no-text-layer</c> does. An
/// image is a single page; the count is 1 so the OCR page cap still applies.
/// </summary>
internal static class ImageDocumentExtractor
{
    // Signatures at offset 0. PNG's 8-byte header, JPEG's SOI+marker, and
    // TIFF's little- and big-endian byte-order marks. Azure DI's prebuilt-read
    // reads all of these; the format sniff is Azure's, this only routes.
    private static readonly byte[][] Signatures =
    [
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], // PNG
        [0xFF, 0xD8, 0xFF],                               // JPEG
        [0x49, 0x49, 0x2A, 0x00],                         // TIFF (little-endian)
        [0x4D, 0x4D, 0x00, 0x2A],                         // TIFF (big-endian)
    ];

    internal static bool Matches(byte[] bytes) =>
        Signatures.Any(signature =>
            bytes.Length >= signature.Length && bytes.AsSpan(0, signature.Length).SequenceEqual(signature));

    internal static DocumentExtractionResult Extract(byte[] bytes) =>
        DocumentExtractionResult.Refused(DocumentExtractionOutcomes.ImageNeedsOcr, pageCount: 1);
}
