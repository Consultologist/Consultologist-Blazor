using Consultologist.PackageFormat;
using Markdig;
using Markdig.Syntax;

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

    // #1510 (the owner's third fix-first group, items 1 and 2): one Markdown standard for specVersion 31.

    /// <summary>G-D4: the advanced bundle's extensions the 31 bundle leaves out.</summary>
    private static readonly Type[] Excluded =
    [
        typeof(Markdig.Extensions.GenericAttributes.GenericAttributesExtension),
        typeof(Markdig.Extensions.CustomContainers.CustomContainerExtension),
        typeof(Markdig.Extensions.MediaLinks.MediaLinkExtension),
        typeof(Markdig.Extensions.Mathematics.MathExtension),
        typeof(Markdig.Extensions.Diagrams.DiagramExtension),
        typeof(Markdig.Extensions.Tables.GridTableExtension),
    ];

    private const string Attributes = "# Plan {#id .class key=value}\n\nSeen by Dr A. Sample {onclick=x}";

    [Fact]
    public void For_BelowThirtyOne_IsTheUnchangedPipeline()
    {
        Assert.Same(ConsultMarkdown.Pipeline, ConsultMarkdown.For(30));
        Assert.Same(ConsultMarkdown.Pipeline, ConsultMarkdown.For(5));
        Assert.Same(ConsultMarkdown.Pipeline31, ConsultMarkdown.For(31));
        Assert.Same(ConsultMarkdown.Pipeline31, ConsultMarkdown.For(32));
    }

    /// <summary>#1543 (ruling 8, D16): <see cref="Pipeline31"/>'s extensions less the library's own, <see cref="ConsultLinkSchemes"/>, its last.</summary>
    private static IEnumerable<IMarkdownExtension> MarkdigsExtensions31()
    {
        Assert.IsType<ConsultLinkSchemes>(ConsultMarkdown.Pipeline31.Extensions[^1]);
        return ConsultMarkdown.Pipeline31.Extensions.Where(e => e is not ConsultLinkSchemes);
    }

    [Fact]
    public void Pipeline31_IsTheAdvancedBundleLessTheExcluded()
    {
        // Pins Markdig 0.41.3's UseAdvancedExtensions: an extension added to or removed from it fails here.
        var advanced = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build().Extensions.Select(e => e.GetType());

        Assert.Equal(advanced.Except(Excluded), MarkdigsExtensions31().Select(e => e.GetType()));
        var emphasis = ConsultMarkdown.Pipeline31.Extensions.OfType<Markdig.Extensions.EmphasisExtras.EmphasisExtraExtension>().Single();
        Assert.Equal(
            Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Superscript | Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Inserted,
            emphasis.Options);
    }

    [Fact]
    public void Pipeline31_IsTheExtensionsTheContractNames()
    {
        var markdig = V31Source.V31Contract.Json("constants.json")["markdig"]!;
        var names = MarkdigsExtensions31().Select(e => e.GetType().Name[..^"Extension".Length]).ToList();

        Assert.Equal(names, ConsultMarkdown.Extensions31);
        Assert.Equal(names, markdig["extensions"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["Superscript", "Inserted"], markdig["emphasisExtras"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(markdig["html"]!.GetValue<bool>());
        var informational = typeof(Markdown).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        Assert.Equal(informational.Split('+')[0], markdig["version"]!.GetValue<string>());
    }

    [Fact]
    public void Pipeline31_ASingleLineEnding_IsNoBreak()
    {
        Assert.Equal("<p>first\nsecond</p>\n", Markdown.ToHtml("first\nsecond", ConsultMarkdown.Pipeline31));
    }

    [Fact]
    public void Pipeline31_Html_IsText()
    {
        var html = Markdown.ToHtml("a <b>bold</b> claim\n\n<div onclick=\"x\">Margaret Okafor</div>", ConsultMarkdown.Pipeline31);

        Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("&lt;div onclick=&quot;x&quot;&gt;", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<div", html);
    }

    [Fact]
    public void Pipeline31_GenericAttributes_AreText()
    {
        var html = Markdown.ToHtml(Attributes, ConsultMarkdown.Pipeline31);

        Assert.Contains("{#id .class key=value}</h1>", html);
        Assert.Contains("<p>Seen by Dr A. Sample {onclick=x}</p>", html);
        Assert.DoesNotContain("onclick=\"", html);
        Assert.DoesNotContain("class=\"class\"", html);
        Assert.DoesNotContain("key=\"", html);
    }

    [Fact]
    public void TheAdvancedBundle_WritesAnyAttributeName()
    {
        // The evidence for leaving GenericAttributes out (#1510 item 2): with HTML off, the advanced bundle still writes
        // whatever attribute the text names, an event handler included.
        var html = Markdown.ToHtml(Attributes, new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build());

        Assert.Equal("<h1 id=\"id\" class=\"class\" key=\"value\">Plan</h1>\n<p onclick=\"x\">Seen by Dr A. Sample </p>\n", html);
    }

    // #1543 (ruling 8, D16): a link, an image or an autolink is written only when its scheme is http, https or mailto.

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.org/a")]
    [InlineData("HTTP://example.org")]
    [InlineData("mailto:clinic@example.org")]
    [InlineData("/plan")]
    [InlineData("plan.md")]
    [InlineData("#plan")]
    [InlineData("//example.org/x")]
    [InlineData("?q=1")]
    public void Writes_AllowsHttpHttpsMailtoAndNoScheme(string? url)
    {
        Assert.True(ConsultLinkSchemes.Writes(url));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:x")]
    [InlineData("java\tscript:x")]
    [InlineData("java\nscript:x")]
    [InlineData(" javascript:x")]
    [InlineData("\u0001javascript:x")]
    [InlineData("data:text/html,x")]
    [InlineData("vbscript:x")]
    [InlineData("file:///x")]
    [InlineData("tel:5550100")]
    [InlineData("ftp://x")]
    [InlineData("C:x")]
    public void Writes_RefusesEveryOtherScheme(string url)
    {
        Assert.False(ConsultLinkSchemes.Writes(url));
    }

    [Fact]
    public void TheAllowedSchemes_AreTheContracts()
    {
        var markdig = V31Source.V31Contract.Json("constants.json")["markdig"]!;

        Assert.Equal(["http", "https", "mailto"], ConsultLinkSchemes.Allowed);
        Assert.Equal(ConsultLinkSchemes.Allowed, markdig["linkSchemes"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void BothPipelines_EndWithTheLinkSchemes()
    {
        Assert.IsType<ConsultLinkSchemes>(ConsultMarkdown.Pipeline.Extensions[^1]);
        Assert.IsType<ConsultLinkSchemes>(ConsultMarkdown.Pipeline31.Extensions[^1]);
    }

    public static TheoryData<string, bool> NestedRefused => new()
    {
        { "![a [b ![c](data:z)](javascript:y)](data:x)", false },
        { "[a ![b](data:y)](javascript:x)", false },
        { "![a ![b](data:y)](data:x)", false },
        { "[a ![b](data:y) c](javascript:x) d", false },
        { "[a ![b](data:y) c](javascript:x) d", true },
    };

    [Theory]
    [MemberData(nameof(NestedRefused))]
    public void ANestedRefusedLinkOrImage_LeavesNoRefusedUrlInTheDocument(string markdown, bool is31)
    {
        var document = Markdown.Parse(markdown, is31 ? ConsultMarkdown.Pipeline31 : ConsultMarkdown.Pipeline);

        foreach (var node in document.Descendants())
        {
            if (node is Markdig.Syntax.Inlines.LinkInline link)
            {
                Assert.True(ConsultLinkSchemes.Writes(link.Url), link.Url);
            }

            Assert.False(node is Markdig.Syntax.Inlines.AutolinkInline auto && !ConsultLinkSchemes.Writes(auto.Url));
        }

        var html = Markdown.ToHtml(markdown, is31 ? ConsultMarkdown.Pipeline31 : ConsultMarkdown.Pipeline);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("href=", html);
        Assert.DoesNotContain("javascript", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAllowedLinkAroundARefusedImage_Stays_AndAnAllowedImageInARefusedLink_Stays(bool is31)
    {
        var pipeline = is31 ? ConsultMarkdown.Pipeline31 : ConsultMarkdown.Pipeline;

        var around = Markdown.ToHtml("[see ![x](data:y)](https://example.org/a)", pipeline);
        Assert.Contains("<a href=\"https://example.org/a\">see x</a>", around);

        var inside = Markdown.ToHtml("[a ![b](https://example.org/i.png) c](javascript:x) d", pipeline);
        Assert.Contains("<img src=\"https://example.org/i.png\"", inside);
        Assert.DoesNotContain("href=", inside);
    }

    [Fact]
    public void AnImageWithAnotherScheme_IsItsAltTextsInlines_AndPlainIsItsText()
    {
        // The alt text's inlines take the image's place as they are; plain text reads the same characters.
        Assert.Equal("<p>Seen: <strong>the scan</strong></p>\n", Markdown.ToHtml("Seen: ![**the scan**](data:text/html,x)", ConsultMarkdown.Pipeline31));
        Assert.Equal("Seen: the scan\n", Markdown.ToPlainText("Seen: ![**the scan**](data:text/html,x)", ConsultMarkdown.Pipeline31));
    }
}
