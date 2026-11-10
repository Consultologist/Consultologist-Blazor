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
    IReadOnlyDictionary<string, string>? FlowRets = null,
    // v29 (#1304, design § 3.2): set when this is a folder item's gate, not an
    // inner if/case — the folder whose item's if reads a node, decided once that
    // node completes. FlowId then names the folder. Null below 29. Trailing.
    string? GateFolder = null,
    // #1356 (v26 design § 1.3, v27 § 1.4): the flows the condition reads that took no
    // branch — for this item, the sibling inner flows it skipped — read as null.
    // Trailing; written only when set, so a decision that reads none serializes as before.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? SkippedNodes = null,
    // v30 (#1341, design § 2.4): the enclosing loops' items, outermost first — what a
    // condition's item.parent reads. Trailing; written only when set.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ConsultParentItem>? Parents = null,
    // v30 (#1342, design § 3.1): set when this is a while's check after pass Pass.Number
    // (0: before the first, under checkFirst) — FlowId names the while. Trailing.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    ConsultPassPayload? Pass = null,
    // v30 (#1342, design § 3.4): set when this is a for's until, judged after the item's
    // body settled — FlowId names the loop, Item the item. Trailing; written only when set.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    bool? Until = null);

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
        if (input.GateFolder is { } gateFolder)
        {
            return DecideItemGate(package, input, gateFolder);
        }

        var flows = ConsultGenerationJobStarter.FlowsOf(package);
        if (input.Pass is { } pass && flows.Flows.TryGetValue(input.FlowId, out var whileFlow) && whileFlow.Kind == WorkflowFlowNodes.Kind.While)
        {
            return DecideWhileCheck(package, input, whileFlow, pass);
        }

        if (input.Until == true && flows.Flows.TryGetValue(input.FlowId, out var loopFlow) && loopFlow.Kind == WorkflowFlowNodes.Kind.For && loopFlow.Node.Until is { } until)
        {
            // v30 (#1342, design § 3.4): the item's until, over its body's nodes and the item.
            var itemScope = new ConsultConditionScope(
                    input.EffectiveInputs ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    ConsultConditionScope.TypesOf(package),
                    input.NodeTexts,
                    input.Folders,
                    input.Run,
                    input.Profile,
                    package)
                .WithFanItem(input.Collection, input.Item) with { SkippedNodes = input.SkippedNodes, Parents = input.Parents };
            var untilItem = WorkflowItemPath.OfItem(input.Item) ?? throw new InvalidOperationException($"The item of '{input.FlowId}' has no id.");
            return itemScope.Holds($"For '{input.FlowId}' until", until)
                ? new ConsultFlowItemDecision(input.FlowId, untilItem, "until", new List<string>(), null, "its until held")
                : new ConsultFlowItemDecision(input.FlowId, untilItem, "again", new List<string>(), string.Empty, null);
        }

        if (!flows.Flows.TryGetValue(input.FlowId, out var flow) || flow.Kind == WorkflowFlowNodes.Kind.For)
        {
            throw new InvalidOperationException($"Node '{input.FlowId}' is not an if or case node of '{package.Ref}'.");
        }

        if (!flows.IsInner(input.FlowId))
        {
            throw new InvalidOperationException($"Node '{input.FlowId}' runs in no loop; a top-level flow node is decided by the fire set.");
        }

        // #1341: the item's path — its id at one level, outer/id inside a nested loop.
        var itemId = WorkflowItemPath.OfItem(input.Item) ?? throw new InvalidOperationException($"The item of '{input.FlowId}' has no id.");
        var scope = new ConsultConditionScope(
                input.EffectiveInputs ?? new Dictionary<string, string>(StringComparer.Ordinal),
                ConsultConditionScope.TypesOf(package),
                input.NodeTexts,
                input.Folders,
                input.Run,
                input.Profile,
                package)
            .WithFanItem(input.Collection, input.Item) with { SkippedNodes = input.SkippedNodes, Parents = input.Parents };

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

    /// <summary>
    /// v30 (#1342, design § 3.1): a while's check after a pass (or, under checkFirst,
    /// before one) — its until first (after a pass only), then its while, over that
    /// pass's nodes and the pass object. Branch: "again" (run another pass), "until"
    /// (its until held) or "check" (its check stopped holding); the bound is the
    /// orchestrator's (it knows max).
    /// </summary>
    private static ConsultFlowItemDecision DecideWhileCheck(WorkflowPackage package, ConsultDecideFlowItemInput input, WorkflowFlowNodes.Flow flow, ConsultPassPayload pass)
    {
        var scope = new ConsultConditionScope(
                input.EffectiveInputs ?? new Dictionary<string, string>(StringComparer.Ordinal),
                ConsultConditionScope.TypesOf(package),
                input.NodeTexts,
                input.Folders,
                input.Run,
                input.Profile,
                package) with { SkippedNodes = input.SkippedNodes, Pass = pass };
        var passId = pass.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (pass.Number > 0 && flow.Node.Until is { } until && scope.Holds($"While '{flow.Node.Id}' until", until))
        {
            return new ConsultFlowItemDecision(flow.Node.Id, passId, "until", new List<string>(), null, "its until held");
        }

        return scope.Holds($"While '{flow.Node.Id}' check", flow.Expression!)
            ? new ConsultFlowItemDecision(flow.Node.Id, passId, "again", new List<string>(), string.Empty, null)
            : new ConsultFlowItemDecision(flow.Node.Id, passId, "check", new List<string>(), null, ConsultConditionScope.Reason(flow.Expression!));
    }

    /// <summary>
    /// v29 (#1304, design § 3.2): a folder item whose if reads a node, judged once
    /// that node completed — over the same scope a start-time gate is judged in
    /// (SettledFolders), now with the node's value to read. Held: "then", nothing
    /// skipped. Not held: "none" and the reason, the condition's TEXT (#369).
    /// </summary>
    private static ConsultFlowItemDecision DecideItemGate(WorkflowPackage package, ConsultDecideFlowItemInput input, string folder)
    {
        var itemId = input.Item.GetValueOrDefault("id") ?? throw new InvalidOperationException($"The gated item of folder '{folder}' has no id.");
        var item = package.Data?.Collections.GetValueOrDefault(folder)?.Items.FirstOrDefault(candidate => string.Equals(candidate.Id, itemId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Folder '{folder}' of '{package.Ref}' has no item '{itemId}'.");
        var gate = item.If ?? throw new InvalidOperationException($"Folder '{folder}' item '{itemId}' of '{package.Ref}' has no if to decide.");

        var holds = new ConsultConditionScope(
                input.EffectiveInputs ?? new Dictionary<string, string>(StringComparer.Ordinal),
                ConsultConditionScope.TypesOf(package),
                input.NodeTexts,
                input.Folders,
                input.Run,
                input.Profile,
                package)
            .WithItem(folder, item)
            .Holds($"Snippet folder '{folder}' item '{itemId}' if", gate);

        return holds
            ? new ConsultFlowItemDecision(folder, itemId, "then", new List<string>(), string.Empty, null)
            : new ConsultFlowItemDecision(folder, itemId, "none", new List<string>(), null, ConsultConditionScope.Reason(gate));
    }
}
