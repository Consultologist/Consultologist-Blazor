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
/// nothing is in flight and nothing is ready.
/// </summary>
/// <remarks>
/// Durable Functions drives it: it awaits no task but <see cref="IRunHost"/>'s (and the recorder's), reads no clock but
/// <see cref="IRunHost.UtcNow"/>, and iterates only in the fixed order or list order. It never throws on package content or
/// model output; a fault of the host or the recorder is theirs and passes through. Tasks 5 to 8 add retries,
/// <c>for</c>, <c>while</c>/<c>if</c>/<c>case</c> and <c>call</c> as the other parts of this partial class.
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

    /// <summary>A call in flight: the instance and the host's task.</summary>
    private sealed record Flight(RunInstance Instance, Task Task, bool Agent);

    /// <summary>One run: its frames and instances, what is in flight, its counters and its failure.</summary>
    private sealed partial class Run(PlanRunner runner, RunInputs inputs, DateTime startedAtUtc)
    {
        private readonly RunPlan plan = runner.plan;
        private readonly RunLimits limits = runner.limits;
        private readonly IRunHost host = runner.host;
        private readonly IRunRecorder recorder = runner.recorder;
        private readonly RunScope scope = new(inputs);
        private readonly RunFrame top = RunFrame.Top(runner.plan);

        /// <summary>Every instance of the run, in the order made.</summary>
        private readonly List<RunInstance> instances = [];

        /// <summary>The calls in flight, in the order dispatched.</summary>
        private readonly List<Flight> flights = [];

        private int agentCalls;
        private int agentsInFlight;
        private int peakInFlight;
        private (string? Sentence, string Key)? failure;

        /// <summary>When the orchestration started (the job's time limit runs from it).</summary>
        public DateTime StartedAtUtc { get; } = startedAtUtc;

        public async Task<RunResult> RunAsync()
        {
            foreach (var key in plan.TopLevel)
                Add(top, plan.Entries[key]);

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

                var finished = await Task.WhenAny(flights.Select(f => f.Task));
                var flight = flights.First(f => ReferenceEquals(f.Task, finished));
                flights.Remove(flight);
                await LandAsync(flight);
            }

            var values = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            foreach (var instance in top.Instances)
            {
                if (instance.Read is { } read)
                    values[instance.Key] = read.DeepClone();
            }

            return new RunResult(
                failure == null ? RunState.Completed : RunState.Failed,
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
            instances.Add(instance);
            return instance;
        }

        /// <summary>The instances not settled, in the fixed order.</summary>
        private IEnumerable<RunInstance> Unsettled() =>
            instances.Where(i => !i.IsSettled).OrderBy(i => i.Rank, RankComparer.Instance);

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
                    if (IsReady(instance) && await StepAsync(instance))
                        moved = true;
                }
            }
            while (moved && failure == null);
        }

        /// <summary>
        /// Whether every key <paramref name="instance"/>'s entry depends on that is visible from its frame has settled. Its
        /// own descendants (a flow node's rule reading its body) are never waited on.
        /// </summary>
        private static bool IsReady(RunInstance instance)
        {
            var entry = instance.Entry;
            foreach (var key in entry.Depends)
            {
                if (IsDescendant(instance.Frame.Map, key, entry.Key))
                    continue;
                if (instance.Frame.Find(key) is { IsSettled: false })
                    return false;
            }

            return true;
        }

        /// <summary>Whether <paramref name="key"/>'s chain of parents in <paramref name="map"/> reaches <paramref name="ancestor"/> (bounded by the map's size).</summary>
        private static bool IsDescendant(IReadOnlyDictionary<string, PlanEntry> map, string key, string ancestor)
        {
            var steps = 0;
            for (var at = map.GetValueOrDefault(key)?.Parent; at != null && steps++ <= map.Count; at = map.GetValueOrDefault(at)?.Parent)
            {
                if (at == ancestor)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Moves a ready instance on with no call: decides its <c>if</c>, evaluates a computed node, and leaves an agent or
        /// a render waiting for dispatch. False when it did not move (a flow node: Tasks 6 to 8).
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
                        scope.Data(entry, instance.Frame, entry.Reads), limits.RenderSteps);
                    flights.Add(new Flight(instance, host.RenderAsync(render), Agent: false));
                    continue;
                }

                if (agentsInFlight >= limits.MaxAgentCalls || !CapsAllow(instance))
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
                instance.Attempt = 1;
                var call = new AgentCall(instance.Key, entry.Key, instance.Frame.Function, instance.Attempt, false,
                    scope.Data(entry, instance.Frame, entry.Reads));
                flights.Add(new Flight(instance, host.CallAgentAsync(call), Agent: true));
            }
        }

        /// <summary>Whether the <c>maxParallel</c> of every loop around <paramref name="instance"/> allows one more call (Task 6; at the top level there is none).</summary>
        private static bool CapsAllow(RunInstance instance) => true;

        /// <summary>Settles a call that finished.</summary>
        private async Task LandAsync(Flight flight)
        {
            if (flight.Agent)
            {
                agentsInFlight--;
                await AgentAsync(flight.Instance, await (Task<AgentOutcome>)flight.Task);
            }
            else
            {
                await RenderedAsync(flight.Instance, await (Task<RenderOutcome>)flight.Task);
            }
        }

        /// <summary>
        /// An agent call's outcome: a reply is read as its format's value (part 02: a text format's reply is the text). A
        /// throttled or failed attempt fails the node with part 09 § 9's sentence; Task 5 adds the retries before it, and
        /// what an invalid reply does (<c>onInvalid</c>).
        /// </summary>
        private async Task AgentAsync(RunInstance instance, AgentOutcome outcome)
        {
            switch (outcome.Kind)
            {
                case AgentOutcomeKind.Completed:
                    var (value, parsed) = plan.Formats.Read(outcome.Reply ?? "", instance.Entry.Format);
                    if (!parsed)
                    {
                        await FailAsync(instance, null); // Task 5: the reply goes to onInvalid
                        return;
                    }

                    await CompleteAsync(instance, value, RowState.Completed);
                    return;
                case AgentOutcomeKind.Throttled:
                    await FailAsync(instance, $"The model service stayed busy for {Count(limits.CapacityWaitMinutes)} minutes. Run the workflow again later.");
                    return;
                default:
                    await FailAsync(instance,
                        $"The model service {ErrorWords(outcome.ErrorKind)} {Count(instance.Attempt)} times in a row. Run the workflow again later.");
                    return;
            }
        }

        /// <summary>A render's outcome: a rendered node completes with its text; a deliverable settles composed (area 08 takes it from there).</summary>
        private async Task RenderedAsync(RunInstance instance, RenderOutcome outcome)
        {
            if (outcome.TooManySteps)
            {
                await FailAsync(instance, $"{Subject(instance)} stopped rendering: it took more than {Count(limits.RenderSteps)} steps.");
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
        private Task FailAsync(RunInstance instance, string? sentence)
        {
            failure ??= (sentence, instance.Key);
            return SettleAsync(instance, RowState.Failed, sentence, RunScope.Absent("failed"), null);
        }

        /// <summary>Settles <paramref name="instance"/> and records its row.</summary>
        private Task SettleAsync(RunInstance instance, RowState state, string? reason, JsonObject read, string? valueHash)
        {
            instance.Settle(state, read);
            var detail = new JsonObject { ["function"] = instance.Frame.Function, ["depth"] = instance.Frame.Depth };
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
        private static string Subject(RunInstance instance)
        {
            var entry = instance.Entry;
            var word = entry.Kind == PlanKind.Deliverable ? "Deliverable"
                : entry.Key.StartsWith("check.", StringComparison.Ordinal) ? "Check"
                : "Node";
            return $"{word} '{entry.Id}'{InFunction(instance)}";
        }

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
