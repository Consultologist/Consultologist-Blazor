using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Auth;
using Consultologist.Api.Documents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.Tests.ByteEquivalence;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.V31;
using Consultologist.Api.Workflow;
using Consultologist.PackageFormat;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (part 09 § 3.6, § 3.4, S09-018; D9; Review Focus 5): a drain met from every door. A 30 start, a 31 start and an
/// email start during a drain each get a job id and no error, schedule nothing, and leave the job waiting; Resume schedules
/// each exactly once, a second Resume none. A scheduled job whose timer ends during a drain waits for Resume, on either
/// orchestrator, and its job limit is timed from after the wait; an orchestration started before this build takes no new
/// step. The release hold refuses as it did. The starter is the real one over substituted stores (the
/// <see cref="V31JobStartTests"/> wiring); Resume is the real function (<see cref="AdminDrainTests.Admin"/>). Content is
/// fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class DrainDoorsTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly Lazy<EngineCapabilities> ThisEngine =
        new(() => new EngineCapabilitiesFactory(OutputContractCatalog.Load(), FormatCatalogLoader.Load()).Current());

    private const string Draft = "Margaret Okafor, 61, referred for a second opinion on her anaemia. (Fictional.)";

    [Fact]
    public async Task EveryDoor_WaitsAndStartsOnce()
    {
        V31Contract.AssertSentence(new SourceRefusal("S09-018", ConsultGenerationJobStatuses.WaitingForUpdate, ""));

        // with no drain, every door schedules at once
        var open = new Doors();
        var openOutcomes = await open.StartEveryDoorAsync();
        Assert.All(openOutcomes, outcome => Assert.Null(outcome.Error));
        Assert.Equal(openOutcomes.Select(o => o.JobId), open.Admin.Scheduled.Select(s => s.Options!.InstanceId));
        Assert.DoesNotContain(open.Admin.Signals, s => s.Operation == nameof(ConsultGenerationJobEntity.MarkWaitingForUpdate));
        Assert.Equal(0, await open.Drain.ParkedCountAsync(CancellationToken.None));

        // the 30 orchestration input is today's but for DrainAware: true
        var input30 = Assert.IsType<ConsultGenerationOrchestrationInput>(open.Admin.Scheduled[0].Input);
        Assert.True(input30.DrainAware);
        var today = JsonSerializer.Serialize(input30 with { DrainAware = false });
        Assert.DoesNotContain("DrainAware", today, StringComparison.Ordinal);
        Assert.Equal(today[..^1] + ",\"DrainAware\":true}", JsonSerializer.Serialize(input30));
        Assert.True(Assert.IsType<ConsultGenerationOrchestrationInputV31>(open.Admin.Scheduled[1].Input).DrainAware);

        // during a drain, every door answers with a job id, schedules nothing, and leaves the job waiting
        var blobs = new EngineDrainTests.MemoryDrainBlobs();
        var doors = new Doors(new EngineDrain(blobs));
        await doors.Admin.DrainAsync();
        var outcomes = await doors.StartEveryDoorAsync();

        // each is parked in its account kind's container (the doors' account is an organisation's)
        Assert.Equal(outcomes.Select(o => ("org", $"{EngineDrain.ParkedPrefix}{o.JobId}.json")).Order(),
            blobs.Documents.Keys.Where(key => key.Name != EngineDrain.StateBlob).Order());

        foreach (var outcome in outcomes)
        {
            Assert.Null(outcome.Error);
            Assert.Null(outcome.ErrorDetail);
            Assert.False(string.IsNullOrWhiteSpace(outcome.JobId));
            Assert.Equal(ConsultGenerationJobStatuses.WaitingForUpdate, (await doors.StateOfAsync(outcome.JobId!)).Status);
        }

        Assert.Empty(doors.Admin.Scheduled);
        Assert.Equal(3, await doors.Drain.ParkedCountAsync(CancellationToken.None));
        Assert.Equal("""{"draining":true,"producing":0,"parked":3}""", FakeHttp.BodyOf(await doors.Admin.StatusAsync()));

        // Resume schedules each exactly once, as the open door scheduled it, and the jobs stop waiting
        Assert.Equal("""{"draining":false,"started":3}""", FakeHttp.BodyOf(await doors.Admin.ResumeAsync()));
        Assert.Equal(outcomes.Select(o => o.JobId), doors.Admin.Scheduled.Select(s => s.Options!.InstanceId));
        Assert.Equal(open.Admin.Scheduled.Select(s => s.Name), doors.Admin.Scheduled.Select(s => s.Name));
        for (var i = 0; i < outcomes.Count; i++)
        {
            Assert.Equal(Masked(open.Admin.Scheduled[i].Input, openOutcomes[i].JobId!), Masked(doors.Admin.Scheduled[i].Input, outcomes[i].JobId!));
            Assert.Equal(ConsultGenerationJobStatuses.Queued, (await doors.StateOfAsync(outcomes[i].JobId!)).Status);
        }

        // a second Resume schedules none
        Assert.Equal("""{"draining":false,"started":0}""", FakeHttp.BodyOf(await doors.Admin.ResumeAsync()));
        Assert.Equal(3, doors.Admin.Scheduled.Count);
    }

    [Fact]
    public async Task AStartThatParksAsTheDrainEnds_IsStartedAtOnce()
    {
        // the drain ends between the start's check and its park: Resume has already run, so the start schedules its own job
        var drain = new EndsAfterFirstCheck(new EngineDrain(new EngineDrainTests.MemoryDrainBlobs()));
        var doors = new Doors(drain);
        await drain.BeginAsync(CancellationToken.None);

        var outcome = await doors.Start31Async();

        Assert.Null(outcome.Error);
        Assert.Equal(outcome.JobId, Assert.Single(doors.Admin.Scheduled).Options!.InstanceId);
        Assert.Equal(0, await drain.ParkedCountAsync(CancellationToken.None));
        Assert.Equal(ConsultGenerationJobStatuses.Queued, (await doors.StateOfAsync(outcome.JobId!)).Status);
        Assert.Equal("""{"draining":false,"started":0}""", FakeHttp.BodyOf(await doors.Admin.ResumeAsync()));
    }

    [Fact]
    public Task AScheduledJob_WhoseTimerEndsDuringADrain_WaitsForResume_At31() => Task.Run(async () =>
    {
        var job = V31Job.Of("nested-note");
        var draining = true;
        var context = job.Context(job.Input() with { ScheduledAtUtc = new DateTimeOffset(V31Job.Now.AddMinutes(1), TimeSpan.Zero) })
            .HoldTimersLongerThan(TimeSpan.FromMinutes(2))
            .On(ConsultGenerationActivityNames.CheckDrain, _ => draining);

        var run = V31Job.RunAsync(context);
        AssertWaiting(context, run, job.State, checks: 1);

        // the drain outlasts the wait: the orchestration checks again and waits again, calling nothing else
        context.EndHeldTimers();
        AssertWaiting(context, run, job.State, checks: 2);

        // Resume, forty minutes on: the job starts, and its limit is timed from now, not from its schedule
        draining = false;
        context.Advance(TimeSpan.FromMinutes(40));
        Assert.Equal(1, context.RaiseEvent(EngineDrain.ResumedEvent));
        await run;

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.Equal(new DateTimeOffset(V31Job.Now.AddMinutes(40), TimeSpan.Zero), job.State.StartedAtUtc);
        Assert.Contains(context.Calls, c => c.Kind == "timer" && c.InputJson == JsonSerializer.Serialize(V31Job.Now.AddMinutes(70)));
        Assert.Equal(0, context.PendingTimers);
    });

    [Fact]
    public Task AScheduledJob_WhoseTimerEndsDuringADrain_WaitsForResume_At30() => Task.Run(async () =>
    {
        var draining = true;
        var run = await EngineRun.PrepareAsync(Package29(), Notes, Outputs,
            input => input with { Request = input.Request with { ScheduledAtUtc = new DateTimeOffset(Now30.AddMinutes(1), TimeSpan.Zero) } });
        run.Context.HoldTimersLongerThan(TimeSpan.FromMinutes(2)).On(ConsultGenerationActivityNames.CheckDrain, _ => draining);

        var running = run.RunAsync();
        AssertWaiting(run.Context, running, run.State, checks: 1, before: [("entity", nameof(ConsultGenerationJobEntity.Initialize))]);

        draining = false;
        Assert.Equal(1, run.Context.RaiseEvent(EngineDrain.ResumedEvent));
        await running;

        Assert.Equal(ConsultGenerationJobStatuses.Completed, run.State.Status);
        // after the wait, the job runs as an undrained one does
        var plain = await EngineRun.StartAsync(Package29(), Notes, Outputs);
        Assert.Equal(Masked(AfterMarkRunning(plain.Context), plain.JobId), Masked(AfterMarkRunning(run.Context), run.JobId));
    });

    [Fact]
    public async Task AnOrchestrationFromBeforeThisBuild_TakesNoNewStep()
    {
        // 5–30: an input with no DrainAware (a payload from before this build reads as false) asks nothing of the drain, and
        // its whole run is EngineRunGoldenTests' golden, unchanged
        var stored = JsonSerializer.Deserialize<ConsultGenerationOrchestrationInput>("""{"Request":{"ConsultDraft":"x"},"AppUserId":"user-1"}""")!;
        Assert.False(stored.DrainAware);

        var before = await EngineRun.PrepareAsync(Package29(), Notes, Outputs, input => input with { DrainAware = false });
        before.Context.On(ConsultGenerationActivityNames.CheckDrain, _ => throw new InvalidOperationException("An orchestration from before the drain asked check-drain."));
        await before.RunAsync();

        Assert.DoesNotContain(before.Context.Calls, c => c.Name == ConsultGenerationActivityNames.CheckDrain);
        Assert.DoesNotContain(before.Context.Calls, c => c.Kind == "event");
        Assert.Equal(File.ReadAllText(Golden("EngineRun29.txt")).Replace("\r\n", "\n"), before.Trace());

        // 31: the same, against a drain-aware run with the drain off
        var job = V31Job.Of("nested-note");
        var context = job.Context(job.Input() with { DrainAware = false })
            .On(ConsultGenerationActivityNames.CheckDrain, _ => throw new InvalidOperationException("An orchestration from before the drain asked check-drain."));
        await V31Job.RunAsync(context);
        var aware = V31Job.Of("nested-note");
        var awareContext = aware.Context();
        await V31Job.RunAsync(awareContext);

        Assert.Equal(ConsultGenerationJobStatuses.Completed, job.State.Status);
        Assert.DoesNotContain(context.Calls, c => c.Name == ConsultGenerationActivityNames.CheckDrain);
        Assert.Equal(awareContext.Calls, context.Calls);
        Assert.Equal(awareContext.CustomStatuses, context.CustomStatuses);
    }

    [Fact]
    public async Task HoldNewJobs_StillRefusesAsToday()
    {
        var hold = Substitute.For<IJobIntakeHold>();
        hold.IsHeld.Returns(true);
        var doors = new Doors(hold: hold);
        await doors.Admin.DrainAsync();

        foreach (var outcome in await doors.StartEveryDoorAsync())
        {
            Assert.Null(outcome.JobId);
            Assert.Equal(ConsultGenerationJobStartError.Held, outcome.Error);
            Assert.Equal(JobIntakeHold.Sentence, outcome.ErrorDetail);
            Assert.Equal(JobIntakeHold.Sentence, outcome.SenderSafeDetail);
            Assert.Equal(JobIntakeHold.RetryAfter, outcome.RetryAfter);
        }

        Assert.Empty(doors.Admin.Signals);
        Assert.Empty(doors.Admin.Scheduled);
        Assert.Equal(0, await doors.Drain.ParkedCountAsync(CancellationToken.None));
    }

    /// <summary>
    /// The orchestration is waiting behind the drain: it has asked check-drain <paramref name="checks"/> times and called
    /// nothing else since its schedule wait (<paramref name="before"/> is what it called before that wait), its job and its
    /// custom status say it waits, and it waits for <see cref="EngineDrain.ResumedEvent"/>.
    /// </summary>
    private static void AssertWaiting(
        FakeOrchestrationContext context, Task run, ConsultGenerationJobState state, int checks, (string, string)[]? before = null)
    {
        Assert.False(run.IsCompleted);
        var expected = new List<(string, string)>(before ?? []) { ("timer", "CreateTimer") };
        for (var check = 1; check <= checks; check++)
        {
            expected.Add(("activity", ConsultGenerationActivityNames.CheckDrain));
            if (check == 1)
                expected.Add(("entity", nameof(ConsultGenerationJobEntity.MarkWaitingForUpdate)));
            expected.Add(("event", EngineDrain.ResumedEvent));
            expected.Add(("timer", "CreateTimer"));
        }

        Assert.Equal(expected, context.Calls.Select(c => (c.Kind, c.Name)));
        Assert.Equal(ConsultGenerationJobStatuses.WaitingForUpdate, state.Status);
        Assert.Equal("""{"status":"Waiting for an update to finish"}""", context.CustomStatuses.Last());
        Assert.Equal([EngineDrain.ResumedEvent], context.AwaitedEvents);
    }

    private static readonly DateTime Now30 = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, ConsultInputValue> Notes = new(StringComparer.Ordinal)
    {
        ["notes"] = ConsultInputValue.OfText("A fictional referral: a 40-year-old with wheeze and a fever."),
        ["kind"] = ConsultInputValue.OfText("new"),
    };

    /// <summary>EngineRunGoldenTests' scripted replies.</summary>
    private static string? Outputs(string key) => WorkflowItemPath.NodeOf(key) switch
    {
        "find_problems" => """
            { "summary": "Two problems.", "problems": [ { "name": "Asthma", "severity": "low" }, { "name": "Sepsis", "severity": "high" } ] }
            """,
        "extract" => """{ "concepts": [ { "term": "Asthma", "id": "195967001", "type": "disorder", "isActive": true } ] }""",
        "scope" => "in_scope",
        _ => null,
    };

    /// <summary>EngineRunGoldenTests' 29 package (<c>EngineRun29.txt</c>).</summary>
    private static WorkflowPackage Package29()
    {
        var bundle = V29Package.Minimal();
        return WorkflowPackageStore.PackageOf(WorkflowPackageStore.Materialise("test@v29", bundle.Manifest,
            new Dictionary<string, string>(bundle.Files, StringComparer.Ordinal), null, V29Catalog.Catalog, V25Package.Catalog(), V29Package.Accepting29));
    }

    private static List<(string Kind, string Name, string InputJson)> AfterMarkRunning(FakeOrchestrationContext context) =>
        context.Calls.SkipWhile(c => c.Name != nameof(ConsultGenerationJobEntity.MarkRunning)).ToList();

    private static string Masked(object? value, string jobId) =>
        (value is string text ? text : JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object)))
            .Replace(jobId, "<job>", StringComparison.Ordinal)
            .Replace(jobId[..8], "<job8>", StringComparison.Ordinal);

    private static string Golden(string name, [CallerFilePath] string path = "") =>
        Path.Combine(Path.GetDirectoryName(path)!, "..", "Golden", name);

    /// <summary>
    /// The real starter, 31 admitted, over substituted stores, a <see cref="FakeRegistry"/> holding a 31 golden and a 30
    /// package resolved by today's store; its Durable client is <see cref="AdminDrainTests.Admin"/>'s, so Resume schedules
    /// on the client the starts parked from; and the jobs' entities, fed every signal.
    /// </summary>
    private sealed class Doors
    {
        private readonly ConsultGenerationJobStarter _starter;
        private readonly WorkflowPackage _package30 = ByteEquivalenceFixtures.Load("example-prior-letters");
        private readonly WorkflowPackageRef _ref31;
        private readonly FakeRegistry _registry = new();

        public Doors(IEngineDrain? drain = null, IJobIntakeHold? hold = null)
        {
            Drain = drain ?? new EngineDrain(new EngineDrainTests.MemoryDrainBlobs());
            Admin = new AdminDrainTests.Admin(Drain);
            _ref31 = _registry.Put(RunFixture.Golden("nested-note")).Ref;

            var settings = Substitute.For<IAccountSettingsStore>();
            settings.GetAsync(Arg.Any<string>(), AccountSettingKeys.TimeZone, Arg.Any<CancellationToken>())
                .Returns(new AccountSetting(AccountSettingKeys.TimeZone, "America/Toronto", "text/plain", DateTimeOffset.UnixEpoch));
            var accounts = Substitute.For<IAccountStore>();
            accounts.GetAccountKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SignInKinds.Organisation);
            var packageStore = Substitute.For<IWorkflowPackageStore>();
            packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>()).Returns(_package30);
            var rateLimiter = Substitute.For<IAccountRateLimiter>();
            rateLimiter.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new RateLimitDecision(true, 60, 59, TimeSpan.FromMinutes(37)));

            var admission = SpecVersionAdmissionTests.With("31");
            var limits = new EngineLimitSettings(new ConfigurationBuilder().Build());
            var engine = EngineAttestation.Current(V29Catalog.Catalog, null);
            _starter = new ConsultGenerationJobStarter(
                NullLogger<ConsultGenerationJobStarter>.Instance,
                packageStore,
                Substitute.For<IWorkflowPackagePinResolver>(),
                V29Catalog.Catalog,
                rateLimiter,
                new FakeOwnership(),
                engine,
                new NoTerminology(),
                accounts,
                settings,
                Substitute.For<IJobOutputsBlobStore>(),
                Substitute.For<IJobInputsBlobStore>(),
                Substitute.For<IConsultGenerationLinkStore>(),
                Substitute.For<Consultologist.Api.Forms.IFormResponseStore>(),
                Substitute.For<Consultologist.Api.Forms.IFormResponseBlobStore>(),
                Substitute.For<IDocumentOcr>(),
                TestFormats.Catalog,
                null,
                hold,
                new V31JobStarter(admission, new CompiledPackageStore(_registry, _registry, ThisEngine.Value, admission, limits), new V31Job.FakeInputs(),
                    limits, accounts, settings, engine, NullLogger<V31JobStarter>.Instance, Drain),
                Drain);
        }

        public IEngineDrain Drain { get; }

        public AdminDrainTests.Admin Admin { get; }

        /// <summary>A 30 start, a 31 start and an email start (a 30 package, as the email door's pin gives it), in that order.</summary>
        public async Task<List<ConsultGenerationJobStartOutcome>> StartEveryDoorAsync() =>
            [await Start30Async(), await Start31Async(), await StartByEmailAsync()];

        public Task<ConsultGenerationJobStartOutcome> Start30Async() =>
            StartAsync(Ref(_package30), Inputs30(), new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App));

        public Task<ConsultGenerationJobStartOutcome> Start31Async() =>
            StartAsync($"{_ref31.Name}@{_ref31.Version}", new(StringComparer.Ordinal) { ["consult_draft"] = ConsultInputValue.OfText(Draft) },
                new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App));

        public Task<ConsultGenerationJobStartOutcome> StartByEmailAsync() =>
            StartAsync(Ref(_package30), Inputs30(), new ConsultGenerationJobOrigin(ConsultGenerationJobSources.Email, "dr.sample@example.test"));

        /// <summary>The job's record: a real entity fed, in order, every signal the job's entity was sent.</summary>
        public async Task<ConsultGenerationJobState> StateOfAsync(string jobId)
        {
            var entity = new ConsultGenerationJobEntity(
                Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(),
                Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());
            foreach (var (_, operation, payload) in Admin.Signals.Where(s => s.Key == jobId))
            {
                var method = typeof(ConsultGenerationJobEntity).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Single(m => m.Name == operation && m.DeclaringType == typeof(ConsultGenerationJobEntity));
                var arguments = method.GetParameters() is [var parameter]
                    ? new[] { JsonSerializer.Deserialize(JsonSerializer.Serialize(payload, payload!.GetType()), parameter.ParameterType) }
                    : [];
                if (method.Invoke(entity, arguments) is Task task)
                    await task;
            }

            return (ConsultGenerationJobState)StateProperty.GetValue(entity)!;
        }

        private Task<ConsultGenerationJobStartOutcome> StartAsync(string packageRef, Dictionary<string, ConsultInputValue> inputs, ConsultGenerationJobOrigin origin) =>
            _starter.StartAsync(Admin.Client, new ConsultGenerationRequest(null, packageRef, Inputs: inputs), "user-1", origin, CancellationToken.None);

        private static string Ref(WorkflowPackage package) => package.Ref;

        private static Dictionary<string, ConsultInputValue> Inputs30() => new(StringComparer.Ordinal)
        {
            ["consult_draft"] = ConsultInputValue.OfText(Draft),
            ["prior_letters"] = ConsultInputValue.OfArray([ConsultInputValue.OfText("Dear Dr A. Sample, Margaret Okafor was seen in clinic. (Fictional.)")]),
        };
    }

    private sealed class NoTerminology : ITerminologyAttestationSource
    {
        public ValueTask<TerminologyAttestation?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult<TerminologyAttestation?>(null);
    }

    /// <summary>A drain whose first check says draining and every later one says not: it ended just after a start checked.</summary>
    private sealed class EndsAfterFirstCheck(IEngineDrain inner) : IEngineDrain
    {
        private int _checks;

        public Task<bool> IsDrainingAsync(CancellationToken ct) => Task.FromResult(Interlocked.Increment(ref _checks) == 1);

        public Task BeginAsync(CancellationToken ct) => inner.BeginAsync(ct);

        public Task<IReadOnlyList<ParkedJob>> EndAsync(CancellationToken ct) => inner.EndAsync(ct);

        public Task ParkAsync(ParkedJob job, CancellationToken ct) => inner.ParkAsync(job, ct);

        public Task ForgetAsync(string jobId, CancellationToken ct) => inner.ForgetAsync(jobId, ct);

        public Task<int> ParkedCountAsync(CancellationToken ct) => inner.ParkedCountAsync(ct);
    }
}
