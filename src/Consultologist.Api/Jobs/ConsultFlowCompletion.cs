using Consultologist.Api.Models;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v26 (#1154, design § 1.3, § 14): a flow node's completion, pure over the
/// snapshot and the outputs so the orchestrator's use is replay-safe. A loop
/// completes as the 25 aggregator over one fan source did — once every item of
/// its returned node has an output, its text is
/// <see cref="ConsultAggregateRenderer.Render"/> over those items in item
/// order, with the aggregator's hash ladder (the #1065 identity the migration
/// relies on: <c>node.&lt;loop&gt;.content</c> is byte-for-byte the aggregator's);
/// a failed item fails the loop as it failed the aggregator. An if or case
/// node completes as its taken body's returned node did — its output IS that
/// node's (the alias); one that took no branch never completes here (the
/// engine's skipped set carries it).
/// </summary>
internal static class ConsultFlowCompletion
{
    /// <summary>What a flow node settled to: its output, or the sentence that fails it.</summary>
    internal sealed record Composed(NodeRunResult? Result, string? Failure);

    /// <summary>The loop's or the branch's output; a failure; null while not every item (or the returned node) has settled.</summary>
    public static Composed? TryCompose(
        ConsultNodeDescriptor flow,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        Func<ConsultNodeDescriptor, IReadOnlyList<IReadOnlyDictionary<string, string>>> fanItems,
        Func<ConsultNodeDescriptor, string, bool> itemFailed,
        // v27 (#1235, design § 1.4): the instance keys (flow:item) an inner if/case
        // skipped by condition — a loop returning that flow composes over the
        // items that returned, in item order; the skipped ones contribute nothing
        // (the count is kept on the record). Null (26) and every item returns.
        IReadOnlySet<string>? skipped = null)
    {
        if (flow.Ret is null || !nodesById.TryGetValue(flow.Ret, out var ret))
        {
            return null; // undecided, or no branch taken: the skipped set's
        }

        if (flow.Kind != ConsultNodeFlows.For)
        {
            return outputs.TryGetValue(ret.Id, out var branch) ? new Composed(branch, null) : null;
        }

        var items = fanItems(ret);
        var failed = items.Where(item => itemFailed(ret, item["id"])).Select(item => $"{ret.Id}:{item["id"]}").ToList();
        if (failed.Count > 0)
        {
            return new Composed(null, $"{flow.Label} could not assemble: {string.Join(", ", failed)} did not complete.");
        }

        var present = items.Where(item => skipped?.Contains(ConsultNodeScheduler.InstanceKey(ret.Id, item["id"])) != true).ToList();
        if (!present.All(item => outputs.ContainsKey(ConsultNodeScheduler.InstanceKey(ret.Id, item["id"]))))
        {
            return null;
        }

        var parts = new List<ConsultAggregateRenderer.Part>
        {
            new ConsultAggregateRenderer.ForEachPart(present
                .Select(item => (
                    item["id"],
                    WorkflowPackageBlocks.ItemName(item),
                    outputs[ConsultNodeScheduler.InstanceKey(ret.Id, item["id"])].RawOutput.Trim()))
                .ToList()),
        };
        var rendered = ConsultAggregateRenderer.Render(parts);
        var hash = ConsultGenerationProvenance.Sha256Hex(rendered);

        return new Composed(
            new NodeRunResult(
                rendered,
                null,
                ConsultGenerationProvenance.ComputeAggregateInputHash(present.Select(item => outputs[ConsultNodeScheduler.InstanceKey(ret.Id, item["id"])].OutputHash).ToList()),
                hash,
                ConsultGenerationProvenance.NodeHashVersionAt25,
                AgentOutputHash: hash),
            null);
    }
}

/// <summary>The words a flow row's <c>Flow</c> carries (design § 10).</summary>
internal static class ConsultNodeFlows
{
    public const string For = "for";
    public const string If = "if";
    public const string Case = "case";
}
