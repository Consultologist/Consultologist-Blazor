using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06): a scripted <see cref="IRunHost"/>. An agent call's outcome and a render's are scripted
/// by the call's <c>Key</c> or, failing that, its <c>EntryKey</c>, an agent's as a sequence (each attempt takes the next
/// outcome, the last repeating); an unscripted agent replies what <see cref="Stub"/> gives, else <c>"Text of &lt;key&gt;."</c>,
/// and an unscripted render gives <c>"Rendered &lt;key&gt;."</c>. In auto mode every answer is given at once; otherwise each
/// is held until the test releases it (<see cref="Release"/>, <see cref="ReleaseAll"/>, <see cref="ReleaseInOrder"/>).
/// The clock is manual (<see cref="Advance"/>), and a delay ends when the clock passes it, or ends cancelled when its token is
/// cancelled first (<see cref="OutstandingDelays"/> counts those still waiting). Releasing completes the task
/// inline, so a test driving the runner off any synchronization context sees each release's effects before it goes on
/// (<see cref="Drive"/>). #1505 (R6): a rule's evaluation is answered at once, never held: scripted by the call's
/// <c>Key</c> and <c>Member</c> (<see cref="Rule"/>), else by <see cref="RuleTurn.Evaluate"/> over <paramref name="plan"/>, as the
/// engine's activity answers; with no plan an unscripted evaluation fails the test.
/// </summary>
internal sealed class FakeRunHost(bool auto = false, RunPlan? plan = null) : IRunHost
{
    private readonly Dictionary<string, AgentOutcome[]> agentScript = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> agentServed = new(StringComparer.Ordinal);
    private readonly HashSet<string> faults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RenderOutcome> renderScript = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Key, string Member), RuleOutcome> ruleScript = new();
    private readonly List<(string Key, Action Answer)> held = [];
    private readonly List<(DateTime Due, TaskCompletionSource Done)> timers = [];
    private readonly List<(string Key, bool Starts)> events = [];
    private Func<AgentCall, string>? stub;

    /// <summary>The time the clock starts at.</summary>
    public static readonly DateTime Start = new(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc/>
    public DateTime UtcNow { get; private set; } = Start;

    /// <summary>The agent calls made, in order.</summary>
    public List<AgentCall> Calls { get; } = [];

    /// <summary>The keys of the agent calls made, in order.</summary>
    public List<string> Dispatched => Calls.Select(c => c.Key).ToList();

    /// <summary>The renders asked for, in order.</summary>
    public List<RenderCall> Renders { get; } = [];

    /// <summary>#1505 (R6): the rule evaluations asked for, in order.</summary>
    public List<RuleCall> Rules { get; } = [];

    /// <summary>The most agent calls held at once.</summary>
    public int InFlightPeak => PeakOf(_ => true);

    /// <summary>The keys of the calls still held.</summary>
    public IReadOnlyList<string> Held => held.Select(h => h.Key).ToList();

    /// <summary>Scripts the reply of the agent call keyed <paramref name="key"/> (a row key or an entry key).</summary>
    public FakeRunHost Reply(string key, string reply) => Agent(key, new AgentOutcome(AgentOutcomeKind.Completed, reply, 0, null));

    /// <summary>Scripts the outcomes of the agent call keyed <paramref name="key"/>: each attempt the next, the last repeating.</summary>
    public FakeRunHost Agent(string key, params AgentOutcome[] outcomes)
    {
        Assert.NotEmpty(outcomes);
        agentScript[key] = outcomes;
        return this;
    }

    /// <summary>Makes <paramref name="reply"/> the reply of every agent call not scripted otherwise.</summary>
    public FakeRunHost Stub(Func<AgentCall, string> reply)
    {
        stub = reply;
        return this;
    }

    /// <summary>Scripts the agent call keyed <paramref name="key"/> (a row key) to fault when its answer is given: the host's own failure.</summary>
    public FakeRunHost Fault(string key)
    {
        faults.Add(key);
        return this;
    }

    /// <summary>Scripts the outcome of the render keyed <paramref name="key"/>.</summary>
    public FakeRunHost Render(string key, RenderOutcome outcome)
    {
        renderScript[key] = outcome;
        return this;
    }

    /// <summary>#1505 (R6): scripts the outcome of the rule <paramref name="member"/> of the row keyed <paramref name="key"/>.</summary>
    public FakeRunHost Rule(string key, string member, RuleOutcome outcome)
    {
        ruleScript[(key, member)] = outcome;
        return this;
    }

    /// <summary>The most agent calls held at once among those whose key <paramref name="belongs"/> (one loop instance's, say).</summary>
    public int PeakOf(Func<string, bool> belongs)
    {
        int now = 0, peak = 0;
        foreach (var (key, starts) in events.Where(e => belongs(e.Key)))
        {
            now += starts ? 1 : -1;
            peak = Math.Max(peak, now);
        }

        return peak;
    }

    /// <summary>
    /// The most groups with an agent call held at once, each call's group being what <paramref name="group"/> gives for its
    /// key (null: in none), as the outer items of a loop instance with a call in flight.
    /// </summary>
    public int PeakGroupsOf(Func<string, string?> group)
    {
        var inFlight = new Dictionary<string, int>(StringComparer.Ordinal);
        var peak = 0;
        foreach (var (key, starts) in events)
        {
            if (group(key) is not { } named)
                continue;
            inFlight[named] = inFlight.GetValueOrDefault(named) + (starts ? 1 : -1);
            peak = Math.Max(peak, inFlight.Count(p => p.Value > 0));
        }

        return peak;
    }

    /// <inheritdoc/>
    public Task<AgentOutcome> CallAgentAsync(AgentCall call)
    {
        Calls.Add(call);
        events.Add((call.Key, true));
        var outcome = Next(call.Key) ?? Next(call.EntryKey)
            ?? new AgentOutcome(AgentOutcomeKind.Completed, stub?.Invoke(call) ?? $"Text of {call.Key}.", 0, null);
        var answer = new TaskCompletionSource<AgentOutcome>();
        return Hold(call.Key, () =>
        {
            events.Add((call.Key, false));
            if (faults.Contains(call.Key))
                answer.SetException(new InvalidOperationException($"The fake host failed {call.Key}."));
            else
                answer.SetResult(outcome);
        }, answer.Task);
    }

    /// <inheritdoc/>
    public Task<RenderOutcome> RenderAsync(RenderCall call)
    {
        Renders.Add(call);
        var outcome = renderScript.GetValueOrDefault(call.Key)
            ?? renderScript.GetValueOrDefault(call.EntryKey)
            ?? new RenderOutcome($"Rendered {call.Key}.", false, null);
        var answer = new TaskCompletionSource<RenderOutcome>();
        return Hold(call.Key, () => answer.SetResult(outcome), answer.Task);
    }

    /// <inheritdoc/>
    public Task<RuleOutcome> EvaluateAsync(RuleCall call)
    {
        Rules.Add(call);
        if (ruleScript.TryGetValue((call.Key, call.Member), out var scripted))
            return Task.FromResult(scripted);
        Assert.True(plan != null, $"The fake host has no plan to evaluate {call.Key} {call.Member} over.");
        return Task.FromResult(RuleTurn.Evaluate(plan!, call));
    }

    /// <summary>The delays asked for, cancelled or not.</summary>
    public int DelaysMade { get; private set; }

    /// <summary>The delays neither ended nor cancelled.</summary>
    public int OutstandingDelays => timers.Count;

    /// <inheritdoc/>
    public Task DelayAsync(TimeSpan wait, CancellationToken cancel)
    {
        DelaysMade++;
        if (cancel.IsCancellationRequested)
            return Task.FromCanceled(cancel);

        var done = new TaskCompletionSource();
        var timer = (UtcNow + wait, done);
        timers.Add(timer);
        cancel.Register(() =>
        {
            timers.Remove(timer);
            done.TrySetCanceled(cancel);
        });
        return done.Task;
    }

    /// <summary>Moves the clock on by <paramref name="by"/>, ending every delay it passes.</summary>
    public void Advance(TimeSpan by)
    {
        UtcNow += by;
        foreach (var timer in timers.Where(t => t.Due <= UtcNow).ToList())
        {
            timers.Remove(timer);
            timer.Done.TrySetResult();
        }
    }

    /// <summary>Gives the answer of the held call keyed <paramref name="key"/>.</summary>
    public void Release(string key)
    {
        var at = held.FindIndex(h => h.Key == key);
        Assert.True(at >= 0, $"{key} is not held; held: {string.Join(", ", Held)}");
        var (_, answer) = held[at];
        held.RemoveAt(at);
        answer();
    }

    /// <summary>Gives every held answer, oldest first, including those of calls made meanwhile, until none is held.</summary>
    public void ReleaseAll()
    {
        while (held.Count > 0)
            Release(held[0].Key);
    }

    /// <summary>Gives the held answers one at a time in an order <paramref name="random"/> chooses, until none is held.</summary>
    public void ReleaseInOrder(Random random)
    {
        while (held.Count > 0)
            Release(held[random.Next(held.Count)].Key);
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a thread with no synchronization context, so each task this host completes resumes
    /// the runner inline, before <see cref="Release"/> returns. <paramref name="body"/> starts the run, releases what it
    /// will, and returns the run, which must have ended by then (so a test never waits on a run that cannot end).
    /// </summary>
    public static Task<RunResult> Drive(Func<Task<RunResult>> body) => Task.Run(() =>
    {
        var run = body();
        Assert.True(run.IsCompleted, "The run has not ended: something it waits on was never released.");
        return run;
    });

    /// <summary>The next scripted outcome for <paramref name="key"/>, the last repeating; null when none is scripted.</summary>
    private AgentOutcome? Next(string key)
    {
        if (!agentScript.TryGetValue(key, out var outcomes))
            return null;
        var served = agentServed.GetValueOrDefault(key);
        agentServed[key] = served + 1;
        return outcomes[Math.Min(served, outcomes.Length - 1)];
    }

    private Task<T> Hold<T>(string key, Action answer, Task<T> task)
    {
        if (auto)
            answer();
        else
            held.Add((key, answer));
        return task;
    }
}
