using System.Globalization;
using Consultologist.Api.Agents;
using Consultologist.Api.Documents;
using Consultologist.Api.Auth;
using Consultologist.Api.Models;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.Workflow;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Jobs;

public static class ConsultGenerationJobSources
{
    public const string App = "app";
    public const string Email = "email";
}

public sealed record ConsultGenerationJobOrigin(
    string Source,
    string? ReplyToAddress = null,
    // #518: decided at start from delivery.emailPdf — false only when the
    // account chose not to email app-initiated runs. Email-door jobs always
    // reply, so they never set it.
    bool EmailRequested = true);

public enum ConsultGenerationJobStartError
{
    MalformedPackageRef,
    ForeignPackageRef,
    RegistryUnavailable,
    PackageNotExecutable,
    SpecVersionNotYetExecutable,
    // #374: the package resolved and downloaded fine; this engine will not
    // accept its content. Chiefly a declared schema matching no contract in the
    // loaded catalog — which can happen to an immutable package that was valid
    // when published, because the match is re-evaluated at every load and the
    // catalog moved underneath it. Reported as RegistryUnavailable it sent an
    // operator to look at storage that was working.
    PackageContentRejected,
    InputsMismatch,
    // #238: a supplied document could not be read. Well-formed request,
    // unsatisfiable content — 422 like InputsMismatch, not 400.
    InputFileUnreadable,
    // #428: a slot's documents read to more text than an input may carry.
    // Well-formed, every document readable, the sum over the cap — refused,
    // never truncated: a consult written from most of a referral with nothing
    // saying so is the worst available outcome (v9 design § 7).
    InputTooLong,
    // #266: the account has spent its window. The one error here that is
    // about the caller's history rather than about this request, and the
    // only one the email door must not answer with a rejection reply.
    RateLimited,
    // #290: a required input is present but carries no referral. Distinct
    // from InputsMismatch, which is about the shape of the request; this is
    // about there being nothing in it to generate from.
    InputWithoutContent,
    // #510: a slot refers to a previous run that is not this account's (or
    // does not exist — the two are one answer, never a 403), that did not
    // complete or has no such deliverable, or whose produced text the
    // retention sweep has deleted. Well-formed, unsatisfiable: 422.
    InputRefNotFound,
    InputRefNotCompleted,
    InputRefTextDeleted,
    // #291: the referral is behind a link we cannot open. Distinct from
    // InputWithoutContent because the remedy differs -- there IS a document,
    // it just never arrived.
    InputBehindACloudLink,
    // #315: every declared deliverable's condition is false for these inputs,
    // so the job would produce nothing. Knowable at start because conditions
    // read declared inputs only — refused rather than run. #434: refused AND
    // recorded — the outcome carries a job id born Failed.
    NoApplicableDeliverable
}

/// <summary>
/// The outcome of resolving a request's inputs against the package declaration:
/// Effective is the resolver map (every declared id present; absent optional
/// inputs as empty strings), Supplied the caller's map for hashing (absent
/// optional inputs omitted). Both null on the v5/v6 legacy path.
/// </summary>
internal sealed record EffectiveInputsResolution(
    IReadOnlyDictionary<string, string>? Effective,
    IReadOnlyDictionary<string, ConsultInputValue>? Supplied,
    string? Error,
    // #369: Error when it names only declared ids and declared values, so the
    // email door may quote it back to the sender. Null for the canonical-form
    // complaints, which end in got '<supplied value>'.
    string? SenderSafeError = null);

/// <summary>
/// JobId without Error: started. Error without JobId: refused, no row. Both
/// (#434): refused because no deliverable applied, and a row born Failed says
/// so — the one kind that carries both, so each door keeps its message and
/// gains the pointer.
/// </summary>
public sealed record ConsultGenerationJobStartOutcome(
    string? JobId,
    ConsultGenerationJobStartError? Error = null,
    string? ErrorDetail = null,
    // #369: the same sentence when — and only when — it is composed of
    // AUTHORED package content and request structure: input ids, result
    // labels, condition literals, declared enum values. Null whenever the
    // detail quotes a SUPPLIED value, because the email door replies to the
    // sender and a supplied value can be PHI (a date of service is the plain
    // case). Safety is a property of the sentence, not of the error kind —
    // InputsMismatch composes both sorts — so it is decided here, at the one
    // place that knows how the string was built, rather than re-derived
    // downstream from an error code that cannot express it.
    string? SenderSafeDetail = null,
    // #266: how long until the caller's window resets. Only set on
    // RateLimited, and only the HTTP door renders it — as Retry-After.
    TimeSpan? RetryAfter = null);

public interface IConsultGenerationJobStarter
{
    /// <summary>
    /// Starts a consult-generation job for an already-resolved app user. The
    /// request must have passed ValidateRequest. Callers: the HTTP endpoint
    /// (bearer-authed) and the email-intake poller (sender-matched).
    /// </summary>
    Task<ConsultGenerationJobStartOutcome> StartAsync(
        DurableTaskClient client,
        ConsultGenerationRequest request,
        string appUserId,
        ConsultGenerationJobOrigin origin,
        CancellationToken cancellationToken);
}

public sealed class ConsultGenerationJobStarter : IConsultGenerationJobStarter
{
    private readonly ILogger<ConsultGenerationJobStarter> _logger;
    private readonly IWorkflowPackageStore _packageStore;
    private readonly IWorkflowPackagePinResolver _pinResolver;
    private readonly OutputContractCatalog _catalog;
    private readonly EngineAttestationResponse _engine;
    private readonly ITerminologyAttestationSource _terminology;
    private readonly IAccountRateLimiter _rateLimiter;
    private readonly IWorkflowPackageOwnership _ownership;
    private readonly IAccountStore _accounts;
    private readonly IAccountSettingsStore _settingsStore;
    private readonly IJobOutputsBlobStore _outputsBlobs;

    public ConsultGenerationJobStarter(
        ILogger<ConsultGenerationJobStarter> logger,
        IWorkflowPackageStore packageStore,
        IWorkflowPackagePinResolver pinResolver,
        OutputContractCatalog catalog,
        IAccountRateLimiter rateLimiter,
        IWorkflowPackageOwnership ownership,
        EngineAttestationResponse engine,
        ITerminologyAttestationSource terminology,
        IAccountStore accounts,
        IAccountSettingsStore settingsStore,
        IJobOutputsBlobStore outputsBlobs)
    {
        _logger = logger;
        _packageStore = packageStore;
        _pinResolver = pinResolver;
        _catalog = catalog;
        _engine = engine;
        _terminology = terminology;
        _rateLimiter = rateLimiter;
        _ownership = ownership;
        _accounts = accounts;
        _settingsStore = settingsStore;
        _outputsBlobs = outputsBlobs;
    }

    public async Task<ConsultGenerationJobStartOutcome> StartAsync(
        DurableTaskClient client,
        ConsultGenerationRequest request,
        string appUserId,
        ConsultGenerationJobOrigin origin,
        CancellationToken cancellationToken)
    {
        // #266: first, before any registry read. This door and the preview
        // endpoint are the only two enforcement points, and between them they
        // cover all three ways in — email reaches the parser through here.
        //
        // A malformed package ref therefore costs a unit. That is correct: it
        // was still a submission, and moving the check below resolution would
        // buy an attacker free registry round trips.
        var decision = await _rateLimiter.AcquireOrAllowAsync(appUserId, _logger, cancellationToken);

        if (!decision.Allowed)
        {
            _logger.LogWarning(
                "Rejected job start: the account is over its submission limit. AppUserId={AppUserId}, Source={Source}, Limit={Limit}",
                appUserId,
                origin.Source,
                decision.Limit);
            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.RateLimited,
                $"This account has submitted {decision.Limit} consults in the past hour, which is its limit. Please try again shortly.",
                // No SenderSafeDetail: the email door never replies to this at
                // all — it queues the message (EmailIntakeProcessor) — so there
                // is no sender-facing sentence to authorise.
                RetryAfter: decision.RetryAfter);
        }

