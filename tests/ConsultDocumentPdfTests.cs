using Consultologist.Api.Email;
using Consultologist.PackageFormat;
using Markdig.Syntax;
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

    // ---- #1510 (the owner's third fix-first group, items 1 to 3): the 31 bundle ------------

    /// <summary>The note Task G3's Web tests render too: one fictional consult using every type the 31 walker draws.</summary>
    private static string EveryBlockNote() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Markdown31", "every-block.md"));

    private static string Text31(string markdown) => string.Join("\n", PageTexts(ConsultDocumentPdf.Render(markdown, 31, Password)));

    [Fact]
    public void TheEveryBlockNote_HoldsEveryTypeTheWalkerDraws()
    {
        var document = Markdig.Markdown.Parse(EveryBlockNote(), ConsultMarkdown.Pipeline31);

        // what the walker meets: every block and inline, but not the definitions a LinkReferenceDefinitionGroup holds,
        // which it passes over in silence
        var met = document.Descendants()
            .Where(node => node is not Block { Parent: LinkReferenceDefinitionGroup })
            .Select(node => node.GetType())
            .ToHashSet();

        Assert.Equal(
            ConsultDocumentPdf.Drawn31.Select(type => type.Name).Order(StringComparer.Ordinal),
            met.Select(type => type.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Render31_EveryBlockOfTheBundle_IsDrawn()
    {
        var text = Text31(EveryBlockNote());

        foreach (var expected in new[]
                 {
                     "Consult note for Margaret Okafor",
                     "Warning", "Seen at Sample Oncology Clinic by Dr A. Sample.",
                     "two-week history [results pending.",
                     "steady", "left", "HPI", "HPI reviewed with Margaret at the visit.",
                     "[x] Bloods taken", "[ ] Echo booked",
                     "a. Continue tamoxifen", "b. Review in clinic", "2. Letter to the GP",
                     "Echocardiogram", "An ultrasound of the heart.",
                     "Drug", "Dose", "Tamoxifen", "20 mg",
                     "A photograph of the forearm", "Figure 1: the rash at the visit",
                     "FBC, U&E, LFT", "indented code line",
                     "Footer by Dr A. Sample",
                     "1. Taken at the visit on the ward.",
                     "ECOG 1", "plan", "a scan of the forearm", "https://example.org/referral", "www.example.org", "A&E.",
                 })
        {
            Assert.Contains(expected, text);
        }

        // the syntax is read, not printed
        foreach (var syntax in new[] { "[!WARNING]", "\"\"", "^2^", "++", "[^1]", "*[HPI]", "^^", "```", "&amp;", "<https", "](" })
        {
            Assert.DoesNotContain(syntax, text);
        }

        // a link reference definition draws nothing
        Assert.DoesNotContain("example.org/clinic", text);
    }

    [Fact]
    public void Render31_ABlockTheWalkerDoesNotKnow_Throws()
    {
        var document = new Markdig.Syntax.MarkdownDocument { new Markdig.Extensions.Yaml.YamlFrontMatterBlock(null!) };
        var section = new MigraDoc.DocumentObjectModel.Document().AddSection();

        var thrown = Assert.Throws<NotSupportedException>(() => ConsultDocumentPdf.Draw(section, document, standard: true));

        Assert.Contains("YamlFrontMatterBlock", thrown.Message);
    }

    [Fact]
    public void Render31_ASingleLineEnding_IsASpace_AndABackslashIsABreak()
    {
        var bytes = ConsultDocumentPdf.Render("Margaret Okafor was seen\ntoday in clinic.\\\nReview in a week.", 31, Password);

        using var document = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(bytes), new UglyToad.PdfPig.ParsingOptions { Password = Password });
        var words = document.GetPage(1).GetWords().ToList();
        double Baseline(string word) => words.First(w => w.Text == word).BoundingBox.Bottom;

        Assert.Equal(Baseline("seen"), Baseline("today"), 0.5);
        Assert.True(Baseline("Review") < Baseline("today") - 5, "the backslash's line break starts a new line");
        Assert.DoesNotContain(words, word => word.Text.Contains('\\'));
    }

    [Theory]
    [InlineData("HPI here, taken in full.")]
    [InlineData("# HPI here")]
    [InlineData("- HPI here")]
    [InlineData("| Section |\n|---|\n| HPI here |")]
    public void Render31_AnAbbreviationThatOpensALine_IsDrawn(string body)
    {
        // Abbreviations wraps the literal it splits in a bare ContainerInline; the walker draws its children
        var text = Text31("*[HPI]: History of presenting illness\n\n" + body);

        Assert.Contains("HPI here", text);
        Assert.DoesNotContain("*[HPI]", text);
    }

    [Fact]
    public void Render31_AnEntity_IsItsCharacter()
    {
        var text = Text31("Margaret&#32;Okafor, A&amp;E");

        Assert.Contains("Margaret Okafor, A&E", text);
        Assert.DoesNotContain("&#32;", text);
    }

    [Fact]
    public void Render31_GenericAttributes_AreText()
    {
        var text = Text31("# Plan {#id .class key=value}\n\nSeen by Dr A. Sample {onclick=x}");

        Assert.Contains("Plan {#id .class key=value}", text);
        Assert.Contains("Seen by Dr A. Sample {onclick=x}", text);
    }

    [Fact]
    public void Render31_ExcludedSyntax_IsText()
    {
        var text = Text31("Margaret Okafor: ~~x~~, $x$ and ==x==.\n\n:::note\nSeen by Dr A. Sample.\n:::");

        Assert.Contains("~~x~~", text);
        Assert.Contains("$x$", text);
        Assert.Contains("==x==", text);
        Assert.Contains(":::note", text);
    }

    // #1543 (ruling 8, D16): both pipelines write a link of another scheme as its text; the walker draws a link's text
    // either way, and what the rewrite leaves in the tree is drawn at 31 (an unknown type throws there).

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void Render_ALinkWithAnotherScheme_DrawsItsTextOnly(int specVersion)
    {
        var text = string.Join("\n", PageTexts(ConsultDocumentPdf.Render(
            "See [the plan](javascript:alert(1)), [the **letter**][l] and ![the scan](data:text/html,x).\n\n[l]: vbscript:x",
            specVersion,
            Password)));

        Assert.Contains("See the plan, the letter and the scan.", text);
        Assert.DoesNotContain("javascript", text);
        Assert.DoesNotContain("vbscript", text);
        Assert.DoesNotContain("data:", text);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void Render_AnAutolinkWithAnotherScheme_DrawsItsUrlAsText(int specVersion)
    {
        var text = string.Join("\n", PageTexts(ConsultDocumentPdf.Render("Call <vbscript:x> now.", specVersion, Password)));

        Assert.Contains("Call vbscript:x now.", text);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void Render_TextAfterANestedRefusedLink_IsKept(int specVersion)
    {
        var text = string.Join("\n", PageTexts(ConsultDocumentPdf.Render("[alpha ![beta](data:y) gamma](javascript:x) delta", specVersion, Password)));

        Assert.Contains("alpha beta gamma delta", text);
        Assert.DoesNotContain("javascript", text);
        Assert.DoesNotContain("data:", text);
    }
}
