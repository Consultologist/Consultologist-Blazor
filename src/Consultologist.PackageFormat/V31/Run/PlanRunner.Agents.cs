using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31.Templates;

namespace Consultologist.PackageFormat.V31.Run;

public sealed partial class PlanRunner
{
    /// <summary>The least wait applied before a retry, in milliseconds: the back-off's start (part 09 § 3.4).</summary>
    private const int LeastWaitMilliseconds = 5000;

    /// <summary>
    /// One agent call, across its attempts: the call as first made, the 429 waits taken (in milliseconds), the other service
    /// failures met, the replies it got, whether its repair was asked for, its <c>onInvalid</c> outcome (part 08 § 4's
    /// <c>invalid</c>, null while every reply was valid), and each retry's record (part 08 § 4's <c>retries</c>).
    /// </summary>
    private sealed class AgentState(AgentCall call)
    {
        public AgentCall Call { get; } = call;

        public long CapacityWaited { get; set; }

        public int ServiceFailures { get; set; }

        public int Replies { get; set; }

        public bool Repaired { get; set; }

        public (string Reason, string Action)? Invalid { get; set; }

        /// <summary>#1504 (D11): the sentences of the reply's marker faults the repair names; empty for a format repair, null before one.</summary>
        public IReadOnlyList<string>? Faults { get; set; }

        public JsonArray Retries { get; } = [];
    }

    private sealed partial class Run
    {
        /// <summary>Each agent instance's call, by instance (reference equality: only looked up, never iterated).</summary>
        private readonly Dictionary<RunInstance, AgentState> agentStates = new(ReferenceEqualityComparer.Instance);

        /// <summary>#1505 (D14): the job's 429 waits taken, in all across its nodes, in milliseconds.</summary>
        private long capacityWaitedInAll;

        /// <summary>#1505 (D14): whether the capacity-wait notice was raised, so it is raised once.</summary>
        private bool capacityNoticed;

        /// <summary>Makes the first attempt of <paramref name="call"/>, in the slot <see cref="DispatchAsync"/> gave it.</summary>
        private void StartAgent(RunInstance instance, AgentCall call)
        {
            agentStates[instance] = new AgentState(call);
            CallAgain(instance);
        }

        /// <summary>Makes the next attempt of <paramref name="instance"/>'s call, in the slot it holds: a retry, or its repair once asked for.</summary>
        private void CallAgain(RunInstance instance)
        {
            var state = agentStates[instance];
            instance.Attempt++;
            var call = state.Call with { Attempt = instance.Attempt, Repair = state.Repaired, Faults = state.Faults };
            flights.Add(new Flight(instance, host.CallAgentAsync(call), FlightKind.Agent));
        }

        /// <summary>
        /// An attempt's outcome (part 09 § 3.4, D7). A reply goes to <see cref="RepliedAsync"/>. A 429 adds its wait to the
        /// node's: past <see cref="RunLimits.CapacityWaitMinutes"/> the node fails with S09-010, else it waits on the host's
        /// timer and calls again. Another service failure counts: at <see cref="RunLimits.ServiceAttempts"/> the node fails with
        /// S09-011, else it waits and calls again. The slot is held throughout, and a retry is part of its call. A call that could
        /// not be made (<see cref="AgentOutcomeKind.Failed"/>) fails the node at once: S05-062 for <c>steps</c>, A06-008 for
        /// <c>compose</c>, S07-006 for <c>plan</c> (and any other kind).
        /// </summary>
        private async Task AgentAsync(RunInstance instance, AgentOutcome outcome)
        {
            var state = agentStates[instance];
            // A wait of 0 or less would call again at once: the back-off's start is applied instead, and counted.
            var wait = outcome.WaitMilliseconds > 0 ? outcome.WaitMilliseconds : LeastWaitMilliseconds;
            switch (outcome.Kind)
            {
                case AgentOutcomeKind.Completed:
                    instance.CallDetail = outcome.Detail;
                    if (outcome.Invalid is { Count: > 0 } faults)
                        await InvalidAsync(instance, state, faults);
                    else
                        await RepliedAsync(instance, state, outcome.Reply ?? "");
                    return;
                case AgentOutcomeKind.Throttled:
                    if (state.CapacityWaited + wait > limits.CapacityWaitMinutes * 60_000L)
                    {
                        await FailAsync(instance, $"The model service stayed busy for {Count(limits.CapacityWaitMinutes)} minutes. Run the workflow again later.");
                        return;
                    }

                    state.CapacityWaited += wait;
                    Retry(instance, wait, "capacity", null);
                    await CapacityWaitedAsync(instance, wait);
                    return;
                case AgentOutcomeKind.Failed:
                    // Not the model service's: no retry changes it, so the node fails with the sentence for its kind.
                    await FailAsync(instance, outcome.ErrorKind switch
                    {
                        "steps" => RunSentences.StoppedRendering(Subject(instance), limits.RenderSteps),
                        "compose" => RunSentences.CouldNotRender(Subject(instance)),
                        _ => ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence,
                    });
                    return;
                default:
                    state.ServiceFailures++;
                    if (state.ServiceFailures >= limits.ServiceAttempts)
                    {
                        await FailAsync(instance,
                            $"The model service {ErrorWords(outcome.ErrorKind)} {Count(limits.ServiceAttempts)} times in a row. Run the workflow again later.");
                        return;
                    }

                    Retry(instance, wait, "service", RecordedError(outcome.ErrorKind));
                    return;
            }
        }

