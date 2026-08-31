using System.Text.Json.Serialization;

using Consultologist.PackageFormat;
using Consultologist.Api.Workflow;

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
    Dictionary<string, List<InputFilePayload>>? InputFiles = null,
    // #510: the same slots, filled from the account's own previous runs. A
    // slot maps to the deliverables it is copied from, in order — a text slot
    // one, an array of text one per element. The server copies the text at
    // start and records the origin, so a reference is resolved, never
    // trusted: the caller names a run and a deliverable, not what they said.
    // A slot appears in one map only.
    Dictionary<string, List<ConsultInputRef>>? InputRefs = null);

/// <summary>#510: one deliverable of one of the account's completed runs.</summary>
public sealed record ConsultInputRef(string JobId, string ResultId);

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
    bool TrackedChangesResolved = false,
    // #512: the file and its reading, each as a digest. FileSha256 is over
    // the bytes as received — before any decoding, the one thing the bytes
    // leave behind, since they are never kept; TextSha256 over the extracted
    // text as it enters the effective-input map (normalised: line endings to
    // LF, trailing whitespace off). Equal file digests with unequal text
    // digests say the extraction changed, not the document. Null on records
    // from before 2026-08-28. Neither is a name.
    string? FileSha256 = null,
    string? TextSha256 = null,
    // #510: for a previous-run element, the run it was copied from and the
    // deliverable — the same account's, resolved by the server at start. The
    // copy's TextSha256 is over the canonical text as it entered the
    // effective-input map. Null on every other kind.
    string? SourceJobId = null,
    string? SourceResultId = null);

public static class ConsultInputOriginKinds
{
    // Extracted from a document by the parser (#235). Kebab-case like every
    // other disposition in this codebase; OCR would join it as its own kind.
    public const string Document = "document";

    // #510: copied at start from one of the account's own completed runs.
    // Byte-for-byte the deliverable: a copy the user edited is typed text and
    // carries no origin.
    public const string PreviousRun = "previous-run";
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
    // #238: where the input text came from, as the server observed it. Null
    // when nothing was recorded — which is every job before this field
    // existed and every job whose inputs were typed.
    //
    // v9 (#428): one origin per document, positionally. A job recorded
    // before this with one document reads as a one-element list.
    IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? InputOrigins = null,
    // #315: declared deliverables this job's inputs excluded, with the reason.
    IReadOnlyList<ConsultSkippedDocument>? SkippedDocuments = null,
    // #361: each forEach collection's items as this job's package declared
    // them, so the run rail draws a fan from the job rather than from whatever
    // is pinned now. Null on every job recorded before the field existed.
    IReadOnlyList<ConsultCollectionRoster>? Collections = null,
    // #373: the package format this job ran under. Null on every job recorded
    // before it was captured — an absent value is the record saying it does not
    // know, which the client renders as no chip rather than a guess.
    int? PackageSpecVersion = null,
    // #432: the package's title at the pinned version, beside the ref. Null on
    // an untitled package and on every job before it; the ref is the fallback.
    string? PackageTitle = null,
    // #434: why this job was created already Failed — no deliverable applied
    // to its inputs. Null on every job that started; RuntimeFailureError is
    // null whenever this is set. Two fields, because "nothing ran" and "ran
    // and failed" are different facts and a reader should not infer one from
    // an event label.
    string? StartFailure = null,
    // #453: the package's tags as they were at the pinned version — stamped,
    // since History cannot read the manifest. Null before v9 and on every
    // job before it; empty for a v9 package that declared none.
    IReadOnlyList<string>? PackageTags = null,
    // #398: the rules the package was read by (package-format@v…) and the
    // contract this record conforms to (provenance@v…); null before 2026-08-25.
    string? PackageFormatRef = null,
    string? ProvenanceRef = null,
    // #403: the terminology edition the run was answered against, and the
    // server that served it; null before 2026-08-25 or when unreadable at start.
    TerminologySnapshot? Terminology = null,
    string? TerminologyServerRef = null,
    // #368: when the produced text was deleted under the retention policy; null while present.
    DateTimeOffset? TextDroppedAtUtc = null,
    // #486: what happened to the completion email (DeliveryOutcomes), when it
    // was sent, and whether the document was attached; null before 2026-08-26
    // or while the job runs.
    string? DeliveryOutcome = null,
    DateTimeOffset? DeliveredAtUtc = null,
    bool? DeliveryDocumentAttached = null,
    // v10 (#496): the boundary. Deciding true with DecidedAtUtc null is "not
    // yet decided" — the count is not known; DecidedAtUtc is when the fire set
    // was decided (start, for every job without a classifier); Classifications
    // what the classifiers answered; DecisionFailureKind why a job ended in
    // the deciding stage ("could-not-decide" | "nothing-applied").
    bool? Deciding = null,
    DateTimeOffset? DecidedAtUtc = null,
    IReadOnlyDictionary<string, string>? Classifications = null,
    string? DecisionFailureKind = null,
    // #514: where the job ran (the deployment's canonical public host — the
    // data-residency statement; null when the deployment names none) and the
    // engine build that ran it. Null on records from before 2026-08-28.
    string? ApiHost = null,
    string? EngineCommit = null,
    // #557: where the produced text lives (container + name, never a URL).
    // Null on pre-#557 records, on Failed jobs, and when the completion
    // write failed and the text stayed on the entity.
    ConsultOutputsBlobPointer? OutputsBlob = null);

