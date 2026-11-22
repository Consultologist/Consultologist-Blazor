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
/// every top-level entry's settled read form (<c>{ value, content, state }</c>), by key; the agent calls made; the most
/// agent calls in flight at once (telemetry only, part 09 § 3.2); and #1505 (area 08) each deliverable's outcome, in plan
/// order, for those whose outcome was decided (<see cref="DeliverableOutcome"/>).
/// </summary>
public sealed record RunResult(
    RunState State,
    string? Sentence,
    string? FailedKey,
    IReadOnlyDictionary<string, JsonNode?> Values,
    int AgentCalls,
    int PeakInFlight,
    IReadOnlyList<DeliverableOutcome> Deliverables);

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
/// in a frame of its own under the same ceiling, <c>PlanRunner.Calls.cs</c>'s. #1504 (area 07; D3): the placements of an
/// agent's, a rendered node's or a deliverable's body are decided once its gate holds, and passed with its call,
/// <c>PlanRunner.Placements.cs</c>'s. #1505 (area 08; part 06 § 7): a deliverable's outcome (skipped, failed, held, then
/// once its gate settles blocked or released) is <c>PlanRunner.Deliverables.cs</c>'s. #1505 (D14): it tells the recorder
/// once of the job's 429 waits passing <see cref="CapacityAlertMinutes"/> and once of the job reaching
/// <see cref="SlowJobPercent"/> of its time, on a host timer cancelled with the others (<see cref="RunNotice"/>).
/// </summary>
/// <remarks>
/// Durable Functions drives it: it awaits no task but <see cref="IRunHost"/>'s (and the recorder's), reads no clock but
/// <see cref="IRunHost.UtcNow"/>, and iterates only in the fixed order or list order. It never throws on package content or
/// model output; a fault of the host or the recorder is theirs and passes through.
/// </remarks>
public sealed partial class PlanRunner
{
    /// <summary>
    /// #1505 (area 08; part 09 § 3.9; D14): the minutes of 429 waits, in all across a job's nodes, past which the operator is
    /// told once (<see cref="RunNotice"/> <c>capacity-wait</c>).
    /// </summary>
    public const int CapacityAlertMinutes = 5;

    /// <summary>
    /// #1505 (area 08; part 09 § 3.9; D14): the share of <see cref="RunLimits.JobMinutes"/>, in percent, at which the operator
    /// is told once that the job is slow (<see cref="RunNotice"/> <c>job-slow</c>).
    /// </summary>
    public const int SlowJobPercent = 80;

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
        private readonly FlushingRecorder recorder = new(runner.recorder);
        private readonly RunScope scope = new(inputs);
        private readonly RunFrame top = RunFrame.Top(runner.plan);

        /// <summary>
        /// The instances not settled, in the fixed order (<see cref="FixedOrder"/>), kept as they are made and settled so that
        /// a round never sorts the run again.
        /// </summary>
        private readonly SortedSet<RunInstance> pending = new(FixedOrder.Instance);

        /// <summary>How many instances the run has made.</summary>
        private int made;

        /// <summary>How many instances the run has opened, against <see cref="RunLimits.JobInstances"/>.</summary>
        private int opened;

        /// <summary>The instance most recently opened (it takes a top-level overflow of the instances limit).</summary>
        private RunInstance? lastOpened;

        /// <summary>The calls in flight, in the order dispatched.</summary>
        private readonly List<Flight> flights = [];

        private int agentCalls;
        private int agentsInFlight;
        private int peakInFlight;
        private (string? Sentence, string? Key)? failure;
        private bool timedOut;

        /// <summary>The host's timer for the job's time left, made the first time the run waits.</summary>
        private Task? jobTimer;

        /// <summary>
        /// #1505 (D14): the host's timer for <see cref="SlowJobPercent"/> of the job's time, made the first time the run waits
        /// and cancelled with the others; null again once it has ended and its notice was raised.
        /// </summary>
        private Task? slowTimer;

        /// <summary>#1505 (D14): whether the job-slow notice was raised, so it is raised once.</summary>
        private bool slowNoticed;

        /// <summary>Cancels every timer the run made (the job's and each retry's) when the run ends, on every path.</summary>
        private readonly CancellationTokenSource timers = new();

