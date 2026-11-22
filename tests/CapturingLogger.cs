using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Tests;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps everything it is handed, for the
/// logging audit § 9 requires (#241): no byte of document content and no
/// filename may reach Application Insights on any path, including the
/// exception paths.
///
/// It records more than the rendered message on purpose. Application Insights
/// stores the structured values as customDimensions, so a template that reads
/// safely — "Rejected. Detail={Detail}" — still ships whatever Detail held.
/// Scopes and exception strings are captured for the same reason: a leak in
/// any of the three is a leak.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _recorded = [];
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    internal IReadOnlyList<string> Recorded => _recorded;

    /// <summary>Each line logged, with its level and its rendered message.</summary>
    internal IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    /// <summary>Everything written, as one string to search.</summary>
    internal string Everything => string.Join("\n", _recorded);

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        Record(state);
        return NullScope.Instance;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _recorded.Add(formatter(state, exception));
        _entries.Add((logLevel, formatter(state, exception)));
        Record(state);

        if (exception != null)
        {
            // ToString rather than Message: the stack and any inner exception
            // are shipped too, and a parser exception can carry a fragment of
            // what it was reading.
            _recorded.Add(exception.ToString());
        }
    }

    private void Record<TState>(TState state)
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> values)
        {
            foreach (var (key, value) in values)
            {
                _recorded.Add($"{key}={value}");
            }

            return;
        }

        if (state != null)
        {
            _recorded.Add(state.ToString() ?? string.Empty);
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
