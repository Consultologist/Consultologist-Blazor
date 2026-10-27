using Consultologist.PackageFormat;

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
        if (package.Manifest.SpecVersion >= 26)
        {
            return ResolveSourceBlocks(package, inputFans);
        }

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

    /// <summary>
    /// v26 (#1154, design § 3): the deliverables' blocks from their sources[],
    /// in result-set then source order. A loop source contributes one block
    /// per item, named by the LOOP — "resultId:loopId:itemId" — the source as
    /// the deliverable declares it (the body node that completes the block is
    /// the engine's concern); an if/case or a plain prose source one block,
    /// "resultId:nodeId". The items are the settled ones the starter hands in
    /// by the loop's collection (a snippet folder's items whole for the
    /// Current endpoint, which has no request to settle them by; an array
    /// input's none, as at 9).
    /// </summary>
    public static IReadOnlyList<WorkflowDeliverableBlock> ResolveSourceBlocks(
        WorkflowPackage package,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? inputFans = null)
    {
        var results = package.Results
            ?? throw new InvalidOperationException($"Package {package.Ref} resolved no result set (specVersion 26 requires one).");
        var nodesById = (package.Nodes ?? new List<WorkflowNodeSpec>()).ToDictionary(node => node.Id, StringComparer.Ordinal);
        var blocks = new List<WorkflowDeliverableBlock>();

        foreach (var result in results)
        {
            foreach (var sourceId in result.Sources ?? new List<string>())
            {
                var source = nodesById.GetValueOrDefault(sourceId)
                    ?? throw new InvalidOperationException($"Package {package.Ref} deliverable '{result.Id}' sources unknown node '{sourceId}'.");

                if (WorkflowFlowNodes.CollectionOf(source.For) is not { } collection)
                {
                    blocks.Add(new WorkflowDeliverableBlock($"{result.Id}:{sourceId}", source.Label, string.Empty));
                    continue;
                }

                IEnumerable<IReadOnlyDictionary<string, string>> items;
                if (inputFans?.GetValueOrDefault(collection) is { } settled)
                {
                    items = settled;
                }
                else if (WorkflowInputFans.IsInputFan(collection))
                {
                    continue; // before a job exists there is no request and so no items
                }
                else
                {
                    var folderId = WorkflowFanSources.CollectionIdOf(collection);
                    var folder = package.Data?.Collections.GetValueOrDefault(folderId)
                        ?? throw new InvalidOperationException($"Package {package.Ref} has no snippet folder '{folderId}'.");
                    items = folder.Items.Select(item => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(item.Fields, StringComparer.Ordinal) { ["id"] = item.Id });
                }

                foreach (var item in items)
                {
                    blocks.Add(new WorkflowDeliverableBlock(
                        $"{result.Id}:{sourceId}:{item["id"]}",
                        ItemName(item),
                        item.GetValueOrDefault("content", string.Empty)));
                }
            }
        }

        return blocks;
    }

    /// <summary>A settled item's display name: its 25 name, its 26 label, else its id.</summary>
    internal static string ItemName(IReadOnlyDictionary<string, string> item) =>
        item.GetValueOrDefault("name") ?? item.GetValueOrDefault("label") ?? item["id"];

    private static IEnumerable<WorkflowDeliverableBlock> ExpandAggregator(
        WorkflowPackage package,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, string>>>? inputFans,
        IReadOnlyDictionary<string, WorkflowNodeSpec> nodesById,
        WorkflowNodeSpec resultNode,
        string? resultId)
    {
        foreach (var sourceRef in resultNode.Aggregate!)
        {
            // node:x below 25, node.x at 25 (#1037).
            var sourceId = TemplateObjectReference.NodeIdOf(sourceRef) ?? sourceRef;
            var source = nodesById.GetValueOrDefault(sourceId)
                ?? throw new InvalidOperationException($"Package {package.Ref} result aggregator references unknown node '{sourceId}'.");

            if (source.ForEach != null && inputFans?.GetValueOrDefault(source.ForEach) is { } settled)
            {
                // v9 (#426): one block per caller element, ids the engine minted.
                // v25 (B3 #1002): a snippet folder fan rides the same map, keyed
                // by its literal forEach, with the items its input-only `when`s
                // settled at job start — so the blocks are the instances.
                foreach (var item in settled)
                {
                    yield return new WorkflowDeliverableBlock(
                        resultId is null ? $"{sourceId}:{item["id"]}" : $"{resultId}:{sourceId}:{item["id"]}",
                        item.GetValueOrDefault("name", item["id"]),
                        item.GetValueOrDefault("content", string.Empty));
                }
            }
            else if (WorkflowInputFans.IsInputFan(source.ForEach))
            {
                // Before a job exists there is no request and so no items.
            }
            else if (source.ForEach != null)
            {
                var collectionId = WorkflowFanSources.CollectionIdOf(source.ForEach);
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
        var collectionId = (resultNode.ForEach is null ? null : WorkflowFanSources.CollectionIdOf(resultNode.ForEach))
            ?? throw new InvalidOperationException($"Package {package.Ref} result node '{resultNode.Id}' declares no forEach.");

        return package.Data?.Collections.GetValueOrDefault(collectionId)
            ?? throw new InvalidOperationException($"Package {package.Ref} has no data collection '{collectionId}'.");
    }
}
