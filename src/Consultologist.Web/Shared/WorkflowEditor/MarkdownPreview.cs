using Markdig;

namespace Consultologist.Web.Shared.WorkflowEditor;

/// <summary>
/// The shared markdown renderer behind every preview surface (the Templates
/// page, the editor's prompt and standards cards, and the v6 assembled note).
/// Raw HTML in the source is escaped, never injected — the output feeds a
/// MarkupString. Pipe tables are the one GFM extension on (#1254): a prose
/// node may draft a table, and without the extension a note showed it as
/// literal pipes; the advanced bundle (task lists, footnotes, autolinks…)
/// stays off so every other preview renders as it did.
/// </summary>
public static class MarkdownPreview
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        // #1254: a GFM table (| a | b | with an alignment row) is a table block —
        // parsed before the soft-break rule below sees its rows.
        .UsePipeTables()
        // Single newlines break lines, matching how the standards and prompt
        // sources are written line-per-line.
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    public static string Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "<p class=\"muted-text\">No content.</p>";
        }

        return Markdown.ToHtml(markdown, Pipeline);
    }
}
