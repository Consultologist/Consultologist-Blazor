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
}
