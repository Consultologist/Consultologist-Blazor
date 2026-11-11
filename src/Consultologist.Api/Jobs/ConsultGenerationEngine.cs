using System.Globalization;
using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Jobs;

public sealed class ConsultGenerationOrchestrator
{
    // Agent activity calls are nondeterministic: a retry re-runs the same prompt and the
    // agent issues fresh tool calls, so a transient tool failure (e.g. a rejected SNOMED
    // search) rarely repeats. Configuration errors (InvalidOperationException from
    // AgentSectionGenerator) are excluded so they fail fast instead of burning retries.
    // See docs/SNOMED_TOOL_FAILURES.md.
    // v29 (#1304): the decision key a folder item's gate rides under (gate:<folder>), never a node id.
    private const string ItemGatePrefix = "gate:";

    /// <summary>v30 (#1342): a while's check decision is keyed while:&lt;id&gt;.</summary>
    private const string WhilePrefix = "while:";

    /// <summary>v30 (#1342): a for's until decision is keyed until:&lt;loop&gt;.</summary>
    private const string UntilPrefix = "until:";

    private static readonly TaskOptions AgentActivityRetryOptions = TaskOptions.FromRetryPolicy(
        new RetryPolicy(
            maxNumberOfAttempts: 3,
            firstRetryInterval: TimeSpan.FromSeconds(5),
            backoffCoefficient: 2.0)
        {
            HandleFailure = failure => !failure.IsCausedBy<InvalidOperationException>()
        });

    [Function(nameof(ConsultGenerationOrchestrator))]
    public async Task RunAsync([OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var input = context.GetInput<ConsultGenerationOrchestrationInput>()
            ?? throw new InvalidOperationException("Consult generation request input is required.");
        var request = input.Request;
        var logger = context.CreateReplaySafeLogger(nameof(ConsultGenerationOrchestrator));

        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), context.InstanceId);

