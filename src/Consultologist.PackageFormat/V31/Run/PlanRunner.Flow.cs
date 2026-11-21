using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

public sealed partial class PlanRunner
{
    /// <summary>
    /// One while instance (part 04 § 3): <c>checkFirst</c> and <c>max</c> as written, each finished pass's record (what
    /// <c>pass.previous</c> and <c>.history</c> read), the pass running, the last pass's <c>ret</c> instance, and how it
    /// stopped.
    /// </summary>
    private sealed class WhileState(PlanEntry entry)
    {
        public bool CheckFirst { get; } = entry.Json["checkFirst"] is JsonValue first && first.GetValueKind() == JsonValueKind.True;

        public int? Max { get; } = Whole(entry.Json["max"]);

        /// <summary>Each finished pass, in order: <c>{ number, node, previous }</c>, <c>node</c> its body nodes' read forms by id.</summary>
        public List<JsonObject> Passes { get; } = [];

        public RunFrame? Running { get; set; }

        public RunInstance? Last { get; set; }

        public string? Stopped { get; set; }
    }

    private sealed partial class Run
    {
        /// <summary>Each while instance once started, by instance (reference equality: only looked up, never iterated).</summary>
        private readonly Dictionary<RunInstance, WhileState> whiles = new(ReferenceEqualityComparer.Instance);

        /// <summary>The body each <c>if</c> or <c>case</c> took and its frame, by instance (looked up only).</summary>
        private readonly Dictionary<RunInstance, (PlanBranch Branch, RunFrame Frame)> taken = new(ReferenceEqualityComparer.Instance);

        // ---- while --------------------------------------------------------------------------

        /// <summary>
        /// Moves a while instance on (part 04 §§ 3.2, 3.3): opens its first pass (with <c>checkFirst</c>, once its check
        /// holds), then, each time a pass's frame has finished, records the pass, decides its check after the pass (unless
        /// <c>checkFirst</c>) and its <c>until</c>, and opens the next pass (with <c>checkFirst</c>, once its check holds
        /// before it) until the check stops holding (<c>"check"</c>), the <c>until</c> holds (<c>"until"</c>) or <c>max</c>
        /// passes have run with the check still holding (<c>"bound"</c>). False when nothing moved.
        /// </summary>
        private async Task<bool> WhileAsync(RunInstance instance)
        {
            var entry = instance.Entry;
            if (!whiles.TryGetValue(instance, out var loop))
            {
                loop = new WhileState(entry);
                whiles[instance] = loop;
                if (entry.While == null || loop.Max is not >= 1 || entry.Ret == null)
                {
                    // part 04 § 3.1: the check, a bound of at least 1 and the ret are never null; publish writes them
                    await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                    return true;
                }

                await NextPassAsync(instance, loop);
                return true;
            }

            if (loop.Running is not { } frame || !frame.Instances.All(i => i.IsSettled))
                return false;

            loop.Running = null;
            var number = loop.Passes.Count + 1;
            if (frame.Instances.FirstOrDefault(i => i.Entry.Key == entry.Ret) is not { State: { } state } ret)
            {
                // RunPlan.Build refuses a ret that is not one of the while's own body nodes, each of which has an instance here.
                await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                return true;
            }

            loop.Last = ret;
            loop.Passes.Add(new JsonObject
            {
                ["number"] = number,
                ["node"] = PassNodes(instance, frame),
                ["previous"] = loop.Passes.LastOrDefault()?.DeepClone(),
            });
            await SettlePassAsync(instance, frame, number, state, ret);

            if (!loop.CheckFirst)
            {
                switch (await DecideAsync(instance, entry.While!, "while", $"pass.{Number(number)}", frame))
                {
                    case null:
                        return true;
                    case false:
                        await SettleWhileAsync(instance, loop, "check");
                        return true;
                }
            }

            if (entry.Until is { } until)
            {
                switch (await DecideAsync(instance, until, "until", $"until.{Number(number)}", frame))
                {
                    case null:
                        return true;
                    case true:
                        await SettleWhileAsync(instance, loop, "until");
                        return true;
                }
            }

            if (!loop.CheckFirst && number >= loop.Max)
            {
                await SettleWhileAsync(instance, loop, "bound");
                return true;
            }

            await NextPassAsync(instance, loop);
            return true;
        }

