using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>#1503 (specVersion 31, area 06): an <see cref="IRunRecorder"/> that keeps the rows and decisions in order.</summary>
internal sealed class ListRecorder : IRunRecorder
{
    /// <summary>The rows recorded, in order.</summary>
    public List<RunRow> Rows { get; } = [];

    /// <summary>The decisions recorded, in order.</summary>
    public List<RunDecision> Decisions { get; } = [];

    /// <summary>The row keyed <paramref name="key"/>.</summary>
    public RunRow Row(string key) => Rows.Single(r => r.Key == key);

    /// <inheritdoc/>
    public Task RowAsync(RunRow row)
    {
        Rows.Add(row);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DecisionAsync(RunDecision decision)
    {
        Decisions.Add(decision);
        return Task.CompletedTask;
    }
}
