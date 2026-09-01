using Consultologist.Web.Services.Locations;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using System.Runtime.CompilerServices;

namespace Consultologist.Web.Services.AI;

public interface IAIEndpointService
{
    /// <summary>
    /// Starts a job from the package's declared inputs. v5/v6 packages accept
    /// the one-entry consult_draft map the setup form synthesizes for them, so
    /// there is one client path across both eras (package-format-v7.md).
    /// </summary>
    Task<ConsultGenerationJobStartResponse> StartConsultGenerationJobAsync(
        IReadOnlyDictionary<string, ConsultInputValue> inputs,
        string? workflowPackage = null,
        DateTimeOffset? scheduledAtUtc = null,
        IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>? files = null,
        // #510: slots filled from previous runs — references the server resolves.
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>? refs = null,
        // #540: per filled input, the held form response its value came from —
        // an assertion beside the value, verified by the server at start.
        IReadOnlyDictionary<string, ConsultInputFormRef>? formRefs = null);

    Task<ConsultGenerationJobResponse> GetConsultGenerationJobAsync(string jobId);

    /// <summary>#202: call off a scheduled run before its timer fires.</summary>
    Task CancelConsultGenerationJobAsync(string jobId);

    /// <summary>
    /// #390: move a scheduled run to a different time. Returns the NEW job id —
    /// rescheduling cancels and re-creates, so the old id is gone.
    /// </summary>
    Task<string> RescheduleConsultGenerationJobAsync(string jobId, DateTimeOffset scheduledAtUtc);

    /// <summary>
    /// #549: run a completed consult again on its held inputs and the same
    /// exact package version. Server-side start from the source job id alone
    /// — the typed inputs never enter the browser. Returns the NEW job id.
    /// </summary>
    Task<string> RerunConsultGenerationJobAsync(string jobId);

    /// <summary>#546: who used this run — the links index's rows, ids only.</summary>
    Task<IReadOnlyList<ConsultJobLinkResponse>> GetConsultGenerationJobLinksAsync(string jobId);

    string GetConsultGenerationJobEventsUrl(string jobId, string attemptId, string? lastEventId = null);

    IAsyncEnumerable<ConsultGenerationJobSseEvent> StreamConsultGenerationJobEventsAsync(
        string jobId,
        string attemptId,
        CancellationToken cancellationToken,
        string? lastEventId = null);
}

