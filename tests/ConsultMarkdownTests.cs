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

    [Fact]
    public void Pipeline31_IsTheAdvancedBundleLessTheExcluded()
    {
        // Pins Markdig 0.41.3's UseAdvancedExtensions: an extension added to or removed from it fails here.
        var advanced = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build().Extensions.Select(e => e.GetType());

        Assert.Equal(advanced.Except(Excluded), ConsultMarkdown.Pipeline31.Extensions.Select(e => e.GetType()));
        var emphasis = ConsultMarkdown.Pipeline31.Extensions.OfType<Markdig.Extensions.EmphasisExtras.EmphasisExtraExtension>().Single();
        Assert.Equal(
            Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Superscript | Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Inserted,
            emphasis.Options);
    }

    [Fact]
    public void Pipeline31_IsTheExtensionsTheContractNames()
    {
        var markdig = V31Source.V31Contract.Json("constants.json")["markdig"]!;
        var names = ConsultMarkdown.Pipeline31.Extensions.Select(e => e.GetType().Name[..^"Extension".Length]).ToList();

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
}
