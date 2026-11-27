using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1543 (ruling 2, D7, as the owner amended it on 2026-11-27): a <c>markdown_block</c> read stands alone on its line at 31,
/// so where a 30 body has one with text beside it, the migration separates it from that text by a blank line, on both
/// sides, and drops nothing; a line of text next to a line holding only a block read gets a blank line too, never a
/// <c>\</c>. One notice per file (A10-093). Where a blank line would break the read's line out of its Markdown block (the
/// text before it starts a list item, a quote or a heading; the text after it would start one, or indented code), nothing
/// on the line is moved and it is a hand edit (A10-094). A macro body is where 30 let a body read the profile, so most cases are
/// example-macros' house_style macro with its one line changed. Content is fictional (Margaret Okafor, Dr A. Sample,
/// Sample Oncology Clinic).
/// </summary>
public class MigrateBlockReadsTests
{
    private const string HouseStyle = "macros/house_style.md";

    private const string Reviewed = "Reviewed against the house style.";

    /// <summary>example-macros with house_style's line written as <paramref name="line"/>, migrated.</summary>
    private static Migration31Result WithHouseStyle(string line) =>
        MigrateNodesTests.Migrated("example-macros", _ => { }, files => files[HouseStyle] = files[HouseStyle].Replace(Reviewed, line, StringComparison.Ordinal));

    /// <summary>house_style's 31 body as the package migrates with its line as given, that line written as <paramref name="line"/>.</summary>
    private static string Given(string line) =>
        MigrateNodesTests.Md(MigrateNodesTests.Migrated("example-macros"), HouseStyle).Body.Replace(Reviewed, line, StringComparison.Ordinal);

    private static string Notice(string file, params string[] reads) => MigrationSentences.BlockReadsMoved(file, reads);