        try
        {
        var nodes = input.Nodes is { Count: > 0 }
            ? input.Nodes
            : throw new InvalidOperationException("Consult generation input carries no workflow nodes; the job start snapshots them from the workflow package.");
        // v7 multi-deliverable jobs carry no single result node — only then may
        // ResultNodeId be null (package-format-v7.md).
        var results = input.Results;
        var v7 = results is { Count: > 0 };
        var resultNodeId = input.ResultNodeId
            ?? (v7 ? null : throw new InvalidOperationException("Consult generation input names no result node; the job start snapshots it from the workflow package."));

        // The work items: v5 jobs carry the one collection's items; v6 jobs carry
        // per-collection sets in Collections and the deliverable's BLOCKS in Items
        // (the result aggregator's expansion, feeding the entity's section model).
        // v10 (#496): a job whose package has classifiers starts deciding — no
        // items yet; the boundary supplies them. Every other job carries its
        // skeleton from the start, as it always did.
        var deciding = input.Deciding == true;
        // v29 (#1303, design § 1.1): the loops over a node's value — no blocks
        // and no items until their source completes (ResolveLoopsFedByAsync); a
        // job whose only loop is one starts with none, and is still a v6 job.
        var unresolvedLoops = new HashSet<string>(input.UnresolvedLoops ?? Array.Empty<string>(), StringComparer.Ordinal);
        var resolvedItems = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>(StringComparer.Ordinal);
        var items = input.Items is { Count: > 0 }
            ? input.Items
            : deciding || unresolvedLoops.Count > 0
                ? Array.Empty<IReadOnlyDictionary<string, string>>()
                : throw new InvalidOperationException("Consult generation input carries no work items; the job start snapshots them from the workflow package.");
        var collections = input.Collections;
        var v6 = collections is { Count: > 0 } || unresolvedLoops.Count > 0;

        // The input map: v7 jobs carry the effective map from the starter;
        // v5/v6 jobs fold the single draft into the conventional input — a pure
        // value-identical view, so replaying instances render the same bytes.
        var effectiveInputs = input.Inputs
            ?? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ConsultGenerationJobStarter.ConsultDraftInputId] = request.ConsultDraft ?? string.Empty
            };

        // v23 #955: the run context a node-macro resolution needs, built once and
        // replay-stable (every argument is an input field or the replay-safe
        // CurrentUtcDateTime). The typed supplied map is reconstructed from the
        // carrier the starter threads whenever a node macro might read it (a
        // deciding job, or a package that declares node macros); it stays null
        // otherwise, and a node with no macros never looks at it.
        var nodeMacroFacts = new ConsultMacroExpander.RunFacts(
            context.CurrentUtcDateTime,
            context.InstanceId,
            input.WorkflowPackage ?? string.Empty,
            input.ApiHost,
            input.ProfileName,
            input.Signature);
        var suppliedInputs = input.SuppliedInputs?.ToDictionary(
            pair => pair.Key, pair => ConsultInputValue.FromJson(pair.Value), StringComparer.Ordinal);

        await context.Entities.CallEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.Initialize),
            new ConsultGenerationJobInitialize(
                context.InstanceId,
                input.AppUserId,
                items,
                input.WorkflowPackage,
                input.EffectiveInputHash,
                input.ItemSteps,
                nodes,
                input.EffectiveInputHashVersion,
                input.CatalogRef,
                input.Source,
                request.ScheduledAtUtc,
                input.InputOrigins,
                // #428: by name — PackageSpecVersion sits between, unpassed.
                InputDocumentOrigins: input.InputDocumentOrigins,
                // #398: by name, for the same reason.
                PackageFormatRef: input.PackageFormatRef,
                ProvenanceRef: input.ProvenanceRef,
                Terminology: input.Terminology,
                TerminologyServerRef: input.TerminologyServerRef,
                TerminologyServerRelease: input.TerminologyServerRelease,
                // v10 (#496): by name, last.
                Deciding: input.Deciding,
                // #514: by name, last.
                ApiHost: input.ApiHost,
                EngineCommit: input.EngineCommit,
                EngineRelease: input.EngineRelease,
                // v25 (B2 #1001): by name, last.
                FormatsRef: input.FormatsRef));

        // #157: a scheduled job sleeps here — visible as Scheduled (entity state
        // above) — then proceeds identically. CurrentUtcDateTime keeps the guard
        // deterministic on replay; past times fall through and run immediately.
        if (request.ScheduledAtUtc is { } scheduledAt && scheduledAt > context.CurrentUtcDateTime)
        {
            context.SetCustomStatus(new { status = ConsultGenerationJobStatuses.Scheduled });
            await context.CreateTimer(scheduledAt.UtcDateTime, CancellationToken.None);
        }

        await context.Entities.CallEntityAsync(entityId, nameof(ConsultGenerationJobEntity.MarkRunning));

        logger.LogInformation(
            "ConsultGenerationOrchestrator started. JobId={JobId}, AppUserId={AppUserId}, ItemCount={ItemCount}, NodeCount={NodeCount}",
            context.InstanceId,
            input.AppUserId,
            items.Count,
            nodes.Count);

        var totalBlockCount = items.Count;
        var completedBlockCount = 0;
        var failedBlockCount = 0;
        var totalNodeCount = nodes.Count;
        var completedNodeCount = 0;

        void PublishStatus(string status) => context.SetCustomStatus(new
        {
            status,
            totalBlockCount,
            completedBlockCount,
            failedBlockCount,
            completedNodeCount,
            totalNodeCount
        });

        PublishStatus(ConsultGenerationJobStatuses.Running);

        // The one-kind interpreter: a per-(node, item) ready loop over the snapshotted
        // descriptors. A scalar node runs once; a forEach node runs once per item,
        // instance i depending item-aligned on same-collection upstream instances.
        // Determinism rules — start order derives only from manifest order plus
        // recorded activity completions; variable rendering is pure over recorded
        // results (external/consultologist-package-format/package-format-v5.md).
        var nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        // v30 (#1341, design § 2.3): the loops that run INSIDE a loop — each runs once per
        // outer item (a loop instance), its list settling per instance. None below 30, so
        // a 26–29 job takes the 29 path everywhere this is consulted.
        var nestedLoops = nodes.Where(node => node.Kind == ConsultNodeFlows.For && ConsultNodeScheduler.LoopDepthOf(node, nodesById) >= 1).ToList();
        var nestedLoopIds = nestedLoops.Select(loop => loop.Id).ToHashSet(StringComparer.Ordinal);
        var loopInstances = new ConsultLoopInstances();
        var chainNodes = nodes.Where(node => node.ForEach != null).ToList();
        var aggregators = nodes.Where(node => node.Aggregate != null).ToList();
        // v26 (#1154, design § 1): the flow nodes — a loop completes as the
        // aggregator it replaces (ConsultFlowCompletion), an if/case as its
        // taken body's returned node; one that took no branch is SKIPPED: its
        // row says so, a reader treats it as settled and reads nothing.
        var flowNodes = nodes.Where(node => node.Kind is not null).ToList();
        var skippedNodes = new HashSet<string>(StringComparer.Ordinal);
        // v27 (#1235, design § 1.3, § 1.4, § 5): an if/case INSIDE a loop is decided
        // once per item, in an activity (decide-flow-item) when its reads settle
        // for the item; the calculus of what each decision settles, the per-item
        // tasks in flight, and the skipped set widened by instance key.
        var inner = new ConsultInnerFlows(nodesById);
        var pendingDecisions = new Dictionary<Task<ConsultFlowItemDecision>, (string FlowId, string ItemId)>();
        // v29 (#1304, design § 3.2): the folder items whose if reads a node — kept at
        // start, each decided once the nodes it reads complete (a decision keyed
        // gate:<folder>); held → it runs as any item, not held → skipped by condition.
        var itemGates = input.ItemGates ?? new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
        var gateDecisions = new Dictionary<string, bool>(StringComparer.Ordinal); // "<folder>/<item>" → held
        var gatesStarted = new HashSet<string>(StringComparer.Ordinal);
        IReadOnlySet<string> SkippedKeys() => inner.SkippedInstances.Count == 0
            ? skippedNodes
            : skippedNodes.Concat(inner.SkippedInstances).ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ItemRets() => inner.Decisions.Count == 0
            ? null
            : inner.Decisions.Values
                .Where(decision => decision.Ret is not null)
                .GroupBy(decision => decision.FlowId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (IReadOnlyDictionary<string, string>)group.ToDictionary(d => d.ItemId, d => d.Ret!, StringComparer.Ordinal), StringComparer.Ordinal);
        // v25 (B5 #1004): a 25 job — one whose nodes carry formats (the job
        // starter's IsAt25, over the snapshot it took). Its aggregators stamp
        // definition 6, and its deliverables fill no slot: an inline macro
        // rendered in the reply of the node that read it (#1044).
        var at25 = nodes.Any(node => node.Format is not null);
        // v29 (#1305): the deliverable's row at composition, "produced" meaning released.
        var at29 = input.PackageSpecVersion >= 29;
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal);
        var pendingTasks = new Dictionary<Task<NodeRunResult>, (string NodeId, string? ItemId)>();
        var started = new HashSet<string>(StringComparer.Ordinal);
        var failedItems = new HashSet<string>(StringComparer.Ordinal);
        var failedAggregators = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodeLevelCompleted = new HashSet<string>(StringComparer.Ordinal);
        // v30 (#1342, design § 3.1): a while's passes — how many are open, the checks
        // started ("<while>/<pass>"), and, once it stops, how many passes it ran and why.
        var passesOpened = new Dictionary<string, int>(StringComparer.Ordinal);
        var whileChecksStarted = new HashSet<string>(StringComparer.Ordinal);
        var whileStops = new Dictionary<string, (int Passes, string Stopped)>(StringComparer.Ordinal);
        // v30 (#1342 B3d): the while instances that could not finish inside a while ("<while>:<outer>").
        var failedWhileInstances = new HashSet<string>(StringComparer.Ordinal);
        // v30 (#1342, design § 3.4): a for's stop (until | bound | end), its items whose until
        // was decided, the checks started, the items not run ("<loop>/<item>"), and the loops
        // whose max has been applied.
        var loopStops = new Dictionary<string, string>(StringComparer.Ordinal);
        var untilDecided = new HashSet<string>(StringComparer.Ordinal);
        var untilStarted = new HashSet<string>(StringComparer.Ordinal);
        var notRunItems = new HashSet<string>(StringComparer.Ordinal);
        var boundsApplied = new HashSet<string>(StringComparer.Ordinal);

        // v25 (B3 #1002): a snippet folder fan (snippet:<folder>) keys its
        // snapshot by the folder id, as a data collection fan did.
        string CollectionIdOf(ConsultNodeDescriptor node) => WorkflowFanSources.CollectionIdOf(node.ForEach!);

        // v6: each forEach node fans over ITS collection's items; v5 keeps the
        // single shared set. Failed-item keys are collection-scoped in v6 (item
        // ids are unique per collection, not globally).
        IReadOnlyList<IReadOnlyDictionary<string, string>> FanItems(ConsultNodeDescriptor node) =>
            // v30 (#1342 B3d): a for inside a while runs once per pass — its row fans over
            // the while's opened passes, as a nested loop's row fans over its outer items.
            node.Kind == ConsultNodeFlows.For && RepeatOwnerOf(node) is { Kind: ConsultNodeFlows.While } passOwner
                ? PassItemsOf(passOwner)
            // v30 (#1341): a node under a nested loop fans over every settled instance's
            // items (each carrying its path), in the outer items' order.
            : nestedLoops.Count > 0 && NestedLoopOf(node) is { } nested
                ? loopInstances.ItemsIn(nested.Id, FanItems(nested).Select(outer => WorkflowItemPath.OfItem(outer)!))
                : v6
                ? resolvedItems.GetValueOrDefault(CollectionIdOf(node))
                    ?? collections?.GetValueOrDefault(CollectionIdOf(node))
                    // v29 (#1303): a node's list not yet known fans nothing — yet.
                    ?? (WorkflowFlowNodes.IsNodeValueCollection(CollectionIdOf(node)) ? Array.Empty<IReadOnlyDictionary<string, string>>() : null)
                    ?? throw new InvalidOperationException($"Node '{node.Id}' fans collection '{CollectionIdOf(node)}', which the job snapshot does not carry.")
                : items;

        // v30 (#1341): the nearest loop a node runs in, when that loop is nested; null otherwise
        // (a top-level loop's body, and a nested loop's own row — it fans over its outer items).
        ConsultNodeDescriptor? NestedLoopOf(ConsultNodeDescriptor node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            for (var ownerId = node.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
            {
                if (owner.Kind == ConsultNodeFlows.For)
                {
                    return nestedLoopIds.Contains(owner.Id) ? owner : null;
                }
            }

            return null;
        }

        // v30 (#1342, design § 3.4): the top-level for a fanned node runs in (its nearest
        // enclosing flow that is a for, when that for is at the top). Null otherwise.
        ConsultNodeDescriptor? TopLoopOf(ConsultNodeDescriptor node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            for (var ownerId = node.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
            {
                if (owner.Kind == ConsultNodeFlows.While)
                {
                    return null;
                }

                if (owner.Kind == ConsultNodeFlows.For)
                {
                    // v30 (#1342 B3c/B3d): a nested for too, and one inside a while.
                    return owner;
                }
            }

            return null;
        }

        // A 30 for that stops early: its until, or its max — at the top, or per instance inside
        // a loop (B3c) or a while (B3d). None below 30.
        bool Stoppable(ConsultNodeDescriptor loop) =>
            loop.Kind == ConsultNodeFlows.For && (loop.Until is not null || loop.Max is not null) && loop.Ret is not null;

        // v30 (#1342 B3c): the instance an item of a loop belongs to — null at the top, else the
        // outer path (its path's prefix at the loop's depth); and an instance's key.
        string? OuterOf(ConsultNodeDescriptor loop, string itemPath) =>
            WorkflowItemPath.Prefix(itemPath, ConsultNodeScheduler.LoopDepthOf(loop, nodesById));

        string LoopKey(ConsultNodeDescriptor loop, string? outer) => ConsultNodeScheduler.InstanceKey(loop.Id, outer);

        // The instances of a loop: one at the top; inside a loop, each outer item's (its row fans over them).
        IReadOnlyList<string?> InstancesOf(ConsultNodeDescriptor loop) =>
            ConsultNodeScheduler.LoopDepthOf(loop, nodesById) == 0
                ? new string?[] { null }
                : FanItems(loop).Select(item => WorkflowItemPath.OfItem(item)).ToList();

        // An instance's items, in list order: the loop's at the top (its returned node fans over
        // them); inside a loop, that instance's settled list (none while it is unsettled or failed).
        IReadOnlyList<IReadOnlyDictionary<string, string>> LoopItems(ConsultNodeDescriptor loop, string? outer = null) =>
            outer is null
                ? FanItems(nodesById[loop.Ret!])
                : loopInstances.Get(loop.Id, outer) is { Failed: false } instance ? instance.Items : Array.Empty<IReadOnlyDictionary<string, string>>();

        // Whether an instance's list is known yet.
        bool InstanceKnown(ConsultNodeDescriptor loop, string? outer) =>
            outer is null ? !unresolvedLoops.Contains(loop.Id) : loopInstances.Get(loop.Id, outer) is { Failed: false };

        // The fanned nodes of a top-level loop: its body, inner flows and branch nodes.
        IEnumerable<ConsultNodeDescriptor> FannedIn(ConsultNodeDescriptor loop) =>
            nodes.Where(node => node.ForEach is not null && TopLoopOf(node)?.Id == loop.Id);

        bool ItemFailedIn(ConsultNodeDescriptor loop, string itemId) =>
            FannedIn(loop).Any(node => failedItems.Contains(FailedKey(node, itemId)));

        bool ItemSettledIn(ConsultNodeDescriptor loop, string itemId) =>
            FannedIn(loop).All(node => inner.Settled(node, itemId, outputs, failedItems.Contains(FailedKey(node, itemId))));

        // v30 (#1342, design § 3.4): may this item of a node's loop start? Past the loop's max,
        // never; under its until, only once every earlier item's until was decided (or the
        // item failed — decided 2026-11-10: a failed item goes on to the next).
        bool MayStart(ConsultNodeDescriptor node, string itemId)
        {
            if (TopLoopOf(node) is not { } loop || !Stoppable(loop))
            {
                return true;
            }

            var items = LoopItems(loop, OuterOf(loop, itemId));
            var index = items.ToList().FindIndex(item => WorkflowItemPath.OfItem(item) == itemId);
            if (loop.Max is { } max && index >= max)
            {
                return false;
            }

            return loop.Until is null
                || items.Take(Math.Max(0, index)).All(earlier => untilDecided.Contains($"{loop.Id}/{WorkflowItemPath.OfItem(earlier)}") || ItemFailedIn(loop, WorkflowItemPath.OfItem(earlier)!));
        }

        // v30 (#1342, design § 3.4): why a 30 loop read whole stopped — until, bound, or end.
        // (B3c: an instance's key, loop:<outer>, for a nested loop's list.)
        string? LoopStoppedOf(string loopKey) =>
            input.PackageSpecVersion >= 30 && nodesById.TryGetValue(WorkflowItemPath.NodeOf(loopKey), out var loop) && loop.Kind == ConsultNodeFlows.For
                ? loopStops.GetValueOrDefault(loopKey) ?? "end"
                : null;

        // v30 (#1342, design § 3): the while a node's instances run in — its nearest
        // enclosing flow that repeats, when no for lies between (a while in a for, or
        // a for in a while, is B3c). Null for every other node, and below 30.
        ConsultNodeDescriptor? WhileOf(ConsultNodeDescriptor node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            for (var ownerId = node.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
            {
                if (owner.Kind == ConsultNodeFlows.For)
                {
                    return null;
                }

                if (owner.Kind == ConsultNodeFlows.While)
                {
                    return owner;
                }
            }

            return null;
        }

        static string PassId(int number) => number.ToString(CultureInfo.InvariantCulture);

        // v30 (#1342 B3c): a pass's path — its number at the top; outer/number inside a for's item.
        static string PassPath(string? outer, int number) => outer is null ? PassId(number) : $"{outer}/{PassId(number)}";

        // A while instance: the while at the top (one), or the while under one outer item.
        static string WhileKey(ConsultNodeDescriptor whileNode, string? outer) => ConsultNodeScheduler.InstanceKey(whileNode.Id, outer);

        // A pass is a while's item: its id is its number; inside a for's item it carries its path.
        static IReadOnlyDictionary<string, string> PassItem(string? outer, int number)
        {
            var item = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = PassId(number), ["name"] = $"Pass {number}" };
            if (outer is not null)
            {
                item[WorkflowItemPath.PathMember] = PassPath(outer, number);
            }

            return item;
        }

        // A pass path's outer path and number (null when the path is not a pass's).
        static (string? Outer, int Number)? PassOfPath(string? path)
        {
            if (path is null)
            {
                return null;
            }

            var segments = WorkflowItemPath.Segments(path);
            return int.TryParse(segments[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? (segments.Count == 1 ? null : WorkflowItemPath.Prefix(path, segments.Count - 1), number)
                : null;
        }

        IReadOnlyList<string> BodyOf(ConsultNodeDescriptor whileNode) => whileNode.Body ?? (IReadOnlyList<string>)Array.Empty<string>();

        // v30 (#1342 B3d): the nearest flow a node repeats under — a for or a while — up its in chain.
        ConsultNodeDescriptor? RepeatOwnerOf(ConsultNodeDescriptor node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            for (var ownerId = node.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
            {
                if (owner.Kind is ConsultNodeFlows.For or ConsultNodeFlows.While)
                {
                    return owner;
                }
            }

            return null;
        }

        // v30 (#1342 B3c/B3d): the outer paths a while's instances run under — none for a while at
        // the top (one instance); inside a for, each of its items (the while's row fans over them);
        // inside a while, each pass that while's instances opened.
        IReadOnlyList<string?> OuterPathsOf(ConsultNodeDescriptor whileNode) =>
            RepeatOwnerOf(whileNode) switch
            {
                null => new string?[] { null },
                { Kind: ConsultNodeFlows.While } owner => PassItemsOf(owner).Select(item => WorkflowItemPath.OfItem(item)).ToList(),
                _ => FanItems(whileNode).Select(item => WorkflowItemPath.OfItem(item)).ToList(),
            };

        // The passes a while's instances opened, as items (a pass's path rides inside a for's item or an outer pass).
        IReadOnlyList<IReadOnlyDictionary<string, string>> PassItemsOf(ConsultNodeDescriptor whileNode) =>
            OuterPathsOf(whileNode)
                .SelectMany(outer => Enumerable.Range(1, passesOpened.GetValueOrDefault(WhileKey(whileNode, outer))).Select(number => PassItem(outer, number)))
                .ToList();

        // The enclosing for's item a while instance runs for — through any while between; null at the top.
        IReadOnlyDictionary<string, string>? OuterItemOf(ConsultNodeDescriptor whileNode, string? outer) =>
            outer is null ? null
            : RepeatOwnerOf(whileNode) is { Kind: ConsultNodeFlows.While } owner
                ? PassOfPath(outer) is { } pass ? OuterItemOf(owner, pass.Outer) : null
                : FanItems(whileNode).FirstOrDefault(item => WorkflowItemPath.OfItem(item) == outer);

        // v30 (#1342 B3d): whether a while instance (or one it runs inside) could not finish.
        bool WhileInstanceFailed(ConsultNodeDescriptor whileNode, string? outer) =>
            failedWhileInstances.Contains(WhileKey(whileNode, outer))
            || (outer is null
                ? failedAggregators.ContainsKey(whileNode.Id)
                : RepeatOwnerOf(whileNode) is { Kind: ConsultNodeFlows.While } owner
                    ? PassOfPath(outer) is { } pass && WhileInstanceFailed(owner, pass.Outer)
                    : failedItems.Contains(FailedKey(whileNode, outer)));

        // v30 (#1342 B3d): whether a while a loop runs inside can still open a pass.
        bool EnclosingWhileRunning(ConsultNodeDescriptor node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            for (var ownerId = node.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
            {
                if (owner.Kind == ConsultNodeFlows.While
                    && OuterPathsOf(owner).Any(outer => !whileStops.ContainsKey(WhileKey(owner, outer)) && !WhileInstanceFailed(owner, outer)))
                {
                    return true;
                }
            }

            return false;
        }

        // The for a while runs in (its nearest enclosing loop), or null at the top.
        ConsultNodeDescriptor? LoopAround(ConsultNodeDescriptor whileNode) =>
            whileNode.In is { } ownerId && nodesById.TryGetValue(ownerId, out var owner) && owner.Kind == ConsultNodeFlows.For ? owner : null;

        // (B3d: an inner while that ran no pass in this pass is settled too.)
        bool PassSettled(ConsultNodeDescriptor whileNode, string? outer, int number) =>
            BodyOf(whileNode).All(bodyId => outputs.ContainsKey(ConsultNodeScheduler.InstanceKey(bodyId, PassPath(outer, number)))
                || inner.SkippedInstances.Contains(ConsultNodeScheduler.InstanceKey(bodyId, PassPath(outer, number))));

        // One pass's direct body texts, for a later pass's pass.previous / .history and for node.<while>.history.
        ConsultPassTexts PassTextsOf(ConsultNodeDescriptor whileNode, string? outer, int number) =>
            new(number, BodyOf(whileNode)
                .Where(bodyId => outputs.ContainsKey(ConsultNodeScheduler.InstanceKey(bodyId, PassPath(outer, number))))
                .ToDictionary(
                    bodyId => bodyId,
                    bodyId => outputs[ConsultNodeScheduler.InstanceKey(bodyId, PassPath(outer, number))] is var output ? output.Classification ?? output.RawOutput : string.Empty,
                    StringComparer.Ordinal));

        // (B3d: inside a while, the enclosing pass as pass.parent.)
        ConsultPassPayload PassOf(ConsultNodeDescriptor whileNode, string? outer, int number) =>
            new(number, Enumerable.Range(1, Math.Max(0, number - 1)).Select(earlier => PassTextsOf(whileNode, outer, earlier)).ToList(),
                RepeatOwnerOf(whileNode) is { Kind: ConsultNodeFlows.While } owner && PassOfPath(outer) is { } enclosing
                    ? PassOf(owner, enclosing.Outer, enclosing.Number)
                    : null);

        // v30 (#1342, design § 3.2): the pass a while's body instance runs in — (B3d) through any
        // for between, its pass the item path's prefix at the while's level.
        ConsultPassPayload? PassFor(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item) =>
            NearestWhileOf(node) is { } whileNode
            && PassOfPath(WorkflowItemPath.Prefix(WorkflowItemPath.OfItem(item), ConsultNodeScheduler.LoopDepthOf(whileNode, nodesById) + 1)) is { } pass
                ? PassOf(whileNode, pass.Outer, pass.Number)
                : null;

        ConsultNodeDescriptor? NearestWhileOf(ConsultNodeDescriptor node)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            for (var ownerId = node.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
            {
                if (owner.Kind == ConsultNodeFlows.While)
                {
                    return owner;
                }
            }

            return null;
        }

        // v30 (#1342 B3c): a while's body inside a for's item renders that item as `item`
        // (the pass is `pass`); its instance key stays the pass's path.
        IReadOnlyDictionary<string, string>? RenderItemOf(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item) =>
            WhileOf(node) is { } whileNode && PassOfPath(WorkflowItemPath.OfItem(item)) is { Outer: { } outer }
                ? OuterItemOf(whileNode, outer)
                : item;

        // v30 (#1342, design § 3.2): the stopped whiles a node reads from outside — passes, why,
        // every pass; inside a for's item, that item's instance.
        IReadOnlyDictionary<string, ConsultWhilePayload>? WhilesFor(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item)
        {
            Dictionary<string, ConsultWhilePayload>? read = null;
            foreach (var readId in node.Reads ?? Array.Empty<string>())
            {
                if (!nodesById.TryGetValue(readId, out var whileNode) || whileNode.Kind != ConsultNodeFlows.While)
                {
                    continue;
                }

                var outer = ConsultNodeScheduler.AlignedPath(whileNode, WorkflowItemPath.OfItem(item), nodesById);
                if (whileStops.TryGetValue(WhileKey(whileNode, outer), out var stop))
                {
                    read ??= new Dictionary<string, ConsultWhilePayload>(StringComparer.Ordinal);
                    read[readId] = new ConsultWhilePayload(stop.Passes, stop.Stopped, Enumerable.Range(1, stop.Passes).Select(number => PassTextsOf(whileNode, outer, number)).ToList());
                }
            }

            return read;
        }

        // v30 (#1341, design § 2.4): the items of the loops enclosing an instance's own
        // loop, outermost first — each the outer loop's item whose path is the instance
        // path's prefix at that loop's depth. Null outside a nested loop.
        IReadOnlyList<ConsultParentItem>? ParentsOf(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item)
        {
            if (nestedLoops.Count == 0 || WorkflowItemPath.OfItem(item) is not { } path)
            {
                return null;
            }

            var enclosing = new List<ConsultNodeDescriptor>();
            var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };
            for (var ownerId = node.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
            {
                if (owner.Kind == ConsultNodeFlows.For)
                {
                    enclosing.Add(owner);
                }
            }

            var depth = WorkflowItemPath.Depth(path);
            var parents = new List<ConsultParentItem>();
            for (var level = 1; level < enclosing.Count && level < depth; level++)
            {
                // A nested loop's own row fans over its enclosing loop's items.
                var innerRow = enclosing[level - 1];
                var prefix = WorkflowItemPath.Prefix(path, depth - level);
                if (innerRow.ForEach is null || FanItems(innerRow).FirstOrDefault(outer => WorkflowItemPath.OfItem(outer) == prefix) is not { } outerItem)
                {
                    break;
                }

                parents.Insert(0, new ConsultParentItem(innerRow.ForEach, outerItem));
            }

            return parents.Count > 0 ? parents : null;
        }

        // v30 (#1341, design § 6): a nested loop's settled instance items, for the lists a
        // loop read whole carries per element. Null below 30 (no nested loop).
        Func<string, string, IReadOnlyList<IReadOnlyDictionary<string, string>>?>? InstanceItems() =>
            nestedLoops.Count == 0
                ? null
                : (loopId, outerPath) => loopInstances.Get(loopId, outerPath) is { Failed: false } instance ? instance.Items : null;

        // v30 (#1341, design § 1.1): a deliverable's block for a nested loop's item is named
        // by that innermost loop — when the node is the loop's ret and the loops above return
        // each other up to the top, the (innermost loop, top loop) a deliverable sources.
        (string Loop, string Top)? NestedBlockOf(ConsultNodeDescriptor node)
        {
            if (node.In is not { } loopId || !nestedLoopIds.Contains(loopId) || !string.Equals(nodesById[loopId].Ret, node.Id, StringComparison.Ordinal))
            {
                return null;
            }

            var top = nodesById[loopId];
            while (top.In is { } upId && nodesById.TryGetValue(upId, out var up) && up.Kind == ConsultNodeFlows.For && string.Equals(up.Ret, top.Id, StringComparison.Ordinal))
            {
                top = up;
            }

            return (loopId, top.Id);
        }

        // v30 (#1341): a loop whose ret is a loop lays out no blocks of its own — the innermost does.
        bool ReturnsALoop(ConsultNodeDescriptor loop) =>
            loop.Ret is { } retId && nodesById.TryGetValue(retId, out var ret) && ret.Kind == ConsultNodeFlows.For;

        // v29 (#1303): the snapshot's collections with every settled node list beside them — what a reader of a loop renders over.
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? CollectionsWithResolved() =>
            resolvedItems.Count == 0
                ? collections
                : (collections ?? new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>())
                    .Concat(resolvedItems)
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        // v29 (#1303, design § 1.2): a node in a loop whose count is not yet known —
        // the loop itself, its body, or an if/case in its body. None of it settles
        // until the loop resolves: an empty list is not yet a finished loop.
        bool AwaitsCount(ConsultNodeDescriptor node) => ConsultNodeScheduler.AwaitsCount(node, nodesById, unresolvedLoops);

        string FailedKey(ConsultNodeDescriptor node, string itemId) =>
            // v30 (#1342 B3d): a row repeating inside a while has no collection — keyed by itself.
            node.ForEach is null && node.Kind is not null ? WorkflowItemPath.Key(node.Id, itemId)
            : v6 ? WorkflowItemPath.Key(CollectionIdOf(node), itemId) : itemId;

        // v26 (#1154): a 26 folder item carries its header's label where a 25 item carried name.
        string ItemName(ConsultNodeDescriptor node, string itemId) =>
            WorkflowPackageBlocks.ItemName(FanItems(node).First(item => WorkflowItemPath.OfItem(item)! == itemId));

        // v26 (#1154, design § 3): a block is named by the LOOP a deliverable
        // sources; the body node the loop returns is what completes it.
        string BlockNodeOf(ConsultNodeDescriptor node) =>
            node.In is { } loopId && nodesById.TryGetValue(loopId, out var loop) && loop.Kind == ConsultNodeFlows.For && string.Equals(loop.Ret, node.Id, StringComparison.Ordinal)
                ? loopId
                : node.Id;

        // v27 (#1235, design § 1.4): the returned node of the branch an inner
        // if/case took for the item completes the block of the loop returning
        // that flow — two levels up, per item.
        string BlockNodeOfItem(ConsultNodeDescriptor node, string itemId) =>
            inner.FlowOf(node) is { } flow && inner.IsTakenRet(node, itemId) && inner.LoopReturning(flow.Id) is { } returning
                ? returning.Id
                : BlockNodeOf(node);

        // The deliverable table: v7 = one entry per result in result-set order
        // (block ids gain the "{resultId}:" prefix); v6 = one unnamed entry
        // with an empty prefix, reproducing today's block ids byte-for-byte;
        // v5 = empty (blocks are the fan items themselves).
        var deliverables = ConsultDeliverables.Resolve(results, v6 ? resultNodeId : null, nodesById);
        // v11 #516: the recorded text of each completed v7 deliverable — what
        // CompleteResultDocument stored, appends and all. The reply/PDF leg
        // reads THIS from rung (c) on (§ 5, closing the fork): the orchestrator
        // can never read entity state back, so the engine keeps what it sent.
        var recordedTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        // v12 #624: a checked deliverable's completion chain waits for its
        // check — the stash carries exactly what Compose's arity invariant
        // demands travel together. Keyed by resultId: two results may share
        // one check, each gating itself.
        // v25 (B8 #1027): every deliverable whose aggregator composed, held
        // until its reads and its check (if any) have settled — released by
        // TryReleaseDeliverablesAsync on every pass, so a check that settled
        // first still frees it (the ≤24 hang B8 closes).
        var pendingDeliverables = new Dictionary<string, (ConsultDeliverables.Deliverable Deliverable, IReadOnlyList<string> SourceRefs, List<ConsultAggregateRenderer.Part> Parts)>(StringComparer.Ordinal);
        var settledChecks = new Dictionary<string, ConsultCheckOutcome>(StringComparer.Ordinal);
        var failedDocuments = new List<ConsultFailedDocument>();
        // v26 (#1155, design § 3): the deliverables composed from their sources —
        // by result id, the text deliverable.<id>.content reads and the ladder
        // stamped on the document at release; the failures beside them (a
        // deliverable id, never a node id). Composed only once the graph is
        // final — a deciding job's stage one holds the declared set, not the
        // decided one.
        var composedDeliverables = new Dictionary<string, ConsultDeliverableComposition.Composed>(StringComparer.Ordinal);
        var failedDeliverables = new Dictionary<string, string>(StringComparer.Ordinal);
        var deliverablesFinal = !deciding;
        IReadOnlySet<string> RunDeliverableIds() => deliverables.Where(d => d.ResultId is not null).Select(d => d.ResultId!).ToHashSet(StringComparer.Ordinal);
        // v10 (#496): the skipped set the reply names — the starter's, or the boundary's.
        var skippedForReply = input.SkippedDocuments;

        // v10 (#496): the deciding stage runs the classifier closure — the
        // classifiers and what they transitively bind — and nothing else.
        // A classifier binds inputs and classifiers only (the validator's
        // rule), so bindings are the whole closure.
        var allNodes = nodes;
        // v25 (#1038, design § 16): at 25 the deciding stage's frontier is what the
        // gating whens read — snapshotted at start — not the classifiers.
        IReadOnlyList<ConsultNodeDescriptor> ClassifierClosure() =>
            input.DecidingNodes is { Count: > 0 } decidingNodes
                ? ConsultNodeScheduler.StageOneClosure(decidingNodes, allNodes, nodesById)
                : ConsultNodeScheduler.StageOneClosure(
                    allNodes
                        // v25 (B8 #1027): a 25 classifier is an enum format — Values
                        // stamped from the parameter or the format itself, no contract.
                        .Where(node => string.Equals(node.OutputContract, OutputContracts.Classification, StringComparison.Ordinal)
                            || (node.Format is not null && node.Values is not null))
                        .Select(node => node.Id)
                        .ToList(),
                    allNodes, nodesById);


        // The payload a prompt node at this place renders over — and (v30 #1343) the
        // scope a let there evaluates over: the same reads, item, parents and pass.
        ConsultPromptNodeActivityInput PromptPayloadOf(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item, string instanceKey) =>
            new ConsultPromptNodeActivityInput(
                instanceKey,
                node.PromptId!,
                new Dictionary<string, string>(effectiveInputs, StringComparer.Ordinal),
                input.WorkflowPackage,
                null,
                node.ConceptSource,
                input.InputTypes is null ? null : new Dictionary<string, string>(input.InputTypes, StringComparer.Ordinal),
                node.Values,
                node.Template,
                node.AgentName,
                node.AgentVersion,
                Format: node.Format,
                Producer: node.Producer,
                FormatHash: node.FormatHash,
                Op: node.Op,
                NodeTexts: ConsultRenderPayload.NodeTextsFor(node, item, nodesById, outputs, SkippedKeys()),
                Folders: RenderFolders(),
                // v30 (#1342 B3c): inside a for's item, a while's body renders that item.
                Item: RenderItemOf(node, item),
                ForEach: node.ForEach,
                Run: ConsultRenderPayload.RunFactsFor(context.CurrentUtcDateTime, context.InstanceId, input.WorkflowPackage, input.ApiHost),
                Profile: ConsultRenderPayload.ProfileFactsFor(input.ProfileName, input.Signature),
                With: node.With,
                OnInvalid: node.OnInvalid,
                // v25 (B5 #1004): what an instance's when is judged against,
                // as the orchestrator holds it — the completion runs in the activity.
                Supplied: input.SuppliedInputs is null ? null : new Dictionary<string, string>(input.SuppliedInputs, StringComparer.Ordinal),
                Classifications: Classifications(),
                FailIfEmpty: node.FailIfEmpty,
                Instances: node.Macros,
                // v26 (#1154, design § 1.3): the loops read whole, the flow
                // nodes' returned nodes, the flows that took no branch.
                Loops: ConsultRenderPayload.LoopsFor(node, nodesById, outputs, CollectionsWithResolved(), SkippedKeys(), stoppedOf: LoopStoppedOf, instanceItems: InstanceItems(), item: item),
                FlowRets: ConsultRenderPayload.FlowRetsFor(node, nodesById, ItemRets()),
                // #1356: and, for this item, the sibling inner flows it skipped.
                SkippedNodes: ConsultRenderPayload.SkippedReadsFor(node, item, nodesById, skippedNodes, inner.SkippedInstances),
                // v26 (#1155, design § 3.1): the deliverables this node reads, composed.
                Deliverables: ConsultRenderPayload.DeliverableTextsFor(node.DeliverableReads, composedDeliverables, RunDeliverableIds()),
                // v30 (#1341, design § 2.4): item.parent, for an instance of a nested loop.
                Parents: ParentsOf(node, item),
                // v30 (#1342, design § 3.2): the pass it runs in; the whiles it reads.
                Pass: PassFor(node, item),
                Whiles: WhilesFor(node, item));

        void StartInstance(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item)
        {
            var instanceKey = ConsultNodeScheduler.InstanceKey(node.Id, WorkflowItemPath.OfItem(item));
            started.Add(instanceKey);

            // v25 (B3 #1002, design § 1): a node with a format renders over
            // the seven objects in the activity; the orchestrator threads what
            // only it holds — the outputs this instance reads, the settled
            // folders, the facts — and no flat variables, no before/after
            // blocks (instances compose on the output side, B5).
            // v30 (#1343, design § 4.2): a let — computed, no model — evaluates its expression
            // over the scope a template at its place reads; a scalar let declares no format.
            if (node.Let is not null && string.Equals(node.Op, WorkflowLetAt30.Op, StringComparison.Ordinal))
            {
                pendingTasks[context.CallActivityAsync<NodeRunResult>(
                    ConsultGenerationActivityNames.RunComputedNode,
                    new ConsultComputedNodeActivityInput(
                        instanceKey,
                        WorkflowLetAt30.Op,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        node.Format,
                        node.FormatHash,
                        Let: node.Let,
                        LetType: node.LetType,
                        OnInvalid: node.OnInvalid,
                        Scope: PromptPayloadOf(node, item, instanceKey)),
                    AgentActivityRetryOptions)] = (node.Id, WorkflowItemPath.OfItem(item));
                return;
            }

            if (node.Format is not null)
            {
                // v25 (B4 #1003, design § 3.7): a computed node — no prompt, no
                // model — runs its operation over the outputs its with names.
                if (string.Equals(node.Producer, WorkflowFormatProducers.Computed, StringComparison.Ordinal))
                {
                    pendingTasks[context.CallActivityAsync<NodeRunResult>(
                        ConsultGenerationActivityNames.RunComputedNode,
                        new ConsultComputedNodeActivityInput(
                            instanceKey,
                            node.Op ?? string.Empty,
                            node.With ?? new Dictionary<string, string>(StringComparer.Ordinal),
                            ConsultRenderPayload.NodeTextsFor(node, item, nodesById, outputs, SkippedKeys()),
                            node.Format,
                            node.FormatHash),
                        AgentActivityRetryOptions)] = (node.Id, WorkflowItemPath.OfItem(item));
                    return;
                }

                pendingTasks[context.CallActivityAsync<NodeRunResult>(
                    ConsultGenerationActivityNames.RunPromptNode,
                    PromptPayloadOf(node, item, instanceKey),
                    AgentActivityRetryOptions)] = (node.Id, WorkflowItemPath.OfItem(item));
                return;
            }

            var variables = ConsultNodeVariableResolver.Resolve(
                node, effectiveInputs, item, input.DataScalars, nodesById, outputs);

            // v8: which of this node's variables are bound to a typed input.
            // Only declared inputs carry a type — item:, node: and data:
            // sources are strings by nature.
            var variableTypes = ConsultNodeVariableResolver.TypedVariables(node, input.InputTypes);

            var (beforeBlocks, afterBlocks) = ResolveNodeMacros(node, item);

            pendingTasks[context.CallActivityAsync<NodeRunResult>(
                ConsultGenerationActivityNames.RunPromptNode,
                new ConsultPromptNodeActivityInput(
                    instanceKey,
                    node.PromptId!,
                    variables,
                    input.WorkflowPackage,
                    node.OutputContract,
                    node.ConceptSource,
                    variableTypes,
                    node.Values,
                    node.Template,
                    // #923 phase 2: the stamped catalog version's agent pin,
                    // resolved at job-start, so the executor uses it directly.
                    node.AgentName,
                    node.AgentVersion,
                    // v23 #955: the resolved macro blocks for this node's prompt.
                    Before: beforeBlocks,
                    After: afterBlocks,
                    // v25 (B2 #1001): the node's format identity, threaded with the pin.
                    Format: node.Format,
                    Producer: node.Producer,
                    FormatHash: node.FormatHash,
                    Op: node.Op),
                AgentActivityRetryOptions)] = (node.Id, WorkflowItemPath.OfItem(item));
        }

        // v27 (#1235): one decision per (flow, item) — the item's scope: the
        // outputs its condition reads for the item, the effective inputs, the
        // settled folders, the facts; recorded once, read back on replay.
        string GateKey(string folder, string itemId) => $"{folder}/{itemId}";

        // v29 (#1304): an item gate whose nodes are known — decided or in flight —
        // that this node reads; a reader of the folder (its body, or a template that
        // reads it whole) carries the gate's nodes in its reads, so it waits here.
        bool WaitsOnItemGate(ConsultNodeDescriptor node) =>
            itemGates.Count > 0
            && itemGates.Any(folder => folder.Value.Any(gate =>
                !gateDecisions.ContainsKey(GateKey(folder.Key, gate.Key))
                && gate.Value.Intersect(node.Reads ?? Array.Empty<string>(), StringComparer.Ordinal).Any()));

        // v29 (#1304): the settled folders with every item a gate excluded taken out — what
        // a render reads (an absent item is inert in a direct read, ConsultGatedScope).
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? RenderFolders()
        {
            var folders = ConsultRenderPayload.FoldersFor(collections);
            if (folders is null || !gateDecisions.ContainsValue(false))
            {
                return folders;
            }

            return folders.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<IReadOnlyDictionary<string, string>>)pair.Value
                    .Where(item => gateDecisions.GetValueOrDefault(GateKey(pair.Key, item.GetValueOrDefault("id", string.Empty)), true))
                    .ToList(),
                StringComparer.Ordinal);
        }

        void StartItemGates()
        {
            foreach (var (folder, gates) in itemGates)
            {
                foreach (var (itemId, reads) in gates)
                {
                    var key = GateKey(folder, itemId);
                    if (gatesStarted.Contains(key)
                        || !reads.All(read => outputs.ContainsKey(read) || skippedNodes.Contains(read))
                        || collections?.GetValueOrDefault(folder)?.FirstOrDefault(candidate => candidate.GetValueOrDefault("id") == itemId) is not { } item)
                    {
                        continue;
                    }

                    gatesStarted.Add(key);
                    pendingDecisions[context.CallActivityAsync<ConsultFlowItemDecision>(
                        ConsultGenerationActivityNames.DecideFlowItem,
                        new ConsultDecideFlowItemInput(
                            input.WorkflowPackage!,
                            folder,
                            WorkflowFanSources.SnippetPrefix + folder,
                            item,
                            new Dictionary<string, string>(effectiveInputs, StringComparer.Ordinal),
                            reads.Where(outputs.ContainsKey).ToDictionary(read => read, read => outputs[read].Classification ?? outputs[read].RawOutput, StringComparer.Ordinal),
                            ConsultRenderPayload.FoldersFor(collections),
                            ConsultRenderPayload.RunFactsFor(context.CurrentUtcDateTime, context.InstanceId, input.WorkflowPackage, input.ApiHost),
                            ConsultRenderPayload.ProfileFactsFor(input.ProfileName, input.Signature),
                            GateFolder: folder),
                        AgentActivityRetryOptions)] = (ItemGatePrefix + folder, itemId);
                }
            }
        }

        // v29 (#1304, design § 3.2): what an item gate's decision settles. Held: the
        // item runs as any item. Not held: in every loop over the folder the item is
        // skipped by condition — each fanned node's instance settled with nothing
        // (so the loop and the deliverable compose the present items, as 27's
        // skipped item), the loop's row for the item with the reason, the block of
        // every deliverable sourcing the loop — and later renders drop it.
        async Task SettleItemGateAsync(string folder, string itemId, ConsultFlowItemDecision decision)
        {
            gateDecisions[GateKey(folder, itemId)] = !decision.Skipped;

            if (!decision.Skipped)
            {
                return;
            }

            var reason = decision.Reason ?? "its if does not hold";
            var name = collections?.GetValueOrDefault(folder)?.FirstOrDefault(candidate => candidate.GetValueOrDefault("id") == itemId) is { } item
                ? WorkflowPackageBlocks.ItemName(item)
                : itemId;

            foreach (var fanned in nodes.Where(n => n.ForEach is not null && string.Equals(CollectionIdOf(n), folder, StringComparison.Ordinal)))
            {
                inner.SkippedInstances.Add(ConsultNodeScheduler.InstanceKey(fanned.Id, itemId));
            }

            foreach (var loop in nodes.Where(n => n.Kind == ConsultNodeFlows.For && n.Collection is { } c && string.Equals(WorkflowFanSources.CollectionIdOf(c), folder, StringComparison.Ordinal)))
            {
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeItemSkipped),
                    new ConsultGenerationNodeItemSkip(loop.Id, itemId, loop.Label, reason));

                foreach (var deliverable in deliverables.Where(d => d.Sources?.Contains(loop.Id, StringComparer.Ordinal) == true))
                {
                    completedBlockCount++;

                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.SkipBlock),
                        new ConsultGenerationBlockSkip(WorkflowItemPath.BlockId(deliverable.BlockPrefix, loop.Id, itemId), name, reason));
                }
            }

            await MarkFullyCompletedChainNodesAsync();
        }

        void StartDecision(ConsultNodeDescriptor flow, IReadOnlyDictionary<string, string> item)
        {
            started.Add(ConsultNodeScheduler.InstanceKey(flow.Id, WorkflowItemPath.OfItem(item)!));
            pendingDecisions[context.CallActivityAsync<ConsultFlowItemDecision>(
                ConsultGenerationActivityNames.DecideFlowItem,
                new ConsultDecideFlowItemInput(
                    input.WorkflowPackage!,
                    flow.Id,
                    flow.ForEach!,
                    item,
                    new Dictionary<string, string>(effectiveInputs, StringComparer.Ordinal),
                    ConsultRenderPayload.NodeTextsFor(flow, item, nodesById, outputs, SkippedKeys()),
                    RenderFolders(),
                    ConsultRenderPayload.RunFactsFor(context.CurrentUtcDateTime, context.InstanceId, input.WorkflowPackage, input.ApiHost),
                    ConsultRenderPayload.ProfileFactsFor(input.ProfileName, input.Signature),
                    ConsultRenderPayload.FlowRetsFor(flow, nodesById, ItemRets()),
                    // #1356: the flows its condition reads that took no branch, for this item.
                    SkippedNodes: ConsultRenderPayload.SkippedReadsFor(flow, item, nodesById, skippedNodes, inner.SkippedInstances),
                    Parents: ParentsOf(flow, item)),
                AgentActivityRetryOptions)] = (flow.Id, WorkflowItemPath.OfItem(item)!);
        }

        // v30 (#1342, design § 3.4): an item's until, once its body settled — decided as an
        // inner if is, over the item and its body's nodes.
        void StartUntilCheck(ConsultNodeDescriptor loop, IReadOnlyDictionary<string, string> item)
        {
            var itemId = WorkflowItemPath.OfItem(item)!;
            untilStarted.Add($"{loop.Id}/{itemId}");
            pendingDecisions[context.CallActivityAsync<ConsultFlowItemDecision>(
                ConsultGenerationActivityNames.DecideFlowItem,
                new ConsultDecideFlowItemInput(
                    input.WorkflowPackage!,
                    loop.Id,
                    nodesById[loop.Ret!].ForEach ?? loop.Collection!,
                    item,
                    new Dictionary<string, string>(effectiveInputs, StringComparer.Ordinal),
                    ConsultRenderPayload.NodeTextsFor(loop, item, nodesById, outputs, SkippedKeys()),
                    RenderFolders(),
                    ConsultRenderPayload.RunFactsFor(context.CurrentUtcDateTime, context.InstanceId, input.WorkflowPackage, input.ApiHost),
                    ConsultRenderPayload.ProfileFactsFor(input.ProfileName, input.Signature),
                    SkippedNodes: ConsultRenderPayload.SkippedReadsFor(loop, item, nodesById, skippedNodes, inner.SkippedInstances),
                    Until: true),
                AgentActivityRetryOptions)] = (UntilPrefix + loop.Id, itemId);
        }

        // What an until settles: held at item k, the items after it are not run.
        async Task SettleUntilAsync(string loopId, string itemId, ConsultFlowItemDecision decision)
        {
            untilDecided.Add($"{loopId}/{itemId}");
            var loop = nodesById[loopId];
            var outer = OuterOf(loop, itemId);
            if (decision.Branch != "until" || loopStops.GetValueOrDefault(LoopKey(loop, outer)) == "until")
            {
                return;
            }

            var items = LoopItems(loop, outer).ToList();
            var index = items.FindIndex(item => WorkflowItemPath.OfItem(item) == itemId);
            var at = $"item {index + 1} ({WorkflowPackageBlocks.ItemName(items[index])})";
            await StopLoopAsync(loop, outer, "until", $"its until held at {at}", items.Skip(index + 1), $"not run: stopped early at {at}");
        }

        // v30 (#1342, design § 3.4): a for's max — once its list is known, the items past it
        // are not run (the count stays the list's length).
        async Task ApplyLoopBoundsAsync()
        {
            // (B3c: per instance inside a loop, as each instance's list settles.)
            foreach (var loop in nodes.Where(node => Stoppable(node) && node.Max is not null))
            {
                foreach (var outer in InstancesOf(loop))
                {
                    if (!InstanceKnown(loop, outer) || !boundsApplied.Add(LoopKey(loop, outer)))
                    {
                        continue;
                    }

                    var items = LoopItems(loop, outer);
                    if (items.Count > loop.Max!.Value)
                    {
                        await StopLoopAsync(loop, outer, "bound", $"stopped at its bound of {loop.Max}", items.Skip(loop.Max.Value), $"not run: stopped at its bound of {loop.Max}");
                    }
                }
            }
        }

        // The stop, in one entity call: every item not run settles (its instances skipped, so
        // the loop and the deliverable compose without them) and its blocks are skipped.
        // (B3c: inside a loop, one instance — its row, and its items' blocks by their paths.)
        async Task StopLoopAsync(ConsultNodeDescriptor loop, string? outer, string stopped, string stopReason, IEnumerable<IReadOnlyDictionary<string, string>> notRun, string itemReason)
        {
            loopStops[LoopKey(loop, outer)] = stopped;
            var nestedBlock = outer is null ? null : NestedBlockOf(nodesById[loop.Ret!]);
            var items = new List<ConsultNotRunItem>();

            foreach (var item in notRun)
            {
                var itemId = WorkflowItemPath.OfItem(item)!;
                if (!notRunItems.Add($"{loop.Id}/{itemId}"))
                {
                    continue;
                }

                foreach (var fanned in FannedIn(loop))
                {
                    inner.SkippedInstances.Add(ConsultNodeScheduler.InstanceKey(fanned.Id, itemId));
                }

                var blockIds = (outer is null
                        ? deliverables
                            .Where(deliverable => deliverable.Sources?.Contains(loop.Id, StringComparer.Ordinal) == true)
                            .Select(deliverable => WorkflowItemPath.BlockId(deliverable.BlockPrefix, loop.Id, itemId))
                        : nestedBlock is { } block
                            ? deliverables
                                .Where(deliverable => deliverable.Sources?.Contains(block.Top, StringComparer.Ordinal) == true)
                                .Select(deliverable => WorkflowItemPath.BlockId(deliverable.BlockPrefix, block.Loop, itemId))
                            : Enumerable.Empty<string>())
                    .ToList();
                completedBlockCount += blockIds.Count;
                items.Add(new ConsultNotRunItem(itemId, WorkflowPackageBlocks.ItemName(item), itemReason, blockIds));
            }

            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.StopLoop),
                new ConsultGenerationLoopStopped(loop.Id, loop.Label, stopped, stopReason, items, Path: outer));
            await MarkFullyCompletedChainNodesAsync();
        }

        // v30 (#1342, design § 3.1): a while's check after pass n (n = 0: before the first,
        // under checkFirst) — decided as an if is, over that pass's nodes and the pass;
        // before the first pass the body reads as skipped.
        void StartWhileCheck(ConsultNodeDescriptor whileNode, string? outer, int number)
        {
            whileChecksStarted.Add($"{WhileKey(whileNode, outer)}/{number}");
            var outerItem = OuterItemOf(whileNode, outer);
            var item = number == 0 ? outerItem : PassItem(outer, number);
            var body = number == 0 ? BodyOf(whileNode).ToList() : null;
            var skipped = body is null ? SkippedKeys() : SkippedKeys().Concat(body).ToHashSet(StringComparer.Ordinal);
            pendingDecisions[context.CallActivityAsync<ConsultFlowItemDecision>(
                ConsultGenerationActivityNames.DecideFlowItem,
                new ConsultDecideFlowItemInput(
                    input.WorkflowPackage!,
                    whileNode.Id,
                    WhilePrefix + whileNode.Id,
                    item ?? PassItem(null, 0),
                    new Dictionary<string, string>(effectiveInputs, StringComparer.Ordinal),
                    ConsultRenderPayload.NodeTextsFor(whileNode, item, nodesById, outputs, skipped),
                    RenderFolders(),
                    ConsultRenderPayload.RunFactsFor(context.CurrentUtcDateTime, context.InstanceId, input.WorkflowPackage, input.ApiHost),
                    ConsultRenderPayload.ProfileFactsFor(input.ProfileName, input.Signature),
                    SkippedNodes: body,
                    // v30 (#1342 B3c): inside a for's item, the check reads that item as `item`.
                    Parents: outerItem is null ? null : new[] { new ConsultParentItem(whileNode.ForEach!, outerItem) },
                    Pass: PassOf(whileNode, outer, number)),
                AgentActivityRetryOptions)] = (WhilePrefix + whileNode.Id, PassPath(outer, number));
        }

        // A while's check is ready once pass n settled and what it reads outside the body has.
        bool WhileCheckReady(ConsultNodeDescriptor whileNode, string? outer, int number) =>
            (number == 0 || PassSettled(whileNode, outer, number))
            && (whileNode.Reads ?? Array.Empty<string>()).All(readId => BodyOf(whileNode).Contains(readId, StringComparer.Ordinal)
                || ConsultNodeScheduler.InstanceKey(readId, ConsultNodeScheduler.AlignedPath(nodesById[readId], outer, nodesById)) is var key
                    && (outputs.ContainsKey(key) || skippedNodes.Contains(readId) || inner.SkippedInstances.Contains(key)));

        // v30 (#1342, design § 3.1): what a check settles — another pass below max; else the
        // while stops: its check, its until, or its bound (a named state, never silent).
        async Task SettleWhileCheckAsync(string whileId, string passPath, ConsultFlowItemDecision decision)
        {
            var whileNode = nodesById[whileId];
            var (outer, number) = PassOfPath(passPath) ?? throw new InvalidOperationException($"While '{whileId}' decided a check for '{passPath}', which is no pass.");
            var max = whileNode.Max ?? WorkflowFlowNodes.MaxPasses;

            if (decision.Branch == "again")
            {
                if (number < max)
                {
                    passesOpened[WhileKey(whileNode, outer)] = number + 1;
                    return;
                }

                await StopWhileAsync(whileNode, outer, number, "bound", $"stopped at its bound of {max}; the check never stopped holding");
                return;
            }

            await StopWhileAsync(whileNode, outer, number, decision.Branch,
                number == 0 ? "its check did not hold before the first pass"
                : decision.Branch == "until" ? "its until held"
                : "its check stopped holding");
        }

        // The while's row (its instance's, inside a for's item) names its passes and why it
        // stopped. At the top: with none, its block is skipped with the reason (decided
        // 2026-11-10); otherwise the last pass's ret completes it (TryCompleteAggregatorsAsync).
        // Inside a for's item: the instance is the item's — its last pass's ret, or skipped.
        async Task StopWhileAsync(ConsultNodeDescriptor whileNode, string? outer, int passes, string stopped, string reason)
        {
            whileStops[WhileKey(whileNode, outer)] = (passes, stopped);
            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.SettleWhile),
                new ConsultGenerationWhileSettled(whileNode.Id, whileNode.Label, passes, stopped, reason, context.CurrentUtcDateTime, Path: outer));

            if (outer is not null)
            {
                await SettleWhileInstanceAsync(whileNode, outer, passes, reason);
                return;
            }

            if (passes > 0)
            {
                return;
            }

            foreach (var skippedId in BodyOf(whileNode).Append(whileNode.Id))
            {
                skippedNodes.Add(skippedId);
                completedNodeCount++;
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeSkipped),
                    new ConsultGenerationNodeSkip(skippedId, nodesById[skippedId].Label, reason, completedNodeCount, totalNodeCount));
            }

            foreach (var deliverable in deliverables.Where(d => d.SourceIds.Contains(whileNode.Id)))
            {
                completedBlockCount++;
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.SkipBlock),
                    new ConsultGenerationBlockSkip($"{deliverable.BlockPrefix}{whileNode.Id}", whileNode.Label, reason));
            }
        }

        // v30 (#1342 B3c): a while instance under a for's item settles as that item's node —
        // its last pass's ret (the item's block, when the while is the loop's ret), or, with no
        // pass, skipped by condition for the item with the reason.
        async Task SettleWhileInstanceAsync(ConsultNodeDescriptor whileNode, string outer, int passes, string reason)
        {
            var loop = LoopAround(whileNode);
            var returnsIt = loop is not null && string.Equals(loop.Ret, whileNode.Id, StringComparison.Ordinal);
            var blockDeliverables = returnsIt
                ? deliverables.Where(d => d.Sources?.Contains(loop!.Id, StringComparer.Ordinal) == true).ToList()
                : new List<ConsultDeliverables.Deliverable>();

            if (passes == 0)
            {
                inner.SkippedInstances.Add(ConsultNodeScheduler.InstanceKey(whileNode.Id, outer));
                foreach (var deliverable in blockDeliverables)
                {
                    completedBlockCount++;
                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.SkipBlock),
                        new ConsultGenerationBlockSkip(WorkflowItemPath.BlockId(deliverable.BlockPrefix, loop!.Id, outer), ItemName(whileNode, outer), reason));
                }
            }
            else
            {
                var result = outputs[ConsultNodeScheduler.InstanceKey(whileNode.Ret!, PassPath(outer, passes))];
                outputs[ConsultNodeScheduler.InstanceKey(whileNode.Id, outer)] = result;
                foreach (var deliverable in blockDeliverables)
                {
                    completedBlockCount++;
                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.CompleteBlock),
                        new BlockGenerationResult(WorkflowItemPath.BlockId(deliverable.BlockPrefix, loop!.Id, outer), ItemName(whileNode, outer), true, result.RawOutput.Trim(), null));
                }
            }

            await MarkFullyCompletedChainNodesAsync();
        }

        // A pass that could not finish fails the while — at the top its block fails with the
        // sentence; inside a for's item, that item fails. No further pass opens; the run goes on.
        async Task FailWhileAsync(ConsultNodeDescriptor whileNode, string? outer, string failure)
        {
            // v30 (#1342 B3d): inside a while, the instance fails the enclosing pass's while instance.
            if (outer is not null && RepeatOwnerOf(whileNode) is { Kind: ConsultNodeFlows.While } owner)
            {
                failedWhileInstances.Add(WhileKey(whileNode, outer));
                if (PassOfPath(outer) is { } enclosing)
                {
                    await FailWhileAsync(owner, enclosing.Outer, failure);
                }

                return;
            }

            if (outer is not null)
            {
                await FailItemAsync(whileNode, outer, failure);
                return;
            }

            if (!failedAggregators.TryAdd(whileNode.Id, failure))
            {
                return;
            }

            failedItems.Add(WhilePrefix + whileNode.Id);
            foreach (var deliverable in deliverables.Where(d => d.SourceIds.Contains(whileNode.Id)))
            {
                failedBlockCount++;
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.FailBlock),
                    new BlockGenerationResult($"{deliverable.BlockPrefix}{whileNode.Id}", whileNode.Label, false, null, failure));
            }
        }

        void StartReadyInstances()
        {
            StartItemGates();

            // v30 (#1342, design § 3.4): each for with an until not stopped by it — the first
            // item whose until is undecided asks it once its body settled; a failed item's
            // until is not asked (it goes on to the next).
            // (B3c: per instance inside a loop.)
            foreach (var (loop, outer) in nodes.Where(node => Stoppable(node) && node.Until is not null)
                .SelectMany(node => InstancesOf(node).Select(outer => (node, outer)))
                .Where(instance => InstanceKnown(instance.node, instance.outer) && loopStops.GetValueOrDefault(LoopKey(instance.node, instance.outer)) != "until"))
            {
                foreach (var item in LoopItems(loop, outer))
                {
                    var itemId = WorkflowItemPath.OfItem(item)!;
                    if (untilDecided.Contains($"{loop.Id}/{itemId}") || notRunItems.Contains($"{loop.Id}/{itemId}") || ItemFailedIn(loop, itemId))
                    {
                        continue;
                    }

                    if (!untilStarted.Contains($"{loop.Id}/{itemId}") && ItemSettledIn(loop, itemId))
                    {
                        StartUntilCheck(loop, item);
                    }

                    break;
                }
            }

            // v30 (#1342): each while not stopped — pass 1 opens at once (unless checkFirst);
            // a settled pass (or, under checkFirst, the start) asks its check.
            // v30 (#1342 B3c): inside a for, one instance per item the loop runs.
            // (B3d: and inside a while, one instance per pass it opened.)
            foreach (var whileNode in nodes.Where(node => node.Kind == ConsultNodeFlows.While))
            {
                foreach (var outer in OuterPathsOf(whileNode))
                {
                    var key = WhileKey(whileNode, outer);
                    if (whileStops.ContainsKey(key)
                        || WhileInstanceFailed(whileNode, outer)
                        || (outer is not null
                            && (inner.SkippedInstances.Contains(ConsultNodeScheduler.InstanceKey(whileNode.Id, outer))
                                || !inner.Taken(whileNode, outer)
                                || !MayStart(whileNode, outer))))
                    {
                        continue;
                    }

                    var opened = passesOpened.GetValueOrDefault(key);
                    if (opened == 0 && whileNode.CheckFirst != true)
                    {
                        passesOpened[key] = opened = 1;
                    }

                    if (!whileChecksStarted.Contains($"{key}/{opened}") && WhileCheckReady(whileNode, outer, opened))
                    {
                        StartWhileCheck(whileNode, outer, opened);
                    }
                }
            }

            foreach (var node in nodes)
            {
                if (node.Aggregate != null)
                {
                    continue; // Aggregators compose inline, never as activities.
                }

                if (node.Check != null)
                {
                    continue; // v12 #624: checks settle inline too — no activity, no model.
                }

                if (node.Kind != null)
                {
                    // v27 (#1235): an if/case inside a loop is decided once per item, in an
                    // activity, when its reads settle for the item — dispatched as an
                    // instance is; every other flow node completes inline (#1154).
                    if (ConsultInnerFlows.IsInner(node))
                    {
                        foreach (var item in FanItems(node))
                        {
                            var itemId = WorkflowItemPath.OfItem(item)!;

                            if (failedItems.Contains(FailedKey(node, itemId))
                                || started.Contains(ConsultNodeScheduler.InstanceKey(node.Id, itemId))
                                || inner.SkippedInstances.Contains(ConsultNodeScheduler.InstanceKey(node.Id, itemId))
                                || WaitsOnItemGate(node)
                                // v30 (#1342): past the loop's max, or waiting on an earlier item's until.
                                || !MayStart(node, itemId))
                            {
                                continue;
                            }

                            if (ConsultNodeScheduler.InstanceReady(node, itemId, nodesById, outputs, SkippedKeys()))
                            {
                                StartDecision(node, item);
                            }
                        }
                    }

                    continue;
                }

                // v30 (#1342): a while's body node runs once per open pass, keyed by the pass.
                if (WhileOf(node) is { } owner)
                {
                    foreach (var outer in OuterPathsOf(owner))
                    {
                        for (var number = 1; number <= passesOpened.GetValueOrDefault(WhileKey(owner, outer)); number++)
                        {
                            var passPath = PassPath(outer, number);
                            if (!started.Contains(ConsultNodeScheduler.InstanceKey(node.Id, passPath))
                                && !WhileInstanceFailed(owner, outer)
                                && ConsultNodeScheduler.InstanceReady(node, passPath, nodesById, outputs, SkippedKeys()))
                            {
                                StartInstance(node, PassItem(outer, number));
                            }
                        }
                    }

                    continue;
                }

                if (node.ForEach is null)
                {
                    if (!started.Contains(node.Id)
                        && !WaitsOnItemGate(node)
                        && ConsultNodeScheduler.InstanceReady(node, null, nodesById, outputs, SkippedKeys()))
                    {
                        StartInstance(node, null);
                    }

                    continue;
                }

                foreach (var item in FanItems(node))
                {
                    var itemId = WorkflowItemPath.OfItem(item)!;

                    // v27 (#1235): a branch node's instance runs only once its flow node
                    // took the branch holding it for this item.
                    if (failedItems.Contains(FailedKey(node, itemId))
                        || started.Contains(ConsultNodeScheduler.InstanceKey(node.Id, itemId))
                        || !inner.Taken(node, itemId)
                        // v29 (#1304): an item its gate excluded never runs; one not yet decided waits.
                        || inner.SkippedInstances.Contains(ConsultNodeScheduler.InstanceKey(node.Id, itemId))
                        || WaitsOnItemGate(node)
                        // v30 (#1342): past the loop's max, or waiting on an earlier item's until.
                        || !MayStart(node, itemId))
                    {
                        continue;
                    }

                    if (ConsultNodeScheduler.InstanceReady(node, itemId, nodesById, outputs, SkippedKeys()))
                    {
                        StartInstance(node, item);
                    }
                }
            }
        }

        // v27 (#1235, design § 1.4, § 2): what one item's decision settles — the
        // branch onto the flow row's map; when none, the item is skipped for the
        // flow: its per-item row with the reason, and the block of every
        // deliverable sourcing the loop that returns it (progress counts it).
        async Task SettleDecisionAsync(string flowId, string itemId, ConsultFlowItemDecision decision)
        {
            var flow = nodesById[flowId];
            var key = ConsultNodeScheduler.InstanceKey(flowId, itemId);
            inner.Decisions[key] = decision;

            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.RecordFlowBranch),
                new ConsultGenerationFlowBranch(flowId, itemId, decision.Branch));

            if (!decision.Skipped)
            {
                return;
            }

            inner.SkippedInstances.Add(key);
            var reason = decision.Reason ?? "took no branch";

            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.MarkNodeItemSkipped),
                new ConsultGenerationNodeItemSkip(flowId, itemId, flow.Label, reason));

            if (v6 && inner.LoopReturning(flowId) is { } loop)
            {
                foreach (var deliverable in deliverables.Where(d => d.SourceIds.Contains(flowId)))
                {
                    completedBlockCount++;

                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.SkipBlock),
                        new ConsultGenerationBlockSkip(WorkflowItemPath.BlockId(deliverable.BlockPrefix, loop.Id, itemId), ItemName(flow, itemId), reason));
                }
            }

            await MarkFullyCompletedChainNodesAsync();
        }

        // v29 (#1303, design § 1.2, § 2.3): the loops over this node's value settle
        // — their items read from the recorded output (so a replay reads the same
        // list), their blocks laid out (one per item per deliverable sourcing the
        // loop), the total re-written, the entity told once per loop. The body then
        // fans as any loop's does. A list that cannot be read fails the job (the
        // source is a top-level node, whose failure is the job's — decided
        // 2026-11-07); an empty one is a loop with no items and a reason.
        async Task<bool> ResolveLoopsFedByAsync(ConsultNodeDescriptor source, NodeRunResult result)
        {
            foreach (var loop in nodes.Where(n => n.Kind == ConsultNodeFlows.For && unresolvedLoops.Contains(n.Id) && !nestedLoopIds.Contains(n.Id)).ToList())
            {
                if (loop.Collection is not { } collection
                    || ConsultNodeValueLoops.SourceOf(collection) is not { } fed
                    || !string.Equals(fed.NodeId, source.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var resolution = ConsultNodeValueLoops.ItemsOf(result, collection, loop.Label, loop.LabelField);

                if (resolution.Failure is { } failure)
                {
                    logger.LogWarning("A loop over a node's value could not read its list. JobId={JobId}, LoopId={LoopId}", context.InstanceId, loop.Id);
                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.MarkNodeFailed),
                        new ConsultGenerationNodeFailure(
                            loop.Id, loop.Label, $"{loop.Id}-failed", failure,
                            nodes.Where(other => other.Id != loop.Id && !outputs.ContainsKey(other.Id)).Select(other => new ConsultItemStepDescriptor(other.Id, other.Label)).ToList(),
                            null, null));
                    PublishStatus(ConsultGenerationJobStatuses.Failed);
                    return false;
                }

                resolvedItems[WorkflowFanSources.CollectionIdOf(collection)] = resolution.Items;
                unresolvedLoops.Remove(loop.Id);

                var blocks = deliverables
                    .Where(deliverable => deliverable.Sources?.Contains(loop.Id, StringComparer.Ordinal) == true && !ReturnsALoop(loop))
                    .SelectMany(deliverable => resolution.Items.Select(item => new ConsultCollectionItem(
                        WorkflowItemPath.BlockId(deliverable.BlockPrefix, loop.Id, WorkflowItemPath.OfItem(item)!), WorkflowPackageBlocks.ItemName(item))))
                    .ToList();
                totalBlockCount += blocks.Count;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.ResolveLoop),
                    new ConsultGenerationLoopResolution(
                        loop.Id,
                        collection,
                        resolution.Items.Select(item => new ConsultCollectionItem(WorkflowItemPath.OfItem(item)!, WorkflowPackageBlocks.ItemName(item))).ToList(),
                        blocks,
                        totalBlockCount,
                        context.CurrentUtcDateTime,
                        resolution.Reason));
            }

            return true;
        }

        // v30 (#1341, design § 2.2–§ 2.5): each nested loop's instance resolves once its
        // source is known for that outer item — the outer item's own field at once, a node
        // of an enclosing body when that node completes for the outer path, a top-level
        // node, input or folder once its list is known. An empty list settles at 0 with its
        // reason; an unreadable one fails THAT outer item (decided 2026-11-10), never the
        // job. The loop leaves the unresolved set when every outer item has its instance.
        async Task ResolveNestedInstancesAsync()
        {
            foreach (var loop in nestedLoops)
            {
                if (!unresolvedLoops.Contains(loop.Id) || loop.Collection is not { } collection)
                {
                    continue;
                }

                foreach (var outer in FanItems(loop))
                {
                    var outerPath = WorkflowItemPath.OfItem(outer)!;
                    if (loopInstances.Has(loop.Id, outerPath) || NestedSourceOf(loop, collection, outer, outerPath) is not { } resolution)
                    {
                        continue;
                    }

                    if (resolution.Failure is { } failure)
                    {
                        loopInstances.Fail(loop.Id, outerPath, failure);
                        await FailNestedInstanceAsync(loop, outer, outerPath, failure);
                        continue;
                    }

                    var instanceItems = ConsultNodeValueLoops.WithPath(resolution.Items, outerPath);
                    loopInstances.Resolve(loop.Id, outerPath, instanceItems, resolution.Reason);

                    // A loop whose ret is a loop (a middle level) lays out no blocks — its innermost loop's instances do.
                    var blocks = !ReturnsALoop(loop) && NestedBlockOf(nodesById[loop.Ret!]) is { } nestedBlock
                        ? deliverables
                            .Where(deliverable => deliverable.Sources?.Contains(nestedBlock.Top, StringComparer.Ordinal) == true)
                            .SelectMany(deliverable => instanceItems.Select(item => new ConsultCollectionItem(
                                WorkflowItemPath.BlockId(deliverable.BlockPrefix, loop.Id, WorkflowItemPath.OfItem(item)!), WorkflowPackageBlocks.ItemName(item))))
                            .ToList()
                        : new List<ConsultCollectionItem>();
                    totalBlockCount += blocks.Count;

                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.ResolveLoop),
                        new ConsultGenerationLoopResolution(
                            loop.Id,
                            collection,
                            instanceItems.Select(item => new ConsultCollectionItem(WorkflowItemPath.OfItem(item)!, WorkflowPackageBlocks.ItemName(item))).ToList(),
                            blocks,
                            totalBlockCount,
                            context.CurrentUtcDateTime,
                            resolution.Reason,
                            Path: outerPath));
                }

                // (B3d: inside a while, not while it can still open a pass.)
                if (!EnclosingLoopUnresolved(loop) && !EnclosingWhileRunning(loop) && FanItems(loop).All(outer => loopInstances.Has(loop.Id, WorkflowItemPath.OfItem(outer)!)))
                {
                    unresolvedLoops.Remove(loop.Id);
                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.ResolveLoop),
                        new ConsultGenerationLoopResolution(
                            loop.Id,
                            collection,
                            loopInstances.ItemsIn(loop.Id, FanItems(loop).Select(outer => WorkflowItemPath.OfItem(outer)!))
                                .Select(item => new ConsultCollectionItem(WorkflowItemPath.OfItem(item)!, WorkflowPackageBlocks.ItemName(item))).ToList(),
                            Array.Empty<ConsultCollectionItem>(),
                            totalBlockCount,
                            context.CurrentUtcDateTime,
                            Settled: true));
                }
            }
        }

        bool EnclosingLoopUnresolved(ConsultNodeDescriptor loop)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { loop.Id };
            for (var ownerId = loop.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
            {
                if (owner.Kind == ConsultNodeFlows.For && unresolvedLoops.Contains(owner.Id))
                {
                    return true;
                }
            }

            return false;
        }

        // v30 (#1341): a nested instance's list, when its source is known for the outer item; null while it is not.
        ConsultNodeValueLoops.Resolution? NestedSourceOf(ConsultNodeDescriptor loop, string collection, IReadOnlyDictionary<string, string> outer, string outerPath)
        {
            if (ConsultNodeValueLoops.SourceOf(collection) is { } fed)
            {
                var source = nodesById[fed.NodeId];
                var aligned = ConsultNodeScheduler.AlignedPath(source, outerPath, nodesById);
                if (outputs.TryGetValue(ConsultNodeScheduler.InstanceKey(source.Id, aligned), out var result))
                {
                    return ConsultNodeValueLoops.ItemsOf(result, collection, loop.Label, loop.LabelField);
                }

                return aligned is not null && source.ForEach is not null && failedItems.Contains(FailedKey(source, aligned))
                    ? new ConsultNodeValueLoops.Resolution(Array.Empty<IReadOnlyDictionary<string, string>>(), null, $"{loop.Label} could not read its list: {source.Label} did not complete for this item.")
                    : null;
            }

            if (collection.StartsWith("item.value.", StringComparison.Ordinal))
            {
                return ConsultNodeValueLoops.ItemsOfOuterItem(outer, collection, loop.Label, loop.LabelField);
            }

            return collections?.GetValueOrDefault(WorkflowFanSources.CollectionIdOf(collection)) is { } listed
                ? new ConsultNodeValueLoops.Resolution(listed, listed.Count == 0 ? $"no items: {collection} was empty" : null, null)
                : null;
        }

        // v30 (#1341, decided 2026-11-10): an unreadable inner list fails its OUTER item —
        // its block fails with the sentence, the loop above cannot assemble it, the rest run on.
        async Task FailNestedInstanceAsync(ConsultNodeDescriptor loop, IReadOnlyDictionary<string, string> outer, string outerPath, string failure)
        {
            logger.LogWarning("A nested loop could not read its list. JobId={JobId}, LoopId={LoopId}", context.InstanceId, loop.Id);
            failedItems.Add(FailedKey(loop, outerPath));

            if (NestedBlockOf(nodesById[loop.Ret!]) is { } nestedBlock)
            {
                foreach (var deliverable in deliverables.Where(d => d.Sources?.Contains(nestedBlock.Top, StringComparer.Ordinal) == true))
                {
                    totalBlockCount++;
                    failedBlockCount++;
                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.FailBlock),
                        new BlockGenerationResult(WorkflowItemPath.BlockId(deliverable.BlockPrefix, loop.Id, outerPath), WorkflowPackageBlocks.ItemName(outer), false, null, failure));
                }
            }

            // Storage 11 (#1341): the failed instance's row, with its sentence.
            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.ResolveLoop),
                new ConsultGenerationLoopResolution(
                    loop.Id, loop.Collection!, Array.Empty<ConsultCollectionItem>(), Array.Empty<ConsultCollectionItem>(),
                    totalBlockCount, context.CurrentUtcDateTime, Path: outerPath, Failure: failure));
        }

        async Task MarkFullyCompletedChainNodesAsync()
        {
            // A forEach node completes at the node level once every item is either
            // done for it or failed earlier in its chain — this is what drives the
            // stage checklist and the node-completed events.
            // #1246: an inner if/case's items settle through the alias flow:item, and
            // this pass ran before the aggregator pass wrote it — so the flow's row
            // was marked one FANNED completion late, and never when its loop was
            // the last fan to finish (the smoke's `applies`). The aliases land first.
            inner.AliasTakenRets(outputs, FanItems);

            foreach (var node in chainNodes)
            {
                // v30 (#1342 B3d): a nested loop's row completes once, where it composes
                // (TryCompleteAggregatorsAsync, with its hashes) — never here as well.
                if (nodeLevelCompleted.Contains(node.Id) || AwaitsCount(node) || node.Kind == ConsultNodeFlows.For)
                {
                    continue;
                }

                // v27 (#1235): an untaken branch instance and an item skipped by condition are settled too.
                // v30 (#1342 B3c): a while's body inside a for's item settles when every instance of
                // its while has stopped (or its item failed or was skipped).
                var allSettled = WhileOf(node) is { } bodyOf
                    ? OuterPathsOf(bodyOf).All(outer => outer is null
                        || whileStops.ContainsKey(WhileKey(bodyOf, outer))
                        || failedItems.Contains(FailedKey(bodyOf, outer))
                        || inner.SkippedInstances.Contains(ConsultNodeScheduler.InstanceKey(bodyOf.Id, outer)))
                    : FanItems(node).All(item =>
                        inner.Settled(node, WorkflowItemPath.OfItem(item)!, outputs, failedItems.Contains(FailedKey(node, WorkflowItemPath.OfItem(item)!))));

                // #1246 (diagnostic, ids and flags only — no text): why an inner flow is not yet node-level complete.
                if (!allSettled && ConsultInnerFlows.IsInner(node) && !context.IsReplaying)
                {
                    var facts = string.Join(" ", FanItems(node).Select(item =>
                    {
                        var itemId = WorkflowItemPath.OfItem(item)!;
                        var key = ConsultNodeScheduler.InstanceKey(node.Id, itemId);
                        var ret = inner.RetOf(node.Id, itemId);
                        return $"{itemId}[decided={inner.Decided(node.Id, itemId)} ret={ret ?? "-"} alias={outputs.ContainsKey(key)} retOut={(ret is null ? "-" : outputs.ContainsKey(ConsultNodeScheduler.InstanceKey(ret, itemId)).ToString())} skipped={inner.SkippedInstances.Contains(key)}]";
                    }));
                    logger.LogInformation("Inner flow not settled at the summary pass. JobId={JobId}, Flow={Flow}, ForEach={ForEach}, Items={Items}", context.InstanceId, node.Id, node.ForEach, facts);
                }

                if (!allSettled)
                {
                    continue;
                }

                nodeLevelCompleted.Add(node.Id);
                completedNodeCount++;

                // #980: the fan's summary row carries no hashes (nothing was sent at node
                // level) but does carry the custom stamp, so a reader of the node-level
                // row knows what its items are.
                var stamp = CustomStamp(node);
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                    new ConsultGenerationNodeUpdate(node.Id, node.Label, null, null, null, completedNodeCount, totalNodeCount,
                        Unattested: stamp.Unattested, CustomSchemaHash: stamp.CustomSchemaHash, CustomAgent: stamp.CustomAgent));
            }
        }

        async Task FailItemAsync(ConsultNodeDescriptor node, string itemId, string error, string? errorStack = null)
        {
            failedItems.Add(FailedKey(node, itemId));

            if (!v6)
            {
                failedBlockCount++;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.FailBlock),
                    new BlockGenerationResult(itemId, ItemName(node, itemId), false, null, error, errorStack));
            }
            else if (NestedBlockOf(node) is { } nestedBlock)
            {
                // v30 (#1341): the innermost loop's block of the item, named by its path.
                foreach (var deliverable in deliverables.Where(d => d.Sources?.Contains(nestedBlock.Top, StringComparer.Ordinal) == true))
                {
                    failedBlockCount++;

                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.FailBlock),
                        new BlockGenerationResult(WorkflowItemPath.BlockId(deliverable.BlockPrefix, nestedBlock.Loop, itemId), ItemName(node, itemId), false, null, error));
                }
            }
            else
            {
                // v6/v7 blocks: every deliverable's result-source block over this
                // collection fails — the item can no longer reach that document.
                // A source shared by two deliverables fails once per owner; v6's
                // single empty-prefix entry reproduces today's ids and order.
                foreach (var deliverable in deliverables)
                {
                    foreach (var source in deliverable.SourceIds
                        .Select(id => nodesById.GetValueOrDefault(id))
                        .Where(source => source?.ForEach != null
                            && string.Equals(CollectionIdOf(source), CollectionIdOf(node), StringComparison.Ordinal)))
                    {
                        failedBlockCount++;

                        await context.Entities.CallEntityAsync(
                            entityId,
                            nameof(ConsultGenerationJobEntity.FailBlock),
                            new BlockGenerationResult(WorkflowItemPath.BlockId(deliverable.BlockPrefix, BlockNodeOf(source!), itemId), ItemName(node, itemId), false, null, error));
                    }
                }
            }

            // Downstream item-aligned instances never start; nodes waiting only on
            // this item may now be node-level complete.
            await MarkFullyCompletedChainNodesAsync();
        }

        // v11 #513/#516, v12 #619/#620: one deliverable's completion chain —
        // Compose (placement-aware, byte-identical to the v11 append with
        // nothing placed) → Finish (signed once) → the recorded text → the
        // entity. Extracted so a checked deliverable runs the SAME chain
        // verbatim after its check passes (v12 #624); the unchecked path is
        // byte-identical control flow.
        async Task CompleteDeliverableAsync(
            ConsultDeliverables.Deliverable deliverable,
            IReadOnlyList<string> sourceRefs,
            List<ConsultAggregateRenderer.Part> parts)
        {
            // v25 (B8 #1027, design § 5.1): a 25 deliverable's placed macros are
            // Scriban templates rendered over the job's scope — in an activity,
            // as every 25 template is — and composed around the sections here:
            // prepend, the aggregator's render, append; the signature read is the
            // activity's fact. The ≤24 path below is untouched.
            if (at25)
            {
                var placements = deliverable.MacroPlacements ?? Array.Empty<ConsultMacroPlacement>();
                var renderedMacros = placements.Count == 0
                    ? new ConsultDeliverableMacrosResult(Array.Empty<ConsultRenderedMacro>(), Array.Empty<ConsultRenderedMacro>(), false)
                    : await context.CallActivityAsync<ConsultDeliverableMacrosResult>(
                        ConsultGenerationActivityNames.RenderDeliverableMacros,
                        new ConsultDeliverableMacrosActivityInput(
                            deliverable.ResultId!,
                            input.WorkflowPackage ?? string.Empty,
                            placements,
                            new Dictionary<string, string>(effectiveInputs, StringComparer.Ordinal),
                            input.InputTypes is null ? null : new Dictionary<string, string>(input.InputTypes, StringComparer.Ordinal),
                            // v26 (#1155): the sources' texts beside the reads (the
                            // aggregator's below 26 — one id, the same filter).
                            (deliverable.Reads ?? Array.Empty<string>()).Concat(deliverable.Sources ?? new[] { deliverable.NodeId })
                                .Where(outputs.ContainsKey)
                                .Distinct(StringComparer.Ordinal)
                                .ToDictionary(id => id, id => outputs[id].Classification ?? outputs[id].RawOutput, StringComparer.Ordinal),
                            RenderFolders(),
                            ConsultRenderPayload.RunFactsFor(context.CurrentUtcDateTime, context.InstanceId, input.WorkflowPackage, input.ApiHost),
                            ConsultRenderPayload.ProfileFactsFor(input.ProfileName, input.Signature),
                            // v26 (#1155, design § 3.1): the deliverables its macros read, composed.
                            Deliverables: ConsultRenderPayload.DeliverableTextsFor(deliverable.DeliverableReads, composedDeliverables, RunDeliverableIds())),
                        AgentActivityRetryOptions);

                var (text25, appended25) = ConsultMacroExpander.ComposeAt25(renderedMacros.Prepend, parts, renderedMacros.Append);
                var (final25, finalAppended25, unsigned25) = ConsultSignatureAppend.Finish(
                    text25, appended25, deliverable.Signature == true, renderedMacros.SignatureRead, input.Signature);
                recordedTexts[deliverable.ResultId!] = final25;

                // v26 (#1155, design § 10): the composed text's hash and the sources
                // on the document's row — the aggregator's agentOutputHash, moved.
                var composed = composedDeliverables.GetValueOrDefault(deliverable.ResultId!);
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.CompleteResultDocument),
                    new ConsultGenerationResultDocument(deliverable.ResultId!, deliverable.Label ?? deliverable.ResultId!, final25, deliverable.Ordinal, finalAppended25, unsigned25,
                        ComposedHash: composed?.ComposedHash, Sources: composed is null ? null : deliverable.Sources));
                return;
            }

            var facts = new ConsultMacroExpander.RunFacts(
                context.CurrentUtcDateTime,
                context.InstanceId,
                input.WorkflowPackage ?? string.Empty,
                input.ApiHost,
                input.ProfileName,
                input.Signature);

            var (text, appended, tokenCarried) = ConsultMacroExpander.Compose(
                sourceRefs,
                parts,
                deliverable.MacroIds,
                deliverable.MacroPlacements,
                input.MacroTexts,
                effectiveInputs,
                input.DataScalars,
                Classifications(),
                facts);

            // #863 (v20): fill the model's inline [[slot:<id>]] markers over the
            // COMPOSED document (never the node output — the marker stays in the
            // node hash), before the signature/hash. The slot-mode placements are
            // the allow-list, already gate-filtered; a slot entry attributes the
            // fill outside node hashes, exactly like an appended macro.
            var authorizedSlotIds = (deliverable.MacroPlacements ?? Array.Empty<ConsultMacroPlacement>())
                .Where(placement => placement.Slot == true)
                .Select(placement => placement.Id)
                .ToHashSet(StringComparer.Ordinal);

            // v25 (B5 #1004, design § 7): at 25 a marker is sanctioned only by the
            // prompt that read macro.<id>.prompt, so it was filled — or removed and
            // named — at that node; the document-level fill is retired, and a
            // deliverable's text is its sources' composed outputs, untouched here.
            var (slotFilledText, slotEntries) = at25
                ? (text, null)
                : ConsultSlotFiller.Fill(
                    text,
                    authorizedSlotIds,
                    input.MacroTexts,
                    effectiveInputs,
                    input.DataScalars,
                    Classifications(),
                    facts);

            // Slot fills sit inside the section bodies, ahead of the trailing
            // appended macros — a slot entry precedes the composed appends in the
            // provenance list. The document text is the authority; the appended
            // list is attribution, so this is order enough (§ 6).
            var appendedWithSlots = slotEntries is { Count: > 0 }
                ? (appended is { Count: > 0 } ? slotEntries.Concat(appended).ToList() : slotEntries)
                : appended;

            var (finalText, finalAppended, unsigned) = ConsultSignatureAppend.Finish(
                slotFilledText, appendedWithSlots, deliverable.Signature == true, tokenCarried, input.Signature);
            recordedTexts[deliverable.ResultId!] = finalText;

            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.CompleteResultDocument),
                new ConsultGenerationResultDocument(deliverable.ResultId!, deliverable.Label ?? deliverable.ResultId!, finalText, deliverable.Ordinal, finalAppended, unsigned));
        }

        // v12 #624 (§ 13): checks settle inline once both operands have —
        // the aggregator's own discipline: pure, no activity, no retries.
        // Then every pending deliverable the check gates either runs its
        // stashed chain verbatim (pass) or records the third state (fail).
        async Task<bool> TrySettleChecksAsync()
        {
            var progressed = false;

            foreach (var checkNode in nodes)
            {
                if (checkNode.Check is not { } check || settledChecks.ContainsKey(checkNode.Id))
                {
                    continue;
                }

                var ofId = check.Of[WorkflowNodeBindingSources.NodePrefix.Length..];
                var inId = check.In[WorkflowNodeBindingSources.NodePrefix.Length..];

                if (!outputs.TryGetValue(ofId, out var ofResult) || !outputs.TryGetValue(inId, out var inResult))
                {
                    continue;
                }

                var outcome = ConsultCheckExecutor.TermsSubset(ofResult.Concepts, inResult.Concepts);
                settledChecks[checkNode.Id] = outcome;
                completedNodeCount++;
                progressed = true;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                    new ConsultGenerationNodeUpdate(
                        checkNode.Id, checkNode.Label, null,
                        null, null,
                        completedNodeCount, totalNodeCount,
                        Check: outcome));
            }

            // v25 (B8 #1027): a 25 check is a computed node that ran as its own
            // activity and recorded its outcome — settled from the record, not
            // recomputed, and not counted or marked a second time.
            foreach (var computed in nodes)
            {
                if (settledChecks.ContainsKey(computed.Id)
                    || ConsultCheckSettlement.RecordedOutcomeOf(computed, outputs) is not { } recorded)
                {
                    continue;
                }

                settledChecks[computed.Id] = recorded;
                progressed = true;
            }

            return progressed;
        }

        // v25 (B8 #1027): the one release of a held deliverable — every node
        // its macros read settled, its check (if any) settled — composing the
        // document on a pass, recording the third state on a fail.
        async Task TryReleaseDeliverablesAsync()
        {
            foreach (var (resultId, pending) in pendingDeliverables.ToList())
            {
                if (!ConsultCheckSettlement.IsReleaseReady(pending.Deliverable, outputs, settledChecks, out var outcome,
                    skippedNodes, composedDeliverables.Keys.ToHashSet(StringComparer.Ordinal), RunDeliverableIds()))
                {
                    continue;
                }

                pendingDeliverables.Remove(resultId);

                if (outcome is null || outcome.Passed)
                {
                    await CompleteDeliverableAsync(pending.Deliverable, pending.SourceRefs, pending.Parts);
                    continue;
                }

                var failed = new ConsultFailedDocument(
                    resultId,
                    pending.Deliverable.Label ?? resultId,
                    ConsultCheckSettlement.FailWithOf(nodesById[pending.Deliverable.CheckNodeId!]),
                    outcome.Uncovered,
                    outcome.Untested,
                    // v26 (#1155): the composed text's hash, refused or not.
                    ComposedHash: composedDeliverables.GetValueOrDefault(resultId)?.ComposedHash);
                failedDocuments.Add(failed);
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.RecordFailedDocument),
                    failed);
            }
        }

        // Aggregators compose inline once every source settles — deterministic,
        // no activity, no retries; a failed contributing item fails the aggregator
        // loud (never a partial document), cascading downstream by absence
        // (package-format-v6-design.md §§ 3, 6).
        async Task TryCompleteAggregatorsAsync()
        {
            await ApplyLoopBoundsAsync();
            var progressed = true;

            while (progressed)
            {
                progressed = false;
                progressed |= await TrySettleChecksAsync();
                await TryReleaseDeliverablesAsync();

                foreach (var aggregator in aggregators)
                {
                    if (outputs.ContainsKey(aggregator.Id) || failedAggregators.ContainsKey(aggregator.Id))
                    {
                        continue;
                    }

                    var parts = new List<ConsultAggregateRenderer.Part>();
                    var sourceHashes = new List<string>();
                    var failedContributors = new List<string>();
                    var allSettled = true;

                    foreach (var sourceRef in aggregator.Aggregate!)
                    {
                        var sourceId = TemplateObjectReference.NodeIdOf(sourceRef) ?? sourceRef;
                        var source = nodesById[sourceId];

                        if (source.ForEach != null)
                        {
                            var sourceItems = FanItems(source);
                            var failed = sourceItems
                                .Where(item => failedItems.Contains(FailedKey(source, WorkflowItemPath.OfItem(item)!)))
                                .Select(item => WorkflowItemPath.OfItem(item)!)
                                .ToList();

                            if (failed.Count > 0)
                            {
                                failedContributors.AddRange(failed.Select(id => WorkflowItemPath.Key(sourceId, id)));
                                continue;
                            }

                            if (!sourceItems.All(item => outputs.ContainsKey(ConsultNodeScheduler.InstanceKey(sourceId, WorkflowItemPath.OfItem(item)!))))
                            {
                                allSettled = false;
                                continue;
                            }

                            parts.Add(new ConsultAggregateRenderer.ForEachPart(sourceItems
                                .Select(item => (
                                    WorkflowItemPath.OfItem(item)!,
                                    item.GetValueOrDefault("name", WorkflowItemPath.OfItem(item)!),
                                    outputs[ConsultNodeScheduler.InstanceKey(sourceId, WorkflowItemPath.OfItem(item)!)].RawOutput.Trim()))
                                .ToList()));
                            sourceHashes.AddRange(sourceItems
                                .Select(item => outputs[ConsultNodeScheduler.InstanceKey(sourceId, WorkflowItemPath.OfItem(item)!)].OutputHash));
                        }
                        else if (failedAggregators.ContainsKey(sourceId))
                        {
                            failedContributors.Add(sourceId);
                        }
                        else if (outputs.TryGetValue(sourceId, out var sourceResult))
                        {
                            parts.Add(new ConsultAggregateRenderer.ScalarPart(sourceResult.RawOutput.Trim()));
                            sourceHashes.Add(sourceResult.OutputHash);
                        }
                        else
                        {
                            allSettled = false;
                        }
                    }

                    if (failedContributors.Count > 0)
                    {
                        failedAggregators[aggregator.Id] =
                            $"{aggregator.Label} could not assemble: {string.Join(", ", failedContributors)} did not complete.";
                        progressed = true;
                        continue;
                    }

                    if (!allSettled)
                    {
                        continue;
                    }

                    var rendered = ConsultAggregateRenderer.Render(parts);
                    // v25 (B5 #1004, design § 6): a 25 aggregator stamps 6 like
                    // every 25 node; its render is its reply, so the two digests
                    // are one (an aggregator carries no instance).
                    var aggregateHash = ConsultGenerationProvenance.Sha256Hex(rendered);
                    var aggregateResult = new NodeRunResult(
                        rendered,
                        null,
                        ConsultGenerationProvenance.ComputeAggregateInputHash(sourceHashes),
                        aggregateHash,
                        at25 ? ConsultGenerationProvenance.NodeHashVersionAt25 : ConsultGenerationProvenance.NodeHashVersion,
                        AgentOutputHash: at25 ? aggregateHash : null);
                    outputs[aggregator.Id] = aggregateResult;
                    completedNodeCount++;
                    progressed = true;

                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                        new ConsultGenerationNodeUpdate(
                            aggregator.Id, aggregator.Label, null,
                            aggregateResult.InputHash, aggregateResult.OutputHash,
                            completedNodeCount, totalNodeCount, aggregateResult.HashVersion,
                            AgentOutputHash: aggregateResult.AgentOutputHash));

                    if (v7)
                    {
                        // Result nodes are distinct per the validator: at most one
                        // deliverable owns this aggregator.
                        var deliverable = deliverables.FirstOrDefault(d => string.Equals(d.NodeId, aggregator.Id, StringComparison.Ordinal));
                        if (deliverable != null)
                        {
                            // v12 #624 (§ 13): the settle/record split — the
                            // aggregator settled and hashed above exactly as
                            // ever; the document itself waits for its check
                            // and (B8 #1027) its reads, released below on this
                            // same pass when nothing holds it.
                            pendingDeliverables[deliverable.ResultId!] = (deliverable, aggregator.Aggregate!, parts);
                        }
                    }
                    else if (string.Equals(aggregator.Id, resultNodeId, StringComparison.Ordinal))
                    {
                        await context.Entities.CallEntityAsync(
                            entityId,
                            nameof(ConsultGenerationJobEntity.CompleteDocument),
                            rendered);
                    }
                }

                // v26 (#1154, design § 1.3): the flow nodes settle on the same
                // pass — a loop as the aggregator's bytes over its returned
                // node's items, an if/case as its taken body's returned node.
                // A loop's row carries the aggregator's ladder; the alias row
                // carries none (its output is the returned node's, recorded
                // there). An if/case a deliverable sources is one block,
                // completed here; a loop's blocks complete item by item.
                // The deliverable's composition from its sources is #1155.
                // v27 (#1235): an if/case inside a loop completes PER ITEM as the branch
                // it took did — the alias flow:item, once the taken branch's returned
                // node has its output for the item; a skipped item has no alias
                // (#1246: the one alias writer, shared with the summary pass).
                progressed |= inner.AliasTakenRets(outputs, FanItems);

                // v30 (#1341, design § 2.3): a nested loop composes PER INSTANCE — the outer
                // item's list of its returned node — into loop:<outer path>, which the loop
                // above reads as that item's text (its ret is this loop).
                foreach (var loop in nestedLoops)
                {
                    foreach (var outer in FanItems(loop))
                    {
                        var outerPath = WorkflowItemPath.OfItem(outer)!;
                        var key = ConsultNodeScheduler.InstanceKey(loop.Id, outerPath);
                        if (outputs.ContainsKey(key) || failedItems.Contains(FailedKey(loop, outerPath)) || loopInstances.Get(loop.Id, outerPath) is not { Failed: false } instance)
                        {
                            continue;
                        }

                        var composedInstance = ConsultFlowCompletion.TryCompose(loop, nodesById, outputs, _ => instance.Items, (node, itemId) => failedItems.Contains(FailedKey(node, itemId)), inner.SkippedInstances);
                        if (composedInstance is null)
                        {
                            continue;
                        }

                        progressed = true;
                        if (composedInstance.Failure is not null)
                        {
                            failedItems.Add(FailedKey(loop, outerPath));
                            continue;
                        }

                        outputs[key] = composedInstance.Result!;

                        // v30 (#1342 B3c): an instance with an until or a max that ran out of items says so.
                        if (Stoppable(loop) && !loopStops.ContainsKey(key))
                        {
                            loopStops[key] = "end";
                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.StopLoop),
                                new ConsultGenerationLoopStopped(loop.Id, loop.Label, "end", "it ran out of items", Array.Empty<ConsultNotRunItem>(), Path: outerPath));
                        }
                    }
                }

                foreach (var flow in flowNodes)
                {
                    if (outputs.ContainsKey(flow.Id) || failedAggregators.ContainsKey(flow.Id) || skippedNodes.Contains(flow.Id) || AwaitsCount(flow))
                    {
                        continue;
                    }

                    NodeRunResult flowResult;
                    if (flow.Kind == ConsultNodeFlows.While)
                    {
                        // v30 (#1342, design § 3.3): a stopped while is its last pass's ret —
                        // one block; its body nodes' rows complete beside it.
                        if (!whileStops.TryGetValue(flow.Id, out var stop) || stop.Passes == 0)
                        {
                            continue;
                        }

                        progressed = true;
                        flowResult = outputs[ConsultNodeScheduler.InstanceKey(flow.Ret!, PassPath(null, stop.Passes))];
                        // (B3d: and the bodies of the whiles inside it.)
                        IEnumerable<string> BodiesWithin(ConsultNodeDescriptor repeating) =>
                            BodyOf(repeating).SelectMany(bodyId => nodesById[bodyId].Kind == ConsultNodeFlows.While
                                ? BodiesWithin(nodesById[bodyId]).Prepend(bodyId)
                                : new[] { bodyId });
                        foreach (var bodyId in BodiesWithin(flow))
                        {
                            completedNodeCount++;
                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                                new ConsultGenerationNodeUpdate(bodyId, nodesById[bodyId].Label, null, null, null, completedNodeCount, totalNodeCount));
                        }
                    }
                    else
                    {
                        var composed = ConsultFlowCompletion.TryCompose(flow, nodesById, outputs, FanItems, (node, itemId) => failedItems.Contains(FailedKey(node, itemId)), inner.SkippedInstances);
                        if (composed is null)
                        {
                            continue;
                        }

                        progressed = true;

                        if (composed.Failure is { } failure)
                        {
                            failedAggregators[flow.Id] = failure;
                            continue;
                        }

                        flowResult = composed.Result!;
                    }
                    outputs[flow.Id] = flowResult;
                    completedNodeCount++;

                    var isLoop = flow.Kind == ConsultNodeFlows.For;
                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                        new ConsultGenerationNodeUpdate(
                            flow.Id, flow.Label, null,
                            isLoop ? flowResult.InputHash : null, isLoop ? flowResult.OutputHash : null,
                            completedNodeCount, totalNodeCount, isLoop ? flowResult.HashVersion : null,
                            AgentOutputHash: isLoop ? flowResult.AgentOutputHash : null));

                    if (isLoop)
                    {
                        // v30 (#1342, design § 3.4): a for with an until or a max that ran out of items says so.
                        // (A nested loop's instances say so on their own rows, B3c.)
                        if (Stoppable(flow) && ConsultNodeScheduler.LoopDepthOf(flow, nodesById) == 0 && !loopStops.ContainsKey(flow.Id))
                        {
                            loopStops[flow.Id] = "end";
                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.StopLoop),
                                new ConsultGenerationLoopStopped(flow.Id, flow.Label, "end", "it ran out of items", Array.Empty<ConsultNotRunItem>()));
                        }

                        continue;
                    }

                    foreach (var deliverable in deliverables)
                    {
                        if (deliverable.SourceIds.Contains(flow.Id))
                        {
                            completedBlockCount++;

                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.CompleteBlock),
                                new BlockGenerationResult($"{deliverable.BlockPrefix}{flow.Id}", flow.Label, true, flowResult.RawOutput.Trim(), null));
                        }
                    }
                }

                // v26 (#1155, design § 3): the deliverable's compose step — once
                // every declared source has settled (a loop's row, an if/case's
                // alias, a prose node), the parts in sources[] order through the
                // aggregator's renderer and ladder; stashed for the release, which
                // waits on the reads and the check as it did. No entity op here:
                // the hash is stamped on the document at release.
                if (deliverablesFinal)
                {
                    foreach (var deliverable in deliverables)
                    {
                        if (deliverable.Sources is null || deliverable.ResultId is null
                            || composedDeliverables.ContainsKey(deliverable.ResultId) || failedDeliverables.ContainsKey(deliverable.ResultId))
                        {
                            continue;
                        }

                        var composedDeliverable = ConsultDeliverableComposition.TryCompose(
                            deliverable, nodesById, outputs, FanItems, (node, itemId) => failedItems.Contains(FailedKey(node, itemId)),
                            // v30 (#1342): and the whiles that ran no pass.
                            whileStops.Any(stop => stop.Value.Passes == 0)
                                ? inner.SkippedInstances.Concat(whileStops.Where(stop => stop.Value.Passes == 0).Select(stop => stop.Key)).ToHashSet(StringComparer.Ordinal)
                                : inner.SkippedInstances);
                        if (composedDeliverable is null)
                        {
                            continue;
                        }

                        progressed = true;

                        if (composedDeliverable.Failure is { } failure)
                        {
                            failedDeliverables[deliverable.ResultId] = failure;
                            continue;
                        }

                        composedDeliverables[deliverable.ResultId] = composedDeliverable;
                        pendingDeliverables[deliverable.ResultId] = (deliverable, deliverable.Sources, composedDeliverable.Parts!);

                        // v29 (#1305, design § 4): the row is written now — composed, held on
                        // its reads and check until the release moves it to the documents.
                        if (at29)
                        {
                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.MarkDeliverableComposed),
                                new ConsultHeldDocument(
                                    deliverable.ResultId, deliverable.Label ?? deliverable.ResultId, deliverable.Ordinal,
                                    composedDeliverable.ComposedHash, deliverable.Sources, context.CurrentUtcDateTime));
                        }
                    }
                }
            }
        }

        // v26 (#1154, design § 1.3, § 1.5): an if/case that took no branch is
        // skipped — its row says so (the branch "none" rides its descriptor),
        // a reader treats it as settled and reads nothing. Judged once the
        // graph is final: at start for a non-deciding job, after the boundary
        // for a deciding one (stage one holds no decided flow).
        async Task MarkSkippedFlowsAsync()
        {
            foreach (var flow in flowNodes)
            {
                if (flow.Branch != WorkflowFlowDecision.None || !skippedNodes.Add(flow.Id))
                {
                    continue;
                }

                completedNodeCount++;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeSkipped),
                    new ConsultGenerationNodeSkip(flow.Id, flow.Label, "took no branch", completedNodeCount, totalNodeCount));
            }
        }

        // The ready loop, run to quiescence: once for every job, and (v10,
        // #496) once more for a deciding job — the classifier closure first,
        // then, after the boundary rebinds the graph, the rest. False when the
        // job failed inside it and the orchestration must return.
        async Task<bool> RunToQuiescenceAsync()
        {
        await ResolveNestedInstancesAsync();
        StartReadyInstances();

        while (pendingTasks.Count > 0 || pendingDecisions.Count > 0)
        {
            var finished = await Task.WhenAny(pendingTasks.Keys.Cast<Task>().Concat(pendingDecisions.Keys));

            // v27 (#1235): a per-item decision came back — settle it, then look again.
            if (finished is Task<ConsultFlowItemDecision> decided)
            {
                var (flowId, decidedItemId) = pendingDecisions[decided];
                pendingDecisions.Remove(decided);
                if (flowId.StartsWith(ItemGatePrefix, StringComparison.Ordinal))
                {
                    await SettleItemGateAsync(flowId[ItemGatePrefix.Length..], decidedItemId, await decided);
                }
                else if (flowId.StartsWith(UntilPrefix, StringComparison.Ordinal))
                {
                    await SettleUntilAsync(flowId[UntilPrefix.Length..], decidedItemId, await decided);
                }
                else if (flowId.StartsWith(WhilePrefix, StringComparison.Ordinal))
                {
                    await SettleWhileCheckAsync(flowId[WhilePrefix.Length..], decidedItemId, await decided);
                }
                else
                {
                    await SettleDecisionAsync(flowId, decidedItemId, await decided);
                }
                await ResolveNestedInstancesAsync();
                await TryCompleteAggregatorsAsync();
                PublishStatus(ConsultGenerationJobStatuses.Running);
                StartReadyInstances();
                continue;
            }

            var completedTask = (Task<NodeRunResult>)finished;
            var (nodeId, itemId) = pendingTasks[completedTask];
            pendingTasks.Remove(completedTask);
            var node = nodesById[nodeId];

            if (itemId is null)
            {
                // Scalar node: an activity failure (post-retries) is a runtime job
                // failure, and an empty declared-required output fails the job with
                // the node's message — unchanged semantics from the wave interpreter.
                // #639: caught HERE so the failed node gets its row and its stack —
                // before this, the exception fell to the outer catch and the job
                // failed with no failed node row at all.
                NodeRunResult result;

                try
                {
                    result = await completedTask;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var (errorType, errorStack) = FailureFactsOf(ex);

                    // v25 (B5 #1004): an empty reply under failIfEmpty is judged in
                    // the activity (on the reply, before anything composes) and
                    // fails the node exactly as the ≤24 check below does — the
                    // author's sentence, never an engine message.
                    if (node.Format is not null && node.FailIfEmpty != null && ConsultNodeCompletion.IsEmptyReplyFailure(errorType))
                    {
                        await FailNodeAsync(
                            context, entityId, node, nodes, outputs,
                            totalBlockCount, completedBlockCount, failedBlockCount, logger);
                        return false;
                    }

                    var skippedNodes = nodes
                        .Where(other => other.Id != nodeId && !outputs.ContainsKey(other.Id))
                        .Select(other => new ConsultItemStepDescriptor(other.Id, other.Label))
                        .ToList();
                    logger.LogWarning(ex, "Consult generation node activity failed. JobId={JobId}, NodeId={NodeId}", context.InstanceId, nodeId);
                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.MarkNodeFailed),
                        new ConsultGenerationNodeFailure(
                            nodeId, node.Label, $"{nodeId}-failed", $"{node.Label} failed: {ex.Message}", skippedNodes,
                            errorType, errorStack));
                    PublishStatus(ConsultGenerationJobStatuses.Failed);
                    return false;
                }

                // Below 25 "empty" is "no concepts", judged here; a 25 node judged
                // its reply in the activity (B5 #1004).
                if (node.Format is null && node.FailIfEmpty != null && (result.Concepts?.Count ?? 0) == 0)
                {
                    await FailNodeAsync(
                        context, entityId, node, nodes, outputs,
                        totalBlockCount, completedBlockCount, failedBlockCount, logger);
                    return false;
                }

                outputs[nodeId] = result;
                completedNodeCount++;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                    NodeUpdateFrom(node, result, completedNodeCount, totalNodeCount));

                // A scalar source of a result aggregator is one block per owning
                // deliverable (v6: the single empty-prefix entry; v5: no entries).
                foreach (var deliverable in deliverables)
                {
                    if (deliverable.SourceIds.Contains(nodeId))
                    {
                        completedBlockCount++;

                        await context.Entities.CallEntityAsync(
                            entityId,
                            nameof(ConsultGenerationJobEntity.CompleteBlock),
                            new BlockGenerationResult($"{deliverable.BlockPrefix}{nodeId}", node.Label, true, result.RawOutput.Trim(), null));
                    }
                }

                // v29 (#1303, design § 1.2): the loops this node's value feeds settle now.
                if (!await ResolveLoopsFedByAsync(node, result))
                {
                    return false;
                }
            }
            else if (WhileOf(node) is { } whileOwner)
            {
                // v30 (#1342, design § 3.3): a pass instance — recorded and hashed as an
                // item's is; once the pass settles, the ready loop asks the check.
                try
                {
                    var passResult = await completedTask;
                    outputs[ConsultNodeScheduler.InstanceKey(nodeId, itemId)] = passResult;
                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.MarkNodeItemCompleted),
                        ItemUpdateFrom(node, itemId, $"Pass {itemId}", passResult, 0, 0) with { Pass = true });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "A while's pass could not finish. JobId={JobId}, NodeId={NodeId}, Pass={Pass}", context.InstanceId, nodeId, itemId);
                    var failedPass = PassOfPath(itemId);
                    await FailWhileAsync(whileOwner, failedPass?.Outer, $"{whileOwner.Label} could not finish pass {failedPass?.Number}: {node.Label} failed: {ex.Message}");
                }
            }
            else
            {
                // forEach instance: failures are per-item — the section fails, the
                // job continues (the section-failure semantics the map era had).
                NodeRunResult result;

                try
                {
                    result = await completedTask;
                }
                catch (Exception ex)
                {
                    var (itemErrorType, itemErrorStack) = FailureFactsOf(ex);

                    // v25 (B5 #1004): the item's empty reply, judged in the activity,
                    // fails the item with the author's sentence as the ≤24 check does.
                    if (node.Format is not null && node.FailIfEmpty != null && ConsultNodeCompletion.IsEmptyReplyFailure(itemErrorType))
                    {
                        await FailItemAsync(node, itemId, node.FailIfEmpty);
                        PublishStatus(ConsultGenerationJobStatuses.Running);
                        StartReadyInstances();
                        continue;
                    }

                    await FailItemAsync(node, itemId, $"{node.Label} failed: {ex.Message}",
                        itemErrorStack is null ? null : $"{itemErrorType}\n{itemErrorStack}");
                    PublishStatus(ConsultGenerationJobStatuses.Running);
                    StartReadyInstances();
                    continue;
                }

                if (node.Format is null && node.FailIfEmpty != null && (result.Concepts?.Count ?? 0) == 0)
                {
                    await FailItemAsync(node, itemId, node.FailIfEmpty);
                    PublishStatus(ConsultGenerationJobStatuses.Running);
                    StartReadyInstances();
                    continue;
                }

                outputs[ConsultNodeScheduler.InstanceKey(nodeId, itemId)] = result;

                var chain = v6
                    ? chainNodes.Where(chainNode => string.Equals(CollectionIdOf(chainNode), CollectionIdOf(node), StringComparison.Ordinal)).ToList()
                    : chainNodes;
                var completedChainCount = chain.Count(chainNode =>
                    outputs.ContainsKey(ConsultNodeScheduler.InstanceKey(chainNode.Id, itemId)));

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeItemCompleted),
                    ItemUpdateFrom(node, itemId, ItemName(node, itemId), result, completedChainCount, chain.Count));

                if (!v6 && nodeId == resultNodeId)
                {
                    completedBlockCount++;

                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.CompleteBlock),
                        new BlockGenerationResult(itemId, ItemName(node, itemId), true, result.RawOutput.Trim(), null));
                }
                else
                {
                    // v6/v7 blocks stream as each deliverable's sources complete
                    // (v5 has no deliverable entries, so this arm is a no-op there).
                    foreach (var deliverable in deliverables)
                    {
                        // v27 (#1235): the taken ret of an inner if/case completes the block of
                        // the loop returning that flow (the deliverable's completion key).
                        // v30 (#1341): the innermost loop's ret completes that loop's block, named by
                        // its path, for a deliverable sourcing the top of the ret chain.
                        if (NestedBlockOf(node) is { } nestedBlock && deliverable.Sources?.Contains(nestedBlock.Top, StringComparer.Ordinal) == true)
                        {
                            completedBlockCount++;

                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.CompleteBlock),
                                new BlockGenerationResult(WorkflowItemPath.BlockId(deliverable.BlockPrefix, nestedBlock.Loop, itemId), ItemName(node, itemId), true, result.RawOutput.Trim(), null));
                            continue;
                        }

                        if (deliverable.SourceIds.Contains(nodeId)
                            || (inner.FlowOf(node) is { } flowOf && inner.IsTakenRet(node, itemId) && deliverable.SourceIds.Contains(flowOf.Id)))
                        {
                            completedBlockCount++;

                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.CompleteBlock),
                                new BlockGenerationResult(WorkflowItemPath.BlockId(deliverable.BlockPrefix, BlockNodeOfItem(node, itemId), itemId), ItemName(node, itemId), true, result.RawOutput.Trim(), null));
                        }
                    }
                }

                await MarkFullyCompletedChainNodesAsync();
            }

            await ResolveNestedInstancesAsync();
            await TryCompleteAggregatorsAsync();
            PublishStatus(ConsultGenerationJobStatuses.Running);
            StartReadyInstances();
        }

        return true;
        }

        if (deciding)
        {
            // Stage one: the classifier closure alone. A failure here ends the
            // job in the deciding stage — born Failed the way #434's record is,
            // never a FinalizeJob over a skeleton that does not exist.
            nodes = ClassifierClosure();
            nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            chainNodes = nodes.Where(node => node.ForEach != null).ToList();
            aggregators = nodes.Where(node => node.Aggregate != null).ToList();
            flowNodes = nodes.Where(node => node.Kind is not null).ToList();
            inner = new ConsultInnerFlows(nodesById);
            totalNodeCount = allNodes.Count;

            try
            {
                if (!await RunToQuiescenceAsync())
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var failedNodeId = pendingTasks.Values.Select(pending => pending.NodeId).FirstOrDefault() ?? "classifier";
                var detail = ex is Microsoft.DurableTask.TaskFailedException failed && failed.FailureDetails?.ErrorType?.EndsWith(nameof(ClassificationOutputContractException), StringComparison.Ordinal) == true
                    ? failed.FailureDetails.ErrorMessage
                    : $"Classifier '{failedNodeId}' failed ({ex.GetType().Name}).";
                logger.LogWarning(ex, "Could not decide what to produce. JobId={JobId}", context.InstanceId);
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.RecordDecisionFailure),
                    new ConsultGenerationDecisionFailure(
                        ConsultGenerationDecisionFailureKinds.CouldNotDecide,
                        detail,
                        Classifications(),
                        null,
                        FailureStack: FailureFactsOf(ex) is var (decisionType, decisionStack) && decisionStack != null
                            ? $"{decisionType}\n{decisionStack}"
                            : null));
                PublishStatus(ConsultGenerationJobStatuses.Failed);
                return;
            }

            // The boundary: every condition, once, over the inputs and the
            // answers; the skeleton and the pruned graph come back with it.
            var answers = Classifications();
            var decision = await context.CallActivityAsync<ConsultDecisionResult>(
                ConsultGenerationActivityNames.DecideDeliverables,
                new ConsultDecideActivityInput(input.WorkflowPackage!, input.SuppliedInputs, answers,
                    // v25 (B8 #1027): the effective map, for the boundary's re-describe.
                    EffectiveInputs: at25 ? new Dictionary<string, string>(effectiveInputs, StringComparer.Ordinal) : null,
                    // v25 (#1038): the render scope's other halves — every scalar
                    // stage-one output (a fanned node has none) and the facts.
                    NodeTexts: at25 ? ScalarNodeTexts() : null,
                    Run: at25 ? ConsultRenderPayload.RunFactsFor(context.CurrentUtcDateTime, context.InstanceId, input.WorkflowPackage, input.ApiHost) : null,
                    Profile: at25 ? ConsultRenderPayload.ProfileFactsFor(input.ProfileName, input.Signature) : null),
                AgentActivityRetryOptions);

            if (decision.Results.Count == 0)
            {
                var reason = decision.EmptyFanLabels.Count > 0
                    ? ConsultGenerationJobStarter.EmptyFanDetail(decision.EmptyFanLabels)
                    : "No document applies after classification. " + string.Join(" ", decision.Skipped.Select(s => $"'{s.Label}' {s.Reason}."));
                logger.LogWarning("No deliverable applies after classification. JobId={JobId}, Declared={Declared}", context.InstanceId, decision.Skipped.Count);
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.RecordDecisionFailure),
                    new ConsultGenerationDecisionFailure(
                        ConsultGenerationDecisionFailureKinds.NothingApplied,
                        reason,
                        answers,
                        decision.Skipped));
                PublishStatus(ConsultGenerationJobStatuses.Failed);
                return;
            }

            // v12 #618 (design § 3): the boundary's descriptors filter with
            // the choices resolved at start — DecideActivity never sees the
            // request, so the map rides the orchestration input.
            var decidedResults = ConsultGenerationJobStarter.FilterDescriptorMacros(decision.Results, input.MacroChoices);

            await context.Entities.CallEntityAsync(
                entityId,
                nameof(ConsultGenerationJobEntity.Decide),
                new ConsultGenerationDecision(
                    decision.Items,
                    decision.Nodes,
                    decidedResults,
                    decision.Skipped.Count > 0 ? decision.Skipped : null,
                    decision.CollectionRosters,
                    decision.ItemSteps,
                    answers,
                    context.CurrentUtcDateTime,
                    decision.ExcludedMacros));

            // Stage two: the graph as decided. The classifiers' outputs stay
            // bindable; nothing recorded is recomputed.
            nodes = decision.Nodes;
            nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            chainNodes = nodes.Where(node => node.ForEach != null).ToList();
            aggregators = nodes.Where(node => node.Aggregate != null).ToList();
            flowNodes = nodes.Where(node => node.Kind is not null).ToList();
            inner = new ConsultInnerFlows(nodesById);
            results = decidedResults;
            items = decision.Items;
            collections = decision.CollectionSets;
            v6 = collections is { Count: > 0 } || unresolvedLoops.Count > 0;
            deliverables = ConsultDeliverables.Resolve(results, v6 ? resultNodeId : null, nodesById);
            deliverablesFinal = true;
            totalBlockCount = items.Count;
            totalNodeCount = nodes.Count;
            skippedForReply = decision.Skipped.Count > 0 ? decision.Skipped : null;
        }

        await MarkSkippedFlowsAsync();

        if (!await RunToQuiescenceAsync())
        {
            return;
        }

        IReadOnlyDictionary<string, string> Classifications() => outputs
            .Where(pair => pair.Value.Classification != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Classification!, StringComparer.Ordinal);

        // v25 (#1038): the completed scalar outputs by node id — an item-keyed
        // instance (a fanned node's) has no one text a deliverable's when could read.
        IReadOnlyDictionary<string, string> ScalarNodeTexts() => outputs
            .Where(pair => !pair.Key.Contains(':') && nodesById.ContainsKey(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Classification ?? pair.Value.RawOutput, StringComparer.Ordinal);

        // v23 #955: the node's macro blocks, resolved when its activity is
        // dispatched — each macro's `when` judged (input: against the supplied
        // map, node: against the classifications the scheduler guarantees are in
        // by now), then the firing macro's text token-substituted. before blocks
        // precede the prompt, after blocks follow it; the renderer composes them.
        // v24 #957: resolved per INSTANCE — a fan node's item is in scope, so a
        // forItem macro composes for that item alone, an item:id when is judged
        // against it, and {{item:…}} tokens fill from its fields. The per-item
        // prompt is attested by the instance's own InputHash, as every instance is.
        (IReadOnlyList<string>?, IReadOnlyList<string>?) ResolveNodeMacros(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item)
        {
            if (node.Macros is not { Count: > 0 })
            {
                return (null, null);
            }

            List<string>? before = null;
            List<string>? after = null;

            foreach (var macro in node.Macros)
            {
                if (macro.ForItem != null && macro.ForItem != item?.GetValueOrDefault("id"))
                {
                    continue;
                }

                if (macro.When != null
                    && (!WorkflowResultConditions.TryParseExpression(macro.When, out var when, out _)
                        || !WorkflowResultConditions.Holds(when, suppliedInputs, Classifications(), item)))
                {
                    continue;
                }

                if (input.MacroTexts is null || !input.MacroTexts.TryGetValue(macro.Id, out var template))
                {
                    continue;
                }

                var expanded = ConsultMacroExpander.Expand(
                    template, effectiveInputs, input.DataScalars, Classifications(), nodeMacroFacts, item);
                (macro.After ? (after ??= new()) : (before ??= new())).Add(expanded);
            }

            return (before, after);
        }

        // v27 (#1235): a branch node's instance its flow node decided against never starts.
        var expectedInstances = nodes.Where(node => node.Aggregate is null && node.Check is null && node.Kind is null).Sum(node =>
            // v30 (#1342): a while's body node runs once per pass each of its instances opened.
            WhileOf(node) is { } whileOwner ? OuterPathsOf(whileOwner).Sum(outer => passesOpened.GetValueOrDefault(WhileKey(whileOwner, outer)))
            : node.ForEach is null
            ? 1
            : FanItems(node).Count(item => !failedItems.Contains(FailedKey(node, WorkflowItemPath.OfItem(item)!)) && !inner.Untaken(node, WorkflowItemPath.OfItem(item)!)
                // v29 (#1304): an item its folder gate excluded never starts.
                && !inner.SkippedInstances.Contains(ConsultNodeScheduler.InstanceKey(node.Id, WorkflowItemPath.OfItem(item)!))));
        if (started.Count(key => outputs.ContainsKey(key)) < expectedInstances && failedItems.Count == 0)
        {
            // Unreachable for validated packages (the graph is acyclic and complete);
            // defensive so a bad snapshot fails loud instead of hanging.
            throw new InvalidOperationException("Workflow nodes could not all be scheduled; the node graph is not executable.");
        }

        // A final pass settles aggregator failure state for the job outcome.
        await TryCompleteAggregatorsAsync();

        string finalStatus;
        string? finalError = null;

        if (v7)
        {
            // Completed requires every declared deliverable produced; the error
            // is the first missing deliverable's, by result-set order
            // (package-format-v7.md).
            // v29 (#1305, design § 4): a deliverable composed and never released was
            // held — named, with why, on the record and in the outcome (26–28 reach
            // the reply leg with it and throw there).
            Dictionary<string, string>? held = null;
            if (at29)
            {
                held = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (resultId, pending) in pendingDeliverables.OrderBy(pair => pair.Value.Deliverable.Ordinal))
                {
                    var reason = ConsultCheckSettlement.HeldReason(
                        pending.Deliverable, outputs, settledChecks, SkippedKeys(),
                        composedDeliverables.Keys.ToHashSet(StringComparer.Ordinal), RunDeliverableIds());
                    var label = pending.Deliverable.Label ?? resultId;
                    held[resultId] = $"{label} was composed but held: {reason}.";

                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.MarkDeliverableComposed),
                        new ConsultHeldDocument(
                            resultId, label, pending.Deliverable.Ordinal,
                            composedDeliverables.GetValueOrDefault(resultId)?.ComposedHash, pending.Deliverable.Sources,
                            context.CurrentUtcDateTime, reason));
                }
            }

            (finalStatus, finalError) = ConsultDeliverables.FinalOutcome(deliverables, outputs, failedAggregators, failedDocuments,
                composedDeliverables.Keys.ToHashSet(StringComparer.Ordinal), failedDeliverables,
                releasedDeliverables: at29
                    ? recordedTexts.Keys.Concat(failedDocuments.Select(d => d.ResultId)).ToHashSet(StringComparer.Ordinal)
                    : null,
                heldDeliverables: held);
        }
        else if (v6)
        {
            // The deliverable is the assembled document: no document, no consult
            // (fail-loud all the way to the job, package-format-v6-design.md § 3).
            var documentProduced = outputs.ContainsKey(resultNodeId!);
            finalStatus = documentProduced
                ? ConsultGenerationJobStatuses.Completed
                : ConsultGenerationJobStatuses.Failed;
            finalError = documentProduced
                ? null
                : failedAggregators.GetValueOrDefault(resultNodeId!)
                    ?? failedAggregators.Values.FirstOrDefault()
                    ?? "The assembled document could not be produced.";
        }
        else
        {
            finalStatus = completedBlockCount > 0
                ? ConsultGenerationJobStatuses.Completed
                : ConsultGenerationJobStatuses.Failed;
        }

        logger.LogInformation(
            "ConsultGenerationOrchestrator completed. JobId={JobId}, Status={Status}, Completed={CompletedSectionCount}, Failed={FailedSectionCount}, Total={TotalSectionCount}",
            context.InstanceId,
            finalStatus,
            completedBlockCount,
            failedBlockCount,
            totalBlockCount);

        await context.Entities.CallEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.FinalizeJob),
            new ConsultGenerationJobFinalize(finalStatus, finalError, input.AccountKind));

        // #159/#217: the completed documents ride into the reply activity so
        // each can be attached as an encrypted PDF when the account has a
        // delivery password. v7 sends the whole set in result-set order (the
        // outcome gate above guarantees every deliverable produced); v6 sends
        // its single document. Sourced from replayed activity outputs —
        // deterministic.
        // v11 #516: the reply/PDF leg reads the recorded document — the same
        // text CompleteResultDocument stored, appends and all — so the app,
        // History, the PDF and the delivery email are one text (§ 5).
        var replyDocuments = v7 && finalStatus == ConsultGenerationJobStatuses.Completed
            ? ConsultDeliverables.ReplyDocumentsFor(
                deliverables,
                recordedTexts,
                failedDocuments.Count > 0 ? failedDocuments.Select(d => d.ResultId).ToHashSet(StringComparer.Ordinal) : null)
            : null;
        var assembledDocument = !v7 && v6 && outputs.TryGetValue(resultNodeId!, out var resultOutput)
            ? resultOutput.RawOutput
            : null;

        await SendEmailIntakeReplyAsync(context, input with { SkippedDocuments = skippedForReply }, finalStatus, logger, assembledDocument, replyDocuments, failedDocuments.Count > 0 ? failedDocuments : null);

        PublishStatus(finalStatus);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "ConsultGenerationOrchestrator unhandled exception. JobId={JobId}, ExceptionType={ExceptionType}, Message={Message}",
                context.InstanceId,
                ex.GetType().FullName,
                ex.Message);

            try
            {
                var (failureType, failureStack) = FailureFactsOf(ex);
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.FinalizeJob),
                    new ConsultGenerationJobFinalize(
                        ConsultGenerationJobStatuses.Failed, ex.Message, input.AccountKind,
                        FailureStack: failureStack is null ? null : $"{failureType}\n{failureStack}"));
            }
            catch (Exception cleanupEx)
            {
                logger.LogWarning(
                    cleanupEx,
                    "ConsultGenerationOrchestrator FinalizeJob cleanup failed. JobId={JobId}, ExceptionType={ExceptionType}, Message={Message}",
                    context.InstanceId,
                    cleanupEx.GetType().FullName,
                    cleanupEx.Message);
            }

            await SendEmailIntakeReplyAsync(context, input, ConsultGenerationJobStatuses.Failed, logger, assembledDocument: null);

            throw;
        }
    }

    /// <summary>
    /// #158/#157/#486: jobs with a reply address announce their terminal status
    /// by email — the starter's caller decides who gets one by setting the
    /// address (email intake: the sender; app jobs: the account's verified
    /// delivery address, or none). The gate reads only orchestration input
    /// (deterministic on replay); a reply failure never fails a finalized job.
    /// #486: whatever happened is recorded on the entity — sent, failed, or
    /// never attempted and why — so the job can say it.
    /// </summary>
    private static async Task SendEmailIntakeReplyAsync(
        TaskOrchestrationContext context,
        ConsultGenerationOrchestrationInput input,
        string finalStatus,
        ILogger logger,
        string? assembledDocument,
        IReadOnlyList<Email.EmailIntakeReplyDocument>? documents = null,
        IReadOnlyList<ConsultFailedDocument>? failedDocuments = null)
    {
        var delivery = await TrySendReplyAsync(context, input, finalStatus, logger, assembledDocument, documents, failedDocuments);

        try
        {
            await context.Entities.CallEntityAsync(
                new EntityInstanceId(nameof(ConsultGenerationJobEntity), context.InstanceId),
                nameof(ConsultGenerationJobEntity.RecordDelivery),
                delivery with { RecordedAtUtc = context.CurrentUtcDateTime });
        }
        catch (Exception recordEx)
        {
            logger.LogWarning(recordEx, "Delivery could not be recorded. JobId={JobId}", context.InstanceId);
        }
    }

    /// <summary>The reply leg, reduced to what the record needs to say.</summary>
    internal static async Task<ConsultGenerationDeliveryRecord> TrySendReplyAsync(
        TaskOrchestrationContext context,
        ConsultGenerationOrchestrationInput input,
        string finalStatus,
        ILogger logger,
        string? assembledDocument,
        IReadOnlyList<Email.EmailIntakeReplyDocument>? documents,
        IReadOnlyList<ConsultFailedDocument>? failedDocuments = null)
    {
        // #518: the account's choice, made at start, comes before the address:
        // a run the user asked not to be emailed is not-requested whatever the
        // address state, and the activity is never invoked.
        if (input.EmailRequested == false)
        {
            return new ConsultGenerationDeliveryRecord(DeliveryOutcomes.NotRequested, default);
        }

        if (string.IsNullOrWhiteSpace(input.ReplyToAddress))
        {
            return new ConsultGenerationDeliveryRecord(DeliveryOutcomes.AddressNotSet, default);
        }

        try
        {
            var outcome = await context.CallActivityAsync<Email.EmailIntakeReplyOutcome?>(
                Email.SendEmailIntakeReplyActivity.Name,
                new Email.EmailIntakeReplyInput(
                    context.InstanceId,
                    input.ReplyToAddress,
                    finalStatus,
                    input.AppUserId,
                    assembledDocument,
                    documents,
                    input.SkippedDocuments,
                    failedDocuments),
                new TaskOptions(new TaskRetryOptions(new RetryPolicy(3, TimeSpan.FromSeconds(10), 2.0))));

            return DeliveryRecordFor(outcome);
        }
        catch (Exception replyEx)
        {
            logger.LogWarning(
                replyEx,
                "Email intake reply activity failed. JobId={JobId}",
                context.InstanceId);
            return new ConsultGenerationDeliveryRecord(DeliveryOutcomes.Failed, default);
        }
    }

    /// <summary>
    /// A null outcome is a replay of a run whose activity returned nothing
    /// (before #486): it sent, and whether a document rode along is unknown.
    /// </summary>
    /// <summary>
    /// #551: the one seam an activity result crosses into an entity update —
    /// extracted so a test can hold that everything the record keeps (the
    /// hashes, the classification, the tokens) actually rides. The
    /// orchestrator body has no harness; this does.
    /// </summary>
    /// <summary>
    /// #760 (custom tier): a custom node's output is an unattested user shape — the
    /// marker, the schema hash and the content-addressed agent that ran, so the
    /// client can distinguish it from an attested catalog contract. One stamp for
    /// the node update, the item update (#980) and the fan's summary row.
    /// v25 (B7 #1006, provenance@v2026.10.1): the stamp is a 22–24 custom-schema
    /// node's (OutputContract custom). A 25 custom producer carries NONE of it —
    /// the record's nodes[] says producer: custom, formatHash and the catalogue's
    /// generic agent, and the row carries the engine's validation: a custom shape,
    /// engine-validated, never unattested in the #760 sense.
    /// </summary>
    internal static (bool? Unattested, string? CustomSchemaHash, string? CustomAgent) CustomStamp(ConsultNodeDescriptor node)
    {
        var custom = string.Equals(node.OutputContract, OutputContracts.Custom, StringComparison.Ordinal);
        return (
            custom ? true : null,
            custom ? node.CustomSchemaHash : null,
            custom && node.AgentName is not null ? $"{node.AgentName}@{node.AgentVersion}" : null);
    }

    internal static ConsultGenerationNodeUpdate NodeUpdateFrom(
        ConsultNodeDescriptor node, NodeRunResult result, int completedNodeCount, int totalNodeCount)
    {
        var stamp = CustomStamp(node);
        return new(node.Id, node.Label, result.Concepts, result.InputHash, result.OutputHash,
            completedNodeCount, totalNodeCount, result.HashVersion, result.Classification, result.Tokens,
            Check: result.Check,
            NodeStartedAtUtc: result.StartedAtUtc, DurationMs: result.DurationMs,
            Unattested: stamp.Unattested,
            CustomSchemaHash: stamp.CustomSchemaHash,
            CustomAgent: stamp.CustomAgent,
            Validation: result.Validation,
            AgentOutputHash: result.AgentOutputHash,
            Appended: result.Appended,
            Excluded: result.Excluded,
            Producer: LetProducerOf(node));
    }

    /// <summary>v30 (#1343, design § 4.2): a let's row names its producer, computed; every other row none (its bytes unchanged).</summary>
    private static string? LetProducerOf(ConsultNodeDescriptor node) =>
        node.Let is not null ? WorkflowFormatProducers.Computed : null;

    internal static ConsultGenerationNodeItemUpdate ItemUpdateFrom(
        ConsultNodeDescriptor node, string itemId, string itemName, NodeRunResult result, int completedChainCount, int totalChainCount)
    {
        // #980: a fanned custom node's items carry the stamp too — before, only a
        // scalar node's row did, and a fanned one was stamped on no row at all.
        var stamp = CustomStamp(node);
        return new(node.Id, node.Label, itemId, itemName,
            result.Concepts, result.InputHash, result.OutputHash,
            completedChainCount, totalChainCount, result.HashVersion, result.Tokens,
            NodeStartedAtUtc: result.StartedAtUtc, DurationMs: result.DurationMs,
            Unattested: stamp.Unattested,
            CustomSchemaHash: stamp.CustomSchemaHash,
            CustomAgent: stamp.CustomAgent,
            Validation: result.Validation,
            AgentOutputHash: result.AgentOutputHash,
            Appended: result.Appended,
            Excluded: result.Excluded,
            Producer: LetProducerOf(node));
    }

    internal static ConsultGenerationDeliveryRecord DeliveryRecordFor(Email.EmailIntakeReplyOutcome? outcome) =>
        outcome switch
        {
            null => new ConsultGenerationDeliveryRecord(DeliveryOutcomes.Sent, default),
            { Sent: true } => new ConsultGenerationDeliveryRecord(DeliveryOutcomes.Sent, default, outcome.DocumentAttached),
            _ => new ConsultGenerationDeliveryRecord(outcome.Reason ?? DeliveryOutcomes.Failed, default)
        };

    /// <summary>
    /// #639: the failure's identity and frames — Durable's FailureDetails
    /// when the exception crossed an activity boundary, the exception's own
    /// otherwise. Never the message: that keeps its existing discipline.
    /// </summary>
    internal static (string? Type, string? Stack) FailureFactsOf(Exception ex) =>
        ex is Microsoft.DurableTask.TaskFailedException failed && failed.FailureDetails is { } details
            ? (details.ErrorType, details.StackTrace)
            : (ex.GetType().FullName, ex.StackTrace);

    private static async Task FailNodeAsync(
        TaskOrchestrationContext context,
        EntityInstanceId entityId,
        ConsultNodeDescriptor failedNode,
        IReadOnlyList<ConsultNodeDescriptor> nodes,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        int totalBlockCount,
        int completedBlockCount,
        int failedBlockCount,
        ILogger logger)
    {
        // Keeps the "-failed" suffix convention the SSE failure path keys on.
        var analysisStatus = $"{failedNode.Id}-failed";
        var analysisError = failedNode.FailIfEmpty!;

        logger.LogWarning(
            "Consult generation node failed. JobId={JobId}, NodeId={NodeId}, Reason={Reason}",
            context.InstanceId,
            failedNode.Id,
            analysisError);

        // The orchestrator holds the graph, so it computes the unreached set — every
        // node neither completed nor the failed one, in manifest order.
        var skippedNodes = nodes
            .Where(node => node.Id != failedNode.Id && !outputs.ContainsKey(node.Id))
            .Select(node => new ConsultItemStepDescriptor(node.Id, node.Label))
            .ToList();

        await context.Entities.CallEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.MarkNodeFailed),
            new ConsultGenerationNodeFailure(failedNode.Id, failedNode.Label, analysisStatus, analysisError, skippedNodes));

        context.SetCustomStatus(new
        {
            status = ConsultGenerationJobStatuses.Failed,
            totalBlockCount,
            completedBlockCount,
            failedBlockCount,
            analysisStatus,
            analysisError
        });
    }
}

