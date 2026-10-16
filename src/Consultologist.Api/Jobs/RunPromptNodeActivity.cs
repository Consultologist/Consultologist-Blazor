using System.Diagnostics;
using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Jobs;

/// <summary>
/// Input of the generic prompt-node activity. The orchestrator resolves the bindings
/// to rendered variable values itself (pure functions over recorded activity results);
/// the activity re-resolves the pinned immutable package only for the template text.
/// </summary>
public sealed record ConsultPromptNodeActivityInput(
    string NodeId,
    string PromptId,
    Dictionary<string, string> Variables,
    string? WorkflowPackage = null,
    string? OutputContract = null,
    string? ConceptSource = null,
    // v8: variable name -> declared input type, for the variables bound to a
    // typed input. Trailing optional; a v5-v7 job replays with null and the
    // renderer behaves exactly as it did.
    Dictionary<string, string>? VariableTypes = null,
    // v10 (#495): a classifier's declared values, appended to the prompt and
    // the set the answer must come from. Trailing optional.
    IReadOnlyList<string>? Values = null,
    // v12 #634 (design § 15): the template discriminator — the render is
    // returned as the answer; no agent runs. Trailing optional; a stored
    // payload replays with null and the activity behaves exactly as it did.
    bool? Template = null,
    // #923 phase 2: the agent pin resolved at job-start from the package's own
    // stamped catalog version. When set, the activity runs this agent directly;
    // when null (a template, or a payload snapshotted before phase 2), it falls
    // back to the global catalog. Trailing optional — replay-safe.
    string? AgentName = null,
    string? AgentVersion = null,
    // v23 #955: the node's macro blocks, resolved orchestrator-side (when-gated,
    // tokens substituted) into verbatim text — before blocks precede the prompt,
    // after blocks follow it. The renderer composes them so the InputHash below
    // covers the exact prompt the model saw. Trailing optional; a payload
    // snapshotted before v23 replays with null and the activity renders as it did.
    IReadOnlyList<string>? Before = null,
    IReadOnlyList<string>? After = null,
    // v25 (B2 #1001): the node's format identity, resolved at job-start from the
    // package's own stamped formats version (see ConsultNodeDescriptor). When
    // Format is set the activity never falls back to the global catalogue: a
    // 25 node runs the agent its package was published against, or refuses.
    // Dispatch on the producer is B4's. Trailing optionals — replay-safe.
    string? Format = null,
    string? Producer = null,
    string? FormatHash = null,
    string? Op = null,
    // v25 (B3 #1002, design § 1): the 25 render scope — what the orchestrator
    // holds and the activity cannot re-resolve: the outputs of the nodes this
    // node reads (item-aligned), the snippet folders as settled at job start,
    // the fan item and its source, the run and profile facts. Variables is
    // the effective inputs map at 25. All null below 25. Appended last, same
    // positional-call rule.
    IReadOnlyDictionary<string, string>? NodeTexts = null,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? Folders = null,
    IReadOnlyDictionary<string, string>? Item = null,
    string? ForEach = null,
    TemplateRunFacts? Run = null,
    TemplateProfileFacts? Profile = null,
    // v25 (B4 #1003, design §§ 3.5–3.7): the node's resolved with and its
    // onInvalid policy, threaded from the descriptor for the dispatch on the
    // producer. Null below 25. Appended last, same rule.
    IReadOnlyDictionary<string, string>? With = null,
    string? OnInvalid = null,
    // v25 (B5 #1004, design §§ 5–7): the two facts an instance's `when` is
    // judged against that only the orchestrator holds — the supplied-inputs
    // map as the job was started with (JSON per input, the Initialize shape)
    // and the classifiers' answers so far. Null below 25. Appended last, same rule.
    IReadOnlyDictionary<string, string>? Supplied = null,
    IReadOnlyDictionary<string, string>? Classifications = null,
    // v25 (B5 #1004, design §§ 3.6, 5–6): the node's failIfEmpty sentence,
    // judged on the reply in the activity, and its before/after instances as
    // the descriptor snapshotted them (the same list the scheduler took its
    // dependencies from), composed around the output at completion. Null
    // below 25 (there the instances ride Before/After, prompt-side). Appended last.
    string? FailIfEmpty = null,
    IReadOnlyList<ConsultNodeMacroDescriptor>? Instances = null);

