using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Migration;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Source.SourceFixture;
using static Consultologist.Api.Tests.V31Templates.TemplateFixture;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D11): a single line ending inside a paragraph is not a break at 31, so
/// publish warns (S05-065, W-3) once per line ending <see cref="MarkdownLineEndings.Find"/> names, in the Markdown bodies a
/// reader sees (G-D8's sites: deliverables, rendered nodes, macros, snippets and renderers of a Markdown format), a body a
/// prompt reads or places included (#1543, ruling 1), never in a prompt or a part. It is a warning: the package publishes. A
/// migrated package with no hand edit made is warned nowhere: the migration ends every such line with \. Content is
/// fictional (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
/// </summary>
public class LineEndingWarningTests
{
    private const string Lines = "Seen by Dr A. Sample\nat Sample Oncology Clinic.\n";

    private static string Sentence(string subject, int line) =>
        $"{subject} has a single line ending inside a paragraph at line {line}: a single line ending is not a break; end the line with \\ or leave a blank line.";

    /// <summary>The S05-065 warnings of <paramref name="files"/>, which the dialect accepts.</summary>
    private static List<SourceRefusal> Warned(Dictionary<string, byte[]> files)
    {
        var check = Check(files);
        Assert.True(check.Refusals.Count == 0, Said(check.Refusals));
        return check.Warnings.Where(w => w.Id == "S05-065").ToList();
    }

    /// <summary>A Markdown snippet <paramref name="id"/> written line per line.</summary>
    private static Dictionary<string, byte[]> WithLines(Dictionary<string, byte[]> files, string id) =>
        files.WithSnippet(id, "Margaret Okafor\nSample Oncology Clinic\n", "markdown_block");

    [Fact]
    public void ASingleLineEndingInAMarkdownDeliverable_Warns_AndPublishes()
    {
        var files = Package().WithDeliverable("# Consultation note\n\n" + Lines + "Reviewed with\nMargaret Okafor.\n\n{{ node.plan.content }}\n");

        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);

