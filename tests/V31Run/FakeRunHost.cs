using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1503 (specVersion 31, area 06): a scripted <see cref="IRunHost"/>. An agent call's outcome and a render's are scripted
/// by the call's <c>Key</c> or, failing that, its <c>EntryKey</c>; an unscripted agent replies <c>"Text of &lt;key&gt;."</c>
/// and an unscripted render gives <c>"Rendered &lt;key&gt;."</c>. In auto mode every answer is given at once; otherwise each
/// is held until the test releases it (<see cref="Release"/>, <see cref="ReleaseAll"/>, <see cref="ReleaseInOrder"/>).
/// The clock is manual (<see cref="Advance"/>), and a delay ends when the clock passes it. Releasing completes the task
/// inline, so a test driving the runner off any synchronization context sees each release's effects before it goes on
/// (<see cref="Drive"/>).
/// </summary>
internal sealed class FakeRunHost(bool auto = false) : IRunHost
{
    private readonly Dictionary<string, AgentOutcome> agentScript = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RenderOutcome> renderScript = new(StringComparer.Ordinal);
    private readonly List<(string Key, Action Answer)> held = [];
    private readonly List<(DateTime Due, TaskCompletionSource Done)> timers = [];
    private readonly List<(string Key, bool Starts)> events = [];

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

    /// <summary>The most agent calls held at once.</summary>
    public int InFlightPeak => PeakOf(_ => true);

    /// <summary>The keys of the calls still held.</summary>
    public IReadOnlyList<string> Held => held.Select(h => h.Key).ToList();

    /// <summary>Scripts the reply of the agent call keyed <paramref name="key"/> (a row key or an entry key).</summary>
    public FakeRunHost Reply(string key, string reply) => Agent(key, new AgentOutcome(AgentOutcomeKind.Completed, reply, 0, null));

    /// <summary>Scripts the outcome of the agent call keyed <paramref name="key"/>.</summary>
    public FakeRunHost Agent(string key, AgentOutcome outcome)
    {
        agentScript[key] = outcome;
        return this;
    }

    /// <summary>Scripts the outcome of the render keyed <paramref name="key"/>.</summary>
    public FakeRunHost Render(string key, RenderOutcome outcome)
    {
        renderScript[key] = outcome;
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

    /// <inheritdoc/>
    public Task<AgentOutcome> CallAgentAsync(AgentCall call)
    {
        Calls.Add(call);
        events.Add((call.Key, true));
        var outcome = agentScript.GetValueOrDefault(call.Key)
            ?? agentScript.GetValueOrDefault(call.EntryKey)
            ?? new AgentOutcome(AgentOutcomeKind.Completed, $"Text of {call.Key}.", 0, null);
        var answer = new TaskCompletionSource<AgentOutcome>();
        return Hold(call.Key, () =>
        {
            events.Add((call.Key, false));
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
    public Task DelayAsync(TimeSpan wait)
    {
        var done = new TaskCompletionSource();
        timers.Add((UtcNow + wait, done));
        return done.Task;
    }

    /// <summary>Moves the clock on by <paramref name="by"/>, ending every delay it passes.</summary>
    public void Advance(TimeSpan by)
    {
        UtcNow += by;
        foreach (var timer in timers.Where(t => t.Due <= UtcNow).ToList())
        {
            timers.Remove(timer);
            timer.Done.SetResult();
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

    private Task<T> Hold<T>(string key, Action answer, Task<T> task)
    {
        if (auto)
            answer();
        else
            held.Add((key, answer));
        return task;
    }
}
