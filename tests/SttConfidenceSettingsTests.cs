using Consultologist.Api.Auth;

namespace Consultologist.Api.Tests;

/// <summary>#1063: the speech-to-text gate — the OCR gate's rules over its own keys.</summary>
public class SttConfidenceSettingsTests
{
    [Theory]
    [InlineData("stt.confidenceGate", true)]
    [InlineData("stt.minConfidence", true)]
    [InlineData("ocr.minConfidence", false)]
    [InlineData("retention.outputDays", false)]
    public void IsSttKey_NamesTheTwo(string key, bool expected)
    {
        Assert.Equal(expected, SttConfidenceSettings.IsSttKey(key));
        Assert.Equal(expected, !OcrConfidenceSettings.IsOcrKey(key) && SttConfidenceSettings.IsSttKey(key));
    }

    [Theory]
    [InlineData("true", null)]
    [InlineData("false", null)]
    [InlineData("yes", "stt.confidenceGate must be true or false.")]
    public void Validate_TheGate(string value, string? expectedError)
    {
        Assert.Equal(expectedError, SttConfidenceSettings.Validate(AccountSettingKeys.SttConfidenceGate, value));
    }

    [Theory]
    [InlineData("80", null)]
    [InlineData("101", "stt.minConfidence must be between 0 and 100.")]
    [InlineData("abc", "stt.minConfidence must be a whole number of percent.")]
    public void Validate_TheMinimum(string value, string? expectedError)
    {
        Assert.Equal(expectedError, SttConfidenceSettings.Validate(AccountSettingKeys.SttMinConfidence, value));
    }

    [Theory]
    [InlineData(null, null, 0.8)]
    [InlineData("true", "90", 0.9)]
    [InlineData("false", "90", null)]
    [InlineData(null, "abc", 0.8)]
    [InlineData(null, "250", 1.0)]
    public void EffectiveMinConfidence_IsAFractionWhenOn_NullWhenOff_AndClampsOnRead(string? gate, string? min, double? expected)
    {
        Assert.Equal(expected, SttConfidenceSettings.EffectiveMinConfidence(gate, min));
    }
}
