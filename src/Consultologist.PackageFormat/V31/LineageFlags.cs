using System.Globalization;

namespace Consultologist.PackageFormat.V31;

/// <summary>#1501 (specVersion 31, area 04): the three flags a node can carry, which spread along what its value is made from (part 03 § 4.4).</summary>
public enum LineageFlag
{
    /// <summary>The node's content comes from a model, or from a node that does.</summary>
    ModelDerived,

    /// <summary>The node's value can differ between runs with the same inputs (it reads <c>run.*</c>, or a run-dependent node).</summary>
    RunDependent,

    /// <summary>The node's text carries profile text, or reads a node that does.</summary>
    ProfileCarrying,
}

/// <summary>#1501 (specVersion 31, area 04): one read, <see cref="Path"/> as written (<c>run.date</c>, <c>node.draft.content</c>); <see cref="Key"/> the node it reads, null when it reads no node.</summary>
public sealed record LineageRead(string Path, string? Key);

/// <summary>
/// #1501 (specVersion 31, area 04): one node of the graph. <paramref name="ContentReads"/> are what its value is
/// made from (§ 4.4 "How far it spreads"): never a condition's reads, never a read of an earlier pass.
/// </summary>
public sealed record LineageNode(string Key, bool StartsModelDerived, bool StartsProfileCarrying, IReadOnlyList<LineageRead> ContentReads);

/// <summary>#1501 (specVersion 31, area 04): a flag's value and why: the first content read, in written order, that makes it true; null when the node starts it itself, or when it is false.</summary>
public sealed record LineageValue(bool Value, string? Because);

/// <summary>
/// #1501 (specVersion 31, area 04, D28): the three contagious flags (§§ 4.4 to 4.6) worked out over a graph of reads
/// the caller supplies (the compiled package's edges are area 05's, what templates read is area 07's). Never throws
/// on its input; unknown keys are ignored.
/// </summary>
public static class LineageFlags
{
    private static readonly LineageFlag[] All = [LineageFlag.ModelDerived, LineageFlag.RunDependent, LineageFlag.ProfileCarrying];

    /// <summary>
    /// A node is run-dependent when a content read's path has the root <c>run</c>, or reads a run-dependent node;
    /// model-derived when it starts so, or reads a model-derived node; profile-carrying likewise. Iterates to a
    /// fixed point, so a cycle ends.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<LineageFlag, LineageValue>> Derive(IReadOnlyList<LineageNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var byKey = new Dictionary<string, LineageNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
            byKey[node.Key] = node;

        var result = new Dictionary<string, IReadOnlyDictionary<LineageFlag, LineageValue>>(StringComparer.Ordinal);
        var flags = new Dictionary<string, Dictionary<LineageFlag, bool>>(StringComparer.Ordinal);
        foreach (var node in byKey.Values)
            flags[node.Key] = All.ToDictionary(f => f, f => Starts(node, f));

        bool changed;
        do
        {
            changed = false;
            foreach (var node in byKey.Values)
                foreach (var flag in All)
                {
                    if (flags[node.Key][flag] || FirstCause(node.ContentReads, flag, key => flags.TryGetValue(key, out var f) && f[flag]) is null)
                        continue;
                    flags[node.Key][flag] = true;
                    changed = true;
                }
        }
        while (changed);

        foreach (var node in byKey.Values)
            result[node.Key] = All.ToDictionary(f => f, f => flags[node.Key][f] && !Starts(node, f)
                ? new LineageValue(true, FirstCause(node.ContentReads, f, key => flags.TryGetValue(key, out var v) && v[f]))
                : new LineageValue(flags[node.Key][f], null));
        return result;
    }

    /// <summary>A decision's flags: what its condition's <paramref name="reads"/> make it, given the nodes' <paramref name="derived"/> flags.</summary>
    public static IReadOnlyDictionary<LineageFlag, LineageValue> OfReads(
        IReadOnlyList<LineageRead> reads, IReadOnlyDictionary<string, IReadOnlyDictionary<LineageFlag, LineageValue>> derived)
    {
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(derived);
        return All.ToDictionary(f => f, f =>
        {
            var cause = FirstCause(reads, f, key => derived.TryGetValue(key, out var v) && v.TryGetValue(f, out var value) && value.Value);
            return new LineageValue(cause is not null, cause);
        });
    }

    /// <summary>S03-036 when the node's <paramref name="declared"/> value of <paramref name="flag"/> is not what its reads make it; null when they agree. <paramref name="path"/> is the node's file.</summary>
    public static SourceRefusal? Disagreement(string nodeId, LineageFlag flag, bool declared, LineageValue derived, string path)
    {
        ArgumentNullException.ThrowIfNull(derived);
        return declared == derived.Value ? null : ExpressionSentences.FlagDisagrees(nodeId, Member(flag), declared, derived, path);
    }

    /// <summary>The flag's member name in a node's front matter: <c>modelDerived</c>, <c>runDependent</c>, <c>profileCarrying</c>.</summary>
    public static string Member(LineageFlag flag) => flag switch
    {
        LineageFlag.ModelDerived => "modelDerived",
        LineageFlag.RunDependent => "runDependent",
        LineageFlag.ProfileCarrying => "profileCarrying",
        _ => ((int)flag).ToString(CultureInfo.InvariantCulture),
    };

    private static bool Starts(LineageNode node, LineageFlag flag) => flag switch
    {
        LineageFlag.ModelDerived => node.StartsModelDerived,
        LineageFlag.ProfileCarrying => node.StartsProfileCarrying,
        _ => false,
    };

    /// <summary>The path of the first read that makes <paramref name="flag"/> true, or null.</summary>
    private static string? FirstCause(IReadOnlyList<LineageRead> reads, LineageFlag flag, Func<string, bool> nodeHas)
    {
        foreach (var read in reads)
        {
            if (flag == LineageFlag.RunDependent && HasRunRoot(read.Path))
                return read.Path;
            if (read.Key is not null && nodeHas(read.Key))
                return read.Path;
        }

        return null;
    }

    private static bool HasRunRoot(string path) =>
        path == "run" || path.StartsWith("run.", StringComparison.Ordinal);
}
