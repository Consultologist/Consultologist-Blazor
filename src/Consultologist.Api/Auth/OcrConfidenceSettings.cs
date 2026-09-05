using System.Globalization;

namespace Consultologist.Api.Auth;

/// <summary>
/// #239: the account's OCR confidence policy — how the two settings
/// (<see cref="AccountSettingKeys.OcrConfidenceGate"/> and
/// <see cref="AccountSettingKeys.OcrMinConfidence"/>) parse, what a save may
/// say, and the effective minimum the extractor gates on.
///
/// The gate is **on by default**: a scan's text is the riskiest input this app
/// reads (a misread dose is a clinical error), so absence means "check", not
/// "skip". Only the explicit word <c>"false"</c> turns it off. The minimum is a
/// whole-number percent (0–100); absent is the default. Saves are refused by
/// name; reads clamp instead of refusing, so a slipped value can never turn the
/// gate into an accident.
/// </summary>
public static class OcrConfidenceSettings
{
    public const int MinPercent = 0;
    public const int MaxPercent = 100;
    public const int DefaultPercent = 80;

    public static bool IsOcrKey(string key) =>
        string.Equals(key, AccountSettingKeys.OcrConfidenceGate, StringComparison.Ordinal)
        || string.Equals(key, AccountSettingKeys.OcrMinConfidence, StringComparison.Ordinal);

    /// <summary>Default-on: only the explicit "false" (any case) turns it off.</summary>
    public static bool GateEnabled(string? gateValue) =>
        !string.Equals(gateValue?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Invariant culture, as everywhere a stored string becomes a number.</summary>
    public static int? ParsePercent(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            ? percent
            : null;

    /// <summary>
    /// The save-time rule for either OCR key, refusal sentence or null. The gate
    /// must be a boolean word; the minimum must be a percent in range.
    /// </summary>
    public static string? Validate(string key, string value)
    {
        if (string.Equals(key, AccountSettingKeys.OcrConfidenceGate, StringComparison.Ordinal))
        {
            var word = value.Trim();
            return string.Equals(word, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(word, "false", StringComparison.OrdinalIgnoreCase)
                ? null
                : $"{AccountSettingKeys.OcrConfidenceGate} must be true or false.";
        }

        var percent = ParsePercent(value);
        if (percent == null)
        {
            return $"{AccountSettingKeys.OcrMinConfidence} must be a whole number of percent.";
        }

        return percent is < MinPercent or > MaxPercent
            ? $"{AccountSettingKeys.OcrMinConfidence} must be between {MinPercent} and {MaxPercent}."
            : null;
    }

    /// <summary>
    /// The threshold the extractor gates on: a fraction in [0, 1] when the gate
    /// is on, or null when it is off (accept whatever OCR returns). Tolerant on
    /// read — a bad stored percent falls back to the default rather than
    /// refusing or disabling the gate.
    /// </summary>
    public static double? EffectiveMinConfidence(string? gateValue, string? minValue)
    {
        if (!GateEnabled(gateValue))
        {
            return null;
        }

        var percent = Math.Clamp(ParsePercent(minValue) ?? DefaultPercent, MinPercent, MaxPercent);
        return percent / 100.0;
    }
}
