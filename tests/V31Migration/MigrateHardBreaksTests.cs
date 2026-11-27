using System.Text.Json.Nodes;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D8, G-D10): 30's pipeline broke every single line ending, 31's reads one
/// inside a paragraph as a space, so the migration ends such a line with <c>\</c> (an explicit hard break) in the Markdown
/// bodies a reader sees (deliverables, rendered nodes, macros, snippets), said once per file (A10-091). A prompt, a part and a
/// body a prompt reads keep their line endings as written (a <c>\</c> would change what the model reads); one that is also
/// shown is said (A10-092). No migrated fixture writes a Markdown body line per line, so each case is a fixture package with
/// one changed. Content is fictional (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
/// </summary>
public class MigrateHardBreaksTests
{
    private const string Letterhead = "Riverside Clinic (fictional)";

    /// <summary>composer-macros' letterhead (a macro its deliverables place, a snippet at 31) written as an address.</summary>
    private const string Address = "Riverside Clinic (fictional)\n12 Sample Street\nSampletown";

    private const string BrokenAddress = "Riverside Clinic (fictional)\\\n12 Sample Street\\\nSampletown";

    private static void WithLetterhead(Dictionary<string, string> files, string text) =>
        files["macros/letterhead.md"] = files["macros/letterhead.md"].Replace(Letterhead, text, StringComparison.Ordinal);

