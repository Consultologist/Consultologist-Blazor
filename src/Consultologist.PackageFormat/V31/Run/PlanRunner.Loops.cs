using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

public sealed partial class PlanRunner
{
    /// <summary>
    /// One item of a loop instance: its position in the list (0-based) and id (§ 2.2), its source element once known (a
    /// list's element, a folder item's header data and body, a production item's <c>.items</c> element), its name (§ 2.7),
    /// its frame once opened, and once settled its state and its <c>.items</c> element (§ 2.6).
    /// </summary>
    private sealed class LoopItem(int position, string id)
    {
        public int Position { get; } = position;

        public string Id { get; } = id;

        /// <summary>Whether its source element is known: at once, or for a loop over <c>.items</c> when the production item settles.</summary>
        public bool Available { get; set; }

        public JsonNode? Value { get; set; }

        /// <summary>A snippet folder item's body (<c>item.content</c>), when <see cref="HasContent"/>.</summary>
        public string? Content { get; set; }

        public bool HasContent { get; set; }

        public string Name { get; set; } = "";

        /// <summary>Whether <see cref="Name"/> is the position name (<c>&lt;loop label&gt; &lt;n&gt;</c>).</summary>
        public bool PositionName { get; set; }

        /// <summary>#1506 (area 09; D2): whether it is a snippet folder's item, whose name comes from its header: package text.</summary>
        public bool FromFolder { get; init; }

        /// <summary>
        /// #1506 (area 09; part 08 § 4; D2): whether <see cref="Name"/> is package text (a position name, of the loop's label,
        /// or a snippet folder item's header), so the row keeps it; else it is drawn from an input or a model and the row
        /// keeps its hash.
        /// </summary>
        public bool NameFromPackage => PositionName || FromFolder;

        public RunFrame? Frame { get; set; }

        public RowState? State { get; set; }

        public JsonObject? Element { get; set; }

        public bool IsSettled => State != null;

        public bool IsOpenOrSettled => IsSettled || Frame != null;
    }

    /// <summary>
    /// One loop instance (part 04 § 2): its members as written, its items, the production loop it follows when it iterates
    /// that loop's <c>.items</c>, why it has none when its source is <c>null</c>, where its <c>until</c> held, how it stopped,
    /// the items open (a frame opened, not yet settled), the first item neither open nor settled, and how many settled.
    /// </summary>
    private sealed class LoopState(PlanEntry entry)
    {
        public bool Parallel { get; } = entry.Json["parallel"] is JsonValue parallel && parallel.GetValueKind() == JsonValueKind.True;

        public int? MaxParallel { get; } = Whole(entry.Json["maxParallel"]);

        public int? Max { get; } = Whole(entry.Json["max"]);

        public string? ItemLabel { get; } = SourceLayout.Text(entry.Json["itemLabel"]);

        public string Label { get; } = SourceLayout.Text(entry.Json["label"]) ?? entry.Id;

        /// <summary>The items the source gave (beyond <see cref="RunLimits.LoopItems"/> none is made).</summary>
        public int Count { get; set; }

        public List<LoopItem> Items { get; } = [];

        public LoopState? Production { get; set; }

        public string? NullReason { get; set; }

        /// <summary>The 1-based position of the item after which its <c>until</c> held, or null.</summary>
        public int? StopAt { get; set; }

        public string? Stopped { get; set; }

        public List<LoopItem> Open { get; } = [];

        public int Next { get; set; }

        public int Settled { get; set; }
    }

    /// <summary>An integer member, or null.</summary>
    private static int? Whole(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var whole) ? whole : null;

    private sealed partial class Run
    {
        /// <summary>Each loop instance once its source is read, by instance (reference equality: only looked up, never iterated).</summary>
        private readonly Dictionary<RunInstance, LoopState> loops = new(ReferenceEqualityComparer.Instance);

        /// <summary>Each loop item's frame and its item, by frame (reference equality: only looked up, never iterated).</summary>
        private readonly Dictionary<RunFrame, LoopItem> itemFrames = new(ReferenceEqualityComparer.Instance);

