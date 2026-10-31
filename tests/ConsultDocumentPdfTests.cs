using Consultologist.Api.Email;
using PdfSharp.Pdf.IO;

namespace Consultologist.Api.Tests;

public class ConsultDocumentPdfTests
{
    private const string Document = "## History of Present Illness\n\nThe patient presents with **worsening** dyspnea.\nSecond line kept hard-broken.\n\n## Plan\n\n- Echo\n- Repeat labs";
    private const string Password = "correct-horse-battery-16";

    [Fact]
    public void Render_ProducesAPdf()
    {
        var bytes = ConsultDocumentPdf.Render(Document, Password);

        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Render_OpensWithTheCorrectPassword()
    {
        var bytes = ConsultDocumentPdf.Render(Document, Password);

        using var stream = new MemoryStream(bytes);
        using var reopened = PdfReader.Open(stream, Password, PdfDocumentOpenMode.ReadOnly);

        Assert.True(reopened.PageCount >= 1);
    }

    [Fact]
    public void Render_RejectsAWrongPassword()
    {
        var bytes = ConsultDocumentPdf.Render(Document, Password);

        using var stream = new MemoryStream(bytes);
        Assert.ThrowsAny<Exception>(() =>
            PdfReader.Open(stream, "wrong-password-16char", PdfDocumentOpenMode.ReadOnly));
    }

    [Fact]
    public void Render_RejectsOpeningWithoutAPassword()
    {
        var bytes = ConsultDocumentPdf.Render(Document, Password);

        using var stream = new MemoryStream(bytes);
        Assert.ThrowsAny<Exception>(() =>
            PdfReader.Open(stream, PdfDocumentOpenMode.ReadOnly));
    }

    [Fact]
    public void Render_HandlesPlainProseWithoutMarkdown()
    {
        var bytes = ConsultDocumentPdf.Render("Just a plain paragraph of prose.", Password);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    // #252 — a consult copied out of Outlook as `hormone␂blocking`. The cause
    // is not the encoding: PDFsharp already emits a Type0/Identity-H font with
    // a ToUnicode map for anything outside Windows-1252. It is that Liberation
    // Sans has no glyph for U+2011, so the map faithfully records .notdef and
    // readers copy a control character.

    private static string ExtractText(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(
            new MemoryStream(pdf),
            new UglyToad.PdfPig.ParsingOptions { Password = Password });

        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    [Fact]
    public void Render_ANonBreakingHyphenSurvivesAsAHyphen()
    {
        // The reported failure, reproduced and fixed. Before the fold this
        // came back as U+0000.
        var bytes = ConsultDocumentPdf.Render("Continue hormone‑blocking treatment.", Password);

        var text = ExtractText(bytes);

        Assert.Contains("hormone‐blocking", text);
        // The exact regression: .notdef copied out as a NUL.
        Assert.False(text.Contains('\0'), "the rendered text still carries a NUL");
    }

    [Fact]
    public void Render_ProducesNoControlCharactersAtAll()
    {
        // The general form of the defect: whatever we cannot draw, a reader
        // must never be handed a control character in a clinical document.
        //
        // #287: this stated the principle without exercising it — every
        // character below was drawable or foldable, so it never reached the
        // branch that produced U+0000. The 一 does.
        var bytes = ConsultDocumentPdf.Render(
            "Dose ‑ unchanged. Range ≤ 5 mg. Patient’s — note – here 一. μg.",
            Password);

        var text = ExtractText(bytes);

        Assert.DoesNotContain(text, c => char.IsControl(c) && c != '\n' && c != '\r');
    }

    [Fact]
    public void Render_HandsBackAWhiteSquareRatherThanAHole()
    {
        // The reported defect, end to end. U+4E00 has no glyph and no
        // same-mark stand-in, so it used to reach the reader as .notdef —
        // glyph 0, whose ToUnicode entry honestly records that it means
        // nothing, i.e. U+0000 in the copy buffer.
        var bytes = ConsultDocumentPdf.Render("Note 一 here.", Password);

        var text = ExtractText(bytes);

        Assert.Contains("□", text);
        Assert.DoesNotContain("一", text);
        // The word after it survives. Outlook on the web dropped the character
        // following a missing glyph, turning "here" into "ere" in a note
        // pasted into a chart; that half is the reader's behaviour and stops
        // mattering once a real glyph is emitted.
        Assert.Contains("here", text);
    }

    [Fact]
    public void Render_LeavesCharactersTheFontCanDrawExactlyAsWritten()
    {
        // The control for the test above: these are all in Liberation Sans,
        // so nothing may be folded. A fix that normalised punctuation
        // wholesale would pass the previous test and fail this one.
        var bytes = ConsultDocumentPdf.Render("Patient’s dose — unchanged – today. μg ≤ 5.", Password);

        var text = ExtractText(bytes);

        Assert.Contains("’", text);
        Assert.Contains("—", text);
        Assert.Contains("–", text);
        Assert.Contains("μ", text);
        Assert.Contains("≤", text);
    }

    [Fact]
    public void Render_WarnsWithCodepointsWhenSomethingCannotBeDrawn()
    {
        // U+4E00 has no glyph and no safe stand-in, so it stays — and is
        // reported, because the point of this issue is that it used to not be.
        var logger = new CapturingLogger<object>();

        ConsultDocumentPdf.Render("Note 一 here.", Password, logger);

        // Recorded holds the rendered message and its structured values, so
        // assert over the whole capture rather than expecting one entry.
        Assert.Contains("cannot draw", logger.Everything);
        Assert.Contains("U+4E00", logger.Everything);
        // The codepoint, never the prose around it.
        Assert.DoesNotContain("Note", logger.Everything);
    }

    [Fact]
    public void Render_DoesNotWarnForOrdinaryProse()
    {
        var logger = new CapturingLogger<object>();

        ConsultDocumentPdf.Render(Document, Password, logger);

        Assert.DoesNotContain(logger.Recorded, m => m.Contains("cannot draw"));
    }

    // #302: a silent fold left production reporting zero undrawable characters
    // whether the guard was working or entirely inert. These pin what each
    // render now says about itself.

    [Fact]
    public void Render_ReportsAFoldSoTheNearMissIsNotSilent()
    {
        // U+2011 is genuinely absent from Liberation Sans, so this exercises
        // the real coverage table rather than a stated one.
        var logger = new CapturingLogger<object>();

        ConsultDocumentPdf.Render("Continue hormone‑blocking treatment.", Password, logger);

        Assert.Contains("folded", logger.Everything);
        Assert.Contains("U+2011", logger.Everything);
    }

    [Fact]
    public void Render_NamesTheDeliveryAFoldBelongsTo()
    {
        // Frequency alone cannot answer "was anything substituted in *this*
        // document", which is the question a delivered PDF actually prompts.
        var logger = new CapturingLogger<object>();

        ConsultDocumentPdf.Render(
            "Continue hormone‑blocking treatment.",
            Password,
            logger,
            new PdfRenderContext("job-1234", "consultation-note"));

        Assert.Contains("job-1234", logger.Everything);
        Assert.Contains("consultation-note", logger.Everything);
    }

    [Fact]
    public void Render_TellsTheLoggerNothingOfWhatTheDocumentSaid()
    {
        // The § 9 audit rule, over both new lines at once: codepoints and ids
        // may travel, prose may not. Structured values are captured too,
        // because a safe-reading template still ships whatever it was handed.
        var logger = new CapturingLogger<object>();

        ConsultDocumentPdf.Render(
            "Continue hormone‑blocking treatment for Mrs Abernathy. Note 一 here.",
            Password,
            logger,
            new PdfRenderContext("job-1234", "consultation-note"));

        Assert.DoesNotContain("Abernathy", logger.Everything);
        Assert.DoesNotContain("hormone", logger.Everything);
        Assert.DoesNotContain("treatment", logger.Everything);
        // And the reports it does make are still there to be undermined.
        Assert.Contains("U+2011", logger.Everything);
        Assert.Contains("U+4E00", logger.Everything);
    }

    [Fact]
    public void Render_SaysNothingAboutCoverageWhenTheFontParses()
    {
        // The control for the unknown-coverage line: it must not fire on the
        // embedded font, or it would be noise on every delivery.
        var logger = new CapturingLogger<object>();

        ConsultDocumentPdf.Render(Document, Password, logger);

        Assert.DoesNotContain("without a glyph-coverage check", logger.Everything);
        Assert.DoesNotContain("could not be read", logger.Everything);
    }

    [Fact]
    public void Render_CarriesAGenericTitleAndTheDocumentLanguage()
    {
        var bytes = ConsultDocumentPdf.Render(Document, Password);

        using var stream = new MemoryStream(bytes);
        using var reopened = PdfReader.Open(stream, Password, PdfDocumentOpenMode.ReadOnly);

        // Generic on purpose: the title shows in a mail client's preview.
        Assert.Equal("Consult", reopened.Info.Title);
        Assert.Equal("en-CA", reopened.Internals.Catalog.Elements.GetString("/Lang"));
    }

    // ---- #1256: a table a prose node drafted ------------------------------------------

    private const string Medications = "## Current medications\n\n| Name | Dose | Frequency | Indication |\n|---|---:|---|---|\n| Ramipril | 10 mg | Once daily | |\n| Metformin | 1 g | Twice daily | |\n| Atorvastatin | 20 mg | At night | |\n| Amlodipine | 5 mg | Once daily | |\n| Furosemide | 40 mg | Each morning | Probable heart failure |";

    private static IReadOnlyList<string> PageTexts(byte[] pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(
            new MemoryStream(pdf),
            new UglyToad.PdfPig.ParsingOptions { Password = Password });

        // page.Text runs the glyphs together; the words, space-joined, read as the page does.
        return document.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))).ToList();
    }

