using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (B4 #1003, design §§ 3.4, 3.5, 3.7): one dispatch on the producer, in
/// two pure halves around the one model call. <see cref="Plan"/> decides what
/// is sent and to whom — attested: the prompt (an enum format trailed with its
/// answer set); custom: the prompt, the catalogue's trailer and the format's
/// schema, to the generic agent; rendered and computed never reach here.
/// <see cref="Settle"/> turns the reply into the record: fences stripped, a
/// structured reply parsed and validated against the format, an enum reply
/// normalised, and the node's <c>onInvalid</c> applied — <c>flag</c> keeps
/// the reply with its validation row, <c>fail</c> (and the reserved
/// <c>retry</c>) fails the node without a Durable retry. A 22–24 custom-schema
/// node takes the same custom path through the engine's pinned catalogue.
/// </summary>
internal static class ConsultNodeDispatch
{
    public const string ConceptListFormat = "concept-list";

    public enum ReplyKind
    {
        Text,
        Enum,
        Structured,
    }

    /// <summary>What the activity sends, to whom, and how the reply is read.</summary>
    public sealed record DispatchPlan(
        string Sent,
        string AgentName,
        string AgentVersion,
        ReplyKind Kind,
        JsonNode? Schema,
        IReadOnlyList<string> Values,
        bool ConceptList,
        string ConceptSource,
        string OnInvalid,
        string Format);

    public static bool Applies(ConsultPromptNodeActivityInput input) =>
        input.Format is not null || string.Equals(input.OutputContract, OutputContracts.Custom, StringComparison.Ordinal);

    public static DispatchPlan Plan(
        ConsultPromptNodeActivityInput input,
        WorkflowPackage package,
        string rendered,
        OutputContractCatalog contracts,
        WorkflowFormatCatalog engineFormats)
    {
        var conceptSource = input.ConceptSource ?? input.NodeId;

        if (input.Format is null)
        {
            return PlanCustomBelow25(input, package, rendered, engineFormats, conceptSource);
        }

        var spec = ConsultGenerationJobStarter.FormatSpecFor(input.Format, package.Formats, package.SourceFiles, package.Manifest)
            ?? throw new InvalidOperationException(
                $"Prompt node '{input.NodeId}' declares format '{input.Format}', which package {package.Ref} does not resolve.");
        var onInvalid = input.OnInvalid ?? WorkflowOnInvalid.Fail;

        switch (input.Producer)
        {
            case WorkflowFormatProducers.Computed:
            case WorkflowFormatProducers.Rendered:
                throw new InvalidOperationException(
                    $"Prompt node '{input.NodeId}' has producer '{input.Producer}', which runs no model and must not reach the prompt activity.");

            case WorkflowFormatProducers.Custom:
            {
                if (spec.Schema is null)
                {
                    throw new InvalidOperationException($"Prompt node '{input.NodeId}' format '{input.Format}' is custom but declares no schema.");
                }

                // The bytes sent are the bytes stamped: the loaded format's
                // canonical hash must be the one the job start recorded.
                var hash = WorkflowFormatSpecJson.Sha256Hex(WorkflowFormatSpecJson.CanonicalJson(spec));

                if (input.FormatHash is not null && !string.Equals(hash, input.FormatHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Prompt node '{input.NodeId}' format '{input.Format}' loaded with hash {hash}, but the job start stamped {input.FormatHash}; the package's format changed under the job.");
                }

                var trailer = (package.Formats ?? engineFormats).CustomProducer.Trailer;
                var (agentName, agentVersion) = RunPromptNodeActivity.ResolveAgentPin(input, contracts);

                return new DispatchPlan(
                    WorkflowCustomPrompt.Compose(rendered, trailer, spec.Schema.ToJsonString()),
                    agentName, agentVersion, ReplyKind.Structured, spec.Schema, Array.Empty<string>(),
                    ConceptList: false, conceptSource, onInvalid, input.Format);
            }

            default:
            {
                var (agentName, agentVersion) = RunPromptNodeActivity.ResolveAgentPin(input, contracts);

                if (spec.Type == WorkflowInputTypes.Enum)
                {
                    var values = input.Values ?? spec.Values ?? new List<string>();

                    if (values.Count == 0)
                    {
                        throw new InvalidOperationException($"Classifier '{input.NodeId}' reached the activity with no values.");
                    }

                    return new DispatchPlan(
                        rendered + ClassificationOutputContract.Trailer(values),
                        agentName, agentVersion, ReplyKind.Enum, null, values,
                        ConceptList: false, conceptSource, onInvalid, input.Format);
                }

                if (spec.Type is WorkflowInputTypes.Object or WorkflowInputTypes.Array)
                {
                    return new DispatchPlan(
                        rendered, agentName, agentVersion, ReplyKind.Structured, spec.Schema, Array.Empty<string>(),
                        ConceptList: string.Equals(input.Format, ConceptListFormat, StringComparison.Ordinal),
                        conceptSource, onInvalid, input.Format);
                }

                return new DispatchPlan(
                    rendered, agentName, agentVersion, ReplyKind.Text, null, Array.Empty<string>(),
                    ConceptList: false, conceptSource, onInvalid, input.Format);
            }
        }
    }

    /// <summary>
    /// A 22–24 custom-schema node (#760 retired): the package's schema goes
    /// into the prompt with the engine's pinned catalogue's trailer, the reply
    /// is validated by the engine, and the policy is fail — the closest
    /// honest reading of what a strict agent promised.
    /// </summary>
    private static DispatchPlan PlanCustomBelow25(
        ConsultPromptNodeActivityInput input,
        WorkflowPackage package,
        string rendered,
        WorkflowFormatCatalog engineFormats,
        string conceptSource)
    {
        var nodeId = input.NodeId.Split(':')[0];
        var node = package.Nodes?.FirstOrDefault(n => string.Equals(n.Id, nodeId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Package {package.Ref} has no node '{nodeId}'.");
        var schemaId = node.Output?.Schema
            ?? throw new InvalidOperationException($"Custom node '{nodeId}' declares no output schema.");
        var file = package.Manifest.CustomSchemas?.GetValueOrDefault(schemaId)
            ?? throw new InvalidOperationException($"Package {package.Ref} declares no custom schema '{schemaId}'.");
        var body = package.SourceFiles?.GetValueOrDefault(file)
            ?? throw new InvalidOperationException($"Custom schema '{schemaId}' file '{file}' is missing from package {package.Ref}.");
        var schema = JsonNode.Parse(body)
            ?? throw new InvalidOperationException($"Custom schema '{schemaId}' is empty.");

        var agentName = string.IsNullOrWhiteSpace(input.AgentName) ? engineFormats.CustomProducer.AgentName : input.AgentName;
        var agentVersion = string.IsNullOrWhiteSpace(input.AgentVersion)
            ? engineFormats.CustomProducer.AgentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : input.AgentVersion;

        return new DispatchPlan(
            WorkflowCustomPrompt.Compose(rendered, engineFormats.CustomProducer.Trailer, schema.ToJsonString()),
            agentName, agentVersion, ReplyKind.Structured, schema, Array.Empty<string>(),
            ConceptList: false, conceptSource, WorkflowOnInvalid.Fail, OutputContracts.Custom);
    }

    /// <summary>The reply as the record holds it, with the node's policy applied.</summary>
    public static NodeRunResult Settle(DispatchPlan plan, string nodeId, string reply, string inputHash, ConsultTokenUsage? tokens)
    {
        switch (plan.Kind)
        {
            case ReplyKind.Text:
                return new NodeRunResult(reply, null, inputHash, ConsultGenerationProvenance.Sha256Hex(reply), ConsultGenerationProvenance.NodeHashVersion, null, tokens);

            case ReplyKind.Enum:
            {
                string? classification = null;
                var errors = new List<string>();

                try
                {
                    classification = ClassificationOutputContract.Normalize(reply, plan.Values, nodeId);
                }
                catch (ClassificationOutputContractException ex)
                {
                    errors.Add(ex.Message);
                }

                var validation = new ConsultValidationOutcome(errors.Count == 0, errors);
                Apply(validation, plan, nodeId);

                return new NodeRunResult(
                    reply, null, inputHash, ConsultGenerationProvenance.Sha256Hex(classification ?? reply),
                    ConsultGenerationProvenance.NodeHashVersion, classification, tokens, Validation: validation);
            }

            default:
            {
                var stripped = WorkflowCodeFences.Strip(reply);
                var errors = new List<string>();
                JsonNode? parsed = null;

                try
                {
                    parsed = JsonNode.Parse(stripped);
                }
                catch (JsonException ex)
                {
                    errors.Add($"the reply is not JSON: {ex.Message}");
                }

                if (errors.Count == 0 && plan.Schema is not null)
                {
                    errors.AddRange(JsonSchemaSubsetValidator.Validate(parsed, plan.Schema));
                }

                var validation = new ConsultValidationOutcome(errors.Count == 0, errors);
                Apply(validation, plan, nodeId);

                var concepts = plan.ConceptList && validation.Valid
                    ? ConceptOutputContract.Deserialize(stripped, plan.ConceptSource)
                    : null;

                return new NodeRunResult(
                    stripped, concepts, inputHash, ConsultGenerationProvenance.Sha256Hex(stripped),
                    ConsultGenerationProvenance.NodeHashVersion, null, tokens, Validation: validation);
            }
        }
    }

    private static void Apply(ConsultValidationOutcome validation, DispatchPlan plan, string nodeId)
    {
        if (validation.Valid || string.Equals(plan.OnInvalid, WorkflowOnInvalid.Flag, StringComparison.Ordinal))
        {
            return;
        }

        throw new ConsultNodeInvalidReplyException(nodeId, plan.Format, plan.OnInvalid, validation.Errors);
    }
}

/// <summary>
/// v25 (B4 #1003, design § 3.5): an invalid reply under <c>onInvalid: fail</c>
/// (or the reserved <c>retry</c>). An InvalidOperationException so the
/// Durable retry policy does not re-send: the policy, not chance, decides.
/// The message carries the validation sentences only — paths and kinds,
/// never the reply's text (#245).
/// </summary>
public sealed class ConsultNodeInvalidReplyException : InvalidOperationException
{
    public ConsultNodeInvalidReplyException(string nodeId, string format, string policy, IReadOnlyList<string> errors)
        : base($"Node '{nodeId}' reply does not conform to format '{format}' (onInvalid: {policy}): {string.Join(" ", errors)}")
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}