public class AIEndpointService : IAIEndpointService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IApiLocations _locations;
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ILogger<AIEndpointService> _logger;

    public AIEndpointService(
        HttpClient httpClient,
        IConfiguration configuration,
        IApiLocations locations,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        ILogger<AIEndpointService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _locations = locations;
        _accessTokenProvider = accessTokenProvider;
        _navigation = navigation;
        _logger = logger;
    }

    /// <summary>
    /// What to throw for a non-success response (#348).
    ///
    /// The transport answers a refusal with <c>{ "error": "…" }</c>, and that
    /// string is the whole point of the refusal — it names the input, the
    /// document or the wait. Discarding it left every 422 on screen as the
    /// word "UnprocessableEntity", which cannot be acted on and cannot be
    /// told apart from the five other refusals sharing that status.
    ///
    /// Anything that is not that shape stays a transport failure: an
    /// infrastructure 502 carries an HTML page, not an answer, and putting it
    /// in front of a clinician would be worse than the status code.
    /// </summary>
    private async Task<Exception> DescribeFailureAsync(HttpResponseMessage response, string operation)
    {
        var body = await response.Content.ReadAsStringAsync();

        _logger.LogError(
            "{Operation} failed with status {StatusCode}: {Error}",
            operation,
            response.StatusCode,
            body);

        return ReadErrorDetail(body) is { } detail
            ? new ConsultGenerationRefusedException(response.StatusCode, detail, ReadRefusalJobId(body))
            : new HttpRequestException($"Azure Function call failed: {response.StatusCode}");
    }

    private static string? ReadErrorDetail(string body) => ReadStringProperty(body, "error");

    // #434: the one refusal that left a row says where it is. Read the same
    // tolerant way as the sentence; absent or null means no row.
    private static string? ReadRefusalJobId(string body) => ReadStringProperty(body, "jobId");

    private static string? ReadStringProperty(string body, string name)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // Matched case-insensitively: the property is written lowercase by
            // the transport, but the same shape reaches here from middleware
            // whose serializer casing is not ours to assume.
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return property.Value.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<ConsultGenerationJobStartResponse> StartConsultGenerationJobAsync(
        IReadOnlyDictionary<string, ConsultInputValue> inputs,
        string? workflowPackage = null,
        DateTimeOffset? scheduledAtUtc = null,
        IReadOnlyDictionary<string, IReadOnlyList<InputFilePayload>>? files = null,
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputRef>>? refs = null,
        IReadOnlyDictionary<string, ConsultInputFormRef>? formRefs = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var functionUrl = _locations.Url(ApiRoutes.ConsultGenerationJobs);
            var request = new ConsultGenerationRequest(
                null,
                workflowPackage,
                scheduledAtUtc,
                inputs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                files is { Count: > 0 }
                    ? files.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.Ordinal)
                    : null,
                refs is { Count: > 0 }
                    ? refs.ToDictionary(pair => pair.Key, pair => pair.Value.ToList(), StringComparer.Ordinal)
                    : null,
                formRefs is { Count: > 0 }
                    ? formRefs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                    : null);

            _logger.LogInformation(
                "Starting consult generation job at {Url}. InputCount={InputCount}, InputLength={InputLength}",
                functionUrl,
                inputs.Count,
                // TextLength, so a boolean counts 4 or 5, a number its digits
                // and structure the text inside it, none of it throwing: this
                // is a log line, not the size cap.
                inputs.Values.Sum(value => value.TextLength));

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, functionUrl)
            {
                Content = JsonContent.Create(request)
            };
            await AddAuthorizationAsync(httpRequest);

            var response = await _httpClient.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                throw await DescribeFailureAsync(response, "Consult generation job start");
            }

            var result = await response.Content.ReadFromJsonAsync<ConsultGenerationJobStartResponse>();

            if (result == null)
            {
                _logger.LogError("Failed to deserialize consult generation job start response");
                throw new InvalidOperationException("Failed to deserialize response");
            }

            _logger.LogInformation(
                "Consult generation job started. JobId={JobId}, ElapsedMs={ElapsedMs}",
                result.JobId,
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error starting consult generation job. ExceptionType={ExceptionType}, Message={Message}, ElapsedMs={ElapsedMs}",
                ex.GetType().FullName,
                ex.Message,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    public async Task<string> RescheduleConsultGenerationJobAsync(string jobId, DateTimeOffset scheduledAtUtc)
    {
        var functionUrl = _locations.Url(ApiRoutes.ConsultGenerationJobs);
        var url = $"{functionUrl.TrimEnd('/')}/{Uri.EscapeDataString(jobId)}/Reschedule";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { scheduledAtUtc })
        };
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw await DescribeFailureAsync(response, "Consult reschedule");
        }

        var payload = await response.Content.ReadFromJsonAsync<RescheduleConsultResponse>();

        return payload?.JobId
            ?? throw new InvalidOperationException("Reschedule succeeded but returned no job id.");
    }

    public async Task<string> RerunConsultGenerationJobAsync(string jobId)
    {
        var functionUrl = _locations.Url(ApiRoutes.ConsultGenerationJobs);
        var url = $"{functionUrl.TrimEnd('/')}/{Uri.EscapeDataString(jobId)}/Rerun";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            // Carries the server's sentence — a 409 names which state refused
            // (not held, deleted on a date, not completed).
            throw await DescribeFailureAsync(response, "Consult rerun");
        }

        var payload = await response.Content.ReadFromJsonAsync<RerunConsultResponse>();

        return payload?.JobId
            ?? throw new InvalidOperationException("Rerun succeeded but returned no job id.");
    }

    public async Task<IReadOnlyList<ConsultJobLinkResponse>> GetConsultGenerationJobLinksAsync(string jobId)
    {
        var functionUrl = _locations.Url(ApiRoutes.ConsultGenerationJobs);
        var url = $"{functionUrl.TrimEnd('/')}/{Uri.EscapeDataString(jobId)}/Links";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            throw await DescribeFailureAsync(response, "Consult links");
        }

        var payload = await response.Content.ReadFromJsonAsync<ConsultJobLinksResponse>();

        return payload?.UsedBy ?? Array.Empty<ConsultJobLinkResponse>();
    }

    public async Task CancelConsultGenerationJobAsync(string jobId)
    {
        var functionUrl = _locations.Url(ApiRoutes.ConsultGenerationJobs);
        var cancelUrl = $"{functionUrl.TrimEnd('/')}/{Uri.EscapeDataString(jobId)}/Cancel";

        using var request = new HttpRequestMessage(HttpMethod.Post, cancelUrl);
        await AddAuthorizationAsync(request);

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            // #348/#369: carries the server's sentence when it sent one, so a
            // 409 says which state refused rather than "the call failed".
            throw await DescribeFailureAsync(response, "Consult cancel");
        }
    }

    public async Task<ConsultGenerationJobResponse> GetConsultGenerationJobAsync(string jobId)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var functionUrl = _locations.Url(ApiRoutes.ConsultGenerationJobs);
            var statusUrl = $"{functionUrl.TrimEnd('/')}/{Uri.EscapeDataString(jobId)}";

            _logger.LogDebug("Polling consult generation job at {Url}. JobId={JobId}", statusUrl, jobId);

            using var request = new HttpRequestMessage(HttpMethod.Get, statusUrl);
            await AddAuthorizationAsync(request);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                throw await DescribeFailureAsync(response, "Consult generation job poll");
            }

            var result = await response.Content.ReadFromJsonAsync<ConsultGenerationJobResponse>();

            if (result == null)
            {
                _logger.LogError("Failed to deserialize consult generation job response");
                throw new InvalidOperationException("Failed to deserialize response");
            }

            _logger.LogDebug(
                "Consult generation job polled. JobId={JobId}, Status={Status}, Completed={CompletedCount}, Failed={FailedCount}, ElapsedMs={ElapsedMs}",
                result.JobId,
                result.Status,
                result.CompletedBlockCount,
                result.FailedBlockCount,
                stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error polling consult generation job. JobId={JobId}, ExceptionType={ExceptionType}, Message={Message}, ElapsedMs={ElapsedMs}",
                jobId,
                ex.GetType().FullName,
                ex.Message,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    public string GetConsultGenerationJobEventsUrl(string jobId, string attemptId, string? lastEventId = null)
    {
        var functionUrl = _locations.Url(ApiRoutes.ConsultGenerationJobs);
        return $"{functionUrl.TrimEnd('/')}/{Uri.EscapeDataString(jobId)}/events?attemptId={Uri.EscapeDataString(attemptId)}";
    }

    public async IAsyncEnumerable<ConsultGenerationJobSseEvent> StreamConsultGenerationJobEventsAsync(
        string jobId,
        string attemptId,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? lastEventId = null)
    {
        var eventsUrl = GetConsultGenerationJobEventsUrl(jobId, attemptId, lastEventId);

        using var request = new HttpRequestMessage(HttpMethod.Get, eventsUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (!string.IsNullOrWhiteSpace(lastEventId))
        {
            request.Headers.TryAddWithoutValidation("Last-Event-ID", lastEventId);
        }

        await AddAuthorizationAsync(request);
        request.SetBrowserResponseStreamingEnabled(true);

        _logger.LogInformation(
            "Opening consult generation SSE stream at {Url}. JobId={JobId}, AttemptId={AttemptId}, ResumeCursorPresent={ResumeCursorPresent}",
            eventsUrl,
            jobId,
            attemptId,
            !string.IsNullOrWhiteSpace(lastEventId));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Consult generation SSE stream failed with status {StatusCode}: {Error}",
                response.StatusCode,
                errorContent);

            throw new HttpRequestException($"Azure Function SSE stream failed: {response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var parser = SseParser.Create(stream);

        await foreach (var item in parser.EnumerateAsync(cancellationToken))
        {
            yield return new ConsultGenerationJobSseEvent(item.EventType, item.Data, item.EventId);
        }
    }

    private async Task AddAuthorizationAsync(HttpRequestMessage request)
    {
        var apiScope = _configuration["AzureFunction:ApiScope"];

        if (string.IsNullOrWhiteSpace(apiScope))
        {
            throw new InvalidOperationException("AzureFunction:ApiScope is not configured.");
        }

        var tokenResult = await _accessTokenProvider.RequestAccessToken(new AccessTokenRequestOptions
        {
            Scopes = new[] { apiScope }
        });

        if (!tokenResult.TryGetToken(out var token))
        {
            throw new AccessTokenNotAvailableException(_navigation, tokenResult, new[] { apiScope });
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
    }
}

public record ConsultGenerationRequest(
    string? ConsultDraft,
    string? WorkflowPackage = null,
    DateTimeOffset? ScheduledAtUtc = null,
    // v8: typed on the wire — a JSON string for text, date and enum, a JSON
    // boolean for boolean. A bare string still means text, so the v7 shape is
    // unchanged for every package that declares no types.
    Dictionary<string, ConsultInputValue>? Inputs = null,
    // #238: a slot filled by documents instead of text. The server extracts
    // them at job start, so origin is observed rather than asserted.
    //
    // v9 (#428): a slot maps to its documents in the order supplied; one
    // document is a one-element list. Only a slot declared an array of text
    // takes several (package-format-v9-design.md § 7). The setup form still
    // attaches one per slot — the picker is #429's.
    Dictionary<string, List<InputFilePayload>>? InputFiles = null,
    // #510: a slot filled from the account's own previous runs — one
    // deliverable per element; the server copies the text at start and
    // records the origin. A slot appears in one map only.
    Dictionary<string, List<ConsultInputRef>>? InputRefs = null,
    // #540: mirrors the Api's InputFormRefs — per filled input, the held
    // form response its value was loaded from, an assertion BESIDE the
    // value in Inputs, verified by the server at start.
    Dictionary<string, ConsultInputFormRef>? InputFormRefs = null);

/// <summary>Mirrors Consultologist.Api.Models.ConsultInputRef.</summary>
public sealed record ConsultInputRef(string JobId, string ResultId);

/// <summary>Mirrors Consultologist.Api.Models.ConsultInputFormRef.</summary>
public sealed record ConsultInputFormRef(string FormId, string ResponseId);

/// <summary>Mirrors Consultologist.Api.Models.InputFilePayload. byte[] rides
/// the JSON body as base64; no filename is sent.</summary>
public sealed record InputFilePayload(string ContentType, byte[] Content);

/// <summary>Mirrors Consultologist.Api.Models.ConsultInputOrigin. Absence
/// means "not recorded", never "typed". One per document, positionally, since
/// v9 (#428).</summary>
public sealed record ConsultInputOrigin(
    string Kind,
    string? Extractor = null,
    int? PageCount = null,
    bool TrackedChangesResolved = false,
    // #512: mirrors the Api's FileSha256 / TextSha256 — the file as received and its reading.
    string? FileSha256 = null,
    string? TextSha256 = null,
    // #510: mirrors the Api's SourceJobId / SourceResultId — a previous-run element's source.
    string? SourceJobId = null,
    string? SourceResultId = null,
    // #540: mirrors the Api's SourceFormId / SourceResponseId — a form-response element's source.
    string? SourceFormId = null,
    string? SourceResponseId = null);
public record ConsultGenerationJobStartResponse(string JobId, string StatusUrl);

/// <summary>#390: the new job a reschedule created, and the one it replaced.</summary>
public record RescheduleConsultResponse(string JobId, string CancelledJobId, DateTimeOffset ScheduledAtUtc);

/// <summary>#549: the new job and the run it replays.</summary>
public record RerunConsultResponse(string JobId, string SourceJobId);

/// <summary>#546: one "used by" edge — mirrors the Api's ConsultJobLinkResponse.</summary>
public sealed record ConsultJobLinkResponse(string JobId, string Kind, string? InputId = null, string? ResultId = null);

/// <summary>#551: mirrors the Api's ConsultTokenUsage — counts, never text; absent is not recorded, never zero.</summary>
public sealed record ConsultTokenUsage(int Input, int Output);

public sealed record ConsultJobLinksResponse(IReadOnlyList<ConsultJobLinkResponse>? UsedBy);
public record ConsultGenerationJobSseEvent(string EventName, string Json, string? EventId = null);
public record ConsultGenerationJobResponse(
    string JobId,
    string? AppUserId,
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
    IReadOnlyList<ConsultGenerationJobHistoryEvent>? History = null,
    IReadOnlyList<ConsultGenerationNodeDescriptor>? Nodes = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? StartedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? WorkflowPackage = null,
    string? EffectiveInputHash = null,
    int? EffectiveInputHashVersion = null,
    IReadOnlyDictionary<string, string>? AgentVersions = null,
    string? CatalogRef = null,
    string? WorkflowOutputHash = null,
    int? WorkflowOutputHashVersion = null,
    IReadOnlyList<ConsultItemStepDescriptor>? ItemSteps = null,
    IReadOnlyDictionary<string, ConsultGenerationNodeStatus>? NodeOutputs = null,
    // v6: the result aggregator's rendered output — the deliverable itself
    // (Completed jobs only; workflowOutputHash v2 is its digest).
    string? AssembledDocument = null,
    // #158: how the job was submitted ("app" | "email"; null = pre-#158 record).
    string? Source = null,
    // #157: when a scheduled job was/is due to start (null = immediate job).
    DateTimeOffset? ScheduledAtUtc = null,
    // v7: one entry per deliverable in result-set order (Completed jobs only;
    // workflowOutputHash v3 covers exactly these documents' digests).
    IReadOnlyList<ConsultGenerationResultDocumentResponse>? AssembledDocuments = null,
    // #238: where each input's text came from, as the server observed it.
    // v9 (#428): one origin per document, in order; a job recorded before
    // that with one document reads as a one-element list.
    IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? InputOrigins = null,
    // #315: declared deliverables this job's inputs excluded, with the reason.
    // Silence here is the failure mode the issue was written against.
    IReadOnlyList<ConsultSkippedDocumentResponse>? SkippedDocuments = null,
    // #361: each forEach collection's items as this job's package declared
    // them, so the run rail draws a fan from the job rather than from whatever
    // is pinned now. Null on every job recorded before the field existed.
    IReadOnlyList<ConsultCollectionRoster>? Collections = null,
    // #373: the package format this job ran under — the one version on the
    // provenance row an outside reader can act on. Null on every job recorded
    // before it was captured, which the row renders as no chip rather than a
    // guess.
    int? PackageSpecVersion = null,
    // #432: the package's title as it was at the pinned version; null when
    // untitled, and the chip shows the ref alone.
    string? PackageTitle = null,
    // #434: mirrors the Api's ConsultGenerationJobResponse.StartFailure — why
    // the job was created already Failed. Null on every job that started.
    string? StartFailure = null,
    // #453: mirrors the Api's ConsultGenerationJobResponse.PackageTags — the
    // pinned manifest's tags as they were when the job ran.
    IReadOnlyList<string>? PackageTags = null,
    // #398: package-format@v… and provenance@v… as the job recorded them; null before.
    string? PackageFormatRef = null,
    string? ProvenanceRef = null,
    // #403: mirrors the Api's Terminology / TerminologyServerRef.
    TerminologySnapshot? Terminology = null,
    string? TerminologyServerRef = null,
    // #368: when the produced text was deleted under the retention policy.
    DateTimeOffset? TextDroppedAtUtc = null,
    // #486: mirrors the Api's DeliveryOutcome / DeliveredAtUtc / DeliveryDocumentAttached.
    string? DeliveryOutcome = null,
    DateTimeOffset? DeliveredAtUtc = null,
    bool? DeliveryDocumentAttached = null,
    // v10 (#496): mirrors the Api's Deciding / DecidedAtUtc / Classifications /
    // DecisionFailureKind — the boundary, and what the classifiers answered.
    bool? Deciding = null,
    DateTimeOffset? DecidedAtUtc = null,
    IReadOnlyDictionary<string, string>? Classifications = null,
    string? DecisionFailureKind = null,
    // #514: mirrors the Api's ApiHost / EngineCommit — where the job ran and what ran it.
    string? ApiHost = null,
    string? EngineCommit = null,
    // #547: the held effective inputs (hydrated server-side while held) and
    // when the retention drop deleted them. HeldInputs is null once dropped
    // or for a job never held; the pointer itself is not mirrored.
    IReadOnlyDictionary<string, string>? HeldInputs = null,
    DateTimeOffset? InputsDroppedAtUtc = null,
    // #582: a rerun's lineage and judgment — mirrors the Api's trailing trio.
    string? RerunOf = null,
    string? RerunVerdict = null,
    string? RerunDivergence = null,
    // #551: the job's token totals, stamped once at completion.
    ConsultTokenUsage? Tokens = null);

/// <summary>Mirrors Consultologist.Api.Workflow.TerminologySnapshot (#403).</summary>
public record TerminologySnapshot(string? Edition, string? Version, string? ImportDate);

/// <summary>Mirrors Consultologist.Api.Models.ConsultSkippedDocument.</summary>
public record ConsultSkippedDocumentResponse(string ResultId, string Label, string Reason);

/// <summary>One v7 deliverable: authored identity, the document, and its digest.</summary>
public record ConsultGenerationResultDocumentResponse(
    string ResultId,
    string Label,
    // #368: null once the retention policy deleted the text; DocumentHash stays.
    string? Text,
    string? DocumentHash = null,
    // v11 #516: produced unsigned although the package requested a signature —
    // true or absent, never false. (Appended[] is deliberately not mirrored;
    // unknown JSON properties are ignored on deserialize.)
    bool? Unsigned = null);

/// <summary>
/// One node of the job's workflow DAG (v5: one kind, ForEach as multiplicity).
/// The provenance panel joins OutputContract with AgentVersions per row.
/// </summary>
public record ConsultGenerationNodeDescriptor(
    string Id,
    string Label,
    string? PromptId = null,
    string? OutputContract = null,
    string? ForEach = null,
    // #361: already serialized by the API, merely undeclared here. An
    // aggregator is what a result node is in every package the block resolver
    // accepts, which is the one thing the rail needed and the wire lacked.
    IReadOnlyList<string>? Aggregate = null,
    // #582: the package's reproducibility claim (v11 #550) — same #361
    // situation, the wire always carried it; the comparison table marks the
    // stages the verdict counted.
    bool? Reproducible = null);

public record ConsultItemStepDescriptor(string Id, string Label);

/// <summary>#361: one forEach collection's items as the job's package declared them.</summary>
public record ConsultCollectionRoster(string CollectionId, IReadOnlyList<ConsultCollectionItem> Items);

public record ConsultCollectionItem(string Id, string Name);

/// <summary>One chain entry: keyed "nodeId" (node level) or "nodeId:itemId" (per item).</summary>
public record ConsultGenerationNodeStatus(
    string NodeId,
    string Label,
    string Status,
    string? InputHash,
    string? OutputHash,
    DateTimeOffset? CompletedAtUtc,
    string? Error,
    // #375: the definition the pair was computed under; null before the ladder.
    int? HashVersion = null,
    // v10 (#496): a classifier's answer.
    string? Classification = null,
    // #551: what this instance's call cost; null is not recorded, never 0.
    ConsultTokenUsage? Tokens = null);

public record ConsultGenerationJobHistoryEvent(string Kind, string Label, string? Detail, DateTimeOffset OccurredAt);

public record ConsultGenerationItemProgress(
    string ItemId,
    string ItemName,
    string? Step,
    int CompletedStepCount,
    int TotalStepCount);
