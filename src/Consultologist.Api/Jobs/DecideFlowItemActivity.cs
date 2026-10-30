using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v27 (#1235, package-format-v27-design.md § 1.3): what a per-item decision
/// asks — the inner if/case, the loop's collection and the item in hand, the
/// outputs its condition reads for that item (item-aligned, by node id), the
/// effective inputs, the settled folders and the facts.
/// </summary>
public sealed record ConsultDecideFlowItemInput(
    string WorkflowPackage,
    string FlowId,
    string Collection,
    IReadOnlyDictionary<string, string> Item,
    IReadOnlyDictionary<string, string>? EffectiveInputs,
    IReadOnlyDictionary<string, string> NodeTexts,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? Folders,
    TemplateRunFacts Run,
    TemplateProfileFacts Profile,
    // The returned node of every flow node the condition reads, so an alias
    // types as it (a body node reading an earlier inner flow by bare id).
    IReadOnlyDictionary<string, string>? FlowRets = null);

/// <summary>
/// v27 (#1235, design § 1.3, § 1.4, § 2): what one item's decision said — the
/// branch the record names ("then" | "else" | "&lt;v1, v2&gt;" | "none"), the taken
/// body's node ids (the instances that run for this item) and its returned
/// node, and — when no branch was taken — the reason the skipped row carries:
/// the condition's TEXT in the fixed form, never the value it read (#369).
/// </summary>
public sealed record ConsultFlowItemDecision(
    string FlowId,
    string ItemId,
    string Branch,
    IReadOnlyList<string> Taken,
    string? Ret,
    string? Reason)
{
    public bool Skipped => Ret is null;
}

/// <summary>
/// v27 (#1235, design § 5): an if/case inside a loop decided once per item —
/// in an activity, as the boundary is, because the decision needs the resolved
/// package (the orchestrator cannot hold it) and must be recorded once and read
/// back on replay. Pure over its input: the scope is the boundary's, with the
/// item bound under the loop's collection and the item-aligned outputs the
/// condition reads.
/// </summary>
public sealed class DecideFlowItemActivity
{
    private readonly IWorkflowPackageStore _packageStore;
    private readonly ILogger<DecideFlowItemActivity> _logger;

    public DecideFlowItemActivity(IWorkflowPackageStore packageStore, ILogger<DecideFlowItemActivity> logger)
    {
        _packageStore = packageStore;
        _logger = logger;
    }

    [Function(ConsultGenerationActivityNames.DecideFlowItem)]
    public async Task<ConsultFlowItemDecision> RunAsync(
        [ActivityTrigger] ConsultDecideFlowItemInput input,
        CancellationToken cancellationToken)
    {
        if (!WorkflowPackageRef.TryParse(input.WorkflowPackage, out var packageRef))
        {
            throw new InvalidOperationException($"The per-item decision has no usable workflow package ref ('{input.WorkflowPackage}').");
        }

        var package = await _packageStore.ResolveAsync(packageRef!, cancellationToken);
        var decision = Decide(package, input);

        _logger.LogInformation(
            "Flow item decided. Package={Package}, Flow={Flow}, Item={Item}, Branch={Branch}",
            package.Ref, decision.FlowId, decision.ItemId, decision.Branch);

        return decision;
    }

    /// <summary>The decision, pure: the tests' entry point.</summary>
    internal static ConsultFlowItemDecision Decide(WorkflowPackage package, ConsultDecideFlowItemInput input)
    {
        var flows = ConsultGenerationJobStarter.FlowsOf(package);
        if (!flows.Flows.TryGetValue(input.FlowId, out var flow) || flow.Kind == WorkflowFlowNodes.Kind.For)
        {
            throw new InvalidOperationException($"Node '{input.FlowId}' is not an if or case node of '{package.Ref}'.");
        }

        if (!flows.IsInner(input.FlowId))
        {
            throw new InvalidOperationException($"Node '{input.FlowId}' runs in no loop; a top-level flow node is decided by the fire set.");
        }

        var itemId = input.Item.GetValueOrDefault("id") ?? throw new InvalidOperationException($"The item of '{input.FlowId}' has no id.");
        var scope = new ConsultConditionScope(
                input.EffectiveInputs ?? new Dictionary<string, string>(StringComparer.Ordinal),
                ConsultConditionScope.TypesOf(package),
                input.NodeTexts,
                input.Folders,
                input.Run,
                input.Profile,
                package)
            .WithFanItem(input.Collection, input.Item);

        var outcome = scope.Decide(flow);
        var reason = outcome.Skipped
            ? flow.Kind == WorkflowFlowNodes.Kind.If
                ? ConsultConditionScope.Reason(flow.Expression!)
                : $"switches on `{flow.Expression!.Trim()}`; no branch lists its value"
            : null;

        return new ConsultFlowItemDecision(
            input.FlowId,
            itemId,
            outcome.Branch,
            outcome.Taken?.Nodes.ToList() ?? new List<string>(),
            outcome.Taken?.RetNodeId,
            reason);
    }
}
