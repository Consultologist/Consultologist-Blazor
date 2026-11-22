using Consultologist.Api.Jobs;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>#1505 (specVersion 31, area 08; R6): an activity's input: the job's package version, its <c>resolveHash</c>, and the call.</summary>
public sealed record EvaluateRuleV31Input(string PackageName, string PackageVersion, string ResolveHash, RuleCall Call);

/// <summary>
/// #1505 (specVersion 31, area 08; R6; part 09 § 3.1): a rule that holds <c>matches</c> or <c>has_word</c>, evaluated in an
/// activity so its outcome is stored in the orchestration's history and a replay reads it, never timing the pattern again.
/// The plan is the store's cache's (the job's own, by its <c>resolveHash</c>), as <see cref="RenderNodeV31Activity"/> loads
/// it, and the outcome is <see cref="RuleTurn.Evaluate"/>'s: the entry's compiled rule over the call's scope, under the
/// plan's time limit. A plan that is not the job's, or an entry or a rule it lacks, is <see cref="RuleTurn.PlanFailure"/>.
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
        var outcome = plan == null ? new RuleOutcome(null, RuleTurn.PlanFailure, null) : RuleTurn.Evaluate(plan, call);
        logger.LogInformation(
            "V31 rule. Key={Key}, EntryKey={EntryKey}, Function={Function}, Member={Member}, FailureKind={FailureKind}",
            call.Key, call.EntryKey, call.Function, call.Member, outcome.FailureKind);
        return outcome;
    }
}
