namespace Consultologist.Api.Documents;

/// <summary>
/// What a person is told when a document could not be read (#235,
/// docs/DOCUMENT_INPUT.md § 8, normative).
///
/// Server-side because that is already the pattern —
/// Account.ValidateDeliveryPassword returns the sentence the client
/// displays — and because it keeps one copy of the copy for the two places
/// that need it: the inline error on the Consults page (#236) and the
/// cause named in an email rejection (#237).
///
/// Every sentence says what happened and what to do instead. None of them
/// names a file, because the reply that carries them can never name a file.
/// </summary>
internal static class DocumentExtractionCopy
{
    internal static string For(string outcome) => outcome switch
    {
        DocumentExtractionOutcomes.UnsupportedType =>
            "We can read .txt, .md, .pdf, .docx and .html files — that one is something else.",

        DocumentExtractionOutcomes.NoTextLayer =>
            "This PDF has no text layer, so it is a scan or a fax. Paste the text instead, "
            + "or attach a PDF exported from your system.",

        DocumentExtractionOutcomes.PasswordProtected =>
            "This PDF is password-protected. Remove the password and try again, "
            + "or paste the text instead.",

        DocumentExtractionOutcomes.Corrupt =>
            "This file could not be read — it may be damaged or incomplete.",

        DocumentExtractionOutcomes.Empty =>
            "There is no text in this file.",

        DocumentExtractionOutcomes.TooLarge =>
            $"That file is larger than {DocumentExtraction.MaxBytes / (1024 * 1024)} MB.",

        DocumentExtractionOutcomes.TooManyPages =>
            $"That document has more than {DocumentExtraction.MaxPages} pages.",

        DocumentExtractionOutcomes.ExpandsTooLarge =>
            "That file unpacks to far more than it appears to contain.",

        DocumentExtractionOutcomes.TooMuchText =>
            "That document holds more text than one input can take. "
            + "Attach the relevant part instead.",

        DocumentExtractionOutcomes.TimedOut =>
            "This file took too long to read. It may be unusually complex or damaged.",

        // The one sentence here that is not about the file. It says so,
        // because "could not be read" would send someone looking for a fault
        // in a document that has none.
        DocumentExtractionOutcomes.Busy =>
            "We are reading several documents right now. Nothing is wrong with this one — try again in a moment.",

        // #239: also not about the file. OCR was tried on the scan and the
        // service was momentarily unavailable — the same "nothing is wrong
        // with this one" shape as Busy, so it must not read as a fault.
        DocumentExtractionOutcomes.OcrUnavailable =>
            "We could not read this scan right now. Nothing is wrong with it — try again in a moment, "
            + "or paste the text instead.",

        _ => "This file could not be read."
    };
}