/// <summary>
/// One node run. Deserialized concepts ride the recorded activity result so Durable
/// replay hands the orchestrator identical data without re-parsing; the hashes form
/// the per-node verification chain.
/// </summary>
public sealed record NodeRunResult(
    string RawOutput,
    IReadOnlyList<ClinicalConcept>? Concepts,
    string InputHash,
    string OutputHash,
    // #375: the definition the pair was computed under. Appended last, defaulted:
    // a recorded activity result from before replays with null.
    int? HashVersion = null,
    // v10 (#495): a classifier's normalised answer — one of its declared
    // values. RawOutput keeps the JSON as the agent sent it; OutputHash is the
    // SHA-256 of this value (v10 § 8). Appended last, defaulted.
    string? Classification = null,
    // #551: what the call cost, as the provider reported it. Appended last,
    // defaulted: a recorded activity result from before replays with null,
    // and null is "not recorded", never zero.
    ConsultTokenUsage? Tokens = null,
    // #639: when the activity started and how long it executed — wall clock
    // at entry and the activity's own stopwatch, so the pair is execution
    // time with queue and retry wait excluded. Appended last, defaulted:
    // null is "not recorded", never zero (aggregates, checks and roll-ups
    // record none — no activity ran).
    DateTimeOffset? StartedAtUtc = null,
    long? DurationMs = null,
    // v25 (B4 #1003, design § 3.5): the reply's validation against its format
    // (null on a text format), and a computed node's check outcome (null
    // otherwise). Appended last, same positional-call rule.
    ConsultValidationOutcome? Validation = null,
    ConsultCheckOutcome? Check = null,
    // v25 (B5 #1004, design § 6): the digest of the reply as returned — the
    // model's, the render, the operation's result — beside OutputHash, which
    // at 25 is over RawOutput after the reply renders and the node's instances
    // compose (definition 6); and what that completion placed and
    // excluded. Null below 25. Appended last, same positional-call rule.
    string? AgentOutputHash = null,
    IReadOnlyList<ConsultAppendedEntry>? Appended = null,
    IReadOnlyList<ConsultNodeExclusion>? Excluded = null);

/// <summary>
/// The generic DAG prompt node: renders the node's prompt with orchestrator-resolved
/// variables and sends it to the agent the output-contract catalog pins for the
/// node's contract (the schema is welded to that agent's definition,
/// package-format-v4.md; selection is catalog-keyed, output-contract-catalog.md).
/// </summary>
public sealed class RunPromptNodeActivity
{
    private readonly IWorkflowPackageStore _packageStore;
    private readonly AgentSectionGenerator _agent;
    private readonly OutputContractCatalog _catalog;
    private readonly ILogger<RunPromptNodeActivity> _logger;
    private readonly WorkflowFormatCatalog _formats;

    public RunPromptNodeActivity(
        IWorkflowPackageStore packageStore,
        AgentSectionGenerator agent,
        OutputContractCatalog catalog,
        ILogger<RunPromptNodeActivity> logger,
        WorkflowFormatCatalog formats)
    {
        _packageStore = packageStore;
        _agent = agent;
        _catalog = catalog;
        _logger = logger;
        _formats = formats;
    }

    /// <summary>
    /// v12 #634 (design § 15): the template's whole answer, pure — one hash,
    /// twice: nothing is sent, and the message-that-would-have-been IS the
    /// output. Tokens null (not recorded, never zero). A malformed schema'd
    /// render is re-thrown as the renderer's own fail-fast type: the same
    /// bytes re-render, so retrying is burning money on a proof that already
    /// failed.
    /// </summary>
    internal static NodeRunResult TemplateResult(
        string rendered,
        string? outputContract,
        string? conceptSource,
        string nodeId)
    {
        var hash = ConsultGenerationProvenance.Sha256Hex(rendered);

        IReadOnlyList<ClinicalConcept>? concepts = null;

        if (string.Equals(outputContract, OutputContracts.ConceptList, StringComparison.Ordinal))
        {
            try
            {
                concepts = ConceptOutputContract.Deserialize(rendered, conceptSource ?? nodeId);
            }
            catch (ConceptOutputContractException ex)
            {
                throw new InvalidOperationException(
                    $"Template '{nodeId}' rendered output that is not valid {OutputContracts.ConceptList} JSON: {ex.Message}", ex);
            }
        }

        return new NodeRunResult(rendered, concepts, hash, hash, ConsultGenerationProvenance.NodeHashVersion, null, null);
    }

