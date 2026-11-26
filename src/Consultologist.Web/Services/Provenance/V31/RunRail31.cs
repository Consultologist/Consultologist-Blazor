using System.Text.Json;
using Consultologist.Web.Services.Workflow;

namespace Consultologist.Web.Services.Provenance.V31;

/// <summary>
/// #1509 (Task 12): one stored row as the <c>row-finished</c> event announces it, <c>{ key, section, status }</c>:
/// <paramref name="Status"/> is the row's <c>status</c>, <c>state</c> or <c>result</c> as stored, null when it has none.
/// </summary>
public sealed record RowFinished(string Key, string Section, string? Status);

/// <summary>A rail block: a top-level node of the package, its own row's stored status, and its item and pass rows.</summary>
public sealed record RailBlock31(string Key, string Name, string? Status, bool Current, IReadOnlyList<RailChild31> Children);

/// <summary>
/// An item or pass row under its top-level block. <paramref name="Label"/> is the item path or pass as the key writes it
/// (<c>2/1</c>, <c>#3</c>), 1-based as stored; a row inside the block with neither (a call's inner node) is labelled by
/// the scopes after the block's key.
/// </summary>
public sealed record RailChild31(string Key, string Label, string? Status);

/// <summary>
/// #1509 (Task 12, D15; part 09 § 4.2): the Create page's rail for a 31 job. The blocks are the package's <c>Blocks</c>,
/// the top-level node keys (<c>node.plan</c>); each takes the stored status of its own row, and the rows keyed under it
/// (<c>&lt;block&gt;.</c>, <c>&lt;block&gt;:</c> or <c>&lt;block&gt;#</c>) appear under it in key order. Check, deliverable and
/// delivery rows are never rail rows. Every status is the stored word (one vocabulary). Pure.
/// </summary>
public static class RunRail31
{
    /// <summary>S09-019: the one status shown in words other than its stored word.</summary>
    public const string Delivering = "Delivering…";

    public static IReadOnlyList<RailBlock31> Build(
        IReadOnlyList<WorkflowPackageBlockResponse> blocks, IReadOnlyCollection<RowFinished> finished, string? currentKey)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(finished);

        var rail = new List<RailBlock31>(blocks.Count);
        foreach (var block in blocks)
        {
            string? status = null;
            var children = new List<RailChild31>();
            foreach (var row in finished.OrderBy(row => row.Key, StringComparer.Ordinal))
            {
                if (!string.Equals(row.Section, "node", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(row.Key, block.Id, StringComparison.Ordinal))
                {
                    status = row.Status;
                }
                else if (Under(row.Key, block.Id) is { } rest)
                {
                    children.Add(new RailChild31(row.Key, Label(rest), row.Status));
                }
            }

            var current = currentKey != null
                && (string.Equals(currentKey, block.Id, StringComparison.Ordinal) || Under(currentKey, block.Id) != null);
            rail.Add(new RailBlock31(block.Id, block.Name, status, current, children));
        }

        return rail;
    }

    /// <summary>The job's status as shown: the stored word, but <c>delivering</c> reads "Delivering…" (S09-019).</summary>
    public static string StatusText(string status) =>
        string.Equals(status, "delivering", StringComparison.Ordinal) ? Delivering : status;

    /// <summary>
    /// A listed row's status as <c>row-finished</c> carries it: a deliverable's <c>state</c>, a delivery's <c>result</c>,
    /// any other row's <c>status</c>; null when the member is absent or not a string.
    /// </summary>
    public static string? StatusOf(string section, JsonElement row)
    {
        var member = section switch
        {
            "deliverable" => "state",
            "delivery" => "result",
            _ => "status",
        };

        return row.ValueKind == JsonValueKind.Object
            && row.TryGetProperty(member, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    /// <summary>The part of <paramref name="key"/> after <paramref name="block"/> and its separator, or null when the key is not under it.</summary>
    private static string? Under(string key, string block) =>
        key.Length > block.Length + 1
        && key.StartsWith(block, StringComparison.Ordinal)
        && key[block.Length] is '.' or ':' or '#'
            ? key[block.Length..]
            : null;

    /// <summary>The item path (after <c>:</c>) or the pass and what follows it (from <c>#</c>), as the key writes them.</summary>
    private static string Label(string rest)
    {
        var at = rest.IndexOfAny([':', '#']);
        if (at < 0)
        {
            return rest.TrimStart('.');
        }

        return rest[at] == ':' ? rest[(at + 1)..] : rest[at..];
    }
}
