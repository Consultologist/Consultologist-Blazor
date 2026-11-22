using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Run;

public sealed partial class PlanRunner
{
    private sealed partial class Run
    {
        /// <summary>Each deliverable's outcome once decided, with its entry's place in <c>graph.order</c>.</summary>
        private readonly List<(int Order, DeliverableOutcome Outcome)> outcomes = [];

        /// <summary>
        /// #1505 (specVersion 31, area 08; part 06 §§ 6.4, 6.6, 7; D5, D8): a deliverable's render gave <paramref name="text"/>.
        /// Its value is the text for a text format; for an object format the text parsed (D8: the value's canonical JSON;
        /// <c>null</c> composes <c>null</c>), its <c>.content</c> that value's canonical JSON. Composed, it reads so to its gate
        /// set (<see cref="RunInstance.Composed"/>, D3), and what needs no gate is decided at once, in part 06 § 7's order:
        /// empty (white space after trimming; in a <c>markdown_block</c>, the escape's <c>&amp;#32;</c> and <c>&amp;#9;</c> are
        /// white space too, <see cref="MarkdownEscape.IsBlank"/>; an object format never is) fails the job under
        /// <c>whenEmpty: fail</c> (A08-005) and is held under <c>hold</c>; otherwise an ungated deliverable is released. A
        /// gated one waits for its gate (<see cref="DecideOutcomesAsync"/>).
        /// </summary>
        private async Task ComposedAsync(RunInstance instance, string text, string? neutral)
        {
            var entry = instance.Entry;
            var textFormat = plan.Formats.TextFormat(entry.Format);
            JsonNode? value;
            string content;
            if (textFormat != null)
            {
                value = JsonValue.Create(text);
                content = text;
            }
            else
            {
                var (parsed, ok) = plan.Formats.Read(text, entry.Format);
                if (!ok)
                {
                    await FailAsync(instance, RunSentences.CouldNotRender(Subject(instance)));
                    return;
                }

                value = parsed;
                content = parsed is null ? "null" : CanonicalJson.Write(parsed);
            }

            instance.ComposedText = content;
            instance.ComposedHash = CanonicalJson.HashText(content);
            instance.Neutral = neutral;
            instance.Composed = new JsonObject { ["value"] = value?.DeepClone(), ["content"] = content, ["state"] = "composed" };

            var empty = textFormat switch
            {
                null => false,
                "plaintext" => string.IsNullOrWhiteSpace(content),
                _ => MarkdownEscape.IsBlank(content),
            };
            if (empty && SourceLayout.Text(entry.Json["whenEmpty"]) is "fail")
            {
                await FailAsync(instance, RunSentences.EmptyDeliverable(entry.Id, SourceLayout.Text(entry.Json["label"]) ?? entry.Id));
                return;
            }

            if (empty && SourceLayout.Text(entry.Json["whenEmpty"]) is "hold")
            {
                await SettleAsync(instance, RowState.Held, RunSentences.HeldEmpty, Final(instance, "held"), instance.ComposedHash);
                return;
            }

            if (entry.Check == null)
            {
                await SettleAsync(instance, RowState.Released, null, Final(instance, "released"), instance.ComposedHash);
                return;
            }

            instance.Phase = InstancePhase.Composed;
        }

