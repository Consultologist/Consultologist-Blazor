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

    // #1543 (ruling 8, D16): both pipelines write a link, an image or an autolink only when its scheme is http, https or
    // mailto (no scheme stays); any other is its text. 30 is Render(md), the 5–30 pipeline; 31 is Render(md, 31).

    private static string Render(string markdown, int specVersion) =>
        specVersion == 31 ? MarkdownPreview.Render(markdown, 31) : MarkdownPreview.Render(markdown);

    [Theory]
    [InlineData("![a [b ![c](data:z)](javascript:y)](data:x)", "a b c", 30)]
    [InlineData("![a [b ![c](data:z)](javascript:y)](data:x)", "a b c", 31)]
    [InlineData("[a ![b](data:y)](javascript:x)", "a b", 30)]
    [InlineData("[a ![b](data:y)](javascript:x)", "a b", 31)]
    [InlineData("![a ![b](data:y)](data:x)", "a b", 30)]
    [InlineData("![a ![b](data:y)](data:x)", "a b", 31)]
    [InlineData("[a ![b](data:y) c](javascript:x) d", "a b c d", 30)]
    [InlineData("[a ![b](data:y) c](javascript:x) d", "a b c d", 31)]
    public void ANestedRefusedLinkOrImage_IsItsWholeText(string markdown, string text, int specVersion)
    {
        var html = Render(markdown, specVersion);

        Assert.DoesNotContain("href=", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("javascript", html);
        Assert.DoesNotContain("data:", html);
        Assert.Equal(text, System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "").Trim());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void ARefusedImageInAnAllowedLink_LeavesTheLink(int specVersion)
    {
        var html = Render("[see ![x](data:y)](https://example.org/a)", specVersion);

        Assert.Contains("<a href=\"https://example.org/a\">see x</a>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("data:", html);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void AnAllowedImageInARefusedLink_LeavesTheImage(int specVersion)
    {
        var html = Render("[a ![b](https://example.org/i.png) c](javascript:x) d", specVersion);

        Assert.Contains("<img src=\"https://example.org/i.png\"", html);
        Assert.DoesNotContain("href=", html);
        Assert.DoesNotContain("javascript", html);
        Assert.Contains(" c d", html);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void ALinkWithAnotherScheme_IsItsText(int specVersion)
    {
        var html = Render("See [the plan](javascript:alert(1)) today.", specVersion);

        Assert.Contains("See the plan today.", html);
        Assert.DoesNotContain("href", html);
        Assert.DoesNotContain("javascript", html);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void AnImageWithAnotherScheme_IsItsAltText(int specVersion)
    {
        var html = Render("![the scan](data:text/html,x)", specVersion);

        Assert.Contains("the scan", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("data:", html);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void AnAutolinkWithAnotherScheme_IsItsUrlText(int specVersion)
    {
        var html = Render("Call <vbscript:x> now.", specVersion);

        Assert.Contains("Call vbscript:x now.", html);
        Assert.DoesNotContain("<a", html);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void AReferenceLinkWithAnotherScheme_IsItsText(int specVersion)
    {
        var html = Render("See [the plan][p] and ![the scan][s].\n\n[p]: javascript:alert(1)\n[s]: data:text/html,x", specVersion);

        Assert.Contains("See the plan and the scan.", html);
        Assert.DoesNotContain("href", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("javascript", html);
        Assert.DoesNotContain("data:", html);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void AnEntityEncodedScheme_IsStillRefused(int specVersion)
    {
        var html = Render("[x](javascript&colon;alert(1))", specVersion);

        Assert.Contains(">x</p>", html);
        Assert.DoesNotContain("href", html);
        Assert.DoesNotContain("javascript", html);
    }

    [Theory]
    [InlineData(30, "[x](JAVASCRIPT:alert(1))")]
    [InlineData(31, "[x](JAVASCRIPT:alert(1))")]
    [InlineData(30, "[x](<java\tscript:alert(1)>)")]
    [InlineData(31, "[x](<java\tscript:alert(1)>)")]
    [InlineData(30, "[x](java&Tab;script:alert(1))")]
    [InlineData(31, "[x](java&Tab;script:alert(1))")]
    [InlineData(30, "[x](java&#10;script:alert(1))")]
    [InlineData(31, "[x](java&#10;script:alert(1))")]
    [InlineData(30, "[x](&#32;javascript:alert(1))")]
    [InlineData(31, "[x](&#32;javascript:alert(1))")]
    public void ASchemeDisguisedByCaseOrWhiteSpace_IsStillRefused(int specVersion, string markdown)
    {
        var html = Render(markdown, specVersion);

        Assert.Contains(">x</p>", html);
        Assert.DoesNotContain("href", html);
        Assert.DoesNotContain("script", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public void AnHttpsLinkAMailtoLinkAndARelativeLink_StayLinks(int specVersion)
    {
        var html = Render(
            "[a](https://example.org/a) [b](mailto:clinic@example.org) [c](/plan) [d](#plan) [e](//example.org/x) [f](HTTP://example.org) "
            + "![g](https://example.org/g.png) <https://example.org/h> <clinic@example.org>",
            specVersion);

        Assert.Contains("<a href=\"https://example.org/a\">a</a>", html);
        Assert.Contains("<a href=\"mailto:clinic@example.org\">b</a>", html);
        Assert.Contains("<a href=\"/plan\">c</a>", html);
        Assert.Contains("<a href=\"#plan\">d</a>", html);
        Assert.Contains("<a href=\"//example.org/x\">e</a>", html);
        Assert.Contains("<a href=\"HTTP://example.org\">f</a>", html);
        Assert.Contains("<img src=\"https://example.org/g.png\" alt=\"g\" />", html);
        Assert.Contains("<a href=\"https://example.org/h\">https://example.org/h</a>", html);
        Assert.Contains("<a href=\"mailto:clinic@example.org\">clinic@example.org</a>", html);
    }

    [Fact]
    public void Render31_ABareAddressWithAnotherScheme_IsItsText()
    {
        // 31's AutoLinks extension links a bare ftp:// address; the filter writes it as its text, a www. address stays.
        var html = MarkdownPreview.Render("Files at ftp://example.org/x and www.example.org.", 31);

        Assert.Contains("Files at ftp://example.org/x and ", html);
        Assert.DoesNotContain("href=\"ftp:", html);
        Assert.Contains("<a href=\"http://www.example.org\">www.example.org</a>", html);
    }
}
