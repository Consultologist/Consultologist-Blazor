using Consultologist.PackageFormat;
using Markdig;

namespace Consultologist.Web.Shared.WorkflowEditor;

/// <summary>
/// The shared markdown renderer behind every preview surface (the Templates
/// page, the editor's prompt and standards cards, and the v6 assembled note).
/// Raw HTML in the source is escaped, never injected — the output feeds a
/// MarkupString. The pipeline is the library's <see cref="ConsultMarkdown.Pipeline"/>
/// (#1256) — the same one the emailed PDF renders from, so a note reads the
/// same in the app and in the mailbox; pipe tables on (#1254), nothing else.
/// </summary>
public static class MarkdownPreview
{
    private static readonly MarkdownPipeline Pipeline = ConsultMarkdown.Pipeline;

    public static string Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "<p class=\"muted-text\">No content.</p>";
        }

        return Markdown.ToHtml(markdown, Pipeline);
    }
}
