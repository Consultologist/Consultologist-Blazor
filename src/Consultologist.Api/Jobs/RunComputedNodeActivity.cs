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
    string? FormatHash = null,
    // v30 (#1343, design § 4.2): a let — its expression, its declared type, its
    // onInvalid (fail | flag), and the scope a template at its place reads (the
    // payload a prompt node there is given: inputs, the outputs it reads, item,
    // item.parent, pass, the loops and whiles it reads whole). Trailing; written
    // only when set, so a terms_subset payload serializes as before.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Let = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LetType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OnInvalid = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ConsultPromptNodeActivityInput? Scope = null);

/// <summary>
/// v25 (B4 #1003, design § 3.7): a computed node — no prompt, no model. The
/// operation runs over the outputs its with names and the result is the
/// node's output: <c>terms_subset</c> yields <c>{ pass, uncovered, untested }</c>
/// as canonical JSON (what <c>node.&lt;id&gt;.value</c> reads) and the same
/// check outcome the v12 inline check recorded, so a deliverable's
/// <c>check:</c> keeps its meaning. The input hash covers the operand texts,
/// the output hash the result; an unknown operation fails without retry.
/// </summary>
public sealed class RunComputedNodeActivity
{
    private readonly IWorkflowPackageStore _packageStore;
    private readonly ILogger<RunComputedNodeActivity> _logger;

    public RunComputedNodeActivity(IWorkflowPackageStore packageStore, ILogger<RunComputedNodeActivity> logger)
    {
        _packageStore = packageStore;
        _logger = logger;
    }

    [Function(ConsultGenerationActivityNames.RunComputedNode)]
    public async Task<NodeRunResult> RunAsync([ActivityTrigger] ConsultComputedNodeActivityInput input, CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        // v30 (#1343): a let evaluates over its package's scope — resolved as the decision activity resolves it.
        WorkflowPackage? package = null;
        if (input.Op == WorkflowLetAt30.Op)
        {
            if (input.Scope is null || !WorkflowPackageRef.TryParse(input.Scope.WorkflowPackage, out var packageRef))
            {
                throw new InvalidOperationException($"Let '{input.NodeId}' carries no usable workflow package ref.");
            }

            package = await _packageStore.ResolveAsync(packageRef!, cancellationToken);
        }

        var result = Compute(input, package);

        _logger.LogInformation(
            "Computed node completed. NodeId={NodeId}, Op={Op}, InputHash={InputHash}, OutputHash={OutputHash}, ElapsedMs={ElapsedMs}",
            input.NodeId,
            input.Op,
            result.InputHash,
            result.OutputHash,
            stopwatch.ElapsedMilliseconds);

        return result with { StartedAtUtc = startedAtUtc, DurationMs = stopwatch.ElapsedMilliseconds };
    }

    private static readonly JsonSerializerOptions ResultJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The pure half: the operation over the payload, as the record will hold it.</summary>
    internal static NodeRunResult Compute(ConsultComputedNodeActivityInput input, WorkflowPackage? package = null)
    {
        switch (input.Op)
        {
            case WorkflowLetAt30.Op:
                return ComputeLet(input, package ?? throw new InvalidOperationException($"Let '{input.NodeId}' was computed without its package."));

            case WorkflowCheckOps.TermsSubsetAt25:
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
                    ConsultGenerationProvenance.NodeHashVersionAt25,
                    Validation: new ConsultValidationOutcome(true, Array.Empty<string>()),
                    Check: outcome,
                    // B5 #1004 (design § 6): the operation's result IS the reply.
                    AgentOutputHash: ConsultGenerationProvenance.Sha256Hex(rendered));

            default:
                throw new InvalidOperationException(
                    $"Computed node '{input.NodeId}' declares operation '{input.Op}', which this engine does not implement.");
        }
    }

    /// <summary>
    /// v30 (#1343, design § 4.2): a let — the expression evaluated over the scope a
    /// template at its place reads, stored as its type says (a text bare, the rest
    /// canonical JSON) and checked against it. The input hash covers the expression
    /// and what it read (the outputs by id, the item, the pass); the output hash the
    /// value. A value not of its type fails the node under onInvalid fail (never
    /// retried — there is no model to repair) and is kept, flagged, under flag; an
    /// expression that cannot be evaluated fails it either way.
    /// </summary>
    private static NodeRunResult ComputeLet(ConsultComputedNodeActivityInput input, WorkflowPackage package)
    {
        var nodeId = WorkflowItemPath.NodeOf(input.NodeId);
        var subject = $"Let '{nodeId}'";

        if (input.Let is not { } expression || input.LetType is not { } type || input.Scope is not { } scopeInput)
        {
            throw new InvalidOperationException($"{subject} carries no expression, type or scope.");
        }

        var scope = ConsultGatedScope.Judge(scopeInput, package);
        var value = WorkflowLetAt30.Evaluate(subject, expression, scope, TemplatePipes.Engine);
        var schema = WorkflowLetAt30.ScalarSpecOf(type) is null
            ? ConsultGenerationJobStarter.FormatSpecFor(type, package.Formats, package.SourceFiles, package.Manifest)?.Schema
            : null;
        var (stored, errors) = WorkflowLetAt30.ValueOf(subject, type, value, schema);
        var onInvalid = input.OnInvalid ?? WorkflowOnInvalid.Fail;

        if (errors.Count > 0 && onInvalid != WorkflowOnInvalid.Flag)
        {
            // The sentence itself is the failure (an InvalidOperationException: not retried).
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var outputHash = ConsultGenerationProvenance.Sha256Hex(stored);

        return new NodeRunResult(
            stored,
            null,
            ConsultGenerationProvenance.Sha256Hex(LetInputOf(expression, scopeInput)),
            outputHash,
            ConsultGenerationProvenance.NodeHashVersionAt25,
            Validation: new ConsultValidationOutcome(errors.Count == 0, errors),
            // As a computed check's: the value IS the reply.
            AgentOutputHash: outputHash);
    }

    /// <summary>What a let's input hash covers: its expression, then each output it read by id, then the item and the pass it ran in.</summary>
    internal static string LetInputOf(string expression, ConsultPromptNodeActivityInput scope)
    {
        var text = new System.Text.StringBuilder(expression);

        foreach (var (id, read) in (scope.NodeTexts ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append('\n').Append(id).Append('=').Append(read);
        }

        foreach (var (key, field) in (scope.Item ?? new Dictionary<string, string>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append("\nitem.").Append(key).Append('=').Append(field);
        }

        if (scope.Pass is { } pass)
        {
            text.Append("\npass=").Append(pass.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return text.ToString();
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
