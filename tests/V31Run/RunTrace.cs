using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06): a whole run as one text, for a golden. Four parts, each under its heading: the agent
/// calls in the order dispatched (row keys); every row in the order recorded, as <c>key | state | reason | value hash</c>
/// (<c>-</c> for none); every decision in the order recorded, as <c>site | member | printed condition | result</c> (a
/// <c>case</c>'s branch taken); and the result: its state, sentence, failed key, agent calls, peak in flight, and each
/// top-level entry's read form as a hash. No clock value: a decision's time is the host's and is left out. LF line ends,
/// a final line end.
/// </summary>
internal static class RunTrace
{
    public static string Of(FakeRunHost host, ListRecorder recorder, RunResult result)
    {
        var text = new StringBuilder();
        text.Append("# dispatched\n");
        foreach (var key in host.Dispatched)
            text.Append(key).Append('\n');

        text.Append("\n# rows\n");
        foreach (var row in recorder.Rows)
            text.Append(Row(row)).Append('\n');

        text.Append("\n# decisions\n");
        foreach (var decision in recorder.Decisions)
            text.Append(Decision(decision)).Append('\n');

        text.Append("\n# result\n");
        text.Append("state | ").Append(Word(result.State.ToString())).Append('\n');
        text.Append("sentence | ").Append(result.Sentence ?? "-").Append('\n');
        text.Append("failed | ").Append(result.FailedKey ?? "-").Append('\n');
        text.Append("agentCalls | ").Append(result.AgentCalls.ToString(CultureInfo.InvariantCulture)).Append('\n');
        text.Append("peakInFlight | ").Append(result.PeakInFlight.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var (key, read) in result.Values.OrderBy(v => v.Key, StringComparer.Ordinal))
            text.Append("value | ").Append(key).Append(" | ").Append(CanonicalJson.Hash(read)).Append('\n');
        return text.ToString();
    }

    /// <summary>A row as a trace line: <c>key | state | reason | value hash</c>.</summary>
    public static string Row(RunRow row) =>
        $"{row.Key} | {Word(row.State.ToString())} | {row.Reason ?? "-"} | {row.ValueHash ?? "-"}";

    /// <summary>A decision as a trace line: <c>site | member | printed | result</c>, a <c>case</c>'s result its branch.</summary>
    public static string Decision(RunDecision decision) =>
        $"{decision.Site} | {decision.Member} | {decision.Printed} | "
        + (decision.Result is { } result ? (result ? "true" : "false") : $"branch {decision.Branch ?? "-"}");

    /// <summary>An enum member as the record words it: <c>NotRun</c> is <c>not_run</c>.</summary>
    private static string Word(string member)
    {
        var word = new StringBuilder();
        foreach (var c in member)
        {
            if (char.IsUpper(c) && word.Length > 0)
                word.Append('_');
            word.Append(char.ToLowerInvariant(c));
        }

        return word.ToString();
    }
}

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 3.2): an oracle for the fixed dispatch order, worked out from the plan, the
/// row keys and when each row was recorded, independently of the runner's own rank and readiness. For each agent call it
/// finds the earliest dispatch at which the call could have been waiting, from conditions every waiting call meets (each
/// is necessary, none is the runner's code): every row it reads, and every row its enclosing nodes and its call read, has
/// been recorded (the row of the instance standing for it from the reader's frame); for a pass after the first, every row
/// of the pass before it has been recorded; for an item of a loop with a cap (<c>maxParallel</c>, or 1 when sequential), at
/// least enough of the earlier items have settled to leave it a place. From that dispatch on, until its own, every call
/// dispatched must rank before it (part 09 § 3.2: the calls' orders, its entry's order, its item positions outermost
/// first, its passes). Not modelled: a loop over another loop's <c>.items</c> (which follows its production loop item by
/// item), which none of the plans checked here has around an agent.
/// </summary>
internal static class DispatchOracle
{
    /// <summary>
    /// Asserts the oracle on a completed run: <paramref name="calls"/> in the order dispatched (each once), the rows with
    /// their stamps (the agent calls made before each was recorded), and <paramref name="result"/> (for snippet item
    /// positions).
    /// </summary>
    public static void Holds(RunPlan plan, IReadOnlyList<AgentCall> calls, ListRecorder recorder, RunResult result)
    {
        Assert.Equal(calls.Count, calls.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count());
        var stamps = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < recorder.Rows.Count; i++)
            stamps.Add(recorder.Rows[i].Key, recorder.Stamps[i]);
        var rows = recorder.Rows.Select(r => (Parts: Parse(r.Key), Stamp: stamps[r.Key])).ToList();
        var positions = Positions(result);

