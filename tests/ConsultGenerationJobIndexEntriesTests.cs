using Consultologist.Api.Jobs;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1326: before 13a33672 (2026-09-03) a second Initialize reset a job's
/// CreatedAtUtc, and the index — keyed by it — gained a second row for the same
/// job: a stale Queued twin beside the finished one. Account/Jobs answers one
/// row per job, the finished one.
/// </summary>
public class ConsultGenerationJobIndexEntriesTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 3, 19, 9, 26, TimeSpan.Zero);

    private static ConsultGenerationJobIndexEntry Entry(string jobId, string status, DateTimeOffset created) =>
        new(jobId, "user-1", status, created, null, null, 9, 0, 0);

    [Fact]
    public void ATwinnedJob_AnswersItsFinishedRow_InPageOrder()
    {
        var page = new[]
        {
            Entry("later", ConsultGenerationJobStatuses.Completed, At.AddDays(1)),
            Entry("c30eb52f", ConsultGenerationJobStatuses.Queued, At.AddSeconds(3)),
            Entry("c30eb52f", ConsultGenerationJobStatuses.Failed, At),
        };

        var kept = ConsultGenerationJobIndexEntries.OnePerJob(page);

        Assert.Equal(new[] { "later", "c30eb52f" }, kept.Select(entry => entry.JobId));
        Assert.Equal(ConsultGenerationJobStatuses.Failed, kept[1].Status);
    }

    [Fact]
    public void NeitherFinished_KeepsTheLaterRow()
    {
        var kept = ConsultGenerationJobIndexEntries.OnePerJob(new[]
        {
            Entry("j", ConsultGenerationJobStatuses.Queued, At),
            Entry("j", ConsultGenerationJobStatuses.Running, At.AddSeconds(2)),
        });

        Assert.Equal(ConsultGenerationJobStatuses.Running, Assert.Single(kept).Status);
    }

    [Fact]
    public void APageWithNoTwins_IsReturnedAsIs()
    {
        var page = new[] { Entry("a", ConsultGenerationJobStatuses.Completed, At), Entry("b", ConsultGenerationJobStatuses.Queued, At) };

        Assert.Equal(page, ConsultGenerationJobIndexEntries.OnePerJob(page));
    }
}
