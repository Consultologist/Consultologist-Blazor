using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Consultologist.PackageFormat.V31.Run;

/// <summary>#1503 (specVersion 31, area 06): how a run ended.</summary>
public enum RunState
{
    /// <summary>Everything settled.</summary>
    Completed,

    /// <summary>A node failed, and the job with it.</summary>
    Failed,

    /// <summary>The job passed its <c>jobMinutes</c>.</summary>
    TimedOut,
}

/// <summary>
/// #1503 (specVersion 31, area 06): what a run gave: its state; for a failed run, the sentence and the key of what failed;
/// every top-level entry's settled read form (<c>{ value, content, state }</c>), by key; the agent calls made; and the most
/// agent calls in flight at once (telemetry only, part 09 § 3.2).
/// </summary>
public sealed record RunResult(
    RunState State,
    string? Sentence,
    string? FailedKey,
    IReadOnlyDictionary<string, JsonNode?> Values,
    int AgentCalls,
    int PeakInFlight);

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 §§ 3.1, 3.2, 3.4; part 04 § 8): the deterministic scheduler that runs a plan.
/// Each round it settles, in the fixed order, everything that needs no call (an <c>if</c> deciding, a <c>computed</c>
/// evaluating), until nothing more settles; then it dispatches the ready agent and render calls in the fixed order
/// (<see cref="RunInstance.Rank"/>), an agent call only while fewer than <see cref="RunLimits.MaxAgentCalls"/> are in flight;
/// then it waits, by <see cref="Task.WhenAny(IEnumerable{Task})"/> over what is in flight, for one to finish, and settles
/// it. A node that fails fails the job: nothing more is dispatched and nothing in flight is awaited. The run ends when
/// nothing is in flight and nothing is ready, or when it reaches <see cref="RunLimits.JobMinutes"/> (it then times out, each
/// call in flight recorded cancelled). An agent's reply, its repair and its retries are <c>PlanRunner.Agents.cs</c>'s; a
/// <c>for</c> loop, its item frames and its <c>maxParallel</c>, <c>PlanRunner.Loops.cs</c>'s; a <c>while</c>'s passes and
/// the branches of an <c>if</c> and a <c>case</c>, <c>PlanRunner.Flow.cs</c>'s; a <c>call</c>'s function body, run inline
/// in a frame of its own under the same ceiling, <c>PlanRunner.Calls.cs</c>'s.
/// </summary>
/// <remarks>
/// Durable Functions drives it: it awaits no task but <see cref="IRunHost"/>'s (and the recorder's), reads no clock but
/// <see cref="IRunHost.UtcNow"/>, and iterates only in the fixed order or list order. It never throws on package content or
/// model output; a fault of the host or the recorder is theirs and passes through.
/// </remarks>
public sealed partial class PlanRunner
{
    private readonly RunPlan plan;
    private readonly RunLimits limits;
    private readonly IRunHost host;
    private readonly IRunRecorder recorder;

