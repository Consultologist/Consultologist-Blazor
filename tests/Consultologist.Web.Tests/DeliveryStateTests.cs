using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #486: what a job says about its email — on the Consults run, in History's
/// row and panel — and what Consults says before a submit.
/// </summary>
public class DeliveryStateTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(DeliveryState.Sent, "delivered")]
    [InlineData(DeliveryState.Failed, "not delivered")]
    [InlineData(DeliveryState.AddressNotSet, "not delivered")]
    [InlineData(DeliveryState.NotConfigured, "not delivered")]
    public void Badge_NamesTheOutcome(string outcome, string expected)
    {
        Assert.Equal(expected, DeliveryState.Badge(outcome));
    }

    [Fact]
    public void NothingRecorded_SaysNothing()
    {
        Assert.Null(DeliveryState.Badge(null));
        Assert.Null(DeliveryState.Describe(null, null));
    }

    [Fact]
    public void Describe_SaysWhetherTheDocumentRodeAlong()
    {
        Assert.Contains("document attached", DeliveryState.Describe(DeliveryState.Sent, true));
        Assert.Contains("link only", DeliveryState.Describe(DeliveryState.Sent, false));
        Assert.Contains("no delivery address", DeliveryState.Describe(DeliveryState.AddressNotSet, null));
        Assert.Contains("failed", DeliveryState.Describe(DeliveryState.Failed, null));
    }

    // --- History

    private void WithHistoryJob(string? outcome, DateTimeOffset? deliveredAt = null, bool? attached = null)
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(
                    JobId, "Completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
                    DeliveryOutcome: outcome, DeliveredAtUtc: deliveredAt)
            },
            null));
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed",
            TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(),
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            EffectiveInputHash: "aaaa", EffectiveInputHashVersion: 3,
            WorkflowOutputHash: "bbbb", WorkflowOutputHashVersion: 3,
            DeliveryOutcome: outcome, DeliveredAtUtc: deliveredAt, DeliveryDocumentAttached: attached));
    }

    [Fact]
    public void HistoryRow_BadgesADeliveredJob_AndThePanelDatesIt()
    {
        // FormatDate is relative ("3 days ago"); the date shows as a prefix before the dash.
        var at = DateTimeOffset.UtcNow.AddDays(-3);
        WithHistoryJob(DeliveryState.Sent, at, attached: true);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Equal("delivered", page.Find(".job-source-badge--delivery").TextContent.Trim());
        var row = page.Find(".provenance-delivery").TextContent.Trim();
        Assert.Contains("document attached", row);
        Assert.Matches("^.+ — Emailed", row);
    }

    [Fact]
    public void HistoryRow_BadgesAJobThatWasNeverEmailed()
    {
        WithHistoryJob(DeliveryState.AddressNotSet);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        var badge = page.Find(".job-source-badge--delivery");
        Assert.Equal("not delivered", badge.TextContent.Trim());
        Assert.Contains("no delivery address", badge.GetAttribute("title"));
        Assert.Contains("no delivery address", page.Find(".provenance-delivery").TextContent);
    }

    [Fact]
    public void HistoryRow_SaysNothingForAJobFromBefore()
    {
        WithHistoryJob(null);

        var page = Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".job-source-badge--delivery"));
        Assert.Empty(page.FindAll(".provenance-delivery"));
    }

    // --- Consults

    private static AccountIdentity Entra() =>
        new("entra-external-id", "https://login.microsoftonline.com/x", "sub-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private void WithDeliveryAddress(string? address)
    {
        AccountService.GetCurrentAccountAsync().Returns(new AccountMeResponse(
            "user-1", "A Clinician", "clinician@example.com", "Active", Entra(), new[] { Entra() },
            DeliveryAddress: address));
    }

    private void WithCompletedRun(string? outcome, bool? attached)
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed",
            TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["section-instructions:hpi"] = "Section prose." },
            FailedBlocks: new Dictionary<string, string>(),
            Success: true,
            AssembledDocuments: new[] { new ConsultGenerationResultDocumentResponse("consult", "Consultation note", "The note.") },
            DeliveryOutcome: outcome, DeliveryDocumentAttached: attached));
    }

    [Fact]
    public void Setup_WarnsBeforeSubmit_WhenNoAddressIsVerified()
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });
        WithDeliveryAddress(null);

        var page = Render<Consults>();

        page.WaitForAssertion(() => Assert.Contains("will not be emailed", page.Find(".delivery-address-notice").TextContent));
    }

    [Fact]
    public void Setup_IsQuiet_WhenAnAddressIsVerified()
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });
        WithDeliveryAddress("dr.a@clinic.example");

        var page = Render<Consults>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".action-row")));
        Assert.Empty(page.FindAll(".delivery-address-notice"));
    }

    [Fact]
    public void TheScheduleConfirmation_PromisesAnEmailOnlyWhereOneWillGo()
    {
        var at = new DateTimeOffset(2026, 8, 27, 6, 0, 0, TimeSpan.Zero);

        Assert.Contains("email at dr.a@clinic.example", Consults.ScheduleConfirmationFor(at, "dr.a@clinic.example"));
        Assert.Contains("will not be emailed", Consults.ScheduleConfirmationFor(at, null));
    }

    [Fact]
    public void ACompletedRun_CarriesItsDeliveryLine()
    {
        WithCompletedRun(DeliveryState.Sent, attached: false);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("link only", page.Find(".delivery-line").TextContent);
    }

    [Fact]
    public void ARunNeverEmailed_SaysSo()
    {
        WithCompletedRun(DeliveryState.AddressNotSet, null);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Contains("no delivery address", page.Find(".delivery-line").TextContent);
    }

    [Fact]
    public void ARunFromBefore_HasNoDeliveryLine()
    {
        WithCompletedRun(null, null);

        var page = Render<Consults>(parameters => parameters.Add(p => p.JobId, JobId));

        Assert.Empty(page.FindAll(".delivery-line"));
    }
}
