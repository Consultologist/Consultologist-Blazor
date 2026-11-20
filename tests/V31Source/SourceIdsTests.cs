using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>#1499: the id rules of part 01 § 6, in the order the reader applies them.</summary>
public class SourceIdsTests
{
    [Theory]
    [InlineData("draft_paragraph", null)]
    [InlineData("Draft", "S01-030")]
    [InlineData("1st", "S01-030")]
    [InlineData("a.b", "S01-030")]
    [InlineData("", "S01-030")]
    [InlineData("con", "S01-032")]
    [InlineData("lpt9", "S01-032")]
    [InlineData("items", "S01-033")]
    [InlineData("check", "S01-033")]
    public void Check_GivesTheFirstFailingRule(string id, string? expected)
    {
        Assert.Equal(expected, SourceIds.Check(id, "nodes/x.md")?.Id);
    }

    [Fact]
    public void Check_RefusesAnIdEndingInANewline()
    {
        // The pattern's $ would match before a final newline; the id rules do not.
        var refusal = SourceIds.Check("plan\n", "nodes/index.json")!;

        Assert.Equal("S01-030", refusal.Id);
        Assert.Equal("'plan\n' is not a valid id: a lowercase letter, then lowercase letters, digits or underscores.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void Check_RefusesSixtyFiveCharacters_AndAcceptsSixtyFour()
    {
        Assert.Null(SourceIds.Check(new string('a', 64), "nodes/x.md"));
        var refusal = SourceIds.Check(new string('a', 65), "nodes/x.md")!;
        Assert.Equal("S01-031", refusal.Id);
        Assert.Equal(
            $"Id '{new string('a', 65)}' is 65 characters; the most is SourceBounds.idLength (64).",
            refusal.Sentence);
        Assert.Equal("nodes/x.md", refusal.Path);
    }

    [Theory]
    [InlineData("items", "member", "items")]
    [InlineData("inputs", "root", "input")]
    [InlineData("values", "member", "value")]
    [InlineData("passes", "member", "passes")]
    [InlineData("pass", "root", "pass")]
    public void Reserved_NamesRootOrMember(string id, string kind, string word)
    {
        var refusal = SourceIds.Check(id, "nodes/x.md")!;
        Assert.Equal("S01-033", refusal.Id);
        Assert.Equal(
            $"Id '{id}' is reserved: {kind} '{word}' is read in references, so nothing may be called '{id}'.",
            refusal.Sentence);
    }

    [Fact]
    public void EveryIdSentence_IsTheDesigns()
    {
        foreach (var id in new[] { "Draft", new string('a', 65), "con", "items" })
        {
            V31Contract.AssertSentence(SourceIds.Check(id, "nodes/x.md")!);
        }
    }
}
