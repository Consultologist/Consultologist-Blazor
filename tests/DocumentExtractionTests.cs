using System.Text;
using Consultologist.Api.Documents;
using Consultologist.Api.Email;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Security;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using System.IO.Compression;

namespace Consultologist.Api.Tests;

/// <summary>
/// The parser that is the sole authority on document formats (#235,
/// docs/DOCUMENT_INPUT.md § 1-§ 4). Dispatch is on content rather than on a
/// filename, every failure is a named outcome rather than an exception, and
/// nothing is ever truncated.
///
/// Every fixture is built here rather than checked in: this repository has
/// never committed a binary test file, and PDFsharp — already a dependency —
/// can produce each case the parser needs to distinguish.
/// </summary>
public class DocumentExtractionTests
{
    private const string Referral = "Emily Lee is a 54 year old woman.\nOncotype DX recurrence score 20.";

    static DocumentExtractionTests()
    {
        // PDFsharp's Core build resolves no system fonts on Linux, so writing
        // a PDF with text needs a resolver. The production renderer embeds
        // Liberation Sans for the same reason (#159); borrow it rather than
        // shipping a second copy.
        GlobalFontSettings.FontResolver ??= new EmbeddedLiberationSans();
    }

    // ---- text ----------------------------------------------------------

    [Fact]
    public void Utf8Text_IsExtracted()
    {
        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(Referral));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Equal(Referral, result.Text);
        Assert.Equal("text/1", result.ExtractorId);
    }

    [Fact]
    public void Utf8WithBom_DropsTheMarkInsteadOfSmugglingIt()
    {
        // A retained U+FEFF is invisible in every viewer and changes the
        // effective-input hash, so the same file would hash differently
        // through the two doors (#242).
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Referral)).ToArray();

        var result = DocumentExtraction.Extract(bytes);

        Assert.Equal(Referral, result.Text);
        Assert.DoesNotContain('﻿', result.Text!);
    }

    [Fact]
    public void Utf16Text_IsDecodedFromItsByteOrderMark()
    {
        // Blind UTF-8 decoding turns this into "\0R\0e\0f..." — the defect
        // #242 records.
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Referral)).ToArray();

        var result = DocumentExtraction.Extract(bytes);

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Equal(Referral, result.Text);
    }

    [Fact]
    public void Windows1252Text_FallsBackRatherThanCorrupting()
    {
        // The characters that matter clinically are the ones Word emits:
        // accented names and an em dash. Blind UTF-8 replaces each with
        // U+FFFD.
        const string prose = "Résumé of prior notes — see attached.";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var result = DocumentExtraction.Extract(Encoding.GetEncoding(1252).GetBytes(prose));

        Assert.Equal(prose, result.Text);
        Assert.DoesNotContain('�', result.Text!);
    }

    [Fact]
    public void BinaryBytes_AreRefusedAsUnsupported()
    {
        var result = DocumentExtraction.Extract([0x00, 0x01, 0x02, 0x00, 0xFF]);

        Assert.Equal(DocumentExtractionOutcomes.UnsupportedType, result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public void CarriageReturnsAreNormalisedAway()
    {
        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes("One.\r\nTwo.\r\n"));

        Assert.Equal("One.\nTwo.", result.Text);
    }

    [Fact]
    public void ALoneCarriageReturn_IsALineEndingToo()
    {
        // Classic Mac endings, and what some PDF viewers put on the
        // clipboard. These survived normalisation until #251, so the same
        // referral typed by hand was different input to the hash.
        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes("One.\rTwo.\r"));

        Assert.Equal("One.\nTwo.", result.Text);
    }


    [Fact]
    public void WhitespaceOnly_IsEmptyRatherThanExtracted()
    {
        // Otherwise it would fill a required slot with a blank string and be
        // rejected later with a worse message.
        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes("   \n\n  \n"));

        Assert.Equal(DocumentExtractionOutcomes.Empty, result.Outcome);
    }

    [Fact]
    public void TextOverTheCharacterCap_IsRefusedNeverTruncated()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', DocumentExtraction.MaxCharacters + 1));

        var result = DocumentExtraction.Extract(bytes);

        Assert.Equal(DocumentExtractionOutcomes.TooMuchText, result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public void BytesOverTheFileCap_AreRefusedBeforeAnyParsing()
    {
        var result = DocumentExtraction.Extract(new byte[DocumentExtraction.MaxBytes + 1]);

        Assert.Equal(DocumentExtractionOutcomes.TooLarge, result.Outcome);
    }

    // ---- pdf -----------------------------------------------------------

    [Fact]
    public void TextPdf_IsExtracted()
    {
        var result = DocumentExtraction.Extract(TextPdf(Referral));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Contains("Emily Lee", result.Text);
        Assert.StartsWith("pdfpig/", result.ExtractorId);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void PdfWhoseHeaderIsNotAtOffsetZero_IsStillRecognised()
    {
        // The specification allows preceding bytes and real scanners emit
        // them. Dispatching on a filename would not have noticed either way.
        var bytes = Encoding.ASCII.GetBytes("junk").Concat(TextPdf(Referral)).ToArray();

        var result = DocumentExtraction.Extract(bytes);

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
    }

    [Fact]
    public void ContentPicksTheExtractor_AndThereIsNoFilenameToMislead()
    {
        // The invariant the re-cut turns on: a source-side extension gate
        // could disagree with this, which is why both were deleted rather
        // than extended. Extract takes bytes and nothing else.
        Assert.Equal("text/1", DocumentExtraction.Extract(Encoding.UTF8.GetBytes(Referral)).ExtractorId);
        Assert.StartsWith("pdfpig/", DocumentExtraction.Extract(TextPdf(Referral)).ExtractorId);
    }

    [Fact]
    public void UserPasswordPdf_IsRefusedAndNeverGuessedAt()
    {
        // Produced by the app's own delivery renderer, which is exactly the
        // file a user might reply with. It must refuse — trying the account's
        // delivery password here would make intake a decryption oracle
        // (docs/DOCUMENT_INPUT.md § 3).
        var bytes = ConsultDocumentPdf.Render("# Consult\n\nBody.", "correct-horse-battery-16");

        var result = DocumentExtraction.Extract(bytes);

        Assert.Equal(DocumentExtractionOutcomes.PasswordProtected, result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public void OwnerPasswordOnlyPdf_IsExtracted()
    {
        // Permissions-only encryption, which hospital and EMR systems emit
        // constantly and every viewer opens without prompting. Refusing these
        // would reject a large share of legitimate referrals.
        var result = DocumentExtraction.Extract(TextPdf(Referral, ownerPassword: "owner-secret-1234"));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Contains("Emily Lee", result.Text);
    }

    [Fact]
    public void PdfWithPagesButNoText_IsNotReportedAsSuccess()
    {
        var result = DocumentExtraction.Extract(BlankPdf(pages: 2));

        Assert.NotEqual(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Equal(DocumentExtractionOutcomes.Empty, result.Outcome);
    }

    [Fact]
    public void ImageOnlyPdf_IsNoTextLayerRatherThanEmpty()
    {
        // The scan-or-fax case, and the most likely refusal once PDFs are
        // accepted. It is distinct from a blank page because the copy differs
        // and because this is precisely what #188's fax parity waits on OCR
        // (#239) for.
        var result = DocumentExtraction.Extract(ImageOnlyPdf());

        Assert.Equal(DocumentExtractionOutcomes.NoTextLayer, result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public void PdfOverThePageCap_IsRefusedBeforeContentIsDecoded()
    {
        var result = DocumentExtraction.Extract(BlankPdf(DocumentExtraction.MaxPages + 1));

        Assert.Equal(DocumentExtractionOutcomes.TooManyPages, result.Outcome);
    }

    [Fact]
    public void TruncatedPdf_IsCorruptRatherThanThrowing()
    {
        // This covers the format-exception path only: truncation was measured
        // across 95 cut points and produced PdfDocumentFormatException every
        // time it failed at all. The catch in PdfDocumentExtractor is
        // deliberately wider than this test proves, because PdfPig's open
        // issues (#1268, #1277) document IndexOutOfRangeException and
        // NullReferenceException escaping from crafted files whose xref
        // offsets go unchecked. Those need the fuzzer's own inputs to
        // reproduce (#241), so do not narrow that catch to match this test.
        var whole = TextPdf(Referral);

        var result = DocumentExtraction.Extract(whole.Take(whole.Length * 2 / 5).ToArray());

        Assert.Equal(DocumentExtractionOutcomes.Corrupt, result.Outcome);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsTheSameResultWhenItBeatsTheClock()
    {
        var result = await DocumentExtraction.ExtractAsync(
            Encoding.UTF8.GetBytes(Referral),
            DocumentExtraction.InteractiveGateWait,
            CancellationToken.None);

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Equal(Referral, result.Text);
    }

    // ---- the concurrency gate (#241, § 9) -------------------------------
    //
    // The gate is passed in rather than raced against a clock: saturation is
    // "no permits", which is a state, not a timing coincidence.

    [Fact]
    public async Task WhenNoSlotComesFree_TheOutcomeIsBusyRatherThanAnError()
    {
        using var saturated = new SemaphoreSlim(0, 1);

        var result = await DocumentExtraction.ExtractAsync(
            Encoding.UTF8.GetBytes(Referral),
            TimeSpan.Zero,
            saturated,
            CancellationToken.None);

        Assert.Equal(DocumentExtractionOutcomes.Busy, result.Outcome);
        Assert.Null(result.Text);
    }

    [Fact]
    public async Task ASlotIsReturnedWhenTheParseFinishes()
    {
        using var gate = new SemaphoreSlim(1, 1);

        var result = await DocumentExtraction.ExtractAsync(
            Encoding.UTF8.GetBytes(Referral),
            TimeSpan.Zero,
            gate,
            CancellationToken.None);

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);

        // Released by the work, not by the caller. A leak here would starve
        // the worker one slot per parse until nothing could be read at all.
        Assert.Equal(1, gate.CurrentCount);
    }

    [Fact]
    public async Task ACallerThatQueuesForASlotStillGetsItsDocument()
    {
        // Waiting is not refusing: a caller that queues and then gets a slot
        // reads normally.
        //
        // Note what this does NOT prove. That the gate wait is separate from
        // MaxParseDuration holds by construction — they are two distinct
        // awaits — and cannot be shown here without a test that runs longer
        // than the 20s parse budget. Naming it after the budget would have
        // claimed more than it checks.
        using var gate = new SemaphoreSlim(0, 1);
        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            gate.Release();
        });

        var result = await DocumentExtraction.ExtractAsync(
            Encoding.UTF8.GetBytes(Referral),
            TimeSpan.FromSeconds(30),
            gate,
            CancellationToken.None);

        await release;

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Equal(Referral, result.Text);
    }

    [Fact]
    public void TheGateIsSmallerThanThePlatformDefault()
    {
        // 16 is what a 2048 MB Flex Consumption instance allows by default,
        // and that instance has one CPU core. A gate at or above the platform
        // default would not be a gate.
        Assert.InRange(DocumentExtraction.MaxConcurrentParses, 1, 15);
    }

    // ---- html (#655) ---------------------------------------------------

    [Fact]
    public void HtmlNote_IsExtractedAsVisibleText()
    {
        var html = "<html><body>" +
            "<div>Emily Lee is a 54 year old woman.</div>" +
            "<div>Oncotype DX recurrence score 20.</div>" +
            "</body></html>";

        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(html));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Contains("Emily Lee is a 54 year old woman.", result.Text);
        Assert.Contains("Oncotype DX recurrence score 20.", result.Text);
        Assert.DoesNotContain("<div>", result.Text);
        Assert.StartsWith("html/", result.ExtractorId);
    }

    [Fact]
    public void HtmlFragment_IsRecognized()
    {
        // The Epic shape: no <html> root, a bare fragment — recognized by
        // the close-tag sniff, not a document root.
        var fragment = "<div class=\"fmtConv\"><span>Referral acknowledged.</span></div>";

        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(fragment));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Contains("Referral acknowledged.", result.Text);
    }

    [Fact]
    public void HtmlEntities_AreDecoded()
    {
        var html = "<p>BP &lt; 140/90 &amp; stable</p>";

        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(html));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Contains("BP < 140/90 & stable", result.Text);
    }

    [Fact]
    public void ScriptAndStyle_AreNotExtracted()
    {
        var html = "<html><head><style>.x{color:red}</style></head><body>" +
            "<script>alert('x')</script><p>Note body.</p></body></html>";

        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(html));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Contains("Note body.", result.Text);
        Assert.DoesNotContain("alert", result.Text);
        Assert.DoesNotContain("color:red", result.Text);
    }

    [Fact]
    public void PdfRenderedAsHtml_IsCorrupt()
    {
        // The sandbox note's actual shape: a PDF rendered into spans. The
        // stripped text is PDF source, not prose — it stays corrupt (#655),
        // detected, never unwrapped.
        var html = "<div class=\"fmtConv\">" +
            "<div><span>%PDF-1.7</span></div>" +
            "<div><span>" + new string('\u0001', 200) + "</span></div>" +
            "</div>";

        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(html));

        Assert.Equal(DocumentExtractionOutcomes.Corrupt, result.Outcome);
    }

    [Fact]
    public void EmptyHtml_IsEmpty()
    {
        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes("<div></div>"));

        Assert.Equal(DocumentExtractionOutcomes.Empty, result.Outcome);
    }

    [Fact]
    public void PlainTextWithAngleBrackets_IsStillText()
    {
        // The guard: prose with a stray <threshold> and no close tag must
        // NOT be claimed by HTML — it falls to the text decoder verbatim.
        var text = "The value is <threshold> today, and the plan holds.";

        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(text));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Equal(text, result.Text);
        Assert.Equal("text/1", result.ExtractorId);
    }

    [Fact]
    public void HtmlOverTheCharacterCap_IsTooMuchText()
    {
        var body = new string('a', 256 * 1024 + 1);
        var html = "<html><body><p>" + body + "</p></body></html>";

        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(html));

        Assert.Equal(DocumentExtractionOutcomes.TooMuchText, result.Outcome);
    }

    // ---- docx ----------------------------------------------------------

    [Fact]
    public void Docx_IsExtracted()
    {
        var result = DocumentExtraction.Extract(Docx());

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Contains("First paragraph.", result.Text);
        Assert.StartsWith("openxml/", result.ExtractorId);
    }

    [Fact]
    public void DocxParagraphs_AreSeparated()
    {
        // InnerText concatenates the whole body with no separators, so two
        // paragraphs arrive as one sentence.
        var result = DocumentExtraction.Extract(Docx());

        Assert.Contains("First paragraph.\nSecond paragraph.", result.Text);
    }

    [Fact]
    public void DocxTable_KeepsItsCellAndRowBoundaries()
    {
        // Losing these turns a medication list into "Amlodipine5 mgRamipril10 mg",
        // which is the same failure the milestone refused when it declined to
        // rejoin hard-wrapped PDF lines.
        var result = DocumentExtraction.Extract(Docx());

        Assert.Contains("Amlodipine\t5 mg\nRamipril\t10 mg", result.Text);
    }

    [Fact]
    public void DocxTrackedChanges_YieldTheAcceptedView()
    {
        // The reason this rule exists, in one assertion. A naive walk of a
        // document whose author changed a dose from 5 mg to 10 mg produces
        // "Dose is 10 mg5 mg daily." — both values, adjacent, in clinical text.
        var result = DocumentExtraction.Extract(Docx());

        Assert.Contains("Dose is 10 mg daily.", result.Text);
        Assert.DoesNotContain("10 mg5 mg", result.Text);
    }

    [Fact]
    public void DocxWithRevisions_RecordsThatTheAcceptedViewWasTaken()
    {
        // Taking the accepted view drops content that was in the file —
        // correct, since the author deleted it, but still a drop, and this
        // project makes its drops visible.
        Assert.True(DocumentExtraction.Extract(Docx()).TrackedChangesResolved);
    }

    [Fact]
    public void DocxWithoutRevisions_ClaimsNothingAboutThem()
    {
        var plain = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(Referral));

        Assert.False(plain.TrackedChangesResolved);
    }

    [Fact]
    public void DocxHiddenText_IsExcluded()
    {
        // Marked vanish: not text the sender is showing anyone.
        var result = DocumentExtraction.Extract(Docx());

        Assert.DoesNotContain("HIDDEN-MARKER", result.Text);
    }

    [Fact]
    public void DocxHeader_IsIncluded()
    {
        // A referral's date and clinic often live only in the letterhead, and
        // dropping content the sender supplied is the silent loss this project
        // refuses elsewhere.
        var result = DocumentExtraction.Extract(Docx());

        Assert.Contains("Meadowbrook Oncology", result.Text);
    }

    [Fact]
    public void Xlsx_IsUnsupportedRatherThanCorrupt()
    {
        // Measured, not assumed: opening a spreadsheet as a WordprocessingDocument
        // succeeds and yields a MainDocumentPart, so "has a main part" is not
        // the discriminator — the content type is. Nothing is wrong with the
        // file; it is simply not one we read.
        var result = DocumentExtraction.Extract(Xlsx());

        Assert.Equal(DocumentExtractionOutcomes.UnsupportedType, result.Outcome);
    }

    [Fact]
    public void TruncatedDocx_IsCorrupt()
    {
        var whole = Docx();

        var result = DocumentExtraction.Extract(whole.Take(whole.Length / 3).ToArray());

        Assert.Equal(DocumentExtractionOutcomes.Corrupt, result.Outcome);
    }

    [Fact]
    public void ArchiveDeclaringEnormousContents_IsRefusedBeforeItIsOpened()
    {
        // A zip's cost is not its size. This one is a few hundred bytes and
        // declares far more, which the byte cap cannot see.
        var result = DocumentExtraction.Extract(ZipBomb());

        Assert.Equal(DocumentExtractionOutcomes.ExpandsTooLarge, result.Outcome);
    }

    // ---- the malformed corpus (#241, § 9) -------------------------------
    //
    // The requirement is not a particular outcome, it is that hostile input
    // produces a NAMED outcome rather than an exception, a hang, or a 500.
    // The specific values below were measured, not predicted.

    public static TheoryData<string, byte[], string> HostileDocuments() => new()
    {
        // A page tree that is its own child. PdfPig 0.1.15's MaxStackDepth
        // guard exists for this shape; without a bound it is an uncatchable
        // StackOverflowException, which takes the worker down.
        { "self-referential page tree", RecursivePdf(), DocumentExtractionOutcomes.Corrupt },

        // The page cap counts pages, not area, so nothing above catches this.
        { "absurd MediaBox", AbsurdMediaBoxPdf(), DocumentExtractionOutcomes.Corrupt },

        // Distinct from the two password cases: those are well-formed
        // encryption we decline to defeat, this is metadata that does not
        // parse.
        { "garbage /Encrypt dictionary", GarbageEncryptedPdf(), DocumentExtractionOutcomes.Corrupt },

        // unsupported-type rather than corrupt, and that is right: a plain
        // zip opens as a WordprocessingDocument with no main part, so nothing
        // is wrong with the file — it simply is not one we read. Measured.
        { "a zip that is not an OPC package", PlainZip(), DocumentExtractionOutcomes.UnsupportedType },

        // The SDK prohibits DTD processing, so the entity never resolves and
        // the part fails to parse. Corrupt is the honest answer; the point of
        // the test is that it is a name and not a filesystem read.
        { "a .docx declaring an external entity", XxeDocx(), DocumentExtractionOutcomes.Corrupt }
    };

    [Theory]
    [MemberData(nameof(HostileDocuments))]
    public void HostileInput_ProducesANamedOutcomeAndNoText(string description, byte[] bytes, string expected)
    {
        // § 9's requirement is not a particular outcome — it is a named
        // outcome rather than an exception, a hang, or a 500. So the general
        // property is asserted first and the measured value second; the
        // expectations above were observed, not predicted.
        //
        // No try/catch on purpose: an escaping exception fails this test,
        // which is half of what § 9 asks for.
        var result = DocumentExtraction.Extract(bytes);

        Assert.Contains(result.Outcome, KnownOutcomes);
        Assert.NotEqual(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Null(result.Text);

        // Every refusal a person can reach has to name its cause; the generic
        // fallback would mean this outcome was never given copy.
        Assert.NotEqual("This file could not be read.", DocumentExtractionCopy.For(result.Outcome));

        Assert.Equal(expected, result.Outcome);
    }

    [Fact]
    public void TextRenamedAsAPdf_IsStillReadAsText()
    {
        // § 9 lists the renamed file as a corpus case. There is nothing to
        // rename here, which is the answer: the parser sees bytes, no
        // filename reaches it, and a declared content type is a hint the
        // sniffer is free to ignore. A .txt called .pdf is simply text.
        var result = DocumentExtraction.Extract(Encoding.UTF8.GetBytes(Referral));

        Assert.Equal(DocumentExtractionOutcomes.Extracted, result.Outcome);
        Assert.Equal(TextDocumentDecoder.ExtractorId, result.ExtractorId);
    }

    // ---- copy ----------------------------------------------------------

    [Fact]
    public void EveryOutcomeHasCopyThatNamesTheCause()
    {
        // The refusal text is the whole of what a person is told, on both
        // doors (#236, #237), so a missing case is a user-facing hole.
        string[] outcomes =
        [
            DocumentExtractionOutcomes.UnsupportedType,
            DocumentExtractionOutcomes.Corrupt,
            DocumentExtractionOutcomes.PasswordProtected,
            DocumentExtractionOutcomes.NoTextLayer,
            DocumentExtractionOutcomes.Empty,
            DocumentExtractionOutcomes.TooLarge,
            DocumentExtractionOutcomes.TooManyPages,
            DocumentExtractionOutcomes.TooMuchText,
            DocumentExtractionOutcomes.TimedOut,
            DocumentExtractionOutcomes.Busy
        ];

        foreach (var outcome in outcomes)
        {
            var copy = DocumentExtractionCopy.For(outcome);

            Assert.NotEqual("This file could not be read.", copy);
            Assert.EndsWith(".", copy);
        }
    }

    // ---- extractor identity (#253) --------------------------------------

    // The two strings the packages in use actually publish, verbatim. PdfPig
    // is version-plus-one-commit; OpenXml is GitVersion's full spread, which
    // is what put a branch name and two copies of a SHA into the provenance
    // panel.
    private const string OpenXmlInformationalVersion =
        "3.5.1+Branch.main.Sha.3139fdfd27414548a41555f7848d5728f6e71a42.3139fdfd27414548a41555f7848d5728f6e71a42";

    private const string PdfPigInformationalVersion =
        "0.1.15+f131f642976936e06ee91cb19d3ed728f9dd18b6";

    [Fact]
    public void BuildMetadata_IsReducedToOneShortCommit()
    {
        Assert.Equal(
            "openxml/3.5.1+3139fdfd",
            ExtractorIdentity.Format("openxml", OpenXmlInformationalVersion, null));

        Assert.Equal(
            "pdfpig/0.1.15+f131f642",
            ExtractorIdentity.Format("pdfpig", PdfPigInformationalVersion, null));
    }

    [Fact]
    public void AVersionWithoutBuildMetadata_PassesThrough()
    {
        Assert.Equal("openxml/3.5.1", ExtractorIdentity.Format("openxml", "3.5.1", null));
    }

    [Fact]
    public void MetadataWithNoRecoverableCommit_KeepsTheVersionAlone()
    {
        // Padding the id with whatever happened to be there would be the
        // noise this fixed. An absent commit is worth saying plainly.
        Assert.Equal("openxml/3.5.1", ExtractorIdentity.Format("openxml", "3.5.1+Branch.main", null));
    }

    [Fact]
    public void AShortHexToken_IsNotMistakenForACommit()
    {
        // The length floor is the rule, not a detail: build metadata is
        // mostly not commits, and a branch named "deadbee" is hex.
        Assert.Equal("openxml/3.5.1", ExtractorIdentity.Format("openxml", "3.5.1+Branch.deadbee", null));
    }

    [Fact]
    public void WithNoInformationalVersion_TheAssemblyVersionIsUsed()
    {
        Assert.Equal("openxml/3.5.1.0", ExtractorIdentity.Format("openxml", null, new Version(3, 5, 1, 0)));
    }

    [Fact]
    public void BothLiveExtractors_NameThemselvesReadably()
    {
        // Against the real assemblies rather than literals, so a package
        // bump that changes the metadata shape fails here — which is
        // exactly how this defect arrived.
        Assert.Matches(@"^pdfpig/[0-9.]+\+[0-9a-f]{8}$", PdfDocumentExtractor.ExtractorId);
        Assert.Matches(@"^openxml/[0-9.]+\+[0-9a-f]{8}$", DocxDocumentExtractor.ExtractorId);
    }

    // ---- fixtures ------------------------------------------------------

    private static byte[] TextPdf(string text, string? ownerPassword = null)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawString(text, new XFont("Liberation Sans", 11), XBrushes.Black, new XPoint(50, 80));
        }

        if (ownerPassword != null)
        {
            // Owner password only: the user password stays empty, which is
            // what makes this the permissions-only case.
            document.SecuritySettings.OwnerPassword = ownerPassword;
            document.SecurityHandler.SetEncryptionToV5();
        }

        return Save(document);
    }

    /// <summary>
    /// A page bearing an image and no glyphs — what a scanned referral or a
    /// fax looks like. The image is a 1x1 JPEG inline rather than a checked-in
    /// file, keeping to this suite's no-committed-binaries convention.
    /// </summary>
    private static byte[] ImageOnlyPdf()
    {
        const string OnePixelJpeg =
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a"
            + "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAA"
            + "AAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==";

        var document = new PdfDocument();
        var page = document.AddPage();

        using (var gfx = XGraphics.FromPdfPage(page))
        using (var image = XImage.FromStream(new MemoryStream(Convert.FromBase64String(OnePixelJpeg))))
        {
            gfx.DrawImage(image, 50, 50, 400, 500);
        }

        return Save(document);
    }

    private static byte[] BlankPdf(int pages)
    {
        var document = new PdfDocument();

        for (var i = 0; i < pages; i++)
        {
            document.AddPage();
        }

        return Save(document);
    }

    /// <summary>
    /// A referral-shaped Word document carrying every case the walk has to get
    /// right: two paragraphs, a tracked dose change, a medication table,
    /// hidden text, and a letterhead in a header part.
    /// </summary>
    private static byte[] Docx()
    {
        using var buffer = new MemoryStream();

        using (var document = WordprocessingDocument.Create(buffer, WordprocessingDocumentType.Document, true))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;

            body.Append(new Paragraph(new Run(new Text("First paragraph."))));
            body.Append(new Paragraph(new Run(new Text("Second paragraph."))));

            var dose = new Paragraph();
            dose.Append(new Run(new Text("Dose is ")));
            dose.Append(new InsertedRun(new Run(new Text("10 mg"))) { Author = "clinician", Id = "1" });
            dose.Append(new DeletedRun(new Run(new DeletedText("5 mg"))) { Author = "clinician", Id = "2" });
            dose.Append(new Run(new Text(" daily.")));
            body.Append(dose);

            var table = new Table();

            foreach (var (drug, dosage) in new[] { ("Amlodipine", "5 mg"), ("Ramipril", "10 mg") })
            {
                table.Append(new TableRow(
                    new TableCell(new Paragraph(new Run(new Text(drug)))),
                    new TableCell(new Paragraph(new Run(new Text(dosage))))));
            }

            body.Append(table);
            body.Append(new Paragraph(new Run(new RunProperties(new Vanish()), new Text("HIDDEN-MARKER"))));

            var header = main.AddNewPart<HeaderPart>();
            header.Header = new Header(new Paragraph(new Run(new Text("Meadowbrook Oncology"))));
        }

        return buffer.ToArray();
    }

    private static byte[] Xlsx()
    {
        using var buffer = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook, true))
        {
            document.AddWorkbookPart().Workbook = new DocumentFormat.OpenXml.Spreadsheet.Workbook(
                new DocumentFormat.OpenXml.Spreadsheet.Sheets());
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Highly compressible content whose declared uncompressed size dwarfs the
    /// archive — the shape a byte cap cannot catch.
    /// </summary>
    private static byte[] ZipBomb()
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < 4; i++)
            {
                using var entry = new StreamWriter(archive.CreateEntry($"part{i}.xml").Open());
                entry.Write(new string('a', 40 * 1024 * 1024));
            }
        }

        return buffer.ToArray();
    }

    private static readonly string[] KnownOutcomes =
    [
        DocumentExtractionOutcomes.UnsupportedType,
        DocumentExtractionOutcomes.Corrupt,
        DocumentExtractionOutcomes.PasswordProtected,
        DocumentExtractionOutcomes.NoTextLayer,
        DocumentExtractionOutcomes.Empty,
        DocumentExtractionOutcomes.TooLarge,
        DocumentExtractionOutcomes.TooManyPages,
        DocumentExtractionOutcomes.ExpandsTooLarge,
        DocumentExtractionOutcomes.TooMuchText,
        DocumentExtractionOutcomes.TimedOut,
        DocumentExtractionOutcomes.Busy
    ];

    /// <summary>
    /// A page tree whose only node lists itself as its own child. Written as
    /// raw bytes rather than built with PDFsharp, which will not emit a
    /// structure this broken — which is the point: no real library produces
    /// hostile input, so hostile fixtures have to be authored.
    ///
    /// No xref table. Lenient parsing is on, so PdfPig brute-force rescans a
    /// file whose cross-reference table is missing or damaged, which is the
    /// path a crafted file would take anyway.
    /// </summary>
    private static byte[] RecursivePdf() => Encoding.ASCII.GetBytes(
        "%PDF-1.4\n"
        + "1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n"
        + "2 0 obj <</Type/Pages/Kids[2 0 R]/Count 1>> endobj\n"
        + "trailer <</Root 1 0 R>>\n"
        + "%%EOF\n");

    /// <summary>
    /// A page declaring a media box roughly 10^9 units on each side. The page
    /// cap counts pages, not area, so nothing above this catches it.
    /// </summary>
    private static byte[] AbsurdMediaBoxPdf() => Encoding.ASCII.GetBytes(
        "%PDF-1.4\n"
        + "1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n"
        + "2 0 obj <</Type/Pages/Kids[3 0 R]/Count 1>> endobj\n"
        + "3 0 obj <</Type/Page/Parent 2 0 R/MediaBox[0 0 1000000000 1000000000]>> endobj\n"
        + "trailer <</Root 1 0 R>>\n"
        + "%%EOF\n");

    /// <summary>
    /// An /Encrypt entry pointing at an object that is not an encryption
    /// dictionary. Distinct from the two password cases above: those are
    /// well-formed encryption we decline to defeat, this is encryption
    /// metadata that does not parse.
    /// </summary>
    private static byte[] GarbageEncryptedPdf() => Encoding.ASCII.GetBytes(
        "%PDF-1.4\n"
        + "1 0 obj <</Type/Catalog/Pages 2 0 R>> endobj\n"
        + "2 0 obj <</Type/Pages/Kids[3 0 R]/Count 1>> endobj\n"
        + "3 0 obj <</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>> endobj\n"
        + "4 0 obj <</Filter/NotAFilter/V 99/R 99/O(x)/U(x)/P -1>> endobj\n"
        + "trailer <</Root 1 0 R/Encrypt 4 0 R/ID[<00><00>]>>\n"
        + "%%EOF\n");

    /// <summary>
    /// A zip carrying one text file and no OPC structure at all. Distinct
    /// from the .xlsx case, which IS a valid package — this one matches the
    /// PK signature and is nothing further.
    /// </summary>
    private static byte[] PlainZip()
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        using (var entry = new StreamWriter(archive.CreateEntry("notes.txt").Open()))
        {
            entry.Write("Not a package.");
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// A real .docx whose word/document.xml is replaced with a DOCTYPE
    /// declaring an external entity that reads a local file. Built by
    /// rewriting a valid package rather than authoring one, so the OPC
    /// structure is genuine and the XML is the only hostile part.
    ///
    /// The SDK prohibits DTD processing, so this never resolves — the test
    /// is that it fails by name rather than by reaching the filesystem.
    /// </summary>
    private static byte[] XxeDocx()
    {
        const string Payload =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE w:document [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body><w:p><w:r><w:t>&xxe;</w:t></w:r></w:p></w:body>
            </w:document>
            """;

        using var buffer = new MemoryStream(Docx());
        buffer.Position = 0;

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            var part = archive.GetEntry("word/document.xml")!;
            part.Delete();

            using var rewritten = new StreamWriter(archive.CreateEntry("word/document.xml").Open());
            rewritten.Write(Payload);
        }

        return buffer.ToArray();
    }

    private static byte[] Save(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private sealed class EmbeddedLiberationSans : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new FontResolverInfo("LiberationSans-Regular");

        public byte[]? GetFont(string faceName)
        {
            using var stream = typeof(ConsultDocumentPdf).Assembly
                .GetManifestResourceStream($"Consultologist.Api.Fonts.{faceName}.ttf");

            if (stream == null)
            {
                return null;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }
}
