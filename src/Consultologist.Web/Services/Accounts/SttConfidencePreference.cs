using System.Globalization;

namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #1063: the account's speech-to-text confidence policy — stt.confidenceGate
/// (a toggle) and stt.minConfidence (a percent) on the generic settings routes,
/// mirroring the Api's SttConfidenceSettings; the OCR gate's twin
/// (<see cref="OcrConfidencePreference"/>), on by default for the same reason:
/// a recording's transcript is the model's only view of what was said.
/// </summary>
public static class SttConfidencePreference
{
    public const string GateKey = "stt.confidenceGate";
    public const string MinKey = "stt.minConfidence";

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
            ? $"On — a recording is accepted only when its transcription confidence is at least {percent ?? DefaultPercent}%"
            : "Off — every transcribed recording is accepted, whatever its confidence";
}