/// <summary>
/// The pure scheduling calculus of the one-kind interpreter: instance identity and
/// per-(node, item) readiness. Kept free of Durable types so the wave semantics are
/// unit-testable (external/consultologist-package-format/package-format-v5.md).
/// </summary>
internal static class ConsultNodeScheduler
{
    /// <summary>Scalar nodes key by id; forEach instances by "nodeId:path" (#1340: the item's path — at one level its id).</summary>
    public static string InstanceKey(string nodeId, string? itemPath)
        => WorkflowItemPath.Key(nodeId, itemPath);

    /// <summary>
    /// #1340 (v30 design § 1.2): a dependency's path for a reader at
    /// <paramref name="readerPath"/> — aligned on the common prefix, as deep as the
    /// dependency runs in loops (none outside every loop).
    /// </summary>
    public static string? AlignedPath(ConsultNodeDescriptor dependency, string? readerPath, IReadOnlyDictionary<string, ConsultNodeDescriptor>? nodesById = null)
        => WorkflowItemPath.Prefix(readerPath, LoopDepthOf(dependency, nodesById));

    /// <summary>
    /// v29 (#1303, design § 1.2): a node in a loop whose count is not yet known — the
    /// loop itself, its body, or a flow node in its body. None of it settles until
    /// the loop resolves: an empty list is not yet a finished loop. v30 (#1340): up
    /// the chain of owners to the root — at ≤29 the shape keeps it at three hops
    /// (body node, inner if/case, loop), so the answer is 29's; guarded against a
    /// body that holds its holder.
    /// </summary>
    public static bool AwaitsCount(
        ConsultNodeDescriptor node,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlySet<string> unresolvedLoops)
    {
        if (unresolvedLoops.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var id = node.Id; seen.Add(id); )
        {
            if (unresolvedLoops.Contains(id))
            {
                return true;
            }

            if (nodesById.GetValueOrDefault(id)?.In is not { } owner)
            {
                return false;
            }

            id = owner;
        }

        return false;
    }

