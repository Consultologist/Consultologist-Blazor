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
/// As the owner amended D2 on 2026-11-27, a line ending beside a line whose only reads are block reads is a hit of its own
/// kind, where <see cref="MarkdownLineEndings.WithBlockReadsSeparated"/> writes a blank line.
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

    /// <summary>The finder with the roles of <see cref="RoleOf"/>: the offsets of its hits.</summary>
    private static IReadOnlyList<int> Find(string body) =>
        Hits(body).Select(h => h.At).ToList();

    /// <summary>The finder's hits, each with its kind, with the roles of <see cref="RoleOf"/>.</summary>
    private static IReadOnlyList<LineEndingHit> Hits(string body) =>
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
        Assert.Equal([new LineEndingHit(10, LineEndingKind.Break)], Hits("Sincerely,\n{{ profile.name }}"));
        Assert.Equal(("Sincerely,\\\n{{ profile.name }}", 1), WithHardBreaks("Sincerely,\n{{ profile.name }}"));
    }

    [Fact]
    public void TheLineAboveABlockReadAlone_IsFound_BesideIt_AndGetsNoBackslash()
    {
        // #1543 (the owner's ruling of 2026-11-27, item 3): a blank line goes beside a line holding only a block read, never \.
        Assert.Equal([new LineEndingHit(10, LineEndingKind.BesideABlockRead)], Hits("Sincerely,\n{{ profile.closing }}"));
        Assert.Equal([new LineEndingHit(17, LineEndingKind.BesideABlockRead)], Hits("With kind regards\n  {{ profile.signature }}\n"));
        Assert.Equal(("Sincerely,\n{{ profile.closing }}", 0), WithHardBreaks("Sincerely,\n{{ profile.closing }}"));
        // A line of inline reads is a line of text; a statement on the read's line is no read.
        Assert.Equal([new LineEndingHit(18, LineEndingKind.BesideABlockRead)], Hits("{{ profile.name }}\n{{ profile.signature }}"));
        Assert.Equal([new LineEndingHit(10, LineEndingKind.BesideABlockRead)], Hits("Sincerely,\n{{ if x }}{{ profile.closing }}{{ end }}"));
    }

    [Fact]
    public void ALetterheadOfInlineReads_BreaksAfterEachButTheLast()
    {
        const string Body = "{{ profile.name }}\n{{ profile.credentials }}\n{{ profile.organisation }}";

        Assert.Equal([18, 44], Find(Body));
        Assert.Equal(("{{ profile.name }}\\\n{{ profile.credentials }}\\\n{{ profile.organisation }}", 2), WithHardBreaks(Body));
    }

    [Fact]
    public void TheLineAfterABlockReadAlone_IsFound_BesideIt()
    {
        // #1543 (the owner's ruling of 2026-11-27, item 3): below a line holding only a block read too, a blank line.
        Assert.Equal([new LineEndingHit(23, LineEndingKind.BesideABlockRead)], Hits("{{ profile.signature }}\nSample Oncology Clinic"));
        Assert.Equal(
            [new LineEndingHit(10, LineEndingKind.BesideABlockRead), new LineEndingHit(34, LineEndingKind.BesideABlockRead)],
            Hits("Sincerely,\n{{ profile.signature }}\nSample Oncology Clinic"));
        // A block read with an inline read beside it has text beside it (S05-066): its line is no block read's alone, and
        // the line ending after it is left as A3 left it.
        Assert.Empty(Find("{{ profile.name }} {{ profile.closing }}\nSample Oncology Clinic"));
        // Text beside the read is no line of only tags: it is found as any line is.
        Assert.Equal([new LineEndingHit(31, LineEndingKind.Break)], Hits("Signed: {{ profile.signature }}\nSample Oncology Clinic"));
        // Two lines of only block reads, or a block read's line beside a statement's: neither.
        Assert.Empty(Find("{{ profile.signature }}\n{{ profile.closing }}"));
        Assert.Empty(Find("{{ profile.signature }}\n{{ if x }}\nSample Oncology Clinic\n{{ end }}"));
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

    /// <summary>What <see cref="MarkdownLineEndings.TextBeside"/> says of the first tag of <paramref name="body"/> holding <paramref name="read"/>.</summary>
    private static (bool Before, bool After) TextBeside(string body, string read)
    {
        var tag = TemplateTags.Scan(body).Tags.First(t => body[t.Start..t.End].Contains(read, StringComparison.Ordinal));
        return MarkdownLineEndings.TextBeside(body, tag, t => RoleOf(body[t.Start..t.End]));
    }

    [Fact]
    public void ATagBesideTheReadThatSpansLines_KeepsTheScanOnTheReadsLine()
    {
        // #1543 (D5; Task A4's review): a statement stepped over that spans lines ends the scan at its line ending: the text
        // on the line before (or after) it is another line's, not text beside the read.
        Assert.Equal((false, false), TextBeside("Seen at: {{ if x\n }}{{ profile.signature }}{{ x = 1\n }} (signed)", "profile.signature"));
        // On one line, the same statements are stepped over and the text beyond them is beside the read.
        Assert.Equal((true, true), TextBeside("Seen at: {{ if x }}{{ profile.signature }}{{ x = 1 }} (signed)", "profile.signature"));
        // An inline read that spans lines is text where it stands beside the read.
        Assert.Equal((true, false), TextBeside("{{ input.name\n | string.strip }}{{ profile.signature }}", "profile.signature"));
    }

    /// <summary><see cref="MarkdownLineEndings.WithBlockReadsSeparated"/>'s text, the tags it separated and those it marked (each list joined by a space).</summary>
    private static (string Text, string Separated, string Marked) Separated(string body)
    {
        var (text, separated, marked) = MarkdownLineEndings.WithBlockReadsSeparated(body, t => RoleOf(body[t.Start..t.End]));
        return (text, string.Join(" ", separated.Select(t => body[t.Start..t.End])), string.Join(" ", marked.Select(t => body[t.Start..t.End])));
    }

    [Fact]
    public void WithBlockReadsSeparated_WritesABlankLineOnEachSide_AndDropsNothing()
    {
        // #1543 (ruling 2, D7, as the owner amended it on 2026-11-27): two line endings before the read (and the tags
        // stepped over beside it), two after it; a read alone, or an inline read, is left; written twice, nothing more.
        Assert.Equal(("Regards, \n\n{{ profile.signature }}", "{{ profile.signature }}", ""), Separated("Regards, {{ profile.signature }}"));
        Assert.Equal(("{{ profile.signature }}\n\n (dictated)", "{{ profile.signature }}", ""), Separated("{{ profile.signature }} (dictated)"));
        Assert.Equal(
            ("Referred for: \n\n{{ if x }}{{ profile.closing }}{{ end }}\n\n (from the intake form).", "{{ profile.closing }}", ""),
            Separated("Referred for: {{ if x }}{{ profile.closing }}{{ end }} (from the intake form)."));
        // Two block reads side by side are separated as one, with one blank line on each side.
        Assert.Equal(
            ("Signed: \n\n{{ profile.signature }}{{ profile.closing }}\n\n.", "{{ profile.signature }} {{ profile.closing }}", ""),
            Separated("Signed: {{ profile.signature }}{{ profile.closing }}."));
        // An inline read beside the block read is text: the blank line stands between them.
        Assert.Equal(("{{ profile.name }} \n\n{{ profile.closing }}", "{{ profile.closing }}", ""), Separated("{{ profile.name }} {{ profile.closing }}"));
        Assert.Equal(("Dictated by {{ profile.name }}.", "", ""), Separated("Dictated by {{ profile.name }}."));
        var once = Separated("Regards,  {{ profile.signature }} (dictated)\nNext line.").Text;
        Assert.Equal("Regards,  \n\n{{ profile.signature }}\n\n (dictated)\nNext line.", once);
        Assert.Equal((once, "", ""), Separated(once));
    }

    [Fact]
    public void WithBlockReadsSeparated_PutsABlankLineBesideALineOfOnlyABlockRead()
    {
        // Item 3: above and below such a line, one more line ending; a blank line already there is left.
        Assert.Equal(
            ("Sincerely,\n\n{{ profile.signature }}\n\nSample Oncology Clinic", "{{ profile.signature }}", ""),
            Separated("Sincerely,\n{{ profile.signature }}\nSample Oncology Clinic"));
        Assert.Equal(("Sincerely,\n\n{{ profile.closing }}\n", "", ""), Separated("Sincerely,\n\n{{ profile.closing }}\n"));
        // Text after a read separated from its line: the text's line is then the next to the read's, and gets its blank line.
        Assert.Equal(
            ("Regards, \n\n{{ profile.signature }}\n\nSample Oncology Clinic", "{{ profile.signature }}", ""),
            Separated("Regards, {{ profile.signature }}\nSample Oncology Clinic"));
        // A line of inline reads above, a \r\n line ending doubled as it is.
        Assert.Equal(("{{ profile.name }}\r\n\r\n{{ profile.signature }}", "{{ profile.signature }}", ""), Separated("{{ profile.name }}\r\n{{ profile.signature }}"));
    }

    [Theory]
    [InlineData("- ")]
    [InlineData("> ")]
    [InlineData("# ")]
    [InlineData("1. ")]
    [InlineData("- > ")]
    [InlineData("  2) ")]
    [InlineData("- Seen: ")]
    [InlineData("> Signed: ")]
    [InlineData("### Plan: ")]
    [InlineData("-")]
    public void WithBlockReadsSeparated_LeavesALineThatStartsABlock(string before)
    {
        // Item 4, as the controller extended it for the owner (Task A5's review): the text before the read starts a list
        // item, a quote or a heading, whether or not more text follows the marker; a blank line would move the read out of
        // it. Nothing on the line is moved, the read is marked.
        var body = "Plan:\n\n" + before + "{{ profile.signature }} (dictated)\n";
        Assert.Equal((body, "", "{{ profile.signature }}"), Separated(body));
    }

    [Theory]
    [InlineData("\tcode")]
    [InlineData("    code")]
    [InlineData("  \t code")]
    [InlineData(" - continued")]
    [InlineData(" * continued")]
    [InlineData(" 3. continued")]
    [InlineData(" > quoted")]
    [InlineData(" # Plan")]
    [InlineData(" -")]
    public void WithBlockReadsSeparated_LeavesALineWhoseTextAfterWouldStartABlock(string after)
    {
        // The text after the read would start a block on its own line: indented code, a list item, a quote, a heading.
        // Nothing on the line is moved, the text before it included; a second read on the line stays too.
        var body = "Regards, {{ profile.signature }}" + after + "\n";
        Assert.Equal((body, "", "{{ profile.signature }}"), Separated(body));
        var two = "Seen {{ profile.closing }} by {{ profile.signature }}" + after;
        Assert.Equal((two, "", "{{ profile.closing }} {{ profile.signature }}"), Separated(two));
    }

    [Fact]
    public void WithBlockReadsSeparated_SeparatesOrdinaryText()
    {
        // A marker that is not at the line's start, or not followed by white space, and up to three spaces before the text
        // after: no block starts, so the read is separated on both sides.
        Assert.Equal("Plan - see \n\n{{ profile.signature }}\n\n   (dictated)", Separated("Plan - see {{ profile.signature }}   (dictated)").Text);
        Assert.Equal("-Seen \n\n{{ profile.signature }}\n\n -x", Separated("-Seen {{ profile.signature }} -x").Text);
        Assert.Equal("#1 \n\n{{ profile.signature }}", Separated("#1 {{ profile.signature }}").Text);
    }

    [Fact]
    public void Separations_AreTheReadsOwnPlaces()
    {
        // #1543 (D8): the places the comparison credits are each separated read's own blank lines, not any line's.
        const string Body = "Dear Margaret Okafor,\n\nReferred for: \n\n{{ if x }}{{ profile.closing }}{{ end }}\n\n (from the intake form).\n\n{{ node.plan.content }}\n";
        var separations = MarkdownLineEndings.Separations(Body, new HashSet<string> { "{{ profile.closing }}" });

        Assert.Equal(
            [(true, "Referred for: "), (false, " (from the intake form).")],
            separations.Select(s => (s.Above, s.Line)));
        Assert.All(separations, s => Assert.Equal("\n\n", Body[s.From..s.To]));
        Assert.Empty(MarkdownLineEndings.Separations(Body, new HashSet<string>()));
        // A tag with text on its own line is not separated.
        Assert.Empty(MarkdownLineEndings.Separations("Seen at: {{ r }}\n\nNext", new HashSet<string> { "{{ r }}" }));
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
