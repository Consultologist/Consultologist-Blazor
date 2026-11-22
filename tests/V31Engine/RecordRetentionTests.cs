using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 11; D2, D10, D13; Review Focus 3): the 7-day text drop knows a 31 job's record.
/// The sweep seals first (a job nobody read gets its record before anything else happens to it), deletes the job's
/// <c>text/</c> blobs, then the entity stamps <c>TextDroppedAtUtc</c> as for any job. The rows and <c>record.json</c> stay.
/// A seal that fails keeps the text for the next pass. A job below storage 12 reaches no record store.
/// </summary>
public class RecordRetentionTests
{
    private const string User = "user-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 30, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheSweep_DeletesA31JobsText_AndKeepsItsRecord()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var rowsBefore = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);
        var textBefore = TextBlobs(job);
        Assert.NotEmpty(textBefore);
        var recordHash = job.State.RecordHash;
        Assert.NotNull(recordHash);

        var harness = new Harness(job);
        var dropped = await harness.SweepAsync();

        Assert.Equal(1, dropped);
        Assert.Empty(TextBlobs(job));
        Assert.Equal(rowsBefore, await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None));
        var record = await job.Blobs.ReadRecordAsync(V31Job.AccountKind, User, V31Job.JobId, CancellationToken.None);
        Assert.Equal(recordHash, RecordDocument.Hash(record!));
        Assert.Equal(recordHash, job.State.RecordHash);
        Assert.NotNull(job.State.TextDroppedAtUtc);
        await harness.Client.Received(1).PurgeInstanceAsync(V31Job.JobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheSweep_SealsAJobNobodyRead()
    {
        // a scheduled 31 job called off, its seal not reached (the cancel route's best-effort step failed or never ran)
        var job = V31Job.Of("nested-note");
        job.State.Status = ConsultGenerationJobStatuses.Scheduled;
        await job.Entity.Cancel();
        Assert.Null(job.State.RecordHash);
        Assert.Null(await job.Blobs.ReadRecordAsync(V31Job.AccountKind, User, V31Job.JobId, CancellationToken.None));

        var harness = new Harness(job);
        Assert.Equal(1, await harness.SweepAsync());

        Assert.NotNull(job.State.RecordHash);
        var record = await job.Blobs.ReadRecordAsync(V31Job.AccountKind, User, V31Job.JobId, CancellationToken.None);
        Assert.Equal(job.State.RecordHash, RecordDocument.Hash(record!));
        Assert.NotNull(job.State.TextDroppedAtUtc);
    }

    [Fact]
    public async Task TheSweep_KeepsTheText_WhenSealingFails()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var textBefore = TextBlobs(job);
        Assert.NotEmpty(textBefore);
        var sealer = Substitute.For<IRecordSealer>();
        sealer.EnsureSealedAsync(default!, default).ReturnsForAnyArgs<Task<SealedRecord?>>(_ => throw new InvalidOperationException("Okafor's referral text"));

        var harness = new Harness(job, sealer);
        var dropped = await harness.SweepAsync();

        Assert.Equal(0, dropped);
        Assert.Equal(textBefore, TextBlobs(job));
        Assert.Null(job.State.TextDroppedAtUtc);
        // nothing else happened to the job: its instance and events stay for the next pass
        await harness.Client.DidNotReceiveWithAnyArgs().PurgeInstanceAsync(default!, default);
        await harness.Events.DidNotReceiveWithAnyArgs().DeleteJobAsync(default!, default);
        // a warning with the job and the exception's type, never its message
        var warning = Assert.Single(harness.SweepLog.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(V31Job.JobId, warning.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(InvalidOperationException).FullName!, warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Okafor", harness.SweepLog.Everything, StringComparison.Ordinal);

        // the next pass, the seal working: the text goes
        var next = new Harness(job);
        Assert.Equal(1, await next.SweepAsync());
        Assert.Empty(TextBlobs(job));
        Assert.NotNull(job.State.TextDroppedAtUtc);
    }

    [Fact]
    public async Task TheSweep_IsSafeToRunTwice()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var harness = new Harness(job);

        Assert.Equal(1, await harness.SweepAsync());
        var (hash, droppedAt) = (job.State.RecordHash, job.State.TextDroppedAtUtc);
        var rows = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);
        var stamps = job.RecordJobs.Stamps.Count;

        // the index still lists it (a stale read): the purge runs again over nothing
        Assert.Equal(1, await harness.SweepAsync());

        Assert.Equal((hash, droppedAt), (job.State.RecordHash, job.State.TextDroppedAtUtc));
        Assert.Equal(rows, await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None));
        Assert.Equal(stamps, job.RecordJobs.Stamps.Count);
        Assert.Empty(TextBlobs(job));
        Assert.NotNull(await job.Blobs.ReadRecordAsync(V31Job.AccountKind, User, V31Job.JobId, CancellationToken.None));
    }

    [Fact]
    public async Task TheSweep_LeavesA30JobAsToday()
    {
        // through the sweep and the real purger and record drop: the 30 job's text goes by the entity alone
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        job.State.SchemaVersion = 11;
        var sealer = Substitute.For<IRecordSealer>();
        var blobs = Substitute.For<IRecordBlobStore>();
        var rowCalls = job.Rows.Table.Calls;
        var harness = new Harness(job, sealer, blobs);

        Assert.Equal(1, await harness.SweepAsync());

        await sealer.DidNotReceiveWithAnyArgs().EnsureSealedAsync(default!, default);
        Assert.Empty(blobs.ReceivedCalls());
        Assert.Equal(rowCalls, job.Rows.Table.Calls);
        Assert.NotNull(job.State.TextDroppedAtUtc);
        await harness.Client.Received(1).PurgeInstanceAsync(V31Job.JobId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AJobBelowStorage12_ReachesNoRecordStore()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        job.State.SchemaVersion = 11;
        var sealer = Substitute.For<IRecordSealer>();
        var blobs = Substitute.For<IRecordBlobStore>();
        var rowCalls = job.Rows.Table.Calls;

        await RecordTextDrop.DropAsync(sealer, job.RecordJobs, blobs, V31Job.JobId, CancellationToken.None);

        await sealer.DidNotReceiveWithAnyArgs().EnsureSealedAsync(default!, default);
        Assert.Empty(blobs.ReceivedCalls());
        Assert.Equal(rowCalls, job.Rows.Table.Calls);
    }

    [Fact]
    public async Task TheRecordsBlobs_SitUnderTheAccountsPrefix_InTheOutputsContainer()
    {
        // account closure removes every blob under {appUserId}/ in both outputs containers (JobOutputsBlobStore.DeleteAccountAsync);
        // the record's text and record.json are named under it, in the container the job's account kind names
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var names = job.Blobs.Blobs.Names();
        Assert.Contains(names, n => n.Name.Contains("/text/", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Name.EndsWith("/record.json", StringComparison.Ordinal));
        Assert.All(names, n =>
        {
            Assert.StartsWith($"{User}/", n.Name, StringComparison.Ordinal);
            Assert.Equal(JobOutputsBlobStore.ContainerFor(V31Job.AccountKind), n.Container);
        });

        await job.Blobs.Blobs.DeleteByPrefixAsync(JobOutputsBlobStore.ContainerFor(V31Job.AccountKind), $"{User}/", CancellationToken.None);

        Assert.Empty(job.Blobs.Blobs.Names());
    }

    private static IReadOnlyList<string> TextBlobs(V31Job job) =>
        job.Blobs.Blobs.Names().Where(n => n.Name.Contains("/text/", StringComparison.Ordinal)).Select(n => n.Name).ToList();

    /// <summary>The sweep over one job of the real entity, the real purger and the real record drop, on the fakes.</summary>
    private sealed class Harness
    {
        public Harness(V31Job job, IRecordSealer? sealer = null, IRecordBlobStore? blobs = null)
        {
            Events = Substitute.For<IConsultGenerationJobEventStore>();
            Client = Substitute.For<DurableTaskClient>("test");
            var entities = Substitute.For<DurableEntityClient>("test");
            Client.Entities.Returns(entities);
            entities.SignalEntityAsync(default!, default!, default, default, default)
                .ReturnsForAnyArgs(call => job.Entity.DropText((ConsultGenerationTextDrop)call.ArgAt<object?>(2)!));

            var drop = new Drop(sealer ?? job.Sealer, job, blobs ?? job.Blobs);
            var purger = new JobTextPurger(Events, Substitute.For<ILegacyJobEventDelete>(), drop);

            var accounts = Substitute.For<IAccountStore>();
            accounts.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<AccountSummary> { new(User, "Active") });
            var index = Substitute.For<IConsultGenerationJobIndexStore>();
            var entry = new ConsultGenerationJobIndexEntry(V31Job.JobId, User, job.State.Status, Now.AddDays(-9), null, Now.AddDays(-8), 1, 1, 0);
            index.ListDueForTextDropAsync(User, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new List<ConsultGenerationJobIndexEntry> { entry });
            var responses = Substitute.For<Consultologist.Api.Forms.IFormResponseStore>();
            responses.ListDueAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
                .Returns(new List<Consultologist.Api.Forms.FormResponseRow>());
            Sweep = new TextRetentionSweep(
                accounts, index, purger, Substitute.For<IAccountSettingsStore>(), responses,
                Substitute.For<Consultologist.Api.Forms.IFormResponsePurger>(), Substitute.For<IAccountRateLimiter>(),
                Substitute.For<IAccountUsageStore>(), Substitute.For<ILinkedInLinkStateStore>(), SweepLog);
        }

        public IConsultGenerationJobEventStore Events { get; }

        public DurableTaskClient Client { get; }

        public CapturingLogger<TextRetentionSweep> SweepLog { get; } = new();

        private TextRetentionSweep Sweep { get; }

        public async Task<int> SweepAsync() =>
            (await Sweep.RunOnceAsync(Client, Now, 7, CancellationToken.None)).Dropped;
    }

    /// <summary>The host's <see cref="RecordTextDrop"/> over the fakes: the same steps, the job's entity read as the client would.</summary>
    private sealed class Drop(IRecordSealer sealer, V31Job job, IRecordBlobStore blobs) : IRecordTextDrop
    {
        public Task DropAsync(DurableTaskClient client, string jobId, CancellationToken ct) =>
            RecordTextDrop.DropAsync(sealer, job.RecordJobs, blobs, jobId, ct);
    }
}
