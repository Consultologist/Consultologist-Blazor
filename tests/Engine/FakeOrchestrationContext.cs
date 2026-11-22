using System.Reflection;
using System.Text.Json;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Consultologist.Api.Tests.Engine;

/// <summary>
/// #1341 B2a: a Durable orchestration context for driving the real
/// <c>ConsultGenerationOrchestrator.RunAsync</c> in a test, deterministically. An
/// activity call is answered by the responder for its name; an entity call is run
/// on a real in-memory entity — its input and result round-trip through JSON, as
/// they do through the Durable store. Every call is recorded, in order. Responders
/// return completed tasks, so the orchestrator's <c>Task.WhenAny</c> picks the
/// first in its list, every run. #1503: a test may hold activity answers
/// (<see cref="HoldActivities"/>) and timers longer than a span
/// (<see cref="HoldTimersLongerThan"/>), which then wait until cancelled, as
/// durable timers do; by default neither is held, as before. #1503 (D9): an
/// activity may be answered unrecorded (<see cref="Unrecorded"/>), and an external
/// event waited for is given by <see cref="RaiseEvent"/>.
/// </summary>
internal sealed class FakeOrchestrationContext : TaskOrchestrationContext
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    private readonly object? _input;
    private readonly Dictionary<string, Func<object?, object?>> _activities = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unrecorded = new(StringComparer.Ordinal);
    private readonly List<(string Name, TaskCompletionSource<object?> Event)> _awaitedEvents = new();
    private readonly FakeEntities _entities;
    private readonly List<(string Name, string InputJson, Action Answer)> _heldActivities = new();
    private readonly List<TaskCompletionSource> _heldTimers = new();
    private readonly Dictionary<string, int> _peakHeld = new(StringComparer.Ordinal);
    private bool _holdActivities;
    private bool _wrapFailures;
    private TimeSpan? _holdTimersLongerThan;
    private DateTime _now;

    public FakeOrchestrationContext(string instanceId, object? input, object entity, DateTime now)
    {
        InstanceId = instanceId;
        _input = input;
        _now = now;
        _entities = new FakeEntities(entity, Calls);
    }

    /// <summary>Every activity and entity call, in the order the orchestrator made it.</summary>
    public List<(string Kind, string Name, string InputJson)> Calls { get; } = new();

    public List<string> CustomStatuses { get; } = new();

    /// <summary>Answers activity <paramref name="name"/> with <paramref name="respond"/>.</summary>
    public FakeOrchestrationContext On(string name, Func<object?, object?> respond)
    {
        _activities[name] = respond;
        _unrecorded.Remove(name);
        return this;
    }

    /// <summary>
    /// #1503 (D9): answers activity <paramref name="name"/> with <paramref name="respond"/> and leaves its calls out of
    /// <see cref="Calls"/>, so a harness whose trace predates the activity keeps its trace.
    /// </summary>
    public FakeOrchestrationContext Unrecorded(string name, Func<object?, object?> respond)
    {
        On(name, respond);
        _unrecorded.Add(name);
        return this;
    }

    /// <summary>#1503 (D9): ends every held timer, as their time coming would.</summary>
    public void EndHeldTimers()
    {
        foreach (var timer in _heldTimers.ToList())
        {
            _heldTimers.Remove(timer);
            timer.TrySetResult();
        }
    }

    /// <summary>#1503 (D9): moves the orchestration's clock on by <paramref name="span"/>, as time passes between episodes.</summary>
    public void Advance(TimeSpan span) => _now += span;

    /// <summary>#1503 (D9): the external events the orchestration is waiting for, by name, oldest first.</summary>
    public IReadOnlyList<string> AwaitedEvents => _awaitedEvents.Select(waiting => waiting.Name).ToList();

    /// <summary>#1503 (D9): raises event <paramref name="name"/>: every wait for it ends. Returns how many were waiting.</summary>
    public int RaiseEvent(string name)
    {
        var waiting = _awaitedEvents.Where(awaited => awaited.Name == name).ToList();
        foreach (var awaited in waiting)
        {
            _awaitedEvents.Remove(awaited);
            awaited.Event.TrySetResult(null);
        }

        return waiting.Count;
    }

    /// <summary>
    /// #1503: holds every activity's answer until <see cref="ReleaseOldest"/> gives it, so a test can see what is in flight
    /// at once (<see cref="PeakHeld"/>). Off by default: answers are given at once.
    /// </summary>
    public FakeOrchestrationContext HoldActivities()
    {
        _holdActivities = true;
        return this;
    }

    /// <summary>
    /// #1503: holds every timer that ends more than <paramref name="span"/> from now until it is cancelled (its task then
    /// ends cancelled), as a durable timer would wait; a shorter one ends at once. Off by default: every timer ends at once.
    /// </summary>
    public FakeOrchestrationContext HoldTimersLongerThan(TimeSpan span)
    {
        _holdTimersLongerThan = span;
        return this;
    }

    /// <summary>
    /// #1503: an activity whose responder throws fails as Durable fails it: with a <see cref="TaskFailedException"/> carrying
    /// the activity's exception. Off by default: the responder's exception itself.
    /// </summary>
    public FakeOrchestrationContext WrapActivityFailures()
    {
        _wrapFailures = true;
        return this;
    }

    /// <summary>#1503: the activities whose answers are held.</summary>
    public int HeldActivities => _heldActivities.Count;

    /// <summary>#1503: the held timers neither ended nor cancelled.</summary>
    public int PendingTimers => _heldTimers.Count;

    /// <summary>#1503: the most answers of activity <paramref name="name"/> held at once.</summary>
    public int PeakHeld(string name) => _peakHeld.GetValueOrDefault(name);

    /// <summary>#1503: gives the oldest held answer.</summary>
    public void ReleaseOldest()
    {
        var (_, _, answer) = _heldActivities[0];
        _heldActivities.RemoveAt(0);
        answer();
    }

    /// <summary>
    /// #1505: gives the oldest held answer whose activity name and input JSON <paramref name="which"/> holds; false when none
    /// does (the others stay held).
    /// </summary>
    public bool ReleaseOldest(Func<string, string, bool> which)
    {
        var at = _heldActivities.FindIndex(held => which(held.Name, held.InputJson));
        if (at < 0)
        {
            return false;
        }

        var (_, _, answer) = _heldActivities[at];
        _heldActivities.RemoveAt(at);
        answer();
        return true;
    }

    public override TaskName Name => new("ConsultGenerationOrchestrator");

    public override string InstanceId { get; }

    public override ParentOrchestrationInstance? Parent => null;

    public override DateTime CurrentUtcDateTime => _now;

    public override bool IsReplaying => false;

    public override TaskOrchestrationEntityFeature Entities => _entities;

    protected override ILoggerFactory LoggerFactory => new CapturingFactory(Log);

    /// <summary>#1506: what the orchestration's replay-safe loggers wrote, every category together.</summary>
    public CapturingLogger<FakeOrchestrationContext> Log { get; } = new();

    /// <summary>A factory whose every logger writes to <see cref="Log"/>.</summary>
    private sealed class CapturingFactory(ILogger logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    public override T GetInput<T>() => (T)_input!;

    public override Task<TResult> CallActivityAsync<TResult>(TaskName name, object? input = null, TaskOptions? options = null)
    {
        var json = JsonSerializer.Serialize(input, input?.GetType() ?? typeof(object), Json);
        if (!_unrecorded.Contains(name.Name))
        {
            Calls.Add(("activity", name.Name, json));
        }

        if (!_activities.TryGetValue(name.Name, out var respond))
        {
            throw new InvalidOperationException($"The test answers no activity '{name.Name}'.");
        }

        // A responder that throws is an activity that failed: the orchestrator awaits a faulted task.
        if (!_holdActivities)
        {
            try
            {
                return Task.FromResult(RoundTrip<TResult>(respond(input)));
            }
            catch (Exception ex)
            {
                return Task.FromException<TResult>(Failure(name, ex));
            }
        }

        var answer = new TaskCompletionSource<TResult>();
        _heldActivities.Add((name.Name, json, () =>
        {
            try
            {
                answer.SetResult(RoundTrip<TResult>(respond(input)));
            }
            catch (Exception ex)
            {
                answer.SetException(Failure(name, ex));
            }
        }));
        _peakHeld[name.Name] = Math.Max(_peakHeld.GetValueOrDefault(name.Name), _heldActivities.Count(held => held.Name == name.Name));
        return answer.Task;
    }

    private Exception Failure(TaskName name, Exception ex) =>
        _wrapFailures ? new TaskFailedException(name.Name, Calls.Count, ex) : ex;

    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
    {
        Calls.Add(("timer", "CreateTimer", JsonSerializer.Serialize(fireAt, Json)));
        if (_holdTimersLongerThan is not { } span || fireAt - CurrentUtcDateTime <= span)
        {
            return Task.CompletedTask;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var timer = new TaskCompletionSource();
        _heldTimers.Add(timer);
        cancellationToken.Register(() =>
        {
            _heldTimers.Remove(timer);
            timer.TrySetCanceled(cancellationToken);
        });
        return timer.Task;
    }

    public override void SetCustomStatus(object? customStatus) =>
        CustomStatuses.Add(JsonSerializer.Serialize(customStatus, customStatus?.GetType() ?? typeof(object), Json));

    public override async Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default)
    {
        Calls.Add(("event", eventName, "null"));
        var awaited = (eventName, new TaskCompletionSource<object?>());
        _awaitedEvents.Add(awaited);
        using var registration = cancellationToken.Register(() =>
        {
            _awaitedEvents.Remove(awaited);
            awaited.Item2.TrySetCanceled(cancellationToken);
        });
        return RoundTrip<T>(await awaited.Item2.Task);
    }

    public override void SendEvent(string instanceId, string eventName, object payload) =>
        throw new NotSupportedException("The orchestrator sends no event.");

    public override Task<TResult> CallSubOrchestratorAsync<TResult>(TaskName orchestratorName, object? input = null, TaskOptions? options = null) =>
        throw new NotSupportedException("The orchestrator starts no sub-orchestration.");

    public override void ContinueAsNew(object? newInput = null, bool preserveUnprocessedEvents = true) =>
        throw new NotSupportedException("The orchestrator does not continue as new.");

    public override Guid NewGuid() => throw new NotSupportedException("The orchestrator mints no guid.");

    internal static T RoundTrip<T>(object? value) =>
        value is null ? default! : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, value.GetType(), Json), Json)!;

    /// <summary>The entity feature: each call runs the named operation on the one entity.</summary>
    private sealed class FakeEntities(object entity, List<(string Kind, string Name, string InputJson)> calls) : TaskOrchestrationEntityFeature
    {
        public override async Task<TResult> CallEntityAsync<TResult>(EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null) =>
            RoundTrip<TResult>(await InvokeAsync(operationName, input));

        public override async Task CallEntityAsync(EntityInstanceId id, string operationName, object? input = null, CallEntityOptions? options = null) =>
            await InvokeAsync(operationName, input);

        public override async Task SignalEntityAsync(EntityInstanceId id, string operationName, object? input = null, SignalEntityOptions? options = null) =>
            await InvokeAsync(operationName, input);

        public override Task<IAsyncDisposable> LockEntitiesAsync(IEnumerable<EntityInstanceId> entityIds) =>
            throw new NotSupportedException("The orchestrator takes no entity lock.");

        public override bool InCriticalSection(out IReadOnlyList<EntityInstanceId> entityIds)
        {
            entityIds = Array.Empty<EntityInstanceId>();
            return false;
        }

        private async Task<object?> InvokeAsync(string operation, object? input)
        {
            var json = input is null ? "null" : JsonSerializer.Serialize(input, input.GetType(), Json);
            calls.Add(("entity", operation, json));

            var method = entity.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(m => m.Name == operation && m.DeclaringType == entity.GetType());
            var parameters = method.GetParameters();
            var arguments = parameters.Length == 0
                ? Array.Empty<object?>()
                : new[] { JsonSerializer.Deserialize(json, parameters[0].ParameterType, Json) };
            var returned = method.Invoke(entity, arguments);

            switch (returned)
            {
                case Task task:
                    await task;
                    return task.GetType().IsGenericType ? task.GetType().GetProperty("Result")!.GetValue(task) : null;
                default:
                    return returned;
            }
        }
    }
}
