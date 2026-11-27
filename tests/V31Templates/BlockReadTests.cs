using Consultologist.Api.Tests.V31Migration;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1543 (ruling 2, D5, D6): a <c>markdown_block</c> read stands alone on its line. In a Markdown body site (a deliverable,
/// a <c>rendered</c> node, a macro body, a snippet, a package renderer of a Markdown format), a read typed
/// <c>markdown_block</c> with no renderer at its pipe's end, with text before it on its line, is refused (S05-066), and with
/// text after it (S05-067). Text is any character but a space or a tab outside every tag and comment, or an inline read; a
/// statement, a comment, a placement, a renderer-ended output or another block read is not. A prompt and a part are not
/// judged. The roles are the real checker's (<see cref="TagRoles"/>). Content is fictional (Margaret Okafor, Dr A. Sample,
/// Sample Oncology Clinic).
/// </summary>
public class BlockReadTests
{
    private const string Letter = "deliverables/letter.md";

    private static string Before(string subject, string read, int line) =>
        $"{subject} has text before {{{{ {read} }}}}, a markdown_block, at line {line}; a markdown_block read stands alone on its line.";

    private static string After(string subject, string read, int line) =>
        $"{subject} has text after {{{{ {read} }}}}, a markdown_block, at line {line}; a markdown_block read stands alone on its line.";

    /// <summary><see cref="TemplateFixture.Package"/> with the Markdown deliverable <c>letter</c>, <paramref name="body"/> its body.</summary>
    private static Dictionary<string, byte[]> WithLetter(string body) =>
        Package().Listed("deliverables/index.json", "letter")
            .With(Letter, Md(Deliverable("Letter", "\"markdown_block\"", "null", "hold"), body));

    /// <summary>The S05-066 and S05-067 refusals of <paramref name="files"/>, each held to its row.</summary>
    private static List<SourceRefusal> BlockReads(Dictionary<string, byte[]> files)
    {
        var found = Check(files).Refusals.Where(r => r.Id is "S05-066" or "S05-067").ToList();
        Assert.All(found, V31Contract.AssertSentence);
        return found;
    }

    /// <summary>Asserts the dialect refuses nothing of <paramref name="files"/>.</summary>
    private static void Accepted(Dictionary<string, byte[]> files)
    {
        var check = Check(files);
        Assert.True(check.Refusals.Count == 0, Said(check.Refusals));
    }

    [Fact]
    public void TextBeforeABlockRead_IsRefused_NamingTheFileTheReadAndTheLine()
    {
        var refusal = Single(Check(WithLetter("{{ node.plan.content }}\n\nSincerely, {{ profile.closing }}\n")));

        Assert.Equal("S05-066", refusal.Id);
        Assert.Equal(Letter, refusal.Path);
        Assert.Equal(Before("Deliverable 'letter'", "profile.closing", 3), refusal.Sentence);
    }

    [Fact]
    public void TextAfterABlockRead_IsRefused()
    {
        var refusal = Single(Check(WithLetter("{{ node.plan.content }}\n\n{{ profile.signature }} (signed)\n")));

        Assert.Equal("S05-067", refusal.Id);
        Assert.Equal(Letter, refusal.Path);
        Assert.Equal(After("Deliverable 'letter'", "profile.signature", 3), refusal.Sentence);
    }

    [Fact]
    public void TextOnBothSides_IsRefusedTwice()
    {
        Assert.Equal(["S05-066", "S05-067"], BlockReads(WithLetter("Signed: {{ profile.signature }}.\n")).Select(r => r.Id));
    }

    [Theory]
    [InlineData("Sincerely,\\\n{{ profile.closing }}\n")]
    [InlineData("Sincerely,\n\n  {{ profile.closing }}\n")]
    [InlineData("Sincerely,\n\t{{ profile.closing }}  \n")]
    [InlineData("Sincerely,\r\n{{ profile.closing }}\r\nDr A. Sample\r\n")]
    public void ABlockReadAloneOnItsLine_OrAfterAnIndent_IsAccepted(string body)
    {
        Accepted(WithLetter(body));
    }

    [Theory]
    [InlineData("- {{ node.plan.content }}\n")]
    [InlineData("> {{ node.plan.content }}\n")]
    [InlineData("**Plan:** {{ node.plan.content }}\n")]
    public void ABlockReadBehindAListMarkerAQuoteMarkerOrBoldText_IsRefused(string body)
    {
        // Review Focus 2: a Markdown marker is text, and the read is refused; nothing throws.
        var refusal = Single(Check(Package().WithAgent("draft", "Write the letter.\n", "\"markdown_block\"")
            .WithDeliverable(body.Replace("node.plan", "node.draft", StringComparison.Ordinal))));

        Assert.Equal(Before("Deliverable 'note'", "node.draft.content", 1), refusal.Sentence);
    }

    [Theory]
    [InlineData("{{ profile.name }}{{ profile.closing }}\n", "S05-066")]
    [InlineData("{{ profile.closing }} {{ profile.name }}\n", "S05-067")]
    public void AnInlineReadBesideABlockRead_IsText(string line, string id)
    {
        var refusal = Single(Check(WithLetter("{{ node.plan.content }}\n\n" + line)));

        Assert.Equal(id, refusal.Id);
        Assert.Equal(id == "S05-066" ? Before("Deliverable 'letter'", "profile.closing", 3) : After("Deliverable 'letter'", "profile.closing", 3), refusal.Sentence);
    }

    [Fact]
    public void AStatementAPlacementOrARendererBesideIt_IsNotText()
    {
        // D5's two accepted examples: the signature renderer's body ({{ value }}{{ end }}), and a block read beside the
        // signature piped into it. A comment, a placement and another block read beside it are not text either.
        var files = Package()
            .WithAgent("draft", "Write the letter.\n", "\"markdown_block\"")
            .WithSnippet("tone", "Plainly.\n", "markdown_block")
            .WithRenderer("signature_after_blank_line", "\"markdown_block\"",
                "{{ if value != null && (value | string.strip) != \"\" }}\n\n{{ value }}{{ end }}")
            .WithDeliverable("{{ node.draft.content | string.strip }}{{ profile.signature | renderer.signature_after_blank_line }}\n\n"
                + "{{# the closing }}{{ profile.closing }}{{ snippet.tone.content }}\n\n"
                + "{{ node.draft.content }} {{ profile.closing }}\n");

        Assert.Empty(BlockReads(files));
        Accepted(files);
    }

    [Fact]
    public void ARendererMidPipe_WithABuiltInAtTheEnd_IsABlockRead()
    {
        // #1543 (D2, D5): a renderer's role is an output whose pipe ends in one; a built-in step after it gives the role of
        // the final value's type, here a markdown_block read, so text before it is refused.
        var files = WithLetter("{{ node.plan.content }}\n\nSincerely, {{ profile.signature | renderer.signature | string.strip }}\n")
            .WithRenderer("signature", "\"markdown_block\"", "{{ value }}\n");

        var refusal = Assert.Single(BlockReads(files));
        Assert.Equal(Before("Deliverable 'letter'", "profile.signature", 3), refusal.Sentence);

        // Ended by the renderer, the same read is a renderer's, and only warned (S05-061).
        var ended = WithLetter("{{ node.plan.content }}\n\nSincerely, {{ profile.signature | renderer.signature }}\n")
            .WithRenderer("signature", "\"markdown_block\"", "{{ value }}\n");
        Assert.Empty(BlockReads(ended));
        Assert.Contains(Check(ended).Warnings, w => w.Id == "S05-061");
    }

    [Fact]
    public void AnInlineReadInsideALine_IsAccepted()
    {
        var files = Package()
            .WithAgent("headline", "Write a one-line headline.\n", "\"markdown_inline\"")
            .WithDeliverable("{{ node.plan.content }}\n\nHeadline: {{ node.headline.content }}\n");

        Accepted(files);
    }

    [Fact]
    public void ABlockReadInAPromptOrAPart_IsNotJudged()
    {
        var files = Package()
            .WithAgent("draft", "Draft: {{ input.consult_draft.content }} for Margaret Okafor.\n\n{{ part.brief }}\n", "\"markdown_block\"")
            .WithJson("nodes/draft.md", h => h["parts"] = System.Text.Json.Nodes.JsonNode.Parse(
                "{ \"brief\": { \"if\": null, \"text\": [\"Plan: {{ node.plan.content }} in brief.\"] } }"))
            .WithDeliverable("{{ node.draft.content }}\n");

        Assert.Empty(BlockReads(files));
        Accepted(files);
    }

    /// <summary>One package per site, each with one block read with text before it: the subject, the file and the line.</summary>
    public static TheoryData<string, string, string, int> Sites() => new()
    {
        { "deliverable", "Deliverable 'letter'", Letter, 3 },
        { "rendered", "Node 'header'", "nodes/header.md", 1 },
        { "macro", "Macro 'closing'", "macros/closing.md", 2 },
        { "snippet", "Snippet 'sign_off'", "snippets/sign_off.md", 1 },
        { "renderer", "Renderer 'signed'", "renderers/signed.md", 1 },
    };

    [Theory]
    [MemberData(nameof(Sites))]
    public void EachSite_IsJudged(string site, string subject, string path, int line)
    {
        var files = site switch
        {
            "deliverable" => WithLetter("{{ node.plan.content }}\n\nSincerely, {{ profile.closing }}\n"),
            "rendered" => Package()
                .WithAgent("draft", "Write the letter.\n", "\"markdown_block\"")
                .WithRendered("header", "Draft: {{ node.draft.content }}\n", "\"markdown_block\"")
                .WithDeliverable("{{ node.header.content }}\n"),
            "macro" => Package()
                .Listed("macros/index.json", "closing")
                .With("macros/closing.md", Md(
                    "{ \"label\": \"Closing\", \"description\": null, \"if\": null, \"format\": \"markdown_block\", "
                    + "\"instruction\": [\"End the letter with this:\", \"\", \"{{~ macro.closing.content }}\"] }",
                    "With kind regards,\\\nYours, {{ profile.closing }}\n"))
                .WithAgent("draft", "Write the letter.\n\n{{~ macro.closing }}\n", "\"markdown_block\"")
                .WithDeliverable("{{ node.draft.content }}\n"),
            "snippet" => Package()
                .WithSnippet("sign_off", "Sincerely, {{ profile.closing }}\n", "markdown_block")
                .WithDeliverable("{{ node.plan.content }}\n\n{{~ snippet.sign_off.content }}\n"),
            _ => Package()
                .WithRenderer("signed", "\"markdown_block\"", "Signed: {{ value }}\n")
                .WithDeliverable("{{ node.plan.content }}\n\n{{ profile.signature | renderer.signed }}\n"),
        };

        var refusal = Single(Check(files));

        Assert.Equal("S05-066", refusal.Id);
        Assert.Equal(path, refusal.Path);
        Assert.Equal(Before(subject, site switch { "rendered" => "node.draft.content", "renderer" => "value", _ => "profile.closing" }, line), refusal.Sentence);
    }

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.Packages), MemberType = typeof(MigratedPackagesTests))]
    public void EveryMigratedPackage_WithNoHandEdit_IsNotRefused(string package)
    {
        // A package as migrated, before any hand edit is made, has no block read with text beside it: the migration moves a
        // read with text before it to its own line (Task A5, D7), and says a read with text after it as a hand edit.
        var result = MigratedPackagesTests.Migrated(package);
        var upload = MigratedPackagesTests.Upload(result);
        var registry = V31Contract.PublishedTypes();
        var parsed = SourceParser.Parse(upload, registry.Names);
        Assert.True(parsed.Package != null, Said(parsed.Refusals));
        var types = SourceTypes.Check(parsed.Package!, registry);
        Assert.True(types.Types != null, Said(types.Refusals));
        var (resolveRefusals, resolution) = SourceResolve.Check(parsed.Package!, types.Types!);
        Assert.True(resolution != null, Said(resolveRefusals));

        var refused = SourceTemplates.Check(parsed.Package!, types.Types!, resolution!, registry).Refusals
            .Where(r => r.Id is "S05-066" or "S05-067").ToList();

        Assert.True(refused.Count == 0, Said(refused));
    }
}
