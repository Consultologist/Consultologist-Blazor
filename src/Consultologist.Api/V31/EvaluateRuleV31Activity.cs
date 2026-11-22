using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1505 (specVersion 31, area 08; R6): an activity's input: the job's package version, its <c>resolveHash</c>, R2's regex
/// limit as the job's start recorded it (<see cref="ConsultGenerationOrchestrationInputV31.RegexMilliseconds"/>), and the call.
/// </summary>
public sealed record EvaluateRuleV31Input(string PackageName, string PackageVersion, string ResolveHash, int RegexMilliseconds, RuleCall Call);

/// <summary>
/// #1505 (specVersion 31, area 08; R6; part 09 § 3.1): a rule that holds <c>matches</c> or <c>has_word</c>, evaluated in an
/// activity so its outcome is stored in the orchestration's history and a replay reads it, never timing the pattern again.
/// The plan is the store's cache's (the job's own, by its <c>resolveHash</c>), as <see cref="RenderNodeV31Activity"/> loads
/// it, and the outcome is <see cref="RuleTurn.Evaluate"/>'s: the entry's compiled rule over the call's scope, under the
/// job's recorded regex limit (<see cref="EvaluateRuleV31Input.RegexMilliseconds"/>), never the setting now, so a job is
/// timed as its start read it: a plan compiled under another limit is the orchestrations' cache's for the job's
/// (<see cref="ConsultGenerationOrchestratorV31.Plans"/>), else built from the verified document and kept there. A plan that is not the job's, or an entry or a rule it lacks, is <see cref="RuleTurn.PlanFailure"/>.
/// The scope can hold input, model and profile values: telemetry is the key, the entry, the function, the member and the
/// failure's kind, never the scope or the value.
/// </summary>
public sealed class EvaluateRuleV31Activity(ICompiledPackageStore store, ILogger<EvaluateRuleV31Activity> logger)
{
    [Function(ConsultGenerationActivityNames.EvaluateRuleV31)]
    public async Task<RuleOutcome> RunAsync([ActivityTrigger] EvaluateRuleV31Input input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var call = input.Call;
        var (plan, _) = await V31Plans.LoadAsync(store, input.PackageName, input.PackageVersion, input.ResolveHash, logger, cancellationToken);
        var timed = plan == null ? null : AtTheJobsLimit(plan, input.RegexMilliseconds);
        var outcome = timed == null ? new RuleOutcome(null, RuleTurn.PlanFailure, null) : RuleTurn.Evaluate(timed, call);
        logger.LogInformation(
            "V31 rule. Key={Key}, EntryKey={EntryKey}, Function={Function}, Member={Member}, FailureKind={FailureKind}",
            call.Key, call.EntryKey, call.Function, call.Member, outcome.FailureKind);
        return outcome;
    }

    /// <summary>
    /// <paramref name="plan"/> with its rules compiled under <paramref name="regexMilliseconds"/>: itself when that is its
    /// limit; else the cache's plan for its <c>resolveHash</c> and that limit; else built from its document (the verified
    /// bytes, as <see cref="LoadPlanV31Activity"/> sends them) and cached. Null when no plan can be made of it.
    /// </summary>
    private static RunPlan? AtTheJobsLimit(RunPlan plan, int regexMilliseconds)
    {
        var limit = TimeSpan.FromMilliseconds(regexMilliseconds);
        if (plan.Evaluator.MatchTimeLimit == limit)
            return plan;
        if (ConsultGenerationOrchestratorV31.Plans.TryGet(plan.ResolveHash, regexMilliseconds) is { } cached)
            return cached;

        var (built, _) = RunPlan.Build(CanonicalJson.Write(plan.Document), plan.ResolveHash, plan.LockHash, limit);
        return built == null ? null : ConsultGenerationOrchestratorV31.Plans.Add(plan.ResolveHash, regexMilliseconds, built);
    }
}
