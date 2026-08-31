using System.Text;
using Consultologist.Api.Documents;
using Consultologist.Api.Auth;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.RateLimiting;
using Consultologist.Api.Workflow;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.Entities;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

public class ConsultGenerationJobStarterTests
{
    private readonly IWorkflowPackageStore _packageStore = Substitute.For<IWorkflowPackageStore>();
    private readonly IWorkflowPackagePinResolver _pinResolver = Substitute.For<IWorkflowPackagePinResolver>();
    private readonly IAccountRateLimiter _rateLimiter = Substitute.For<IAccountRateLimiter>();
    private readonly DurableTaskClient _client = Substitute.For<DurableTaskClient>("test");
    private readonly DurableEntityClient _entities = Substitute.For<DurableEntityClient>("test");
    private readonly FakeOwnership _ownership = new();
    private readonly IAccountStore _accounts = Substitute.For<IAccountStore>();
    private readonly IAccountSettingsStore _settings = Substitute.For<IAccountSettingsStore>();
    private readonly IJobOutputsBlobStore _outputsBlobs = Substitute.For<IJobOutputsBlobStore>();
    private readonly IJobInputsBlobStore _inputsBlobs = Substitute.For<IJobInputsBlobStore>();

    // #290: a terse but genuine referral. These fixtures used to say
    // "draft", which is not a referral and which the content floor
    // correctly refuses — the tests were asserting behaviour against
    // input no clinician would send.
    private const string Referral =
        "65M, newly diagnosed adenocarcinoma of the lung, stage IIIA, for consideration of chemoradiation. PMHx HTN.";

    private ConsultGenerationJobStarter CreateStarter(ILogger<ConsultGenerationJobStarter>? logger = null, string? apiHost = null)
    {
        _client.Entities.Returns(_entities);
        _rateLimiter
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitDecision(true, 60, 59, TimeSpan.FromMinutes(37)));

