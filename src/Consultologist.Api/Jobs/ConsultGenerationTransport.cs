using System.Diagnostics;
using System.Globalization;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Jobs;

public sealed class ConsultGenerationJobs
{
    private const string LastEventIdHeaderName = "Last-Event-ID";
    private const int MaxScheduleHorizonDays = 7;

    // Per-input text bound, matching the email-intake body cap
    // (EmailIntakeProcessor.MaxDraftLength).
    internal const int MaxInputLength = 256 * 1024;

    // #238: bytes accepted per attached document, and across one request.
    // The per-file bound matches the parser's own (DocumentExtraction.MaxBytes)
    // so a file that clears this is one the parser will also accept; the total
    // mirrors email's per-message budget.
    internal const int MaxInputFileBytes = 10 * 1024 * 1024;
    internal const int MaxInputFilesTotalBytes = 20 * 1024 * 1024;

    // v9 layer 1 (#421): structure at the door. Shape limits in the same sense
    // as MaxInputLength — refused, never truncated — and bounding allocation
    // before the starter sees the request. The text cap applies to every text
    // scalar wherever it sits, so the worst case is 256 elements of 256 KB; a
    // slot filled by documents is bounded as a whole by MaxInputLength at the
    // starter, after extraction (#428), where the total is first known.
    internal const int MaxArrayElements = 256;
    internal const int MaxObjectFields = 64;
    // v10 (#493): structure nests, so one total bounds what the caps above
    // could otherwise multiply. Refused, never truncated.
    internal const int MaxStructureNodes = 4096;
    private const string MissingSseAttemptId = "missing";
    private const string InvalidSseAttemptId = "invalid";
    private const string SseExitReasonCompleted = "Completed";
    private const string SseExitReasonTerminalFailure = "TerminalFailure";
    private const string SseExitReasonTerminalInitialState = "TerminalInitialState";
    private const string SseExitReasonRequestAborted = "RequestAborted";
    private const string SseExitReasonServerTimeout = "ServerTimeout";
    private const string SseExitReasonServerError = "ServerError";
    private const string SseExitReasonChannelCompleted = "ChannelCompleted";

    private static readonly TimeSpan SsePollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SseHeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SseStreamTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SseInitialJobResponsePollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SseInitialJobResponseTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<ConsultGenerationJobs> _logger;
    private readonly IAccountAuthorizer _authorizer;
    private readonly IConsultGenerationJobEventStore _eventStore;
    private readonly IConsultGenerationJobStarter _jobStarter;
    // #202: the cancellation reply. A scheduled job is one the user walked away
    // from, so calling it off is told the same way its completion would be.
    private readonly Email.IGraphMailClient _mail;
    private readonly IConfiguration _configuration;
    // #486: the verified delivery address lives in the account's settings.
    private readonly IAccountSettingsStore _settingsStore;
    private readonly IJobOutputsBlobStore _outputsStore;
    private readonly IJobInputsBlobStore _inputsStore;
    private readonly IConsultGenerationLinkStore _linkStore;

    public ConsultGenerationJobs(
        ILogger<ConsultGenerationJobs> logger,
        IAccountAuthorizer authorizer,
        IConsultGenerationJobEventStore eventStore,
        IConsultGenerationJobStarter jobStarter,
        Email.IGraphMailClient mail,
        IConfiguration configuration,
        IAccountSettingsStore settingsStore,
        IJobOutputsBlobStore outputsStore,
        IJobInputsBlobStore inputsStore,
        IConsultGenerationLinkStore linkStore)
    {
        _logger = logger;
        _authorizer = authorizer;
        _eventStore = eventStore;
        _jobStarter = jobStarter;
        _mail = mail;
        _configuration = configuration;
        _settingsStore = settingsStore;
        _outputsStore = outputsStore;
        _inputsStore = inputsStore;
        _linkStore = linkStore;
    }

    /// <summary>#486: the confirmed address, or null — never the token claim.</summary>
    private async Task<string?> GetDeliveryAddressAsync(string appUserId, CancellationToken cancellationToken)
    {
        var setting = await _settingsStore.GetAsync(appUserId, AccountSettingKeys.DeliveryAddress, cancellationToken);
        return string.IsNullOrWhiteSpace(setting?.Value) ? null : setting.Value;
    }

