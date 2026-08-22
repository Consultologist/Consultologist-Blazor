namespace Consultologist.Api.Workflow;

/// <summary>One deliverable block: in v5 a section (standards item), in v6 a result-aggregator expansion entry.</summary>
public sealed record WorkflowDeliverableBlock(string Id, string Name, string Content);

/// <summary>
/// The single block source for a resolved package: v5 = the result node's
/// forEach collection; v6 = the result aggregator's expansion. Shared by the
/// WorkflowPackages/Current endpoint and consult job start.
/// </summary>
public static class WorkflowPackageBlocks
{
    /// <param name="inputFans">
    /// v9 (#426): the items of each input fan, keyed by the literal forEach
    /// string ("input:prior_notes"), as the job starter builds them from the
    /// request. Null where no request exists — the WorkflowPackages/Current
    /// endpoint — and an input fan then contributes no blocks: the run rail
    /// fills from the job's roster once one starts.
    /// </param>
    public static IReadOnlyList<WorkflowDeliverableBlock> Resolve(
        WorkflowPackage package,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? inputFans = null)
    {
        if (package.Manifest.SpecVersion >= 7)
        {
            return ResolveResultSetBlocks(package, inputFans);
        }

        if (package.Manifest.SpecVersion == 6)
        {
            return ResolveBlocks(package, inputFans);
        }

        return ResolveCollection(package).Items
            .Select(item => new WorkflowDeliverableBlock(
                item.Id,
                item.Fields.GetValueOrDefault("name", item.Id),
                item.Fields.GetValueOrDefault("content", string.Empty)))
            .ToList();
    }

    /// <summary>
    /// v6: the deliverable's blocks — the result aggregator's expansion in source
    /// order (package-format-v6-design.md § 4). forEach sources contribute one
    /// block per item (composite "nodeId:itemId" ids, collection index order);
    /// scalar sources one block under the node id.
    /// </summary>
    public static IReadOnlyList<WorkflowDeliverableBlock> ResolveBlocks(
        WorkflowPackage package,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? inputFans = null)
    {
        var nodes = package.Nodes ?? new List<WorkflowNodeSpec>();
        var nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var resultNode = nodesById.GetValueOrDefault(package.ResultNodeId ?? string.Empty)
            ?? throw new InvalidOperationException($"Package {package.Ref} has no result node '{package.ResultNodeId}'.");

        if (resultNode.Aggregate is null)
        {
            throw new InvalidOperationException($"Package {package.Ref} result node '{resultNode.Id}' is not an aggregator (specVersion 6 requires one).");
        }

        return ExpandAggregator(package, inputFans, nodesById, resultNode, resultId: null).ToList();
    }

    /// <summary>
    /// v7: the union of each deliverable's expansion in result-set order, block
    /// ids carrying the deliverable dimension — "resultId:nodeId:itemId"
    /// ("resultId:nodeId" for scalar sources) — so two deliverables sharing a
    /// source never collide (package-format-v7.md § 4). v5/v6 ids are unchanged.
    /// </summary>
    public static IReadOnlyList<WorkflowDeliverableBlock> ResolveResultSetBlocks(
        WorkflowPackage package,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? inputFans = null)
    {
        var results = package.Results
            ?? throw new InvalidOperationException($"Package {package.Ref} resolved no result set (specVersion 7 requires one).");

        var nodes = package.Nodes ?? new List<WorkflowNodeSpec>();
        var nodesById = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var blocks = new List<WorkflowDeliverableBlock>();

        foreach (var result in results)
        {
            var resultNode = nodesById.GetValueOrDefault(result.NodeId)
                ?? throw new InvalidOperationException($"Package {package.Ref} has no result node '{result.NodeId}'.");

            if (resultNode.Aggregate is null)
            {
                throw new InvalidOperationException($"Package {package.Ref} result node '{resultNode.Id}' is not an aggregator (specVersion 7 requires one).");
            }

            blocks.AddRange(ExpandAggregator(package, inputFans, nodesById, resultNode, result.Id));
        }

        return blocks;
    }

    private static IEnumerable<WorkflowDeliverableBlock> ExpandAggregator(
        WorkflowPackage package,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? inputFans,
        IReadOnlyDictionary<string, WorkflowNodeSpec> nodesById,
        WorkflowNodeSpec resultNode,
        string? resultId)
    {
        foreach (var sourceRef in resultNode.Aggregate!)
        {
            var sourceId = sourceRef.StartsWith(WorkflowNodeBindingSources.NodePrefix, StringComparison.Ordinal)
                ? sourceRef[WorkflowNodeBindingSources.NodePrefix.Length..]
                : sourceRef;
            var source = nodesById.GetValueOrDefault(sourceId)
                ?? throw new InvalidOperationException($"Package {package.Ref} result aggregator references unknown node '{sourceId}'.");

            if (WorkflowInputFans.IsInputFan(source.ForEach))
            {
                // v9 (#426): one block per caller element, ids the engine minted.
                // Before a job exists there is no request and so no items.
                foreach (var item in inputFans?.GetValueOrDefault(source.ForEach!) ?? Array.Empty<IReadOnlyDictionary<string, string>>())
                {
                    yield return new WorkflowDeliverableBlock(
                        resultId is null ? $"{sourceId}:{item["id"]}" : $"{resultId}:{sourceId}:{item["id"]}",
                        item.GetValueOrDefault("name", item["id"]),
                        string.Empty);
                }
            }
            else if (source.ForEach != null)
            {
                var collectionId = source.ForEach[WorkflowNodeBindingSources.DataPrefix.Length..];
                var collection = package.Data?.Collections.GetValueOrDefault(collectionId)
                    ?? throw new InvalidOperationException($"Package {package.Ref} has no data collection '{collectionId}'.");

                foreach (var item in collection.Items)
                {
                    yield return new WorkflowDeliverableBlock(
                        resultId is null ? $"{sourceId}:{item.Id}" : $"{resultId}:{sourceId}:{item.Id}",
                        item.Fields.GetValueOrDefault("name", item.Id),
                        item.Fields.GetValueOrDefault("content", string.Empty));
                }
            }
            else
            {
                yield return new WorkflowDeliverableBlock(
                    resultId is null ? sourceId : $"{resultId}:{sourceId}",
                    source.Label,
                    string.Empty);
            }
        }
    }

    /// <summary>The result node's forEach collection — the items a v5 job fans over.</summary>
    public static WorkflowDataCollection ResolveCollection(WorkflowPackage package)
    {
        var resultNode = package.Nodes?.FirstOrDefault(node => node.Id == package.ResultNodeId)
            ?? throw new InvalidOperationException($"Package {package.Ref} has no result node '{package.ResultNodeId}'.");
        var collectionId = resultNode.ForEach?[WorkflowNodeBindingSources.DataPrefix.Length..]
            ?? throw new InvalidOperationException($"Package {package.Ref} result node '{resultNode.Id}' declares no forEach.");

        return package.Data?.Collections.GetValueOrDefault(collectionId)
            ?? throw new InvalidOperationException($"Package {package.Ref} has no data collection '{collectionId}'.");
    }
}
