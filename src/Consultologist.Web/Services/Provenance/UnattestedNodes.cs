using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance;

/// <summary>
/// #933: the custom tier's marker, read the one way both pages read it — in
/// two kinds since B7 (#1006, provenance@v2026.10.1):
/// <list type="bullet">
/// <item><b>unattested</b> (#760, packages at 22–24): a custom-schema node's
/// shape is the package's own — not a catalog contract, not SNOMED-grounded —
/// and the record stamps its output <c>unattested</c> with the schema hash and
/// the agent. The descriptor's contract is literally <c>custom</c>, so such a
/// node is recognised before it completes and on records from before the
/// engine stamped every row (#980).</item>
/// <item><b>custom shape, engine-validated</b> (specVersion 25): a node whose
/// <c>producer</c> is <c>custom</c> runs on the catalogue's attested generic
/// agent and the engine validates its reply against the declared schema. The
/// record carries no unattested marker for it — the descriptor's producer,
/// format hash and agent say what ran — and a reader shows it as a
/// package-defined shape, never as unattested.</item>
/// </list>
/// The catalog doctrine holds for both: such output is shown for what it is,
/// never mistaken for a catalog-contract deliverable.
/// </summary>
public static class UnattestedNodes
{
    public const string CustomContract = "custom";

    /// <summary>The 25 producer word, as the record spells it.</summary>
    public const string CustomProducer = "custom";

    public const string UnattestedKind = "unattested";

    public const string CustomShapeKind = "custom-shape";

    public const string Sentence =
        "Unattested: a user-defined output shape — not an attested clinical contract, no terminology grounding.";

    public const string CustomShapeSentence =
        "Custom shape, engine-validated: a package-defined output shape the engine checked against its declared schema; the agent is attested, the shape is the author's — no terminology grounding.";

    public sealed record Entry(string NodeId, string Label, string? CustomSchemaHash, string? CustomAgent, string Kind = UnattestedKind, string? Format = null);

    public static bool IsCustom(ConsultGenerationNodeDescriptor? descriptor) =>
        string.Equals(descriptor?.OutputContract, CustomContract, StringComparison.Ordinal);

    /// <summary>B7: a specVersion 25 custom producer — a custom shape, engine-validated.</summary>
    public static bool IsCustomShape(ConsultGenerationNodeDescriptor? descriptor) =>
        string.Equals(descriptor?.Producer, CustomProducer, StringComparison.Ordinal);

    public static bool IsUnattested(ConsultGenerationNodeDescriptor? descriptor, ConsultGenerationNodeStatus? status) =>
        IsCustom(descriptor) || status?.Unattested == true;

    /// <summary>Either kind: the pill goes on the row.</summary>
    public static bool IsMarked(ConsultGenerationNodeDescriptor? descriptor, ConsultGenerationNodeStatus? status) =>
        IsUnattested(descriptor, status) || IsCustomShape(descriptor);

    public static string KindOf(ConsultGenerationNodeDescriptor? descriptor, ConsultGenerationNodeStatus? status) =>
        IsCustomShape(descriptor) && !IsUnattested(descriptor, status) ? CustomShapeKind : UnattestedKind;

    public static string SentenceFor(string kind) => kind == CustomShapeKind ? CustomShapeSentence : Sentence;

    /// <summary>The pill's word.</summary>
    public static string Pill(string kind) => kind == CustomShapeKind ? "custom shape" : "unattested";

    /// <summary>The entry a 25 custom-shape descriptor yields: the format, its file's hash and the generic agent, all from the descriptor.</summary>
    public static Entry CustomShapeEntry(ConsultGenerationNodeDescriptor descriptor) =>
        new(descriptor.Id, descriptor.Label, descriptor.FormatHash,
            descriptor.AgentName is { Length: > 0 } name ? $"{name}@{descriptor.AgentVersion}" : null,
            CustomShapeKind, descriptor.Format);

    /// <summary>The job's marked nodes by id — a custom descriptor or a stamped node-level row (unattested), a 25 custom producer (custom shape) — with the hash and agent the node-level row carries, or failing that the first item row that does (#980), or the descriptor's own at 25.</summary>
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
            else if (IsCustomShape(descriptor))
            {
                entries[descriptor.Id] = CustomShapeEntry(descriptor);
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

    /// <summary>"schema 0123456789ab · agent custom-abc@1" for an unattested entry; "format mine · hash 0123456789ab · served by custom-json@1, attested" for a custom shape; or what the record did not carry.</summary>
    public static string Detail(Entry entry)
    {
        var hash = entry.CustomSchemaHash is { Length: > 0 } h ? (h.Length > 12 ? h[..12] : h) : "not recorded";

        return entry.Kind == CustomShapeKind
            ? $"format {entry.Format ?? "not recorded"} · hash {hash} · served by {entry.CustomAgent ?? "not recorded"}, attested"
            : $"schema {hash} · agent {entry.CustomAgent ?? "not recorded"}";
    }

    public static string CountSentence(int count) =>
        count == 1 ? "1 unattested block" : $"{count} unattested blocks";

    public static string CountSentence(string kind, int count) => kind == CustomShapeKind
        ? (count == 1 ? "1 custom-shape block" : $"{count} custom-shape blocks")
        : CountSentence(count);

    /// <summary>The chip's text over a mixed set: each kind counted, unattested first.</summary>
    public static string Summary(IReadOnlyDictionary<string, Entry> entries)
    {
        var unattested = entries.Values.Count(e => e.Kind == UnattestedKind);
        var shapes = entries.Values.Count(e => e.Kind == CustomShapeKind);
        var parts = new List<string>();
        if (unattested > 0)
        {
            parts.Add(CountSentence(unattested));
        }

        if (shapes > 0)
        {
            parts.Add(CountSentence(CustomShapeKind, shapes));
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The sentence(s) a mixed set needs, each kind once.</summary>
    public static string SentencesOf(IReadOnlyDictionary<string, Entry> entries) =>
        string.Join(" ", entries.Values.Select(e => e.Kind).Distinct().OrderBy(k => k == CustomShapeKind ? 1 : 0).Select(SentenceFor));
}
