using System.Runtime.CompilerServices;
using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using static Consultologist.Web.Tests.V31.Package31Fixtures;

namespace Consultologist.Web.Tests.V31;

/// <summary>
/// Task 12 (#1509, D15): the Create page watching a 31 job (record storage 12). The rail lists the package's top-level
/// nodes and sets each from <c>row-finished</c> events (or, on re-attach, the <c>Rows</c> listing); a released deliverable's
/// text is fetched once by its key; a blocked one is named, never shown, and links to History. The job's status is shown
/// as stored, except <c>delivering</c> ("Delivering…"). A 5–30 job keeps today's run view.
/// </summary>
public class ConsultsRunView31Tests : ClientRenderTestContext
{
    private const string JobId = "31aa0000bbbb1111cccc2222dddd3333";
    private const string NoteText = "Dr A. Sample saw Margaret Okafor at Sample Oncology Clinic.";
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    private void Pin31()
    {
        WorkflowService.GetCurrentPackageAsync().Returns(Package(Input("referral", "Referral", true, Plaintext)));
        WorkflowService.GetCurrentPackageContentAsync()
            .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new InvalidOperationException("content unavailable"));
        AIService.GetJobRowsAsync(JobId).Returns(Array.Empty<RecordRowRef>());
    }

    private static ConsultGenerationJobResponse Job31(string status, string? error = null, string? currentKey = null) =>
        new(
            JobId, "user-1", status,
            TotalBlockCount: 0, CompletedBlockCount: 0, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(), FailedBlocks: new Dictionary<string, string>(),
            Success: status == "Completed",
            SchemaVersion: 12,
            StartedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
            WorkflowPackage: "example-nested-note@v2026.12.1",
            RuntimeFailureError: error,
            PackageSpecVersion: 31,
            Rows: 0,
            RowStates: new Dictionary<string, int>(),
            CurrentKey: currentKey);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static RecordRowRef Listed(string key, string section, string row) => new(key, section, Json(row));

    private static RecordRowText Released(string text) =>
        new("deliverable.note", "deliverable", Json("""{"key":"deliverable.note","label":"Note","state":"released","format":"markdown_block","composedHash":"sha256:1"}"""),
            Json(JsonSerializer.Serialize(new { content = text })), false);

    private static ConsultGenerationJobSseEvent RowFinished(string key, string section, string? status) =>
        new("row-finished", JsonSerializer.Serialize(new { key, section, status }), $"{JobId}:row:{key}");

    private static ConsultGenerationJobSseEvent Done(ConsultGenerationJobResponse final) =>
        new("done", JsonSerializer.Serialize(final), $"{JobId}:done");

    private void Stream(params ConsultGenerationJobSseEvent[] events) =>
        AIService.StreamConsultGenerationJobEventsAsync(JobId, Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(call => Events(call.ArgAt<CancellationToken>(2), events));

    /// <summary>The events, then (unless one was <c>done</c>) an open stream until the page lets go, as a live one stays.</summary>
    private static async IAsyncEnumerable<ConsultGenerationJobSseEvent> Events(
        [EnumeratorCancellation] CancellationToken cancellation, ConsultGenerationJobSseEvent[] events)
    {
        foreach (var item in events)
        {
            await Task.Yield();
            yield return item;
            if (item.EventName == "done")
            {
                yield break;
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private IRenderedComponent<Consults> RenderRun() =>
        Render<Consults>(parameters => parameters.Add(page => page.JobId, JobId));

    private static string RailStatus(IRenderedComponent<Consults> page, string key) =>
        page.Find($".run31-rail [data-key='{key}'] .run31-rail__status").TextContent.Trim();

    [Fact]
    public void ARowFinishedEvent_UpdatesTheRail_WithoutAFetch()
    {
        Pin31();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job31("Running"));
        Stream(RowFinished("node.plan", "node", "Completed"), RowFinished("node.sections.paragraphs.draft_paragraph:2/1", "node", "Completed"));

        var page = RenderRun();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Completed", RailStatus(page, "node.plan"));
            Assert.Equal("Completed", RailStatus(page, "node.sections.paragraphs.draft_paragraph:2/1"));
        }, Settle);
        Assert.Equal("2/1", page.Find(".run31-rail [data-key='node.sections.paragraphs.draft_paragraph:2/1'] .node-row__label").TextContent.Trim());
        // the listing is read once, on attaching; an event is applied as it comes, and a node row is never fetched
        AIService.Received(1).GetJobRowsAsync(JobId);
        AIService.DidNotReceiveWithAnyArgs().GetJobRowAsync(default!, default!);
        Assert.Empty(page.FindAll(".consult-run__grid > .run-rail:not(.run31-rail)"));
    }

    [Fact]
    public void TheStatusDelivering_ReadsDelivering_AndTheDocumentIsShown()
    {
        Pin31();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job31("delivering"));
        AIService.GetJobRowsAsync(JobId).Returns([Listed("deliverable.note", "deliverable", """{"key":"deliverable.note","label":"Note","state":"released"}""")]);
        AIService.GetJobRowAsync(JobId, "deliverable.note").Returns(Released(NoteText));
        Stream();

        var page = RenderRun();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Delivering…", page.Find(".run31-status").TextContent.Trim());
            Assert.Contains(NoteText, page.Find(".run31-deliverable[data-key='deliverable.note']").TextContent);
        }, Settle);
        Assert.DoesNotContain(">delivering<", page.Markup);
    }

    [Fact]
    public void WaitingForAnUpdateToFinish_IsShownAsWritten()
    {
        Pin31();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job31("Waiting for an update to finish"));
        Stream();

        var page = RenderRun();

        page.WaitForAssertion(() => Assert.Equal("Waiting for an update to finish", page.Find(".run31-status").TextContent.Trim()), Settle);
    }

    [Fact]
    public void AReleasedDeliverable_ShowsItsText_FetchedByKeyOnce()
    {
        Pin31();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job31("Running"));
        AIService.GetJobRowAsync(JobId, "deliverable.note").Returns(Released(NoteText));
        // a resumed stream replays an event; the text is still fetched once
        Stream(
            RowFinished("deliverable.note", "deliverable", "released"),
            RowFinished("deliverable.note", "deliverable", "released"),
            Done(Job31("Completed")));

        var page = RenderRun();

        page.WaitForAssertion(() =>
        {
            var deliverable = page.Find(".run31-deliverable[data-key='deliverable.note']");
            Assert.Contains(NoteText, deliverable.TextContent);
            Assert.Equal("released", deliverable.QuerySelector(".run31-deliverable__state")!.TextContent.Trim());
            Assert.Equal("Note", deliverable.QuerySelector(".panel-title")!.TextContent.Trim());
            Assert.Equal("Completed", page.Find(".run31-status").TextContent.Trim());
        }, Settle);
        AIService.Received(1).GetJobRowAsync(JobId, "deliverable.note");
    }

    [Fact]
    public void ABlockedDeliverable_IsNamedBlocked_WithALinkToHistory_AndNoText()
    {
        Pin31();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job31("Running"));
        AIService.GetJobRowAsync(JobId, "deliverable.note").Returns(Released(NoteText));
        Stream(RowFinished("deliverable.note", "deliverable", "blocked"), Done(Job31("Completed")));

        var page = RenderRun();

        page.WaitForAssertion(() =>
        {
            var deliverable = page.Find(".run31-deliverable[data-key='deliverable.note']");
            Assert.Equal("blocked", deliverable.QuerySelector(".run31-deliverable__state")!.TextContent.Trim());
            Assert.Equal($"/history/{JobId}", deliverable.QuerySelector("a")!.GetAttribute("href"));
        }, Settle);
        Assert.DoesNotContain(NoteText, page.Markup);
        Assert.Empty(page.FindAll(".run31-deliverable .note-preview"));
        AIService.DidNotReceiveWithAnyArgs().GetJobRowAsync(default!, default!);
    }

    [Fact]
    public void AFailedJob_ShowsItsErrorSentence()
    {
        const string error = "The model call for 'node.plan' failed after 3 attempts. Run the workflow again later.";
        Pin31();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job31("Failed", error: error));

        var page = RenderRun();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Failed", page.Find(".run31-status").TextContent.Trim());
            Assert.Equal(error, page.Find(".run31-error").TextContent.Trim());
        }, Settle);
    }

    [Fact]
    public void Reattaching_ToA31Job_BuildsTheRailFromTheRowsListing()
    {
        Pin31();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job31("Completed"));
        AIService.GetJobRowsAsync(JobId).Returns(
        [
            Listed("deliverable.note", "deliverable", """{"key":"deliverable.note","label":"Note","state":"released"}"""),
            Listed("node.plan", "node", """{"status":"Completed"}"""),
            Listed("node.plan/if:macro.letrozole", "decision", """{"result":"true"}"""),
            Listed("node.sections", "node", """{"status":"Completed"}"""),
            Listed("node.sections.paragraphs.draft_paragraph:1/1", "node", """{"status":"Completed"}"""),
            Listed("node.sections.paragraphs.draft_paragraph:1/2", "node", """{"status":"Failed"}"""),
        ]);
        AIService.GetJobRowAsync(JobId, "deliverable.note").Returns(Released(NoteText));

        var page = RenderRun();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("Completed", RailStatus(page, "node.plan"));
            Assert.Equal("Completed", RailStatus(page, "node.sections"));
            Assert.Equal("Failed", RailStatus(page, "node.sections.paragraphs.draft_paragraph:1/2"));
            Assert.Contains(NoteText, page.Find(".run31-deliverable[data-key='deliverable.note']").TextContent);
        }, Settle);
        Assert.Equal(["1/1", "1/2"], page.FindAll(".run31-rail .node-row--item .node-row__label").Select(label => label.TextContent.Trim()));
        Assert.Empty(page.FindAll(".run31-rail [data-key='node.plan/if:macro.letrozole']"));
        AIService.DidNotReceiveWithAnyArgs().StreamConsultGenerationJobEventsAsync(default!, default!, default, default);
    }

    [Fact]
    public void ADroppedText_SaysA12002_WithItsHash_AndNoEmptyPane()
    {
        Pin31();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job31("Completed"));
        AIService.GetJobRowsAsync(JobId).Returns([Listed("deliverable.note", "deliverable", """{"key":"deliverable.note","state":"released"}""")]);
        AIService.GetJobRowAsync(JobId, "deliverable.note").Returns(new RecordRowText(
            "deliverable.note", "deliverable", Json("""{"key":"deliverable.note","label":"Note","state":"released","composedHash":"sha256:9f2c"}"""), null, true));

        var page = RenderRun();

        page.WaitForAssertion(() =>
            Assert.Equal("The text was deleted 7 days after the job finished. Its hash is kept.", page.Find(".run31-deliverable__deleted").TextContent.Trim()), Settle);
        Assert.Equal("sha256:9f2c", page.Find(".run31-deliverable__hash").TextContent.Trim());
        Assert.Empty(page.FindAll(".run31-deliverable .note-preview"));
    }

    [Fact]
    public void A30Job_UsesTodaysRunView()
    {
        WithPinnedPackage(blocks: new[] { Block("hpi", "History of Present Illness") });
        AIService.GetConsultGenerationJobAsync(JobId).Returns(new ConsultGenerationJobResponse(
            JobId, "user-1", "Completed",
            TotalBlockCount: 1, CompletedBlockCount: 1, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string> { ["hpi"] = NoteText }, FailedBlocks: new Dictionary<string, string>(),
            Success: true, SchemaVersion: 11, PackageSpecVersion: 30));

        var page = RenderRun();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".run-rail")), Settle);
        Assert.Empty(page.FindAll(".run31-rail"));
        Assert.Empty(page.FindAll(".run31-status"));
        AIService.DidNotReceiveWithAnyArgs().GetJobRowsAsync(default!);
    }
}
