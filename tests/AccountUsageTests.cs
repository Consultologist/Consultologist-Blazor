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

    // ----- the served window: defaults, refusals by name, the read-side clamp -----

    private static readonly DateTimeOffset Now = new(2026, 9, 15, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingAsked_ServesTheLastThirtyDays_EndingToday()
    {
        var (from, to, error) = Consultologist.Api.Account.ResolveUsageWindow(null, null, Now);

        Assert.Null(error);
        Assert.Equal(("2026-08-17", "2026-09-15"), (from, to));
    }

    [Fact]
    public void AChosenRange_IsServedAsAsked()
    {
        var (from, to, error) = Consultologist.Api.Account.ResolveUsageWindow("2026-09-01", "2026-09-07", Now);

        Assert.Null(error);
        Assert.Equal(("2026-09-01", "2026-09-07"), (from, to));
    }

    [Theory]
    [InlineData("not-a-date", null, "from must be a date as yyyy-MM-dd.")]
    [InlineData(null, "01/09/2026", "to must be a date as yyyy-MM-dd.")]
    [InlineData("2026-09-07", "2026-09-01", "from must not be after to.")]
    public void ABadWindow_RefusesByName(string? from, string? to, string expected)
    {
        var (_, _, error) = Consultologist.Api.Account.ResolveUsageWindow(from, to, Now);

        Assert.Equal(expected, error);
    }

    [Fact]
    public void AnOversizedWindow_ClampsInsteadOfRefusing()
    {
        // Reads never refuse for asking too much history — the window pulls
        // its start up to the 92-day ceiling (M6's cleanup rule horizon).
        var (from, to, error) = Consultologist.Api.Account.ResolveUsageWindow("2026-01-01", "2026-09-15", Now);

        Assert.Null(error);
        Assert.Equal("2026-09-15", to);
        Assert.Equal("2026-06-16", from); // 92 days inclusive
    }

    // ----- #553: the operator join, pure -----

    [Fact]
    public void TheOperatorRows_SumPerUser_AndJoinWhatTheRecordCarries()
    {
        var usage = new[]
        {
            new AccountUsageDay("user-1", "2026-09-01", 2, 2000, 600),
            new AccountUsageDay("user-1", "2026-09-02", 1, 1000, 300),
            new AccountUsageDay("user-2", "2026-09-01", 5, 9000, 2500)
        };
        var directory = new[]
        {
            new Consultologist.Api.Auth.AccountDirectoryEntry("user-1", "Dr One", "Active", "organisation"),
            new Consultologist.Api.Auth.AccountDirectoryEntry("user-2", "Dr Two", "Active", "personal"),
            new Consultologist.Api.Auth.AccountDirectoryEntry("user-3", "Dr Idle", "Active", null)
        };
        var tenants = new Dictionary<string, string?>
        {
            ["user-1"] = "tenant-a",
            ["user-2"] = "9188040d-6c67-4c5b-b112-36a304b66dad"
        };

        var rows = Consultologist.Api.Workflow.OperatorUsage.RowsOf(usage, directory, tenants);

        Assert.Equal(2, rows.Count);
        // One user's numbers never bleed into another's.
        Assert.Equal(
            new Consultologist.Api.Workflow.OperatorUsageRowResponse("user-1", "Dr One", "organisation", "tenant-a", 3, 3000, 900),
            rows[0]);
        Assert.Equal(
            new Consultologist.Api.Workflow.OperatorUsageRowResponse("user-2", "Dr Two", "personal", "9188040d-6c67-4c5b-b112-36a304b66dad", 5, 9000, 2500),
            rows[1]);
        // An account with no usage in the window does not appear.
        Assert.DoesNotContain(rows, row => row.AppUserId == "user-3");
    }

    [Fact]
    public void ARowWithoutDirectoryOrTenant_StaysHonest()
    {
        var usage = new[] { new AccountUsageDay("ghost", "2026-09-01", 1, 100, 50) };

        var row = Assert.Single(Consultologist.Api.Workflow.OperatorUsage.RowsOf(
            usage, Array.Empty<Consultologist.Api.Auth.AccountDirectoryEntry>(), new Dictionary<string, string?>()));

        Assert.Equal(string.Empty, row.DisplayName);
        Assert.Null(row.TenantId);
        Assert.Equal((1, 100, 50), (row.ConsultsCompleted, row.TokensIn, row.TokensOut));
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
