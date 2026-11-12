using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;

namespace Consultologist.Web.Tests;

/// <summary>
/// v30 (#1345 C1b, design § 10): a storage-11 record as a tree — an inner loop's instance under
/// its outer item with its own count, items in run order and named from the roster, a while's
/// passes and why it stopped, not-run items with their reason, a let, an inner if per item; and
/// which records take the tree at all.
/// </summary>
public class RunTreeTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    internal static ConsultGenerationJobResponse Record(
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> rows,
        IReadOnlyList<ConsultLoopInstanceRow>? instances = null,
        IReadOnlyList<ConsultCollectionRoster>? rosters = null,
        string status = "Completed",
        int schema = 11) =>
        new("job-1", "user-1", status, 1, 1, 0, new Dictionary<string, string>(), new Dictionary<string, string>(), status == "Completed",
            Nodes: nodes, NodeOutputs: rows, Collections: rosters, SchemaVersion: schema, LoopInstances: instances);

    internal static ConsultGenerationNodeStatus Row(string status, string? reason = null) =>
        new("n", "n", status, "in", "out", null, null, Reason: reason);

    internal static Dictionary<string, ConsultGenerationNodeStatus> Rows(params (string Key, string Status)[] rows) =>
        rows.ToDictionary(r => r.Key, r => Row(r.Status), StringComparer.Ordinal);

    internal static ConsultGenerationNodeDescriptor[] TwoLevels() => new ConsultGenerationNodeDescriptor[]
    {
        new("plan", "Plan the sections", Format: "section_plan", Producer: "custom"),
        new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "outline", "paragraphs" }, Ret: "paragraphs", Count: 2, LabelField: "title"),
        new("outline", "Outline the section", In: "parts", Format: "outline", Producer: "custom"),
        new("paragraphs", "Paragraphs", Kind: "for", Collection: "node.outline.value.points", Body: new[] { "draft_paragraph" }, Ret: "draft_paragraph", In: "parts"),
        new("draft_paragraph", "Draft paragraph", In: "paragraphs", Format: "prose", Producer: "attested"),
    };

    internal static readonly ConsultLoopInstanceRow[] TwoInstances =
    {
        new("paragraphs", "1", 2, At),
        new("paragraphs", "2", 1, At),
    };

    internal static readonly ConsultCollectionRoster[] Sections =
    {
        new("node.plan.value.sections", new[] { new ConsultCollectionItem("1", "History of present illness"), new ConsultCollectionItem("2", "Plan") }),
    };

    [Fact]
    public void ALoopInALoop_PutsEachInstanceUnderItsOuterItem_WithItsOwnCount()
    {
        var rows = Rows(("plan", "Completed"), ("parts", "Completed"), ("outline:1", "Completed"), ("outline:2", "Completed"),
            ("draft_paragraph:1/1", "Completed"), ("draft_paragraph:1/2", "Completed"), ("draft_paragraph:2/1", "Completed"));

        var tree = RunTree.Build(Record(TwoLevels(), rows, TwoInstances, Sections));

        var parts = Assert.IsType<RunTree.LoopEntry>(tree[1]);
        Assert.Equal("2", parts.CountText);
        Assert.Equal(new[] { ("1", "History of present illness"), ("2", "Plan") }, parts.Items.Select(i => (i.Path, i.Name)));
        var first = parts.Items[0];
        Assert.Equal((RunTree.Completed, 3, 3), (first.Status, first.Done, first.Steps));
        var inner = Assert.IsType<RunTree.LoopEntry>(first.Children[1]);
        Assert.Equal(("1", "2"), (inner.Path, inner.CountText));
        Assert.Equal(new[] { "1/1", "1/2" }, inner.Items.Select(i => i.Path));
        Assert.Equal(new[] { "2/1" }, Assert.IsType<RunTree.LoopEntry>(parts.Items[1].Children[1]).Items.Select(i => i.Path));
        Assert.Equal("Paragraphs 2", inner.Items[1].Name);
    }

    [Fact]
    public void AnEmptyInstance_SaysWhy_AndAFailedStep_RollsItsItemUp()
    {
        var instances = new[] { new ConsultLoopInstanceRow("paragraphs", "1", 1, At), new ConsultLoopInstanceRow("paragraphs", "2", 0, At, Reason: "no items: node.outline.value.points was empty") };
        var rows = Rows(("outline:1", "Completed"), ("outline:2", "Completed"), ("draft_paragraph:1/1", "Failed"));

        var parts = Assert.IsType<RunTree.LoopEntry>(RunTree.Build(Record(TwoLevels(), rows, instances, Sections))[1]);

        Assert.Equal(RunTree.Failed, parts.Items[0].Status);
        var empty = Assert.IsType<RunTree.LoopEntry>(parts.Items[1].Children[1]);
        Assert.Equal("0 — no items: node.outline.value.points was empty", empty.CountText);
        Assert.Empty(empty.Items);
    }

    [Fact]
    public void Items_ComeInRunOrder_NumbersAsNumbers()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "refine" }, Count: 11),
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft" }, In: "parts", Max: 2),
            new("draft", "Draft", In: "refine"),
        };
        var rows = Enumerable.Range(1, 11).ToDictionary(n => $"draft:{n}/1", _ => Row("Completed"), StringComparer.Ordinal);

        var parts = Assert.IsType<RunTree.LoopEntry>(RunTree.Build(Record(nodes, rows))[0]);

        Assert.Equal(Enumerable.Range(1, 11).Select(n => n.ToString()), parts.Items.Select(i => i.Path));
    }

    [Fact]
    public void AnItemTheLoopDidNotRun_IsSkippedWithItsReason()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "draft" }, Ret: "draft", Count: 3, Until: "true", Stopped: "until", StopReason: "its until held at item 2 (Parts 2)"),
            new("draft", "Draft", In: "parts"),
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "x" }, Max: 1),
            new("x", "X", In: "refine"),
        };
        var rows = new Dictionary<string, ConsultGenerationNodeStatus>(StringComparer.Ordinal)
        {
            ["draft:1"] = Row("Completed"), ["draft:2"] = Row("Completed"),
            ["parts:3"] = Row("Skipped", "not run: stopped early at item 2 (Parts 2)"),
        };

        var parts = Assert.IsType<RunTree.LoopEntry>(RunTree.Build(Record(nodes, rows))[0]);

        Assert.Equal("its until held at item 2 (Parts 2)", parts.StopText);
        Assert.Equal((RunTree.Skipped, "not run: stopped early at item 2 (Parts 2)"), (parts.Items[2].Status, parts.Items[2].Reason));
    }

    [Theory]
    [InlineData(3, "bound", "3 passes — stopped at its bound")]
    [InlineData(2, "check", "2 passes — the check stopped holding")]
    [InlineData(1, "until", "1 pass — its until held")]
    public void AStoppedWhile_SaysItsPassesAndWhy_EachPassItsRows(int passes, string stopped, string words)
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft", "judge" }, Ret: "draft", Max: 3, Passes: passes, Stopped: stopped),
            new("draft", "Draft", In: "refine"),
            new("judge", "Judge", In: "refine"),
        };
        var rows = Enumerable.Range(1, passes).SelectMany(n => new[] { ($"draft:{n}", "Completed"), ($"judge:{n}", "Completed") }).ToArray();

        var refine = Assert.IsType<RunTree.WhileEntry>(RunTree.Build(Record(nodes, Rows(rows)))[0]);

        Assert.Equal(words, refine.Words);
        Assert.Equal(Enumerable.Range(1, passes), refine.Passes.Select(p => p.Number));
        Assert.All(refine.Passes, pass => Assert.Equal((RunTree.Completed, 2, 2), (pass.Status, pass.Done, pass.Steps)));
    }

    [Fact]
    public void ARunningWhile_SaysWhichPassOfItsBound_AndAWhileInALoop_ReadsItsInstance()
    {
        var running = new ConsultGenerationNodeDescriptor[]
        {
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft" }, Max: 3),
            new("draft", "Draft", In: "refine"),
        };
        var refine = Assert.IsType<RunTree.WhileEntry>(RunTree.Build(Record(running, Rows(("draft:1", "Completed"), ("draft:2", "Running")), status: "Running"))[0]);
        Assert.Equal("pass 2 of at most 3", refine.Words);
        Assert.Equal(RunTree.Running, refine.Passes[1].Status);

        var inALoop = new ConsultGenerationNodeDescriptor[]
        {
            new("parts", "Parts", Kind: "for", Collection: "input.parts", Body: new[] { "refine" }, Count: 1),
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft" }, In: "parts", Max: 2),
            new("draft", "Draft", In: "refine"),
        };
        var parts = Assert.IsType<RunTree.LoopEntry>(RunTree.Build(Record(inALoop, Rows(("draft:1/1", "Completed"), ("draft:1/2", "Completed")),
            new[] { new ConsultLoopInstanceRow("refine", "1", null, At, Passes: 2, Stopped: "bound") }))[0]);
        var nested = Assert.IsType<RunTree.WhileEntry>(Assert.Single(parts.Items).Children[0]);
        Assert.Equal(("1", "2 passes — stopped at its bound"), (nested.Path, nested.Words));
        Assert.Equal(new[] { "1/1", "1/2" }, nested.Passes.Select(p => p.Path));
    }

    [Fact]
    public void ALet_IsANodeEntry_ThatKnowsItIsALet()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("problem_count", "Counting", Producer: "computed", Op: "let", Let: "1", LetType: "number"),
        };

        var let = Assert.IsType<RunTree.NodeEntry>(Assert.Single(RunTree.Build(Record(nodes, Rows(("problem_count", "Completed"))))));

        Assert.True(let.IsLet);
        Assert.Equal(RunTree.Completed, let.Row!.Status);
    }

    [Fact]
    public void AnInnerIf_ReadsTheBranchItTookForTheItem_OrWhyItTookNone()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "outline", "paragraphs", "triage" }, Count: 2),
            new("outline", "Outline", In: "parts"),
            new("paragraphs", "Paragraphs", Kind: "for", Collection: "node.outline.value.points", Body: new[] { "d" }, In: "parts"),
            new("d", "D", In: "paragraphs"),
            new("triage", "Triage", Kind: "if", Expression: "item.value.kind == \"history\"", Body: new[] { "deep" }, In: "parts",
                Branches: new Dictionary<string, string> { ["1"] = "then", ["2"] = "none" }),
            new("deep", "Deep", In: "triage"),
        };
        var rows = new Dictionary<string, ConsultGenerationNodeStatus>(StringComparer.Ordinal)
        {
            ["outline:1"] = Row("Completed"), ["outline:2"] = Row("Completed"),
            ["deep:1"] = Row("Completed"),
            ["triage:2"] = Row("Skipped", "needs `item.value.kind == \"history\"`; it does not hold"),
        };

        var parts = Assert.IsType<RunTree.LoopEntry>(RunTree.Build(Record(nodes, rows))[0]);

        Assert.Equal("then → deep", Assert.IsType<RunTree.FlowEntry>(parts.Items[0].Children[2]).BranchText);
        Assert.StartsWith("skipped — needs", Assert.IsType<RunTree.FlowEntry>(parts.Items[1].Children[2]).BranchText);
    }

    [Fact]
    public void OnlyAStorage11RecordWithA30Shape_TakesTheTree()
    {
        Assert.True(RunTree.Applies(Record(TwoLevels(), Rows())));
        Assert.False(RunTree.Applies(Record(TwoLevels(), Rows(), schema: 10)));
        // A storage-11 record whose loops are one level deep keeps its flat groups.
        Assert.False(RunTree.Applies(Record(TwoLevels().Take(3).Select(n => n with { Body = n.Id == "parts" ? new[] { "outline" } : n.Body }).ToArray(), Rows())));
    }

    [Fact]
    public void AnItemNothingOfWhichRan_IsPending_NeverSkipped_AndARunningItemsFirstUnrunStep_IsInFlight()
    {
        // #1345 C1c: the rail reads a running record — part 2 has not started; part 1 is mid-way.
        var rows = Rows(("outline:1", "Completed"));
        var parts = Assert.IsType<RunTree.LoopEntry>(RunTree.Build(Record(TwoLevels(), rows, new[] { new ConsultLoopInstanceRow("paragraphs", "1", 1, At) }, Sections, status: "Running"))[1]);

        Assert.Equal(RunTree.Running, parts.Items[0].Status);
        Assert.Equal(RunTree.Pending, parts.Items[1].Status);
        var inner = Assert.IsType<RunTree.LoopEntry>(parts.Items[0].Children[1]);
        Assert.Equal(RunTree.Pending, inner.Items.Single().Status);

        // A pass: the first step with no row is the one in flight, the rest pending.
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft", "judge", "note" }, Max: 3),
            new("draft", "Draft", In: "refine"), new("judge", "Judge", In: "refine"), new("note", "Note", In: "refine"),
        };
        var refine = Assert.IsType<RunTree.WhileEntry>(RunTree.Build(Record(nodes, Rows(("draft:1", "Completed")), status: "Running"))[0]);
        var steps = refine.Passes.Single().Children.Cast<RunTree.NodeEntry>().Select(n => n.Status);
        Assert.Equal(new[] { RunTree.Completed, RunTree.Running, RunTree.Pending }, steps);

        // On a finished record nothing is in flight: what never ran stays pending.
        var finished = Assert.IsType<RunTree.WhileEntry>(RunTree.Build(Record(nodes, Rows(("draft:1", "Completed")), status: "Failed"))[0]);
        Assert.Equal(new[] { RunTree.Completed, RunTree.Pending, RunTree.Pending }, finished.Passes.Single().Children.Cast<RunTree.NodeEntry>().Select(n => n.Status));
    }
}
