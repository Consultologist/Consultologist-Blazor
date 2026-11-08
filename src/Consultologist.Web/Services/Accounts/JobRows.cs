namespace Consultologist.Web.Services.Accounts;

/// <summary>
/// #1326: one History row per job. Before 13a33672 (2026-09-03) a second
/// Initialize reset a job's CreatedAtUtc, and the index — keyed by it — gained a
/// stale Queued twin beside the finished row; a page that listed both polled the
/// twin forever. The finished row wins; with neither finished, the later one.
/// </summary>
public static class JobRows
{
    public static IReadOnlyList<AccountJobSummaryResponse> OnePerJob(IReadOnlyList<AccountJobSummaryResponse> rows)
    {
        var kept = new List<AccountJobSummaryResponse>(rows.Count);
        var slotByJob = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!slotByJob.TryGetValue(row.JobId, out var slot))
            {
                slotByJob[row.JobId] = kept.Count;
                kept.Add(row);
            }
            else if (Prefer(row, kept[slot]))
            {
                kept[slot] = row;
            }
        }

        return kept;
    }

    private static bool Prefer(AccountJobSummaryResponse candidate, AccountJobSummaryResponse held) =>
        IsFinished(candidate.Status) != IsFinished(held.Status)
            ? IsFinished(candidate.Status)
            : candidate.CreatedAtUtc > held.CreatedAtUtc;

    private static bool IsFinished(string status) => status is "Completed" or "Failed" or "Cancelled";
}
