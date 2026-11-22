using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8): the delivery activity's input: the job, its account, and one attempt of one
/// send. The send carries its documents' composed text across the activity boundary (as a render's input carries its
/// scope); it is never logged.
/// </summary>
public sealed record DeliverV31Input(string JobId, string AppUserId, DeliverySend Send);

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; part 09 § 3.8; D10, D11): one attempt of one send, by its kind's channel
/// (<see cref="DeliveryChannels"/>); a kind with no channel (<c>emr</c>, <c>zip</c>) is not set up. It never throws for a
/// send's failure: a channel's fault is a failed send (<c>error</c>), so the activity's outcome is what the orchestration's
/// history holds and the runner retries it (no Durable retry policy). Telemetry is the job, the kind, the folder, the
/// number of documents, the attempt and the outcome, never a document's text.
/// </summary>
public sealed class DeliverV31Activity(DeliveryChannels channels, ILogger<DeliverV31Activity> logger)
{
    [Function(ConsultGenerationActivityNames.DeliverV31)]
    public async Task<DeliverySendOutcome> RunAsync([ActivityTrigger] DeliverV31Input input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var send = input.Send;
        DeliverySendOutcome outcome;
        if (channels.For(send.Kind) is not { } channel)
        {
            outcome = new DeliverySendOutcome(DeliverySendResult.NotSetUp, null);
        }
        else
        {
            try
            {
                outcome = await channel.SendAsync(input, cancellationToken)
                    ?? new DeliverySendOutcome(DeliverySendResult.Failed, "error");
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "V31 delivery channel faulted. JobId={JobId}, Kind={Kind}, ExceptionType={ExceptionType}",
                    input.JobId, send.Kind, ex.GetType().FullName);
                outcome = new DeliverySendOutcome(DeliverySendResult.Failed, "error");
            }
        }

        logger.LogInformation(
            "V31 delivery send. JobId={JobId}, Kind={Kind}, Folder={Folder}, Documents={Documents}, Attempt={Attempt}, Result={Result}, ErrorKind={ErrorKind}",
            input.JobId, send.Kind, send.Folder, send.Documents.Count, send.Attempt, outcome.Result, outcome.ErrorKind);
        return outcome;
    }
}
