using AngleSharp.Dom;
using Microsoft.AspNetCore.Components.Web;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
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

    private void WithJobStatus(string status, DateTimeOffset? scheduledAt = null)
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(
                    JobId, status, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                    TotalBlockCount: 9, CompletedBlockCount: 9, FailedBlockCount: 0,
                    ScheduledAtUtc: scheduledAt)
            },
            null));
    }

    private IRenderedComponent<History> RenderList() => Render<History>();

    private static IElement Link(IRenderedComponent<History> page) => page.Find(".run-view-link");

    /// <summary>
    /// A Scheduled row carries several of these since #390 — select by label
    /// rather than by position, or a new control silently retargets the test.
    /// </summary>
    private static IElement RowButton(IRenderedComponent<History> page, string label) =>
        page.FindAll(".cancel-run-button").First(button => button.TextContent.Trim() == label);

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

        Assert.Equal("cancel", RowButton(RenderList(), "cancel").TextContent.Trim());
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

        await RowButton(page, "cancel").ClickAsync(new MouseEventArgs());

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
        await RowButton(page, "cancel").ClickAsync(new MouseEventArgs());

        Assert.Contains("already started", page.Markup, StringComparison.Ordinal);
    }

    private static readonly DateTimeOffset ScheduledFor =
        new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AScheduledRun_OffersToChangeItsTime()
    {
        WithJobStatus("Scheduled", ScheduledFor);

        var labels = RenderList().FindAll(".cancel-run-button").Select(b => b.TextContent.Trim());

        Assert.Contains("change time", labels);
    }

    [Fact]
    public async Task TheEditor_OpensOnTheTimeTheJobActuallyHas()
    {
        // #390: prefilled from the record, which is also why reset exists.
        WithJobStatus("Scheduled", ScheduledFor);
        var page = RenderList();

        await RowButton(page, "change time").ClickAsync(new());

        Assert.Equal(
            ScheduleDefault.ToLocalInputValue(ScheduledFor),
            page.Find("input[type=datetime-local]").GetAttribute("value"));
    }

    [Fact]
    public async Task Reset_ReturnsTheFieldToTheJobsTime()
    {
        // Type over it, press reset, get the record back — the only place the
        // original time is still shown once the field is edited.
        WithJobStatus("Scheduled", ScheduledFor);
        var page = RenderList();
        await RowButton(page, "change time").ClickAsync(new());

        page.Find("input[type=datetime-local]").Change("2026-08-25T09:15");
        await RowButton(page, "reset").ClickAsync(new());

        Assert.Equal(
            ScheduleDefault.ToLocalInputValue(ScheduledFor),
            page.Find("input[type=datetime-local]").GetAttribute("value"));
    }

    [Fact]
    public async Task Moving_SendsTheNewTimeAndLeavesTheOldRowCancelled()
    {
        // The row cannot be patched: a reschedule produces a NEW job id. The
        // original stays as Cancelled (#202's rule) and the new job is added.
        WithJobStatus("Scheduled", ScheduledFor);
        const string NewJobId = "ffffffffffffffffffffffffffffffff";
        DateTimeOffset? sent = null;

        AIService.RescheduleConsultGenerationJobAsync(JobId, Arg.Do<DateTimeOffset>(value => sent = value))
            .Returns(NewJobId);
        AIService.GetConsultGenerationJobAsync(NewJobId).Returns(new ConsultGenerationJobResponse(
            NewJobId, "user-1", "Scheduled",
            TotalBlockCount: 9, CompletedBlockCount: 0, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(),
            FailedBlocks: new Dictionary<string, string>(),
            Success: false,
            ScheduledAtUtc: ScheduledFor.AddDays(1)));

        var page = RenderList();
        await RowButton(page, "change time").ClickAsync(new());
        page.Find("input[type=datetime-local]").Change("2026-08-21T09:15");
        await RowButton(page, "move").ClickAsync(new());

        await AIService.Received(1).RescheduleConsultGenerationJobAsync(JobId, Arg.Any<DateTimeOffset>());
        Assert.NotNull(sent);
        Assert.Equal(new DateTime(2026, 8, 21, 9, 15, 0), sent!.Value.ToLocalTime().DateTime);

        // Two rows: the cancelled original and the new scheduled one.
        // Two rows, not one changed row: the cancelled original and the new
        // scheduled job. Asserting the COUNT as well, because "contains
        // Scheduled" alone passed even with the new row missing — the deep-link
        // path has its own Insert, and mutating that one proved nothing.
        Assert.Equal(2, page.FindAll(".job-item").Count);
        var badges = page.FindAll(".job-status-badge").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Cancelled", badges);
        Assert.Contains("Scheduled", badges);
    }

    [Fact]
    public void ATerminalRun_OffersNoTimeChange()
    {
        WithJobStatus("Completed");

        Assert.DoesNotContain(
            "change time",
            RenderList().FindAll(".cancel-run-button").Select(b => b.TextContent.Trim()));
    }
}
