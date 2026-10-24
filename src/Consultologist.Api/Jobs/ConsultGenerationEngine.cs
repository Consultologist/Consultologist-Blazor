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
        var items = input.Items is { Count: > 0 }
            ? input.Items
            : deciding
                ? Array.Empty<IReadOnlyDictionary<string, string>>()
                : throw new InvalidOperationException("Consult generation input carries no work items; the job start snapshots them from the workflow package.");
        var collections = input.Collections;
        var v6 = collections is { Count: > 0 };

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
        var chainNodes = nodes.Where(node => node.ForEach != null).ToList();
        var aggregators = nodes.Where(node => node.Aggregate != null).ToList();
        // v25 (B5 #1004): a 25 job — one whose nodes carry formats (the job
        // starter's IsAt25, over the snapshot it took). Its aggregators stamp
        // definition 6, and its deliverables fill no slot: an inline macro
        // rendered in the reply of the node that read it (#1044).
        var at25 = nodes.Any(node => node.Format is not null);
        var outputs = new Dictionary<string, NodeRunResult>(StringComparer.Ordinal);
        var pendingTasks = new Dictionary<Task<NodeRunResult>, (string NodeId, string? ItemId)>();
        var started = new HashSet<string>(StringComparer.Ordinal);
        var failedItems = new HashSet<string>(StringComparer.Ordinal);
        var failedAggregators = new Dictionary<string, string>(StringComparer.Ordinal);
        var nodeLevelCompleted = new HashSet<string>(StringComparer.Ordinal);

        // v25 (B3 #1002): a snippet folder fan (snippet:<folder>) keys its
        // snapshot by the folder id, as a data collection fan did.
        string CollectionIdOf(ConsultNodeDescriptor node) => WorkflowFanSources.CollectionIdOf(node.ForEach!);

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


        void StartInstance(ConsultNodeDescriptor node, IReadOnlyDictionary<string, string>? item)
        {
            var instanceKey = ConsultNodeScheduler.InstanceKey(node.Id, item?["id"]);
            started.Add(instanceKey);

            // v25 (B3 #1002, design § 1): a node with a format renders over
            // the seven objects in the activity; the orchestrator threads what
            // only it holds — the outputs this instance reads, the settled
            // folders, the facts — and no flat variables, no before/after
            // blocks (instances compose on the output side, B5).
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
                            ConsultRenderPayload.NodeTextsFor(node, item, nodesById, outputs),
                            node.Format,
                            node.FormatHash),
                        AgentActivityRetryOptions)] = (node.Id, item?["id"]);
                    return;
                }

                pendingTasks[context.CallActivityAsync<NodeRunResult>(
                    ConsultGenerationActivityNames.RunPromptNode,
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
                        NodeTexts: ConsultRenderPayload.NodeTextsFor(node, item, nodesById, outputs),
                        Folders: ConsultRenderPayload.FoldersFor(collections),
                        Item: item,
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
                        Instances: node.Macros),
                    AgentActivityRetryOptions)] = (node.Id, item?["id"]);
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

                if (node.Check != null)
                {
                    continue; // v12 #624: checks settle inline too — no activity, no model.
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
                            (deliverable.Reads ?? Array.Empty<string>()).Append(deliverable.NodeId)
                                .Where(outputs.ContainsKey)
                                .Distinct(StringComparer.Ordinal)
                                .ToDictionary(id => id, id => outputs[id].Classification ?? outputs[id].RawOutput, StringComparer.Ordinal),
                            ConsultRenderPayload.FoldersFor(collections),
                            ConsultRenderPayload.RunFactsFor(context.CurrentUtcDateTime, context.InstanceId, input.WorkflowPackage, input.ApiHost),
                            ConsultRenderPayload.ProfileFactsFor(input.ProfileName, input.Signature)),
                        AgentActivityRetryOptions);

                var (text25, appended25) = ConsultMacroExpander.ComposeAt25(renderedMacros.Prepend, parts, renderedMacros.Append);
                var (final25, finalAppended25, unsigned25) = ConsultSignatureAppend.Finish(
                    text25, appended25, deliverable.Signature == true, renderedMacros.SignatureRead, input.Signature);
                recordedTexts[deliverable.ResultId!] = final25;

                await context.Entities.CallEntityAsync(
                    entityId,
                    nameof(ConsultGenerationJobEntity.CompleteResultDocument),
                    new ConsultGenerationResultDocument(deliverable.ResultId!, deliverable.Label ?? deliverable.ResultId!, final25, deliverable.Ordinal, finalAppended25, unsigned25));
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
                if (!ConsultCheckSettlement.IsReleaseReady(pending.Deliverable, outputs, settledChecks, out var outcome))
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
                    outcome.Untested);
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
                                    item["id"],
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
            }
        }

        // The ready loop, run to quiescence: once for every job, and (v10,
        // #496) once more for a deciding job — the classifier closure first,
        // then, after the boundary rebinds the graph, the rest. False when the
        // job failed inside it and the orchestration must return.
        async Task<bool> RunToQuiescenceAsync()
        {
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
            results = decidedResults;
            items = decision.Items;
            collections = decision.CollectionSets;
            v6 = collections is { Count: > 0 };
            deliverables = ConsultDeliverables.Resolve(results, v6 ? resultNodeId : null, nodesById);
            totalBlockCount = items.Count;
            totalNodeCount = nodes.Count;
            skippedForReply = decision.Skipped.Count > 0 ? decision.Skipped : null;
        }

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

        var expectedInstances = nodes.Where(node => node.Aggregate is null && node.Check is null).Sum(node => node.ForEach is null
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
            (finalStatus, finalError) = ConsultDeliverables.FinalOutcome(deliverables, outputs, failedAggregators, failedDocuments);
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
            Excluded: result.Excluded);
    }

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
            Excluded: result.Excluded);
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
    /// <summary>Scalar nodes key by id; forEach instances by "nodeId:itemId".</summary>
    public static string InstanceKey(string nodeId, string? itemId)
        => itemId is null ? nodeId : $"{nodeId}:{itemId}";

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
        IReadOnlyDictionary<string, NodeRunResult> outputs)
    {
        return NodeDependencies(node).All(dependencyId =>
            outputs.ContainsKey(InstanceKey(
                dependencyId,
                nodesById[dependencyId].ForEach is null ? null : itemId)));
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
        IReadOnlyList<string>? Reads = null);

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

        if (results is { Count: > 0 })
        {
            return results
                .Select((result, ordinal) => new Deliverable(
                    result.Id,
                    result.Label,
                    $"{result.Id}:",
                    result.NodeId,
                    SourcesOf(nodesById.GetValueOrDefault(result.NodeId)),
                    ordinal,
                    result.Macros,
                    result.Signature,
                    result.MacroPlacements,
                    result.Check is { } check && ConsultCheckRefs.NodeIdOf(check) is { } checkId && nodesById.ContainsKey(checkId)
                        ? checkId
                        : null,
                    result.Reads))
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
        IReadOnlyList<ConsultFailedDocument>? failedDocuments = null)
    {
        var missing = deliverables.Where(d => !outputs.ContainsKey(d.NodeId)).ToList();

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
            target.ForEach is null ? null : item?["id"]
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
