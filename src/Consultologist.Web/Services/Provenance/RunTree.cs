using Consultologist.PackageFormat;
using Consultologist.Web.Services.AI;

namespace Consultologist.Web.Services.Provenance;

/// <summary>
/// v30 (#1345 C1b, design § 10): a 30 record as the tree the run took — a loop's items with
/// each item's body under it, an inner loop's instance under its outer item with its own
/// count, a while's passes, the items an until or a max stopped short of, a let's row. Pure:
/// History draws it, the Create page's rail (C1c) will too.
/// </summary>
public static class RunTree
{
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Running = "Running";
    public const string Skipped = "Skipped";

    /// <summary>#1345 C1c: nothing of it has run yet — never "skipped" (it may still run).</summary>
    public const string Pending = "Pending";

    /// <summary>A node of the tree.</summary>
    public abstract record Entry(ConsultGenerationNodeDescriptor Descriptor);

    /// <summary>A plain node (or a let), at a path when it runs in a body; its row when it has one.</summary>
    public sealed record NodeEntry(ConsultGenerationNodeDescriptor Descriptor, string? Path, ConsultGenerationNodeStatus? Row, bool InFlight = false) : Entry(Descriptor)
    {
        public bool IsLet => RecordFlow.IsLet(Descriptor);

        /// <summary>Its row's status; Running when it is the step in flight; Pending when it has no row yet.</summary>
        public string Status => Row?.Status ?? (InFlight ? Running : Pending);
    }

    /// <summary>A for — at the top (Path null) or one instance at its outer item's path — with its count's words and its items.</summary>
    public sealed record LoopEntry(
        ConsultGenerationNodeDescriptor Descriptor,
        string? Path,
        ConsultGenerationNodeStatus? Row,
        string CountText,
        string? StopText,
        IReadOnlyList<ItemEntry> Items) : Entry(Descriptor);

    /// <summary>One item of a loop instance: its path, name, rolled-up status, why it did not run, its body.</summary>
    public sealed record ItemEntry(string Path, string Name, string Status, string? Reason, IReadOnlyList<Entry> Children, int Done, int Steps);

    /// <summary>A while — at the top or one instance at an outer path — with its words and its passes.</summary>
    public sealed record WhileEntry(
        ConsultGenerationNodeDescriptor Descriptor,
        string? Path,
        ConsultGenerationNodeStatus? Row,
        string Words,
        IReadOnlyList<PassEntry> Passes) : Entry(Descriptor);

    /// <summary>One pass of a while: its number, rolled-up status and body.</summary>
    public sealed record PassEntry(int Number, string Path, string Status, IReadOnlyList<Entry> Children, int Done, int Steps);

    /// <summary>An if/case — at the top (its branch, the bodies it pruned) or per item inside a loop (the branch it took for the item).</summary>
    public sealed record FlowEntry(
        ConsultGenerationNodeDescriptor Descriptor,
        string? Path,
        ConsultGenerationNodeStatus? Row,
        string BranchText,
        IReadOnlyList<Entry> Children) : Entry(Descriptor);

    /// <summary>
    /// Whether a record takes the tree: storage 11 or newer, and something only 30 has — a loop
    /// or a while inside a repeat, a while, or a let. Every other record keeps its flat groups.
    /// </summary>
    public static bool Applies(ConsultGenerationJobResponse detail) =>
        detail.SchemaVersion >= 11
        && detail.Nodes is { Count: > 0 } nodes
        && nodes.Any(node => RecordFlow.IsWhile(node) || RecordFlow.IsLet(node) || (RecordFlow.IsRepeat(node) && RecordFlow.RepeatOf(node, nodes) is not null));

    public static IReadOnlyList<Entry> Build(ConsultGenerationJobResponse detail) => new Builder(detail).Top();

    private sealed class Builder
    {
        private readonly IReadOnlyList<ConsultGenerationNodeDescriptor> _nodes;
        private readonly Dictionary<string, ConsultGenerationNodeDescriptor> _byId;
        private readonly IReadOnlyDictionary<string, ConsultGenerationNodeStatus> _rows;
        private readonly IReadOnlyList<ConsultLoopInstanceRow> _instances;
        private readonly IReadOnlyList<ConsultCollectionRoster> _rosters;
        private readonly bool _finished;

