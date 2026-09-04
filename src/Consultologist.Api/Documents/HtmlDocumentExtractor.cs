using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Consultologist.Api.Documents;

/// <summary>
/// #655: HTML as a first-class format. The Epic SMART intake spike (#190)
/// found chart documents arrive as text/html; before this they either
/// parsed to `corrupt` or slipped through the text fallback as tag soup
/// (the raw markup becoming the "text"). This extracts the VISIBLE text —
/// tags dropped, entities decoded, block boundaries kept as line breaks —
/// using AngleSharp's HTML5 parser.
///
/// Two refusals it keeps deliberately: a note that is really a PDF rendered
/// into HTML spans (the sandbox's own shape) extracts to PDF source, which
/// stays `corrupt` — detected, never unwrapped (#655's stated boundary);
/// and empty-after-strip is `empty` through the shared Normalize gate.
/// </summary>
internal static class HtmlDocumentExtractor
{
    internal static string ExtractorId { get; } =
        ExtractorIdentity.For("html", typeof(HtmlParser).Assembly);

    // How much of the document the sniffer decodes to decide "is this HTML".
    private const int SniffBytes = 4096;

    // Block-level elements whose boundary becomes a line break, so a note
    // rendered as one <div> per line does not run together into one blob.
    private static readonly string[] BlockTags =
        ["div", "p", "br", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "section", "article", "blockquote"];

    // The stripped text is refused as corrupt when control/replacement
    // characters exceed this share of it — the PDF-rendered-as-HTML note
    // (a deflate stream carried as span text) sits far above; real prose
    // sits at essentially zero.
    private const double MaxUnreadableRatio = 0.15;

    /// <summary>
    /// HTML has no magic number, so this sniffs a decoded prefix. A close
    /// tag (<c>&lt;/word</c>) is the discriminator: it catches an HTML
    /// fragment with no &lt;html&gt; root (the Epic shape — many
    /// &lt;/div&gt;/&lt;/span&gt;) while plain prose with a stray
    /// <c>&lt;word&gt;</c> and no matching close tag does not trigger.
    /// Binary is rejected first so this never fights the text decoder.
    /// </summary>
    internal static bool Matches(byte[] bytes)
    {
        if (!TextDocumentDecoder.LooksLikeText(bytes))
        {
            return false;
        }

        var prefix = TextDocumentDecoder.Decode(bytes);
        if (prefix.Length > SniffBytes)
        {
            prefix = prefix[..SniffBytes];
        }

        return LooksLikeHtml(prefix);
    }

    /// <summary>Extracted so the sniff heuristic can be asserted directly.</summary>
    internal static bool LooksLikeHtml(string prefix)
    {
        var lower = prefix.ToLowerInvariant();

        if (lower.Contains("<!doctype html", StringComparison.Ordinal)
            || lower.Contains("<html", StringComparison.Ordinal)
            || lower.Contains("<head", StringComparison.Ordinal)
            || lower.Contains("<body", StringComparison.Ordinal))
        {
            return true;
        }

        // A close tag: '<', '/', then an ASCII letter.
        for (var i = 0; i + 2 < lower.Length; i++)
        {
            if (lower[i] == '<' && lower[i + 1] == '/' && char.IsAsciiLetter(lower[i + 2]))
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
            var html = TextDocumentDecoder.Decode(bytes);
            var document = new HtmlParser().ParseDocument(html);

            // Script and style carry no visible text — drop them before the walk.
            foreach (var node in document.QuerySelectorAll("script,style"))
            {
                node.Remove();
            }

            var builder = new StringBuilder();
            AppendVisibleText(document.Body ?? (INode)document.DocumentElement, builder);
            var text = CollapseWhitespace(builder.ToString());

            if (text.Length > DocumentExtraction.MaxCharacters)
            {
                return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.TooMuchText);
            }

            // The PDF-rendered-as-HTML note: its stripped text is PDF source,
            // not readable content. Refused as corrupt — detected, not
            // unwrapped (#655). Empty-after-strip falls to the shared
            // Normalize gate, which downgrades whitespace-only to `empty`.
            if (LooksUnreadable(text))
            {
                return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Corrupt);
            }

            return DocumentExtractionResult.Extracted(text, ExtractorId, pageCount: null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return DocumentExtractionResult.Refused(DocumentExtractionOutcomes.Corrupt);
        }
    }

    private static void AppendVisibleText(INode node, StringBuilder builder)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == NodeType.Text)
            {
                builder.Append(child.TextContent);
            }
            else if (child.NodeType == NodeType.Element)
            {
                var element = (IElement)child;
                var isBlock = Array.IndexOf(BlockTags, element.LocalName) >= 0;

                if (isBlock)
                {
                    builder.Append('\n');
                }

                AppendVisibleText(child, builder);

                if (isBlock)
                {
                    builder.Append('\n');
                }
            }
        }
    }

    /// <summary>Collapse runs of spaces/tabs; trim each line; drop blank runs to one break.</summary>
    internal static string CollapseWhitespace(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var kept = new List<string>(lines.Length);
        var blankPending = false;

        foreach (var raw in lines)
        {
            var line = System.Text.RegularExpressions.Regex.Replace(raw, "[ \\t\\f\\v\\u00A0]+", " ").Trim();
            if (line.Length == 0)
            {
                blankPending = kept.Count > 0;
                continue;
            }

            if (blankPending)
            {
                kept.Add(string.Empty);
                blankPending = false;
            }

            kept.Add(line);
        }

        return string.Join("\n", kept);
    }

    /// <summary>Extracted so the corrupt heuristic can be asserted directly.</summary>
    internal static bool LooksUnreadable(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("%PDF-", StringComparison.Ordinal))
        {
            return true;
        }

        if (text.Length == 0)
        {
            return false; // empty is the Normalize gate's business, not corrupt.
        }

        var unreadable = 0;
        foreach (var ch in text)
        {
            if (ch == '�' || (char.IsControl(ch) && ch != '\n' && ch != '\t'))
            {
                unreadable++;
            }
        }

        return (double)unreadable / text.Length > MaxUnreadableRatio;
    }
}
