using Bunit;
using Microsoft.AspNetCore.Components;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Microsoft.FluentUI.AspNetCore.Components;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #879: the History command bar — search by run id, filter by status and date —
/// narrows the FluentDataGrid over the whole in-memory history. Filters are driven
/// through the component ValueChanged callbacks (bUnit runs no JS, so the DOM value
/// of a Fluent input doesn't reflect a programmatic set — the #876 lesson).
/// </summary>
public class HistoryFilterTests : ClientRenderTestContext
{
    private const string CompletedA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string FailedB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string CompletedC = "cccccccccccccccccccccccccccccccc";

    private IRenderedComponent<History> RenderWithThreeJobs()
    {
        var now = DateTimeOffset.Now;
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[]
            {
                new AccountJobSummaryResponse(CompletedA, "Completed", now, now, now, 9, 9, 0),
                new AccountJobSummaryResponse(FailedB, "Failed", now.AddDays(-10), now.AddDays(-10), now.AddDays(-10), 9, 3, 6),
                new AccountJobSummaryResponse(CompletedC, "Completed", now, now, now, 9, 9, 0),
            },
            null));
        return Render<History>();
    }

    private static int RowCount(IRenderedComponent<History> page) => page.FindAll("tbody tr").Count;

    // ValueChanged must be invoked on the renderer's dispatcher.
    private static Task SetAsync<T>(IRenderedComponent<History> page, EventCallback<T> callback, T value) =>
        page.InvokeAsync(() => callback.InvokeAsync(value));

    [Fact]
    public void AllThreeRows_ShowByDefault()
    {
        var page = RenderWithThreeJobs();
        Assert.Equal(3, RowCount(page));
    }

    [Fact]
    public async Task SearchByRunId_NarrowsToMatches()
    {
        var page = RenderWithThreeJobs();

        await SetAsync(page, page.FindComponents<FluentSearch>().First().Instance.ValueChanged, "aaaaaaaa");

        Assert.Equal(1, RowCount(page));
        Assert.Contains(CompletedA[..8], page.Markup);
    }

    [Fact]
    public async Task StatusFilter_NarrowsToThatStatus()
    {
        var page = RenderWithThreeJobs();

        await SetAsync(page, page.FindComponents<FluentSelect<string>>().First().Instance.ValueChanged, "Failed");

        Assert.Equal(1, RowCount(page));
        Assert.Contains(FailedB[..8], page.Markup);
    }

    [Fact]
    public async Task FromDateFilter_ExcludesOlderRuns()
    {
        var page = RenderWithThreeJobs();

        // From today → the 10-days-ago Failed run drops out, the two from today stay.
        await SetAsync(page, page.FindComponents<FluentDatePicker>().First().Instance.ValueChanged, (DateTime?)DateTime.Today);

        Assert.Equal(2, RowCount(page));
    }

    [Fact]
    public async Task ClearingTheSearch_RestoresAllRows()
    {
        var page = RenderWithThreeJobs();

        var search = page.FindComponents<FluentSearch>().First().Instance;
        await SetAsync(page, search.ValueChanged, "aaaaaaaa");
        Assert.Equal(1, RowCount(page));

        await SetAsync(page, search.ValueChanged, string.Empty);
        Assert.Equal(3, RowCount(page));
    }
}
