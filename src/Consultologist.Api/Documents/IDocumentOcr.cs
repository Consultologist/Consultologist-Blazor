namespace Consultologist.Api.Documents;

/// <summary>
/// #239: reads an image-only PDF (a scan or a fax) that carries no text layer,
/// the case <see cref="PdfDocumentExtractor"/> refuses as
/// <see cref="DocumentExtractionOutcomes.NoTextLayer"/>. This is the one part
/// of document reading that is NOT pure — it calls a cloud service — so it
/// lives behind an interface and is invoked only at the impure edge
/// (<see cref="DocumentExtraction.ExtractAsync"/>), never in the pure
/// <see cref="DocumentExtraction.Extract"/> format core.
///
/// The implementation never throws for a service fault: it catches its own SDK
/// exceptions and timeouts and returns <see cref="DocumentOcrStatus.Unavailable"/>,
/// so the edge maps a transient outage to a "try again" outcome without ever
/// knowing the SDK's types.
/// </summary>
public interface IDocumentOcr
{
    /// <summary>
    /// True when the OCR endpoint app setting is present. When false the edge
    /// leaves the <c>no-text-layer</c> outcome untouched — OCR is off and the
    /// scan gets today's "paste it instead" copy. Graceful degrade, never a
    /// throw, so the feature can deploy dark and be enabled per environment.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Reads the visible text of an image-only PDF. <paramref name="pageCount"/>
    /// is the parser's page count, carried for logging. Returns a status the
    /// edge maps: Extracted (text + extractor id), Empty (the scan held no
    /// readable text), or Unavailable (the service errored or timed out).
    /// </summary>
    Task<DocumentOcrResult> ReadAsync(byte[] pdfBytes, int? pageCount, CancellationToken cancellationToken);
}

public enum DocumentOcrStatus
{
    Extracted,
    Empty,
    Unavailable
}

public sealed record DocumentOcrResult(DocumentOcrStatus Status, string? Text, string? ExtractorId);
