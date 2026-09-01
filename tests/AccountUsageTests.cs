using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests;

/// <summary>
/// #552: the arithmetic, not the plumbing — the table round trip is untested
/// by construction (no Azurite in CI, the rate limiter's own rule), so the
/// pure seams carry the logic.
/// </summary>
public class AccountUsageTests
{
    [Fact]
    public void TheDayKey_IsTheUtcDay_WhateverTheOffset()
    {
        // The WindowKey pin's twin: two offsets naming the same UTC instant
        // land on the same row.
        var lateInHalifax = new DateTimeOffset(2026, 8, 31, 22, 30, 0, TimeSpan.FromHours(-3));
        var earlyInBerlin = new DateTimeOffset(2026, 9, 1, 3, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-09-01", TableAccountUsageStore.DayKey(lateInHalifax));
        Assert.Equal(TableAccountUsageStore.DayKey(lateInHalifax), TableAccountUsageStore.DayKey(earlyInBerlin));
    }

    [Fact]
    public void TheDayKey_IsInvariantAndSortable()
    {
        Assert.Equal("2026-01-05", TableAccountUsageStore.DayKey(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero)));
        // Ordinal order equals chronological order — what the RowKey range read rests on.
        Assert.True(string.CompareOrdinal("2026-01-05", "2026-01-31") < 0);
        Assert.True(string.CompareOrdinal("2026-01-31", "2026-02-01") < 0);
    }

    // ----- the write at finalize: exactly once, never failing the job -----

    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State, IAccountUsageStore Usage) Entity()
    {
        var usage = Substitute.For<IAccountUsageStore>();
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(),
            Substitute.For<IJobOutputsBlobStore>(),
            Substitute.For<IJobInputsBlobStore>(),
            usage);
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" }
        }));
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!, usage);
    }

    private static async Task RunWithTokensAsync(ConsultGenerationJobEntity entity)
    {
        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate(
            "extract", "Extract", null, "in1", "out1", 1, 1, 5, null, new ConsultTokenUsage(900, 200)));
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0));
    }

    [Fact]
    public async Task ACompletedRun_AddsOneConsultAndItsTokens_ToTheUtcDay()
    {
        var (entity, state, usage) = Entity();
        await RunWithTokensAsync(entity);

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        await usage.Received(1).AddAsync(
            "user-1",
            TableAccountUsageStore.DayKey(DateTimeOffset.UtcNow),
            1,
            new ConsultTokenUsage(900, 200),
            Arg.Any<CancellationToken>());
        Assert.NotNull(state().UsageRecordedAtUtc);
    }

    [Fact]
    public async Task AFailedRun_AddsItsTokens_ButNoConsult()
    {
        var (entity, _, usage) = Entity();
        entity.MarkNodeCompleted(new ConsultGenerationNodeUpdate(
            "extract", "Extract", null, "in1", "out1", 1, 2, 5, null, new ConsultTokenUsage(900, 200)));

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, "boom"));

        await usage.Received(1).AddAsync(
            "user-1", Arg.Any<string>(), 0, new ConsultTokenUsage(900, 200), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASecondFinalize_AddsNothingMore()
    {
        // The reply leg can re-finalize a job as Failed after a Completed
        // finalize already ran — the day row must never count one job twice.
        var (entity, _, usage) = Entity();
        await RunWithTokensAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, "reply leg failed"));

        await usage.Received(1).AddAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ConsultTokenUsage?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AUsageWriteFailure_NeverFailsTheFinalize_AndTheRecordSaysSo()
    {
        var (entity, state, usage) = Entity();
        await RunWithTokensAsync(entity);
        usage.AddAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<ConsultTokenUsage?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage blip"));

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        Assert.Equal(ConsultGenerationJobStatuses.Completed, state().Status);
        Assert.Contains(state().History, h => h.Kind == "storage" && h.Label.Contains("Usage row not written"));
        // No stamp: a later finalize may retry the write.
        Assert.Null(state().UsageRecordedAtUtc);
    }
}
