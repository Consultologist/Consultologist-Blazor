using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using NSubstitute.Extensions;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1332: History's "watch live" opens /create/&lt;jobId&gt; for a run the server
/// still calls Scheduled. The page treated every non-terminal job as running:
/// "Generating…", an elapsed readout counted from page load, the first node
/// marked running, and the generic heading where the package's deliverable
/// label belongs.
/// </summary>
public class ConsultsScheduledReattachTests : ClientRenderTestContext
{
    private const string JobId = "80368dea8689491a92760d9fc4e5bd01";
    private const string PackageRef = "example-problem-list@v2026.11.1";
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    private void WithTwoNodePackage()
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });
        WorkflowService.Configure().GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());
    }

    private static ConsultGenerationJobResponse Job(
        string status,
        DateTimeOffset? scheduledAtUtc = null,
        DateTimeOffset? startedAtUtc = null,
        string? workflowPackage = null) =>
        new(
            JobId, "user-1", status,
            TotalBlockCount: 1, CompletedBlockCount: 0, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(), FailedBlocks: new Dictionary<string, string>(),
            Success: false,
            StartedAtUtc: startedAtUtc,
            WorkflowPackage: workflowPackage,
            ScheduledAtUtc: scheduledAtUtc);

    private IRenderedComponent<Consults> RenderRun() =>
        Render<Consults>(parameters => parameters.Add(page => page.JobId, JobId));

    private static IEnumerable<string> RailGlyphs(IRenderedComponent<Consults> page) =>
        page.FindAll(".node-row__status").Select(status => status.TextContent.Trim());

    [Fact]
    public void AScheduledJob_SaysWhenItRuns_WithNoGeneratingReadout_AndNoNodeRunning()
    {
        WithTwoNodePackage();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job("Scheduled", scheduledAtUtc: DateTimeOffset.UtcNow.AddHours(3)));

        var page = RenderRun();

        page.WaitForAssertion(() => Assert.Contains("Scheduled to run at", page.Markup), Settle);
        Assert.Empty(page.FindAll(".generating-status"));
        Assert.DoesNotContain("Generating", page.Find(".empty-note-state").TextContent);
        Assert.NotEmpty(page.FindAll(".node-row"));
        Assert.DoesNotContain("●", RailGlyphs(page));
    }

    [Fact]
    public void AScheduledJobThatStarts_SwitchesToTheRunningView_CountingFromItsStart()
    {
        WithTwoNodePackage();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(
            Job("Scheduled", scheduledAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5)),
            Job("Running", scheduledAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5), startedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-2)));

        var page = RenderRun();

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Scheduled to run at", page.Markup);
            Assert.StartsWith("2:0", page.Find(".generating-status").TextContent.Trim());
            Assert.Contains("●", RailGlyphs(page));
        }, Settle);
    }

    [Fact]
    public void AScheduledJob_SwitchesOnAHeartbeatThatSaysRunning()
    {
        WithTwoNodePackage();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job("Scheduled", scheduledAtUtc: DateTimeOffset.UtcNow.AddSeconds(-5)));
        AIService.StreamConsultGenerationJobEventsAsync(JobId, Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(Heartbeat("Running"));

        var page = RenderRun();

        page.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Scheduled to run at", page.Markup);
            Assert.NotEmpty(page.FindAll(".generating-status"));
        }, Settle);
    }

    [Fact]
    public void ARunningJob_CountsFromItsStart_NotFromWhenThePageOpened()
    {
        WithTwoNodePackage();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job("Running", startedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-3)));

        var page = RenderRun();

        page.WaitForAssertion(() => Assert.StartsWith("3:0", page.Find(".generating-status").TextContent.Trim()), Settle);
    }

    [Fact]
    public void TheHeading_IsTheJobsOwnPackagesDeliverable_NotThePins()
    {
        WithTwoNodePackage();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job("Scheduled", scheduledAtUtc: DateTimeOffset.UtcNow.AddHours(3), workflowPackage: PackageRef));
        using var manifest = JsonDocument.Parse("""{"deliverables":[{"id":"note","node":"assemble","label":"Problem-oriented note"}]}""");
        WorkflowService.Configure().GetCurrentPackageContentAsync(PackageRef).Returns(
            new WorkflowPackageContentResponse("example-problem-list", "v2026.11.1", 29, manifest.RootElement.Clone(), new Dictionary<string, string>()));

        var page = RenderRun();

        page.WaitForAssertion(() => Assert.Contains("Problem-oriented note", page.Find(".empty-note-state").TextContent), Settle);
        Assert.DoesNotContain("Consultation note", page.Find(".empty-note-state").TextContent);
    }

    [Fact]
    public void TheHeading_StaysGeneric_WhenTheJobsPackageCannotBeFetched()
    {
        WithTwoNodePackage();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job("Scheduled", scheduledAtUtc: DateTimeOffset.UtcNow.AddHours(3), workflowPackage: PackageRef));
        WorkflowService.Configure().GetCurrentPackageContentAsync(PackageRef)
            .Returns<Task<WorkflowPackageContentResponse>>(_ => throw new InvalidOperationException("unavailable"));

        var page = RenderRun();

        page.WaitForAssertion(() => Assert.Contains("Scheduled to run at", page.Markup), Settle);
        Assert.Contains("Consultation note", page.Find(".empty-note-state").TextContent);
    }

    [Fact]
    public void ANestedLoopsCountResolved_KeepsTheTotalAFloor_UntilEveryInstanceCanHaveSettled()
    {
        // v30 (#1345): two instances of a loop inside a loop settle live; the snapshot still lists
        // the loop as unresolved, so the total stays a floor ("+"), as it would not for a 29 loop.
        WithTwoNodePackage();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job("Running", startedAtUtc: DateTimeOffset.UtcNow) with { UnresolvedLoops = new[] { "paragraphs" } });
        AIService.StreamConsultGenerationJobEventsAsync(JobId, Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(call => CountResolved(call.ArgAt<CancellationToken>(2), ("paragraphs", "1", 2, 4), ("paragraphs", "2", 1, 5)));

        var page = RenderRun();

        page.WaitForAssertion(() => Assert.Contains("of 5+ sections complete", page.Markup), Settle);
    }

    [Fact]
    public void ATopLevelCountResolved_EndsTheFloor_AsAt29()
    {
        WithTwoNodePackage();
        AIService.GetConsultGenerationJobAsync(JobId).Returns(Job("Running", startedAtUtc: DateTimeOffset.UtcNow) with { UnresolvedLoops = new[] { "problems" } });
        AIService.StreamConsultGenerationJobEventsAsync(JobId, Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(call => CountResolved(call.ArgAt<CancellationToken>(2), ("problems", null, 3, 4)));

        var page = RenderRun();

        page.WaitForAssertion(() => Assert.Contains("of 4 sections complete", page.Markup), Settle);
        Assert.DoesNotContain("of 4+ sections", page.Markup);
    }

    /// <summary>The events, then an open stream (as a live one stays) until the page lets go — a stream that ended would re-poll the snapshot.</summary>
    private static async IAsyncEnumerable<ConsultGenerationJobSseEvent> CountResolved(CancellationToken cancellation, params (string Loop, string? Path, int Count, int Total)[] events)
    {
        var n = 0;
        foreach (var (loop, path, count, total) in events)
        {
            await Task.Yield();
            var pathJson = path is null ? "null" : $"\"{path}\"";
            yield return new ConsultGenerationJobSseEvent("count-resolved",
                $$"""{"JobId":"{{JobId}}","LoopId":"{{loop}}","Count":{{count}},"Total":{{total}},"Pending":0,"Path":{{pathJson}}}""", $"cr-{++n}");
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async IAsyncEnumerable<ConsultGenerationJobSseEvent> Heartbeat(string status)
    {
        await Task.Yield();
        yield return new ConsultGenerationJobSseEvent("heartbeat", $$"""{"JobId":"{{JobId}}","Status":"{{status}}"}""", "hb-1");
    }
}
