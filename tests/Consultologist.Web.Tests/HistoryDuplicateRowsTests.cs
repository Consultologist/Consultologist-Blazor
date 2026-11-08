using System.Net;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Accounts;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1326: History polled seven stale Queued index rows every 5 s for as long as
/// the page was open. Each was a duplicate of a run that had finished (the
/// pre-13a33672 Initialize reset wrote a second row), and three answered 404.
/// The list keeps one row per job id, and a run the server no longer has is
/// named and left alone rather than retried forever.
/// </summary>
public class HistoryDuplicateRowsTests : ClientRenderTestContext
{
    private const string JobId = "ba1ec33bc68f4e88b0c8cce2eeea93eb";
    private static readonly DateTimeOffset At = new(2026, 8, 28, 1, 2, 12, TimeSpan.Zero);

    // bUnit's default 1 s wait timed out once under the full suite's load; the poll itself is 20 ms.
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    private static AccountJobSummaryResponse Row(string status, DateTimeOffset created, string jobId = JobId) =>
        new(jobId, status, created, created, created, TotalBlockCount: 9, CompletedBlockCount: status == "Completed" ? 9 : 0, FailedBlockCount: 0);

    private void WithRows(params AccountJobSummaryResponse[] rows) =>
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(rows, null));

    private IRenderedComponent<History> RenderFast() =>
        Render<History>(parameters => parameters.Add(p => p.FastPollInterval, TimeSpan.FromMilliseconds(20)));

    [Fact]
    public void OnePerJob_KeepsTheFinishedTwin_InListOrder()
    {
        var other = Row("Completed", At.AddDays(1), "ffffffffffffffffffffffffffffffff");
        var rows = new[] { other, Row("Completed", At.AddMilliseconds(150)), Row("Queued", At) };

        var kept = JobRows.OnePerJob(rows);

        Assert.Equal(new[] { other.JobId, JobId }, kept.Select(row => row.JobId));
        Assert.Equal("Completed", kept[1].Status);
    }

    [Fact]
    public void OnePerJob_WithNoFinishedTwin_KeepsTheLaterRow()
    {
        var kept = JobRows.OnePerJob(new[] { Row("Queued", At), Row("Running", At.AddSeconds(2)) });

        Assert.Equal("Running", Assert.Single(kept).Status);
    }

    [Fact]
    public void AJobListedTwice_ShowsOnce_AsItsFinishedRow_AndIsNotPolled()
    {
        WithRows(Row("Completed", At.AddMilliseconds(150)), Row("Queued", At));

        var page = RenderFast();

        var badge = Assert.Single(page.FindAll(".job-status-badge"));
        Assert.Contains("Completed", badge.TextContent);
        Assert.DoesNotContain("watch live", page.Markup);
        Thread.Sleep(150);
        AIService.DidNotReceive().GetConsultGenerationJobAsync(JobId);
    }

    [Fact]
    public void ARunTheServerNoLongerHas_IsNamedNotFound_AndPolledOnce()
    {
        WithRows(Row("Queued", At));
        AIService.GetConsultGenerationJobAsync(JobId)
            .ThrowsAsync(new ConsultGenerationRefusedException(HttpStatusCode.NotFound, "Consult generation job was not found."));

        var page = RenderFast();

        page.WaitForAssertion(() => Assert.Contains("Not found", page.Find(".job-status-badge").TextContent), Settle);
        Assert.Contains("job-status-badge--notfound", page.Find(".job-status-badge").ClassName);
        Assert.DoesNotContain("watch live", page.Markup);
        Thread.Sleep(200);
        AIService.Received(1).GetConsultGenerationJobAsync(JobId);
    }

    [Fact]
    public void AnyOtherPollFailure_IsStillRetried()
    {
        WithRows(Row("Queued", At));
        AIService.GetConsultGenerationJobAsync(JobId)
            .ThrowsAsync(new ConsultGenerationRefusedException(HttpStatusCode.InternalServerError, "boom"));

        var page = RenderFast();

        page.WaitForAssertion(() => Assert.True(AIService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAIEndpointService.GetConsultGenerationJobAsync)) >= 2), Settle);
        Assert.DoesNotContain("Not found", page.Find(".job-status-badge").TextContent);
    }
}
