using System.Reflection;
using System.Text;
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
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1503 (specVersion 31, area 06; part 09 § 1.2 steps 1 to 6, § 5.3; R1; D6): a 31 job starts through the real starter.
/// With the preview setting unset a 31 package is answered as today and nothing of 31 is asked; with it, the version's
/// stamped pair is loaded, the account and the inputs checked, the inputs held, the entity initialised with the hashes
/// and the limits, and the job scheduled on <see cref="ConsultGenerationOrchestratorV31"/>; a refusal is a start failure
/// recorded on the job. A 30 package starts exactly as before either way. The starter's stores are substituted as
/// <see cref="EngineRun"/> wires them. Content is fictional (Margaret Okafor, Dr A. Sample).
/// </summary>
public class V31JobStartTests
{
    private static readonly PropertyInfo StateProperty =
        typeof(ConsultGenerationJobEntity).GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static readonly Lazy<EngineCapabilities> ThisEngine =
        new(() => new EngineCapabilitiesFactory(OutputContractCatalog.Load(), FormatCatalogLoader.Load()).Current());

    private const string Draft = "Margaret Okafor, 61, referred for a second opinion on her anaemia. (Fictional.)";

    private const string Zone = "America/Toronto";

    [Fact]
    public async Task WithoutThePreviewSetting_A31PackageIsAnsweredAsToday()
    {
        var start = new Start(preview: null);
        var packageRef = start.Put("nested-note");
        start.PackageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Throws(new WorkflowPackageSpecVersionException(RefText(packageRef), 31, WorkflowPackageStore.SupportedSpecVersions.ToList()));

        var outcome = await start.RunAsync(packageRef, Text(Draft));

        Assert.Null(outcome.JobId);
        Assert.Equal(ConsultGenerationJobStartError.SpecVersionNotYetExecutable, outcome.Error);
        Assert.Equal(0, start.Store.Calls);
        Assert.Empty(start.Registry.Reads);
        Assert.Empty(start.Scheduled);
        Assert.Empty(start.Signals);
    }

    public static TheoryData<string> RegistryFailures() => ["no latest pointer", "an invalid latest pointer", "a storage failure"];

    [Theory]
    [MemberData(nameof(RegistryFailures))]
    public async Task ARegistryFailure_IsAnsweredAsWithThePreviewOff(string condition)
    {
        // the 5–30 store and the 31 load read the same registry: whatever it does to today's start (the store's
        // ResolveAsync, as WorkflowPackageStore throws for the condition), it does to the version's resolution and reads
        Exception Failure() => condition switch
        {
            "no latest pointer" => new InvalidOperationException("Workflow package blob 'unknown-package/latest.json' was not found in container 'workflow-packages'."),
            "an invalid latest pointer" => new InvalidOperationException("Latest pointer for workflow package 'unknown-package' is missing or holds an invalid version."),
            _ => new Azure.RequestFailedException(503, "The server is busy."),
        };

        async Task<(ConsultGenerationJobStartOutcome? Outcome, Exception? Thrown, Start Start)> RunAsync(string? preview, bool atRead)
        {
            var start = new Start(preview);
            if (atRead)
                start.Registry.ReadFailure = Failure();
            else
                start.Registry.ResolveFailure = Failure();
            start.PackageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>()).Throws(_ => Failure());
            try
            {
                return (await start.RunAsync(new ConsultGenerationRequest(null, "unknown-package@latest", Inputs: Text(Draft))), null, start);
            }
            catch (Exception ex)
            {
                return (null, ex, start);
            }
        }

