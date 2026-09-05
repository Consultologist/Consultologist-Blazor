using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Consultologist.Api.Documents;

/// <summary>
/// Reads the text layer of a PDF (#235). One of the parser's registered
/// extractors — it knows about PDFs and nothing else knows about it.
/// </summary>
internal static class PdfDocumentExtractor
{
    /// <summary>
    /// The PDF header need not sit at offset 0 — the specification allows
    /// preceding bytes, and real files from scanners and mail gateways have
    /// them — so scan a prefix rather than testing byte 0.
    /// </summary>
    private const int HeaderSearchWindow = 1024;

    private static readonly byte[] Header = "%PDF-"u8.ToArray();

    /// <summary>
    /// Lenient parsing stays on (the default): turning it off rejects files
    /// every PDF viewer opens, which is the wrong trade for EMR output that
    /// is routinely out of spec.
    ///
    /// MaxStackDepth is lowered well below its default of 256. A crafted
    /// PDF can drive PdfPig into deep recursion and raise
    /// StackOverflowException, which .NET cannot catch — it takes the
    /// worker process down and every invocation sharing it. PdfPig 0.1.15
    /// guards the known path (#1274); this bounds the ones nobody has found
    /// yet. Referral PDFs do not nest anywhere near this deep.
    /// </summary>
    private static readonly ParsingOptions Options = new()
    {
        UseLenientParsing = true,
        MaxStackDepth = 32
    };

    internal static string ExtractorId { get; } =
        ExtractorIdentity.For("pdfpig", typeof(PdfDocument).Assembly);

    internal static bool Matches(byte[] bytes)
    {
        var window = Math.Min(bytes.Length, HeaderSearchWindow);

        for (var start = 0; start + Header.Length <= window; start++)
        {
            if (bytes.AsSpan(start, Header.Length).SequenceEqual(Header))
            {
                return true;
            }
        }

        return false;
    }

    internal static DocumentExtractionResult Extract(byte[] bytes)
    {
        try
        {
            // Scope the document as tightly as possible: PdfPig caches every
            // object it resolves for the document's lifetime with no
            // eviction, so memory is released on Dispose and not before.
            using var document = PdfDocument.Open(bytes, Options);

            if (document.NumberOfPages > DocumentExtraction.MaxPages)
            {
                // Page count comes from the page-tree walk Open already did;
                // no page content has been decoded yet, which is the whole
                // point of checking here.
                return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.TooManyPages);
            }

            var text = new StringBuilder();
            var letters = 0;
            var images = 0;

            foreach (var page in document.GetPages())
            {
                letters += page.Letters.Count;
                images += page.NumberOfImages;

                // Not page.Text: PdfPig's own documentation says not to use
                // it unless you know what you are doing, because it returns
                // raw content-stream order rather than reading order.
                text.Append(ContentOrderTextExtractor.GetText(page));
                text.Append('\n');

                if (text.Length > DocumentExtraction.MaxCharacters)
                {
                    return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.TooMuchText);
                }
            }

            if (letters == 0)
            {
                // No glyphs anywhere. Images present means a scan or a fax —
                // the case OCR answers (#239) and #188 is blocked on. No
                // images means the document is genuinely blank, and the two
                // deserve different copy. no-text-layer carries the page count
                // so the OCR fallback can cap pages and record what it read.
                return images > 0
                    ? DocumentExtractionResult.Refused(
                        DocumentExtractionOutcomes.NoTextLayer, document.NumberOfPages)
                    : DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Empty);
            }

            return DocumentExtractionResult.Extracted(
                text.ToString(),
                ExtractorId,
                document.NumberOfPages);
        }
        catch (PdfDocumentEncryptedException)
        {
            // Only a real user password reaches here: PdfPig always tries the
            // empty string, so permissions-only encryption — what hospital
            // systems emit, and what every viewer opens without prompting —
            // has already succeeded above.
            //
            // Deliberately not attempted: the account's delivery password.
            // This app encrypts outbound documents with it (#159), so trying
            // it against inbound files would make intake a decryption oracle
            // and quietly turn that password into an ingest credential
            // (docs/DOCUMENT_INPUT.md § 3).
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.PasswordProtected);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Broad by necessity, not by laziness. PdfPig's own open issues
            // (#1268, #1277) document IndexOutOfRangeException and
            // NullReferenceException escaping from malformed files, because
            // untrusted xref offsets are used without bounds checks. From
            // hostile input those are expected results, not defects here, so
            // every one of them is a corrupt document.
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Corrupt);
        }
    }
}
