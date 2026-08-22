using System.Text.Json.Serialization;

namespace Consultologist.Api.Models;

public record ConsultGenerationRequest(
    // Exactly one of ConsultDraft / Inputs per request. The legacy field stays
    // valid for every package; against a v7 package it back-fills the
    // consult_draft slot (package-format-v7.md).
    string? ConsultDraft,
    string? WorkflowPackage = null,
    // #157: run later — the orchestrator sleeps on a durable timer until
    // this time. Null = run immediately; past values also run immediately.
    DateTimeOffset? ScheduledAtUtc = null,
    // v7: the named-input map (declared id → value). Validated against the
    // package declaration at job start.
    //
    // v8 types the value on the wire: a JSON string for text, date and enum,
    // a JSON boolean for boolean (package-format-v8-design.md § 4). A v7
    // caller's {"id": "text"} is unchanged and still valid — every v5–v7
    // input is a text slot.
    Dictionary<string, ConsultInputValue>? Inputs = null,
    // #238: the same slots, filled by documents instead of text. The server
    // extracts these at job start (docs/DOCUMENT_INPUT.md § 5), so a slot's
    // origin is something the server observed rather than something the
    // caller asserted. A slot appears in one map or the other, never both.
    //
    // v9 (#428): a slot maps to its documents, in the order supplied. One
    // document is a one-element list. A slot declared an array of text takes
    // several, each becoming one element; a text slot takes exactly one, and
    // the starter refuses more once it knows the declaration
    // (package-format-v9-design.md § 7).
    Dictionary<string, List<InputFilePayload>>? InputFiles = null);

/// <summary>
/// A document supplied for an input slot. System.Text.Json serialises byte[]
/// as base64, so this rides the existing JSON body — no multipart parser for
/// untrusted input, no upload-then-reference staging, and no bytes at rest.
///
/// No filename: it can itself be PHI ("Smith_John_referral.pdf"), the parser
/// dispatches on content anyway, and a request-scoped one would land in
/// Functions request logging.
/// </summary>
public sealed record InputFilePayload(string ContentType, byte[] Content);

/// <summary>
/// #238: what the server observed about where an input's text came from.
/// Recorded beside the effective-input hash and never inside it — per
/// document, positionally, since v9 (#428): origins[id][i] describes element
/// i of the slot.
///
/// Absence means "not recorded" — never "typed". Email jobs supply text until
/// #237 lands, and every job recorded before this existed has no entry at
/// all, so reading a missing entry as an assertion about the input would be
/// a claim nobody made.
/// </summary>
public sealed record ConsultInputOrigin(
    string Kind,
    string? Extractor = null,
    int? PageCount = null,
    // #240: the document carried tracked changes and the accepted view was
    // taken. A reviewer asking why a consult says something the referral did
    // not can see that a revision layer existed and was resolved.
    bool TrackedChangesResolved = false);

public static class ConsultInputOriginKinds
{
    // Extracted from a document by the parser (#235). Kebab-case like every
    // other disposition in this codebase; OCR would join it as its own kind.
    public const string Document = "document";
}

public record ConsultGenerationJobStartResponse(
    string JobId,
    string StatusUrl);

public record ConsultGenerationJobResponse(
    string JobId,
    string AppUserId,
    string Status,
    int TotalBlockCount,
    int CompletedBlockCount,
    int FailedBlockCount,
    Dictionary<string, string> GeneratedBlocks,
    Dictionary<string, string> FailedBlocks,
    bool Success,
    int? SchemaVersion = null,
    string? AnalysisStatus = null,
    string? AnalysisError = null,
    int? CompletedStageCount = null,
    int? TotalStageCount = null,
    IReadOnlyDictionary<string, ConsultGenerationItemProgress>? ItemProgress = null,
    string? RuntimeFailureStage = null,
    string? RuntimeFailureError = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    IReadOnlyList<JobHistoryEvent>? History = null,
    string? WorkflowPackage = null,
    string? EffectiveInputHash = null,
    IReadOnlyList<ConsultItemStepDescriptor>? ItemSteps = null,
    IReadOnlyList<ConsultNodeDescriptor>? Nodes = null,
    IReadOnlyDictionary<string, ConsultGenerationNodeStatusResponse>? NodeOutputs = null,
    IReadOnlyDictionary<string, string>? AgentVersions = null,
    int? EffectiveInputHashVersion = null,
    string? CatalogRef = null,
    string? WorkflowOutputHash = null,
    int? WorkflowOutputHashVersion = null,
    // v6: the result aggregator's rendered output — the deliverable itself
    // (Completed jobs only; hash version 2 covers exactly these bytes).
    string? AssembledDocument = null,
    // #158: how the job was submitted ("app" | "email"; null = pre-#158 record).
    string? Source = null,
    // #157: when a scheduled job was/is due to start (null = immediate job).
    DateTimeOffset? ScheduledAtUtc = null,
    // v7: the per-deliverable documents in result-set order (Completed jobs
    // only; hash version 3 covers exactly these). Null on v5/v6 jobs.
    IReadOnlyList<ConsultGenerationResultDocumentResponse>? AssembledDocuments = null,
    // #238: per-slot record of where the input text came from, as the server
    // observed it. Null when nothing was recorded — which is every job before
    // this field existed and every job whose inputs were typed.
    IReadOnlyDictionary<string, ConsultInputOrigin>? InputOrigins = null,
    // #315: declared deliverables this job's inputs excluded, with the reason.
    IReadOnlyList<ConsultSkippedDocument>? SkippedDocuments = null,
    // #361: each forEach collection's items as this job's package declared
    // them, so the run rail draws a fan from the job rather than from whatever
    // is pinned now. Null on every job recorded before the field existed.
    IReadOnlyList<ConsultCollectionRoster>? Collections = null,
    // #373: the package format this job ran under. Null on every job recorded
    // before it was captured — an absent value is the record saying it does not
    // know, which the client renders as no chip rather than a guess.
    int? PackageSpecVersion = null);

