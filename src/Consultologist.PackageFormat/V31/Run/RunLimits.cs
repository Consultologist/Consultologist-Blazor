using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.2, 3.4; part 08 § 3): every engine limit in force for one job, each an
/// engine setting, recorded once in the job header's <c>limits</c>. The defaults are the spec's starting values. A limit the
/// runner counts against (<see cref="MaxAgentCalls"/>, <see cref="CapacityWaitMinutes"/>, <see cref="ServiceAttempts"/>,
/// <see cref="JobMinutes"/>, <see cref="LoopItems"/>, <see cref="JobAgentCalls"/>, <see cref="RenderSteps"/>) below 1 is
/// refused, through <c>with</c> too, with an <see cref="ArgumentOutOfRangeException"/> naming it: it is an engine setting, not
/// package content.
/// </summary>
/// <param name="MaxAgentCalls">The per-job ceiling: agent calls in flight at once (<c>ConsultGeneration__MaxAgentCallsPerJob</c>).</param>
/// <param name="CapacityWaitMinutes">The minutes a node may wait on 429s in all.</param>
/// <param name="ServiceAttempts">The attempts a node's call is given for another service failure.</param>
/// <param name="JobMinutes">The minutes a job may run from its orchestration's start.</param>
/// <param name="DeliveryMinutes">The minutes delivery may take.</param>
/// <param name="DeliveryAttempts">The attempts each destination gets.</param>
/// <param name="LoopItems">The items one loop instance may be given.</param>
/// <param name="JobAgentCalls">The agent calls one job may make (a retry or a repair is part of its call).</param>
/// <param name="RenderSteps">The steps one render may take.</param>
public sealed record RunLimits(
    int MaxAgentCalls = 16,
    int CapacityWaitMinutes = 10,
    int ServiceAttempts = 5,
    int JobMinutes = 30,
    int DeliveryMinutes = 15,
    int DeliveryAttempts = 3,
    int LoopItems = 200,
    int JobAgentCalls = 500,
    int RenderSteps = 100_000)
{
    private readonly int maxAgentCalls = AtLeastOne(MaxAgentCalls, nameof(MaxAgentCalls));
    private readonly int capacityWaitMinutes = AtLeastOne(CapacityWaitMinutes, nameof(CapacityWaitMinutes));
    private readonly int serviceAttempts = AtLeastOne(ServiceAttempts, nameof(ServiceAttempts));
    private readonly int jobMinutes = AtLeastOne(JobMinutes, nameof(JobMinutes));
    private readonly int loopItems = AtLeastOne(LoopItems, nameof(LoopItems));
    private readonly int jobAgentCalls = AtLeastOne(JobAgentCalls, nameof(JobAgentCalls));
    private readonly int renderSteps = AtLeastOne(RenderSteps, nameof(RenderSteps));

    /// <summary>The per-job ceiling: agent calls in flight at once.</summary>
    public int MaxAgentCalls { get => maxAgentCalls; init => maxAgentCalls = AtLeastOne(value, nameof(MaxAgentCalls)); }

    /// <summary>The minutes a node may wait on 429s in all.</summary>
    public int CapacityWaitMinutes { get => capacityWaitMinutes; init => capacityWaitMinutes = AtLeastOne(value, nameof(CapacityWaitMinutes)); }

    /// <summary>The attempts a node's call is given for another service failure.</summary>
    public int ServiceAttempts { get => serviceAttempts; init => serviceAttempts = AtLeastOne(value, nameof(ServiceAttempts)); }

    /// <summary>The minutes a job may run from its orchestration's start.</summary>
    public int JobMinutes { get => jobMinutes; init => jobMinutes = AtLeastOne(value, nameof(JobMinutes)); }

    /// <summary>The items one loop instance may be given.</summary>
    public int LoopItems { get => loopItems; init => loopItems = AtLeastOne(value, nameof(LoopItems)); }

    /// <summary>The agent calls one job may make.</summary>
    public int JobAgentCalls { get => jobAgentCalls; init => jobAgentCalls = AtLeastOne(value, nameof(JobAgentCalls)); }

    /// <summary>The steps one render may take.</summary>
    public int RenderSteps { get => renderSteps; init => renderSteps = AtLeastOne(value, nameof(RenderSteps)); }

    /// <summary>The header's <c>limits</c> (part 08 § 3, D8): exactly its nine members, in its order.</summary>
    public JsonObject ToJson() => new()
    {
        ["maxAgentCalls"] = MaxAgentCalls,
        ["capacityWaitMinutes"] = CapacityWaitMinutes,
        ["serviceAttempts"] = ServiceAttempts,
        ["jobMinutes"] = JobMinutes,
        ["deliveryMinutes"] = DeliveryMinutes,
        ["deliveryAttempts"] = DeliveryAttempts,
        ["loopItems"] = LoopItems,
        ["jobAgentCalls"] = JobAgentCalls,
        ["renderSteps"] = RenderSteps,
    };

    /// <summary>
    /// The limits <paramref name="limits"/> holds, as <see cref="ToJson"/> wrote them: each of the nine members an integer.
    /// The engine rebuilds a job's limits from what its orchestration input recorded, so a replay never reads a setting
    /// that changed since. A member absent or not an integer is the engine's own error (an <see cref="ArgumentException"/>
    /// naming it), never package content; one below 1 is refused as the constructor refuses it.
    /// </summary>
    public static RunLimits FromJson(JsonObject limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new RunLimits(
            Member(limits, "maxAgentCalls"),
            Member(limits, "capacityWaitMinutes"),
            Member(limits, "serviceAttempts"),
            Member(limits, "jobMinutes"),
            Member(limits, "deliveryMinutes"),
            Member(limits, "deliveryAttempts"),
            Member(limits, "loopItems"),
            Member(limits, "jobAgentCalls"),
            Member(limits, "renderSteps"));
    }

    private static int Member(JsonObject limits, string name) =>
        limits[name] is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number && value.TryGetValue<int>(out var number)
            ? number
            : throw new ArgumentException($"The limits hold no integer '{name}'.", nameof(limits));

    private static int AtLeastOne(int value, string name) =>
        value >= 1 ? value : throw new ArgumentOutOfRangeException(name, value, $"{name} is at least 1.");
}
