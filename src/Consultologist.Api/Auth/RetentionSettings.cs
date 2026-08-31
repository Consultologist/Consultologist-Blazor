using System.Globalization;

namespace Consultologist.Api.Auth;

/// <summary>
/// #548: the retention rules — how the two account settings parse, what a
/// save may say, and what the sweep actually uses. The rule is
/// 1 ≤ inputDays ≤ outputDays ≤ 30: inputs are the more sensitive class and
/// never outlive the outputs, and 30 days is the storage account's lifecycle
/// ceiling — a longer promise would be broken by the platform. Saves are
/// refused by name; reads clamp instead of refusing, so a value that slipped
/// past validation (or a raised deployment default) can never stretch a
/// clock beyond the ceiling.
/// </summary>
public static class RetentionSettings
{
    public const int MinDays = 1;
    public const int MaxDays = 30;

    public static bool IsRetentionKey(string key) =>
        string.Equals(key, AccountSettingKeys.RetentionOutputDays, StringComparison.Ordinal)
        || string.Equals(key, AccountSettingKeys.RetentionInputDays, StringComparison.Ordinal);

    /// <summary>The other clock — the one a save must stay consistent with.</summary>
    public static string SiblingKey(string key) =>
        string.Equals(key, AccountSettingKeys.RetentionOutputDays, StringComparison.Ordinal)
            ? AccountSettingKeys.RetentionInputDays
            : AccountSettingKeys.RetentionOutputDays;

    /// <summary>
    /// Invariant culture as everywhere a stored string becomes a number: the
    /// row must read the same whatever locale the host wakes up in. Null for
    /// anything that is not a plain whole number.
    /// </summary>
    public static int? Parse(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            ? days
            : null;

    /// <summary>
    /// The save-time rule, refusal sentence or null. The sibling is the
    /// stored value of <see cref="SiblingKey"/>; a sibling that is absent or
    /// itself out of shape constrains nothing — the sweep's clamp is the
    /// backstop, not this.
    /// </summary>
    public static string? Validate(string key, string value, string? siblingValue)
    {
        var days = Parse(value);
        if (days == null)
        {
            return $"{key} must be a whole number of days.";
        }

        if (days < MinDays || days > MaxDays)
        {
            return $"{key} must be between {MinDays} and {MaxDays}.";
        }

        var sibling = Parse(siblingValue);
        if (sibling is < MinDays or > MaxDays)
        {
            sibling = null;
        }

        if (sibling is { } other)
        {
            if (string.Equals(key, AccountSettingKeys.RetentionInputDays, StringComparison.Ordinal) && days > other)
            {
                return $"{AccountSettingKeys.RetentionInputDays} ({days}) must not exceed {AccountSettingKeys.RetentionOutputDays} ({other}).";
            }

            if (string.Equals(key, AccountSettingKeys.RetentionOutputDays, StringComparison.Ordinal) && days < other)
            {
                return $"{AccountSettingKeys.RetentionOutputDays} ({days}) must not be less than {AccountSettingKeys.RetentionInputDays} ({other}).";
            }
        }

        return null;
    }

    /// <summary>
    /// What the sweep uses: each clock is the account's value when it reads
    /// as a number, else the deployment default; both clamped to
    /// [1, 30], and inputs never past outputs. Tolerant on purpose — a bad
    /// stored value shortens nothing and stretches nothing, it just falls
    /// back.
    /// </summary>
    public static (int OutputDays, int InputDays) Effective(string? outputValue, string? inputValue, int defaultDays)
    {
        var output = Math.Clamp(Parse(outputValue) ?? defaultDays, MinDays, MaxDays);
        var input = Math.Clamp(Parse(inputValue) ?? defaultDays, MinDays, MaxDays);
        return (output, Math.Min(input, output));
    }
}
