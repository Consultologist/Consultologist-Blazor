using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;
using NSubstitute.Extensions;

namespace Consultologist.Web.Tests;

/// <summary>
/// v30 (#1345 C1c, design § 10): the Create page rail draws a running 30 job as the tree the
/// run takes — the snapshot with the live events laid over it, following the run (the item or
/// pass in flight open with its holders, the rest closed), a group you open or close kept as
/// you left it; per-instance counts, a while's pass, not-run items, a let, labelField names.
/// </summary>
public class ConsultsRunRailTreeTests : ClientRenderTestContext
{
    private const string JobId = "80368dea8689491a92760d9fc4e5bd02";
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset At = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    private void WithRunning(
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> rows,
        IReadOnlyList<ConsultLoopInstanceRow>? instances = null,
        IReadOnlyList<ConsultCollectionRoster>? rosters = null,
        Func<CancellationToken, IAsyncEnumerable<ConsultGenerationJobSseEvent>>? stream = null)
    {
        WithPinnedPackage(blocks: new[] { Block("section-instructions:hpi", "History of Present Illness") });
        WorkflowService.Configure().GetCurrentPackageContentAsync().Returns(EditorFixtures.V7());
        AIService.GetConsultGenerationJobAsync(JobId).Returns(RunTreeTests.Record(nodes, rows, instances, rosters, status: "Running") with
        {
            JobId = JobId, StartedAtUtc = DateTimeOffset.UtcNow, Success = false,
        });
        AIService.StreamConsultGenerationJobEventsAsync(JobId, Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<string?>())
            .Returns(call => (stream ?? (ct => Events(ct)))(call.ArgAt<CancellationToken>(2)));
    }

    private IRenderedComponent<Consults> Render() => Render<Consults>(parameters => parameters.Add(page => page.JobId, JobId));

    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    private static string Glyph(AngleSharp.Dom.IElement group) => Text(group.QuerySelector(":scope > summary .node-row__status, :scope > .node-row__status")!);

    /// <summary>The events (each after the gate, when one is given), then an open stream until the page lets go.</summary>
    private static async IAsyncEnumerable<ConsultGenerationJobSseEvent> Events(CancellationToken cancellation, Task? gate = null, params (string Name, string Json)[] events)
    {
        if (gate is not null)
        {
            await gate;
        }

        var n = 0;
        foreach (var (name, json) in events)
        {
            await Task.Yield();
            yield return new ConsultGenerationJobSseEvent(name, json, $"e-{++n}");
        }

        try
        {
            await Task.Delay(Timeout.Infinite, cancellation);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static (string, string) CountResolved(string loop, string path, int count, int total) =>
        ("count-resolved", $$"""{"JobId":"{{JobId}}","LoopId":"{{loop}}","Count":{{count}},"Total":{{total}},"Pending":0,"Path":"{{path}}"}""");

    private static (string, string) ItemStep(string step, string item) =>
        ("item-step", $$"""{"JobId":"{{JobId}}","ItemId":"{{item}}","ItemName":"{{item}}","Step":"{{step}}","Label":"{{step}}","Message":"","CompletedStepCount":1,"TotalStepCount":1}""");

    // ---- a loop inside a loop, mid-run ------------------------------------------------------

    private static Dictionary<string, ConsultGenerationNodeStatus> PartTwoOutlining() => RunTreeTests.Rows(
        ("plan", "Completed"), ("outline:1", "Completed"), ("draft_paragraph:1/1", "Completed"), ("draft_paragraph:1/2", "Completed"), ("outline:2", "Completed"));

    private static readonly ConsultLoopInstanceRow[] FirstInstance = { new("paragraphs", "1", 2, At) };

    [Fact]
    public void ANestedLoopMidRun_FollowsTheRun_DoneItemsClosed_TheRunningOneOpenAndCounting()
    {
        WithRunning(RunTreeTests.TwoLevels(), PartTwoOutlining(), FirstInstance, RunTreeTests.Sections);
        var page = Render();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("details[data-loop='parts']")), Settle);
        var parts = page.Find("details[data-loop='parts']");
        Assert.True(parts.HasAttribute("open"));
        Assert.Equal("1/2", Text(parts.QuerySelector("[data-loop-count]")!));

        var history = page.Find("details[data-item][data-path='1']");
        Assert.False(history.HasAttribute("open"));
        Assert.Equal(("✓", "History of present illness", "3/3 steps"), (Glyph(history), Text(history.QuerySelector(".node-row__label")!), Text(history.QuerySelector("[data-item-summary]")!)));

        var plan = page.Find("details[data-item][data-path='2']");
        Assert.True(plan.HasAttribute("open"));
        Assert.Equal("●", Glyph(plan));
        Assert.Equal("counting…", Text(plan.QuerySelector("details[data-loop='paragraphs'][data-path='2'] [data-loop-count]")!));
    }

    [Fact]
    public void LiveEvents_MoveTheTree_ACountThenAStep_BeforeAnotherSnapshot()
    {
        WithRunning(RunTreeTests.TwoLevels(), PartTwoOutlining(), FirstInstance, RunTreeTests.Sections,
            ct => Events(ct, null, CountResolved("paragraphs", "2", 1, 4), ItemStep("draft_paragraph", "2/1")));
        var page = Render();

        page.WaitForAssertion(() =>
        {
            var plan = page.Find("details[data-item][data-path='2']");
            Assert.Equal("✓", Glyph(plan));
            Assert.Equal("1/1", Text(plan.QuerySelector("details[data-loop='paragraphs'] [data-loop-count]")!));
        }, Settle);
        // Done, it closes as its sibling did.
        Assert.False(page.Find("details[data-item][data-path='2']").HasAttribute("open"));
    }

