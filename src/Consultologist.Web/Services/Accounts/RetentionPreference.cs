using System.Globalization;

namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #548: how long a run's text is kept after completion — retention.outputDays
/// and retention.inputDays on the generic settings routes, mirroring the Api's
/// RetentionSettings. Empty until chosen: null means "not chosen", and the
/// deployment default governs, as it did before the option existed. The rule
/// is 1 ≤ inputDays ≤ outputDays ≤ 30 — inputs never outlive outputs, and 30
/// days is the storage lifecycle ceiling.
/// </summary>
public static class RetentionPreference
{
    public const string OutputDaysKey = "retention.outputDays";
    public const string InputDaysKey = "retention.inputDays";

    public const string ContentType = "text/plain";

    public const int MinDays = 1;
    public const int MaxDays = 30;

    /// <summary>The deployment's TextRetention__Days — what "not chosen" means.</summary>
    public const int DefaultDays = 7;

    /// <summary>Invariant and plain, as the Api reads it: a whole number of days or null.</summary>
    public static int? Parse(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var days)
            ? days
            : null;

    public static bool InRange(int days) => days is >= MinDays and <= MaxDays;

    public static string Store(int days) => days.ToString(CultureInfo.InvariantCulture);

    /// <summary>The state line — what unset means is said, not left silent.</summary>
    public static string Describe(int? days) => days switch
    {
        null => $"Not chosen — kept {DefaultDays} days, as today",
        1 => "Kept 1 day after completion",
        _ => $"Kept {days} days after completion"
    };
}
