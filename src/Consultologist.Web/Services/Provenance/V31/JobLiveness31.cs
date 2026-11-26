namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1509 (Task 14, D9): whether a job is shown as stopped. A job that is not terminal, has begun, and began more than
/// <see cref="StoppedAfterMinutes"/> ago (part 09 § 3.6's hard cap, which already covers the job's and its delivery's minutes
/// and the margin) is stopped. The stored status is never changed; the page only says A12-001 and stops polling fast.
/// </summary>
public static class JobLiveness31
{
    public const int StoppedAfterMinutes = 60;

    public static bool IsStopped(string? status, DateTimeOffset? startedAtUtc, DateTimeOffset now) =>
        status is not ("Completed" or "Failed" or "Cancelled")
        && startedAtUtc is { } started
        && now - started > TimeSpan.FromMinutes(StoppedAfterMinutes);
}
