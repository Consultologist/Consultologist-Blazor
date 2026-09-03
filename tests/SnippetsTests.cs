using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

/// <summary>
/// #561: the Api's read half of the profile's snippet library. The Web owns
/// the row; this class must read exactly the bytes the Web writes — the wire
/// literal below is pinned VERBATIM in the Web suite too
/// (ConsultsSnippetTests.Wire): a casing drift on either side would silently
/// read "no snippets", which tolerant parsing would never surface.
/// </summary>
public class SnippetsTests
{
    internal const string Wire =
        """{"Items":[{"Id":"normal-exam","Name":"Normal exam","Text":"Cardiovascular and respiratory examination unremarkable.","UpdatedAtUtc":"2026-09-03T12:00:00+00:00"}]}""";

    [Fact]
    public void TheWebsBytes_ReadBack()
    {
        var set = Snippets.Parse(Wire);

        var snippet = Assert.Single(set.Items);
        Assert.Equal("normal-exam", snippet.Id);
        Assert.Equal("Normal exam", snippet.Name);
        Assert.Equal("Cardiovascular and respiratory examination unremarkable.", snippet.Text);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero), snippet.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"Items\":null}")]
    [InlineData("{}")]
    public void AnUnreadableRow_IsAnEmptySet_NeverAnError(string? value)
    {
        Assert.Empty(Snippets.Parse(value).Items);
    }
}
