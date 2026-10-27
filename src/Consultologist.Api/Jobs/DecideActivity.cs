using Consultologist.Api.Agents;
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
    IReadOnlyDictionary<string, string> Classifications,
    // v25 (B8 #1027): the effective inputs the start resolved — what a 25
    // node's text parameters resolve from when the boundary re-describes the
    // nodes (the starter's own call). Null below 25 and on payloads from
    // before. Appended last, the positional-call rule.
    IReadOnlyDictionary<string, string>? EffectiveInputs = null,
    // v25 (#1038, design § 16): the render scope's other halves — every scalar
    // stage-one output (Classification ?? RawOutput, by node id) and the run
    // and profile facts — over which the boundary judges every 25 when. Null
    // below 25 and on payloads from before. Appended last, same rule.
    IReadOnlyDictionary<string, string>? NodeTexts = null,
    TemplateRunFacts? Run = null,
    TemplateProfileFacts? Profile = null);

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
    private readonly WorkflowFormatCatalog _formats;
    private readonly ILogger<DecideActivity> _logger;

    public DecideActivity(
        IWorkflowPackageStore packageStore, WorkflowFormatCatalog formats, ILogger<DecideActivity> logger)
    {
        _packageStore = packageStore;
        _formats = formats;
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

        var result = Decide(package, supplied, input.Classifications, _formats, input.EffectiveInputs, input.NodeTexts, input.Run, input.Profile);

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
        IReadOnlyDictionary<string, string> classifications,
        WorkflowFormatCatalog? engineFormats = null,
        IReadOnlyDictionary<string, string>? effectiveInputs = null,
        IReadOnlyDictionary<string, string>? nodeTexts = null,
        TemplateRunFacts? run = null,
        TemplateProfileFacts? profile = null)
    {
        // v25 (#1038): the boundary's scope — the effective inputs, the stage-one
        // outputs, the facts; the folders settled over the folder-less scope first.
        ConsultConditionScope? scope = null;
        if (ConsultGenerationJobStarter.IsAt25(package))
        {
            var bare = new ConsultConditionScope(
                effectiveInputs ?? new Dictionary<string, string>(StringComparer.Ordinal),
                ConsultConditionScope.TypesOf(package),
                nodeTexts ?? new Dictionary<string, string>(StringComparer.Ordinal),
                null,
                run ?? new TemplateRunFacts(string.Empty, string.Empty, package.Ref, string.Empty),
                profile ?? new TemplateProfileFacts(string.Empty, string.Empty),
                package);
            scope = bare.WithFolders(ConsultRenderPayload.FoldersFor(ConsultGenerationJobStarter.SettledFolders(package, supplied, bare)));
        }

        var fireSet = ConsultGenerationJobStarter.DecideFireSet(package, supplied, classifications, scope);

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
        var skeleton = ConsultGenerationJobStarter.ResolveSkeleton(narrowed, supplied, scope);

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
        var deliverableReads = ConsultGenerationJobStarter.DeliverableReads(narrowed);
        // v26 (#1155): the deliverables each deliverable's macros and each node read.
        var deliverableDeliverableReads = ConsultGenerationJobStarter.DeliverableDeliverableReads(narrowed);
        var nodeDeliverableReads = ConsultGenerationJobStarter.NodeDeliverableReads(narrowed);
        var firingDescriptors = fireSet.Firing
            .Select(result =>
            {
                var judged = ConsultGenerationJobStarter.DecidePlacementWhens(result, supplied, classifications, scope);
                excludedMacros.AddRange(judged.Excluded);
                // The boundary never sees the request: the choice filter is
                // the engine's (FilterDescriptorMacros), so no choices here.
                return ConsultGenerationJobStarter.DescribeResult(result, judged.Macros, judged.Placements, null, deliverableReads?.GetValueOrDefault(result.Id), deliverableDeliverableReads?.GetValueOrDefault(result.Id));
            })
            .ToList();

        // v25 (B8 #1027): the stage-two descriptors are described as the
        // starter described stage one's — the stamp, the derived edges (a 25
        // node's only dependencies) and the effective inputs.
        var derivedEdges = ConsultGenerationJobStarter.DeriveEdges(narrowed);
        // v26 (#1154): the flow rows and the branches the boundary decided.
        var flows = ConsultGenerationJobStarter.FlowsOf(narrowed);

        return new ConsultDecisionResult(
            firingDescriptors,
            fireSet.Skipped,
            narrowed.Nodes!.Select(node => ConsultGenerationJobStarter.DescribeNode(
                node, narrowed.SchemaContracts, narrowed.ContractAgents, narrowed.SourceFiles, narrowed.Manifest.Macros,
                narrowed.Formats ?? engineFormats, narrowed.Stamp?.Formats, derivedEdges?.GetValueOrDefault(node.Id), effectiveInputs, narrowed.Manifest,
                flows, fireSet.Branches?.GetValueOrDefault(node.Id), nodeDeliverableReads?.GetValueOrDefault(node.Id))).ToList(),
            skeleton.Items,
            skeleton.CollectionSets,
            skeleton.CollectionRosters,
            ConsultGenerationJobStarter.ChainSteps(narrowed),
            Array.Empty<string>(),
            excludedMacros.Count > 0 ? excludedMacros : null);
    }
}
