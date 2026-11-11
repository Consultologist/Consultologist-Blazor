using System.Text.RegularExpressions;
using Consultologist.Web.Services.AI;
using Consultologist.Web.Services.Provenance;

namespace Consultologist.Web.Tests;

/// <summary>
/// v30 (#1345, design § 10): the run diagram over a storage-11 record — a loop inside a loop
/// (to three levels), each flow its own subgraph inside its owner's, every node declared once
/// and inside its own box; a while labelled with its bound, its passes and why it stopped; a
/// let drawn as a computed subroutine; an until's unrun items as "not run".
/// </summary>
public class RunDagDiagramV30Tests
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);

    private static ConsultGenerationJobResponse Job(
        IReadOnlyList<ConsultGenerationNodeDescriptor> nodes,
        IReadOnlyDictionary<string, ConsultGenerationNodeStatus> outputs,
        IReadOnlyList<ConsultLoopInstanceRow>? instances = null,
        string status = "Completed") =>
        new("job-1", "user-1", status,
            TotalBlockCount: 1, CompletedBlockCount: 0, FailedBlockCount: 0,
            GeneratedBlocks: new Dictionary<string, string>(), FailedBlocks: new Dictionary<string, string>(),
            Success: status == "Completed",
            Nodes: nodes, NodeOutputs: outputs, LoopInstances: instances);

    private static ConsultGenerationNodeStatus Row(string id, string status, string? reason = null) =>
        new(id, id, status, "i", "o", null, null, Reason: reason);

    private static Dictionary<string, ConsultGenerationNodeStatus> Rows(params (string Key, string Status)[] rows) =>
        rows.ToDictionary(r => r.Key, r => Row(r.Key, r.Status), StringComparer.Ordinal);

    private static string SubgraphText(string diagram, string subgraphId)
    {
        var start = diagram.IndexOf($"subgraph {subgraphId}[", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no subgraph {subgraphId}");
        var lines = diagram[start..].Split('\n');
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            depth += lines[i].StartsWith("subgraph ", StringComparison.Ordinal) ? 1 : lines[i] == "end" ? -1 : 0;
            if (depth == 0)
            {
                return string.Join('\n', lines[..(i + 1)]) + "\n";
            }
        }

        throw new Xunit.Sdk.XunitException($"subgraph {subgraphId} is never closed");
    }

    private static void DeclaredOnce(string diagram, string id) =>
        Assert.Single(Regex.Matches(diagram, $@"(?m)^{id}(\[|\{{)"));

    // ---- a loop inside a loop -----------------------------------------------------------------

    private static ConsultGenerationNodeDescriptor[] ThreeLevels() => new ConsultGenerationNodeDescriptor[]
    {
        new("plan", "Plan the sections", Format: "section_plan", Producer: "custom"),
        new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "outline", "paragraphs" }, Ret: "paragraphs", Count: 2, Reads: new[] { "plan" }),
        new("outline", "Outline", In: "parts", Format: "outline", Producer: "custom"),
        new("paragraphs", "Paragraphs", Kind: "for", Collection: "node.outline.value.points", Body: new[] { "draft_paragraph", "sentences" }, Ret: "sentences", In: "parts", Reads: new[] { "outline" }),
        new("draft_paragraph", "Draft paragraph", In: "paragraphs", Format: "prose", Producer: "attested"),
        new("sentences", "Sentences", Kind: "for", Collection: "input.questions", Body: new[] { "draft_sentence" }, Ret: "draft_sentence", In: "paragraphs"),
        new("draft_sentence", "Draft sentence", In: "sentences", Format: "prose", Producer: "attested"),
    };

    [Fact]
    public void ALoopInALoopInALoop_NestsEachSubgraph_AndDeclaresEveryNodeOnceInsideItsOwnBox()
    {
        var outputs = Rows(("plan", "Completed"), ("parts", "Completed"), ("outline:1", "Completed"), ("outline:2", "Completed"),
            ("paragraphs", "Completed"), ("draft_paragraph:1/1", "Completed"), ("draft_paragraph:1/2", "Completed"), ("draft_paragraph:2/1", "Completed"),
            ("sentences", "Completed"), ("draft_sentence:1/1/1", "Completed"), ("draft_sentence:1/2/1", "Completed"), ("draft_sentence:2/1/1", "Completed"));
        var instances = new[]
        {
            new ConsultLoopInstanceRow("paragraphs", "1", 2, At), new ConsultLoopInstanceRow("paragraphs", "2", 1, At),
            new ConsultLoopInstanceRow("sentences", "1/1", 1, At), new ConsultLoopInstanceRow("sentences", "1/2", 1, At), new ConsultLoopInstanceRow("sentences", "2/1", 1, At),
        };

        var diagram = RunDagDiagram.Build(Job(ThreeLevels(), outputs, instances));

        var parts = SubgraphText(diagram, "for_parts");
        var paragraphs = SubgraphText(diagram, "for_paragraphs");
        var sentences = SubgraphText(diagram, "for_sentences");
        Assert.Contains(paragraphs, parts);
        Assert.Contains(sentences, paragraphs);
        Assert.Contains("\ndraft_sentence[", sentences);
        Assert.Contains("\ndraft_paragraph[", paragraphs);
        Assert.DoesNotContain("draft_sentence[", paragraphs.Replace(sentences, string.Empty));
        foreach (var id in new[] { "plan", "parts", "outline", "paragraphs", "draft_paragraph", "sentences", "draft_sentence" })
        {
            DeclaredOnce(diagram, id);
        }

        Assert.Contains("subgraph for_parts[\"parts: per node.plan.value.sections item (2/2)\"]", diagram);
        Assert.Contains("subgraph for_paragraphs[\"paragraphs: per node.outline.value.points item (3/3 · 2 instances)\"]", diagram);
        Assert.Contains("subgraph for_sentences[\"sentences: per input.questions item (3/3 · 3 instances)\"]", diagram);
        // Edges wait for the outermost end: none sits inside the outer box.
        Assert.DoesNotContain("-->", parts);
    }

    [Fact]
    public void ANestedLoopStillCounting_SaysSo_BesideItsSettledInstances()
    {
        var nodes = ThreeLevels().Take(5).ToArray();
        nodes[3] = nodes[3] with { Body = new[] { "draft_paragraph" }, Ret = "draft_paragraph" };
        var outputs = Rows(("plan", "Completed"), ("outline:1", "Completed"), ("draft_paragraph:1/1", "Completed"));

        var diagram = RunDagDiagram.Build(Job(nodes, outputs, new[] { new ConsultLoopInstanceRow("paragraphs", "1", 2, At) }, status: "Running"));

        Assert.Contains("(1/2 · 1 instance, counting…)", diagram);
    }

    // ---- a while ------------------------------------------------------------------------------

    private static ConsultGenerationNodeDescriptor[] Refining(int? passes = null, string? stopped = null) => new ConsultGenerationNodeDescriptor[]
    {
        new("refine", "Refining the summary", Kind: "while", Expression: "node.judge.value.verdict != \"good\"", Body: new[] { "draft", "judge" }, Ret: "draft", Max: 3, Passes: passes, Stopped: stopped),
        new("draft", "Draft", In: "refine", Format: "prose", Producer: "attested"),
        new("judge", "Judge", In: "refine", Format: "verdict", Producer: "custom", Reads: new[] { "draft" }),
    };

    [Theory]
    [InlineData(3, "bound", "3 passes — stopped at its bound")]
    [InlineData(2, "check", "2 passes — the check stopped holding")]
    [InlineData(1, "until", "1 pass — its until held")]
    public void AStoppedWhile_SaysItsPassesAndWhy_InALoopsBox_NeverADiamond(int passes, string stopped, string words)
    {
        var rows = new List<(string, string)> { ("refine", "Completed") };
        for (var n = 1; n <= passes; n++)
        {
            rows.Add(($"draft:{n}", "Completed"));
            rows.Add(($"judge:{n}", "Completed"));
        }

        var diagram = RunDagDiagram.Build(Job(Refining(passes, stopped), Rows(rows.ToArray())));

        Assert.Contains($"subgraph while_refine[\"refine: while node.judge.value.verdict != #quot;good#quot; — {words}\"]", diagram);
        Assert.Contains("\nrefine[\"refine<br/>Refining the summary<br/>while: node.judge.value.verdict != #quot;good#quot;<br/>at most 3\"]", diagram);
        Assert.DoesNotContain("refine{", diagram);
        Assert.DoesNotContain("branch: none", diagram);
        Assert.Contains($"<br/>({passes}/{passes} passes)", SubgraphText(diagram, "while_refine"));
        Assert.Matches(@"class [^\n]*\bdraft\b[^\n]* done", diagram);
    }

    [Fact]
    public void ARunningWhile_SaysWhichPass_OfItsBound()
    {
        var diagram = RunDagDiagram.Build(Job(Refining(), Rows(("draft:1", "Completed"), ("judge:1", "Completed"), ("draft:2", "Running")), status: "Running"));

        Assert.Contains("— pass 2 of at most 3\"]", diagram);
        Assert.Matches(@"class [^\n]*\bdraft\b[^\n]* running", diagram);
    }

    [Fact]
    public void AWhileInALoop_IsASubgraphInsideTheLoops()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("parts", "Parts", Kind: "for", Collection: "input.parts", Body: new[] { "refine" }, Ret: "refine", Count: 1),
            new("refine", "Refining", Kind: "while", Expression: "true", Body: new[] { "draft" }, Ret: "draft", Max: 2, In: "parts"),
            new("draft", "Draft", In: "refine", Format: "prose", Producer: "attested"),
        };

        var diagram = RunDagDiagram.Build(Job(nodes, Rows(("draft:1/1", "Completed"), ("draft:1/2", "Completed")),
            new[] { new ConsultLoopInstanceRow("refine", "1", null, At, Passes: 2, Stopped: "bound") }));

        Assert.Contains(SubgraphText(diagram, "while_refine"), SubgraphText(diagram, "for_parts"));
        DeclaredOnce(diagram, "draft");
    }

    // ---- a let --------------------------------------------------------------------------------

    [Fact]
    public void ALet_IsAComputedSubroutine_NamingItsType()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("find_problems", "Finding", Format: "problem_list", Producer: "custom"),
            new("problem_count", "Counting the problems", Producer: "computed", Op: "let", Reads: new[] { "find_problems" }, Let: "node.find_problems.value.problems | array.size", LetType: "number"),
            new("summary", "Summary", Format: "problem_list", Producer: "computed", Op: "let", Let: "node.find_problems.value", LetType: "problem_list"),
        };

        var diagram = RunDagDiagram.Build(Job(nodes, Rows(("find_problems", "Completed"), ("problem_count", "Completed"))));

        Assert.Contains("\nproblem_count[[\"problem_count<br/>Counting the problems<br/>computed · let → number\"]]", diagram);
        // A format-typed let names its type once, never also as a model's format line.
        Assert.Contains("summary[[\"summary<br/>Summary<br/>computed · let → problem_list\"]]", diagram);
        Assert.Contains("find_problems --> problem_count", diagram);
        Assert.Matches(@"class [^\n]*\bproblem_count\b[^\n]* done", diagram);
    }

    // ---- not run ------------------------------------------------------------------------------

    [Fact]
    public void AnUntilsUnrunItems_ReadNotRun_ApartFromSkips()
    {
        var nodes = new ConsultGenerationNodeDescriptor[]
        {
            new("parts", "Parts", Kind: "for", Collection: "node.plan.value.sections", Body: new[] { "draft" }, Ret: "draft", Count: 3, Until: "item.value.kind == \"plan\"", Stopped: "until"),
            new("draft", "Draft", In: "parts", Format: "prose", Producer: "attested"),
        };
        var outputs = new Dictionary<string, ConsultGenerationNodeStatus>(StringComparer.Ordinal)
        {
            ["draft:1"] = Row("draft:1", "Completed"),
            ["draft:2"] = Row("draft:2", "Completed"),
            ["parts:3"] = Row("parts:3", "Skipped", "not run: stopped early at item 2 (Plan)"),
        };

        var diagram = RunDagDiagram.Build(Job(nodes, outputs));

        Assert.Contains("subgraph for_parts[\"parts: per node.plan.value.sections item (2/2 · 1 not run)\"]", diagram);
    }
}
