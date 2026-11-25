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
/// to finish. A send that is sent is final (#1507, D17: one that went as the link-only email is <c>sent</c>, its attempt
/// <c>sent link-only</c> and its row's reason why); one whose kind is not set up is final at once, <c>not sent</c> with S08-006, and
/// never retried; one that failed is retried after 2 minutes, doubling after, each wait cut to the time the window has left,
/// until it has made <see cref="RunLimits.DeliveryAttempts"/> attempts. No attempt starts once
/// <see cref="RunLimits.DeliveryMinutes"/> have passed from the start of delivering; what is unsent then (never started, or
/// waiting to retry) is <c>failed</c>. A send in flight when the window closes is waited for (#1508: its activity has its own
/// timeout and is not cancelled, so the email can still go out): an email that went out is <c>sent</c>, and a failure keeps
/// its error kind and starts no attempt, as when the host's clock is already past the close (a Durable replay step). Every row is recorded, in the fixed order, once every
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

    /// <summary>
    /// Delivers each released deliverable of <paramref name="outcomes"/> (a job's, in plan order) to each of its destinations;
    /// #1507 (D19), with <paramref name="blockedEmail"/>, the link-only email of a job that released nothing
    /// (<see cref="DeliveryPlan.Sends"/>).
    /// </summary>
    public Task<DeliveryResult> RunAsync(IReadOnlyList<DeliverableOutcome> outcomes, bool blockedEmail = true)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var rowKinds = outcomes.ToDictionary(o => o.Key, DeliveryPlan.MailKindsOf, StringComparer.Ordinal);
        return new Delivering(this, DeliveryPlan.Sends(outcomes, blockedEmail), rowKinds).RunAsync();
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
    private sealed class Delivering(
        DeliveryRunner runner, IReadOnlyList<DeliverySend> sends, IReadOnlyDictionary<string, IReadOnlyList<string>> rowKinds)
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

        /// <summary>Whether the window has closed and delivery is landing what was in flight: no attempt starts after.</summary>
        private bool closing;

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
                        await CloseAsync();
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
                        await CloseAsync();
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
                    // #1507 (D19): the one link-only send of a blocked job gives a row to each mail destination each
                    // document lists, its result the send's, a reason naming a kind (S08-006, failed: <kind>) the row's own.
                    var kinds = destination.Send.EveryDeliverableBlocked
                        ? rowKinds.GetValueOrDefault(document.Deliverable) ?? [destination.Send.Kind]
                        : [destination.Send.Kind];
                    foreach (var kind in kinds)
                    {
                        var row = new DeliveryRow(
                            DeliveryPlan.Key(document.Deliverable, kind),
                            document.Deliverable,
                            kind,
                            destination.Result!,
                            ReasonFor(destination, kind),
                            destination.AtUtc,
                            destination.Attempts.ToList());
                        rows.Add(row);
                        await recorder.DeliveryAsync(row);
                        await recorder.FlushAsync();
                    }
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
                    // #1507 (D17, D19): the link-only email is sent; its attempt says so, and the row's reason says why.
                    destination.Attempts.Add(new DeliveryAttempt(
                        flight.AtUtc, outcome?.LinkOnly == null ? DeliveryPlan.Sent : DeliveryPlan.SentLinkOnly));
                    destination.Result = DeliveryPlan.Sent;
                    destination.Reason = outcome?.LinkOnly;
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
            if (closing || made >= limits.DeliveryAttempts || left <= TimeSpan.Zero)
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
        /// The window closed. Every send in flight is waited for (a Durable activity is not cancelled, so an email can still go
        /// out; it has its own timeout) and its result landed, in the order started: an email that went out is <c>sent</c>, and a
        /// failure keeps its error kind and is not retried. Then each destination not final, in the fixed order (one that never
        /// started, or was waiting to retry), is failed. No attempt starts after the close.
        /// </summary>
        private async Task CloseAsync()
        {
            closing = true;
            foreach (var sending in flights.Where(f => !f.Wait).ToList())
            {
                flights.Remove(sending);
                Land(sending, await (Task<DeliverySendOutcome>)sending.Task);
            }

            foreach (var destination in destinations.Where(d => d.Result == null))
                Fail(destination);
        }

        /// <summary>The destination's reason as a row of <paramref name="kind"/> says it: a kind-naming sentence in that kind.</summary>
        private static string? ReasonFor(Destination destination, string kind)
        {
            if (string.Equals(kind, destination.Send.Kind, StringComparison.Ordinal))
                return destination.Reason;
            return destination.Result switch
            {
                DeliveryPlan.Failed => DeliveryPlan.FailedKind(kind),
                DeliveryPlan.NotSent => DeliveryPlan.NotSetUp(kind),
                _ => destination.Reason,
            };
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
