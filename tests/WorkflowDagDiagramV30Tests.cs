using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// v30 (#1346, design § 11): the Editor's Graph pane draws a 30 package nested, as the run
/// diagram draws a 30 record (#1345) — a flow in a body is a subgraph inside its owner's, to
/// any depth, every node declared once and inside its own box; a while is a loop's box with its
/// bound; a let a computed subroutine naming its type; every edge after the outermost end.
/// </summary>
public class WorkflowDagDiagramV30Tests
{
    private static string SubgraphText(string diagram, string subgraphId)
    {
        var start = diagram.IndexOf($"subgraph {subgraphId}[", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no subgraph {subgraphId}\n{diagram}");
        var lines = diagram[start..].Split('\n');
        var depth = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            depth += line.StartsWith("subgraph ", StringComparison.Ordinal) ? 1 : line == "end" ? -1 : 0;
            if (depth == 0)
            {
                return string.Join('\n', lines[..(i + 1)]) + "\n";
            }
        }

        throw new Xunit.Sdk.XunitException($"subgraph {subgraphId} is never closed");
    }

    private static void DeclaredOnce(string diagram, string id) =>
        Assert.Single(Regex.Matches(diagram, $@"(?m)^\s*{id}(\[|\{{)"));

    [Fact]
    public void ALoopInALoopInALoop_NestsEachSubgraph_DeclaringEveryNodeOnceInsideItsOwnBox()
    {
        var diagram = WorkflowDagDiagram.Generate(EngineRunNestedTests.ThreeLevels().Manifest);

        var parts = SubgraphText(diagram, "for_parts");
        var paragraphs = SubgraphText(diagram, "for_paragraphs");
        var sentences = SubgraphText(diagram, "for_sentences");
        Assert.Contains(paragraphs, parts);
        Assert.Contains(sentences, paragraphs);
        Assert.Contains("draft_sentence[", sentences);
        Assert.Contains("draft_paragraph[", paragraphs);
        Assert.DoesNotContain("draft_sentence[", paragraphs.Replace(sentences, string.Empty));
        foreach (var id in new[] { "parts", "outline", "paragraphs", "draft_paragraph", "sentences", "draft_sentence" })
        {
            DeclaredOnce(diagram, id);
        }

        // Edges wait for the outermost end: none sits inside the outer box.
        Assert.DoesNotContain("-->", parts);
        Assert.Contains("outline -->|\"for\"| for_paragraphs", diagram);
        Assert.Contains("sentences -->|\"ret\"| paragraphs", diagram);
    }

    [Fact]
    public void AWhile_IsALoopsBoxWithItsBound_NeverADiamond()
    {
        var diagram = WorkflowDagDiagram.Generate(WorkflowV30WhileTests.Refining().Manifest);

        Assert.Contains("subgraph while_refine[\"refine: while node.judge.value.verdict != #quot;good#quot; — at most 3\"]", diagram);
        Assert.Contains("refine[\"refine<br/>Refining the summary<br/>while: node.judge.value.verdict != #quot;good#quot;<br/>at most 3\"]", diagram);
        Assert.DoesNotContain("refine{", diagram);
        var body = SubgraphText(diagram, "while_refine");
        Assert.Contains("draft[", body);
        Assert.Contains("judge[", body);
        Assert.Contains("draft -->|\"ret\"| refine", diagram);
    }

    [Fact]
    public void AWhileInALoop_IsASubgraphInsideTheLoops()
    {
        var bundle = WorkflowV30WhileTests.Refining(root =>
        {
            V30Package.Node(root, "parts")["nodes"] = new JsonArray("outline", "paragraphs", "refine");
            V30Package.Node(root, "parts")["ret"] = "node.paragraphs";
            root["deliverables"]![0]!["sources"]!.AsArray().Remove(root["deliverables"]![0]!["sources"]!.AsArray().Single(s => s!.GetValue<string>() == "node.refine"));
        });

        var diagram = WorkflowDagDiagram.Generate(bundle.Manifest);

        Assert.Contains(SubgraphText(diagram, "while_refine"), SubgraphText(diagram, "for_parts"));
        DeclaredOnce(diagram, "draft");
        DeclaredOnce(diagram, "refine");
    }

    [Fact]
    public void ALet_IsAComputedSubroutineNamingItsType_AtTheTopAndInALoop()
    {
        var top = WorkflowDagDiagram.Generate(WorkflowV30LetTests.Counting().Manifest);
        Assert.Contains("problem_count[[\"problem_count<br/>Counting the problems<br/>computed · let → number\"]]", top);

        var inALoop = WorkflowDagDiagram.Generate(WorkflowV30LetTests.Counting(root =>
        {
            (V30Package.Node(root, "problem_count")["let"], V30Package.Node(root, "problem_count")["type"]) = ("item.value.name | string.size", "number");
            V30Package.Node(root, "problems")["nodes"]!.AsArray().Insert(0, "problem_count");
        }).Manifest);
        Assert.Contains("problem_count[[", SubgraphText(inALoop, "for_problems"));
        DeclaredOnce(inALoop, "problem_count");
    }
}