    [Function(ConsultGenerationActivityNames.RunPromptNode)]
    public async Task<NodeRunResult> RunAsync(
        [ActivityTrigger] ConsultPromptNodeActivityInput input,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            _logger.LogInformation(
                "Starting prompt node. NodeId={NodeId}, PromptId={PromptId}, OutputContract={OutputContract}, Package={Package}",
                input.NodeId,
                input.PromptId,
                input.OutputContract,
                input.WorkflowPackage);

            if (!WorkflowPackageRef.TryParse(input.WorkflowPackage, out var packageRef))
            {
                throw new InvalidOperationException(
                    $"Prompt node '{input.NodeId}' has no usable workflow package ref ('{input.WorkflowPackage}').");
            }

            var package = await _packageStore.ResolveAsync(packageRef!, cancellationToken);

            if (package.Prompts is null || !package.Prompts.TryGetValue(input.PromptId, out var prompt))
            {
                throw new InvalidOperationException(
                    $"Workflow package {package.Ref} has no prompt '{input.PromptId}' for node '{input.NodeId}'.");
            }

            // v9 (#425): the package is already in hand for the template, so
            // its declarations ride along and an object's fields render as
            // their own types — what the publish-time probe checked against.
            // v25 (B3 #1002): a node with a format renders over the seven
            // objects from the scope the orchestrator threaded and the package
            // in hand; below 25 the flat map, as ever.
            // v25 (B5 #1004, design § 7): the macro.<id>.prompt reads the render
            // evaluates are the inline macros the prompt that ran instructs.
            var promptTrace = new TemplateRenderer.TemplateRenderTrace();
            var rendered = input.Format is not null
                ? ConsultRenderPayload.RenderAt25(input, package, prompt.TemplateText, promptTrace)
                : PromptTemplateRenderer.Render(
                    prompt,
                    input.Variables,
                    input.VariableTypes,
                    WorkflowVariableDeclarations.For(package.Manifest),
                    // v23 #955: the node's resolved macro blocks compose around the
                    // prompt here, so `sent`/InputHash below cover them.
                    input.Before,
                    input.After);
            // v12 #634 (design § 15): the first early return — the render IS
            // the answer. The catalog, the agent and the classifier trailer
            // are never touched; the validator guarantees a template never
            // carries the classification contract.
            if (input.Template == true)
            {
                var templateResult = TemplateResult(rendered, input.OutputContract, input.ConceptSource, input.NodeId);

                // v25 (B5 #1004, design § 6): a rendered node's pair stamps 6 and
                // its reply IS the render — agentOutputHash is the same digest.
                if (input.Format is not null)
                {
                    templateResult = templateResult with { HashVersion = ConsultGenerationProvenance.NodeHashVersionAt25, AgentOutputHash = templateResult.OutputHash };
                }

                _logger.LogInformation(
                    "Template node completed. NodeId={NodeId}, ConceptCount={ConceptCount}, Hash={Hash}, ElapsedMs={ElapsedMs}",
                    input.NodeId,
                    templateResult.Concepts?.Count,
                    templateResult.OutputHash,
                    stopwatch.ElapsedMilliseconds);

                // #639: the trace rides every recorded result — template too.
                return templateResult with { StartedAtUtc = startedAtUtc, DurationMs = stopwatch.ElapsedMilliseconds };
            }

            // v25 (B4 #1003): a node with a format — and a 22–24 custom-schema
            // node, #760 retired — dispatches on its producer: what is sent and
            // to whom is planned, the one call is made, the reply is settled
            // under the node's onInvalid.
            if (ConsultNodeDispatch.Applies(input))
            {
                var plan = ConsultNodeDispatch.Plan(input, package, rendered, _catalog, _formats);
                var planInputHash = ConsultGenerationProvenance.Sha256Hex(plan.Sent);
                var reply = await _agent.SendPromptAsync(input.NodeId, plan.Sent, plan.AgentName, plan.AgentVersion, cancellationToken);
                var settled = ConsultNodeDispatch.Settle(plan, input.NodeId, reply.Text, planInputHash, reply.Tokens);

                // v25 (B5 #1004, design §§ 6–7): the completion order, stated
                // once — failIfEmpty on the reply, the reply rendered,
                // the instances composed, outputHash over what came out.
                if (input.Format is not null)
                {
                    settled = ConsultNodeCompletion.Complete(settled, input, package, promptTrace.PromptReads.ToHashSet(StringComparer.Ordinal));
                }

                _logger.LogInformation(
                    "Prompt node completed. NodeId={NodeId}, Producer={Producer}, Valid={Valid}, ConceptCount={ConceptCount}, InputHash={InputHash}, OutputHash={OutputHash}, ElapsedMs={ElapsedMs}",
                    input.NodeId,
                    input.Producer ?? OutputContracts.Custom,
                    settled.Validation?.Valid,
                    settled.Concepts?.Count,
                    settled.InputHash,
                    settled.OutputHash,
                    stopwatch.ElapsedMilliseconds);

                return settled with { StartedAtUtc = startedAtUtc, DurationMs = stopwatch.ElapsedMilliseconds };
            }

            // v10 (#495): a classifier's prompt ends with the values it may
            // answer, in one deterministic form; the message sent is what is
            // hashed. The values reach here on the activity input.
            var isClassifier = string.Equals(input.OutputContract, OutputContracts.Classification, StringComparison.Ordinal);
            var values = input.Values ?? Array.Empty<string>();

            if (isClassifier && values.Count == 0)
            {
                throw new InvalidOperationException($"Classifier '{input.NodeId}' reached the activity with no values.");
            }

            var sent = isClassifier ? rendered + ClassificationOutputContract.Trailer(values) : rendered;
            var inputHash = ConsultGenerationProvenance.Sha256Hex(sent);

            // #923 phase 2: the job starter resolved the agent pin from the
            // package's own stamped catalog version and threaded it here, so the
            // package runs the agent it was published against — not the engine's
            // current global pin. The _catalog fallback covers a payload
            // snapshotted before phase 2 (an in-flight orchestration mid-upgrade).
            var (agentName, agentVersion) = ResolveAgentPin(input, _catalog);

            var agentResponse = await _agent.SendPromptAsync(
                input.NodeId,
                sent,
                agentName,
                agentVersion,
                cancellationToken);
            var rawOutput = agentResponse.Text;

            var classification = isClassifier
                ? ClassificationOutputContract.Normalize(rawOutput, values, input.NodeId)
                : null;
            var outputHash = ConsultGenerationProvenance.Sha256Hex(classification ?? rawOutput);

            var concepts = string.Equals(input.OutputContract, OutputContracts.ConceptList, StringComparison.Ordinal)
                ? ConceptOutputContract.Deserialize(rawOutput, input.ConceptSource ?? input.NodeId)
                : null;

            _logger.LogInformation(
                "Prompt node completed. NodeId={NodeId}, ConceptCount={ConceptCount}, InputHash={InputHash}, OutputHash={OutputHash}, ElapsedMs={ElapsedMs}",
                input.NodeId,
                concepts?.Count,
                inputHash,
                outputHash,
                stopwatch.ElapsedMilliseconds);

            Console.Error.WriteLine(
                $"[PromptNode] NodeId={input.NodeId}; ConceptCount={concepts?.Count.ToString() ?? "-"}; InputHash={inputHash}; OutputHash={outputHash}; ElapsedMs={stopwatch.ElapsedMilliseconds}");

            return new NodeRunResult(rawOutput, concepts, inputHash, outputHash, ConsultGenerationProvenance.NodeHashVersion, classification, agentResponse.Tokens, startedAtUtc, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Prompt node failed. NodeId={NodeId}, PromptId={PromptId}, ExceptionType={ExceptionType}, Message={Message}, ElapsedMs={ElapsedMs}",
                input.NodeId,
                input.PromptId,
                ex.GetType().FullName,
                ex.Message,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    /// <summary>
    /// #923 phase 2: the job starter resolved the agent pin from the package's
    /// own stamped catalog version and threaded it here, so the package runs
    /// the agent it was published against — not the engine's current global
    /// pin. The catalogue fallback covers a payload snapshotted before phase 2
    /// (an in-flight orchestration mid-upgrade). v25 (B2 #1001): a node that
    /// carries a format REFUSES that fallback — its agent is the stamped
    /// catalogue's or nothing; resolving against whatever the engine runs today
    /// would silently run a different agent (design § 3.3).
    /// </summary>
    internal static (string AgentName, string AgentVersion) ResolveAgentPin(ConsultPromptNodeActivityInput input, OutputContractCatalog catalog)
    {
        if (input.AgentName is { Length: > 0 } pinnedName && input.AgentVersion is { Length: > 0 } pinnedVersion)
        {
            return (pinnedName, pinnedVersion);
        }

        if (input.Format is not null)
        {
            throw new InvalidOperationException(
                $"Node '{input.NodeId}' is a specVersion 25 node of format '{input.Format}' and carries no agent pin; "
                + "a 25 node runs the agent its package was published against, never the engine's current catalogue. "
                + "The job was started before the engine threaded formats — start it again.");
        }

        var entry = catalog.GetEntry(input.OutputContract ?? OutputContracts.Text);
        return (entry.AgentName, entry.AgentVersion);
    }
}
