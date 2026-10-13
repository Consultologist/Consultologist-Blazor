using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v25 (B4 #1003, design § 3.7): the computed node's payload — the operation,
/// the node's resolved with (operand parameter → node id, text parameters as
/// literals), the operands' outputs keyed by node id (item-aligned, as the
/// orchestrator threads every read), and the format identity for the record.
/// </summary>
public sealed record ConsultComputedNodeActivityInput(
    string NodeId,
    string Op,
    IReadOnlyDictionary<string, string> With,
    IReadOnlyDictionary<string, string> NodeTexts,
    string? Format = null,
    string? FormatHash = null);

/// <summary>
/// v25 (B4 #1003, design § 3.7): a computed node — no prompt, no model. The
/// operation runs over the outputs its with names and the result is the
/// node's output: <c>terms-subset</c> yields <c>{ pass, uncovered, untested }</c>
/// as canonical JSON (what <c>node.&lt;id&gt;.value</c> reads) and the same
/// check outcome the v12 inline check recorded, so a deliverable's
/// <c>check:</c> keeps its meaning. The input hash covers the operand texts,
/// the output hash the result; an unknown operation fails without retry.
/// </summary>
public sealed class RunComputedNodeActivity
{
    private readonly ILogger<RunComputedNodeActivity> _logger;

    public RunComputedNodeActivity(ILogger<RunComputedNodeActivity> logger)
    {
        _logger = logger;
    }

    [Function(ConsultGenerationActivityNames.RunComputedNode)]
    public Task<NodeRunResult> RunAsync([ActivityTrigger] ConsultComputedNodeActivityInput input)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var result = Compute(input);

        _logger.LogInformation(
            "Computed node completed. NodeId={NodeId}, Op={Op}, InputHash={InputHash}, OutputHash={OutputHash}, ElapsedMs={ElapsedMs}",
            input.NodeId,
            input.Op,
            result.InputHash,
            result.OutputHash,
            stopwatch.ElapsedMilliseconds);

        return Task.FromResult(result with { StartedAtUtc = startedAtUtc, DurationMs = stopwatch.ElapsedMilliseconds });
    }

    public const string TermsSubset = "terms-subset";

    private static readonly JsonSerializerOptions ResultJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The pure half: the operation over the payload, as the record will hold it.</summary>
    internal static NodeRunResult Compute(ConsultComputedNodeActivityInput input)
    {
        switch (input.Op)
        {
            case TermsSubset:
                var of = Operand(input, "of");
                var inDocument = Operand(input, "in");
                var outcome = ConsultCheckExecutor.TermsSubset(
                    ConceptOutputContract.Deserialize(of.Text, of.NodeId),
                    ConceptOutputContract.Deserialize(inDocument.Text, inDocument.NodeId));
                var rendered = JsonSerializer.Serialize(
                    new TermsSubsetResult(outcome.Passed, outcome.Uncovered ?? Array.Empty<string>(), outcome.Untested ?? Array.Empty<string>()),
                    ResultJson);

                return new NodeRunResult(
                    rendered,
                    null,
                    ConsultGenerationProvenance.Sha256Hex($"{of.Text}\n{inDocument.Text}"),
                    ConsultGenerationProvenance.Sha256Hex(rendered),
                    ConsultGenerationProvenance.NodeHashVersion,
                    Validation: new ConsultValidationOutcome(true, Array.Empty<string>()),
                    Check: outcome);

            default:
                throw new InvalidOperationException(
                    $"Computed node '{input.NodeId}' declares operation '{input.Op}', which this engine does not implement.");
        }
    }

    private static (string NodeId, string Text) Operand(ConsultComputedNodeActivityInput input, string parameter)
    {
        if (!input.With.TryGetValue(parameter, out var nodeId) || string.IsNullOrWhiteSpace(nodeId))
        {
            throw new InvalidOperationException($"Computed node '{input.NodeId}' names no node for parameter '{parameter}'.");
        }

        if (!input.NodeTexts.TryGetValue(nodeId, out var text))
        {
            throw new InvalidOperationException($"Computed node '{input.NodeId}' reads node '{nodeId}' for parameter '{parameter}', whose output the payload does not carry.");
        }

        return (nodeId, text);
    }

    private sealed record TermsSubsetResult(bool Pass, IReadOnlyList<string> Uncovered, IReadOnlyList<string> Untested);
}