        /// <summary>
        /// Opens the next pass's frame with every body instance at once, binding <c>pass</c> (§ 3.3): <c>number</c>,
        /// <c>previous</c> (the last pass's record, <c>null</c> on the first) and <c>history</c> (every earlier pass's
        /// <c>{ number, node }</c>). With <c>checkFirst</c>, the check is decided first, over that frame: false settles the
        /// while at <c>"check"</c>, and true past <c>max</c> passes at <c>"bound"</c>.
        /// </summary>
        private async Task NextPassAsync(RunInstance instance, WhileState loop)
        {
            var entry = instance.Entry;
            var outer = instance.Frame;
            var number = loop.Passes.Count + 1;
            var binding = new JsonObject
            {
                ["number"] = number,
                ["previous"] = loop.Passes.LastOrDefault()?.DeepClone(),
                ["history"] = History(loop),
            };
            var frame = outer.Open(instance, outer.Map, outer.Function, outer.ItemPath, outer.ItemPositions,
                [.. outer.Passes, (outer.ItemPath.Count, number)], null, binding, null);

            if (loop.CheckFirst)
            {
                switch (await DecideAsync(instance, entry.While!, "while", $"pass.{Number(number)}", frame))
                {
                    case null:
                        return;
                    case false:
                        await SettleWhileAsync(instance, loop, "check");
                        return;
                }

                if (number > loop.Max)
                {
                    await SettleWhileAsync(instance, loop, "bound");
                    return;
                }
            }

            loop.Running = frame;
            foreach (var key in entry.Body)
            {
                if (outer.Map.TryGetValue(key, out var body))
                    Add(frame, body);
            }
        }

        /// <summary>Every finished pass as <c>.history</c> and <c>pass.history</c> hold it: <c>{ number, node }</c>, oldest first.</summary>
        private static JsonArray History(WhileState loop) =>
            new(loop.Passes.Select(p => (JsonNode?)new JsonObject { ["number"] = p["number"]!.DeepClone(), ["node"] = p["node"]!.DeepClone() }).ToArray());

        /// <summary>
        /// A pass's nodes by bare id (part 01: unique within a while at any depth), each one's read form
        /// (<c>{ value, content, state }</c>) as seen from the pass's frame: its body nodes and, through each <c>if</c> and
        /// <c>case</c> among them, every branch's nodes (one in a branch not taken reads <c>not_run</c>, a flow node there
        /// <c>skipped</c>); an inner loop is read whole, as its own node. Each node, then its branches' nodes, in body order.
        /// </summary>
        private static JsonObject PassNodes(RunInstance instance, RunFrame frame)
        {
            var nodes = new JsonObject();
            var pending = new Stack<string>(instance.Entry.Body.Reverse());
            while (pending.Count > 0)
            {
                var key = pending.Pop();
                if (frame.Map.GetValueOrDefault(key) is not { } entry || nodes.ContainsKey(entry.Id))
                    continue;
                nodes[entry.Id] = RunScope.Read(key, frame).DeepClone();
                if (entry.Kind is PlanKind.If or PlanKind.Case)
                {
                    foreach (var inner in entry.Branches.SelectMany(b => b.Nodes).Reverse())
                        pending.Push(inner);
                }
            }

            return nodes;
        }

        /// <summary>
        /// Decides <paramref name="rule"/> (the while's <paramref name="member"/>) over <paramref name="frame"/> and records
        /// the decision at <paramref name="site"/> (part 08 § 5.2); a failure fails the while with part 03 § 11.3's sentence
        /// and gives null.
        /// </summary>
        private async Task<bool?> DecideAsync(RunInstance instance, IJsonLogicRule rule, string member, string site, RunFrame frame)
        {
            var entry = instance.Entry;
            var decision = rule.Decide(scope.Data(entry, frame, entry.Reads));
            if (decision.Failure is { } failed)
            {
                await FailAsync(instance, ExpressionSentences.RunTime(Subject(instance), member, failed));
                return null;
            }

            await recorder.DecisionAsync(new RunDecision(instance.Key, site, entry.Printed(member), decision.Result, null, host.UtcNow));
            return decision.Result;
        }

