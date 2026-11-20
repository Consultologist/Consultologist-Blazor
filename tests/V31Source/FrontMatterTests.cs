using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>#1499: the front matter split of part 01 § 4.</summary>
public class FrontMatterTests
{
    [Fact]
    public void Split_ReadsHeaderAndBody()
    {
        var split = FrontMatter.Split("---\n{ \"a\": 1 }\n---\nBody\n");
        Assert.Equal("{ \"a\": 1 }", split!.Header);
        Assert.Equal("Body\n", split.Body);
    }

    [Fact]
    public void Split_KeepsABlankLineAfterTheFence()
    {
        Assert.Equal("\nBody", FrontMatter.Split("---\n{}\n---\n\nBody")!.Body);
    }

    [Theory]
    [InlineData("---\n{}\n---")]
    [InlineData("---\n{}\n---\n")]
    public void Split_AcceptsAHeaderOnlyFile(string text)
    {
        var split = FrontMatter.Split(text);
        Assert.Equal("{}", split!.Header);
        Assert.Equal("", split.Body);
    }

    [Fact]
    public void Split_AcceptsAHeaderOverSeveralLines()
    {
        Assert.Equal("{\n  \"a\": 1\n}", FrontMatter.Split("---\n{\n  \"a\": 1\n}\n---\nx")!.Header);
    }

    [Fact]
    public void Split_KeepsLaterFenceLinesInTheBody()
    {
        var split = FrontMatter.Split("---\n{}\n---\none\n---\ntwo\n");
        Assert.Equal("{}", split!.Header);
        Assert.Equal("one\n---\ntwo\n", split.Body);
    }

    [Theory]
    [InlineData("--- \n{}\n---\n")]
    [InlineData("{}\n")]
    [InlineData("---\n{}\n")]
    [InlineData(" ---\n{}\n---\n")]
    [InlineData("")]
    public void Split_RefusesAnythingElse(string text)
    {
        Assert.Null(FrontMatter.Split(text));
    }

    [Fact]
    public void IsEmptyBody_IsTrueForWhitespaceOnly()
    {
        Assert.True(FrontMatter.IsEmptyBody(""));
        Assert.True(FrontMatter.IsEmptyBody(" \n\t\n"));
        Assert.False(FrontMatter.IsEmptyBody("\n x \n"));
    }
}
