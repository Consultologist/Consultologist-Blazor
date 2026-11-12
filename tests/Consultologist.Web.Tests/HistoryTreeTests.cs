using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Accounts;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Workflow;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v30 (#1345 C1b, design § 10): History draws a storage-11 record as the tree the run took —
/// an inner loop's instance under its outer item with its own count, items and passes closed
/// with a rolled-up summary, a while's words, not-run items with their reason, a let's computed
/// row; a storage-10 record keeps its flat groups.
/// </summary>
public class HistoryTreeTests : ClientRenderTestContext
{
    private const string JobId = "0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset At = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    private void WithRecord(
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> rows,
        IReadOnlyList<ConsultLoopInstanceRow>? instances = null,
        IReadOnlyList<ConsultCollectionRoster>? rosters = null,
        int schema = 11)
    {
        WorkflowService.GetEngineAsync().Returns(new EngineView("9c1ca4ac9373f1dc01e1fec68772304ef7d23ca6", "v2026.08.6", "v2026.08.3"));
        // Terminal only: a non-terminal row would start the page's polling.
        AccountService.GetJobsAsync(Arg.Any<int>(), Arg.Any<string?>()).Returns(new AccountJobsResponse(
            new[] { new AccountJobSummaryResponse(JobId, "Completed", At, At, At, TotalBlockCount: 3, CompletedBlockCount: 3, FailedBlockCount: 0) },
            null));
        AIService.GetConsultGenerationJobAsync(JobId).Returns(RunTreeTests.Record(nodes, rows, instances, rosters, schema: schema) with
        {
            JobId = JobId, EffectiveInputHash = "aaaa", EffectiveInputHashVersion = 6, WorkflowOutputHash = "bbbb", WorkflowOutputHashVersion = 6, PackageSpecVersion = schema >= 11 ? 30 : 29,
        });
    }

    private IRenderedComponent<History> Render() => Render<History>(parameters => parameters.Add(p => p.JobId, JobId));

    private static string Text(AngleSharp.Dom.IElement element) => System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    private static Dictionary<string, ConsultGenerationNodeStatus> TwoLevelRows() => RunTreeTests.Rows(
        ("plan", "Completed"), ("parts", "Completed"), ("outline:1", "Completed"), ("outline:2", "Completed"),
        ("draft_paragraph:1/1", "Completed"), ("draft_paragraph:1/2", "Completed"), ("draft_paragraph:2/1", "Completed"));

    [Fact]
    public void ALoopInALoop_DrawsEachInstanceUnderItsOuterItem_ItemsClosed_TheLoopOpen()
    {
        WithRecord(RunTreeTests.TwoLevels(), TwoLevelRows(), RunTreeTests.TwoInstances, RunTreeTests.Sections);
        var page = Render();

        var parts = page.Find("details[data-loop='parts']:not([data-path])");
        Assert.True(parts.HasAttribute("open"));
        Assert.Equal("for item in node.plan.value.sections (2)", Text(parts.QuerySelector("[data-loop-count]")!));

        var items = parts.QuerySelectorAll(":scope > details[data-item]").ToList();
        Assert.Equal(new[] { "1", "2" }, items.Select(i => i.GetAttribute("data-path")));
        Assert.All(items, item => Assert.False(item.HasAttribute("open")));
        Assert.Equal("History of present illness", Text(items[0].QuerySelector(".node-row__label")!));
        Assert.Equal("3/3 steps", Text(items[0].QuerySelector("[data-item-summary]")!));

        // Each part's paragraphs, under that part, with its own count.
        var first = items[0].QuerySelector("details[data-loop='paragraphs'][data-path='1']")!;
        Assert.Equal("for item in node.outline.value.points (2)", Text(first.QuerySelector("[data-loop-count]")!));
        Assert.Equal(new[] { "1/1", "1/2" }, first.QuerySelectorAll("[data-item]").Select(i => i.GetAttribute("data-path")));
        Assert.Equal("for item in node.outline.value.points (1)", Text(items[1].QuerySelector("details[data-loop='paragraphs'][data-path='2'] [data-loop-count]")!));
        // A body node's row under its item, with its hashes.
        Assert.Contains("in in", Text(items[0].QuerySelector("[data-node='outline'][data-path='1']")!));
    }

    [Fact]
    public void AnEmptyInstance_SaysWhy()
    {
        WithRecord(RunTreeTests.TwoLevels(), RunTreeTests.Rows(("outline:1", "Completed"), ("outline:2", "Completed"), ("draft_paragraph:1/1", "Completed")),
            new[] { new ConsultLoopInstanceRow("paragraphs", "1", 1, At), new ConsultLoopInstanceRow("paragraphs", "2", 0, At, Reason: "no items: node.outline.value.points was empty") },
            RunTreeTests.Sections);
        var page = Render();

        Assert.Equal("for item in node.outline.value.points (0 — no items: node.outline.value.points was empty)",
            Text(page.Find("details[data-loop='paragraphs'][data-path='2'] [data-loop-count]")));
    }

    [Theory]
    [InlineData(3, "bound", "3 passes — stopped at its bound")]
    [InlineData(2, "check", "2 passes — the check stopped holding")]
    public void AWhile_SaysItsPassesAndWhy_EachPassClosedWithItsRows(int passes, string stopped, string words)
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("refine", "Refining the summary", Kind: "while", Expression: "node.judge.value.verdict != \"good\"", Body: new[] { "draft", "judge" }, Ret: "draft", Max: 3, Passes: passes, Stopped: stopped),
            new("draft", "Draft", In: "refine", Format: "prose", Producer: "attested"),
            new("judge", "Judge", In: "refine", Format: "verdict", Producer: "custom"),
        };
        var rows = Enumerable.Range(1, passes).SelectMany(n => new[] { ($"draft:{n}", "Completed"), ($"judge:{n}", "Completed") }).Append(("refine", "Completed")).ToArray();
        WithRecord(nodes, RunTreeTests.Rows(rows));
        var page = Render();

        var refine = page.Find("details[data-while='refine']");
        Assert.Equal($"while node.judge.value.verdict != \"good\" · {words}", Text(refine.QuerySelector("[data-while-words]")!));
        var passRows = refine.QuerySelectorAll("details[data-pass]").ToList();
        Assert.Equal(Enumerable.Range(1, passes).Select(n => n.ToString()), passRows.Select(p => p.GetAttribute("data-pass")));
        Assert.All(passRows, pass => Assert.False(pass.HasAttribute("open")));
        Assert.Equal("Pass 2", Text(passRows[1].QuerySelector(".node-row__label")!));
        Assert.NotNull(passRows[1].QuerySelector("[data-node='judge'][data-path='2']"));
    }

    [Fact]
    public void AnItemTheLoopDidNotRun_ReadsSkippedWithItsReason()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "refine" }, Ret: "refine", Count: 3, Until: "item.value.kind == \"plan\"", Stopped: "until", StopReason: "its until held at item 2 (Plan)"),
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft" }, In: "parts", Max: 1),
            new("draft", "Draft", In: "refine", Format: "prose", Producer: "attested"),
        };
        var rows = new Dictionary<string, ConsultGenerationNodeStatus>(StringComparer.Ordinal)
        {
            ["draft:1/1"] = RunTreeTests.Row("Completed"), ["draft:2/1"] = RunTreeTests.Row("Completed"),
            ["parts:3"] = RunTreeTests.Row("Skipped", "not run: stopped early at item 2 (Plan)"),
        };
        WithRecord(nodes, rows);
        var page = Render();

        var notRun = page.Find("[data-item][data-path='3']");
        Assert.Contains("node-row--skipped", notRun.ClassName);
        Assert.Equal("not run: stopped early at item 2 (Plan)", Text(notRun.QuerySelector("[data-item-summary]")!));
        Assert.Equal("· its until held at item 2 (Plan)", Text(page.Find("[data-loop-stop]")));
    }

    [Fact]
    public void AFailedStep_RollsItsItemUpToFailed_AndShowsItsStack()
    {
        var rows = TwoLevelRows();
        rows["draft_paragraph:1/2"] = new("draft_paragraph", "Draft paragraph", "Failed", "in", null, null, "The model refused.", ErrorType: "InvalidOperationException", ErrorStack: "at Draft()");
        WithRecord(RunTreeTests.TwoLevels(), rows, RunTreeTests.TwoInstances, RunTreeTests.Sections);
        var page = Render();

        var item = page.Find("details[data-item][data-path='1']");
        Assert.Equal("✗", Text(item.QuerySelector(":scope > summary .node-row__status")!));
        Assert.Contains("at Draft()", Text(page.Find("details[data-item][data-path='1/2'] .node-stack__frames")));
    }

    [Fact]
    public void ALet_IsAComputedRow_NamingItsType_FlaggedWhenItsValueMissedIt()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("problem_count", "Counting the problems", Producer: "computed", Op: "let", Let: "node.find_problems.value.problems | array.size", LetType: "number"),
            new("summary", "Summary", Format: "problem_list", Producer: "computed", Op: "let", Let: "node.find_problems.value", LetType: "problem_list"),
        };
        var rows = new Dictionary<string, ConsultGenerationNodeStatus>(StringComparer.Ordinal)
        {
            ["problem_count"] = new("problem_count", "Counting the problems", "Completed", "7a1e9f", "d4735e", At, null, Producer: "computed"),
            ["summary"] = new("summary", "Summary", "Completed", "i", "o", At, null, Producer: "computed",
                Validation: new ConsultValidationOutcome(false, new[] { "Let 'summary''s value does not match format problem_list: missing summary." })),
        };
        WithRecord(nodes, rows);
        var page = Render();

        var count = page.Find("[data-node='problem_count']");
        Assert.Equal("computed · let → number", Text(count.QuerySelector(".node-row__contract")!));
        Assert.Contains("out d4735e", Text(count));
        Assert.Equal("computed · let → problem_list", Text(page.Find("[data-node='summary'] .node-row__contract")));
        Assert.Contains("missing summary", page.Find("[data-node='summary'] .node-row__flag").GetAttribute("title"));
        Assert.Empty(count.QuerySelectorAll(".node-row__flag"));
    }

    [Fact]
    public void AStorage10Record_KeepsItsFlatGroups()
    {
        var nodes = RunTreeTests.TwoLevels();
        WithRecord(nodes, TwoLevelRows(), RunTreeTests.TwoInstances, RunTreeTests.Sections, schema: 10);
        var page = Render();

        Assert.Empty(page.FindAll("[data-loop]"));
        Assert.NotEmpty(page.FindAll(".node-group summary"));
    }
}
