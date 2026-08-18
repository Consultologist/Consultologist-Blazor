namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #390: the account's preferred time of day for a scheduled consult, and the
/// arithmetic both pages need to turn it into an instant.
///
/// It rides the generic settings routes rather than a dedicated endpoint —
/// Account/Settings/{key} accepts any non-secret key, and this is the same
/// shape as the workflow-package pin (consult.workflowPackage).
///
/// The stored value is a LOCAL time of day, not an instant. Scheduling has been
/// browser-local since #157, and "2 AM where I am" is not something a UTC value
/// can mean.
/// </summary>
public static class ScheduleDefault
{
    public const string SettingKey = "consult.scheduleTime";

    public const string ContentType = "text/plain";

    /// <summary>
    /// What a scheduled consult used before #390, and what it still uses when
    /// the account has set no preference. Empty until chosen is the rule; this
    /// is the fallback, not a value written on the user's behalf.
    /// </summary>
    public const string FallbackTimeOfDay = "02:00";

    /// <summary>
    /// A stored setting as an HH:mm string, or null when it is absent, blank or
    /// unparseable. Callers fall back rather than erroring — an unreadable
    /// preference must not stop someone scheduling a consult.
    /// </summary>
    public static string? Parse(string? storedValue)
        => TimeOnly.TryParseExact(storedValue?.Trim(), "HH:mm", out var timeOfDay)
            ? timeOfDay.ToString("HH:mm")
            : null;

    /// <summary>
    /// The next local occurrence of a time of day, as UTC — the rule #157
    /// established: today if it is still ahead, otherwise tomorrow.
    /// </summary>
    public static DateTimeOffset NextOccurrenceUtc(string? timeOfDay, DateTime localNow)
    {
        var parsed = Parse(timeOfDay) ?? FallbackTimeOfDay;
        var time = TimeOnly.ParseExact(parsed, "HH:mm");
        var next = localNow.Date.Add(time.ToTimeSpan());

        if (localNow >= next)
        {
            next = next.AddDays(1);
        }

        return new DateTimeOffset(next).ToUniversalTime();
    }

    /// <summary>
    /// The value for an &lt;input type="datetime-local"&gt;, which speaks local
    /// wall-clock time with no offset.
    /// </summary>
    public static string ToLocalInputValue(DateTimeOffset instant)
        => instant.ToLocalTime().ToString("yyyy-MM-ddTHH:mm");

    /// <summary>
    /// A datetime-local input's value back to an instant, or null when the user
    /// has cleared or half-typed it.
    /// </summary>
    public static DateTimeOffset? FromLocalInputValue(string? value)
        => DateTime.TryParse(value, out var local)
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUniversalTime()
            : null;
}