        foreach (var atRead in condition == "a storage failure" ? new[] { false, true } : [false])
        {
            var off = await RunAsync(null, atRead);
            var on = await RunAsync("31", atRead);

            Assert.Equal(off.Outcome, on.Outcome);
            Assert.Equal(off.Thrown?.GetType(), on.Thrown?.GetType());
            Assert.Empty(on.Start.Scheduled);
            Assert.Empty(on.Start.Signals);
            if (condition != "a storage failure")
                Assert.Equal(ConsultGenerationJobStartError.RegistryUnavailable, on.Outcome!.Error);
        }
    }

    [Theory]
    [MemberData(nameof(RegistryFailures))]
    public async Task TheStore_AnswersARegistryFailure_NeverThrows(string condition)
    {
        var registry = new FakeRegistry();
        var packageRef = registry.Put(RunFixture.Golden("nested-note")).Ref;
        if (condition == "a storage failure")
            registry.ReadFailure = new Azure.RequestFailedException(503, "The server is busy.");
        else
            registry.ResolveFailure = new InvalidOperationException("Latest pointer for workflow package 'nested-note' is missing or holds an invalid version.");

        var store = V31Job.StoreOver(registry);
        var load = await store.TryLoadAsync(packageRef, CancellationToken.None);

        Assert.NotNull(load);
        Assert.True(load.Unreadable);
        Assert.Null(load.Plan);
        Assert.Null(load.Refusal);
        Assert.StartsWith($"The registry could not be read for {packageRef.Name}@", load.OperatorDetail, StringComparison.Ordinal);
        Assert.DoesNotContain("busy", load.OperatorDetail, StringComparison.Ordinal);

        // at run time it is the engine's fault, not the package's: the activity fails (the job with A06-010), as when the read threw
        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LoadPlanV31Activity(store).RunAsync(new LoadPlanV31Input(packageRef.Name, packageRef.Version, "sha256:r", "sha256:l"), CancellationToken.None));
        Assert.Equal(load.OperatorDetail, failed.Message);
    }

    [Fact]
    public async Task WithIt_AJobIsScheduledOnTheV31Orchestrator()
    {
        var start = new Start(preview: "31");
        var packageRef = start.Put("nested-note");
        var pair = CompiledGoldenTests.Published(RunFixture.Golden("nested-note"));

        var outcome = await start.RunAsync(packageRef, Text(Draft));

        Assert.Null(outcome.Error);
        var (name, payload, options) = Assert.Single(start.Scheduled);
        Assert.Equal(nameof(ConsultGenerationOrchestratorV31), name);
        Assert.Equal(outcome.JobId, options!.InstanceId);
        var input = Assert.IsType<ConsultGenerationOrchestrationInputV31>(payload);
        Assert.Equal(outcome.JobId, input.JobId);
        Assert.Equal("user-1", input.AppUserId);
        Assert.Equal((packageRef.Name, packageRef.Version), (input.PackageName, input.PackageVersion));
        Assert.Equal(pair.ResolveHash, input.ResolveHash);
        Assert.Equal(pair.LockHash, input.LockHash);
        Assert.Equal(new RunLimits().ToJson().ToJsonString(), input.Limits.ToJsonString());
        Assert.Equal(2000, input.RegexMilliseconds);
        Assert.Equal(Zone, input.TimeZone);
        Assert.True(input.DrainAware);
        Assert.Equal(SignInKinds.Organisation, input.AccountKind);
        Assert.Equal(outcome.JobId, (string?)input.RunFactsBase["id"]);
        Assert.True(input.RunFactsBase.ContainsKey("engine"));
        Assert.DoesNotContain("Okafor", JsonSerializer.Serialize(input), StringComparison.Ordinal);

        // the inputs are held where the job's held inputs live, and the entity records the pointer
        var pointer = RunInputsBlobStore.PointerFor(SignInKinds.Organisation, "user-1", outcome.JobId!);
        Assert.Equal(pointer.Name, input.InputsBlob);
        Assert.Equal(
            $$$"""{"inputs":{"consult_draft":{"value":"{{{Draft}}}","content":"{{{Draft}}}","title":null}},"profile":{}}""",
            start.Inputs.Documents[pointer].ToJsonString());

        var (operation, signalled) = Assert.Single(start.Signals);
        Assert.Equal(nameof(ConsultGenerationJobEntity.Initialize), operation);
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(signalled);
        Assert.Equal(31, initialize.PackageSpecVersion);
        Assert.Equal(RefText(packageRef), initialize.WorkflowPackage);
        Assert.Equal(pair.ResolveHash, initialize.ResolveHash);
        Assert.Equal(pair.LockHash, initialize.LockHash);
        Assert.Equal(input.Limits.ToJsonString(), initialize.Limits!.ToJsonString());
        Assert.Null(initialize.StartedAtUtc);
        Assert.Equal(pointer, initialize.InputsBlob);
        _ = start.PackageStore.DidNotReceiveWithAnyArgs().ResolveAsync(default!, default);

        // the entity, Initialized as the starter signalled it, holds the header the orchestration's RecordV31Header agrees with
        var entity = Entity();
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(initialize));
        var state = (ConsultGenerationJobState)StateProperty.GetValue(entity)!;
        Assert.Equal((31, pair.ResolveHash, pair.LockHash), (state.PackageSpecVersion, state.ResolveHash, state.LockHash));
        Assert.Equal(new RunLimits().ToJson().ToDictionary(m => m.Key, m => (int)m.Value!), state.Limits);
        Assert.Null(state.StartedAtUtc);

        var startedAt = new DateTimeOffset(2026, 11, 17, 14, 0, 0, TimeSpan.Zero);
        entity.RecordV31Header(new ConsultGenerationV31Header(input.ResolveHash, input.LockHash, state.Limits!, startedAt));
        Assert.Equal(startedAt, state.StartedAtUtc);
        Assert.Equal(pair.ResolveHash, state.ResolveHash);
    }

    [Fact]
    public async Task ALoadRefusal_IsARecordedStartFailure()
    {
        var start = new Start(preview: "31");
        var packageRef = start.Put("nested-note");
        var resolvePath = FakeRegistry.PathOf(packageRef, CompiledPackage.ResolveFile);
        start.Registry.Blobs[resolvePath] = [.. start.Registry.Blobs[resolvePath], (byte)' '];

        var outcome = await start.RunAsync(packageRef, Text(Draft));

        var sentence = CompiledPackageStore.HashMismatch(packageRef.Name, packageRef.Version);
        V31Contract.AssertSentence(new SourceRefusal("S09-009", sentence, ""));
        Assert.NotNull(outcome.JobId);
        Assert.Equal(ConsultGenerationJobStartError.PackageContentRejected, outcome.Error);
        Assert.Equal(sentence, outcome.ErrorDetail);
        Assert.Equal(sentence, outcome.SenderSafeDetail);
        Assert.Empty(start.Scheduled);
        Assert.Empty(start.Inputs.Documents);

        var failure = AssertStartFailure(start, outcome.JobId!, sentence);
        Assert.Equal(31, failure.Initialize.PackageSpecVersion);
    }

    [Fact]
    public async Task AnAccountWithNoTimeZone_IsRefusedAtStart()
    {
        var start = new Start(preview: "31", timeZone: null);
        var packageRef = start.Put("nested-note");

        var outcome = await start.RunAsync(packageRef, Text(Draft));

        V31Contract.AssertSentence(new SourceRefusal("S06-008", V31StartChecks.NoTimeZone, ""));
        Assert.NotNull(outcome.JobId);
        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Equal(V31StartChecks.NoTimeZone, outcome.ErrorDetail);
        Assert.Equal(V31StartChecks.NoTimeZone, outcome.SenderSafeDetail);
        Assert.Empty(start.Scheduled);
        Assert.Empty(start.Inputs.Documents);
        AssertStartFailure(start, outcome.JobId!, V31StartChecks.NoTimeZone);
    }

    [Fact]
    public async Task EveryRefusal_IsRecorded_InOneStartFailure()
    {
        var start = new Start(preview: "31", timeZone: "Mars/Phobos");
        var packageRef = start.Put("nested-note");

        var outcome = await start.RunAsync(packageRef, Text("   "));

        var expected = $"{V31StartChecks.NoTimeZone}\nInput 'consult_draft' is required; fill it in to run this workflow.";
        Assert.Equal(expected, outcome.ErrorDetail);
        Assert.Empty(start.Scheduled);
        AssertStartFailure(start, outcome.JobId!, expected);
    }

    [Fact]
    public async Task AnUndeclaredInput_IsRecorded_ButNeverQuotedToTheSender()
    {
        var start = new Start(preview: "31");
        var packageRef = start.Put("nested-note");
        var inputs = Text(Draft);
        inputs["Okafor_referral"] = ConsultInputValue.OfText(Draft);

        var outcome = await start.RunAsync(packageRef, inputs);

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Equal("Unknown input(s) 'Okafor_referral' (declared: consult_draft).", outcome.ErrorDetail);
        Assert.Null(outcome.SenderSafeDetail);
        Assert.Empty(start.Scheduled);
    }

    [Fact]
    public async Task AnUndeclaredInput_AndNoTimeZone_TellTheSenderOnlyTheZone()
    {
        // the record holds every sentence; the sender is told every one but the undeclared ids' (A06-013), which quotes them
        var start = new Start(preview: "31", timeZone: null);
        var packageRef = start.Put("nested-note");
        var inputs = Text(Draft);
        inputs["Okafor_referral"] = ConsultInputValue.OfText(Draft);

        var outcome = await start.RunAsync(packageRef, inputs);

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Equal($"{V31StartChecks.NoTimeZone}\nUnknown input(s) 'Okafor_referral' (declared: consult_draft).", outcome.ErrorDetail);
        Assert.Equal(V31StartChecks.NoTimeZone, outcome.SenderSafeDetail);
        AssertStartFailure(start, outcome.JobId!, outcome.ErrorDetail!);
    }

    [Fact]
    public async Task ADocumentSentToATextInput_ArrivesAsItsText()
    {
        var start = new Start(preview: "31");
        var packageRef = start.Put("nested-note");
        var request = new ConsultGenerationRequest(
            null,
            RefText(packageRef),
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["consult_draft"] = [new InputFilePayload("text/plain", Encoding.UTF8.GetBytes(Draft))],
            });

        var outcome = await start.RunAsync(request);

        Assert.Null(outcome.Error);
        var document = Assert.Single(start.Inputs.Documents).Value;
        Assert.Equal(Draft, (string?)document["inputs"]!["consult_draft"]!["value"]);
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload);
        Assert.Equal(ConsultInputOriginKinds.Document, Assert.Single(initialize.InputDocumentOrigins!["consult_draft"]).Kind);
    }

    [Fact]
    public async Task A30Package_WithThePreviewSettingOn_StartsExactlyAsBefore()
    {
        var package = ByteEquivalenceFixtures.Load("example-prior-letters");
        var inputs = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = ConsultInputValue.OfText(Draft),
            ["prior_letters"] = ConsultInputValue.OfArray([ConsultInputValue.OfText("Dear Dr A. Sample, Margaret Okafor was seen in clinic. (Fictional.)")]),
        };

        var off = new Start(preview: null, package30: package);
        var offOutcome = await off.RunAsync(PackageRefOf(package), inputs);
        var on = new Start(preview: "31", package30: package);
        var onOutcome = await on.RunAsync(PackageRefOf(package), inputs);

        Assert.Null(offOutcome.Error);
        Assert.Null(onOutcome.Error);
        Assert.Equal(nameof(ConsultGenerationOrchestrator), Assert.Single(on.Scheduled).Name);
        Assert.Equal(Masked(off, offOutcome.JobId!), Masked(on, onOutcome.JobId!));
        Assert.Equal(0, off.Store.Calls);
        Assert.Equal(1, on.Store.Calls);
        Assert.Equal([FakeRegistry.PathOf(PackageRefOf(package), PublishedStamp.FileName)], on.Registry.Reads);
        Assert.Empty(on.Inputs.Documents);
    }

    [Fact]
    public async Task ExamplePriorLetters_WithNoLetters_StartsAt31()
    {
        // R1: at 31 a list left empty runs its loop over 0 items; 30 refuses the same start (NoApplicableDeliverable)
        var draft = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal) { ["consult_draft"] = ConsultInputValue.OfText(Draft) };

        var package30 = ByteEquivalenceFixtures.Load("example-prior-letters");
        var at30 = new Start(preview: "31", package30: package30);
        var outcome30 = await at30.RunAsync(PackageRefOf(package30), draft);

        var at31 = new Start(preview: "31");
        var packageRef31 = at31.PutPriorLetters();
        var outcome31 = await at31.RunAsync(packageRef31, draft);

        Assert.Equal(ConsultGenerationJobStartError.NoApplicableDeliverable, outcome30.Error);
        Assert.Empty(at30.Scheduled);
        Assert.Null(outcome31.Error);
        Assert.Equal(nameof(ConsultGenerationOrchestratorV31), Assert.Single(at31.Scheduled).Name);
        Assert.False(Assert.Single(at31.Inputs.Documents).Value["inputs"]!.AsObject().ContainsKey("prior_letters"));

        // and an empty list, supplied, starts too
        var at31Empty = new Start(preview: "31");
        var withNone = new Dictionary<string, ConsultInputValue>(draft, StringComparer.Ordinal) { ["prior_letters"] = ConsultInputValue.OfArray([]) };
        var outcomeEmpty = await at31Empty.RunAsync(at31Empty.PutPriorLetters(), withNone);
        Assert.Null(outcomeEmpty.Error);
        Assert.Equal("[]", Assert.Single(at31Empty.Inputs.Documents).Value["inputs"]!["prior_letters"]!["value"]!.ToJsonString());
    }

    private static Dictionary<string, ConsultInputValue> Text(string draft) =>
        new(StringComparer.Ordinal) { ["consult_draft"] = ConsultInputValue.OfText(draft) };

    private static string RefText(WorkflowPackageRef packageRef) => $"{packageRef.Name}@{packageRef.Version}";

    private static WorkflowPackageRef PackageRefOf(WorkflowPackage package) =>
        WorkflowPackageRef.TryParse(package.Ref, out var parsed) ? parsed! : throw new InvalidOperationException(package.Ref);

    private static ConsultGenerationJobStartFailure AssertStartFailure(Start start, string jobId, string reason)
    {
        var (operation, payload) = Assert.Single(start.Signals);
        Assert.Equal(nameof(ConsultGenerationJobEntity.RecordStartFailure), operation);
        var failure = Assert.IsType<ConsultGenerationJobStartFailure>(payload);
        Assert.Equal(reason, failure.Reason);
        Assert.Equal(jobId, failure.Initialize.JobId);
        Assert.Equal(31, failure.Initialize.PackageSpecVersion);
        return failure;
    }

    /// <summary>The start's Initialize and orchestration input as JSON, the job id masked.</summary>
    private static string Masked(Start start, string jobId) =>
        (JsonSerializer.Serialize(Assert.Single(start.Signals).Payload) + "\n" + JsonSerializer.Serialize(Assert.Single(start.Scheduled).Input))
            .Replace(jobId, "<job>", StringComparison.Ordinal)
            .Replace(jobId[..8], "<job8>", StringComparison.Ordinal);

    private static ConsultGenerationJobEntity Entity() => new(
        Substitute.For<IConsultGenerationJobIndexStore>(), Substitute.For<IJobOutputsBlobStore>(),
        Substitute.For<IJobInputsBlobStore>(), Substitute.For<IAccountUsageStore>());

    /// <summary>The real starter over substituted stores, a <see cref="FakeRegistry"/> and the real 31 store (counted).</summary>
    private sealed class Start
    {
        private readonly ConsultGenerationJobStarter _starter;

        public Start(string? preview, string? timeZone = Zone, WorkflowPackage? package30 = null)
        {
            var settings = Substitute.For<IAccountSettingsStore>();
            if (timeZone != null)
            {
                settings.GetAsync(Arg.Any<string>(), AccountSettingKeys.TimeZone, Arg.Any<CancellationToken>())
                    .Returns(new AccountSetting(AccountSettingKeys.TimeZone, timeZone, "text/plain", DateTimeOffset.UnixEpoch));
            }

            var accounts = Substitute.For<IAccountStore>();
            accounts.GetAccountKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(SignInKinds.Organisation);

            if (package30 != null)
                PackageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>()).Returns(package30);

            var rateLimiter = Substitute.For<IAccountRateLimiter>();
            rateLimiter.TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new RateLimitDecision(true, 60, 59, TimeSpan.FromMinutes(37)));

            var entities = Substitute.For<DurableEntityClient>("test");
            Client.Entities.Returns(entities);
            entities.SignalEntityAsync(Arg.Any<EntityInstanceId>(), Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<SignalEntityOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Signals.Add(((string)call[1], call[2]));
                    return Task.CompletedTask;
                });
            Client.ScheduleNewOrchestrationInstanceAsync(Arg.Any<TaskName>(), Arg.Any<object?>(), Arg.Any<StartOrchestrationOptions?>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var options = (StartOrchestrationOptions?)call[2];
                    Scheduled.Add((((TaskName)call[0]).Name, call[1], options));
                    return Task.FromResult(options!.InstanceId!);
                });

            var admission = SpecVersionAdmissionTests.With(preview);
            var limits = new EngineLimitSettings(new ConfigurationBuilder().Build());
            Store = new CountingStore(new CompiledPackageStore(Registry, Registry, ThisEngine.Value, admission, limits));
            var engine = EngineAttestation.Current(V29Catalog.Catalog, null);
            _starter = new ConsultGenerationJobStarter(
                NullLogger<ConsultGenerationJobStarter>.Instance,
                PackageStore,
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
                null,
                new V31JobStarter(admission, Store, Inputs, limits, accounts, settings, engine, NullLogger<V31JobStarter>.Instance));
        }

        public FakeRegistry Registry { get; } = new();

        public CountingStore Store { get; }

        public V31Job.FakeInputs Inputs { get; } = new();

        public IWorkflowPackageStore PackageStore { get; } = Substitute.For<IWorkflowPackageStore>();

        public DurableTaskClient Client { get; } = Substitute.For<DurableTaskClient>("test");

        /// <summary>Each entity signal: the operation and its payload, in order.</summary>
        public List<(string Operation, object? Payload)> Signals { get; } = new();

        /// <summary>Each orchestration scheduled: its name, its input and its options, in order.</summary>
        public List<(string Name, object? Input, StartOrchestrationOptions? Options)> Scheduled { get; } = new();

        /// <summary>Publishes the golden named <paramref name="golden"/>.</summary>
        public WorkflowPackageRef Put(string golden) => Registry.Put(RunFixture.Golden(golden)).Ref;

        /// <summary>
        /// Publishes part 04 § 2.4's prior letters at 31 as <c>example-prior-letters</c>, its list <c>prior_letters</c>
        /// optional, as the 30 package's is.
        /// </summary>
        public WorkflowPackageRef PutPriorLetters()
        {
            var files = PlanRunnerLoopsTests.PriorLetters().WithJson("manifest.json", m =>
            {
                m["name"] = "example-prior-letters";
                m["inputs"]!["prior_letters"]!["required"] = false;
            });
            var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed, templates: PlanRunnerLoopsTests.PriorLettersReads());
            Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Sentence}")));
            return Registry.Put(compiled!.Resolve, compiled.Lock);
        }

        public Task<ConsultGenerationJobStartOutcome> RunAsync(WorkflowPackageRef packageRef, Dictionary<string, ConsultInputValue> inputs) =>
            RunAsync(new ConsultGenerationRequest(null, RefText(packageRef), Inputs: inputs));

        public Task<ConsultGenerationJobStartOutcome> RunAsync(ConsultGenerationRequest request) =>
            _starter.StartAsync(Client, request, "user-1", new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
    }

    /// <summary>The 31 store, its calls counted.</summary>
    private sealed class CountingStore(ICompiledPackageStore inner) : ICompiledPackageStore
    {
        private int _calls;

        public int Calls => _calls;

        public Task<CompiledLoad?> TryLoadAsync(WorkflowPackageRef packageRef, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return inner.TryLoadAsync(packageRef, ct);
        }
    }

    private sealed class NoTerminology : ITerminologyAttestationSource
    {
        public ValueTask<TerminologyAttestation?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult<TerminologyAttestation?>(null);
    }
}
