using Markdig;

namespace Consultologist.PackageFormat;

/// <summary>
/// #1256: the one Markdown pipeline every surface that renders an assembled
/// note or a template preview shares — the Web's <c>MarkdownPreview</c> (the
/// Consults note, the editor's cards) and the Api's emailed PDF — so the two
/// cannot drift (they did: #1254 turned pipe tables on in the Web alone, and
/// a note with a table printed in the PDF as pipes). Three decisions, each
/// with its reason: HTML in the source is escaped, never injected (the output
/// feeds a MarkupString and a PDF); pipe tables are the one GFM extension on
/// (a prose node may draft a table; the advanced bundle — task lists,
/// footnotes, autolinks — would change what every existing preview renders);
/// a single newline is a hard break (the standards and prompt sources are
/// written line-per-line).
/// </summary>
public static class ConsultMarkdown
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();
}