        public Builder(ConsultGenerationJobResponse detail)
        {
            _nodes = detail.Nodes ?? Array.Empty<ConsultGenerationNodeDescriptor>();
            _byId = _nodes.GroupBy(n => n.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            _rows = detail.NodeOutputs ?? new Dictionary<string, ConsultGenerationNodeStatus>();
            _instances = detail.LoopInstances ?? Array.Empty<ConsultLoopInstanceRow>();
            _rosters = detail.Collections ?? Array.Empty<ConsultCollectionRoster>();
            _finished = detail.Status is "Completed" or "Failed" or "Cancelled";
        }

        public IReadOnlyList<Entry> Top()
        {
            var bodyIds = RecordFlow.BodyIds(_nodes);
            return _nodes.Where(node => !bodyIds.Contains(node.Id)).Select(node => EntryOf(node, null)).ToList();
        }

        private ConsultGenerationNodeStatus? RowAt(string id, string? path) =>
            _rows.GetValueOrDefault(path is null ? id : WorkflowItemPath.Key(id, path));

        private Entry EntryOf(ConsultGenerationNodeDescriptor node, string? path)
        {
            if (RecordFlow.IsLoop(node))
            {
                return Loop(node, path);
            }

            if (RecordFlow.IsWhile(node))
            {
                return While(node, path);
            }

            if (RecordFlow.IsBranching(node))
            {
                return Flow(node, path);
            }

            return new NodeEntry(node, path, RowAt(node.Id, path));
        }

        private IEnumerable<ConsultGenerationNodeDescriptor> BodyOf(ConsultGenerationNodeDescriptor flow) =>
            (flow.Body ?? Array.Empty<string>()).Where(_byId.ContainsKey).Select(id => _byId[id]);

        // ---- a for -------------------------------------------------------------------------

        private LoopEntry Loop(ConsultGenerationNodeDescriptor loop, string? outer)
        {
            var instance = outer is null ? null : _instances.FirstOrDefault(row => row.LoopId == loop.Id && row.Path == outer);
            var depth = outer is null ? 1 : WorkflowItemPath.Depth(outer) + 1;

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (loop.Collection is { } collection)
            {
                var collectionId = WorkflowFanSources.CollectionIdOf(collection);
                foreach (var item in _rosters.Where(r => r.CollectionId == collectionId).SelectMany(r => r.Items))
                {
                    var path = outer is null || item.Id.Contains('/') ? item.Id : WorkflowItemPath.Of(new[] { outer, item.Id });
                    if (Under(path, outer, depth))
                    {
                        paths.Add(path);
                        names.TryAdd(path, item.Name);
                    }
                }
            }

            // The loop's own rows (a not-run item has only that) and every body row at this depth.
            foreach (var id in new[] { loop.Id }.Concat(Leaves(loop)))
            {
                foreach (var key in _rows.Keys)
                {
                    if (WorkflowItemPath.PathFor(key, id) is { } rowPath && Prefix(rowPath, depth) is { } prefix && Under(prefix, outer, depth))
                    {
                        paths.Add(prefix);
                    }
                }
            }

            var count = outer is null ? loop.Count : instance?.Count;

            // #1345 C1c: a list read off a node's value or an outer item's field numbers its items
            // 1..n — a settled count names every item, the ones nothing has run for yet included.
            if (count is > 0 and var settledCount && Positional(loop.Collection))
            {
                for (var position = 1; position <= settledCount; position++)
                {
                    var id = position.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    paths.Add(outer is null ? id : WorkflowItemPath.Of(new[] { outer, id }));
                }
            }

            var items = paths.OrderBy(p => p, PathOrder.Instance).Select(path => Item(loop, path, names.GetValueOrDefault(path))).ToList();
            var reason = outer is null ? loop.Reason : instance?.Reason ?? instance?.Failure;
            var countText = count is { } known
                ? reason is { } why ? $"{known} — {why}" : known.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : instance?.Failure is { } failure ? failure
                : LoopCount.IsNodeValue(loop.Collection) || outer is not null ? LoopCount.Describe(_finished)
                : items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var stop = outer is null ? loop.StopReason : instance?.StopReason;
            return new LoopEntry(loop, outer, outer is null ? RowAt(loop.Id, null) : null, countText, stop, items);
        }

        private ItemEntry Item(ConsultGenerationNodeDescriptor loop, string path, string? name)
        {
            var own = RowAt(loop.Id, path);
            var label = name ?? $"{loop.Label} {WorkflowItemPath.Last(path)}";
            if (own is { Status: Skipped })
            {
                return new ItemEntry(path, label, Skipped, own.Reason ?? "skipped", Array.Empty<Entry>(), 0, 0);
            }

            var children = BodyOf(loop).Select(body => EntryOf(body, path)).ToList();
            var (status, done, steps) = RollUp(children);
            return new ItemEntry(path, label, status, null, MarkInFlight(children, status), done, steps);
        }

        // ---- a while -----------------------------------------------------------------------

        private WhileEntry While(ConsultGenerationNodeDescriptor whileNode, string? outer)
        {
            var instance = outer is null ? null : _instances.FirstOrDefault(row => row.LoopId == whileNode.Id && row.Path == outer);
            var depth = outer is null ? 1 : WorkflowItemPath.Depth(outer) + 1;
            var numbers = Leaves(whileNode)
                .SelectMany(id => _rows.Keys.Select(key => WorkflowItemPath.PathFor(key, id)))
                .OfType<string>()
                .Select(rowPath => Prefix(rowPath, depth))
                .OfType<string>()
                .Where(prefix => Under(prefix, outer, depth))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, PathOrder.Instance)
                .ToList();

            var passes = numbers.Select(passPath =>
            {
                var children = BodyOf(whileNode).Select(body => EntryOf(body, passPath)).ToList();
                var (status, done, steps) = RollUp(children);
                var number = int.TryParse(WorkflowItemPath.Last(passPath), out var n) ? n : 0;
                return new PassEntry(number, passPath, status, MarkInFlight(children, status), done, steps);
            }).ToList();

            var row = outer is null ? RowAt(whileNode.Id, null) : null;
            var passCount = outer is null ? whileNode.Passes : instance?.Passes;
            var stopped = outer is null ? whileNode.Stopped : instance?.Stopped;
            var reason = outer is null ? whileNode.StopReason : instance?.StopReason;
            return new WhileEntry(whileNode, outer, row, WhileWords(whileNode.Max, passCount, stopped, reason, passes.Count, row), passes);
        }

