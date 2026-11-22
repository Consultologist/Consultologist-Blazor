using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06): an <see cref="IRunRecorder"/> that keeps the rows and decisions in order, each row with
/// what <paramref name="stamp"/> gave when it was recorded (the agent calls made so far, say; 0 without one), and #1505
/// (area 08) the delivery rows and the notices for the operator.
/// </summary>
internal sealed class ListRecorder(Func<int>? stamp = null) : IRunRecorder
{
    /// <summary>#1506 (area 09; D12): the flushes awaited.</summary>
    public int Flushes { get; private set; }

    /// <summary>#1506: everything recorded, in order, as <c>row</c>, <c>decision</c>, <c>delivery</c> or <c>flush</c> with its key.</summary>
    public List<string> Events { get; } = [];

    /// <inheritdoc/>
    public Task FlushAsync()
    {
        Flushes++;
        Events.Add("flush");
        return Task.CompletedTask;
    }

    /// <summary>The rows recorded, in order.</summary>
    public List<RunRow> Rows { get; } = [];

    /// <summary>Each row's stamp, in the order of <see cref="Rows"/>.</summary>
    public List<int> Stamps { get; } = [];

    /// <summary>The decisions recorded, in order.</summary>
    public List<RunDecision> Decisions { get; } = [];

    /// <summary>The row keyed <paramref name="key"/>.</summary>
    public RunRow Row(string key) => Rows.Single(r => r.Key == key);

    /// <inheritdoc/>
    public Task RowAsync(RunRow row)
    {
        Rows.Add(row);
        Events.Add("row " + row.Key);
        Stamps.Add(stamp?.Invoke() ?? 0);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DecisionAsync(RunDecision decision)
    {
        Decisions.Add(decision);
        Events.Add("decision " + decision.Id);
        return Task.CompletedTask;
    }

    /// <summary>#1505 (area 08): the delivery rows recorded, in order.</summary>
    public List<DeliveryRow> Deliveries { get; } = [];

    /// <inheritdoc/>
    public Task DeliveryAsync(DeliveryRow row)
    {
        Deliveries.Add(row);
        Events.Add("delivery " + row.Key);
        return Task.CompletedTask;
    }

    /// <summary>#1505 (area 08; D14): the notices for the operator raised, in order.</summary>
    public List<RunNotice> Notices { get; } = [];

    /// <inheritdoc/>
    public Task NoticeAsync(RunNotice notice)
    {
        Notices.Add(notice);
        return Task.CompletedTask;
    }
}