        var jobId = Guid.NewGuid().ToString("N");
        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), jobId);

        // A workflow package is mandatory: resolve the ref here (request → account
        // pin → default) to a concrete immutable version and snapshot it into the
        // job, so the whole run — and the provenance record — uses one version even
        // when the pin says "latest". Registry failure stops the job before it exists.
        if (!WorkflowPackageRef.TryParse(request.WorkflowPackage, out var packageRef))
        {
            if (!string.IsNullOrWhiteSpace(request.WorkflowPackage))
            {
                _logger.LogWarning("Invalid consult job request: malformed workflow package ref '{PackageRef}'.", request.WorkflowPackage);
                return new ConsultGenerationJobStartOutcome(
                    null,
                    ConsultGenerationJobStartError.MalformedPackageRef,
                    "WorkflowPackage is not a valid package reference.");
            }

            packageRef = await _pinResolver.ResolvePinAsync(appUserId, cancellationToken);
        }
        else if (!await _ownership.CanAccessAsync(packageRef!.Name, appUserId, cancellationToken))
        {
            // The acct-* access rule at job start: a caller-supplied ref to a
            // foreign account package is rejected before any registry read.
            _logger.LogWarning(
                "Rejected foreign account-package ref at job start. AppUserId={AppUserId}, Ref={Ref}",
                appUserId,
                request.WorkflowPackage);
            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.ForeignPackageRef,
                "Workflow package is not accessible from this account.");
        }

        WorkflowPackage package;
        try
        {
            package = await _packageStore.ResolveAsync(packageRef!, cancellationToken);
        }
        catch (WorkflowPackageSpecVersionException ex)
        {
            // The package is there and readable; this engine will not run that
            // version. A warning, not an error — nothing is broken, and calling
            // it a registry outage sent people looking in the wrong place.
            _logger.LogWarning(
                "Rejected job start: the pinned package is a specVersion this engine does not run. Pin={Pin}, SpecVersion={SpecVersion}",
                packageRef,
                ex.SpecVersion);
            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.SpecVersionNotYetExecutable,
                ex.Message);
        }
        catch (WorkflowPackageContentException ex)
        {
            // A warning, not an error: nothing is broken. The package is there,
            // the registry is there, and the two disagree — which an operator
            // can act on only if told so.
            _logger.LogWarning(
                "Rejected job start: the pinned package's content is not accepted by this engine. Pin={Pin}, Detail={Detail}",
                packageRef,
                ex.Message);
            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.PackageContentRejected,
                ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Workflow package resolution failed at job start. Pin={Pin}", packageRef);
            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.RegistryUnavailable,
                "Workflow package registry is unavailable.");
        }

        // #238: documents become text before anything else looks at the
        // request, so everything downstream — validation, resolution, hashing,
        // the orchestration — stays the string-keyed pipeline it already was.
        // Extraction is the pre-step docs/DOCUMENT_INPUT.md describes, not a
        // new kind of input.
        // #510: references to previous runs become text first, the way
        // documents do — the server copies, so the origin is observed.
        var resolution = await ResolveInputRefsAsync(client.Entities, _outputsBlobs, request, appUserId, cancellationToken);
        if (resolution.Error != null)
        {
            _logger.LogWarning("Rejected job start: a previous-run reference was refused. Kind={Kind}", resolution.ErrorKind);
            return new ConsultGenerationJobStartOutcome(null, resolution.ErrorKind, resolution.Error);
        }

        request = resolution.Request;

        var extraction = await ExtractInputFilesAsync(request, package.Manifest, GateWaitFor(origin), cancellationToken);
        if (extraction.Error != null)
        {
            _logger.LogWarning(
                "Rejected job start: attached documents were refused. Kind={Kind}, Outcome={Outcome}",
                extraction.ErrorKind,
                extraction.Outcome);
            // #217: the extraction complaint names an authored input id, never
            // the attachment's filename — a filename can itself be PHI.
            return new ConsultGenerationJobStartOutcome(
                null,
                extraction.ErrorKind,
                extraction.Error,
                SenderSafeDetail: extraction.SenderSafeError);
        }

        request = NormalizeInputs(extraction.Request);
        var inputOrigins = MergeOrigins(resolution.Origins, extraction.Origins);

        var inputs = ResolveEffectiveInputs(request, package.Manifest);
        if (inputs.Error != null)
        {
            _logger.LogWarning(
                "Rejected job start: inputs do not satisfy the package declaration. Package={Package}, Detail={Detail}",
                package.Ref,
                inputs.Error);
            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.InputsMismatch,
                inputs.Error,
                // Set for some mismatches and not others — the single clearest
                // reason this cannot be an allowlist over error kinds.
                SenderSafeDetail: inputs.SenderSafeError);
        }

        // #290: present is not the same as filled. ResolveEffectiveInputs has
        // just confirmed every required input exists and is not whitespace —
        // which a body containing only a OneDrive link satisfies. Generating
        // from that produced a complete consult whose every section read
        // "not documented", and delivered it.
        var withoutContent = InputContent.FindInputWithoutContent(
            request,
            package.Manifest,
            inputs.Supplied,
            InputContent.MinimumCharacters);

        if (withoutContent != null)
        {
            _logger.LogWarning(
                "Rejected job start: a required input carries no referral. Package={Package}, Input={Input}, Characters={Characters}, Minimum={Minimum}",
                package.Ref,
                withoutContent,
                inputs.Supplied is null
                    ? InputContent.MeaningfulLength(request.ConsultDraft)
                    : InputContent.MeaningfulLength(inputs.Supplied.GetValueOrDefault(withoutContent)),
                InputContent.MinimumCharacters);

            var withoutContentDetail =
                $"'{withoutContent}' does not contain a referral to work from. "
                    + "If the document was attached as a cloud link, please attach the file itself and re-send.";

            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.InputWithoutContent,
                withoutContentDetail,
                // An authored input id and fixed prose; the input's CONTENT —
                // the thing that was too short — is never quoted.
                SenderSafeDetail: withoutContentDetail);
        }

        // #291: #290's content floor is necessary but not sufficient. A body
        // holding only a OneDrive link and a signature cleared forty
        // characters and generated a consult anyway. The link is the signal
        // the floor cannot see.
        var behindLink = InputContent.FindInputBehindACloudLink(
            request,
            package.Manifest,
            inputs.Effective,
            inputOrigins);

        if (behindLink != null)
        {
            _logger.LogWarning(
                "Rejected job start: a required input points at a file we cannot open. Package={Package}, Input={Input}",
                package.Ref,
                behindLink);

            var behindLinkDetail =
                $"'{behindLink}' refers to a file stored in the cloud rather than containing the referral itself. "
                    + "We cannot open linked files — please attach the document to the message and re-send.";

            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.InputBehindACloudLink,
                behindLinkDetail,
                // An authored input id and fixed prose; the link itself, which
                // may carry a filename, is not quoted.
                SenderSafeDetail: behindLinkDetail);
        }

        // A consult_draft-only Inputs map against a legacy package folds into
        // the draft field, so everything downstream sees the v5/v6 shape.
        if (package.Manifest.SpecVersion < 7 && request.Inputs is { Count: > 0 })
        {
            request = request with { ConsultDraft = request.Inputs[ConsultDraftInputId].Canonical, Inputs = null };
        }

        // A multi-deliverable v7 package resolves ResultNodeId null by design —
        // its result SET is the executability signal.
        if (package.Nodes is not { Count: > 0 }
            || (package.ResultNodeId is null && package.Results is not { Count: > 0 }))
        {
            _logger.LogWarning("Workflow package {Package} (specVersion {SpecVersion}) has no executable nodes; jobs require specVersion 2 or newer.", package.Ref, package.Manifest.SpecVersion);
            return new ConsultGenerationJobStartOutcome(
                null,
                ConsultGenerationJobStartError.PackageNotExecutable,
                $"Workflow package {package.Ref} (specVersion {package.Manifest.SpecVersion}) predates prompt templates; pin a specVersion 2 or newer package.");
        }

        // v5: the result node's collection is the one section source. v6/v7:
        // Items carries the deliverable BLOCKS (each result aggregator's
        // expansion — WorkflowPackageBlocks dispatches the id scheme by spec)
        // and Collections carries one item set per fanned collection
        // (package-format-v6-design.md §§ 4–5; package-format-v7.md).
        // v8: the fire set, decided once, here. Conditions read declared inputs
        // only, so this is knowable before anything runs — which is what lets
        // the block skeleton still be built up front and TotalBlockCount stay
        // the stored scalar #176 made it.
        //
        // v10 (#496, package-format-v10-design.md § 5): a package with
        // classifiers decides at the BOUNDARY instead — the classifier closure
        // runs first, then DecideActivity evaluates every condition over the
        // supplied inputs and the answers, and the entity's Decide stamps the
        // count. Nothing here moves for a package without one: by control
        // flow, as #355 did it, so every v9 payload byte is untouched.
        //
        // Filtering the PACKAGE rather than teaching the engine about
        // conditions is the whole trick: block expansion, deliverable
        // resolution and the outcome rule all walk the result list and need no
        // change at all.
        var deciding = package.Nodes.Any(WorkflowNodeKinds.IsClassifier);
        var skipped = new List<ConsultSkippedDocument>();
        // #434: the deliverables as declared, before the fire set narrows
        // package.Results — the born-Failed record lists every one of them.
        var declaredResults = package.Results ?? new List<WorkflowResolvedResult>();

        if (!deciding && package.Results is { Count: > 0 })
        {
            var fireSet = DecideFireSet(package, inputs.Supplied, classifications: null);
            skipped = fireSet.Skipped;

            if (fireSet.Firing.Count == 0)
            {
                // Knowable before any model call, so nothing is spent. #434:
                // the job is created all the same — born Failed, carrying the
                // deliverables that did not apply and what each wanted — so
                // History holds a row for a submission that produced nothing.
                // The doors keep their messages; the row is what is new.
                _logger.LogWarning(
                    "Rejected job start: no deliverable applies to these inputs. JobId={JobId}, Package={Package}, Declared={Declared}",
                    jobId,
                    package.Ref,
                    skipped.Count);
                var noneApplyDetail = NoneApplyDetail(skipped);

                // #369: authored throughout. Labels and condition literals
                // come from the manifest, and WorkflowResultCondition.Explain
                // prints only what is safe on every surface: a declared enum
                // value, true/false, and a count of entries. A number, a date
                // or a field's value — which v9 conditions may read (#427) —
                // is the patient's and is never printed; the sentence says
                // what was needed and that it was not met.
                return await RecordNoApplicableDeliverableAsync(
                    client, entityId, jobId, appUserId, request, package, inputs, origin, inputOrigins, skipped, noneApplyDetail);
            }

            if (fireSet.Package.Nodes!.Count < package.Nodes.Count)
            {
                _logger.LogInformation(
                    "Pruned nodes outside the fire set. Package={Package}, Firing={Firing}, Skipped={Skipped}, Dropped={Dropped}",
                    package.Ref,
                    fireSet.Firing.Count,
                    skipped.Count,
                    package.Nodes.Count - fireSet.Package.Nodes.Count);
            }

            package = fireSet.Package;
        }

        IReadOnlyList<IReadOnlyDictionary<string, string>> items;
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? collectionSets = null;
        IReadOnlyList<ConsultCollectionRoster>? collectionRosters = null;

        if (deciding)
        {
            // The skeleton is the boundary's: no blocks yet, no fans yet, the
            // count not yet decided. The rail draws the job's nodes from the
            // snapshot and the data-collection rosters ride so a fan it will
            // later run is already drawable.
            items = Array.Empty<IReadOnlyDictionary<string, string>>();
            var skeleton = ResolveSkeleton(package, inputs.Supplied);
            collectionSets = skeleton.CollectionSets;
            collectionRosters = skeleton.CollectionRosters;
        }
        else if (package.Manifest.SpecVersion >= 6)
        {
            var skeleton = ResolveSkeleton(package, inputs.Supplied);

            // An empty fan produces no items, no blocks, no document — v8's
            // empty-fire-set case wearing different clothes, and recorded the
            // same way: at start, by name, before anything is spent (v9 § 5,
            // #434). Every declared deliverable is the one not produced, for
            // the same reason — the fan feeds them all — unless a condition
            // had already skipped it, in which case its own reason stands.
            if (skeleton.EmptyFanLabels.Count > 0)
            {
                _logger.LogWarning(
                    "Rejected job start: a fanned input has no entries. JobId={JobId}, Package={Package}, Inputs={Inputs}",
                    jobId,
                    package.Ref,
                    string.Join(", ", skeleton.EmptyFanLabels));
                var notProduced = NotProducedByEmptyFan(declaredResults, skipped, skeleton.EmptyFanLabels);
                var emptyFanDetail = EmptyFanDetail(skeleton.EmptyFanLabels);

                return await RecordNoApplicableDeliverableAsync(
                    client, entityId, jobId, appUserId, request, package, inputs, origin, inputOrigins, notProduced, emptyFanDetail);
            }

            items = skeleton.Items;
            collectionSets = skeleton.CollectionSets;
            collectionRosters = skeleton.CollectionRosters;
        }
        else
        {
            var collection = WorkflowPackageBlocks.ResolveCollection(package);
            items = collection.Items
                .Select(item => (IReadOnlyDictionary<string, string>)item.Fields)
                .ToList();
        }

        var dataScalars = package.Data?.Scalars;

        var resolvedPackageRef = package.Ref;
        // The per-section step list is the forEach chain, in manifest order — the
        // display/progress skeleton the section-prose-step events hang off.
        var sectionSteps = deciding
            ? null
            : package.Nodes
                .Where(node => node.ForEach != null)
                .Select(node => new ConsultItemStepDescriptor(node.Id, node.Label))
                .ToList();
        var nodes = package.Nodes.Select(node => DescribeNode(node, package.SchemaContracts)).ToList();

        // Provenance: identify the artifacts and input that produce this consult.
        // v5/v6: the hash covers the draft only (definition version 2). v7: the
        // supplied input map (definition version 3 — absent optional inputs
        // omitted). Sections are package content, covered by the
        // workflowPackage ref; agent identities are covered by catalogRef — the
        // record stores refs, not copies (#105).
        // See docs/customizable-workflow/provenance.md.
        // Five definitions. v10 hashes the map recursed (definition 6, the
        // same bytes as 5 for a map with no nesting — v10 § 8). v9 hashes the
        // STRUCTURED map — field ids sorted, arrays in the caller's order,
        // UTF-8 as-is (package-format-v9-design.md § 8). v8 hashes the typed
        // map, so a boolean hashes as `true` and not as `"true"` (v8 § 6). v7
        // keeps hashing canonical strings; v5/v6 keep the draft-only
        // definition. Never compared across versions.
        //
        // The `>= 8` arm is the one #422 exists for: before the `>= 9` arm
        // above it, a v9 job would have been hashed by definition 4 and
        // stamped 4, with nothing erroring. Definition 4 now refuses
        // structure, so a gate that slips again fails the start instead.
        var specVersion = package.Manifest.SpecVersion;
        var (effectiveInputHash, effectiveInputHashVersion) = EffectiveInputHashOf(specVersion, request, inputs);

        // Only v8 has types, and only the non-text ones are worth carrying:
        // null here means a v5-v7 job's payload is byte-identical to before.
        var declaredInputTypes = specVersion >= 8
            ? package.Manifest.Inputs?
                .Where(input => WorkflowInputTypes.Of(input) != WorkflowInputTypes.Text)
                .ToDictionary(input => input.Id, WorkflowInputTypes.Of, StringComparer.Ordinal)
            : null;

        if (declaredInputTypes is { Count: 0 })
        {
            declaredInputTypes = null;
        }

        var resultDescriptors = package.Results?
            .Select(result => new ConsultResultDescriptor(result.Id, result.NodeId, result.Label, result.Macros, result.Signature))
            .ToList();

        // v11 #513: the macro templates and the account's display name,
        // snapshotted at start — what was promised when the job was submitted,
        // not what the package or profile says when it wakes. Null unless the
        // package declares macros, so every pre-v11 payload writes the bytes
        // it always wrote, and no macro-less start pays the table read.
        IReadOnlyDictionary<string, string>? macroTexts = null;
        string? profileName = null;
        if (package.Manifest.Macros is { Count: > 0 } macroSpecs)
        {
            macroTexts = macroSpecs.ToDictionary(
                macro => macro.Id,
                macro => package.SourceFiles![macro.File],
                StringComparer.Ordinal);
            profileName = await _accounts.GetDisplayNameAsync(appUserId, cancellationToken);
        }

        // #557: the account's kind — which text container the outputs blob
        // lands in. Read unconditionally: every completed job writes one.
        var accountKind = await _accounts.GetAccountKindAsync(appUserId, cancellationToken);

        // v11 #516: the chosen signature block, snapshotted at start — what
        // was promised when the job was submitted, through both doors. Only
        // a package that marks a deliverable signed pays the table read; no
        // chosen block travels as the flag without a snapshot, which the
        // engine records as produced unsigned.
        ConsultSignatureSnapshot? signatureSnapshot = null;
        if (package.Results?.Any(result => result.Signature == true) == true)
        {
            var signatureSetting = await _settingsStore.GetAsync(appUserId, AccountSettingKeys.ProfileSignatures, cancellationToken);
            var chosen = SignatureBlocks.Chosen(SignatureBlocks.Parse(signatureSetting?.Value));
            if (chosen != null)
            {
                signatureSnapshot = new ConsultSignatureSnapshot(
                    chosen.Id,
                    chosen.Text,
                    chosen.UpdatedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }

        await client.Entities.SignalEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.Initialize),
            new ConsultGenerationJobInitialize(
                jobId,
                appUserId,
                items,
                resolvedPackageRef,
                effectiveInputHash,
                sectionSteps,
                nodes,
                EffectiveInputHashVersion: effectiveInputHashVersion,
                Source: origin.Source,
                ScheduledAtUtc: request.ScheduledAtUtc,
                InputDocumentOrigins: inputOrigins,
                SkippedDocuments: skipped.Count > 0 ? skipped : null,
                Collections: collectionRosters,
                // #373: what the manifest was written against, recorded rather
                // than resolved later — a fork lives in the private registry
                // that nothing outside can read, and a pin can be re-pointed.
                PackageSpecVersion: specVersion,
                // #432: and its title, as the pinned manifest carries it.
                PackageTitle: package.Manifest.Title,
                // #453: and its tags.
                PackageTags: package.Manifest.Tags,
                // v10 (#496): by name, last — a package without a classifier
                // leaves it null and writes the bytes it always wrote.
                Deciding: deciding ? true : null));

        var terminology = await _terminology.GetAsync(cancellationToken);
        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(ConsultGenerationOrchestrator),
            new ConsultGenerationOrchestrationInput(
                request,
                appUserId,
                resolvedPackageRef,
                effectiveInputHash,
                sectionSteps,
                nodes,
                package.ResultNodeId,
                items,
                dataScalars,
                EffectiveInputHashVersion: effectiveInputHashVersion,
                InputTypes: declaredInputTypes,
                SkippedDocuments: skipped.Count > 0 ? skipped : null,
                CatalogRef: _catalog.ResolvedRef,
                Collections: collectionSets,
                Source: origin.Source,
                ReplyToAddress: origin.ReplyToAddress,
                Results: resultDescriptors,
                Inputs: inputs.Effective,
                InputDocumentOrigins: inputOrigins,
                // #398: the rules this job's package is read by, and the contract
                // its record conforms to — the versions this build was built
                // against, as Public/Engine attests them.
                PackageFormatRef: EngineAttestation.RefOf(EngineAttestation.PackageFormatRegistry, _engine.PackageFormat),
                ProvenanceRef: EngineAttestation.RefOf(EngineAttestation.ProvenanceRegistry, _engine.Provenance),
                // #403: the edition the terminology server had loaded, and its build.
                Terminology: terminology?.Terminology,
                TerminologyServerRef: terminology?.ServerRef,
                // v10 (#496): the boundary's inputs, only when there is one.
                Deciding: deciding ? true : null,
                SuppliedInputs: deciding ? SuppliedCarrier(inputs.Supplied) : null,
                // #514: where this runs and what runs it, as Public/Engine attests.
                ApiHost: _engine.ApiHost,
                EngineCommit: _engine.Commit,
                // #518: the choice made at start; the reply leg reads only this.
                EmailRequested: origin.EmailRequested,
                // v11 #513: the expansion facts assembly needs, when macros are declared.
                MacroTexts: macroTexts,
                ProfileName: profileName,
                // v11 #516: the chosen signature as of this start, when the
                // package marks a deliverable signed.
                Signature: signatureSnapshot,
                // #557: the outputs container's kind.
                AccountKind: accountKind),
            new StartOrchestrationOptions { InstanceId = jobId },
            cancellationToken);

        _logger.LogInformation(
            "Consult generation job started. JobId={JobId}, Source={Source}, BlockCount={BlockCount}",
            instanceId,
            origin.Source,
            items.Count);

        return new ConsultGenerationJobStartOutcome(instanceId);
    }

    /// <summary>
    /// The provenance hash and its definition version for a request under a
    /// package of this specVersion. One function, because the job that is born
    /// Failed (#434) records the same hash the job that runs would have.
    /// </summary>
    internal static (string Hash, int Version) EffectiveInputHashOf(
        int specVersion,
        ConsultGenerationRequest request,
        EffectiveInputsResolution inputs)
    {
        var hash = specVersion switch
        {
            >= 10 => ConsultGenerationProvenance.ComputeNestedInputsHash(inputs.Supplied!),
            >= 9 => ConsultGenerationProvenance.ComputeStructuredInputsHash(inputs.Supplied!),
            >= 8 => ConsultGenerationProvenance.ComputeTypedInputsHash(inputs.Supplied!),
            >= 7 => ConsultGenerationProvenance.ComputeDeclaredInputsHash(
                inputs.Supplied!.ToDictionary(pair => pair.Key, pair => pair.Value.Canonical, StringComparer.Ordinal)),
            _ => ConsultGenerationProvenance.ComputeDraftOnlyHash(request)
        };

        var version = specVersion switch
        {
            >= 10 => ConsultGenerationProvenance.NestedInputsHashVersion,
            >= 9 => ConsultGenerationProvenance.StructuredInputsHashVersion,
            >= 8 => ConsultGenerationProvenance.TypedInputsHashVersion,
            >= 7 => ConsultGenerationProvenance.DeclaredInputsHashVersion,
            _ => 2
        };

        return (hash, version);
    }

    /// <summary>The fire set, decided once: the results that hold, the rest with each condition's sentence, the nodes pruned to the firing closure (#355).</summary>
    internal sealed record FireSetDecision(WorkflowPackage Package, List<WorkflowResolvedResult> Firing, List<ConsultSkippedDocument> Skipped);

    /// <summary>
    /// The one code path that decides a fire set — at start (v8) or at the
    /// boundary (v10 § 5), over the supplied inputs and, at the boundary, the
    /// classifiers' answers. Pure over its arguments.
    /// </summary>
    internal static FireSetDecision DecideFireSet(
        WorkflowPackage package,
        IReadOnlyDictionary<string, ConsultInputValue>? supplied,
        IReadOnlyDictionary<string, string>? classifications)
    {
        var firing = new List<WorkflowResolvedResult>();
        var skipped = new List<ConsultSkippedDocument>();

        foreach (var result in package.Results ?? new List<WorkflowResolvedResult>())
        {
            if (WorkflowResultConditions.Holds(result.Condition, supplied, classifications))
            {
                firing.Add(result);
                continue;
            }

            skipped.Add(new ConsultSkippedDocument(
                result.Id,
                result.Label,
                WorkflowResultConditions.Explain(result.Condition!, supplied, classifications)));
        }

        if (firing.Count == 0)
        {
            return new FireSetDecision(package, firing, skipped);
        }

        var narrowed = package with { Results = firing };

        // #355: the fire set decides which NODES run, not only which
        // deliverables assemble. Gated on a skip having happened. With nothing
        // skipped the closure is the identity — the validator requires every
        // node to reach some result — so v5-v7 and every all-firing v8 job
        // keeps a byte-identical durable payload by control flow rather than by
        // an argument, and a package that slipped past that rule still runs its
        // orphan node loudly instead of having it silently pruned away. v10: a
        // classifier reaches no result and is kept — its value was the decision.
        if (skipped.Count > 0)
        {
            var reachable = WorkflowNodeClosure.Reachable(
                firing.Select(result => result.NodeId),
                WorkflowNodeClosure.Edges(narrowed.Nodes!));
            var live = narrowed.Nodes!.Where(node => reachable.Contains(node.Id) || WorkflowNodeKinds.IsClassifier(node)).ToList();
            narrowed = narrowed with { Nodes = live };
        }

        return new FireSetDecision(narrowed, firing, skipped);
    }

    /// <summary>The block skeleton and the fans of a resolved (narrowed, pruned) package over the supplied inputs.</summary>
    internal sealed record SkeletonResolution(
        IReadOnlyList<IReadOnlyDictionary<string, string>> Items,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>> CollectionSets,
        IReadOnlyList<ConsultCollectionRoster> CollectionRosters,
        IReadOnlyList<string> EmptyFanLabels);

    internal static SkeletonResolution ResolveSkeleton(WorkflowPackage package, IReadOnlyDictionary<string, ConsultInputValue>? supplied)
    {
        // v9 (#426): a fan over a caller-supplied array. Its items come from
        // the request — one per element, ids the engine mints — keyed by the
        // literal forEach string, which is what the orchestrator's
        // CollectionIdOf already yields for a non-data source and what the
        // rail matches a roster by. The supplied map is the typed one, so an
        // array of objects fans its elements as carriers.
        var inputsById = (package.Manifest.Inputs ?? new List<WorkflowInputSpec>())
            .ToDictionary(input => input.Id, StringComparer.Ordinal);
        var inputFans = package.Nodes
            .Where(node => WorkflowInputFans.IsInputFan(node.ForEach))
            .Select(node => node.ForEach!)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                key => key,
                key => WorkflowInputFans.Items(
                    inputsById[WorkflowInputFans.InputIdOf(key)],
                    supplied?.GetValueOrDefault(WorkflowInputFans.InputIdOf(key))),
                StringComparer.Ordinal);

        var emptyFanLabels = inputFans
            .Where(fan => fan.Value.Count == 0)
            .Select(fan => $"'{inputsById[WorkflowInputFans.InputIdOf(fan.Key)].Label}'")
            .ToList();

        IReadOnlyList<IReadOnlyDictionary<string, string>> items = emptyFanLabels.Count > 0
            ? Array.Empty<IReadOnlyDictionary<string, string>>()
            : WorkflowPackageBlocks.Resolve(package, inputFans)
                .Select(block => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["id"] = block.Id,
                    ["name"] = block.Name
                })
                .ToList();

        var dataSets = package.Nodes
            .Where(node => node.ForEach != null && !WorkflowInputFans.IsInputFan(node.ForEach))
            .Select(node => node.ForEach![WorkflowNodeBindingSources.DataPrefix.Length..])
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                collectionId => collectionId,
                collectionId => (IReadOnlyList<IReadOnlyDictionary<string, string>>)(package.Data?.Collections.GetValueOrDefault(collectionId)
                        ?? throw new InvalidOperationException($"Package {package.Ref} has no data collection '{collectionId}'."))
                    .Items
                    .Select(item => (IReadOnlyDictionary<string, string>)item.Fields)
                    .ToList(),
                StringComparer.Ordinal);

        var collectionSets = dataSets
            .Concat(inputFans)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

        // #361: the same rosters, slimmed to what a run rail needs. The
        // orchestrator's copy above carries every field including content —
        // the whole standards text — and none of that belongs on a status
        // response the client polls.
        var collectionRosters = collectionSets
            .Select(entry => new ConsultCollectionRoster(
                entry.Key,
                entry.Value
                    .Select(item => new ConsultCollectionItem(
                        item.GetValueOrDefault("id", string.Empty),
                        item.GetValueOrDefault("name", item.GetValueOrDefault("id", string.Empty))))
                    .ToList()))
            .ToList();

        return new SkeletonResolution(items, collectionSets, collectionRosters, emptyFanLabels);
    }

    /// <summary>Every declared deliverable as not produced by an empty fan, keeping a condition's own reason where one skipped it first.</summary>
    internal static List<ConsultSkippedDocument> NotProducedByEmptyFan(
        IReadOnlyList<WorkflowResolvedResult> declaredResults,
        IReadOnlyList<ConsultSkippedDocument> skipped,
        IReadOnlyList<string> emptyLabels)
    {
        var fanReason = $"is written from {string.Join(" and ", emptyLabels)}, which has no entries";
        var conditionSkipped = skipped.ToDictionary(document => document.ResultId, StringComparer.Ordinal);
        return declaredResults
            .Select(result => conditionSkipped.TryGetValue(result.Id, out var byCondition)
                ? byCondition
                : new ConsultSkippedDocument(result.Id, result.Label, fanReason))
            .ToList();
    }

    internal static string EmptyFanDetail(IReadOnlyList<string> emptyLabels) =>
        "No document applies to these inputs. "
        + string.Join(" ", emptyLabels.Select(label => $"{label} has no entries, and every document this package produces is written from them."));

    internal static string NoneApplyDetail(IReadOnlyList<ConsultSkippedDocument> notProduced) =>
        "No document applies to these inputs. " + string.Join(" ", notProduced.Select(s => $"'{s.Label}' {s.Reason}."));

    /// <summary>v10 (#496): the supplied values as their wire JSON, for the boundary to read back.</summary>
    internal static IReadOnlyDictionary<string, string>? SuppliedCarrier(IReadOnlyDictionary<string, ConsultInputValue>? supplied) =>
        supplied?.ToDictionary(pair => pair.Key, pair => pair.Value.AsJson(), StringComparer.Ordinal);

    /// <summary>
    /// #434: the one refusal that leaves a row. A well-formed, authorized
    /// request met a package whose conditions produced nothing — not a request
    /// problem (those return before a job exists) but a fact about this
    /// package and these inputs, which the operator will be asked about. The
    /// record is born Failed in one entity signal: no orchestration, nothing
    /// spent. It carries the provenance a run would have (package, hash,
    /// catalog, origins, title) and no skeleton — nothing ran. The outcome
    /// carries BOTH the job id and the error, the only kind that does, so
    /// each door keeps its message and gains the pointer.
    /// </summary>
    private async Task<ConsultGenerationJobStartOutcome> RecordNoApplicableDeliverableAsync(
        DurableTaskClient client,
        EntityInstanceId entityId,
        string jobId,
        string appUserId,
        ConsultGenerationRequest request,
        WorkflowPackage package,
        EffectiveInputsResolution inputs,
        ConsultGenerationJobOrigin origin,
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? inputOrigins,
        IReadOnlyList<ConsultSkippedDocument> notProduced,
        string detail)
    {
        var specVersion = package.Manifest.SpecVersion;
        var (effectiveInputHash, effectiveInputHashVersion) = EffectiveInputHashOf(specVersion, request, inputs);

        var terminology = await _terminology.GetAsync(CancellationToken.None);
        await client.Entities.SignalEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.RecordStartFailure),
            new ConsultGenerationJobStartFailure(
                new ConsultGenerationJobInitialize(
                    jobId,
                    appUserId,
                    Array.Empty<IReadOnlyDictionary<string, string>>(),
                    package.Ref,
                    effectiveInputHash,
                    EffectiveInputHashVersion: effectiveInputHashVersion,
                    CatalogRef: _catalog.ResolvedRef,
                    Source: origin.Source,
                    ScheduledAtUtc: request.ScheduledAtUtc,
                    SkippedDocuments: notProduced,
                    PackageSpecVersion: specVersion,
                    InputDocumentOrigins: inputOrigins,
                    PackageTitle: package.Manifest.Title,
                    PackageTags: package.Manifest.Tags,
                    PackageFormatRef: EngineAttestation.RefOf(EngineAttestation.PackageFormatRegistry, _engine.PackageFormat),
                    ProvenanceRef: EngineAttestation.RefOf(EngineAttestation.ProvenanceRegistry, _engine.Provenance),
                    Terminology: terminology?.Terminology,
                    TerminologyServerRef: terminology?.ServerRef,
                    ApiHost: _engine.ApiHost,
                    EngineCommit: _engine.Commit),
                detail));

        return new ConsultGenerationJobStartOutcome(
            jobId,
            ConsultGenerationJobStartError.NoApplicableDeliverable,
            detail,
            SenderSafeDetail: detail);
    }

    internal const string ConsultDraftInputId = "consult_draft";

    /// <summary>
    /// Resolves the request's inputs against the package declaration
    /// (package-format-v7.md request contract). v5/v6: only a
    /// consult_draft-only Inputs map is acceptable (folded into the draft by
    /// the caller); v7: the supplied map (or the back-filled legacy draft)
    /// must cover every required declared id and name no undeclared ones.
    /// </summary>
    /// <summary>
    /// The result of turning a request's attached documents into text: the
    /// request with those slots filled and <see cref="ConsultGenerationRequest.InputFiles"/>
    /// cleared, plus what the server observed about each one.
    /// </summary>
    internal sealed record InputFileExtraction(
        ConsultGenerationRequest Request,
        // #428: per document, positionally.
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? Origins,
        string? Error,
        string? Outcome,
        ConsultGenerationJobStartError ErrorKind = ConsultGenerationJobStartError.InputFileUnreadable,
        // Error when it names only a declared id and fixed prose. Null when
        // the id is one the caller made up.
        string? SenderSafeError = null);

    /// <summary>
    /// Reads every attached document and folds its text into the input map
    /// (#238). One refusal fails the whole start: a consult generated from
    /// the inputs that happened to be readable would be a partial referral
    /// presented as a whole one.
    /// </summary>
    /// <summary>
    /// #241: the wait budget depends on which door this came through, and the
    /// asymmetry is deliberate.
    ///
    /// Every start-failure path in EmailIntakeProcessor moves the message to
    /// the Rejected folder, writes a claim and replies to the sender. There is
    /// no "leave it for the next poll" branch. So a transient <c>busy</c> on
    /// that door would permanently reject a referral and tell a clinician
    /// their document could not be read, which would be false. A background
    /// poller can afford to wait; it cannot afford to be wrong.
    /// </summary>
    internal static TimeSpan GateWaitFor(ConsultGenerationJobOrigin origin) =>
        string.Equals(origin.Source, ConsultGenerationJobSources.Email, StringComparison.Ordinal)
            ? DocumentExtraction.BackgroundGateWait
            : DocumentExtraction.InteractiveGateWait;

    internal sealed record InputRefResolution(
        ConsultGenerationRequest Request,
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? Origins,
        string? Error = null,
        ConsultGenerationJobStartError ErrorKind = ConsultGenerationJobStartError.InputRefNotFound);

    /// <summary>
    /// #510: copy each referenced deliverable's text into its slot and record
    /// where it came from. The run must be this account's (a foreign or
    /// unknown id is one answer: not found), completed, still holding its
    /// text, and have the deliverable. The reference itself leaves the
    /// request, as the bytes of a document do.
    /// </summary>
    internal static async Task<InputRefResolution> ResolveInputRefsAsync(
        DurableEntityClient entities,
        IJobOutputsBlobStore outputsBlobs,
        ConsultGenerationRequest request,
        string appUserId,
        CancellationToken cancellationToken)
    {
        if (request.InputRefs is not { Count: > 0 })
        {
            return new InputRefResolution(request, null);
        }

        var inputs = request.Inputs is { Count: > 0 }
            ? new Dictionary<string, ConsultInputValue>(request.Inputs, StringComparer.Ordinal)
            : new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal);
        var origins = new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>(StringComparer.Ordinal);
        var sources = new Dictionary<string, ConsultGenerationJobState?>(StringComparer.Ordinal);

        foreach (var (id, refs) in request.InputRefs)
        {
            var texts = new List<ConsultInputValue>();
            var slotOrigins = new List<ConsultInputOrigin>();

            foreach (var reference in refs)
            {
                if (!sources.TryGetValue(reference.JobId, out var source))
                {
                    var entity = await entities.GetEntityAsync<ConsultGenerationJobState>(
                        new EntityInstanceId(nameof(ConsultGenerationJobEntity), reference.JobId),
                        cancellation: cancellationToken);
                    source = entity?.State;
                    // Someone else's run is not found, not forbidden: a 403
                    // would confirm it exists.
                    if (source != null && !string.Equals(source.AppUserId, appUserId, StringComparison.Ordinal))
                    {
                        source = null;
                    }

                    // #557: a source completed after the migration holds its
                    // text in the outputs blob — hydrate before the reads
                    // below, so the copy and its digest see the real text. A
                    // missing blob leaves the state unhydrated and falls into
                    // the text-deleted refusal, never a silent empty copy.
                    if (source is { OutputsBlob: not null, TextDroppedAtUtc: null })
                    {
                        var payload = await outputsBlobs.ReadAsync(source.OutputsBlob, cancellationToken);
                        if (payload != null)
                        {
                            JobOutputsHydration.Apply(source, payload);
                        }
                    }

                    sources[reference.JobId] = source;
                }

                var run = ShortRunId(reference.JobId);
                if (source == null)
                {
                    return new InputRefResolution(request, null,
                        $"Input '{id}' refers to run {run}, which this account does not have.",
                        ConsultGenerationJobStartError.InputRefNotFound);
                }

                if (!string.Equals(source.Status, ConsultGenerationJobStatuses.Completed, StringComparison.Ordinal))
                {
                    return new InputRefResolution(request, null,
                        $"Input '{id}' refers to run {run}, which did not complete.",
                        ConsultGenerationJobStartError.InputRefNotCompleted);
                }

                var document = source.AssembledDocuments?.FirstOrDefault(d => string.Equals(d.ResultId, reference.ResultId, StringComparison.Ordinal));
                if (document == null)
                {
                    return new InputRefResolution(request, null,
                        $"Input '{id}' refers to run {run}, which has no deliverable '{reference.ResultId}'.",
                        ConsultGenerationJobStartError.InputRefNotFound);
                }

                if (source.TextDroppedAtUtc != null || document.Text == null)
                {
                    var when = source.TextDroppedAtUtc is { } dropped ? $" on {dropped:yyyy-MM-dd}" : string.Empty;
                    return new InputRefResolution(request, null,
                        $"Input '{id}' refers to run {run}, whose produced text was deleted{when} under the retention policy.",
                        ConsultGenerationJobStartError.InputRefTextDeleted);
                }

                texts.Add(ConsultInputValue.OfText(document.Text));
                slotOrigins.Add(new ConsultInputOrigin(
                    ConsultInputOriginKinds.PreviousRun,
                    TextSha256: ConsultGenerationProvenance.Sha256Hex(CanonicalText.Normalize(document.Text)),
                    SourceJobId: reference.JobId,
                    SourceResultId: reference.ResultId));
            }

            // A text slot takes one; several become an array, as documents do.
            // The declaration is checked downstream, the way it is for files.
            inputs[id] = texts.Count == 1 ? texts[0] : ConsultInputValue.OfArray(texts);
            origins[id] = slotOrigins;
        }

        return new InputRefResolution(request with { Inputs = inputs, InputRefs = null }, origins);
    }

    private static string ShortRunId(string jobId) => jobId.Length > 8 ? jobId[..8] + "…" : jobId;

    private static IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? MergeOrigins(
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? first,
        IReadOnlyDictionary<string, IReadOnlyList<ConsultInputOrigin>>? second)
    {
        if (first is not { Count: > 0 }) return second;
        if (second is not { Count: > 0 }) return first;
        var merged = new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>(first, StringComparer.Ordinal);
        foreach (var (id, list) in second) merged[id] = list;
        return merged;
    }

    internal static async Task<InputFileExtraction> ExtractInputFilesAsync(
        ConsultGenerationRequest request,
        WorkflowPackageManifest manifest,
        TimeSpan gateWait,
        CancellationToken cancellationToken)
    {
        if (request.InputFiles is not { Count: > 0 })
        {
            return new InputFileExtraction(request, null, null, null);
        }

        var inputs = request.Inputs is { Count: > 0 }
            ? new Dictionary<string, ConsultInputValue>(request.Inputs, StringComparer.Ordinal)
            : new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal);
        var origins = new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>(StringComparer.Ordinal);

        // v5/v6 declare nothing: the one implicit slot is a text.
        var declared = manifest.SpecVersion >= 7
            ? (manifest.Inputs ?? []).ToDictionary(input => input.Id, StringComparer.Ordinal)
            : new Dictionary<string, WorkflowInputSpec>(StringComparer.Ordinal)
            {
                [ConsultDraftInputId] = new(ConsultDraftInputId, "Consult draft")
            };

        foreach (var (id, documents) in request.InputFiles.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            // #428: several documents become the elements of an array of text
            // (v9 design § 7). Anything else takes one — a text slot because
            // concatenation would have to invent a boundary the request is
            // careful never to carry, every other type because a document is
            // text. Checked before any bytes are parsed, once the declaration
            // is known; a lone document into an undeclared slot keeps today's
            // path and is refused by name further on.
            var spec = declared.GetValueOrDefault(id);
            var several = spec != null
                && WorkflowInputTypes.Of(spec) == WorkflowInputTypes.Array
                && WorkflowInputTypes.ElementTypeOf(spec) == WorkflowInputTypes.Text;

            if (documents.Count > 1 && !several)
            {
                if (spec is null)
                {
                    return new InputFileExtraction(
                        request, null, $"Input '{id}' is not declared by this package and takes no documents.", null,
                        ConsultGenerationJobStartError.InputsMismatch);
                }

                var takesOne = $"Input '{id}' is {DescribeDeclaredType(spec)} and takes one document; declare it an array of text to supply several.";

                return new InputFileExtraction(
                    request, null, takesOne, null, ConsultGenerationJobStartError.InputsMismatch, SenderSafeError: takesOne);
            }

            var texts = new List<ConsultInputValue>(documents.Count);
            var slotOrigins = new List<ConsultInputOrigin>(documents.Count);
            var total = 0;

            for (var index = 0; index < documents.Count; index++)
            {
                // #512: the file's digest, over the bytes as received and before
                // the parser touches them — the one thing the bytes leave on the
                // record, since they are cleared below and never kept.
                var fileSha256 = ConsultGenerationProvenance.Sha256Hex(documents[index].Content);
                var result = await DocumentExtraction.ExtractAsync(documents[index].Content, gateWait, cancellationToken);

                if (!DocumentExtraction.Succeeded(result))
                {
                    // Positioned — counted from one — only among several, so
                    // a single document's sentence is the preview's.
                    var detail = documents.Count == 1
                        ? DocumentExtractionCopy.For(result.Outcome)
                        : $"Input '{id}' document {index + 1} of {documents.Count}: {DocumentExtractionCopy.For(result.Outcome)}";

                    return new InputFileExtraction(request, null, detail, result.Outcome, SenderSafeError: detail);
                }

                // The aggregate cap (v9 design § 7): each document is bounded
                // by the parser, the slot by what an input may carry — the
                // same bound the door holds typed text to, first knowable
                // here. Raw lengths, before normalisation, as the door's are.
                total += result.Text!.Length;

                if (total > ConsultGenerationJobs.MaxInputLength)
                {
                    var detail = $"Input '{id}' exceeds {ConsultGenerationJobs.MaxInputLength / 1024} KB across its {documents.Count} documents.";

                    return new InputFileExtraction(
                        request, null, detail, null, ConsultGenerationJobStartError.InputTooLong, SenderSafeError: detail);
                }

                // A document that read to nothing is still one element and
                // still one origin: the record says which one it was.
                texts.Add(ConsultInputValue.OfText(result.Text!));
                // #512: the reading's digest is over the text as it enters the
                // effective-input map — extraction has already normalised it,
                // and NormalizeInputs below is idempotent over it — so the two
                // hashes the record carries for one document are the file and
                // exactly the bytes the input hash saw for it.
                slotOrigins.Add(new ConsultInputOrigin(
                    ConsultInputOriginKinds.Document,
                    result.ExtractorId,
                    result.PageCount,
                    result.TrackedChangesResolved,
                    FileSha256: fileSha256,
                    TextSha256: ConsultGenerationProvenance.Sha256Hex(CanonicalText.Normalize(result.Text!))));
            }

            // One document into a text slot is the text it always was, so hash
            // definitions 3 and 4 see the same bytes they did.
            inputs[id] = several ? ConsultInputValue.OfArray(texts) : texts[0];
            origins[id] = slotOrigins;
        }

        // InputFiles cleared here, and this is load-bearing rather than tidy:
        // the request is carried verbatim into the orchestration input, which
        // Durable persists to the storage account and spills to blob past the
        // inline limit. Leaving the bytes on would put every attached document
        // at rest with no retention story, contradicting the promise that
        // extraction keeps them transient (docs/DOCUMENT_INPUT.md § 5).
        // Nothing downstream needs them: the text is in Inputs.
        return new InputFileExtraction(
            request with { Inputs = inputs, InputFiles = null },
            origins,
            null,
            null);
    }

    /// <summary>"a text", "an enum", "an array of object" — for a sentence about a declaration.</summary>
    private static string DescribeDeclaredType(WorkflowInputSpec spec) => WorkflowDeclarationNode.Of(spec).Describe();

    /// <summary>
    /// Line endings to LF, trailing whitespace off the end — applied to every
    /// input, typed and extracted alike, before the effective-input hash sees
    /// any of it (#238, docs/DOCUMENT_INPUT.md § 2).
    ///
    /// Nothing here normalised before, so the same referral pasted from a
    /// Windows editor and attached as a file hashed differently for no reason
    /// a reader of the record could see. Normalising only extracted text would
    /// have kept that split, which is the property this milestone exists to
    /// close. The hash *definition* is unchanged — only the text reaching it —
    /// so DeclaredInputsHashVersion stays 3.
    /// </summary>
    internal static ConsultGenerationRequest NormalizeInputs(ConsultGenerationRequest request)
    {
        var draft = CanonicalText.Normalize(request.ConsultDraft);

        if (request.Inputs is not { Count: > 0 })
        {
            return request with { ConsultDraft = draft };
        }

        var normalized = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal);

        foreach (var (id, value) in request.Inputs)
        {
            normalized[id] = NormalizeValue(value);
        }

        return request with { ConsultDraft = draft, Inputs = normalized };
    }

    /// <summary>
    /// Only text carries whitespace worth normalising; a boolean has none,
    /// and normalising it would erase the type. A number is the digits the
    /// caller sent. Structure is rebuilt with every text scalar inside it
    /// normalised — per element, per field — in the order it arrived, which
    /// definition 5 treats as significant (#422, v9 § 8). The "hook
    /// CanonicalText does not have" is this composition rather than a change
    /// to CanonicalText, which stays the one rule applied to one string.
    /// </summary>
    private static ConsultInputValue NormalizeValue(ConsultInputValue value) => value.Kind switch
    {
        ConsultInputKind.Text => ConsultInputValue.OfText(CanonicalText.Normalize(value.Text ?? string.Empty)),
        ConsultInputKind.Object => ConsultInputValue.OfObject(
            value.Fields!.Select(field => new ConsultInputEntry(field.Id, NormalizeValue(field.Value)))),
        ConsultInputKind.Array => ConsultInputValue.OfArray(value.Elements!.Select(NormalizeValue)),
        _ => value
    };

    internal static EffectiveInputsResolution ResolveEffectiveInputs(
        ConsultGenerationRequest request,
        WorkflowPackageManifest manifest)
    {
        if (manifest.SpecVersion < 7)
        {
            if (request.Inputs is { Count: > 0 })
            {
                var foreign = request.Inputs.Keys
                    .Where(id => !string.Equals(id, ConsultDraftInputId, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (foreign.Count > 0)
                {
                    return new EffectiveInputsResolution(null, null,
                        $"Inputs names undeclared input(s) {string.Join(", ", foreign.Select(id => $"'{id}'"))}: a specVersion {manifest.SpecVersion} package accepts only consult_draft.");
                }

                // v9 layer 1 (#421): the draft folds into the v5/v6 shape through
                // its canonical string, which structure does not have. Refused
                // here, where the slot can be named, rather than thrown at the
                // fold. An authored id and fixed prose, so sender-safe.
                if (request.Inputs.TryGetValue(ConsultDraftInputId, out var draft)
                    && draft.Kind is ConsultInputKind.Number or ConsultInputKind.Object or ConsultInputKind.Array)
                {
                    var legacyDetail = $"Input 'consult_draft' is the consult draft and must be sent as a JSON string; got {draft.Described}.";
                    return new EffectiveInputsResolution(null, null, legacyDetail, legacyDetail);
                }
            }

            return new EffectiveInputsResolution(null, null, null);
        }

        var declared = manifest.Inputs ?? new List<WorkflowInputSpec>();
        var supplied = request.Inputs is { Count: > 0 }
            ? request.Inputs
            : !string.IsNullOrWhiteSpace(request.ConsultDraft)
                ? new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
                    { [ConsultDraftInputId] = ConsultInputValue.OfText(request.ConsultDraft) }
                : null;

        if (supplied is null)
        {
            const string noneSupplied = "No inputs were supplied.";
            return new EffectiveInputsResolution(null, null, noneSupplied, noneSupplied);
        }

        var declaredIds = declared.Select(input => input.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = supplied.Keys
            .Where(id => !declaredIds.Contains(id))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (unknown.Count > 0)
        {
            // No SenderSafeError, deliberately: these ids are the CALLER's, and
            // on the email door an input id is an attachment's filename stem —
            // "Smith_John_referral" (EmailAttachmentInputs). Email cannot in
            // fact reach this branch, since it only ever assigns slots it
            // matched against declared ids, but the guarantee is about the
            // sentence rather than the door that produced it.
            return new EffectiveInputsResolution(null, null,
                $"Unknown input(s) {string.Join(", ", unknown.Select(id => $"'{id}'"))} (declared: {string.Join(", ", declaredIds.Order(StringComparer.Ordinal))}).");
        }

        var missing = declared
            .Where(input => input.Required
                && (!supplied.TryGetValue(input.Id, out var value) || value.IsBlank))
            .Select(input => input.Id)
            .ToList();
        if (missing.Count > 0)
        {
            // Every id here is DECLARED — it is missing from the request, so it
            // was read off the manifest, not out of the caller's message. This
            // is the sentence an emailed refusal most often needs.
            var missingDetail = $"Required input(s) {string.Join(", ", missing.Select(id => $"'{id}'"))} missing.";
            return new EffectiveInputsResolution(null, null, missingDetail, missingDetail);
        }

        // v8: a supplied value must be canonical for its declared type
        // (package-format-v8-design.md § 4). Rejected, never normalised —
        // silently rewriting '2026-8-1' would hash a value nobody sent, and
        // provenance would record input that never arrived. An absent optional
        // is not checked: absence is not a malformed value.
        foreach (var input in declared)
        {
            if (!supplied.TryGetValue(input.Id, out var value) || value.IsBlank)
            {
                continue;
            }

            var expected = CanonicalFormComplaint(input, value);
            if (expected != null)
            {
                // No SenderSafeError: every canonical-form complaint ends in
                // got '<the supplied value>'. A date slot's rejected value is a
                // date of service — the plain case for why a refusal reply may
                // not quote what was sent. The web door still shows it.
                return new EffectiveInputsResolution(null, null, $"Input '{input.Id}' {expected}");
            }
        }

        // The resolver map covers every declared id — an absent optional input
        // renders as empty (package-format-v7-design.md § 3 resolution rule).
        var effective = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var input in declared)
        {
            // The resolver map is strings: item:, node: and data: bindings are
            // strings too, and the renderer re-types the declared inputs from
            // VariableTypes rather than carrying a union here. A scalar is its
            // canonical string; structure is its carrier (ResolverForm).
            effective[input.Id] = supplied.TryGetValue(input.Id, out var value)
                ? ResolverForm(value)
                : string.Empty;
        }

        return new EffectiveInputsResolution(effective, supplied, null);
    }

    /// <summary>
    /// The one place shape is decided (v9 § 10, #423): what a supplied value
    /// becomes on the string-map road to the renderer. A scalar is its
    /// canonical string, as v7 and v8 carried it. Structure has none and
    /// travels as its wire JSON instead, reconstructed at the last hop by the
    /// renderer, which is told to by the variable's declared type — the same
    /// mechanism v8 used for a date and a boolean. Lossless, so no second
    /// payload slot is needed and every in-flight job replays untouched: the
    /// maps have the same shape they always had.
    /// </summary>
    internal static string ResolverForm(ConsultInputValue value) =>
        value.HasCanonical ? value.Canonical : value.AsJson();

    /// <summary>
    /// What is wrong with a supplied value for its declared type, or null when
    /// nothing is. Phrased to complete "Input '&lt;id&gt;' …" so the caller
    /// always names the slot — a message that says only "invalid date" makes
    /// the author hunt for which field.
    ///
    /// v9 (#424): declaration-aware. A number wants a JSON number; an object
    /// wants exactly its declared fields, each a canonical scalar; an array
    /// wants every element canonical for its items, names the element that
    /// is not, and — required — has at least one. A v8 scalar slot still
    /// refuses structure. Messages name kinds, ids and indices, never a
    /// structured value.
    /// </summary>
    internal static string? CanonicalFormComplaint(WorkflowInputSpec input, ConsultInputValue value)
        => ValueComplaint(WorkflowDeclarationNode.Of(input), value, where: string.Empty);

    /// <summary>
    /// v10 (#493): the one rule at every level. A declaration node is an
    /// input, a field or an array's element; the sentence completes "Input
    /// '&lt;id&gt;' " with the path spelled — "element 1 field 'contact' field
    /// 'phone' is a text and …" — and a one-level declaration reads exactly
    /// as it did at v9.
    /// </summary>
    private static string? ValueComplaint(WorkflowDeclarationNode node, ConsultInputValue value, string where) =>
        node.Type switch
        {
            WorkflowInputTypes.Object => ObjectComplaint(node, value, where),
            WorkflowInputTypes.Array => ArrayComplaint(node, value, where),
            _ => ScalarComplaint(node.Type, node.Values, value) is { } scalar ? $"{where}{scalar}" : null
        };

    /// <summary>
    /// The scalar rule, for an input, a field or an array element alike. Shape
    /// first: the JSON kind and the declared type must agree — this is the 422
    /// half of the strictness, a well-formed request whose value disagrees
    /// with the declaration. Then the canonical form: a date's spelling, an
    /// enum's membership.
    /// </summary>
    private static string? ScalarComplaint(string type, IReadOnlyList<string>? values, ConsultInputValue value)
    {
        if (type == WorkflowInputTypes.Number)
        {
            return value.IsNumber ? null : $"is a number and must be sent as a JSON number; got {value.Described}.";
        }

        // A number, an object, an array or a null in a slot declared as one of
        // v8's scalars. The message names the kind, never the value.
        if (value.Kind is ConsultInputKind.Number or ConsultInputKind.Object or ConsultInputKind.Array or ConsultInputKind.Null)
        {
            return type == WorkflowInputTypes.Boolean
                ? $"is a boolean and must be sent as JSON true or false; got {value.Described}."
                : $"is a {type} and must be sent as a JSON string; got {value.Described}.";
        }

        if (type == WorkflowInputTypes.Boolean && !value.IsBoolean)
        {
            return $"is a boolean and must be sent as JSON true or false, not a string; got '{value.Canonical}'.";
        }

        if (type != WorkflowInputTypes.Boolean && value.IsBoolean)
        {
            return $"is a {type} and must be sent as a JSON string; got a boolean.";
        }

        return type switch
        {
            WorkflowInputTypes.Date when !DateOnly.TryParseExact(
                value.Canonical, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) =>
                $"is a date and must be written YYYY-MM-DD; got '{value.Canonical}'.",

            WorkflowInputTypes.Enum when values?.Contains(value.Canonical, StringComparer.Ordinal) != true =>
                $"accepts {string.Join(", ", (values ?? new List<string>()).Select(v => $"'{v}'"))}; got '{value.Canonical}'.",

            _ => null
        };
    }

    /// <summary>
    /// An object against its declared fields: exactly the declared keys, every
    /// required one present, no null (absence is spelled by leaving the key
    /// out — v9 § 4, operator's decision), each value canonical for its field.
    /// <paramref name="where"/> is empty for an object input and "element N "
    /// for an element of an array of objects.
    /// </summary>
    private static string? ObjectComplaint(WorkflowDeclarationNode node, ConsultInputValue value, string where)
    {
        if (!value.IsObject)
        {
            return $"{where}is an object and must be sent as a JSON object; got {value.Described}.";
        }

        var fields = node.Fields ?? Array.Empty<WorkflowDeclarationNode>();
        var declared = fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
        var supplied = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in value.Fields!)
        {
            if (!declared.TryGetValue(entry.Id, out var field))
            {
                return $"{where}has a field '{entry.Id}' it does not declare (fields: {string.Join(", ", fields.Select(f => f.Id))}).";
            }

            supplied.Add(entry.Id);

            if (entry.Value.IsNull)
            {
                return $"{where}field '{entry.Id}' is null; omit an optional field instead.";
            }

            var complaint = ValueComplaint(field, entry.Value, where: $"{where}field '{entry.Id}' ");
            if (complaint != null)
            {
                return complaint;
            }
        }

        var missing = fields.FirstOrDefault(field => field.Required && !supplied.Contains(field.Id));

        return missing is null ? null : $"{where}is missing required field '{missing.Id}'.";
    }

    /// <summary>
    /// An array against its declared items: a JSON array; not empty when the
    /// input is required (present and empty is not absent, v9 § 4, so the
    /// required check above let it through to be refused here by name); no
    /// null element; each element canonical for the items — a scalar rule, or
    /// the object rule for an array of objects.
    /// </summary>
    private static string? ArrayComplaint(WorkflowDeclarationNode node, ConsultInputValue value, string where)
    {
        if (!value.IsArray)
        {
            return $"{where}is an array and must be sent as a JSON array; got {value.Described}.";
        }

        if (node.Required && value.Elements!.Count == 0)
        {
            return $"{where}is required and has no entries.";
        }

        // The element's declaration: a bare items keeps the v9 reading; an
        // element spec (v10) carries its own shape. Text when undeclared.
        var element = node.Items ?? new WorkflowDeclarationNode(WorkflowInputTypes.Text, node.Label, true, null, null, null);

        for (var index = 0; index < value.Elements!.Count; index++)
        {
            if (value.Elements[index].IsNull)
            {
                return $"{where}element {index} is null.";
            }

            var complaint = ValueComplaint(element, value.Elements[index], where: $"{where}element {index} ");
            if (complaint != null)
            {
                return complaint;
            }
        }

        return null;
    }

    internal static ConsultNodeDescriptor DescribeNode(
        WorkflowNodeSpec node,
        IReadOnlyDictionary<string, string>? schemaContracts)
    {
        return new ConsultNodeDescriptor(
            node.Id,
            node.Label,
            node.Prompt,
            node.Bindings?.ToDictionary(
                pair => pair.Key,
                pair => new ConsultNodeBindingDescriptor(pair.Value.From, pair.Value.As),
                StringComparer.Ordinal),
            // v10 (#495): a classifier's contract is implied by its kind — the
            // one shape no package declares by schema id.
            OutputContract: WorkflowNodeKinds.IsClassifier(node)
                ? OutputContracts.Classification
                : node.Output is null
                    ? null
                    : schemaContracts?.GetValueOrDefault(node.Output.Schema)
                        ?? throw new InvalidOperationException(
                            $"Node '{node.Id}' declares schema '{node.Output.Schema}' with no resolved output contract."),
            FailIfEmpty: node.Output?.FailIfEmpty,
            ForEach: node.ForEach,
            ConceptSource: WorkflowNodeDefaults.WellKnownConceptSources.GetValueOrDefault(node.Id, node.Id),
            Aggregate: node.Aggregate,
            Values: WorkflowNodeKinds.IsClassifier(node) ? node.Values : null,
            // v11 #550: only true or null — an indifferent or pre-v11 node
            // writes the bytes it always wrote.
            Reproducible: node.Reproducible == true ? true : null);
    }
}