        private string WhileWords(int? max, int? passes, string? stopped, string? reason, int seen, ConsultGenerationNodeStatus? row)
        {
            var bound = max?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "—";
            if (passes is { } count)
            {
                if (count == 0)
                {
                    return reason ?? "its check did not hold before the first pass";
                }

                var why = stopped switch
                {
                    "bound" => "stopped at its bound",
                    "until" => "its until held",
                    "check" => "the check stopped holding",
                    _ => reason ?? "stopped",
                };
                return $"{count} {(count == 1 ? "pass" : "passes")} — {why}";
            }

            if (row is { Status: Skipped, Reason: { } skipped })
            {
                return skipped;
            }

            return seen == 0 ? $"at most {bound}" : _finished ? $"{seen} {(seen == 1 ? "pass" : "passes")}" : $"pass {seen} of at most {bound}";
        }

        // ---- an if/case --------------------------------------------------------------------

        private FlowEntry Flow(ConsultGenerationNodeDescriptor flow, string? path)
        {
            if (path is null)
            {
                var taken = BodyOf(flow).Where(body => RowAt(body.Id, null) is not null || _rows.Keys.Any(k => WorkflowItemPath.PathFor(k, body.Id) is not null)).Select(body => EntryOf(body, null)).ToList();
                return new FlowEntry(flow, null, RowAt(flow.Id, null), $"branch: {RecordFlow.BranchText(flow)}", taken);
            }

            // Inside a loop: the branch it took for this item (its branches map, keyed by the
            // item's path or, below 30, its id), or the reason it took none.
            var branch = flow.Branches?.GetValueOrDefault(path) ?? flow.Branches?.GetValueOrDefault(WorkflowItemPath.Last(path));
            var own = RowAt(flow.Id, path);
            if (branch == "none" || own is { Status: Skipped })
            {
                return new FlowEntry(flow, path, own, $"skipped — {own?.Reason ?? "took no branch"}", Array.Empty<Entry>());
            }

            var children = BodyOf(flow).Where(body => RowAt(body.Id, path) is not null || RecordFlow.IsFlow(body)).Select(body => EntryOf(body, path)).ToList();
            var returned = children.OfType<NodeEntry>().FirstOrDefault(child => child.Row is not null);
            return new FlowEntry(flow, path, own, branch is null ? "per item" : returned is null ? branch : $"{branch} → {returned.Descriptor.Id}", children);
        }

