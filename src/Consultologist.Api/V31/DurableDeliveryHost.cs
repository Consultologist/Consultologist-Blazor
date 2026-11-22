using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.DurableTask;

namespace Consultologist.Api.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.8; D12): <see cref="IDeliveryHost"/> on a Durable orchestration,
/// <see cref="DurableRunHost"/>'s twin. The clock is the replay-safe <see cref="TaskOrchestrationContext.CurrentUtcDateTime"/>;
/// an attempt is <see cref="ConsultGenerationActivityNames.DeliverV31"/>, given the job, its account and the send, with no
/// retry policy (the runner retries inside its window); a wait is a durable timer, cancelled with the runner's token. As
/// <see cref="IDeliveryHost.SendAsync"/> says, a send's failure is an outcome: an activity that failed
/// (<see cref="TaskFailedException"/>) is a failed send, <c>error</c>, and the task never faults for it.
/// </summary>
public sealed class DurableDeliveryHost(TaskOrchestrationContext context, ConsultGenerationOrchestrationInputV31 input) : IDeliveryHost
{
    public DateTime UtcNow => context.CurrentUtcDateTime;

    public async Task<DeliverySendOutcome> SendAsync(DeliverySend send)
    {
        try
        {
            return await context.CallActivityAsync<DeliverySendOutcome>(
                    ConsultGenerationActivityNames.DeliverV31, new DeliverV31Input(input.JobId, input.AppUserId, send))
                ?? new DeliverySendOutcome(DeliverySendResult.Failed, "error");
        }
        catch (TaskFailedException)
        {
            return new DeliverySendOutcome(DeliverySendResult.Failed, "error");
        }
    }

    public Task DelayAsync(TimeSpan wait, CancellationToken cancel) =>
        context.CreateTimer(context.CurrentUtcDateTime + (wait > TimeSpan.Zero ? wait : TimeSpan.Zero), cancel);
}