        Assert.True(refusals.Count == 0, Said(refusals));
        var warnings = compiled!.Warnings.Where(w => w.Id == "S05-065").ToList();
        // One paragraph of four lines: three line endings inside it, none at its last line.
        Assert.Equal([Sentence("Deliverable 'note'", 3), Sentence("Deliverable 'note'", 4), Sentence("Deliverable 'note'", 5)], warnings.Select(w => w.Sentence));
        Assert.All(warnings, w =>
        {
            Assert.Equal("deliverables/note.md", w.Path);
            V31Contract.AssertSentence(w);
        });
    }

    [Fact]
    public void TheLineAboveAnOutputAlone_Warns()
    {
        // #1543 (ruling 2, D2): a line above a line of only a read (the profile's name, an inline read) is warned, as the
        // migration writes its \ there; above a placement alone it is not.
        var files = Package().WithSnippet("tone", "Plainly.\n", "markdown_block")
            .WithDeliverable("Sincerely,\n{{ profile.name }}\n\nSeen by\n{{~ snippet.tone.content }}\n\n{{ node.plan.content }}\n");

        var warning = Assert.Single(Warned(files));
        Assert.Equal(Sentence("Deliverable 'note'", 1), warning.Sentence);
        Assert.Equal("deliverables/note.md", warning.Path);
        V31Contract.AssertSentence(warning);
    }

    [Fact]
    public void TheLineAboveABlockReadAlone_Warns()
    {
        // #1543 (ruling 2, D2): the closing is a markdown_block read, typed by the checker; the line above it is warned.
        var files = Package().WithDeliverable("Sincerely,\n{{ profile.closing }}\n\n{{ node.plan.content }}\n");

        var warning = Assert.Single(Warned(files));
        Assert.Equal(Sentence("Deliverable 'note'", 1), warning.Sentence);
        V31Contract.AssertSentence(warning);
    }

    [Fact]
    public void ALineOnEachSideOfABlockReadAlone_Warns()
    {
        // #1543 (the owner's ruling of 2026-11-27, item 3): a blank line goes on each side of a line holding only a block
        // read, so each single line ending beside it is warned: above it and below it.
        var files = Package().WithDeliverable("Sincerely,\n{{ profile.closing }}\nDr A. Sample\n\n{{ node.plan.content }}\n");

        Assert.Equal([Sentence("Deliverable 'note'", 1), Sentence("Deliverable 'note'", 2)], Warned(files).Select(w => w.Sentence));
        var below = Assert.Single(Warned(Package().WithDeliverable("{{ profile.closing }}\nDr A. Sample\n\n{{ node.plan.content }}\n")));
        Assert.Equal(Sentence("Deliverable 'note'", 1), below.Sentence);
        V31Contract.AssertSentence(below);
    }

    [Fact]
    public void ASingleLineEndingInAPrompt_DoesNotWarn()
    {
        var files = Package().WithAgent("draft", "Write the letter for Margaret Okafor.\nKeep it short.\n\n{{ node.plan.content }}\n", "\"markdown_block\"")
            .WithDeliverable("{{ node.draft.content }}\n");

        Assert.Empty(Warned(files));
    }

    [Fact]
    public void APlainTextDeliverable_DoesNotWarn()
    {
        // Minimal's deliverable is plain text; a plain-text snippet it places, likewise.
        var files = Minimal().WithSnippet("seen_by", "Seen by Dr A. Sample\nat Sample Oncology Clinic.\n")
            .With("deliverables/note.md", Md(Deliverable("Consultation note", Plain, "null", "hold"),
                Lines + "\n{{ snippet.seen_by.content }}\n\n{{ node.plan.content }}\n"));

        Assert.Empty(Warned(files));
    }

    [Theory]
    [InlineData("Seen by Dr A. Sample\\\nat Sample Oncology Clinic.\n")]
    [InlineData("Seen by Dr A. Sample  \nat Sample Oncology Clinic.\n")]
    [InlineData("Seen by Dr A. Sample\n\nat Sample Oncology Clinic.\n")]
    [InlineData("- Seen by Dr A. Sample\n- at Sample Oncology Clinic.\n")]
    public void ABackslashOrABlankLine_DoesNotWarn(string text)
    {
        var files = Package().WithDeliverable(text + "\n{{ node.plan.content }}\n");

        Assert.Empty(Warned(files));
    }

    [Fact]
    public void ASnippetAPromptPlaces_Warns()
    {
        // #1543 (ruling 1): a snippet only a prompt places is a Markdown body like any other, and warned.
        var prompted = WithLines(Package(), "letterhead")
            .WithAgent("draft", "Write the letter.\n\n{{~ snippet.letterhead.content }}\n\n{{ node.plan.content }}\n", "\"markdown_block\"")
            .WithDeliverable("{{ node.draft.content }}\n");
        var warning = Assert.Single(Warned(prompted));
        Assert.Equal(Sentence("Snippet 'letterhead'", 1), warning.Sentence);
        Assert.Equal("snippets/letterhead.md", warning.Path);
        V31Contract.AssertSentence(warning);
    }

    [Fact]
    public void ARenderedNodeOnlyAPromptReads_Warns()
    {
        var read = Package().WithRendered("header", "Margaret Okafor\nSample Oncology Clinic\n", "\"markdown_block\"")
            .WithAgent("draft", "Write the letter.\n\n{{ node.header.content }}\n\n{{ node.plan.content }}\n", "\"markdown_block\"")
            .WithDeliverable("{{ node.draft.content }}\n");
        var warning = Assert.Single(Warned(read));
        Assert.Equal(Sentence("Node 'header'", 1), warning.Sentence);
        Assert.Equal("nodes/header.md", warning.Path);
    }

    [Fact]
    public void AMacroAndARenderedNode_Warn()
    {
        var files = Package()
            .Listed("macros/index.json", "closing")
            .With("macros/closing.md", Md(
                "{ \"label\": \"Closing\", \"description\": null, \"if\": null, \"format\": \"markdown_block\", "
                + "\"instruction\": [\"End the letter with this:\", \"\", \"{{~ macro.closing.content }}\"] }",
                "With kind regards,\nDr A. Sample\n"))
            .WithAgent("draft", "Write the letter.\n\n{{~ macro.closing }}\n", "\"markdown_block\"")
            .WithRendered("header", "Margaret Okafor\nSample Oncology Clinic\n", "\"markdown_block\"")
            .WithRendered("plain_header", "Margaret Okafor\nSample Oncology Clinic\n")
            .WithDeliverable("{{ node.header.content }}\n\n{{ node.plain_header.content }}\n\n{{ node.draft.content }}\n");

        var warnings = Warned(files);

        Assert.Equal(
            [("macros/closing.md", Sentence("Macro 'closing'", 1)), ("nodes/header.md", Sentence("Node 'header'", 1))],
            warnings.Select(w => (w.Path, w.Sentence)));
    }

    [Theory]
    [MemberData(nameof(MigratedPackagesTests.Packages), MemberType = typeof(MigratedPackagesTests))]
    public void EveryMigratedPackage_WithNoHandEdit_HasNoLineEndingWarning(string package)
    {
        // #1543 (ruling 1, D1): the migration's sites are publish's and both read the line endings with one finder, so a
        // package as migrated, before any hand edit is made, is warned nowhere.
        var result = MigratedPackagesTests.Migrated(package);

        Assert.Empty(Warnings(result));

        // The files the migration itself writes get no \ from it, and hold nothing to warn of.
        var written = result.Files.Keys.Where(k => k.StartsWith("renderers/", StringComparison.Ordinal) && k.EndsWith(".md", StringComparison.Ordinal))
            .Concat(result.StripNodes.Keys.Select(k => "nodes/" + k["node.".Length..].Replace('.', '/') + ".md"))
            .Where(result.Files.ContainsKey)
            .ToList();
        Assert.All(written, path => Assert.Empty(MarkdownLineEndings.Find(FrontMatter.Split(result.Files[path])!.Body, _ => TagRole.Statement)));
    }

    /// <summary>
    /// The files the dialect warns S05-065 of, each once, in order: publish's warnings, or, for a package publish refuses (an
    /// edit not made), those of the dialect's check after the reader, the types and the resolver (each asserted to accept).
    /// </summary>
    private static List<string> Warnings(Migration31Result result)
    {
        var upload = MigratedPackagesTests.Upload(result);
        var registry = V31Contract.PublishedTypes();
        var (refusals, compiled) = CompiledPackage.Publish(upload, registry, CompiledGoldenTests.Fixed);
        IEnumerable<SourceRefusal> warnings;
        if (refusals.Count == 0)
        {
            warnings = compiled!.Warnings;
        }
        else
        {
            var parsed = SourceParser.Parse(upload, registry.Names);
            Assert.True(parsed.Package != null, Said(parsed.Refusals));
            var types = SourceTypes.Check(parsed.Package!, registry);
            Assert.True(types.Types != null, Said(types.Refusals));
            var (resolveRefusals, resolution) = SourceResolve.Check(parsed.Package!, types.Types!);
            Assert.True(resolution != null, Said(resolveRefusals));
            warnings = SourceTemplates.Check(parsed.Package!, types.Types!, resolution!, registry).Warnings;
        }

        return warnings.Where(w => w.Id == "S05-065").Select(w => w.Path).Distinct().Order(StringComparer.Ordinal).ToList();
    }
}