        return new ConsultGenerationJobStarter(
            logger ?? NullLogger<ConsultGenerationJobStarter>.Instance,
            _packageStore,
            _pinResolver,
            TestCatalog.Instance,
            _rateLimiter,
            _ownership,
            // #398: the build's attestation — the vendored indexes the test output carries.
            // #514: and the host the test names, as Public__ApiHost would.
            EngineAttestation.Current(TestCatalog.Instance, apiHost),
            // #403: what the terminology server says, or nothing.
            _terminology,
            _accounts,
            _settings,
            _outputsBlobs,
            _inputsBlobs);
    }

    private readonly FakeTerminologySource _terminology = new();

    private sealed class FakeTerminologySource : ITerminologyAttestationSource
    {
        public TerminologyAttestation? Next { get; set; }
        public ValueTask<TerminologyAttestation?> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Next);
    }

    [Fact]
    public async Task TheJob_RecordsTheTerminologyTheServerReported_OrNothing()
    {
        // #403: the edition and the server's build, as the attestation source
        // says at start; a source that says nothing stamps nothing.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));
        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        _terminology.Next = new TerminologyAttestation(
            new TerminologySnapshot("SNOMEDCT 20251130 import.", "2025-11-30", "2025-12-21T22:39:16.944Z"),
            "snomed-snowstorm-mcp@0fff939d4a5c3a6e7b8c9d0e1f2a3b4c5d6e7f80", DateTimeOffset.UtcNow);
        var outcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
        Assert.Null(outcome.Error);
        Assert.Equal("2025-11-30", orchestrationInput!.Terminology!.Version);
        Assert.Equal("SNOMEDCT 20251130 import.", orchestrationInput.Terminology.Edition);
        Assert.Equal("snomed-snowstorm-mcp@0fff939d4a5c3a6e7b8c9d0e1f2a3b4c5d6e7f80", orchestrationInput.TerminologyServerRef);

        _terminology.Next = null;
        orchestrationInput = null;
        await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
        Assert.Null(orchestrationInput!.Terminology);
        Assert.Null(orchestrationInput.TerminologyServerRef);
    }

    [Fact]
    public async Task TheJob_RecordsTheFormatAndProvenanceVersions_TheBuildWasBuiltAgainst()
    {
        // #398: package-format@<vendored> and provenance@<vendored>, from the
        // same attestation Public/Engine serves — never from a registry
        // lookup at run time, and never from a pin the engine might not run.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);

        Assert.Null(outcome.Error);
        var expectedFormat = EngineAttestation.PackageFormatVersionIn(AppContext.BaseDirectory);
        var expectedProvenance = EngineAttestation.ProvenanceVersionIn(AppContext.BaseDirectory);
        Assert.NotNull(expectedFormat);
        Assert.NotNull(expectedProvenance);
        Assert.Equal($"package-format@{expectedFormat}", orchestrationInput!.PackageFormatRef);
        Assert.Equal($"provenance@{expectedProvenance}", orchestrationInput.ProvenanceRef);
        Assert.NotEqual(orchestrationInput.CatalogRef, orchestrationInput.PackageFormatRef);
    }

    [Fact]
    public async Task TheRecord_NamesTheHostAndTheEngine_FromTheAttestation()
    {
        // #514: where the job runs and what runs it — the deployment's
        // canonical host from configuration (never the request's authority,
        // never the Azure name) and the build's commit; null host when the
        // deployment names none, which the record then says.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter(apiHost: "https://East.CA.api.consultologist.ai/").StartAsync(
            _client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.Equal("east.ca.api.consultologist.ai", orchestrationInput!.ApiHost);
        Assert.Equal(EngineAttestation.Current(TestCatalog.Instance).Commit, orchestrationInput.EngineCommit);

        var unnamed = await CreateStarter().StartAsync(
            _client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
        Assert.Null(unnamed.Error);
        Assert.Null(orchestrationInput!.ApiHost);
    }

    /// <summary>
    /// Overrides the permissive default CreateStarter installs. Must be
    /// called AFTER it, since NSubstitute lets the last matching stub win.
    /// </summary>
    private void Refuse() =>
        _rateLimiter
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitDecision(false, 60, 0, TimeSpan.FromMinutes(37)));

    private static WorkflowPackage ExecutableV5Package()
    {
        var manifest = V5Fixtures.Manifest();
        var files = V5Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            SchemaContracts: TestOutputContracts.CatalogSchemas,
            Data: data,
            ResultNodeId: "section-instructions");
    }

    [Fact]
    public async Task MalformedPackageRef_ReturnsError()
    {
        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral, "not a valid ref"),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.MalformedPackageRef, outcome.Error);
        Assert.Null(outcome.JobId);
    }

    [Fact]
    public async Task ARecordedSluggedPackage_IsNotForeign_AndAForeignSluggedOneIs()
    {
        // #447: the starter asks the record, not the name.
        WithTypedPackage();
        _ownership.Records.Add(("11112222333344445555666677778888", "acct-111122223333-breast"));

        var own = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral, "acct-111122223333-breast@latest"),
            "11112222333344445555666677778888",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);
        Assert.NotEqual(ConsultGenerationJobStartError.ForeignPackageRef, own.Error);
        Assert.NotEqual(ConsultGenerationJobStartError.MalformedPackageRef, own.Error);

        var foreign = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral, "acct-111122223333-breast@latest"),
            "99999999999999999999999999999999",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);
        Assert.Equal(ConsultGenerationJobStartError.ForeignPackageRef, foreign.Error);
    }

    [Fact]
    public async Task ForeignAccountPackageRef_ReturnsError()
    {
        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral, "acct-deadbeefdead@latest"),
            "11112222333344445555666677778888",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.ForeignPackageRef, outcome.Error);
    }

    [Fact]
    public async Task RegistryFailure_ReturnsError()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorkflowPackage>>(_ => throw new InvalidOperationException("registry down"));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.Email, "user@example.com"),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.RegistryUnavailable, outcome.Error);
    }

    [Fact]
    public async Task NonExecutablePackage_ReturnsError()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(V5Fixtures.Manifest()));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.PackageNotExecutable, outcome.Error);
    }

    private static WorkflowPackage ExecutableV7Package(
        WorkflowPackageManifest manifest,
        IReadOnlyList<WorkflowResolvedResult> results)
    {
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            SchemaContracts: TestOutputContracts.CatalogSchemas,
            Data: data,
            ResultNodeId: results.Count == 1 ? results[0].NodeId : null,
            Results: results);
    }

    [Fact]
    public async Task TheInputs_AreHeld_AtStart_AndThePointerRidesInitialize()
    {
        // #547: the effective map, byte-for-byte what runs, plus each
        // supplied value's typed wire form — written before the
        // orchestration is scheduled, into the container the kind names.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));
        _accounts.GetAccountKindAsync("user-1", Arg.Any<CancellationToken>()).Returns("organisation");
        var pointer = new ConsultInputsBlobPointer("org-job-inputs", "user-1/x.json");
        JobInputsPayload? written = null;
        _inputsBlobs.WriteAsync("organisation", "user-1", Arg.Any<string>(), Arg.Do<JobInputsPayload>(p => written = p), Arg.Any<CancellationToken>())
            .Returns(pointer);

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Any<object?>(),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.Equal(Referral, written!.Effective!["consult_draft"]);
        Assert.Equal(ConsultInputValue.OfText(Referral).AsJson(), written.Supplied!["consult_draft"]);
        Assert.Equal(pointer, initialize!.InputsBlob);
    }

    [Fact]
    public async Task AnInputsWriteFailure_NeverRefusesTheStart()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));
        _inputsBlobs.WriteAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobInputsPayload>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage blip"));

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Any<object?>(),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);

        // The run proceeds unheld; a rerun later refuses by name.
        Assert.Null(outcome.Error);
        Assert.Null(initialize!.InputsBlob);
    }

    [Fact]
    public async Task ALegacyJob_IsNotHeld()
    {
        // v5/v6 carries no effective map — the archived-format tail.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV5Package());
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Any<object?>(),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);

        Assert.Null(outcome.Error);
        await _inputsBlobs.DidNotReceiveWithAnyArgs().WriteAsync(default, default!, default!, default!, default);
    }

    // ----- #549: a rerun replays the held inputs and names its source -----

    [Fact]
    public async Task ARerunStart_StampsARerunOrigin_OnEveryEffectiveSlot()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Any<object?>(),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App, RerunOfJobId: "source-job-1"), CancellationToken.None);

        Assert.Null(outcome.Error);
        var origins = initialize!.InputDocumentOrigins;
        Assert.NotNull(origins);
        var origin = Assert.Single(origins!["consult_draft"]);
        Assert.Equal(ConsultInputOriginKinds.Rerun, origin.Kind);
        Assert.Equal("source-job-1", origin.SourceJobId);
        // The digest is over the effective value verbatim — equal to the
        // source's slot value by construction.
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(Referral), origin.TextSha256);
        Assert.Null(origin.SourceResultId);
        Assert.Null(origin.FileSha256);
    }

    [Fact]
    public async Task AnOrdinaryStart_StampsNoRerunOrigin()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));
        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Any<object?>(),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);

        Assert.Null(initialize!.InputDocumentOrigins);
    }

    [Fact]
    public async Task ARequestRebuiltFromTheHeldPayload_ReproducesTheEffectiveInputHash()
    {
        // The pin the whole feature rests on: resubmitting the blob's
        // Supplied half (typed wire JSON → FromJson) under the same package
        // yields the same effectiveInputHash the source recorded.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));
        _accounts.GetAccountKindAsync("user-1", Arg.Any<CancellationToken>()).Returns("organisation");
        JobInputsPayload? written = null;
        _inputsBlobs.WriteAsync(Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Do<JobInputsPayload>(p => written = p), Arg.Any<CancellationToken>())
            .Returns(new ConsultInputsBlobPointer("org-job-inputs", "user-1/x.json"));
        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var sourceOutcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
        Assert.Null(sourceOutcome.Error);
        var sourceHash = orchestrationInput!.EffectiveInputHash;
        Assert.NotNull(sourceHash);
        Assert.NotNull(written);

        // The rerun's rebuild: no draft, no files, no refs — only Supplied.
        var rebuilt = new ConsultGenerationRequest(
            ConsultDraft: null,
            Inputs: written!.Supplied!.ToDictionary(
                pair => pair.Key,
                pair => ConsultInputValue.FromJson(pair.Value),
                StringComparer.Ordinal));
        orchestrationInput = null;

        var rerunOutcome = await CreateStarter().StartAsync(_client, rebuilt, "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App, RerunOfJobId: "source-job-1"), CancellationToken.None);

        Assert.Null(rerunOutcome.Error);
        Assert.Equal(sourceHash, orchestrationInput!.EffectiveInputHash);
    }

    [Fact]
    public async Task ASignedPackage_SnapshotsTheChosenSignature_AndAPlainOneDoesNot()
    {
        // v11 #516: the chosen block rides the orchestration input as of the
        // start — through both doors, since both come through this starter.
        // Only a package that marks a deliverable signed pays the table read;
        // a signed package on an account with no chosen block starts with a
        // null snapshot (the engine records the deliverable unsigned).
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Consultation note", null, null, true) }));
        _settings.GetAsync("user-1", AccountSettingKeys.ProfileSignatures, Arg.Any<CancellationToken>())
            .Returns(new AccountSetting(
                AccountSettingKeys.ProfileSignatures,
                """{"Blocks":[{"Id":"clinic-letters","Name":"Clinic letters","Text":"Taylor Reyes, MD","UpdatedAtUtc":"2026-08-30T12:00:00+00:00"}],"ChosenId":"clinic-letters"}""",
                "application/json",
                DateTimeOffset.UtcNow));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.Equal(new ConsultSignatureSnapshot("clinic-letters", "Taylor Reyes, MD", "2026-08-30"), orchestrationInput!.Signature);
        Assert.True(Assert.Single(orchestrationInput.Results!).Signature);

        // Signed package, no chosen block: starts, with a null snapshot.
        orchestrationInput = null;
        _settings.GetAsync("user-1", AccountSettingKeys.ProfileSignatures, Arg.Any<CancellationToken>())
            .Returns((AccountSetting?)null);
        var unsignedOutcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
        Assert.Null(unsignedOutcome.Error);
        Assert.Null(orchestrationInput!.Signature);
        Assert.True(Assert.Single(orchestrationInput.Results!).Signature);

        // A package that marks nothing signed pays no table read.
        orchestrationInput = null;
        _settings.ClearReceivedCalls();
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Consultation note") }));
        await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
        Assert.Null(orchestrationInput!.Signature);
        await _settings.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMacroPackage_SnapshotsTemplatesAndTheProfileName_AndAPlainOneDoesNot()
    {
        // v11 #513: the templates and the display name ride the orchestration
        // input — what was promised when the job was submitted, the
        // EmailRequested principle — and only a macro package pays the lookup.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        var (manifest, files) = V11Fixtures.WithMacro("By {{profile:name}}.");
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(
                manifest,
                Nodes: manifest.Nodes,
                SchemaContracts: TestOutputContracts.CatalogSchemas,
                Data: data,
                ResultNodeId: "assemble-note",
                SourceFiles: files,
                Results: new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Consultation note", null, new[] { "disclaimer" }) }));
        _accounts.GetDisplayNameAsync("user-1", Arg.Any<CancellationToken>()).Returns("Taylor Reyes");

        // The v11 fixture descends from the structured v9 one: supply its map.
        var supplied = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = Referral,
            ["seen_on"] = "2026-08-10",
            ["encounter_kind"] = "follow_up",
            ["prior_notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("First.") })
        };

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(null, Inputs: supplied), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.Equal("By {{profile:name}}.", orchestrationInput!.MacroTexts!["disclaimer"]);
        Assert.Equal("Taylor Reyes", orchestrationInput.ProfileName);
        Assert.Equal(new[] { "disclaimer" }, Assert.Single(orchestrationInput.Results!).Macros);

        // A package with no macros pays no table read and writes the nulls of before.
        orchestrationInput = null;
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));
        await CreateStarter().StartAsync(_client, new ConsultGenerationRequest(Referral), "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App), CancellationToken.None);
        Assert.Null(orchestrationInput!.MacroTexts);
        Assert.Null(orchestrationInput.ProfileName);
        await _accounts.Received(1).GetDisplayNameAsync("user-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SpecVersion8Package_StampsHashVersion4()
    {
        // A text-only v8 package hashes to the same bytes a v7 one would,
        // because a text value serialises identically either way. What version
        // 4 buys shows up once a value is NOT text — pinned separately in
        // TypedAndStringForms_HashDifferently.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(orchestrationInput);
        Assert.Equal(4, orchestrationInput.EffectiveInputHashVersion);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeTypedInputsHash(
                new Dictionary<string, ConsultInputValue> { ["consult_draft"] = Referral }),
            orchestrationInput.EffectiveInputHash);
    }

    [Fact]
    public async Task SpecVersion9Package_StampsHashVersion5()
    {
        // #422: the gate. Before the `>= 9` arm existed, a v9 job took the
        // `>= 8` arm — hashed by definition 4 and stamped 4, with nothing
        // erroring. The store is mocked, so a specVersion-9 manifest reaches
        // the starter here even though acceptance is #424's; scalar inputs,
        // because no declaration can ask for structure yet.
        var supplied = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = Referral,
            ["seen_on"] = "2026-08-10",
            ["encounter_kind"] = "follow_up",
            ["billable"] = ConsultInputValue.OfBoolean(true)
        };

        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Typed() with { SpecVersion = 9, Tags = new List<string>() },
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: supplied),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(orchestrationInput);
        Assert.NotNull(initialize);
        Assert.Equal(5, orchestrationInput.EffectiveInputHashVersion);
        Assert.Equal(5, initialize.EffectiveInputHashVersion);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeStructuredInputsHash(supplied),
            orchestrationInput.EffectiveInputHash);
        Assert.Equal(orchestrationInput.EffectiveInputHash, initialize.EffectiveInputHash);

        // Definition 4 agrees with 5 on an ASCII map of scalars, so the hash
        // alone cannot tell the gate moved — the stamped version above can,
        // and ProvenanceHashTests pins where the two functions part.
        Assert.Equal(
            ConsultGenerationProvenance.ComputeTypedInputsHash(supplied),
            orchestrationInput.EffectiveInputHash);
    }

    [Fact]
    public async Task SpecVersion9Package_WithStructure_StartsAndCarriesTheDeclaration()
    {
        // #424 end to end: the first layer at which structure passes the
        // starter. The typed map reaches the orchestration payload as it is,
        // the resolver map carries the carriers, InputTypes names the new
        // types for the renderer, and the hash is definition 5 of the structure.
        var supplied = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = Referral,
            ["seen_on"] = "2026-08-10",
            ["encounter_kind"] = "follow_up",
            ["length_of_stay"] = ConsultInputValue.OfNumber("3"),
            ["prior_notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("First."), ConsultInputValue.OfText("Second.") }),
            ["patient"] = ConsultInputValue.OfObject(new[] { new ConsultInputEntry("family_name", ConsultInputValue.OfText("Smith")), new ConsultInputEntry("age", ConsultInputValue.OfNumber("40")) })
        };

        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V9Fixtures.Structured(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: supplied),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(orchestrationInput);
        Assert.Equal(5, orchestrationInput.EffectiveInputHashVersion);
        Assert.Equal(ConsultGenerationProvenance.ComputeStructuredInputsHash(supplied), orchestrationInput.EffectiveInputHash);
        Assert.Equal(supplied["prior_notes"], orchestrationInput.Request.Inputs!["prior_notes"]);
        Assert.Equal("""["First.","Second."]""", orchestrationInput.Inputs!["prior_notes"]);
        Assert.Equal("3", orchestrationInput.Inputs["length_of_stay"]);
        Assert.Equal(WorkflowInputTypes.Array, orchestrationInput.InputTypes!["prior_notes"]);
        Assert.Equal(WorkflowInputTypes.Object, orchestrationInput.InputTypes["patient"]);
        Assert.Equal(WorkflowInputTypes.Number, orchestrationInput.InputTypes["length_of_stay"]);
    }

    // #426 (v9 layer 6): a fan over a caller-supplied array. The job snapshots
    // the fan's items from the request under the literal forEach key, expands
    // one block per element, and stamps TotalBlockCount once at start.

    // #434: the one refusal that signals the entity.
    private ConsultGenerationJobStartFailure? _recordedStartFailure;

    private async Task<(ConsultGenerationJobStartOutcome Outcome, ConsultGenerationJobInitialize? Initialize, ConsultGenerationOrchestrationInput? Input)>
        StartFannedAsync(WorkflowPackageManifest manifest, ConsultInputValue? priorNotes, string fannedId = "prior_notes",
            IReadOnlyList<WorkflowResolvedResult>? results = null, string? apiHost = null)
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                manifest,
                results ?? new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Consultation note") }));

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.RecordStartFailure),
            Arg.Do<object>(payload => _recordedStartFailure = payload as ConsultGenerationJobStartFailure));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var supplied = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = Referral,
            ["seen_on"] = "2026-08-10",
            ["encounter_kind"] = "follow_up"
        };
        if (priorNotes is not null)
        {
            supplied[fannedId] = priorNotes;
        }

        var outcome = await CreateStarter(apiHost: apiHost).StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: supplied),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        return (outcome, initialize, orchestrationInput);
    }

    [Fact]
    public async Task AFanOverACallersArray_StartsWithOneBlockPerElement()
    {
        var (outcome, initialize, input) = await StartFannedAsync(V9Fixtures.Fanned(), ConsultInputValue.OfArray(new[]
        {
            ConsultInputValue.OfText("Seen in clinic; BP 150/95."),
            ConsultInputValue.OfText("Follow-up; BP 130/85.")
        }));

        Assert.Null(outcome.Error);
        Assert.NotNull(initialize);
        Assert.NotNull(input);

        // The deliverable's blocks: the standards fan's two, then the notes fan's
        // two — ids the engine minted, names that never carry the note.
        Assert.Equal(
            new[] { "consult:section-instructions:hpi", "consult:section-instructions:pmh", "consult:summarise-note:0", "consult:summarise-note:1" },
            initialize.Items.Select(item => item["id"]).ToArray());
        Assert.Equal("Prior notes 2", initialize.Items[3]["name"]);
        Assert.All(initialize.Items, item => Assert.DoesNotContain("BP", item["name"], StringComparison.Ordinal));

        // The snapshot the orchestrator fans over, under the literal key.
        var fan = input.Collections!["input:prior_notes"];
        Assert.Equal(new[] { "0", "1" }, fan.Select(item => item["id"]));
        Assert.Equal("Follow-up; BP 130/85.", fan[1]["value"]);
        Assert.Contains("standards", input.Collections.Keys);

        // The slim roster the rail reads: ids and names, never values.
        var roster = initialize.Collections!.Single(r => r.CollectionId == "input:prior_notes");
        Assert.Equal(new[] { "Prior notes 1", "Prior notes 2" }, roster.Items.Select(item => item.Name));

        Assert.Equal(5, input.EffectiveInputHashVersion);
    }

    [Fact]
    public async Task AnEmptyFan_IsRefusedAtStart_NamingTheInput_AndLeavesARow()
    {
        // No items, no blocks, no document: v8's empty fire set in different
        // clothes, refused the same way and through the same enum value —
        // and, since #434, recorded the same way too.
        var (outcome, initialize, _) = await StartFannedAsync(V9Fixtures.Fanned(),
            ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>()));

        // A required array with no entries is refused as the wrong shape first
        // (#424); the fan's own refusal needs the optional declaration.
        Assert.NotNull(outcome.Error);
        Assert.Null(outcome.JobId);
        Assert.Null(initialize);
        Assert.Null(_recordedStartFailure);

        var manifest = V9Fixtures.Fanned();
        manifest = manifest with
        {
            Inputs = manifest.Inputs!.Select(i => i.Id == "prior_notes" ? i with { Required = false } : i).ToList()
        };
        var (absent, absentInit, absentRun) = await StartFannedAsync(manifest, priorNotes: null, apiHost: "east.ca.api.consultologist.ai");

        Assert.Equal(ConsultGenerationJobStartError.NoApplicableDeliverable, absent.Error);
        Assert.Equal(
            "No document applies to these inputs. 'Prior notes' has no entries, and every document this package produces is written from them.",
            absent.ErrorDetail);
        Assert.Equal(absent.ErrorDetail, absent.SenderSafeDetail);

        // The row: born Failed, nothing scheduled, every deliverable listed as
        // not produced for the fan's reason.
        Assert.NotNull(absent.JobId);
        Assert.Null(absentInit);
        Assert.Null(absentRun);
        var recorded = Assert.IsType<ConsultGenerationJobStartFailure>(_recordedStartFailure);
        Assert.Equal(absent.ErrorDetail, recorded.Reason);
        Assert.Equal(absent.JobId, recorded.Initialize.JobId);
        Assert.Empty(recorded.Initialize.Items);
        var notProduced = Assert.Single(recorded.Initialize.SkippedDocuments!);
        Assert.Equal(("consult", "Consultation note"), (notProduced.ResultId, notProduced.Label));
        // #514: a job born Failed says where and by what, as a run does.
        Assert.Equal("east.ca.api.consultologist.ai", recorded.Initialize.ApiHost);
        Assert.Equal(EngineAttestation.Current(TestCatalog.Instance).Commit, recorded.Initialize.EngineCommit);
        Assert.Equal("is written from 'Prior notes', which has no entries", notProduced.Reason);
        Assert.Equal(9, recorded.Initialize.PackageSpecVersion);
        Assert.Equal(ConsultGenerationProvenance.StructuredInputsHashVersion, recorded.Initialize.EffectiveInputHashVersion);
    }

    [Fact]
    public async Task AnEmptyFan_ListsEveryDeclaredDeliverable_EachWithItsOwnReason()
    {
        // Found in verification of #430 run 3: the fire set narrows the
        // package's results before the fan is checked, and the record listed
        // only the firing ones. Every declared deliverable belongs on it, in
        // declaration order — the condition-skipped with the condition's
        // reason, the rest with the fan's.
        var manifest = V9Fixtures.Fanned();
        manifest = manifest with
        {
            Inputs = manifest.Inputs!.Select(i => i.Id == "prior_notes" ? i with { Required = false } : i).ToList()
        };
        var results = new List<WorkflowResolvedResult>
        {
            new("consult", "assemble-note", "Consultation note"),
            new("long_stay", "assemble-note", "Long-stay summary", new WorkflowResultCondition("length_of_stay", "7", false, Ordering: ">")),
            new("digest", "assemble-note", "Prior-notes digest")
        };

        var (outcome, _, _) = await StartFannedAsync(manifest, priorNotes: null, results: results);

        Assert.Equal(ConsultGenerationJobStartError.NoApplicableDeliverable, outcome.Error);
        var recorded = Assert.IsType<ConsultGenerationJobStartFailure>(_recordedStartFailure);
        Assert.Equal(
            new[]
            {
                ("consult", "is written from 'Prior notes', which has no entries"),
                ("long_stay", "needs length_of_stay to be > 7; it is not supplied"),
                ("digest", "is written from 'Prior notes', which has no entries")
            },
            recorded.Initialize.SkippedDocuments!.Select(d => (d.ResultId, d.Reason)));
    }

    [Fact]
    public async Task AFanOverAnArrayOfObjects_CarriesEachElementAsItsCarrier()
    {
        var manifest = V9Fixtures.Fanned(forEach: "input:medications");
        var element = ConsultInputValue.OfObject(new[]
        {
            new ConsultInputEntry("name", ConsultInputValue.OfText("metformin")),
            new ConsultInputEntry("dose", ConsultInputValue.OfText("500 mg"))
        });

        var (outcome, _, input) = await StartFannedAsync(manifest, ConsultInputValue.OfArray(new[] { element }), fannedId: "medications");

        Assert.Null(outcome.Error);
        var fan = input!.Collections!["input:medications"];
        Assert.Equal(element, ConsultInputValue.FromJson(fan[0]["value"]));
        Assert.Equal("Medications 1", fan[0]["name"]);
    }

    [Fact]
    public async Task SpecVersion8Package_WithABoolean_HashesTheTypedForm()
    {
        // The text-only case above cannot tell the two hash functions apart —
        // a text value serialises identically either way. A boolean is what
        // makes version 4 observable end to end.
        var supplied = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = Referral,
            ["seen_on"] = "2026-08-10",
            ["encounter_kind"] = "follow_up",
            ["billable"] = ConsultInputValue.OfBoolean(true)
        };

        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V8Fixtures.Typed(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: supplied),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(orchestrationInput);
        Assert.Equal(4, orchestrationInput.EffectiveInputHashVersion);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeTypedInputsHash(supplied),
            orchestrationInput.EffectiveInputHash);

        // And it is NOT what v7's function would have produced from the same
        // values flattened to strings — the whole reason the version moved.
        Assert.NotEqual(
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(
                supplied.ToDictionary(pair => pair.Key, pair => pair.Value.Canonical, StringComparer.Ordinal)),
            orchestrationInput.EffectiveInputHash);

        // The declared types reach the orchestrator so the renderer can type
        // the variables; text slots are omitted, so a v7 job carries nothing.
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["seen_on"] = WorkflowInputTypes.Date,
                ["encounter_kind"] = WorkflowInputTypes.Enum,
                ["billable"] = WorkflowInputTypes.Boolean
            },
            orchestrationInput.InputTypes);
    }

    // #315: the fire set is decided at start, and the package is filtered
    // before blocks and descriptors are built. The engine never learns what a
    // condition is.

    private static WorkflowPackage ConditionalPackage(bool theNoteAlsoTakesTheGuidelines = false)
    {
        var manifest = V8Fixtures.Conditional();

        if (theNoteAlsoTakesTheGuidelines)
        {
            // MultiCollection's original shape, which MultiDeliverable narrowed:
            // the note aggregates BOTH chains. That makes the letter's chain
            // shared rather than private, so skipping the letter may drop its
            // aggregator and nothing else.
            var shared = new List<WorkflowNodeSpec>(manifest.Nodes!);
            var noteIndex = shared.FindIndex(node => node.Id == "assemble-note");
            shared[noteIndex] = shared[noteIndex] with
            {
                Aggregate = new List<string> { "node:section-instructions", "node:contextualize" }
            };
            manifest = manifest with { Nodes = shared };
        }
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        return new WorkflowPackage(
            manifest,
            Nodes: manifest.Nodes,
            SchemaContracts: TestOutputContracts.CatalogSchemas,
            Data: data,
            ResultNodeId: null,
            Results: new List<WorkflowResolvedResult>
            {
                new("consult_note", "assemble-note", "Consultation note"),
                new("patient_letter", "assemble-letter", "Patient letter",
                    new WorkflowResultCondition("encounter_kind", "follow_up", false))
            });
    }

    private async Task<(ConsultGenerationJobStartOutcome Outcome,
                        ConsultGenerationJobInitialize? Initialize,
                        ConsultGenerationOrchestrationInput? Input)> StartConditionalAsync(
        string encounterKind,
        bool theNoteAlsoTakesTheGuidelines = false)
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ConditionalPackage(theNoteAlsoTakesTheGuidelines));

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Referral,
                ["seen_on"] = "2026-08-10",
                ["encounter_kind"] = encounterKind
            }),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        return (outcome, initialize, orchestrationInput);
    }

    [Fact]
    public async Task ANonFiringDeliverable_ContributesNoBlocks()
    {
        // The assertion that pins #176 surviving: TotalBlockCount is stamped
        // once from this list, so filtering the package before expansion is
        // what keeps it a stored scalar rather than something recomputed.
        var (firing, firingInit, _) = await StartConditionalAsync("follow_up");
        var (skipping, skippingInit, _) = await StartConditionalAsync("new_patient");

        Assert.Null(firing.Error);
        Assert.Null(skipping.Error);

        Assert.Contains(firingInit!.Items, item => item["id"].StartsWith("patient_letter:", StringComparison.Ordinal));
        Assert.DoesNotContain(skippingInit!.Items, item => item["id"].StartsWith("patient_letter:", StringComparison.Ordinal));
        Assert.True(skippingInit.Items.Count < firingInit.Items.Count);

        // The note's blocks are untouched either way.
        Assert.Contains(skippingInit.Items, item => item["id"].StartsWith("consult_note:", StringComparison.Ordinal));
    }

    // #355: the fire set decides which NODES run, not only which deliverables
    // assemble. There is no orchestrator harness in this repo, so "did not run"
    // is asserted as "was never handed to the engine" — sound because the
    // engine's scheduling loop iterates exactly the list shipped here.

    private static string[] NodeIds(IReadOnlyList<ConsultNodeDescriptor>? nodes) =>
        nodes!.Select(node => node.Id).ToArray();

    [Fact]
    public async Task ASkippedDeliverablesNodes_ReachNeitherPayload()
    {
        // Both payloads, because the entity stamps State.Nodes first-writer-wins
        // and the orchestration input is what replays: a prune that reached one
        // and not the other would leave the two disagreeing about the same job.
        var (_, initialize, orchestrationInput) = await StartConditionalAsync("new_patient");

        var dead = new[] { "assemble-letter", "contextualize", "agg-guidelines", "summarize-guideline" };

        foreach (var payload in new[] { NodeIds(initialize!.Nodes), NodeIds(orchestrationInput!.Nodes) })
        {
            Assert.All(dead, id => Assert.DoesNotContain(id, payload));
        }

        Assert.Equal(NodeIds(initialize.Nodes), NodeIds(orchestrationInput.Nodes));
    }

    [Fact]
    public async Task TheFiringDeliverablesNodes_AreShippedAndTransitivelyClosed()
    {
        // Under-inclusion is the fatal direction: the engine indexes nodesById
        // without a guard and ExpandAggregator throws on an unknown node. So the
        // closure is asserted as a property over both edge kinds rather than as
        // a list that would still pass if the walk stopped one hop early.
        var (_, _, orchestrationInput) = await StartConditionalAsync("new_patient");
        var shipped = orchestrationInput!.Nodes!;
        var ids = shipped.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(8, shipped.Count);
        Assert.Contains("assemble-note", ids);

        foreach (var node in shipped)
        {
            var references = (node.Bindings ?? new Dictionary<string, ConsultNodeBindingDescriptor>())
                .Values.Select(binding => binding.From)
                .Concat(node.Aggregate ?? new List<string>())
                .Where(from => from.StartsWith("node:", StringComparison.Ordinal))
                .Select(from => from["node:".Length..]);

            Assert.All(references, reference => Assert.Contains(reference, ids));
        }
    }

    [Fact]
    public async Task TheCollectionsAndItemStepsNarrowWithTheNodes()
    {
        // These are derived from the same package, so they narrow together — the
        // SSE synthesizer indexes ItemSteps while the engine counts its own
        // chain nodes, and the two would disagree if only one side were pruned.
        var (_, _, orchestrationInput) = await StartConditionalAsync("new_patient");

        Assert.Equal(new[] { "standards" }, orchestrationInput!.Collections!.Keys.ToArray());
        Assert.Equal(
            new[] { "standard-section-draft", "patient-section-draft", "section-instructions" },
            orchestrationInput.ItemSteps!.Select(step => step.Id).ToArray());
    }

    [Fact]
    public async Task TheJobRecordsItsOwnFanRoster()
    {
        // #361: the rail draws a fan's rows from the job, so the job has to
        // carry them. Slim on purpose — the orchestrator's copy of a collection
        // carries every field including content, which is the whole standards
        // text, and none of that belongs on a status response.
        var (_, initialize, _) = await StartConditionalAsync("new_patient");

        var roster = Assert.Single(initialize!.Collections!);

        Assert.Equal("standards", roster.CollectionId);
        Assert.Equal(new[] { "hpi", "pmh" }, roster.Items.Select(item => item.Id).ToArray());

        // Names, not just ids: they are what the rail's item rows read, and the
        // reason the roster cannot be recovered from the block list alone.
        Assert.All(roster.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Name)));
    }

    [Fact]
    public async Task WhenEveryDeliverableFires_EveryNodeIsShipped()
    {
        var (_, initialize, orchestrationInput) = await StartConditionalAsync("follow_up");

        Assert.Equal(12, orchestrationInput!.Nodes!.Count);
        Assert.Equal(12, initialize!.Nodes!.Count);
        Assert.Contains("assemble-letter", NodeIds(orchestrationInput.Nodes));
    }

    [Fact]
    public async Task ANodeFeedingBothDeliverables_SurvivesTheSkip()
    {
        // The over-pruning guard, and a sharp one: a skip HAS happened, so the
        // closure genuinely runs — and only the dead aggregator itself may go,
        // because the note reaches everything else the letter did.
        var (_, _, orchestrationInput) = await StartConditionalAsync(
            "new_patient", theNoteAlsoTakesTheGuidelines: true);
        var ids = NodeIds(orchestrationInput!.Nodes);

        Assert.DoesNotContain("assemble-letter", ids);
        Assert.Contains("contextualize", ids);
        Assert.Contains("agg-guidelines", ids);
        Assert.Contains("summarize-guideline", ids);
        Assert.Equal(11, ids.Length);

        // Both collections are still shipped, because the note now fans both.
        Assert.Equal(
            new[] { "guidelines", "standards" },
            orchestrationInput.Collections!.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task AnUnconditionalPackage_ShipsEveryDeclaredNode()
    {
        // The gate. A v7 package cannot declare a condition, so nothing can be
        // skipped and the prune never runs — the whole pre-v8 world in one
        // assertion.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V7Fixtures.MultiDeliverable(),
                new List<WorkflowResolvedResult>
                {
                    new("consult_note", "assemble-note", "Consultation note"),
                    new("patient_letter", "assemble-letter", "Patient letter")
                }));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Referral
            }),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(
            V7Fixtures.MultiDeliverable().Nodes!.Select(node => node.Id).ToArray(),
            NodeIds(orchestrationInput!.Nodes));
    }

    [Fact]
    public async Task ASkippedDeliverable_IsRecordedWithItsReason()
    {
        var (_, initialize, orchestrationInput) = await StartConditionalAsync("new_patient");

        var skipped = Assert.Single(initialize!.SkippedDocuments!);
        Assert.Equal("patient_letter", skipped.ResultId);
        Assert.Equal("Patient letter", skipped.Label);
        Assert.Contains("encounter_kind", skipped.Reason);
        Assert.Contains("'new_patient'", skipped.Reason);

        // And it reaches the orchestration input, so the completion reply can
        // say it too.
        Assert.Single(orchestrationInput!.SkippedDocuments!);
    }

    [Fact]
    public async Task AFiringJob_RecordsNoSkips()
    {
        var (_, initialize, _) = await StartConditionalAsync("follow_up");

        Assert.Null(initialize!.SkippedDocuments);
    }

    [Fact]
    public async Task WhenNoDeliverableApplies_TheJobIsRefusedAtStart()
    {
        // Knowable before any model call, so nothing is created and nothing is
        // spent. The message names each deliverable and what it wanted.
        var manifest = V8Fixtures.Conditional();
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);

        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(
                manifest,
                Nodes: manifest.Nodes,
                SchemaContracts: TestOutputContracts.CatalogSchemas,
                Data: data,
                Results: new List<WorkflowResolvedResult>
                {
                    // Both read the optional boolean, which is not supplied:
                    // absence satisfies nothing, so the fire set is empty. Using
                    // an undeclared enum value instead would be refused earlier,
                    // by the canonical-form check, and prove nothing about this.
                    new("consult_note", "assemble-note", "Consultation note",
                        new WorkflowResultCondition("billable", null, false)),
                    new("patient_letter", "assemble-letter", "Patient letter",
                        new WorkflowResultCondition("billable", "true", false))
                }));

        ConsultGenerationJobStartFailure? recorded = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.RecordStartFailure),
            Arg.Do<object>(payload => recorded = payload as ConsultGenerationJobStartFailure));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Referral,
                ["seen_on"] = "2026-08-10",
                ["encounter_kind"] = "follow_up"
            }),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.Email, "sender@example.org"),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.NoApplicableDeliverable, outcome.Error);
        Assert.Contains("Consultation note", outcome.ErrorDetail);
        Assert.Contains("Patient letter", outcome.ErrorDetail);
        Assert.Contains("not supplied", outcome.ErrorDetail);

        // #369: and it may be quoted back to whoever sent it. Labels and
        // condition literals are authored package content; the supplied-value
        // branch of Explain can only ever print a declared enum value or a
        // boolean, because anything else was refused before conditions ran.
        Assert.Equal(outcome.ErrorDetail, outcome.SenderSafeDetail);

        // #434: refused AND recorded. The outcome carries the job id beside
        // the error; the entity was told once, with the provenance a run would
        // have had and the deliverables that did not apply; nothing was
        // scheduled and Initialize was never signalled.
        Assert.NotNull(outcome.JobId);
        Assert.NotNull(recorded);
        Assert.Equal(outcome.JobId, recorded!.Initialize.JobId);
        Assert.Equal(outcome.ErrorDetail, recorded.Reason);
        Assert.Empty(recorded.Initialize.Items);
        Assert.Equal($"{manifest.Name}@{manifest.Version}", recorded.Initialize.WorkflowPackage);
        Assert.Equal(ConsultGenerationProvenance.TypedInputsHashVersion, recorded.Initialize.EffectiveInputHashVersion);
        Assert.NotNull(recorded.Initialize.EffectiveInputHash);
        Assert.Equal(8, recorded.Initialize.PackageSpecVersion);
        Assert.Equal(ConsultGenerationJobSources.Email, recorded.Initialize.Source);
        Assert.Equal("user-1", recorded.Initialize.AppUserId);
        Assert.Equal(
            new[] { ("consult_note", "Consultation note"), ("patient_letter", "Patient letter") },
            recorded.Initialize.SkippedDocuments!.Select(s => (s.ResultId, s.Label)));
        Assert.All(recorded.Initialize.SkippedDocuments!, s => Assert.Contains("not supplied", s.Reason));
        Assert.Null(recorded.Initialize.Nodes);
        Assert.Null(recorded.Initialize.ItemSteps);
        // #453: a v8 package has no tags section; the record says null, as a
        // started job's would.
        Assert.Null(recorded.Initialize.PackageTags);
        await _entities.DidNotReceive().SignalEntityAsync(
            Arg.Any<EntityInstanceId>(), nameof(ConsultGenerationJobEntity.Initialize), Arg.Any<object>());
        await _client.DidNotReceive().ScheduleNewOrchestrationInstanceAsync(
            Arg.Any<TaskName>(), Arg.Any<object?>(), Arg.Any<StartOrchestrationOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOtherRefusals_StillLeaveNoRow()
    {
        // #434 draws a line: a row exists when a well-formed, authorized
        // request met a package that produced nothing. A request problem —
        // here a missing required input — creates nothing, as before.
        var manifest = V8Fixtures.Conditional();
        var files = V6Fixtures.Files(manifest);
        var data = WorkflowDataResolver.Resolve(manifest, files, new List<string>());
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(manifest, Nodes: manifest.Nodes, SchemaContracts: TestOutputContracts.CatalogSchemas, Data: data,
                Results: new List<WorkflowResolvedResult> { new("consult_note", "assemble-note", "Consultation note") }));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Referral
            }),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Null(outcome.JobId);
        await _entities.DidNotReceiveWithAnyArgs().SignalEntityAsync(default, default!, default);
    }

    [Fact]
    public async Task WhenNoDeliverableApplies_TheRefusalNeverPrintsThePatientsValue()
    {
        // #427: a v9 condition may read a number, a date or a field of an
        // object — the patient's. The refusal says what was needed and that
        // it was not met, and is still quotable back to the sender.
        var manifest = V9Fixtures.Conditional("patient.age >= 65");
        var files = V6Fixtures.Files(manifest);
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);

        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(
                manifest,
                Nodes: manifest.Nodes,
                SchemaContracts: TestOutputContracts.CatalogSchemas,
                Data: data,
                Results: new List<WorkflowResolvedResult>
                {
                    new("consult_note", "assemble-note", "Consultation note",
                        new WorkflowResultCondition("patient", "65", false, Field: "age", Ordering: ">=")),
                    new("patient_letter", "assemble-letter", "Patient letter",
                        new WorkflowResultCondition("length_of_stay", "7", false, Ordering: ">"))
                }));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Referral,
                ["seen_on"] = "2026-08-10",
                ["encounter_kind"] = "follow_up",
                ["length_of_stay"] = ConsultInputValue.OfNumber("3"),
                ["patient"] = ConsultInputValue.OfObject(new[]
                {
                    new ConsultInputEntry("family_name", "Smith"),
                    new ConsultInputEntry("age", ConsultInputValue.OfNumber("41"))
                })
            }),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.NoApplicableDeliverable, outcome.Error);
        Assert.Contains("'Consultation note' needs patient.age to be >= 65; it is not.", outcome.ErrorDetail);
        Assert.Contains("'Patient letter' needs length_of_stay to be > 7; it is not.", outcome.ErrorDetail);
        Assert.DoesNotContain("41", outcome.ErrorDetail);
        Assert.DoesNotContain("Smith", outcome.ErrorDetail);
        Assert.DoesNotContain(" 3", outcome.ErrorDetail);
        Assert.Equal(outcome.ErrorDetail, outcome.SenderSafeDetail);
    }

    /// <summary>
    /// #369: the pinned package for the sender-safety tests — the typed v8
    /// declaration, whose consult_draft, seen_on and encounter_kind are all
    /// required and whose seen_on is a date.
    /// </summary>
    private void WithTypedPackage(string? title = null, List<string>? tags = null)
    {
        // #432: a title rides on a v9 manifest; the typed v8 shape otherwise.
        // #453: tags likewise — a stated empty set unless the test says.
        var manifest = title is null
            ? V8Fixtures.Typed()
            : V8Fixtures.Typed() with { SpecVersion = 9, Title = title, Tags = tags ?? new List<string>() };
        var errors = new List<string>();

        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(
                manifest,
                Nodes: manifest.Nodes,
                SchemaContracts: TestOutputContracts.CatalogSchemas,
                Data: WorkflowDataResolver.Resolve(manifest, V6Fixtures.Files(manifest), errors),
                Results: new List<WorkflowResolvedResult> { new("consult_note", "assemble-note", "Consultation note") }));
    }

    private Task<ConsultGenerationJobStartOutcome> StartWithAsync(
        params (string Id, ConsultInputValue Value)[] inputs)
        => CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: inputs.ToDictionary(
                input => input.Id, input => input.Value, StringComparer.Ordinal)),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

    [Fact]
    public async Task AMissingRequiredInput_MayBeToldToTheSender()
    {
        // The commonest emailed refusal there is, and the one that cost two
        // round trips to diagnose: the id is read off the MANIFEST — it is
        // missing from the request — so nothing the caller wrote appears in it.
        WithTypedPackage();

        var outcome = await StartWithAsync(("consult_draft", Referral));

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Contains("'seen_on'", outcome.SenderSafeDetail);
        Assert.Equal(outcome.ErrorDetail, outcome.SenderSafeDetail);
    }

    [Fact]
    public async Task AMalformedValue_IsNeverToldToTheSender()
    {
        // Same error code as the test above, opposite answer — which is the
        // whole reason this cannot be an allowlist over ConsultGenerationJobStartError.
        // The complaint ends in got '<the supplied value>', and a date slot's
        // rejected value is a date of service.
        WithTypedPackage();

        var outcome = await StartWithAsync(
            ("consult_draft", Referral),
            ("seen_on", "1965-03-02x"),
            ("encounter_kind", "follow_up"));

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Null(outcome.SenderSafeDetail);
        // The web door still gets it: this is a withholding, not a redaction.
        Assert.Contains("1965-03-02x", outcome.ErrorDetail);
    }

    [Fact]
    public async Task AnUndeclaredInputId_IsNeverToldToTheSender()
    {
        // An input id is an attachment's filename stem on the email door, and a
        // filename can itself be PHI. Email cannot reach this branch — it only
        // assigns slots it matched against declared ids — but the guarantee is
        // about the sentence, not about the door that produced it.
        WithTypedPackage();

        var outcome = await StartWithAsync(("Smith_John_referral", Referral));

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Null(outcome.SenderSafeDetail);
        Assert.Contains("Smith_John_referral", outcome.ErrorDetail);
    }

    [Fact]
    public async Task TheStarter_RecordsThePackagesSpecVersionOnTheJob()
    {
        // #373: the number a reader of the provenance row can act on. Captured
        // at start because it cannot be resolved afterwards — a fork lives in
        // the private registry, invisible to the public chain.
        WithTypedPackage();

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));

        await StartWithAsync(
            ("consult_draft", Referral),
            ("seen_on", "2026-08-10"),
            ("encounter_kind", "follow_up"));

        Assert.NotNull(initialize);
        Assert.Equal(8, initialize!.PackageSpecVersion);
    }

    [Theory]
    [InlineData("Breast oncology consults")]
    [InlineData(null)]
    public async Task TheStarter_RecordsThePackagesTitleOnTheJob(string? title)
    {
        // #432: captured at start, like the spec version — History cannot see
        // the manifest, and a later rename must not rewrite what this ran.
        WithTypedPackage(title);

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));

        await StartWithAsync(
            ("consult_draft", Referral),
            ("seen_on", "2026-08-10"),
            ("encounter_kind", "follow_up"));

        Assert.NotNull(initialize);
        Assert.Equal(title, initialize!.PackageTitle);
    }

    [Fact]
    public async Task TheStarter_RecordsThePackagesTagsOnTheJob_AndNullBeforeNine()
    {
        // #453: stamped beside the title, in authored order. A v8 package has
        // no section, and the record says so with null rather than [].
        WithTypedPackage("Breast oncology consults", new List<string> { "oncology", "Breast" });
        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));

        await StartWithAsync(("consult_draft", Referral), ("seen_on", "2026-08-10"), ("encounter_kind", "follow_up"));

        Assert.Equal(new[] { "oncology", "Breast" }, initialize!.PackageTags);

        WithTypedPackage();
        initialize = null;
        await StartWithAsync(("consult_draft", Referral), ("seen_on", "2026-08-10"), ("encounter_kind", "follow_up"));

        Assert.NotNull(initialize);
        Assert.Null(initialize!.PackageTags);
    }

    [Fact]
    public void TypedAndStringForms_HashDifferently()
    {
        // This is the test #313's body asked for, and it is only correct
        // because inputs are typed on the wire: {"billable": true} and
        // {"billable": "true"} are different JSON and hash differently. Under
        // an untyped wire there would be nothing to tell apart, which is why
        // the hash definition had to move (package-format-v8-design.md § 6).
        var flag = ConsultGenerationProvenance.ComputeTypedInputsHash(
            new Dictionary<string, ConsultInputValue> { ["billable"] = ConsultInputValue.OfBoolean(true) });
        var text = ConsultGenerationProvenance.ComputeTypedInputsHash(
            new Dictionary<string, ConsultInputValue> { ["billable"] = "true" });

        Assert.NotEqual(flag, text);

        // And a text value still hashes as v7 computed it, so the two
        // definitions agree wherever they can — they are just never compared.
        Assert.Equal(
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(
                new Dictionary<string, string> { ["consult_draft"] = "Draft." }),
            ConsultGenerationProvenance.ComputeTypedInputsHash(
                new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "Draft." }));
    }

    [Fact]
    public async Task APackageStrandedByTheCatalog_IsNotReportedAsARegistryOutage()
    {
        // #374: the sharp case. A published version is immutable, but the
        // schema-to-catalog match is re-evaluated on every load — so a catalog
        // change can strand a package that was valid when published, with
        // nothing about the package having changed. Reported as
        // RegistryUnavailable it sent an operator to look at storage that was
        // working, for a package that was also fine.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorkflowPackage>>(_ => throw new WorkflowPackageContentException(
                "Workflow package general@v2026.08.1 schema 'concept-list' does not canonically match any contract in "
                    + "output-contracts@v2026.07.3. The package is unchanged and immutable; the catalog moved."));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.PackageContentRejected, outcome.Error);
        Assert.NotEqual(ConsultGenerationJobStartError.RegistryUnavailable, outcome.Error);
        // The sentence has to name the catalog, or it does not say what moved.
        Assert.Contains("output-contracts@v2026.07.3", outcome.ErrorDetail);
        Assert.Contains("the catalog moved", outcome.ErrorDetail);
    }

    [Fact]
    public async Task ARegistryFailure_StillReportsAnOutage()
    {
        // The other half of the split: a real storage failure must not be
        // quietly reclassified as a content disagreement.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorkflowPackage>>(_ => throw new InvalidOperationException("blob container unreachable"));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.RegistryUnavailable, outcome.Error);
    }

    [Fact]
    public async Task APackageTheEngineWillNotRun_IsNotReportedAsARegistryOutage()
    {
        // The package is there and readable; this engine does not run that
        // version. Before #313 this arrived as RegistryUnavailable — "the
        // registry is unavailable" — for a package sitting perfectly readable,
        // logged as an error. SpecVersionNotYetExecutable existed for it and
        // was raised nowhere.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns<Task<WorkflowPackage>>(_ => throw new WorkflowPackageSpecVersionException(
                "general@v2026.08.1", 8, new[] { 5, 6, 7 }));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.SpecVersionNotYetExecutable, outcome.Error);
        Assert.Contains("specVersion 8", outcome.ErrorDetail);
        // A genuine registry failure keeps its own error.
        Assert.NotEqual(ConsultGenerationJobStartError.RegistryUnavailable, outcome.Error);
    }

    [Fact]
    public async Task SpecVersion7Package_StartsWithPrefixedBlocksAndInputMap()
    {
        // The legacy draft field back-fills the consult_draft slot; the
        // snapshot carries prefixed block ids, the result set, the effective
        // input map, and hash version 3.
        var request = new ConsultGenerationRequest(Referral);
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V7Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            request,
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(initialize);
        Assert.Equal(
            new[] { "consult:section-instructions:hpi", "consult:section-instructions:pmh" },
            initialize.Items.Select(item => item["id"]).ToArray());
        Assert.Equal(3, initialize.EffectiveInputHashVersion);
        Assert.NotNull(orchestrationInput);
        Assert.Equal(3, orchestrationInput.EffectiveInputHashVersion);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(
                new Dictionary<string, string> { ["consult_draft"] = Referral }),
            orchestrationInput.EffectiveInputHash);
        Assert.Equal(
            new[] { new ConsultResultDescriptor("consult", "assemble-note", "Assemble note") },
            orchestrationInput.Results!.ToArray());
        Assert.Equal(
            new Dictionary<string, string> { ["consult_draft"] = Referral },
            orchestrationInput.Inputs);
    }

    [Fact]
    public async Task SpecVersion7MultiResultPackage_StartsWithBothDeliverables()
    {
        // ResultNodeId is null by design for a multi-result set — the result
        // set itself is the executability signal.
        var request = new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = Referral });
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V7Fixtures.MultiDeliverable(),
                new List<WorkflowResolvedResult>
                {
                    new("consult_note", "assemble-note", "Consultation note"),
                    new("patient_letter", "assemble-letter", "Patient letter")
                }));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            request,
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(orchestrationInput);
        Assert.Null(orchestrationInput.ResultNodeId);
        Assert.Equal(2, orchestrationInput.Results!.Count);
        // The optional prior_notes input was not supplied: the effective map
        // carries it empty for the resolver; the hash covers the supplied map.
        Assert.Equal(string.Empty, orchestrationInput.Inputs!["prior_notes"]);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(
                new Dictionary<string, string> { ["consult_draft"] = Referral }),
            orchestrationInput.EffectiveInputHash);
    }

    [Fact]
    public async Task SpecVersion7Package_UnknownInput_ReturnsInputsMismatch()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                V7Fixtures.Minimal(),
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue> { ["labs"] = "CBC normal." }),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Contains("'labs'", outcome.ErrorDetail);
        Assert.Contains("declared: consult_draft", outcome.ErrorDetail);
    }

    [Fact]
    public async Task Success_SignalsInitializeAndSchedulesWithSameJobIdAndDraftHash()
    {
        var request = new ConsultGenerationRequest(Referral);
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV5Package());

        ConsultGenerationJobInitialize? initialize = null;
        EntityInstanceId? entityId = null;
        await _entities.SignalEntityAsync(
            Arg.Do<EntityInstanceId>(id => entityId = id),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        StartOrchestrationOptions? options = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Do<StartOrchestrationOptions?>(o => options = o),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            request,
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.JobId);
        Assert.NotNull(initialize);
        Assert.Equal(outcome.JobId, initialize.JobId);
        Assert.Equal(outcome.JobId, options?.InstanceId);
        // EntityInstanceId normalizes entity names to lowercase.
        Assert.Equal(nameof(ConsultGenerationJobEntity), entityId?.Name, ignoreCase: true);
        Assert.Equal("user-1", initialize.AppUserId);
        Assert.NotNull(orchestrationInput);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeDraftOnlyHash(request),
            orchestrationInput.EffectiveInputHash);
        Assert.Equal(initialize.EffectiveInputHash, orchestrationInput.EffectiveInputHash);
    }
    [Fact]
    public async Task AttachedDocument_FillsItsSlotAndHashesLikeTheEquivalentText()
    {
        // The check that proves extraction stayed a pre-step: a slot filled
        // from a document and the same slot typed by hand are the same input,
        // and the record says so by producing the same hash.
        var request = new ConsultGenerationRequest(
            null,
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["consult_draft"] = [new("text/plain", System.Text.Encoding.UTF8.GetBytes(Referral))]
            });

        var captured = await StartV7AndCaptureAsync(request);

        Assert.Null(captured.Outcome.Error);
        Assert.Equal(
            new Dictionary<string, string> { ["consult_draft"] = Referral },
            captured.OrchestrationInput!.Inputs);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeDeclaredInputsHash(
                new Dictionary<string, string> { ["consult_draft"] = Referral }),
            captured.OrchestrationInput.EffectiveInputHash);
    }

    [Fact]
    public async Task AttachedDocument_IsRecordedAsReadFromADocument()
    {
        var request = new ConsultGenerationRequest(
            null,
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["consult_draft"] = [new("text/plain", System.Text.Encoding.UTF8.GetBytes(Referral))]
            });

        var captured = await StartV7AndCaptureAsync(request);

        // #428: per document, positionally — one document, one origin, and
        // the #238-era single-valued slot left null.
        var origin = Assert.Single(Assert.Contains("consult_draft", captured.Initialize!.InputDocumentOrigins));
        Assert.Equal(ConsultInputOriginKinds.Document, origin.Kind);
        Assert.Equal("text/1", origin.Extractor);
        Assert.Null(captured.Initialize.InputOrigins);
    }

    // ----- #510: an input copied from a previous run -----

    private const string SourceJob = "0123456789abcdef0123456789abcdef";

    private static ConsultGenerationJobState SourceRun(string appUserId = "user-1", string status = "Completed", string? text = "The earlier note.", DateTimeOffset? dropped = null) =>
        new()
        {
            JobId = SourceJob,
            AppUserId = appUserId,
            Status = status,
            TextDroppedAtUtc = dropped,
            AssembledDocuments = new List<ConsultGenerationResultDocumentState>
            {
                new() { ResultId = "consult", Label = "Consultation note", Text = text, Ordinal = 0 }
            }
        };

    private void WithSourceRun(ConsultGenerationJobState? state)
    {
        _entities.GetEntityAsync<ConsultGenerationJobState>(Arg.Any<EntityInstanceId>(), Arg.Any<CancellationToken>())
            .Returns(state == null
                ? null
                : new EntityMetadata<ConsultGenerationJobState>(new EntityInstanceId(nameof(ConsultGenerationJobEntity), SourceJob), state));
    }

    private static ConsultGenerationRequest ReferringRequest() => new(
        null,
        InputRefs: new Dictionary<string, List<ConsultInputRef>> { ["consult_draft"] = new() { new ConsultInputRef(SourceJob, "consult") } });

    [Fact]
    public async Task APreviousRunsDeliverable_IsCopiedIn_AndItsOriginRecorded()
    {
        WithSourceRun(SourceRun(text: Referral + "\r\nSecond line.  "));

        var captured = await StartV7AndCaptureAsync(ReferringRequest());

        Assert.Null(captured.Outcome.Error);
        var input = captured.OrchestrationInput!;
        // The copy is the deliverable's text; the reference itself is gone.
        Assert.Equal(Referral + "\nSecond line.", input.Inputs!["consult_draft"]);
        Assert.Null(input.Request.InputRefs);
        var origin = Assert.Single(input.InputDocumentOrigins!["consult_draft"]);
        Assert.Equal(ConsultInputOriginKinds.PreviousRun, origin.Kind);
        Assert.Equal(SourceJob, origin.SourceJobId);
        Assert.Equal("consult", origin.SourceResultId);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(Referral + "\nSecond line."), origin.TextSha256);
        Assert.Null(origin.Extractor);
        Assert.Null(origin.FileSha256);
    }

    [Fact]
    public async Task AMigratedSourceRun_IsHydratedFromItsOutputsBlob()
    {
        // #557: a source completed after the migration carries no entity
        // text — the copy reads it from the outputs blob through the same
        // refusal ladder, so digest and origin see the real text.
        var pointer = new ConsultOutputsBlobPointer("org-job-outputs", "user-1/" + SourceJob + ".json");
        var source = SourceRun(text: null);
        source.OutputsBlob = pointer;
        WithSourceRun(source);
        _outputsBlobs.ReadAsync(pointer, Arg.Any<CancellationToken>())
            .Returns(new JobOutputsPayload(
                JobOutputsPayload.CurrentVersion,
                null,
                new[] { new JobOutputsDocument("consult", Referral, null) },
                null,
                null));

        var captured = await StartV7AndCaptureAsync(ReferringRequest());

        Assert.Null(captured.Outcome.Error);
        Assert.Equal(Referral, captured.OrchestrationInput!.Inputs!["consult_draft"]);
    }

    [Fact]
    public async Task AMigratedSourceRun_WithItsBlobGone_IsRefusedAsDeleted()
    {
        // A live pointer with no blob never becomes a silent empty copy.
        var source = SourceRun(text: null);
        source.OutputsBlob = new ConsultOutputsBlobPointer("org-job-outputs", "user-1/x.json");
        WithSourceRun(source);

        var captured = await StartV7AndCaptureAsync(ReferringRequest());

        Assert.Equal(ConsultGenerationJobStartError.InputRefTextDeleted, captured.Outcome.Error);
    }

    [Fact]
    public async Task AnotherAccountsRun_IsNotFound_NeverForbidden()
    {
        WithSourceRun(SourceRun(appUserId: "user-2"));

        var captured = await StartV7AndCaptureAsync(ReferringRequest());

        Assert.Equal(ConsultGenerationJobStartError.InputRefNotFound, captured.Outcome.Error);
        Assert.Contains("does not have", captured.Outcome.ErrorDetail);
        Assert.DoesNotContain("user-2", captured.Outcome.ErrorDetail);
    }

    [Fact]
    public async Task AnUnknownRun_IsNotFound()
    {
        WithSourceRun(null);

        var captured = await StartV7AndCaptureAsync(ReferringRequest());

        Assert.Equal(ConsultGenerationJobStartError.InputRefNotFound, captured.Outcome.Error);
    }

    [Fact]
    public async Task ARunWhoseTextWasDeleted_IsRefused_NamingTheDay()
    {
        // #368: the sweep leaves hashes, not text. An empty copy would be the
        // worst outcome; the refusal says what happened and when.
        WithSourceRun(SourceRun(text: null, dropped: new DateTimeOffset(2026, 9, 2, 3, 0, 0, TimeSpan.Zero)));

        var captured = await StartV7AndCaptureAsync(ReferringRequest());

        Assert.Equal(ConsultGenerationJobStartError.InputRefTextDeleted, captured.Outcome.Error);
        Assert.Contains("deleted on 2026-09-02", captured.Outcome.ErrorDetail);
    }

    [Fact]
    public async Task ARunThatDidNotComplete_OrHasNoSuchDeliverable_IsRefused()
    {
        WithSourceRun(SourceRun(status: "Failed"));
        var failed = await StartV7AndCaptureAsync(ReferringRequest());
        Assert.Equal(ConsultGenerationJobStartError.InputRefNotCompleted, failed.Outcome.Error);

        WithSourceRun(SourceRun());
        var missing = await StartV7AndCaptureAsync(new ConsultGenerationRequest(
            null,
            InputRefs: new Dictionary<string, List<ConsultInputRef>> { ["consult_draft"] = new() { new ConsultInputRef(SourceJob, "letter") } }));
        Assert.Equal(ConsultGenerationJobStartError.InputRefNotFound, missing.Outcome.Error);
        Assert.Contains("no deliverable 'letter'", missing.Outcome.ErrorDetail);
    }

    [Fact]
    public void ThePreviousRunKind_IsTheKebabWord()
    {
        Assert.Equal("previous-run", ConsultInputOriginKinds.PreviousRun);
    }

    [Fact]
    public async Task TypedInput_RecordsNoOrigin()
    {
        // Absence means "not recorded", never "typed" — email jobs supply text
        // until #237, and every job predating this field has none either.
        var request = new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = Referral });

        var captured = await StartV7AndCaptureAsync(request);

        Assert.Null(captured.Initialize!.InputOrigins);
        Assert.Null(captured.OrchestrationInput!.InputOrigins);
    }

    [Fact]
    public async Task AttachedDocumentBytes_NeverReachDurableState()
    {
        // The regression guard. The whole request is carried into the
        // orchestration input, which Durable persists to the storage account
        // and spills to blob past the inline limit — so leaving the bytes on
        // would put every attached document at rest with no retention story.
        // The extracted text is in Inputs; nothing downstream needs the file.
        var request = new ConsultGenerationRequest(
            null,
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["consult_draft"] = [new("text/plain", System.Text.Encoding.UTF8.GetBytes(Referral))]
            });

        var captured = await StartV7AndCaptureAsync(request);

        Assert.Null(captured.OrchestrationInput!.Request.InputFiles);
    }

    [Fact]
    public async Task UnreadableDocument_IsRefusedWithTheSameSentenceThePreviewGives()
    {
        // Binary that is not a format we read. It must come back as a start
        // error rather than an exception, and say the same thing the preview
        // endpoint would have said about the same bytes.
        var request = new ConsultGenerationRequest(
            null,
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["consult_draft"] = [new("application/octet-stream", [0x00, 0x01, 0x02, 0x00, 0xFF])]
            });

        var captured = await StartV7AndCaptureAsync(request);

        Assert.Equal(ConsultGenerationJobStartError.InputFileUnreadable, captured.Outcome.Error);
        Assert.Equal(
            DocumentExtractionCopy.For(DocumentExtractionOutcomes.UnsupportedType),
            captured.Outcome.ErrorDetail);
        // #369: and the sender may be told it — the copy describes a format,
        // never the file's name or contents (#217). This path replied with its
        // cause before SenderSafeDetail existed and must keep doing so.
        Assert.Equal(captured.Outcome.ErrorDetail, captured.Outcome.SenderSafeDetail);
    }

    [Fact]
    public void CrlfAndLfText_NowHashIdentically()
    {
        // Nothing normalised before, so the same referral pasted from a
        // Windows editor and typed on Linux were "different input" to the
        // record for a reason no reader of it could see.
        var windows = ConsultGenerationJobStarter.NormalizeInputs(new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "One.\r\nTwo.\r\n" }));
        var unix = ConsultGenerationJobStarter.NormalizeInputs(new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "One.\nTwo." }));

        Assert.Equal(
            ConsultGenerationProvenance.ComputeTypedInputsHash(windows.Inputs!),
            ConsultGenerationProvenance.ComputeTypedInputsHash(unix.Inputs!));
    }

    [Fact]
    public void BareCrText_HashesLikeItsLfEquivalent()
    {
        // The CRLF case above was closed in #238; a lone \r survived both
        // normalisation sites until #251, so § 2's "conservative and
        // universal" was broader than the code. A referral carrying classic
        // Mac endings hashed differently from the same referral typed, and
        // the record called them different input for a reason no reader
        // could see.
        var mac = ConsultGenerationJobStarter.NormalizeInputs(new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "One.\rTwo.\r" }));
        var unix = ConsultGenerationJobStarter.NormalizeInputs(new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "One.\nTwo." }));

        Assert.Equal(
            ConsultGenerationProvenance.ComputeTypedInputsHash(mac.Inputs!),
            ConsultGenerationProvenance.ComputeTypedInputsHash(unix.Inputs!));
    }

    [Fact]
    public void ANullConsultDraft_SurvivesNormalisationAsNull()
    {
        // The trap in sharing one normaliser across call sites: this runs
        // over ConsultDraft too, and a helper that collapsed null to ""
        // would turn a v5/v6 job's absent draft into an empty one. The v2
        // draft-only hash serialises the field, so {"consultDraft":null}
        // and {"consultDraft":""} are different hashes.
        var normalized = ConsultGenerationJobStarter.NormalizeInputs(
            new ConsultGenerationRequest(null, Inputs: null));

        Assert.Null(normalized.ConsultDraft);
    }

    [Fact]
    public void NormalizeInputs_NormalisesTextInsideStructure_AndLeavesNumbersAlone()
    {
        // #422: the per-element hook #421 deferred. Every text scalar inside
        // an array or an object is normalised like a top-level one — the same
        // referral as an element of prior_notes and as consult_draft must hash
        // identically — in the order it arrived, which definition 5 keeps.
        var normalized = ConsultGenerationJobStarter.NormalizeInputs(new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = "One.\r\nTwo.\r\n",
                ["prior_notes"] = ConsultInputValue.OfArray(new[]
                {
                    ConsultInputValue.OfText("Second.\r\n"),
                    ConsultInputValue.OfText("First.\rStill first.  ")
                }),
                ["patient"] = ConsultInputValue.OfObject(new[]
                {
                    new ConsultInputEntry("note", ConsultInputValue.OfText("Note.\r\n")),
                    new ConsultInputEntry("age", ConsultInputValue.OfNumber("1.50"))
                }),
                ["length_of_stay"] = ConsultInputValue.OfNumber("3")
            }));

        Assert.Equal("One.\nTwo.", normalized.Inputs!["consult_draft"].Text);
        Assert.Equal(
            new[] { "Second.", "First.\nStill first." },
            normalized.Inputs["prior_notes"].Elements!.Select(element => element.Text));
        Assert.Equal("Note.", normalized.Inputs["patient"].Fields![0].Value.Text);
        Assert.Equal(ConsultInputValue.OfNumber("1.50"), normalized.Inputs["patient"].Fields[1].Value);
        Assert.Equal(ConsultInputValue.OfNumber("3"), normalized.Inputs["length_of_stay"]);
    }

    [Fact]
    public void CrlfAndLfInsideAnArray_HashIdenticallyUnderDefinition5()
    {
        var windows = ConsultGenerationJobStarter.NormalizeInputs(new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue>
            {
                ["prior_notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("One.\r\nTwo.\r\n") })
            }));
        var unix = ConsultGenerationJobStarter.NormalizeInputs(new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue>
            {
                ["prior_notes"] = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("One.\nTwo.") })
            }));

        Assert.Equal(
            ConsultGenerationProvenance.ComputeStructuredInputsHash(windows.Inputs!),
            ConsultGenerationProvenance.ComputeStructuredInputsHash(unix.Inputs!));
    }

    private sealed record StartCapture(
        ConsultGenerationJobStartOutcome Outcome,
        ConsultGenerationJobInitialize? Initialize,
        ConsultGenerationOrchestrationInput? OrchestrationInput);

    // ---- the parse gate's wait budget (#241, § 9) -----------------------

    [Fact]
    public void TheEmailDoorWaitsFarLongerForAParseSlotThanTheAppDoor()
    {
        // Not a preference. Every start-failure path in EmailIntakeProcessor
        // moves the message to the Rejected folder, writes a claim and REPLIES
        // to the sender — there is no "leave it for the next poll" branch. So
        // a transient busy on that door would permanently reject a referral
        // and tell a clinician their document could not be read, which would
        // be false.
        //
        // A background poller can afford to be slow. It cannot afford to be
        // wrong.
        var app = ConsultGenerationJobStarter.GateWaitFor(
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App));
        var email = ConsultGenerationJobStarter.GateWaitFor(
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.Email, "doc@example.com"));

        Assert.Equal(DocumentExtraction.InteractiveGateWait, app);
        Assert.Equal(DocumentExtraction.BackgroundGateWait, email);
        Assert.True(email > app * 10, "the email budget must not be a rounding difference from the app's");
    }

    // ---- the logging audit (#241, § 9) ----------------------------------
    //
    // "Bytes are never persisted and never logged, including on the exception
    // paths." Traced by reading every log statement on this path, and pinned
    // here so it is a property rather than an observation.

    private const string Sentinel = "SENTINEL-CLINICAL-CONTENT-0f1e2d";

    [Fact]
    public async Task AReadableDocument_PutsNoneOfItsContentInTheLog()
    {
        var log = new CapturingLogger<ConsultGenerationJobStarter>();

        await StartV7AndCaptureAsync(
            new ConsultGenerationRequest(
                null,
                InputFiles: new Dictionary<string, List<InputFilePayload>>
                {
                    ["consult_draft"] = [new("text/plain", Encoding.UTF8.GetBytes(Sentinel))]
                }),
            log);

        Assert.DoesNotContain(Sentinel, log.Everything, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableDocument_PutsNoneOfItsContentInTheLog()
    {
        // The exception path is the one § 9 calls out, because it is where
        // "include the input so we can debug it" is most tempting. The bytes
        // here are a truncated zip carrying the sentinel, so a handler that
        // echoed any fragment of what it was reading would be caught.
        var log = new CapturingLogger<ConsultGenerationJobStarter>();
        var corrupt = Encoding.UTF8.GetBytes("PK" + Sentinel);

        var captured = await StartV7AndCaptureAsync(
            new ConsultGenerationRequest(
                null,
                InputFiles: new Dictionary<string, List<InputFilePayload>>
                {
                    ["consult_draft"] = [new("application/octet-stream", corrupt)]
                }),
            log);

        Assert.Equal(ConsultGenerationJobStartError.InputFileUnreadable, captured.Outcome.Error);
        Assert.DoesNotContain(Sentinel, log.Everything, StringComparison.Ordinal);
    }

    // ----- #428: several documents for one slot -------------------------

    private static Dictionary<string, ConsultInputValue> V9Typed() => new(StringComparer.Ordinal)
    {
        ["consult_draft"] = Referral,
        ["seen_on"] = "2026-08-10",
        ["encounter_kind"] = "follow_up"
    };

    private static InputFilePayload Text(string text) =>
        new("text/plain", Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task ADocumentsOrigin_CarriesTheFilesDigestAndItsReadings()
    {
        // #512: the file as received and the text the input hash saw for it —
        // two digests per document, positionally. The file carries a CRLF and
        // trailing whitespace, so the reading's digest is not the raw bytes'
        // digest and not the digest of the un-normalised text: it is exactly
        // the SHA-256 of the value that lands in the effective-input map.
        var bytesOne = Encoding.UTF8.GetBytes("One.\r\nline two   \r\n");
        var bytesTwo = Encoding.UTF8.GetBytes("Two.");
        var request = new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["prior_notes"] = [new InputFilePayload("text/plain", bytesOne), new InputFilePayload("text/plain", bytesTwo)]
            });

        var captured = await StartAndCaptureAsync(V9Fixtures.Structured(), request);

        Assert.Null(captured.Outcome.Error);
        var origins = captured.OrchestrationInput!.InputDocumentOrigins!["prior_notes"];
        var elements = captured.OrchestrationInput.Request.Inputs!["prior_notes"].Elements!;
        Assert.Equal(2, origins.Count);

        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(bytesOne), origins[0].FileSha256);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(elements[0].Canonical), origins[0].TextSha256);
        Assert.NotEqual(origins[0].FileSha256, origins[0].TextSha256);
        Assert.NotEqual(ConsultGenerationProvenance.Sha256Hex("One.\r\nline two   \r\n"), origins[0].TextSha256);

        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(bytesTwo), origins[1].FileSha256);
        Assert.Equal(ConsultGenerationProvenance.Sha256Hex(elements[1].Canonical), origins[1].TextSha256);
        // A plain file whose reading is its own bytes: the two digests agree,
        // which is the statement "nothing changed between file and reading".
        Assert.Equal(origins[1].FileSha256, origins[1].TextSha256);
    }

    [Fact]
    public async Task FourDocumentsIntoOneSlot_BecomeFourElementsInSuppliedOrderWithFourOrigins()
    {
        // The issue's first test: order is the caller's and is the order the
        // elements hash in (definition 5), so a reversal would be a different
        // record of the same referral.
        var request = new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["prior_notes"] = [Text("One."), Text("Two."), Text("Three."), Text("Four.")]
            });

        var captured = await StartAndCaptureAsync(V9Fixtures.Structured(), request);

        Assert.Null(captured.Outcome.Error);
        var expected = ConsultInputValue.OfArray(new[] { "One.", "Two.", "Three.", "Four." }.Select(ConsultInputValue.OfText));
        Assert.Equal(expected, captured.OrchestrationInput!.Request.Inputs!["prior_notes"]);
        Assert.Equal("""["One.","Two.","Three.","Four."]""", captured.OrchestrationInput.Inputs!["prior_notes"]);
        Assert.Null(captured.OrchestrationInput.Request.InputFiles);

        var supplied = V9Typed();
        supplied["prior_notes"] = expected;
        Assert.Equal(ConsultGenerationProvenance.ComputeStructuredInputsHash(supplied), captured.OrchestrationInput.EffectiveInputHash);

        var origins = Assert.Contains("prior_notes", captured.Initialize!.InputDocumentOrigins);
        Assert.Equal(4, origins.Count);
        Assert.All(origins, origin => Assert.Equal(ConsultInputOriginKinds.Document, origin.Kind));
        Assert.Null(captured.Initialize.InputOrigins);
        Assert.Equal(4, captured.OrchestrationInput.InputDocumentOrigins!["prior_notes"].Count);
    }

    [Fact]
    public async Task OneDocumentIntoAnArrayOfTextSlot_IsAOneElementArray()
    {
        var request = new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["prior_notes"] = [Text("One.")] });

        var captured = await StartAndCaptureAsync(V9Fixtures.Structured(), request);

        Assert.Null(captured.Outcome.Error);
        Assert.Equal(
            ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("One.") }),
            captured.OrchestrationInput!.Request.Inputs!["prior_notes"]);
        Assert.Single(captured.Initialize!.InputDocumentOrigins!["prior_notes"]);
    }

    [Fact]
    public async Task ASlotWhoseDocumentsSumPastTheCap_IsRefusedNotTruncated()
    {
        // Each document clears the parser's own bound; together they exceed
        // what an input may carry. Refused with the slot named and the count
        // of documents — never the text, never a filename.
        var half = new string('a', ConsultGenerationJobs.MaxInputLength / 2);
        var over = await StartAndCaptureAsync(V9Fixtures.Structured(), new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["prior_notes"] = [Text(half + "a"), Text(half + "a")] }));

        Assert.Equal(ConsultGenerationJobStartError.InputTooLong, over.Outcome.Error);
        Assert.Equal("Input 'prior_notes' exceeds 256 KB across its 2 documents.", over.Outcome.ErrorDetail);
        Assert.Equal(over.Outcome.ErrorDetail, over.Outcome.SenderSafeDetail);
        Assert.Null(over.Initialize);
        Assert.Null(over.OrchestrationInput);

        // Exactly at the bound passes: the cap is strict, as the door's is.
        var at = await StartAndCaptureAsync(V9Fixtures.Structured(), new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["prior_notes"] = [Text(half), Text(half)] }));

        Assert.Null(at.Outcome.Error);
    }

    [Fact]
    public async Task TheContentFloor_MeasuresTheSlotNotTheElement()
    {
        // One thin document among several is not an empty referral; several
        // thin ones are.
        var manifest = V9Fixtures.WithInput(new("prior_notes", "Prior notes", Required: true, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text));

        var mixed = await StartAndCaptureAsync(manifest, new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["prior_notes"] = [Text("Thin."), Text(Referral)] }));

        Assert.Null(mixed.Outcome.Error);

        var thin = await StartAndCaptureAsync(manifest, new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["prior_notes"] = [Text("Thin."), Text("Thin."), Text("Thin."), Text("Thin.")] }));

        Assert.Equal(ConsultGenerationJobStartError.InputWithoutContent, thin.Outcome.Error);
        Assert.Contains("'prior_notes' does not contain a referral to work from.", thin.Outcome.ErrorDetail);

        // And the origins of the thin documents were never the question: a
        // document that read to little is still recorded — the refusal here
        // is about the slot, before anything is recorded.
        Assert.Null(thin.Initialize);
    }

    [Fact]
    public async Task OneDocumentIntoATextSlot_OnAV8Package_HashesLikeTheEquivalentText()
    {
        // The v8 twin of AttachedDocument_FillsItsSlotAndHashesLikeTheEquivalentText:
        // a text slot's one document is OfText, so definition 4 sees the
        // bytes it always did.
        var request = new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["seen_on"] = "2026-08-10",
                ["encounter_kind"] = "follow_up"
            },
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["consult_draft"] = [Text(Referral)] });

        var captured = await StartAndCaptureAsync(V8Fixtures.Typed(), request);

        Assert.Null(captured.Outcome.Error);
        Assert.Equal(4, captured.OrchestrationInput!.EffectiveInputHashVersion);
        Assert.Equal(ConsultInputValue.OfText(Referral), captured.OrchestrationInput.Request.Inputs!["consult_draft"]);
        Assert.Equal(
            ConsultGenerationProvenance.ComputeTypedInputsHash(new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Referral,
                ["seen_on"] = "2026-08-10",
                ["encounter_kind"] = "follow_up"
            }),
            captured.OrchestrationInput.EffectiveInputHash);
    }

    [Fact]
    public async Task TwoDocumentsIntoATextSlot_AreRefusedByName()
    {
        // A text slot takes one: concatenation would invent the boundary the
        // request is careful never to carry (v9 design § 7). Refused before a
        // byte is parsed, with a sentence the email door may quote back.
        const string expected = "Input 'consult_draft' is a text and takes one document; declare it an array of text to supply several.";

        var v9 = await StartAndCaptureAsync(V9Fixtures.Structured(), new ConsultGenerationRequest(
            null,
            Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal) { ["seen_on"] = "2026-08-10", ["encounter_kind"] = "follow_up" },
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["consult_draft"] = [Text("One."), Text("Two.")] }));

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, v9.Outcome.Error);
        Assert.Equal(expected, v9.Outcome.ErrorDetail);
        Assert.Equal(expected, v9.Outcome.SenderSafeDetail);

        // The same on a v7 package, whose every slot is a text.
        var v7 = await StartV7AndCaptureAsync(new ConsultGenerationRequest(
            null,
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["consult_draft"] = [Text("One."), Text("Two.")] }));

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, v7.Outcome.Error);
        Assert.Equal(expected, v7.Outcome.ErrorDetail);
    }

    [Fact]
    public async Task SeveralDocumentsIntoAnUndeclaredSlot_AreRefusedWithoutQuotingTheIdToTheSender()
    {
        var captured = await StartAndCaptureAsync(V9Fixtures.Structured(), new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>> { ["made_up"] = [Text("One."), Text("Two.")] }));

        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, captured.Outcome.Error);
        Assert.Equal("Input 'made_up' is not declared by this package and takes no documents.", captured.Outcome.ErrorDetail);
        Assert.Null(captured.Outcome.SenderSafeDetail);
    }

    [Fact]
    public async Task AnUnreadableSecondDocument_NamesItsPosition()
    {
        var captured = await StartAndCaptureAsync(V9Fixtures.Structured(), new ConsultGenerationRequest(
            null,
            Inputs: V9Typed(),
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["prior_notes"] = [Text("One."), new("application/octet-stream", [0x00, 0x01, 0x02, 0x00, 0xFF])]
            }));

        Assert.Equal(ConsultGenerationJobStartError.InputFileUnreadable, captured.Outcome.Error);
        Assert.StartsWith("Input 'prior_notes' document 2 of 2: ", captured.Outcome.ErrorDetail);
        Assert.Equal(captured.Outcome.ErrorDetail, captured.Outcome.SenderSafeDetail);
    }

    [Fact]
    public async Task FourReadableDocuments_PutNoneOfTheirContentInTheLog()
    {
        var log = new CapturingLogger<ConsultGenerationJobStarter>();
        var sentinels = Enumerable.Range(1, 4).Select(n => $"{Sentinel}-{n}").ToList();

        await StartAndCaptureAsync(
            V9Fixtures.Structured(),
            new ConsultGenerationRequest(
                null,
                Inputs: V9Typed(),
                InputFiles: new Dictionary<string, List<InputFilePayload>> { ["prior_notes"] = sentinels.Select(Text).ToList() }),
            log);

        Assert.DoesNotContain(Sentinel, log.Everything, StringComparison.Ordinal);
    }

    private Task<StartCapture> StartV7AndCaptureAsync(
        ConsultGenerationRequest request,
        ILogger<ConsultGenerationJobStarter>? logger = null) =>
        StartAndCaptureAsync(V7Fixtures.Minimal(), request, logger);

    private async Task<StartCapture> StartAndCaptureAsync(
        WorkflowPackageManifest manifest,
        ConsultGenerationRequest request,
        ILogger<ConsultGenerationJobStarter>? logger = null)
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                manifest,
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter(logger).StartAsync(
            _client,
            request,
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        return new StartCapture(outcome, initialize, orchestrationInput);
    }

    [Fact]
    public async Task ScalarDataEntry_ReachesTheOrchestrationInput()
    {
        // The one hop in the scalar chain nothing covered. The validator
        // accepts a `data:<id>` scalar binding and ConsultNodeVariableResolver
        // binds it into a prompt — both tested — but between them the starter
        // has to lift package.Data.Scalars onto the orchestration input, and
        // that line had no test. A package-authored value that never left the
        // package would fail at render, in production, with the binding
        // reading "carries no data scalars".
        var manifest = V7Fixtures.Minimal();
        manifest.Data!["specialty"] = "data/specialty.txt";

        var files = V6Fixtures.Files(manifest);
        files["data/specialty.txt"] = "Oncology";

        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);

        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(
                manifest,
                Nodes: manifest.Nodes,
                SchemaContracts: TestOutputContracts.CatalogSchemas,
                Data: data,
                ResultNodeId: "assemble-note",
                Results: new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Assemble note") }));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(orchestrationInput);
        var specialty = Assert.Contains("specialty", orchestrationInput.DataScalars);
        Assert.Equal("Oncology", specialty);
    }

    [Fact]
    public async Task ScalarDataEntry_IsDistinguishedByTheTrailingSlashAlone()
    {
        // One character decides it: WorkflowDataResolver reads a path ending
        // in '/' as a collection and anything else as a scalar. The editor
        // writes this path, so a stray slash either way silently reclassifies
        // the entry — a collection is not bindable and a scalar is not
        // iterable, and both fail late.
        var manifest = V7Fixtures.Minimal();
        manifest.Data!["specialty"] = "data/specialty/";

        var files = V6Fixtures.Files(manifest);
        files["data/specialty.txt"] = "Oncology";

        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);

        // Read as a collection, so it is not a scalar and its index is missing.
        Assert.DoesNotContain("specialty", data.Scalars);
        Assert.Contains(errors, e => e.Contains("specialty") && e.Contains("index.json"));
    }

    // #266 — the rate limit is checked here because this door serves both the
    // app and the email poller. Two enforcement points cover all three ways in.

    [Fact]
    public async Task OverTheLimit_IsRefusedWithHowLongUntilItResets()
    {
        var starter = CreateStarter();
        Refuse();

        var outcome = await starter.StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.RateLimited, outcome.Error);
        Assert.Null(outcome.JobId);
        Assert.Equal(TimeSpan.FromMinutes(37), outcome.RetryAfter);
        Assert.Contains("60", outcome.ErrorDetail);
    }

    [Fact]
    public async Task OverTheLimit_IsRefusedBeforeTheRegistryIsTouched()
    {
        // The check is first in StartAsync deliberately: a refused submission
        // must not buy free registry round trips.
        var starter = CreateStarter();
        Refuse();

        await starter.StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        await _pinResolver.DidNotReceive().ResolvePinAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _packageStore.DidNotReceive().ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheEmailDoorIsRateLimitedToo()
    {
        // It reaches the parser through this method, so exempting it would
        // leave the limit trivially bypassable — 25 messages every two
        // minutes. What the email door does with the refusal is
        // EmailIntakeProcessor's business, and it is not a rejection reply.
        var starter = CreateStarter();
        Refuse();

        var outcome = await starter.StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.Email, "user@example.com"),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.RateLimited, outcome.Error);
    }

    // #290 — a required input that is present but carries no referral. The
    // failure this prevents: a full consult generated from a body containing
    // only a OneDrive link, every section reading "not documented", delivered.

    [Fact]
    public async Task AReferralThatIsOnlyALink_DoesNotStartAJob()
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV5Package());

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(
                "https://consultologist-my.sharepoint.com/:w:/g/personal/u/EX9fLk2mQ_dHqB7wZ8vNc1kBqL3rT6yPmA2sK4uW0nXeVg"),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.Email, "doc@example.com"),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.InputWithoutContent, outcome.Error);
        Assert.Null(outcome.JobId);
        // The sentence has to tell them what to do differently.
        Assert.Contains("attach the file itself", outcome.ErrorDetail);
        // #369: it names an authored input id and quotes none of the content
        // that was too short, so it is the sender's to read.
        Assert.Equal(outcome.ErrorDetail, outcome.SenderSafeDetail);
        await _client.DidNotReceiveWithAnyArgs().ScheduleNewOrchestrationInstanceAsync(
            default, default, default, default);
    }

    [Fact]
    public async Task AReferralBehindACloudLink_DoesNotStartAJob()
    {
        // #291: the message that defeated #290's floor -- a OneDrive link
        // wrapped in a greeting and a signature, which clears forty
        // characters and generated a complete empty consult.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV5Package());

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(
                "Hi, here is the referral. https://consultologist-my.sharepoint.com/:w:/g/personal/u/EX9fLk2m Regards, Dr X, Oncology"),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.Email, "doc@example.com"),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.InputBehindACloudLink, outcome.Error);
        Assert.Contains("attach the document", outcome.ErrorDetail);
        // #369: an authored input id and fixed prose — the link, which may
        // carry a filename, is not quoted.
        Assert.Equal(outcome.ErrorDetail, outcome.SenderSafeDetail);
        await _client.DidNotReceiveWithAnyArgs().ScheduleNewOrchestrationInstanceAsync(
            default, default, default, default);
    }

    [Fact]
    public async Task ATerseButRealReferral_StillStarts()
    {
        // The regression in the other direction, and the reason the floor
        // strips URLs rather than counting characters.
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV5Package());
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(), Arg.Any<object?>(), Arg.Any<StartOrchestrationOptions?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.JobId);
    }

    [Fact]
    public async Task ALimiterThatThrows_DoesNotStopTheSubmission()
    {
        // TableAccountRateLimiter catches its own storage faults, so this
        // pins the second layer: AcquireOrAllowAsync makes fail-open true of
        // any implementation. The asymmetry is the justification — losing the
        // limit costs CPU, refusing on a fault costs a referral.
        var starter = CreateStarter();
        _rateLimiter
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<RateLimitDecision>>(_ => throw new InvalidOperationException("table down"));
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV5Package());

        var outcome = await starter.StartAsync(
            _client,
            new ConsultGenerationRequest(Referral),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Null(outcome.Error);
        Assert.NotNull(outcome.JobId);
    }
    // v10 (#496): a package with classifiers starts deciding.
    private async Task<(ConsultGenerationJobStartOutcome Outcome, ConsultGenerationJobInitialize? Initialize, ConsultGenerationOrchestrationInput? Input)>
        StartClassifierAsync(string when)
    {
        var (manifest, files) = V10Fixtures.WithClassifier();
        var errors = new List<string>();
        var data = WorkflowDataResolver.Resolve(manifest, files, errors);
        Assert.Empty(errors);
        Assert.True(WorkflowResultConditions.TryParseExpression(when, out var condition, out var error), error);

        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackage(
                manifest,
                Nodes: manifest.Nodes,
                SchemaContracts: TestOutputContracts.CatalogSchemas,
                Data: data,
                Results: new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Consultation note", condition) }));

        ConsultGenerationJobInitialize? initialize = null;
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.Initialize),
            Arg.Do<object>(payload => initialize = payload as ConsultGenerationJobInitialize));
        await _entities.SignalEntityAsync(
            Arg.Any<EntityInstanceId>(),
            nameof(ConsultGenerationJobEntity.RecordStartFailure),
            Arg.Do<object>(payload => _recordedStartFailure = payload as ConsultGenerationJobStartFailure));

        ConsultGenerationOrchestrationInput? orchestrationInput = null;
        _client.ScheduleNewOrchestrationInstanceAsync(
                Arg.Any<TaskName>(),
                Arg.Do<object?>(payload => orchestrationInput = payload as ConsultGenerationOrchestrationInput),
                Arg.Any<StartOrchestrationOptions?>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult(((StartOrchestrationOptions?)callInfo[2])!.InstanceId!));

        var supplied = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = Referral,
            ["seen_on"] = "2026-08-10",
            ["encounter_kind"] = "follow_up"
        };

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: supplied),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        return (outcome, initialize, orchestrationInput);
    }

    [Fact]
    public async Task AClassifierPackage_StartsDeciding_WithNoSkeletonAndEveryNode()
    {
        var (outcome, initialize, input) = await StartClassifierAsync("node:scope == in_scope");

        Assert.Null(outcome.Error);
        Assert.Null(_recordedStartFailure);
        Assert.NotNull(initialize);
        Assert.True(initialize!.Deciding);
        Assert.Empty(initialize.Items);
        Assert.Null(initialize.SkippedDocuments);
        Assert.Null(initialize.ItemSteps);
        Assert.Contains(initialize.Nodes!, node => node.Id == "scope" && node.OutputContract == Consultologist.Api.Agents.OutputContracts.Classification);
        Assert.Contains(initialize.Nodes!, node => node.Id == "assemble-note");

        Assert.NotNull(input);
        Assert.True(input!.Deciding);
        Assert.Empty(input.Items!);
        // Every declared deliverable rides — the boundary narrows.
        Assert.Equal("consult", Assert.Single(input.Results!).Id);
        // The supplied values ride as their wire JSON and read back as themselves.
        Assert.Equal("\"follow_up\"", input.SuppliedInputs!["encounter_kind"]);
        Assert.Equal("follow_up", ConsultInputValue.FromJson(input.SuppliedInputs["encounter_kind"]).Canonical);
    }

    [Fact]
    public async Task AClassifierPackage_IsNeverRefusedAtStart_ForAConditionOnlyTheBoundaryCanAnswer()
    {
        // At start no classification exists, so this condition is absent —
        // and the starter must not treat absent as "nothing applies".
        var (outcome, initialize, _) = await StartClassifierAsync("node:scope == in_scope");

        Assert.Null(outcome.Error);
        Assert.NotNull(initialize);
        Assert.Null(_recordedStartFailure);
    }

    [Fact]
    public async Task APackageWithoutClassifiers_WritesNoDecidingFlag()
    {
        var (outcome, initialize, input) = await StartFannedAsync(V9Fixtures.Fanned(), ConsultInputValue.OfArray(new[]
        {
            ConsultInputValue.OfText("Seen in clinic; BP 150/95."),
            ConsultInputValue.OfText("Follow-up; BP 130/85.")
        }));

        Assert.Null(outcome.Error);
        Assert.NotNull(initialize);
        Assert.Null(initialize!.Deciding);
        Assert.Null(input!.Deciding);
        Assert.Null(input.SuppliedInputs);
    }

}

