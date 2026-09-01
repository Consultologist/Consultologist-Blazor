using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

/// <summary>
/// #543: only the account's own word runs a response at once — unset, blank
/// and garbage all hold (the #518 rule), and a save of anything but the two
/// words is refused by name so a typo never reads as chosen.
/// </summary>
public class FormResponseModeTests
{
    [Theory]
    [InlineData("runAtOnce", "runAtOnce")]
    [InlineData("  runatonce  ", "runAtOnce")]
    [InlineData("RUNATONCE", "runAtOnce")]
    [InlineData("hold", "hold")]
    [InlineData(" HOLD ", "hold")]
    public void TheTwoWords_Parse_WhateverTheCaseAndPadding(string stored, string expected) =>
        Assert.Equal(expected, FormResponseModes.Of(stored));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("run at once")]
    [InlineData("true")]
    [InlineData("atOnce")]
    public void AnythingElse_IsNotChosen_WhichHolds(string? stored) =>
        Assert.Null(FormResponseModes.Of(stored));

    [Fact]
    public void ASaveOfAnyOtherWord_IsRefusedByName()
    {
        Assert.Null(FormResponseModes.Validate("hold"));
        Assert.Null(FormResponseModes.Validate("runAtOnce"));
        Assert.Equal("forms.responseMode must be 'hold' or 'runAtOnce'.", FormResponseModes.Validate("yes"));
        Assert.Equal("forms.responseMode must be 'hold' or 'runAtOnce'.", FormResponseModes.Validate(null));
    }

    [Fact]
    public void TheKey_IsTheFormsAreaWord()
    {
        Assert.Equal("forms.responseMode", AccountSettingKeys.FormResponseMode);
    }
}