    /// <summary>
    /// #1340: how many loops a node's instances are keyed under — at ≤29 a node
    /// fans or it does not (one level); B2 (#1341) walks the chain of loops.
    /// </summary>
    public static int LoopDepthOf(ConsultNodeDescriptor node) => LoopDepthOf(node, null);

    /// <summary>
    /// #1341 (v30 design § 1.2): how many loops a node's instances are keyed under —
    /// the <c>for</c> rows up its <c>in</c> chain (a body node under its loop; a loop's
    /// own row under the loops around it), cycle-guarded; a ≤25 node fanned by its own
    /// forEach (no <c>in</c>) is one. Without the rows, one level, as at 29.
    /// </summary>
    public static int LoopDepthOf(ConsultNodeDescriptor node, IReadOnlyDictionary<string, ConsultNodeDescriptor>? nodesById)
    {
        if (nodesById is null)
        {
            return node.ForEach is null ? 0 : 1;
        }

        var depth = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal) { node.Id };

        for (var ownerId = node.In; ownerId is not null && seen.Add(ownerId) && nodesById.TryGetValue(ownerId, out var owner); ownerId = owner.In)
        {
            // v30 (#1342): a while's passes key its body as a loop's items do.
            if (owner.Kind is ConsultNodeFlows.For or ConsultNodeFlows.While)
            {
                depth++;
            }
        }

