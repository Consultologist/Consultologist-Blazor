using System.Net;
using Consultologist.Api.Jobs;
using Consultologist.Api.Models;
using Consultologist.Api.Workflow;

using Consultologist.PackageFormat;
namespace Consultologist.Api.Tests;

/// <summary>
/// #374: the start-refusal status mapping, which was an inline switch inside
/// the endpoint that no test reached — and whose default is 500, so a missing
/// arm silently turns a refusal the engine understands into an apparent server
/// fault.
/// </summary>
public class ConsultGenerationStartStatusTests
{
    [Theory]
    // The split this issue is about: the registry being down is a 503, and a
    // package whose content the catalog no longer matches is not.
    [InlineData(ConsultGenerationJobStartError.RegistryUnavailable, HttpStatusCode.ServiceUnavailable)]
    [InlineData(ConsultGenerationJobStartError.PackageContentRejected, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ConsultGenerationJobStartError.SpecVersionNotYetExecutable, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ConsultGenerationJobStartError.MalformedPackageRef, HttpStatusCode.BadRequest)]
    [InlineData(ConsultGenerationJobStartError.ForeignPackageRef, HttpStatusCode.Forbidden)]
    [InlineData(ConsultGenerationJobStartError.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(ConsultGenerationJobStartError.InputsMismatch, HttpStatusCode.UnprocessableEntity)]
    [InlineData(ConsultGenerationJobStartError.NoApplicableDeliverable, HttpStatusCode.UnprocessableEntity)]
    public void EachRefusal_AnswersItsOwnStatus(ConsultGenerationJobStartError error, HttpStatusCode expected)
    {
        Assert.Equal(expected, ConsultGenerationJobs.StatusFor(error));
    }

    [Fact]
    public void NoRefusalFallsThroughToAServerError()
    {
        // The default is 500. Every kind the engine can return must be named
        // above it, or the caller is told the server broke when it did not.
        Assert.All(
            Enum.GetValues<ConsultGenerationJobStartError>(),
            error => Assert.NotEqual(HttpStatusCode.InternalServerError, ConsultGenerationJobs.StatusFor(error)));
    }

    [Fact]
    public void TheStrandingSentence_NamesTheCatalogThatMoved()
    {
        // #374: an operator reading this needs to know which of the two things
        // changed. The package cannot have — it is immutable — so the catalog
        // version is the whole content of the message.
        var message = WorkflowPackageContentException
            .SchemaUnmatched("general@v2026.08.1", "concept-list", "output-contracts@v2026.07.3")
            .Message;

        Assert.Contains("general@v2026.08.1", message);
        Assert.Contains("concept-list", message);
        Assert.Contains("output-contracts@v2026.07.3", message);
        Assert.Contains("the catalog moved", message);
    }

    [Theory]
    // #202: only a run that has not started may be called off. The deferral in
    // #157 is about an unfired timer; stopping work already paid for is a
    // different decision.
    [InlineData(ConsultGenerationJobStatuses.Scheduled, null)]
    [InlineData(ConsultGenerationJobStatuses.Running, "already started")]
    [InlineData(ConsultGenerationJobStatuses.Queued, "already started")]
    [InlineData(ConsultGenerationJobStatuses.Completed, "already completed")]
    [InlineData(ConsultGenerationJobStatuses.Failed, "already failed")]
    [InlineData(ConsultGenerationJobStatuses.Cancelled, "already cancelled")]
    public void OnlyAScheduledRunMayBeCancelled(string status, string? expectedFragment)
    {
        var refusal = ConsultGenerationJobs.RefusalForCancel(status);

        if (expectedFragment is null)
        {
            Assert.Null(refusal);
            return;
        }

        Assert.Contains(expectedFragment, refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AScheduleTimeInsideTheHorizon_IsAccepted()
    {
        Assert.Null(ConsultGenerationJobs.RefusalForScheduleTime(DateTimeOffset.UtcNow.AddDays(6)));
    }

    [Fact]
    public void AScheduleTimeBeyondTheHorizon_IsRefused()
    {
        // #390: a reschedule must not put a job somewhere a fresh submit could
        // not — the start endpoint enforces the same seven days.
        var refusal = ConsultGenerationJobs.RefusalForScheduleTime(DateTimeOffset.UtcNow.AddDays(8));

        Assert.NotNull(refusal);
        Assert.Contains("7 days", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void APastScheduleTime_IsStillAccepted()
    {
        // Deliberate, and inherited from #157: a past time runs immediately,
        // which is a legitimate way to say "actually, now".
        Assert.Null(ConsultGenerationJobs.RefusalForScheduleTime(DateTimeOffset.UtcNow.AddHours(-1)));
    }

    // ----- #549: the rerun refusals and the rebuild -----

    private static ConsultGenerationJobResponse RerunSource(
        string status = ConsultGenerationJobStatuses.Completed,
        bool held = true,
        DateTimeOffset? inputsDroppedAtUtc = null,
        string? workflowPackage = "general@v2026.08.4") =>
        new("job-1", "user-1", status, 1, 1, 0, new Dictionary<string, string>(), new Dictionary<string, string>(), true,
            WorkflowPackage: workflowPackage,
            InputsBlob: held ? new ConsultInputsBlobPointer("org-job-inputs", "user-1/job-1.json") : null,
            InputsDroppedAtUtc: inputsDroppedAtUtc);

    [Fact]
    public void ACompletedHeldRun_MayBeRerun()
    {
        Assert.Null(ConsultGenerationJobs.RefusalForRerun(RerunSource()));
    }

    [Theory]
    [InlineData(ConsultGenerationJobStatuses.Running)]
    [InlineData(ConsultGenerationJobStatuses.Scheduled)]
    [InlineData(ConsultGenerationJobStatuses.Failed)]
    [InlineData(ConsultGenerationJobStatuses.Cancelled)]
    public void OnlyACompletedRunMayBeRerun(string status)
    {
        var refusal = ConsultGenerationJobs.RefusalForRerun(RerunSource(status));

        Assert.Contains("Only a completed consult can be rerun", refusal, StringComparison.Ordinal);
        Assert.Contains(status.ToLowerInvariant(), refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnheldRun_RefusesByName()
    {
        Assert.Contains(
            "inputs were not held",
            ConsultGenerationJobs.RefusalForRerun(RerunSource(held: false)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADroppedRun_RefusesWithTheDate()
    {
        var refusal = ConsultGenerationJobs.RefusalForRerun(
            RerunSource(inputsDroppedAtUtc: new DateTimeOffset(2026, 9, 8, 3, 0, 0, TimeSpan.Zero)));

        Assert.Contains("deleted on 2026-09-08", refusal, StringComparison.Ordinal);
        Assert.Contains("retention policy", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordWithoutItsPackageRef_Refuses()
    {
        // A rerun never re-resolves a pin — with no recorded ref there is
        // nothing faithful to run.
        Assert.Contains(
            "does not name the package version",
            ConsultGenerationJobs.RefusalForRerun(RerunSource(workflowPackage: null)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheRebuild_CarriesOnlyTheSuppliedValues_UnderTheRecordsExactRef()
    {
        var payload = new JobInputsPayload(
            JobInputsPayload.CurrentVersion,
            Effective: new Dictionary<string, string> { ["consult_draft"] = "The referral text." },
            Supplied: new Dictionary<string, string>
            {
                ["consult_draft"] = ConsultInputValue.OfText("The referral text.").AsJson()
            });

        var request = ConsultGenerationJobs.RerunRequestFrom(RerunSource(), payload);

        Assert.Equal("general@v2026.08.4", request.WorkflowPackage);
        Assert.Equal(ConsultInputValue.OfText("The referral text."), request.Inputs!["consult_draft"]);
        Assert.Null(request.ConsultDraft);
        Assert.Null(request.InputFiles);
        Assert.Null(request.InputRefs);
        Assert.Null(request.ScheduledAtUtc);
    }
}
