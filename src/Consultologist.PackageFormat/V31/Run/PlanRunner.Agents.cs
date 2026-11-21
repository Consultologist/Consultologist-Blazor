using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

public sealed partial class PlanRunner
{
    /// <summary>
    /// One agent call, across its attempts: the call as first made, the 429 waits taken (in milliseconds), the other service
    /// failures met, whether its repair was asked for, and each retry's record.
    /// </summary>
    private sealed class AgentState(AgentCall call)
    {
        public AgentCall Call { get; } = call;

        public long CapacityWaited { get; set; }

        public int ServiceFailures { get; set; }

        public bool Repaired { get; set; }

        public JsonArray Retries { get; } = [];
    }

    private sealed partial class Run
    {
        /// <summary>Each agent instance's call, by instance (reference equality: only looked up, never iterated).</summary>
        private readonly Dictionary<RunInstance, AgentState> agentStates = new(ReferenceEqualityComparer.Instance);

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
            var call = state.Call with { Attempt = instance.Attempt, Repair = state.Repaired };
            flights.Add(new Flight(instance, host.CallAgentAsync(call), FlightKind.Agent));
        }

        /// <summary>
        /// An attempt's outcome (part 09 § 3.4, D7). A reply goes to <see cref="RepliedAsync"/>. A 429 adds its wait to the
        /// node's: past <see cref="RunLimits.CapacityWaitMinutes"/> the node fails with S09-010, else it waits on the host's
        /// timer and calls again. Another service failure counts: at <see cref="RunLimits.ServiceAttempts"/> the node fails with
        /// S09-011, else it waits and calls again. The slot is held throughout, and a retry is part of its call.
        /// </summary>
        private async Task AgentAsync(RunInstance instance, AgentOutcome outcome)
        {
            var state = agentStates[instance];
            var wait = Math.Max(0, outcome.WaitMilliseconds);
            switch (outcome.Kind)
            {
                case AgentOutcomeKind.Completed:
                    await RepliedAsync(instance, state, outcome.Reply ?? "");
                    return;
                case AgentOutcomeKind.Throttled:
                    if (state.CapacityWaited + wait > limits.CapacityWaitMinutes * 60_000L)
                    {
                        await FailAsync(instance, $"The model service stayed busy for {Count(limits.CapacityWaitMinutes)} minutes. Run the workflow again later.");
                        return;
                    }

                    state.CapacityWaited += wait;
                    Retry(instance, state, wait, "capacity", null);
                    return;
                default:
                    state.ServiceFailures++;
                    if (state.ServiceFailures >= limits.ServiceAttempts)
                    {
                        await FailAsync(instance,
                            $"The model service {ErrorWords(outcome.ErrorKind)} {Count(limits.ServiceAttempts)} times in a row. Run the workflow again later.");
                        return;
                    }

                    Retry(instance, state, wait, "service", outcome.ErrorKind);
                    return;
            }
        }

        /// <summary>Records a retry on the node's row (its time, its wait, what for, the error's kind) and waits on the host's timer, holding the slot.</summary>
        private void Retry(RunInstance instance, AgentState state, int wait, string reason, string? errorKind)
        {
            state.Retries.Add(new JsonObject
            {
                ["atUtc"] = host.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["waitMilliseconds"] = wait,
                ["for"] = reason,
                ["errorKind"] = errorKind,
            });
            flights.Add(new Flight(instance, host.DelayAsync(TimeSpan.FromMilliseconds(wait)), FlightKind.Retry));
        }

        /// <summary>
        /// A reply (part 02 § 3.4, part 05 § 1.2): read as its format's value (a text format's reply is the text) and validated
        /// against the full format. An invalid one is asked again once, under <c>onInvalid: "retry"</c>, in the same slot and
        /// as the same call; invalid again, or under <c>"fail"</c>, the node fails with A06-005, which names the format and
        /// never the reply. A valid value that is empty fails the node with its <c>failIfEmpty</c> sentence, when it has one.
        /// </summary>
        private async Task RepliedAsync(RunInstance instance, AgentState state, string reply)
        {
            var entry = instance.Entry;
            var (value, parsed) = plan.Formats.Read(reply, entry.Format);
            if (!parsed || !plan.Formats.Validate(value, entry.Format).Valid)
            {
                if (!state.Repaired && SourceLayout.Text(entry.Json["onInvalid"]) == "retry")
                {
                    state.Repaired = true;
                    CallAgain(instance);
                    return;
                }

                await FailAsync(instance, $"Node '{entry.Id}'{InFunction(instance)} gave a reply that isn't {FormatName(entry.Format)}.");
                return;
            }

            if (SourceLayout.Text(entry.Json["failIfEmpty"]) is { } empty && IsEmpty(value))
            {
                await FailAsync(instance, empty);
                return;
            }

            await CompleteAsync(instance, value, RowState.Completed);
        }

        /// <summary>
        /// Whether a valid value is empty for <c>failIfEmpty</c>, as at version 30: text that is blank (white space only),
        /// or an empty list. An object, a number, a boolean and <c>null</c> are never empty.
        /// </summary>
        private static bool IsEmpty(JsonNode? value) => value switch
        {
            JsonArray list => list.Count == 0,
            JsonValue text when text.GetValueKind() == JsonValueKind.String => string.IsNullOrWhiteSpace(text.GetValue<string>()),
            _ => false,
        };

        /// <summary>An agent row's detail: its <c>retries</c> (<c>[]</c> for none) and whether it was <c>repaired</c>.</summary>
        private void AgentDetail(RunInstance instance, JsonObject detail)
        {
            var state = agentStates.GetValueOrDefault(instance);
            detail["retries"] = state?.Retries.DeepClone() ?? new JsonArray();
            detail["repaired"] = state?.Repaired ?? false;
        }

        // ---- the job limit --------------------------------------------------------------------

        /// <summary>Whether the job has run <see cref="RunLimits.JobMinutes"/> from its orchestration's start, by the host's clock.</summary>
        private bool PastTheJobLimit() => host.UtcNow - StartedAtUtc >= TimeSpan.FromMinutes(limits.JobMinutes);

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
