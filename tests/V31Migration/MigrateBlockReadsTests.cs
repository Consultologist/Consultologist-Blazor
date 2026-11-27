using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Migration;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.Api.Tests.V31Migration;

/// <summary>
/// #1543 (ruling 2, D7): a <c>markdown_block</c> read stands alone on its line at 31, so where a 30 body has one with text
/// beside it, the migration moves it onto a line of its own and drops nothing: the text before it ends with <c>\</c> (an
/// explicit hard break) and the read starts the next line; the text after it starts the next line after a plain line
/// ending. One notice per file (A10-093), no hand edit. A macro body is where 30 let a body read the profile, so most cases
/// are example-macros' house_style macro with its one line changed. Content is fictional (Margaret Okafor, Dr A. Sample).
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
    public void TextBeforeTheSignature_EndsWithABackslash_AndTheReadStartsTheNextLine()
    {
        var result = WithHouseStyle("Regards, {{ profile.signature }}");

        // Nothing is dropped: the line is the same but for \ and a line ending before the read.
        Assert.Equal(Given("Regards, \\\n{{ profile.signature }}"), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, result.MovedReads);
        const string Sentence = "In 'macros/house_style.md', the read profile.signature now stands alone on its line (after \\, an explicit hard break, where text stood before it): at specVersion 31 a markdown_block read stands alone on its line.";
        Assert.Equal(Sentence, Notice(HouseStyle, "profile.signature"));
        Assert.Single(result.Notices, n => n == Sentence);
        V31Contract.AssertSentence(new SourceRefusal("A10-093", Sentence, ""));
        Assert.DoesNotContain(result.HandEdits, e => e.Contains(HouseStyle, StringComparison.Ordinal));
        // The line before the read already ends with a hard break: the hard-breaks pass writes no second \.
        Assert.False(result.HardBreaks.ContainsKey(HouseStyle));
        Assert.Empty(Judged(result, HouseStyle));

        // The fixture as given moves nothing and says nothing.
        var given = MigrateNodesTests.Migrated("example-macros");
        Assert.Empty(given.MovedReads);
        Assert.DoesNotContain(given.Notices, n => n.Contains("now stand", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoReadsInOneFile_AreOneNotice()
    {
        var result = WithHouseStyle("Regards, {{ profile.signature }}\nDraft: {{ input.consult_draft }}");

        Assert.Equal(Given("Regards, \\\n{{ profile.signature }}\nDraft: \\\n{{ input.consult_draft.content }}"), MigrateNodesTests.Md(result, HouseStyle).Body);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 2 }, result.MovedReads);
        const string Sentence = "In 'macros/house_style.md', the reads profile.signature, input.consult_draft.content now stand alone on their lines (after \\, an explicit hard break, where text stood before them): at specVersion 31 a markdown_block read stands alone on its line.";
        Assert.Equal(Sentence, Notice(HouseStyle, "profile.signature", "input.consult_draft.content"));
        Assert.Single(result.Notices, n => n.StartsWith("In 'macros/house_style.md', the read", StringComparison.Ordinal));
        Assert.Contains(Sentence, result.Notices);
        V31Contract.AssertSentence(new SourceRefusal("A10-093", Sentence, ""));
        Assert.Empty(Judged(result, HouseStyle));
    }

    [Fact]
    public void TextAfterTheRead_StartsTheNextLine()
    {
        var result = WithHouseStyle("{{ profile.signature }} (dictated)");

        var body = MigrateNodesTests.Md(result, HouseStyle).Body;
        Assert.Equal(Given("{{ profile.signature }}\n (dictated)"), body);
        Assert.DoesNotContain("\\", body, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, result.MovedReads);
        Assert.Contains(Notice(HouseStyle, "profile.signature"), result.Notices);
        Assert.DoesNotContain(result.HandEdits, e => e.Contains(HouseStyle, StringComparison.Ordinal));
        Assert.False(result.HardBreaks.ContainsKey(HouseStyle));
        Assert.Empty(Judged(result, HouseStyle));
    }

    [Fact]
    public void TextOnBothSides_IsMovedOnBoth()
    {
        // edge-unset-object's intake_line reads a field of an object input (a 30 text field, markdown_block at 31) behind
        // the guard the migration writes: the guard's tags stay on the read's line, the text before ends with \, and the
        // text after starts the next line.
        const string File = "snippets/intake_line.md";
        var result = MigrateNodesTests.Migrated("edge-unset-object");

        Assert.Equal(
            "Referred for: \\\n{{ if input.intake.value }}{{ input.intake.value.reason }}{{ end }}\n (from the intake form).",
            MigrateNodesTests.Md(result, File).Body.TrimEnd('\n'));
        Assert.Equal(new Dictionary<string, int> { [File] = 1 }, result.MovedReads);
        Assert.Contains(Notice(File, "input.intake.value.reason"), result.Notices);
        Assert.Empty(Judged(result, File));
    }

    [Fact]
    public void MigratingTwice_MovesNothingMore()
    {
        // Review Focus 1: trailing spaces before the read stay before the \; the pass run twice moves the read once and
        // says the file once.
        var m = MigrateHardBreaksTests.Passes("example-macros", files => files[HouseStyle] = files[HouseStyle].Replace(Reviewed, "Regards,   {{ profile.signature }} (dictated)", StringComparison.Ordinal));
        Migrate.BlockReads(m);
        var once = m.Sources[HouseStyle].Body;
        Migrate.BlockReads(m);
        Assert.Equal(once, m.Sources[HouseStyle].Body);
        Assert.Contains("Regards,   \\\n{{ profile.signature }}\n (dictated)", once, StringComparison.Ordinal);
        Assert.Equal(new Dictionary<string, int> { [HouseStyle] = 1 }, m.MovedReads);
        Assert.Single(m.Result([]).Notices, n => n.StartsWith("In 'macros/house_style.md', the read", StringComparison.Ordinal));

        // A 30 source already in the moved form (a \ there, the text after on its own line) is migrated as written.
        var moved = WithHouseStyle("Regards,\\\n{{ profile.signature }}\n(dictated)");
        Assert.Equal(Given("Regards,\\\n{{ profile.signature }}\n(dictated)"), MigrateNodesTests.Md(moved, HouseStyle).Body);
        Assert.Empty(moved.MovedReads);
    }

    [Fact]
    public void ACrLfBody_IsMovedAsAnLfOne()
    {
        // Review Focus 1: the file is normalised first (part 01 § 2.4), so a \r\n body is moved as its \n one is.
        var lf = WithHouseStyle("Regards, {{ profile.signature }}\nDraft: {{ input.consult_draft }}");
        var crlf = MigrateNodesTests.Migrated("example-macros", _ => { }, files => files[HouseStyle] = files[HouseStyle]
            .Replace(Reviewed, "Regards, {{ profile.signature }}\nDraft: {{ input.consult_draft }}", StringComparison.Ordinal)
            .Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.Equal(MigrateNodesTests.Md(lf, HouseStyle).Body, MigrateNodesTests.Md(crlf, HouseStyle).Body);
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
        Assert.DoesNotContain(result.Notices, n => n.Contains("now stand", StringComparison.Ordinal));
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
        Assert.DoesNotContain(result.Notices, n => n.Contains("now stand", StringComparison.Ordinal));
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
