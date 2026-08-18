using System.Globalization;

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
        // InvariantCulture on BOTH halves. A custom format's ":" means the
        // CULTURE's time separator, not a colon — so under a culture that uses
        // "." this parsed 07:45 and handed back "07.45", a value nothing could
        // read again, including this method. Blazor takes its culture from the
        // browser, so that is a real machine rather than a hypothetical.
        //
        // Both precisions, because <input type="time"> sends HH:mm:ss whenever
        // its step admits seconds and HH:mm otherwise. Rejecting the first is
        // what answered a correctly entered time with "Enter a time as HH:MM".
        // The stored form is always HH:mm.
        => TimeOnly.TryParseExact(
            storedValue?.Trim(),
            new[] { "HH:mm", "HH:mm:ss" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var timeOfDay)
            ? timeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// The next local occurrence of a time of day, as UTC — the rule #157
    /// established: today if it is still ahead, otherwise tomorrow.
    /// </summary>
    public static DateTimeOffset NextOccurrenceUtc(string? timeOfDay, DateTime localNow)
    {
        var parsed = Parse(timeOfDay) ?? FallbackTimeOfDay;
        var time = TimeOnly.ParseExact(parsed, "HH:mm", CultureInfo.InvariantCulture);
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
        // Invariant for the same reason: the control speaks one format, not the
        // reader's.
        => instant.ToLocalTime().ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    /// <summary>The value for a datetime-local input's max attribute.</summary>
    public static string LocalInputValueIn(TimeSpan offset)
        => (DateTime.Now + offset).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// A datetime-local input's value back to an instant, or null when the user
    /// has cleared or half-typed it.
    /// </summary>
    public static DateTimeOffset? FromLocalInputValue(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Local)).ToUniversalTime()
            : null;
}
