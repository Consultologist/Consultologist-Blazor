using System.Text;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 §§ 1.1, 3; part 09 §§ 3.7, 3.8; D13; Review Focus 3): the record is sealed at
/// every terminal state, once. A job that completed, failed, timed out, faulted, was cancelled or never started gets its
/// canonical document (<see cref="RecordDocument.Assemble"/> of its header and its stored rows) in <c>record.json</c> and
/// its <c>recordHash</c> and byte count on the entity; sealing again, or racing a seal whose stamp has not landed, gives
/// the same hash. A job still delivering, or a 5–30 job, is never sealed.
/// </summary>
public class RecordSealerTests
{
    private const string UserId = "user-1";

    [Fact]
    public async Task ACompletedJob_IsSealed()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        var document = await DocumentOf(job);
        var rows = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);
        Assert.NotEmpty(rows);
        Assert.Equal(RecordDocument.Assemble(job.State.ToRecordHeader(), rows), document);
        Assert.Equal(ConsultGenerationJobStatuses.Completed, (string?)JsonNode.Parse(document)!["status"]);
        Assert.Null(JsonNode.Parse(document)!["error"]);

        // on the entity: the document's hash and its UTF-8 byte count, written once
        Assert.Equal(RecordDocument.Hash(document), job.State.RecordHash);
        Assert.Equal(Encoding.UTF8.GetByteCount(document), job.State.RecordBytes);
        Assert.Single(job.RecordJobs.Stamps);

        // the log: the job, counts and the hash, never text
        var sealedLine = Assert.Single(job.SealLog.Entries, e => e.Message.StartsWith("V31 record sealed.", StringComparison.Ordinal));
        Assert.Equal(
            $"V31 record sealed. JobId={V31Job.JobId}, Rows={rows.Count}, Bytes={job.State.RecordBytes}, RecordHash={job.State.RecordHash}",
            sealedLine.Message);
        Assert.DoesNotContain("Okafor", job.SealLog.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SealingTwice_GivesTheSameHash()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var first = (job.State.RecordHash, job.State.RecordBytes);
        var listsBefore = job.Rows.Table.Calls;

        // a job already stamped: the stamp, without reading a row or a blob
        var again = await job.Sealer.EnsureSealedAsync(V31Job.JobId, CancellationToken.None);

        Assert.NotNull(again);
        Assert.Equal(first, (again!.RecordHash, (long?)again.Bytes));
        Assert.Equal(listsBefore, job.Rows.Table.Calls);
        Assert.Single(job.RecordJobs.Stamps);
    }

    [Fact]
    public async Task SealingTwice_GivesTheSameHash_WhenTheSecondRacesTheFirst()
    {
        // the first seal's blob is written; its stamp has not landed on the entity yet (it is a signal). A second sealer
        // finds no recordHash, assembles, finds the blob there, and takes that document's hash and length, not its own.
        var job = V31Job.Of("nested-note");
        job.Seal = _ => null; // the orchestration's seal never happened
        await V31Job.RunAsync(job.Context());
        Assert.Null(job.State.RecordHash);

        var raced = "{\"a\":\"the first sealer's bytes\"}";
        Assert.True(await job.Blobs.WriteRecordAsync(V31Job.AccountKind, UserId, V31Job.JobId, raced, CancellationToken.None));

        var second = await job.Sealer.EnsureSealedAsync(V31Job.JobId, CancellationToken.None);

        Assert.Equal(RecordDocument.Hash(raced), second!.RecordHash);
        Assert.Equal(Encoding.UTF8.GetByteCount(raced), second.Bytes);
        Assert.Equal(raced, await DocumentOf(job));
        Assert.Equal(RecordDocument.Hash(raced), job.State.RecordHash);

        // and once more: the same
        var third = await job.Sealer.EnsureSealedAsync(V31Job.JobId, CancellationToken.None);
        Assert.Equal(second, third);
    }

    [Fact]
    public async Task SealingTwice_BothRacing_StampsOneHash()
    {
        // two sealers, neither seeing the other's stamp: the second write finds the first's blob; both stamp its hash
        var job = V31Job.Of("nested-note");
        job.Seal = _ => null;
        await V31Job.RunAsync(job.Context());
        var unstamped = new EntityRecordJobs(job.Entity, V31Job.AccountKind);
        var blind = new UnstampedJobs(unstamped);

        var first = await new RecordSealer(blind, job.Rows, job.Blobs, NullLogger<RecordSealer>.Instance).EnsureSealedAsync(V31Job.JobId, CancellationToken.None);
        var second = await new RecordSealer(blind, job.Rows, job.Blobs, NullLogger<RecordSealer>.Instance).EnsureSealedAsync(V31Job.JobId, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(2, unstamped.Stamps.Count);
        Assert.Single(unstamped.Stamps.Distinct());
        Assert.Equal(first!.RecordHash, job.State.RecordHash);
    }

    [Fact]
    public async Task TheEntitysSeal_IsWriteOnce()
    {
        var logger = new CapturingLogger<ConsultGenerationJobEntity>();
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(),
            Substitute.For<IAccountUsageStore>(), logger);
        var jobs = new EntityRecordJobs(entity);
        await entity.RecordStartFailure(new ConsultGenerationJobStartFailure(Initialize(31), "The package could not be loaded."));

        await jobs.StampAsync(V31Job.JobId, "sha256:aaaa", 10, CancellationToken.None);
        await jobs.StampAsync(V31Job.JobId, "sha256:bbbb", 20, CancellationToken.None);

        var state = (await jobs.ReadAsync(V31Job.JobId, CancellationToken.None))!;
        Assert.Equal(("sha256:aaaa", 10L), (state.RecordHash, state.RecordBytes));
        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("sha256:aaaa", warning.Message, StringComparison.Ordinal);
        Assert.Contains("sha256:bbbb", warning.Message, StringComparison.Ordinal);

        // the same seal again is a no-op, and says nothing
        await jobs.StampAsync(V31Job.JobId, "sha256:aaaa", 10, CancellationToken.None);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task AFailedJob_IsSealed_WithItsEndState()
    {
        var job = V31Job.Of("nested-note");
        var stub = job.Agent;
        job.Agent = call => call.Key == "node.plan" ? new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 5000, "error") : stub(call);
        await V31Job.RunAsync(job.Context());

        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        var header = await AssertSealedAsync(job);
        Assert.Equal(ConsultGenerationJobStatuses.Failed, (string?)header["status"]);
        Assert.Equal("The model service returned an error 5 times in a row. Run the workflow again later.", (string?)header["error"]);
    }

    [Fact]
    public async Task ATimedOutJob_IsSealed_WithItsEndState()
    {
        var job = V31Job.Of("nested-note");
        var context = job.Context().WrapActivityFailures();
        await Task.Run(() =>
        {
            var run = V31Job.RunAsync(context.HoldActivities());
            while (context.ReleaseOldest((name, _) => name != ConsultGenerationActivityNames.RunAgentNodeV31))
            {
            }

            context.EndHeldTimers();
            while (context.HeldActivities > 0)
                context.ReleaseOldest();
            Assert.True(run.IsCompleted, "The orchestration has not ended: something it waits on was never released.");
            return run;
        });

        var header = await AssertSealedAsync(job);
        Assert.Equal(ConsultGenerationJobStatuses.Failed, (string?)header["status"]);
        Assert.Equal("The job didn't finish within 30 minutes. Run the workflow again later.", (string?)header["error"]);
    }

    [Fact]
    public async Task ACancelledJob_IsSealed_WithItsEndState()
    {
        // a scheduled 31 job called off: the entity's Cancel, no orchestration step after it, no rows
        var job = V31Job.Of("nested-note");
        job.State.SchemaVersion = 12;
        job.State.Status = ConsultGenerationJobStatuses.Scheduled;
        await job.Entity.Cancel();
        // the cancel route's own step, once the entity is cancelled
        await ConsultGenerationJobs.SealCancelledAsync(job.Sealer, job.State.SchemaVersion, V31Job.JobId, NullLogger.Instance, CancellationToken.None);

        var header = await AssertSealedAsync(job);
        Assert.Equal(ConsultGenerationJobStatuses.Cancelled, (string?)header["status"]);
        Assert.Empty(await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None));
    }

    [Fact]
    public async Task AJobThatNeverStarted_IsSealed_WithItsEndState()
    {
        // a start failure: no orchestration, no rows, no held inputs; the account's kind picks the container
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(),
            Substitute.For<IAccountUsageStore>());
        await entity.RecordStartFailure(new ConsultGenerationJobStartFailure(Initialize(31), "The package could not be loaded."));
        var jobs = new EntityRecordJobs(entity, Consultologist.Api.Auth.SignInKinds.Personal);
        var rows = new FakeRecordRowStore();
        var blobs = new FakeRecordBlobStore();

        var seal = await new RecordSealer(jobs, rows, blobs, NullLogger<RecordSealer>.Instance).EnsureSealedAsync(V31Job.JobId, CancellationToken.None);

        var document = await blobs.ReadRecordAsync(Consultologist.Api.Auth.SignInKinds.Personal, UserId, V31Job.JobId, CancellationToken.None);
        Assert.NotNull(document);
        Assert.Equal((RecordDocument.Hash(document!), (long)Encoding.UTF8.GetByteCount(document!), 0), (seal!.RecordHash, seal.Bytes, seal.Rows));
        var header = JsonNode.Parse(document!)!;
        Assert.Equal(12, (int?)header["schemaVersion"]);
        Assert.Equal(ConsultGenerationJobStatuses.Failed, (string?)header["status"]);
        Assert.Equal("The package could not be loaded.", (string?)header["error"]);
        Assert.Empty(header["nodeOutputs"]!.AsObject());
        Assert.Empty(header["deliverables"]!.AsArray());
        var state = (await jobs.ReadAsync(V31Job.JobId, CancellationToken.None))!;
        Assert.Equal(seal.RecordHash, state.RecordHash);
        Assert.Equal(1, jobs.KindReads);
    }

    [Fact]
    public async Task ADeliveringJob_IsNotSealed_UntilDeliveryEnds()
    {
        // checked-note delivers the patient letter by email and PDF; while it is delivering a seal finds it not terminal
        var job = V31Job.Of("checked-note");
        var whileDelivering = new List<(string Status, SealedRecord? Seal, bool Blob)>();
        job.Deliver = _ =>
        {
            var seal = job.Sealer.EnsureSealedAsync(V31Job.JobId, CancellationToken.None).GetAwaiter().GetResult();
            whileDelivering.Add((job.State.Status, seal, HasRecordBlob(job)));
            return new DeliverySendOutcome(DeliverySendResult.Sent, null);
        };

        await V31Job.RunAsync(job.Context());

        Assert.NotEmpty(whileDelivering);
        Assert.All(whileDelivering, d => Assert.Equal((ConsultGenerationJobStatuses.Delivering, (SealedRecord?)null, false), d));

        // sealed once delivery ended: each delivery row under its deliverable
        var header = await AssertSealedAsync(job);
        Assert.Equal(ConsultGenerationJobStatuses.Completed, (string?)header["status"]);
        var letter = header["deliverables"]!.AsArray().Single(d => (string?)d!["key"] == "deliverable.patient.patient_letter")!;
        Assert.Equal(
            ["delivery:deliverable.patient.patient_letter/email", "delivery:deliverable.patient.patient_letter/pdf"],
            letter["deliveries"]!.AsArray().Select(d => (string?)d!["key"]));
        Assert.All(letter["deliveries"]!.AsArray(), d => Assert.Equal("sent", (string?)d!["result"]));
    }

    [Theory]
    [InlineData(30, ConsultGenerationJobStatuses.Completed)]
    [InlineData(30, ConsultGenerationJobStatuses.Failed)]
    [InlineData(26, ConsultGenerationJobStatuses.Cancelled)]
    public async Task A30Job_IsNeverSealed(int specVersion, string status)
    {
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(),
            Substitute.For<IAccountUsageStore>());
        await entity.Initialize(Initialize(specVersion));
        var jobs = new EntityRecordJobs(entity, V31Job.AccountKind);
        var state = (await jobs.ReadAsync(V31Job.JobId, CancellationToken.None))!;
        state.Status = status;
        Assert.True(state.SchemaVersion < 12);
        var rows = new FakeRecordRowStore();
        var blobs = new FakeRecordBlobStore();

        Assert.Null(await new RecordSealer(jobs, rows, blobs, NullLogger<RecordSealer>.Instance).EnsureSealedAsync(V31Job.JobId, CancellationToken.None));

        // nothing touched: no row read, no blob, no stamp, not even the account's kind
        Assert.Equal(0, rows.Table.Calls);
        Assert.Equal(0, blobs.Blobs.Calls);
        Assert.Empty(jobs.Stamps);
        Assert.Equal(0, jobs.KindReads);
        Assert.Null(state.RecordHash);
        Assert.Null(state.RecordBytes);

        // nor does the entity take a seal below 31
        await jobs.StampAsync(V31Job.JobId, "sha256:aaaa", 10, CancellationToken.None);
        Assert.Null(state.RecordHash);
    }

    [Fact]
    public async Task AJobThatDoesNotExist_OrIsRunning_IsNotSealed()
    {
        var job = V31Job.Of("nested-note");
        job.State.SchemaVersion = 12;
        job.State.Status = ConsultGenerationJobStatuses.Running;

        Assert.Null(await job.Sealer.EnsureSealedAsync(V31Job.JobId, CancellationToken.None));
        job.RecordJobs.Missing = true;
        Assert.Null(await job.Sealer.EnsureSealedAsync(V31Job.JobId, CancellationToken.None));

        Assert.Equal(0, job.Blobs.Blobs.Calls);
        Assert.Empty(job.RecordJobs.Stamps);
    }

    [Fact]
    public async Task AnEngineFault_StillSeals()
    {
        // the orchestration's catch: the job failed with A06-010, and the record is sealed with that end state
        var job = V31Job.Of("nested-note");
        job.Agent = _ => throw new InvalidOperationException("Unauthorized: Margaret Okafor's prompt (fictional)");
        var context = job.Context().WrapActivityFailures();

        await Assert.ThrowsAsync<Microsoft.DurableTask.TaskFailedException>(() => V31Job.RunAsync(context));

        Assert.Equal(ConsultGenerationOrchestratorV31.EngineFault, job.State.FailureError);
        var header = await AssertSealedAsync(job);
        Assert.Equal(ConsultGenerationJobStatuses.Failed, (string?)header["status"]);
        Assert.Equal(ConsultGenerationOrchestratorV31.EngineFault, (string?)header["error"]);
        Assert.DoesNotContain("Okafor", await DocumentOf(job), StringComparison.Ordinal);
        var seal = context.Calls.FindIndex(c => c.Name == ConsultGenerationActivityNames.SealRecordV31);
        Assert.True(seal > context.Calls.FindLastIndex(c => c.Name == nameof(ConsultGenerationJobEntity.FinalizeJob)));
    }

    [Fact]
    public async Task AnEngineFault_WithASealerThatThrows_KeepsTheJobsError()
    {
        var job = V31Job.Of("nested-note");
        job.Agent = _ => throw new InvalidOperationException("Unauthorized: Margaret Okafor's prompt (fictional)");
        job.Seal = _ => throw new InvalidOperationException("The record's container is down.");
        var context = job.Context().WrapActivityFailures();

        var fault = await Assert.ThrowsAsync<Microsoft.DurableTask.TaskFailedException>(() => V31Job.RunAsync(context));

        // the fault the orchestration rethrows is the agent's, never the seal's
        Assert.Equal(ConsultGenerationActivityNames.RunAgentNodeV31, fault.TaskName);
        Assert.Equal(ConsultGenerationJobStatuses.Failed, job.State.Status);
        Assert.Equal(ConsultGenerationOrchestratorV31.EngineFault, job.State.FailureError);
        Assert.Null(job.State.RecordHash);
        Assert.Single(context.Calls, c => c.Name == ConsultGenerationActivityNames.SealRecordV31);
        // the alert still goes, after the seal
        Assert.Single(job.Alerts);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task ASealerThatThrows_NeverChangesTheJob(string how)
    {
        var job = V31Job.Of("nested-note");
        if (how == "failed")
        {
            var stub = job.Agent;
            job.Agent = call => call.Key == "node.plan" ? new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 5000, "error") : stub(call);
        }

        job.Seal = _ => throw new InvalidOperationException("The record's container is down.");
        var context = job.Context().WrapActivityFailures();

        await V31Job.RunAsync(context);

        Assert.Equal(how == "failed" ? ConsultGenerationJobStatuses.Failed : ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Equal(
            how == "failed" ? "The model service returned an error 5 times in a row. Run the workflow again later." : null,
            job.State.FailureError);
        Assert.Null(job.State.RecordHash);
        Assert.Single(context.Calls, c => c.Name == ConsultGenerationActivityNames.SealRecordV31);
    }

    [Fact]
    public async Task ARecordTheRowsCannotMake_IsAnEngineFault_LoggedAndThrown()
    {
        // a delivery whose deliverable has no row: Assemble refuses it; the sealer logs the job and the type, and throws
        var job = V31Job.Of("nested-note");
        job.Seal = _ => null;
        await V31Job.RunAsync(job.Context());
        await job.Rows.WriteAsync(V31Job.JobId,
            [new RecordRow("delivery", "delivery:deliverable.gone/email", "{\"deliverable\":\"deliverable.gone\",\"key\":\"delivery:deliverable.gone/email\"}")],
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.Sealer.EnsureSealedAsync(V31Job.JobId, CancellationToken.None));

        Assert.Equal(
            $"V31 record could not be sealed. JobId={V31Job.JobId}, ExceptionType={typeof(InvalidOperationException).FullName}",
            Assert.Single(job.SealLog.Entries, e => e.Level == LogLevel.Error).Message);
        Assert.False(HasRecordBlob(job));
        Assert.Null(job.State.RecordHash);
    }

    [Fact]
    public void TheHeldInputsContainer_NamesTheAccountKind()
    {
        var state = ConsultGenerationJobState.Create(V31Job.JobId, UserId, Array.Empty<IReadOnlyDictionary<string, string>>());
        Assert.Null(DurableRecordJobs.KindOfHeldInputs(state));
        state.InputsBlob = JobInputsBlobStore.PointerFor(Consultologist.Api.Auth.SignInKinds.Organisation, UserId, V31Job.JobId);
        Assert.Equal(Consultologist.Api.Auth.SignInKinds.Organisation, DurableRecordJobs.KindOfHeldInputs(state));
        state.InputsBlob = JobInputsBlobStore.PointerFor(Consultologist.Api.Auth.SignInKinds.Personal, UserId, V31Job.JobId);
        Assert.Equal(Consultologist.Api.Auth.SignInKinds.Personal, DurableRecordJobs.KindOfHeldInputs(state));
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(11, false)]
    [InlineData(null, false)]
    public async Task TheCancelRoute_SealsA31Job_BestEffort(int? schemaVersion, bool seals)
    {
        var sealer = Substitute.For<IRecordSealer>();
        await ConsultGenerationJobs.SealCancelledAsync(sealer, schemaVersion, V31Job.JobId, NullLogger.Instance, CancellationToken.None);
        await sealer.Received(seals ? 1 : 0).EnsureSealedAsync(V31Job.JobId, Arg.Any<CancellationToken>());

        // a sealer that throws is logged, never thrown: the cancel already happened
        var throwing = Substitute.For<IRecordSealer>();
        throwing.EnsureSealedAsync(default!, default).ReturnsForAnyArgs<Task<SealedRecord?>>(_ => throw new InvalidOperationException("down"));
        var logger = new CapturingLogger<ConsultGenerationJobs>();
        await ConsultGenerationJobs.SealCancelledAsync(throwing, schemaVersion, V31Job.JobId, logger, CancellationToken.None);
        Assert.Equal(seals ? 1 : 0, logger.Entries.Count(e => e.Level == LogLevel.Warning));

        // no sealer (a host without one): nothing
        await ConsultGenerationJobs.SealCancelledAsync(null, schemaVersion, V31Job.JobId, logger, CancellationToken.None);
    }

    // ---- #1506 (Task 15): a sealed record whose document is gone is restored from its rows -------------------------------

    [Fact]
    public async Task AMissingDocument_IsRestoredFromItsRows()
    {
        var job = V31Job.Of("checked-note");
        await V31Job.RunAsync(job.Context());
        var original = await DocumentOf(job);
        var stamp = (job.State.RecordHash, job.State.RecordBytes);
        Assert.True(job.Blobs.RemoveRecord(V31Job.AccountKind, UserId, V31Job.JobId));

        var restored = await job.Sealer.ReadSealedAsync(V31Job.JobId, CancellationToken.None);

        Assert.Equal(original, restored);
        Assert.Equal(original, await DocumentOf(job));
        Assert.Equal(stamp, (job.State.RecordHash, job.State.RecordBytes));
        Assert.Single(job.RecordJobs.Stamps);
        Assert.Equal(
            $"V31 record restored. JobId={V31Job.JobId}, Bytes={stamp.RecordBytes}, RecordHash={stamp.RecordHash}",
            Assert.Single(job.SealLog.Entries, e => e.Message.StartsWith("V31 record restored.", StringComparison.Ordinal)).Message);

        // read again: the blob, as it is, and no second restore
        Assert.Equal(original, await job.Sealer.ReadSealedAsync(V31Job.JobId, CancellationToken.None));
        Assert.Single(job.SealLog.Entries, e => e.Message.StartsWith("V31 record restored.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadSealed_SealsAJobNotSealedYet_AndIsNullForOneThatCannotBe()
    {
        var job = V31Job.Of("nested-note");
        job.Seal = _ => null;
        await V31Job.RunAsync(job.Context());
        Assert.Null(job.State.RecordHash);

        var document = await job.Sealer.ReadSealedAsync(V31Job.JobId, CancellationToken.None);

        Assert.Equal(await DocumentOf(job), document);
        Assert.Equal(RecordDocument.Hash(document!), job.State.RecordHash);

        var running = V31Job.Of("nested-note");
        running.State.Status = ConsultGenerationJobStatuses.Delivering;
        Assert.Null(await running.Sealer.ReadSealedAsync(V31Job.JobId, CancellationToken.None));
        Assert.Equal(0, running.Blobs.Blobs.Calls);
    }

    [Fact]
    public async Task ARestoredDocument_AfterTheTextDrop_StillHashesToTheStamp()
    {
        var job = V31Job.Of("checked-note");
        await V31Job.RunAsync(job.Context());
        var original = await DocumentOf(job);
        var stamp = job.State.RecordHash;

        // the sweep's text drop (its record share, then the entity's stamp and retention events), and the inputs' drop
        await RecordTextDrop.DropAsync(job.Sealer, job.RecordJobs, job.Blobs, V31Job.JobId, CancellationToken.None);
        await job.Entity.DropText(new ConsultGenerationTextDrop(new DateTimeOffset(V31Job.Now.AddDays(7), TimeSpan.Zero)));
        await job.Entity.DropInputs(new ConsultGenerationInputsDrop(new DateTimeOffset(V31Job.Now.AddDays(7), TimeSpan.Zero)));
        Assert.NotNull(job.State.TextDroppedAtUtc);
        Assert.Contains(job.State.History, e => e.Kind == "retention");
        Assert.True(job.Blobs.RemoveRecord(V31Job.AccountKind, UserId, V31Job.JobId));

        var restored = await job.Sealer.ReadSealedAsync(V31Job.JobId, CancellationToken.None);

        Assert.Equal(original, restored);
        Assert.Equal(stamp, RecordDocument.Hash(restored!));
        Assert.Equal(stamp, job.State.RecordHash);
    }

    [Fact]
    public async Task ARestore_ThatDoesNotHashToTheStamp_WritesNothing()
    {
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var stamp = job.State.RecordHash!;
        Assert.True(job.Blobs.RemoveRecord(V31Job.AccountKind, UserId, V31Job.JobId));
        const string content = "Margaret Okafor's tampered row (fictional)";
        await job.Rows.WriteAsync(V31Job.JobId,
            [new RecordRow("decision", "decision:zz.tampered", $"{{\"condition\":\"{content}\",\"id\":\"decision:zz.tampered\"}}")],
            CancellationToken.None);
        var tampered = RecordDocument.Hash(RecordDocument.Assemble(job.State.ToRecordHeader(), await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None)));

        var fault = await Assert.ThrowsAsync<InvalidOperationException>(() => job.Sealer.ReadSealedAsync(V31Job.JobId, CancellationToken.None));

        Assert.DoesNotContain("Okafor", fault.Message, StringComparison.Ordinal);
        Assert.False(HasRecordBlob(job));
        Assert.Equal(stamp, job.State.RecordHash);
        var error = Assert.Single(job.SealLog.Entries, e => e.Level == LogLevel.Error);
        Assert.Equal($"V31 record could not be restored. JobId={V31Job.JobId}, Expected={stamp}, Got={tampered}", error.Message);
        Assert.DoesNotContain("Okafor", job.SealLog.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoRestorers_AgreeOnOneDocument()
    {
        // both find the blob gone; the first restores it, the second's write finds it there and serves that one
        var job = V31Job.Of("nested-note");
        await V31Job.RunAsync(job.Context());
        var original = await DocumentOf(job);
        Assert.True(job.Blobs.RemoveRecord(V31Job.AccountKind, UserId, V31Job.JobId));
        var firstLog = new CapturingLogger<RecordSealer>();
        var secondLog = new CapturingLogger<RecordSealer>();
        var first = new RecordSealer(job.RecordJobs, job.Rows, new ReadsMissingOnce(job.Blobs), firstLog);
        var second = new RecordSealer(job.RecordJobs, job.Rows, new ReadsMissingOnce(job.Blobs), secondLog);

        var one = await first.ReadSealedAsync(V31Job.JobId, CancellationToken.None);
        var other = await second.ReadSealedAsync(V31Job.JobId, CancellationToken.None);

        Assert.Equal(original, one);
        Assert.Equal(original, other);
        Assert.Single(job.Blobs.Blobs.Names(), n => n.Name == BlobRecordStore.RecordName(UserId, V31Job.JobId));
        Assert.Single(firstLog.Entries.Concat(secondLog.Entries), e => e.Message.StartsWith("V31 record restored.", StringComparison.Ordinal));
        Assert.Single(job.RecordJobs.Stamps);
    }

    // ---- #1506 (final review F1): a terminal job at storage 12 is frozen -------------------------------------------------

    [Theory]
    [InlineData(ConsultGenerationJobStatuses.Completed)]
    [InlineData(ConsultGenerationJobStatuses.Failed)]
    public async Task ASealedJobsHeader_NeverChanges_AndARestoreStillHashesToTheStamp(string status)
    {
        var job = V31Job.Of("checked-note");
        if (status == ConsultGenerationJobStatuses.Failed)
        {
            var stub = job.Agent;
            job.Agent = call => call.Key == "node.plan" ? new AgentOutcome(AgentOutcomeKind.ServiceFailure, null, 5000, "error") : stub(call);
        }

        await V31Job.RunAsync(job.Context());
        Assert.Equal(status, job.State.Status);
        var header = job.State.ToRecordHeader().ToJsonString();
        var stamp = job.State.RecordHash;
        Assert.NotNull(stamp);

        // every operation that writes a header member, called on the sealed job with other values
        await job.Entity.Cancel();
        await job.Entity.FinalizeJob(new ConsultGenerationJobFinalize(
            status == ConsultGenerationJobStatuses.Failed ? ConsultGenerationJobStatuses.Completed : ConsultGenerationJobStatuses.Failed,
            "Another error.", V31Job.AccountKind));
        job.Entity.RecordV31Header(new ConsultGenerationV31Header(
            "sha256:other", "sha256:other", new Dictionary<string, int> { ["maxAgentCalls"] = 1 }, DateTimeOffset.UnixEpoch,
            new JsonObject { ["other"] = true }, new JsonObject { ["other"] = true }));
        job.Entity.RecordV31Rows(new ConsultGenerationV31Rows([], new ConsultGenerationV31Tokens(1000, 1000, 2000)));
        var other = new ConsultGenerationJobInitialize(
            V31Job.JobId, UserId, Array.Empty<IReadOnlyDictionary<string, string>>(), "other@9.9.9", PackageSpecVersion: 31,
            StartedAtUtc: DateTimeOffset.UnixEpoch);
        await job.Entity.Initialize(other);
        await job.Entity.RecordStartFailure(new ConsultGenerationJobStartFailure(other, "The package could not be loaded."));
        await job.Entity.MarkNodeFailed(new ConsultGenerationNodeFailure("node.plan", "Plan", "failed", "boom", []));

        Assert.Equal(header, job.State.ToRecordHeader().ToJsonString());
        Assert.Equal(status, job.State.Status);

        // the document gone: restored from the rows under the header as it is, it hashes to the stamp
        Assert.True(job.Blobs.RemoveRecord(V31Job.AccountKind, UserId, V31Job.JobId));
        var restored = await job.Sealer.ReadSealedAsync(V31Job.JobId, CancellationToken.None);
        Assert.Equal(stamp, RecordDocument.Hash(restored!));
    }

    [Fact]
    public async Task ATerminalJobAtStorage12_IsFrozen_BeforeItsSealLands()
    {
        // the stamp is a signal sent after the document is written: the freeze is on the state, not on the stamp
        var job = V31Job.Of("nested-note");
        job.Seal = _ => null;
        await V31Job.RunAsync(job.Context());
        Assert.Null(job.State.RecordHash);
        var header = job.State.ToRecordHeader().ToJsonString();

        await job.Entity.Cancel();
        await job.Entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, "Another error.", V31Job.AccountKind));

        Assert.Equal(header, job.State.ToRecordHeader().ToJsonString());
    }

    [Theory]
    [InlineData(30)]
    [InlineData(26)]
    public async Task A30TerminalJob_CancelAndFinalize_BehaveAsBefore(int specVersion)
    {
        var entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(), Substitute.For<IJobInputsBlobStore>(),
            Substitute.For<IAccountUsageStore>());
        await entity.Initialize(Initialize(specVersion));
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
        var jobs = new EntityRecordJobs(entity, V31Job.AccountKind);
        var state = (await jobs.ReadAsync(V31Job.JobId, CancellationToken.None))!;
        Assert.True(state.SchemaVersion < 12);

        // a reply-leg failure re-finalizes a 5–30 job as Failed, as it always did
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, "The reply could not be sent."));
        state = (await jobs.ReadAsync(V31Job.JobId, CancellationToken.None))!;
        Assert.Equal((ConsultGenerationJobStatuses.Failed, "The reply could not be sent."), (state.Status, state.FailureError));

        // and Cancel still writes the cancel
        await entity.Cancel();
        state = (await jobs.ReadAsync(V31Job.JobId, CancellationToken.None))!;
        Assert.Equal(ConsultGenerationJobStatuses.Cancelled, state.Status);
        Assert.Contains(state.History, e => e.Label == "Cancelled before the scheduled run.");
    }

    /// <summary>Asserts <paramref name="job"/>'s record is sealed (the document is its header and rows; its hash and length on the entity) and gives the document.</summary>
    private static async Task<JsonNode> AssertSealedAsync(V31Job job)
    {
        var document = await DocumentOf(job);
        var rows = await job.Rows.ListAsync(V31Job.JobId, CancellationToken.None);
        Assert.Equal(RecordDocument.Assemble(job.State.ToRecordHeader(), rows), document);
        Assert.Equal(RecordDocument.Hash(document), job.State.RecordHash);
        Assert.Equal(Encoding.UTF8.GetByteCount(document), job.State.RecordBytes);

        // and sealing again gives the same
        var again = await job.Sealer.EnsureSealedAsync(V31Job.JobId, CancellationToken.None);
        Assert.Equal((job.State.RecordHash, job.State.RecordBytes), (again!.RecordHash, (long?)again.Bytes));
        return JsonNode.Parse(document)!;
    }

    private static async Task<string> DocumentOf(V31Job job) =>
        await job.Blobs.ReadRecordAsync(V31Job.AccountKind, UserId, V31Job.JobId, CancellationToken.None)
        ?? throw new Xunit.Sdk.XunitException("No record.json was written.");

    private static bool HasRecordBlob(V31Job job) =>
        job.Blobs.Blobs.Names().Any(n => n.Name == BlobRecordStore.RecordName(UserId, V31Job.JobId));

    private static ConsultGenerationJobInitialize Initialize(int specVersion) => new(
        V31Job.JobId, UserId, Array.Empty<IReadOnlyDictionary<string, string>>(), "nested-note@1.0.0", PackageSpecVersion: specVersion);

    /// <summary>A blob store whose first read of <c>record.json</c> finds nothing: a restorer that read before another wrote.</summary>
    private sealed class ReadsMissingOnce(IRecordBlobStore inner) : IRecordBlobStore
    {
        private bool _read;

        public Task WriteTextAsync(string? accountKind, string appUserId, string jobId, string key, string part, JsonObject text, CancellationToken ct) =>
            inner.WriteTextAsync(accountKind, appUserId, jobId, key, part, text, ct);

        public Task<JsonObject?> ReadTextAsync(string? accountKind, string appUserId, string jobId, string key, CancellationToken ct) =>
            inner.ReadTextAsync(accountKind, appUserId, jobId, key, ct);

        public Task DeleteTextAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct) =>
            inner.DeleteTextAsync(accountKind, appUserId, jobId, ct);

        public Task<bool> WriteRecordAsync(string? accountKind, string appUserId, string jobId, string document, CancellationToken ct) =>
            inner.WriteRecordAsync(accountKind, appUserId, jobId, document, ct);

        public Task<string?> ReadRecordAsync(string? accountKind, string appUserId, string jobId, CancellationToken ct)
        {
            if (!_read)
            {
                _read = true;
                return Task.FromResult<string?>(null);
            }

            return inner.ReadRecordAsync(accountKind, appUserId, jobId, ct);
        }
    }

    /// <summary>The sealer's view with every read showing no stamp: two sealers that never see each other's.</summary>
    private sealed class UnstampedJobs(EntityRecordJobs inner) : IRecordJobs
    {
        public async Task<ConsultGenerationJobState?> ReadAsync(string jobId, CancellationToken ct)
        {
            var state = await inner.ReadAsync(jobId, ct);
            if (state == null)
                return null;
            var copy = System.Text.Json.JsonSerializer.Deserialize<ConsultGenerationJobState>(System.Text.Json.JsonSerializer.Serialize(state))!;
            copy.RecordHash = null;
            copy.RecordBytes = null;
            return copy;
        }

        public Task<string?> AccountKindAsync(ConsultGenerationJobState job, CancellationToken ct) => inner.AccountKindAsync(job, ct);

        public Task StampAsync(string jobId, string recordHash, long bytes, CancellationToken ct) => inner.StampAsync(jobId, recordHash, bytes, ct);
    }
}
