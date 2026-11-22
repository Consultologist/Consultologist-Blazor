using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; D12): the engine's <see cref="IRunRecorder"/> until area 09 replaces it (this is the
/// seam). Each finished row and each decision signals <see cref="ConsultGenerationJobEntity.RecordV31Progress"/> on the
/// job's entity (counts and the current key only), and a row goes to telemetry as its key, state and value hash (#1504:
/// and its detail's <c>inputHash</c> and <c>runNeutralHash</c>; the rest of the detail is stored by area 09). A signal
/// from an orchestration is a completed task, and the job's entity takes them in the order sent, so the runner awaits
/// nothing but what the context gave it. #1505 (D14): a notice is an alert for the operator, an activity of the context's.
/// </summary>
public sealed class EntityRunRecorder(TaskOrchestrationContext context, ConsultGenerationOrchestrationInputV31 input) : IRunRecorder
{
    private readonly EntityInstanceId _entity = new(nameof(ConsultGenerationJobEntity), input.JobId);
    private readonly ILogger _logger = context.CreateReplaySafeLogger(nameof(EntityRunRecorder));

    public Task RowAsync(RunRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var state = StateName(row.State);
        _logger.LogInformation(
            "V31 row. JobId={JobId}, Key={Key}, Kind={Kind}, State={State}, ValueHash={ValueHash}, InputHash={InputHash}, RunNeutralHash={RunNeutralHash}",
            input.JobId,
            row.Key,
            row.Kind,
            state,
            row.ValueHash,
            SourceLayout.Text(row.Detail["inputHash"]),
            SourceLayout.Text(row.Detail["runNeutralHash"]));
        return context.Entities.SignalEntityAsync(_entity, nameof(ConsultGenerationJobEntity.RecordV31Progress),
            new ConsultGenerationV31Progress(row.Key, state));
    }

    public Task DecisionAsync(RunDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return context.Entities.SignalEntityAsync(_entity, nameof(ConsultGenerationJobEntity.RecordV31Progress),
            new ConsultGenerationV31Progress(decision.Owner, null));
    }

    /// <summary>
    /// #1505 (area 08; part 08 § 10; D13): a delivery row goes to telemetry as its key, kind, result and attempt count (its
    /// reason is a fixed sentence, its attempts' outcomes error kinds: neither carries a value), and is counted on the entity
    /// as <c>delivery:&lt;result&gt;</c>, apart from the run's row states.
    /// </summary>
    public Task DeliveryAsync(DeliveryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _logger.LogInformation(
            "V31 delivery. JobId={JobId}, Key={Key}, Kind={Kind}, Result={Result}, Attempts={Attempts}",
            input.JobId,
            row.Key,
            row.Kind,
            row.Result,
            row.Attempts.Count);
        return context.Entities.SignalEntityAsync(_entity, nameof(ConsultGenerationJobEntity.RecordV31Progress),
            new ConsultGenerationV31Progress(row.Key, DeliveryStateName(row.Result)));
    }

    /// <summary>
    /// #1505 (area 08; part 09 § 3.9; D14): a notice alerts the operator through
    /// <see cref="ConsultGenerationActivityNames.AlertOperatorV31"/>, a durable task of the orchestration's, so a replay reads it
    /// raised and never raises it twice: its kind, the job, its key and its minutes. An alert that could not be sent never
    /// fails the run.
    /// </summary>
    public Task NoticeAsync(RunNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return AlertOperatorV31Activity.RaiseAsync(context, new OperatorAlert(notice.Kind, input.JobId, notice.Key, notice.Minutes), _logger);
    }

    /// <summary>A delivery row's result as the entity counts it: <c>delivery:sent</c>, <c>delivery:not sent</c>, <c>delivery:failed</c>.</summary>
    internal static string DeliveryStateName(string result) => $"delivery:{result}";

    /// <summary>Part 08's word for a row's state.</summary>
    internal static string StateName(RowState state) => state switch
    {
        RowState.Completed => "completed",
        RowState.Skipped => "skipped",
        RowState.NotRun => "not_run",
        RowState.Failed => "failed",
        RowState.Cancelled => "cancelled",
        RowState.Released => "released",
        RowState.Held => "held",
        RowState.Blocked => "blocked",
        _ => state.ToString(),
    };
}
