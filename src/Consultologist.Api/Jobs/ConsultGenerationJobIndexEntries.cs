namespace Consultologist.Api.Jobs;

/// <summary>
/// #1326: one Account/Jobs row per job. Before 13a33672 (2026-09-03) a second
/// Initialize reset a job's CreatedAtUtc, and the index — keyed by it — gained a
/// stale Queued twin beside the finished row. Within a page the finished row
/// wins; with neither finished, the later one. A pair split across pages is the
/// client's to collapse (History's JobRows).
/// </summary>
public static class ConsultGenerationJobIndexEntries
{
    public static IReadOnlyList<ConsultGenerationJobIndexEntry> OnePerJob(IReadOnlyList<ConsultGenerationJobIndexEntry> page)
    {
        var kept = new List<ConsultGenerationJobIndexEntry>(page.Count);
        var slotByJob = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in page)
        {
            if (!slotByJob.TryGetValue(entry.JobId, out var slot))
            {
                slotByJob[entry.JobId] = kept.Count;
                kept.Add(entry);
            }
            else if (Prefer(entry, kept[slot]))
            {
                kept[slot] = entry;
            }
        }

        return kept.Count == page.Count ? page : kept;
    }

    private static bool Prefer(ConsultGenerationJobIndexEntry candidate, ConsultGenerationJobIndexEntry held) =>
        IsFinished(candidate.Status) != IsFinished(held.Status)
            ? IsFinished(candidate.Status)
            : candidate.CreatedAtUtc > held.CreatedAtUtc;

    private static bool IsFinished(string status) =>
        status is ConsultGenerationJobStatuses.Completed or ConsultGenerationJobStatuses.Failed or ConsultGenerationJobStatuses.Cancelled;
}
