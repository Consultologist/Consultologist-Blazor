using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31.Templates;
using Markdig;
using Markdig.Syntax;

namespace Consultologist.Api.Tests.V31Templates;

/// <summary>
/// #1510 (the owner's third fix-first group, item 4; G-D8): <see cref="MarkdownLineEndings.Find"/> names the line endings
/// of a template body that 31's pipeline would join (a single line ending inside a paragraph, which 30's pipeline broke),
/// and <see cref="MarkdownLineEndings.WithHardBreaks"/> writes a <c>\</c> before each. #1543 (ruling 2, D2): a line beside
/// a line of only tags is judged by the tags' roles, which the callers take from the checker; here a small table gives them.
/// Content is fictional (Margaret Okafor, Dr A. Sample, Sample Oncology Clinic).
/// </summary>
public class MarkdownLineEndingsTests
{
    /// <summary>
    /// A tag's role by its text: <c>if</c>, <c>for</c>, <c>else</c>, <c>end</c> and an assignment are statements, a
    /// snippet, part or macro a placement, a pipe into <c>renderer.*</c> a renderer, the profile's signature or closing a
    /// block read, any other output an inline read.
    /// </summary>
    private static TagRole RoleOf(string tag)
    {
        var code = tag.Trim('{', '}', '~', '-', ' ');
        if (code.Split(' ')[0] is "if" or "else" or "for" or "end" || code.Contains(" = ", StringComparison.Ordinal))
            return TagRole.Statement;
        if (code.Contains("renderer.", StringComparison.Ordinal))
            return TagRole.Renderer;
        if (code.Split('.')[0] is "snippet" or "part" or "macro")
            return TagRole.Placement;
        return code is "profile.signature" or "profile.closing" ? TagRole.BlockRead : TagRole.InlineRead;
    }

    /// <summary>The finder with the roles of <see cref="RoleOf"/>.</summary>
    private static IReadOnlyList<int> Find(string body) =>
        MarkdownLineEndings.Find(body, t => RoleOf(body[t.Start..t.End]));

    private static (string Text, int Count) WithHardBreaks(string body) =>
        MarkdownLineEndings.WithHardBreaks(body, t => RoleOf(body[t.Start..t.End]));

    [Fact]
    public void ASingleLineEndingInAParagraph_IsFound()
    {
        const string Body = "Margaret Okafor\n12 Sample Street\nSampletown";

        Assert.Equal([15, 32], Find(Body));
        Assert.Equal([5], Find("> one\n> two"));
        Assert.Equal([7], Find("- first\n  second\n- third"));
    }

    [Fact]
    public void NotInAFenceATableOrAParagraphsLastLine()
    {
        Assert.Empty(Find("```\nSeen by\nDr A. Sample\n```\n"));
        Assert.Empty(Find("    Seen by\n    Dr A. Sample\n"));
        Assert.Empty(Find("| Drug | Dose |\n|---|---|\n| A | 1 |\n| B | 2 |\n"));
        Assert.Empty(Find("First paragraph.\n\nSecond paragraph.\n"));
    }

    [Fact]
    public void NotBeforeAListItemOrAHeading()
    {
        Assert.Empty(Find("Plan:\n- rest\n- fluids\n"));
        Assert.Empty(Find("Summary\n# Plan\nRest.\n"));
        Assert.Empty(Find("Summary\n=======\n"));
    }

    [Fact]
    public void ALineEndingATildeRemoves_IsNotFound()
    {
        Assert.Empty(Find("Dear {{ input.name ~}}\nthank you for seeing her."));
        Assert.Empty(Find("Dear {{ input.name ~}}  \nthank you for seeing her."));

        // A comment closed by ~}} removes the line ending too, though it renders nothing (Scan lists no such tag).
        const string Comment = "Dear Dr A. Sample {{# salutation ~}}\nthank you for seeing her.";
        Assert.Empty(Find(Comment));
        Assert.Equal((Comment, 0), WithHardBreaks(Comment));
    }

    [Fact]
    public void TheLineAboveAnInlineReadAlone_IsFound()
    {
        // #1543 (ruling 2, D2): the G-D8 gap: 30 broke the line before the name, and so does 31 with the \.
        Assert.Equal([10], Find("Sincerely,\n{{ profile.name }}"));
        Assert.Equal(("Sincerely,\\\n{{ profile.name }}", 1), WithHardBreaks("Sincerely,\n{{ profile.name }}"));
    }

