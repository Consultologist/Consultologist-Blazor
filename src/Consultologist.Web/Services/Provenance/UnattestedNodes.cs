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
/// and on every fan item (the engine marks only the node-level row, #980).
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

    /// <summary>The job's unattested nodes by id — a custom descriptor, or a node-level row stamped unattested — with the hash and agent the row carries.</summary>
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
                var status = response.NodeOutputs?.GetValueOrDefault(descriptor.Id);
                entries[descriptor.Id] = new Entry(descriptor.Id, descriptor.Label, status?.CustomSchemaHash, status?.CustomAgent);
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

    /// <summary>"schema 0123456789ab · agent custom-abc@1", or what the record did not carry.</summary>
    public static string Detail(Entry entry) =>
        $"schema {(entry.CustomSchemaHash is { Length: > 0 } hash ? (hash.Length > 12 ? hash[..12] : hash) : "not recorded")} · agent {entry.CustomAgent ?? "not recorded"}";

    public static string CountSentence(int count) =>
        count == 1 ? "1 unattested block" : $"{count} unattested blocks";
}
