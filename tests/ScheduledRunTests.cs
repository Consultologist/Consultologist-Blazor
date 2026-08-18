using System.Reflection;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using NSubstitute;

namespace Consultologist.Api.Tests;

public class ScheduledRunTests
{
    [Fact]
    public void ValidateRequest_NoSchedule_IsValid()
    {
        Assert.Null(ConsultGenerationJobs.ValidateRequest(new ConsultGenerationRequest("draft")));
    }

    [Fact]
    public void ValidateRequest_WithinHorizon_IsValid()
    {
        var request = new ConsultGenerationRequest("draft", ScheduledAtUtc: DateTimeOffset.UtcNow.AddHours(8));

        Assert.Null(ConsultGenerationJobs.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_PastSchedule_IsValid_RunsImmediately()
    {
        var request = new ConsultGenerationRequest("draft", ScheduledAtUtc: DateTimeOffset.UtcNow.AddMinutes(-10));

        Assert.Null(ConsultGenerationJobs.ValidateRequest(request));
    }

    [Fact]
    public void ValidateRequest_BeyondHorizon_IsRejected()
    {
        var request = new ConsultGenerationRequest("draft", ScheduledAtUtc: DateTimeOffset.UtcNow.AddDays(8));

        Assert.Equal("ScheduledAtUtc is more than 7 days out.", ConsultGenerationJobs.ValidateRequest(request));
    }

    private static readonly PropertyInfo StateProperty = typeof(ConsultGenerationJobEntity)
        .GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;

    private static ConsultGenerationJobState StateOf(ConsultGenerationJobEntity entity) =>
        (ConsultGenerationJobState)StateProperty.GetValue(entity)!;

    private static readonly List<IReadOnlyDictionary<string, string>> Items = new()
    {
        new Dictionary<string, string> { ["id"] = "hpi", ["name"] = "HPI" }
    };

    private static ConsultGenerationJobEntity CreateEntity() =>
        new(Substitute.For<IConsultGenerationJobIndexStore>());

    [Fact]
    public async Task Cancel_WritesATerminalRecordThatIsNotAFailure()
    {
        // #202: nothing went wrong and nothing was spent, so Cancelled is its
        // own terminal status rather than a flavour of Failed.
        var entity = CreateEntity();
        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", Items, ScheduledAtUtc: DateTimeOffset.UtcNow.AddHours(6)));

        await entity.Cancel();

        var state = StateOf(entity);
        Assert.Equal(ConsultGenerationJobStatuses.Cancelled, state.Status);
        Assert.NotNull(state.CompletedAtUtc);
        Assert.Null(state.FailureError);
        Assert.Contains(state.History, e => e.Label.Contains("Cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MarkRunning_AfterCancel_LeavesItCancelled()
    {
        // THE race this issue turns on. Terminating the orchestration does not
        // un-schedule a timer that has already fired, so MarkRunning can arrive
        // after the cancel — and without the guard the job runs after the user
        // has been told it will not. A 409 at the endpoint cannot prevent this;
        // by then the decision has already been made.
        var entity = CreateEntity();
        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", Items, ScheduledAtUtc: DateTimeOffset.UtcNow.AddMinutes(2)));
        await entity.Cancel();

        await entity.MarkRunning();

        Assert.Equal(ConsultGenerationJobStatuses.Cancelled, StateOf(entity).Status);
    }

    [Fact]
    public async Task MarkRunning_OnAScheduledJob_StillRuns()
    {
        // The guard must not swallow the ordinary path it sits in front of.
        var entity = CreateEntity();
        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", Items, ScheduledAtUtc: DateTimeOffset.UtcNow.AddHours(6)));

        await entity.MarkRunning();

        Assert.Equal(ConsultGenerationJobStatuses.Running, StateOf(entity).Status);
    }

    [Fact]
    public async Task Initialize_FutureSchedule_LandsScheduled()
    {
        var entity = CreateEntity();
        var scheduledAt = DateTimeOffset.UtcNow.AddHours(6);

        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", Items, ScheduledAtUtc: scheduledAt));

        var state = StateOf(entity);
        Assert.Equal(ConsultGenerationJobStatuses.Scheduled, state.Status);
        Assert.Equal(scheduledAt, state.ScheduledAtUtc);
    }

    [Fact]
    public async Task Initialize_PastSchedule_StaysQueued()
    {
        var entity = CreateEntity();

        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", Items, ScheduledAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5)));

        Assert.Equal(ConsultGenerationJobStatuses.Queued, StateOf(entity).Status);
    }

    [Fact]
    public async Task Initialize_NoSchedule_StaysQueued()
    {
        var entity = CreateEntity();

        await entity.Initialize(new ConsultGenerationJobInitialize("job-1", "user-1", Items));

        var state = StateOf(entity);
        Assert.Equal(ConsultGenerationJobStatuses.Queued, state.Status);
        Assert.Null(state.ScheduledAtUtc);
    }

    [Fact]
    public async Task MarkRunning_FlipsScheduledToRunning()
    {
        var entity = CreateEntity();
        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-1", "user-1", Items, ScheduledAtUtc: DateTimeOffset.UtcNow.AddHours(6)));

        await entity.MarkRunning();

        Assert.Equal(ConsultGenerationJobStatuses.Running, StateOf(entity).Status);
    }

    private static Consultologist.Api.Auth.AppAccount CreateAccount(string? email) =>
        new("user-1", "Test User", email, "Active",
            new Consultologist.Api.Auth.AccountIdentity("entra-external-id", "iss", "sub", default, default),
            Array.Empty<Consultologist.Api.Auth.AccountIdentity>());

    [Fact]
    public void ReplyAddressFor_ScheduledJob_UsesAccountEmail()
    {
        var request = new ConsultGenerationRequest("draft", ScheduledAtUtc: DateTimeOffset.UtcNow.AddHours(8));

        Assert.Equal("doc@example.com", ConsultGenerationJobs.ReplyAddressFor(request, CreateAccount("doc@example.com")));
    }

    [Fact]
    public void ReplyAddressFor_ImmediateJob_IsNull()
    {
        Assert.Null(ConsultGenerationJobs.ReplyAddressFor(new ConsultGenerationRequest("draft"), CreateAccount("doc@example.com")));
    }

    [Fact]
    public void ReplyAddressFor_ScheduledJobWithoutAccountEmail_IsNull()
    {
        var request = new ConsultGenerationRequest("draft", ScheduledAtUtc: DateTimeOffset.UtcNow.AddHours(8));

        Assert.Null(ConsultGenerationJobs.ReplyAddressFor(request, CreateAccount(null)));
    }

    [Fact]
    public void IndexEntryAndResponse_CarryScheduledAtUtc()
    {
        var state = ConsultGenerationJobState.Create("job-1", "user-1", Items);
        var scheduledAt = DateTimeOffset.UtcNow.AddHours(6);
        state.ScheduledAtUtc = scheduledAt;

        Assert.Equal(scheduledAt, state.ToIndexEntry().ScheduledAtUtc);
        Assert.Equal(scheduledAt, state.ToResponse().ScheduledAtUtc);
    }
}
