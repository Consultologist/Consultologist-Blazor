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
/// first in its list, every run.
/// </summary>
internal sealed class FakeOrchestrationContext : TaskOrchestrationContext
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true };

    private readonly object? _input;
    private readonly Dictionary<string, Func<object?, object?>> _activities = new(StringComparer.Ordinal);
    private readonly FakeEntities _entities;

    public FakeOrchestrationContext(string instanceId, object? input, object entity, DateTime now)
    {
        InstanceId = instanceId;
        _input = input;
        CurrentUtcDateTime = now;
        _entities = new FakeEntities(entity, Calls);
    }

    /// <summary>Every activity and entity call, in the order the orchestrator made it.</summary>
    public List<(string Kind, string Name, string InputJson)> Calls { get; } = new();

    public List<string> CustomStatuses { get; } = new();

    /// <summary>Answers activity <paramref name="name"/> with <paramref name="respond"/>.</summary>
    public FakeOrchestrationContext On(string name, Func<object?, object?> respond)
    {
        _activities[name] = respond;
        return this;
    }

    public override TaskName Name => new("ConsultGenerationOrchestrator");

    public override string InstanceId { get; }

    public override ParentOrchestrationInstance? Parent => null;

    public override DateTime CurrentUtcDateTime { get; }

    public override bool IsReplaying => false;

    public override TaskOrchestrationEntityFeature Entities => _entities;

    protected override ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

    public override T GetInput<T>() => (T)_input!;

    public override Task<TResult> CallActivityAsync<TResult>(TaskName name, object? input = null, TaskOptions? options = null)
    {
        var json = JsonSerializer.Serialize(input, input?.GetType() ?? typeof(object), Json);
        Calls.Add(("activity", name.Name, json));

        if (!_activities.TryGetValue(name.Name, out var respond))
        {
            throw new InvalidOperationException($"The test answers no activity '{name.Name}'.");
        }

        var result = respond(input);
        return Task.FromResult(RoundTrip<TResult>(result));
    }

    public override Task CreateTimer(DateTime fireAt, CancellationToken cancellationToken)
    {
        Calls.Add(("timer", "CreateTimer", JsonSerializer.Serialize(fireAt, Json)));
        return Task.CompletedTask;
    }

    public override void SetCustomStatus(object? customStatus) =>
        CustomStatuses.Add(JsonSerializer.Serialize(customStatus, customStatus?.GetType() ?? typeof(object), Json));

    public override Task<T> WaitForExternalEvent<T>(string eventName, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The orchestrator waits for no external event.");

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
