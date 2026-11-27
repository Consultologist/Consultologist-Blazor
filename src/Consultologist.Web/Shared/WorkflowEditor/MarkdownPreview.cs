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
/// <para>
/// #1510 (G-D2): a 31 surface (the run view's note, a <c>markdown_block</c> input's preview) passes its spec version, and
/// renders through <see cref="ConsultMarkdown.For"/>, as the 31 PDF does. <see cref="Render(string)"/> stays the 5–30 rendering.
/// </para>
/// </summary>
public static class MarkdownPreview
{
    public static string Render(string markdown) => Render(markdown, ConsultMarkdown.Pipeline);

    public static string Render(string markdown, int specVersion) => Render(markdown, ConsultMarkdown.For(specVersion));

    private static string Render(string markdown, MarkdownPipeline pipeline)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "<p class=\"muted-text\">No content.</p>";
        }

        return Markdown.ToHtml(markdown, pipeline);
    }
}
