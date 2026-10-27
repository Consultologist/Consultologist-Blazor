namespace Consultologist.Api.Auth;

/// <summary>
/// #1063: the account's speech-to-text confidence policy — the OCR gate's twin
/// (<see cref="OcrConfidenceSettings"/>): the same rules over
/// <see cref="AccountSettingKeys.SttConfidenceGate"/> and
/// <see cref="AccountSettingKeys.SttMinConfidence"/>. On by default; only the
/// explicit word "false" turns it off; the minimum a whole-number percent,
/// absent = the default; saves refused by name, reads clamp.
/// </summary>
public static class SttConfidenceSettings
{
    public const int MinPercent = OcrConfidenceSettings.MinPercent;
    public const int MaxPercent = OcrConfidenceSettings.MaxPercent;
    public const int DefaultPercent = OcrConfidenceSettings.DefaultPercent;

    public static bool IsSttKey(string key) =>
        string.Equals(key, AccountSettingKeys.SttConfidenceGate, StringComparison.Ordinal)
        || string.Equals(key, AccountSettingKeys.SttMinConfidence, StringComparison.Ordinal);

    public static bool GateEnabled(string? gateValue) => OcrConfidenceSettings.GateEnabled(gateValue);

    public static int? ParsePercent(string? value) => OcrConfidenceSettings.ParsePercent(value);

    /// <summary>The save-time rule for either key, refusal sentence or null.</summary>
    public static string? Validate(string key, string value)
    {
        if (string.Equals(key, AccountSettingKeys.SttConfidenceGate, StringComparison.Ordinal))
        {
            var word = value.Trim();
            return string.Equals(word, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "false", StringComparison.OrdinalIgnoreCase)
                ? null
                : $"{AccountSettingKeys.SttConfidenceGate} must be true or false.";
        }

        var percent = ParsePercent(value);
        if (percent == null)
        {
            return $"{AccountSettingKeys.SttMinConfidence} must be a whole number of percent.";
        }

        return percent is < MinPercent or > MaxPercent
            ? $"{AccountSettingKeys.SttMinConfidence} must be between {MinPercent} and {MaxPercent}."
            : null;
    }

    /// <summary>The threshold the edge gates on: a fraction in [0, 1] when the gate is on, null when off.</summary>
    public static double? EffectiveMinConfidence(string? gateValue, string? minValue) =>
        OcrConfidenceSettings.EffectiveMinConfidence(gateValue, minValue);
}
