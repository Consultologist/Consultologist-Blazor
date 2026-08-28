using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using System.Net.ServerSentEvents;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Jobs;

public sealed class ConsultGenerationJobEntity : TaskEntity<ConsultGenerationJobState>
{
    private readonly IConsultGenerationJobIndexStore _indexStore;

    public ConsultGenerationJobEntity(IConsultGenerationJobIndexStore indexStore)
    {
        _indexStore = indexStore;
    }

    public async Task Initialize(ConsultGenerationJobInitialize input)
    {
        Seed(input);

        await _indexStore.UpsertAsync(State.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// v10 (#496, package-format-v10-design.md § 5): the boundary. A job whose
    /// package has classifiers was initialised deciding — no blocks, the
    /// count not yet known — and this is the one signal that decides: the
    /// fire set, the skipped set with each condition's sentence, the pruned
    /// nodes, the block skeleton, the count. Write-once, first-writer-wins:
    /// #176's rule with the moment moved, never the rule.
    /// </summary>
    public async Task Decide(ConsultGenerationDecision input)
    {
        var state = EnsureState();

        if (state.DecidedAtUtc != null || state.StartFailure != null)
        {
            return;
        }

        state.TotalBlockCount = input.Items.Count;
        foreach (var item in input.Items)
        {
            state.GetOrAddBlock(item["id"], item.GetValueOrDefault("name", item["id"]));
        }

        state.Nodes = input.Nodes?.ToList() ?? state.Nodes;
        state.ItemSteps = input.ItemSteps?.ToList() ?? state.ItemSteps;
        state.Collections = input.Collections?.ToList() ?? state.Collections;
        state.SkippedDocuments = input.SkippedDocuments?.ToList();
        state.Classifications = input.Classifications?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        state.DecidedAtUtc = input.DecidedAtUtc;
        state.History.Add(new JobHistoryEvent(
            "decided",
            input.Results is { Count: > 0 }
                ? $"Decided: {input.Items.Count} sections in {input.Results.Count} documents"
                : $"Decided: {input.Items.Count} sections",
            null,
            input.DecidedAtUtc));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// v10 (#496): a job that ends in the deciding stage — a classifier that
    /// failed, or a fire set that came back empty after classification. Born
    /// Failed the way #434's record is (StartFailure set, FailureError null,
    /// a stated zero), with the kind saying which, and the classifier values
    /// on the record because a declared value is printable.
    /// </summary>
    public async Task RecordDecisionFailure(ConsultGenerationDecisionFailure input)
    {
        var state = EnsureState();

        if (ConsultGenerationJobEntity.IsTerminal(state.Status))
        {
            return;
        }

        state.Status = ConsultGenerationJobStatuses.Failed;
        state.StartFailure = input.Reason;
        state.DecisionFailureKind = input.Kind;
        state.Classifications = input.Classifications?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        state.SkippedDocuments ??= input.SkippedDocuments?.ToList();
        state.SchemaVersion = 7;
        state.CompletedAtUtc = DateTimeOffset.UtcNow;
        state.History.Add(new JobHistoryEvent(
            "failure",
            input.Kind == ConsultGenerationDecisionFailureKinds.NothingApplied ? "No document applies after classification" : "Could not decide what to produce",
            input.Reason,
            DateTimeOffset.UtcNow));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// #434: a well-formed, authorized request met a package whose conditions
    /// left nothing to produce. The row exists so the operator has something
    /// to point at; nothing ran and nothing was spent, so it is born terminal
    /// — Failed, with <see cref="ConsultGenerationJobState.StartFailure"/>
    /// saying why and <see cref="ConsultGenerationJobState.FailureError"/>
    /// left null, which is how a reader tells "failed at start" from "ran and
    /// failed". Its own operation for the reason <see cref="Cancel"/> is:
    /// one signal, one write, no orchestration to order it against.
    /// </summary>
    public async Task RecordStartFailure(ConsultGenerationJobStartFailure input)
    {
        Seed(input.Initialize);

        var state = State;
        state.Status = ConsultGenerationJobStatuses.Failed;
        state.StartFailure = input.Reason;
        // The record's own storage shape is the current one, as a job that
        // ran stamps at its first document; nothing here will stamp it later.
        state.SchemaVersion = 7;
        state.CompletedAtUtc = DateTimeOffset.UtcNow;
        state.History.Add(new JobHistoryEvent("failure", "No document applies", input.Reason, DateTimeOffset.UtcNow));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    private void Seed(ConsultGenerationJobInitialize input)
    {
        if (State == null || State.Blocks.Count == 0)
        {
            State = ConsultGenerationJobState.Create(input.JobId, input.AppUserId, input.Items);
        }
        else
        {
            State.JobId = string.IsNullOrWhiteSpace(State.JobId) ? input.JobId : State.JobId;
            State.AppUserId = string.IsNullOrWhiteSpace(State.AppUserId) ? input.AppUserId : State.AppUserId;

            if (State.CreatedAtUtc == default)
            {
                State.CreatedAtUtc = DateTimeOffset.UtcNow;
            }

            if (State.TotalBlockCount == 0)
            {
                State.TotalBlockCount = input.Items.Count;
            }

            foreach (var item in input.Items)
            {
                State.GetOrAddBlock(item["id"], item.GetValueOrDefault("name", item["id"]));
            }
        }

        State.WorkflowPackage ??= input.WorkflowPackage;
        State.EffectiveInputHash ??= input.EffectiveInputHash;
        State.ItemSteps ??= input.ItemSteps?.ToList();
        State.Nodes ??= input.Nodes?.ToList();
        State.Collections ??= input.Collections?.ToList();
        State.EffectiveInputHashVersion ??= input.EffectiveInputHashVersion;
        State.CatalogRef ??= input.CatalogRef;
        State.PackageFormatRef ??= input.PackageFormatRef;
        State.ProvenanceRef ??= input.ProvenanceRef;
        State.Terminology ??= input.Terminology;
        State.TerminologyServerRef ??= input.TerminologyServerRef;
        State.ApiHost ??= input.ApiHost;
        State.EngineCommit ??= input.EngineCommit;
        State.Source ??= input.Source;
        State.ScheduledAtUtc ??= input.ScheduledAtUtc;
        State.PackageSpecVersion ??= input.PackageSpecVersion;
        State.PackageTitle ??= input.PackageTitle;
        State.PackageTags ??= input.PackageTags?.ToList();
        State.InputOrigins ??= input.InputOrigins?.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        State.InputDocumentOrigins ??= input.InputDocumentOrigins?.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToList(),
            StringComparer.Ordinal);
        State.SkippedDocuments ??= input.SkippedDocuments?.ToList();

        // v10 (#496): a job that starts deciding has no count yet — the
        // boundary stamps it. Every other job is decided at start, as it always
        // was; the date is what a reader tells the two apart by.
        if (input.Deciding == true)
        {
            State.Deciding ??= true;
        }
        else
        {
            State.DecidedAtUtc ??= State.CreatedAtUtc;
        }

        // #157: a future schedule shows as Scheduled until MarkRunning; entities
        // run exactly once per signal, so the wall clock is safe here.
        if (State.Status == ConsultGenerationJobStatuses.Queued
            && State.ScheduledAtUtc is { } scheduledAt
            && scheduledAt > DateTimeOffset.UtcNow)
        {
            State.Status = ConsultGenerationJobStatuses.Scheduled;
        }
    }

    public async Task MarkRunning()
    {
        var state = EnsureState();

        // #202: the cancel race. Terminating the orchestration does not
        // un-schedule a timer that has already fired, so a cancel landing in
        // that window would otherwise be walked back to Running here — and the
        // job would run after the user was told it would not. Terminal is
        // terminal; the orchestration is being torn down regardless.
        if (IsTerminal(state.Status))
        {
            return;
        }

        state.Status = ConsultGenerationJobStatuses.Running;
        state.StartedAtUtc ??= DateTimeOffset.UtcNow;
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    public async Task CompleteBlock(BlockGenerationResult result)
    {
        var state = EnsureState();
        var block = state.GetOrAddBlock(result.BlockId, result.BlockName);
        block.Status = ConsultGenerationBlockStatuses.Completed;
        block.GeneratedText = result.GeneratedText ?? string.Empty;
        block.Error = null;
        block.CompletedAtUtc = DateTimeOffset.UtcNow;
        state.History.Add(new JobHistoryEvent("success", $"Section completed: {result.BlockName}", null, DateTimeOffset.UtcNow));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// v6: stores the result aggregator's rendered output — the assembled
    /// document that IS the deliverable (package-format-v6-design.md § 4).
    /// </summary>
    public async Task CompleteDocument(string text)
    {
        var state = EnsureState();
        state.SchemaVersion = 6;
        state.AssembledDocument = text;
        state.History.Add(new JobHistoryEvent("success", "Assembled document produced.", null, DateTimeOffset.UtcNow));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// v7: stores one deliverable's rendered output (package-format-v7.md).
    /// Upsert keyed by ResultId, ordered by Ordinal; v6's CompleteDocument and
    /// AssembledDocument are untouched — the two shapes never mix in one record.
    /// </summary>
    public async Task CompleteResultDocument(ConsultGenerationResultDocument input)
    {
        var state = EnsureState();
        state.SchemaVersion = 7;
        state.AssembledDocuments ??= new List<ConsultGenerationResultDocumentState>();
        state.AssembledDocuments.RemoveAll(d => d.ResultId == input.ResultId);
        state.AssembledDocuments.Add(new ConsultGenerationResultDocumentState
        {
            ResultId = input.ResultId,
            Label = input.Label,
            Text = input.Text,
            Ordinal = input.Ordinal
        });
        state.AssembledDocuments.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
        state.History.Add(new JobHistoryEvent("success", $"Assembled document produced: {input.Label}", null, DateTimeOffset.UtcNow));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    public async Task FailBlock(BlockGenerationResult result)
    {
        var state = EnsureState();
        var block = state.GetOrAddBlock(result.BlockId, result.BlockName);
        block.Status = ConsultGenerationBlockStatuses.Failed;
        block.GeneratedText = null;
        block.Error = string.IsNullOrWhiteSpace(result.Error) ? "Section generation failed." : result.Error;
        block.CompletedAtUtc = DateTimeOffset.UtcNow;
        state.History.Add(new JobHistoryEvent("failure", $"Section failed: {result.BlockName}", block.Error, DateTimeOffset.UtcNow));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// One forEach instance completed: records the per-item node output (composite
    /// "nodeId:itemId" key, per-item provenance hashes) and the item's chain
    /// progress — the fields section-prose-step events are synthesized from.
    /// </summary>
    public void MarkNodeItemCompleted(ConsultGenerationNodeItemUpdate input)
    {
        var state = EnsureState();
        state.SchemaVersion = 6;

        var output = state.GetOrAddNodeOutput($"{input.NodeId}:{input.ItemId}", input.Label);
        output.NodeId = input.NodeId;
        output.ItemId = input.ItemId;
        output.Status = ConsultGenerationNodeStatuses.Completed;
        output.Concepts = input.Concepts?.ToList();
        output.InputHash = input.InputHash;
        output.OutputHash = input.OutputHash;
        output.HashVersion = input.HashVersion;
        output.CompletedAtUtc = DateTimeOffset.UtcNow;

        var progress = state.GetOrAddItemProgress(input.ItemId, input.ItemName);
        progress.Step = input.NodeId;
        progress.CompletedStepCount = input.CompletedChainCount;
        progress.TotalStepCount = input.TotalChainCount;
        State = state;
    }

    public void MarkNodeCompleted(ConsultGenerationNodeUpdate input)
    {
        var state = EnsureState();
        state.SchemaVersion = 6;

        var node = state.GetOrAddNodeOutput(input.NodeId, input.Label);
        node.Status = ConsultGenerationNodeStatuses.Completed;
        node.Concepts = input.Concepts?.ToList();
        node.InputHash = input.InputHash;
        node.OutputHash = input.OutputHash;
        node.HashVersion = input.HashVersion;
        node.CompletedAtUtc = DateTimeOffset.UtcNow;

        // v10 (#496): a classifier's answer, a declared value — on the node
        // and in the job's classifications, which the boundary reads.
        if (input.Classification != null)
        {
            node.Classification = input.Classification;
            state.Classifications ??= new Dictionary<string, string>(StringComparer.Ordinal);
            state.Classifications[input.NodeId] = input.Classification;
        }

        state.CompletedStageCount = input.CompletedNodeCount;
        state.TotalStageCount = input.TotalNodeCount;
        state.History.Add(new JobHistoryEvent("success", input.Label, null, DateTimeOffset.UtcNow));
        State = state;
    }

    public async Task MarkNodeFailed(ConsultGenerationNodeFailure input)
    {
        var state = EnsureState();
        state.SchemaVersion = 6;

        var node = state.GetOrAddNodeOutput(input.NodeId, input.Label);
        node.Status = ConsultGenerationNodeStatuses.Failed;
        node.Error = input.Error;
        node.CompletedAtUtc = DateTimeOffset.UtcNow;

        state.AnalysisStatus = input.Status;
        state.AnalysisError = input.Error;
        state.History.Add(new JobHistoryEvent("failure", input.Label, input.Error, DateTimeOffset.UtcNow));

        // The orchestrator holds the graph, so it computes the unreached set; skipped
        // nodes get entries and Skipped status here.
        foreach (var skipped in input.SkippedNodes)
        {
            var skippedNode = state.GetOrAddNodeOutput(skipped.Id, skipped.Label);
            skippedNode.Status = ConsultGenerationNodeStatuses.Skipped;
            state.History.Add(new JobHistoryEvent("skipped", skipped.Label, null, DateTimeOffset.UtcNow));
        }

        foreach (var block in state.Blocks.Values.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            state.History.Add(new JobHistoryEvent("skipped", $"Section not reached: {block.Name}", null, DateTimeOffset.UtcNow));
        }

        state.Status = ConsultGenerationJobStatuses.Failed;
        state.CompletedAtUtc = DateTimeOffset.UtcNow;
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// #202: called off before it ran. Its own operation rather than
    /// FinalizeJob, whose body branches on Completed and Failed only — routed
    /// through there a cancelled job would get no History event at all, and
    /// would inherit the "section not reached" bookkeeping that belongs to a
    /// failure. Nothing here ran, so there is nothing to reconcile.
    /// </summary>
    public async Task Cancel()
    {
        var state = EnsureState();
        state.Status = ConsultGenerationJobStatuses.Cancelled;
        state.CompletedAtUtc = DateTimeOffset.UtcNow;
        state.History.Add(new JobHistoryEvent(
            "skipped", "Cancelled before the scheduled run.", null, DateTimeOffset.UtcNow));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// #368: the retention sweep deletes the produced text — the assembled
    /// documents, the sections, the extracted concepts — and keeps everything
    /// else. Terminal jobs only; idempotent; the hashes were stored at
    /// completion (or are stamped now, for a record from before) so nothing
    /// about what was produced is lost, only the production itself.
    /// </summary>
    public async Task DropText(ConsultGenerationTextDrop input)
    {
        var state = EnsureState();
        if (!IsTerminal(state.Status) || state.TextDroppedAtUtc != null)
        {
            return;
        }

        state.StampOutputHashes();
        state.AssembledDocument = null;
        foreach (var document in state.AssembledDocuments ?? new List<ConsultGenerationResultDocumentState>())
        {
            document.Text = null;
        }

        foreach (var block in state.Blocks.Values)
        {
            block.GeneratedText = null;
        }

        foreach (var node in state.NodeOutputs?.Values ?? Enumerable.Empty<ConsultNodeOutputState>())
        {
            node.Concepts = null;
        }

        state.TextDroppedAtUtc = input.DroppedAtUtc;
        state.History.Add(new JobHistoryEvent("retention", "Produced text deleted (retention policy)", null, input.DroppedAtUtc));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>
    /// #486: delivery, recorded once on a terminal job. A record that says
    /// nothing is a pre-#486 record; one that says "address-not-set" is a job
    /// the user can see was never emailed.
    /// </summary>
    public async Task RecordDelivery(ConsultGenerationDeliveryRecord input)
    {
        var state = EnsureState();
        if (!IsTerminal(state.Status) || state.DeliveryOutcome != null)
        {
            return;
        }

        state.DeliveryOutcome = input.Outcome;
        state.DeliveryDocumentAttached = input.DocumentAttached;
        state.DeliveredAtUtc = input.Outcome == DeliveryOutcomes.Sent ? input.RecordedAtUtc : null;
        state.History.Add(new JobHistoryEvent(
            "delivery",
            input.Outcome switch
            {
                DeliveryOutcomes.Sent => input.DocumentAttached == true
                    ? "Emailed to the delivery address (document attached)"
                    : "Emailed to the delivery address (link only)",
                DeliveryOutcomes.AddressNotSet => "Not emailed — no delivery address on the account",
                DeliveryOutcomes.NotConfigured => "Not emailed — delivery not configured",
                _ => "Email failed"
            },
            null,
            input.RecordedAtUtc));
        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    /// <summary>The states nothing may move a job out of.</summary>
    internal static bool IsTerminal(string status) =>
        status is ConsultGenerationJobStatuses.Completed
            or ConsultGenerationJobStatuses.Failed
            or ConsultGenerationJobStatuses.Cancelled;

    public async Task FinalizeJob(ConsultGenerationJobFinalize input)
    {
        var state = EnsureState();
        state.Status = input.Status;
        state.CompletedAtUtc = DateTimeOffset.UtcNow;

        foreach (var node in (state.NodeOutputs?.Values ?? Enumerable.Empty<ConsultNodeOutputState>())
            .Where(node => node.Status == ConsultGenerationNodeStatuses.Running))
        {
            node.Status = ConsultGenerationNodeStatuses.Completed;
            node.CompletedAtUtc = DateTimeOffset.UtcNow;

            // The map node completes here rather than through MarkNodeCompleted, so
            // the stage/node count catches up here too (was reported 4/5 on completed
            // jobs otherwise).
            if (input.Status == ConsultGenerationJobStatuses.Completed)
            {
                state.CompletedStageCount = state.TotalStageCount;
            }
        }

        if (input.Status == ConsultGenerationJobStatuses.Completed)
        {
            state.History.Add(new JobHistoryEvent("success", "Done", null, DateTimeOffset.UtcNow));
            state.StampOutputHashes();
        }
        else if (input.Status == ConsultGenerationJobStatuses.Failed)
        {
            state.FailureError = input.Error;
            state.History.Add(new JobHistoryEvent("failure", "Failed", input.Error, DateTimeOffset.UtcNow));

            foreach (var block in state.Blocks.Values
                .Where(b => b.Status is not (ConsultGenerationBlockStatuses.Completed or ConsultGenerationBlockStatuses.Failed))
                .OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                state.History.Add(new JobHistoryEvent("skipped", $"Section not reached: {block.Name}", null, DateTimeOffset.UtcNow));
            }
        }

        State = state;

        await _indexStore.UpsertAsync(state.ToIndexEntry(), CancellationToken.None);
    }

    [Function(nameof(ConsultGenerationJobEntity))]
    public static Task RunEntityAsync([EntityTrigger] TaskEntityDispatcher dispatcher)
    {
        return dispatcher.DispatchAsync<ConsultGenerationJobEntity>();
    }

    private ConsultGenerationJobState EnsureState()
    {
        return State ?? ConsultGenerationJobState.Create(string.Empty, string.Empty, Array.Empty<IReadOnlyDictionary<string, string>>());
    }

}

public sealed record ConsultGenerationOrchestrationInput(
    ConsultGenerationRequest Request,
    string AppUserId,
    string? WorkflowPackage = null,
    string? EffectiveInputHash = null,
    IReadOnlyList<ConsultItemStepDescriptor>? ItemSteps = null,
    IReadOnlyList<ConsultNodeDescriptor>? Nodes = null,
    string? ResultNodeId = null,
    IReadOnlyList<IReadOnlyDictionary<string, string>>? Items = null,
    IReadOnlyDictionary<string, string>? DataScalars = null,
    int EffectiveInputHashVersion = 2,
    // v8: declared input id -> type, so the renderer can hand Scriban a real
    // DateOnly or bool instead of a string. Trailing optional, null for jobs
    // already in flight (#215/#217).
    IReadOnlyDictionary<string, string>? InputTypes = null,
    // #315: carried so the completion reply can name them.
    IReadOnlyList<ConsultSkippedDocument>? SkippedDocuments = null,
    string? CatalogRef = null,
    // v6 (package-format-v6-design.md): one item set per fanned collection,
    // keyed by collection id. Non-null selects the v6 path; Items then carries
    // the deliverable's BLOCKS (the result aggregator's expansion) for the
    // entity's section model, while the fan reads these sets.
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? Collections = null,
    // #158: how the job was submitted ("app" | "email"; null = pre-#158 record)
    // and, for email jobs, where the completion reply goes. Append-only for
    // Durable payload compatibility.
    string? Source = null,
    string? ReplyToAddress = null,
    // v7 (package-format-v7.md): the result set (non-null selects per-
    // deliverable execution) and the effective input map (every declared id
    // present; absent optional inputs as empty strings). Null on every v5/v6
    // job — sleeping instances replay through the legacy arms untouched.
    IReadOnlyList<ConsultResultDescriptor>? Results = null,
    IReadOnlyDictionary<string, string>? Inputs = null,
    // #238: where each input's text came from, as observed by the server at
    // job start. Null on every job recorded before this existed, and on every
    // job whose inputs were typed — absence means "not recorded", never
    // "typed" (docs/DOCUMENT_INPUT.md § 7).
    IReadOnlyDictionary<string, ConsultInputOrigin>? InputOrigins = null,
    // v9 (#428): one origin per document, positionally — origins[id][i]
    // describes element i. The single-origin slot above stays for instances
    // in flight (a scheduled job sleeps up to seven days); new jobs write
    // only this one. Appended last: this is the payload a sleeping instance
    // re-reads.
    IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? InputDocumentOrigins = null,
    // #398: the format registry version the package is read by, and the
    // provenance contract version the record conforms to — package-format@v…
    // and provenance@v…, from the build's attestation. Appended last: this
    // is the payload a sleeping instance re-reads.
    string? PackageFormatRef = null,
    string? ProvenanceRef = null,
    // #403: the terminology edition the server had loaded when the job
    // started, and the server's build. Appended last, same reason.
    TerminologySnapshot? Terminology = null,
    string? TerminologyServerRef = null,
    // v10 (#496): the package has classifiers — the fire set is decided at the
    // boundary, so Items is empty here and the decision activity needs the
    // supplied values (each as its wire JSON, ConsultInputValue.AsJson) to
    // evaluate the conditions. Appended last, same reason.
    bool? Deciding = null,
    IReadOnlyDictionary<string, string>? SuppliedInputs = null,
    // #514: where the job ran and what ran it — the deployment's canonical
    // public host (Public__ApiHost; null when it names none) and the engine
    // build's commit, both from the attestation. Appended last, same reason.
    string? ApiHost = null,
    string? EngineCommit = null);

public sealed record ConsultGenerationJobInitialize(
    string JobId,
    string AppUserId,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Items,
    string? WorkflowPackage = null,
    string? EffectiveInputHash = null,
    IReadOnlyList<ConsultItemStepDescriptor>? ItemSteps = null,
    IReadOnlyList<ConsultNodeDescriptor>? Nodes = null,
    int EffectiveInputHashVersion = 2,
    string? CatalogRef = null,
    string? Source = null,
    DateTimeOffset? ScheduledAtUtc = null,
    // #238: see ConsultGenerationOrchestrationInput.InputOrigins.
    IReadOnlyDictionary<string, ConsultInputOrigin>? InputOrigins = null,
    // #315: deliverables the package declared and this job will not produce.
    IReadOnlyList<ConsultSkippedDocument>? SkippedDocuments = null,
    // #361: see ConsultGenerationJobResponse.Collections.
    IReadOnlyList<ConsultCollectionRoster>? Collections = null,
    // #373: the PACKAGE's specVersion — what rules the manifest this job ran
    // was written against. Distinct from SchemaVersion below, which is this
    // record's own storage shape; the two are unrelated ladders that happen to
    // collide at 7.
    //
    // Appended last on purpose: ConsultGenerationEngine calls Initialize
    // POSITIONALLY, so a parameter inserted anywhere else rebinds the arguments
    // after it and compiles while quietly corrupting provenance.
    int? PackageSpecVersion = null,
    // v9 (#428): see ConsultGenerationOrchestrationInput.InputDocumentOrigins.
    // Appended last for the same reason as PackageSpecVersion.
    IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? InputDocumentOrigins = null,
    // #432: the package's title as the pinned manifest carried it — what the
    // reader sees beside the ref, as it was when the job ran. Appended last
    // for the reason PackageSpecVersion gives.
    string? PackageTitle = null,
    // #453: and its tags, as the pinned manifest carried them — authored
    // labels, the safety class of the title; null before v9, an empty list
    // for a v9 package that declared none. Appended last, same reason.
    IReadOnlyList<string>? PackageTags = null,
    // #398: see ConsultGenerationOrchestrationInput. Appended last, same reason.
    string? PackageFormatRef = null,
    string? ProvenanceRef = null,
    // #403: see ConsultGenerationOrchestrationInput. Appended last, same reason.
    TerminologySnapshot? Terminology = null,
    string? TerminologyServerRef = null,
    // v10 (#496): the package has classifiers, so the fire set — and with it
    // the count, the blocks, the pruned nodes — is decided at the boundary,
    // not here. Null on every job before, and on every package without a
    // classifier: those are decided at start. Appended last, same reason.
    bool? Deciding = null,
    // #514: see ConsultGenerationOrchestrationInput. Appended last, same reason.
    string? ApiHost = null,
    string? EngineCommit = null);

public sealed record ConsultGenerationNodeUpdate(
    string NodeId,
    string Label,
    IReadOnlyList<ClinicalConcept>? Concepts,
    string? InputHash,
    string? OutputHash,
    int CompletedNodeCount,
    int TotalNodeCount,
    // #375: the pair's definition. Appended last for the reason Initialize's
    // trailing fields give: the engine calls this positionally.
    int? HashVersion = null,
    // v10 (#496): a classifier's normalised answer. Appended last, same reason.
    string? Classification = null);

/// <summary>v10 (#496): what the boundary decided — the one Decide signal.</summary>
public sealed record ConsultGenerationDecision(
    IReadOnlyList<IReadOnlyDictionary<string, string>> Items,
    IReadOnlyList<ConsultNodeDescriptor>? Nodes,
    IReadOnlyList<ConsultResultDescriptor>? Results,
    IReadOnlyList<ConsultSkippedDocument>? SkippedDocuments,
    IReadOnlyList<ConsultCollectionRoster>? Collections,
    IReadOnlyList<ConsultItemStepDescriptor>? ItemSteps,
    IReadOnlyDictionary<string, string>? Classifications,
    DateTimeOffset DecidedAtUtc);

public static class ConsultGenerationDecisionFailureKinds
{
    /// <summary>A classifier failed, so the fire set could not be decided.</summary>
    public const string CouldNotDecide = "could-not-decide";
    /// <summary>The classifiers answered and no deliverable's condition held.</summary>
    public const string NothingApplied = "nothing-applied";
}

/// <summary>v10 (#496): a job that ends in the deciding stage.</summary>
public sealed record ConsultGenerationDecisionFailure(
    string Kind,
    string Reason,
    IReadOnlyDictionary<string, string>? Classifications,
    IReadOnlyList<ConsultSkippedDocument>? SkippedDocuments = null);

public sealed record ConsultGenerationNodeFailure(
    string NodeId,
    string Label,
    string Status,
    string Error,
    IReadOnlyList<ConsultItemStepDescriptor> SkippedNodes);

public sealed record ConsultGenerationJobFinalize(string Status, string? Error = null);

/// <summary>#368: the retention sweep's one signal — when the text is deleted.</summary>
public sealed record ConsultGenerationTextDrop(DateTimeOffset DroppedAtUtc);

/// <summary>
/// #486: what happened to the completion email, written once when the job
/// ends. The address itself is never on the record — it is the account's.
/// </summary>
public static class DeliveryOutcomes
{
    /// <summary>The email went out (with or without the document attached — see DocumentAttached).</summary>
    public const string Sent = "sent";
    /// <summary>The reply activity failed after its retries.</summary>
    public const string Failed = "failed";
    /// <summary>An app job on an account with no verified delivery address.</summary>
    public const string AddressNotSet = "address-not-set";
    /// <summary>The mailbox or app URL is not configured on this deployment.</summary>
    public const string NotConfigured = "not-configured";
}

/// <summary>#486: the orchestrator's one delivery signal.</summary>
public sealed record ConsultGenerationDeliveryRecord(
    string Outcome,
    DateTimeOffset RecordedAtUtc,
    bool? DocumentAttached = null);

/// <summary>
/// #434: everything Initialize would have been given, plus the sentence that
/// says why nothing will run — authored package content (deliverable labels
/// and what each condition wanted), never a supplied value.
/// </summary>
public sealed record ConsultGenerationJobStartFailure(ConsultGenerationJobInitialize Initialize, string Reason);

/// <summary>
/// One completed v7 deliverable: the result aggregator's rendered output with
/// its authored identity. Ordinal is the result-set position — aggregators
/// complete in data-dependent order, so the position travels with the payload.
/// </summary>
public sealed record ConsultGenerationResultDocument(
    string ResultId,
    string Label,
    string Text,
    int Ordinal);

/// <summary>
/// One forEach instance's completion: per-item provenance plus the item's chain
/// progress (the fields section-prose-step events are synthesized from).
/// </summary>
public sealed record ConsultGenerationNodeItemUpdate(
    string NodeId,
    string Label,
    string ItemId,
    string ItemName,
    IReadOnlyList<ClinicalConcept>? Concepts,
    string? InputHash,
    string? OutputHash,
    int CompletedChainCount,
    int TotalChainCount,
    // #375: appended last, same reason.
    int? HashVersion = null);

public sealed class ConsultGenerationJobState
{
    public string JobId { get; set; } = string.Empty;
    public string AppUserId { get; set; } = string.Empty;
    public string Status { get; set; } = ConsultGenerationJobStatuses.Queued;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public int TotalBlockCount { get; set; }
    /// <summary>
    /// The shape of THIS RECORD, not of the package — which fields below a
    /// reader should trust. Stamped by whichever code path produced the record
    /// (CompleteDocument writes 6, CompleteResultDocument 7), never declared by
    /// anyone, and never refused: a record at 2 is read forever.
    ///
    /// PackageSpecVersion is the one an outside reader wants (#373).
    /// </summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>
    /// Every value SchemaVersion has ever been stamped with — the published
    /// fact in provenance-versions.json (#400), pinned against this list by
    /// test. A new storage shape is added here and there in the same change.
    /// </summary>
    public static readonly IReadOnlyList<int> StorageVersions = new[] { 2, 6, 7 };

    /// <summary>#373: the specVersion of the package this job ran.</summary>
    public int? PackageSpecVersion { get; set; }

    /// <summary>#432: the package's title at the pinned version; null when untitled, and on every job before it.</summary>
    public string? PackageTitle { get; set; }
    /// <summary>#453: the package's tags at the pinned version; null before v9 and on every job before it.</summary>
    public List<string>? PackageTags { get; set; }
    public string? AnalysisStatus { get; set; }
    public string? AnalysisError { get; set; }
    public int CompletedStageCount { get; set; }
    public int TotalStageCount { get; set; }
    // The deliverable's blocks (#175): v5 = the sections; v6 = the result
    // aggregator's expansion (composite "sourceNodeId:itemId" keys for forEach
    // sources, node ids for scalar sources).
    public Dictionary<string, ConsultGenerationBlockState> Blocks { get; set; } = new();

    // Per-forEach-item chain progress (#175), keyed by the plain item id —
    // the fields section-prose-step events are synthesized from. Disjoint
    // from Blocks by design; the old dual-purpose Sections dict is gone
    // (stored records were wiped prerelease, no legacy shape survives).
    public Dictionary<string, ConsultGenerationItemProgressState> ItemProgress { get; set; } = new();
    public List<JobHistoryEvent> History { get; set; } = new();
    public string? FailureError { get; set; }
    // #434: set when the job was created already Failed because no deliverable
    // applied to its inputs. Nothing ran: FailureError stays null, there are
    // no blocks, and TotalBlockCount is a stated zero. Null on every job that
    // started. v10 (#496): also a job that ended in the deciding stage, with
    // DecisionFailureKind saying which way.
    public string? StartFailure { get; set; }

    // v10 (#496): the boundary. Deciding is stamped at Initialize for a job
    // whose package has classifiers; DecidedAtUtc when the fire set was
    // decided — at start for every other job, at the boundary for one that was
    // deciding. Null DecidedAtUtc with Deciding true = "not yet decided", the
    // named state a reader shows instead of a zero count.
    public bool? Deciding { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    // v10 (#496): what the classifiers answered, by node id — declared values,
    // printable; the fire set was decided over them.
    public Dictionary<string, string>? Classifications { get; set; }
    public string? DecisionFailureKind { get; set; }
    public string? WorkflowPackage { get; set; }
    public string? EffectiveInputHash { get; set; }

    // #238: per-slot record of where the input text came from. Null for every
    // job whose inputs were typed, and for every job predating this field.
    public Dictionary<string, ConsultInputOrigin>? InputOrigins { get; set; }
    // v9 (#428): the same record per document, positionally. Added beside
    // InputOrigins rather than in its place: records at rest hold the single
    // shape, and a converter on a durable payload is a worse bargain than a
    // second field. A record has one or the other, never both; ToResponse
    // projects either into the one response map.
    public Dictionary<string, List<ConsultInputOrigin>>? InputDocumentOrigins { get; set; }
    // The effective-input hash definition this job used: null/1 = draft+sections
    // (pre-v5, historical); 2 = draft only (v5/v6); 3 = the declared inputs as
    // strings (v7); 4 = the typed scalars (v8); 5 = structured values with
    // sorted field ids and UTF-8 as-is (v9). Never compared across versions —
    // docs/customizable-workflow/provenance.md.
    public int? EffectiveInputHashVersion { get; set; }

    // LEGACY, read-only since #105: records ≤ 2026-07-17 stored the contract →
    // agent-version map; later records carry catalogRef only (the catalog version
    // document holds the mapping). Kept so old records keep serving their map.
    public Dictionary<string, string>? AgentVersions { get; set; }

    // The concrete output-contract catalog version this job ran under
    // (output-contracts@vYYYY.MM.N) — the registry artifact resolving every
    // agentVersions entry (#93; docs/customizable-workflow/provenance.md).
    public string? CatalogRef { get; set; }

    // #398: the format registry version whose documents define the rules this
    // job's package was interpreted under (package-format@vYYYY.MM.N), and the
    // provenance contract version this record conforms to (provenance@v…) —
    // the build's own, write-once like CatalogRef; null before 2026-08-25.
    public string? PackageFormatRef { get; set; }
    public string? ProvenanceRef { get; set; }

    // #403: the SNOMED CT edition the terminology server had loaded when the
    // job started (what every concept's flags were answered against) and the
    // server's build (snomed-snowstorm-mcp@<commit>); write-once; null when
    // the server could not be read at start and on records from before.
    public TerminologySnapshot? Terminology { get; set; }
    public string? TerminologyServerRef { get; set; }

    // #514: the canonical public host of the deployment that ran the job —
    // where the data was processed (the residency statement) — and the
    // engine build that ran it. Write-once; null when the deployment names no
    // host, and on records from before.
    public string? ApiHost { get; set; }
    public string? EngineCommit { get; set; }
    public List<ConsultItemStepDescriptor>? ItemSteps { get; set; }
    public List<ConsultNodeDescriptor>? Nodes { get; set; }

    // #361: the job's own fan rosters, stamped once beside Nodes. Null on every
    // job recorded before the field existed, which the client falls back from.
    public List<ConsultCollectionRoster>? Collections { get; set; }
    public Dictionary<string, ConsultNodeOutputState>? NodeOutputs { get; set; }

    // v6: the result aggregator's rendered output — the deliverable itself
    // (stored text, the same species as sections' GeneratedText).
    public string? AssembledDocument { get; set; }

    // v7: one entry per completed deliverable, ordered by result-set position.
    // Never set on the same record as AssembledDocument.
    public List<ConsultGenerationResultDocumentState>? AssembledDocuments { get; set; }

    // #368: the deliverable hash, stored once at completion rather than
    // derived on every read, so it outlives the text it was computed over.
    // Null on records completed before 2026-08-25, which ToResponse derives.
    public string? WorkflowOutputHash { get; set; }
    public int? WorkflowOutputHashVersion { get; set; }

    // #368: when the produced text was deleted under the retention policy.
    // After it: no AssembledDocument(s) text, no GeneratedText, no Concepts;
    // every hash, node, ref and label stays. Null while the text is present.
    public DateTimeOffset? TextDroppedAtUtc { get; set; }

    // #486: what happened to the completion email (DeliveryOutcomes); null
    // on records from before, or while the job is still running.
    public string? DeliveryOutcome { get; set; }
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public bool? DeliveryDocumentAttached { get; set; }

    // #315: the deliverables this job's inputs excluded. Recorded, so a
    // reader is never left inferring a missing document from a shorter list.
    public List<ConsultSkippedDocument>? SkippedDocuments { get; set; }

    // #158: how the job was submitted ("app" | "email"; null = pre-#158 record).
    public string? Source { get; set; }

    // #157: when a scheduled job was/is due to start (null = immediate job).
    public DateTimeOffset? ScheduledAtUtc { get; set; }

    public static ConsultGenerationJobState Create(
        string jobId,
        string appUserId,
        IReadOnlyList<IReadOnlyDictionary<string, string>> items)
    {
        return new ConsultGenerationJobState
        {
            JobId = jobId,
            AppUserId = appUserId,
            Status = ConsultGenerationJobStatuses.Queued,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            TotalBlockCount = items.Count,
            Blocks = items.ToDictionary(
                item => item["id"],
                item => new ConsultGenerationBlockState
                {
                    Id = item["id"],
                    Name = item.GetValueOrDefault("name", item["id"]),
                    Status = ConsultGenerationBlockStatuses.Pending
                })
        };
    }

    public ConsultGenerationJobIndexEntry ToIndexEntry()
    {
        return new ConsultGenerationJobIndexEntry(
            JobId,
            AppUserId,
            Status,
            CreatedAtUtc,
            StartedAtUtc,
            CompletedAtUtc,
            TotalBlockCount,
            Blocks.Values.Count(b => b.Status == ConsultGenerationBlockStatuses.Completed),
            Blocks.Values.Count(b => b.Status == ConsultGenerationBlockStatuses.Failed),
            Source,
            ScheduledAtUtc,
            FailedAtStart: StartFailure != null,
            TextDroppedAtUtc: TextDroppedAtUtc,
            DeliveryOutcome: DeliveryOutcome,
            DeliveredAtUtc: DeliveredAtUtc,
            Deciding: Deciding == true && DecidedAtUtc == null && StartFailure == null,
            DecisionFailureKind: DecisionFailureKind);
    }

    public ConsultNodeOutputState GetOrAddNodeOutput(string nodeId, string label)
    {
        NodeOutputs ??= new Dictionary<string, ConsultNodeOutputState>(StringComparer.Ordinal);

        if (!NodeOutputs.TryGetValue(nodeId, out var node))
        {
            node = new ConsultNodeOutputState { NodeId = nodeId, Label = label };
            NodeOutputs[nodeId] = node;
        }

        return node;
    }

    public ConsultGenerationBlockState GetOrAddBlock(string blockId, string blockName)
    {
        if (!Blocks.TryGetValue(blockId, out var block))
        {
            block = new ConsultGenerationBlockState
            {
                Id = blockId,
                Name = blockName,
                Status = ConsultGenerationBlockStatuses.Pending
            };

            Blocks[blockId] = block;
        }

        return block;
    }

    public ConsultGenerationItemProgressState GetOrAddItemProgress(string itemId, string itemName)
    {
        if (!ItemProgress.TryGetValue(itemId, out var progress))
        {
            progress = new ConsultGenerationItemProgressState { Id = itemId, Name = itemName };
            ItemProgress[itemId] = progress;
        }

        return progress;
    }

    /// <summary>
    /// #428: the one response map, from whichever field this record holds. A
    /// #238-era record's single origin becomes a one-element list — it was one
    /// document, and the reader sees it as such.
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? ProjectInputOrigins()
    {
        if (InputDocumentOrigins is { Count: > 0 })
        {
            return InputDocumentOrigins.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<ConsultInputOrigin>)pair.Value.AsReadOnly(),
                StringComparer.Ordinal);
        }

        return InputOrigins?.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ConsultInputOrigin>)new[] { pair.Value },
            StringComparer.Ordinal);
    }

    /// <summary>
    /// #368: compute the deliverable hashes from the text once and keep them.
    /// Write-once; a no-op when already stamped or when there is no text to
    /// hash. The same three-way dispatch ToResponse derived by until now.
    /// </summary>
    public void StampOutputHashes()
    {
        if (Status != ConsultGenerationJobStatuses.Completed || TextDroppedAtUtc != null)
        {
            return;
        }

        foreach (var document in AssembledDocuments ?? new List<ConsultGenerationResultDocumentState>())
        {
            document.DocumentHash ??= document.Text == null ? null : ConsultGenerationProvenance.Sha256Hex(document.Text);
        }

        if (WorkflowOutputHash != null)
        {
            return;
        }

        if (AssembledDocuments is { Count: > 0 } && AssembledDocuments.All(d => d.Text != null))
        {
            WorkflowOutputHash = ConsultGenerationProvenance.ComputeResultSetHash(
                AssembledDocuments.ToDictionary(d => d.ResultId, d => d.Text!, StringComparer.Ordinal));
            WorkflowOutputHashVersion = ConsultGenerationProvenance.ResultSetHashVersion;
        }
        else if (AssembledDocument != null)
        {
            WorkflowOutputHash = ConsultGenerationProvenance.ComputeAssembledDocumentHash(AssembledDocument);
            WorkflowOutputHashVersion = ConsultGenerationProvenance.AssembledDocumentHashVersion;
        }
        else if (AssembledDocuments is not { Count: > 0 })
        {
            WorkflowOutputHash = ConsultGenerationProvenance.ComputeWorkflowOutputHash(
                Blocks.Values.Where(b => b.Status == ConsultGenerationBlockStatuses.Completed)
                    .ToDictionary(b => b.Id, b => b.GeneratedText ?? string.Empty));
            WorkflowOutputHashVersion = ConsultGenerationProvenance.WorkflowOutputHashVersion;
        }
    }

    public ConsultGenerationJobResponse ToResponse()
    {
        // #368: once the text is deleted the sections are gone too — an empty
        // map, not empty strings, so no reader mistakes absence for content.
        var completedSections = Blocks.Values
            .Where(block => block.Status == ConsultGenerationBlockStatuses.Completed && (TextDroppedAtUtc == null || block.GeneratedText != null))
            .ToDictionary(block => block.Id, block => block.GeneratedText ?? string.Empty);

        var failedSections = Blocks.Values
            .Where(block => block.Status == ConsultGenerationBlockStatuses.Failed)
            .ToDictionary(block => block.Id, block => block.Error ?? "Section generation failed.");

        var itemProgress = ItemProgress.Values
            .ToDictionary(
                progress => progress.Id,
                progress => new ConsultGenerationItemProgress(
                    progress.Id,
                    progress.Name,
                    progress.Step,
                    progress.CompletedStepCount,
                    progress.TotalStepCount));

        return new ConsultGenerationJobResponse(
            JobId,
            AppUserId,
            Status,
            // The stored scalar (the phase-7 decision), with the block count as
            // the seed-time fallback.
            TotalBlockCount > 0 ? TotalBlockCount : Blocks.Count,
            completedSections.Count,
            failedSections.Count,
            completedSections,
            failedSections,
            completedSections.Count > 0,
            SchemaVersion,
            AnalysisStatus,
            AnalysisError,
            CompletedStageCount,
            TotalStageCount,
            itemProgress,
            CreatedAtUtc: CreatedAtUtc,
            StartedAtUtc: StartedAtUtc,
            CompletedAtUtc: CompletedAtUtc,
            RuntimeFailureError: FailureError,
            History: History.Count > 0 ? History.AsReadOnly() : null,
            WorkflowPackage: WorkflowPackage,
            EffectiveInputHash: EffectiveInputHash,
            InputOrigins: ProjectInputOrigins(),
            SkippedDocuments: SkippedDocuments,
            Source: Source,
            ScheduledAtUtc: ScheduledAtUtc,
            ItemSteps: ItemSteps,
            Nodes: Nodes,
            Collections: Collections,
            NodeOutputs: NodeOutputs?.ToDictionary(
                pair => pair.Key,
                pair => new ConsultGenerationNodeStatusResponse(
                    pair.Value.NodeId,
                    pair.Value.Label,
                    pair.Value.Status,
                    pair.Value.InputHash,
                    pair.Value.OutputHash,
                    pair.Value.CompletedAtUtc,
                    pair.Value.Error,
                    pair.Value.HashVersion,
                    pair.Value.Classification)),
            AgentVersions: AgentVersions,
            EffectiveInputHashVersion: EffectiveInputHashVersion,
            CatalogRef: CatalogRef,
            PackageFormatRef: PackageFormatRef,
            ProvenanceRef: ProvenanceRef,
            Terminology: Terminology,
            TerminologyServerRef: TerminologyServerRef,
            ApiHost: ApiHost,
            EngineCommit: EngineCommit,
            PackageSpecVersion: PackageSpecVersion,
            PackageTitle: PackageTitle,
            StartFailure: StartFailure,
            PackageTags: PackageTags,
            // #368: stored at completion since 2026-08-25 (StampOutputHashes);
            // a record from before derives on read as it always did, by the
            // same three-way dispatch: v7 document set → v3, v6 single
            // document → v2, v5 sections → v1. The deliverable hash of a
            // partial job is undefined, so only completed jobs carry it.
            WorkflowOutputHash: Status != ConsultGenerationJobStatuses.Completed
                ? null
                : WorkflowOutputHash
                    ?? (AssembledDocuments is { Count: > 0 }
                        ? ConsultGenerationProvenance.ComputeResultSetHash(
                            AssembledDocuments.ToDictionary(d => d.ResultId, d => d.Text ?? string.Empty, StringComparer.Ordinal))
                        : AssembledDocument != null
                            ? ConsultGenerationProvenance.ComputeAssembledDocumentHash(AssembledDocument)
                            : ConsultGenerationProvenance.ComputeWorkflowOutputHash(completedSections)),
            WorkflowOutputHashVersion: Status != ConsultGenerationJobStatuses.Completed
                ? null
                : WorkflowOutputHashVersion
                    ?? (AssembledDocuments is { Count: > 0 }
                        ? ConsultGenerationProvenance.ResultSetHashVersion
                        : AssembledDocument != null
                            ? ConsultGenerationProvenance.AssembledDocumentHashVersion
                            : ConsultGenerationProvenance.WorkflowOutputHashVersion),
            AssembledDocument: Status == ConsultGenerationJobStatuses.Completed ? AssembledDocument : null,
            AssembledDocuments: Status == ConsultGenerationJobStatuses.Completed && AssembledDocuments is { Count: > 0 }
                ? AssembledDocuments
                    .Select(d => new ConsultGenerationResultDocumentResponse(
                        d.ResultId,
                        d.Label,
                        d.Text,
                        d.DocumentHash ?? (d.Text == null ? null : ConsultGenerationProvenance.Sha256Hex(d.Text))))
                    .ToList()
                : null,
            TextDroppedAtUtc: TextDroppedAtUtc,
            DeliveryOutcome: DeliveryOutcome,
            DeliveredAtUtc: DeliveredAtUtc,
            DeliveryDocumentAttached: DeliveryDocumentAttached,
            Deciding: Deciding,
            DecidedAtUtc: DecidedAtUtc,
            Classifications: Classifications,
            DecisionFailureKind: DecisionFailureKind);
    }
}

/// <summary>One deliverable block: its status and, when finished, its text or error.</summary>
public sealed class ConsultGenerationBlockState
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = ConsultGenerationBlockStatuses.Pending;
    public string? GeneratedText { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
}

/// <summary>One completed v7 deliverable: authored identity plus the rendered text.</summary>
public sealed class ConsultGenerationResultDocumentState
{
    public string ResultId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    // #368: null once the retention policy deleted it; DocumentHash stays.
    public string? Text { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    // #368: stored at completion; null on records from before (derived then).
    public string? DocumentHash { get; set; }
}

/// <summary>One forEach item's chain progress — the section-prose-step source.</summary>
public sealed class ConsultGenerationItemProgressState
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Step { get; set; }
    public int CompletedStepCount { get; set; }
    public int TotalStepCount { get; set; } = ConsultGenerationItemSteps.TotalStepCount;
}

/// <summary>Per-node run state: status, concepts (for JSON nodes), and provenance hashes.</summary>
public sealed class ConsultNodeOutputState
{
    public string NodeId { get; set; } = string.Empty;

    // Set on per-item entries (composite "nodeId:itemId" keys); null on scalar nodes.
    public string? ItemId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Status { get; set; } = ConsultGenerationNodeStatuses.Running;
    public List<ClinicalConcept>? Concepts { get; set; }
    public string? InputHash { get; set; }
    public string? OutputHash { get; set; }
    // #375: the definition both hashes were computed under; null on records
    // from before the ladder (hash-definitions.md § 4 says what that means).
    public int? HashVersion { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string? Error { get; set; }
    // v10 (#496): a classifier's answer — a declared value.
    public string? Classification { get; set; }
}

public static class ConsultGenerationNodeStatuses
{
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Skipped = "Skipped";
}

public static class ConsultGenerationJobStatuses
{
    public const string Queued = "Queued";
    // #157: initialized with a future ScheduledAtUtc; the orchestrator sleeps
    // on a durable timer, then MarkRunning flips it to Running.
    public const string Scheduled = "Scheduled";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    // #202: a Scheduled job called off before its timer fired. Terminal, and
    // distinct from Failed: nothing went wrong and nothing was spent.
    public const string Cancelled = "Cancelled";
}

public static class ConsultGenerationActivityNames
{
    public const string RunPromptNode = "run-prompt-node";
    // v10 (#496): the boundary — decide the fire set over the classifier values.
    public const string DecideDeliverables = "decide-deliverables";
}

public static class ConsultGenerationItemSteps
{
    /// <summary>The single SSE event name for every prose step; the payload carries the step id and label.</summary>
    public const string EventName = "item-step";

    /// <summary>Deserialization default for pre-milestone-3 job snapshots without a step list.</summary>
    public const int TotalStepCount = 3;
}

public static class ConsultGenerationBlockStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