        /// <summary>
        /// #1505 (part 06 § 7; D4, D5, D6; R2): in the settle round, in the fixed order, decides each composed deliverable whose
        /// gate check has settled: a <c>check_result</c> that passed releases it; one that did not blocks it with S08-007
        /// (<see cref="RunSentences.CheckDetail"/>); a gate skipped by its own <c>if</c> releases it with S06-006; a gate that
        /// failed to run (D4) blocks it with S08-008. A blocked deliverable reads <c>null</c> to every reader but its gate set,
        /// and keeps its composed hash for the record. True when one was decided.
        /// </summary>
        private async Task<bool> DecideOutcomesAsync()
        {
            var moved = false;
            foreach (var instance in Unsettled().Where(i => i.Phase == InstancePhase.Composed).ToList())
            {
                if (failure != null)
                    return moved;
                if (instance.Frame.Find(instance.Entry.Check!) is not { IsSettled: true } gate)
                    continue;

                var id = gate.Entry.Id;
                if (gate.State == RowState.Skipped)
                {
                    await SettleAsync(instance, RowState.Released, RunSentences.ReleasedCheckSkipped(id), Final(instance, "released"), instance.ComposedHash);
                }
                else if (gate.State != RowState.Completed)
                {
                    await SettleAsync(instance, RowState.Blocked, RunSentences.BlockedCouldNotRun(id, gate.Reason ?? ""), RunScope.Absent("blocked"), instance.ComposedHash);
                }
                else if (gate.Read?["value"] is JsonObject result && result["pass"] is JsonValue pass && pass.GetValueKind() == JsonValueKind.True)
                {
                    await SettleAsync(instance, RowState.Released, null, Final(instance, "released"), instance.ComposedHash);
                }
                else
                {
                    var found = gate.Read?["value"] as JsonObject;
                    var detail = RunSentences.CheckDetail(Whole(found?["passed"]), Whole(found?["total"]), (found?["uncovered"] as JsonArray)?.Count);
                    await SettleAsync(instance, RowState.Blocked, RunSentences.BlockedByCheck(id, detail), RunScope.Absent("blocked"), instance.ComposedHash);
                }

                moved = true;
            }

            return moved;
        }

        /// <summary>
        /// #1505 (D4): a leaf's <c>if</c> or a computed node's <c>expression</c> failed at run time, or its value does not fit
        /// its format. A gate check (one a deliverable's <c>check</c> names) settles failed with <paramref name="sentence"/> and
        /// reads <c>null</c>, and the job goes on (each deliverable it gates is blocked); anything else fails the job.
        /// </summary>
        private Task FailLeafAsync(RunInstance instance, string sentence) =>
            instance.Entry.Gates.Count > 0
                ? SettleAsync(instance, RowState.Failed, sentence, RunScope.Absent("failed"), null)
                : FailAsync(instance, sentence);

        /// <summary>
        /// #1505 (part 08 § 10): a deliverable's row is final: its detail is its render's (<see cref="Merge"/>) with
        /// <c>check</c> (the gate's key, or <c>null</c>) and <c>composedHash</c>, and its outcome is kept for <see cref="RunResult.Deliverables"/> (a deliverable cancelled by the
        /// job's time limit has none).
        /// </summary>
        private void DeliverableSettled(RunInstance instance, RowState state, string? reason, JsonObject detail)
        {
            var entry = instance.Entry;
            Merge(instance.CallDetail, detail);
            detail["check"] = entry.Check;
            detail["composedHash"] = instance.ComposedHash;
            DeliverableState? outcome = state switch
            {
                RowState.Released => DeliverableState.Released,
                RowState.Held => DeliverableState.Held,
                RowState.Blocked => DeliverableState.Blocked,
                RowState.Skipped => DeliverableState.Skipped,
                RowState.Failed => DeliverableState.Failed,
                _ => null,
            };
            if (outcome is not { } decided)
                return;

            var rest = instance.Key["deliverable.".Length..];
            outcomes.Add((entry.Order, new DeliverableOutcome(
                instance.Key,
                SourceLayout.Text(entry.Json["label"]) ?? entry.Id,
                plan.Formats.TextFormat(entry.Format) ?? BundleKey(entry.Format) ?? "",
                rest.Length > entry.Id.Length ? rest[..^(entry.Id.Length + 1)] : "",
                decided,
                reason,
                entry.Check == null ? null : instance.Frame.Find(entry.Check)?.Key ?? entry.Check,
                instance.ComposedText,
                instance.ComposedHash,
                entry.Json["delivery"] is JsonArray kinds ? kinds.Select(k => SourceLayout.Text(k)).OfType<string>().ToList() : [],
                SourceLayout.Text(entry.Json["bundle"]))));
        }

