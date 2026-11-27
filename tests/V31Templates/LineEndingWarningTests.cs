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
/// reader sees (G-D8's sites: deliverables, rendered nodes, macros, snippets and renderers of a Markdown format), never in a
/// prompt, a part or a body only a prompt reads. It is a warning: the package publishes. On migrated packages the warnings
/// fall exactly where the migration said A10-092. Content is fictional (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
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
    public void ASnippetAPromptPlaces_DoesNotWarn()
    {
        // Only a prompt places it: a \ there would change what the model reads, and nothing shows it.
        var prompted = WithLines(Package(), "letterhead")
            .WithAgent("draft", "Write the letter.\n\n{{~ snippet.letterhead.content }}\n\n{{ node.plan.content }}\n", "\"markdown_block\"")
            .WithDeliverable("{{ node.draft.content }}\n");
        Assert.Empty(Warned(prompted));

        // A rendered node only a prompt reads, likewise.
        var read = Package().WithRendered("header", "Margaret Okafor\nSample Oncology Clinic\n", "\"markdown_block\"")
            .WithAgent("draft", "Write the letter.\n\n{{ node.header.content }}\n\n{{ node.plan.content }}\n", "\"markdown_block\"")
            .WithDeliverable("{{ node.draft.content }}\n");
        Assert.Empty(Warned(read));

        // Placed by the deliverable as well, it is shown, and warned (where the migration says A10-092).
        var shown = WithLines(Package(), "letterhead")
            .WithAgent("draft", "Write the letter.\n\n{{~ snippet.letterhead.content }}\n\n{{ node.plan.content }}\n", "\"markdown_block\"")
            .WithDeliverable("{{~ snippet.letterhead.content }}\n\n{{ node.draft.content }}\n");
        var warning = Assert.Single(Warned(shown));
        Assert.Equal(Sentence("Snippet 'letterhead'", 1), warning.Sentence);
        Assert.Equal("snippets/letterhead.md", warning.Path);
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
    public void EveryMigratedPackage_WarnsOnlyWhereA10_092Said(string package)
    {
        // A package with hand edits is checked with them made; one whose edits are not made (HandEditsTests.NotEdited)
        // as migrated.
        var result = MigratedPackagesTests.Migrated(package).HandEdits.Count == 0 || HandEditsTests.NotEdited.ContainsKey(package)
            ? MigratedPackagesTests.Migrated(package)
            : MigrationFixture.Edited(package);

        Assert.Equal(KeptForAPrompt(result), Warnings(result));

        // The files the migration itself writes get no \ from it, and hold nothing to warn of.
        var written = result.Files.Keys.Where(k => k.StartsWith("renderers/", StringComparison.Ordinal) && k.EndsWith(".md", StringComparison.Ordinal))
            .Concat(result.StripNodes.Keys.Select(k => "nodes/" + k["node.".Length..].Replace('.', '/') + ".md"))
            .Where(result.Files.ContainsKey)
            .ToList();
        Assert.All(written, path => Assert.Empty(MarkdownLineEndings.Find(FrontMatter.Split(result.Files[path])!.Body)));
    }

    [Fact]
    public void AMigratedBodyAPromptReadsAndADeliverableShows_IsWarnedWhereA10_092Said()
    {
        // G5's case: example-v12-constructs' patient_header made a Markdown body written line per line before the hard-breaks
        // pass; the tone prompt and the consult deliverable read it. The migration keeps its line endings and says A10-092;
        // publish warns on it, and nowhere else.
        var m = Passes("example-v12-constructs");
        m.Sources["nodes/patient_header.md"].Json["format"] = "markdown_block";
        m.Sources["nodes/patient_header.md"].Body = "Margaret Okafor\nSample Oncology Clinic";
        m.Sources["nodes/tone.md"].Body += "\n{{ node.patient_header.content }}";
        Migrate.HardBreaks(m);
        Migrate.Derived(m);
        var result = MigrationFixture.Overlay(m.Result([]), MigrationFixture.HandEditsOf("example-v12-constructs"));

        Assert.Equal(["nodes/patient_header.md"], KeptForAPrompt(result));
        Assert.Equal(["nodes/patient_header.md"], Warnings(result));
    }

    /// <summary>The files the migration said A10-092 of, in order.</summary>
    private static List<string> KeptForAPrompt(Migration31Result result)
    {
        const string Tail = "' is read by a prompt, so its single line endings are kept as written; at specVersion 31 they do not break where it is shown: end such a line with \\.";
        return result.Notices.Where(n => n.StartsWith('\'') && n.EndsWith(Tail, StringComparison.Ordinal))
            .Select(n => n[1..^Tail.Length]).Order(StringComparer.Ordinal).ToList();
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

    /// <summary><paramref name="package"/> migrated pass by pass up to the hard-breaks pass, as <c>MigrateHardBreaksTests</c> does.</summary>
    private static Migrated Passes(string package)
    {
        var (json, files) = ByteEquivalenceFixtures.ReadPackage(package);
        var (given, refusals) = Migrated.Read(json, files, MigrationFixture.Options(package));
        Assert.True(given is not null, string.Join("\n", refusals));
        var (m, renames) = Migrate.Renames.Apply(given!);
        Assert.True(m is not null, string.Join("\n", renames));
        Migrate.Layout(m!);
        Migrate.Inputs(m!);
        Migrate.Nodes(m!);
        Migrate.Templates(m!);
        Migrate.Macros(m!);
        Migrate.Checks(m!);
        Migrate.Deliverables(m!);
        Migrate.StripLoopItems(m!);
        return m!;
    }
}
