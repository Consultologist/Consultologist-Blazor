using Consultologist.PackageFormat.V31.Run;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1505 (specVersion 31, area 08; D10 to D12): a scripted <see cref="IDeliveryHost"/>. A send is named by its first
/// document's deliverable and its kind (<see cref="DeliveryPlan.Key"/>), and its outcomes are scripted by that name as a
/// sequence (each attempt takes the next, the last repeating); an unscripted send is sent. Each answer is given at once,
/// unless the name is held (<see cref="Hold"/>): its sends are then answered only by <see cref="Answer"/>. The clock is
/// manual (<see cref="Advance"/>; <see cref="MoveClock"/> moves it without ending delays), and a delay ends when the clock passes it, or ends cancelled when its token is cancelled first
/// (<see cref="OutstandingDelays"/> counts those still waiting), as <see cref="FakeRunHost"/>'s do.
/// </summary>
internal sealed class FakeDeliveryHost : IDeliveryHost
{
    private readonly Dictionary<string, DeliverySendOutcome[]> script = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> served = new(StringComparer.Ordinal);
    private readonly HashSet<string> held = new(StringComparer.Ordinal);
    private readonly List<(string Name, TaskCompletionSource<DeliverySendOutcome> Answer)> waiting = [];
    private readonly List<(DateTime Due, TaskCompletionSource Done)> timers = [];

    /// <summary>The time the clock starts at.</summary>
    public static readonly DateTime Start = new(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc/>
    public DateTime UtcNow { get; private set; } = Start;

    /// <summary>The sends asked for, in order, each with the host's time it was asked at.</summary>
    public List<(DeliverySend Send, DateTime AtUtc)> Sends { get; } = [];

    /// <summary>The delays neither ended nor cancelled.</summary>
    public int OutstandingDelays => timers.Count;

    /// <summary>The name of <paramref name="send"/>: its first document's deliverable and its kind.</summary>
    public static string NameOf(DeliverySend send) => DeliveryPlan.Key(send.Documents[0].Deliverable, send.Kind);

    /// <summary>Scripts the outcomes of the send named <paramref name="name"/>: each attempt the next, the last repeating.</summary>
    public FakeDeliveryHost Script(string name, params DeliverySendOutcome[] outcomes)
    {
        Assert.NotEmpty(outcomes);
        script[name] = outcomes;
        return this;
    }

    /// <summary>Holds every send named <paramref name="name"/>: it is answered only by <see cref="Answer"/>.</summary>
    public FakeDeliveryHost Hold(string name)
    {
        held.Add(name);
        return this;
    }

    /// <inheritdoc/>
    public Task<DeliverySendOutcome> SendAsync(DeliverySend send)
    {
        Sends.Add((send, UtcNow));
        var name = NameOf(send);
        if (held.Contains(name))
        {
            var answer = new TaskCompletionSource<DeliverySendOutcome>();
            waiting.Add((name, answer));
            return answer.Task;
        }

        if (!script.TryGetValue(name, out var outcomes))
            return Task.FromResult(new DeliverySendOutcome(DeliverySendResult.Sent, null));
        var count = served.GetValueOrDefault(name);
        served[name] = count + 1;
        return Task.FromResult(outcomes[Math.Min(count, outcomes.Length - 1)]);
    }

    /// <inheritdoc/>
    public Task DelayAsync(TimeSpan wait, CancellationToken cancel)
    {
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

    /// <summary>
    /// Moves the clock on by <paramref name="by"/> without ending any delay: a Durable replay step that starts after the
    /// close, its clock past a timer whose event it has not read yet.
    /// </summary>
    public void MoveClock(TimeSpan by) => UtcNow += by;

    /// <summary>
    /// Ends every delay due at or before <see cref="Start"/> + <paramref name="by"/>, without moving the clock: a replay step
    /// reading a timer's event that fired before the step's clock.
    /// </summary>
    public void EndDelaysDueBy(TimeSpan by)
    {
        foreach (var timer in timers.Where(t => t.Due <= Start + by).ToList())
        {
            timers.Remove(timer);
            timer.Done.TrySetResult();
        }
    }

    /// <summary>Answers the oldest held send named <paramref name="name"/> with <paramref name="outcome"/>, inline.</summary>
    public void Answer(string name, DeliverySendOutcome outcome)
    {
        var at = waiting.FindIndex(w => w.Name == name);
        Assert.True(at >= 0, $"No send named {name} is held.");
        var (_, answer) = waiting[at];
        waiting.RemoveAt(at);
        answer.SetResult(outcome);
    }

    /// <summary>
    /// Runs <paramref name="body"/> on a thread with no synchronization context, so each delay this host ends resumes the
    /// runner inline, before <see cref="Advance"/> returns. <paramref name="body"/> starts the run, moves the clock as it
    /// will, and returns the run, which must have ended by then.
    /// </summary>
    public static Task<DeliveryResult> Drive(Func<Task<DeliveryResult>> body) => Task.Run(() =>
    {
        var run = body();
        Assert.True(run.IsCompleted, "The delivery has not ended: something it waits on never ended.");
        return run;
    });

    /// <summary>
    /// Runs delivery as Durable replay steps do: <paramref name="start"/> starts the run under a context that queues each
    /// continuation of the runner; each of <paramref name="steps"/> then ends or answers what it will outside that context,
    /// and only after it the runner reads what finished, in one go (the queue is run to its end). The run must have ended
    /// after the last step.
    /// </summary>
    public static Task<DeliveryResult> DriveInSteps(Func<Task<DeliveryResult>> start, params Action[] steps) => Task.Run(() =>
    {
        var context = new StepContext();
        var run = context.Within(start);
        foreach (var step in steps)
        {
            step();
            context.Within(() => context.RunQueued());
        }

        Assert.True(run.IsCompleted, "The delivery has not ended: something it waits on never ended.");
        return run;
    });

    /// <summary>A context that queues what is posted to it, run by <see cref="RunQueued"/> in order on the caller's thread.</summary>
    private sealed class StepContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> queue = new();

        public override void Post(SendOrPostCallback d, object? state) => queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public void RunQueued()
        {
            while (queue.TryDequeue(out var next))
                next.Callback(next.State);
        }

        /// <summary>Runs <paramref name="body"/> with this context current, then puts the caller's back.</summary>
        public T Within<T>(Func<T> body)
        {
            var before = Current;
            SetSynchronizationContext(this);
            try
            {
                return body();
            }
            finally
            {
                SetSynchronizationContext(before);
            }
        }

        public void Within(Action body) => Within(() =>
        {
            body();
            return 0;
        });
    }
}
