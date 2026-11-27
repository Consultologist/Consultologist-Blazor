namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1509 (Task 14, D9): whether a job is shown as stopped. A job that is not terminal, has begun, and began more than
/// <see cref="StoppedAfterMinutes"/> ago (part 09 § 3.6's hard cap, which already covers the job's and its delivery's minutes
/// and the margin) is stopped. The stored status is never changed; the page only says A12-001 and stops polling fast.
/// </summary>
public static class JobLiveness31
{
    public const int StoppedAfterMinutes = 60;

    /// <summary>The status a 31 job holds while it delivers (part 09 § 3.8); not terminal.</summary>
    public const string DeliveringStatus = "delivering";

    /// <summary>The terminal statuses (part 08 § 1.2: a sealed record's <c>status</c>): Completed, Failed and Cancelled.</summary>
    public static bool IsTerminal(string? status) => status is "Completed" or "Failed" or "Cancelled";

    public static bool IsStopped(string? status, DateTimeOffset? startedAtUtc, DateTimeOffset now) =>
        !IsTerminal(status)
        && startedAtUtc is { } started
        && now - started > TimeSpan.FromMinutes(StoppedAfterMinutes);
}
