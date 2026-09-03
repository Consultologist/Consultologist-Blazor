using Consultologist.Web.Services.Accounts;

namespace Consultologist.Web.Tests;

/// <summary>
/// #561: the Web's half of the snippet wire format, plus the pure rules the
/// profile card and the setup form share. The wire literal is pinned
/// VERBATIM in the Api suite too (SnippetsTests.Wire).
/// </summary>
public class SnippetsModelTests
{
    internal const string Wire =
        """{"Items":[{"Id":"normal-exam","Name":"Normal exam","Text":"Cardiovascular and respiratory examination unremarkable.","UpdatedAtUtc":"2026-09-03T12:00:00+00:00"}]}""";

    [Fact]
    public void Serialize_WritesThePinnedWireBytes()
    {
        var set = new Snippets.SnippetSet(new List<Snippets.Snippet>
        {
            new("normal-exam", "Normal exam",
                "Cardiovascular and respiratory examination unremarkable.",
                new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero))
        });

        Assert.Equal(Wire, Snippets.Serialize(set));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Items\":null}")]
    public void AnUnreadableValue_IsAnEmptySet(string? value)
    {
        Assert.Empty(Snippets.Parse(value).Items);
    }

    [Theory]
    [InlineData("Normal exam", "normal-exam")]
    [InlineData("  BP follow-up!!  ", "bp-follow-up")]
    [InlineData("???", "snippet")]
    public void SlugFor_IsTheSignatureRule(string name, string expected)
    {
        Assert.Equal(expected, Snippets.SlugFor(name, Array.Empty<string>()));
    }

    [Fact]
    public void SlugFor_SuffixesCollisions()
    {
        Assert.Equal("normal-exam-2",
            Snippets.SlugFor("Normal exam", new[] { "normal-exam" }));
        Assert.Equal("normal-exam-3",
            Snippets.SlugFor("Normal exam", new[] { "normal-exam", "normal-exam-2" }));
    }

    [Fact]
    public void Describe_CountsAndNamesTheSurface()
    {
        Assert.Contains("No snippets yet", Snippets.Describe(Snippets.Empty()));

        var one = Snippets.Parse(Wire);
        Assert.Equal("1 snippet — offered on the setup form's text inputs", Snippets.Describe(one));
    }

    // #561's chosen insertion: append, blank-line separated — the snippet is
    // ordinary typed text from that moment.

    [Fact]
    public void Insert_IntoAnEmptyField_IsTheSnippetVerbatim()
    {
        Assert.Equal("The text.", Snippets.Insert("", "The text."));
        Assert.Equal("The text.", Snippets.Insert(null, "The text."));
        Assert.Equal("The text.", Snippets.Insert("   ", "The text."));
    }

    [Fact]
    public void Insert_IntoTypedText_AppendsBlankLineSeparated()
    {
        Assert.Equal("Typed so far.\n\nThe snippet.",
            Snippets.Insert("Typed so far.\n", "The snippet."));
    }
}