        return depth == 0 && node.ForEach is not null ? 1 : depth;
    }

    /// <summary>
    /// A scalar node is ready when every node: dependency completed; a forEach
    /// instance additionally requires same-collection dependencies item-aligned
    /// (the validator guarantees no cross-collection edges exist). #1065: a
    /// template read of an aggregator is an ordinary dependency — the aggregator
    /// completes into the same outputs map the pass it composes, and a when never
    /// reads one, so the deciding stage's closure needs no aggregate sources.
    /// </summary>
    public static bool InstanceReady(
        ConsultNodeDescriptor node,
        string? itemId,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        // v26 (#1154, design § 1.3): a flow node that took no branch is
        // settled with nothing to read — a reader does not wait on it.
        IReadOnlySet<string>? skipped = null)
    {
        return NodeDependencies(node).All(dependencyId =>
        {
            var key = InstanceKey(dependencyId, AlignedPath(nodesById[dependencyId], itemId, nodesById));
            // v27 (#1235): an if/case inside a loop skipped for the item is settled too (flow:item).
            return skipped?.Contains(dependencyId) == true || skipped?.Contains(key) == true || outputs.ContainsKey(key);
        });
    }

    /// <summary>
    /// v10 (#496) / v25 (#1038): the deciding stage's nodes — the frontier (the
    /// classifiers below 25; what the gating whens read at 25) closed over their
    /// dependencies, in package order.
    /// </summary>
    public static IReadOnlyList<ConsultNodeDescriptor> StageOneClosure(
        IReadOnlyList<string> frontier,
        IReadOnlyList<ConsultNodeDescriptor> allNodes,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById)
    {
        var closure = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(frontier);

        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!closure.Add(id) || !nodesById.TryGetValue(id, out var node))
            {
                continue;
            }

            foreach (var dependency in NodeDependencies(node))
            {
                pending.Enqueue(dependency);
            }
        }

        return allNodes.Where(node => closure.Contains(node.Id)).ToList();
    }

    public static IEnumerable<string> NodeDependencies(ConsultNodeDescriptor node)
    {
        var bindingDeps = (node.Bindings ?? new Dictionary<string, ConsultNodeBindingDescriptor>())
            .Values
            .Where(binding => binding.From.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal))
            .Select(binding => binding.From[WorkflowNodeBindingSources.NodePrefix.Length..]);

        // v23 #955: a node macro gated on a classifier (when: node:<id> …) makes
        // that classifier a dependency too, so the node waits for its answer and
        // the when is judged against a populated classifications map, not an empty
        // one. node: in a when names a classifier (the validator guarantees it),
        // and a classifier always runs, so this never deadlocks.
        // v25 (#1038): a 25 node's Reads already carry what its instances' whens
        // read (the walk's edges), and a 25 when is no ≤24 tree to read leaves from.
        var macroWhenDeps = (node.Reads is not null ? Array.Empty<ConsultNodeMacroDescriptor>() : node.Macros ?? new List<ConsultNodeMacroDescriptor>())
            .Where(macro => macro.When != null)
            .SelectMany(macro =>
                WorkflowResultConditions.TryParseExpression(macro.When, out var when, out _) && when != null
                    ? when.Leaves.Where(leaf => leaf.IsNodeValue).Select(leaf => leaf.NodeId!)
                    : Enumerable.Empty<string>());

        // v24 #957: a macro whose TEXT reads {{classification:x}} needs x answered
        // before it is expanded — the starter snapshots those ids on the descriptor.
        var macroTextDeps = (node.Macros ?? new List<ConsultNodeMacroDescriptor>())
            .SelectMany(macro => macro.Reads ?? Array.Empty<string>());

        // v25 (B3 #1002, design § 2): the derived edges — what the node's
        // template and with read, closed over the snippets and macro texts
        // the prompt reads — snapshotted on the descriptor at job start. A 25
        // node carries no bindings, so these ARE its dependencies.
        var derivedDeps = node.Reads ?? Array.Empty<string>();

        return bindingDeps.Concat(macroWhenDeps).Concat(macroTextDeps).Concat(derivedDeps).Distinct(StringComparer.Ordinal);
    }
}

