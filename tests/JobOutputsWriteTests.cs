using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests;

/// <summary>
/// #557: the entity writes the outputs blob at completion — after the hashes
/// are stamped, into the container the kind names — and never sheds the only
/// copy without a recorded pointer: a write failure leaves a pre-#557-shaped
/// record that says what happened, never a Failed consult.
/// </summary>
public class JobOutputsWriteTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State, IJobOutputsBlobStore Store) Job()
    {
        var store = Substitute.For<IJobOutputsBlobStore>();
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), store, Substitute.For<IJobInputsBlobStore>());
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" }
        }));
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!, store);
    }

    private static async Task ProduceAsync(ConsultGenerationJobEntity entity)
    {
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.CompleteResultDocument(new ConsultGenerationResultDocument("note", "Consultation note", "Consultation note", 0));
    }

    [Fact]
    public async Task Completion_WritesThePayload_IntoTheKindsContainer_AndRecordsThePointer()
    {
        var (entity, state, store) = Job();
        var pointer = new ConsultOutputsBlobPointer("org-job-outputs", "user-1/job-1.json");
        JobOutputsPayload? written = null;
        store.WriteAsync("organisation", "user-1", "job-1", Arg.Do<JobOutputsPayload>(p => written = p), Arg.Any<CancellationToken>())
            .Returns(pointer);

        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed, AccountKind: "organisation"));

        Assert.NotNull(written);
        var document = written!.Documents!.Single();
        Assert.Equal("Consultation note", document.Text);
        // Stamp-before-write: the payload carries the stamped digest.
        Assert.Equal(state().AssembledDocuments!.Single().DocumentHash, document.DocumentHash);
        Assert.Equal("Consultation note", written.BlockTexts!["note:draft"]);
        Assert.Equal(pointer, state().OutputsBlob);
        Assert.Equal(pointer, state().ToResponse().OutputsBlob);
        // With the pointer recorded, the entity shed the four text species.
        Assert.Null(state().AssembledDocuments!.Single().Text);
        Assert.Null(state().Blocks["note:draft"].GeneratedText);
        // What it keeps: the stamped hashes, names and flags.
        Assert.NotNull(state().AssembledDocuments!.Single().DocumentHash);
        Assert.NotNull(state().WorkflowOutputHash);
    }

    [Fact]
    public async Task DropText_DeletesTheBlobFirst_AndKeepsThePointer()
    {
        var (entity, state, store) = Job();
        var pointer = new ConsultOutputsBlobPointer("org-job-outputs", "user-1/job-1.json");
        store.WriteAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobOutputsPayload>(), Arg.Any<CancellationToken>())
            .Returns(pointer);
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed, AccountKind: "organisation"));

        await entity.DropText(new ConsultGenerationTextDrop(new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero)));

        await store.Received(1).DeleteAsync(pointer, Arg.Any<CancellationToken>());
        Assert.NotNull(state().TextDroppedAtUtc);
        // The pointer is part of the record; TextDroppedAtUtc gates it.
        Assert.Equal(pointer, state().OutputsBlob);
    }

    [Fact]
    public async Task AFailedBlobDelete_PersistsNothing_SoTheSweepRetries()
    {
        var (entity, state, store) = Job();
        var pointer = new ConsultOutputsBlobPointer("org-job-outputs", "user-1/job-1.json");
        store.WriteAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobOutputsPayload>(), Arg.Any<CancellationToken>())
            .Returns(pointer);
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed, AccountKind: "organisation"));
        store.DeleteAsync(pointer, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage blip"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => entity.DropText(new ConsultGenerationTextDrop(DateTimeOffset.UtcNow)));

        // Nothing persisted: the record still says text present, so IsDue
        // keeps the job on the sweep's list and the next run re-signals.
        Assert.Null(state().TextDroppedAtUtc);
    }

    [Fact]
    public async Task AWriteFailure_KeepsTheText_RecordsNoPointer_AndTheRecordSaysSo()
    {
        var (entity, state, store) = Job();
        store.WriteAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobOutputsPayload>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage blip"));

        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed, AccountKind: "organisation"));

        // The invariant: the only copy is never shed without a pointer — and
        // a storage blip never turns a produced consult into a Failed record.
        Assert.Equal(ConsultGenerationJobStatuses.Completed, state().Status);
        Assert.Null(state().OutputsBlob);
        Assert.Equal("Consultation note", state().AssembledDocuments!.Single().Text);
        Assert.Contains(state().History, h => h.Kind == "storage" && h.Label.Contains("Outputs blob not written"));
    }

    [Fact]
    public async Task AFailedJob_WritesNoBlob()
    {
        var (entity, _, store) = Job();

        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, "boom"));

        await store.DidNotReceiveWithAnyArgs().WriteAsync(default, default!, default!, default!, default);
    }

    // ----- #547: the held inputs ride the same drop -----

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State, IJobOutputsBlobStore Outputs, IJobInputsBlobStore Inputs) JobWithInputs()
    {
        var outputs = Substitute.For<IJobOutputsBlobStore>();
        var inputs = Substitute.For<IJobInputsBlobStore>();
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), outputs, inputs);
        StateProperty.SetValue(entity, ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" }
        }));
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!, outputs, inputs);
    }

    [Fact]
    public async Task DropText_DeletesTheHeldInputs_StampsAndSaysSo()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        var pointer = new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json");
        state().InputsBlob = pointer;
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
        var droppedAt = new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero);

        await entity.DropText(new ConsultGenerationTextDrop(droppedAt));

        await inputs.Received(1).DeleteAsync(pointer, Arg.Any<CancellationToken>());
        Assert.Equal(droppedAt, state().InputsDroppedAtUtc);
        // The pointer is part of the record; InputsDroppedAtUtc gates it.
        Assert.Equal(pointer, state().InputsBlob);
        Assert.Contains(state().History, h => h.Kind == "retention" && h.Label.Contains("Held inputs deleted"));
    }

    [Fact]
    public async Task AnUnheldJob_DropsWithoutClaimingInputs()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        await entity.DropText(new ConsultGenerationTextDrop(DateTimeOffset.UtcNow));

        await inputs.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        Assert.Null(state().InputsDroppedAtUtc);
        Assert.DoesNotContain(state().History, h => h.Label.Contains("Held inputs deleted"));
    }

    [Fact]
    public async Task AFailedInputsDelete_PersistsNothing_SoTheSweepRetries()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        var pointer = new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json");
        state().InputsBlob = pointer;
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
        inputs.DeleteAsync(pointer, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage blip"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => entity.DropText(new ConsultGenerationTextDrop(DateTimeOffset.UtcNow)));

        Assert.Null(state().TextDroppedAtUtc);
        Assert.Null(state().InputsDroppedAtUtc);
    }

    // ----- #548: the inputs clock may fire first — the inputs-only drop -----

    [Fact]
    public async Task DropInputs_DeletesTheBlob_StampsAndKeepsThePointer_AndTouchesNoText()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        var pointer = new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json");
        state().InputsBlob = pointer;
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
        var droppedAt = new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero);

        await entity.DropInputs(new ConsultGenerationInputsDrop(droppedAt));

        await inputs.Received(1).DeleteAsync(pointer, Arg.Any<CancellationToken>());
        Assert.Equal(droppedAt, state().InputsDroppedAtUtc);
        Assert.Equal(pointer, state().InputsBlob);
        Assert.Contains(state().History, h => h.Kind == "retention" && h.Label.Contains("Held inputs deleted"));
        // The produced text is untouched: its own clock has not fired.
        Assert.Null(state().TextDroppedAtUtc);
        Assert.DoesNotContain(state().History, h => h.Label.Contains("Produced text deleted"));
    }

    [Fact]
    public async Task DropInputs_OnAnUnheldJob_RecordsNothing()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        await entity.DropInputs(new ConsultGenerationInputsDrop(DateTimeOffset.UtcNow));

        await inputs.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        Assert.Null(state().InputsDroppedAtUtc);
        Assert.DoesNotContain(state().History, h => h.Label.Contains("Held inputs deleted"));
    }

    [Fact]
    public async Task DropInputs_OnANonTerminalJob_RecordsNothing()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        state().InputsBlob = new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json");

        await entity.DropInputs(new ConsultGenerationInputsDrop(DateTimeOffset.UtcNow));

        await inputs.DidNotReceiveWithAnyArgs().DeleteAsync(default!, default);
        Assert.Null(state().InputsDroppedAtUtc);
    }

    [Fact]
    public async Task DropInputs_IsIdempotent()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        state().InputsBlob = new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json");
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
        var droppedAt = new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero);
        await entity.DropInputs(new ConsultGenerationInputsDrop(droppedAt));

        await entity.DropInputs(new ConsultGenerationInputsDrop(droppedAt.AddDays(1)));

        await inputs.Received(1).DeleteAsync(Arg.Any<ConsultInputsBlobPointer>(), Arg.Any<CancellationToken>());
        Assert.Equal(droppedAt, state().InputsDroppedAtUtc);
        Assert.Single(state().History, h => h.Label.Contains("Held inputs deleted"));
    }

    [Fact]
    public async Task AFailedInputsOnlyDelete_PersistsNothing_SoTheSweepRetries()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        var pointer = new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json");
        state().InputsBlob = pointer;
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
        inputs.DeleteAsync(pointer, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage blip"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => entity.DropInputs(new ConsultGenerationInputsDrop(DateTimeOffset.UtcNow)));

        Assert.Null(state().InputsDroppedAtUtc);
    }

    [Fact]
    public async Task DropText_AfterDropInputs_NeitherRedeletesNorResays()
    {
        var (entity, state, _, inputs) = JobWithInputs();
        var pointer = new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json");
        state().InputsBlob = pointer;
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
        var inputsDroppedAt = new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero);
        await entity.DropInputs(new ConsultGenerationInputsDrop(inputsDroppedAt));

        await entity.DropText(new ConsultGenerationTextDrop(inputsDroppedAt.AddDays(4)));

        // One delete, one sentence, the first stamp: the split clocks never
        // make the record say the same deletion twice.
        await inputs.Received(1).DeleteAsync(Arg.Any<ConsultInputsBlobPointer>(), Arg.Any<CancellationToken>());
        Assert.Single(state().History, h => h.Label.Contains("Held inputs deleted"));
        Assert.Equal(inputsDroppedAt, state().InputsDroppedAtUtc);
        Assert.NotNull(state().TextDroppedAtUtc);
    }

    [Fact]
    public async Task TheIndexEntry_CarriesTheInputsColumns()
    {
        var (entity, state, _, _) = JobWithInputs();
        await ProduceAsync(entity);
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        Assert.False(state().ToIndexEntry().InputsHeld);

        state().InputsBlob = new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json");
        Assert.True(state().ToIndexEntry().InputsHeld);
        Assert.Null(state().ToIndexEntry().InputsDroppedAtUtc);

        var droppedAt = new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero);
        await entity.DropInputs(new ConsultGenerationInputsDrop(droppedAt));
        Assert.True(state().ToIndexEntry().InputsHeld);
        Assert.Equal(droppedAt, state().ToIndexEntry().InputsDroppedAtUtc);
    }
}