        // ---- shared ------------------------------------------------------------------------

        /// <summary>
        /// #1345 C1c: on a run still going, the first step of a running item or pass that has no
        /// row yet is the one in flight — drawn running, as the rail's earliest unsettled node is.
        /// </summary>
        private IReadOnlyList<Entry> MarkInFlight(List<Entry> children, string status)
        {
            if (_finished || status != Running || children.Any(child => child is NodeEntry { Row.Status: Running }))
            {
                return children;
            }

            var index = children.FindIndex(child => child is NodeEntry { Row: null });
            if (index >= 0)
            {
                children[index] = ((NodeEntry)children[index]) with { InFlight = true };
            }

            return children;
        }

        /// <summary>Every non-flow id under a flow, to any depth — the ids whose rows carry its items' paths.</summary>
        private IEnumerable<string> Leaves(ConsultGenerationNodeDescriptor flow)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { flow.Id };
            var stack = new Stack<ConsultGenerationNodeDescriptor>(BodyOf(flow));
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (!seen.Add(node.Id))
                {
                    continue;
                }

                if (RecordFlow.IsFlow(node))
                {
                    yield return node.Id;
                    foreach (var body in BodyOf(node))
                    {
                        stack.Push(body);
                    }
                }
                else
                {
                    yield return node.Id;
                }
            }
        }

        /// <summary>A loop whose items are numbered by position: over a node's value, or an outer item's field.</summary>
        private static bool Positional(string? collection) =>
            LoopCount.IsNodeValue(collection) || collection?.StartsWith("item.value.", StringComparison.Ordinal) == true;

        /// <summary>A path's first <paramref name="depth"/> segments, or null when it is shorter.</summary>
        private static string? Prefix(string path, int depth)
        {
            var segments = WorkflowItemPath.Segments(path);
            return segments.Count < depth ? null : string.Join('/', segments.Take(depth));
        }

        /// <summary>Whether a path at a depth sits under an outer path (always, at the top).</summary>
        private static bool Under(string path, string? outer, int depth) =>
            WorkflowItemPath.Depth(path) == depth && (outer is null || path.StartsWith(outer + "/", StringComparison.Ordinal));

        /// <summary>
        /// An item's or a pass's status from its body: any failed → failed; any running → running;
        /// every step done or skipped → done (skipped when all were); something settled → running;
        /// nothing → pending. Steps count leaf rows, nested instances included.
        /// </summary>
        private static (string Status, int Done, int Steps) RollUp(IReadOnlyList<Entry> children)
        {
            var leaves = LeafStatuses(children).ToList();
            var done = leaves.Count(s => s == Completed);
            // #1345 C1c: nothing started is pending (it may yet run), never skipped.
            var status = leaves.Any(s => s == Failed) ? Failed
                : leaves.Any(s => s == Running) ? Running
                : leaves.Count > 0 && leaves.All(s => s is Completed or Skipped) ? (done > 0 ? Completed : Skipped)
                : done > 0 || leaves.Any(s => s == Skipped) ? Running
                : Pending;
            return (status, done, leaves.Count);
        }

        private static IEnumerable<string> LeafStatuses(IEnumerable<Entry> entries)
        {
            foreach (var entry in entries)
            {
                switch (entry)
                {
                    case NodeEntry node:
                        yield return node.Row?.Status ?? Pending;
                        break;
                    case LoopEntry loop:
                        foreach (var item in loop.Items)
                        {
                            foreach (var s in item.Children.Count == 0 ? new[] { item.Status } : LeafStatuses(item.Children))
                            {
                                yield return s;
                            }
                        }

                        break;
                    case WhileEntry whileEntry:
                        foreach (var s in LeafStatuses(whileEntry.Passes.SelectMany(p => p.Children)))
                        {
                            yield return s;
                        }

                        break;
                    case FlowEntry flow:
                        foreach (var s in flow.Children.Count == 0 ? new[] { Skipped } : LeafStatuses(flow.Children))
                        {
                            yield return s;
                        }

                        break;
                }
            }
        }
    }
}
