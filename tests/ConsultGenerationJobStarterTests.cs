using System.Text;
using Consultologist.Api.Documents;
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

namespace Consultologist.Api.Tests;

public class ConsultGenerationJobStarterTests
{
    private readonly IWorkflowPackageStore _packageStore = Substitute.For<IWorkflowPackageStore>();
    private readonly IWorkflowPackagePinResolver _pinResolver = Substitute.For<IWorkflowPackagePinResolver>();
    private readonly IAccountRateLimiter _rateLimiter = Substitute.For<IAccountRateLimiter>();
    private readonly DurableTaskClient _client = Substitute.For<DurableTaskClient>("test");
    private readonly DurableEntityClient _entities = Substitute.For<DurableEntityClient>("test");

    // #290: a terse but genuine referral. These fixtures used to say
    // "draft", which is not a referral and which the content floor
    // correctly refuses — the tests were asserting behaviour against
    // input no clinician would send.
    private const string Referral =
        "65M, newly diagnosed adenocarcinoma of the lung, stage IIIA, for consideration of chemoradiation. PMHx HTN.";

    private ConsultGenerationJobStarter CreateStarter(ILogger<ConsultGenerationJobStarter>? logger = null)
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
            _rateLimiter);
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
                V8Fixtures.Typed() with { SpecVersion = 9 },
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

    private async Task<(ConsultGenerationJobStartOutcome Outcome, ConsultGenerationJobInitialize? Initialize, ConsultGenerationOrchestrationInput? Input)>
        StartFannedAsync(WorkflowPackageManifest manifest, ConsultInputValue? priorNotes, string fannedId = "prior_notes")
    {
        _pinResolver.ResolvePinAsync("user-1", Arg.Any<CancellationToken>())
            .Returns(new WorkflowPackageRef("general", "latest"));
        _packageStore.ResolveAsync(Arg.Any<WorkflowPackageRef>(), Arg.Any<CancellationToken>())
            .Returns(ExecutableV7Package(
                manifest,
                new List<WorkflowResolvedResult> { new("consult", "assemble-note", "Consultation note") }));

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

        var outcome = await CreateStarter().StartAsync(
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
    public async Task AnEmptyFan_IsRefusedAtStart_NamingTheInput()
    {
        // No items, no blocks, no document: v8's empty fire set in different
        // clothes, refused the same way and through the same enum value.
        var (outcome, initialize, _) = await StartFannedAsync(V9Fixtures.Fanned(),
            ConsultInputValue.OfArray(Array.Empty<ConsultInputValue>()));

        // A required array with no entries is refused as the wrong shape first
        // (#424); the fan's own refusal needs the optional declaration.
        Assert.NotNull(outcome.Error);
        Assert.Null(outcome.JobId);
        Assert.Null(initialize);

        var manifest = V9Fixtures.Fanned();
        manifest = manifest with
        {
            Inputs = manifest.Inputs!.Select(i => i.Id == "prior_notes" ? i with { Required = false } : i).ToList()
        };
        var (absent, absentInit, _) = await StartFannedAsync(manifest, priorNotes: null);

        Assert.Equal(ConsultGenerationJobStartError.NoApplicableDeliverable, absent.Error);
        Assert.Null(absent.JobId);
        Assert.Null(absentInit);
        Assert.Equal(
            "No document applies to these inputs. 'Prior notes' has no entries, and every document this package produces is written from them.",
            absent.ErrorDetail);
        Assert.Equal(absent.ErrorDetail, absent.SenderSafeDetail);
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

        var outcome = await CreateStarter().StartAsync(
            _client,
            new ConsultGenerationRequest(null, Inputs: new Dictionary<string, ConsultInputValue>(StringComparer.Ordinal)
            {
                ["consult_draft"] = Referral,
                ["seen_on"] = "2026-08-10",
                ["encounter_kind"] = "follow_up"
            }),
            "user-1",
            new ConsultGenerationJobOrigin(ConsultGenerationJobSources.App),
            CancellationToken.None);

        Assert.Equal(ConsultGenerationJobStartError.NoApplicableDeliverable, outcome.Error);
        Assert.Null(outcome.JobId);
        Assert.Contains("Consultation note", outcome.ErrorDetail);
        Assert.Contains("Patient letter", outcome.ErrorDetail);
        Assert.Contains("not supplied", outcome.ErrorDetail);

        // #369: and it may be quoted back to whoever sent it. Labels and
        // condition literals are authored package content; the supplied-value
        // branch of Explain can only ever print a declared enum value or a
        // boolean, because anything else was refused before conditions ran.
        Assert.Equal(outcome.ErrorDetail, outcome.SenderSafeDetail);
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
    private void WithTypedPackage(string? title = null)
    {
        // #432: a title rides on a v9 manifest; the typed v8 shape otherwise.
        var manifest = title is null
            ? V8Fixtures.Typed()
            : V8Fixtures.Typed() with { SpecVersion = 9, Title = title };
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