    [Fact]
    public void ADeliverableWrittenLinePerLine_EndsItsLinesWithABackslash()
    {
        var result = MigrateNodesTests.Migrated("composer-macros", _ => { }, files => WithLetterhead(files, Address));

        Assert.Equal(BrokenAddress, MigrateNodesTests.Md(result, "snippets/letterhead.md").Body);
        Assert.Equal(new Dictionary<string, int> { ["snippets/letterhead.md"] = 2 }, result.HardBreaks);
        const string Notice = "In 'snippets/letterhead.md', 2 line endings inside a paragraph end with \\ (an explicit hard break), where specVersion 30 broke the line.";
        Assert.Single(result.Notices, n => n == Notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-091", Notice, ""));
        Assert.DoesNotContain(result.HandEdits, e => e.Contains("letterhead", StringComparison.Ordinal));

        // The fixture as given writes no Markdown body line per line: nothing is broken, nothing said.
        var given = MigrateNodesTests.Migrated("composer-macros");
        Assert.Empty(given.HardBreaks);
        Assert.DoesNotContain(given.Notices, n => n.Contains("(an explicit hard break)", StringComparison.Ordinal));
    }

    [Fact]
    public void APromptAndASnippetAPromptPlaces_AreLeftAsWritten()
    {
        // example-macros' summary prompt is written line per line ("Task:" then its line), and places the snippet style,
        // written here as two lines: neither is given a \, and neither is said.
        const string Style = "Write in the first person plural,\npast tense, without abbreviations the patient would not know.";
        var result = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["snippets/style.md"] = files["snippets/style.md"].Replace(
            "Write in the first person plural, past tense, without abbreviations the patient would not know.", Style, StringComparison.Ordinal));

        var prompt = MigrateNodesTests.Md(result, "nodes/summary.md").Body;
        Assert.NotEmpty(MarkdownLineEndings.Find(prompt));
        Assert.Contains("Task:\nSummarise the consult draft", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\\\n", prompt, StringComparison.Ordinal);
        Assert.StartsWith(Style + "\n", MigrateNodesTests.Md(result, "snippets/style.md").Body, StringComparison.Ordinal);
        Assert.Empty(result.HardBreaks);
        Assert.DoesNotContain(result.Notices, n => n.Contains("nodes/summary.md'", StringComparison.Ordinal) || n.Contains("snippets/style.md'", StringComparison.Ordinal));

        // A prompt's parts are never a site either (example-conditional-documents' billing part is written line per line).
        var parts = MigrateNodesTests.Migrated("example-conditional-documents");
        Assert.Empty(parts.HardBreaks);
    }

    [Fact]
    public void AMacroASnippetAPromptPlacesPlaces_EndsItsLinesWithABackslash()
    {
        // example-macros' summary prompt places the snippet style, which places macro.house_style: inside a prompt that read is
        // the macro's marker (part 05 § 3.1: a snippet reads what the template that places it reads), never its body. The
        // macro's body, written here as two lines, reaches the deliverable through the marker, so it is broken and said.
        const string Body = "Reviewed against\nthe house style.";
        var result = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["macros/house_style.md"] = files["macros/house_style.md"].Replace(
            "Reviewed against the house style.", Body, StringComparison.Ordinal));

        Assert.Contains("{{ macro.house_style }}", MigrateNodesTests.Md(result, "snippets/style.md").Body, StringComparison.Ordinal);
        Assert.StartsWith("Reviewed against\\\nthe house style.", MigrateNodesTests.Md(result, "macros/house_style.md").Body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { ["macros/house_style.md"] = 1 }, result.HardBreaks);
        Assert.Single(result.Notices, n => n.StartsWith("In 'macros/house_style.md', 1 line endings", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("'macros/house_style.md'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEditedBody_IsRecountedWhateverItsLineEndings()
    {
        // An operator's edit laid over the migrated files is recounted as the rule reads a break: a \ before \n or \r\n, after
        // an odd number of \ (an even number is escaped backslashes).
        var folder = Directory.CreateTempSubdirectory("hard-breaks-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "snippets"));
            File.WriteAllText(Path.Combine(folder, "snippets", "letterhead.md"),
                "---\n{\"label\":\"Letterhead\",\"description\":null,\"if\":null,\"format\":\"markdown_block\"}\n---\nRiverside Clinic (fictional)\\\r\n12 Sample Street\\\\\r\nSampletown\\\nSample County\n");
            var edited = MigrationFixture.Overlay(MigrateNodesTests.Migrated("composer-macros"), folder);
            Assert.Equal(2, edited.HardBreaks["snippets/letterhead.md"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void APlainTextDeliverable_IsLeftAsWritten()
    {
        // Every deliverable the migration writes is markdown_block, so the pass is run on one whose format is set to
        // plaintext before it, beside one left markdown_block.
        const string Lines = "Seen by\nDr A. Sample";
        var m = Passes("composer-macros");
        m.Sources["deliverables/prepended.md"].Json["format"] = "plaintext";
        m.Sources["deliverables/prepended.md"].Body = Lines;
        m.Sources["deliverables/appended.md"].Body = Lines;
        Migrate.HardBreaks(m);

        Assert.Equal(Lines, m.Sources["deliverables/prepended.md"].Body);
        Assert.Equal("Seen by\\\nDr A. Sample", m.Sources["deliverables/appended.md"].Body);
        Assert.Equal(new Dictionary<string, int> { ["deliverables/appended.md"] = 1 }, m.HardBreaks);

        // A plain-text macro the migration writes (composer-macros' new_visit_note: its placements are plain text) likewise.
        const string Note = "First visit\nto this clinic.";
        var result = MigrateNodesTests.Migrated("composer-macros", _ => { }, files => files["macros/new_visit_note.md"] = files["macros/new_visit_note.md"].Replace(
            "First visit to this clinic.", Note, StringComparison.Ordinal));
        Assert.Equal("plaintext", (string?)MigrateNodesTests.Md(result, "macros/new_visit_note.md").Header["format"]);
        Assert.StartsWith(Note, MigrateNodesTests.Md(result, "macros/new_visit_note.md").Body, StringComparison.Ordinal);
        Assert.Empty(result.HardBreaks);
    }

    [Fact]
    public void ARenderedNodeAPromptAndADeliverableRead_IsLeft_WithA10_092()
    {
        // The migration writes every rendered node plaintext, so example-v12-constructs' patient_header (which the consult
        // deliverable reads) is made a Markdown body written line per line before the pass, and the tone prompt reads it.
        const string Header = "Margaret Okafor\nSample Oncology Clinic";
        var m = Passes("example-v12-constructs");
        m.Sources["nodes/patient_header.md"].Json["format"] = "markdown_block";
        m.Sources["nodes/patient_header.md"].Body = Header;
        m.Sources["nodes/tone.md"].Body += "\n{{ node.patient_header.content }}";
        Migrate.HardBreaks(m);

        Assert.Equal(Header, m.Sources["nodes/patient_header.md"].Body);
        Assert.Empty(m.HardBreaks);
        var result = m.Result([]);
        const string Notice = "'nodes/patient_header.md' is read by a prompt, so its single line endings are kept as written; at specVersion 31 they do not break where it is shown: end such a line with \\.";
        Assert.Single(result.Notices, n => n == Notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-092", Notice, ""));

        // Read by the deliverable alone, it is broken; read by the prompt alone (the deliverable reads it no more), it is
        // left and not said, since nothing shows it.
        var shown = Passes("example-v12-constructs");
        shown.Sources["nodes/patient_header.md"].Json["format"] = "markdown_block";
        shown.Sources["nodes/patient_header.md"].Body = Header;
        Migrate.HardBreaks(shown);
        Assert.Equal("Margaret Okafor\\\nSample Oncology Clinic", shown.Sources["nodes/patient_header.md"].Body);

        var unshown = Passes("example-v12-constructs");
        unshown.Sources["nodes/patient_header.md"].Json["format"] = "markdown_block";
        unshown.Sources["nodes/patient_header.md"].Body = Header;
        unshown.Sources["nodes/tone.md"].Body += "\n{{ node.patient_header.content }}";
        unshown.Sources["deliverables/consult.md"].Body = unshown.Sources["deliverables/consult.md"].Body!.Replace("{{ node.patient_header.content | string.strip }}", "", StringComparison.Ordinal);
        Migrate.HardBreaks(unshown);
        Assert.Equal(Header, unshown.Sources["nodes/patient_header.md"].Body);
        Assert.DoesNotContain(unshown.Result([]).Notices, n => n.StartsWith("'nodes/patient_header.md'", StringComparison.Ordinal));
    }

    [Fact]
    public void MigratingTwice_WritesNoSecondBackslash()
    {
        // The pass run twice on one working set writes each \ once and says the file once.
        var m = Passes("composer-macros", files => WithLetterhead(files, Address));
        Migrate.HardBreaks(m);
        Migrate.HardBreaks(m);
        Assert.Equal(BrokenAddress, m.Sources["snippets/letterhead.md"].Body);
        Assert.Equal(new Dictionary<string, int> { ["snippets/letterhead.md"] = 2 }, m.HardBreaks);
        Assert.Single(m.Result([]).Notices, n => n.StartsWith("In 'snippets/letterhead.md'", StringComparison.Ordinal));

        // A 30 source that already ends its lines with \ (a hard break at 30 too) is migrated as written, and not said.
        var result = MigrateNodesTests.Migrated("composer-macros", _ => { }, files => WithLetterhead(files, BrokenAddress));
        Assert.Equal(BrokenAddress, MigrateNodesTests.Md(result, "snippets/letterhead.md").Body);
        Assert.Empty(result.HardBreaks);
    }

    /// <summary>
    /// <paramref name="package"/> (its 30 files changed by <paramref name="change"/>) migrated pass by pass up to the
    /// hard-breaks pass, the working set returned for a test to change before it.
    /// </summary>
    private static Migrated Passes(string package, Action<Dictionary<string, string>>? change = null)
    {
        var (json, read) = ByteEquivalenceFixtures.ReadPackage(package);
        var files = new Dictionary<string, string>(read, StringComparer.Ordinal);
        change?.Invoke(files);
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