        /// <summary>#1506 (area 09; D7): the gate checks settled whose rows wait for their deliverables, in the order they settled.</summary>
        private readonly List<(RunInstance Gate, RunRow Row)> heldGates = [];

        /// <summary>
        /// #1506 (area 09; part 08 § 6; D7): records <paramref name="gate"/>'s held row once every deliverable it gates has
        /// settled, its <c>effect</c> their outcome (<see cref="GateEffect"/>); until then it stays held. Its value, its
        /// decisions and its deliverables' outcomes were all settled as before: only its row waits.
        /// </summary>
        private async Task ReleaseGateAsync(RunInstance gate)
        {
            var at = heldGates.FindIndex(h => ReferenceEquals(h.Gate, gate));
            if (at < 0)
                return;
            var settled = new List<(string Key, RowState State)>();
            foreach (var key in gate.Entry.Gates)
            {
                if (gate.Frame.Find(key) is not { IsSettled: true, State: { } state } deliverable)
                    return;
                settled.Add((deliverable.Key, state));
            }

            var row = heldGates[at].Row;
            heldGates.RemoveAt(at);
            row.Detail["effect"] = GateEffect(settled);
            await recorder.RowAsync(row);
        }

        /// <summary>
        /// #1506 (area 09; D7): when the run ends, whatever the end (completed, failed, timed out), each gate's row still held
        /// is recorded with <c>effect</c> <c>null</c>: what it would have caused is not known.
        /// </summary>
        private async Task RecordHeldGatesAsync()
        {
            foreach (var (_, row) in heldGates)
            {
                row.Detail["effect"] = null;
                await recorder.RowAsync(row);
            }

            heldGates.Clear();
        }

        /// <summary>
        /// #1506 (area 09; part 08 § 6; D7): a gate's <c>effect</c> from its deliverables' states: the state word
        /// (<c>released</c>, <c>held</c>, <c>blocked</c>; <c>skipped</c> for one its own <c>if</c> skipped) for one deliverable;
        /// for more, <c>&lt;word&gt; &lt;deliverable key&gt;</c> joined by <c>; </c> in key order. <c>null</c> when one failed
        /// or was cancelled: the job ended there, and what the gate caused is not known.
        /// </summary>
        private static string? GateEffect(List<(string Key, RowState State)> settled)
        {
            if (settled.Count == 0 || settled.Any(s => s.State is not (RowState.Released or RowState.Held or RowState.Blocked or RowState.Skipped)))
                return null;
            if (settled.Count == 1)
                return StateWord(settled[0].State);
            return string.Join("; ", settled.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{StateWord(s.State)} {s.Key}"));
        }

        /// <summary>A composed deliverable's read form once released or held: <c>{ value, content, state }</c>, with its <c>neutral</c> text when it has one.</summary>
        private static JsonObject Final(RunInstance instance, string state)
        {
            var read = new JsonObject
            {
                ["value"] = instance.Composed?["value"]?.DeepClone(),
                ["content"] = instance.ComposedText,
                ["state"] = state,
            };
            if (instance.Neutral != null)
                read["neutral"] = instance.Neutral;
            return read;
        }

        /// <summary>A <c>check_result</c>'s count as a whole number, or null when it is not one.</summary>
        private static int? Whole(JsonNode? node) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
            && int.TryParse(value.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole)
                ? whole
                : null;

        /// <summary>The bundle key a format's <c>$ref</c> names (<c>#/$defs/&lt;key&gt;</c>), or null.</summary>
        private static string? BundleKey(JsonNode? format) =>
            format is JsonObject site && SourceLayout.Text(site["$ref"]) is { } reference && reference.StartsWith("#/$defs/", StringComparison.Ordinal)
                ? reference["#/$defs/".Length..]
                : null;
    }
}
