using Markdig;

namespace Consultologist.PackageFormat.V31;

/// <summary>
/// #1501 (specVersion 31, area 04): the one Markdown-to-plain-text conversion (decision D13): Markdig's
/// <c>Markdown.ToPlainText</c> over the <c>UseAdvancedExtensions</c> pipeline (part 02 § 5.2), <c>\r\n</c> and <c>\r</c>
/// turned to <c>\n</c>, line breaks at the very end removed. It is <c>plain_conversion</c> 1.0.0, pinned by goldens; the
/// <c>plain</c> operation and plain-text output both use it.
/// </summary>
public static class PlainConversion
{
    /// <summary>The conversion's version, as in <c>constants.json</c>.</summary>
    public const string Version = "1.0.0";

    // Its own pipeline: a change to the Editor's rendering pipeline must not change the bytes of plain.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>Plain text for <paramref name="markdown"/>; <c>""</c> for null or empty text. Never throws.</summary>
    public static string ToPlain(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return "";
        var plain = Markdown.ToPlainText(markdown, Pipeline);
        return plain.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
    }
}
