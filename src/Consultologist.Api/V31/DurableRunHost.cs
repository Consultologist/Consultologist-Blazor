using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.DurableTask;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.1, 3.4; D7): <see cref="IRunHost"/> on a Durable orchestration. The clock is
/// the replay-safe <see cref="TaskOrchestrationContext.CurrentUtcDateTime"/>; an agent call's attempt is
/// <see cref="ConsultGenerationActivityNames.RunAgentNodeV31"/> and a render <see cref="ConsultGenerationActivityNames.RenderNodeV31"/>,
/// each given the call with the job's package version and <c>resolveHash</c> (an agent's also the job's recorded
/// <c>renderSteps</c>, from <paramref name="limits"/>; #1506 the job, its account and its kind, where the activity writes the
/// text it produced), with no retry policy (the runner retries); #1505 (R6) a rule that holds
/// <c>matches</c> or <c>has_word</c> is <see cref="ConsultGenerationActivityNames.EvaluateRuleV31"/>, given the call the same
/// way and the job's recorded regex limit, with no retry policy, so its outcome is in the history and a replay reads it;
/// a wait is a durable timer, cancelled with the runner's token (an outstanding timer keeps an orchestration from completing).
/// </summary>
public sealed class DurableRunHost(TaskOrchestrationContext context, ConsultGenerationOrchestrationInputV31 input, RunLimits limits) : IRunHost
{
    public DateTime UtcNow => context.CurrentUtcDateTime;

    public Task<AgentOutcome> CallAgentAsync(AgentCall call) =>
        context.CallActivityAsync<AgentOutcome>(
            ConsultGenerationActivityNames.RunAgentNodeV31,
            new RunAgentNodeV31Input(input.PackageName, input.PackageVersion, input.ResolveHash, limits.RenderSteps, call,
                input.JobId, input.AppUserId, input.AccountKind));

    public Task<RenderOutcome> RenderAsync(RenderCall call) =>
        context.CallActivityAsync<RenderOutcome>(
            ConsultGenerationActivityNames.RenderNodeV31,
            new RenderNodeV31Input(input.PackageName, input.PackageVersion, input.ResolveHash, call,
                input.JobId, input.AppUserId, input.AccountKind));

    public Task<RuleOutcome> EvaluateAsync(RuleCall call) =>
        context.CallActivityAsync<RuleOutcome>(
            ConsultGenerationActivityNames.EvaluateRuleV31,
            new EvaluateRuleV31Input(input.PackageName, input.PackageVersion, input.ResolveHash, input.RegexMilliseconds, call));

    public Task DelayAsync(TimeSpan wait, CancellationToken cancel) =>
        context.CreateTimer(context.CurrentUtcDateTime + (wait > TimeSpan.Zero ? wait : TimeSpan.Zero), cancel);
}
