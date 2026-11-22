using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31.Templates;
using Markdig;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 06 § 6.4; D7): <see cref="MarkdownEscape.Escape"/>, CommonMark's backslash escapes
/// for ASCII punctuation and, since #1505's ruling R9, the first space or tab leading each line as its character reference,
/// so plain text read into Markdown displays as exactly that text. Content is
/// fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class MarkdownEscapeTests
{
    private const string Punctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    [Fact]
    public void EachAsciiPunctuationCharacter_GainsABackslash()
    {
        Assert.Equal(32, Punctuation.Length);
        foreach (var c in Punctuation)
            Assert.Equal("\\" + c, MarkdownEscape.Escape(c.ToString()));
        Assert.Equal("1.0.0", MarkdownEscape.Version);
    }

    [Fact]
    public void LettersDigitsSpacesNewlinesAndNonAscii_AreUnchanged()
    {
        const string plain = "Margaret Okafor 58\nseen by Dr A Sample\t\r\nété 痛み — “quoted” ½ € 🩺 ";
        Assert.Equal(plain, MarkdownEscape.Escape(plain));
        Assert.Equal("", MarkdownEscape.Escape(""));
        for (var c = (char)0; c < 128; c++)
        {
            // A space or a tab alone leads its line (ALineStartingWithFourSpaces_IsNotACodeBlock).
            if (!Punctuation.Contains(c, StringComparison.Ordinal) && c is not ' ' and not '\t')
                Assert.Equal(c.ToString(), MarkdownEscape.Escape(c.ToString()));
        }
    }

    [Fact]
    public void ALineStartingWithFourSpaces_IsNotACodeBlock()
    {
        // #1505 comment 4, ruling 1 (R9): the first leading space of each line is its entity; the rest is escaped as before.
        Assert.Equal("&#32;", MarkdownEscape.Escape(" "));
        Assert.Equal("&#32;   Seen with her daughter\\.", MarkdownEscape.Escape("    Seen with her daughter."));
        Assert.Equal("Seen\n&#32;   with \\*her\\*\r\n&#32;daughter\r&#32; \\#4", MarkdownEscape.Escape("Seen\n    with *her*\r\n daughter\r  #4"));
        Assert.Equal("Hb 104 g\\/L    ferritin 9", MarkdownEscape.Escape("Hb 104 g/L    ferritin 9"));
        Assert.Equal("Margaret Okafor\n\nDr A\\. Sample", MarkdownEscape.Escape("Margaret Okafor\n\nDr A. Sample"));

        // A piece of a longer text that does not start its line keeps its first space.
        Assert.Equal(" \\(clinic\\)\\.\n&#32;x", MarkdownEscape.Escape(" (clinic).\n x", startsLine: false));

        var html = Markdown.ToHtml(MarkdownEscape.Escape("Seen today.\n    Margaret Okafor, 58."), ConsultMarkdown.Pipeline);
        Assert.DoesNotContain("<code>", html, StringComparison.Ordinal);
        Assert.Equal("<p>Seen today.<br />\n    Margaret Okafor, 58.</p>\n", html);
    }

    [Fact]
    public void ALineStartingWithATab_IsNotACodeBlock()
    {
        Assert.Equal("&#9;", MarkdownEscape.Escape("\t"));
        Assert.Equal("&#9;Ferritin 9", MarkdownEscape.Escape("\tFerritin 9"));
        Assert.Equal("&#9;\tx\n&#32;\ty\n&#9; z", MarkdownEscape.Escape("\t\tx\n \ty\n\t z"));
        Assert.Equal("a\tb", MarkdownEscape.Escape("a\tb"));

        var html = Markdown.ToHtml(MarkdownEscape.Escape("Plan:\n\tFBC and ferritin"), ConsultMarkdown.Pipeline);
        Assert.DoesNotContain("<code>", html, StringComparison.Ordinal);
        Assert.Equal("<p>Plan:<br />\n\tFBC and ferritin</p>\n", html);
    }

    [Fact]
    public void TheEscapedText_RendersAsThatText_InOneParagraph()
    {
        const string plain = "*not bold* # no heading [x](y) 1. item <b>";

        var html = Markdown.ToHtml(MarkdownEscape.Escape(plain), ConsultMarkdown.Pipeline);

        Assert.Equal("<p>*not bold* # no heading [x](y) 1. item &lt;b&gt;</p>\n", html);
    }

    [Fact]
    public void ALineThatWouldBeABlock_StaysText()
    {
        foreach (var plain in new[] { "# Plan", "- a list", "> quoted", "1. first", "---", "```", "+ more" })
        {
            var html = Markdown.ToHtml(MarkdownEscape.Escape(plain), ConsultMarkdown.Pipeline);
            Assert.Equal($"<p>{System.Net.WebUtility.HtmlEncode(plain).Replace("&#39;", "'", StringComparison.Ordinal)}</p>\n", html);
        }
    }
}
