using System.Text.Json.Nodes;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.V31;
using Consultologist.PackageFormat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Consultologist.Api.Tests.V31Engine;

/// <summary>
/// #1509 (specVersion 31, area 12; D8): a 31 job's held inputs are its run-inputs document (<c>{ inputs, profile }</c>), not a
/// 5–30 job's <see cref="JobInputsPayload"/>. Its response lists them while they are held, and the rerun route rebuilds a request
/// from them, as it does from a 5–30 job's, with no verdict.
/// </summary>
public class V31RerunTests
{
    private const string Letter = "Typed letter (fictional).";

    private static ConsultGenerationJobs Transport(IRunInputsStore? runInputs, IJobInputsBlobStore? inputsStore = null) => new(
        NullLogger<ConsultGenerationJobs>.Instance,
        Substitute.For<Consultologist.Api.Auth.IAccountAuthorizer>(),
        Substitute.For<IConsultGenerationJobEventStore>(),
        Substitute.For<IConsultGenerationJobStarter>(),
        Substitute.For<Consultologist.Api.Email.IGraphMailClient>(),
        new ConfigurationBuilder().Build(),
        Substitute.For<Consultologist.Api.Auth.IAccountSettingsStore>(),
        Substitute.For<IJobOutputsBlobStore>(),
        inputsStore ?? Substitute.For<IJobInputsBlobStore>(),
        Substitute.For<IConsultGenerationLinkStore>(),
        runInputs: runInputs);

    /// <summary>A 31 job started over <paramref name="start"/>, as the entity would hold it once completed.</summary>
    private static ConsultGenerationJobResponse Completed31(V31JobStartTests.Start start)
    {
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(Assert.Single(start.Signals).Payload);
        var entity = V31JobStartTests.Entity();
        entity.Initialize(initialize).GetAwaiter().GetResult();
        var state = V31JobStartTests.StateOf(entity);
        state.Status = ConsultGenerationJobStatuses.Completed;
        return state.ToResponse();
    }

    private static async Task<ConsultGenerationJobResponse> StartedPriorLettersAsync(V31JobStartTests.Start start)
    {
        var inputs = V31JobStartTests.Text(V31JobStartTests.Draft);
        inputs["prior_letters"] = ConsultInputValue.OfArray([ConsultInputValue.OfText(Letter)]);
        var outcome = await start.RunAsync(start.PutPriorLetters(), inputs);
        Assert.Null(outcome.Error);
        return Completed31(start);
    }

    [Fact]
    public async Task A31JobsResponse_ListsItsHeldInputs_WhileTheyAreHeld()
    {
        var start = new V31JobStartTests.Start(preview: "31");
        var response = await StartedPriorLettersAsync(start);

        var hydrated = await Transport(start.Inputs).HydrateOutputsAsync(response, CancellationToken.None);

        // a typed value as the 5-30 response gives it; a structured value as its JSON
        Assert.Equal(V31JobStartTests.Draft, hydrated.HeldInputs!["consult_draft"]);
        Assert.Equal(ConsultInputValue.OfArray([ConsultInputValue.OfText(Letter)]).AsJson(), hydrated.HeldInputs["prior_letters"]);
        Assert.Equal(["consult_draft", "prior_letters"], hydrated.HeldInputs.Keys.Order(StringComparer.Ordinal));
        // never the profile half
        Assert.DoesNotContain("profile", hydrated.HeldInputs.Keys);
    }

    [Fact]
    public async Task A31JobsResponse_ListsItsHeldInputs_AndNoneOnceDropped()
    {
        var start = new V31JobStartTests.Start(preview: "31");
        var response = await StartedPriorLettersAsync(start);
        var runInputs = Substitute.For<IRunInputsStore>();

        var hydrated = await Transport(runInputs).HydrateOutputsAsync(
            response with { InputsDroppedAtUtc = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero) }, CancellationToken.None);

