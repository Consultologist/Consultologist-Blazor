using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>#1503 (specVersion 31, area 06): an activity's input: the job's package version, its <c>resolveHash</c>, and the call.</summary>
public sealed record RenderNodeV31Input(string PackageName, string PackageVersion, string ResolveHash, RenderCall Call);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.1): a rendered node's or a deliverable's render, in an activity, as templates
/// render today: the plan from the store's cache (the job's own, by its <c>resolveHash</c>), the entry's body composed over
/// the call's scope within its step budget. A plan that is not the job's, or an entry it lacks, is the render's failure.
/// Telemetry: the key, the length and hash of what was rendered, never the text.
/// </summary>
public sealed class RenderNodeV31Activity(ICompiledPackageStore store, IBodyComposer composer, ILogger<RenderNodeV31Activity> logger)
{
    [Function(ConsultGenerationActivityNames.RenderNodeV31)]
    public async Task<RenderOutcome> RunAsync([ActivityTrigger] RenderNodeV31Input input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (plan, refusal) = await V31Plans.LoadAsync(store, input.PackageName, input.PackageVersion, input.ResolveHash, cancellationToken);
        if (plan == null)
            return new RenderOutcome(null, false, refusal);

        var call = input.Call;
        var composed = composer.Compose(plan, call.EntryKey, call.Function, call.Scope, call.StepLimit);
        logger.LogInformation(
            "V31 render. Key={Key}, TooManySteps={TooManySteps}, Failed={Failed}, Length={Length}, Hash={Hash}",
            call.Key,
            composed.TooManySteps,
            composed.Failure != null,
            composed.Text?.Length,
            composed.Text == null ? null : CanonicalJson.HashText(composed.Text));
        return new RenderOutcome(composed.Text, composed.TooManySteps, composed.Failure);
    }
}

/// <summary>#1503 (specVersion 31, area 06): a job's plan as an activity loads it.</summary>
internal static class V31Plans
{
    /// <summary>
    /// The plan of <paramref name="name"/>@<paramref name="version"/> from the store (its cache after the first load), held
    /// to the job's <paramref name="resolveHash"/>: a version not stamped 31 is A06-004's; a refusal is its sentence; a plan
    /// whose hash is not the job's is S09-009's, so a job never runs bytes other than the ones its start checked.
    /// </summary>
    public static async Task<(RunPlan? Plan, string? Refusal)> LoadAsync(
        ICompiledPackageStore store, string name, string version, string resolveHash, CancellationToken cancellationToken)
    {
        var load = await store.TryLoadAsync(new WorkflowPackageRef(name, version), cancellationToken);
        if (load == null)
            return (null, CompiledPackageStore.NotPublished(name, version));
        if (load.Plan == null)
            return (null, load.Refusal ?? HashMismatch(name, version));
        if (!string.Equals(load.Plan.ResolveHash, resolveHash, StringComparison.Ordinal))
            return (null, HashMismatch(name, version));
        return (load.Plan, null);
    }

    /// <summary>S09-009 for <paramref name="name"/>@<paramref name="version"/>.</summary>
    public static string HashMismatch(string name, string version) => CompiledPackageStore.HashMismatch(name, version);
}
