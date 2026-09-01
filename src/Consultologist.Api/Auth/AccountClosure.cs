using Consultologist.Api.Forms;
using Consultologist.Api.Jobs;
using Consultologist.Api.Workflow;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Auth;

/// <summary>
/// #559 (storage-separation.md § 2.6): the one deleter of what is never
/// deleted. Removes everything an account owns in an order that leaves no
/// orphan still resolving — text, packages, records and links, index,
/// settings, identities, the account row — and writes one closure row as
/// the audit. What stays: usage counts, rate-limit rows, the email claim
/// ledger (none of which this class can even reach — they are not
/// dependencies, by design). Idempotent: the closure row is the claim, and
/// every step tolerates what an earlier interrupted run already removed.
/// Logs carry ids and counts only.
/// </summary>
public sealed record AccountClosureOutcome(
    ClosedAccountRecord? Closed,
    bool AlreadyClosed = false,
    string? Refusal = null,
    bool NotFound = false);

public sealed class AccountClosure
{
    private readonly IAccountStore _accounts;
    private readonly IAccountSettingsStore _settings;
    private readonly IConsultGenerationJobIndexStore _index;
    private readonly IConsultGenerationJobEventStore _events;
    private readonly ILegacyJobEventDelete _legacyEvents;
    private readonly IConsultGenerationLinkStore _links;
    private readonly IFormResponseStore _formResponses;
    private readonly IJobOutputsBlobStore _outputsBlobs;
    private readonly IJobInputsBlobStore _inputsBlobs;
    private readonly IFormResponseBlobStore _formResponseBlobs;
    private readonly IWorkflowPackageOwnership _ownership;
    private readonly IWorkflowPackageRegistryWriter _packageRegistry;
    private readonly IClosedAccountsStore _closed;
    private readonly ILogger<AccountClosure> _logger;

    public AccountClosure(
        IAccountStore accounts,
        IAccountSettingsStore settings,
        IConsultGenerationJobIndexStore index,
        IConsultGenerationJobEventStore events,
        ILegacyJobEventDelete legacyEvents,
        IConsultGenerationLinkStore links,
        IFormResponseStore formResponses,
        IJobOutputsBlobStore outputsBlobs,
        IJobInputsBlobStore inputsBlobs,
        IFormResponseBlobStore formResponseBlobs,
        IWorkflowPackageOwnership ownership,
        IWorkflowPackageRegistryWriter packageRegistry,
        IClosedAccountsStore closed,
        ILogger<AccountClosure> logger)
    {
        _accounts = accounts;
        _settings = settings;
        _index = index;
        _events = events;
        _legacyEvents = legacyEvents;
        _links = links;
        _formResponses = formResponses;
        _outputsBlobs = outputsBlobs;
        _inputsBlobs = inputsBlobs;
        _formResponseBlobs = formResponseBlobs;
        _ownership = ownership;
        _packageRegistry = packageRegistry;
        _closed = closed;
        _logger = logger;
    }

    public async Task<AccountClosureOutcome> CloseAsync(
        DurableTaskClient client,
        string appUserId,
        string operatorAppUserId,
        CancellationToken cancellationToken)
    {
        // The idempotence claim: a second call finds nothing to do and says so.
        if (await _closed.TryGetAsync(appUserId, cancellationToken) is { } existing)
        {
            return new AccountClosureOutcome(existing, AlreadyClosed: true);
        }

        var status = await _accounts.TryGetStatusAsync(appUserId, cancellationToken);
        if (status == null)
        {
            return new AccountClosureOutcome(null, NotFound: true);
        }

        if (!string.Equals(status, AccountStatuses.Disabled, StringComparison.Ordinal))
        {
            // The two-step keeps a slip from being final: disable first (the
            // #191 operator write), then close.
            return new AccountClosureOutcome(null, Refusal: $"The account is {status}, not Disabled — disable it first, then close.");
        }

        var jobs = await _index.ListAllAsync(appUserId, cancellationToken);

        // 1. Text. Per job: a still-live orchestration is stopped the #202
        // way (entity Cancel, then terminate — terminate must precede purge),
        // and the streamed events go from both tables. Then the account's
        // whole prefix in every text container, kind-blind.
        foreach (var job in jobs)
        {
            if (!ConsultGenerationJobEntity.IsTerminal(job.Status))
            {
                var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), job.JobId);
                await client.Entities.SignalEntityAsync(entityId, nameof(ConsultGenerationJobEntity.Cancel), cancellation: cancellationToken);
                try
                {
                    await client.TerminateInstanceAsync(job.JobId, "Account closed (#559).", cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Already terminal, or never scheduled — closure goes on.
                    _logger.LogInformation(ex, "Closure: terminate was unnecessary. JobId={JobId}", job.JobId);
                }
            }

            await _events.DeleteJobAsync(job.JobId, cancellationToken);
            await _legacyEvents.DeleteJobAsync(job.JobId, cancellationToken);
        }

