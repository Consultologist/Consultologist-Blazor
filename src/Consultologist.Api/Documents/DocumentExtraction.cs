namespace Consultologist.Api.Documents;

/// <summary>
/// What became of an attempt to read a document (#235). Dispositions, so
/// kebab-case values — the same shape and convention as
/// <see cref="Email.EmailIntakeOutcomes"/>. Persisted entity statuses are
/// PascalCase in this codebase; these are neither persisted nor statuses.
/// </summary>
public static class DocumentExtractionOutcomes
{
    public const string Extracted = "extracted";
    public const string UnsupportedType = "unsupported-type";
    public const string Corrupt = "corrupt";
    public const string PasswordProtected = "password-protected";
    // A PDF with pages but no glyphs: a scan or a fax. Its own outcome
    // because the copy differs, and because it is the case #188's fax
    // parity is blocked on until OCR (#239).
    public const string NoTextLayer = "no-text-layer";
    public const string Empty = "empty";
    public const string TooLarge = "too-large";
    public const string TooManyPages = "too-many-pages";
    // #240: a container whose declared contents dwarf the file itself. Its own
    // outcome because the file was within every byte bound and still cannot be
    // read safely.
    public const string ExpandsTooLarge = "expands-too-large";
    // Never truncation: half a referral generating a whole consult is a
    // clinical wrong-data error (docs/DOCUMENT_INPUT.md § 4).
    public const string TooMuchText = "too-much-text";
    // #235: the parse outran its wall clock. See ExtractAsync for what this
    // does and does not bound.
    public const string TimedOut = "timed-out";
    // #241: no parse slot came free in time. The only outcome here that is
    // about us rather than about the document — the file may be perfectly
    // readable, and resending is the right advice.
    public const string Busy = "busy";
    // #239: OCR was configured and tried on an image-only PDF, but the service
    // errored or timed out. Transient and about us, like Busy — never tell a
    // clinician a readable fax is unreadable — so 503 and "try again", not a
    // 422 refusal of the file.
    public const string OcrUnavailable = "ocr-unavailable";
    // #239: OCR read the scan, but its mean word confidence was below the
    // minimum the account set in its profile. A policy refusal, not a service
    // fault — 422, and the copy points at the profile setting.
    public const string OcrLowConfidence = "ocr-low-confidence";
}

internal sealed record DocumentExtractionResult(
    string Outcome,
    string? Text,
    string? ExtractorId,
    int? PageCount,
    // #240: the document carried tracked changes and this is the accepted
    // view of them. Recorded because taking that view drops content that was
    // in the file — correct, since the author deleted it, but still a drop,
    // and this project makes its drops visible.
    bool TrackedChangesResolved = false)
{
    internal static DocumentExtractionResult Refused(string outcome) => new(outcome, null, null, null);

    // #239: a refusal that still carries the page count. no-text-layer uses it
    // so the OCR fallback can enforce a page cap and record how many pages it
    // read — the count comes from the parse, so this stays pure.
    internal static DocumentExtractionResult Refused(string outcome, int? pageCount) =>
        new(outcome, null, null, pageCount);

    internal static DocumentExtractionResult Extracted(
        string text,
        string extractorId,
        int? pageCount,
        bool trackedChangesResolved = false) =>
        new(DocumentExtractionOutcomes.Extracted, text, extractorId, pageCount, trackedChangesResolved);
}

