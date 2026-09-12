namespace Consultologist.Web.Services;

/// <summary>
/// #684: formats a run's elapsed wall-clock time for the "Generating…" readout.
/// Runs are minutes, so the default is m:ss; an hour-plus run rolls to h:mm:ss
/// rather than showing "60:00".
/// </summary>
internal static class ElapsedTime
{
    public static string Format(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");
}
