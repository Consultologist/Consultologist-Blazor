using System.Globalization;

namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #239: the account's OCR confidence policy — ocr.confidenceGate (a toggle)
/// and ocr.minConfidence (a percent) on the generic settings routes, mirroring
/// the Api's OcrConfidenceSettings.
///
/// Unlike the other preferences, the gate is **on by default**: a scan's text
/// is the riskiest input the app reads, so absence means "check", not "skip".
/// Only a stored "false" turns it off. The minimum is a whole-number percent;
/// absent means the default.
/// </summary>
public static class OcrConfidencePreference
{
    public const string GateKey = "ocr.confidenceGate";
    public const string MinKey = "ocr.minConfidence";

    public const string ContentType = "text/plain";

    public const int MinPercent = 0;
    public const int MaxPercent = 100;
    public const int DefaultPercent = 80;

    /// <summary>Default-on: only a stored "false" turns the gate off.</summary>
    public static bool GateEnabled(string? value) =>
        !string.Equals(value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

    public static int? ParsePercent(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            ? percent
            : null;

    public static bool InRange(int percent) => percent is >= MinPercent and <= MaxPercent;

    /// <summary>The percent to show and gate on: the stored value or the default.</summary>
    public static int EffectivePercent(string? value) => ParsePercent(value) ?? DefaultPercent;

    public static string StoreGate(bool enabled) => enabled ? "true" : "false";

    public static string StorePercent(int percent) => percent.ToString(CultureInfo.InvariantCulture);

    /// <summary>The state line — what the policy currently does, said plainly.</summary>
    public static string Describe(bool enabled, int? percent) =>
        enabled
            ? $"On — a scanned PDF is accepted only when its OCR confidence is at least {percent ?? DefaultPercent}%"
            : "Off — every readable scan is accepted, whatever its OCR confidence";
}