    [Fact]
    public void TheLineAboveABlockReadAlone_IsFound()
    {
        Assert.Equal([10], Find("Sincerely,\n{{ profile.closing }}"));
        Assert.Equal([17], Find("With kind regards\n  {{ profile.signature }}\n"));
    }

    [Fact]
    public void ALetterheadOfInlineReads_BreaksAfterEachButTheLast()
    {
        const string Body = "{{ profile.name }}\n{{ profile.credentials }}\n{{ profile.organisation }}";

        Assert.Equal([18, 44], Find(Body));
        Assert.Equal(("{{ profile.name }}\\\n{{ profile.credentials }}\\\n{{ profile.organisation }}", 2), WithHardBreaks(Body));
    }

    [Fact]
    public void TheLineAfterABlockReadAlone_IsNotFound()
    {
        // A \ after a block read would be text after it.
        Assert.Empty(Find("{{ profile.signature }}\nSample Oncology Clinic"));
        Assert.Empty(Find("{{ profile.name }} {{ profile.closing }}\nSample Oncology Clinic"));
        // Text beside the read is no line of only tags: it is found as any line is.
        Assert.Equal([31], Find("Signed: {{ profile.signature }}\nSample Oncology Clinic"));
    }

    [Fact]
    public void ALineOfStatementsPlacementsOrARenderer_BreaksNeitherSide()
    {
        // Its output may be nothing, which would leave the \ at a paragraph's end; after it, the \ could end one.
        Assert.Equal([3], Find("One\nTwo\n{{ if input.followup.value }}\nThree\n{{ end }}\nFour"));
        Assert.Empty(Find("Before\n  {{ snippet.closing.content }} {{ run.date }}\nAfter"));
        Assert.Empty(Find("Before\n{{ macro.letrozole }}\nAfter"));
        Assert.Empty(Find("Before\n{{ profile.signature | renderer.signature_after_line_end }}\nAfter"));
        Assert.Empty(Find("Before\n{{ x = 1 }}\nAfter"));
        Assert.Empty(Find("Before\n{{# a note to the author }}\nAfter"));
    }

    [Fact]
    public void AReadBesideAStatementOnItsLine_BreaksNeitherSide()
    {
        Assert.Empty(Find("Sincerely,\n{{ if x }}{{ profile.name }}{{ end }}\nSample Oncology Clinic"));
    }

    [Fact]
    public void ALineEndingInsideATag_IsNotFound()
    {
        const string Body = "Seen {{ input.name\n| string.strip }} today\nand again";

        Assert.Equal([Body.IndexOf(" today\n", StringComparison.Ordinal) + 6], Find(Body));
    }

    [Fact]
    public void APipeInsideATag_DoesNotMakeATable()
    {
        // Read as written, the first two lines are a table's header and delimiter rows, whose line endings are no paragraph's.
        const string Body = "Seen {{ a | b }}\nRest | fluids\nby Dr A. Sample";
        const string Table = "Seen {{ a | b }}\n---|---\n\nby Dr A. Sample";

        Assert.Single(Markdown.Parse(Table, ConsultMarkdown.Pipeline31).Descendants<Markdig.Extensions.Tables.Table>());
        Assert.Equal([16, 30], Find(Body));
        Assert.Equal([16], Find(Table));
    }

    [Fact]
    public void ABackslashOrTwoSpaces_IsAlreadyABreak()
    {
        Assert.Empty(Find("Margaret Okafor\\\n12 Sample Street  \nSampletown"));
        Assert.Equal([16], Find("Margaret Okafor \n12 Sample Street"));
    }

    [Fact]
    public void WithHardBreaks_IsIdempotent()
    {
        var (once, count) = WithHardBreaks("Margaret Okafor \n12 Sample Street\n{{ if x }}\nSampletown\n{{ end }}");

        Assert.Equal("Margaret Okafor \\\n12 Sample Street\n{{ if x }}\nSampletown\n{{ end }}", once);
        Assert.Equal(1, count);
        Assert.Equal((once, 0), WithHardBreaks(once));
        Assert.Equal(("Dear Dr A. Sample", 0), WithHardBreaks("Dear Dr A. Sample"));
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
        Assert.Empty(Find(body));
        Assert.Equal((body, 0), WithHardBreaks(body));
    }
}
