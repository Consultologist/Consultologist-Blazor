using System.Reflection;
using Consultologist.PackageFormat;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Security;
using Microsoft.Extensions.Logging;
using MdTable = Markdig.Extensions.Tables.Table;

namespace Consultologist.Api.Email;

/// <summary>
/// Which delivery a render belongs to (#302). Both values are already judged
/// safe to log on this path — the attachment filename is built from the same
/// two — so the diagnostics can answer "was anything substituted in *this*
/// document" rather than only "how often does this happen".
/// </summary>
internal sealed record PdfRenderContext(string JobId, string ResultId);

/// <summary>
/// #159: renders the assembled consult document (the normative v6 markdown —
/// "## " section headings, blank-line separators, free-form agent prose
/// inside) to a password-protected PDF (AES-256, PDF 2.0 encryption V5).
/// The markdown pipeline is the one the client's MarkdownPreview renders from
/// (<see cref="ConsultMarkdown.Pipeline"/>, #1256: HTML disabled, pipe tables,
/// soft line breaks hard) — so the PDF reads like the in-app preview, a table
/// laid out as a table. The password protects the document in the mailbox; it
/// is not end-to-end secrecy from the server, which produced the plaintext
/// (docs/ASYNC_DELIVERY.md §3).
/// </summary>
internal static class ConsultDocumentPdf
{
    private static readonly MarkdownPipeline Pipeline = ConsultMarkdown.Pipeline;

    // A4 less the 2.2 cm side margins set in Render — what a table may span.
    private const double UsableWidthCm = 21.0 - 2 * 2.2;
    private const double MinColumnWidthCm = 2.0;

    private static readonly object FontResolverLock = new();
    private static FontGlyphCoverage? _coverage;

    internal static byte[] Render(
        string markdown,
        string password,
        ILogger? logger = null,
        PdfRenderContext? context = null)
    {
        EnsureFontResolver();

        // #252: what the embedded font can actually draw. Characters it
        // cannot are folded onto identical marks it can, and whatever is left
        // is counted so it stops being silent.
        var coverage = EnsureCoverage(logger);
        var prepared = RenderableText.Prepare(markdown, coverage);

        var jobId = context?.JobId;
        var resultId = context?.ResultId;

        // #302: with coverage unknown the fold is inert — nothing substituted,
        // nothing counted — and the run looks identical to one with nothing to
        // report. EnsureCoverage says so once per worker; this says it for the
        // specific document, which is what an investigation actually holds.
        // Rare by construction, so it costs nothing when the guard is working.
        if (coverage.IsUnknown)
        {
            logger?.LogWarning(
                "Consult PDF rendered without a glyph-coverage check; nothing was folded or counted. Status={Status}, JobId={JobId}, ResultId={ResultId}",
                coverage.Status,
                jobId,
                resultId);
        }

        if (prepared.Folded.Count > 0)
        {
            // Information, not Warning: a fold is the mechanism working. It is
            // recorded because the frequency is the only evidence for how
            // often a character arrives that the font cannot draw (#287).
            logger?.LogInformation(
                "Consult PDF folded characters the embedded font cannot draw onto identical marks it can. Codepoints={Codepoints}, JobId={JobId}, ResultId={ResultId}",
                RenderableText.Describe(prepared.Folded),
                jobId,
                resultId);
        }

        if (prepared.Unrenderable.Count > 0)
        {
            logger?.LogWarning(
                "Consult PDF contains characters the embedded font cannot draw; they will render as missing glyphs. Codepoints={Codepoints}, JobId={JobId}, ResultId={ResultId}",
                RenderableText.Describe(prepared.Unrenderable),
                jobId,
                resultId);
        }

        markdown = prepared.Text;

        var document = new Document();
        var section = document.Sections.AddSection();
        section.PageSetup.TopMargin = Unit.FromCentimeter(2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2.2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2.2);

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Liberation Sans";
        normal.Font.Size = 10.5;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(6);

        foreach (var block in Markdig.Markdown.Parse(markdown, Pipeline))
        {
            AppendBlock(section, block, indentLevel: 0);
        }

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        var pdf = renderer.PdfDocument;

        // A generic title: it names the document in a mail client's preview
        // and in a reader's title bar, and must therefore carry nothing about
        // the patient (docs/ASYNC_DELIVERY.md § 3).
        pdf.Info.Title = "Consult";

        // #252: the document language, for screen readers and for any reader
        // deciding how to hyphenate or pronounce it. PDFsharp exposes no
        // typed property, so it goes on the catalog directly.
        pdf.Internals.Catalog.Elements.SetString("/Lang", "en-CA");

        var securitySettings = pdf.SecuritySettings;
        securitySettings.UserPassword = password;
        securitySettings.OwnerPassword = password;
        pdf.SecurityHandler.SetEncryptionToV5();

        using var stream = new MemoryStream();
        pdf.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static void AppendBlock(Section section, Block block, int indentLevel)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var headingParagraph = section.AddParagraph();
                headingParagraph.Format.Font.Bold = true;
                headingParagraph.Format.Font.Size = heading.Level switch
                {
                    1 => 15,
                    2 => 13,
                    _ => 11.5
                };
                headingParagraph.Format.SpaceBefore = Unit.FromPoint(10);
                headingParagraph.Format.SpaceAfter = Unit.FromPoint(4);
                AppendInlines(headingParagraph, heading.Inline);
                break;

            case ParagraphBlock paragraph:
                var bodyParagraph = section.AddParagraph();
                if (indentLevel > 0)
                {
                    bodyParagraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * indentLevel);
                }