/// <summary>
/// The deliverable table and job outcome — pure functions over the snapshot, so
/// orchestrator-side use is replay-safe. v7 jobs get one entry per declared
/// result with the "{resultId}:" block-id prefix; v6 jobs get one unnamed
/// empty-prefix entry so block ids and signal order reproduce byte-for-byte;
/// v5 jobs get none (blocks are the fan items themselves).
/// </summary>
internal static class ConsultDeliverables
{
    internal sealed record Deliverable(
        string? ResultId,
        string? Label,
        string BlockPrefix,
        string NodeId,
        IReadOnlyList<string> SourceIds,
        int Ordinal,
        // v11 #513: the macro ids this deliverable appends, in declared order.
        IReadOnlyList<string>? MacroIds = null,
        // v11 #516: the package marks this deliverable signed.
        bool? Signature = null,
        // v12 #619: the placed macros' anchors, from the descriptor.
        IReadOnlyList<ConsultMacroPlacement>? MacroPlacements = null,
        // v12 #624: the check gating this deliverable — bare node id. v25 (B8
        // #1027): read in either spelling, and null when the node is not in
        // the run (a check gated out by its own when must not stash the
        // deliverable forever — the ≤24 hang B8 closes).
        string? CheckNodeId = null,
        // v25 (B8 #1027): the nodes this deliverable's placed macros read —
        // its document composes only once every one has settled.
        IReadOnlyList<string>? Reads = null,
        // v26 (#1154, design § 3): the deliverable's sources as declared — a
        // loop, an if/case or a prose node each. SourceIds then holds the
        // COMPLETION keys: a loop's returned node (its items complete the
        // loop's blocks), an if/case's own id, a prose node's own. Null below 26.
        IReadOnlyList<string>? Sources = null,
        // v26 (#1155, design § 3.1): the deliverables this deliverable's placed
        // macros read as deliverable.<id>.content — its release waits on their
        // composition. Null below 26 and when none.
        IReadOnlyList<string>? DeliverableReads = null);