/// <summary>
/// The sole authority on document formats (#234, docs/DOCUMENT_INPUT.md
/// § 1-§ 2). Nothing outside this folder knows what a PDF is: the two
/// sources — the Consults upload and email intake — hand over bytes and
/// render or map whatever named outcome comes back.
///
/// Dispatch is on content, never on a filename. A source-side extension
/// gate would be a second authority that can disagree with this one, and a
/// .txt full of PDF bytes is the case that exposes it.
///
/// <see cref="Extract"/> is pure: no I/O, no configuration, no logging.
/// Adding a format means one entry in <see cref="Formats"/> plus its
/// extractor, and nothing else anywhere — which is #240's acceptance
/// criterion.
///
/// <see cref="ExtractAsync"/> is where that stops, and deliberately: it owns
/// the wall clock and, since #241, the concurrency gate, which reads one app
/// setting. Both bound the *running* of a parse rather than the reading of a
/// format, so the pure core stays pure and the impure edge is one method.
///
/// The one dependency outside this folder is
/// <see cref="CanonicalText.Normalize"/>, and it is not a format concern: it
/// is the canonicalisation the job starter applies to every input, shared
/// here so a document and the same text typed cannot differ on line endings
/// alone (#251). Formats stay sealed in; text canonicalisation was never in.
/// </summary>
internal static class DocumentExtraction
{
    // Bytes accepted. Bounds what arrives, and only that: PdfPig retains
    // every object it resolves for a document's lifetime, so peak memory is
    // a multiple of this rather than a fraction (docs/DOCUMENT_INPUT.md § 4).
    internal const int MaxBytes = 10 * 1024 * 1024;

    // Parse cost is per page, not per byte — a small file can declare
    // thousands of pages.
    internal const int MaxPages = 100;

    // Characters produced, mirroring ConsultGenerationJobs.MaxInputLength:
    // text that clears this bound is text the API would accept as an input.
    internal const int MaxCharacters = 256 * 1024;

    // #240: a container's cost is not its size. Read from the central
    // directory, which costs no decompression — a hostile archive can lie
    // there, so this is a first bound and not the only one (#241).
    internal const long MaxExpandedBytes = 100L * 1024 * 1024;
    internal const int MaxArchiveEntries = 512;

