using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.2, 3.4; part 08 § 3): every engine limit in force for one job, each an
/// engine setting, recorded once in the job header's <c>limits</c>. The defaults are the spec's starting values.
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
}
