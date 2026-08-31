using Consultologist.Api.Auth;
using Xunit;

namespace Consultologist.Tests;

/// <summary>
/// #548: the retention rules. Saves refuse by name; reads clamp. The rule is
/// 1 ≤ inputDays ≤ outputDays ≤ 30.
/// </summary>
public class RetentionSettingsTests
{
    [Theory]
    [InlineData("retention.outputDays", true)]
    [InlineData("retention.inputDays", true)]
    [InlineData("delivery.emailPdf", false)]
    [InlineData("retention.OutputDays", false)] // ordinal, as every key comparison
    public void OnlyTheTwoClocksAreRetentionKeys(string key, bool expected) =>
        Assert.Equal(expected, RetentionSettings.IsRetentionKey(key));

    [Fact]
    public void EachClockNamesTheOtherAsSibling()
    {
        Assert.Equal(AccountSettingKeys.RetentionInputDays, RetentionSettings.SiblingKey(AccountSettingKeys.RetentionOutputDays));
        Assert.Equal(AccountSettingKeys.RetentionOutputDays, RetentionSettings.SiblingKey(AccountSettingKeys.RetentionInputDays));
    }

    [Theory]
    [InlineData("7", 7)]
    [InlineData(" 30 ", 30)]
    [InlineData("1", 1)]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("seven", null)]
    [InlineData("7.5", null)]
    [InlineData("-3", null)] // NumberStyles.None: no sign, no separators
    [InlineData("1,0", null)]
    public void ParseReadsPlainWholeNumbersOnly(string? value, int? expected) =>
        Assert.Equal(expected, RetentionSettings.Parse(value));

    [Theory]
    [InlineData("retention.outputDays", "abc", "retention.outputDays must be a whole number of days.")]
    [InlineData("retention.inputDays", "", "retention.inputDays must be a whole number of days.")]
    [InlineData("retention.outputDays", "0", "retention.outputDays must be between 1 and 30.")]
    [InlineData("retention.outputDays", "31", "retention.outputDays must be between 1 and 30.")]
    [InlineData("retention.inputDays", "0", "retention.inputDays must be between 1 and 30.")]
    [InlineData("retention.inputDays", "31", "retention.inputDays must be between 1 and 30.")]
    public void ShapeRefusalsSayTheKeyAndTheRule(string key, string value, string expected) =>
        Assert.Equal(expected, RetentionSettings.Validate(key, value, siblingValue: null));

    [Fact]
    public void InputsMayNotOutliveOutputs()
    {
        var error = RetentionSettings.Validate(AccountSettingKeys.RetentionInputDays, "10", siblingValue: "7");
        Assert.Equal("retention.inputDays (10) must not exceed retention.outputDays (7).", error);
    }

    [Fact]
    public void OutputsMayNotDropBelowInputs()
    {
        var error = RetentionSettings.Validate(AccountSettingKeys.RetentionOutputDays, "3", siblingValue: "5");
        Assert.Equal("retention.outputDays (3) must not be less than retention.inputDays (5).", error);
    }

    [Theory]
    [InlineData("7", null)]      // no sibling set: nothing to be consistent with
    [InlineData("7", "junk")]    // unreadable sibling constrains nothing
    [InlineData("7", "31")]      // out-of-range sibling constrains nothing
    [InlineData("7", "7")]       // equal is allowed
    [InlineData("7", "10")]      // inputs 7 under outputs 10
    public void ValidInputSavesPass(string value, string? sibling) =>
        Assert.Null(RetentionSettings.Validate(AccountSettingKeys.RetentionInputDays, value, sibling));

    [Fact]
    public void OutputSaveMayEqualInputs() =>
        Assert.Null(RetentionSettings.Validate(AccountSettingKeys.RetentionOutputDays, "5", siblingValue: "5"));

    [Theory]
    [InlineData(null, null, 7, 7, 7)]       // nothing chosen: the deployment default governs both
    [InlineData("3", null, 7, 3, 3)]        // outputs shortened: inputs follow down (never past outputs)
    [InlineData(null, "2", 7, 7, 2)]        // inputs shortened alone
    [InlineData("10", "4", 7, 10, 4)]       // both chosen
    [InlineData("junk", "4", 7, 7, 4)]      // unreadable outputs fall back
    [InlineData("10", "junk", 7, 10, 7)]    // unreadable inputs fall back to the default
    [InlineData("45", null, 7, 30, 7)]      // a slipped value clamps to the ceiling
    [InlineData("0", null, 7, 1, 1)]        // and to the floor
    [InlineData(null, "45", 7, 7, 7)]       // inputs clamp then still bow to outputs
    [InlineData(null, null, 45, 30, 30)]    // a raised deployment default clamps too
    [InlineData("5", "9", 7, 5, 5)]         // inputs past outputs (slipped): outputs win
    public void EffectiveClampsAndOrders(string? output, string? input, int defaultDays, int expectedOutput, int expectedInput)
    {
        var (outputDays, inputDays) = RetentionSettings.Effective(output, input, defaultDays);
        Assert.Equal(expectedOutput, outputDays);
        Assert.Equal(expectedInput, inputDays);
    }
}