        /// <summary>The key of the loop whose <c>.items</c> an entry iterates, or null, by entry (looked up only).</summary>
        private readonly Dictionary<PlanEntry, string?> itemsSources = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Whether <paramref name="instance"/> is ready (<see cref="RunInstance.IsReady"/>). A loop over a visible loop's
        /// <c>.items</c> does not wait for that loop to settle: it follows it item by item (part 06 § 3.2), and
        /// <see cref="LoopAsync"/> waits until that loop has opened its items.
        /// </summary>
        private bool Ready(RunInstance instance) =>
            instance.Entry.Kind == PlanKind.For && ItemsSource(instance) is { } key && instance.Frame.Find(key) != null
                ? instance.IsReady(except: key)
                : instance.IsReady();

        /// <summary>The key of the loop whose <c>.items</c> <paramref name="instance"/>'s <c>in</c> names (<c>node.&lt;l&gt;.items</c>), or null.</summary>
        private string? ItemsSource(RunInstance instance)
        {
            var entry = instance.Entry;
            if (itemsSources.TryGetValue(entry, out var known))
                return known;

            string? key = null;
            var map = instance.Frame.Map;
            if (SourceLayout.Text(entry.Json["in"]) is { } reference
                && reference.EndsWith(".items", StringComparison.Ordinal)
                && !map.ContainsKey(reference)
                && map.TryGetValue(reference[..^".items".Length], out var production)
                && production.Kind == PlanKind.For)
            {
                key = production.Key;
            }

            itemsSources[entry] = key;
            return key;
        }

        /// <summary>
        /// Moves a loop instance on (part 04 §§ 2.2 to 2.7): reads its source the first time, then settles the items whose
        /// frames have finished (deciding its <c>until</c> after each, when sequential), marks <c>not_run</c> the items beyond
        /// its <c>max</c> or after its <c>until</c> held, opens the next items' frames (every body instance at once), and
        /// settles the loop when every item has. False when nothing moved.
        /// </summary>
        private async Task<bool> LoopAsync(RunInstance instance)
        {
            var moved = false;
            if (!loops.TryGetValue(instance, out var loop))
            {
                var (read, failed) = MakeLoop(instance);
                if (read == null)
                    return false; // the loop whose .items it iterates has not opened its items
                loops[instance] = read;
                if (failed != null)
                {
                    await FailAsync(instance, failed);
                    return true;
                }

                loop = read;
                moved = true;
            }

            if (loop.Production is { } production)
                moved |= Arrive(loop, production);
            moved |= await SettleItemsAsync(instance, loop);
            if (failure != null)
                return true;
            moved |= await AdvanceAsync(instance, loop);
            if (loop.Settled == loop.Items.Count)
            {
                await SettleLoopAsync(instance, loop);
                return true;
            }

            return moved;
        }

        /// <summary>
        /// The loop instance with its source read (§ 2.2), or null to wait (it iterates a loop's <c>.items</c> that has not
        /// opened its items), with a failure when the source is not a list (A06-006) or longer than
        /// <see cref="RunLimits.LoopItems"/> (S04-078). <c>null</c> gives 0 items and S04-077's reason.
        /// </summary>
        private (LoopState? Loop, string? Failure) MakeLoop(RunInstance instance)
        {
            var entry = instance.Entry;
            var frame = instance.Frame;
            var loop = new LoopState(entry);
            var reference = SourceLayout.Text(entry.Json["in"]) ?? "";

            if (ItemsSource(instance) is { } key && frame.Find(key) is { } source)
            {
                if (loops.TryGetValue(source, out var production))
                {
                    loop.Count = production.Items.Count;
                    if (TooMany(instance, loop) is { } many)
                        return (loop, many);
                    loop.Production = production;
                    foreach (var item in production.Items)
                        loop.Items.Add(new LoopItem(item.Position, item.Id));
                    return (loop, null);
                }

                if (!source.IsSettled)
                    return (null, null);
            }

            List<(string Id, JsonNode? Value, string? Content, bool HasContent)> given;
            if (reference.StartsWith("snippet.", StringComparison.Ordinal))
            {
                if (SnippetItems(frame, reference) is not { } items)
                    return (loop, NotAList(entry, reference));
                given = items;
            }
            else
            {
                var value = At(scope.Data(entry, frame, [reference]), reference);
                if (value is null)
                {
                    loop.NullReason = $"0 items: {reference} is null";
                    return (loop, null);
                }

                if (value is not JsonArray list)
                    return (loop, NotAList(entry, reference));

                // #1506 (R3): item ids are 1-based everywhere, an array input's included.
                given = list.Select((element, i) => ((i + 1).ToString(CultureInfo.InvariantCulture), element, (string?)null, false)).ToList();
            }

            loop.Count = given.Count;
            if (TooMany(instance, loop) is { } tooMany)
                return (loop, tooMany);
            for (var i = 0; i < given.Count; i++)
            {
                var (id, element, content, hasContent) = given[i];
                var item = new LoopItem(i, id) { Available = true, Value = element, Content = content, HasContent = hasContent, FromFolder = hasContent };
                Name(loop, item, element);
                loop.Items.Add(item);
            }

            return (loop, null);
        }