    internal static readonly TimeSpan MaxParseDuration = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How many documents may be parsed at once in this worker (#241,
    /// docs/DOCUMENT_INPUT.md § 9).
    ///
    /// Four, and CPU decides that as much as memory does. A 2048 MB Flex
    /// Consumption instance — what this app runs on — has one CPU core, so
    /// past a handful of concurrent parses there is no throughput to gain and
    /// only memory to lose. Four sharing one core still finish inside
    /// MaxParseDuration; the platform's default of sixteen would not
    /// reliably.
    ///
    /// On memory: a realistic parse retains 10-15 MB (measured against
    /// PdfPig 0.1.15 — cost tracks pages and extracted text, not file size),
    /// and even at a pessimistic 100 MB for a pathological one, four is
    /// 400 MB inside 2048.
    ///
    /// An app setting so it can be tuned without a deploy.
    /// </summary>
    internal static int MaxConcurrentParses { get; } =
        int.TryParse(Environment.GetEnvironmentVariable("DocumentExtraction__MaxConcurrentParses"), out var configured)
        && configured > 0
            ? configured
            : 4;

    /// <summary>
    /// #239: the most pages an image-only PDF may have before OCR is skipped
    /// and it degrades to <c>no-text-layer</c>. OCR is priced per page, so this
    /// bounds per-document cost; it defaults to <see cref="MaxPages"/> (a scan
    /// the parser accepted is one OCR tries to read) and is tunable per
    /// environment without a deploy. Over the cap is a skip, not a truncation —
    /// the project never reads half a document (§ 4).
    /// </summary>
    internal static int OcrMaxPages =>
        int.TryParse(Environment.GetEnvironmentVariable("DocumentExtraction__OcrMaxPages"), out var ocrPages)
        && ocrPages > 0
            ? ocrPages
            : MaxPages;

    /// <summary>
    /// What an interactive caller waits for a slot before being told we are
    /// busy: the preview endpoint and app-originated job start, where someone
    /// is watching a spinner.
    /// </summary>
    internal static readonly TimeSpan InteractiveGateWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// What the email poller waits. Long enough that expiry means a real
    /// outage rather than a busy minute, because <c>busy</c> must never reach
    /// that door: every start-failure path in EmailIntakeProcessor moves the
    /// message to the Rejected folder and replies to the sender, so a
    /// transient refusal would permanently reject a referral and tell a
    /// clinician their document could not be read — which would be false.
    ///
    /// Bounded rather than infinite because a slot can be held by an orphaned
    /// parse (see ExtractAsync), so waiting forever could hang the poll.
    /// </summary>
    internal static readonly TimeSpan BackgroundGateWait = TimeSpan.FromMinutes(5);

    private static readonly SemaphoreSlim ParseGate = new(MaxConcurrentParses, MaxConcurrentParses);

    private sealed record DocumentFormat(Func<byte[], bool> Matches, Func<byte[], DocumentExtractionResult> Extract);

    /// <summary>
    /// Signature-matched formats, tried in order. Text is not here because
    /// it has no signature — it is the fallback in <see cref="Extract"/>,
    /// which is the honest way to say "everything else, if it reads as
    /// text".
    /// </summary>
    private static readonly IReadOnlyList<DocumentFormat> Formats =
    [
        new DocumentFormat(PdfDocumentExtractor.Matches, PdfDocumentExtractor.Extract),
        new DocumentFormat(DocxDocumentExtractor.Matches, DocxDocumentExtractor.Extract),
        // #655: before the text fallback, so HTML yields visible text rather
        // than the tag soup the decoder would return for it.
        new DocumentFormat(HtmlDocumentExtractor.Matches, HtmlDocumentExtractor.Extract)
    ];

    /// <summary>
    /// Reads a document, bounded by the wall clock.
    /// </summary>
    /// <remarks>
    /// Honest about what this bounds: PdfPig is synchronous and accepts no
    /// CancellationToken, so the timeout releases the *caller* and does not
    /// stop the *work* — an orphaned thread keeps running until it finishes
    /// or the worker recycles. It is still worth having, because the
    /// alternative is a request that never returns, and a hang would
    /// otherwise be the one failure with no named outcome. Bounding the
    /// work itself needs process isolation, which #241 weighs.
    ///
    /// The timeout wraps the whole parse rather than a page loop: PdfPig
    /// resolves the header, the cross-reference table — including a
    /// brute-force rescan of the entire file when that table is damaged —
    /// and the page tree inside Open, which is where its historical hangs
    /// lived.
    ///
    /// <paramref name="gateWait"/> is how long to wait for one of
    /// <see cref="MaxConcurrentParses"/> slots. It is deliberately separate
    /// from <see cref="MaxParseDuration"/>: queueing must not eat the parse
    /// budget, or a busy minute would turn readable documents into
    /// <c>timed-out</c>.
    /// </remarks>
    internal static Task<DocumentExtractionResult> ExtractAsync(
        byte[] bytes,
        TimeSpan gateWait,
        CancellationToken cancellationToken,
        IDocumentOcr? ocr = null,
        double? ocrMinConfidence = null) =>
        ExtractAsync(bytes, gateWait, ParseGate, cancellationToken, ocr, ocrMinConfidence);

    /// <summary>
    /// The gate as a parameter, so saturation can be tested by handing in a
    /// semaphore with no permits rather than by racing real parses against a
    /// clock. Production always uses <see cref="ParseGate"/>.
    /// </summary>
    internal static async Task<DocumentExtractionResult> ExtractAsync(
        byte[] bytes,
        TimeSpan gateWait,
        SemaphoreSlim gate,
        CancellationToken cancellationToken,
        IDocumentOcr? ocr = null,
        double? ocrMinConfidence = null)
    {
        if (!await gate.WaitAsync(gateWait, cancellationToken))
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Busy);
        }

        // The token is deliberately NOT passed to Task.Run. An already-
        // cancelled token would cancel the task without ever running the
        // delegate, so the finally below would not run and the slot would
        // leak. The work is not cancellable once started anyway — that is
        // what the remarks above say — so the token bought nothing here.
        var parse = Task.Run(() =>
        {
            try
            {
                return Extract(bytes);
            }
            finally
            {
                // Released when the WORK finishes, not when the caller stops
                // waiting. A timed-out parse keeps its slot until the orphan
                // finishes, which is correct: it is still holding the memory
                // this gate exists to bound.
                gate.Release();
            }
        });

        DocumentExtractionResult result;

