using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v10 (#496, package-format-v10-design.md § 5): what the boundary asks. The
/// supplied values ride as their wire JSON; the classifiers' answers are the
/// orchestrator's replayed outputs.
/// </summary>
public sealed record ConsultDecideActivityInput(
    string WorkflowPackage,
    IReadOnlyDictionary<string, string>? SuppliedInputs,
    IReadOnlyDictionary<string, string> Classifications);

/// <summary>
/// What the boundary decided. Results empty = nothing applies (the
/// skipped set says what each wanted); EmptyFanLabels non-empty = a fanned
/// input had no entries (the same refusal v9 makes at start).
/// </summary>
public sealed record ConsultDecisionResult(
    IReadOnlyList<ConsultResultDescriptor> Results,
    IReadOnlyList<ConsultSkippedDocument> Skipped,
    IReadOnlyList<ConsultNodeDescriptor> Nodes,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Items,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> CollectionSets,
    IReadOnlyList<ConsultCollectionRoster> CollectionRosters,
    IReadOnlyList<ConsultItemStepDescriptor> ItemSteps,
    IReadOnlyList<string> EmptyFanLabels,
    // v12 #631 (§ 14): macros the firing results' when-clauses excluded —
    // null when nothing was gated out (the control's bytes). Trailing: the
    // engine reads this positionally off the recorded activity result.
    IReadOnlyList<ConsultExcludedMacro>? ExcludedMacros = null);

/// <summary>
/// The boundary as an activity: block expansion and the node closure need
/// the resolved package, which the orchestrator cannot hold, so — as the
/// prompt-node activity does — this re-resolves the pinned immutable
/// version and runs the starter's own DecideFireSet and ResolveSkeleton
/// over the supplied inputs and the classifiers' answers. Pure over its
/// input; recorded once; the orchestrator reads it back on replay.
/// </summary>
public sealed class DecideActivity
{
    private readonly IWorkflowPackageStore _packageStore;
    private readonly ILogger<DecideActivity> _logger;

    public DecideActivity(IWorkflowPackageStore packageStore, ILogger<DecideActivity> logger)
    {
        _packageStore = packageStore;
        _logger = logger;
    }

    [Function(ConsultGenerationActivityNames.DecideDeliverables)]
    public async Task<ConsultDecisionResult> RunAsync(
        [ActivityTrigger] ConsultDecideActivityInput input,
        CancellationToken cancellationToken)
    {
        if (!WorkflowPackageRef.TryParse(input.WorkflowPackage, out var packageRef))
        {
            throw new InvalidOperationException($"The boundary has no usable workflow package ref ('{input.WorkflowPackage}').");
        }

        var package = await _packageStore.ResolveAsync(packageRef!, cancellationToken);
        var supplied = input.SuppliedInputs?.ToDictionary(
            pair => pair.Key,
            pair => ConsultInputValue.FromJson(pair.Value),
            StringComparer.Ordinal);

        var result = Decide(package, supplied, input.Classifications);

        _logger.LogInformation(
            "Boundary decided. Package={Package}, Firing={Firing}, Skipped={Skipped}, Nodes={Nodes}, Blocks={Blocks}, EmptyFans={EmptyFans}",
            package.Ref,
            result.Results.Count,
            result.Skipped.Count,
            result.Nodes.Count,
            result.Items.Count,
            result.EmptyFanLabels.Count);

        return result;
    }

    /// <summary>The decision, pure: the tests' entry point.</summary>
    internal static ConsultDecisionResult Decide(
        WorkflowPackage package,
        IReadOnlyDictionary<string, ConsultInputValue>? supplied,
        IReadOnlyDictionary<string, string> classifications)
    {
        var fireSet = ConsultGenerationJobStarter.DecideFireSet(package, supplied, classifications);

        if (fireSet.Firing.Count == 0)
        {
            return new ConsultDecisionResult(
                Array.Empty<ConsultResultDescriptor>(),
                fireSet.Skipped,
                Array.Empty<ConsultNodeDescriptor>(),
                Array.Empty<IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(),
                Array.Empty<ConsultCollectionRoster>(),
                Array.Empty<ConsultItemStepDescriptor>(),
                Array.Empty<string>());
        }

        var narrowed = fireSet.Package;
        var skeleton = ConsultGenerationJobStarter.ResolveSkeleton(narrowed, supplied);

        if (skeleton.EmptyFanLabels.Count > 0)
        {
            var declared = package.Results ?? new List<WorkflowResolvedResult>();
            return new ConsultDecisionResult(
                Array.Empty<ConsultResultDescriptor>(),
                ConsultGenerationJobStarter.NotProducedByEmptyFan(declared, fireSet.Skipped, skeleton.EmptyFanLabels),
                Array.Empty<ConsultNodeDescriptor>(),
                Array.Empty<IReadOnlyDictionary<string, string>>(),
                new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(),
                Array.Empty<ConsultCollectionRoster>(),
                Array.Empty<ConsultItemStepDescriptor>(),
                skeleton.EmptyFanLabels);
        }

        // v12 #631 (§ 14): the firing results' when-gated macros are judged
        // here, with the classifier answers in hand — every clause, the
        // input-only ones included, so one construct has one evaluation
        // moment. A when-skipped result never reaches this loop: skip stays
        // skip, and nothing is recorded for a document that does not exist.
        var excludedMacros = new List<ConsultExcludedMacro>();
        var firingDescriptors = fireSet.Firing
            .Select(result =>
            {
                var judged = ConsultGenerationJobStarter.DecideMacroWhens(result, supplied, classifications);
                excludedMacros.AddRange(judged.Excluded);
                return new ConsultResultDescriptor(
                    result.Id, result.NodeId, result.Label, judged.Macros, result.Signature,
                    ConsultGenerationJobStarter.FilterPlacements(result.MacroPlacements, judged.Macros), result.Check);
            })
            .ToList();

        return new ConsultDecisionResult(
            firingDescriptors,
            fireSet.Skipped,
            narrowed.Nodes!.Select(node => ConsultGenerationJobStarter.DescribeNode(node, narrowed.SchemaContracts)).ToList(),
            skeleton.Items,
            skeleton.CollectionSets,
            skeleton.CollectionRosters,
            narrowed.Nodes!
                .Where(node => node.ForEach != null)
                .Select(node => new ConsultItemStepDescriptor(node.Id, node.Label))
                .ToList(),
            Array.Empty<string>(),
            excludedMacros.Count > 0 ? excludedMacros : null);
    }
}
