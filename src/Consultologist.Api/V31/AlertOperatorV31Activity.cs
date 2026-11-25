using Consultologist.Api.Jobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; part 09 § 3.9; D14): the activity a 31 orchestration alerts the operator through, so a
/// replay reads that the alert was raised and never raises it twice. It never throws: an alert that could not be sent is
/// logged and dropped (<see cref="IOperatorAlerts"/>).
/// </summary>
public sealed class AlertOperatorV31Activity(IOperatorAlerts alerts, ILogger<AlertOperatorV31Activity> logger)
{
    [Function(ConsultGenerationActivityNames.AlertOperatorV31)]
    public async Task RunAsync([ActivityTrigger] OperatorAlert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);
        try
        {
            await alerts.RaiseAsync(alert, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Operator alert failed. Kind={Kind}, JobId={JobId}, ExceptionType={ExceptionType}",
                alert.Kind, alert.JobId, ex.GetType().FullName);
        }
    }
}

/// <summary>
/// #1508 (ruling 4, item 5; part 09 § 3.9; D14): the alerts one 31 orchestration raises. <see cref="Raise"/> schedules the
/// <see cref="ConsultGenerationActivityNames.AlertOperatorV31"/> activity and returns at once, so the run never waits on an
/// alert; the orchestration calls <see cref="WhenSentAsync"/> once, as it ends, so it does not complete with an alert
/// outstanding. An alert's failure is logged (the fault's type only) and never fails the orchestration or the job. The
/// activities are scheduled in the order <see cref="Raise"/> is called, the same on every replay.
/// </summary>
internal sealed class PendingAlerts(TaskOrchestrationContext context, ILogger logger)
{
    private readonly List<Task> _sent = [];

    /// <summary>Schedules the alert's activity; not awaited.</summary>
    public void Raise(OperatorAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        _sent.Add(SendAsync(alert));
    }

    /// <summary>Ends once every alert raised has been answered, or has failed. Never faults.</summary>
    public Task WhenSentAsync() => Task.WhenAll(_sent);

    // Every exception, a cancellation included: WhenSentAsync is awaited in the orchestrator's finally, after the job is
    // final, so a fault here would fail an orchestration whose job has already ended (or replace the fault being thrown).
    // The call takes no cancellation token, so a cancellation here is the alert's own outcome, never the orchestration's.
    private async Task SendAsync(OperatorAlert alert)
    {
        try
        {
            await context.CallActivityAsync(ConsultGenerationActivityNames.AlertOperatorV31, alert);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Operator alert failed. Kind={Kind}, JobId={JobId}, ExceptionType={ExceptionType}",
                alert.Kind, alert.JobId, ConsultGenerationOrchestratorV31.FaultType(ex));
        }
    }
}
