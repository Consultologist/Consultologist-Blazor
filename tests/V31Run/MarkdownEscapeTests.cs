using System.Text.Json.Nodes;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31.Templates;
using Markdig;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 06 § 6.4; D7): <see cref="MarkdownEscape.Escape"/>, CommonMark's backslash escapes
/// for ASCII punctuation and, since #1505's ruling R9, the first space or tab leading each line as its character reference,
/// so plain text read into Markdown displays as exactly that text, and since #1510 (G-D7) two spaces before each line
/// ending, so its lines stay lines. A 31 test: its renders go through <see cref="ConsultMarkdown.For"/>(31). Content is
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
    public void LettersDigitsSpacesAndNonAscii_AreUnchanged()
    {
        // #1510 (G-D7): a line ending gains two spaces before it (ALineEnding_IsWrittenAsAHardBreak), so it is not here.
        const string plain = "Margaret Okafor 58 seen by Dr A Sample\t été 痛み — “quoted” ½ € 🩺 ";
        Assert.Equal(plain, MarkdownEscape.Escape(plain));
        Assert.Equal("", MarkdownEscape.Escape(""));
        for (var c = (char)0; c < 128; c++)
        {
            // A space or a tab alone leads its line (ALineStartingWithFourSpaces_IsNotACodeBlock).
            if (!Punctuation.Contains(c, StringComparison.Ordinal) && c is not ' ' and not '\t' and not '\n' and not '\r')
                Assert.Equal(c.ToString(), MarkdownEscape.Escape(c.ToString()));
        }
    }

    [Fact]
    public void ALineStartingWithFourSpaces_IsNotACodeBlock()
    {
        // #1505 comment 4, ruling 1 (R9): the first leading space of each line is its entity; the rest is escaped as before.
        Assert.Equal("&#32;", MarkdownEscape.Escape(" "));
        Assert.Equal("&#32;   Seen with her daughter\\.", MarkdownEscape.Escape("    Seen with her daughter."));
        Assert.Equal("Seen  \n&#32;   with \\*her\\*  \r\n&#32;daughter  \r&#32; \\#4", MarkdownEscape.Escape("Seen\n    with *her*\r\n daughter\r  #4"));
        Assert.Equal("Hb 104 g\\/L    ferritin 9", MarkdownEscape.Escape("Hb 104 g/L    ferritin 9"));
        Assert.Equal("Margaret Okafor  \n  \nDr A\\. Sample", MarkdownEscape.Escape("Margaret Okafor\n\nDr A. Sample"));

        // A piece of a longer text that does not start its line keeps its first space.
        Assert.Equal(" \\(clinic\\)\\.  \n&#32;x", MarkdownEscape.Escape(" (clinic).\n x", startsLine: false));

        var html = Markdown.ToHtml(MarkdownEscape.Escape("Seen today.\n    Margaret Okafor, 58."), ConsultMarkdown.For(31));
        Assert.DoesNotContain("<code>", html, StringComparison.Ordinal);
        Assert.Equal("<p>Seen today.<br />\n    Margaret Okafor, 58.</p>\n", html);
    }

    [Fact]
    public void ALineStartingWithATab_IsNotACodeBlock()
    {
        Assert.Equal("&#9;", MarkdownEscape.Escape("\t"));
        Assert.Equal("&#9;Ferritin 9", MarkdownEscape.Escape("\tFerritin 9"));
        Assert.Equal("&#9;\tx  \n&#32;\ty  \n&#9; z", MarkdownEscape.Escape("\t\tx\n \ty\n\t z"));
        Assert.Equal("a\tb", MarkdownEscape.Escape("a\tb"));

        var html = Markdown.ToHtml(MarkdownEscape.Escape("Plan:\n\tFBC and ferritin"), ConsultMarkdown.For(31));
        Assert.DoesNotContain("<code>", html, StringComparison.Ordinal);
        Assert.Equal("<p>Plan:<br />\n\tFBC and ferritin</p>\n", html);
    }

    [Fact]
    public void AWhiteSpaceOnlyLine_IsEscapedAsAnyLine_AndStaysBlank()
    {
        // R9 as worded: a line of white space only is led by a space or tab too, so it gets its entity. The escaped text
        // still displays blank, and IsBlank says so (Task 15's review): failIfEmpty and whenEmpty judge it as before.
        Assert.Equal("&#32; ", MarkdownEscape.Escape("  "));
        Assert.Equal("&#9;  \n", MarkdownEscape.Escape("\t\n"));
        Assert.Equal("Seen  \n&#32;   \nPlan", MarkdownEscape.Escape("Seen\n  \nPlan"));
        foreach (var blank in new[] { "  ", "\t\n", " \t \r\n\t", "\n\n", "" })
        {
            Assert.True(MarkdownEscape.IsBlank(MarkdownEscape.Escape(blank)), JsonValue.Create(blank).ToJsonString());
            Assert.True(MarkdownEscape.IsBlank(blank));
        }

        Assert.True(MarkdownEscape.IsBlank("&#32;&#9;&#32; \n"));
        Assert.False(MarkdownEscape.IsBlank(MarkdownEscape.Escape(" .")));
        Assert.False(MarkdownEscape.IsBlank("&#32;x"));
        Assert.False(MarkdownEscape.IsBlank("&#33;"));
        Assert.False(MarkdownEscape.IsBlank("&#32"));
        Assert.False(MarkdownEscape.IsBlank("&#x20;"));
        Assert.False(MarkdownEscape.IsBlank("&nbsp;"));

        Assert.DoesNotContain("<code>", Markdown.ToHtml(MarkdownEscape.Escape("Seen\n    \n\tPlan"), ConsultMarkdown.For(31)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEscapedText_RendersAsThatText_InOneParagraph()
    {
        const string plain = "*not bold* # no heading [x](y) 1. item <b>";

        var html = Markdown.ToHtml(MarkdownEscape.Escape(plain), ConsultMarkdown.For(31));

        Assert.Equal("<p>*not bold* # no heading [x](y) 1. item &lt;b&gt;</p>\n", html);
    }

    [Fact]
    public void ALineThatWouldBeABlock_StaysText()
    {
        foreach (var plain in new[] { "# Plan", "- a list", "> quoted", "1. first", "---", "```", "+ more" })
        {
            var html = Markdown.ToHtml(MarkdownEscape.Escape(plain), ConsultMarkdown.For(31));
            Assert.Equal($"<p>{System.Net.WebUtility.HtmlEncode(plain).Replace("&#39;", "'", StringComparison.Ordinal)}</p>\n", html);
        }
    }

    [Fact]
    public void ALineEnding_IsWrittenAsAHardBreak()
    {
        // #1510 (G-D7): two spaces before each line ending, a \r\n counting as one; a blank line's ending too.
        Assert.Equal("a  \nb", MarkdownEscape.Escape("a\nb"));
        Assert.Equal("a  \r\nb", MarkdownEscape.Escape("a\r\nb"));
        Assert.Equal("a  \n  \nb", MarkdownEscape.Escape("a\n\nb"));
        Assert.Equal("a  \rb", MarkdownEscape.Escape("a\rb"));
        Assert.Equal("Margaret Okafor  \n", MarkdownEscape.Escape("Margaret Okafor\n"));
        Assert.Equal("  \n", MarkdownEscape.Escape("\n", startsLine: false));
    }

    [Fact]
    public void AnEscapedText_KeepsItsLines_ThroughThe31Pipeline()
    {
        // At 31 a single line ending is no break (G-D3), so without the escape's two spaces these lines would join.
        foreach (var ending in new[] { "\n", "\r\n" })
        {
            var lines = new[] { "Margaret Okafor, 58.", "Seen by Dr A. Sample", "    at Sample Oncology Clinic", "Plan: FBC & ferritin" };
            var html = Markdown.ToHtml(MarkdownEscape.Escape(string.Join(ending, lines)), ConsultMarkdown.For(31));
            Assert.Equal(lines.Length - 1, Count(html, "<br />"));
            Assert.Equal(1, Count(html, "<p>"));

            // A blank line stays a paragraph break; the two spaces at a paragraph's end are dropped.
            var paragraphs = Markdown.ToHtml(MarkdownEscape.Escape(string.Join(ending, "Seen today.", "Margaret Okafor", "", "Plan:", "FBC")), ConsultMarkdown.For(31));
            Assert.Equal("<p>Seen today.<br />\nMargaret Okafor</p>\n<p>Plan:<br />\nFBC</p>\n", paragraphs.ReplaceLineEndings("\n"));
        }
    }

    [Fact]
    public void EscapingPieceByPiece_IsEscapingWhole()
    {
        // G-D7: the rule is local, so a text escaped in two pieces (each a line start only where it starts a line) is the
        // text escaped whole, at every cut but one between \r and \n (where EscapedSubstitutions falls back to the unit).
        const string plain = "Seen  by Dr A. Sample\n    Margaret *Okafor*\r\n\r\n\t#4\r- plan\n\n";
        var whole = MarkdownEscape.Escape(plain);
        for (var cut = 0; cut <= plain.Length; cut++)
        {
            if (cut > 0 && cut < plain.Length && plain[cut - 1] == '\r' && plain[cut] == '\n')
                continue;
            var pieces = MarkdownEscape.Escape(plain[..cut]) + MarkdownEscape.Escape(plain[cut..], MarkdownEscape.StartsLine(plain, cut));
            Assert.Equal(whole, pieces);
        }
    }

    private static int Count(string text, string of)
    {
        var n = 0;
        for (var at = text.IndexOf(of, StringComparison.Ordinal); at >= 0; at = text.IndexOf(of, at + of.Length, StringComparison.Ordinal))
            n++;
        return n;
    }
}
