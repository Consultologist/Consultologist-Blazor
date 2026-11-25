using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8): the delivery activity's input: the job, its account, and one attempt of one
/// send. The send carries its documents' composed text across the activity boundary (as a render's input carries its
/// scope); it is never logged. #1507 (area 10; D18): <paramref name="Delivery"/> is the start's delivery settings, from the
/// orchestration's input; <c>null</c> for a job started before it, whose channels read the account's settings at the attempt.
/// </summary>
public sealed record DeliverV31Input(string JobId, string AppUserId, DeliverySend Send, V31DeliverySettings? Delivery = null);

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; part 09 § 3.8; D10, D11): one attempt of one send, by its kind's channel
/// (<see cref="DeliveryChannels"/>); a kind with no channel (<c>emr</c>, <c>zip</c>) is not set up. It never throws for a
/// send's failure: a channel's fault is a failed send (<c>error</c>), so the activity's outcome is what the orchestration's
/// history holds and the runner retries it (no Durable retry policy). #1508: each attempt is bounded by
/// <see cref="DefaultSendTimeout"/> (a linked token the channel's calls must honour); its expiry is a failed send <c>timeout</c>. Telemetry is the job, the kind, the folder, the
/// number of documents, the attempt and the outcome, never a document's text.
/// </summary>
public sealed class DeliverV31Activity(DeliveryChannels channels, ILogger<DeliverV31Activity> logger)
{
    /// <summary>
    /// #1508: the bound of one attempt. The runner waits for a send in flight at the window's close, so the job's end depends
    /// on it; the attempt's token is cancelled after it and the attempt is a failed send of kind <c>timeout</c>. Well inside
    /// the 15-minute delivery window.
    /// </summary>
    internal static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The bound of one attempt (<see cref="DefaultSendTimeout"/>; tests shorten it).</summary>
    internal TimeSpan SendTimeout { get; init; } = DefaultSendTimeout;

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
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bound.CancelAfter(SendTimeout);
            try
            {
                outcome = await channel.SendAsync(input, bound.Token)
                    ?? new DeliverySendOutcome(DeliverySendResult.Failed, "error");
            }
            catch (OperationCanceledException) when (bound.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "V31 delivery send timed out. JobId={JobId}, Kind={Kind}, Attempt={Attempt}", input.JobId, send.Kind, send.Attempt);
                outcome = new DeliverySendOutcome(DeliverySendResult.Failed, "timeout");
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
            "V31 delivery send. JobId={JobId}, Kind={Kind}, Folder={Folder}, Documents={Documents}, Attempt={Attempt}, Result={Result}, ErrorKind={ErrorKind}, LinkOnly={LinkOnly}",
            input.JobId, send.Kind, send.Folder, send.Documents.Count, send.Attempt, outcome.Result, outcome.ErrorKind, outcome.LinkOnly);
        return outcome;
    }
}