    [Fact]
    public void TextBeforeTheSignature_IsFollowedByABlankLine_AndTheReadStartsItsOwnLine()
    {
        var result = WithHouseStyle("Regards, {{ profile.signature }}");

        // Nothing is dropped: the line is the same but for the blank line before the read, and no \ is written.
        var body = MigrateNodesTests.Md(result, HouseStyle).Body;
        Assert.Equal(Given("Regards, \n\n{{ profile.signature }}"), body);
        Assert.DoesNotContain("\\", body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, result.MovedReads);
        Assert.Equal(["{{ profile.signature }}"], result.SeparatedReads[HouseStyle]);
        const string Sentence = "In 'macros/house_style.md', the read profile.signature now starts its own line, after a blank line: at specVersion 31 a markdown_block read stands alone on its line.";
        Assert.Equal(Sentence, Notice(HouseStyle, "profile.signature"));
        Assert.Single(result.Notices, n => n == Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A10-093", Sentence, ""));
        Assert.DoesNotContain(result.HandEdits, e => e.Contains(HouseStyle, StringComparison.Ordinal));
        Assert.False(result.HardBreaks.ContainsKey(HouseStyle));
        Assert.Empty(Judged(result, HouseStyle));

        // The fixture as given separates nothing and says nothing.
        var given = MigrateNodesTests.Migrated("example-macros");
        Assert.Empty(given.MovedReads);
        Assert.Empty(given.SeparatedReads);
        Assert.DoesNotContain(given.Notices, n => n.Contains("after a blank line", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoReadsInOneFile_AreOneNotice()
    {
        var result = WithHouseStyle("Regards, {{ profile.signature }}\n\nDraft: {{ input.consult_draft }}");

        Assert.Equal(Given("Regards, \n\n{{ profile.signature }}\n\nDraft: \n\n{{ input.consult_draft.content }}"), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 2 }, result.MovedReads);
        const string Sentence = "In 'macros/house_style.md', the reads profile.signature, input.consult_draft.content now start their own lines, after a blank line: at specVersion 31 a markdown_block read stands alone on its line.";
        Assert.Equal(Sentence, Notice(HouseStyle, "profile.signature", "input.consult_draft.content"));
        Assert.Single(result.Notices, n => n.StartsWith("In 'macros/house_style.md', the read", StringComparison.Ordinal));
        Assert.Contains(Sentence, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-093", Sentence, ""));
        Assert.Empty(Judged(result, HouseStyle));
    }

    [Fact]
    public void TextAfterTheRead_StartsItsOwnLine_AfterABlankLine()
    {
        var result = WithHouseStyle("{{ profile.signature }} (dictated)");

        var body = MigrateNodesTests.Md(result, HouseStyle).Body;
        Assert.Equal(Given("{{ profile.signature }}\n\n (dictated)"), body);
        Assert.DoesNotContain("\\", body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, result.MovedReads);
        Assert.Contains(Notice(HouseStyle, "profile.signature"), result.Notices);
        Assert.DoesNotContain(result.HandEdits, e => e.Contains(HouseStyle, StringComparison.Ordinal));
        Assert.False(result.HardBreaks.ContainsKey(HouseStyle));
        Assert.Empty(Judged(result, HouseStyle));
    }

    [Fact]
    public void TextOnBothSides_IsSeparatedOnBoth()
    {
        // edge-unset-object's intake_line reads a field of an object input (a 30 text field, markdown_block at 31) behind
        // the guard the migration writes: the guard's tags stay on the read's line, a blank line on each side.
        const string File = "snippets/intake_line.md";
        var result = MigrateNodesTests.Migrated("edge-unset-object");

        Assert.Equal(
            "Referred for: \n\n{{ if input.intake.value }}{{ input.intake.value.reason }}{{ end }}\n\n (from the intake form).",
            MigrateNodesTests.Md(result, File).Body.TrimEnd('\n'));
        Assert.Equal(new Dictionary<string, int> { [File] = 1 }, result.MovedReads);
        Assert.Equal(["{{ input.intake.value.reason }}"], result.SeparatedReads[File]);
        Assert.Contains(Notice(File, "input.intake.value.reason"), result.Notices);
        Assert.Empty(Judged(result, File));
    }

    [Fact]
    public void ALineOfTextAboveOrBelowALineOfOnlyABlockRead_GetsABlankLine_NotABackslash()
    {
        // Item 3 (Task A3's case): Sincerely, above a line of only the signature, and the clinic's line below it. Each line
        // ending beside the read's line becomes a blank line; the hard-breaks pass writes no \ there.
        var result = WithHouseStyle("Sincerely,\n{{ profile.signature }}\nSample Oncology Clinic");

        var body = MigrateNodesTests.Md(result, HouseStyle).Body;
        Assert.Equal(Given("Sincerely,\n\n{{ profile.signature }}\n\nSample Oncology Clinic"), body);
        Assert.DoesNotContain("\\", body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, result.MovedReads);
        Assert.Contains(Notice(HouseStyle, "profile.signature"), result.Notices);
        Assert.False(result.HardBreaks.ContainsKey(HouseStyle));
        Assert.Empty(Judged(result, HouseStyle));

        // A line above a line of only an inline read keeps A3's \.
        var name = WithHouseStyle("Sincerely,\n{{ profile.name }}");
        Assert.StartsWith("Sincerely,\\\n{{ profile.name }}", MigrateNodesTests.Md(name, HouseStyle).Body, StringComparison.Ordinal);
        Assert.Empty(name.MovedReads);
    }

    [Fact]
    public void ALineOfInlineReadsAboveABlockRead_GetsABlankLine()
    {
        // A line of only an inline read is a line of text: the blank line stands between it and the signature's line, and
        // the line above the name keeps its \.
        var result = WithHouseStyle("Dictated by\n{{ profile.name }}\n{{ profile.signature }}");

        Assert.Equal(Given("Dictated by\\\n{{ profile.name }}\n\n{{ profile.signature }}"), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, result.MovedReads);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, result.HardBreaks);
        Assert.Empty(Judged(result, HouseStyle));
    }

    /// <summary>A10-094's reason, as pinned: the read on its line at line 1 of house_style's body.</summary>
    private const string BreaksItsBlock =
        "the read profile.signature at line 1 stands in a line that a blank line would break out of its Markdown block (a list item, a quote, a heading or indented text), and at specVersion 31 a markdown_block read stands alone on its line; rewrite the line.";

    [Theory]
    [InlineData("- Seen: ")]
    [InlineData("> Signed: ")]
    [InlineData("# Plan: ")]
    [InlineData("1. See ")]
    [InlineData("- ")]
    [InlineData("- > ")]
    [InlineData("  * ")]
    [InlineData("2) ")]
    public void TextBeforeTheReadThatStartsABlock_IsAHandEdit(string before)
    {
        // Item 4, as the controller extended it for the owner (Task A5's review): a blank line before the read would leave
        // the read outside the list item, the quote or the heading its line starts, so nothing on the line is moved, the
        // line is a hand edit (A10-094), and publish refuses it (S05-066) until it is made.
        var line = before + "{{ profile.signature }}";
        var result = WithHouseStyle(line);

        Assert.Equal(Given(line), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Empty(result.MovedReads);
        Assert.Empty(result.SeparatedReads);
        Assert.DoesNotContain(result.Notices, n => n.Contains("after a blank line", StringComparison.Ordinal));
        var reason = MigrationSentences.ReadBreaksItsBlock("profile.signature", 1);
        Assert.Equal(BreaksItsBlock, reason);
        V31Contract.AssertSentence(new SourceRefusal("A10-094", reason, ""));
        var edit = Assert.Single(result.HandEdits, e => e.Contains(HouseStyle, StringComparison.Ordinal));
        Assert.Equal(MigrationSentences.HandEdit(HouseStyle, "body", reason), edit);
        Assert.Equal("Hand edit needed in macros/house_style.md, body: " + reason, edit);
        Assert.Contains(Judged(result, HouseStyle), r => r.Id == "S05-066");
    }

    [Theory]
    [InlineData("\t(dictated)")]
    [InlineData("    (dictated)")]
    [InlineData(" - continued")]
    [InlineData(" > quoted")]
    [InlineData(" ## Plan")]
    public void TextAfterTheReadThatWouldStartABlock_IsAHandEdit(string after)
    {
        // The text after the read, on its own line, would itself start a block: indented code (a tab, four or more
        // spaces), a list item, a quote or a heading. Nothing on the line is moved, the text before it included.
        var line = "Regards, {{ profile.signature }}" + after;
        var result = WithHouseStyle(line);

        Assert.Equal(Given(line), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Empty(result.MovedReads);
        var edit = Assert.Single(result.HandEdits, e => e.Contains(HouseStyle, StringComparison.Ordinal));
        Assert.Equal(MigrationSentences.HandEdit(HouseStyle, "body", BreaksItsBlock), edit);
        Assert.Contains(Judged(result, HouseStyle), r => r.Id == "S05-067");

        // With no text before, the same.
        var alone = WithHouseStyle("{{ profile.signature }}" + after);
        Assert.Empty(alone.MovedReads);
        Assert.Single(alone.HandEdits, e => e == MigrationSentences.HandEdit(HouseStyle, "body", BreaksItsBlock));
    }

    [Theory]
    [InlineData("Regards, {{ profile.signature }} (dictated)", "Regards, \n\n{{ profile.signature }}\n\n (dictated)")]
    [InlineData("Plan - see {{ profile.signature }}, then > below", "Plan - see \n\n{{ profile.signature }}\n\n, then > below")]
    [InlineData("-Seen {{ profile.signature }}   (dictated)", "-Seen \n\n{{ profile.signature }}\n\n   (dictated)")]
    [InlineData("#1 {{ profile.signature }} -x", "#1 \n\n{{ profile.signature }}\n\n -x")]
    public void OrdinaryTextOnBothSides_IsStillSeparated(string line, string separated)
    {
        // A marker inside the text, a - or # not followed by white space, three spaces before the text after: none starts
        // a block, so the read is separated on both sides and no hand edit is raised.
        var result = WithHouseStyle(line);

        Assert.Equal(Given(separated), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, result.MovedReads);
        Assert.DoesNotContain(result.HandEdits, e => e.Contains(HouseStyle, StringComparison.Ordinal));
        Assert.Empty(Judged(result, HouseStyle));
    }

    [Fact]
    public void ABlankLineAlreadyThere_IsLeft()
    {
        const string Line = "Sincerely,\n\n{{ profile.signature }}\n\nSample Oncology Clinic";
        var result = WithHouseStyle(Line);

        Assert.Equal(Given(Line), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Empty(result.MovedReads);
        Assert.DoesNotContain(result.Notices, n => n.Contains("after a blank line", StringComparison.Ordinal));
    }

    [Fact]
    public void MigratingTwice_WritesNothingMore()
    {
        // Trailing spaces before the read stay; the pass run twice separates the read once and says the file once.
        var m = MigrateHardBreaksTests.Passes("example-macros", files => files[HouseStyle] = files[HouseStyle].Replace(Reviewed, "Regards,   {{ profile.signature }} (dictated)\nSample Oncology Clinic", StringComparison.Ordinal));
        Migrate.BlockReads(m);
        var once = m.Sources[HouseStyle].Body;
        Migrate.BlockReads(m);
        Assert.Equal(once, m.Sources[HouseStyle].Body);
        Assert.Contains("Regards,   \n\n{{ profile.signature }}\n\n (dictated)\nSample Oncology Clinic", once, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, m.MovedReads);
        Assert.Single(m.Result([]).Notices, n => n.StartsWith("In 'macros/house_style.md', the read", StringComparison.Ordinal));

        // The hard-breaks pass then breaks the text's own line ending, and a second run of both writes nothing more.
        Migrate.HardBreaks(m);
        var broken = m.Sources[HouseStyle].Body;
        Assert.Contains("\n\n (dictated)\\\nSample Oncology Clinic", broken, StringComparison.Ordinal);
        Migrate.LineEndings(m);
        Assert.Equal(broken, m.Sources[HouseStyle].Body);
    }

    [Fact]
    public void ACrLfBody_IsSeparatedAsAnLfOne()
    {
        // The file is normalised first (part 01 § 2.4), so a \r\n body is separated as its \n one is.
        const string Lines = "Regards, {{ profile.signature }}\nDraft: {{ input.consult_draft }}";
        var lf = WithHouseStyle(Lines);
        var crlf = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files[HouseStyle] = files[HouseStyle]
            .Replace(Reviewed, Lines, StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(MigrateNodesTests.Md(lf, HouseStyle).Body, MigrateNodesTests.Md(crlf, HouseStyle).Body);
        Assert.DoesNotContain("\r", MigrateNodesTests.Md(crlf, HouseStyle).Body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 2 }, crlf.MovedReads);
    }

    [Fact]
    public void AnInlineRead_IsLeftWhereItIs()
    {
        // The profile's name is a string, an inline read: it stays inside its line, and nothing is said.
        const string Line = "Dictated by {{ profile.name }}; not yet reviewed.";
        var result = WithHouseStyle(Line);

        Assert.Equal(Given(Line), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Empty(result.MovedReads);
        Assert.DoesNotContain(result.Notices, n => n.Contains("after a blank line", StringComparison.Ordinal));
    }

    [Fact]
    public void APromptIsLeftAsWritten()
    {
        // example-macros' summary prompt reads the consult draft (a markdown_block at 31) inside a line: a prompt is not a
        // Markdown value (ruling 1), so it is not a site, and the read stays where it is.
        var result = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files["prompts/summary.md"] = files["prompts/summary.md"]
            .Replace("Consult draft:\n{{ input.consult_draft }}", "Consult draft: {{ input.consult_draft }} as written.", StringComparison.Ordinal));

        var prompt = MigrateNodesTests.Md(result, "nodes/summary.md").Body;
        Assert.Matches(@"Consult draft: \{\{ [^\n]*input\.consult_draft[^\n]* \}\} as written\.", prompt);
        Assert.Empty(result.MovedReads);
        Assert.DoesNotContain(result.Notices, n => n.Contains("after a blank line", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("edge-unset-object", "intake-unset", "deliverable.note", "Referred for: ")]
    [InlineData("edge-unruled-whitespace", "default", "deliverable.problems", "Seen at: ")]
    public async Task AnEmptyValue_LeavesNoBackslashInTheLetter(string package, string scenario, string deliverable, string text)
    {
        // The read's value is empty (an unset intake, an unset clinic): the composed letter holds the text before the read
        // and the blank lines, and no \ (a \ before an empty value was a literal one in the letter).
        var (_, run, _) = await EquivalenceFixture.Compared(package, scenario);

        var letter = run.Deliverables.Single(d => d.Key == deliverable).Text;
        Assert.StartsWith(text, letter, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", letter, StringComparison.Ordinal);
    }

    /// <summary>
    /// The S05-065, S05-066 and S05-067 the dialect says of <paramref name="file"/> in <paramref name="result"/> as migrated
    /// (its check after the reader, the types and the resolver, each asserted to accept: a package with other hand edits
    /// left is judged too).
    /// </summary>
    private static List<SourceRefusal> Judged(Migration31Result result, string file)
    {
        var upload = MigratedPackagesTests.Upload(result);
        var registry = V31Contract.PublishedTypes();
        var parsed = SourceParser.Parse(upload, registry.Names);
        Assert.True(parsed.Package != null, Said(parsed.Refusals));
        var types = SourceTypes.Check(parsed.Package!, registry);
        Assert.True(types.Types != null, Said(types.Refusals));
        var (resolveRefusals, resolution) = SourceResolve.Check(parsed.Package!, types.Types!);
        Assert.True(resolution != null, Said(resolveRefusals));
        var check = SourceTemplates.Check(parsed.Package!, types.Types!, resolution!, registry);
        return check.Refusals.Concat(check.Warnings)
            .Where(r => r.Path == file && r.Id is "S05-065" or "S05-066" or "S05-067")
            .ToList();
    }

    private static string Said(IEnumerable<SourceRefusal> refusals) => string.Join("\n", refusals.Select(r => $"{r.Id} {r.Path}: {r.Sentence}"));
}
