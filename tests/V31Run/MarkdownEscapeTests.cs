using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31.Templates;
using Markdig;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 06 § 6.4; D7): <see cref="MarkdownEscape.Escape"/>, CommonMark's backslash escapes
/// for ASCII punctuation and nothing else, so plain text read into Markdown displays as exactly that text. Content is
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
            if (!Punctuation.Contains(c, StringComparison.Ordinal))
                Assert.Equal(c.ToString(), MarkdownEscape.Escape(c.ToString()));
        }
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
