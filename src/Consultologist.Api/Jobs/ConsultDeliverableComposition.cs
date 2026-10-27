using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

namespace Consultologist.Api.Jobs;

/// <summary>
/// v26 (#1155, design § 3, § 3.1): the deliverable's compose step, pure over
/// the snapshot and the outputs so the orchestrator's use is replay-safe. At
/// 25 the aggregator node composed its sources' parts and readers read it; at
/// 26 the deliverable composes its own <c>sources[]</c> once every source has
/// settled — a loop's returned node's items as one <c>ForEachPart</c> (the
/// items, the texts and the hashes the 25 aggregator took, so the bytes and
/// the ladder are its), an if/case's returned node and a prose node each as a
/// <c>ScalarPart</c> — in declared order, through the same renderer. The
/// composed text is what <c>deliverable.&lt;id&gt;.content</c> reads and what
/// the deliverable's macros and signature are composed around at release.
/// </summary>
internal static class ConsultDeliverableComposition
{
    /// <summary>What a deliverable composed to: its parts, the render and the ladder — or the sentence that fails it.</summary>
    internal sealed record Composed(
        List<ConsultAggregateRenderer.Part>? Parts,
        string? Rendered,
        string? InputHash,
        string? ComposedHash,
        string? Failure);

    /// <summary>The composition; a failure; null while a source has not settled.</summary>
    public static Composed? TryCompose(
        ConsultDeliverables.Deliverable deliverable,
        IReadOnlyDictionary<string, ConsultNodeDescriptor> nodesById,
        IReadOnlyDictionary<string, NodeRunResult> outputs,
        Func<ConsultNodeDescriptor, IReadOnlyList<IReadOnlyDictionary<string, string>>> fanItems,
        Func<ConsultNodeDescriptor, string, bool> itemFailed)
    {
        var parts = new List<ConsultAggregateRenderer.Part>();
        var hashes = new List<string>();
        var failed = new List<string>();

        foreach (var sourceId in deliverable.Sources ?? Array.Empty<string>())
        {
            var source = nodesById.GetValueOrDefault(sourceId)
                ?? throw new InvalidOperationException($"Deliverable '{deliverable.ResultId}' sources node '{sourceId}', which the job does not carry.");

            if (source.Flow == ConsultNodeFlows.For)
            {
                var ret = source.Ret is { } retId && nodesById.TryGetValue(retId, out var returned)
                    ? returned
                    : throw new InvalidOperationException($"Deliverable '{deliverable.ResultId}' sources loop '{sourceId}', which returns no node the job carries.");
                var items = fanItems(ret);
                var failedItems = items.Where(item => itemFailed(ret, item["id"])).Select(item => $"{ret.Id}:{item["id"]}").ToList();
                if (failedItems.Count > 0)
                {
                    failed.AddRange(failedItems);
                    continue;
                }

                // The loop's own row settles the items (ConsultFlowCompletion); its
                // bytes are the part's bytes.
                if (!outputs.ContainsKey(sourceId))
                {
                    return null;
                }

                parts.Add(new ConsultAggregateRenderer.ForEachPart(items
                    .Select(item => (
                        item["id"],
                        WorkflowPackageBlocks.ItemName(item),
                        outputs[ConsultNodeScheduler.InstanceKey(ret.Id, item["id"])].RawOutput.Trim()))
                    .ToList()));
                hashes.AddRange(items.Select(item => outputs[ConsultNodeScheduler.InstanceKey(ret.Id, item["id"])].OutputHash));
                continue;
            }

            // An if/case's row is its returned node's result (the alias); a
            // prose node's its own.
            if (!outputs.TryGetValue(sourceId, out var scalar))
            {
                return null;
            }

            parts.Add(new ConsultAggregateRenderer.ScalarPart(scalar.RawOutput.Trim()));
            hashes.Add(scalar.OutputHash);
        }

        if (failed.Count > 0)
        {
            return new Composed(null, null, null, null,
                $"{deliverable.Label ?? deliverable.ResultId} could not assemble: {string.Join(", ", failed)} did not complete.");
        }

        var rendered = ConsultAggregateRenderer.Render(parts);
        return new Composed(
            parts,
            rendered,
            ConsultGenerationProvenance.ComputeAggregateInputHash(hashes),
            ConsultGenerationProvenance.Sha256Hex(rendered),
            null);
    }
}
