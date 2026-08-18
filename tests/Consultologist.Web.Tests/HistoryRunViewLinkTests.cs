using AngleSharp.Dom;
using Microsoft.AspNetCore.Components.Web;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #367: the link from a History row to the run view, which is the only place a
/// job's rail is drawn. It used to be guarded on Running or Queued, so the
/// surface #356 and #361 had just made trustworthy was unreachable for every job
/// a minute after it finished — and for a Scheduled job, which the guard missed
/// altogether.
///
/// These render the list rather than the deep-linked detail: the link lives on
/// the row summary, so no detail load is needed to reach it.
/// </summary>
public class HistoryRunViewLinkTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";

    private void WithJobStatus(string status)
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(
                    JobId, status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    TotalBlockCount: 9, CompletedBlockCount: 9, FailedBlockCount: 0)
            },
            null));
    }

    private IRenderedComponent<History> RenderList() => Render<History>();

    private static IElement Link(IRenderedComponent<History> page) => page.Find(".run-view-link");

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    public void ATerminalJob_LinksToItsRunView(string status)
    {
        // The reported defect. Failed is the sharper half: a job that failed is
        // exactly the one whose rail you want, and it never had a link.
        WithJobStatus(status);

        var link = Link(RenderList());

        Assert.Equal($"/consults/{JobId}", link.GetAttribute("href"));
        Assert.Equal("view run", link.TextContent.Trim());
    }

    [Fact]
    public void AScheduledJob_IsLinkedTooAndReadsAsStillToCome()
    {
        // The gap the issue did not name: the old guard covered Running and
        // Queued only, so an overnight job (#157) had no link at all.
        WithJobStatus("Scheduled");

        var link = Link(RenderList());

        Assert.Equal($"/consults/{JobId}", link.GetAttribute("href"));
        Assert.Equal("watch live", link.TextContent.Trim());
    }

    [Fact]
    public void ATerminalJobsLink_DoesNotSayWatchLive()
    {
        // The label is the whole point of dropping the guard rather than simply
        // rendering the old link unconditionally: nothing is live to watch.
        WithJobStatus("Completed");

        Assert.DoesNotContain("watch live", RenderList().Markup);
    }

    [Fact]
    public void AScheduledRun_OffersToCancel()
    {
        // #202: the run has not started, so calling it off costs nothing.
        WithJobStatus("Scheduled");

        var button = RenderList().Find(".cancel-run-button");

        Assert.Equal("cancel", button.TextContent.Trim());
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Running")]
    [InlineData("Cancelled")]
    public void AnythingElse_OffersNoCancel(string status)
    {
        // Running is the one worth naming: stopping work already paid for is a
        // different decision, and #157 deferred only the unfired-timer case.
        WithJobStatus(status);

        Assert.Empty(RenderList().FindAll(".cancel-run-button"));
    }

    [Fact]
    public void ACancelledRun_ReadsAsTerminal()
    {
        // Or the poll loop never stops and the link keeps offering to watch a
        // run that will not happen.
        WithJobStatus("Cancelled");

        Assert.Equal("view run", Link(RenderList()).TextContent.Trim());
    }

    [Fact]
    public async Task Cancelling_CallsTheEndpointAndUpdatesTheRowInPlace()
    {
        WithJobStatus("Scheduled");
        var page = RenderList();

        await page.Find(".cancel-run-button").ClickAsync(new MouseEventArgs());

        await AIService.Received(1).CancelConsultGenerationJobAsync(JobId);
        // The row stays — a consult that was submitted and stopped is a fact
        // worth keeping — and reads Cancelled without a reload.
        Assert.Contains("Cancelled", page.Find(".job-status-badge").TextContent);
    }

    [Fact]
    public async Task ARefusedCancel_SaysWhichStateRefused()
    {
        // The whole reason the endpoint answers 409 with a sentence.
        WithJobStatus("Scheduled");
        AIService.CancelConsultGenerationJobAsync(JobId)
            .Returns<Task>(_ => throw new InvalidOperationException("This consult has already started, so it can no longer be cancelled."));

        var page = RenderList();
        await page.Find(".cancel-run-button").ClickAsync(new MouseEventArgs());

        Assert.Contains("already started", page.Markup, StringComparison.Ordinal);
    }
}