        /// <summary>
        /// Records a pass's row (part 04 § 9), keyed <c>&lt;while key&gt;#&lt;n&gt;</c> at its place in the item path: its
        /// state and value are its <c>ret</c>'s, and it carries the pass's number and the item it runs in.
        /// </summary>
        private Task SettlePassAsync(RunInstance instance, RunFrame frame, int number, RowState state, RunInstance ret)
        {
            var content = state == RowState.Completed ? SourceLayout.Text(ret.Read?["content"]) : null;
            var detail = new JsonObject { ["function"] = instance.Frame.Function, ["depth"] = instance.Frame.Depth };
            ItemNameDetail(instance.Frame, detail);
            detail["pass"] = number;
            return recorder.RowAsync(new RunRow(RunKeys.Of(instance.Entry.Key, frame.ItemPath, frame.Passes), instance.Entry.Key,
                PlanKind.While, state, state == RowState.Completed ? null : ret.Reason, content == null ? null : CanonicalJson.HashText(content), detail));
        }

        /// <summary>
        /// Settles the while (§§ 3.2, 3.4): <c>.value</c> and <c>.content</c> its last pass's <c>ret</c>'s, <c>.passes</c>,
        /// <c>.stopped</c> and <c>.history</c>. With no pass run it reads as skipped, its reason S03-001's form of its check.
        /// </summary>
        private Task SettleWhileAsync(RunInstance instance, WhileState loop, string stopped)
        {
            loop.Stopped = stopped;
            if (loop.Last is not { } last)
            {
                var skipped = RunScope.Absent("skipped");
                skipped["passes"] = 0;
                skipped["stopped"] = stopped;
                skipped["history"] = new JsonArray();
                return SettleAsync(instance, RowState.Skipped, JsonLogicPrinter.Reason("skipped", instance.Entry.Json["while"], false), skipped, null);
            }

            var content = SourceLayout.Text(last.Read?["content"]);
            var read = new JsonObject
            {
                ["value"] = last.Read?["value"]?.DeepClone(),
                ["content"] = content,
                ["state"] = "completed",
                ["passes"] = loop.Passes.Count,
                ["stopped"] = stopped,
                ["history"] = History(loop),
            };
            return SettleAsync(instance, RowState.Completed, null, read, content == null ? null : CanonicalJson.HashText(content));
        }

        /// <summary>
        /// A while row's detail (part 04 § 9): <c>max</c>, <c>checkFirst</c>, the <c>passes</c> run, <c>stopped</c>, and its
        /// <c>reason</c>: S04-005 at its bound, else null.
        /// </summary>
        private void WhileDetail(RunInstance instance, JsonObject detail)
        {
            var loop = whiles.GetValueOrDefault(instance) ?? new WhileState(instance.Entry);
            detail["max"] = loop.Max;
            detail["checkFirst"] = loop.CheckFirst;
            detail["passes"] = loop.Passes.Count;
            detail["stopped"] = loop.Stopped;
            detail["reason"] = loop is { Stopped: "bound", Max: { } max }
                ? $"stopped at its bound of {Count(max)}; the check never stopped holding"
                : null;
        }

        // ---- if and case --------------------------------------------------------------------

        /// <summary>
        /// Moves an <c>if</c> or a <c>case</c> on (part 04 § 5): once ready it decides, records the decision and opens the
        /// body it took with every instance at once (an empty one settles it skipped); once that body's frame has finished
        /// it settles as its <c>ret</c>: the same state, reason and read form. False when nothing moved.
        /// </summary>
        private async Task<bool> BranchAsync(RunInstance instance)
        {
            if (!taken.TryGetValue(instance, out var open))
            {
                var chosen = instance.Entry.Kind == PlanKind.If ? await IfAsync(instance) : await CaseAsync(instance);
                if (chosen == null)
                    return true; // settled skipped, or failed

                var frame = instance.Frame.OpenBranch(instance);
                taken[instance] = (chosen, frame);
                foreach (var key in chosen.Nodes)
                {
                    if (frame.Map.TryGetValue(key, out var body))
                        Add(frame, body);
                }

                return true;
            }

            if (!open.Frame.Instances.All(i => i.IsSettled))
                return false;

            // RunPlan.Build refuses a ret that is not one of its body's nodes, each of which has an instance here.
            if (open.Frame.Instances.FirstOrDefault(i => i.Entry.Key == open.Branch.Ret) is not { State: { } state, Read: { } read } ret)
            {
                await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                return true;
            }

            var content = SourceLayout.Text(read["content"]);
            await SettleAsync(instance, state, state == RowState.Completed ? null : ret.Reason, read.DeepClone().AsObject(),
                content == null ? null : CanonicalJson.HashText(content));
            return true;
        }

