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
/// bodies (deliverables, rendered nodes, macros, snippets), said once per file (A10-091). #1543 (ruling 1): a body a prompt
/// reads or places is one of them; a prompt and its parts are not Markdown values, and keep their line endings as written.
/// No migrated fixture writes such a body line per line, so each case is a fixture package with one changed. Content is fictional (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
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
    public void TheLineAboveTheProfilesName_GetsItsBackslash()
    {
        // #1543 (ruling 2, D2; the G-D8 gap): example-macros' house_style macro (a macro body is where 30 let a body read the
        // profile) written as a closing above the profile's name on a line of its own. 30 broke the line before the name; the
        // name is an inline read, so the line above it ends with \, said by A10-091.
        var result = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["macros/house_style.md"] = files["macros/house_style.md"].Replace(
            "Reviewed against the house style.", "Sincerely,\n{{ profile.name }}", StringComparison.Ordinal));

        Assert.StartsWith("Sincerely,\\\n{{ profile.name }}", MigrateNodesTests.Md(result, "macros/house_style.md").Body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { ["macros/house_style.md"] = 1 }, result.HardBreaks);
        const string Notice = "In 'macros/house_style.md', 1 line ending inside a paragraph ends with \\ (an explicit hard break), where specVersion 30 broke the line.";
        Assert.Single(result.Notices, n => n == Notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-091", Notice, ""));

        // sign_off's file is one the checker refuses as migrated (its instruction is a hand edit): its tags are not typed,
        // so they take the role that says least, and the line above the name is left as written.
        var refused = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["macros/sign_off.md"] = files["macros/sign_off.md"].Replace(
            "Dictated by {{ profile.name }}; not yet reviewed.", "Sincerely,\n{{ profile.name }}", StringComparison.Ordinal));
        Assert.StartsWith("Sincerely,\n{{ profile.name }}", MigrateNodesTests.Md(refused, "macros/sign_off.md").Body, StringComparison.Ordinal);
        Assert.Empty(refused.HardBreaks);
    }

    [Fact]
    public void ASnippetAPromptPlaces_EndsItsLinesWithABackslash()
    {
        // #1543 (ruling 1): example-macros' summary prompt places the snippet style, written here as two lines. A body a
        // prompt places is a site like any other: it gets its \, said by A10-091, and no notice says a prompt reads it.
        const string Style = "Write in the first person plural,\npast tense, without abbreviations the patient would not know.";
        var result = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["snippets/style.md"] = files["snippets/style.md"].Replace(
            "Write in the first person plural, past tense, without abbreviations the patient would not know.", Style, StringComparison.Ordinal));

        Assert.StartsWith("Write in the first person plural,\\\npast tense", MigrateNodesTests.Md(result, "snippets/style.md").Body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { ["snippets/style.md"] = 1 }, result.HardBreaks);
        const string Notice = "In 'snippets/style.md', 1 line ending inside a paragraph ends with \\ (an explicit hard break), where specVersion 30 broke the line.";
        Assert.Single(result.Notices, n => n == Notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-091", Notice, ""));
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("'snippets/style.md' is read by a prompt", StringComparison.Ordinal));
    }

    [Fact]
    public void APromptAndAPart_AreLeftAsWritten()
    {
        // example-macros' summary prompt is written line per line ("Task:" then its line), and example-conditional-documents'
        // billing part likewise: neither is a site, so the pass leaves each byte for byte, and says neither.
        var m = Passes("example-macros");
        var prompt = m.Sources["nodes/summary.md"].Body!;
        Assert.NotEmpty(MarkdownLineEndings.Find(prompt, _ => TagRole.Statement));
        Assert.Contains("Task:\nSummarise the consult draft", prompt, StringComparison.Ordinal);
        Migrate.HardBreaks(m);
        Assert.Equal(prompt, m.Sources["nodes/summary.md"].Body);
        Assert.False(m.HardBreaks.ContainsKey("nodes/summary.md"));
        Assert.DoesNotContain(m.Result([]).Notices, n => n.Contains("nodes/summary.md'", StringComparison.Ordinal));

        const string Section = "nodes/sections_consult_note_section/consult_note_section.md";
        var parts = Passes("example-conditional-documents");
        var header = parts.Sources[Section].Json["parts"]!.ToJsonString();
        Assert.Contains("\"Billing:\",\"End the section", header, StringComparison.Ordinal);
        var body = parts.Sources[Section].Body;
        Migrate.HardBreaks(parts);
        Assert.Equal(header, parts.Sources[Section].Json["parts"]!.ToJsonString());
        Assert.Equal(body, parts.Sources[Section].Body);
        Assert.Empty(parts.HardBreaks);
    }

    [Fact]
    public void TheLineAboveABlockRead_GetsNoBackslash()
    {
        // #1543 (the owner's ruling of 2026-11-27, item 3): beside a line holding only a block read (the signature) the
        // migration writes a blank line, never \: the hard-breaks pass alone leaves the line ending as it is.
        var m = Passes("example-macros", files => files["macros/house_style.md"] = files["macros/house_style.md"].Replace(
            "Reviewed against the house style.", "Sincerely,\n{{ profile.signature }}", StringComparison.Ordinal));
        var body = m.Sources["macros/house_style.md"].Body;
        Assert.Contains("Sincerely,\n{{ profile.signature }}", body, StringComparison.Ordinal);

        Migrate.HardBreaks(m);

        Assert.Equal(body, m.Sources["macros/house_style.md"].Body);
        Assert.False(m.HardBreaks.ContainsKey("macros/house_style.md"));
    }

    [Fact]
    public void OneLineEnding_SaysLineEnding_TwoSayLineEndings()
    {
        // #1543 (ruling 10): A10-091's count agrees with its noun.
        var one = MigrationSentences.ExplicitHardBreaks("snippets/letterhead.md", 1);
        var two = MigrationSentences.ExplicitHardBreaks("snippets/letterhead.md", 2);

        Assert.Equal("In 'snippets/letterhead.md', 1 line ending inside a paragraph ends with \\ (an explicit hard break), where specVersion 30 broke the line.", one);
        Assert.Equal("In 'snippets/letterhead.md', 2 line endings inside a paragraph end with \\ (an explicit hard break), where specVersion 30 broke the line.", two);
        V31Contract.AssertSentence(new SourceRefusal("A10-091", one, ""));
        V31Contract.AssertSentence(new SourceRefusal("A10-091", two, ""));

        // The contract takes the two forms, not a count against its noun.
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => V31Contract.AssertSentence(new SourceRefusal("A10-091", one.Replace("1 line ending inside a paragraph ends", "1 line endings inside a paragraph end", StringComparison.Ordinal), "")));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => V31Contract.AssertSentence(new SourceRefusal("A10-091", two.Replace("2 line endings inside a paragraph end", "2 line ending inside a paragraph ends", StringComparison.Ordinal), "")));
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
        Assert.Single(result.Notices, n => n.StartsWith("In 'macros/house_style.md', 1 line ending inside", StringComparison.Ordinal));
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
    public void ARenderedNodeAPromptReads_EndsItsLinesWithABackslash()
    {
        // The migration writes every rendered node plaintext, so example-v12-constructs' patient_header (which the consult
        // deliverable reads) is made a Markdown body written line per line before the pass, and the tone prompt reads it.
        // #1543 (ruling 1): read by a prompt or not, it gets its \, said by A10-091, and no notice says a prompt reads it.
        const string Header = "Margaret Okafor\nSample Oncology Clinic";
        const string Broken = "Margaret Okafor\\\nSample Oncology Clinic";
        const string Notice = "In 'nodes/patient_header.md', 1 line ending inside a paragraph ends with \\ (an explicit hard break), where specVersion 30 broke the line.";
        var m = Passes("example-v12-constructs");
        m.Sources["nodes/patient_header.md"].Json["format"] = "markdown_block";
        m.Sources["nodes/patient_header.md"].Body = Header;
        m.Sources["nodes/tone.md"].Body += "\n{{ node.patient_header.content }}";
        Migrate.HardBreaks(m);

        Assert.Equal(Broken, m.Sources["nodes/patient_header.md"].Body);
        Assert.Equal(new Dictionary<string, int> { ["nodes/patient_header.md"] = 1 }, m.HardBreaks);
        var result = m.Result([]);
        Assert.Single(result.Notices, n => n == Notice);
        V31Contract.AssertSentence(new SourceRefusal("A10-091", Notice, ""));
        Assert.DoesNotContain(result.Notices, n => n.StartsWith("'nodes/patient_header.md' is read by a prompt", StringComparison.Ordinal));

        // Read by the prompt alone (the deliverable reads it no more), likewise.
        var prompted = Passes("example-v12-constructs");
        prompted.Sources["nodes/patient_header.md"].Json["format"] = "markdown_block";
        prompted.Sources["nodes/patient_header.md"].Body = Header;
        prompted.Sources["nodes/tone.md"].Body += "\n{{ node.patient_header.content }}";
        prompted.Sources["deliverables/consult.md"].Body = prompted.Sources["deliverables/consult.md"].Body!.Replace("{{ node.patient_header.content | string.strip }}", "", StringComparison.Ordinal);
        Migrate.HardBreaks(prompted);
        Assert.Equal(Broken, prompted.Sources["nodes/patient_header.md"].Body);
        Assert.Single(prompted.Result([]).Notices, n => n == Notice);
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
    internal static Migrated Passes(string package, Action<Dictionary<string, string>>? change = null)
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
