using Consultologist.Web.Shared.WorkflowEditor;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1254: the shared markdown renderer — a GFM pipe table renders as a table (the
/// first 27 smoke's medications section showed as literal pipes), with the two
/// behaviours it already had pinned beside it: raw HTML escaped, a single
/// newline a hard break, and the empty paragraph for nothing.
/// </summary>
public class MarkdownPreviewTests
{
    [Fact]
    public void APipeTable_WithAnAlignmentRow_RendersAsATable()
    {
        var html = MarkdownPreview.Render("| Name | Dose | Frequency |\n|---|---:|---|\n| Ramipril | 10 mg | Once daily |\n| Furosemide | 40 mg | Each morning |");

        Assert.Contains("<table>", html);
        Assert.Contains("<thead>", html);
        Assert.Contains("<th>Name</th>", html);
        Assert.Contains("<th style=\"text-align: right;\">Dose</th>", html);
        Assert.Contains("<td style=\"text-align: right;\">10 mg</td>", html);
        Assert.Contains("<td>Each morning</td>", html);
        Assert.DoesNotContain("|", html);
    }

    [Fact]
    public void RawHtml_IsEscaped_NotInjected()
    {
        var html = MarkdownPreview.Render("Plain <script>alert(1)</script> text");

        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void ASingleNewline_IsAHardBreak_StillWithTablesOn()
    {
        var html = MarkdownPreview.Render("first line\nsecond line");

        Assert.Contains("first line<br />", html);
        Assert.Contains("second line", html);
    }

    [Fact]
    public void NothingToRender_IsTheMutedParagraph()
    {
        Assert.Equal("<p class=\"muted-text\">No content.</p>", MarkdownPreview.Render("  \n "));
    }

    // #1510 (the owner's third fix-first group, G-D2): a 31 surface renders through ConsultMarkdown.For(31).

    private static string EveryBlock() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Markdown31", "every-block.md"));

    [Fact]
    public void Render31_EveryBlockOfTheBundle_RendersItsElements()
    {
        var html = MarkdownPreview.Render(EveryBlock(), 31);

        foreach (var element in new[]
                 {
                     "<sup>", "<ins>", "<cite>", "<abbr", "<dl>", "<figure>", "<footer>", "markdown-alert",
                     "class=\"footnotes\"", "type=\"checkbox\"", "<ol type=\"a\"",
                 })
        {
            Assert.Contains(element, html);
        }
    }

    [Fact]
    public void Render31_GenericAttributes_AreText()
    {
        var html = MarkdownPreview.Render("# Plan {#id .class key=value}\n\nSeen by Dr A. Sample {onclick=x}", 31);

        // as an attribute (` onclick=`); the text keeps `{onclick=x}`
        Assert.DoesNotContain(" onclick=", html);
        Assert.Contains("{onclick=x}", html);
        Assert.Contains("{#id .class key=value}", html);
    }

    [Fact]
    public void Render31_ExcludedSyntax_IsText()
    {
        var html = MarkdownPreview.Render(
            "Dose ~~withdrawn~~ and ==marked==.\n\n![a video](https://www.youtube.com/watch?v=abc123)\n\nA $5$ fee and $x^2$.\n\n:::note\nSeen by Dr A. Sample.\n:::",
            31);

        Assert.DoesNotContain("<del>", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.DoesNotContain("class=\"math\"", html);
        Assert.DoesNotContain("class=\"note\"", html);
        Assert.Contains("~~withdrawn~~", html);
        Assert.Contains("==marked==", html);
    }

    [Fact]
    public void Render31_ASingleLineEnding_IsNoBreak()
    {
        var html = MarkdownPreview.Render("first line\nsecond line", 31);

        Assert.DoesNotContain("<br", html);
        Assert.Contains("first line\nsecond line", html);
    }

    [Fact]
    public void Render30_IsTodaysRendering()
    {
        Assert.Contains("first line<br />", MarkdownPreview.Render("first line\nsecond line", 30));
        Assert.Equal("<p class=\"muted-text\">No content.</p>", MarkdownPreview.Render("  \n ", 31));
    }
}
