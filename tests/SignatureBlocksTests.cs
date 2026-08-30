using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

/// <summary>
/// #516: the Api's read half of the profile's signature blocks. The Web owns
/// the row; this class must read exactly the bytes the Web writes — the wire
/// literal below is pinned VERBATIM in the Web suite too
/// (SignatureProfileTests.Wire): a casing drift on either side would silently
/// read "none chosen", which tolerant parsing would never surface.
/// </summary>
public class SignatureBlocksTests
{
    private const string Wire =
        """{"Blocks":[{"Id":"clinic-letters","Name":"Clinic letters","Text":"Taylor Reyes, MD\nDept. of Medicine","UpdatedAtUtc":"2026-08-30T12:00:00+00:00"}],"ChosenId":"clinic-letters"}""";

    [Fact]
    public void TheWebsBytes_ReadBack_ChosenAndAll()
    {
        var set = SignatureBlocks.Parse(Wire);

        var chosen = SignatureBlocks.Chosen(set);
        Assert.Equal("clinic-letters", chosen!.Id);
        Assert.Equal("Clinic letters", chosen.Name);
        Assert.Equal("Taylor Reyes, MD\nDept. of Medicine", chosen.Text);
        Assert.Equal(new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), chosen.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"Blocks\":null}")]
    [InlineData("{}")]
    public void AnUnreadableRow_IsAnEmptySet_NeverAnError(string? value)
    {
        var set = SignatureBlocks.Parse(value);

        Assert.Empty(set.Blocks);
        Assert.Null(set.ChosenId == null ? null : SignatureBlocks.Chosen(set));
        Assert.Null(SignatureBlocks.Chosen(set));
    }

    [Fact]
    public void ADanglingChosenId_ChoosesNobody_NeverTheFirstBlock()
    {
        var set = SignatureBlocks.Parse(
            """{"Blocks":[{"Id":"a","Name":"A","Text":"T","UpdatedAtUtc":"2026-08-30T12:00:00+00:00"}],"ChosenId":"ghost"}""");

        Assert.Single(set.Blocks);
        Assert.Null(SignatureBlocks.Chosen(set));
    }

    [Fact]
    public void NoChosenId_ChoosesNobody_HoweverManyBlocksExist()
    {
        var set = SignatureBlocks.Parse(
            """{"Blocks":[{"Id":"a","Name":"A","Text":"T","UpdatedAtUtc":"2026-08-30T12:00:00+00:00"}],"ChosenId":null}""");

        Assert.Null(SignatureBlocks.Chosen(set));
    }
}
