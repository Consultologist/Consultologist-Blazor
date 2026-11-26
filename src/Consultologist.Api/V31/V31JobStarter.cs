using System.Text.Json.Nodes;
using Consultologist.Api.Auth;
using Consultologist.Api.Documents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.V31;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 1.2 steps 1 to 6, § 5.3, § 7.2; part 06 § 10.4; R1, R4, R5; D5, D6, D9): the
/// job-start door's 31 branch. <see cref="ConsultGenerationJobStarter"/> calls it after the hold, the rate limit and the
/// package ref, and only this deployment runs 31 (<see cref="SpecVersionAdmission.Runs31"/>) asks the compiled store: a
/// version with no 31 stamp is today's path's (null, after one <c>publish.json</c> read). A load refusal is a start failure
/// recorded on the job (<c>RecordStartFailure</c>, the clinician's sentence; the operator's in the log). Otherwise the
/// request's typed inputs, its documents and its references (#1509: <c>InputRefs</c>, <c>InputFormRefs</c> and
/// <c>InputFormFiles</c>; read as today's start reads them), with its <c>MacroChoices</c> read as
/// boolean inputs (#1504; by the macro's <c>if</c>, ruled by the owner on #1507; <see cref="V31StartChecks.Choices"/>), are checked with the account
/// (<see cref="V31StartChecks"/>): any refusal is one recorded start failure holding every sentence. Then the inputs are
/// held (<see cref="IRunInputsStore"/>, where the job's held inputs live), the entity initialised at specVersion 31 with
/// the hashes and the limits, and <see cref="ConsultGenerationOrchestratorV31"/> scheduled with the job's id as its instance
/// id. The run facts are worked out when the orchestration starts (D6), not here.
/// </summary>
public sealed class V31JobStarter(
    SpecVersionAdmission admission,
    ICompiledPackageStore store,
    IRunInputsStore inputs,
    EngineLimitSettings limits,
    IAccountStore accounts,
    IAccountSettingsStore settings,
    EngineAttestationResponse engine,
    ILogger<V31JobStarter> logger,
    // #1503 (D9): the drain before a deploy; null (a test) reads as never draining.
    IEngineDrain? drain = null,
    // #1509: the lineage index a 5–30 start writes (History's "Used by" on the source job); null (a test) writes none.
    IConsultGenerationLinkStore? links = null)
{
    /// <summary>
    /// #1509 (D7): the held inputs document's member naming, in ordinal order, the inputs that hold an element of an earlier
    /// run's deliverable (<see cref="RunInputs.ModelDerivedInputs"/>); absent when none does.
    /// </summary>
    internal const string ModelDerivedMember = "modelDerived";

    /// <summary>What a profile field of <c>needs.profile</c> is read from: the account settings that hold it (D6).</summary>
    private static readonly Dictionary<string, string> ProfileSettings = new(StringComparer.Ordinal)
    {
        ["credentials"] = AccountSettingKeys.ProfileCredentials,
        ["organisation"] = AccountSettingKeys.ProfileOrganisation,
        ["closing"] = AccountSettingKeys.ProfileClosing,
    };

    /// <summary>
    /// #1508 (Review Focus 4): S09-001 for <paramref name="specVersion"/> when it is 31 and this deployment does not admit
    /// it (the preview setting unset), for today's path to answer a 31 pin with; null otherwise.
    /// </summary>
    internal string? NotAdmitted(int specVersion) =>
        specVersion == PublishedStamp.SpecVersion && !admission.Admits(specVersion) ? admission.NotSupported(specVersion) : null;

    /// <summary>
    /// Starts <paramref name="request"/> as a 31 job <paramref name="jobId"/> of <paramref name="packageRef"/>, or null when
    /// this deployment does not run 31 or the version is not a 31 one, so today's path continues unchanged.
    /// <paramref name="extract"/> is the starter's own reading of a request's documents against a manifest.
    /// </summary>
    internal async Task<ConsultGenerationJobStartOutcome?> StartAsync(
        DurableTaskClient client,
        ConsultGenerationRequest request,
        string appUserId,
        ConsultGenerationJobOrigin origin,
        WorkflowPackageRef packageRef,
        string jobId,
        Func<ConsultGenerationRequest, WorkflowPackageManifest, Task<ConsultGenerationJobStarter.InputFileExtraction>> extract,
        CancellationToken cancellationToken)
    {
        if (!admission.Runs31)
            return null;

        var load = await store.TryLoadAsync(packageRef, cancellationToken);
        if (load == null)
            return null;
        if (load.Unreadable)
        {
            // Today's path reads the same registry and answers the failure as it does with 31 off.
            logger.LogWarning("31 load could not read the registry; today's path answers the start. Detail={Detail}", load.OperatorDetail);
            return null;
        }

        var entityId = new EntityInstanceId(nameof(ConsultGenerationJobEntity), jobId);
        if (load.Plan is not { } plan)
        {
            logger.LogWarning(
                "Rejected 31 job start: the version could not be loaded. JobId={JobId}, Package={Package}@{Version}, RefusalId={RefusalId}, Detail={Detail}",
                jobId, packageRef.Name, packageRef.Version, load.RefusalId, load.OperatorDetail ?? load.Refusal);
            var refusal = load.Refusal ?? CompiledPackageStore.HashMismatch(packageRef.Name, packageRef.Version);
            return await RecordStartFailureAsync(
                client, entityId, Header(jobId, appUserId, $"{packageRef.Name}@{packageRef.Version}", origin, request, null), refusal,
                ConsultGenerationJobStartError.PackageContentRejected, senderSafe: refusal);
        }

        // Documents become text as today's start makes them: a document sent to a text input arrives as its text. #1509 (R2):
        // so do its references, read as today's start reads them: an earlier run's deliverable, a held form response, a
        // questionnaire file, each into the input it names (an earlier run's element at its position among typed rows and
        // documents); a refusal is today's.
        var extraction = await extract(request, ManifestOf(plan));
        if (extraction.Error != null)
        {
            logger.LogWarning("Rejected 31 job start: attached documents were refused. Kind={Kind}, Outcome={Outcome}", extraction.ErrorKind, extraction.Outcome);
            return new ConsultGenerationJobStartOutcome(null, extraction.ErrorKind, extraction.Error, SenderSafeDetail: extraction.SenderSafeError);
        }

        request = ConsultGenerationJobStarter.NormalizeInputs(extraction.Request);
        // #1507 (ruled by the owner on #1507, 2026-11-22, 3): each MacroChoices entry is the value of the boolean input its macro's if reads.
        var (supplied, choiceRefusals) = V31StartChecks.Choices(plan, Supplied(request), request.MacroChoices);
        var (profile, signature) = await ProfileAsync(plan, appUserId, request.SignatureBlockId, cancellationToken);
        var timeZone = await TimeZoneAsync(settings, appUserId, cancellationToken);

        // #1505 (R8): the run facts known now as the orchestration will know them (its RunFactsBase, and run.package when the
        // plan reads it); run.date is worked out when the orchestration starts (D6), so an if that reads it is the run's to decide.
        var startFacts = new JsonObject { ["id"] = jobId, ["engine"] = engine.Release };
        if (plan.RunFacts.Contains("run.package", StringComparer.Ordinal))
            startFacts["package"] = $"{plan.Package}@{plan.Version}";

        // #1509 (D7; part 08 § 3.1): an input holding an element of an earlier run's deliverable starts out model-derived.
        var modelDerived = (extraction.Origins ?? new Dictionary<string, IReadOnlyList<ConsultInputOrigin>>())
            .Where(pair => pair.Value.Any(o => o.Kind == ConsultInputOriginKinds.PreviousRun))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var (runInputs, inputRefusals) = V31StartChecks.Inputs(plan, supplied, startFacts, profile, modelDerived);
        var accountRefusals = AccountRefusals(plan, profile, timeZone, signature.Refusal);

        var refusals = accountRefusals.Concat(choiceRefusals).Concat(inputRefusals).ToList();
        var header = Header(jobId, appUserId, $"{plan.Package}@{plan.Version}", origin, request, plan) with
        {
            // #1506 (part 08 § 3.1; D15): each document's origin with what its extraction used.
            InputDocumentOrigins = extraction.Origins is { Count: > 0 } origins
                ? origins.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<ConsultInputOrigin>)pair.Value.Select(origin => ExtractionSteps.Of(origin, extraction.OcrModelVersions)).ToList(),
                    StringComparer.Ordinal)
                : null,
            SignatureBlockId = signature.BlockId,
            SignatureChosen = signature.Chosen,
        };
        // #1506 (hash-definitions.md § 7.2): the inputs' hash, and each input's, once they are read.
        if (runInputs != null)
        {
            header = header with
            {
                EffectiveInputHash = EffectiveInputHash(runInputs.Inputs),
                InputHashes = runInputs.Inputs.ToDictionary(pair => pair.Key, pair => CanonicalJson.Hash(ValueOf(pair.Value)), StringComparer.Ordinal),
            };
        }
        if (refusals.Count > 0)
        {
            logger.LogWarning(
                "Rejected 31 job start: the account or the inputs were refused. JobId={JobId}, Package={Package}@{Version}, Refusals={Refusals}",
                jobId, plan.Package, plan.Version, string.Join(", ", refusals.Select(r => r.Id)));
            // A06-013 (and A10-043, #1507) quote the caller's own ids, which on the email door can be a file name: the
            // sender is told every other sentence, and nothing when those are all there is.
            var senderSafe = refusals.Where(r => r.Id is not ("A06-013" or "A10-043")).Select(r => r.Sentence).ToList();
            return await RecordStartFailureAsync(
                client, entityId, header, string.Join("\n", refusals.Select(r => r.Sentence)),
                ConsultGenerationJobStartError.InputsMismatch, senderSafe.Count > 0 ? string.Join("\n", senderSafe) : null);
        }

        // #1505 (R8): no deliverable can exist on these inputs; refused as a 30 start is, in its words, before anything is held.
        if (V31StartChecks.NoDeliverableCanExist(plan, runInputs!))
        {
            var notProduced = V31StartChecks.NotProduced(plan);
            var detail = ConsultGenerationJobStarter.NoneApplyDetail(notProduced);
            logger.LogWarning(
                "Rejected 31 job start: no deliverable applies to these inputs. JobId={JobId}, Package={Package}@{Version}, Declared={Declared}",
                jobId, plan.Package, plan.Version, notProduced.Count);
            return await RecordStartFailureAsync(
                client, entityId, header with { SkippedDocuments = notProduced }, detail, ConsultGenerationJobStartError.NoApplicableDeliverable, detail);
        }

        var accountKind = await accounts.GetAccountKindAsync(appUserId, cancellationToken);
        var document = new JsonObject
        {
            ["inputs"] = new JsonObject(runInputs!.Inputs.Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone()))),
            ["profile"] = new JsonObject(plan.NeedsProfile.Distinct(StringComparer.Ordinal)
                .Select(field => KeyValuePair.Create(field, (JsonNode?)profile.GetValueOrDefault(field)))),
        };
        // #1509 (D7): the inputs holding an earlier run's text, for the run's decisions; written only when there are any.
        if (runInputs.ModelDerivedInputs.Count > 0)
            document[ModelDerivedMember] = new JsonArray(runInputs.ModelDerivedInputs.Order(StringComparer.Ordinal).Select(id => (JsonNode?)id).ToArray());
        var pointer = await inputs.WriteAsync(accountKind, appUserId, jobId, document, cancellationToken);

        // #1509 (#546): the lineage edges, as a 5–30 start writes them after it holds its inputs: one row per element taken
        // from an earlier run, one for a replay. Best-effort like that start: a storage blip never refuses a start, and the
        // job's own origins remain the truth this index projects.
        if (links != null)
        {
            var lineageLinks = ConsultGenerationJobStarter.LinksFrom(jobId, appUserId, extraction.Origins, origin.RerunOfJobId, DateTimeOffset.UtcNow);
            if (lineageLinks.Count > 0)
            {
                try
                {
                    await links.WriteAsync(lineageLinks, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Lineage links not written; the run proceeds. JobId={JobId}", jobId);
                }
            }
        }

        var recorded = limits.Limits.ToJson();
        await client.Entities.SignalEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.Initialize),
            header with
            {
                InputsBlob = pointer,
                ResolveHash = plan.ResolveHash,
                LockHash = plan.LockHash,
                Limits = (JsonObject)recorded.DeepClone(),
            });

        var orchestrationInput = new ConsultGenerationOrchestrationInputV31(
            jobId,
            appUserId,
            plan.Package,
            plan.Version,
            plan.ResolveHash,
            plan.LockHash,
            recorded,
            (int)limits.RegexLimit.TotalMilliseconds,
            pointer.Name,
            new JsonObject { ["id"] = jobId, ["engine"] = engine.Release },
            timeZone,
            request.ScheduledAtUtc,
            DrainAware: true,
            accountKind,
            // #1507 (R8, D18): the address and delivery.emailPdf as this start read them (#518), never the password
            new V31DeliverySettings(origin.ReplyToAddress, origin.EmailRequested));

        // Part 09 § 3.6 (D9): during a drain the job is held and initialised as above, and waits, parked; the door still answers with its id.
        if (await EngineDrainJobs.ParkIfDrainingAsync(drain, client, jobId, nameof(ConsultGenerationOrchestratorV31), orchestrationInput, accountKind, logger, cancellationToken))
            return new ConsultGenerationJobStartOutcome(jobId);

        var instanceId = await client.ScheduleNewOrchestrationInstanceAsync(
            nameof(ConsultGenerationOrchestratorV31),
            orchestrationInput,
            new StartOrchestrationOptions { InstanceId = jobId },
            cancellationToken);

        logger.LogInformation(
            "Consult generation job started at 31. JobId={JobId}, Source={Source}, Package={Package}@{Version}, ResolveHash={ResolveHash}",
            instanceId, origin.Source, plan.Package, plan.Version, plan.ResolveHash);
        return new ConsultGenerationJobStartOutcome(instanceId);
    }

    /// <summary>
    /// What today's start reads a request's documents against, for <paramref name="plan"/>: its inputs, in order, each
    /// labelled and required as declared, an array of a text format taking several documents, and its channel.
    /// </summary>
    internal static WorkflowPackageManifest ManifestOf(RunPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var declared = new List<WorkflowInputSpec>();
        foreach (var id in plan.InputOrder)
        {
            var input = plan.Inputs[id];
            var format = input["format"];
            var several = plan.Formats.Expand(format) is JsonObject expanded
                && SourceLayout.Text(expanded["type"]) == "array"
                && format is JsonObject site
                && plan.Formats.IsText(site["items"]);
            declared.Add(new WorkflowInputSpec(
                id,
                SourceLayout.Text(input["label"]) ?? id,
                input["required"] is JsonValue required && required.TryGetValue<bool>(out var isRequired) && isRequired,
                Type: several ? WorkflowInputTypes.Array : null,
                Items: several ? WorkflowInputTypes.Text : null,
                ExpectedContent: SourceLayout.Text(input["channel"])));
        }

        return new WorkflowPackageManifest(plan.Package, plan.Version, PublishedStamp.SpecVersion, Inputs: declared);
    }

    /// <summary>The request's values by id, as JSON: its typed inputs, else its consult draft as <c>consult_draft</c>, as today.</summary>
    private static Dictionary<string, JsonNode?> Supplied(ConsultGenerationRequest request)
    {
        var supplied = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        if (request.Inputs is { Count: > 0 } given)
        {
            foreach (var (id, value) in given)
                supplied[id] = JsonNode.Parse(value.AsJson());
        }
        else if (!string.IsNullOrWhiteSpace(request.ConsultDraft))
        {
            supplied[ConsultGenerationJobStarter.ConsultDraftInputId] = request.ConsultDraft;
        }

        return supplied;
    }

    /// <summary>
    /// The profile fields <paramref name="plan"/> needs, each read where the account keeps it (D6): <c>name</c> the
    /// account's display name, <c>signature</c> the text of the block <see cref="V31StartChecks.Signature"/> picks for
    /// <paramref name="requested"/> (#1505, D15), the others their settings. With them, the block used, by id, and how it was
    /// chosen, or A08-004's sentence; all null when the plan reads no signature.
    /// </summary>
    private Task<(Dictionary<string, string?> Profile, (string? BlockId, string? Chosen, string? Refusal) Signature)> ProfileAsync(
        RunPlan plan, string appUserId, string? requested, CancellationToken cancellationToken) =>
        ProfileAsync(settings, accounts, plan, appUserId, requested, cancellationToken);

    /// <summary>
    /// <see cref="ProfileAsync(RunPlan, string, string?, CancellationToken)"/> over the given stores, which the start-checks
    /// route (D1 of #1509) reads an account with, so it answers what a start would.
    /// </summary>
    internal static async Task<(Dictionary<string, string?> Profile, (string? BlockId, string? Chosen, string? Refusal) Signature)> ProfileAsync(
        IAccountSettingsStore settings, IAccountStore accounts, RunPlan plan, string appUserId, string? requested, CancellationToken cancellationToken)
    {
        var profile = new Dictionary<string, string?>(StringComparer.Ordinal);
        (string? BlockId, string? Chosen, string? Refusal) signature = (null, null, null);
        foreach (var field in plan.NeedsProfile.Distinct(StringComparer.Ordinal))
        {
            if (field == "signature")
            {
                var blocks = SignatureBlocks.Parse((await settings.GetAsync(appUserId, AccountSettingKeys.ProfileSignatures, cancellationToken))?.Value);
                var (block, chosen, refusal) = V31StartChecks.Signature(blocks, requested);
                profile[field] = block?.Text;
                signature = (block?.Id, chosen, refusal);
                continue;
            }

            profile[field] = field switch
            {
                "name" => await accounts.GetDisplayNameAsync(appUserId, cancellationToken),
                _ when ProfileSettings.TryGetValue(field, out var key) => (await settings.GetAsync(appUserId, key, cancellationToken))?.Value,
                _ => null,
            };
        }

        return (profile, signature);
    }

    /// <summary>The account's time zone setting, as the start reads it (null when unset).</summary>
    internal static async Task<string?> TimeZoneAsync(IAccountSettingsStore settings, string appUserId, CancellationToken cancellationToken) =>
        (await settings.GetAsync(appUserId, AccountSettingKeys.TimeZone, cancellationToken))?.Value;

    /// <summary>
    /// The account's refusals as a start orders them (#1505, D15): <see cref="V31StartChecks.Account"/>'s, with a signature
    /// block named that the account lacks (<paramref name="signatureRefusal"/>) as A08-004, said in S06-031's place.
    /// </summary>
    internal static IReadOnlyList<SourceRefusal> AccountRefusals(
        RunPlan plan, IReadOnlyDictionary<string, string?> profile, string? timeZone, string? signatureRefusal)
    {
        var refusals = V31StartChecks.Account(plan, profile, timeZone);
        if (signatureRefusal is { } notTheAccounts)
            refusals = refusals.Where(r => r.Id != "S06-031").Prepend(new SourceRefusal("A08-004", notTheAccounts, "")).ToList();
        return refusals;
    }

    /// <summary>
    /// #1506 (specVersion 31, area 09; hash-definitions.md § 7.2, effective-input definition 7): the hash of the supplied
    /// inputs as one canonical (RFC 8785) JSON object keyed by input id, each the <c>value</c> of the input's read form
    /// (<paramref name="readForms"/>: a document's text as its extraction gave it, a boolean choice as its value), absent
    /// optional inputs omitted, written <c>sha256:&lt;hex&gt;</c>.
    /// </summary>
    internal static string EffectiveInputHash(IReadOnlyDictionary<string, JsonNode?> readForms)
    {
        ArgumentNullException.ThrowIfNull(readForms);
        return CanonicalJson.Hash(new JsonObject(readForms.Select(pair => KeyValuePair.Create(pair.Key, ValueOf(pair.Value)?.DeepClone()))));
    }

    /// <summary>The <c>value</c> of a read form (<c>{ value, content, title }</c>).</summary>
    private static JsonNode? ValueOf(JsonNode? readForm) => readForm is JsonObject form ? form["value"] : readForm;

    /// <summary>The job's Initialize as far as the start knows it before its inputs are held.</summary>
    private ConsultGenerationJobInitialize Header(
        string jobId, string appUserId, string workflowPackage, ConsultGenerationJobOrigin origin, ConsultGenerationRequest request, RunPlan? plan) =>
        new(
            jobId,
            appUserId,
            Array.Empty<IReadOnlyDictionary<string, string>>(),
            workflowPackage,
            Source: origin.Source,
            ScheduledAtUtc: request.ScheduledAtUtc,
            PackageSpecVersion: PublishedStamp.SpecVersion,
            // #1506 (hash-definitions.md § 7.2): the definition a 31 job's inputs are hashed by.
            EffectiveInputHashVersion: ConsultGenerationProvenance.EffectiveInputHashVersionAt31,
            PackageTitle: plan == null ? null : SourceLayout.Text(plan.Document["title"]),
            PackageTags: plan?.Document["tags"] is JsonArray tags ? tags.Select(SourceLayout.Text).OfType<string>().ToList() : null,
            PackageFormatRef: EngineAttestation.RefOf(EngineAttestation.PackageFormatRegistry, engine.PackageFormat),
            ProvenanceRef: EngineAttestation.RefOf(EngineAttestation.ProvenanceRegistry, engine.Provenance),
            ApiHost: engine.ApiHost,
            EngineCommit: engine.Commit,
            EngineRelease: engine.Release);

    /// <summary>
    /// A job born Failed (#434's shape): no node runs, the record holds <paramref name="reason"/>, the door gets the job's id
    /// with it, and the email sender is told <paramref name="senderSafe"/> (the sentences that quote nothing the caller sent).
    /// </summary>
    private static async Task<ConsultGenerationJobStartOutcome> RecordStartFailureAsync(
        DurableTaskClient client,
        EntityInstanceId entityId,
        ConsultGenerationJobInitialize header,
        string reason,
        ConsultGenerationJobStartError error,
        string? senderSafe)
    {
        await client.Entities.SignalEntityAsync(
            entityId,
            nameof(ConsultGenerationJobEntity.RecordStartFailure),
            new ConsultGenerationJobStartFailure(header, reason));
        return new ConsultGenerationJobStartOutcome(header.JobId, error, reason, SenderSafeDetail: senderSafe);
    }
}