        /// <summary>S04-078 when the loop was given more than <see cref="RunLimits.LoopItems"/> items, else null.</summary>
        private string? TooMany(RunInstance instance, LoopState loop) =>
            loop.Count > limits.LoopItems
                ? $"Loop '{instance.Entry.Id}' was given {Count(loop.Count)} items; this engine runs at most {Count(limits.LoopItems)} per loop."
                : null;

        /// <summary>A06-006: a source that is not a list at run time (a value that escaped its format); never the value.</summary>
        private static string NotAList(PlanEntry entry, string reference) => $"Loop '{entry.Id}' was given {reference}, which isn't a list.";

        /// <summary>
        /// The items of the snippet folder <paramref name="reference"/> (<c>snippet.&lt;folder&gt;</c>, the function's own,
        /// else the package's): ids the file stems in written order, each value the header data (the members its folder's
        /// format declares), each content its body; null when the folder is not there.
        /// </summary>
        private List<(string Id, JsonNode? Value, string? Content, bool HasContent)>? SnippetItems(RunFrame frame, string reference)
        {
            JsonObject? folder;
            Func<string, JsonObject?> find;
            if (frame.Function is { } id && plan.Functions.TryGetValue(id, out var function)
                && function.Json["snippets"] is JsonObject own && own[reference] is JsonObject found)
            {
                folder = found;
                find = key => own[key] as JsonObject;
            }
            else
            {
                folder = plan.Snippets.GetValueOrDefault(reference);
                find = key => plan.Snippets.GetValueOrDefault(key);
            }

            if (folder?["items"] is not JsonArray ids)
                return null;
            var fields = (plan.Formats.Expand(folder["format"]) as JsonObject)?["properties"] as JsonObject;
            var items = new List<(string, JsonNode?, string?, bool)>(ids.Count);
            foreach (var stem in ids)
            {
                if (SourceLayout.Text(stem) is not { } itemId || find($"{reference}.{itemId}") is not { } file)
                    return null;
                var value = new JsonObject();
                foreach (var (field, _) in fields ?? new JsonObject())
                {
                    if (file.TryGetPropertyValue(field, out var data))
                        value[field] = data?.DeepClone();
                }

                items.Add((itemId, value, SourceLayout.Text(file["body"]), true));
            }

            return items;
        }

        /// <summary>The value at <paramref name="path"/> in <paramref name="data"/> (<see cref="RunScope.Data"/> places it there).</summary>
        private static JsonNode? At(JsonObject data, string path)
        {
            JsonNode? at = data;
            foreach (var segment in path.Split('.'))
                at = (at as JsonObject)?[segment];
            return at;
        }

        /// <summary>
        /// Names <paramref name="item"/> (§ 2.7): its element's <c>itemLabel</c> field as 30 names an item from a field
        /// (<see cref="WorkflowItemNames.FromField"/>: trimmed, and when longer than
        /// <see cref="WorkflowItemNames.MaxLength"/> characters cut, trimmed again and ended "…", a surrogate pair never
        /// split; #1508, 2026-11-25, item 4); with <c>itemLabel</c> null, or a field empty after trimming (or not text),
        /// its position name.
        /// </summary>
        private static void Name(LoopState loop, LoopItem item, JsonNode? element)
        {
            var drawn = loop.ItemLabel is { } field && element is JsonObject row
                && row[field] is JsonValue text && text.GetValueKind() == JsonValueKind.String
                ? WorkflowItemNames.FromField(text.GetValue<string>())
                : null;
            if (string.IsNullOrEmpty(drawn))
            {
                item.Name = PositionName(loop, item.Position);
                item.PositionName = true;
                return;
            }

            item.Name = drawn;
        }