                AppendInlines(bodyParagraph, paragraph.Inline);
                break;

            case ListBlock list:
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var first = true;
                    foreach (var child in item)
                    {
                        if (first && child is ParagraphBlock itemParagraph)
                        {
                            var listParagraph = section.AddParagraph();
                            listParagraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * (indentLevel + 1));
                            listParagraph.Format.SpaceAfter = Unit.FromPoint(2);
                            listParagraph.AddText(list.IsOrdered ? $"{item.Order}. " : "• ");
                            AppendInlines(listParagraph, itemParagraph.Inline);
                            first = false;
                        }
                        else
                        {
                            AppendBlock(section, child, indentLevel + 1);
                        }
                    }
                }

                break;

            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    AppendBlock(section, child, indentLevel + 1);
                }

                break;

            case CodeBlock code:
                var codeParagraph = section.AddParagraph();
                codeParagraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * (indentLevel + 1));
                codeParagraph.AddText(code.Lines.ToString());
                break;

            case ThematicBreakBlock:
                var ruleParagraph = section.AddParagraph();
                ruleParagraph.Format.SpaceBefore = Unit.FromPoint(6);
                ruleParagraph.Format.SpaceAfter = Unit.FromPoint(6);
                ruleParagraph.AddText(new string('—', 12));
                break;

            // #1256: a pipe table a prose node drafted — laid out as a table, the
            // way the in-app preview shows it. MigraDoc needs explicit column
            // widths: the usable width is shared in proportion to each column's
            // longest cell (the dose column stays narrow, the indication column
            // takes the rest), none below a floor; the header row repeats across
            // a page break; a row never splits. Cell text was folded with the
            // rest of the markdown before parsing (#252), so nothing more here.
            case MdTable table:
                if (!table.IsValid())
                {
                    // The source's pipes, kept rather than dropped.
                    AppendTableAsText(section, table, indentLevel);
                    break;
                }

                AppendTable(section, table, indentLevel);
                break;

            default:
                // Conservative fallback for a block the walker does not know. Markdig's
                // LinkReferenceDefinitionGroup lands here and must stay silent (its text
                // is a definition, not prose); anything else is reported by the tests.
                var span = block.Span;
                if (span.Length > 0)
                {
                    section.AddParagraph();
                }

                break;
        }
    }

    private static void AppendTable(Section section, MdTable table, int indentLevel)
    {
        var rows = table.OfType<TableRow>().ToList();
        var columnCount = rows.Max(row => row.Count);
        if (columnCount == 0)
        {
            return;
        }

        // Each column's weight: its longest cell's text, the header included; a
        // declared width (a percentage, rarely set) wins when present.
        var weights = new double[columnCount];
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count && i < columnCount; i++)
            {
                weights[i] = Math.Max(weights[i], Math.Max(1, CellTextLength(row[i])));
            }
        }

        var available = UsableWidthCm - 0.5 * indentLevel;
        var widths = new double[columnCount];
        for (var i = 0; i < columnCount; i++)
        {
            var declared = i < table.ColumnDefinitions.Count ? table.ColumnDefinitions[i].Width : 0;
            widths[i] = declared > 0 ? available * declared / 100.0 : 0;
        }

        var undeclared = Enumerable.Range(0, columnCount).Where(i => widths[i] <= 0).ToList();
        if (undeclared.Count > 0)
        {
            var remaining = Math.Max(available - widths.Sum(), MinColumnWidthCm * undeclared.Count);
            var totalWeight = undeclared.Sum(i => weights[i]);
            foreach (var i in undeclared)
            {
                widths[i] = Math.Max(MinColumnWidthCm, remaining * weights[i] / totalWeight);
            }

            // The floors may overshoot the page; scale the undeclared columns back to it.
            var overshoot = widths.Sum() - available;
            if (overshoot > 0)
            {
                var scalable = undeclared.Sum(i => widths[i]);
                foreach (var i in undeclared)
                {
                    widths[i] -= overshoot * widths[i] / scalable;
                }
            }
        }

        var migra = section.AddTable();
        migra.Borders.Width = 0;
        migra.Format.SpaceAfter = Unit.FromPoint(6);
        migra.TopPadding = Unit.FromPoint(2);
        migra.BottomPadding = Unit.FromPoint(2);
        if (indentLevel > 0)
        {
            migra.Rows.LeftIndent = Unit.FromCentimeter(0.5 * indentLevel);
        }

        for (var i = 0; i < columnCount; i++)
        {
            var column = migra.AddColumn(Unit.FromCentimeter(widths[i]));
            var alignment = i < table.ColumnDefinitions.Count ? table.ColumnDefinitions[i].Alignment : null;
            column.Format.Alignment = alignment switch
            {
                TableColumnAlign.Right => ParagraphAlignment.Right,
                TableColumnAlign.Center => ParagraphAlignment.Center,
                _ => ParagraphAlignment.Left
            };
        }

        foreach (var row in rows)
        {
            var migraRow = migra.AddRow();
            migraRow.Borders.Bottom.Width = row.IsHeader ? 0.5 : 0.25;
            if (row.IsHeader)
            {
                migraRow.HeadingFormat = true;
                migraRow.Format.Font.Bold = true;
                migraRow.Format.Font.Size = 9.5;
            }

            for (var i = 0; i < columnCount; i++)
            {
                var cellParagraph = migraRow.Cells[i].AddParagraph();
                cellParagraph.Format.SpaceAfter = 0;
                if (i < row.Count && row[i] is TableCell cell)
                {
                    foreach (var child in cell)
                    {
                        if (child is ParagraphBlock cellBlock)
                        {
                            AppendInlines(cellParagraph, cellBlock.Inline);
                        }
                    }
                }
            }
        }
    }

    /// <summary>A malformed table's rows as text, pipes between the cells — kept, never dropped.</summary>
    private static void AppendTableAsText(Section section, MdTable table, int indentLevel)
    {
        foreach (var row in table.OfType<TableRow>())
        {
            var paragraph = section.AddParagraph();
            if (indentLevel > 0)
            {
                paragraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * indentLevel);
            }

            var first = true;
            foreach (var cell in row.OfType<TableCell>())
            {
                paragraph.AddText(first ? "| " : " | ");
                first = false;
                foreach (var child in cell)
                {
                    if (child is ParagraphBlock cellBlock)
                    {
                        AppendInlines(paragraph, cellBlock.Inline);
                    }
                }
            }

            paragraph.AddText(" |");
        }
    }

    private static int CellTextLength(Block cell) =>
        cell.Descendants<LiteralInline>().Sum(literal => literal.Content.Length)
        + cell.Descendants<CodeInline>().Sum(code => code.Content.Length);

    private static void AppendInlines(Paragraph paragraph, ContainerInline? inlines)
    {
        if (inlines == null)
        {
            return;
        }

        foreach (var inline in inlines)
        {
            AppendInline(paragraph, inline, bold: false, italic: false);
        }
    }

    private static void AppendInline(Paragraph paragraph, Inline inline, bool bold, bool italic)
    {
        switch (inline)
        {
            case LiteralInline literal:
                AddText(paragraph, literal.Content.ToString(), bold, italic);
                break;

            case EmphasisInline emphasis:
                var childBold = bold || emphasis.DelimiterCount >= 2;
                var childItalic = italic || emphasis.DelimiterCount == 1;
                foreach (var child in emphasis)
                {
                    AppendInline(paragraph, child, childBold, childItalic);
                }

                break;

            case LineBreakInline:
                paragraph.AddLineBreak();
                break;

            case CodeInline code:
                AddText(paragraph, code.Content, bold, italic);
                break;

            case LinkInline link:
                // Text only — the PDF is a clinical document, not hypertext.
                foreach (var child in link)
                {
                    AppendInline(paragraph, child, bold, italic);
                }

                break;

            case ContainerInline container:
                foreach (var child in container)
                {
                    AppendInline(paragraph, child, bold, italic);
                }

                break;

            default:
                var text = inline.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    AddText(paragraph, text, bold, italic);
                }

                break;
        }
    }

    private static void AddText(Paragraph paragraph, string text, bool bold, bool italic)
    {
        if (text.Length == 0)
        {
            return;
        }

        var formatted = paragraph.AddFormattedText(text);
        formatted.Bold = bold;
        formatted.Italic = italic;
    }

    private static void EnsureFontResolver()
    {
        lock (FontResolverLock)
        {
            GlobalFontSettings.FontResolver ??= new LiberationSansFontResolver();
        }
    }

    /// <summary>
    /// Read once from the same bytes the resolver embeds, so coverage can
    /// never disagree with the font actually in the file.
    ///
    /// #302: warns here, where the result is computed, so a failure is stated
    /// once with its cause rather than once per document. The cache means a
    /// logger that is absent on the first render loses this line for the
    /// worker's life — which is why <see cref="Render"/> also reports the
    /// state per document, and why the two lines are not redundant.
    /// </summary>
    private static FontGlyphCoverage EnsureCoverage(ILogger? logger)
    {
        lock (FontResolverLock)
        {
            if (_coverage != null)
            {
                return _coverage;
            }

            var bytes = new LiberationSansFontResolver().GetFont("LiberationSans-Regular");
            _coverage = bytes == null
                ? FontGlyphCoverage.Missing()
                : FontGlyphCoverage.Read(bytes);

            if (_coverage.IsUnknown)
            {
                logger?.LogWarning(
                    "Consult PDF glyph coverage could not be read; every character will be assumed drawable and nothing will be folded or counted. Status={Status}",
                    _coverage.Status);
            }

            return _coverage;
        }
    }

    /// <summary>
    /// PDFsharp Core resolves no system fonts on Linux; Liberation Sans (SIL
    /// OFL) is embedded in the assembly (Fonts/, LICENSE alongside).
    /// </summary>
    private sealed class LiberationSansFontResolver : IFontResolver
    {
        public string DefaultFontName => "Liberation Sans";

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            var face = (bold, italic) switch
            {
                (true, true) => "LiberationSans-BoldItalic",
                (true, false) => "LiberationSans-Bold",
                (false, true) => "LiberationSans-Italic",
                _ => "LiberationSans-Regular"
            };

            return new FontResolverInfo(face);
        }

        public byte[]? GetFont(string faceName)
        {
            var resourceName = $"Consultologist.Api.Fonts.{faceName}.ttf";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);

            if (stream == null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }
}
