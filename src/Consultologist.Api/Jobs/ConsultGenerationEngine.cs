using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

public sealed class ConsultGenerationOrchestrator
{
    // Agent activity calls are nondeterministic: a retry re-runs the same prompt and the
    // agent issues fresh tool calls, so a transient tool failure (e.g. a rejected SNOMED
    // search) rarely repeats. Configuration errors (InvalidOperationException from
    // AgentSectionGenerator) are excluded so they fail fast instead of burning retries.
    // See docs/SNOMED_TOOL_FAILURES.md.
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
        var items = input.Items is { Count: > 0 }
            ? input.Items
            : throw new InvalidOperationException("Consult generation input carries no work items; the job start snapshots them from the workflow package.");
        var collections = input.Collections;
        var v6 = collections is { Count: > 0 };

        // The input map: v7 jobs carry the effective map from the starter;
        // v5/v6 jobs fold the single draft into the conventional slot — a pure
        // value-identical view, so replaying instances render the same bytes.
        var effectiveInputs = input.Inputs
            ?? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ConsultGenerationJobStarter.ConsultDraftInputId] = request.ConsultDraft ?? string.Empty
            };

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
                input.InputOrigins));

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
        var chainNodes = nodes.Where(node => node.ForEach != null).ToList();
        var aggregators = nodes.Where(node => node.Aggregate != null).ToList();
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal);
        var pendingTasks = new Dictionary<Task<NodeRunResult>, (string NodeId, string? ItemId)>();
        var started = new HashSet<string>(StringComparer.Ordinal);
        var failedItems = new HashSet<string>(StringComparer.Ordinal);
        var failedAggregators = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodeLevelCompleted = new HashSet<string>(StringComparer.Ordinal);

        string CollectionIdOf(ConsultNodeDescriptor node) =>
            node.ForEach!.StartsWith(WorkflowNodeBindingSources.DataPrefix, StringComparison.Ordinal)
                ? node.ForEach[WorkflowNodeBindingSources.DataPrefix.Length..]
                : node.ForEach;

        // v6: each forEach node fans over ITS collection's items; v5 keeps the
        // single shared set. Failed-item keys are collection-scoped in v6 (item
        // ids are unique per collection, not globally).
        IReadOnlyList<IReadOnlyDictionary<string, string>> FanItems(ConsultNodeDescriptor node) =>
            v6
                ? collections!.GetValueOrDefault(CollectionIdOf(node))
                    ?? throw new InvalidOperationException($"Node '{node.Id}' fans collection '{CollectionIdOf(node)}', which the job snapshot does not carry.")
                : items;

        string FailedKey(ConsultNodeDescriptor node, string itemId) =>
            v6 ? $"{CollectionIdOf(node)}:{itemId}" : itemId;

        string ItemName(ConsultNodeDescriptor node, string itemId) =>
            FanItems(node).First(item => item["id"] == itemId).GetValueOrDefault("name", itemId);

        // The deliverable table: v7 = one entry per result in result-set order
        // (block ids gain the "{resultId}:" prefix); v6 = one unnamed entry
        // with an empty prefix, reproducing today's block ids byte-for-byte;
        // v5 = empty (blocks are the fan items themselves).
        var deliverables = ConsultDeliverables.Resolve(results, v6 ? resultNodeId : null, nodesById);

        void StartInstance(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item)
        {
            var instanceKey = ConsultNodeScheduler.InstanceKey(node.Id, item?["id"]);
            started.Add(instanceKey);

            var variables = ConsultNodeVariableResolver.Resolve(
                node, effectiveInputs, item, input.DataScalars, nodesById, outputs);

            // v8: which of this node's variables are bound to a typed input.
            // Only declared inputs carry a type — item:, node: and data:
            // sources are strings by nature.
            var variableTypes = ConsultNodeVariableResolver.TypedVariables(node, input.InputTypes);

            pendingTasks[context.CallActivityAsync<NodeRunResult>(
                ConsultGenerationActivityNames.RunPromptNode,
                new ConsultPromptNodeActivityInput(
                    instanceKey,
                    node.PromptId!,
                    variables,
                    input.WorkflowPackage,
                    node.OutputContract,
                    node.ConceptSource,
                    variableTypes),
                AgentActivityRetryOptions)] = (node.Id, item?["id"]);
        }

        void StartReadyInstances()
        {
            foreach (var node in nodes)
            {
                if (node.Aggregate != null)
                {
                    continue; // Aggregators compose inline, never as activities.
                }

                if (node.ForEach is null)
                {
                    if (!started.Contains(node.Id)
                        && ConsultNodeScheduler.InstanceReady(node, null, nodesById, outputs))
                    {
                        StartInstance(node, null);
                    }

                    continue;
                }

                foreach (var item in FanItems(node))
                {
                    var itemId = item["id"];

                    if (failedItems.Contains(FailedKey(node, itemId))
                        || started.Contains(ConsultNodeScheduler.InstanceKey(node.Id, itemId)))
                    {
                        continue;
                    }

                    if (ConsultNodeScheduler.InstanceReady(node, itemId, nodesById, outputs))
                    {
                        StartInstance(node, item);
                    }
                }
            }
        }

        async Task MarkFullyCompletedChainNodesAsync()
        {
            // A forEach node completes at the node level once every item is either
            // done for it or failed earlier in its chain — this is what drives the
            // stage checklist and the node-completed events.
            foreach (var node in chainNodes)
            {
                if (nodeLevelCompleted.Contains(node.Id))
                {
                    continue;
                }

                var allSettled = FanItems(node).All(item =>
                    failedItems.Contains(FailedKey(node, item["id"]))
                    || outputs.ContainsKey(ConsultNodeScheduler.InstanceKey(node.Id, item["id"])));

                if (!allSettled)
                {
                    continue;
                }

                nodeLevelCompleted.Add(node.Id);
                completedNodeCount++;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                    new ConsultGenerationNodeUpdate(node.Id, node.Label, null, null, null, completedNodeCount, totalNodeCount));
            }
        }

        async Task FailItemAsync(ConsultNodeDescriptor node, string itemId, string error)
        {
            failedItems.Add(FailedKey(node, itemId));

            if (!v6)
            {
                failedBlockCount++;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.FailBlock),
                    new BlockGenerationResult(itemId, ItemName(node, itemId), false, null, error));
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
                            new BlockGenerationResult($"{deliverable.BlockPrefix}{source!.Id}:{itemId}", ItemName(node, itemId), false, null, error));
                    }
                }
            }

            // Downstream item-aligned instances never start; nodes waiting only on
            // this item may now be node-level complete.
            await MarkFullyCompletedChainNodesAsync();
        }

        // Aggregators compose inline once every source settles — deterministic,
        // no activity, no retries; a failed contributing item fails the aggregator
        // loud (never a partial document), cascading downstream by absence
        // (package-format-v6-design.md §§ 3, 6).
        async Task TryCompleteAggregatorsAsync()
        {
            var progressed = true;

            while (progressed)
            {
                progressed = false;

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
                        var sourceId = sourceRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                            ? sourceRef[WorkflowNodeBindingSources.NodePrefix.Length..]
                            : sourceRef;
                        var source = nodesById[sourceId];

                        if (source.ForEach != null)
                        {
                            var sourceItems = FanItems(source);
                            var failed = sourceItems
                                .Where(item => failedItems.Contains(FailedKey(source, item["id"])))
                                .Select(item => item["id"])
                                .ToList();

                            if (failed.Count > 0)
                            {
                                failedContributors.AddRange(failed.Select(id => $"{sourceId}:{id}"));
                                continue;
                            }

                            if (!sourceItems.All(item => outputs.ContainsKey(ConsultNodeScheduler.InstanceKey(sourceId, item["id"]))))
                            {
                                allSettled = false;
                                continue;
                            }

                            parts.Add(new ConsultAggregateRenderer.ForEachPart(sourceItems
                                .Select(item => (
                                    item.GetValueOrDefault("name", item["id"]),
                                    outputs[ConsultNodeScheduler.InstanceKey(sourceId, item["id"])].RawOutput.Trim()))
                                .ToList()));
                            sourceHashes.AddRange(sourceItems
                                .Select(item => outputs[ConsultNodeScheduler.InstanceKey(sourceId, item["id"])].OutputHash));
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
                    var aggregateResult = new NodeRunResult(
                        rendered,
                        null,
                        ConsultGenerationProvenance.ComputeAggregateInputHash(sourceHashes),
                        ConsultGenerationProvenance.Sha256Hex(rendered));
                    outputs[aggregator.Id] = aggregateResult;
                    completedNodeCount++;
                    progressed = true;

                    await context.Entities.CallEntityAsync(
                        entityId,
                        nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                        new ConsultGenerationNodeUpdate(
                            aggregator.Id, aggregator.Label, null,
                            aggregateResult.InputHash, aggregateResult.OutputHash,
                            completedNodeCount, totalNodeCount));

                    if (v7)
                    {
                        // Result nodes are distinct per the validator: at most one
                        // deliverable owns this aggregator.
                        var deliverable = deliverables.FirstOrDefault(d => string.Equals(d.NodeId, aggregator.Id, StringComparison.Ordinal));
                        if (deliverable != null)
                        {
                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.CompleteResultDocument),
                                new ConsultGenerationResultDocument(deliverable.ResultId!, deliverable.Label ?? deliverable.ResultId!, rendered, deliverable.Ordinal));
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
            }
        }

        StartReadyInstances();

        while (pendingTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(pendingTasks.Keys);
            var (nodeId, itemId) = pendingTasks[completedTask];
            pendingTasks.Remove(completedTask);
            var node = nodesById[nodeId];

            if (itemId is null)
            {
                // Scalar node: an activity failure (post-retries) is a runtime job
                // failure, and an empty declared-required output fails the job with
                // the node's message — unchanged semantics from the wave interpreter.
                var result = await completedTask;

                if (node.FailIfEmpty != null && (result.Concepts?.Count ?? 0) == 0)
                {
                    await FailNodeAsync(
                        context, entityId, node, nodes, outputs,
                        totalBlockCount, completedBlockCount, failedBlockCount, logger);
                    return;
                }

                outputs[nodeId] = result;
                completedNodeCount++;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.MarkNodeCompleted),
                    new ConsultGenerationNodeUpdate(
                        node.Id, node.Label, result.Concepts, result.InputHash, result.OutputHash,
                        completedNodeCount, totalNodeCount));

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
                    await FailItemAsync(node, itemId, $"{node.Label} failed: {ex.Message}");
                    PublishStatus(ConsultGenerationJobStatuses.Running);
                    StartReadyInstances();
                    continue;
                }

                if (node.FailIfEmpty != null && (result.Concepts?.Count ?? 0) == 0)
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
                    new ConsultGenerationNodeItemUpdate(
                        node.Id, node.Label, itemId, ItemName(node, itemId),
                        result.Concepts, result.InputHash, result.OutputHash,
                        completedChainCount, chain.Count));

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
                        if (deliverable.SourceIds.Contains(nodeId))
                        {
                            completedBlockCount++;

                            await context.Entities.CallEntityAsync(
                                entityId,
                                nameof(ConsultGenerationJobEntity.CompleteBlock),
                                new BlockGenerationResult($"{deliverable.BlockPrefix}{nodeId}:{itemId}", ItemName(node, itemId), true, result.RawOutput.Trim(), null));
                        }
                    }
                }

                await MarkFullyCompletedChainNodesAsync();
            }

            await TryCompleteAggregatorsAsync();
            PublishStatus(ConsultGenerationJobStatuses.Running);
            StartReadyInstances();
        }

        var expectedInstances = nodes.Where(node => node.Aggregate is null).Sum(node => node.ForEach is null
            ? 1
            : FanItems(node).Count(item => !failedItems.Contains(FailedKey(node, item["id"]))));
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
            (finalStatus, finalError) = ConsultDeliverables.FinalOutcome(deliverables, outputs, failedAggregators);
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
            new ConsultGenerationJobFinalize(finalStatus, finalError));

        // #159/#217: the completed documents ride into the reply activity so
        // each can be attached as an encrypted PDF when the account has a
        // delivery password. v7 sends the whole set in result-set order (the
        // outcome gate above guarantees every deliverable produced); v6 sends
        // its single document. Sourced from replayed activity outputs —
        // deterministic.
        var replyDocuments = v7 && finalStatus == ConsultGenerationJobStatuses.Completed
            ? deliverables
                .Select(deliverable => new Email.EmailIntakeReplyDocument(
                    deliverable.ResultId!,
                    deliverable.Label ?? deliverable.ResultId!,
                    outputs[deliverable.NodeId].RawOutput))
                .ToList()
            : null;
        var assembledDocument = !v7 && v6 && outputs.TryGetValue(resultNodeId!, out var resultOutput)
            ? resultOutput.RawOutput
            : null;

        await SendEmailIntakeReplyAsync(context, input, finalStatus, logger, assembledDocument, replyDocuments);

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
                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.FinalizeJob),
                    new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Failed, ex.Message));
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
    /// #158/#157: jobs with a reply address announce their terminal status by
    /// email — the starter's caller decides who gets one by setting the address
    /// (email intake: the sender; scheduled app jobs: the account email;
    /// immediate app jobs: none). The gate reads only orchestration input
    /// (deterministic on replay); a reply failure never fails a finalized job.
    /// </summary>
    private static async Task SendEmailIntakeReplyAsync(
        TaskOrchestrationContext context,
        ConsultGenerationOrchestrationInput input,
        string finalStatus,
        ILogger logger,
        string? assembledDocument,
        IReadOnlyList<Email.EmailIntakeReplyDocument>? documents = null)
    {
        if (string.IsNullOrWhiteSpace(input.ReplyToAddress))
        {
            return;
        }

        try
        {
            await context.CallActivityAsync(
                Email.SendEmailIntakeReplyActivity.Name,
                new Email.EmailIntakeReplyInput(
                    context.InstanceId,
                    input.ReplyToAddress,
                    finalStatus,
                    input.AppUserId,
                    assembledDocument,
                    documents,
                    input.SkippedDocuments),
                new TaskOptions(new TaskRetryOptions(new RetryPolicy(3, TimeSpan.FromSeconds(10), 2.0))));
        }
        catch (Exception replyEx)
        {
            logger.LogWarning(
                replyEx,
                "Email intake reply activity failed. JobId={JobId}",
                context.InstanceId);
        }
    }

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
    /// <summary>Scalar nodes key by id; forEach instances by "nodeId:itemId".</summary>
    public static string InstanceKey(string nodeId, string? itemId)
        => itemId is null ? nodeId : $"{nodeId}:{itemId}";

    /// <summary>
    /// A scalar node is ready when every node: dependency completed; a forEach
    /// instance additionally requires same-collection dependencies item-aligned
    /// (the validator guarantees no aggregate or cross-collection edges exist).
    /// </summary>
    public static bool InstanceReady(
        ConsultNodeDescriptor node,
        string? itemId,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, NodeRunResult> outputs)
    {
        return NodeDependencies(node).All(dependencyId =>
            outputs.ContainsKey(InstanceKey(
                dependencyId,
                nodesById[dependencyId].ForEach is null ? null : itemId)));
    }

    public static IEnumerable<string> NodeDependencies(ConsultNodeDescriptor node)
    {
        return (node.Bindings ?? new Dictionary<string, ConsultNodeBindingDescriptor>())
            .Values
            .Where(binding => binding.From.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal))
            .Select(binding => binding.From[WorkflowNodeBindingSources.NodePrefix.Length..]);
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
        int Ordinal);

    public static IReadOnlyList<Deliverable> Resolve(
        IReadOnlyList<ConsultResultDescriptor>? results,
        string? v6ResultNodeId,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById)
    {
        static IReadOnlyList<string> SourcesOf(ConsultNodeDescriptor? node) =>
            node?.Aggregate?
                .Select(sourceRef => sourceRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                    ? sourceRef[WorkflowNodeBindingSources.NodePrefix.Length..]
                    : sourceRef)
                .ToList()
            ?? (IReadOnlyList<string>)new List<string>();

        if (results is { Count: > 0 })
        {
            return results
                .Select((result, ordinal) => new Deliverable(
                    result.Id,
                    result.Label,
                    $"{result.Id}:",
                    result.NodeId,
                    SourcesOf(nodesById.GetValueOrDefault(result.NodeId)),
                    ordinal))
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
    /// The v7 job outcome: Completed iff every deliverable's aggregator
    /// produced; otherwise Failed with the first missing deliverable's error
    /// by result-set order (fallbacks mirroring the v6 selection).
    /// </summary>
    public static (string Status, string? Error) FinalOutcome(
        IReadOnlyList<Deliverable> deliverables,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        IReadOnlyDictionary<string, string> failedAggregators)
    {
        var missing = deliverables.Where(d => !outputs.ContainsKey(d.NodeId)).ToList();

        if (missing.Count == 0)
        {
            return (ConsultGenerationJobStatuses.Completed, null);
        }

        var error = failedAggregators.GetValueOrDefault(missing[0].NodeId)
            ?? failedAggregators.Values.FirstOrDefault()
            ?? "The assembled documents could not be produced.";
        return (ConsultGenerationJobStatuses.Failed, error);
    }
}

/// <summary>
/// Resolves a node instance's bindings to rendered variable values — pure functions
/// over the orchestration input and recorded activity results, so orchestrator-side
/// use is replay-safe. Renderer implementations stay where the byte-pinning tests
/// point: ConsultGenerationConceptFormatter and AgentSectionGenerator.FormatConcepts.
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
        WorkflowConceptRenderers.ConceptBullets => ConsultGenerationConceptFormatter.Format(concepts),
        WorkflowConceptRenderers.ConceptContext => AgentSectionGenerator.FormatConcepts(concepts),
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
            target.ForEach is null ? null : item?["id"]
                ?? throw new InvalidOperationException(
                    $"Node '{nodeId}' binding '{variable}' targets forEach node '{targetId}' outside a forEach instance."))];

        if (target.OutputContract is null)
        {
            return result.RawOutput;
        }

        return Render(binding.As ?? WorkflowConceptRenderers.ConceptBullets, result.Concepts
            ?? throw new InvalidOperationException($"Node '{nodeId}' binding '{variable}' targets '{targetId}', which recorded no concepts."));
    }
}