        Assert.Null(hydrated.HeldInputs);
        await runInputs.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);
    }

    [Fact]
    public async Task Rerunning_A31Job_StartsA31Job_WithTheSameInputs_AndRerunOf()
    {
        var start = new V31JobStartTests.Start(preview: "31");
        var response = await StartedPriorLettersAsync(start);
        var sourceDocument = Assert.Single(start.Inputs.Documents).Value;
        var transport = Transport(start.Inputs);

        var request = await transport.RerunRequestAsync(response, CancellationToken.None);
        Assert.Equal(response.WorkflowPackage, request.WorkflowPackage);
        Assert.Null(request.ConsultDraft);
        Assert.Null(request.InputRefs);

        var origin = new ConsultGenerationJobOrigin(
            ConsultGenerationJobSources.App, RerunOfJobId: response.JobId, RerunBaseline: ConsultGenerationJobs.RerunBaselineFor(response.JobId, response));
        var outcome = await start.RunAsync(request, origin);
        Assert.Null(outcome.Error);

        // a 31 job, holding the source's inputs
        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(start.Signals.Last().Payload);
        Assert.NotEqual(response.JobId, initialize.JobId);
        Assert.Equal(31, initialize.PackageSpecVersion);
        Assert.Equal(response.WorkflowPackage, initialize.WorkflowPackage);
        var rerunDocument = Assert.Single(start.Inputs.Documents, d => d.Key != response.InputsBlob).Value;
        Assert.Equal(sourceDocument["inputs"]!.ToJsonString(), rerunDocument["inputs"]!.ToJsonString());
        Assert.Equal(response.EffectiveInputHash, initialize.EffectiveInputHash);

        // and its response names the source
        var entity = V31JobStartTests.Entity();
        await entity.Initialize(initialize);
        Assert.Equal(response.JobId, V31JobStartTests.StateOf(entity).ToResponse().RerunOf);
    }

    [Fact]
    public async Task Rerunning_KeepsTheSignaturePick_WhenTheSourceRecordedJob()
    {
        var start = new V31JobStartTests.Start(preview: "31", signatures: V31JobStartTests.Blocks());
        var outcome = await start.RunAsync(V31JobStartTests.Signed(start.PutSigned(), "sig-2"));
        Assert.Null(outcome.Error);
        var response = Completed31(start);
        Assert.Equal(("sig-2", "job"), (response.SignatureBlockId, response.SignatureChosen));

        var request = await Transport(start.Inputs).RerunRequestAsync(response, CancellationToken.None);

        Assert.Equal("sig-2", request.SignatureBlockId);

        // a default pick is made afresh by the account
        var byDefault = new V31JobStartTests.Start(preview: "31", signatures: V31JobStartTests.Blocks());
        Assert.Null((await byDefault.RunAsync(V31JobStartTests.Signed(byDefault.PutSigned(), null))).Error);
        var defaulted = Completed31(byDefault);
        Assert.Equal("default", defaulted.SignatureChosen);
        Assert.Null((await Transport(byDefault.Inputs).RerunRequestAsync(defaulted, CancellationToken.None)).SignatureBlockId);
    }

    [Fact]
    public async Task Rerunning_AJobWhoseInputsWereDropped_IsRefusedAs30RefusesIt()
    {
        var start = new V31JobStartTests.Start(preview: "31");
        var at31 = await StartedPriorLettersAsync(start);
        var dropped = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var at30 = at31 with { SchemaVersion = 11 };

        var refusal31 = ConsultGenerationJobs.RefusalForRerun(at31 with { InputsDroppedAtUtc = dropped });
        var refusal30 = ConsultGenerationJobs.RefusalForRerun(at30 with { InputsDroppedAtUtc = dropped });

        Assert.Equal("The held inputs were deleted on 2026-11-20 under the retention policy, so this run can no longer be rerun.", refusal31);
        Assert.Equal(refusal30, refusal31);
        Assert.Equal(
            ConsultGenerationJobs.RefusalForRerun(at30 with { InputsBlob = null }),
            ConsultGenerationJobs.RefusalForRerun(at31 with { InputsBlob = null }));
    }

    [Fact]
    public async Task A31JobWhoseDocumentIsGone_IsLoudToRerun_AsA30Is()
    {
        var start = new V31JobStartTests.Start(preview: "31");
        var response = await StartedPriorLettersAsync(start);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Transport(new V31Job.FakeInputs()).RerunRequestAsync(response, CancellationToken.None));
    }

    [Fact]
    public async Task ARerunsVerdict_IsNullAt31()
    {
        var start = new V31JobStartTests.Start(preview: "31");
        var source = await StartedPriorLettersAsync(start);
        var baseline = ConsultGenerationJobs.RerunBaselineFor(source.JobId, source);
        Assert.Equal(source.JobId, baseline.SourceJobId);
        Assert.Empty(baseline.NodeHashes);

        // the rerun's own record: initialised at 31 with the baseline, then completed
        var entity = V31JobStartTests.Entity();
        await entity.Initialize(new ConsultGenerationJobInitialize(
            "job-31-rerun", "user-1", Array.Empty<IReadOnlyDictionary<string, string>>(), PackageSpecVersion: 31, RerunBaseline: baseline));
        await entity.FinalizeJob(new ConsultGenerationJobFinalize(ConsultGenerationJobStatuses.Completed));

        var response = V31JobStartTests.StateOf(entity).ToResponse();
        Assert.Equal(source.JobId, response.RerunOf);
        Assert.Null(response.RerunVerdict);
        Assert.Null(response.RerunDivergence);
    }

    [Fact]
    public async Task Rerunning_A30Job_IsUnchanged()
    {
        var payload = new JobInputsPayload(
            JobInputsPayload.CurrentVersion,
            new Dictionary<string, string> { ["consult_draft"] = "The referral text." },
            new Dictionary<string, string> { ["consult_draft"] = ConsultInputValue.OfText("The referral text.").AsJson() });
        var held = new ConsultInputsBlobPointer("org-job-inputs", "u/j.json");
        var inputsStore = Substitute.For<IJobInputsBlobStore>();
        inputsStore.ReadAsync(held, Arg.Any<CancellationToken>()).Returns(payload);
        var runInputs = Substitute.For<IRunInputsStore>();
        var source = new ConsultGenerationJobResponse(
            "job-30", "user-1", ConsultGenerationJobStatuses.Completed, 1, 1, 0,
            new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            InputsBlob: held, WorkflowPackage: "general@v2026.08.4", SchemaVersion: 11,
            EffectiveInputHash: "aaaa", EffectiveInputHashVersion: 5);

        var request = await Transport(runInputs, inputsStore).RerunRequestAsync(source, CancellationToken.None);

        Assert.Equivalent(ConsultGenerationJobs.RerunRequestFrom(source, payload), request);
        Assert.Equal(ConsultInputValue.OfText("The referral text."), request.Inputs!["consult_draft"]);
        Assert.Null(request.SignatureBlockId);
        await runInputs.DidNotReceiveWithAnyArgs().ReadAsync(default!, default);

        // its baseline is the hashes it was, and its verdict is still judged
        var baseline = ConsultGenerationJobs.RerunBaselineFor("job-30", source);
        Assert.Equal(("job-30", "aaaa", 5), (baseline.SourceJobId, baseline.EffectiveInputHash, baseline.EffectiveInputHashVersion));
    }

    // ---- #1509 (D8; part 08 § 5.1): a rerun keeps the source's origin of an earlier run's text ----------------------------

    /// <summary>A 31 job whose <c>consult_draft</c> (typed) and <c>prior_letters</c> (a typed letter, then an earlier run's deliverable) were started.</summary>
    private static async Task<(V31JobStartTests.Start Start, ConsultGenerationJobResponse Source)> StartedFromAnEarlierRunAsync()
    {
        var start = new V31JobStartTests.Start(preview: "31");
        start.WithSourceRun(V31JobStartTests.SourceRun());
        var inputs = V31JobStartTests.Text(V31JobStartTests.Draft);
        inputs["prior_letters"] = ConsultInputValue.OfArray([ConsultInputValue.OfText(Letter)]);
        var outcome = await start.RunAsync(new ConsultGenerationRequest(
            null, V31JobStartTests.RefText(start.PutPriorLetters()), Inputs: inputs,
            InputRefs: V31JobStartTests.Refs("prior_letters", new ConsultInputRef(V31JobStartTests.SourceJob, "consult"))));
        Assert.Null(outcome.Error);
        return (start, Completed31(start));
    }

    private static async Task<(JsonArray Header, JsonObject Document, ConsultGenerationJobInitialize Initialize)> RerunAsync(
        V31JobStartTests.Start start, ConsultGenerationJobResponse source)
    {
        var request = await Transport(start.Inputs).RerunRequestAsync(source, CancellationToken.None);
        var outcome = await start.RunAsync(request, ConsultGenerationJobs.RerunOriginFor(source.JobId, source, null, true));
        Assert.Null(outcome.Error);

        var initialize = Assert.IsType<ConsultGenerationJobInitialize>(start.Signals.Last().Payload);
        var entity = V31JobStartTests.Entity();
        await entity.Initialize(initialize);
        var header = V31JobStartTests.StateOf(entity).ToRecordHeader()["inputs"]!.AsArray();
        return (header, start.Inputs.Documents[initialize.InputsBlob!], initialize);
    }

    [Fact]
    public async Task Rerunning_KeepsAnEarlierRunsOrigin_AndItsModelDerivedMark()
    {
        var (start, source) = await StartedFromAnEarlierRunAsync();
        Assert.Equal(["prior_letters"], start.Inputs.Documents.Values.Single()["modelDerived"]!.AsArray().Select(n => (string?)n));

        var (header, document, _) = await RerunAsync(start, source);

        // the input's elements: the typed letter, then the earlier run's text with its ORIGINAL job and deliverable
        var letters = header.Single(i => (string?)i!["id"] == "prior_letters")!.AsObject();
        Assert.Equal(
            """[{"origin":"typed"},{"origin":"previous-run","jobId":"0123456789abcdef0123456789abcdef","resultId":"consult","modelDerived":true}]""",
            letters["elements"]!.ToJsonString());
        // the run's decisions read it as model-derived
        Assert.Equal(["prior_letters"], document["modelDerived"]!.AsArray().Select(n => (string?)n));
    }

    [Fact]
    public async Task Rerunning_ATypedInput_StaysTyped()
    {
        var (start, source) = await StartedFromAnEarlierRunAsync();

        var (header, document, _) = await RerunAsync(start, source);

        var draft = header.Single(i => (string?)i!["id"] == "consult_draft")!.AsObject();
        Assert.Equal("typed", (string?)draft["origin"]);
        Assert.False(draft.ContainsKey("modelDerived"));
        Assert.DoesNotContain("consult_draft", document["modelDerived"]!.AsArray().Select(n => (string?)n));

        // a source with nothing from an earlier run inherits nothing and records no model-derived member
        var plain = new V31JobStartTests.Start(preview: "31");
        Assert.Null((await plain.RunAsync(plain.PutPriorLetters(), V31JobStartTests.Text(V31JobStartTests.Draft))).Error);
        var plainSource = Completed31(plain);
        Assert.Null(ConsultGenerationJobs.RerunOriginFor(plainSource.JobId, plainSource, null, true).InheritedInputOrigins?.GetValueOrDefault("consult_draft"));
        var (_, plainDocument, _) = await RerunAsync(plain, plainSource);
        Assert.False(plainDocument.ContainsKey("modelDerived"));
    }

    [Fact]
    public async Task Rerunning_WritesTheLineageLinksToTheOriginalSources()
    {
        var (start, source) = await StartedFromAnEarlierRunAsync();
        start.Links.ClearReceivedCalls();

        var (_, _, initialize) = await RerunAsync(start, source);

        var rows = Assert.Single(start.Links.ReceivedCalls(), c => c.GetMethodInfo().Name == nameof(IConsultGenerationLinkStore.WriteAsync))
            .GetArguments()[0] as IReadOnlyList<ConsultGenerationLink>;
        Assert.Equal(2, rows!.Count);
        var rerunEdge = Assert.Single(rows, r => r.Kind == ConsultInputOriginKinds.Rerun);
        Assert.Equal((source.JobId, initialize.JobId), (rerunEdge.SourceJobId, rerunEdge.ConsumerJobId));
        var original = Assert.Single(rows, r => r.Kind == ConsultInputOriginKinds.PreviousRun);
        Assert.Equal((V31JobStartTests.SourceJob, initialize.JobId, "prior_letters", "consult", 1),
            (original.SourceJobId, original.ConsumerJobId, original.InputId, original.ResultId, original.ElementIndex));
    }

    [Fact]
    public void AStartRequest_CannotClaimOrDropAnOrigin()
    {
        // the public request record has no member that carries an origin, so nothing on the wire can mark or unmark an input
        Assert.DoesNotContain(
            typeof(ConsultGenerationRequest).GetProperties(),
            p => p.Name.Contains("Origin", StringComparison.OrdinalIgnoreCase) && p.Name != nameof(ConsultGenerationRequest.InputRefs)
                || p.PropertyType.ToString().Contains(nameof(ConsultInputOrigin), StringComparison.Ordinal));

        // and a body that names one binds to nothing
        var body = System.Text.Json.JsonSerializer.Deserialize<ConsultGenerationRequest>(
            """{"inputs":{"consult_draft":"x"},"inheritedInputOrigins":{"consult_draft":[{"kind":"previous-run","sourceJobId":"j"}]},"inputOrigins":{"consult_draft":[{"kind":"typed"}]}}""",
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(body);
        Assert.Equal(["consult_draft"], body!.Inputs!.Keys);
    }
}