        /// <summary>When the orchestration started (the job's time limit runs from it).</summary>
        public DateTime StartedAtUtc { get; } = startedAtUtc;

        public async Task<RunResult> RunAsync()
        {
            foreach (var key in plan.TopLevel)
                await Add(top, plan.Entries[key]);

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
                    await recorder.FlushRoundAsync();

                    if (flights.Count == 0)
                    {
                        // Nothing in flight and nothing more settles: the run is over, or stuck (a document publish did not write).
                        if (Unsettled().FirstOrDefault() is { } stuck)
                            await FailAsync(stuck, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                        break;
                    }

                    jobTimer ??= host.DelayAsync(TimeLeft(), timers.Token);
                    if (!slowNoticed)
                        slowTimer ??= host.DelayAsync(TimeToSlow(), timers.Token);
                    var waits = flights.Select(f => f.Task).Append(jobTimer);
                    var finished = await Task.WhenAny(slowTimer == null ? waits : waits.Append(slowTimer));
                    if (ReferenceEquals(finished, jobTimer) || PastTheJobLimit())
                    {
                        // Nothing that finished at or after the limit is read.
                        await TimeOutAsync();
                        break;
                    }

                    if (ReferenceEquals(finished, slowTimer))
                    {
                        await SlowAsync();
                        await recorder.FlushRoundAsync();
                        continue;
                    }

                    var flight = flights.First(f => ReferenceEquals(f.Task, finished));
                    flights.Remove(flight);
                    await LandAsync(flight);
                    await recorder.FlushRoundAsync();
                }
            }
            catch (Exception) when (heldGates.Count > 0)
            {
                // #1506 (final review F2): an engine fault (the host's own exception) ends the run too; each gate's row still
                // held is recorded first, effect null, so the caller's last flush stores it. The fault propagates as it was:
                // a recording that throws here never replaces it.
                try
                {
                    await RecordHeldGatesAsync();
                }
                catch (Exception)
                {
                    // the original fault wins
                }

                throw;
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
                if (slowTimer != null)
                    Observe(slowTimer);
                timers.Dispose();
            }

