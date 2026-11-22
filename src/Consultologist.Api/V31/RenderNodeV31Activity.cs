using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06): an activity's input: the job's package version, its <c>resolveHash</c>, and the call
/// (#1504: with the runner's placement decisions, <see cref="RenderCall.Placed"/>).
/// </summary>
public sealed record RenderNodeV31Input(string PackageName, string PackageVersion, string ResolveHash, RenderCall Call);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.1): a rendered node's or a deliverable's render, in an activity: the plan
/// from the store's cache (the job's own, by its <c>resolveHash</c>). #1504 (area 07; part 05 §§ 4, 9; part 08 §§ 4.2, 8.2,
/// 10): the render is <see cref="RenderTurn.Render"/>'s (the entry's body composed over the call's scope with its
/// placements decided, within its step budget): its text, its run-neutral text and the row's detail. A plan that is not
/// the job's, or an entry it lacks, is the render's failure. Telemetry: the key, the lengths and hashes of what was
/// rendered, never the text.
/// </summary>
public sealed class RenderNodeV31Activity(ICompiledPackageStore store, ILogger<RenderNodeV31Activity> logger)
{
    [Function(ConsultGenerationActivityNames.RenderNodeV31)]
    public async Task<RenderOutcome> RunAsync([ActivityTrigger] RenderNodeV31Input input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (plan, refusal) = await V31Plans.LoadAsync(store, input.PackageName, input.PackageVersion, input.ResolveHash, logger, cancellationToken);
        if (plan == null)
            return new RenderOutcome(null, false, refusal);

        var call = input.Call;
        var rendered = RenderTurn.Render(plan, call);
        V31EngineFaults.Log(logger, rendered.EngineFault, call.Key, call.EntryKey, call.Function);
        logger.LogInformation(
            "V31 render. Key={Key}, TooManySteps={TooManySteps}, Failed={Failed}, Length={Length}, Hash={Hash}, NeutralLength={NeutralLength}, NeutralHash={NeutralHash}",
            call.Key,
            rendered.TooManySteps,
            rendered.Failure != null,
            rendered.Text?.Length,
            rendered.Text == null ? null : CanonicalJson.HashText(rendered.Text),
            rendered.NeutralText?.Length,
            rendered.NeutralText == null ? null : CanonicalJson.HashText(rendered.NeutralText));
        return rendered;
    }
}

/// <summary>
/// #1505 ruling 6 (R14): an exception the engine met reading a template (A08-006, the engine's fault, never the author's),
/// logged once per composition at error level by the sentence id, the row's key, entry and function, and the exception's
/// type: never its message, its stack or any text.
/// </summary>
internal static class V31EngineFaults
{
    public static void Log(ILogger logger, string? exceptionType, string key, string entryKey, string? function)
    {
        if (exceptionType == null)
            return;
        logger.LogError(
            "V31 engine fault reading a template. SentenceId={SentenceId}, Key={Key}, EntryKey={EntryKey}, Function={Function}, ExceptionType={ExceptionType}",
            "A08-006", key, entryKey, function, exceptionType);
    }
}

/// <summary>#1503 (specVersion 31, area 06): a job's plan as an activity loads it.</summary>
internal static class V31Plans
{
    /// <summary>
    /// The plan of <paramref name="name"/>@<paramref name="version"/> from the store (its cache after the first load), held
    /// to the job's <paramref name="resolveHash"/>: a version not stamped 31 is A06-004's; a refusal is its sentence; a plan
    /// whose hash is not the job's is S09-009's, so a job never runs bytes other than the ones its start checked. A registry
    /// that could not be read throws <see cref="InvalidOperationException"/> (the operator's detail, no value).
    /// </summary>
    public static async Task<(RunPlan? Plan, string? Refusal)> LoadAsync(
        ICompiledPackageStore store, string name, string version, string resolveHash, ILogger logger, CancellationToken cancellationToken)
    {
        var load = await store.TryLoadAsync(new WorkflowPackageRef(name, version), cancellationToken);
        if (load == null)
            return (null, CompiledPackageStore.NotPublished(name, version));
        // A registry that could not be read is the engine's fault, not the package's: the activity fails, as it did when
        // the read threw, and the job with it (A06-010).
        if (load.Unreadable)
            throw new InvalidOperationException(load.OperatorDetail);
        if (load.Plan == null)
        {
            // D5: the job shows the S09 sentence; the operator's log keeps the store's (the S07 sentence: package text and hashes).
            logger.LogWarning(
                "V31 plan refused at run. Package={Package}@{Version}, RefusalId={RefusalId}, Detail={Detail}",
                name, version, load.RefusalId, load.OperatorDetail ?? load.Refusal);
            return (null, load.Refusal ?? HashMismatch(name, version));
        }
        if (!string.Equals(load.Plan.ResolveHash, resolveHash, StringComparison.Ordinal))
            return (null, HashMismatch(name, version));
        return (load.Plan, null);
    }

    /// <summary>S09-009 for <paramref name="name"/>@<paramref name="version"/>.</summary>
    public static string HashMismatch(string name, string version) => CompiledPackageStore.HashMismatch(name, version);
}