        /// <summary>S04-004's form: the loop's label and the 1-based position (<c>Sections 2</c>). Reasons always use it.</summary>
        private static string PositionName(LoopState loop, int position) =>
            $"{loop.Label} {(position + 1).ToString(CultureInfo.InvariantCulture)}";

        /// <summary>For a loop over <c>.items</c>: each item whose production item has settled becomes available, its element that item's.</summary>
        private static bool Arrive(LoopState loop, LoopState production)
        {
            var moved = false;
            for (var i = loop.Next; i < loop.Items.Count; i++)
            {
                var item = loop.Items[i];
                if (item.Available || production.Items[i].Element is not { } element)
                    continue;
                item.Available = true;
                item.Value = element;
                Name(loop, item, element);
                moved = true;
            }

            return moved;
        }

        /// <summary>
        /// Settles, in list order, each open item whose frame has finished: its state and value are its <c>ret</c>'s. A
        /// sequential loop then decides its <c>until</c> over that item's frame, recording the decision (<c>until.&lt;n&gt;</c>).
        /// </summary>
        private async Task<bool> SettleItemsAsync(RunInstance instance, LoopState loop)
        {
            var entry = instance.Entry;
            var moved = false;
            foreach (var item in loop.Open.OrderBy(i => i.Position).ToList())
            {
                var frame = item.Frame!;
                if (!frame.Instances.All(i => i.IsSettled))
                    continue;

                loop.Open.Remove(item);

                // RunPlan.Build refuses a ret that is not one of the loop's own body nodes, and every body node gets an
                // instance in the item's frame; a for whose ret is null is not what publish writes (part 04 § 2.1: never
                // null). Either is the document's fault, said as S07-006, never an item completed with no value.
                var ret = entry.Ret is { } key ? frame.Instances.FirstOrDefault(i => i.Entry.Key == key) : null;
                if (ret is not { State: { } state })
                {
                    await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                    return true;
                }

                await SettleItemAsync(instance, loop, item, state, state == RowState.Completed ? null : ret.Reason, ret.Read);
                moved = true;

                if (loop.Parallel || entry.Until is not { } until || loop.StopAt != null)
                    continue;
                var data = scope.Data(entry, frame, entry.Reads);
                var decision = await DecideRuleAsync(instance, until, "until", data);
                if (decision.Failure is { } failed)
                {
                    await FailAsync(instance, RuleFailed(Subject(instance), "until", failed));
                    return true;
                }

                var position = item.Position + 1;
                await RecordDecisionAsync(instance, frame, $"until.{position.ToString(CultureInfo.InvariantCulture)}",
                    entry.Rules.GetValueOrDefault("until"), entry.Printed("until"), data, decision.Result, null);
                if (decision.Result)
                    loop.StopAt = position;
            }

            return moved;
        }

        /// <summary>
        /// Walks the items not yet open or settled, in list order: one beyond <c>max</c> (S04-002) or after the <c>until</c>
        /// held (S04-001) settles <c>not_run</c>; the next is opened while the loop allows: every item at once when
        /// <c>parallel</c>, at most <c>maxParallel</c> open at a time in this loop instance, or one at a time when sequential.
        /// </summary>
        private async Task<bool> AdvanceAsync(RunInstance instance, LoopState loop)
        {
            var moved = false;
            for (var i = loop.Next; i < loop.Items.Count; i++)
            {
                var item = loop.Items[i];
                if (item.IsOpenOrSettled)
                    continue;
                if (!item.Available)
                {
                    if (!loop.Parallel)
                        break; // items run in list order
                    continue;
                }

                if (NotRunReason(loop, item) is { } reason)
                {
                    await SettleItemAsync(instance, loop, item, RowState.NotRun, reason, null);
                    moved = true;
                    continue;
                }

                var full = loop.Parallel ? loop.MaxParallel is { } cap && loop.Open.Count >= cap : loop.Open.Count > 0;
                if (full)
                {
                    if (!loop.Parallel || loop.Max == null)
                        break; // nothing further on can be marked not run yet
                    continue;
                }

                if (!await OpensAsync(instance))
                    return moved;
                await Open(instance, loop, item);
                moved = true;
            }

            while (loop.Next < loop.Items.Count && loop.Items[loop.Next].IsOpenOrSettled)
                loop.Next++;
            return moved;
        }

