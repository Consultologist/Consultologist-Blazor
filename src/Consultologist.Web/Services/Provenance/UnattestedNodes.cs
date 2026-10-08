using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance;

/// <summary>
/// #933: the custom tier's marker (#760), read the one way both pages read it.
/// A custom-schema node runs on an engine-provisioned, content-addressed agent
/// — not a catalog contract, not git-attested, not SNOMED-grounded — and the
/// record stamps its output <c>unattested</c> with the schema hash and the
/// agent. The catalog doctrine says such output must be shown as such, never
/// mistaken for a catalog-contract deliverable. The descriptor's contract is
/// literally <c>custom</c>, so a custom node is recognised before it completes
/// and on records from before the engine stamped every row (#980 stamps the
/// fan's items and its summary row; before it, a fanned custom node was
/// stamped on no row at all).
/// </summary>
public static class UnattestedNodes
{
    public const string CustomContract = "custom";

    public const string Sentence =
        "Unattested: a user-defined output shape — not an attested clinical contract, no terminology grounding.";

    public sealed record Entry(string NodeId, string Label, string? CustomSchemaHash, string? CustomAgent);

    public static bool IsCustom(ConsultGenerationNodeDescriptor? descriptor) =>
        string.Equals(descriptor?.OutputContract, CustomContract, StringComparison.Ordinal);

    public static bool IsUnattested(ConsultGenerationNodeDescriptor? descriptor, ConsultGenerationNodeStatus? status) =>
        IsCustom(descriptor) || status?.Unattested == true;

    /// <summary>The job's unattested nodes by id — a custom descriptor, or a node-level row stamped unattested — with the hash and agent the node-level row carries, or failing that the first item row that does (#980).</summary>
    public static IReadOnlyDictionary<string, Entry> Of(ConsultGenerationJobResponse? response)
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (response is null)
        {
            return entries;
        }

        foreach (var descriptor in response.Nodes ?? Array.Empty<ConsultGenerationNodeDescriptor>())
        {
            if (IsCustom(descriptor))
            {
                var (hash, agent) = StampOf(response, descriptor.Id);
                entries[descriptor.Id] = new Entry(descriptor.Id, descriptor.Label, hash, agent);
            }
        }

        foreach (var (key, status) in response.NodeOutputs ?? new Dictionary<string, ConsultGenerationNodeStatus>())
        {
            if (status.Unattested == true && !key.Contains(':', StringComparison.Ordinal) && !entries.ContainsKey(key))
            {
                entries[key] = new Entry(key, status.Label, status.CustomSchemaHash, status.CustomAgent);
            }
        }

        return entries;
    }

    /// <summary>The hash and agent a node's rows carry: the node-level row first, else the first stamped item row (`nodeId:*`).</summary>
    private static (string? Hash, string? Agent) StampOf(ConsultGenerationJobResponse response, string nodeId)
    {
        var outputs = response.NodeOutputs;
        if (outputs is null)
        {
            return (null, null);
        }

        var nodeLevel = outputs.GetValueOrDefault(nodeId);
        if (nodeLevel?.CustomSchemaHash is { Length: > 0 } || nodeLevel?.CustomAgent is { Length: > 0 })
        {
            return (nodeLevel.CustomSchemaHash, nodeLevel.CustomAgent);
        }

        var item = outputs
            .Where(pair => pair.Key.StartsWith(nodeId + ":", StringComparison.Ordinal))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .FirstOrDefault(status => status.CustomSchemaHash is { Length: > 0 } || status.CustomAgent is { Length: > 0 });

        return (item?.CustomSchemaHash ?? nodeLevel?.CustomSchemaHash, item?.CustomAgent ?? nodeLevel?.CustomAgent);
    }

    /// <summary>"schema 0123456789ab · agent custom-abc@1", or what the record did not carry.</summary>
    public static string Detail(Entry entry) =>
        $"schema {(entry.CustomSchemaHash is { Length: > 0 } hash ? (hash.Length > 12 ? hash[..12] : hash) : "not recorded")} · agent {entry.CustomAgent ?? "not recorded"}";

    public static string CountSentence(int count) =>
        count == 1 ? "1 unattested block" : $"{count} unattested blocks";
}