        /// <summary>
        /// Decides an <c>if</c> (§ 5.1) and records it (<c>self</c>): its then when it holds, else its else; with no else, a
        /// false or <c>null</c> skips it with S03-001's reason. Null when it settled or failed.
        /// </summary>
        private async Task<PlanBranch?> IfAsync(RunInstance instance)
        {
            var entry = instance.Entry;
            if (entry.If is not { } rule || entry.Branches.Count != 2)
            {
                await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                return null;
            }

            var decision = rule.Decide(scope.Data(entry, instance.Frame, entry.Reads));
            if (decision.Failure is { } failed)
            {
                await FailAsync(instance, ExpressionSentences.RunTime(Subject(instance), "if", failed));
                return null;
            }

            await recorder.DecisionAsync(new RunDecision(instance.Key, "self", entry.Printed("if"), decision.Result, null, host.UtcNow));
            var chosen = entry.Branches[decision.Result ? 0 : 1];
            if (chosen.Nodes.Count > 0)
                return chosen;

            await SettleAsync(instance, RowState.Skipped, JsonLogicPrinter.Reason("skipped", entry.Json["if"], false), RunScope.Absent("skipped"), null);
            return null;
        }

        /// <summary>
        /// Decides a <c>case</c> (§ 5.2) and records it (<c>case</c>, the branch taken: its 1-based position, or
        /// <c>else</c>): the branch whose <c>when</c> holds the subject's value; <c>null</c> or a value no branch lists goes to
        /// the else. An empty branch skips it with S04-006 (the value is the package's own enum value); an empty else, which
        /// publish allows only when the branches list every value, fails it with A06-007 (a value that escaped its format,
        /// never quoted). Null when it settled or failed.
        /// </summary>
        private async Task<PlanBranch?> CaseAsync(RunInstance instance)
        {
            var entry = instance.Entry;
            var subject = SourceLayout.Text(entry.Json["case"]) ?? "";
            var value = At(scope.Data(entry, instance.Frame, [subject]), subject);

            int? matched = null;
            JsonNode? listed = null;
            for (var i = 0; i < entry.Branches.Count - 1 && matched == null && value is not null; i++)
            {
                listed = entry.Branches[i].When?.FirstOrDefault(w => JsonNode.DeepEquals(w, value));
                if (listed != null)
                    matched = i;
            }

            var branch = matched is { } at ? Number(at + 1) : "else";
            await recorder.DecisionAsync(new RunDecision(instance.Key, "case", subject, null, branch, host.UtcNow));
            var chosen = entry.Branches[matched ?? entry.Branches.Count - 1];
            if (chosen.Nodes.Count > 0)
                return chosen;

            if (matched == null)
            {
                await FailAsync(instance, NoBranch(entry, subject));
                return null;
            }

            var word = listed is JsonValue text && text.GetValueKind() == JsonValueKind.String ? text.GetValue<string>() : CanonicalJson.Write(listed!);
            await SettleAsync(instance, RowState.Skipped, $"case '{entry.Id}': arm for {word} does nothing", RunScope.Absent("skipped"), null);
            return null;
        }

        /// <summary>A06-007: a case subject's value that no branch lists, with no else (a value that escaped its format); never the value.</summary>
        private static string NoBranch(PlanEntry entry, string subject) =>
            $"Case '{entry.Id}' was given {subject}, which no branch lists, and it has no else.";

        /// <summary>A count as a site writes it: invariant digits.</summary>
        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
