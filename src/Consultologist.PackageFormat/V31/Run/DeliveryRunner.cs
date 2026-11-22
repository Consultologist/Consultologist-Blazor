namespace Consultologist.PackageFormat.V31.Run;

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; part 09 § 3.8): what delivery gave: each destination's row, in the fixed
/// order of <see cref="DeliveryPlan.Sends"/> (a bundle's in its documents' order), and how many of them are <c>failed</c>.
/// </summary>
public sealed record DeliveryResult(IReadOnlyList<DeliveryRow> Rows, int Failed);

/// <summary>
/// #1505 (specVersion 31, area 08; part 06 § 8; part 09 § 3.8; D10 to D13): the deterministic runner of delivery at the end
/// of a job that succeeded. It makes the first attempt of every send of <see cref="DeliveryPlan.Sends"/> at once, in their
/// order, then waits, by <see cref="Task.WhenAny(IEnumerable{Task})"/> over what is in flight and the window's timer, for one
/// to finish. A send that is sent is final; one whose kind is not set up is final at once, <c>not sent</c> with S08-006, and
/// never retried; one that failed is retried after 2 minutes, doubling after, each wait cut to the time the window has left,
/// until it has made <see cref="RunLimits.DeliveryAttempts"/> attempts. No attempt starts once
/// <see cref="RunLimits.DeliveryMinutes"/> have passed from the start of delivering; what is unsent then (its send in
/// flight when the window's timer ends, or waiting to retry) is <c>failed</c>. A send's result is always read, even when the
/// host's clock is already past the close (a Durable replay step that started after it): an email that went out is
/// <c>sent</c>, and a failure keeps its error kind and starts no attempt. Every row is recorded, in the fixed order, once every
/// result is final or the window has closed.
/// </summary>
/// <remarks>
/// Durable Functions drives it: it awaits no task but <see cref="IDeliveryHost"/>'s (and the recorder's), reads no clock but
/// <see cref="IDeliveryHost.UtcNow"/>, iterates only in the fixed order, and cancels every timer it made when it ends, on
/// every path, as <see cref="PlanRunner"/> does. It never throws on a deliverable's text; a fault of the host or the recorder
/// is theirs and passes through.
/// </remarks>
public sealed class DeliveryRunner
{
    /// <summary>The wait before a destination's second attempt; each wait after doubles it (D12).</summary>
    private static readonly TimeSpan FirstWait = TimeSpan.FromMinutes(2);

    private readonly RunLimits limits;
    private readonly IDeliveryHost host;
    private readonly IRunRecorder recorder;

