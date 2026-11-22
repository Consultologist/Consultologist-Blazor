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

    // ---- #1506 (final review F5): the record storage column ---------------------------------------

    /// <summary>The index row's properties as they were before the record storage column (the table binder writes the static type's).</summary>
    private static readonly string[] RowProperties =
    [
        "PartitionKey", "RowKey", "Timestamp", "ETag", "JobId", "Status", "CreatedAtUtc", "StartedAtUtc", "CompletedAtUtc",
        "TotalBlockCount", "CompletedBlockCount", "FailedBlockCount", "Source", "ScheduledAtUtc", "FailedAtStart",
        "TextDroppedAtUtc", "DeliveryOutcome", "DeliveredAtUtc", "Deciding", "DecisionFailureKind", "InputsHeld",
        "InputsDroppedAtUtc",
    ];

    [Fact]
    public void A30JobsIndexRow_IsWrittenWithoutTheRecordStorageColumn()
    {
        var entity = TableConsultGenerationJobIndexStore.ToEntity(Entry("j", ConsultGenerationJobStatuses.Completed, At));

        Assert.Equal(typeof(ConsultGenerationJobIndexEntity), entity.GetType());
        Assert.Equal(RowProperties.Order(StringComparer.Ordinal), typeof(ConsultGenerationJobIndexEntity).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal));
        // nor does its JSON (the job list's) name it
        Assert.DoesNotContain("RecordStorage", System.Text.Json.JsonSerializer.Serialize(Entry("j", ConsultGenerationJobStatuses.Completed, At)), StringComparison.Ordinal);
    }

    [Fact]
    public void AStorage12JobsIndexRow_CarriesItsRecordStorage_AndReadsBack()
    {
        var entry = Entry("j", ConsultGenerationJobStatuses.Completed, At) with { RecordStorage = 12 };

        var entity = Assert.IsType<ConsultGenerationJobRecordIndexEntity>(TableConsultGenerationJobIndexStore.ToEntity(entry));

        Assert.Equal(12, entity.RecordStorage);
        Assert.Equal(entry, TableConsultGenerationJobIndexStore.ToEntry(entity));
        // a row from before the column reads null
        var old = new ConsultGenerationJobRecordIndexEntity { JobId = "j", PartitionKey = "user-1", Status = ConsultGenerationJobStatuses.Completed, CreatedAtUtc = At, TotalBlockCount = 9 };
        Assert.Null(TableConsultGenerationJobIndexStore.ToEntry(old).RecordStorage);
    }

    [Theory]
    [InlineData(31, 12)]
    [InlineData(30, null)]
    [InlineData(26, null)]
    public void TheEntity_WritesRecordStorage_OnlyForAStorage12Job(int specVersion, int? expected)
    {
        var state = ConsultGenerationJobState.Create("j", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>());
        state.PackageSpecVersion = specVersion;
        ConsultGenerationJobEntity.Stamp(state, 7);

        Assert.Equal(expected, state.ToIndexEntry().RecordStorage);
    }
}