        /// <summary>
        /// Records a retry on the node's row as part 08 § 4 words it (<c>{ atUtc, waitMs, kind, error }</c>) and waits on the
        /// host's timer, holding the slot. The timer is cancelled if the run ends first.
        /// </summary>
        private void Retry(RunInstance instance, int wait, string kind, string? error)
        {
            agentStates[instance].Retries.Add(new JsonObject
            {
                ["atUtc"] = host.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["waitMs"] = wait,
                ["kind"] = kind,
                ["error"] = error,
            });
            flights.Add(new Flight(instance, host.DelayAsync(TimeSpan.FromMilliseconds(wait), timers.Token), FlightKind.Retry));
        }

        /// <summary>Part 08 § 4's <c>error</c> for the host's kind of a service failure: <c>timeout</c>, <c>5xx</c> or <c>connection</c>.</summary>
        private static string RecordedError(string? kind) => kind switch
        {
            "timeout" => "timeout",
            "unreachable" => "connection",
            _ => "5xx",
        };

        /// <summary>
        /// A reply (part 02 § 3.4, part 05 § 1.2): read as its format's value (a text format's reply is the text) and validated
        /// against the full format. An invalid one is asked again once, under <c>onInvalid: "retry"</c>, in the same slot and
        /// as the same call; invalid again, or under <c>"fail"</c>, the node fails with A06-005, which names the format and
        /// never the reply. The outcome is the row's <c>invalid</c> (part 08 § 4): that sentence as its <c>reason</c>, the
        /// node's <c>onInvalid</c> as its <c>action</c>, and the replies the node got as its <c>attempts</c>. A valid value
        /// that is empty fails the node with its <c>failIfEmpty</c> sentence, when it has one.
        /// </summary>
        private async Task RepliedAsync(RunInstance instance, AgentState state, string reply)
        {
            var entry = instance.Entry;
            state.Replies++;
            var (value, parsed) = plan.Formats.Read(reply, entry.Format);
            if (!parsed || !plan.Formats.Validate(value, entry.Format).Valid)
            {
                var sentence = $"Node '{entry.Id}' gave a reply that isn't {FormatName(entry.Format)}.";
                await OnInvalidAsync(instance, state, sentence, sentence, []);
                return;
            }

            if (SourceLayout.Text(entry.Json["failIfEmpty"]) is { } empty && IsEmpty(value, plan.Formats.TextFormat(entry.Format) is "markdown_block" or "markdown_inline"))
            {
                await FailAsync(instance, empty);
                return;
            }

            await CompleteAsync(instance, value);
        }

        /// <summary>
        /// #1504 (part 05 § 8.6; part 08 § 4; D11): a reply whose macro markers are wrong (<see cref="AgentOutcome.Invalid"/>),
        /// the reply as it came, never read: the row's <c>invalid</c> reason is the faults' short forms joined by <c>; </c>,
        /// and <c>onInvalid</c> is applied as for a reply that does not fit its format, the repair naming each fault's
        /// sentence and the failure the first's, after the node.
        /// </summary>
        private Task InvalidAsync(RunInstance instance, AgentState state, IReadOnlyList<ReplyFault> faults)
        {
            state.Replies++;
            return OnInvalidAsync(instance, state, string.Join("; ", faults.Select(f => f.Reason)),
                $"Node '{instance.Entry.Id}': {faults[0].Sentence}", [.. faults.Select(f => f.Sentence)]);
        }

        /// <summary>
        /// The node's <c>onInvalid</c> for an invalid reply (part 05 § 1.2): recorded as the row's <c>invalid</c>
        /// (<paramref name="reason"/> and the action); under <c>retry</c> asked again once, in the same slot and as the same
        /// call, the repair carrying <paramref name="faults"/>; invalid again, or under <c>fail</c>, the node fails with
        /// <paramref name="sentence"/>.
        /// </summary>
        private async Task OnInvalidAsync(RunInstance instance, AgentState state, string reason, string sentence, IReadOnlyList<string> faults)
        {
            var retry = SourceLayout.Text(instance.Entry.Json["onInvalid"]) == "retry";
            state.Invalid = (reason, retry ? "retry" : "fail");
            if (retry && !state.Repaired)
            {
                state.Repaired = true;
                state.Faults = faults;
                CallAgain(instance);
                return;
            }

            await FailAsync(instance, sentence);
        }