    [Fact]
    public void Render_ATable_LaysOutItsCells_NoneAsAPipe()
    {
        // The first 27 smoke's medications section printed as a paragraph of pipes:
        // the PDF's own pipeline lacked pipe tables and its walker had no Table case.
        var bytes = ConsultDocumentPdf.Render(Medications, Password);

        var pages = PageTexts(bytes);
        var text = string.Join("\n", pages);
        Assert.Single(pages);
        foreach (var expected in new[] { "Current medications", "Name", "Dose", "Frequency", "Indication", "Ramipril", "10 mg", "Metformin", "Twice daily", "Furosemide", "Each morning", "Probable heart failure" })
        {
            Assert.Contains(expected, text);
        }

        Assert.DoesNotContain("|", text);
        Assert.DoesNotContain("---", text);
    }

    [Fact]
    public void Render_ATablesHeaderRow_RepeatsAcrossAPageBreak()
    {
        var rows = string.Join("\n", Enumerable.Range(1, 70).Select(i => $"| Medication {i} | {i} mg | Once daily | Indication {i} |"));
        var long_ = "## Current medications\n\n| Name | Dose | Frequency | Indication |\n|---|---:|---|---|\n" + rows;

        var pages = PageTexts(ConsultDocumentPdf.Render(long_, Password));

        Assert.True(pages.Count >= 2, $"expected a page break, got {pages.Count} page(s)");
        Assert.All(pages, page => Assert.Contains("Frequency", page));
        Assert.Contains("Medication 70", pages[^1]);
        Assert.DoesNotContain("|", string.Join("\n", pages));
    }

    [Fact]
    public void Render_APipeLineWithoutASeparatorRow_IsNotATable_AndItsTextSurvives()
    {
        var text = string.Join("\n", PageTexts(ConsultDocumentPdf.Render("## Plan\n\n| not | a | table |\n\nFollow up in three weeks.", Password)));

        Assert.Contains("not", text);
        Assert.Contains("table", text);
        Assert.Contains("Follow up in three weeks.", text);
    }
}
