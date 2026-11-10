using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat;

/// <summary>
/// v30 (#1340, package-format-v30-design.md § 1): an item's <b>path</b> — the item
/// ids from the outermost loop inward, joined by <c>/</c> — and the keys built on
/// it. Every structure that carries an item (the scheduler's instance keys, block
/// ids, the record's node rows and item progress, the event keys, the pages'
/// parses) builds and reads it here, so nesting (B2) and passes (B3) change only
/// this file.
///
/// <para><b>One level is today's id, byte for byte</b>: a one-segment path is the
/// item id, and a key is <c>{node}:{path}</c>. Item ids keep their source's form
/// (an array input's 0-based position, a node value's 1-based one, a folder
/// item's file stem) and at 26 and above never hold <c>/</c> or <c>:</c>; node ids
/// are <c>^[a-z][a-z0-9_]*</c>. So a key splits at its first <c>:</c>
/// (<see cref="TryParseKey"/>), and a block id's path is the text after its last.
/// Below 26 an engine item may carry a whole block id as its id
/// (<c>draft:consult:hpi</c>), where the first and the last <c>:</c> disagree: each
/// parse keeps the rule its site has always used — <see cref="TryParseKey"/> for
/// the first, <see cref="TrySplitAtLast"/> for the last — so no key reads
/// differently.</para>
///
/// <para>A folder gate's <c>{folder}/{item}</c> key is not a path (§ 1.1): it
/// lives in its own dictionary and never meets one.</para>
/// </summary>
public static class WorkflowItemPath
{
    public const char Separator = '/';

    /// <summary>The ids joined, outermost first.</summary>
    public static string Of(IEnumerable<string> ids) => string.Join(Separator, ids);

    /// <summary>One loop's item: the path is the id.</summary>
    public static string Single(string id) => id;

    /// <summary>The ids of a path, outermost first.</summary>
    public static IReadOnlyList<string> Segments(string path) => path.Split(Separator);

    /// <summary>How many loops deep a path is.</summary>
    public static int Depth(string path) => Segments(path).Count;

    /// <summary>The innermost item's id.</summary>
    public static string Last(string path) => Segments(path)[^1];

    /// <summary>
    /// A dependency's path from its reader's: the reader's first
    /// <paramref name="depth"/> segments, where <paramref name="depth"/> is how many
    /// loops the dependency runs in — the common prefix (§ 1.2). Null at depth 0
    /// (a node outside every loop) or when the reader has no path.
    /// </summary>
    public static string? Prefix(string? path, int depth)
    {
        if (path is null || depth <= 0)
        {
            return null;
        }

        var segments = Segments(path);
        return depth >= segments.Count ? path : Of(segments.Take(depth));
    }

    /// <summary>
    /// The path a fan item carries — at one level its id. Where the engine holds an
    /// item as its <c>id</c>/<c>name</c>/<c>value</c> map, this is the one place the
    /// path is read from it.
    /// </summary>
    public static string? OfItem(IReadOnlyDictionary<string, string>? item) =>
        item is not null && item.TryGetValue("id", out var id) ? id : null;

    /// <inheritdoc cref="OfItem(IReadOnlyDictionary{string, string}?)"/>
    public static string? OfItem(JsonObject? item) => item?["id"]?.GetValue<string>();

    /// <summary>A node's key for one path — <c>{node}:{path}</c>, the bare node id outside every loop.</summary>
    public static string Key(string nodeId, string? path) => path is null ? nodeId : $"{nodeId}:{path}";

    /// <summary>Splits a <see cref="Key"/>: the node before the first <c>:</c>, the path after it; false for a bare node id.</summary>
    public static bool TryParseKey(string key, out string nodeId, out string path)
    {
        var colon = key.IndexOf(':');

        if (colon <= 0)
        {
            (nodeId, path) = (key, string.Empty);
            return false;
        }

        (nodeId, path) = (key[..colon], key[(colon + 1)..]);
        return true;
    }

    /// <summary>
    /// Splits a key at its <b>last</b> <c>:</c> — the head before it, the last
    /// segment after — the rule the event stream's item-step parse and the pages'
    /// item reads have always used; false when there is none.
    /// </summary>
    public static bool TrySplitAtLast(string key, out string head, out string last)
    {
        var colon = key.LastIndexOf(':');

        if (colon <= 0)
        {
            (head, last) = (key, string.Empty);
            return false;
        }

        (head, last) = (key[..colon], key[(colon + 1)..]);
        return true;
    }

    /// <summary>The node of a key — the whole key when it carries no path.</summary>
    public static string NodeOf(string key) => TryParseKey(key, out var nodeId, out _) ? nodeId : key;

    /// <summary>Whether a key carries a path (an item's row, not a node's).</summary>
    public static bool IsItemKey(string key) => key.Contains(':');

    /// <summary>The path of a key that belongs to <paramref name="nodeId"/>, or null.</summary>
    public static string? PathFor(string key, string nodeId) =>
        key.Length > nodeId.Length + 1 && key[nodeId.Length] == ':' && key.StartsWith(nodeId, StringComparison.Ordinal)
            ? key[(nodeId.Length + 1)..]
            : null;

    /// <summary>A loop block's id: <c>{prefix}{node}:{path}</c> — the prefix the deliverable's (<c>note:</c>, or empty).</summary>
    public static string BlockId(string prefix, string nodeId, string path) => $"{prefix}{nodeId}:{path}";

    /// <summary>The path a block id names — the text after its last <c>:</c>; the id itself when it has none.</summary>
    public static string PathOfBlock(string blockId)
    {
        var last = blockId.LastIndexOf(':');
        return last < 0 ? blockId : blockId[(last + 1)..];
    }

    /// <summary>
    /// A loop block's <c>{loop}:{path}</c> — its last two <c>:</c>-segments
    /// (<c>note:sections:plan</c> → <c>sections:plan</c>); null for a block that
    /// names no item.
    /// </summary>
    public static string? LoopItemOfBlock(string blockId)
    {
        var last = blockId.LastIndexOf(':');

        if (last <= 0)
        {
            return null;
        }

        var previous = blockId.LastIndexOf(':', last - 1);
        return previous < 0 ? blockId : blockId[(previous + 1)..];
    }
}
