using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using Consultologist.Api.Agents;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Tests.Engine;
using Consultologist.Api.Tests.V31Resolve;
using Consultologist.Api.Tests.V31Run;
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
/// #1503 (specVersion 31, area 06): a 31 job, end to end in a test. A golden is published into a <see cref="FakeRegistry"/>
/// and loaded by the real <see cref="CompiledPackageStore"/>; a real entity holds the job as Task 13's start would leave it;
/// the real <see cref="ConsultGenerationOrchestratorV31"/> runs on a <see cref="FakeOrchestrationContext"/> whose
/// <c>load-plan-v31</c>, <c>load-run-inputs-v31</c>, <c>render-node-v31</c> and (#1505) <c>evaluate-rule-v31</c> are the real activities (the inputs from a
/// fake <see cref="IRunInputsStore"/> holding <see cref="RunFixture.GoldenInputs"/> at the job's held-inputs pointer), and whose <c>run-agent-node-v31</c> is
/// answered by <see cref="Agent"/> (by default part 10 § 3.1's stub, by key), (#1505) <c>deliver-v31</c> by <see cref="Deliver"/> and <c>alert-operator-v31</c> by <see cref="Alert"/>;
/// (#1506) <c>write-record-rows-v31</c> is the real activity over <see cref="Rows"/> and <see cref="Blobs"/>. Timers longer than five minutes (the job's)
/// wait, as durable timers do, until cancelled; shorter ones (a retry's) end at once.
/// </summary>
internal sealed class V31Job
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly Lazy<EngineCapabilities> ThisEngine =
        new(() => Factory().Current());

    /// <summary>The engine's capabilities factory over the bundled catalogues, as the host builds it.</summary>
    public static EngineCapabilitiesFactory Factory() =>
        new(OutputContractCatalog.Load(), FormatCatalogLoader.Load(), V31AgentsCatalogue.Load());

    /// <summary>The orchestration's clock.</summary>
    public static readonly DateTime Now = new(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc);

    public const string JobId = "job-31-0001";

    /// <summary>#1506: this engine's toolchain over the bundled catalogues, as the host builds it (<c>load-run-inputs-v31</c>'s).</summary>
    public static readonly Lazy<EngineToolchain> Toolchain = new(() => new EngineToolchain(
        EngineAttestation.Current(OutputContractCatalog.Load()), OutputContractCatalog.Load(), FormatCatalogLoader.Load(),
        new Lazy<V31AgentsCatalogue>(() => V31AgentsCatalogue.Load())));

    private V31Job(CompiledGoldenTests.GoldenCase golden, Action<JsonObject>? resolve = null)
    {
        Registry = new FakeRegistry();
        if (resolve == null)
        {
            (Ref, Pair) = Registry.Put(golden);
        }
        else
        {
            // #1506: the golden with its resolve.json changed, the pair made to agree
            Ref = Registry.PutChanged(golden, resolve);
            var resolveText = System.Text.Encoding.UTF8.GetString(Registry.Blobs[FakeRegistry.PathOf(Ref, CompiledPackage.ResolveFile)]);
            var lockText = System.Text.Encoding.UTF8.GetString(Registry.Blobs[FakeRegistry.PathOf(Ref, ResolveCompiler.LockFile)]);
            Pair = CompiledGoldenTests.Published(golden) with
            {
                Resolve = resolveText,
                ResolveHash = CanonicalJson.HashText(resolveText),
                Lock = lockText,
                LockHash = CanonicalJson.HashText(lockText),
            };
        }

        Store = StoreOver(Registry);
        Plan = Store.TryLoadAsync(Ref, CancellationToken.None).GetAwaiter().GetResult()!.Plan!;
        Agent = call => new AgentOutcome(AgentOutcomeKind.Completed, RunFixture.StubReply(Plan, call), 0, null);
        Alert = Alerts.Add;
        Entity = new ConsultGenerationJobEntity(
            Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(),
            HeldInputs, Usage);
        var state = ConsultGenerationJobState.Create(JobId, "user-1", Array.Empty<IReadOnlyDictionary<string, string>>());
        state.PackageSpecVersion = 31;
        // #1506 (Task 11): record storage 12, as the start's Initialize stamps a 31 job (the sealer seals from 12 only)
        state.SchemaVersion = 12;
        // as Task 13's start leaves it: the run-inputs document is the job's held inputs
        state.InputsBlob = Inputs.WriteAsync(AccountKind, "user-1", JobId, Document(RunFixture.GoldenInputs()), CancellationToken.None).GetAwaiter().GetResult();
        StateProperty.SetValue(Entity, state);
    }

    public FakeRegistry Registry { get; }

    public WorkflowPackageRef Ref { get; }

    public CompiledPackage Pair { get; }

    public CompiledPackageStore Store { get; }

    public RunPlan Plan { get; }

    public ConsultGenerationJobEntity Entity { get; }

    public FakeInputs Inputs { get; } = new();

    /// <summary>#1506: the account usage store the entity writes the job's usage row to.</summary>
    public IAccountUsageStore Usage { get; } = Substitute.For<IAccountUsageStore>();

    /// <summary>#1506 (Task 10): the record's rows, as <c>write-record-rows-v31</c> stores them (shared by two jobs to replay one).</summary>
    public FakeRecordRowStore Rows { get; set; } = new();

    /// <summary>#1506 (Task 10): the record's text, as the activities write it.</summary>
    public FakeRecordBlobStore Blobs { get; set; } = new();

    /// <summary>#1506 (Task 10): what <c>write-record-rows-v31</c> logged.</summary>
    public CapturingLogger<WriteRecordRowsV31Activity> RecordLog { get; } = new();

    /// <summary>#1506 (Task 11): the sealer's view of the job: the entity, read and stamped as the Durable client would.</summary>
    public EntityRecordJobs RecordJobs => _recordJobs ??= new EntityRecordJobs(Entity, AccountKind);

    private EntityRecordJobs? _recordJobs;

    /// <summary>#1506 (Task 11): what the sealer logged.</summary>
    public CapturingLogger<RecordSealer> SealLog { get; } = new();

    /// <summary>#1506 (Task 11): the real sealer over <see cref="RecordJobs"/>, <see cref="Rows"/> and <see cref="Blobs"/>.</summary>
    public RecordSealer Sealer => new(RecordJobs, Rows, Blobs, SealLog);

    /// <summary>#1506 (Task 11): answers each <c>seal-record-v31</c> call (by default, the real activity over <see cref="Sealer"/>).</summary>
    public Func<SealRecordV31Input, SealedRecord?>? Seal { get; set; }

    /// <summary>The job inputs' store the entity drops the held inputs through.</summary>
    public IJobInputsBlobStore HeldInputs { get; } = Substitute.For<IJobInputsBlobStore>();

    /// <summary>The account's kind (an organisation's).</summary>
    public const string AccountKind = Consultologist.Api.Auth.SignInKinds.Organisation;

    /// <summary>Answers each attempt of an agent call.</summary>
    public Func<AgentCall, AgentOutcome> Agent { get; set; }

    /// <summary>#1505: answers each attempt of a delivery send (by default, sent).</summary>
    public Func<DeliverV31Input, DeliverySendOutcome> Deliver { get; set; } = _ => new DeliverySendOutcome(DeliverySendResult.Sent, null);

    /// <summary>#1505 (D14): answers each <c>alert-operator-v31</c> call (by default, it keeps the alert in <see cref="Alerts"/>).</summary>
    public Action<OperatorAlert> Alert { get; set; }

    /// <summary>#1505 (D14): the alerts raised through the activity, in order.</summary>
    public List<OperatorAlert> Alerts { get; } = [];

    /// <summary>The limits the job's input records.</summary>
    public RunLimits Limits { get; set; } = new();

    /// <summary>The job's <c>resolveHash</c> as its input says it (the pair's by default).</summary>
    public string? ResolveHash { get; set; }

    /// <summary>The job's <c>lockHash</c> as its input says it (the pair's by default).</summary>
    public string? LockHash { get; set; }

    public ConsultGenerationJobState State => (ConsultGenerationJobState)StateProperty.GetValue(Entity)!;

    public static V31Job Of(string golden) => new(CompiledGoldenTests.Cases().Single(c => c.Name == golden));

    /// <summary>#1506: a job of <paramref name="golden"/> with <paramref name="resolve"/> applied to its <c>resolve.json</c>.</summary>
    public static V31Job Of(string golden, Action<JsonObject> resolve) => new(CompiledGoldenTests.Cases().Single(c => c.Name == golden), resolve);

    /// <summary>#1505: a job of <paramref name="golden"/>, a package of the test's own.</summary>
    public static V31Job Of(CompiledGoldenTests.GoldenCase golden) => new(golden);

    /// <summary>The engine's store over <paramref name="registry"/>, 31 admitted.</summary>
    public static CompiledPackageStore StoreOver(FakeRegistry registry) =>
        new(registry, registry, ThisEngine, SpecVersionAdmissionTests.With("31"), new EngineLimitSettings(new ConfigurationBuilder().Build()));

    /// <summary>The orchestration input Task 13's start would give.</summary>
    public ConsultGenerationOrchestrationInputV31 Input() => new(
        JobId, "user-1", Ref.Name, Ref.Version, ResolveHash ?? Pair.ResolveHash, LockHash ?? Pair.LockHash, Limits.ToJson(), 2000,
        State.InputsBlob!.Name, new JsonObject { ["id"] = JobId }, "America/Toronto", null, true, AccountKind);

    /// <summary>The context, every activity answered as the summary says; its input <paramref name="input"/>, else <see cref="Input"/>.</summary>
    public FakeOrchestrationContext Context(ConsultGenerationOrchestrationInputV31? input = null)
    {
        return new FakeOrchestrationContext(JobId, input ?? Input(), Entity, Now)
            .HoldTimersLongerThan(TimeSpan.FromMinutes(5))
            .On(ConsultGenerationActivityNames.LoadPlanV31, payload =>
                new LoadPlanV31Activity(Store).RunAsync((LoadPlanV31Input)payload!, CancellationToken.None).GetAwaiter().GetResult())
            .On(ConsultGenerationActivityNames.LoadRunInputsV31, payload =>
                new LoadRunInputsV31Activity(Inputs, Store, Toolchain.Value).RunAsync((LoadRunInputsV31Input)payload!, CancellationToken.None).GetAwaiter().GetResult())
            .On(ConsultGenerationActivityNames.RunAgentNodeV31, payload => Agent(((RunAgentNodeV31Input)payload!).Call))
            .On(ConsultGenerationActivityNames.EvaluateRuleV31, payload =>
                new EvaluateRuleV31Activity(Store, NullLogger<EvaluateRuleV31Activity>.Instance)
                    .RunAsync((EvaluateRuleV31Input)payload!, CancellationToken.None).GetAwaiter().GetResult())
            .On(ConsultGenerationActivityNames.DeliverV31, payload => Deliver((DeliverV31Input)payload!))
            .On(ConsultGenerationActivityNames.AlertOperatorV31, payload =>
            {
                Alert((OperatorAlert)payload!);
                return null;
            })
            .On(ConsultGenerationActivityNames.RenderNodeV31, payload =>
                new RenderNodeV31Activity(Store, Blobs, NullLogger<RenderNodeV31Activity>.Instance)
                    .RunAsync((RenderNodeV31Input)payload!, CancellationToken.None).GetAwaiter().GetResult())
            // #1506 (Task 10): the round's rows, stored by the real activity over the fake stores
            .On(ConsultGenerationActivityNames.WriteRecordRowsV31, payload =>
                new WriteRecordRowsV31Activity(Rows, Blobs, RecordLog)
                    .RunAsync((WriteRecordRowsV31Input)payload!, CancellationToken.None).GetAwaiter().GetResult())
            // #1506 (Task 11): the record sealed by the real activity over the fake stores and the entity
            .On(ConsultGenerationActivityNames.SealRecordV31, payload => Seal is { } seal
                ? seal((SealRecordV31Input)payload!)
                : SealRecordV31Activity.RunAsync((SealRecordV31Input)payload!, Sealer, CancellationToken.None).GetAwaiter().GetResult())
            // #1503 (D9): the input is DrainAware; the drain is off here and check-drain is left out of the calls the
            // tests read (the drain's own tests answer it recorded: DrainDoorsTests).
            .Unrecorded(ConsultGenerationActivityNames.CheckDrain, _ => false);
    }

    /// <summary>Runs the orchestration on <paramref name="context"/> to its end.</summary>
    public static Task RunAsync(FakeOrchestrationContext context) => new ConsultGenerationOrchestratorV31().RunAsync(context);

    /// <summary>
    /// Runs the orchestration with every activity held, on a thread with no synchronization context, releasing the oldest
    /// held answer until none is held; the run must have ended by then.
    /// </summary>
    public static Task DriveAsync(FakeOrchestrationContext context) => Task.Run(() =>
    {
        var run = RunAsync(context.HoldActivities());
        while (context.HeldActivities > 0)
            context.ReleaseOldest();
        Assert.True(run.IsCompleted, "The orchestration has not ended: something it waits on was never released.");
        return run;
    });

    /// <summary>The agent activity's calls, in the order made.</summary>
    public static List<AgentCall> AgentCalls(FakeOrchestrationContext context) =>
        context.Calls.Where(c => c.Kind == "activity" && c.Name == ConsultGenerationActivityNames.RunAgentNodeV31)
            .Select(c => System.Text.Json.JsonSerializer.Deserialize<RunAgentNodeV31Input>(c.InputJson)!.Call)
            .ToList();

    /// <summary>The inputs document of <paramref name="inputs"/>, as <see cref="IRunInputsStore"/> holds it.</summary>
    public static JsonObject Document(RunInputs inputs)
    {
        var given = new JsonObject();
        foreach (var (id, read) in inputs.Inputs)
            given[id] = read?.DeepClone();
        var profile = new JsonObject();
        foreach (var (field, text) in inputs.Profile)
            profile[field] = text;
        return new JsonObject { ["inputs"] = given, ["profile"] = profile };
    }

    /// <summary>An in-memory <see cref="IRunInputsStore"/> where the real one writes: <see cref="RunInputsBlobStore.PointerFor"/>.</summary>
    internal sealed class FakeInputs : IRunInputsStore
    {
        public ConcurrentDictionary<ConsultInputsBlobPointer, JsonObject> Documents { get; } = new();

        public Task<ConsultInputsBlobPointer> WriteAsync(string? accountKind, string appUserId, string jobId, JsonObject document, CancellationToken ct)
        {
            var pointer = RunInputsBlobStore.PointerFor(accountKind, appUserId, jobId);
            Documents[pointer] = (JsonObject)document.DeepClone();
            return Task.FromResult(pointer);
        }

        public Task<JsonObject?> ReadAsync(ConsultInputsBlobPointer pointer, CancellationToken ct) =>
            Task.FromResult(Documents.TryGetValue(pointer, out var document) ? (JsonObject?)document.DeepClone() : null);
    }
}