        /// <summary>Why an available item will not run: beyond <c>max</c> (S04-002), or after the <c>until</c> held (S04-001, with the position name); else null.</summary>
        private static string? NotRunReason(LoopState loop, LoopItem item)
        {
            if (loop.Max is { } max && item.Position >= max)
                return BeyondMax(max);
            if (loop.StopAt is { } at)
                return StoppedEarly(loop, at);
            return null;
        }

        /// <summary>S04-002: <c>not run: beyond max &lt;n&gt;</c>.</summary>
        private static string BeyondMax(int max) => $"not run: beyond max {Count(max)}";

        /// <summary>S04-001 with the position name of the item (1-based <paramref name="at"/>) after which the <c>until</c> held, never a drawn name.</summary>
        private static string StoppedEarly(LoopState loop, int at) => $"stopped early at {PositionName(loop, at - 1)}";

        /// <summary>
        /// What <c>item</c> reads for <paramref name="item"/> in its frame, and what its <c>.items</c> element holds as
        /// <c>item</c> (part 04 § 2.6, "an item, as above"): <c>{ id, value[, content] }</c>, or for a loop over <c>.items</c>
        /// the production item's element with its <c>id</c>. The frame's binding adds <c>parent</c>.
        /// </summary>
        private static JsonObject ItemOf(LoopState loop, LoopItem item)
        {
            if (loop.Production != null && item.Value is JsonObject element)
            {
                var chained = element.DeepClone().AsObject();
                chained["id"] = item.Id;
                return chained;
            }

            var plain = new JsonObject { ["id"] = item.Id, ["value"] = item.Value?.DeepClone() };
            if (item.HasContent)
                plain["content"] = item.Content;
            return plain;
        }

        /// <summary>
        /// Opens <paramref name="item"/>'s frame in the loop's frame, with every body instance at once, binding <c>item</c>:
        /// <c>{ id, value[, content], parent }</c>, or for a loop over <c>.items</c> the production item's element with its
        /// <c>id</c> and <c>parent</c>; <c>parent</c> is the enclosing loop's item (part 04 § 2.6).
        /// </summary>
        private async Task Open(RunInstance instance, LoopState loop, LoopItem item)
        {
            var frame = instance.Frame;
            var binding = ItemOf(loop, item);
            if (frame.Binding(f => f.Item != null)?.Item is { } parent)
                binding["parent"] = parent.DeepClone();

            item.Frame = frame.Open(instance, frame.Map, frame.Function, [.. frame.ItemPath, item.Id], [.. frame.ItemPositions, item.Position],
                frame.Passes, binding, null, null);
            item.Frame.OpenedAtUtc = host.UtcNow;
            loop.Open.Add(item);
            itemFrames[item.Frame] = item;
            foreach (var key in instance.Entry.Body)
            {
                if (frame.Map.TryGetValue(key, out var body))
                    await Add(item.Frame, body);
            }
        }