            await RecordHeldGatesAsync();
            await recorder.FlushRoundAsync();

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
                peakInFlight,
                outcomes.OrderBy(o => o.Order).Select(o => o.Outcome).ToList());
        }

        // ---- the round ----------------------------------------------------------------------

        /// <summary>The instance of <paramref name="entry"/> in <paramref name="frame"/>, added to the run.</summary>
        private async Task Add(RunFrame frame, PlanEntry entry)
        {
            if (!await OpensAsync(frame.Owner))
                return;
            var instance = frame.Add(entry);
            instance.Sequence = made++;
            lastOpened = instance;
            pending.Add(instance);
        }

        /// <summary>
        /// #1506 (R2, D1): whether the run may open one more instance (a node, check or deliverable instance, a loop item, a
        /// pass, a call's body). Past <see cref="RunLimits.JobInstances"/> the instance is not opened and the job fails with
        /// A09-001 through <see cref="FailAsync"/>, which settles <paramref name="owner"/> (the loop, while, branch or call
        /// whose step it was) as failed and records its row, so the result's key names that row. A top-level overflow has no
        /// owner: the most recently opened instance still unsettled takes the failure, so the key is never null.
        /// </summary>
        private async Task<bool> OpensAsync(RunInstance? owner)
        {
            if (failure != null)
                return false;
            if (opened >= limits.JobInstances)
            {
                var target = owner is { IsSettled: false } ? owner : lastOpened is { IsSettled: false } ? lastOpened : owner ?? lastOpened;
                var sentence = RunSentences.JobInstances(limits.JobInstances);
                if (target != null && !target.IsSettled)
                    await FailAsync(target, sentence);
                else
                    failure = (sentence, target?.Key);
                return false;
            }

            opened++;
            return true;
        }

        /// <summary>The instances not settled, in the fixed order.</summary>
        private IEnumerable<RunInstance> Unsettled() => pending;

        /// <summary>
        /// Settles, in the fixed order, every ready instance that needs no call, then (#1505) decides the outcome of each
        /// composed deliverable whose gate has settled, until nothing more moves.
        /// </summary>
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

                if (failure == null && await DecideOutcomesAsync())
                    moved = true;
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
                    if (await GateAsync(instance) && await PlaceAsync(instance))
                        instance.Phase = InstancePhase.Waiting;
                    return true;
                case PlanKind.Computed:
                    if (await GateAsync(instance))
                    {
                        instance.StartedAtUtc ??= host.UtcNow;
                        await ComputeAsync(instance);
                    }

                    return true;
                case PlanKind.For:
                    instance.StartedAtUtc ??= host.UtcNow;
                    return await LoopAsync(instance);
                case PlanKind.While:
                    instance.StartedAtUtc ??= host.UtcNow;
                    return await WhileAsync(instance);
                case PlanKind.If or PlanKind.Case:
                    instance.StartedAtUtc ??= host.UtcNow;
                    return await BranchAsync(instance);
                case PlanKind.Call:
                    instance.StartedAtUtc ??= host.UtcNow;
                    return await CallAsync(instance);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Decides a leaf's or a deliverable's <c>if</c>: true when it runs. False or <c>null</c> skips it with S03-001's reason
        /// and records the decision; a failure fails it with part 03 § 11.3's sentence (#1505, D4: a gate check's, not the job).
        /// </summary>
        private async Task<bool> GateAsync(RunInstance instance)
        {
            var entry = instance.Entry;
            if (entry.If is not { } rule)
                return true;

            var data = scope.Data(entry, instance.Frame, entry.Reads);
            var decision = await DecideRuleAsync(instance, rule, "if", data);
            if (decision.Failure is { } failed)
            {
                await FailLeafAsync(instance, RuleFailed(Subject(instance), "if", failed));
                return false;
            }

            await RecordDecisionAsync(instance, instance.Frame, "self", entry.Rules.GetValueOrDefault("if"), entry.Printed("if"), data, decision.Result, null);
            if (decision.Result)
                return true;

            await SettleAsync(instance, RowState.Skipped, JsonLogicPrinter.Reason("skipped", entry.Json["if"], false), RunScope.Absent("skipped"), null);
            return false;
        }

        /// <summary>
        /// Evaluates a computed node inline; a value that does not fit its format fails it with A06-003 (04-D49: <c>null</c>
        /// is a value; #1505, D4: not for a gate check, whose value is a <c>check_result</c>, and whose failure is its own).
        /// </summary>
        private async Task ComputeAsync(RunInstance instance)
        {
            var entry = instance.Entry;
            if (entry.Expression is not { } expression)
            {
                await FailAsync(instance, ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence);
                return;
            }

            var data = scope.Data(entry, instance.Frame, entry.Reads);
            instance.EvaluatedOver = data;
            var value = await EvaluateRuleAsync(instance, expression, "expression", data);
            if (value.Failure is { } failed)
            {
                await FailLeafAsync(instance, RuleFailed(ComputedSubject(instance), "expression", failed));
                return;
            }

            if ((value.Value != null || entry.Gates.Count > 0) && !plan.Formats.Validate(value.Value, entry.Format).Valid)
            {
                await FailLeafAsync(instance, $"Node '{entry.Id}' computed a value that isn't {FormatName(entry.Format)}.");
                return;
            }

            await CompleteAsync(instance, value.Value);
        }

        // ---- rules (#1505, R6) --------------------------------------------------------------

        /// <summary>
        /// The one seam every rule the runner decides goes through: <paramref name="rule"/> (the instance's
        /// <paramref name="member"/>: <c>if</c>, <c>while</c>, <c>until</c> or a placement's site) as a condition over
        /// <paramref name="data"/>. A rule that holds <c>matches</c> or <c>has_word</c> is decided by the host
        /// (<see cref="IRunHost.EvaluateAsync"/>, awaited here; it takes no slot), so a replay reads the stored outcome rather
        /// than timing the pattern again; every other rule inline.
        /// </summary>
        private async Task<JsonLogicDecision> DecideRuleAsync(RunInstance instance, IJsonLogicRule rule, string member, JsonObject data) =>
            rule.ReadsPattern ? RuleTurn.DecisionOf(await host.EvaluateAsync(RuleCallOf(instance, member, data))) : rule.Decide(data);

        /// <summary>As <see cref="DecideRuleAsync"/>, for a computed node's <c>expression</c>: its value.</summary>
        private async Task<JsonLogicValue> EvaluateRuleAsync(RunInstance instance, IJsonLogicRule rule, string member, JsonObject data) =>
            rule.ReadsPattern ? RuleTurn.ValueOf(await host.EvaluateAsync(RuleCallOf(instance, member, data))) : rule.Evaluate(data);

        private static RuleCall RuleCallOf(RunInstance instance, string member, JsonObject data) =>
            new(instance.Key, instance.Entry.Key, instance.Frame.Function, member, data);

        /// <summary>
        /// #1506 (area 09; part 08 § 5; R6, D4): records a decision of <paramref name="instance"/> (its owner) at
        /// <paramref name="site"/>: <paramref name="rule"/>'s reads, each with the hash of its value in <paramref name="data"/>
        /// (the data the rule was decided over, inline or sent to the host), and the flags of what they read
        /// (<see cref="RunScope.Lineage"/>, read in <paramref name="frame"/>, where the rule was decided). A rule the plan does
        /// not hold (never, for a plan <see cref="RunPlan.Build"/> made) records no reads and both flags.
        /// </summary>
        private Task RecordDecisionAsync(
            RunInstance instance, RunFrame frame, string site, PlanRule? rule, string condition, JsonObject data, bool? result, string? branch)
        {
            var reads = new List<RunRead>();
            var (runDependent, modelDerived) = rule == null ? (true, true) : (false, false);
            foreach (var path in rule?.Reads ?? [])
            {
                reads.Add(new RunRead(path, CanonicalJson.Hash(RunScope.At(data, path))));
                var (run, model) = RunScope.Lineage(instance.Entry, frame, path);
                runDependent |= run;
                modelDerived |= model;
            }

            return recorder.DecisionAsync(new RunDecision(instance.Key, site, condition, rule?.ExpressionHash ?? CanonicalJson.Hash(null),
                result, branch, reads, runDependent, modelDerived, host.UtcNow));
        }

        /// <summary>
        /// Part 03 § 11.3's sentence for a rule that failed at run time; a host that answered from a plan not the job's
        /// (<see cref="RuleTurn.PlanFailure"/>) is S07-006, as an agent call's is.
        /// </summary>
        private string RuleFailed(string subject, string member, JsonLogicFailure failed) =>
            ReferenceEquals(failed, RuleTurn.NotTheJobsPlan)
                ? ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence
                : ExpressionSentences.RunTime(subject, member, failed);

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
                    instance.StartedAtUtc ??= host.UtcNow;
                    var render = new RenderCall(instance.Key, entry.Key, instance.Frame.Function,
                        scope.Data(entry, instance.Frame, CallReads(entry)), limits.RenderSteps, instance.Placed);
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
                instance.StartedAtUtc ??= host.UtcNow;
                var call = new AgentCall(instance.Key, entry.Key, instance.Frame.Function, 1, false,
                    scope.Data(entry, instance.Frame, CallReads(entry)), instance.Placed);
                StartAgent(instance, call);
            }
        }

        /// <summary>
        /// What a call's scope holds: exactly the entry's compiled <c>reads</c> (its body's and its header's, as publish wrote
        /// them), and (#1504, D4) the run-neutral text of each node its run spots read so (<see cref="PlanEntry.NeutralReads"/>),
        /// never more, since Durable keeps an activity's input in its history. <see cref="RunPlan.Build"/> has checked
        /// <c>reads</c> is a list of strings.
        /// </summary>
        private static IEnumerable<string> CallReads(PlanEntry entry) =>
            (entry.Json["reads"] is JsonArray reads ? reads.Select(r => SourceLayout.Text(r)).OfType<string>() : []).Concat(entry.NeutralReads);

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

        /// <summary>
        /// A render's outcome: a rendered node completes with its text; a deliverable is composed (#1505:
        /// <c>ComposedAsync</c>, which decides what it can of its outcome). #1504 (part 05 § 4; part 08 §§ 8.2, 10): its row
        /// carries the outcome's detail, and its read form its run-neutral text as <c>neutral</c> when it has one.
        /// </summary>
        private async Task RenderedAsync(RunInstance instance, RenderOutcome outcome)
        {
            instance.CallDetail = outcome.Detail;
            instance.CallProduced = outcome.Produced;
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

            if (instance.Entry.Kind == PlanKind.Deliverable)
            {
                await ComposedAsync(instance, outcome.Text ?? "", outcome.NeutralText);
                return;
            }

            await CompleteAsync(instance, JsonValue.Create(outcome.Text ?? ""), outcome.NeutralText);
        }

        // ---- settling -----------------------------------------------------------------------

        /// <summary>
        /// Settles <paramref name="instance"/> with <paramref name="value"/>: its <c>.content</c> is its text for a text format,
        /// else the value's canonical JSON (D11); #1504 (D4): with <paramref name="neutral"/>, a render's run-neutral text, as
        /// its <c>.neutral</c>.
        /// </summary>
        private Task CompleteAsync(RunInstance instance, JsonNode? value, string? neutral = null)
        {
            var content = Content(instance.Entry, value);
            var read = new JsonObject
            {
                ["value"] = value?.DeepClone(),
                ["content"] = content,
                ["state"] = "completed",
            };
            if (neutral != null)
                read["neutral"] = neutral;
            return SettleAsync(instance, RowState.Completed, null, read, content == null ? null : CanonicalJson.HashText(content));
        }

        /// <summary>
        /// Fails <paramref name="instance"/>, and the job with it, with <paramref name="sentence"/>; #1505: a deliverable that
        /// composed (empty, under <c>whenEmpty: fail</c>) keeps its composed hash for the record.
        /// </summary>
        private Task FailAsync(RunInstance instance, string sentence)
        {
            failure ??= (sentence, instance.Key);
            return SettleAsync(instance, RowState.Failed, sentence, RunScope.Absent("failed"), instance.ComposedHash);
        }

        /// <summary>
        /// Settles <paramref name="instance"/> and records its row: #1506 (area 09; part 08 § 4; D2) its detail the entry's
        /// facts (<see cref="EntryDetail"/>), its item's name (<see cref="ItemNameDetail"/>) and its kind's members, and its
        /// settled text beside it (<see cref="SettledText"/>), never in the detail. A computed row has its two hashes
        /// (<see cref="ComputedHashes"/>, D9) and a check's row part 08 § 6's members (<see cref="CheckDetail"/>). #1506 (D7): a
        /// gate check's row is held until the last deliverable it gates has settled (<see cref="ReleaseGateAsync"/>), so its
        /// <c>effect</c> is written on its one row; a deliverable's row, once recorded, may release its gate's.
        /// </summary>
        private async Task SettleAsync(RunInstance instance, RowState state, string? reason, JsonObject read, string? valueHash)
        {
            instance.Settle(state, read, reason);
            pending.Remove(instance);
            var detail = new JsonObject { ["function"] = instance.Frame.Function, ["depth"] = instance.Frame.Depth };
            EntryDetail(instance.Entry, detail);
            ClockDetail(detail, state, instance.StartedAtUtc);
            var itemName = ItemNameDetail(instance.Frame, detail);
            string? spotsIn = null;
            if (instance.Entry.Kind == PlanKind.Agent)
            {
                AgentDetail(instance, detail);
                spotsIn = SourceLayout.Text(instance.CallProduced?["sent"]);
            }
            else if (instance.Entry.Kind == PlanKind.Rendered)
            {
                Merge(instance.CallDetail, detail);
                spotsIn = SourceLayout.Text(read["content"]);
            }
            else if (instance.Entry.Kind == PlanKind.Deliverable)
            {
                DeliverableSettled(instance, state, reason, detail);
                spotsIn = instance.ComposedText;
            }
            else if (instance.Entry.Kind == PlanKind.For)
            {
                LoopDetail(instance, detail);
            }
            else if (instance.Entry.Kind == PlanKind.While)
            {
                WhileDetail(instance, detail);
            }
            else if (instance.Entry.Kind == PlanKind.Call)
            {
                CallDetail(instance, detail);
            }
            else if (instance.Entry.Kind == PlanKind.Computed)
            {
                ComputedHashes(instance, detail);
            }

            var text = SettledText(read, itemName, RecordRows.SpotTexts(detail["runSpots"], spotsIn));
            if (instance.Entry.Kind == PlanKind.Deliverable && instance.ComposedText is { } composed)
            {
                // #1506 (R1): a deliverable that composed keeps its composed text, released, held, blocked or failed empty
                text ??= [];
                text["content"] = composed;
            }

            if (IsCheck(instance.Key))
                text = WithLists(text, CheckDetail(instance, read, detail));
            var row = new RunRow(instance.Key, instance.Entry.Key, instance.Entry.Kind, state, reason, valueHash, detail, text);
            if (instance.Entry.Gates.Count > 0)
            {
                heldGates.Add((instance, row));
                await ReleaseGateAsync(instance);
                return;
            }

            await recorder.RowAsync(row);
            if (instance.Entry.Kind == PlanKind.Deliverable && instance.Entry.Check is { } check && instance.Frame.Find(check) is { } gate)
                await ReleaseGateAsync(gate);
        }

        /// <summary>
        /// #1506 (area 09; part 08 § 4; final review F3): a row's clock members, storage 11's: <c>startedAtUtc</c> (the host's
        /// clock when it started, <paramref name="startedAtUtc"/>), <c>completedAtUtc</c> (the host's clock now, as it settles)
        /// and <c>durationMs</c> (their difference in whole milliseconds), as round-trip UTC text. All three <c>null</c> for a
        /// row that never started: skipped, not run, or failed before it started (a limit refused it).
        /// </summary>
        private void ClockDetail(JsonObject detail, RowState state, DateTime? startedAtUtc)
        {
            if (state is RowState.Skipped or RowState.NotRun || startedAtUtc is not { } started)
            {
                detail["startedAtUtc"] = null;
                detail["completedAtUtc"] = null;
                detail["durationMs"] = null;
                return;
            }

            var completed = host.UtcNow;
            detail["startedAtUtc"] = RecordRows.Clock(started);
            detail["completedAtUtc"] = RecordRows.Clock(completed);
            detail["durationMs"] = RecordRows.DurationMs(started, completed);
        }

        /// <summary>
        /// #1506 (area 09; part 08 § 4; D9): a computed row's <c>inputHash</c>, over its <c>expression</c> and the values it
        /// read (its rule's reads, each hashed from the data it was evaluated over), and when the row is run-dependent its
        /// <c>runNeutralHash</c>, the same with the run-dependent reads' hashes left out (<see cref="RunScope.Lineage"/>, read
        /// in its frame). Neither when the expression was never evaluated (skipped by its <c>if</c>, never reached).
        /// </summary>
        private static void ComputedHashes(RunInstance instance, JsonObject detail)
        {
            if (instance.EvaluatedOver is not { } data || !instance.Entry.Rules.TryGetValue("expression", out var rule))
                return;
            var reads = rule.Reads.Select(path => new RunRead(path, CanonicalJson.Hash(RunScope.At(data, path)))).ToList();
            detail["inputHash"] = RecordRows.ComputedInputHash(rule.Json, reads);
            if (IsTrue(detail["runDependent"]))
            {
                var dependent = reads.Where(r => RunScope.Lineage(instance.Entry, instance.Frame, r.Path).RunDependent).Select(r => r.Path);
                detail["runNeutralHash"] = RecordRows.ComputedNeutralHash(rule.Json, reads, dependent);
            }
        }

        /// <summary>#1506 (area 09; part 08 § 2.2): whether a row key is in the record's checks section (its root is <c>check.</c>).</summary>
        private static bool IsCheck(string key) => key.StartsWith("check.", StringComparison.Ordinal);

        /// <summary>
        /// #1506 (area 09; part 08 § 6; D7, D8): a check's row members from what only the runner knows: <c>alignedWith</c>
        /// (<see cref="AlignedWith(RunFrame)"/>), <c>checked</c> and <c>reads</c> (<see cref="CheckReads"/>, over the data its
        /// expression was evaluated over, else its reads now), its <c>check_result</c>'s members (<see cref="RecordRows.CheckResult"/>)
        /// and its <c>effect</c>: for a check that gates nothing, <c>read by &lt;its first reader&gt;</c> or <c>null</c>
        /// (<see cref="ReadBy"/>); a gate's is written when its row is released. Gives back the result's lists for the
        /// settled text.
        /// </summary>
        private JsonObject? CheckDetail(RunInstance instance, JsonObject read, JsonObject detail)
        {
            var entry = instance.Entry;
            detail["alignedWith"] = AlignedWith(instance.Frame);
            var (target, reads) = CheckReads(instance, instance.EvaluatedOver ?? scope.Data(entry, instance.Frame, entry.Reads));
            detail["checked"] = target;
            detail["reads"] = reads;
            var lists = RecordRows.CheckResult(read["value"], detail);
            detail["effect"] = entry.Gates.Count > 0 ? null : ReadBy(entry, instance.Frame.Map);
            return lists;
        }

        /// <summary>
        /// #1506 (area 09; part 08 § 6; D8): what a check ran on and its other reads. <c>checked</c> is the one deliverable its
        /// reads name, with its composed hash; with none, the one node they name, with the hash of its <c>.content</c>; else
        /// <c>null</c>. <c>reads</c> is every other path it reads (<see cref="CompiledReads"/>, in path order), each with the
        /// canonical hash of the value in <paramref name="data"/>.
        /// </summary>
        private static (JsonObject? Checked, JsonArray Reads) CheckReads(RunInstance instance, JsonObject data)
        {
            var map = instance.Frame.Map;
            var named = CompiledReads(instance.Entry).Select(path => (Path: path, Key: NamedKey(map, path))).ToList();
            var deliverables = named.Select(n => n.Key).OfType<string>().Where(k => k.StartsWith("deliverable.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            var nodes = named.Select(n => n.Key).OfType<string>().Where(k => k.StartsWith("node.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            var target = deliverables.Count == 1 ? deliverables[0] : deliverables.Count == 0 && nodes.Count == 1 ? nodes[0] : null;

            JsonObject? found = null;
            if (target != null)
            {
                var standing = instance.Frame.Find(target);
                var hash = standing == null ? null
                    : standing.Entry.Kind == PlanKind.Deliverable ? standing.ComposedHash
                    : SourceLayout.Text(standing.Read?["content"]) is { } content ? CanonicalJson.HashText(content)
                    : null;
                found = new JsonObject { ["ref"] = standing?.Key ?? target, ["hash"] = hash };
            }

            var reads = new JsonArray();
            foreach (var (path, key) in named)
            {
                if (target != null && key == target)
                    continue;
                reads.Add(new JsonObject { ["path"] = path, ["hash"] = CanonicalJson.Hash(RunScope.At(data, path)) });
            }

            return (found, reads);
        }

        /// <summary>
        /// #1506 (area 09; D7, D8): an entry's compiled reads, its <c>reads</c> as publish wrote them (in order, each path once,
        /// none under another); <see cref="PlanEntry.Reads"/> when it has none.
        /// </summary>
        private static IReadOnlyList<string> CompiledReads(PlanEntry entry) =>
            entry.Json["reads"] is JsonArray reads
                ? reads.Select(r => SourceLayout.Text(r)).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
                : entry.Reads;

        /// <summary>The key of <paramref name="map"/> a read of <paramref name="path"/> names: the longest key that is the path or a prefix of it at a dot; null for none.</summary>
        private static string? NamedKey(IReadOnlyDictionary<string, PlanEntry> map, string path)
        {
            var segments = path.Split('.');
            for (var length = segments.Length; length >= 2; length--)
            {
                var key = string.Join('.', segments, 0, length);
                if (map.ContainsKey(key))
                    return key;
            }

            return null;
        }

        /// <summary>#1506 (D7): each check entry's <c>read by</c> effect once worked out, by entry (reference equality: only looked up).</summary>
        private readonly Dictionary<PlanEntry, string?> readers = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// #1506 (area 09; part 08 § 6; D7): <c>read by &lt;key&gt;</c>, the first in key order of the entries of
        /// <paramref name="map"/> whose compiled reads (<see cref="CompiledReads"/>) name <paramref name="check"/>; <c>null</c>
        /// when none does.
        /// </summary>
        private string? ReadBy(PlanEntry check, IReadOnlyDictionary<string, PlanEntry> map)
        {
            if (readers.TryGetValue(check, out var known))
                return known;
            string? first = null;
            foreach (var (key, entry) in map)
            {
                if (ReferenceEquals(entry, check) || (first != null && string.CompareOrdinal(key, first) >= 0))
                    continue;
                if (CompiledReads(entry).Any(path => NamedKey(map, path) == check.Key))
                    first = key;
            }

            var effect = first == null ? null : $"read by {first}";
            readers[check] = effect;
            return effect;
        }

        /// <summary>#1506 (area 09; part 08 §§ 6, 11): <paramref name="text"/> with a check result's lists (<paramref name="lists"/>) added.</summary>
        private static JsonObject? WithLists(JsonObject? text, JsonObject? lists)
        {
            if (lists == null)
                return text;
            text ??= [];
            foreach (var (name, list) in lists)
                text[name] = list?.DeepClone();
            return text;
        }

        /// <summary>
        /// #1506 (area 09; part 08 §§ 4, 4.1): the entry's facts on its row, as <c>resolve.json</c> writes them: <c>label</c>,
        /// <c>producer</c>, the three flags (an agent row always <c>modelDerived</c>), <c>formatHash</c> and <c>typeHash</c>
        /// (<c>null</c> for a flow kind), and on an agent row its <c>agent</c>: the binding's attested agent and the binding.
        /// </summary>
        private void EntryDetail(PlanEntry entry, JsonObject detail)
        {
            var json = entry.Json;
            var flow = entry.Kind is PlanKind.For or PlanKind.While or PlanKind.If or PlanKind.Case or PlanKind.Call;
            detail["label"] = SourceLayout.Text(json["label"]) ?? entry.Id;
            detail["producer"] = SourceLayout.Text(json["producer"]) ?? (entry.Kind == PlanKind.Agent ? "generated" : "computed");
            detail["runDependent"] = IsTrue(json["runDependent"]);
            detail["modelDerived"] = entry.Kind == PlanKind.Agent || IsTrue(json["modelDerived"]);
            detail["profileCarrying"] = IsTrue(json["profileCarrying"]);
            detail["formatHash"] = flow ? null : SourceLayout.Text(json["formatHash"]);
            detail["typeHash"] = flow ? null : SourceLayout.Text(json["typeHash"]);
            if (entry.Kind == PlanKind.Agent)
                detail["agent"] = AgentOf(entry);
        }

        /// <summary>
        /// Part 08 § 4.1's <c>agent</c> of an agent entry: <c>{ name, version, binding { format, producer, via } }</c> from the
        /// plan's <c>bindings.&lt;format key&gt;.&lt;producer&gt;</c>; <c>null</c> when the plan names none (the call then failed).
        /// </summary>
        private JsonObject? AgentOf(PlanEntry entry)
        {
            const string defs = "#/$defs/";
            if (entry.Format is not JsonObject format || SourceLayout.Text(format["$ref"]) is not { } reference
                || !reference.StartsWith(defs, StringComparison.Ordinal) || SourceLayout.Text(entry.Json["producer"]) is not { } producer
                || ((plan.Document["bindings"] as JsonObject)?[reference[defs.Length..]] as JsonObject)?[producer] is not JsonObject binding
                || SourceLayout.Text(binding["agentName"]) is not { } name)
            {
                return null;
            }

            JsonNode? version = binding["agentVersion"] switch
            {
                JsonValue number when number.GetValueKind() == JsonValueKind.Number && number.TryGetValue<int>(out var n) => n,
                JsonValue text when text.GetValueKind() == JsonValueKind.String
                    && int.TryParse(text.GetValue<string>(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => null,
            };
            return new JsonObject
            {
                ["name"] = name,
                ["version"] = version,
                ["binding"] = new JsonObject { ["format"] = reference[defs.Length..], ["producer"] = producer, ["via"] = SourceLayout.Text(binding["via"]) },
            };
        }

        private static bool IsTrue(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;

        /// <summary>
        /// #1506 (area 09; D2): a row's settled text, for the retained text and never the row: <c>content</c> (the read form's
        /// <c>.content</c>), <c>value</c> (its <c>.value</c>'s canonical JSON when that is not text), <c>itemName</c> (a name not
        /// drawn from package text, <paramref name="itemName"/>) and <c>spots</c> (<paramref name="spots"/>); <c>null</c> when
        /// there is none.
        /// </summary>
        private static JsonObject? SettledText(JsonObject read, string? itemName, JsonObject? spots)
        {
            var text = new JsonObject();
            if (SourceLayout.Text(read["content"]) is { } content)
                text["content"] = content;
            if (read["value"] is { } value && !(value is JsonValue v && v.GetValueKind() == JsonValueKind.String))
                text["value"] = CanonicalJson.Write(value);
            if (itemName != null)
                text["itemName"] = itemName;
            if (spots != null)
                text["spots"] = spots;
            return text.Count == 0 ? null : text;
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