        /// <summary>
        /// Whether a valid value is empty for <c>failIfEmpty</c>, as at version 30: text that is blank (white space only),
        /// or an empty list. An object, a number, a boolean and <c>null</c> are never empty. #1505 (Task 15's review): in a
        /// Markdown node (<paramref name="markdown"/>), where a plain-text macro's body is escaped in, the escape's
        /// <c>&amp;#32;</c> and <c>&amp;#9;</c> are white space too (<see cref="MarkdownEscape.IsBlank"/>), so a blank body
        /// stays blank under R9; in plain text they are the characters written.
        /// </summary>
        private static bool IsEmpty(JsonNode? value, bool markdown) => value switch
        {
            JsonArray list => list.Count == 0,
            JsonValue text when text.GetValueKind() == JsonValueKind.String =>
                markdown ? MarkdownEscape.IsBlank(text.GetValue<string>()) : string.IsNullOrWhiteSpace(text.GetValue<string>()),
            _ => false,
        };

        /// <summary>
        /// An agent row's detail, in part 08 § 4's names: #1504, the members its last attempt's outcome gave
        /// (<see cref="AgentTurn"/>'s: hashes, run spots, renders, macro rows, units); <c>invalid</c> (<c>null</c> when every
        /// reply was valid) and <c>retries</c> (<c>[]</c> for none).
        /// </summary>
        private void AgentDetail(RunInstance instance, JsonObject detail)
        {
            Merge(instance.CallDetail, detail);
            var state = agentStates.GetValueOrDefault(instance);
            detail["invalid"] = state?.Invalid is { } invalid
                ? new JsonObject { ["reason"] = invalid.Reason, ["action"] = invalid.Action, ["attempts"] = state.Replies }
                : null;
            detail["retries"] = state?.Retries.DeepClone() ?? new JsonArray();
        }

        // ---- the job limit --------------------------------------------------------------------

        /// <summary>Whether the job has run <see cref="RunLimits.JobMinutes"/> from its orchestration's start, by the host's clock.</summary>
        private bool PastTheJobLimit() => host.UtcNow - StartedAtUtc >= TimeSpan.FromMinutes(limits.JobMinutes);

        /// <summary>The job's time left by the host's clock, never below zero.</summary>
        private TimeSpan TimeLeft()
        {
            var left = StartedAtUtc + TimeSpan.FromMinutes(limits.JobMinutes) - host.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>#1505 (D14): the minutes into the job at which it is slow: <see cref="SlowJobPercent"/> of its <see cref="RunLimits.JobMinutes"/>.</summary>
        private TimeSpan SlowAt => TimeSpan.FromTicks(TimeSpan.FromMinutes(limits.JobMinutes).Ticks / 100 * SlowJobPercent);

        /// <summary>#1505 (D14): the time to <see cref="SlowAt"/> by the host's clock, never below zero.</summary>
        private TimeSpan TimeToSlow()
        {
            var left = StartedAtUtc + SlowAt - host.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>
        /// #1505 (part 09 § 3.9; D14): the slow-job timer ended before the job's limit: the operator is told once, with the
        /// minutes the job has run (<c>job-slow</c>), and the timer is not made again.
        /// </summary>
        private Task SlowAsync()
        {
            slowNoticed = true;
            slowTimer = null;
            return recorder.NoticeAsync(new RunNotice("job-slow", null, (int)SlowAt.TotalMinutes));
        }

        /// <summary>
        /// #1505 (part 09 § 3.9; D14): adds a 429 wait to the job's in all; the first that takes it past
        /// <see cref="CapacityAlertMinutes"/> tells the operator once (<c>capacity-wait</c>), with the row that waited and the
        /// waits in all, in whole minutes.
        /// </summary>
        private Task CapacityWaitedAsync(RunInstance instance, int wait)
        {
            capacityWaitedInAll += wait;
            if (capacityNoticed || capacityWaitedInAll <= CapacityAlertMinutes * 60_000L)
                return Task.CompletedTask;
            capacityNoticed = true;
            return recorder.NoticeAsync(new RunNotice("capacity-wait", instance.Key, (int)(capacityWaitedInAll / 60_000)));
        }

        /// <summary>
        /// The job reached its time limit (part 09 § 3.4): it times out with S09-012, and each instance in flight (a call, a
        /// retry's wait, a render), in the fixed order, is recorded cancelled with S08-009. Nothing in flight is read after.
        /// </summary>
        private async Task TimeOutAsync()
        {
            var minutes = Count(limits.JobMinutes);
            failure = ($"The job didn't finish within {minutes} minutes. Run the workflow again later.", null);
            timedOut = true;
            foreach (var instance in flights.Select(f => f.Instance).OrderBy(i => i.Rank, RankComparer.Instance).ToList())
                await SettleAsync(instance, RowState.Cancelled, $"cancelled: the job didn't finish within {minutes} minutes", RunScope.Absent("cancelled"), null);
        }

        /// <summary>
        /// Observes the fault of a task the run no longer awaits, so a late failure of the host is never an unobserved task
        /// exception. The continuation reads nothing of the run and changes nothing.
        /// </summary>
        private static void Observe(Task task) =>
            _ = task.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