/// <summary>
/// One v7 deliverable on the job response: authored id and label, the text, and
/// its digest — the same per-document hash the v3 result-set hash is computed
/// over, so History displays the definition rather than a parallel one.
/// </summary>
public sealed record ConsultGenerationResultDocumentResponse(
    string ResultId,
    string Label,
    // #368: null once the retention policy deleted the text; DocumentHash stays.
    string? Text,
    string? DocumentHash = null,
    // v11 #513 (provenance § 7): what was appended after the aggregated
    // sections, in applied order. Null on every record without appends.
    IReadOnlyList<ConsultAppendedEntry>? Appended = null,
    // v11 #516: true when the package marked this deliverable signed and no
    // block was chosen on the profile — produced unsigned, said by name.
    // Only true or null, never false: a signed-and-appended document stores
    // nothing here.
    bool? Unsigned = null);

/// <summary>
/// v11 #513 (provenance § 7): one block appended to a deliverable's assembled
/// document after the aggregated sections — a macro now, the signature from
/// rung (c). The job's package version pins the template's bytes; the text
/// itself is inside the document and its hash.
/// </summary>
public sealed record ConsultAppendedEntry(
    string Kind,
    string Id,
    // v11 #516: the signature's as-of date (yyyy-MM-dd — when the block was
    // last edited, as snapshotted at start). Null on macro entries and on
    // entries stored before this field existed. Appended last — entries are
    // stored payloads.
    string? AsOf = null);

public static class ConsultAppendedKinds
{
    public const string Macro = "macro";

    public const string Signature = "signature";
}

/// <summary>
/// #557 (storage-separation.md § 2.2): where a completed job's text lives —
/// the container (which carries the account's kind) and the blob name,
/// never a URL. Part of the record: kept after the text is dropped, when
/// textDroppedAtUtc gates every read of it.
/// </summary>
public sealed record ConsultOutputsBlobPointer(string Container, string Name);

/// <summary>
/// #547 (storage-separation.md § 2.1): where a job's held inputs live —
/// container + name, never a URL. Kept after the drop;
/// inputsDroppedAtUtc gates every read of it.
/// </summary>
public sealed record ConsultInputsBlobPointer(string Container, string Name);

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
    IReadOnlyList<string>? Aggregate = null,
    // v10 (#495): a classifier's declared values. Trailing optional, so the
    // snapshot a sleeping job re-reads binds as it did.
    IReadOnlyList<string>? Values = null,
    // v11 #550 (record § 6): the package's claim that this node's output is
    // the same for the same input — carried for #549's rerun verdict, never
    // enforced at run time. Only true or null, never false. Appended last,
    // same reason as Values.
    bool? Reproducible = null);

public sealed record ConsultNodeBindingDescriptor(string From, string? As = null);

/// <summary>
/// One deliverable of a v7 job, snapshotted from the resolved package's result
/// set at start — a Jobs-layer type so registry records never enter durable
/// payloads.
/// </summary>
public sealed record ConsultResultDescriptor(
    string Id,
    string NodeId,
    string Label,
    // v11 #513: the macro ids appended to this deliverable, in declared order.
    // Null on every pre-v11 job — appended last, this is a durable payload.
    IReadOnlyList<string>? Macros = null,
    // v11 #516: the package marks this deliverable signed. Null on every
    // pre-v11 job — appended last, same reason.
    bool? Signature = null);

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
    string? Error = null,
    // #375: the definition the pair was computed under; absent before the ladder.
    int? HashVersion = null,
    // v10 (#496): a classifier's answer, a declared value.
    string? Classification = null);

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
