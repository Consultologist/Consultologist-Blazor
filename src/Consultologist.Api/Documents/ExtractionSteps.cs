using Consultologist.Api.Models;

namespace Consultologist.Api.Documents;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 3.1; D15): what a document's extraction used, read off the origin the
/// extraction recorded (its <see cref="ConsultInputOrigin.Extractor"/>, <c>name/version[+commit]</c>, and its kind), so the
/// 5–30 extraction is not touched: the parsing library and its version (PdfPig, DocumentFormat.OpenXml, AngleSharp, or the
/// engine's own reader of text, WebVTT and SubRip), and the OCR service and model when OCR read it (an image's text, or a
/// PDF without a text layer, which PdfPig parsed first). No step converts a document to Markdown today, so
/// <c>converter</c> is always null; a recording's transcription is neither a parse nor OCR, so it records neither. An
/// origin with no extractor (typed text, a previous run's deliverable) is returned as it is.
/// </summary>
internal static class ExtractionSteps
{
    /// <summary>The parsing library each extractor name stands for.</summary>
    private static readonly Dictionary<string, string> Parsers = new(StringComparer.Ordinal)
    {
        ["pdfpig"] = "PdfPig",
        ["openxml"] = "DocumentFormat.OpenXml",
        ["html"] = "AngleSharp",
        ["text"] = "text",
        ["vtt"] = "vtt",
        ["srt"] = "srt",
    };

    /// <summary>The OCR extractor's name (<see cref="AzureDocumentIntelligenceOcr"/>'s).</summary>
    private const string OcrExtractor = "docintel";

    /// <summary>
    /// <paramref name="origin"/> with its <c>converter</c>, <c>parser</c> and <c>ocr</c>, each null where the step was not used.
    /// #1508 (part 08 § 3.1): the OCR step's model is the dated version the service gave
    /// (<paramref name="ocrModelVersions"/>, by the document's file hash), <c>prebuilt-read/2024-11-30</c>, and
    /// <c>prebuilt-read</c> alone when it gave none.
    /// </summary>
    public static ConsultInputOrigin Of(ConsultInputOrigin origin, IReadOnlyDictionary<string, string>? ocrModelVersions = null)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (Split(origin.Extractor) is not { } extractor)
            return origin;
        var (name, version) = extractor;

        if (Parsers.TryGetValue(name, out var parser))
            return origin with { Converter = null, Parser = new ConsultExtractionStep(parser, version), Ocr = null };

        if (name == OcrExtractor)
        {
            // An image has no parse; a PDF reached OCR after PdfPig found no text layer in it.
            var parsed = string.Equals(origin.Kind, ConsultInputOriginKinds.Image, StringComparison.Ordinal)
                ? null
                : Split(PdfDocumentExtractor.ExtractorId) is { } pdf ? new ConsultExtractionStep(Parsers["pdfpig"], pdf.Version) : null;
            return origin with
            {
                Converter = null,
                Parser = parsed,
                Ocr = new ConsultOcrStep(
                    AzureDocumentIntelligenceOcr.Service,
                    origin.FileSha256 is { } file && ocrModelVersions?.GetValueOrDefault(file) is { } dated
                        ? $"{AzureDocumentIntelligenceOcr.Model}/{dated}"
                        : AzureDocumentIntelligenceOcr.Model),
            };
        }

        return origin;
    }

    /// <summary>An extractor id's name and version (<c>pdfpig/0.1.15+f131f642</c> → <c>pdfpig</c>, <c>0.1.15</c>); null when it has no <c>/</c>.</summary>
    private static (string Name, string Version)? Split(string? extractor)
    {
        var slash = extractor?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        if (extractor == null || slash < 1)
            return null;
        var rest = extractor[(slash + 1)..];
        var plus = rest.IndexOf('+', StringComparison.Ordinal);
        return (extractor[..slash], plus < 0 ? rest : rest[..plus]);
    }
}