        /// <summary>
        /// Settles <paramref name="item"/> as <paramref name="state"/> and records its row (keyed <c>&lt;loop key&gt;:&lt;path&gt;</c>,
        /// named per § 2.7): its <c>.items</c> element is <c>{ item, label, state, reason, value, content, nodes }</c>.
        /// </summary>
        private Task SettleItemAsync(RunInstance instance, LoopState loop, LoopItem item, RowState state, string? reason, JsonObject? ret)
        {
            item.State = state;
            loop.Settled++;
            var completed = state == RowState.Completed;
            var content = completed ? SourceLayout.Text(ret?["content"]) : null;
            item.Element = new JsonObject
            {
                ["item"] = ItemOf(loop, item),
                ["label"] = item.Name,
                ["state"] = StateWord(state),
                ["reason"] = reason,
                ["value"] = completed ? ret?["value"]?.DeepClone() : null,
                ["content"] = content,
                ["nodes"] = Nodes(instance, item),
            };

            var detail = new JsonObject { ["function"] = instance.Frame.Function, ["depth"] = instance.Frame.Depth };
            EntryDetail(instance.Entry, detail);
            ClockDetail(detail, state, item.Frame?.OpenedAtUtc);
            var name = NameDetail(item, detail);
            var settled = new JsonObject { ["value"] = completed ? ret?["value"]?.DeepClone() : null, ["content"] = content };
            var key = RunKeys.Of(instance.Frame.Rooted(instance.Entry.Key), [.. instance.Frame.ItemPath, item.Id], instance.Frame.Passes);
            var text = SettledText(settled, name, null);
            if (IsCheck(key))
            {
                // #1506 (area 09; part 08 § 6): a check loop's item row is a check row: aligned with its production item, its
                // check_result's members, and the loop's effect; what it checked and read are on the check rows inside it
                detail["alignedWith"] = AlignedWith(instance, item);
                // the item's value is its ret's, so its failWith is the literal in the ret's expression
                var producer = instance.Entry.Ret is { } retKey ? item.Frame?.Instances.FirstOrDefault(i => i.Entry.Key == retKey)?.Entry : null;
                text = WithLists(text, RecordRows.CheckResult(settled["value"], detail, producer ?? instance.Entry));
                detail["effect"] = ReadBy(instance.Entry, instance.Frame.Map);
            }

            return recorder.RowAsync(new RunRow(key, instance.Entry.Key, PlanKind.For, state, reason,
                content == null ? null : CanonicalJson.HashText(content), detail, text));
        }

        /// <summary>
        /// #1506 (area 09; part 08 § 6; part 06 § 3.1): the <c>alignedWith</c> of a row written in <paramref name="frame"/>: for
        /// a row inside an item of a loop over a production loop's <c>.items</c> (the innermost item it runs in), the row key
        /// of the production item with the same id (<see cref="AlignedWith(RunInstance, LoopItem)"/>); else <c>null</c>.
        /// </summary>
        private string? AlignedWith(RunFrame frame)
        {
            for (var at = frame; at != null; at = at.Parent)
            {
                if (itemFrames.TryGetValue(at, out var item))
                    return at.Owner is { } loop ? AlignedWith(loop, item) : null;
            }

            return null;
        }

        /// <summary>
        /// #1506 (area 09; part 06 § 3.1): the row key of the production item <paramref name="item"/> of the loop
        /// <paramref name="instance"/> lines up with (<c>node.sections:2</c> for item 2 of a loop over
        /// <c>node.sections.items</c>): the production loop's instance key with the item's id, as <see cref="RunKeys.Of"/>
        /// writes it; <c>null</c> when the loop does not iterate a production loop's <c>.items</c>.
        /// </summary>
        private string? AlignedWith(RunInstance instance, LoopItem item)
        {
            if (!loops.TryGetValue(instance, out var loop) || loop.Production == null
                || ItemsSource(instance) is not { } source || instance.Frame.Find(source) is not { } production)
            {
                return null;
            }

            var frame = production.Frame;
            return RunKeys.Of(frame.Rooted(production.Entry.Key), [.. frame.ItemPath, item.Id], frame.Passes);
        }

        /// <summary>Each body node of an item by bare id: its <c>value</c>, <c>content</c> and <c>state</c> (<c>not_run</c> when the item never ran).</summary>
        private static JsonObject Nodes(RunInstance instance, LoopItem item)
        {
            var nodes = new JsonObject();
            foreach (var key in instance.Entry.Body)
            {
                var id = instance.Frame.Map.GetValueOrDefault(key)?.Id ?? key;
                var read = item.Frame?.Instances.FirstOrDefault(i => i.Entry.Key == key)?.Read;
                nodes[id] = new JsonObject
                {
                    ["value"] = read?["value"]?.DeepClone(),
                    ["content"] = read?["content"]?.DeepClone(),
                    ["state"] = read?["state"]?.DeepClone() ?? RunScope.NotRun,
                };
            }

            return nodes;
        }

