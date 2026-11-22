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

    /// <summary>
    /// Raises <paramref name="alert"/> from an orchestration, through the activity. Its failure is logged (the fault's type
    /// only) and never fails the orchestration or the job.
    /// </summary>
    internal static async Task RaiseAsync(TaskOrchestrationContext context, OperatorAlert alert, ILogger logger)
    {
        try
        {
            await context.CallActivityAsync(ConsultGenerationActivityNames.AlertOperatorV31, alert);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                "Operator alert failed. Kind={Kind}, JobId={JobId}, ExceptionType={ExceptionType}",
                alert.Kind, alert.JobId, ConsultGenerationOrchestratorV31.FaultType(ex));
        }
    }
}
