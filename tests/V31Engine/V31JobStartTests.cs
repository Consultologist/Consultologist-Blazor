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
using Consultologist.Api.Tests.V31Templates;
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
        new(() => V31Job.Factory().Current());

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

    [Theory]
    [InlineData(null)]
    [InlineData("31")]
    public async Task A30Start_NeverReadsThe31EnginesCapabilities(string? preview)
    {
        // #1505 R15 (the final review): the 31 store's engine capabilities hold the 31 agents catalogue; a 5-30 start, with
        // the preview unset or admitting 31, never builds them, so a defect in agents/v31 cannot fail it
        var touched = 0;
        var capabilities = new Lazy<EngineCapabilities>(() =>
        {
            Interlocked.Increment(ref touched);
            throw new InvalidOperationException("The 31 agents catalogue was read.");
        });
        var package = ByteEquivalenceFixtures.Load("example-prior-letters");
        var inputs = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = ConsultInputValue.OfText(Draft),
            ["prior_letters"] = ConsultInputValue.OfArray([ConsultInputValue.OfText("Dear Dr A. Sample, Margaret Okafor was seen in clinic. (Fictional.)")]),
        };

        var start = new Start(preview, package30: package, engineCapabilities: capabilities);
        var outcome = await start.RunAsync(PackageRefOf(package), inputs);

        Assert.Null(outcome.Error);
        Assert.Equal(nameof(ConsultGenerationOrchestrator), Assert.Single(start.Scheduled).Name);
        Assert.Equal(0, touched);
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

    // ---- MacroChoices (#1504, ruled by the owner on #1504, ruling 6; part 05 § 8.2) ----------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMacroChoice_IsItsBooleanInputsValue(bool choice)
    {
        var start = new Start(preview: "31");
        var packageRef = start.PutChoices();

        var outcome = await start.RunAsync(WithChoices(packageRef, Text(Draft), ("letrozole", choice)));

        Assert.Null(outcome.Error);
        var held = Assert.Single(start.Inputs.Documents).Value;
        Assert.Equal($$"""{"value":{{(choice ? "true" : "false")}},"content":null,"title":null}""", held["inputs"]!["letrozole"]!.ToJsonString());

        // the run's scope holds input.letrozole.value as chosen, and the macro whose if reads it is decided by it
        var plan = RunFixture.Plan(Choices());
        var inputs = new RunInputs(
            held["inputs"]!.AsObject().ToDictionary(p => p.Key, p => p.Value?.DeepClone(), StringComparer.Ordinal),
            new JsonObject(),
            new Dictionary<string, string?>(StringComparer.Ordinal));
        var (_, host, recorder) = await PlacementDecisionsTests.RunAsync(plan, PlacementDecisionsTests.Host(plan, _ => ComposingRunHost.Judgement(true)), inputs);
        Assert.Equal([("macro.letrozole", choice)], PlacementDecisionsTests.Placements(recorder, "node.draft").Select(d => (d.Site, d.Result)));
        Assert.Equal(choice, host.Calls.Single(c => c.Key == "node.draft").Placed!["macro.letrozole"]);
    }

    [Fact]
    public async Task AMacroChoiceAgreeingWithTheInputSent_IsAccepted()
    {
        var start = new Start(preview: "31");
        var packageRef = start.PutChoices();
        var inputs = Text(Draft);
        inputs["letrozole"] = ConsultInputValue.OfBoolean(true);

        var outcome = await start.RunAsync(WithChoices(packageRef, inputs, ("letrozole", true)));

        Assert.Null(outcome.Error);
        Assert.Equal(true, (bool?)Assert.Single(start.Inputs.Documents).Value["inputs"]!["letrozole"]!["value"]);
    }

    [Theory]
    [InlineData("tamoxifen")]
    [InlineData("consult_draft")]
    public async Task AMacroChoiceNamingNoBooleanInput_IsRefused(string id)
    {
        // an id the package does not declare, and a declared input that is text
        var start = new Start(preview: "31");
        var packageRef = start.PutChoices();

        var outcome = await start.RunAsync(WithChoices(packageRef, Text(Draft), (id, true)));

        var sentence = $"MacroChoices names '{id}', which is not a boolean input of the package.";
        V31Contract.AssertSentence(new SourceRefusal("A07-015", sentence, ""));
        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Equal(sentence, outcome.ErrorDetail);
        Assert.Null(outcome.SenderSafeDetail); // it quotes the caller's own id, as A06-013 does
        Assert.Empty(start.Scheduled);
        Assert.Empty(start.Inputs.Documents);
        AssertStartFailure(start, outcome.JobId!, sentence);
    }

    [Fact]
    public async Task AMacroChoiceAgainstTheSameInputSentDifferently_IsRefused()
    {
        var start = new Start(preview: "31");
        var packageRef = start.PutChoices();
        var inputs = Text(Draft);
        inputs["letrozole"] = ConsultInputValue.OfBoolean(false);

        var outcome = await start.RunAsync(WithChoices(packageRef, inputs, ("letrozole", true)));

        var sentence = "Input 'letrozole' is given one value in Inputs and another in MacroChoices; send it once.";
        V31Contract.AssertSentence(new SourceRefusal("A07-016", sentence, ""));
        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Equal(sentence, outcome.ErrorDetail);
        Assert.Equal(sentence, outcome.SenderSafeDetail);
        Assert.Empty(start.Scheduled);
        AssertStartFailure(start, outcome.JobId!, sentence);
    }

    // ---- the signature block (#1505, area 08; part 06 § 10.3; part 08 § 3; D15) -------------------------------

    [Fact]
    public async Task TheChosenBlock_IsTheDefault()
    {
        var start = new Start(preview: "31", signatures: Blocks());
        var packageRef = start.PutSigned();

        var outcome = await start.RunAsync(packageRef, Text(Draft));

        Assert.Null(outcome.Error);
        Assert.Equal(ClinicBlock, (string?)Assert.Single(start.Inputs.Documents).Value["profile"]!["signature"]);
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload);
        Assert.Equal(("sig-1", "default"), (initialize.SignatureBlockId, initialize.SignatureChosen));

        // the entity keeps them, written once at Initialize
        var entity = Entity();
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(initialize));
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(
            initialize with { SignatureBlockId = "sig-2", SignatureChosen = "job" }));
        var state = (ConsultGenerationJobState)StateProperty.GetValue(entity)!;
        Assert.Equal(("sig-1", "default"), (state.SignatureBlockId, state.SignatureChosen));

        // the block's text is in no signal, no orchestration input
        Assert.DoesNotContain("Sample Oncology Clinic", JsonSerializer.Serialize(initialize), StringComparison.Ordinal);
        Assert.DoesNotContain("Sample Oncology Clinic", JsonSerializer.Serialize(Assert.Single(start.Scheduled).Input), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlockNamedByTheRequest_IsThisJobs()
    {
        var start = new Start(preview: "31", signatures: Blocks());
        var packageRef = start.PutSigned();

        var outcome = await start.RunAsync(Signed(packageRef, "sig-2"));

        Assert.Null(outcome.Error);
        Assert.Equal(WardBlock, (string?)Assert.Single(start.Inputs.Documents).Value["profile"]!["signature"]);
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload);
        Assert.Equal(("sig-2", "job"), (initialize.SignatureBlockId, initialize.SignatureChosen));
    }

    [Fact]
    public async Task NamingTheChosenBlock_IsStillTheDefault()
    {
        var start = new Start(preview: "31", signatures: Blocks());
        var packageRef = start.PutSigned();

        var outcome = await start.RunAsync(Signed(packageRef, "sig-1"));

        Assert.Null(outcome.Error);
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload);
        Assert.Equal(("sig-1", "default"), (initialize.SignatureBlockId, initialize.SignatureChosen));
    }

    [Fact]
    public async Task ABlockThatIsNotTheAccounts_RefusesTheStart()
    {
        var start = new Start(preview: "31", signatures: Blocks());
        var packageRef = start.PutSigned();

        var outcome = await start.RunAsync(Signed(packageRef, "sig-9"));

        var sentence = "The signature chosen for this job isn't in your profile. Choose one in Profile.";
        Assert.Equal(sentence, V31StartChecks.SignatureNotInProfile);
        V31Contract.AssertSentence(new SourceRefusal("A08-004", sentence, ""));
        Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
        Assert.Equal(sentence, outcome.ErrorDetail); // and not S06-031 as well: the account has a signature
        Assert.Equal(sentence, outcome.SenderSafeDetail);
        Assert.Empty(start.Scheduled);
        Assert.Empty(start.Inputs.Documents);
        var failure = AssertStartFailure(start, outcome.JobId!, sentence);
        Assert.Equal(((string?)null, (string?)null), (failure.Initialize.SignatureBlockId, failure.Initialize.SignatureChosen));
    }

    [Fact]
    public async Task ABlankBlock_CountsAsNotSet()
    {
        var signature = ResolveSentences.SignatureNotSet();
        foreach (var (signatures, requested) in new[] { (Blocks(chosen: "sig-3"), (string?)null), (Blocks(), "sig-3"), (Blocks(chosen: null), null) })
        {
            var start = new Start(preview: "31", signatures: signatures);
            var packageRef = start.PutSigned();

            var outcome = await start.RunAsync(Signed(packageRef, requested));

            V31Contract.AssertSentence(signature);
            Assert.Equal(ConsultGenerationJobStartError.InputsMismatch, outcome.Error);
            Assert.Equal(signature.Sentence, outcome.ErrorDetail);
            Assert.Equal(signature.Sentence, outcome.SenderSafeDetail);
            Assert.Empty(start.Scheduled);
            AssertStartFailure(start, outcome.JobId!, signature.Sentence);
        }
    }

    [Fact]
    public async Task APackageThatReadsNoSignature_RecordsNeither()
    {
        // the request's block is not even the account's: a package that reads no signature does not look
        var start = new Start(preview: "31", signatures: Blocks());
        var packageRef = start.Put("nested-note");

        var outcome = await start.RunAsync(Signed(packageRef, "sig-9"));

        Assert.Null(outcome.Error);
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload);
        Assert.Equal(((string?)null, (string?)null), (initialize.SignatureBlockId, initialize.SignatureChosen));
        Assert.DoesNotContain("Signature", JsonSerializer.Serialize(initialize), StringComparison.Ordinal);

        var entity = Entity();
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(initialize));
        var state = (ConsultGenerationJobState)StateProperty.GetValue(entity)!;
        Assert.Equal(((string?)null, (string?)null), (state.SignatureBlockId, state.SignatureChosen));
        Assert.DoesNotContain("Signature", JsonSerializer.Serialize(state), StringComparison.Ordinal);

        // a state stored before this change (no members) reads both as null
        var stored = JsonSerializer.Deserialize<ConsultGenerationJobState>("""{"JobId":"job-1","PackageSpecVersion":31}""")!;
        Assert.Equal(((string?)null, (string?)null), (stored.SignatureBlockId, stored.SignatureChosen));
    }

    [Fact]
    public async Task A30Start_IgnoresTheMember()
    {
        var package = ByteEquivalenceFixtures.Load("example-prior-letters");
        var inputs = new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
        {
            ["consult_draft"] = ConsultInputValue.OfText(Draft),
            ["prior_letters"] = ConsultInputValue.OfArray([ConsultInputValue.OfText("Dear Dr A. Sample, Margaret Okafor was seen in clinic. (Fictional.)")]),
        };

        var without = new Start(preview: "31", package30: package, signatures: Blocks());
        var withoutOutcome = await without.RunAsync(new ConsultGenerationRequest(null, package.Ref, Inputs: inputs));
        var with = new Start(preview: "31", package30: package, signatures: Blocks());
        var withOutcome = await with.RunAsync(new ConsultGenerationRequest(null, package.Ref, Inputs: inputs, SignatureBlockId: "sig-9"));

        Assert.Null(withoutOutcome.Error);
        Assert.Null(withOutcome.Error);
        Assert.Equal(nameof(ConsultGenerationOrchestrator), Assert.Single(with.Scheduled).Name);
        Assert.Equal(Masked(without, withoutOutcome.JobId!), Masked(with, withOutcome.JobId!));
        Assert.DoesNotContain("sig-9", Masked(with, withOutcome.JobId!), StringComparison.Ordinal);
    }

    // ---- no deliverable can exist (#1505, area 08; R8) ---------------------------------------------------------

    [Fact]
    public async Task AStart_WhereNoDeliverableCanExist_IsRefused()
    {
        // the note's if reads an input only, and is false (the input sent false) or null (left out): 30's refusal and words
        foreach (var billing in new bool?[] { false, null })
        {
            var start = new Start(preview: "31");
            var packageRef = start.PutGated(Billed);
            var inputs = Text(Draft);
            if (billing is { } sent)
                inputs["include_billing"] = ConsultInputValue.OfBoolean(sent);

            var outcome = await start.RunAsync(packageRef, inputs);

            var detail = "No document applies to these inputs. 'Consultation note' skipped: input.include_billing.value = true is false.";
            Assert.Equal(ConsultGenerationJobStartError.NoApplicableDeliverable, outcome.Error);
            Assert.Equal(detail, outcome.ErrorDetail);
            Assert.Equal(detail, outcome.SenderSafeDetail);
            Assert.NotNull(outcome.JobId);
            Assert.Empty(start.Scheduled);
            Assert.Empty(start.Inputs.Documents);
            var failure = AssertStartFailure(start, outcome.JobId!, detail);
            var skipped = Assert.Single(failure.Initialize.SkippedDocuments!);
            Assert.Equal(("deliverable.note", "Consultation note"), (skipped.ResultId, skipped.Label));
        }

        // and with the input sent true, it starts
        var billed = new Start(preview: "31");
        var billedInputs = Text(Draft);
        billedInputs["include_billing"] = ConsultInputValue.OfBoolean(true);
        Assert.Null((await billed.RunAsync(billed.PutGated(Billed), billedInputs)).Error);
        Assert.Single(billed.Scheduled);
    }

    [Fact]
    public async Task AStart_WhereNoDeliverableCanExist_WithADeliverableWhoseIfReadsANode_Starts()
    {
        // a node's value is not known at the start (read now, it would be null and the if false): the note may exist, so the run decides
        var start = new Start(preview: "31");
        var packageRef = start.PutGated("{ \"!\": [{ \"blank\": [{ \"var\": \"node.plan.value\" }] }] }");

        var outcome = await start.RunAsync(packageRef, Text(Draft));

        Assert.Null(outcome.Error);
        Assert.Equal(nameof(ConsultGenerationOrchestratorV31), Assert.Single(start.Scheduled).Name);
    }

    [Fact]
    public async Task AStart_WhereNoDeliverableCanExist_WithAnUnconditionalDeliverable_Starts()
    {
        var start = new Start(preview: "31");
        var packageRef = start.PutGated(Billed, unconditional: true);

        var outcome = await start.RunAsync(packageRef, Text(Draft));

        Assert.Null(outcome.Error);
        Assert.Equal(nameof(ConsultGenerationOrchestratorV31), Assert.Single(start.Scheduled).Name);
    }

    // ---- the header of a 31 job (#1506, area 09; part 08 §§ 1, 3, 3.1, 13; D14, D15) ----------------------------

    [Fact]
    public async Task A31Job_IsStorage12_FromItsCreation()
    {
        // the start
        var started = Entity();
        await started.Initialize(new ConsultGenerationJobInitialize("job-31", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), PackageSpecVersion: 31));
        Assert.Equal(12, StateOf(started).SchemaVersion);

        // a start failure
        var refused = Entity();
        await refused.RecordStartFailure(new ConsultGenerationJobStartFailure(
            new ConsultGenerationJobInitialize("job-31", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), PackageSpecVersion: 31), V31StartChecks.NoTimeZone));
        Assert.Equal(12, StateOf(refused).SchemaVersion);

        // a decision failure
        var undecided = Entity();
        await undecided.Initialize(new ConsultGenerationJobInitialize("job-31", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), PackageSpecVersion: 31));
        await undecided.RecordDecisionFailure(new ConsultGenerationDecisionFailure(ConsultGenerationDecisionFailureKinds.NothingApplied, "Nothing applied.", null));
        Assert.Equal(12, StateOf(undecided).SchemaVersion);

        Assert.Equal(12, ConsultGenerationJobState.StorageVersions[^1]);
    }

    [Fact]
    public async Task A30Job_IsStill11()
    {
        var started = Entity();
        await started.Initialize(new ConsultGenerationJobInitialize("job-30", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), PackageSpecVersion: 30));
        Assert.Equal(11, StateOf(started).SchemaVersion);

        var refused = Entity();
        await refused.RecordStartFailure(new ConsultGenerationJobStartFailure(
            new ConsultGenerationJobInitialize("job-30", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), PackageSpecVersion: 30), "No document applies."));
        Assert.Equal(11, StateOf(refused).SchemaVersion);
    }

    [Fact]
    public async Task TheStart_HashesItsInputs()
    {
        async Task<ConsultGenerationJobInitialize> StartWith(string draft)
        {
            var start = new Start(preview: "31");
            var outcome = await start.RunAsync(start.Put("nested-note"), Text(draft));
            Assert.Null(outcome.Error);
            return Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload);
        }

        var first = await StartWith(Draft);
        var again = await StartWith(Draft);
        var other = await StartWith(Draft + " Seen with her daughter.");

        // hash-definitions.md § 7.2: the canonical JSON object keyed by input id, each the value of its read form
        Assert.Equal(7, first.EffectiveInputHashVersion);
        Assert.Equal(CanonicalJson.Hash(new JsonObject { ["consult_draft"] = Draft }), first.EffectiveInputHash);
        Assert.StartsWith("sha256:", first.EffectiveInputHash, StringComparison.Ordinal);
        Assert.Equal(first.EffectiveInputHash, again.EffectiveInputHash);
        Assert.NotEqual(first.EffectiveInputHash, other.EffectiveInputHash);

        // and each input's own hash, for the header's inputs
        Assert.Equal(CanonicalJson.Hash(JsonValue.Create(Draft)), first.InputHashes!["consult_draft"]);
        Assert.DoesNotContain("Okafor", JsonSerializer.Serialize(first), StringComparison.Ordinal);

        // on the entity, write-once
        var entity = Entity();
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(first));
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(other));
        Assert.Equal((first.EffectiveInputHash, (int?)7), (StateOf(entity).EffectiveInputHash, StateOf(entity).EffectiveInputHashVersion));
    }

    [Fact]
    public async Task ADocumentInput_RecordsItsExtraction()
    {
        var start = new Start(preview: "31");
        var packageRef = start.Put("nested-note");
        var request = new ConsultGenerationRequest(
            null,
            RefText(packageRef),
            InputFiles: new Dictionary<string, List<InputFilePayload>>
            {
                ["consult_draft"] = [new InputFilePayload("application/pdf", DocumentExtractionTests.TextPdf(Draft))],
            });

        var outcome = await start.RunAsync(request);

        Assert.Null(outcome.Error);
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload);
        var origin = Assert.Single(initialize.InputDocumentOrigins!["consult_draft"]);
        Assert.StartsWith("pdfpig/", origin.Extractor, StringComparison.Ordinal);
        Assert.Equal("PdfPig", origin.Parser!.Name);
        Assert.Matches(@"^[0-9]+\.[0-9]+\.[0-9]+$", origin.Parser.Version);
        Assert.Null(origin.Converter);
        Assert.Null(origin.Ocr);

        var entity = Entity();
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(initialize));
        var input = Assert.Single(StateOf(entity).ToRecordHeader()["inputs"]!.AsArray())!.AsObject();
        Assert.Equal("consult_draft", (string?)input["id"]);
        Assert.Equal("document", (string?)input["origin"]);
        Assert.Equal("document", (string?)input["kind"]);
        // #1506 (final review F4): part 08 § 3.1's extractor, as the extraction named it
        Assert.Equal(origin.Extractor, (string?)input["extractor"]);
        Assert.Equal(
            ["id", "origin", "hash", "kind", "extractor", "pageCount", "fileSha256", "textSha256", "converter", "parser", "ocr"],
            input.Select(m => m.Key));
        Assert.Equal(1, (int?)input["pageCount"]);
        Assert.Equal(origin.FileSha256, (string?)input["fileSha256"]);
        Assert.Equal(origin.TextSha256, (string?)input["textSha256"]);
        Assert.Equal("PdfPig", (string?)input["parser"]!["name"]);
        Assert.Equal(origin.Parser.Version, (string?)input["parser"]!["version"]);
        Assert.True(input.ContainsKey("converter") && input["converter"] is null);
        Assert.True(input.ContainsKey("ocr") && input["ocr"] is null);

        // a typed input: origin typed, and none of the three
        var typed = new Start(preview: "31");
        Assert.Null((await typed.RunAsync(typed.Put("nested-note"), Text(Draft))).Error);
        var typedEntity = Entity();
        await typedEntity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(
            Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(typed.Signals).Payload)));
        var typedInput = Assert.Single(StateOf(typedEntity).ToRecordHeader()["inputs"]!.AsArray())!.AsObject();
        Assert.Equal(["id", "origin", "hash"], typedInput.Select(m => m.Key));
        Assert.Equal("typed", (string?)typedInput["origin"]);
        Assert.Equal(CanonicalJson.Hash(JsonValue.Create(Draft)), (string?)typedInput["hash"]);
    }

    [Fact]
    public void ADocumentInput_At30_RecordsItsOriginAsBefore()
    {
        // the 5–30 path's origin is written as it was: none of the 31 members
        var origin = new ConsultInputOrigin(ConsultInputOriginKinds.Document, "pdfpig/0.1.15", 1, false, FileSha256: "aa", TextSha256: "bb");
        Assert.Equal(
            """{"Kind":"document","Extractor":"pdfpig/0.1.15","PageCount":1,"TrackedChangesResolved":false,"FileSha256":"aa","TextSha256":"bb","SourceJobId":null,"SourceResultId":null,"SourceFormId":null,"SourceResponseId":null}""",
            JsonSerializer.Serialize(origin));
    }

    [Fact]
    public async Task A31Job_WritesNothingRetiredAt12()
    {
        // part 08 § 3: macroChoices, excludedMacros and classifications are retired at 12, as is the signature snapshot
        var start = new Start(preview: "31", signatures: Blocks());
        var packageRef = start.PutChoices();

        var outcome = await start.RunAsync(WithChoices(packageRef, Text(Draft), ("letrozole", true)));

        Assert.Null(outcome.Error);
        var entity = Entity();
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(
            Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload)));
        var state = JsonNode.Parse(JsonSerializer.Serialize(StateOf(entity)))!.AsObject();
        Assert.Equal(12, (int?)state["SchemaVersion"]);
        foreach (var retired in new[] { "MacroChoices", "ExcludedMacros", "Classifications", "Signature" })
            Assert.Null(state[retired]);
        Assert.DoesNotContain("AsOf", state.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Sample Oncology Clinic", state.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToRecordHeader_HasEveryMember_AndNoText()
    {
        var start = new Start(preview: "31", signatures: Blocks());
        var outcome = await start.RunAsync(start.PutSigned(), Text(Draft));
        Assert.Null(outcome.Error);

        var entity = Entity();
        await entity.Initialize(FakeOrchestrationContext.RoundTrip<ConsultGenerationJobInitialize>(
            Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload)));
        var state = StateOf(entity);
        var limits = new RunLimits().ToJson().ToDictionary(m => m.Key, m => (int)m.Value!, StringComparer.Ordinal);
        entity.RecordV31Header(new ConsultGenerationV31Header(state.ResolveHash!, state.LockHash!, limits, new DateTimeOffset(2026, 11, 17, 14, 0, 0, TimeSpan.Zero)));
        entity.RecordV31Header(new ConsultGenerationV31Header(state.ResolveHash!, state.LockHash!, limits, new DateTimeOffset(2026, 11, 17, 14, 0, 0, TimeSpan.Zero),
            new JsonObject { ["culture"] = "invariant" }, new JsonObject()));

        var header = state.ToRecordHeader();

        Assert.Equal(
            [
                "schemaVersion", "packageSpecVersion", "workflowPackage", "packageFormatRef", "provenanceRef", "formatsRef", "catalogRef",
                "engineCommit", "engineRelease", "apiHost", "terminology", "jobId", "status", "error", "createdAtUtc", "startedAtUtc", "completedAtUtc",
                "resolveHash", "lockHash", "versionsRan", "runFacts", "limits", "signatureBlockId", "signatureChosen",
                "effectiveInputHash", "effectiveInputHashVersion", "inputs", "tokens",
            ],
            header.Select(m => m.Key));
        Assert.Equal(12, (int?)header["schemaVersion"]);
        Assert.Equal(31, (int?)header["packageSpecVersion"]);
        Assert.Equal(outcome.JobId, (string?)header["jobId"]);
        Assert.Equal("2026-11-17T14:00:00.0000000+00:00", (string?)header["startedAtUtc"]);
        Assert.Null(header["completedAtUtc"]);
        Assert.Null(header["error"]);
        Assert.Null(header["tokens"]); // no model call yet
        // #1506 (final review M3): a 31 job's start reads no terminology server, so its snapshot is null, never absent
        Assert.True(header.ContainsKey("terminology") && header["terminology"] is null);
        Assert.Equal(10, header["limits"]!.AsObject().Count);
        Assert.Equal("invariant", (string?)header["versionsRan"]!["culture"]);
        Assert.Equal(("sig-1", "default"), ((string?)header["signatureBlockId"], (string?)header["signatureChosen"]));
        Assert.Equal(7, (int?)header["effectiveInputHashVersion"]);

        var text = header.ToJsonString();
        foreach (var absent in new[] { "\"history\"", "\"recordHash\"", "\"textDroppedAtUtc\"", "Okafor", "Sample Oncology Clinic" })
            Assert.DoesNotContain(absent, text, StringComparison.Ordinal);

        // #1506 (final review M2): the job's token sums, once its rows had some: { input, output, total }
        entity.RecordV31Rows(new ConsultGenerationV31Rows([], new ConsultGenerationV31Tokens(120, 30, 150)));
        entity.RecordV31Rows(new ConsultGenerationV31Rows([], new ConsultGenerationV31Tokens(10, 5, 15)));
        Assert.Equal("""{"input":130,"output":35,"total":165}""", StateOf(entity).ToRecordHeader()["tokens"]!.ToJsonString());
    }

    private static ConsultGenerationJobState StateOf(ConsultGenerationJobEntity entity) => (ConsultGenerationJobState)StateProperty.GetValue(entity)!;

    private const string Billed = "{ \"===\": [{ \"var\": \"input.include_billing.value\" }, true] }";

    private const string ClinicBlock = "Dr A. Sample\nSample Oncology Clinic";

    private const string WardBlock = "Dr A. Sample\nWard 4, Sample Oncology Clinic";

    /// <summary>The account's signature blocks as the Web stores them: <c>sig-1</c> (the clinic), <c>sig-2</c> (the ward), <c>sig-3</c> (blank), <paramref name="chosen"/> chosen.</summary>
    private static string Blocks(string? chosen = "sig-1") => JsonSerializer.Serialize(new SignatureBlocks.SignatureBlockSet(
        [
            new("sig-1", "Clinic", ClinicBlock, DateTimeOffset.UnixEpoch),
            new("sig-2", "Ward", WardBlock, DateTimeOffset.UnixEpoch),
            new("sig-3", "Blank", "  \n ", DateTimeOffset.UnixEpoch),
        ],
        chosen));

    private static ConsultGenerationRequest Signed(WorkflowPackageRef packageRef, string? block) =>
        new(null, RefText(packageRef), Inputs: Text(Draft), SignatureBlockId: block);

    /// <summary>
    /// The optional boolean input <c>letrozole</c> (the per-run choice of the macro <c>letrozole</c>, part 05 § 8.2), the
    /// macro decided on it, and the agent <c>draft</c> placing it.
    /// </summary>
    internal static Dictionary<string, byte[]> Choices() =>
        TemplateFixture.Package()
            .WithJson("manifest.json", m => m["inputs"]!["letrozole"] = JsonNode.Parse(
                "{ \"label\": \"On letrozole\", \"description\": null, \"format\": \"boolean\", \"required\": false, \"channel\": null }"))
            .Listed("macros/index.json", "letrozole")
            .With("macros/letrozole.md", SourceFixture.Md(
                "{ \"label\": \"On letrozole\", \"description\": null, "
                + "\"if\": { \"===\": [{ \"var\": \"input.letrozole.value\" }, true] }, \"format\": \"plaintext\", "
                + "\"instruction\": [\"Place {{ macro.letrozole.content }} at the end of the plan.\"] }",
                "Margaret Okafor continues letrozole 2.5 mg daily.\n"))
            .WithAgent("draft", "Draft the plan for Margaret Okafor.\n{{ macro.letrozole }}\n");

    private static ConsultGenerationRequest WithChoices(
        WorkflowPackageRef packageRef, Dictionary<string, ConsultInputValue> inputs, params (string Id, bool Choice)[] choices) =>
        new(null, RefText(packageRef), Inputs: inputs,
            MacroChoices: choices.ToDictionary(c => c.Id, c => c.Choice, StringComparer.Ordinal));

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

        public Start(string? preview, string? timeZone = Zone, WorkflowPackage? package30 = null, string? signatures = null, Lazy<EngineCapabilities>? engineCapabilities = null)
        {
            var settings = Substitute.For<IAccountSettingsStore>();
            if (timeZone != null)
            {
                settings.GetAsync(Arg.Any<string>(), AccountSettingKeys.TimeZone, Arg.Any<CancellationToken>())
                    .Returns(new AccountSetting(AccountSettingKeys.TimeZone, timeZone, "text/plain", DateTimeOffset.UnixEpoch));
            }

            if (signatures != null)
            {
                settings.GetAsync(Arg.Any<string>(), AccountSettingKeys.ProfileSignatures, Arg.Any<CancellationToken>())
                    .Returns(new AccountSetting(AccountSettingKeys.ProfileSignatures, signatures, "application/json", DateTimeOffset.UnixEpoch));
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
            Store = new CountingStore(new CompiledPackageStore(Registry, Registry, engineCapabilities ?? ThisEngine, admission, limits));
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

        /// <summary>Publishes <see cref="Choices"/>.</summary>
        public WorkflowPackageRef PutChoices() => Publish(Choices());

        /// <summary>Publishes <see cref="SourceFixture.Minimal"/> with its note signed: it reads <c>profile.signature</c>.</summary>
        public WorkflowPackageRef PutSigned() => Publish(SourceFixture.Minimal().With("deliverables/note.md", SourceFixture.Md(
            SourceFixture.Deliverable("Consultation note", TemplateFixture.Plain, "null", "hold"),
            "{{ node.plan.content }}\n\n{{ profile.signature }}\n")));

        /// <summary>
        /// Publishes <see cref="SourceFixture.Minimal"/> with the optional boolean input <c>include_billing</c> and its note's
        /// <c>if</c> <paramref name="condition"/>; with <paramref name="unconditional"/>, a second deliverable <c>letter</c> with none.
        /// </summary>
        public WorkflowPackageRef PutGated(string condition, bool unconditional = false)
        {
            var files = SourceFixture.Minimal()
                .WithJson("manifest.json", m => m["inputs"]!["include_billing"] = JsonNode.Parse(
                    "{ \"label\": \"Include the billing section\", \"description\": null, \"format\": \"boolean\", \"required\": false, \"channel\": null }"))
                .WithJson("deliverables/note.md", h => h["if"] = JsonNode.Parse(condition));
            if (unconditional)
            {
                files = files.Listed("deliverables/index.json", "letter").With("deliverables/letter.md", SourceFixture.Md(
                    SourceFixture.Deliverable("Letter to the GP", TemplateFixture.Plain, "null", "hold"), "{{ node.plan.content }}\n"));
            }

            return Publish(files);
        }

        private WorkflowPackageRef Publish(Dictionary<string, byte[]> files)
        {
            var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
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
