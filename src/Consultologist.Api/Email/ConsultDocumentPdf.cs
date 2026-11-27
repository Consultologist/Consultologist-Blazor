using System.Reflection;
using Consultologist.PackageFormat;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Figures;
using Markdig.Extensions.Footers;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Security;
using Microsoft.Extensions.Logging;
using MdTable = Markdig.Extensions.Tables.Table;
using MdFootnote = Markdig.Extensions.Footnotes.Footnote;

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
    /// <summary>
    /// #1510 (the owner's third fix-first group, item 3; G-D5): every block and inline type the 31 bundle
    /// (<see cref="ConsultMarkdown.Pipeline31"/>) produces, each drawn by its own case. At 31 the walker draws these and
    /// throws on anything else: there is no generic fallback for a 31 note. The parts a container's case lays out itself
    /// (a list's items, a table's rows and cells, a definition's term) are named too; a
    /// <see cref="LinkReferenceDefinitionGroup"/> is silent, so its definitions are never met. An unmatched <c>[</c> is
    /// the one <see cref="DelimiterInline"/> left after the parse. A bare <see cref="ContainerInline"/> is the wrapper
    /// Abbreviations writes round a literal it splits.
    /// </summary>
    internal static IReadOnlySet<Type> Drawn31 { get; } = new HashSet<Type>
    {
        typeof(HeadingBlock), typeof(ParagraphBlock), typeof(ListBlock), typeof(ListItemBlock), typeof(QuoteBlock),
        typeof(AlertBlock), typeof(CodeBlock), typeof(FencedCodeBlock), typeof(ThematicBreakBlock),
        typeof(MdTable), typeof(TableRow), typeof(TableCell),
        typeof(DefinitionList), typeof(DefinitionItem), typeof(DefinitionTerm), typeof(Figure), typeof(FigureCaption),
        typeof(FooterBlock), typeof(FootnoteGroup), typeof(MdFootnote), typeof(LinkReferenceDefinitionGroup),
        typeof(LiteralInline), typeof(EmphasisInline), typeof(LineBreakInline), typeof(CodeInline), typeof(LinkInline),
        typeof(AutolinkInline), typeof(HtmlEntityInline), typeof(TaskList), typeof(FootnoteLink), typeof(AbbreviationInline),
        typeof(LinkDelimiterInline), typeof(ContainerInline),
    };

    // A4 less the 2.2 cm side margins set in Render — what a table may span.
    private const double UsableWidthCm = 21.0 - 2 * 2.2;
    private const double MinColumnWidthCm = 2.0;

    private static readonly object FontResolverLock = new();
    private static FontGlyphCoverage? _coverage;

    /// <summary>The 5–30 rendering (<see cref="ConsultMarkdown.Pipeline"/>): the reply of a 5–30 job, whose payload carries no specVersion.</summary>
    internal static byte[] Render(
        string markdown,
        string password,
        ILogger? logger = null,
        PdfRenderContext? context = null) =>
        Render(markdown, specVersion: 30, password, logger, context);

    /// <summary>
    /// #1510 (G-D2): renders through the pipeline of the job's <paramref name="specVersion"/>, chosen by
    /// <see cref="ConsultMarkdown.For"/>, the selector the Web's preview shares; at 31 the walker draws by the standard
    /// (<see cref="Drawn31"/>).
    /// </summary>
    internal static byte[] Render(
        string markdown,
        int specVersion,
        string password,
        ILogger? logger = null,
        PdfRenderContext? context = null)
    {
        var pipeline = ConsultMarkdown.For(specVersion);

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

        Draw(section, Markdig.Markdown.Parse(markdown, pipeline), standard: ReferenceEquals(pipeline, ConsultMarkdown.Pipeline31));

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

    /// <summary>Draws a parsed note into <paramref name="section"/>; <paramref name="standard"/> is the 31 walker (G-D5).</summary>
    internal static void Draw(Section section, MarkdownDocument document, bool standard)
    {
        foreach (var block in document)
        {
            AppendBlock(section, block, indentLevel: 0, standard);
        }
    }

    private static void AppendBlock(Section section, Block block, int indentLevel, bool standard)
    {
        // At 31 a type is drawn only by its own case: a type the bundle does not produce (a YamlFrontMatterBlock is a
        // CodeBlock) must not be drawn by its base type's.
        if (standard && !Drawn31.Contains(block.GetType()))
        {
            throw NotDrawn(block);
        }

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
                AppendInlines(headingParagraph, heading.Inline, standard);
                break;

            case ParagraphBlock paragraph:
                var bodyParagraph = section.AddParagraph();
                if (indentLevel > 0)
                {
                    bodyParagraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * indentLevel);
                }

                AppendInlines(bodyParagraph, paragraph.Inline, standard);
                break;

            case ListBlock list:
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    if (standard && item.GetType() != typeof(ListItemBlock))
                    {
                        throw NotDrawn(item);
                    }

                    var label = !list.IsOrdered ? "• " : standard ? $"{OrderedLabel(list.BulletType, item.Order)}. " : $"{item.Order}. ";
                    AppendLabelled(section, item, label, indentLevel, standard);
                }

                break;

            // #1510 (G-D5): an alert is a QuoteBlock, so it comes first: its kind as a bold title, its children indented.
            case AlertBlock alert when standard:
                var alertTitle = section.AddParagraph();
                if (indentLevel > 0)
                {
                    alertTitle.Format.LeftIndent = Unit.FromCentimeter(0.5 * indentLevel);
                }

                alertTitle.Format.SpaceAfter = Unit.FromPoint(2);
                var kind = alert.Kind.ToString();
                AddText(alertTitle, kind.Length == 0 ? kind : char.ToUpperInvariant(kind[0]) + kind[1..].ToLowerInvariant(), new Look(Bold: true));
                foreach (var child in alert)
                {
                    AppendBlock(section, child, indentLevel + 1, standard);
                }

                break;

            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    AppendBlock(section, child, indentLevel + 1, standard);
                }

                break;

            case CodeBlock code:
                var codeParagraph = section.AddParagraph();
                codeParagraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * (indentLevel + 1));
                codeParagraph.AddText(code.Lines.ToString());
                break;

            case ThematicBreakBlock:
                AppendRule(section);
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
                    AppendTableAsText(section, table, indentLevel, standard);
                    break;
                }

                AppendTable(section, table, indentLevel, standard);
                break;

            // #1510 (G-D5): a definition list: each term bold, what defines it indented.
            case DefinitionList definitions when standard:
                foreach (var item in definitions)
                {
                    if (item is not DefinitionItem definition)
                    {
                        throw NotDrawn(item);
                    }

                    foreach (var child in definition)
                    {
                        if (child is DefinitionTerm term)
                        {
                            var termParagraph = section.AddParagraph();
                            if (indentLevel > 0)
                            {
                                termParagraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * indentLevel);
                            }

                            termParagraph.Format.SpaceAfter = Unit.FromPoint(2);
                            AppendInlines(termParagraph, term.Inline, standard, new Look(Bold: true));
                        }
                        else
                        {
                            AppendBlock(section, child, indentLevel + 1, standard);
                        }
                    }
                }

                break;

            // #1510 (G-D5): a figure's blocks as they are, its caption italic.
            case Figure figure when standard:
                foreach (var child in figure)
                {
                    if (child is FigureCaption caption)
                    {
                        var captionParagraph = section.AddParagraph();
                        if (indentLevel > 0)
                        {
                            captionParagraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * indentLevel);
                        }

                        AppendInlines(captionParagraph, caption.Inline, standard, new Look(Italic: true));
                    }
                    else
                    {
                        AppendBlock(section, child, indentLevel, standard);
                    }
                }

                break;

            // #1510 (G-D5): a footer's blocks, at 9 pt.
            case FooterBlock footer when standard:
                var before = section.Elements.Count;
                foreach (var child in footer)
                {
                    AppendBlock(section, child, indentLevel, standard);
                }

                for (var i = before; i < section.Elements.Count; i++)
                {
                    if (section.Elements[i] is Paragraph footerParagraph)
                    {
                        footerParagraph.Format.Font.Size = 9;
                    }
                }

                break;

            // #1510 (G-D5): the footnotes, after a rule, each as "n. " and its blocks.
            case FootnoteGroup footnotes when standard:
                AppendRule(section);
                foreach (var child in footnotes)
                {
                    if (child is not MdFootnote footnote)
                    {
                        throw NotDrawn(child);
                    }

                    AppendLabelled(section, footnote, $"{footnote.Order}. ", indentLevel, standard);
                }

                break;

            // #1510 (G-D5): link reference definitions (and the footnotes' and headings' own) are definitions, not prose.
            case LinkReferenceDefinitionGroup when standard:
                break;

            default:
                if (standard)
                {
                    throw NotDrawn(block);
                }

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

    /// <summary>
    /// A list item or a footnote: its first paragraph after <paramref name="label"/>, its other blocks indented under it.
    /// 5–30's list items, as they were.
    /// </summary>
    private static void AppendLabelled(Section section, ContainerBlock item, string label, int indentLevel, bool standard)
    {
        var first = true;
        foreach (var child in item)
        {
            if (first && child is ParagraphBlock itemParagraph)
            {
                var listParagraph = section.AddParagraph();
                listParagraph.Format.LeftIndent = Unit.FromCentimeter(0.5 * (indentLevel + 1));
                listParagraph.Format.SpaceAfter = Unit.FromPoint(2);
                listParagraph.AddText(label);
                AppendInlines(listParagraph, itemParagraph.Inline, standard);
                first = false;
            }
            else
            {
                AppendBlock(section, child, indentLevel + 1, standard);
            }
        }
    }

    /// <summary>
    /// #1510 (G-D5): an ordered item's label at 31. ListExtras keeps the list's kind in <see cref="ListBlock.BulletType"/>
    /// (<c>1</c>, <c>a</c>, <c>A</c>, <c>i</c>, <c>I</c>) and gives each item its number, counted from the list's start
    /// as a number (<c>c.</c> is order 3, <c>iv.</c> order 4).
    /// </summary>
    private static string OrderedLabel(char kind, int order) => kind switch
    {
        'a' or 'A' when order >= 1 => Letters(order, kind),
        'i' => Roman(order).ToLowerInvariant(),
        'I' => Roman(order),
        _ => order.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    /// <summary>a … z, then aa, ab …, as an HTML <c>type="a"</c> list counts.</summary>
    private static string Letters(int order, char first)
    {
        var letters = "";
        for (var n = order; n > 0; n = (n - 1) / 26)
        {
            letters = (char)(first + (n - 1) % 26) + letters;
        }

        return letters;
    }

    private static string Roman(int order)
    {
        if (order < 1)
        {
            return order.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var roman = new System.Text.StringBuilder();
        foreach (var (value, numeral) in new[] { (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I") })
        {
            for (; order >= value; order -= value)
            {
                roman.Append(numeral);
            }
        }

        return roman.ToString();
    }

    private static void AppendRule(Section section)
    {
        var ruleParagraph = section.AddParagraph();
        ruleParagraph.Format.SpaceBefore = Unit.FromPoint(6);
        ruleParagraph.Format.SpaceAfter = Unit.FromPoint(6);
        ruleParagraph.AddText(new string('—', 12));
    }

    private static NotSupportedException NotDrawn(MarkdownObject node) =>
        new($"The specVersion 31 PDF has no case for a Markdown {node.GetType().Name}; the 31 bundle is drawn type by type, with no fallback.");

    private static void AppendTable(Section section, MdTable table, int indentLevel, bool standard)
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
                    AppendCell(cellParagraph, cell, standard);
                }
            }
        }
    }

    /// <summary>A malformed table's rows as text, pipes between the cells — kept, never dropped.</summary>
    private static void AppendTableAsText(Section section, MdTable table, int indentLevel, bool standard)
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
                AppendCell(paragraph, cell, standard);
            }

            paragraph.AddText(" |");
        }
    }

    /// <summary>A cell's text. A pipe table's cell holds paragraphs only; at 31 anything else is a type not drawn.</summary>
    private static void AppendCell(Paragraph paragraph, TableCell cell, bool standard)
    {
        foreach (var child in cell)
        {
            if (child is ParagraphBlock cellBlock && (!standard || child.GetType() == typeof(ParagraphBlock)))
            {
                AppendInlines(paragraph, cellBlock.Inline, standard);
            }
            else if (standard)
            {
                throw NotDrawn(child);
            }
        }
    }

    private static int CellTextLength(Block cell) =>
        cell.Descendants<LiteralInline>().Sum(literal => literal.Content.Length)
        + cell.Descendants<CodeInline>().Sum(code => code.Content.Length);

    /// <summary>How a run of text is set: 5–30 use bold and italic only; 31 adds superscript and underline (G-D5).</summary>
    private readonly record struct Look(bool Bold = false, bool Italic = false, bool Superscript = false, bool Underline = false);

    private static void AppendInlines(Paragraph paragraph, ContainerInline? inlines, bool standard, Look look = default)
    {
        if (inlines == null)
        {
            return;
        }

        foreach (var inline in inlines)
        {
            AppendInline(paragraph, inline, look, standard);
        }
    }

    private static void AppendInline(Paragraph paragraph, Inline inline, Look look, bool standard)
    {
        if (standard && !Drawn31.Contains(inline.GetType()))
        {
            throw NotDrawn(inline);
        }

        switch (inline)
        {
            case LiteralInline literal:
                AddText(paragraph, literal.Content.ToString(), look);
                break;

            // #1510 (G-D5): at 31 an emphasis is read by its delimiter: "" (a citation) italic, ^ superscript, ++ underline.
            case EmphasisInline { DelimiterChar: '"' } citation when standard:
                AppendChildren(paragraph, citation, look with { Italic = true }, standard);
                break;

            case EmphasisInline { DelimiterChar: '^' } superscript when standard:
                AppendChildren(paragraph, superscript, look with { Superscript = true }, standard);
                break;

            case EmphasisInline { DelimiterChar: '+' } inserted when standard:
                AppendChildren(paragraph, inserted, look with { Underline = true }, standard);
                break;

            case EmphasisInline emphasis when !standard || emphasis.DelimiterChar is '*' or '_':
                var childLook = look with
                {
                    Bold = look.Bold || emphasis.DelimiterCount >= 2,
                    Italic = look.Italic || emphasis.DelimiterCount == 1
                };
                AppendChildren(paragraph, emphasis, childLook, standard);
                break;

            // #1510 (G-D5): at 31 a single line ending (soft) is one space; a hard break (\ or two spaces) is a line break.
            case LineBreakInline { IsHard: false } when standard:
                AddText(paragraph, " ", look);
                break;

            case LineBreakInline:
                paragraph.AddLineBreak();
                break;

            case CodeInline code:
                AddText(paragraph, code.Content, look);
                break;

            case LinkInline link:
                // Text only — the PDF is a clinical document, not hypertext. At 31 too: an image's children are its alt
                // text, and an autolinked www. address its URL text.
                AppendChildren(paragraph, link, look, standard);
                break;

            // #1510 (G-D5): the 31 inlines, each its text.
            case AutolinkInline autolink when standard:
                AddText(paragraph, autolink.Url, look);
                break;

            case HtmlEntityInline entity when standard:
                // its character: the escape's &#32; is a space
                AddText(paragraph, entity.Transcoded.ToString(), look);
                break;

            case TaskList task when standard:
                // ASCII, which the embedded font draws; the literal after it starts with the space
                AddText(paragraph, task.Checked ? "[x]" : "[ ]", look);
                break;

            case FootnoteLink footnoteLink when standard:
                // a back link (the footnote's own, to its reference) draws nothing
                if (!footnoteLink.IsBackLink)
                {
                    AddText(paragraph, footnoteLink.Footnote.Order.ToString(System.Globalization.CultureInfo.InvariantCulture), look with { Superscript = true });
                }

                break;

            case AbbreviationInline abbreviation when standard:
                AddText(paragraph, abbreviation.Abbreviation.Label ?? "", look);
                break;

            case DelimiterInline delimiter when standard:
                // an unmatched [ : its literal, then what followed it
                AddText(paragraph, delimiter.ToLiteral(), look);
                AppendChildren(paragraph, delimiter, look, standard);
                break;

            // #1510 (G-D5, ruled in G2): the bare wrapper Abbreviations writes round a literal it splits (as in
            // "HPI here"): its children. Only the exact type; a subclass reaches this only if named above.
            case ContainerInline wrapper when standard && wrapper.GetType() == typeof(ContainerInline):
                AppendChildren(paragraph, wrapper, look, standard);
                break;

            case ContainerInline container when !standard:
                AppendChildren(paragraph, container, look, standard);
                break;

            default:
                if (standard)
                {
                    throw NotDrawn(inline);
                }

                var text = inline.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    AddText(paragraph, text, look);
                }

                break;
        }
    }

    private static void AppendChildren(Paragraph paragraph, ContainerInline container, Look look, bool standard)
    {
        foreach (var child in container)
        {
            AppendInline(paragraph, child, look, standard);
        }
    }

    private static void AddText(Paragraph paragraph, string text, Look look)
    {
        if (text.Length == 0)
        {
            return;
        }

        var formatted = paragraph.AddFormattedText(text);
        formatted.Bold = look.Bold;
        formatted.Italic = look.Italic;

        // set only when on, so 5–30's runs are formatted exactly as before
        if (look.Superscript)
        {
            formatted.Superscript = true;
        }

        if (look.Underline)
        {
            formatted.Underline = Underline.Single;
        }
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