        var ranks = new List<IReadOnlyList<int>>();
        var earliest = new List<int>();
        foreach (var call in calls)
        {
            var site = Site.Of(plan, call);
            ranks.Add(site.Rank(positions));
            earliest.Add(site.Earliest(stamps, rows, positions));
        }

        for (var e = 0; e < calls.Count; e++)
        {
            // a call is never dispatched before it could be waiting
            Assert.True(earliest[e] <= e, $"{calls[e].Key} was dispatched {e}th, before it could be waiting (at {earliest[e]}).");
            for (var d = earliest[e]; d < e; d++)
            {
                Assert.True(Compare(ranks[d], ranks[e]) < 0,
                    $"{calls[d].Key} was dispatched {d}th while {calls[e].Key}, which ranks before it, could have been waiting since {earliest[e]}.");
            }
        }
    }

    /// <summary>Ranks compared element by element, a shorter one first on a tie.</summary>
    public static int Compare(IReadOnlyList<int> x, IReadOnlyList<int> y)
    {
        for (var i = 0; i < Math.Min(x.Count, y.Count); i++)
        {
            if (x[i] != y[i])
                return x[i].CompareTo(y[i]);
        }

        return x.Count.CompareTo(y.Count);
    }

    /// <summary>A row key's parts: the entry's rooted key, the item ids, the passes (each with the items outside it).</summary>
    internal sealed record KeyParts(string Rooted, IReadOnlyList<string> Items, IReadOnlyList<(int Position, int Pass)> Passes);

    /// <summary><paramref name="key"/> read back into its parts, checked to be what <see cref="RunKeys.Of"/> writes.</summary>
    internal static KeyParts Parse(string key)
    {
        var end = key.IndexOfAny([':', '#']);
        var items = new List<string>();
        var passes = new List<(int, int)>();
        for (var at = end; at >= 0 && at < key.Length;)
        {
            var next = key.IndexOfAny([':', '/', '#'], at + 1);
            if (next < 0)
                next = key.Length;
            var token = key[(at + 1)..next];
            if (key[at] == '#')
                passes.Add((items.Count, int.Parse(token, CultureInfo.InvariantCulture)));
            else
                items.Add(token);
            at = next;
        }

        var parts = new KeyParts(end < 0 ? key : key[..end], items, passes);
        Assert.Equal(key, RunKeys.Of(parts.Rooted, parts.Items, parts.Passes));
        return parts;
    }

    /// <summary>Each item id's position in its list: a list item's id is its position; a snippet item's, read from the loops' <c>.items</c>.</summary>
    private static Dictionary<string, int> Positions(RunResult result)
    {
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        void Walk(JsonNode? node, int depth)
        {
            if (depth > 64)
                return;
            if (node is JsonObject obj)
            {
                if (obj["items"] is JsonArray items)
                {
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (items[i]?["item"]?["id"] is JsonValue id && id.TryGetValue<string>(out var text))
                            positions.TryAdd(text, i);
                    }
                }

                foreach (var (_, member) in obj)
                    Walk(member, depth + 1);
            }
            else if (node is JsonArray array)
            {
                foreach (var member in array)
                    Walk(member, depth + 1);
            }
        }

        foreach (var (_, read) in result.Values)
            Walk(read, 0);
        return positions;
    }

    private static int Position(string id, IReadOnlyDictionary<string, int> positions)
    {
        if (int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var position))
            return position;
        Assert.True(positions.TryGetValue(id, out position), $"No position for the item '{id}'.");
        return position;
    }

    /// <summary>
    /// One body's entries as a call's frame sees them: the package's (<see cref="Prefix"/> null) or a function's, keyed
    /// through the call (<see cref="Prefix"/> the call's rooted key), with the loops outside it (the caller's, for a function).
    /// </summary>
    private sealed record Context(IReadOnlyDictionary<string, PlanEntry> Map, string? Prefix, IReadOnlyList<PlanEntry> Outside)
    {
        public string Rooted(string key) => Prefix == null ? key : Prefix + key[key.IndexOf('.')..];

        /// <summary>The loops (<c>for</c>, <c>while</c>) enclosing <paramref name="entry"/>, outermost first, those outside the body included.</summary>
        public List<PlanEntry> Loops(PlanEntry entry)
        {
            var loops = new List<PlanEntry>();
            for (var at = Parent(entry); at != null; at = Parent(at))
            {
                if (at.Kind is PlanKind.For or PlanKind.While)
                    loops.Insert(0, at);
            }

            return [.. Outside, .. loops];
        }

        public PlanEntry? Parent(PlanEntry entry) => entry.Parent is { } parent ? Map.GetValueOrDefault(parent) : null;

        /// <summary>Whether <paramref name="ancestor"/> is <paramref name="entry"/> or one of its parents.</summary>
        public bool Encloses(PlanEntry ancestor, PlanEntry entry)
        {
            for (PlanEntry? at = entry; at != null; at = Parent(at))
            {
                if (ReferenceEquals(at, ancestor))
                    return true;
            }

            return false;
        }

        /// <summary>The nearest loop strictly enclosing <paramref name="entry"/> in this body (through any <c>if</c> or <c>case</c>), or null.</summary>
        public PlanEntry? Level(PlanEntry entry)
        {
            for (var at = Parent(entry); at != null; at = Parent(at))
            {
                if (at.Kind is PlanKind.For or PlanKind.While)
                    return at;
            }

            return null;
        }
    }

    /// <summary>One agent call: its entry, its context, its key's parts, and the call node it runs under (null at the package's level).</summary>
    private sealed class Site
    {
        private readonly PlanEntry entry;
        private readonly Context context;
        private readonly KeyParts parts;
        private readonly PlanEntry? caller;
        private readonly Context top;

        private Site(PlanEntry entry, Context context, KeyParts parts, PlanEntry? caller, Context top)
        {
            this.entry = entry;
            this.context = context;
            this.parts = parts;
            this.caller = caller;
            this.top = top;
        }

        public static Site Of(RunPlan plan, AgentCall call)
        {
            var parts = Parse(call.Key);
            var top = new Context(plan.Entries, null, []);
            if (call.Function == null)
                return new Site(plan.Entries[call.EntryKey], top, parts, null, top);

            // the call node's rooted key is the row's, less the entry key's part after its first dot (one call deep)
            var suffix = call.EntryKey[call.EntryKey.IndexOf('.')..];
            Assert.EndsWith(suffix, parts.Rooted);
            var callKey = parts.Rooted[..^suffix.Length];
            Assert.True(plan.Entries.TryGetValue(callKey, out var caller), $"{call.Key}: the oracle reads calls one deep only.");
            var context = new Context(plan.Functions[call.Function].Entries, callKey, top.Loops(caller!));
            return new Site(context.Map[call.EntryKey], context, parts, caller, top);
        }

        /// <summary>Part 09 § 3.2's rank: the call's order, the entry's, the item positions outermost first, the passes.</summary>
        public IReadOnlyList<int> Rank(IReadOnlyDictionary<string, int> positions) =>
        [
            .. caller == null ? [] : new[] { caller.Order },
            entry.Order,
            .. parts.Items.Select(i => Position(i, positions)),
            .. parts.Passes.Select(p => p.Pass),
        ];

        /// <summary>The earliest dispatch (by the agent calls made before it) at which every necessary condition held.</summary>
        public int Earliest(
            IReadOnlyDictionary<string, int> stamps,
            IReadOnlyList<(KeyParts Parts, int Stamp)> rows,
            IReadOnlyDictionary<string, int> positions)
        {
            var earliest = 0;

            // what it and its enclosing nodes read, and what its call and the call's enclosing nodes read
            var readers = new List<(PlanEntry Reader, Context In)>();
            for (PlanEntry? at = entry; at != null; at = context.Parent(at))
                readers.Add((at, context));
            for (var at = caller; at != null; at = top.Parent(at))
                readers.Add((at, top));
            foreach (var (reader, body) in readers)
            {
                foreach (var depends in reader.Depends)
                {
                    if (!body.Map.TryGetValue(depends, out var read) || body.Encloses(reader, read))
                        continue;

                    // the instance standing for it from the reader's frame: the nearest of it and its parents visible there
                    var standing = read;
                    while (body.Level(standing) is { } level && !body.Encloses(level, reader))
                        standing = body.Parent(standing)!;
                    var loops = body.Loops(standing);
                    var key = RunKeys.Of(body.Rooted(standing.Key),
                        [.. parts.Items.Take(loops.Count(l => l.Kind == PlanKind.For))],
                        [.. parts.Passes.Take(loops.Count(l => l.Kind == PlanKind.While))]);
                    Assert.True(stamps.TryGetValue(key, out var stamp), $"{RunKeys.Of(parts.Rooted, parts.Items, parts.Passes)} reads {key}, which has no row.");
                    earliest = Math.Max(earliest, stamp);
                }
            }

            // each loop around it: a pass after the first waits for the pass before; a capped item for a place
            var all = context.Loops(entry);
            int items = 0, passes = 0;
            foreach (var loop in all)
            {
                var body = top.Map.TryGetValue(loop.Key, out var found) && ReferenceEquals(found, loop) ? top : context;
                var rooted = body.Rooted(loop.Key);
                if (loop.Kind == PlanKind.While)
                {
                    var pass = parts.Passes[passes].Pass;
                    if (pass > 1)
                    {
                        var before = rows.Where(r => r.Parts.Rooted.StartsWith(rooted + ".", StringComparison.Ordinal)
                                && Same(r.Parts, items, passes)
                                && r.Parts.Passes.Count > passes && r.Parts.Passes[passes].Pass == pass - 1)
                            .ToList();
                        Assert.NotEmpty(before);
                        earliest = Math.Max(earliest, before.Max(r => r.Stamp));
                    }

                    passes++;
                    continue;
                }

                var parallel = loop.Json["parallel"] is JsonValue p && p.TryGetValue<bool>(out var on) && on;
                int? cap = parallel ? (loop.Json["maxParallel"] is JsonValue m && m.TryGetValue<int>(out var max) ? max : null) : 1;
                if (cap is { } c)
                {
                    // the item rows of this loop instance before this item (ids are 0- or 1-based, or file stems: its
                    // place in the list is how many come before it); items open in list order while fewer than c are
                    // open, so at least place - c + 1 of those before it have settled
                    var own = Position(parts.Items[items], positions);
                    var earlier = rows.Where(r => r.Parts.Rooted == rooted && r.Parts.Items.Count == items + 1
                            && Same(r.Parts, items, passes) && r.Parts.Passes.Count == passes
                            && Position(r.Parts.Items[items], positions) < own)
                        .Select(r => r.Stamp).Order().ToList();
                    var needed = earlier.Count - c + 1;
                    if (needed > 0)
                        earliest = Math.Max(earliest, earlier[needed - 1]);
                }

                items++;
            }

            return earliest;
        }

        /// <summary>Whether <paramref name="other"/> has this call's first <paramref name="items"/> items and first <paramref name="passes"/> passes.</summary>
        private bool Same(KeyParts other, int items, int passes) =>
            other.Items.Count >= items && other.Items.Take(items).SequenceEqual(parts.Items.Take(items), StringComparer.Ordinal)
            && other.Passes.Count >= passes && other.Passes.Take(passes).SequenceEqual(parts.Passes.Take(passes));
    }
}