    /// <summary>A runner of delivery under <paramref name="limits"/>, talking to <paramref name="host"/> and <paramref name="recorder"/>.</summary>
    public DeliveryRunner(RunLimits limits, IDeliveryHost host, IRunRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recorder);
        this.limits = limits;
        this.host = host;
        this.recorder = recorder;
    }

    /// <summary>Delivers each released deliverable of <paramref name="outcomes"/> (a job's, in plan order) to each of its destinations.</summary>
    public Task<DeliveryResult> RunAsync(IReadOnlyList<DeliverableOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        return new Delivering(this, DeliveryPlan.Sends(outcomes)).RunAsync();
    }

    /// <summary>One send's destination: its attempts so far, and its result once final.</summary>
    private sealed class Destination(DeliverySend send)
    {
        public DeliverySend Send { get; set; } = send;

        public List<DeliveryAttempt> Attempts { get; } = [];

        public string? Result { get; set; }

        public string? Reason { get; set; }

        public DateTime? AtUtc { get; set; }
    }

    /// <summary>Something in flight for a destination: an attempt (started at <paramref name="AtUtc"/>), or the wait before its next.</summary>
    private sealed record Flight(Destination To, Task Task, bool Wait, DateTime AtUtc);

    /// <summary>One delivery: its destinations, in the fixed order, and what is in flight.</summary>
    private sealed class Delivering(DeliveryRunner runner, IReadOnlyList<DeliverySend> sends)
    {
        private readonly RunLimits limits = runner.limits;
        private readonly IDeliveryHost host = runner.host;
        private readonly IRunRecorder recorder = runner.recorder;
        private readonly List<Destination> destinations = sends.Select(s => new Destination(s)).ToList();

        /// <summary>What is in flight, in the order started.</summary>
        private readonly List<Flight> flights = [];

        /// <summary>Cancels every timer delivery made (the window's and each retry's) when it ends, on every path.</summary>
        private readonly CancellationTokenSource timers = new();

        /// <summary>The host's timer for the window's time left, made the first time delivery waits.</summary>
        private Task? window;

        private DateTime closesAtUtc;

        public async Task<DeliveryResult> RunAsync()
        {
            closesAtUtc = host.UtcNow + TimeSpan.FromMinutes(limits.DeliveryMinutes);
            try
            {
                foreach (var destination in destinations)
                    Attempt(destination);

                while (flights.Count > 0)
                {
                    window ??= host.DelayAsync(TimeLeft(), timers.Token);
                    var finished = await Task.WhenAny(flights.Select(f => f.Task).Append(window));
                    if (ReferenceEquals(finished, window))
                    {
                        // The window closed: what is unsent is failed.
                        Close();
                        break;
                    }

                    var flight = flights.First(f => ReferenceEquals(f.Task, finished));
                    flights.Remove(flight);
                    if (!flight.Wait)
                    {
                        // A send's result is a fact outside the run (an email that went out), so it is always read, even
                        // when the host's clock (a replay step's) is already past the close; a failure then starts no attempt.
                        Land(flight, await (Task<DeliverySendOutcome>)flight.Task);
                        continue;
                    }

                    if (Closed())
                    {
                        // A wait that ended at or after the close: no attempt starts, and what is unsent is failed.
                        Close();
                        break;
                    }

                    Attempt(flight.To);
                }
            }
            finally
            {
                // What is still in flight is never awaited: its late answer changes nothing, its timer is cancelled (an
                // orchestration does not complete while one is outstanding), and its fault is never unobserved.
                timers.Cancel();
                foreach (var abandoned in flights)
                    Observe(abandoned.Task);
                if (window != null)
                    Observe(window);
                timers.Dispose();
            }

            var rows = new List<DeliveryRow>();
            foreach (var destination in destinations)
            {
                foreach (var document in destination.Send.Documents)
                {
                    var row = new DeliveryRow(
                        DeliveryPlan.Key(document.Deliverable, destination.Send.Kind),
                        document.Deliverable,
                        destination.Send.Kind,
                        destination.Result!,
                        destination.Reason,
                        destination.AtUtc,
                        destination.Attempts.ToList());
                    rows.Add(row);
                    await recorder.DeliveryAsync(row);
                }
            }

            return new DeliveryResult(rows, rows.Count(r => r.Result == DeliveryPlan.Failed));
        }

        /// <summary>Starts the destination's next attempt, unless the window has closed (it is then failed).</summary>
        private void Attempt(Destination destination)
        {
            if (Closed())
            {
                Fail(destination);
                return;
            }

            var attempt = destination.Attempts.Count + 1;
            destination.Send = destination.Send with { Attempt = attempt };
            var at = host.UtcNow;
            flights.Add(new Flight(destination, host.SendAsync(destination.Send), false, at));
        }

        /// <summary>An attempt gave <paramref name="outcome"/>: sent and not set up are final; a failure waits for its next attempt, while one is left.</summary>
        private void Land(Flight flight, DeliverySendOutcome? outcome)
        {
            var destination = flight.To;
            switch (outcome?.Result)
            {
                case DeliverySendResult.Sent:
                    destination.Attempts.Add(new DeliveryAttempt(flight.AtUtc, DeliveryPlan.Sent));
                    destination.Result = DeliveryPlan.Sent;
                    destination.AtUtc = flight.AtUtc;
                    return;

                case DeliverySendResult.NotSetUp:
                    destination.Result = DeliveryPlan.NotSent;
                    destination.Reason = DeliveryPlan.NotSetUp(destination.Send.Kind);
                    return;
            }

            destination.Attempts.Add(new DeliveryAttempt(
                flight.AtUtc, outcome?.ErrorKind is { } kind ? $"{DeliveryPlan.Failed}: {kind}" : DeliveryPlan.Failed));
            var made = destination.Attempts.Count;
            var left = TimeLeft();
            if (made >= limits.DeliveryAttempts || left <= TimeSpan.Zero)
            {
                Fail(destination);
                return;
            }

            // 2 minutes before attempt 2, doubling after, cut to the time the window has left.
            var minutes = FirstWait.TotalMinutes * Math.Pow(2, made - 1);
            var wait = minutes >= left.TotalMinutes ? left : TimeSpan.FromMinutes(minutes);
            flights.Add(new Flight(destination, host.DelayAsync(wait, timers.Token), true, host.UtcNow));
        }

        /// <summary>
        /// The window closed. A send that has already finished, unread (a Durable step reads what finished before it in one go),
        /// is landed first, in the order started: an email that went out is <c>sent</c>, and a failure keeps its error kind.
        /// Then each destination not final, in the fixed order, is failed, a send still in flight recorded as the attempt
        /// <c>failed: window</c> (its result is never read).
        /// </summary>
        private void Close()
        {
            foreach (var finished in flights.Where(f => f is { Wait: false, Task.IsCompleted: true }).ToList())
            {
                flights.Remove(finished);
                Land(finished, ((Task<DeliverySendOutcome>)finished.Task).GetAwaiter().GetResult());
            }

            foreach (var destination in destinations.Where(d => d.Result == null))
            {
                if (flights.FirstOrDefault(f => f.To == destination) is { Wait: false } sending)
                    destination.Attempts.Add(new DeliveryAttempt(sending.AtUtc, $"{DeliveryPlan.Failed}: window"));
                Fail(destination);
            }
        }

        private static void Fail(Destination destination)
        {
            destination.Result = DeliveryPlan.Failed;
            destination.Reason = DeliveryPlan.FailedKind(destination.Send.Kind);
        }

        /// <summary>Whether the window has closed, by the host's clock.</summary>
        private bool Closed() => host.UtcNow >= closesAtUtc;

        /// <summary>The window's time left by the host's clock, never below zero.</summary>
        private TimeSpan TimeLeft()
        {
            var left = closesAtUtc - host.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }

        /// <summary>Observes the fault of a task delivery no longer awaits, so a late failure of the host is never an unobserved task exception.</summary>
        private static void Observe(Task task) =>
            _ = task.ContinueWith(
                static t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