    [Fact]
    public void AGroupYouCloseOrOpen_StaysAsYouLeftIt_WhenTheNextEventArrives()
    {
        var gate = new TaskCompletionSource();
        WithRunning(RunTreeTests.TwoLevels(), PartTwoOutlining(), FirstInstance, RunTreeTests.Sections,
            ct => Events(ct, gate.Task, CountResolved("paragraphs", "2", 2, 5)));
        var page = Render();
        page.WaitForAssertion(() => Assert.True(page.Find("details[data-item][data-path='2']").HasAttribute("open")), Settle);

        page.Find("details[data-item][data-path='2'] > summary").Click();
        page.Find("details[data-item][data-path='1'] > summary").Click();
        gate.SetResult();

        page.WaitForAssertion(() => Assert.Equal("0/2", Text(page.Find("details[data-loop='paragraphs'][data-path='2'] [data-loop-count]"))), Settle);
        // Still running, yet closed as you left it; the done one open as you left it.
        Assert.Equal("●", Glyph(page.Find("details[data-item][data-path='2']")));
        Assert.False(page.Find("details[data-item][data-path='2']").HasAttribute("open"));
        Assert.True(page.Find("details[data-item][data-path='1']").HasAttribute("open"));
    }

    // ---- a while ----------------------------------------------------------------------------

    [Fact]
    public void AWhileOnItsSecondPass_SaysSo_PassOneClosed_PassTwoOpenWithItsStepInFlight()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("refine", "Refining the summary", Kind: "while", Expression: "true", Body: new[] { "draft", "judge" }, Ret: "draft", Max: 3),
            new("draft", "Draft", In: "refine", Format: "prose", Producer: "attested"),
            new("judge", "Judge", In: "refine", Format: "verdict", Producer: "custom"),
        };
        WithRunning(nodes, RunTreeTests.Rows(("draft:1", "Completed"), ("judge:1", "Completed"), ("draft:2", "Completed")));
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal("pass 2 of at most 3", Text(page.Find("details[data-while='refine'] [data-while-words]"))), Settle);
        var first = page.Find("details[data-pass='1']");
        var second = page.Find("details[data-pass='2']");
        Assert.Equal(("✓", false), (Glyph(first), first.HasAttribute("open")));
        Assert.Equal(("●", true), (Glyph(second), second.HasAttribute("open")));
        Assert.Equal("●", Text(second.QuerySelector("[data-node='judge'] .node-row__status")!));
    }

    // ---- not run, a let ---------------------------------------------------------------------

    [Fact]
    public void ANotRunItem_FromItsBlockSkippedEvent_ReadsSkippedWithItsReason()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "refine" }, Ret: "refine", Count: 3, Until: "item.value.kind == \"plan\""),
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft" }, In: "parts", Max: 1),
            new("draft", "Draft", In: "refine"),
        };
        WithRunning(nodes, RunTreeTests.Rows(("draft:1/1", "Completed"), ("draft:2/1", "Completed")),
            stream: ct => Events(ct, null, ("block-skipped", $$"""{"JobId":"{{JobId}}","BlockId":"consult:parts:3","Reason":"not run: stopped early at item 2 (Parts 2)"}""")));
        var page = Render();

        page.WaitForAssertion(() =>
        {
            var notRun = page.Find("[data-item][data-path='3']");
            Assert.Equal("⊘", Text(notRun.QuerySelector(".node-row__status")!));
            Assert.Equal("not run: stopped early at item 2 (Parts 2)", Text(notRun.QuerySelector("[data-item-summary]")!));
        }, Settle);
    }

    [Fact]
    public void ALet_CompletesOnItsNodeCompletedEvent()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("problem_count", "Counting the problems", Producer: "computed", Op: "let", Let: "1", LetType: "number"),
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft" }, Max: 2),
            new("draft", "Draft", In: "refine"),
        };
        WithRunning(nodes, RunTreeTests.Rows(),
            stream: ct => Events(ct, null, ("node-completed", $$"""{"JobId":"{{JobId}}","NodeId":"problem_count","Label":"Counting the problems","Message":"","CompletedNodeCount":1,"TotalNodeCount":2}""")));
        var page = Render();

        page.WaitForAssertion(() => Assert.Equal("✓", Text(page.Find("[data-node='problem_count'] .node-row__status"))), Settle);
    }

    [Fact]
    public void A29Job_KeepsTheRailItHad()
    {
        WithRunning(RunTreeTests.TwoLevels(), PartTwoOutlining(), FirstInstance, RunTreeTests.Sections);
        AIService.GetConsultGenerationJobAsync(JobId).Returns(RunTreeTests.Record(RunTreeTests.TwoLevels(), PartTwoOutlining(), FirstInstance, RunTreeTests.Sections, status: "Running", schema: 10) with
        {
            JobId = JobId, StartedAtUtc = DateTimeOffset.UtcNow, Success = false,
        });
        var page = Render();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(".node-row")), Settle);
        Assert.Empty(page.FindAll("[data-loop]"));
    }
}
