using System.Globalization;
using Consultologist.Api.Agents;
using Consultologist.Api.Documents;
using Consultologist.Api.Models;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.Workflow;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
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
    string? ReplyToAddress = null);

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

    public ConsultGenerationJobStarter(
        ILogger<ConsultGenerationJobStarter> logger,
        IWorkflowPackageStore packageStore,
        IWorkflowPackagePinResolver pinResolver,
        OutputContractCatalog catalog,
        IAccountRateLimiter rateLimiter,
        IWorkflowPackageOwnership ownership,
        EngineAttestationResponse engine,
        ITerminologyAttestationSource terminology)
    {
        _logger = logger;
        _packageStore = packageStore;
        _pinResolver = pinResolver;
        _catalog = catalog;
        _engine = engine;
        _terminology = terminology;
        _rateLimiter = rateLimiter;
        _ownership = ownership;
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
        var inputOrigins = extraction.Origins;

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
        // Filtering the PACKAGE rather than teaching the engine about
        // conditions is the whole trick: block expansion, deliverable
        // resolution and the outcome rule all walk the result list and need no
        // change at all.
        var skipped = new List<ConsultSkippedDocument>();
        // #434: the deliverables as declared, before the fire set narrows
        // package.Results — the born-Failed record lists every one of them.
        var declaredResults = package.Results ?? new List<WorkflowResolvedResult>();

        if (package.Results is { Count: > 0 })
        {
            var firing = new List<WorkflowResolvedResult>();

            foreach (var result in package.Results)
            {
                if (WorkflowResultConditions.Holds(result.Condition, inputs.Supplied))
                {
                    firing.Add(result);
                    continue;
                }

                skipped.Add(new ConsultSkippedDocument(
                    result.Id,
                    result.Label,
                    WorkflowResultConditions.Explain(result.Condition!, inputs.Supplied)));
            }

            if (firing.Count == 0)
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
                var noneApplyDetail = "No document applies to these inputs. "
                    + string.Join(" ", skipped.Select(s => $"'{s.Label}' {s.Reason}."));

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

            package = package with { Results = firing };

            // #355: the fire set decides which NODES run, not only which
            // deliverables assemble. Filtering Results alone left a node whose
            // only deliverable was skipped executing anyway — paid for, and the
            // document it assembled discarded. Worse, a scalar prompt node's
            // await in the engine is unguarded, so a failure in that dead branch
            // failed a job whose every firing deliverable was assemblable.
            //
            // Pruning the package's nodes here, beside its results, is the
            // follow-through on filtering the package rather than teaching the
            // engine about conditions: block expansion, the collection sets, the
            // item steps and the node descriptors all derive from `package` and
            // need no change of their own.
            //
            // Gated on a skip having happened. With nothing skipped the closure
            // is the identity — the validator requires every node to reach some
            // result — so v5-v7 and every all-firing v8 job keeps a
            // byte-identical durable payload by control flow rather than by an
            // argument, and a package that slipped past that rule still runs its
            // orphan node loudly instead of having it silently pruned away.
            if (skipped.Count > 0)
            {
                var reachable = WorkflowNodeClosure.Reachable(
                    firing.Select(result => result.NodeId),
                    WorkflowNodeClosure.Edges(package.Nodes!));
                var live = package.Nodes!.Where(node => reachable.Contains(node.Id)).ToList();

                if (live.Count < package.Nodes!.Count)
                {
                    _logger.LogInformation(
                        "Pruned nodes outside the fire set. Package={Package}, Firing={Firing}, Skipped={Skipped}, Dropped={Dropped}",
                        package.Ref,
                        firing.Count,
                        skipped.Count,
                        string.Join(", ", package.Nodes!.Where(node => !reachable.Contains(node.Id)).Select(node => node.Id)));
                }

                package = package with { Nodes = live };
            }
        }

        IReadOnlyList<IReadOnlyDictionary<string, string>> items;
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? collectionSets = null;
        IReadOnlyList<ConsultCollectionRoster>? collectionRosters = null;

        if (package.Manifest.SpecVersion >= 6)
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
                        inputs.Supplied?.GetValueOrDefault(WorkflowInputFans.InputIdOf(key))),
                    StringComparer.Ordinal);

            // An empty fan produces no items, no blocks, no document — v8's
            // empty-fire-set case wearing different clothes, and recorded the
            // same way: at start, by name, before anything is spent (v9 § 5,
            // #434). Every declared deliverable is the one not produced, for
            // the same reason, so the record lists each of them with it.
            var emptyFans = inputFans.Where(fan => fan.Value.Count == 0).Select(fan => fan.Key).ToList();
            if (emptyFans.Count > 0)
            {
                var emptyLabels = emptyFans.Select(key => $"'{inputsById[WorkflowInputFans.InputIdOf(key)].Label}'").ToList();
                var emptyDetail = "No document applies to these inputs. "
                    + string.Join(" ", emptyLabels.Select(label => $"{label} has no entries, and every document this package produces is written from them."));
                // Every declared deliverable, in declaration order: the ones the
                // fire set skipped keep their condition's reason; the ones that
                // would have fired are not produced because they are written
                // from the empty fan. Phrased to follow "not produced — it".
                var fanReason = $"is written from {string.Join(" and ", emptyLabels)}, which has no entries";
                var conditionSkipped = skipped.ToDictionary(document => document.ResultId, StringComparer.Ordinal);
                var notProduced = declaredResults
                    .Select(result => conditionSkipped.TryGetValue(result.Id, out var byCondition)
                        ? byCondition
                        : new ConsultSkippedDocument(result.Id, result.Label, fanReason))
                    .ToList();

                _logger.LogWarning(
                    "Rejected job start: a fanned input has no entries. JobId={JobId}, Package={Package}, Fans={Fans}",
                    jobId,
                    package.Ref,
                    string.Join(", ", emptyFans));

                // Authored labels and fixed prose: the sender may read it.
                return await RecordNoApplicableDeliverableAsync(
                    client, entityId, jobId, appUserId, request, package, inputs, origin, inputOrigins, notProduced, emptyDetail);
            }

            items = WorkflowPackageBlocks.Resolve(package, inputFans)
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

            collectionSets = dataSets
                .Concat(inputFans)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

            // #361: the same rosters, slimmed to what a run rail needs. The
            // orchestrator's copy above carries every field including content —
            // the whole standards text — and none of that belongs on a status
            // response the client polls.
            collectionRosters = collectionSets
                .Select(entry => new ConsultCollectionRoster(
                    entry.Key,
                    entry.Value
                        .Select(item => new ConsultCollectionItem(
                            item.GetValueOrDefault("id", string.Empty),
                            item.GetValueOrDefault("name", item.GetValueOrDefault("id", string.Empty))))
                        .ToList()))
                .ToList();
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
        var sectionSteps = package.Nodes
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
            .Select(result => new ConsultResultDescriptor(result.Id, result.NodeId, result.Label))
            .ToList();

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
                PackageTags: package.Manifest.Tags));

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
                TerminologyServerRef: terminology?.ServerRef),
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
                    TerminologyServerRef: terminology?.ServerRef),
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
                slotOrigins.Add(new ConsultInputOrigin(
                    ConsultInputOriginKinds.Document,
                    result.ExtractorId,
                    result.PageCount,
                    result.TrackedChangesResolved));
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
            OutputContract: node.Output is null
                ? null
                : schemaContracts?.GetValueOrDefault(node.Output.Schema)
                    ?? throw new InvalidOperationException(
                        $"Node '{node.Id}' declares schema '{node.Output.Schema}' with no resolved output contract."),
            FailIfEmpty: node.Output?.FailIfEmpty,
            ForEach: node.ForEach,
            ConceptSource: WorkflowNodeDefaults.WellKnownConceptSources.GetValueOrDefault(node.Id, node.Id),
            Aggregate: node.Aggregate);
    }
}