    public static IReadOnlyList<Deliverable> Resolve(
        IReadOnlyList<ConsultResultDescriptor>? results,
        string? v6ResultNodeId,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById)
    {
        static IReadOnlyList<string> SourcesOf(ConsultNodeDescriptor? node) =>
            node?.Aggregate?
                .Select(sourceRef => TemplateObjectReference.NodeIdOf(sourceRef) ?? sourceRef)
                .ToList()
            ?? (IReadOnlyList<string>)new List<string>();

        // v26 (#1154): the completion keys of a deliverable's declared sources.
        IReadOnlyList<string> CompletionKeysOf(IReadOnlyList<string> sources) =>
            sources
                .Select(id => nodesById.GetValueOrDefault(id) is { Kind: ConsultNodeFlows.For, Ret: { } ret } ? ret : id)
                .ToList();

        if (results is { Count: > 0 })
        {
            return results
                .Select((result, ordinal) => new Deliverable(
                    result.Id,
                    result.Label,
                    $"{result.Id}:",
                    result.NodeId,
                    result.Sources is { Count: > 0 } sources ? CompletionKeysOf(sources) : SourcesOf(nodesById.GetValueOrDefault(result.NodeId)),
                    ordinal,
                    result.Macros,
                    result.Signature,
                    result.MacroPlacements,
                    result.Check is { } check && ConsultCheckRefs.NodeIdOf(check) is { } checkId && nodesById.ContainsKey(checkId)
                        ? checkId
                        : null,
                    result.Reads,
                    result.Sources,
                    result.DeliverableReads))
                .ToList();
        }

        if (v6ResultNodeId is null
            || nodesById.GetValueOrDefault(v6ResultNodeId) is not { Aggregate: not null } resultNode)
        {
            return Array.Empty<Deliverable>();
        }

        return new[] { new Deliverable(null, null, string.Empty, v6ResultNodeId, SourcesOf(resultNode), 0) };
    }

