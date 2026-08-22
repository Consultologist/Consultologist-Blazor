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
        IReadOnlyDictionary<string, InputFilePayload>? files = null);

    Task<ConsultGenerationJobResponse> GetConsultGenerationJobAsync(string jobId);

    /// <summary>#202: call off a scheduled run before its timer fires.</summary>
    Task CancelConsultGenerationJobAsync(string jobId);

    /// <summary>
    /// #390: move a scheduled run to a different time. Returns the NEW job id —
    /// rescheduling cancels and re-creates, so the old id is gone.
    /// </summary>
    Task<string> RescheduleConsultGenerationJobAsync(string jobId, DateTimeOffset scheduledAtUtc);

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
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly NavigationManager _navigation;
    private readonly ILogger<AIEndpointService> _logger;

    public AIEndpointService(
        HttpClient httpClient,
        IConfiguration configuration,
        IAccessTokenProvider accessTokenProvider,
        NavigationManager navigation,
        ILogger<AIEndpointService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
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
            ? new ConsultGenerationRefusedException(response.StatusCode, detail)
            : new HttpRequestException($"Azure Function call failed: {response.StatusCode}");
    }

    private static string? ReadErrorDetail(string body)
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
                if (string.Equals(property.Name, "error", StringComparison.OrdinalIgnoreCase)
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
        IReadOnlyDictionary<string, InputFilePayload>? files = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var functionUrl = _configuration["AzureFunction:ConsultGenerationJobsUrl"];

            if (string.IsNullOrEmpty(functionUrl))
            {
                _logger.LogError("AzureFunction:ConsultGenerationJobsUrl is not configured");
                throw new InvalidOperationException("Azure Function consult generation jobs URL is not configured");
            }

            var request = new ConsultGenerationRequest(
                null,
                workflowPackage,
                scheduledAtUtc,
                inputs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                files is { Count: > 0 }
                    ? files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
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
        var functionUrl = _configuration["AzureFunction:ConsultGenerationJobsUrl"];

        if (string.IsNullOrEmpty(functionUrl))
        {
            throw new InvalidOperationException("Azure Function consult generation jobs URL is not configured");
        }

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

    public async Task CancelConsultGenerationJobAsync(string jobId)
    {
        var functionUrl = _configuration["AzureFunction:ConsultGenerationJobsUrl"];

        if (string.IsNullOrEmpty(functionUrl))
        {
            throw new InvalidOperationException("Azure Function consult generation jobs URL is not configured");
        }

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
            var functionUrl = _configuration["AzureFunction:ConsultGenerationJobsUrl"];

            if (string.IsNullOrEmpty(functionUrl))
            {
                _logger.LogError("AzureFunction:ConsultGenerationJobsUrl is not configured");
                throw new InvalidOperationException("Azure Function consult generation jobs URL is not configured");
            }

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
        var functionUrl = _configuration["AzureFunction:ConsultGenerationJobsUrl"];

        if (string.IsNullOrEmpty(functionUrl))
        {
            _logger.LogError("AzureFunction:ConsultGenerationJobsUrl is not configured");
            throw new InvalidOperationException("Azure Function consult generation jobs URL is not configured");
        }

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
    // #238: a slot filled by a document instead of text. The server extracts
    // it at job start, so origin is observed rather than asserted. Nothing in
    // the UI sends these yet — #236 is what attaches a file.
    Dictionary<string, InputFilePayload>? InputFiles = null);

/// <summary>Mirrors Consultologist.Api.Models.InputFilePayload. byte[] rides
/// the JSON body as base64; no filename is sent.</summary>
public sealed record InputFilePayload(string ContentType, byte[] Content);

/// <summary>Mirrors Consultologist.Api.Models.ConsultInputOrigin. Absence
/// means "not recorded", never "typed".</summary>
public sealed record ConsultInputOrigin(
    string Kind,
    string? Extractor = null,
    int? PageCount = null,
    bool TrackedChangesResolved = false);
public record ConsultGenerationJobStartResponse(string JobId, string StatusUrl);

/// <summary>#390: the new job a reschedule created, and the one it replaced.</summary>
public record RescheduleConsultResponse(string JobId, string CancelledJobId, DateTimeOffset ScheduledAtUtc);
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
    IReadOnlyDictionary<string, ConsultInputOrigin>? InputOrigins = null,
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
    int? PackageSpecVersion = null);

/// <summary>Mirrors Consultologist.Api.Models.ConsultSkippedDocument.</summary>
public record ConsultSkippedDocumentResponse(string ResultId, string Label, string Reason);

/// <summary>One v7 deliverable: authored identity, the document, and its digest.</summary>
public record ConsultGenerationResultDocumentResponse(
    string ResultId,
    string Label,
    string Text,
    string? DocumentHash = null);

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
    IReadOnlyList<string>? Aggregate = null);

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
    string? Error);

public record ConsultGenerationJobHistoryEvent(string Kind, string Label, string? Detail, DateTimeOffset OccurredAt);

public record ConsultGenerationItemProgress(
    string ItemId,
    string ItemName,
    string? Step,
    int CompletedStepCount,
    int TotalStepCount);