        var blobs = await _outputsBlobs.DeleteAccountAsync(appUserId, cancellationToken)
            + await _inputsBlobs.DeleteAccountAsync(appUserId, cancellationToken)
            + await _formResponseBlobs.DeleteAccountAsync(appUserId, cancellationToken);

        // 2. Packages: the ownership rows are the authoritative fork set
        // (#462 — a bare acct-<12hex> prefix can collide across accounts, so
        // the prefix is never trusted alone); every version of every fork,
        // the folder, then the rows.
        var names = await _ownership.ListAsync(appUserId, cancellationToken);
        var packageBlobs = 0;
        foreach (var name in names)
        {
            packageBlobs += await _packageRegistry.DeletePackageAsync(name, cancellationToken);
        }

        await _ownership.DeleteAllAsync(appUserId, cancellationToken);

        // 3. Records: the orchestration instance (its id IS the job id), the
        // entity's own state (the implicit delete operation — the SDK has no
        // DeleteEntityAsync), and the links partition per job (cross-account
        // edges cannot exist, so both directions go with the account's own
        // partitions). Then the form-response rows, tombstones and all.
        var links = 0;
        foreach (var job in jobs)
        {
            try
            {
                await client.PurgeInstanceAsync(job.JobId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogInformation(ex, "Closure: orchestration purge found nothing. JobId={JobId}", job.JobId);
            }

            var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), job.JobId);
            await client.Entities.SignalEntityAsync(entityId, "delete", cancellation: cancellationToken);
            links += await _links.DeleteForJobAsync(job.JobId, cancellationToken);
        }

        var responses = await _formResponses.DeleteAllAsync(appUserId, cancellationToken);

        // The delete signals above are asynchronous; a just-emptied entity
        // may be swept by a later closure's clean instead of this one. The
        // clean is hub-wide by design — there is no per-account form.
        try
        {
            await client.Entities.CleanEntityStorageAsync(continueUntilComplete: true, cancellation: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogInformation(ex, "Closure: entity storage clean deferred to a later run.");
        }

        // 4-7. The index partition, the settings partition, the identities
        // and the account row — the row last, so a crash mid-way re-runs.
        await _index.DeleteAllAsync(appUserId, cancellationToken);
        var settings = await _settings.DeleteAllAsync(appUserId, cancellationToken);
        var identities = await _accounts.DeleteAccountAsync(appUserId, cancellationToken);

        var record = new ClosedAccountRecord(
            appUserId, DateTimeOffset.UtcNow, operatorAppUserId,
            Jobs: jobs.Count, Blobs: blobs, Packages: packageBlobs, Links: links,
            Responses: responses, Settings: settings, Identities: identities);
        await _closed.RecordAsync(record, cancellationToken);

        _logger.LogInformation(
            "Account closed. AppUserId={AppUserId}, Operator={Operator}, Jobs={Jobs}, Blobs={Blobs}, PackageBlobs={PackageBlobs}, Links={Links}, Responses={Responses}, Settings={Settings}, Identities={Identities}",
            appUserId, operatorAppUserId, jobs.Count, blobs, packageBlobs, links, responses, settings, identities);

        return new AccountClosureOutcome(record);
    }
}