    /// <summary>
    /// v11 #516: the reply/PDF leg's documents, from the recorded texts (the
    /// bytes CompleteResultDocument stored — appends and all). Strict
    /// indexing on purpose: a Completed job recorded every deliverable, so a
    /// missing key is a broken invariant and fails loud, as the pre-#516
    /// outputs indexing did.
    /// </summary>
    internal static List<Email.EmailIntakeReplyDocument> ReplyDocumentsFor(
        IReadOnlyList<Deliverable> deliverables,
        IReadOnlyDictionary<string, string> recordedTexts,
        IReadOnlySet<string>? failedResultIds = null)
    {
        // v12 #624: a check-failed deliverable has no recorded text and is
        // named in the reply's failed lines instead — it leaves this set;
        // the strict indexing stays for everything produced (a missing key
        // there is still a broken invariant, failing loud).
        return deliverables
            .Where(deliverable => failedResultIds?.Contains(deliverable.ResultId!) != true)
            .Select(deliverable => new Email.EmailIntakeReplyDocument(
                deliverable.ResultId!,
                deliverable.Label ?? deliverable.ResultId!,
                recordedTexts[deliverable.ResultId!]))
            .ToList();
    }

    /// <summary>
    /// The v7 job outcome: Completed iff every deliverable's aggregator
    /// produced; otherwise Failed with the first missing deliverable's error
    /// by result-set order (fallbacks mirroring the v6 selection).
    /// </summary>
    public static (string Status, string? Error) FinalOutcome(
        IReadOnlyList<Deliverable> deliverables,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        IReadOnlyDictionary<string, string> failedAggregators,
        IReadOnlyList<ConsultFailedDocument>? failedDocuments = null,
        // v26 (#1155): a 26 deliverable is produced when it COMPOSED from its
        // sources (25's meaning — the aggregator's output); its failure is its own.
        IReadOnlySet<string>? composedDeliverables = null,
        IReadOnlyDictionary<string, string>? failedDeliverables = null,
        // v29 (#1305, design § 4; #1194): at 29 a deliverable is produced when it was
        // RELEASED — its document recorded or refused by its check; one composed and
        // never released is named, with why it was held. Null below 29.
        IReadOnlySet<string>? releasedDeliverables = null,
        IReadOnlyDictionary<string, string>? heldDeliverables = null)
    {
        bool Produced(Deliverable d) => d.Sources is null
            ? outputs.ContainsKey(d.NodeId)
            : d.ResultId is not null && (releasedDeliverables ?? composedDeliverables)?.Contains(d.ResultId) == true;

        var missing = deliverables.Where(d => !Produced(d)).ToList();

        if (missing.Count == 0)
        {
            // v12 #624 (§ 13): a check fail is per-document — siblings
            // produce and the job completes. A job whose EVERY deliverable
            // failed its check ends with no documents and says why.
            var failedIds = failedDocuments?.Select(d => d.ResultId).ToHashSet(StringComparer.Ordinal);
            if (failedIds is { Count: > 0 } && deliverables.All(d => d.ResultId != null && failedIds.Contains(d.ResultId)))
            {
                return (ConsultGenerationJobStatuses.Failed, failedDocuments![0].Reason);
            }

            return (ConsultGenerationJobStatuses.Completed, null);
        }

        var error = (missing[0].Sources is null
                ? failedAggregators.GetValueOrDefault(missing[0].NodeId)
                : heldDeliverables?.GetValueOrDefault(missing[0].ResultId ?? string.Empty)
                    ?? failedDeliverables?.GetValueOrDefault(missing[0].ResultId ?? string.Empty))
            ?? failedDeliverables?.Values.FirstOrDefault()
            ?? failedAggregators.Values.FirstOrDefault()
            ?? "The assembled documents could not be produced.";
        return (ConsultGenerationJobStatuses.Failed, error);
    }
}

/// <summary>
/// Resolves a node instance's bindings to rendered variable values — pure functions
/// over the orchestration input and recorded activity results, so orchestrator-side
/// use is replay-safe. Renderer implementations stay where the byte-pinning tests
/// point: the library's ConceptRenderers (#1138; the editor renders its sample through them).
/// </summary>
internal static class ConsultNodeVariableResolver
{
    /// <summary>
    /// The node's variables that are bound to a typed declared input, as
    /// variable name -> type. Null when nothing is typed, so a v5-v7 job
    /// carries no new payload at all and replays byte-identically.
    /// </summary>
    public static Dictionary<string, string>? TypedVariables(
        ConsultNodeDescriptor node,
        IReadOnlyDictionary<string, string>? inputTypes)
    {
        if (inputTypes is not { Count: > 0 } || node.Bindings is not { Count: > 0 })
        {
            return null;
        }

        Dictionary<string, string>? typed = null;

        foreach (var (variable, binding) in node.Bindings)
        {
            if (!binding.From.StartsWith(WorkflowNodeBindingSources.InputPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var id = binding.From[WorkflowNodeBindingSources.InputPrefix.Length..];
            if (inputTypes.TryGetValue(id, out var type) && type != WorkflowInputTypes.Text)
            {
                (typed ??= new Dictionary<string, string>(StringComparer.Ordinal))[variable] = type;
            }
        }

        return typed;
    }

    public static Dictionary<string, string> Resolve(
        ConsultNodeDescriptor node,
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyDictionary<string, string>? item,
        IReadOnlyDictionary<string, string>? dataScalars,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, NodeRunResult> outputs)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (variable, binding) in node.Bindings ?? new Dictionary<string, ConsultNodeBindingDescriptor>())
        {
            variables[variable] = binding.From switch
            {
                // v7: any declared input; v5/v6 jobs carry the one-entry
                // consult_draft map, so the lookup is value-identical to the
                // old direct draft string.
                _ when binding.From.StartsWith(WorkflowNodeBindingSources.InputPrefix, StringComparison.Ordinal)
                    => inputs.GetValueOrDefault(binding.From[WorkflowNodeBindingSources.InputPrefix.Length..])
                        ?? throw new InvalidOperationException(
                            $"Node '{node.Id}' binding '{variable}' reads undeclared input '{binding.From[WorkflowNodeBindingSources.InputPrefix.Length..]}'."),
                _ when binding.From.StartsWith("item:", StringComparison.Ordinal)
                    => (item ?? throw new InvalidOperationException(
                            $"Node '{node.Id}' binding '{variable}' reads '{binding.From}' outside a forEach instance."))
                        .GetValueOrDefault(binding.From["item:".Length..])
                        ?? throw new InvalidOperationException(
                            $"Node '{node.Id}' binding '{variable}' reads item field '{binding.From["item:".Length..]}', which the item does not carry."),
                _ when binding.From.StartsWith(WorkflowNodeBindingSources.DataPrefix, StringComparison.Ordinal)
                    => (dataScalars ?? throw new InvalidOperationException(
                            $"Node '{node.Id}' binding '{variable}' reads '{binding.From}' but the job carries no data scalars."))
                        .GetValueOrDefault(binding.From[WorkflowNodeBindingSources.DataPrefix.Length..])
                        ?? throw new InvalidOperationException(
                            $"Node '{node.Id}' binding '{variable}' reads unknown data entry '{binding.From[WorkflowNodeBindingSources.DataPrefix.Length..]}'."),
                _ when binding.From.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                    => RenderNodeOutput(node.Id, variable, binding, item, nodesById, outputs),
                _ => throw new InvalidOperationException(
                    $"Node '{node.Id}' binding '{variable}' has source '{binding.From}', which a node cannot resolve.")
            };
        }

        return variables;
    }

    public static string Render(string renderer, IReadOnlyList<ClinicalConcept> concepts) => renderer switch
    {
        WorkflowConceptRenderers.ConceptBullets => ConceptRenderers.Bullets(concepts),
        WorkflowConceptRenderers.ConceptContext => ConceptRenderers.Context(concepts),
        _ => throw new InvalidOperationException($"Unknown concept renderer '{renderer}'.")
    };

    private static string RenderNodeOutput(
        string nodeId,
        string variable,
        ConsultNodeBindingDescriptor binding,
        IReadOnlyDictionary<string, string>? item,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, NodeRunResult> outputs)
    {
        var targetId = binding.From[WorkflowNodeBindingSources.NodePrefix.Length..];
        var target = nodesById[targetId];
        var result = outputs[ConsultNodeScheduler.InstanceKey(
            targetId,
            target.ForEach is null ? null : ConsultNodeScheduler.AlignedPath(target, WorkflowItemPath.OfItem(item), nodesById)
                ?? throw new InvalidOperationException(
                    $"Node '{nodeId}' binding '{variable}' targets forEach node '{targetId}' outside a forEach instance."))];

        if (target.OutputContract is null)
        {
            return result.RawOutput;
        }

        // v10 (#495): a classifier's value renders as its text (§ 4).
        if (string.Equals(target.OutputContract, OutputContracts.Classification, StringComparison.Ordinal))
        {
            return result.Classification
                ?? throw new InvalidOperationException($"Node '{nodeId}' binding '{variable}' targets classifier '{targetId}', which recorded no value.");
        }

        // #760 (custom tier): a custom node's output is user-defined JSON — string-
        // consumable, interpolated whole into the downstream template/prompt. (Per-field
        // access is a follow-up.)
        if (string.Equals(target.OutputContract, OutputContracts.Custom, StringComparison.Ordinal))
        {
            return result.RawOutput;
        }

        return Render(binding.As ?? WorkflowConceptRenderers.ConceptBullets, result.Concepts
            ?? throw new InvalidOperationException($"Node '{nodeId}' binding '{variable}' targets '{targetId}', which recorded no concepts."));
    }
}
