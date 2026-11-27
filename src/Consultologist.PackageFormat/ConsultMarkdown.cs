using Markdig;
using Markdig.Extensions.EmphasisExtras;

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
/// <para>
/// #1510 (the owner's third fix-first group, items 1 and 2): specVersion 31 renders by one standard,
/// <see cref="Pipeline31"/>, and every surface picks its pipeline by the job's specVersion through
/// <see cref="For"/>. 5–30 keep <see cref="Pipeline"/>, unchanged, until 30 is retired.
/// </para>
/// </summary>
public static class ConsultMarkdown
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UsePipeTables()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    /// <summary>
    /// The specVersion 31 pipeline (part 02 § 5.2): Markdig 0.41.3's <c>UseAdvancedExtensions</c>, in its order, built
    /// explicitly, with HTML off and standard line handling (a single line ending is not a break). Left out:
    /// GenericAttributes (<c>{…}</c> writes any attribute name, an event handler included), CustomContainers (a
    /// <c>class</c> from the text), MediaLinks (an embed), Mathematics, Diagrams and GridTables (neither surface draws
    /// them), and emphasis extras other than superscript and inserted (the PDF has no strikethrough or highlight).
    /// <c>constants.json</c> <c>markdig</c> lists the same extensions (<see cref="Extensions31"/>).
    /// </summary>
    public static readonly MarkdownPipeline Pipeline31 = new MarkdownPipelineBuilder()
        .UseAlertBlocks()
        .UseAbbreviations()
        .UseAutoIdentifiers()
        .UseCitations()
        .UseDefinitionLists()
        .UseEmphasisExtras(EmphasisExtraOptions.Superscript | EmphasisExtraOptions.Inserted)
        .UseFigures()
        .UseFooters()
        .UseFootnotes()
        .UsePipeTables()
        .UseListExtras()
        .UseTaskLists()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    /// <summary>The names of <see cref="Pipeline31"/>'s extensions, each type's name less <c>Extension</c>, in order: <c>constants.json</c>'s <c>markdig.extensions</c>.</summary>
    public static IReadOnlyList<string> Extensions31 { get; } =
        ["Alert", "Abbreviation", "AutoIdentifier", "Citation", "DefinitionList", "EmphasisExtra", "Figure", "Footer", "Footnote", "PipeTable", "ListExtra", "TaskList", "AutoLink"];

    /// <summary>The pipeline for a job or package of <paramref name="specVersion"/>: <see cref="Pipeline31"/> from 31, else <see cref="Pipeline"/>.</summary>
    public static MarkdownPipeline For(int specVersion) => specVersion >= 31 ? Pipeline31 : Pipeline;
}
