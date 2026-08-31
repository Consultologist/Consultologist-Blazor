using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Consultologist.Api.Email;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;

namespace Consultologist.Api.Tests;

/// <summary>
/// #486: delivery recorded on the job — once, on a terminal job, with the
/// outcome the reply leg reduced to.
/// </summary>
public class DeliveryRecordTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static readonly DateTimeOffset At = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);

    private static (ConsultGenerationJobEntity Entity, Func<ConsultGenerationJobState> State, IConsultGenerationJobIndexStore Index) Job()
    {
        var index = Substitute.For<IConsultGenerationJobIndexStore>();
        var entity = new ConsultGenerationJobEntity(index, Substitute.For<IJobOutputsBlobStore>());
        var state = ConsultGenerationJobState.Create("job-1", "user-1", new[]
        {
            new Dictionary<string, string> { ["id"] = "note:draft", ["name"] = "Consultation note" }
        });
        StateProperty.SetValue(entity, state);
        return (entity, () => (ConsultGenerationJobState)StateProperty.GetValue(entity)!, index);
    }

    private static async Task CompleteAsync(ConsultGenerationJobEntity entity)
    {
        await entity.CompleteBlock(new BlockGenerationResult("note:draft", "Consultation note", true, "Consultation note", null));
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));
    }

    [Fact]
    public async Task RecordDelivery_Sent_DatesIt_AndSaysWhetherTheDocumentRodeAlong()
    {
        var (entity, state, index) = Job();
        await CompleteAsync(entity);
        index.ClearReceivedCalls();

        await entity.RecordDelivery(new ConsultGenerationDeliveryRecord(DeliveryOutcomes.Sent, At, DocumentAttached: true));

        var response = state().ToResponse();
        Assert.Equal(DeliveryOutcomes.Sent, response.DeliveryOutcome);
        Assert.Equal(At, response.DeliveredAtUtc);
        Assert.True(response.DeliveryDocumentAttached);
        Assert.Contains(response.History, e => e.Kind == "delivery" && e.Label.Contains("document attached"));
        await index.Received(1).UpsertAsync(
            Arg.Is<ConsultGenerationJobIndexEntry>(e => e.DeliveryOutcome == DeliveryOutcomes.Sent && e.DeliveredAtUtc == At),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordDelivery_NotSent_HasNoDeliveredDate()
    {
        var (entity, state, _) = Job();
        await CompleteAsync(entity);

        await entity.RecordDelivery(new ConsultGenerationDeliveryRecord(DeliveryOutcomes.AddressNotSet, At));

        var response = state().ToResponse();
        Assert.Equal(DeliveryOutcomes.AddressNotSet, response.DeliveryOutcome);
        Assert.Null(response.DeliveredAtUtc);
        Assert.Contains(response.History, e => e.Kind == "delivery" && e.Label.Contains("no delivery address"));
    }

    // #518: the account chose no email — recorded as its own outcome, so a
    // missing email is never read as a failure.
    [Fact]
    public async Task RecordDelivery_NotRequested_IsAChoice_NotAFailure()
    {
        var (entity, state, _) = Job();
        await CompleteAsync(entity);

        await entity.RecordDelivery(new ConsultGenerationDeliveryRecord(DeliveryOutcomes.NotRequested, At));

        var response = state().ToResponse();
        Assert.Equal("not-requested", response.DeliveryOutcome);
        Assert.Null(response.DeliveredAtUtc);
        Assert.Contains(response.History, e => e.Kind == "delivery" && e.Label == "Not emailed — by your choice");
    }

    [Fact]
    public async Task TheReplyLeg_StopsBeforeTheAddress_WhenTheEmailWasNotRequested()
    {
        // The choice wins over address-not-set, and the activity is never
        // reached: no orchestration context is needed to answer.
        var input = new ConsultGenerationOrchestrationInput(
            new ConsultGenerationRequest("draft"), "user-1", Source: "app", ReplyToAddress: "verified@clinic.example", EmailRequested: false);

        var record = await ConsultGenerationOrchestrator.TrySendReplyAsync(null!, input, "Completed", NullLogger.Instance, null, null);

        Assert.Equal(DeliveryOutcomes.NotRequested, record.Outcome);
    }

    [Fact]
    public async Task RecordDelivery_IsWriteOnce()
    {
        var (entity, state, _) = Job();
        await CompleteAsync(entity);

        await entity.RecordDelivery(new ConsultGenerationDeliveryRecord(DeliveryOutcomes.Failed, At));
        await entity.RecordDelivery(new ConsultGenerationDeliveryRecord(DeliveryOutcomes.Sent, At.AddMinutes(1)));

        Assert.Equal(DeliveryOutcomes.Failed, state().DeliveryOutcome);
        Assert.Single(state().History, e => e.Kind == "delivery");
    }

    [Fact]
    public async Task RecordDelivery_RefusesARunningJob()
    {
        var (entity, state, _) = Job();

        await entity.RecordDelivery(new ConsultGenerationDeliveryRecord(DeliveryOutcomes.Sent, At));

        Assert.Null(state().DeliveryOutcome);
    }

    [Fact]
    public async Task ARecordFromBefore_SaysNothing()
    {
        var (entity, state, _) = Job();
        await CompleteAsync(entity);

        var response = state().ToResponse();
        Assert.Null(response.DeliveryOutcome);
        Assert.Null(response.DeliveredAtUtc);
        Assert.Null(response.DeliveryDocumentAttached);
    }

    [Fact]
    public void DeliveryRecordFor_ReducesTheActivityOutcome()
    {
        Assert.Equal(
            (DeliveryOutcomes.Sent, (bool?)true),
            Reduce(new EmailIntakeReplyOutcome(true, true)));
        Assert.Equal(
            (DeliveryOutcomes.Sent, (bool?)false),
            Reduce(new EmailIntakeReplyOutcome(true, false)));
        Assert.Equal(
            (DeliveryOutcomes.NotConfigured, (bool?)null),
            Reduce(new EmailIntakeReplyOutcome(false, false, DeliveryOutcomes.NotConfigured)));
        Assert.Equal(
            (DeliveryOutcomes.Failed, (bool?)null),
            Reduce(new EmailIntakeReplyOutcome(false, false)));
        // A replay from before the activity returned anything: it sent.
        Assert.Equal((DeliveryOutcomes.Sent, (bool?)null), Reduce(null));

        static (string, bool?) Reduce(EmailIntakeReplyOutcome? outcome)
        {
            var record = ConsultGenerationOrchestrator.DeliveryRecordFor(outcome);
            return (record.Outcome, record.DocumentAttached);
        }
    }
}
