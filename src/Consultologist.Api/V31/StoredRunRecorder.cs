using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1506 (specVersion 31, area 09; part 08 § 1.1; part 09 § 3.7; D10, D11, D12; R14): the engine's <see cref="IRunRecorder"/>.
/// A row, a decision and a delivery row are buffered (<see cref="RecordDocument.Of(RunRow)"/> and its twins; a row's settled
/// text, <see cref="RunRow.Text"/>, beside it by key) and their tasks are completed tasks. <see cref="FlushAsync"/> writes
/// the buffer in one <see cref="ConsultGenerationActivityNames.WriteRecordRowsV31"/> call (the texts, then the rows); a row
/// the record holds differently throws <see cref="RecordConflictException"/>, which the orchestration fails as an engine
/// fault (A06-010); then it signals <see cref="ConsultGenerationJobEntity.RecordV31Rows"/> once with each row's key and state
/// (a decision's none, a delivery's <c>delivery:&lt;result&gt;</c>) and the round's token sums, so the entity never counts
/// a row that is not stored. The buffer is plain state, built the same on every replay; the recorder awaits only the
/// context's tasks. A row and a delivery row go to telemetry as their key, kind, state and hashes, never text. #1505 (D14): a
/// notice is an alert for the operator, an activity of the context's.
/// </summary>
public sealed class StoredRunRecorder(TaskOrchestrationContext context, ConsultGenerationOrchestrationInputV31 input) : IRunRecorder
{
    private readonly EntityInstanceId _entity = new(nameof(ConsultGenerationJobEntity), input.JobId);
    private readonly ILogger _logger = context.CreateReplaySafeLogger(nameof(StoredRunRecorder));
    private readonly List<RecordRow> _rows = [];
    private readonly Dictionary<string, JsonObject> _settled = new(StringComparer.Ordinal);
    private readonly List<ConsultGenerationV31Progress> _progress = [];
    private ConsultGenerationV31Tokens? _tokens;

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
        _rows.Add(RecordDocument.Of(row));
        if (row.Text is { Count: > 0 } text)
            _settled[row.Key] = (JsonObject)text.DeepClone();
        _progress.Add(new ConsultGenerationV31Progress(row.Key, state));
        AddTokens(row.Detail["tokens"]);
        return Task.CompletedTask;
    }

    public Task DecisionAsync(RunDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        _rows.Add(RecordDocument.Of(decision));
        _progress.Add(new ConsultGenerationV31Progress(decision.Owner, null));
        return Task.CompletedTask;
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
        _rows.Add(RecordDocument.Of(row));
        _progress.Add(new ConsultGenerationV31Progress(row.Key, DeliveryStateName(row.Result)));
        return Task.CompletedTask;
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

    /// <summary>
    /// #1506 (D12): writes what was recorded since the last flush, then counts it on the entity; nothing when nothing was.
    /// Throws <see cref="RecordConflictException"/> when a row differs from the stored one (the entity is not told of the round).
    /// #1506 (final review F7): throws <see cref="RecordRowTooLargeException"/> (the first such row's key and length) when a
    /// row was too large to store, once the round's other rows are counted. The buffer is cleared before the write, so a
    /// flush that throws leaves nothing to write twice.
    /// </summary>
    public async Task FlushAsync()
    {
        if (_rows.Count == 0)
            return;

        var rows = _rows.ToList();
        var settled = new Dictionary<string, JsonObject>(_settled, StringComparer.Ordinal);
        var progress = _progress.ToList();
        var tokens = _tokens;
        _rows.Clear();
        _settled.Clear();
        _progress.Clear();
        _tokens = null;

        var written = await context.CallActivityAsync<WriteRecordRowsV31Result>(
            ConsultGenerationActivityNames.WriteRecordRowsV31,
            new WriteRecordRowsV31Input(input.JobId, input.AppUserId, input.AccountKind, rows, settled));
        if (written?.Conflicts is { Count: > 0 } conflicts)
            throw new RecordConflictException(conflicts);

        // #1506 (final review F7): a row too large to store was not written; the round's others were, and are counted
        var tooLarge = written?.TooLarge is { Count: > 0 } keys ? new HashSet<string>(keys, StringComparer.Ordinal) : null;
        await context.Entities.SignalEntityAsync(_entity, nameof(ConsultGenerationJobEntity.RecordV31Rows),
            new ConsultGenerationV31Rows(tooLarge == null ? progress : StoredOnly(rows, progress, tooLarge), tokens));
        if (tooLarge != null)
        {
            var first = rows.First(row => tooLarge.Contains(row.Key));
            throw new RecordRowTooLargeException(first.Key, first.Json.Length);
        }
    }

    /// <summary>
    /// The round's progress without the rows that were not stored (<paramref name="tooLarge"/>): <paramref name="rows"/> and
    /// <paramref name="progress"/> are in the same order, one entry per row recorded.
    /// </summary>
    private static List<ConsultGenerationV31Progress> StoredOnly(
        IReadOnlyList<RecordRow> rows, IReadOnlyList<ConsultGenerationV31Progress> progress, HashSet<string> tooLarge)
    {
        var stored = new List<ConsultGenerationV31Progress>(progress.Count);
        for (var i = 0; i < progress.Count; i++)
        {
            if (i >= rows.Count || !tooLarge.Contains(rows[i].Key))
                stored.Add(progress[i]);
        }

        return stored;
    }

    /// <summary>Adds an agent row's <c>tokens { input, output, total }</c> (R14) to the round's.</summary>
    private void AddTokens(JsonNode? tokens)
    {
        if (tokens is not JsonObject sums)
            return;
        var input = Number(sums["input"]);
        var output = Number(sums["output"]);
        var total = sums["total"] is null ? input + output : Number(sums["total"]);
        _tokens = _tokens is { } round
            ? new ConsultGenerationV31Tokens(round.Input + input, round.Output + output, round.Total + total)
            : new ConsultGenerationV31Tokens(input, output, total);
    }

    private static int Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var n) ? n : 0;

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
