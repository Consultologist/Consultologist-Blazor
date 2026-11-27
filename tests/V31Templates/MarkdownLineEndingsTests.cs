using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31.Templates;
using Markdig;
using Markdig.Syntax;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D8): <see cref="MarkdownLineEndings.Find"/> names the line endings
/// of a template body that 31's pipeline would join (a single line ending inside a paragraph, which 30's pipeline broke),
/// and <see cref="MarkdownLineEndings.WithHardBreaks"/> writes a <c>\</c> before each. Content is fictional (Margaret
/// Okafor, Dr A. Sample, Sample Oncology Clinic).
/// </summary>
public class MarkdownLineEndingsTests
{
    [Fact]
    public void ASingleLineEndingInAParagraph_IsFound()
    {
        const string Body = "Margaret Okafor\n12 Sample Street\nSampletown";

        Assert.Equal([15, 32], MarkdownLineEndings.Find(Body));
        Assert.Equal([5], MarkdownLineEndings.Find("> one\n> two"));
        Assert.Equal([7], MarkdownLineEndings.Find("- first\n  second\n- third"));
    }

    [Fact]
    public void NotInAFenceATableOrAParagraphsLastLine()
    {
        Assert.Empty(MarkdownLineEndings.Find("```\nSeen by\nDr A. Sample\n```\n"));
        Assert.Empty(MarkdownLineEndings.Find("    Seen by\n    Dr A. Sample\n"));
        Assert.Empty(MarkdownLineEndings.Find("| Drug | Dose |\n|---|---|\n| A | 1 |\n| B | 2 |\n"));
        Assert.Empty(MarkdownLineEndings.Find("First paragraph.\n\nSecond paragraph.\n"));
    }

    [Fact]
    public void NotBeforeAListItemOrAHeading()
    {
        Assert.Empty(MarkdownLineEndings.Find("Plan:\n- rest\n- fluids\n"));
        Assert.Empty(MarkdownLineEndings.Find("Summary\n# Plan\nRest.\n"));
        Assert.Empty(MarkdownLineEndings.Find("Summary\n=======\n"));
    }

    [Fact]
    public void ALineEndingATildeRemoves_IsNotFound()
    {
        Assert.Empty(MarkdownLineEndings.Find("Dear {{ input.name ~}}\nthank you for seeing her."));
        Assert.Empty(MarkdownLineEndings.Find("Dear {{ input.name ~}}  \nthank you for seeing her."));

        // A comment closed by ~}} removes the line ending too, though it renders nothing (Scan lists no such tag).
        const string Comment = "Dear Dr A. Sample {{# salutation ~}}\nthank you for seeing her.";
        Assert.Empty(MarkdownLineEndings.Find(Comment));
        Assert.Equal((Comment, 0), MarkdownLineEndings.WithHardBreaks(Comment));
    }

    [Fact]
    public void ALineOfOnlyTags_BreaksNeitherSide()
    {
        Assert.Equal([3], MarkdownLineEndings.Find("One\nTwo\n{{ if input.followup.value }}\nThree\n{{ end }}\nFour"));
        Assert.Empty(MarkdownLineEndings.Find("Before\n  {{ snippet.closing.content }} {{ run.date }}\nAfter"));
        Assert.Empty(MarkdownLineEndings.Find("Before\n{{# a note to the author }}\nAfter"));
    }

    [Fact]
    public void ALineEndingInsideATag_IsNotFound()
    {
        const string Body = "Seen {{ input.name\n| string.strip }} today\nand again";

        Assert.Equal([Body.IndexOf(" today\n", StringComparison.Ordinal) + 6], MarkdownLineEndings.Find(Body));
    }

    [Fact]
    public void APipeInsideATag_DoesNotMakeATable()
    {
        // Read as written, the first two lines are a table's header and delimiter rows, whose line endings are no paragraph's.
        const string Body = "Seen {{ a | b }}\nRest | fluids\nby Dr A. Sample";
        const string Table = "Seen {{ a | b }}\n---|---\n\nby Dr A. Sample";

        Assert.Single(Markdown.Parse(Table, ConsultMarkdown.Pipeline31).Descendants<Markdig.Extensions.Tables.Table>());
        Assert.Equal([16, 30], MarkdownLineEndings.Find(Body));
        Assert.Equal([16], MarkdownLineEndings.Find(Table));
    }

    [Fact]
    public void ABackslashOrTwoSpaces_IsAlreadyABreak()
    {
        Assert.Empty(MarkdownLineEndings.Find("Margaret Okafor\\\n12 Sample Street  \nSampletown"));
        Assert.Equal([16], MarkdownLineEndings.Find("Margaret Okafor \n12 Sample Street"));
    }

    [Fact]
    public void WithHardBreaks_IsIdempotent()
    {
        var (once, count) = MarkdownLineEndings.WithHardBreaks("Margaret Okafor \n12 Sample Street\n{{ if x }}\nSampletown\n{{ end }}");

        Assert.Equal("Margaret Okafor \\\n12 Sample Street\n{{ if x }}\nSampletown\n{{ end }}", once);
        Assert.Equal(1, count);
        Assert.Equal((once, 0), MarkdownLineEndings.WithHardBreaks(once));
        Assert.Equal(("Dear Dr A. Sample", 0), MarkdownLineEndings.WithHardBreaks("Dear Dr A. Sample"));
    }

    /// <summary>Markdown Markdig's parse refuses as nested too deeply (as <c>PlainConversionTests.TooDeep</c>), each with line endings in it.</summary>
    public static TheoryData<string> TooDeep() =>
    [
        string.Concat(Enumerable.Repeat("> ", 200)) + "Margaret Okafor\n12 Sample Street",
        string.Concat(Enumerable.Range(0, 300).Select(i => new string(' ', i * 2) + "- level\n")),
        new string('[', 50_000) + "\nSampletown",
    ];

    [Theory]
    [MemberData(nameof(TooDeep))]
    public void MarkdownTooDeepToParse_HasNoHits_NeverAThrow(string body)
    {
        // a publish warning pass and a migration pass must not turn into a failure
        Assert.Empty(MarkdownLineEndings.Find(body));
        Assert.Equal((body, 0), MarkdownLineEndings.WithHardBreaks(body));
    }
}
