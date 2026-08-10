using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
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
        IReadOnlyDictionary<string, string> inputs,
        string? workflowPackage = null,
        DateTimeOffset? scheduledAtUtc = null,
        IReadOnlyDictionary<string, InputFilePayload>? files = null);

    Task<ConsultGenerationJobResponse> GetConsultGenerationJobAsync(string jobId);

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

    public async Task<ConsultGenerationJobStartResponse> StartConsultGenerationJobAsync(
        IReadOnlyDictionary<string, string> inputs,
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
                inputs.Values.Sum(value => value.Length));

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, functionUrl)
            {
                Content = JsonContent.Create(request)
            };
            await AddAuthorizationAsync(httpRequest);

            var response = await _httpClient.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Consult generation job start failed with status {StatusCode}: {Error}",
                    response.StatusCode,
                    errorContent);

                throw new HttpRequestException($"Azure Function call failed: {response.StatusCode}");
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
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Consult generation job poll failed with status {StatusCode}: {Error}",
                    response.StatusCode,
                    errorContent);

                throw new HttpRequestException($"Azure Function call failed: {response.StatusCode}");
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
    Dictionary<string, string>? Inputs = null,
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
    IReadOnlyList<ConsultSkippedDocumentResponse>? SkippedDocuments = null);

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
    string? ForEach = null);

public record ConsultItemStepDescriptor(string Id, string Label);

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