        try
        {
            result = await parse.WaitAsync(MaxParseDuration, cancellationToken);
        }
        catch (TimeoutException)
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.TimedOut);
        }

        // #239: OCR fallback for image-only PDFs, on this impure edge only —
        // the pure Extract cannot make a network call. Fires ONLY on
        // no-text-layer (a text PDF, a corrupt one, a timed-out one, an empty
        // one never reach OCR — cost and correctness both), and only when OCR
        // is configured and the scan is within the page cap; anything else
        // returns the parser's result untouched, so an unconfigured or absent
        // ocr leaves today's behaviour exactly as it was. The parse-gate slot
        // is already released (the finally in the Task.Run delegate ran before
        // the await above returned), so this network call is outside the gate
        // and outside MaxParseDuration — the OCR reader owns its own clock.
        if (ocr is { IsConfigured: true }
            && string.Equals(result.Outcome, DocumentExtractionOutcomes.NoTextLayer, StringComparison.Ordinal)
            && result.PageCount is int pages
            && pages <= OcrMaxPages)
        {
            var ocrResult = await ocr.ReadAsync(bytes, result.PageCount, cancellationToken);

            return ocrResult.Status switch
            {
                DocumentOcrStatus.Extracted =>
                    AcceptOrGateOcr(ocrResult, result.PageCount, ocrMinConfidence),
                DocumentOcrStatus.Empty =>
                    DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Empty),
                _ =>
                    DocumentExtractionResult.Refused(DocumentExtractionOutcomes.OcrUnavailable),
            };
        }

        return result;
    }

    /// <summary>
    /// #239: the account's OCR confidence policy. When a minimum is set and the
    /// mean word confidence is below it, refuse as <c>ocr-low-confidence</c>
    /// rather than feed a doubtful read into a consult — a misread dose is a
    /// clinical error. No minimum (the gate off) accepts whatever OCR returned.
    /// Accepted text goes through Normalize, like every other path, so
    /// whitespace-only OCR output still folds to <c>empty</c>.
    /// </summary>
    private static DocumentExtractionResult AcceptOrGateOcr(
        DocumentOcrResult ocr, int? pageCount, double? minConfidence)
    {
        if (minConfidence is double min && ocr.MeanConfidence is double mean && mean < min)
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.OcrLowConfidence, pageCount);
        }

        return Normalize(DocumentExtractionResult.Extracted(ocr.Text!, ocr.ExtractorId!, pageCount));
    }

    internal static DocumentExtractionResult Extract(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Empty);
        }

        if (bytes.Length > MaxBytes)
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.TooLarge);
        }

        foreach (var format in Formats)
        {
            if (format.Matches(bytes))
            {
                return Normalize(format.Extract(bytes));
            }
        }

        if (!TextDocumentDecoder.LooksLikeText(bytes))
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.UnsupportedType);
        }

        var text = TextDocumentDecoder.Decode(bytes);

        if (text.Length > MaxCharacters)
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.TooMuchText);
        }

        return Normalize(DocumentExtractionResult.Extracted(text, TextDocumentDecoder.ExtractorId, null));
    }

    /// <summary>
    /// Conservative and nothing more: line endings to LF, trailing
    /// whitespace off the end — <see cref="LineEndings.Normalize"/>, the
    /// same call the job starter makes over every input so that a document
    /// and the same text typed cannot differ on line endings alone.
    ///
    /// Deliberately not done here (docs/DOCUMENT_INPUT.md § 2): de-hyphenating
    /// words split across a line break, rejoining hard-wrapped lines into
    /// paragraphs, stripping repeated page headers. Each reads better and
    /// each can corrupt clinical text — rejoining lines turns a
    /// one-per-line medication list into a run-on sentence.
    /// </summary>
    private static DocumentExtractionResult Normalize(DocumentExtractionResult result)
    {
        if (result.Text == null)
        {
            return result;
        }

        var text = CanonicalText.Normalize(result.Text);

        // A document of nothing but whitespace is empty, not extracted —
        // otherwise it would fill a required input slot with a blank string
        // and the job would reject it later with a worse message.
        return text.Length == 0
            ? DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Empty)
            : result with { Text = text };
    }

    /// <summary>
    /// True when text came out and there is some of it. Everything else is
    /// a refusal the caller renders or maps.
    /// </summary>
    internal static bool Succeeded(DocumentExtractionResult result) =>
        string.Equals(result.Outcome, DocumentExtractionOutcomes.Extracted, StringComparison.Ordinal);
}