    [Function("StartConsultGenerationJob")]
    public async Task<HttpResponseData> StartAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "ConsultGenerationJobs")] HttpRequestData req,
        [DurableClient] DurableTaskClient client)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        _logger.LogInformation(
            "StartConsultGenerationJob entered. InvocationId={InvocationId}, Method={Method}, Url={Url}",
            req.FunctionContext.InvocationId,
            req.Method,
            req.Url);

        if (IsOptions(req))
        {
            _logger.LogInformation(
                "StartConsultGenerationJob returning OPTIONS response. InvocationId={InvocationId}",
                req.FunctionContext.InvocationId);

            return CreateEmptyResponse(req, HttpStatusCode.OK);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        // #195: the one door an Unverified account may not pass. It keeps
        // everything it already made — this refuses only the making of more,
        // and says which it is, since a bare 403 here reads as a bug when the
        // rest of the app plainly works.
        if (!AccountAuthorizer.CanStartConsults(account))
        {
            return await CreateJsonResponseAsync(
                req,
                HttpStatusCode.Forbidden,
                new { error = "This account cannot start new consults until its LinkedIn profile is reconnected. Existing consults remain available." },
                req.FunctionContext.CancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "StartConsultGenerationJob reading request body. InvocationId={InvocationId}",
                req.FunctionContext.InvocationId);

            ConsultGenerationRequest? generationRequest = null;

            {
                try
                {
                    _logger.LogInformation(
                        "StartConsultGenerationJob deserializing request body. InvocationId={InvocationId}",
                        req.FunctionContext.InvocationId);

                    // #238: parsed straight from the stream. Buffering to a
                    // string first cost two copies of the whole body, and a
                    // request carrying an attached document is base64 —
                    // ~1.33x the file, then again as UTF-16.
                    generationRequest = await JsonSerializer.DeserializeAsync<ConsultGenerationRequest>(
                        req.Body,
                        JsonOptions,
                        cancellationToken);
                }
                catch (ConsultInputShapeException ex)
                {
                    // A shape the input converter refuses — an exponent, structure
                    // past one level, a repeated key. Named, because a caller who
                    // sent 1e3 should not get the same answer as one who sent a
                    // truncated body. The message carries the token and the path,
                    // never the value, which may be patient data.
                    var shapeError = MalformedInputMessage(ex);

                    _logger.LogWarning(ex, "Invalid ConsultGenerationJobs request: {ValidationError}", shapeError);

                    return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = shapeError }, cancellationToken);
                }
                catch (JsonException ex)
                {
                    const string malformedJsonError = "Malformed JSON request body.";

                    _logger.LogWarning(
                        ex,
                        "Invalid ConsultGenerationJobs request: {ValidationError}",
                        malformedJsonError);

                    return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = malformedJsonError }, cancellationToken);
                }
            }

            var validationError = ValidateRequest(generationRequest);

            if (validationError != null)
            {
                _logger.LogWarning("Invalid ConsultGenerationJobs request: {ValidationError}", validationError);
                return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = validationError }, cancellationToken);
            }

            var request = generationRequest!;

            var outcome = await _jobStarter.StartAsync(
                client,
                request,
                account.AppUserId,
                new ConsultGenerationJobOrigin(
                    ConsultGenerationJobSources.App,
                    ReplyAddressFor(await GetDeliveryAddressAsync(account.AppUserId, cancellationToken)),
                    EmailRequested: await EmailRequestedAsync(account.AppUserId, cancellationToken)),
                cancellationToken);

            if (outcome.Error != null)
            {
                var status = StatusFor(outcome.Error.Value);
                return await CreateJsonResponseAsync(
                    req,
                    status,
                    // #434: the same status and the same sentence; when the
                    // refusal left a row (NoApplicableDeliverable, the only
                    // kind that does) the body also says where it is.
                    new { error = outcome.ErrorDetail, jobId = outcome.JobId },
                    cancellationToken,
                    outcome.RetryAfter);
            }

            var instanceId = outcome.JobId!;
            var statusUrl = BuildStatusUrl(req, instanceId);

            _logger.LogInformation(
                "Consult generation job started via HTTP. JobId={JobId}, ElapsedMs={ElapsedMs}",
                instanceId,
                stopwatch.ElapsedMilliseconds);

            return await CreateJsonResponseAsync(req, HttpStatusCode.Accepted, new ConsultGenerationJobStartResponse(instanceId, statusUrl), cancellationToken);
        }
        catch (Exception ex)
        {
            // #245: the request body is in scope here; the type name says what
            // failed without risking a fragment of it in the log or the reply.
            _logger.LogError(
                ex,
                "Error starting consult generation job. ExceptionType={ExceptionType}, ElapsedMs={ElapsedMs}",
                ex.GetType().FullName,
                stopwatch.ElapsedMilliseconds);

            return await CreateJsonResponseAsync(req, HttpStatusCode.InternalServerError, new { error = $"Internal error: {ex.GetType().Name}" }, cancellationToken);
        }
    }

    /// <summary>
    /// #202: call off a scheduled run before its timer fires. Deferred at #157
    /// because "an operator can terminate the orchestration manually" — true
    /// only while there is an operator to ask.
    /// </summary>
    [Function("CancelConsultGenerationJob")]
    public async Task<HttpResponseData> CancelAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "ConsultGenerationJobs/{jobId}/Cancel")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string jobId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (IsOptions(req))
        {
            return CreateEmptyResponse(req, HttpStatusCode.OK);
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = "JobId is required." }, cancellationToken);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        // The ENTITY-backed read only. GetJobResponseAsync falls back to
        // synthesizing a response from the orchestration instance using the
        // CALLER's id when no entity record exists, which carries no ownership
        // evidence at all — fine for a read that finds nothing, not for a
        // mutation.
        var response = await GetEntityBackedJobResponseAsync(client, jobId, cancellationToken);

        if (response == null
            || !string.Equals(response.AppUserId, account.AppUserId, StringComparison.Ordinal))
        {
            // Someone else's job is not found, not forbidden: the 403 would
            // confirm it exists.
            return await CreateJsonResponseAsync(
                req, HttpStatusCode.NotFound, new { error = "Consult generation job was not found." }, cancellationToken);
        }

        if (RefusalForCancel(response.Status) is { } refusal)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.Conflict, new { error = refusal }, cancellationToken);
        }

        // Order is load-bearing. The entity is written FIRST so its terminal
        // Cancelled wins in MergeEntityAndRuntimeStatus; terminate first and the
        // runtime status arrives as Terminated, which MapRuntimeStatus reads as
        // Failed — every cancelled job would read as a failure.
        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), jobId);
        await client.Entities.SignalEntityAsync(
            entityId, nameof(ConsultGenerationJobEntity.Cancel), cancellation: cancellationToken);

        await client.TerminateInstanceAsync(jobId, "Cancelled by the account holder (#202).", cancellationToken);

        _logger.LogInformation(
            "Cancelled a scheduled consult run. JobId={JobId}, AppUserId={AppUserId}",
            jobId,
            account.AppUserId);

        await TrySendCancellationReplyAsync(jobId, account.Email, cancellationToken);

        return await CreateJsonResponseAsync(
            req,
            HttpStatusCode.OK,
            new { jobId, status = ConsultGenerationJobStatuses.Cancelled },
            cancellationToken);
    }

    /// <summary>
    /// #390: move a scheduled run to a different time.
    ///
    /// This cannot be a record update. The fire time comes from the
    /// ORCHESTRATION INPUT, fixed once the instance starts
    /// (ConsultGenerationEngine's CreateTimer), so writing a new time to the
    /// entity would change what History displays while the timer still fired at
    /// the old one — worse than not offering the edit.
    ///
    /// So it cancels and re-creates. Server-side, because the client has
    /// nothing to re-create FROM: no response carries the inputs, the entity and
    /// index store none, and the per-tab memento is never set on the scheduled
    /// path. The only copy is the sleeping instance's own input, read here. The
    /// consult text never re-enters the browser.
    /// </summary>
    [Function("RescheduleConsultGenerationJob")]
    public async Task<HttpResponseData> RescheduleAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "ConsultGenerationJobs/{jobId}/Reschedule")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string jobId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (IsOptions(req))
        {
            return CreateEmptyResponse(req, HttpStatusCode.OK);
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = "JobId is required." }, cancellationToken);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        RescheduleConsultRequest? body;

        try
        {
            body = JsonSerializer.Deserialize<RescheduleConsultRequest>(
                await new StreamReader(req.Body).ReadToEndAsync(cancellationToken), JsonOptions);
        }
        catch (JsonException)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = "Request body is not valid JSON." }, cancellationToken);
        }

        if (body?.ScheduledAtUtc is not { } newTime)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = "ScheduledAtUtc is required." }, cancellationToken);
        }

        if (RefusalForScheduleTime(newTime) is { } timeRefusal)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.UnprocessableEntity, new { error = timeRefusal }, cancellationToken);
        }

        // Entity-backed only, for the reason CancelAsync gives.
        var response = await GetEntityBackedJobResponseAsync(client, jobId, cancellationToken);

        if (response == null
            || !string.Equals(response.AppUserId, account.AppUserId, StringComparison.Ordinal))
        {
            return await CreateJsonResponseAsync(
                req, HttpStatusCode.NotFound, new { error = "Consult generation job was not found." }, cancellationToken);
        }

        if (RefusalForCancel(response.Status) is { } refusal)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.Conflict, new { error = refusal }, cancellationToken);
        }

        var instance = await client.GetInstancesAsync(jobId, getInputsAndOutputs: true, cancellationToken);
        var original = instance?.ReadInputAs<ConsultGenerationOrchestrationInput>();

        if (original?.Request == null)
        {
            // Nothing to re-create from. Refusing beats cancelling a job we
            // cannot replace.
            _logger.LogWarning("Reschedule found no orchestration input. JobId={JobId}", jobId);
            return await CreateJsonResponseAsync(
                req, HttpStatusCode.Conflict, new { error = "This consult can no longer be rescheduled. Cancel it and submit a new one." }, cancellationToken);
        }

        // Cancel first: a failure here must not leave two live jobs for one
        // consult. The reverse order could double-run it.
        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), jobId);
        await client.Entities.SignalEntityAsync(
            entityId, nameof(ConsultGenerationJobEntity.Cancel), cancellation: cancellationToken);
        await client.TerminateInstanceAsync(jobId, "Rescheduled by the account holder (#390).", cancellationToken);

        var origin = new ConsultGenerationJobOrigin(
            // Keep where it came from — a rescheduled email consult is still an
            // email consult — and the account's verified delivery address (#486);
            // an email consult rescheduled from the app is delivered to the
            // account, never re-sent to the original sender.
            original.Request.ScheduledAtUtc != null && response.Source != null ? response.Source : ConsultGenerationJobSources.App,
            ReplyAddressFor(await GetDeliveryAddressAsync(account.AppUserId, cancellationToken)),
            // #518: a reschedule is a new start — the choice is read again.
            EmailRequested: await EmailRequestedAsync(account.AppUserId, cancellationToken));

        var outcome = await _jobStarter.StartAsync(
            client,
            original.Request with { ScheduledAtUtc = newTime },
            account.AppUserId,
            origin,
            cancellationToken);

        if (outcome.Error != null)
        {
            // The old job is already cancelled, so say so rather than implying
            // nothing happened.
            _logger.LogError(
                "Reschedule cancelled the old job but could not start the new one. JobId={JobId}, Error={Error}",
                jobId,
                outcome.Error);

            return await CreateJsonResponseAsync(
                req,
                StatusFor(outcome.Error.Value),
                new { error = $"The consult was cancelled but could not be rescheduled: {outcome.ErrorDetail}", jobId = outcome.JobId },
                cancellationToken);
        }

        _logger.LogInformation(
            "Rescheduled a consult. OldJobId={OldJobId}, NewJobId={NewJobId}, ScheduledAtUtc={ScheduledAtUtc}",
            jobId,
            outcome.JobId,
            newTime);

        return await CreateJsonResponseAsync(
            req,
            HttpStatusCode.OK,
            new { jobId = outcome.JobId, cancelledJobId = jobId, scheduledAtUtc = newTime },
            cancellationToken);
    }

    /// <summary>
    /// #549: start a new job from a completed run's held inputs — the blob's
    /// Supplied half under the record's exact package ref, never a
    /// re-resolved pin. The blob and only the blob: the orchestration
    /// instance is purged by retention, and #547 made the blob the durable
    /// copy. Every effective slot of the new job carries a rerun origin
    /// naming this source. A rerun is a new consult: it passes the start
    /// door's account gate and spends a rate-limit unit like any other run.
    /// </summary>
    [Function("RerunConsultGenerationJob")]
    public async Task<HttpResponseData> RerunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "ConsultGenerationJobs/{jobId}/Rerun")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string jobId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (IsOptions(req))
        {
            return CreateEmptyResponse(req, HttpStatusCode.OK);
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = "JobId is required." }, cancellationToken);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        // #195: a rerun makes a new consult, so it passes the same gate the
        // start door holds, with the same named sentence.
        if (!AccountAuthorizer.CanStartConsults(account))
        {
            return await CreateJsonResponseAsync(
                req,
                HttpStatusCode.Forbidden,
                new { error = "This account cannot start new consults until its LinkedIn profile is reconnected. Existing consults remain available." },
                cancellationToken);
        }

        // Entity-backed only, for the reason CancelAsync gives.
        var response = await GetEntityBackedJobResponseAsync(client, jobId, cancellationToken);

        if (response == null
            || !string.Equals(response.AppUserId, account.AppUserId, StringComparison.Ordinal))
        {
            return await CreateJsonResponseAsync(
                req, HttpStatusCode.NotFound, new { error = "Consult generation job was not found." }, cancellationToken);
        }

        if (RefusalForRerun(response) is { } refusal)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.Conflict, new { error = refusal }, cancellationToken);
        }

        // A live pointer whose blob is gone is a broken invariant — loud, as
        // the read choke point throws, never a quiet unheld fallback.
        var payload = await _inputsStore.ReadAsync(response.InputsBlob!, cancellationToken);
        if (payload?.Supplied == null)
        {
            throw new InvalidOperationException($"Inputs blob missing for job {jobId}.");
        }

        var origin = new ConsultGenerationJobOrigin(
            // The issue's word: a rerun is an app action wherever the source
            // came from, delivered to the account's verified address (#486),
            // with the email choice read afresh (#518).
            ConsultGenerationJobSources.App,
            ReplyAddressFor(await GetDeliveryAddressAsync(account.AppUserId, cancellationToken)),
            EmailRequested: await EmailRequestedAsync(account.AppUserId, cancellationToken),
            RerunOfJobId: jobId,
            // #582: the verdict's evidence, taken here while the source is in
            // hand — its hashes are immutable once Completed.
            RerunBaseline: BaselineFrom(jobId, response));

        var outcome = await _jobStarter.StartAsync(
            client,
            RerunRequestFrom(response, payload),
            account.AppUserId,
            origin,
            cancellationToken);

        if (outcome.Error != null)
        {
            return await CreateJsonResponseAsync(
                req,
                StatusFor(outcome.Error.Value),
                new { error = outcome.ErrorDetail },
                cancellationToken);
        }

        _logger.LogInformation(
            "Rerun started. SourceJobId={SourceJobId}, NewJobId={NewJobId}",
            jobId,
            outcome.JobId);

        return await CreateJsonResponseAsync(
            req,
            HttpStatusCode.OK,
            new { jobId = outcome.JobId, sourceJobId = jobId },
            cancellationToken);
    }

    /// <summary>
    /// Why this job may not be rerun, or null when it may. Completed runs
    /// only (#549's word); the inputs must be held and not yet dropped; and
    /// the record must name the exact package version — a rerun never
    /// re-resolves a pin. Extracted so it can be asserted directly.
    /// </summary>
    internal static string? RefusalForRerun(ConsultGenerationJobResponse response) =>
        response.Status != ConsultGenerationJobStatuses.Completed
            ? $"Only a completed consult can be rerun; this one is {response.Status.ToLowerInvariant()}."
            : response.InputsBlob == null
                ? "This run's inputs were not held, so it cannot be rerun."
                : response.InputsDroppedAtUtc is { } dropped
                    ? $"The held inputs were deleted on {dropped:yyyy-MM-dd} under the retention policy, so this run can no longer be rerun."
                    : response.WorkflowPackage == null
                        ? "This record does not name the package version it ran, so it cannot be rerun."
                        : null;

    /// <summary>
    /// #546: who used this run — the links index's rows for it, ids only.
    /// The "copied from" side lives on each consumer's own origins; this is
    /// the inversion. Entity-backed ownership on purpose: the response
    /// contains OTHER runs' ids, so the synthesizing read's caller-supplied
    /// ownership is not acceptable, and someone else's job is not found,
    /// never forbidden.
    /// </summary>
    [Function("GetConsultGenerationJobLinks")]
    public async Task<HttpResponseData> GetLinksAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "ConsultGenerationJobs/{jobId}/Links")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string jobId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        if (IsOptions(req))
        {
            return CreateEmptyResponse(req, HttpStatusCode.OK);
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = "JobId is required." }, cancellationToken);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var response = await GetEntityBackedJobResponseAsync(client, jobId, cancellationToken);

        if (response == null
            || !string.Equals(response.AppUserId, account.AppUserId, StringComparison.Ordinal))
        {
            return await CreateJsonResponseAsync(
                req, HttpStatusCode.NotFound, new { error = "Consult generation job was not found." }, cancellationToken);
        }

        var consumers = await _linkStore.ListConsumersAsync(jobId, cancellationToken);

        return await CreateJsonResponseAsync(
            req,
            HttpStatusCode.OK,
            new { usedBy = UsedByFrom(consumers) },
            cancellationToken);
    }

    /// <summary>The wire projection: consumer ids and the edge's words, nothing else. Extracted so it can be asserted directly.</summary>
    internal static IReadOnlyList<ConsultJobLinkResponse> UsedByFrom(IReadOnlyList<ConsultGenerationLink> links) =>
        links
            .Select(link => new ConsultJobLinkResponse(link.ConsumerJobId, link.Kind, link.InputId, link.ResultId))
            .ToList();

    /// <summary>
    /// #582: the source's hashes, lifted from its entity-backed response —
    /// every per-stage pair with its hashVersion, and the effective-input
    /// hash the rerun must reproduce. Hashes only, never text.
    /// </summary>
    internal static ConsultRerunBaseline BaselineFrom(string sourceJobId, ConsultGenerationJobResponse source) =>
        new(
            sourceJobId,
            source.EffectiveInputHash,
            source.EffectiveInputHashVersion,
            source.NodeOutputs?.ToDictionary(
                pair => pair.Key,
                pair => new ConsultRerunBaselineNode(pair.Value.InputHash, pair.Value.OutputHash, pair.Value.HashVersion),
                StringComparer.Ordinal)
                ?? new Dictionary<string, ConsultRerunBaselineNode>(StringComparer.Ordinal));

    /// <summary>
    /// The rebuild: no draft, no files, no refs, no schedule — only the held
    /// Supplied values (typed wire JSON back to ConsultInputValue, the exact
    /// inverse of what #547 wrote) under the record's own package ref, which
    /// resolves to itself. The same map means the same effectiveInputHash.
    /// </summary>
    internal static ConsultGenerationRequest RerunRequestFrom(ConsultGenerationJobResponse source, JobInputsPayload payload) =>
        new(
            ConsultDraft: null,
            WorkflowPackage: source.WorkflowPackage,
            Inputs: payload.Supplied!.ToDictionary(
                pair => pair.Key,
                pair => ConsultInputValue.FromJson(pair.Value),
                StringComparer.Ordinal));

    /// <summary>
    /// The same horizon the start endpoint enforces, so a reschedule cannot put
    /// a job somewhere a fresh submit could not. Past times stay valid — they
    /// run immediately, which is a legitimate way to say "actually, now".
    /// </summary>
    internal static string? RefusalForScheduleTime(DateTimeOffset scheduledAt)
        => scheduledAt > DateTimeOffset.UtcNow.AddDays(MaxScheduleHorizonDays)
            ? $"ScheduledAtUtc is more than {MaxScheduleHorizonDays} days out."
            : null;

    /// <summary>
    /// Why this job may not be cancelled, or null when it may. Only a Scheduled
    /// job qualifies: the deferral in #157 is about a run that has not started,
    /// and stopping one that has is a different decision about work already paid
    /// for. Extracted so it can be asserted directly.
    /// </summary>
    internal static string? RefusalForCancel(string status) => status switch
    {
        ConsultGenerationJobStatuses.Scheduled => null,
        ConsultGenerationJobStatuses.Queued or ConsultGenerationJobStatuses.Running =>
            "This consult has already started, so it can no longer be cancelled.",
        _ => $"This consult is already {status.ToLowerInvariant()}."
    };

    /// <summary>
    /// A fresh no-PHI message, never a Graph /reply — the same rule the intake
    /// replies follow, for the same reason. A reply that cannot send must not
    /// fail a cancel that already happened.
    /// </summary>
    private async Task TrySendCancellationReplyAsync(string jobId, string? toAddress, CancellationToken cancellationToken)
    {
        var mailbox = _configuration["EmailIntake:MailboxAddress"];

        if (string.IsNullOrWhiteSpace(toAddress) || string.IsNullOrWhiteSpace(mailbox))
        {
            return;
        }

        try
        {
            var appBaseUrl = _configuration["EmailIntake:AppBaseUrl"]?.TrimEnd('/');
            var body = "Your scheduled consult was cancelled, and will not run.\n\n"
                + "You can schedule another from the app"
                + (appBaseUrl == null ? "." : $":\n{appBaseUrl}/history/{jobId}\n")
                + "\nThis message intentionally contains no clinical content.";

            await _mail.SendMailAsync(mailbox, toAddress, "Consult cancelled", body, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cancellation reply could not be sent. JobId={JobId}", jobId);
        }
    }

    [Function("GetConsultGenerationJob")]
    public async Task<HttpResponseData> GetAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "ConsultGenerationJobs/{jobId}")] HttpRequestData req,
        [DurableClient] DurableTaskClient client,
        string jobId)
    {
        var cancellationToken = req.FunctionContext.CancellationToken;

        _logger.LogInformation(
            "GetConsultGenerationJob entered. InvocationId={InvocationId}, Method={Method}, Url={Url}, JobId={JobId}",
            req.FunctionContext.InvocationId,
            req.Method,
            req.Url,
            jobId);

        if (IsOptions(req))
        {
            _logger.LogInformation(
                "GetConsultGenerationJob returning OPTIONS response. InvocationId={InvocationId}, JobId={JobId}",
                req.FunctionContext.InvocationId,
                jobId);

            return CreateEmptyResponse(req, HttpStatusCode.OK);
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.BadRequest, new { error = "JobId is required." }, cancellationToken);
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            return AccountAuthorizer.CreateUnauthorizedResponse(req);
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            return AccountAuthorizer.CreateForbiddenResponse(req);
        }

        var response = await GetJobResponseAsync(client, jobId, account.AppUserId, cancellationToken);

        if (response == null)
        {
            return await CreateJsonResponseAsync(req, HttpStatusCode.NotFound, new { error = "Consult generation job was not found." }, cancellationToken);
        }

        await TryMaterializeEventsForPollingAsync(response, cancellationToken);

        return await CreateJsonResponseAsync(req, HttpStatusCode.OK, response, cancellationToken);
    }

    [Function("GetConsultGenerationJobEvents")]
    public async Task<IActionResult> GetEventsAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "options", Route = "ConsultGenerationJobs/{jobId}/events")] HttpRequest req,
        [DurableClient] DurableTaskClient client,
        string jobId)
    {
        var cancellationToken = req.HttpContext.RequestAborted;
        var attemptId = GetSseAttemptId(req);

        _logger.LogInformation(
            "GetConsultGenerationJobEvents entered. Method={Method}, Path={Path}, JobId={JobId}, AttemptId={AttemptId}",
            req.Method,
            req.Path,
            jobId,
            attemptId);

        if (IsOptions(req))
        {
            _logger.LogInformation(
                "GetConsultGenerationJobEvents returning OPTIONS response. JobId={JobId}",
                jobId);

            FunctionCors.Apply(req, req.HttpContext.Response);
            return new OkResult();
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            FunctionCors.Apply(req, req.HttpContext.Response);
            return new BadRequestObjectResult(new { error = "JobId is required." });
        }

        var account = await _authorizer.AuthorizeAsync(req, cancellationToken);

        if (account == null)
        {
            FunctionCors.Apply(req, req.HttpContext.Response);
            req.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";
            return new UnauthorizedResult();
        }

        if (!AccountAuthorizer.CanUseApp(account))
        {
            FunctionCors.Apply(req, req.HttpContext.Response);
            return new ForbidResult();
        }

        if (!TryGetResumeAfterSequence(req, jobId, out var resumeAfterSequence, out var lastEventIdError))
        {
            FunctionCors.Apply(req, req.HttpContext.Response);
            return new BadRequestObjectResult(new { error = lastEventIdError });
        }

        var initialResponse = await WaitForInitialJobResponseAsync(
            client,
            jobId,
            account.AppUserId,
            cancellationToken);

        if (initialResponse == null)
        {
            FunctionCors.Apply(req, req.HttpContext.Response);
            return new NotFoundObjectResult(new { error = "Consult generation job was not found." });
        }

        var events = CreateConsultGenerationJobEventsAsync(
            client,
            jobId,
            account.AppUserId,
            attemptId,
            resumeAfterSequence,
            initialResponse,
            cancellationToken);

        return new CorsResultActionResult(TypedResults.ServerSentEvents(events));
    }

    private IAsyncEnumerable<SseItem<string>> CreateConsultGenerationJobEventsAsync(
        DurableTaskClient client,
        string jobId,
        string appUserId,
        string attemptId,
        long resumeAfterSequence,
        ConsultGenerationJobResponse initialResponse,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SseItem<string>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _ = WriteConsultGenerationJobEventsAsync(
            client,
            jobId,
            appUserId,
            attemptId,
            resumeAfterSequence,
            initialResponse,
            channel.Writer,
            cancellationToken);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task WriteConsultGenerationJobEventsAsync(
        DurableTaskClient client,
        string jobId,
        string appUserId,
        string attemptId,
        long resumeAfterSequence,
        ConsultGenerationJobResponse initialResponse,
        ChannelWriter<SseItem<string>> writer,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var initialEventCount = 0;
        var replayEventCount = 0;
        var liveEventCount = 0;
        var heartbeatCount = 0;
        var highestEmittedSequence = resumeAfterSequence;
        var latestEventId = (string?)null;
        var latestEventType = (string?)null;
        var terminalStatus = (string?)null;
        var latestStatus = initialResponse.Status;
        var exitReason = SseExitReasonChannelCompleted;
        var serverErrorType = (string?)null;

        try
        {
            _logger.LogInformation(
                "Consult generation SSE stream connected. JobId={JobId}, AppUserId={AppUserId}, AttemptId={AttemptId}, ResumeAfterSequence={ResumeAfterSequence}",
                jobId,
                appUserId,
                attemptId,
                resumeAfterSequence);

            if (resumeAfterSequence > 0)
            {
                var replayWriteResult = await WriteReplayedEventsAsync(
                    writer,
                    jobId,
                    appUserId,
                    resumeAfterSequence,
                    cancellationToken);
                highestEmittedSequence = replayWriteResult.HighestEmittedSequence;
                replayEventCount += replayWriteResult.EventCount;
                latestEventId = replayWriteResult.LatestEventId ?? latestEventId;
                latestEventType = replayWriteResult.LatestEventType ?? latestEventType;

                _logger.LogInformation(
                    "Consult generation SSE replay events sent. JobId={JobId}, AppUserId={AppUserId}, AttemptId={AttemptId}, ResumeAfterSequence={ResumeAfterSequence}, ReplayEventCount={ReplayEventCount}, HighestEmittedSequence={HighestEmittedSequence}",
                    jobId,
                    appUserId,
                    attemptId,
                    resumeAfterSequence,
                    replayEventCount,
                    highestEmittedSequence);
            }

            var initialWriteResult = await WriteMaterializedEventsAsync(
                writer,
                initialResponse,
                highestEmittedSequence,
                cancellationToken);
            highestEmittedSequence = initialWriteResult.HighestEmittedSequence;
            initialEventCount += initialWriteResult.EventCount;
            latestEventId = initialWriteResult.LatestEventId ?? latestEventId;
            latestEventType = initialWriteResult.LatestEventType ?? latestEventType;

            _logger.LogInformation(
                "Consult generation SSE initial events sent. JobId={JobId}, AppUserId={AppUserId}, AttemptId={AttemptId}, Status={Status}, TotalCount={TotalCount}, CompletedCount={CompletedCount}, FailedCount={FailedCount}, InitialEventCount={InitialEventCount}",
                jobId,
                appUserId,
                attemptId,
                initialResponse.Status,
                initialResponse.TotalBlockCount,
                initialResponse.CompletedBlockCount,
                initialResponse.FailedBlockCount,
                initialEventCount);

            if (IsTerminalJobStatus(initialResponse.Status))
            {
                terminalStatus = initialResponse.Status;
                exitReason = initialResponse.Status == ConsultGenerationJobStatuses.Failed
                    ? SseExitReasonTerminalFailure
                    : SseExitReasonTerminalInitialState;
                return;
            }

            var streamStartedAt = DateTimeOffset.UtcNow;
            var nextHeartbeatAt = streamStartedAt + SseHeartbeatInterval;

            while (!cancellationToken.IsCancellationRequested
                && DateTimeOffset.UtcNow - streamStartedAt < SseStreamTimeout)
            {
                await Task.Delay(SsePollInterval, cancellationToken);

                var latestResponse = await GetJobResponseAsync(client, jobId, appUserId, cancellationToken);

                if (latestResponse == null)
                {
                    throw new InvalidOperationException("Consult generation job was not found.");
                }

                latestStatus = latestResponse.Status;

                var liveWriteResult = await WriteMaterializedEventsAsync(
                    writer,
                    latestResponse,
                    highestEmittedSequence,
                    cancellationToken);
                highestEmittedSequence = liveWriteResult.HighestEmittedSequence;
                liveEventCount += liveWriteResult.EventCount;
                latestEventId = liveWriteResult.LatestEventId ?? latestEventId;
                latestEventType = liveWriteResult.LatestEventType ?? latestEventType;

                if (IsTerminalJobStatus(latestResponse.Status))
                {
                    terminalStatus = latestResponse.Status;
                    exitReason = latestResponse.Status == ConsultGenerationJobStatuses.Failed
                        ? SseExitReasonTerminalFailure
                        : SseExitReasonCompleted;
                    return;
                }

                if (DateTimeOffset.UtcNow >= nextHeartbeatAt)
                {
                    var heartbeat = CreateSseItem(
                        "heartbeat",
                        new ConsultGenerationJobHeartbeatEvent(jobId, latestResponse.Status));
                    await writer.WriteAsync(heartbeat, cancellationToken);
                    heartbeatCount++;
                    latestEventId = heartbeat.EventId ?? latestEventId;
                    latestEventType = heartbeat.EventType;

                    nextHeartbeatAt = DateTimeOffset.UtcNow + SseHeartbeatInterval;
                }
            }

            exitReason = SseExitReasonServerTimeout;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            exitReason = SseExitReasonRequestAborted;
        }
        catch (ChannelClosedException)
        {
            exitReason = SseExitReasonChannelCompleted;
        }
        catch (Exception ex)
        {
            exitReason = SseExitReasonServerError;
            serverErrorType = ex.GetType().FullName;
            writer.TryComplete(ex);
            return;
        }
        finally
        {
            var logLevel = exitReason is SseExitReasonTerminalFailure
                or SseExitReasonServerTimeout
                or SseExitReasonServerError
                ? LogLevel.Warning
                : LogLevel.Information;

            _logger.Log(
                logLevel,
                "Consult generation SSE stream exited. JobId={JobId}, AppUserId={AppUserId}, AttemptId={AttemptId}, ExitReason={ExitReason}, ElapsedMs={ElapsedMs}, ResumeAfterSequence={ResumeAfterSequence}, ReplayEventCount={ReplayEventCount}, InitialEventCount={InitialEventCount}, LiveEventCount={LiveEventCount}, HeartbeatCount={HeartbeatCount}, LatestEventId={LatestEventId}, LatestEventType={LatestEventType}, TerminalStatus={TerminalStatus}, LatestStatus={LatestStatus}, ServerErrorType={ServerErrorType}",
                jobId,
                appUserId,
                attemptId,
                exitReason,
                stopwatch.ElapsedMilliseconds,
                resumeAfterSequence,
                replayEventCount,
                initialEventCount,
                liveEventCount,
                heartbeatCount,
                latestEventId,
                latestEventType,
                terminalStatus,
                latestStatus,
                serverErrorType);

            writer.TryComplete();
        }
    }

    /// <summary>
    /// Snapshots one package node into the job's descriptor form: bindings flattened,
    /// the node's schema resolved to its catalog contract id, forEach carried through,
    /// and the legacy concept-source stamp applied for the four canonical analysis
    /// node ids.
    /// </summary>
    private static string BuildStatusUrl(HttpRequestData req, string jobId)
    {
        var authority = req.Url.GetLeftPart(UriPartial.Authority);
        return $"{authority}/api/ConsultGenerationJobs/{jobId}";
    }

    /// <summary>
    /// The 400 for a shape the input converter refused: its own sentence, plus
    /// where in the body it sat. Neither names a value.
    /// </summary>
    internal static string MalformedInputMessage(ConsultInputShapeException exception) =>
        string.IsNullOrEmpty(exception.Path)
            ? exception.Message
            : $"{exception.Message} At {exception.Path}.";

    /// <summary>
    /// The door's shape rules for one value, by kind. Text: blank and the
    /// cap, as always. Structure: the counts, and the cap on every text
    /// scalar inside — a boolean and a number have no length to run away
    /// with. An empty array passes: it is present and empty (v9 § 4), and
    /// whether that satisfies the slot is the starter's 422.
    /// </summary>
    private static string? ValidateInputValue(string id, ConsultInputValue value)
    {
        var budget = MaxStructureNodes;
        return ValidateValue($"Input '{id}'", value, top: true, ref budget);
    }

    /// <summary>
    /// v10 (#493): the same caps at every level, and one total for the
    /// structure so depth cannot multiply the worst case — 256 elements of
    /// 64 fields of 256 elements is not a request, it is an allocation.
    /// </summary>
    private static string? ValidateValue(string site, ConsultInputValue value, bool top, ref int budget)
    {
        if (--budget < 0)
        {
            return $"{site} is part of a structure with more than {MaxStructureNodes} values.";
        }

        switch (value.Kind)
        {
            case ConsultInputKind.Text:
                if (value.IsBlank && top)
                {
                    // Blank only at the top: an empty text inside structure is
                    // the starter's to judge against the declaration.
                    return $"{site} is blank.";
                }

                return value.Text!.Length > MaxInputLength
                    ? $"{site} exceeds {MaxInputLength / 1024} KB."
                    : null;

            case ConsultInputKind.Null:
                // The converter never produces this at the top of the map; an
                // in-process caller could. Inside structure it is the starter's.
                return top ? $"{site} is null." : null;

            case ConsultInputKind.Object:
                if (value.Fields!.Count > MaxObjectFields)
                {
                    return $"{site} has more than {MaxObjectFields} fields.";
                }

                foreach (var field in value.Fields)
                {
                    if (string.IsNullOrWhiteSpace(field.Id))
                    {
                        return $"{site} has a blank field id.";
                    }

                    var fieldError = ValidateValue($"{site} field '{field.Id}'", field.Value, top: false, ref budget);
                    if (fieldError != null)
                    {
                        return fieldError;
                    }
                }

                return null;

            case ConsultInputKind.Array:
                if (value.Elements!.Count > MaxArrayElements)
                {
                    return $"{site} has more than {MaxArrayElements} elements.";
                }

                for (var index = 0; index < value.Elements.Count; index++)
                {
                    var elementError = ValidateValue($"{site} element {index}", value.Elements[index], top: false, ref budget);
                    if (elementError != null)
                    {
                        return elementError;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    internal static string? ValidateRequest(ConsultGenerationRequest? request)
    {
        if (request == null)
        {
            return "Request body is required.";
        }

        var hasDraft = !string.IsNullOrWhiteSpace(request.ConsultDraft);
        var hasInputs = request.Inputs is { Count: > 0 };
        var hasFiles = request.InputFiles is { Count: > 0 };
        var hasRefs = request.InputRefs is { Count: > 0 };

        // #510: references are resolved at start; here only their shape.
        if (hasDraft && hasRefs)
        {
            return "Send ConsultDraft or InputRefs, not both.";
        }

        if (hasRefs)
        {
            foreach (var (id, refs) in request.InputRefs!)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return "InputRefs contains a blank id.";
                }

                if (request.Inputs?.ContainsKey(id) == true)
                {
                    return $"Input '{id}' was supplied as both text and a previous run.";
                }

                if (request.InputFiles?.ContainsKey(id) == true)
                {
                    return $"Input '{id}' was supplied as both a file and a previous run.";
                }

                if (refs is not { Count: > 0 })
                {
                    return $"Input '{id}' refers to no previous run.";
                }

                if (refs.Count > MaxArrayElements)
                {
                    return $"Input '{id}' refers to more than {MaxArrayElements} previous runs.";
                }

                foreach (var reference in refs)
                {
                    if (reference == null || !IsJobId(reference.JobId) || string.IsNullOrWhiteSpace(reference.ResultId))
                    {
                        return $"Input '{id}' refers to a previous run without a valid run id and deliverable.";
                    }
                }
            }
        }

        // #540: a form reference is an assertion BESIDE a value in Inputs,
        // never a fill request — only its shape is checked here; the starter
        // verifies it against the held response.
        if (request.InputFormRefs is { Count: > 0 })
        {
            foreach (var (id, reference) in request.InputFormRefs)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return "InputFormRefs contains a blank id.";
                }

                if (request.Inputs?.ContainsKey(id) != true)
                {
                    return $"Input '{id}' carries a form reference but no value.";
                }

                if (reference == null || string.IsNullOrWhiteSpace(reference.FormId) || string.IsNullOrWhiteSpace(reference.ResponseId))
                {
                    return $"Input '{id}' refers to a form response without a form id and response id.";
                }
            }
        }

        // Exactly one of the two forms: silently preferring one would drop
        // caller data (package-format-v7.md request contract).
        if (hasDraft && hasInputs)
        {
            return "Send ConsultDraft or Inputs, not both.";
        }

        if (hasDraft && hasFiles)
        {
            return "Send ConsultDraft or InputFiles, not both.";
        }

        if (!hasDraft && !hasInputs && !hasFiles && !hasRefs)
        {
            return "ConsultDraft, Inputs or InputFiles is required.";
        }

        if (hasFiles)
        {
            var totalBytes = 0L;

            foreach (var (id, documents) in request.InputFiles!)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return "InputFiles contains a blank id.";
                }

                // Same slot from both directions is ambiguous in the way the
                // v7 contract already refuses: nobody needs both, and
                // choosing one would drop the other silently.
                if (request.Inputs?.ContainsKey(id) == true)
                {
                    return $"Input '{id}' was supplied as both text and a file.";
                }

                // v9 (#428): a slot lists its documents. An empty list says
                // nothing about the slot; it is refused rather than read as
                // absent, the way an empty file is.
                if (documents is not { Count: > 0 })
                {
                    return $"Input '{id}' has no documents.";
                }

                // Several documents become an array, so the array bound holds.
                if (documents.Count > MaxArrayElements)
                {
                    return $"Input '{id}' has more than {MaxArrayElements} documents.";
                }

                for (var index = 0; index < documents.Count; index++)
                {
                    var file = documents[index];

                    // Counted from one, the way a sender numbers attachments
                    // (docs/DOCUMENT_INPUT.md § 6); named only when there is
                    // more than one, so a single document reads as it did.
                    var which = documents.Count == 1 ? string.Empty : $" document {index + 1}";

                    if (file?.Content is not { Length: > 0 })
                    {
                        return $"Input file '{id}'{which} is empty.";
                    }

                    if (file.Content.Length > MaxInputFileBytes)
                    {
                        return $"Input file '{id}'{which} exceeds {MaxInputFileBytes / (1024 * 1024)} MB.";
                    }

                    totalBytes += file.Content.Length;
                }
            }

            // A per-file cap does not bound a request carrying several. The
            // budget mirrors email's per-message one (docs/DOCUMENT_INPUT.md
            // § 4) so the two doors cost a request the same.
            if (totalBytes > MaxInputFilesTotalBytes)
            {
                return $"Input files exceed {MaxInputFilesTotalBytes / (1024 * 1024)} MB in total.";
            }
        }

        if (hasInputs)
        {
            foreach (var (id, value) in request.Inputs!)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    return "Inputs contains a blank id.";
                }

                if (value is null)
                {
                    return $"Input '{id}' is blank.";
                }

                var valueError = ValidateInputValue(id, value);
                if (valueError != null)
                {
                    return valueError;
                }
            }
        }

        // Past times are NOT errors (clock skew) — the orchestrator's timer
        // guard just runs them immediately. Only the horizon is enforced.
        if (request.ScheduledAtUtc is { } scheduledAt
            && scheduledAt > DateTimeOffset.UtcNow.AddDays(MaxScheduleHorizonDays))
        {
            return $"ScheduledAtUtc is more than {MaxScheduleHorizonDays} days out.";
        }

        return null;
    }

    // #486: every app job — immediate or scheduled — is delivered to the
    // account's verified delivery address, and to nothing else. The token-claim
    // email (#157's choice for scheduled jobs) is unverified and not unique,
    // so it is no longer an address. No address → no email, and the job says so.
    internal static string? ReplyAddressFor(string? deliveryAddress) =>
        string.IsNullOrWhiteSpace(deliveryAddress) ? null : deliveryAddress;

    /// <summary>#518: read at start, like the address — a choice changed mid-run does not change what was promised.</summary>
    private async Task<bool> EmailRequestedAsync(string appUserId, CancellationToken cancellationToken)
    {
        var setting = await _settingsStore.GetAsync(appUserId, AccountSettingKeys.EmailPdf, cancellationToken);
        return EmailRequestedFor(setting?.Value);
    }

    internal static bool EmailRequestedFor(string? emailPdfSetting) =>
        DeliveryAddress.EmailPdfOf(emailPdfSetting) != false;

    private async Task<ConsultGenerationJobResponse?> GetJobResponseAsync(
        DurableTaskClient client,
        string jobId,
        string appUserId,
        CancellationToken cancellationToken)
    {
        var entityBackedResponse = await GetEntityBackedJobResponseAsync(client, jobId, cancellationToken);
        var instance = await client.GetInstancesAsync(jobId, getInputsAndOutputs: false, cancellationToken);

        if (instance?.RuntimeStatus == OrchestrationRuntimeStatus.Failed)
        {
            instance = await client.GetInstancesAsync(jobId, getInputsAndOutputs: true, cancellationToken)
                ?? instance;
        }

        if (entityBackedResponse != null)
        {
            if (!string.Equals(entityBackedResponse.AppUserId, appUserId, StringComparison.Ordinal))
            {
                return null;
            }

            // #557: a record whose text lives in the outputs blob is
            // hydrated here — the one choke point every terminal read (GET,
            // SSE snapshot/done, polling materialization) flows through, so
            // a text-less response can never be served or persisted as an
            // event. Pre-#557 records (no pointer) and dropped records pass
            // through untouched.
            return await HydrateOutputsAsync(
                MergeEntityAndRuntimeStatus(entityBackedResponse, instance),
                cancellationToken);
        }

        if (instance == null)
        {
            return null;
        }

        var runtimeFailure = GetSanitizedRuntimeFailure(instance);

        return new ConsultGenerationJobResponse(
            jobId,
            appUserId,
            MapRuntimeStatus(instance.RuntimeStatus),
            0,
            0,
            0,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            false,
            RuntimeFailureStage: runtimeFailure?.Stage,
            RuntimeFailureError: runtimeFailure?.Error);
    }

    private async Task<ConsultGenerationJobResponse?> WaitForInitialJobResponseAsync(
        DurableTaskClient client,
        string jobId,
        string appUserId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < SseInitialJobResponseTimeout)
        {
            var response = await GetJobResponseAsync(client, jobId, appUserId, cancellationToken);

            if (response != null)
            {
                return response;
            }

            await Task.Delay(SseInitialJobResponsePollInterval, cancellationToken);
        }

        return await GetJobResponseAsync(client, jobId, appUserId, cancellationToken);
    }

    /// <summary>
    /// #557: text from the outputs blob, when the record points at one and
    /// the text is not dropped. A live pointer with a missing blob is a
    /// broken invariant — surfaced, never served as silently empty text.
    /// </summary>
    internal async Task<ConsultGenerationJobResponse> HydrateOutputsAsync(
        ConsultGenerationJobResponse response,
        CancellationToken cancellationToken)
    {
        if (response.OutputsBlob != null && response.TextDroppedAtUtc == null)
        {
            var payload = await _outputsStore.ReadAsync(response.OutputsBlob, cancellationToken);
            response = payload == null
                ? throw new InvalidOperationException($"Outputs blob missing for job {response.JobId}.")
                : JobOutputsHydration.Apply(response, payload);
        }

        // #547: the held inputs, while held — what History shows. The same
        // broken-invariant posture as outputs: a live pointer with no blob
        // is loud, never silently absent inputs.
        if (response.InputsBlob != null && response.InputsDroppedAtUtc == null)
        {
            var held = await _inputsStore.ReadAsync(response.InputsBlob, cancellationToken);
            response = held == null
                ? throw new InvalidOperationException($"Inputs blob missing for job {response.JobId}.")
                : response with { HeldInputs = held.Effective };
        }

        return response;
    }

    private static async Task<ConsultGenerationJobResponse?> GetEntityBackedJobResponseAsync(
        DurableTaskClient client,
        string jobId,
        CancellationToken cancellationToken)
    {
        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), jobId);
        var entity = await client.Entities.GetEntityAsync<ConsultGenerationJobState>(
            entityId,
            cancellation: cancellationToken);

        return entity?.State?.ToResponse();
    }

    private static ConsultGenerationJobResponse MergeEntityAndRuntimeStatus(
        ConsultGenerationJobResponse response,
        OrchestrationMetadata? instance)
    {
        if (instance == null
            || IsTerminalJobStatus(response.Status)
            || !IsTerminalRuntimeStatus(instance.RuntimeStatus))
        {
            return response;
        }

        var runtimeFailure = GetSanitizedRuntimeFailure(instance);

        return response with
        {
            Status = MapRuntimeStatus(instance.RuntimeStatus),
            RuntimeFailureStage = runtimeFailure?.Stage,
            RuntimeFailureError = runtimeFailure?.Error,
            History = AugmentHistoryForRuntimeFailure(response, runtimeFailure)
        };
    }

    private static IReadOnlyList<JobHistoryEvent>? AugmentHistoryForRuntimeFailure(
        ConsultGenerationJobResponse response,
        ConsultGenerationRuntimeFailure? runtimeFailure)
    {
        if (response.History == null || response.History.Count == 0)
        {
            return response.History;
        }

        var additional = new List<JobHistoryEvent>
        {
            new("failure", "Failed", runtimeFailure?.Error, DateTimeOffset.UtcNow)
        };

        var finishedIds = response.GeneratedBlocks.Keys
            .Concat(response.FailedBlocks.Keys)
            .ToHashSet();

        if (response.ItemProgress != null)
        {
            foreach (var (_, progress) in response.ItemProgress
                .Where(p => !finishedIds.Contains(p.Key))
                .OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var name = !string.IsNullOrWhiteSpace(progress.ItemName) ? progress.ItemName : progress.ItemId;
                additional.Add(new JobHistoryEvent("skipped", $"Section not reached: {name}", null, DateTimeOffset.UtcNow));
            }
        }

        return [.. response.History, .. additional];
    }

    private static bool IsTerminalJobStatus(string status)
    {
        return status is ConsultGenerationJobStatuses.Completed
            or ConsultGenerationJobStatuses.Failed
            // #202: load-bearing. Without it MergeEntityAndRuntimeStatus
            // overrides the entity's Cancelled with the runtime's Terminated,
            // which MapRuntimeStatus reads as Failed.
            or ConsultGenerationJobStatuses.Cancelled;
    }

    private static bool IsTerminalRuntimeStatus(OrchestrationRuntimeStatus status)
    {
        if (status.ToString().Equals("Canceled", StringComparison.Ordinal))
        {
            return true;
        }

        return status is OrchestrationRuntimeStatus.Completed
            or OrchestrationRuntimeStatus.Failed
            or OrchestrationRuntimeStatus.Terminated;
    }

    private static string MapRuntimeStatus(OrchestrationRuntimeStatus runtimeStatus)
    {
        if (runtimeStatus.ToString().Equals("Canceled", StringComparison.Ordinal))
        {
            return ConsultGenerationJobStatuses.Failed;
        }

        return runtimeStatus switch
        {
            OrchestrationRuntimeStatus.Completed => ConsultGenerationJobStatuses.Completed,
            OrchestrationRuntimeStatus.Failed => ConsultGenerationJobStatuses.Failed,
            OrchestrationRuntimeStatus.Terminated => ConsultGenerationJobStatuses.Failed,
            OrchestrationRuntimeStatus.Pending => ConsultGenerationJobStatuses.Queued,
            _ => ConsultGenerationJobStatuses.Running
        };
    }

    private static bool IsOptions(HttpRequestData req)
    {
        return string.Equals(req.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOptions(HttpRequest req)
    {
        return HttpMethods.IsOptions(req.Method);
    }

    private static string GetSseAttemptId(HttpRequest req)
    {
        var rawAttemptId = req.Query["attemptId"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(rawAttemptId))
        {
            return MissingSseAttemptId;
        }

        return Guid.TryParse(rawAttemptId.Trim(), out var attemptId)
            ? attemptId.ToString("D")
            : InvalidSseAttemptId;
    }

    private static bool TryGetResumeAfterSequence(
        HttpRequest req,
        string jobId,
        out long resumeAfterSequence,
        out string? error)
    {
        resumeAfterSequence = 0;
        error = null;

        if (!req.Headers.TryGetValue(LastEventIdHeaderName, out var headerValues))
        {
            return true;
        }

        var values = new List<string>();

        foreach (var value in headerValues)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value.Trim());
            }
        }

        if (values.Count == 0)
        {
            return true;
        }

        if (values.Count != 1)
        {
            error = "Invalid Last-Event-ID header.";
            return false;
        }

        var lastEventId = values[0];
        var separatorIndex = lastEventId.IndexOf(':', StringComparison.Ordinal);

        if (separatorIndex <= 0
            || separatorIndex != lastEventId.LastIndexOf(':')
            || separatorIndex == lastEventId.Length - 1)
        {
            error = "Invalid Last-Event-ID header.";
            return false;
        }

        var eventJobId = lastEventId[..separatorIndex];
        var sequenceText = lastEventId[(separatorIndex + 1)..];

        if (!string.Equals(eventJobId, jobId, StringComparison.Ordinal))
        {
            error = "Last-Event-ID does not match the requested job.";
            return false;
        }

        if (sequenceText.Length != 12
            || sequenceText.Any(character => character is < '0' or > '9')
            || !long.TryParse(sequenceText, out resumeAfterSequence))
        {
            error = "Invalid Last-Event-ID header.";
            return false;
        }

        return true;
    }

    private static HttpResponseData CreateEmptyResponse(HttpRequestData req, HttpStatusCode statusCode)
    {
        var response = req.CreateResponse(statusCode);
        FunctionCors.Apply(req, response);
        return response;
    }

    private static async Task<HttpResponseData> CreateJsonResponseAsync<T>(
        HttpRequestData req,
        HttpStatusCode statusCode,
        T payload,
        CancellationToken cancellationToken,
        TimeSpan? retryAfter = null)
    {
        var response = req.CreateResponse(statusCode);
        FunctionCors.Apply(req, response);

        if (retryAfter is { } wait)
        {
            // Whole seconds, and never zero — the header is delta-seconds and
            // a 0 invites an immediate retry that is certain to be refused.
            response.Headers.Add(
                "Retry-After",
                Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture));
        }

        await response.WriteAsJsonAsync(payload, cancellationToken);
        return response;
    }

    private async Task<SseMaterializedWriteResult> WriteReplayedEventsAsync(
        ChannelWriter<SseItem<string>> writer,
        string jobId,
        string appUserId,
        long resumeAfterSequence,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ConsultGenerationJobStoredEvent> storedEvents;

        try
        {
            storedEvents = await _eventStore.ReadAfterAsync(
                jobId,
                appUserId,
                resumeAfterSequence,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Consult generation SSE replay read failed. JobId={JobId}, ResumeAfterSequence={ResumeAfterSequence}",
                jobId,
                resumeAfterSequence);

            throw;
        }

        return await WriteStoredEventsAsync(
            writer,
            storedEvents,
            resumeAfterSequence,
            cancellationToken);
    }

    private async Task<SseMaterializedWriteResult> WriteMaterializedEventsAsync(
        ChannelWriter<SseItem<string>> writer,
        ConsultGenerationJobResponse response,
        long highestEmittedSequence,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ConsultGenerationJobStoredEvent> storedEvents;

        try
        {
            storedEvents = await _eventStore.AppendAsync(
                response.JobId,
                response.AppUserId,
                CreateSemanticEventCandidates(response),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Consult generation SSE event persistence failed. JobId={JobId}, Status={Status}",
                response.JobId,
                response.Status);

            throw;
        }

        return await WriteStoredEventsAsync(
            writer,
            storedEvents,
            highestEmittedSequence,
            cancellationToken);
    }

    private static async Task<SseMaterializedWriteResult> WriteStoredEventsAsync(
        ChannelWriter<SseItem<string>> writer,
        IReadOnlyList<ConsultGenerationJobStoredEvent> storedEvents,
        long highestEmittedSequence,
        CancellationToken cancellationToken)
    {
        var eventCount = 0;
        var latestEventId = (string?)null;
        var latestEventType = (string?)null;

        foreach (var storedEvent in storedEvents.Where(storedEvent => storedEvent.Sequence > highestEmittedSequence))
        {
            var item = CreateSseItem(storedEvent);
            await writer.WriteAsync(item, cancellationToken);
            highestEmittedSequence = Math.Max(highestEmittedSequence, storedEvent.Sequence);
            eventCount++;
            latestEventId = item.EventId;
            latestEventType = item.EventType;
        }

        return new SseMaterializedWriteResult(
            highestEmittedSequence,
            eventCount,
            latestEventId,
            latestEventType);
    }

    private async Task TryMaterializeEventsForPollingAsync(
        ConsultGenerationJobResponse response,
        CancellationToken cancellationToken)
    {
        try
        {
            await _eventStore.AppendAsync(
                response.JobId,
                response.AppUserId,
                CreateSemanticEventCandidates(response),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Consult generation polling event materialization failed. Returning job response without persisted SSE catch-up events. JobId={JobId}, Status={Status}",
                response.JobId,
                response.Status);
        }
    }

    private static SseItem<string> CreateSseItem(ConsultGenerationJobStoredEvent storedEvent)
    {
        return new SseItem<string>(storedEvent.PayloadJson, storedEvent.EventType)
        {
            EventId = storedEvent.SseId
        };
    }

    private static SseItem<string> CreateSseItem<T>(
        string eventName,
        T payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return new SseItem<string>(json, eventName);
    }

    internal static IReadOnlyList<ConsultGenerationJobEventCandidate> CreateSemanticEventCandidates(
        ConsultGenerationJobResponse response)
    {
        var candidates = new List<ConsultGenerationJobEventCandidate>();

        AddEventCandidate(candidates, "snapshot", "snapshot", response);
        // Pre-DAG (SchemaVersion 2) snapshots regenerate no stage candidates; their
        // events were materialized while they ran and replay from the event store. The
        // node path's failure branch covers both eras via the '-failed' status suffix.
        AddNodeEventCandidates(candidates, response);
        AddItemStepEventCandidates(candidates, response);

        foreach (var generatedSection in response.GeneratedBlocks.OrderBy(section => section.Key, StringComparer.Ordinal))
        {
            AddEventCandidate(
                candidates,
                "block-completed",
                $"block-completed:{generatedSection.Key}",
                new ConsultGenerationJobBlockCompletedEvent(response.JobId, generatedSection.Key, generatedSection.Value));
        }

        foreach (var failedSection in response.FailedBlocks.OrderBy(section => section.Key, StringComparer.Ordinal))
        {
            AddEventCandidate(
                candidates,
                "block-failed",
                $"block-failed:{failedSection.Key}",
                new ConsultGenerationJobBlockFailedEvent(response.JobId, failedSection.Key, failedSection.Value));
        }

        if (IsTerminalJobStatus(response.Status))
        {
            if (response.Status == ConsultGenerationJobStatuses.Failed)
            {
                AddTerminalFailureEventCandidate(candidates, response);
                return candidates;
            }

            AddEventCandidate(candidates, "done", "done", response);
        }

        return candidates;
    }

    private static void AddTerminalFailureEventCandidate(
        List<ConsultGenerationJobEventCandidate> candidates,
        ConsultGenerationJobResponse response)
    {
        if (IsAnalysisFailureStatus(response.AnalysisStatus ?? string.Empty))
        {
            return;
        }

        var stage = response.RuntimeFailureStage;
        var error = response.RuntimeFailureError;

        if (string.IsNullOrWhiteSpace(stage))
        {
            stage = response.FailedBlocks.Count > 0
                ? "block-generation-failed"
                : ConsultGenerationRuntimeFailure.StageName;
        }

        if (string.IsNullOrWhiteSpace(error))
        {
            error = response.FailedBlocks.Count > 0
                ? "Consult generation failed because no blocks were generated."
                : "Consult generation failed while running the backend workflow. Backend workflow stopped before completion.";
        }

        AddEventCandidate(
            candidates,
            "error",
            $"error:{stage}",
            new ConsultGenerationJobStreamError(response.JobId, error, stage));
    }

    private static void AddNodeEventCandidates(
        List<ConsultGenerationJobEventCandidate> candidates,
        ConsultGenerationJobResponse response)
    {
        // Failures ride the existing error-event path via the '-failed' status suffix.
        if (IsAnalysisFailureStatus(response.AnalysisStatus ?? string.Empty))
        {
            AddEventCandidate(
                candidates,
                "error",
                $"error:{response.AnalysisStatus}",
                new ConsultGenerationJobStreamError(
                    response.JobId,
                    response.AnalysisError ?? "Consult generation failed.",
                    response.AnalysisStatus));
        }

        if (response.Nodes == null || response.NodeOutputs == null)
        {
            return;
        }

        var totalNodeCount = response.Nodes.Count;
        var emitted = 0;

        foreach (var node in response.Nodes)
        {
            if (!response.NodeOutputs.TryGetValue(node.Id, out var output))
            {
                continue;
            }

            // A node emits its completion at the node level (a forEach node completes
            // once every item settles; per-item progress rides the section-prose-step
            // events). Skipped/failed nodes surface through the error event and job
            // history instead.
            if (output.Status != ConsultGenerationNodeStatuses.Completed)
            {
                continue;
            }

            emitted++;
            AddEventCandidate(
                candidates,
                ConsultGenerationNodeEvents.EventName,
                $"node:{node.Id}",
                new ConsultGenerationJobNodeCompletedEvent(
                    response.JobId,
                    node.Id,
                    node.Label,
                    $"{node.Label} completed.",
                    emitted,
                    totalNodeCount));
        }
    }

    private static void AddItemStepEventCandidates(
        List<ConsultGenerationJobEventCandidate> candidates,
        ConsultGenerationJobResponse response)
    {
        // Pre-milestone-3 snapshots carry no step list; their prose events were
        // materialized while they ran and replay from the event store.
        if (response.ItemProgress == null || response.ItemSteps is not { Count: > 0 } steps)
        {
            return;
        }

        // #356: which (node, item) pairs completed is recorded exactly — the
        // node outputs are keyed "nodeId:itemId", one entry per completed fan
        // instance. This used to emit one event per step 1..CompletedStepCount
        // and take the node id from ItemSteps[stepCount - 1], which invents the
        // identity the client keys its per-item ticks on: ItemSteps is every
        // forEach node in the package, while CompletedStepCount is counted over
        // one collection's chain, so a multi-collection package named a node
        // from the wrong collection. Math.Clamp hid the overflow, not the
        // mismatch.
        var labels = steps.ToDictionary(step => step.Id, step => step.Label, StringComparer.Ordinal);

        var completed = (response.NodeOutputs ?? new Dictionary<string, ConsultGenerationNodeStatusResponse>())
            .Where(entry => entry.Value.Status == ConsultGenerationNodeStatuses.Completed)
            .Select(entry => (Key: entry.Key, Separator: entry.Key.LastIndexOf(':')))
            .Where(entry => entry.Separator > 0)
            .Select(entry => (
                NodeId: entry.Key[..entry.Separator],
                ItemId: entry.Key[(entry.Separator + 1)..]))
            .Where(entry => labels.ContainsKey(entry.NodeId))
            .OrderBy(entry => entry.ItemId, StringComparer.Ordinal)
            .ThenBy(entry => entry.NodeId, StringComparer.Ordinal);

        foreach (var (nodeId, itemId) in completed)
        {
            if (!response.ItemProgress.TryGetValue(itemId, out var progress))
            {
                continue;
            }

            AddEventCandidate(
                candidates,
                ConsultGenerationItemSteps.EventName,
                $"item-step:{itemId}:{nodeId}",
                new ConsultGenerationItemStepEvent(
                    response.JobId,
                    itemId,
                    progress.ItemName,
                    nodeId,
                    labels[nodeId],
                    $"{labels[nodeId]} completed.",
                    progress.CompletedStepCount,
                    progress.TotalStepCount));
        }
    }

    private static void AddEventCandidate<T>(
        List<ConsultGenerationJobEventCandidate> candidates,
        string eventType,
        string eventKey,
        T payload)
    {
        candidates.Add(new ConsultGenerationJobEventCandidate(
            eventType,
            eventKey,
            JsonSerializer.Serialize(payload, JsonOptions)));
    }

    private static ConsultGenerationRuntimeFailure? GetSanitizedRuntimeFailure(OrchestrationMetadata instance)
    {
        if (MapRuntimeStatus(instance.RuntimeStatus) != ConsultGenerationJobStatuses.Failed)
        {
            return null;
        }

        if (instance.FailureDetails == null)
        {
            return new ConsultGenerationRuntimeFailure(
                ConsultGenerationRuntimeFailure.StageName,
                "Consult generation failed while running the backend workflow. Backend workflow stopped before completion.");
        }

        return GetSanitizedRuntimeFailure(instance.FailureDetails);
    }

    private static ConsultGenerationRuntimeFailure GetSanitizedRuntimeFailure(TaskFailureDetails failureDetails)
    {
        var failureText = GetFailureText(failureDetails);
        var action = GetRuntimeFailureAction(failureText);
        var cause = GetRuntimeFailureCause(failureText);

        return new ConsultGenerationRuntimeFailure(
            ConsultGenerationRuntimeFailure.StageName,
            $"Consult generation failed while {action}. {cause}");
    }

    private static string GetFailureText(TaskFailureDetails failureDetails)
    {
        var parts = new List<string>();

        for (var current = failureDetails; current != null; current = current.InnerFailure)
        {
            if (!string.IsNullOrWhiteSpace(current.ErrorType))
            {
                parts.Add(current.ErrorType);
            }

            if (!string.IsNullOrWhiteSpace(current.ErrorMessage))
            {
                parts.Add(current.ErrorMessage);
            }
        }

        return string.Join(" ", parts);
    }

    private static string GetRuntimeFailureAction(string failureText)
    {
        if (failureText.Contains(ConsultGenerationActivityNames.RunPromptNode, StringComparison.Ordinal))
        {
            return "running a workflow step";
        }

        return "running the backend workflow";
    }

    private static string GetRuntimeFailureCause(string failureText)
    {
        if (failureText.Contains("HTTP 408", StringComparison.OrdinalIgnoreCase)
            || failureText.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || failureText.Contains("timed out", StringComparison.OrdinalIgnoreCase))
        {
            return "Upstream AI service timed out.";
        }

        return "A backend dependency failed.";
    }

    private static bool IsAnalysisFailureStatus(string status)
    {
        return status.EndsWith("-failed", StringComparison.Ordinal);
    }

    private sealed class CorsResultActionResult : IActionResult
    {
        private readonly IResult _result;

        public CorsResultActionResult(IResult result)
        {
            _result = result;
        }

        public Task ExecuteResultAsync(ActionContext context)
        {
            FunctionCors.Apply(context.HttpContext.Request, context.HttpContext.Response);
            context.HttpContext.Response.Headers.CacheControl = "no-cache";
            return _result.ExecuteAsync(context.HttpContext);
        }
    }

    private sealed record SseMaterializedWriteResult(
        long HighestEmittedSequence,
        int EventCount,
        string? LatestEventId,
        string? LatestEventType);

    /// <summary>
    /// The HTTP status each start refusal answers with. Extracted from the
    /// endpoint so it can be asserted directly (#374): it was an inline switch
    /// no test reached, and its default is 500 — so a missing arm turns a
    /// well-understood refusal into an apparent server fault.
    /// </summary>
    /// <summary>#510: a job id is a 32-hex instance id, and a reference names nothing else.</summary>
    internal static bool IsJobId(string? id) =>
        id is { Length: 32 } && id.All(c => char.IsAsciiHexDigitLower(c) || char.IsAsciiDigit(c));

    internal static HttpStatusCode StatusFor(ConsultGenerationJobStartError error)
        => error switch
        {
            ConsultGenerationJobStartError.MalformedPackageRef => HttpStatusCode.BadRequest,
            ConsultGenerationJobStartError.ForeignPackageRef => HttpStatusCode.Forbidden,
            ConsultGenerationJobStartError.RegistryUnavailable => HttpStatusCode.ServiceUnavailable,
            ConsultGenerationJobStartError.PackageNotExecutable => HttpStatusCode.UnprocessableEntity,
            ConsultGenerationJobStartError.SpecVersionNotYetExecutable => HttpStatusCode.UnprocessableEntity,
            // #374: the package is readable and the registry is up, so
            // this is not a 503. The content and the catalog disagree.
            ConsultGenerationJobStartError.PackageContentRejected => HttpStatusCode.UnprocessableEntity,
            // Well-formed request, unsatisfiable against this package's
            // input declaration — 422, not 400 (the request-shape rules
            // in ValidateRequest are the 400s).
            ConsultGenerationJobStartError.InputsMismatch => HttpStatusCode.UnprocessableEntity,
            // #315: well-formed, satisfiable against the declaration,
            // and still nothing to produce. Same 422 family.
            ConsultGenerationJobStartError.NoApplicableDeliverable => HttpStatusCode.UnprocessableEntity,
            // #238: likewise — the request was well formed, the
            // document inside it could not be read. Same status the
            // preview endpoint returns for the same cause.
            ConsultGenerationJobStartError.InputFileUnreadable => HttpStatusCode.UnprocessableEntity,
            ConsultGenerationJobStartError.InputTooLong => HttpStatusCode.UnprocessableEntity,
            // #290: present but carrying no referral. Unsatisfiable
            // content, not a malformed request.
            ConsultGenerationJobStartError.InputWithoutContent => HttpStatusCode.UnprocessableEntity,
            ConsultGenerationJobStartError.InputBehindACloudLink => HttpStatusCode.UnprocessableEntity,
            // #510: a reference to a run this account does not have, that
            // did not complete, or whose text is gone — unsatisfiable, and
            // never a 403 that would confirm a foreign run exists.
            ConsultGenerationJobStartError.InputRefNotFound => HttpStatusCode.UnprocessableEntity,
            ConsultGenerationJobStartError.InputRefNotCompleted => HttpStatusCode.UnprocessableEntity,
            ConsultGenerationJobStartError.InputRefTextDeleted => HttpStatusCode.UnprocessableEntity,
            ConsultGenerationJobStartError.InputFormRefNotFound => HttpStatusCode.UnprocessableEntity,
            ConsultGenerationJobStartError.InputFormRefValuesDeleted => HttpStatusCode.UnprocessableEntity,
            ConsultGenerationJobStartError.InputFormRefMismatch => HttpStatusCode.UnprocessableEntity,
            // #266: nothing is wrong with the request at all — the
            // account has spent its window. 429 is the one status
            // that says "the same request will work later".
            ConsultGenerationJobStartError.RateLimited => HttpStatusCode.TooManyRequests,
            _ => HttpStatusCode.InternalServerError
        };

}

public sealed record ConsultGenerationJobBlockCompletedEvent(
    string JobId,
    string BlockId,
    string Text);

public sealed record ConsultGenerationJobBlockFailedEvent(
    string JobId,
    string BlockId,
    string Error);

public sealed record ConsultGenerationJobHeartbeatEvent(
    string JobId,
    string Status);

public sealed record ConsultGenerationJobStreamError(
    string JobId,
    string Error,
    string? Stage = null);

public static class ConsultGenerationNodeEvents
{
    /// <summary>The single SSE event name for every DAG node; the payload carries the node id and label.</summary>
    public const string EventName = "node-completed";
}

public sealed record ConsultGenerationJobNodeCompletedEvent(
    string JobId,
    string NodeId,
    string Label,
    string Message,
    int CompletedNodeCount,
    int TotalNodeCount);

public sealed record ConsultGenerationItemStepEvent(
    string JobId,
    string ItemId,
    string ItemName,
    string Step,
    string Label,
    string Message,
    int CompletedStepCount,
    int TotalStepCount);

public sealed record ConsultGenerationRuntimeFailure(
    string Stage,
    string Error)
{
    public const string StageName = "runtime-failed";
}
