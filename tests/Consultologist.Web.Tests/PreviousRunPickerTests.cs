using System;
using Bunit;
using System.Text.Json;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Tests.V31;
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
            // #893: a FluentMenu now — the accessible name sits on the menu's anchored
            // region, the menu role is set by the web component at runtime.
            Assert.NotNull(page.Find("[aria-label='Previous runs']"));
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

    // #1543, ruling 9: a storage 12 run's released deliverables, from its rows.
    private const string Storage12Job = "run-31";

    private void GivenAStorage12Run(params RecordRowRef[] rows)
    {
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>())
            .Returns(new AccountJobsResponse(new[] { Job(Storage12Job, "Completed") }, null));
        AIService.GetConsultGenerationJobAsync(Storage12Job).Returns(Record31Fixtures.Header() with { JobId = Storage12Job });
        AIService.GetJobRowsAsync(Storage12Job).Returns(rows);
    }

    private static RecordRowRef Deliverable(string id, RowState state, string label) =>
        Record31Fixtures.NodeRow("deliverable." + id, PlanKind.Deliverable, state,
            state == RowState.Released ? null : "held: empty", detail: $$"""{ "label": "{{label}}" }""");

    private IRenderedComponent<PreviousRunPicker> OpenTheRun(LoadedDeliverable[]? chosen = null)
    {
        var page = Render<PreviousRunPicker>(p => p.Add(x => x.OnChosen,
            Microsoft.AspNetCore.Components.EventCallback.Factory.Create<LoadedDeliverable>(this, d => { chosen?.SetValue(d, 0); })));
        page.Find(".run-picker__trigger").Click();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".run-picker__run")));
        page.Find(".run-picker__run-toggle").Click();
        return page;
    }

    private static RecordRowText TextOf(string id, string? content) =>
        new("deliverable." + id, "deliverable", JsonSerializer.SerializeToElement(new { }),
            content == null ? null : JsonSerializer.SerializeToElement(new { content }), content == null);

    [Fact]
    public void AStorage12Run_ListsItsReleasedDeliverables_ByLabel()
    {
        GivenAStorage12Run(
            Deliverable("emr.note", RowState.Released, "Consultation note for Margaret Okafor"),
            Deliverable("blocked_one", RowState.Blocked, "Refused letter"),
            Deliverable("held_one", RowState.Held, "Held summary"));

        var page = OpenTheRun();

        page.WaitForAssertion(() =>
        {
            var items = page.FindAll(".run-picker__deliverable");
            Assert.Equal("Consultation note for Margaret Okafor", Assert.Single(items).TextContent.Trim());
        });
    }

    [Fact]
    public void ChoosingOne_FetchesItsText_AndGivesItsReference()
    {
        GivenAStorage12Run(Deliverable("emr.note", RowState.Released, "Consultation note"));
        AIService.GetJobRowAsync(Storage12Job, "deliverable.emr.note").Returns(TextOf("emr.note", "Dear Dr A. Sample, ..."));
        var chosen = new LoadedDeliverable[1];
        var page = OpenTheRun(chosen);
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".run-picker__deliverable")));

        page.Find(".run-picker__deliverable").Click();

        page.WaitForAssertion(() => Assert.NotNull(chosen[0]));
        Assert.Equal(new LoadedDeliverable(Storage12Job, "emr.note", "Consultation note", When, "Dear Dr A. Sample, ..."), chosen[0]);
        Assert.Equal(new ConsultInputRef(Storage12Job, "emr.note"), chosen[0].Reference);
    }

    [Fact]
    public void AStorage12Run_WithNoReleasedDeliverable_SaysWhatA5To30RunSays()
    {
        GivenAStorage12Run(Deliverable("held_one", RowState.Held, "Held summary"));

        var page = OpenTheRun();

        page.WaitForAssertion(() => Assert.Contains("This run lists no deliverables.", page.Markup));
        Assert.Empty(page.FindAll(".run-picker__deliverable"));
    }

    [Fact]
    public void AChosenDeliverable_WhoseTextWasDropped_SaysSo_AndChoosesNothing()
    {
        GivenAStorage12Run(Deliverable("emr.note", RowState.Released, "Consultation note"));
        AIService.GetJobRowAsync(Storage12Job, "deliverable.emr.note").Returns(TextOf("emr.note", null));
        var chosen = new LoadedDeliverable[1];
        var page = OpenTheRun(chosen);
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".run-picker__deliverable")));

        page.Find(".run-picker__deliverable").Click();

        page.WaitForAssertion(() => Assert.Contains("was deleted", page.Markup));
        Assert.Null(chosen[0]);
    }
}