/// <summary>
/// One v7 deliverable on the job response: authored id and label, the text, and
/// its digest — the same per-document hash the v3 result-set hash is computed
/// over, so History displays the definition rather than a parallel one.
/// </summary>
public sealed record ConsultGenerationResultDocumentResponse(
    string ResultId,
    string Label,
    string Text,
    string? DocumentHash = null);

/// <summary>
/// The identity and display label of one per-item chain step, snapshotted from the
/// job's workflow package at start.
/// </summary>
public sealed record ConsultItemStepDescriptor(string Id, string Label);

/// <summary>
/// One forEach collection's items as the job's package declared them, snapshotted
/// at start (#361): the run rail draws a fan's rows from this rather than from
/// whatever package happens to be pinned when someone looks at the run.
///
/// Deliberately slim — id and display name only. The orchestrator's copy of a
/// collection carries every field including <c>content</c>, which is the whole
/// standards text; none of that belongs on a status response.
/// </summary>
public sealed record ConsultCollectionRoster(string CollectionId, IReadOnlyList<ConsultCollectionItem> Items);

public sealed record ConsultCollectionItem(string Id, string Name);

/// <summary>
/// One node of the job's workflow DAG, snapshotted from the pinned package at start —
/// the orchestrator's whole worldview of the graph (Durable replay never re-reads the
/// registry for shape).
/// </summary>
public sealed record ConsultNodeDescriptor(
    string Id,
    string Label,
    string? PromptId = null,
    IReadOnlyDictionary<string, ConsultNodeBindingDescriptor>? Bindings = null,
    string? OutputContract = null,
    string? FailIfEmpty = null,
    string? ForEach = null,
    string? ConceptSource = null,
    IReadOnlyList<string>? Aggregate = null);

public sealed record ConsultNodeBindingDescriptor(string From, string? As = null);

/// <summary>
/// One deliverable of a v7 job, snapshotted from the resolved package's result
/// set at start — a Jobs-layer type so registry records never enter durable
/// payloads.
/// </summary>
public sealed record ConsultResultDescriptor(string Id, string NodeId, string Label);

/// <summary>
/// A deliverable the package declared and this job did not produce, because its
/// condition did not hold (package-format-v8-design.md § 5).
///
/// Recorded rather than omitted. A job that produces fewer documents than its
/// package declares and says nothing is indistinguishable from a deliverable
/// that silently failed — which is the failure #315 was written against.
///
/// Reason names the input, its supplied value and what the condition wanted.
/// Safe on every surface: labels and enum values are authored package content,
/// a boolean is true or false, and none of it is free text.
/// </summary>
public sealed record ConsultSkippedDocument(string ResultId, string Label, string Reason);

/// <summary>
/// Per-node run status and provenance exposed on the job response — the hashes form
/// the step-level verification chain (dag-improvements #6). Concepts stay off the
/// wire; they live in entity state.
/// </summary>
public sealed record ConsultGenerationNodeStatusResponse(
    string NodeId,
    string Label,
    string Status,
    string? InputHash = null,
    string? OutputHash = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? Error = null);

/// <summary>#390: the body of a reschedule — a job id in the route, a time here.</summary>
public sealed record RescheduleConsultRequest(DateTimeOffset? ScheduledAtUtc);

public record JobHistoryEvent(string Kind, string Label, string? Detail, DateTimeOffset OccurredAt);

public record BlockGenerationResult(
    string BlockId,
    string BlockName,
    bool Success,
    string? GeneratedText,
    string? Error);

public sealed record ClinicalConcept(
    string Term,
    string Type,
    string Id,
    bool IsSnomedConcept,
    bool IsActive,
    string Source,
    string? Support = null);

public sealed record ConsultGenerationItemProgress(
    string ItemId,
    string ItemName,
    string? Step,
    int CompletedStepCount,
    int TotalStepCount);
