using System;
using Bunit;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Shared;
using NSubstitute;
using Xunit;

namespace Consultologist.Web.Tests;

/// <summary>
/// #510/#691: the "load from a previous run" disclosure. Opening it lists the
/// account's completed runs (only completed ones are choosable), fetched on
/// demand rather than at render.
/// </summary>
public class PreviousRunPickerTests : ClientRenderTestContext
{
    private static readonly DateTimeOffset When = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);

    private static AccountJobSummaryResponse Job(string id, string status) =>
        new(id, status, When, When, When, TotalBlockCount: 2, CompletedBlockCount: 2, FailedBlockCount: 0);

    [Fact]
    public void Opening_ListsOnlyCompletedRuns()
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[] { Job("run-done", "Completed"), Job("run-failed", "Failed") }, null));

        var page = Render<PreviousRunPicker>();
        Assert.Equal("false", page.Find(".run-picker__trigger").GetAttribute("aria-expanded"));

        page.Find(".run-picker__trigger").Click();

        page.WaitForAssertion(() =>
        {
            var panel = page.Find(".run-picker__panel");
            Assert.Equal("group", panel.GetAttribute("role"));
            Assert.Equal("Previous runs", panel.GetAttribute("aria-label"));
            // The failed run is not listed — only completed runs have deliverables.
            Assert.Single(page.FindAll(".run-picker__run"));
        });
        Assert.Equal("true", page.Find(".run-picker__trigger").GetAttribute("aria-expanded"));
    }

    [Fact]
    public void WhenListingFails_ShowsAnError()
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>())
            .Returns<AccountJobsResponse>(_ => throw new InvalidOperationException("network down"));

        var page = Render<PreviousRunPicker>();
        page.Find(".run-picker__trigger").Click();

        page.WaitForAssertion(() =>
            Assert.Contains("network down", page.Find(".run-picker__error").TextContent));
    }
}
