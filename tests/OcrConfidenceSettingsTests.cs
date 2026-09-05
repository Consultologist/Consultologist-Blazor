using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

public class OcrConfidenceSettingsTests
{
    [Theory]
    // Default ON: absent, blank, or anything but "false" is on.
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("anything", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("  false  ", false)]
    public void GateEnabled_IsOnUnlessExplicitlyFalse(string? value, bool enabled)
    {
        Assert.Equal(enabled, OcrConfidenceSettings.GateEnabled(value));
    }

    [Theory]
    [InlineData("80", 80)]
    [InlineData("0", 0)]
    [InlineData("100", 100)]
    [InlineData("  75 ", 75)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("-5", null)]
    [InlineData("80.5", null)]
    public void ParsePercent_IsAWholeNumberOrNull(string value, int? expected)
    {
        Assert.Equal(expected, OcrConfidenceSettings.ParsePercent(value));
    }

    [Theory]
    [InlineData("true", null)]
    [InlineData("false", null)]
    [InlineData("True", null)]
    [InlineData("yes", "ocr.confidenceGate must be true or false.")]
    public void Validate_Gate_AcceptsBooleanWordsOnly(string value, string? expectedError)
    {
        Assert.Equal(expectedError, OcrConfidenceSettings.Validate(AccountSettingKeys.OcrConfidenceGate, value));
    }

    [Theory]
    [InlineData("80", null)]
    [InlineData("0", null)]
    [InlineData("100", null)]
    [InlineData("101", "ocr.minConfidence must be between 0 and 100.")]
    [InlineData("abc", "ocr.minConfidence must be a whole number of percent.")]
    public void Validate_Min_IsAPercentInRange(string value, string? expectedError)
    {
        Assert.Equal(expectedError, OcrConfidenceSettings.Validate(AccountSettingKeys.OcrMinConfidence, value));
    }

    [Theory]
    // Gate off → no threshold (accept whatever OCR returns).
    [InlineData("false", "80", null)]
    // Gate on (absent/true) → the stored percent as a fraction, default 80.
    [InlineData(null, null, 0.80)]
    [InlineData("true", "90", 0.90)]
    [InlineData(null, "50", 0.50)]
    // Tolerant read: a bad or out-of-range percent falls back / clamps, never
    // disabling the gate.
    [InlineData("true", "abc", 0.80)]
    [InlineData("true", "150", 1.00)]
    public void EffectiveMinConfidence_IsTheGatedFractionOrNull(string? gate, string? min, double? expected)
    {
        Assert.Equal(expected, OcrConfidenceSettings.EffectiveMinConfidence(gate, min));
    }
}
