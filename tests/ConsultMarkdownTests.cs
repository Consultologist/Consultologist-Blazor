using Consultologist.PackageFormat;
using Markdig;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1256: the one Markdown pipeline the Web preview and the emailed PDF share —
/// its three decisions pinned where it lives.
/// </summary>
public class ConsultMarkdownTests
{
    [Fact]
    public void PipeTables_AreOn_WithTheAlignmentRowHonoured()
    {
        var html = Markdown.ToHtml("| Name | Dose |\n|---|---:|\n| Ramipril | 10 mg |", ConsultMarkdown.Pipeline);

        Assert.Contains("<table>", html);
        Assert.Contains("<th>Name</th>", html);
        Assert.Contains("<td style=\"text-align: right;\">10 mg</td>", html);
        Assert.DoesNotContain("|", html);
    }

    [Fact]
    public void RawHtml_IsEscaped()
    {
        var html = Markdown.ToHtml("a <b>bold</b> claim", ConsultMarkdown.Pipeline);

        Assert.Contains("&lt;b&gt;", html);
        Assert.DoesNotContain("<b>", html);
    }

    [Fact]
    public void ASingleNewline_IsAHardBreak()
    {
        Assert.Contains("first<br />", Markdown.ToHtml("first\nsecond", ConsultMarkdown.Pipeline));
    }

    [Fact]
    public void TheAdvancedBundle_StaysOff()
    {
        // A task list and a strikethrough render as their text — nothing beyond pipe tables is on.
        var html = Markdown.ToHtml("- [ ] todo\n\n~~gone~~", ConsultMarkdown.Pipeline);

        Assert.DoesNotContain("type=\"checkbox\"", html);
        Assert.DoesNotContain("<del>", html);
        Assert.Contains("[ ] todo", html);
    }
}
