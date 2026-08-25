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
    Dictionary<string, string>? VariableTypes = null);

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
    int? HashVersion = null);

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

    public RunPromptNodeActivity(
        IWorkflowPackageStore packageStore,
        AgentSectionGenerator agent,
        OutputContractCatalog catalog,
        ILogger<RunPromptNodeActivity> logger)
    {
        _packageStore = packageStore;
        _agent = agent;
        _catalog = catalog;
        _logger = logger;
    }

    [Function(ConsultGenerationActivityNames.RunPromptNode)]
    public async Task<NodeRunResult> RunAsync(
        [ActivityTrigger] ConsultPromptNodeActivityInput input,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

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
            var rendered = PromptTemplateRenderer.Render(
                prompt,
                input.Variables,
                input.VariableTypes,
                WorkflowVariableDeclarations.For(package.Manifest));
            var inputHash = ConsultGenerationProvenance.Sha256Hex(rendered);

            var entry = _catalog.GetEntry(input.OutputContract ?? OutputContracts.Text);
            var rawOutput = await _agent.SendPromptAsync(
                input.NodeId,
                rendered,
                entry.AgentName,
                entry.AgentVersion,
                cancellationToken);
            var outputHash = ConsultGenerationProvenance.Sha256Hex(rawOutput);

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

            return new NodeRunResult(rawOutput, concepts, inputHash, outputHash, ConsultGenerationProvenance.NodeHashVersion);
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
}
