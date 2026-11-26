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
}
