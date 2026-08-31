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
        var entity = new ConsultGenerationJobEntity(Substitute.For<IConsultGenerationJobIndexStore>(), store);
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
}