        /// <summary>
        /// Settles the loop (§§ 2.4, 2.5): <c>.value</c> its completed items' values, <c>.items</c> every item's element,
        /// <c>.content</c> D11's rendering (<c>## &lt;item name&gt;</c>, a blank line, the item's text; completed items only,
        /// joined by a blank line), and <c>.stopped</c>: <c>until</c>, <c>bound</c> (items beyond <c>max</c>) or <c>end</c>.
        /// </summary>
        private Task SettleLoopAsync(RunInstance instance, LoopState loop)
        {
            loop.Stopped = loop.StopAt != null ? "until" : loop.Max is { } max && loop.Items.Count > max ? "bound" : "end";
            var completed = loop.Items.Where(i => i.State == RowState.Completed).ToList();
            var content = string.Join("\n\n", completed.Select(i => $"## {i.Name}\n\n{SourceLayout.Text(i.Element!["content"]) ?? ""}"));
            var read = new JsonObject
            {
                ["value"] = new JsonArray(completed.Select(i => i.Element!["value"]?.DeepClone()).ToArray()),
                ["items"] = new JsonArray(loop.Items.Select(i => (JsonNode?)i.Element!.DeepClone()).ToArray()),
                ["content"] = content,
                ["state"] = "completed",
                ["stopped"] = loop.Stopped,
            };
            return SettleAsync(instance, RowState.Completed, null, read, CanonicalJson.HashText(content));
        }

        /// <summary>
        /// A loop row's detail (part 04 § 9, part 08 § 4): <c>parallel</c>, <c>maxParallel</c>, <c>itemLabel</c>'s field, the
        /// <c>count</c> of items given, <c>stopped</c>, and its <c>reason</c>: S04-077 for a <c>null</c> source, the not-run
        /// items' reason when it stopped at its <c>until</c> or its <c>max</c>, else null.
        /// </summary>
        private void LoopDetail(RunInstance instance, JsonObject detail)
        {
            var read = loops.GetValueOrDefault(instance);
            var loop = read ?? new LoopState(instance.Entry); // a loop that never read its source has its members as written
            detail["parallel"] = loop.Parallel;
            detail["maxParallel"] = loop.MaxParallel;
            detail["itemLabel"] = loop.ItemLabel;
            detail["count"] = read?.Count;
            detail["stopped"] = loop.Stopped;
            detail["reason"] = loop switch
            {
                { NullReason: { } nullReason } => nullReason,
                { Stopped: "until", StopAt: { } at } => StoppedEarly(loop, at),
                { Stopped: "bound", Max: { } max } => BeyondMax(max),
                _ => null,
            };
        }

        /// <summary>
        /// Part 08 § 4's <c>itemName</c> on a row written in <paramref name="frame"/>: the name of the innermost loop item it
        /// runs in (through any frame between, a while's or a call's), with whether it is the position name; <c>null</c> and
        /// <c>false</c> outside every loop. #1506 (D2): <see cref="NameDetail"/>'s, the name given back when the row keeps only
        /// its hash.
        /// </summary>
        private string? ItemNameDetail(RunFrame frame, JsonObject detail)
        {
            LoopItem? item = null;
            for (var at = frame; at != null && item == null; at = at.Parent)
                item = itemFrames.GetValueOrDefault(at);
            return NameDetail(item, detail);
        }

        /// <summary>
        /// #1506 (area 09; part 08 § 4; D2): <paramref name="item"/>'s name on a row: <c>itemName</c> the name when it is package
        /// text (<see cref="LoopItem.NameFromPackage"/>), else <c>null</c> with <c>itemNameHash</c> the name's hash, the name
        /// then given back for the settled text; <c>positionName</c>; all <c>null</c> (and <c>false</c>) for no item.
        /// </summary>
        private static string? NameDetail(LoopItem? item, JsonObject detail)
        {
            var kept = item is { NameFromPackage: true };
            detail["itemName"] = kept ? item!.Name : null;
            detail["itemNameHash"] = item == null || kept ? null : CanonicalJson.HashText(item.Name);
            detail["positionName"] = item?.PositionName ?? false;
            return item == null || kept ? null : item.Name;
        }
    }

    /// <summary>A row state as a reader reads it (part 04 § 2.6); one arm per state, so a state added later is never read as another.</summary>
    internal static string StateWord(RowState state) => state switch
    {
        RowState.Completed => "completed",
        RowState.Skipped => "skipped",
        RowState.NotRun => RunScope.NotRun,
        RowState.Failed => "failed",
        RowState.Cancelled => "cancelled",
        RowState.Released => "released",
        RowState.Held => "held",
        RowState.Blocked => "blocked",
        _ => state.ToString(),
    };
}
