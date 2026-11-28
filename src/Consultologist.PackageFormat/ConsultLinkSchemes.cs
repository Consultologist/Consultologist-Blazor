using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1543 (the owner's ruling 8, D16): both of <see cref="ConsultMarkdown"/>'s pipelines write a link, an image or an
/// autolink only when its scheme is one of <see cref="Allowed"/> (a URL with no scheme, such as <c>/plan</c>,
/// <c>#plan</c> or <c>//host</c>, stays). The parsed document is rewritten once parsing is done, so every reader of the
/// pipeline (the Web's HTML, the PDF walker, <c>plain()</c>) sees the same tree: a <see cref="LinkInline"/> of another
/// scheme (a link or an image, inline or by reference) is replaced by its children (its text; an image's alt text), an
/// <see cref="AutolinkInline"/> by a literal of its URL text. <c>constants.json</c> <c>markdig.linkSchemes</c> lists the
/// same schemes; <c>markdig.extensions</c> does not name this extension, which is not Markdig's.
/// </summary>
public sealed partial class ConsultLinkSchemes : IMarkdownExtension
{
    /// <summary>The schemes a link, an image or an autolink is written with: <c>constants.json</c> <c>markdig.linkSchemes</c>.</summary>
    public static IReadOnlyList<string> Allowed { get; } = ["http", "https", "mailto"];

    /// <summary>
    /// Whether a link to <paramref name="url"/> is written: it has no scheme, or one of <see cref="Allowed"/>, in any
    /// case. The scheme is read after ASCII white space and control characters are removed (a browser drops a tab or a
    /// line ending inside <c>java&#9;script:</c>), as <c>^[A-Za-z][A-Za-z0-9+.-]*:</c>.
    /// </summary>
    public static bool Writes(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return true;
        }

        var read = string.Concat(url.Where(c => c > ' ' && c != '\u007F'));
        var scheme = Scheme().Match(read);
        return !scheme.Success || Allowed.Contains(scheme.Groups[1].Value, StringComparer.OrdinalIgnoreCase);
    }

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        pipeline.DocumentProcessed -= Rewrite;
        pipeline.DocumentProcessed += Rewrite;
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private static void Rewrite(MarkdownDocument document)
    {
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            // A heading's reference (AutoIdentifiers) carries its URL as GetDynamicUrl, as the HTML renderer reads it.
            if (Writes(link.GetDynamicUrl?.Invoke() ?? link.Url))
            {
                continue;
            }

            while (link.FirstChild is { } child)
            {
                child.Remove();
                link.InsertBefore(child);
            }

            link.Remove();
        }

        foreach (var autolink in document.Descendants<AutolinkInline>().ToList())
        {
            if (Writes(autolink.Url))
            {
                continue;
            }

            autolink.ReplaceBy(new LiteralInline(autolink.Url)
            {
                Span = autolink.Span,
                Line = autolink.Line,
                Column = autolink.Column,
            });
        }
    }

    [GeneratedRegex("^([A-Za-z][A-Za-z0-9+.-]*):")]
    private static partial Regex Scheme();
}