public class ResolveEffectiveInputsTests
{
    [Fact]
    public void LegacyPackage_DraftOnly_ResolvesNullMaps()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest("Draft."), V5Fixtures.Manifest());

        Assert.Null(resolution.Error);
        Assert.Null(resolution.Effective);
        Assert.Null(resolution.Supplied);
    }

    [Fact]
    public void LegacyPackage_ForeignInputId_IsRejected()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>
            {
                ["consult_draft"] = "Draft.",
                ["labs"] = "CBC normal."
            }),
            V6Fixtures.SingleCollection());

        Assert.Contains("'labs'", resolution.Error);
        Assert.Contains("accepts only consult_draft", resolution.Error);
    }

    [Fact]
    public void V7_LegacyDraft_BackFillsTheConventionalSlot()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest("Draft."), V7Fixtures.Minimal());

        Assert.Null(resolution.Error);
        Assert.Equal(new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "Draft." }, resolution.Supplied);
        Assert.Equal(new Dictionary<string, string> { ["consult_draft"] = "Draft." }, resolution.Effective);
    }

    [Fact]
    public void V7_AbsentOptionalInput_IsEmptyInEffectiveAndOmittedInSupplied()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "Draft." }),
            V7Fixtures.MultiDeliverable());

        Assert.Null(resolution.Error);
        Assert.False(resolution.Supplied!.ContainsKey("prior_notes"));
        Assert.Equal(string.Empty, resolution.Effective!["prior_notes"]);
        Assert.Equal("Draft.", resolution.Effective["consult_draft"]);
    }

    // #313: v8 types a slot, and a supplied value must be canonical for its
    // type — rejected, never normalised, so provenance records what arrived.

    private static Dictionary<string, ConsultInputValue> TypedInputs(
        params (string Id, ConsultInputValue Value)[] overrides)
    {
        var inputs = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = "Draft.",
            ["seen_on"] = "2026-08-10",
            ["encounter_kind"] = "follow_up"
        };

        foreach (var (id, value) in overrides)
        {
            inputs[id] = value;
        }

        return inputs;
    }

    [Fact]
    public void V8_CanonicalTypedValues_AreAccepted()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: TypedInputs(("billable", ConsultInputValue.OfBoolean(true)))),
            V8Fixtures.Typed());

        Assert.Null(resolution.Error);
        Assert.Equal("2026-08-10", resolution.Effective!["seen_on"]);
    }

    [Theory]
    // Valid-but-different is still rejected: normalising it would hash a value
    // nobody sent.
    [InlineData("seen_on", "2026-8-1", "must be written YYYY-MM-DD")]
    [InlineData("seen_on", "10/08/2026", "must be written YYYY-MM-DD")]
    // A string for a boolean slot is now a TYPE error, not a spelling one:
    // the wire carries JSON true/false, so any string is the wrong shape.
    [InlineData("billable", "yes", "must be sent as JSON true or false")]
    [InlineData("billable", "true", "must be sent as JSON true or false")]
    [InlineData("encounter_kind", "procedure", "'new_patient', 'follow_up'")]
    public void V8_NonCanonicalValue_IsRejectedAndNamesTheInput(string id, string value, string expected)
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: TypedInputs((id, value))),
            V8Fixtures.Typed());

        Assert.Contains($"Input '{id}'", resolution.Error);
        Assert.Contains(expected, resolution.Error);
    }

    [Fact]
    public void V8_BooleanSentAsJsonBoolean_IsAccepted()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: TypedInputs(("billable", ConsultInputValue.OfBoolean(false)))),
            V8Fixtures.Typed());

        Assert.Null(resolution.Error);
        // false is an answer, not an absence: it survives the required/blank
        // check and reaches the resolver map as "false".
        Assert.Equal("false", resolution.Effective!["billable"]);
    }

    [Fact]
    public void V8_StringForANonBooleanSlot_IsStillRequired()
    {
        // The mirror of the boolean rule: a JSON boolean in a text, date or
        // enum slot is the wrong shape too.
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: TypedInputs(("seen_on", ConsultInputValue.OfBoolean(true)))),
            V8Fixtures.Typed());

        Assert.Contains("Input 'seen_on'", resolution.Error);
        Assert.Contains("must be sent as a JSON string", resolution.Error);
    }

    [Fact]
    public void V8_AbsentOptionalTypedInput_IsNotCheckedForCanonicalForm()
    {
        // Absence is not a malformed value; the v7 resolution rule stands.
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: TypedInputs()),
            V8Fixtures.Typed());

        Assert.Null(resolution.Error);
        Assert.Equal(string.Empty, resolution.Effective!["billable"]);
    }

    // #421 (v9 layer 1): the wire now carries a number, an object and an
    // array, and no v5–v8 declaration can ask for any of them. Each is a 422
    // naming the slot — never a crash, and never the value, which is why the
    // payloads here are distinctive strings asserted absent from the error.

    private static ConsultInputValue Structured(string kind) => kind switch
    {
        "number" => ConsultInputValue.OfNumber("424242"),
        "object" => ConsultInputValue.OfObject(new[] { new ConsultInputEntry("k", ConsultInputValue.OfText("secret")) }),
        "array" => ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("secret") }),
        "array-with-null" => ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("secret"), ConsultInputValue.NullElement }),
        "empty-array" => ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>()),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [Theory]
    [InlineData("consult_draft", "number", "must be sent as a JSON string; got a number")]
    [InlineData("seen_on", "object", "must be sent as a JSON string; got an object")]
    [InlineData("encounter_kind", "array", "must be sent as a JSON string; got an array")]
    [InlineData("billable", "number", "must be sent as JSON true or false; got a number")]
    [InlineData("billable", "array-with-null", "must be sent as JSON true or false; got an array")]
    public void V8_StructuredValue_IsRejectedAndNamesTheInput(string id, string kind, string expected)
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: TypedInputs((id, Structured(kind)))),
            V8Fixtures.Typed());

        Assert.Contains($"Input '{id}'", resolution.Error);
        Assert.Contains(expected, resolution.Error);
        Assert.DoesNotContain("424242", resolution.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", resolution.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void V8_AnEmptyArray_IsTheWrongShape_NotAMissingInput()
    {
        // Present and empty (v9 § 4): it is not blank, so it reaches the
        // shape check and is refused for what it is, rather than read as an
        // absence the caller never expressed.
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: TypedInputs(("consult_draft", Structured("empty-array")))),
            V8Fixtures.Typed());

        Assert.Contains("Input 'consult_draft'", resolution.Error);
        Assert.Contains("got an array", resolution.Error);
        Assert.DoesNotContain("missing", resolution.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyPackage_StructuredConsultDraft_IsRefusedNotFolded()
    {
        // A v5/v6 package folds consult_draft into the draft field through its
        // canonical string. Structure has none, so the fold would throw; the
        // refusal lands in the <7 branch where the slot can be named.
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Structured("object")
            }),
            V5Fixtures.Manifest());

        Assert.Contains("Input 'consult_draft'", resolution.Error);
        Assert.Contains("must be sent as a JSON string; got an object", resolution.Error);
        Assert.DoesNotContain("secret", resolution.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolverForm_IsCanonicalForScalars_AndTheCarrierForStructure()
    {
        // #423: the one place shape is decided. Scalars keep the string v7
        // and v8 carried; structure, which has no canonical string, travels
        // as its wire JSON and is reconstructed by the renderer. Until #424
        // admits a structured declaration, nothing reaches this with
        // structure end to end — which is why it is a function with a test.
        var array = ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("a"), ConsultInputValue.NullElement });
        var obj = ConsultInputValue.OfObject(new[] { new ConsultInputEntry("z", ConsultInputValue.OfNumber("1.50")) });

        Assert.Equal("Draft.", ConsultGenerationJobStarter.ResolverForm("Draft."));
        Assert.Equal("true", ConsultGenerationJobStarter.ResolverForm(ConsultInputValue.OfBoolean(true)));
        Assert.Equal("1.50", ConsultGenerationJobStarter.ResolverForm(ConsultInputValue.OfNumber("1.50")));
        Assert.Equal("""["a",null]""", ConsultGenerationJobStarter.ResolverForm(array));
        Assert.Equal("""{"z":1.50}""", ConsultGenerationJobStarter.ResolverForm(obj));

        // And the carrier reads back as the value, which is what makes the
        // road lossless.
        Assert.Equal(array, ConsultInputValue.FromJson(ConsultGenerationJobStarter.ResolverForm(array)));
        Assert.Equal(obj, ConsultInputValue.FromJson(ConsultGenerationJobStarter.ResolverForm(obj)));
    }

    // #424 (v9 layer 4): the declaration can ask for structure now, and the
    // starter holds a supplied value to it — a JSON number for a number, exactly
    // the declared fields for an object, canonical elements for an array. The
    // messages name kinds, ids and indices, never a value.

    private static Dictionary<string, ConsultInputValue> StructuredInputs(
        params (string Id, ConsultInputValue Value)[] overrides)
    {
        var inputs = TypedInputs(
            ("length_of_stay", ConsultInputValue.OfNumber("3")),
            ("patient", Patient(("family_name", ConsultInputValue.OfText("Smith")), ("age", ConsultInputValue.OfNumber("40")))),
            ("prior_notes", ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("First note."), ConsultInputValue.OfText("Second note.") })),
            ("medications", ConsultInputValue.OfArray(new[]
            {
                Patient(("name", ConsultInputValue.OfText("metformin")), ("dose", ConsultInputValue.OfText("500 mg"))),
                Patient(("name", ConsultInputValue.OfText("ramipril")))
            })));

        foreach (var (id, value) in overrides)
        {
            inputs[id] = value;
        }

        return inputs;
    }

    private static ConsultInputValue Patient(params (string Id, ConsultInputValue Value)[] fields) =>
        ConsultInputValue.OfObject(fields.Select(field => new ConsultInputEntry(field.Id, field.Value)));

    [Fact]
    public void V9_StructuredValues_AreAccepted_AndReachTheResolverAsCarriers()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: StructuredInputs()),
            V9Fixtures.Structured());

        Assert.Null(resolution.Error);
        Assert.Equal("3", resolution.Effective!["length_of_stay"]);
        // Structure travels as its carrier (#423); the renderer reconstructs it.
        Assert.Equal("""["First note.","Second note."]""", resolution.Effective["prior_notes"]);
        Assert.Equal("""{"family_name":"Smith","age":40}""", resolution.Effective["patient"]);
        Assert.Equal(StructuredInputs()["medications"], ConsultInputValue.FromJson(resolution.Effective["medications"]));
    }

    public static TheoryData<string, string, string> StructuredRefusals()
    {
        var data = new TheoryData<string, string, string>();

        void Add(string id, ConsultInputValue value, string expected) => data.Add(id, value.AsJson(), expected);

        Add("length_of_stay", ConsultInputValue.OfText("3"), "is a number and must be sent as a JSON number; got text.");
        Add("length_of_stay", ConsultInputValue.OfBoolean(true), "is a number and must be sent as a JSON number; got a boolean.");
        Add("patient", ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>()), "is an object and must be sent as a JSON object; got an array.");
        Add("patient", Patient(("family_name", ConsultInputValue.OfText("x")), ("nickname", ConsultInputValue.OfText("y"))),
            "has a field 'nickname' it does not declare (fields: family_name, age, sex).");
        Add("patient", Patient(("age", ConsultInputValue.OfNumber("40"))), "is missing required field 'family_name'.");
        Add("patient", Patient(("family_name", ConsultInputValue.OfText("x")), ("age", ConsultInputValue.NullElement)),
            "field 'age' is null; omit an optional field instead.");
        Add("patient", Patient(("family_name", ConsultInputValue.OfText("x")), ("age", ConsultInputValue.OfText("40"))),
            "field 'age' is a number and must be sent as a JSON number; got text.");
        Add("patient", Patient(("family_name", ConsultInputValue.OfText("x")), ("sex", ConsultInputValue.OfText("other"))),
            "field 'sex' accepts 'female', 'male'; got 'other'.");
        Add("prior_notes", ConsultInputValue.OfText("one note"), "is an array and must be sent as a JSON array; got text.");
        Add("prior_notes", ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("a"), ConsultInputValue.NullElement }), "element 1 is null.");
        Add("prior_notes", ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("a"), ConsultInputValue.OfNumber("3") }),
            "element 1 is a text and must be sent as a JSON string; got a number.");
        Add("medications", ConsultInputValue.OfArray(new[] { Patient(("name", ConsultInputValue.OfText("x"))), Patient(("dose", ConsultInputValue.OfText("y"))) }),
            "element 1 is missing required field 'name'.");
        Add("medications", ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("metformin") }),
            "element 0 is an object and must be sent as a JSON object; got text.");
        // A v8 scalar slot still refuses structure, as it has since #421.
        Add("seen_on", ConsultInputValue.OfArray(new[] { ConsultInputValue.OfText("2026-08-10") }),
            "is a date and must be sent as a JSON string; got an array.");

        return data;
    }

    [Theory]
    [MemberData(nameof(StructuredRefusals))]
    public void V9_AValueDisagreeingWithTheDeclaration_IsRefusedAndNamesThePlace(string id, string carrier, string expected)
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: StructuredInputs((id, ConsultInputValue.FromJson(carrier)))),
            V9Fixtures.Structured());

        Assert.Equal($"Input '{id}' {expected}", resolution.Error);
    }

    [Fact]
    public void V9_ARequiredArrayWithNoEntries_IsRefusedByName()
    {
        // Present and empty is not absent (v9 § 4): the required check lets it
        // through, and the shape check refuses it naming the slot.
        var manifest = V9Fixtures.WithInput(new WorkflowInputSpec("prior_notes", "Prior notes",
            Required: true, Type: WorkflowInputTypes.Array, Items: WorkflowInputTypes.Text));

        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: StructuredInputs(("prior_notes", ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>())))),
            manifest);

        Assert.Equal("Input 'prior_notes' is required and has no entries.", resolution.Error);
    }

    [Fact]
    public void V9_AnOptionalArrayWithNoEntries_IsAccepted()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: StructuredInputs(("prior_notes", ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>())))),
            V9Fixtures.Structured());

        Assert.Null(resolution.Error);
        Assert.Equal("[]", resolution.Effective!["prior_notes"]);
    }

    [Fact]
    public void V8_UntypedInputs_BehaveExactlyAsV7()
    {
        // The minimal-v8 migration: same declaration, same acceptance.
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest("Draft."), V8Fixtures.Minimal());

        Assert.Null(resolution.Error);
        Assert.Equal(new Dictionary<string, ConsultInputValue> { ["consult_draft"] = "Draft." }, resolution.Supplied);
    }

    [Fact]
    public void V7_MissingRequiredInput_IsRejected()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue> { ["prior_notes"] = "Old notes." }),
            V7Fixtures.MultiDeliverable());

        Assert.Contains("Required input(s) 'consult_draft' missing", resolution.Error);
    }

    [Fact]
    public void V7_UnknownInput_IsRejectedListingTheDeclaration()
    {
        var resolution = ConsultGenerationJobStarter.ResolveEffectiveInputs(
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>
            {
                ["consult_draft"] = "Draft.",
                ["labs"] = "CBC normal."
            }),
            V7Fixtures.MultiDeliverable());

        Assert.Contains("Unknown input(s) 'labs'", resolution.Error);
        Assert.Contains("declared: consult_draft, prior_notes", resolution.Error);
    }
}

/// <summary>Loads the real bundled catalog once for starter tests.</summary>
file static class TestCatalog
{
    public static readonly Consultologist.Api.Agents.OutputContractCatalog Instance = Load();

    private static Consultologist.Api.Agents.OutputContractCatalog Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Consultologist.sln")))
        {
            dir = dir.Parent;
        }

        return Consultologist.Api.Agents.OutputContractCatalog.Load(
            Path.Combine(dir!.FullName, "external", "consultologist-agents", "agents"));
    }


}