    /// <summary>A runner of <paramref name="plan"/> under <paramref name="limits"/>, talking to <paramref name="host"/> and <paramref name="recorder"/>.</summary>
    public PlanRunner(RunPlan plan, RunLimits limits, IRunHost host, IRunRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recorder);
        this.plan = plan;
        this.limits = limits;
        this.host = host;
        this.recorder = recorder;
    }

    /// <summary>Runs the plan once on <paramref name="inputs"/>, for an orchestration that started at <paramref name="startedAtUtc"/>.</summary>
    public Task<RunResult> RunAsync(RunInputs inputs, DateTime startedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return new Run(this, inputs, startedAtUtc).RunAsync();
    }

    /// <summary>What a flight waits on: an agent call's attempt, a render, or the wait before an agent call's next attempt.</summary>
    private enum FlightKind
    {
        Agent,
        Render,
        Retry,
    }

    /// <summary>Something in flight: the instance and the host's task. An instance has at most one at a time.</summary>
    private sealed record Flight(RunInstance Instance, Task Task, FlightKind Kind);

    /// <summary>One run: its frames and instances, what is in flight, its counters and its failure.</summary>
    private sealed partial class Run(PlanRunner runner, RunInputs inputs, DateTime startedAtUtc)
    {
        private readonly RunPlan plan = runner.plan;
        private readonly RunLimits limits = runner.limits;
        private readonly IRunHost host = runner.host;
        private readonly IRunRecorder recorder = runner.recorder;
        private readonly RunScope scope = new(inputs);
        private readonly RunFrame top = RunFrame.Top(runner.plan);

        /// <summary>
        /// The instances not settled, in the fixed order (<see cref="FixedOrder"/>), kept as they are made and settled so that
        /// a round never sorts the run again.
        /// </summary>
        private readonly SortedSet<RunInstance> pending = new(FixedOrder.Instance);

        /// <summary>How many instances the run has made.</summary>
        private int made;

        /// <summary>The calls in flight, in the order dispatched.</summary>
        private readonly List<Flight> flights = [];

        private int agentCalls;
        private int agentsInFlight;
        private int peakInFlight;
        private (string? Sentence, string? Key)? failure;
        private bool timedOut;

        /// <summary>The host's timer for the job's time left, made the first time the run waits.</summary>
        private Task? jobTimer;

        /// <summary>Cancels every timer the run made (the job's and each retry's) when the run ends, on every path.</summary>
        private readonly CancellationTokenSource timers = new();

        /// <summary>When the orchestration started (the job's time limit runs from it).</summary>
        public DateTime StartedAtUtc { get; } = startedAtUtc;

        public async Task<RunResult> RunAsync()
        {
            foreach (var key in plan.TopLevel)
                Add(top, plan.Entries[key]);

            try
            {
                if (PastTheJobLimit())
                    await TimeOutAsync();

                while (failure == null)
                {
                    await SettleRoundAsync();
                    if (failure != null)
                        break;
                    await DispatchAsync();
                    if (failure != null)
                        break;

                    if (flights.Count == 0)
                    {
                        // Nothing in flight and nothing more settles: the run is over, or stuck (a document publish did not write).
                        if (Unsettled().FirstOrDefault() is { } stuck)
                            await FailAsync(stuck, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                        break;
                    }

                    jobTimer ??= host.DelayAsync(TimeLeft(), timers.Token);
                    var finished = await Task.WhenAny(flights.Select(f => f.Task).Append(jobTimer));
                    if (ReferenceEquals(finished, jobTimer) || PastTheJobLimit())
                    {
                        // Nothing that finished at or after the limit is read.
                        await TimeOutAsync();
                        break;
                    }

                    var flight = flights.First(f => ReferenceEquals(f.Task, finished));
                    flights.Remove(flight);
                    await LandAsync(flight);
                }
            }
            finally
            {
                // What is still in flight is never awaited: its late answer changes nothing, its timer is cancelled (an
                // orchestration does not complete while one is outstanding), and its fault is never unobserved.
                timers.Cancel();
                foreach (var abandoned in flights)
                    Observe(abandoned.Task);
                if (jobTimer != null)
                    Observe(jobTimer);
                timers.Dispose();
            }

            var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var instance in top.Instances)
            {
                if (instance.Read is { } read)
                    values[instance.Key] = read.DeepClone();
            }

            return new RunResult(
                timedOut ? RunState.TimedOut : failure == null ? RunState.Completed : RunState.Failed,
                failure?.Sentence,
                failure?.Key,
                values,
                agentCalls,
                peakInFlight);
        }

        // ---- the round ----------------------------------------------------------------------

        /// <summary>The instance of <paramref name="entry"/> in <paramref name="frame"/>, added to the run.</summary>
        private RunInstance Add(RunFrame frame, PlanEntry entry)
        {
            var instance = frame.Add(entry);
            instance.Sequence = made++;
            pending.Add(instance);
            return instance;
        }

        /// <summary>The instances not settled, in the fixed order.</summary>
        private IEnumerable<RunInstance> Unsettled() => pending;

        /// <summary>Settles, in the fixed order, every ready instance that needs no call, until nothing more moves.</summary>
        private async Task SettleRoundAsync()
        {
            bool moved;
            do
            {
                moved = false;
                foreach (var instance in Unsettled().Where(i => i.Phase == InstancePhase.Pending).ToList())
                {
                    if (failure != null)
                        return;
                    if (!instance.WasReady)
                        instance.WasReady = Ready(instance);
                    if (instance.WasReady && await StepAsync(instance))
                        moved = true;
                }
            }
            while (moved && failure == null);
        }

        /// <summary>
        /// Moves a ready instance on with no call: decides its <c>if</c>, evaluates a computed node, leaves an agent or a
        /// render waiting for dispatch, and moves a loop (<c>PlanRunner.Loops.cs</c>), a while, an <c>if</c> or a
        /// <c>case</c> (<c>PlanRunner.Flow.cs</c>), or a <c>call</c> (<c>PlanRunner.Calls.cs</c>) on. False when it did not
        /// move (a flow node with nothing to do yet).
        /// </summary>
        private async Task<bool> StepAsync(RunInstance instance)
        {
            switch (instance.Entry.Kind)
            {
                case PlanKind.Agent or PlanKind.Rendered or PlanKind.Deliverable:
                    if (await GateAsync(instance))
                        instance.Phase = InstancePhase.Waiting;
                    return true;
                case PlanKind.Computed:
                    if (await GateAsync(instance))
                        await ComputeAsync(instance);
                    return true;
                case PlanKind.For:
                    return await LoopAsync(instance);
                case PlanKind.While:
                    return await WhileAsync(instance);
                case PlanKind.If or PlanKind.Case:
                    return await BranchAsync(instance);
                case PlanKind.Call:
                    return await CallAsync(instance);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Decides a leaf's or a deliverable's <c>if</c>: true when it runs. False or <c>null</c> skips it with S03-001's reason
        /// and records the decision; a failure fails it with part 03 § 11.3's sentence.
        /// </summary>
        private async Task<bool> GateAsync(RunInstance instance)
        {
            var entry = instance.Entry;
            if (entry.If is not { } rule)
                return true;

            var decision = rule.Decide(scope.Data(entry, instance.Frame, entry.Reads));
            if (decision.Failure is { } failed)
            {
                await FailAsync(instance, ExpressionSentences.RunTime(Subject(instance), "if", failed));
                return false;
            }

            await recorder.DecisionAsync(new RunDecision(instance.Key, "self", entry.Printed("if"), decision.Result, null, host.UtcNow));
            if (decision.Result)
                return true;

            await SettleAsync(instance, RowState.Skipped, JsonLogicPrinter.Reason("skipped", entry.Json["if"], false), RunScope.Absent("skipped"), null);
            return false;
        }

        /// <summary>Evaluates a computed node inline; a value that does not fit its format fails it with A06-003 (04-D49: <c>null</c> is a value).</summary>
        private async Task ComputeAsync(RunInstance instance)
        {
            var entry = instance.Entry;
            if (entry.Expression is not { } expression)
            {
                await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                return;
            }

            var value = expression.Evaluate(scope.Data(entry, instance.Frame, entry.Reads));
            if (value.Failure is { } failed)
            {
                await FailAsync(instance, ExpressionSentences.RunTime(ComputedSubject(instance), "expression", failed));
                return;
            }

            if (value.Value != null && !plan.Formats.Validate(value.Value, entry.Format).Valid)
            {
                await FailAsync(instance, $"Node '{entry.Id}' computed a value that isn't {FormatName(entry.Format)}.");
                return;
            }

            await CompleteAsync(instance, value.Value, RowState.Completed);
        }

        /// <summary>Dispatches the waiting calls in the fixed order: an agent call while a slot is free and the job's calls allow.</summary>
        private async Task DispatchAsync()
        {
            foreach (var instance in Unsettled().Where(i => i.Phase == InstancePhase.Waiting).ToList())
            {
                var entry = instance.Entry;
                if (entry.Kind != PlanKind.Agent)
                {
                    // A render takes no slot.
                    instance.Phase = InstancePhase.Running;
                    var render = new RenderCall(instance.Key, entry.Key, instance.Frame.Function,
                        scope.Data(entry, instance.Frame, CompiledReads(entry)), limits.RenderSteps);
                    flights.Add(new Flight(instance, host.RenderAsync(render), FlightKind.Render));
                    continue;
                }

                if (agentsInFlight >= limits.MaxAgentCalls)
                    continue;
                if (agentCalls >= limits.JobAgentCalls)
                {
                    await FailAsync(instance, $"The job reached {Count(limits.JobAgentCalls)} agent calls, the most this engine runs for one job.");
                    return;
                }

                agentCalls++;
                agentsInFlight++;
                peakInFlight = Math.Max(peakInFlight, agentsInFlight);
                instance.Phase = InstancePhase.Running;
                var call = new AgentCall(instance.Key, entry.Key, instance.Frame.Function, 1, false,
                    scope.Data(entry, instance.Frame, CompiledReads(entry)));
                StartAgent(instance, call);
            }
        }

        /// <summary>
        /// What a call's scope holds: exactly the entry's compiled <c>reads</c> (its body's and its header's, as publish wrote
        /// them), never more, since Durable keeps an activity's input in its history. <see cref="RunPlan.Build"/> has checked
        /// it is a list of strings.
        /// </summary>
        private static IEnumerable<string> CompiledReads(PlanEntry entry) =>
            entry.Json["reads"] is JsonArray reads ? reads.Select(r => SourceLayout.Text(r)).OfType<string>() : [];

        /// <summary>
        /// Moves on what finished: an agent call's attempt (its slot is freed once the node settles, a repair or a retry
        /// keeping it), the wait before its next attempt, or a render.
        /// </summary>
        private async Task LandAsync(Flight flight)
        {
            switch (flight.Kind)
            {
                case FlightKind.Agent:
                    await AgentAsync(flight.Instance, await (Task<AgentOutcome>)flight.Task);
                    if (flight.Instance.IsSettled)
                        agentsInFlight--;
                    return;
                case FlightKind.Retry:
                    await flight.Task;
                    CallAgain(flight.Instance);
                    return;
                default:
                    await RenderedAsync(flight.Instance, await (Task<RenderOutcome>)flight.Task);
                    return;
            }
        }

        /// <summary>A render's outcome: a rendered node completes with its text; a deliverable settles composed (area 08 takes it from there).</summary>
        private async Task RenderedAsync(RunInstance instance, RenderOutcome outcome)
        {
            if (outcome.TooManySteps)
            {
                await FailAsync(instance, RunSentences.StoppedRendering(Subject(instance), limits.RenderSteps));
                return;
            }

            if (outcome.Failure is { } failed)
            {
                await FailAsync(instance, failed);
                return;
            }

            var text = JsonValue.Create(outcome.Text ?? "");
            await CompleteAsync(instance, text, instance.Entry.Kind == PlanKind.Deliverable ? RowState.Composed : RowState.Completed);
        }

        // ---- settling -----------------------------------------------------------------------

        /// <summary>Settles <paramref name="instance"/> with <paramref name="value"/>: its <c>.content</c> is its text for a text format, else the value's canonical JSON (D11).</summary>
        private Task CompleteAsync(RunInstance instance, JsonNode? value, RowState state)
        {
            var content = Content(instance.Entry, value);
            var read = new JsonObject
            {
                ["value"] = value?.DeepClone(),
                ["content"] = content,
                ["state"] = state == RowState.Composed ? "composed" : "completed",
            };
            return SettleAsync(instance, state, null, read, content == null ? null : CanonicalJson.HashText(content));
        }

        /// <summary>Fails <paramref name="instance"/>, and the job with it, with <paramref name="sentence"/>.</summary>
        private Task FailAsync(RunInstance instance, string sentence)
        {
            failure ??= (sentence, instance.Key);
            return SettleAsync(instance, RowState.Failed, sentence, RunScope.Absent("failed"), null);
        }

        /// <summary>Settles <paramref name="instance"/> and records its row.</summary>
        private Task SettleAsync(RunInstance instance, RowState state, string? reason, JsonObject read, string? valueHash)
        {
            instance.Settle(state, read, reason);
            pending.Remove(instance);
            var detail = new JsonObject { ["function"] = instance.Frame.Function, ["depth"] = instance.Frame.Depth };
            ItemNameDetail(instance.Frame, detail);
            if (instance.Entry.Kind == PlanKind.Agent)
                AgentDetail(instance, detail);
            else if (instance.Entry.Kind == PlanKind.For)
                LoopDetail(instance, detail);
            else if (instance.Entry.Kind == PlanKind.While)
                WhileDetail(instance, detail);
            else if (instance.Entry.Kind == PlanKind.Call)
                CallDetail(instance, detail);
            return recorder.RowAsync(new RunRow(instance.Key, instance.Entry.Key, instance.Entry.Kind, state, reason, valueHash, detail));
        }

        // ---- words --------------------------------------------------------------------------

        /// <summary>The <c>.content</c> of <paramref name="value"/> for <paramref name="entry"/>: <c>null</c> for <c>null</c>, the text for a text format, else canonical JSON.</summary>
        private string? Content(PlanEntry entry, JsonNode? value)
        {
            if (value is null)
                return null;
            if (plan.Formats.IsText(entry.Format) && value is JsonValue text && text.GetValueKind() == JsonValueKind.String)
                return text.GetValue<string>();
            return CanonicalJson.Write(value);
        }

        /// <summary>D4's <c>&lt;site&gt;</c> for an instance's <c>if</c> and its render: <c>Node 'id'</c>, <c>Check 'id'</c> or <c>Deliverable 'id'</c>, with <c> in function 'f'</c> inside one.</summary>
        private static string Subject(RunInstance instance) => RunSentences.Subject(instance.Entry, instance.Frame.Function);

        /// <summary>D4's <c>&lt;site&gt;</c> for a computed node's <c>expression</c>.</summary>
        private static string ComputedSubject(RunInstance instance) => $"Computed node '{instance.Entry.Id}'{InFunction(instance)}";

        private static string InFunction(RunInstance instance) =>
            instance.Frame.Function is { } function ? $" in function '{function}'" : "";

        /// <summary>A format's name in a sentence: the bundle key a <c>$ref</c> names, else <c>its format</c>.</summary>
        private static string FormatName(JsonNode? format) =>
            format is JsonObject { Count: 1 } site && SourceLayout.Text(site["$ref"]) is { } reference
            && reference.StartsWith("#/$defs/", StringComparison.Ordinal)
                ? reference["#/$defs/".Length..]
                : "its format";

        /// <summary>S09-011's words for an error's kind.</summary>
        private static string ErrorWords(string? kind) => kind switch
        {
            "timeout" => "timed out",
            "unreachable" => "couldn't be reached",
            _ => "returned an error",
        };

        /// <summary>A count or a limit as a sentence writes it: invariant, with thousands separated (<c>100,000</c>).</summary>
        private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
