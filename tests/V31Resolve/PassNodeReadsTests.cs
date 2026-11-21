using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Run;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1504 (specVersion 31, area 07; ruled by the owner on #1504, ruling 7; part 04 §§ 3.3, 4): what publish says of a read
/// of this pass's node, <c>pass.node.&lt;id&gt;</c>, which is a dependency edge as <c>node.&lt;id&gt;</c> is. Two body nodes
/// reading each other through it are a circle (A05-003), as is a node reading the flow node that encloses it; a node
/// inside an inner loop of the body is a list from outside that loop (S04-039). Every shape publish accepts loads. Content
/// is fictional (Dr A. Sample).
/// </summary>
public class PassNodeReadsTests
{
    private const string Plain = "\"plaintext\"";

    [Fact]
    public void TwoBodyNodesReadingEachOtherThroughPass_AreRefused()
    {
        // the judge reads node.draft_summary; the summary reads this pass's judge, pass.node.judge
        var files = ResolveFixture.NestedNote()
            .With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                "Summarise the note in two sentences for Dr A. Sample, as judged: {{ pass.node.judge.content }}\n"));

        var refusal = Assert.Single(Refusals(files));

        Assert.Equal(("A05-003",
            "Node 'draft_summary' reads node 'judge', which reads node 'draft_summary': nodes can't read each other in a circle. Break the circle.",
            "nodes/refine/draft_summary.md"), (refusal.Id, refusal.Sentence, refusal.Path));
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void TwoBodyNodesReadingEachOtherThroughPass_InTheirIfs_AreRefused()
    {
        // both ways through pass.node, in the header if
        var files = ResolveFixture.NestedNote()
            .With("nodes/refine/draft_summary.md", Gated("Drafting the summary", Plain,
                "{ \"!\": [{ \"blank\": [{ \"var\": \"pass.node.judge.content\" }] }] }", "Summarise the note for Dr A. Sample.\n"))
            .With("nodes/refine/judge.md", Gated("Judging the summary", "\"verdict\"",
                "{ \"!\": [{ \"blank\": [{ \"var\": \"pass.node.draft_summary.content\" }] }] }", "Say whether the summary is good.\n"));

        var refusal = Assert.Single(Refusals(files));

        Assert.Equal("A05-003", refusal.Id);
        V31Contract.AssertSentence(refusal);
    }

    [Theory]
    [InlineData("pass.node.notes.content")]
    [InlineData("pass.node.note.content")]
    public void PassNodeNamingAnEnclosingNode_IsRefused(string read)
    {
        // the note, inside the loop notes inside the while refine, reads its own loop (or itself) as this pass's node
        var files = PlanRunnerFlowTests.LoopInWhile()
            .With("nodes/refine/notes/note.md", Agent("Noting a section", Plain,
                $"Note the section '{{{{ item.value.title }}}}' after {{{{ {read} }}}}.\n"));

        var refusal = Assert.Single(Refusals(files));

        // a circle is said from its ordinal-first node (D34): the loop, at its own file, when the note reads it
        Assert.Equal(read == "pass.node.note.content"
                ? ("A05-003", "Node 'note' reads node 'note', which reads node 'note': nodes can't read each other in a circle. Break the circle.",
                    "nodes/refine/notes/note.md")
                : ("A05-003", "Node 'notes' reads node 'note', which reads node 'notes': nodes can't read each other in a circle. Break the circle.",
                    "nodes/refine/notes/index.json"),
            (refusal.Id, refusal.Sentence, refusal.Path));
        V31Contract.AssertSentence(refusal);
    }

    [Fact]
    public void PassNodeNamingTheWhileItself_IsRefused()
    {
        // the judge reads its own while as this pass's node, in its prompt and in its if: no node of the while's body
        // (already refused before ruling 7; pinned here beside the rest)
        var files = ResolveFixture.NestedNote()
            .With("nodes/refine/judge.md", Agent("Judging the summary", "\"verdict\"",
                "Say whether the summary is good.\n\n{{ node.draft_summary.content }} {{ pass.node.refine.content }}\n"));
        var refusal = Assert.Single(Refusals(files));
        Assert.Equal(("S05-011", "Node 'judge' reads pass.node.refine.content at line 3, which pass doesn't have.", "nodes/refine/judge.md"),
            (refusal.Id, refusal.Sentence, refusal.Path));
        V31Contract.AssertSentence(refusal);

        var inIf = ResolveFixture.NestedNote()
            .With("nodes/refine/judge.md", Gated("Judging the summary", "\"verdict\"",
                "{ \"!\": [{ \"blank\": [{ \"var\": \"pass.node.refine.content\" }] }] }",
                "Say whether the summary is good.\n\n{{ node.draft_summary.content }}\n"));
        var inIfRefusal = Assert.Single(Refusals(inIf));
        Assert.Equal(("S03-014", "Node 'judge' reads pass.node.refine.content, which doesn't exist here.", "nodes/refine/judge.md"),
            (inIfRefusal.Id, inIfRefusal.Sentence, inIfRefusal.Path));
        V31Contract.AssertSentence(inIfRefusal);
    }

    [Fact]
    public void PassNodeNamingAnInnerLoopsNode_IsRefused()
    {
        // the judge, a body node of refine, reads the note of refine's inner loop notes as this pass's node
        var files = PlanRunnerFlowTests.LoopInWhile()
            .With("nodes/refine/judge.md", Agent("Judging the notes", "\"verdict\"",
                "Say whether the notes are good or need another pass.\n\n{{ pass.node.note.content }}\n"));

        var refusal = Assert.Single(Refusals(files));

        Assert.Equal(("S04-039",
            "Node 'judge' reads node 'note', which runs inside loop 'notes'; read node.notes.",
            "nodes/refine/judge.md"), (refusal.Id, refusal.Sentence, refusal.Path));
        V31Contract.AssertSentence(refusal);
    }

    /// <summary>Each shape of a pass read publish accepts, by name.</summary>
    public static TheoryData<string> AcceptedShapes() => new(Shapes().Keys);

    [Theory]
    [MemberData(nameof(AcceptedShapes))]
    public void EveryAcceptedPassShape_PublishesAndLoads(string shape)
    {
        var files = Shapes()[shape];

        var (refusals, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        Assert.True(refusals.Count == 0, string.Join(" | ", refusals.Select(r => $"{r.Id} {r.Path} {r.Sentence}")));

        var (plan, refusal) = RunPlan.Build(compiled!.Resolve, compiled.ResolveHash, compiled.LockHash, null);
        Assert.True(refusal == null, refusal?.Sentence);
        Assert.NotNull(plan);
    }

    /// <summary>
    /// Every way a body reads a pass that publish accepts: this pass's node one way (in a prompt, in an if, from a while's
    /// own check), an earlier pass's both ways, the pass's number and history, and the enclosing while's pass and node,
    /// directly and through a loop between.
    /// </summary>
    private static Dictionary<string, Dictionary<string, byte[]>> Shapes()
    {
        const string judgeReadsDraft = "Say whether the summary is good or needs another pass.\n\n{{ pass.node.draft_summary.content }}\n";
        return new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal)
        {
            ["this pass's node, one way"] = ResolveFixture.NestedNote()
                .With("nodes/refine/judge.md", Agent("Judging the summary", "\"verdict\"", judgeReadsDraft)),
            ["this pass's node, in an if"] = ResolveFixture.NestedNote()
                .With("nodes/refine/judge.md", Gated("Judging the summary", "\"verdict\"",
                    "{ \"!\": [{ \"blank\": [{ \"var\": \"pass.node.draft_summary.content\" }] }] }",
                    "Say whether the summary is good.\n\n{{ node.draft_summary.content }}\n")),
            ["the while's check reads this pass's node"] = ResolveFixture.NestedNote()
                .WithJson("nodes/refine/index.json", j => j["while"] = JsonNode.Parse("""{ "!==": [{ "var": "pass.node.judge.value.verdict" }, "good"] }""")),
            ["an earlier pass's node, both ways"] = ResolveFixture.NestedNote()
                .With("nodes/refine/judge.md", Agent("Judging the summary", "\"verdict\"", judgeReadsDraft))
                .With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                    "Summarise the note for Dr A. Sample, after the last verdict: {{ pass.previous.node.judge.content }}\n")),
            ["the pass's number and history"] = ResolveFixture.NestedNote()
                .With("nodes/refine/draft_summary.md", Agent("Drafting the summary", Plain,
                    "Summarise the note for Dr A. Sample (pass {{ pass.number }}).\n"))
                .WithJson("nodes/refine/index.json", j => j["until"] = JsonNode.Parse("""{ ">": [{ "size": [{ "var": "pass.history" }] }, 1] }""")),
            ["a loop's node reads the while's node outside the loop"] = PlanRunnerFlowTests.LoopInWhile()
                .WithJson("nodes/refine/index.json", j => j["nodes"] = new JsonArray("judge", "notes"))
                .With("nodes/refine/judge.md", Agent("Judging the notes", "\"verdict\"", "Say whether the notes are ready to write.\n"))
                .With("nodes/refine/notes/note.md", Agent("Noting a section", Plain,
                    "Note the section '{{ item.value.title }}' after {{ pass.node.judge.content }}.\n")),
            ["an earlier pass's node of the inner loop"] = PlanRunnerFlowTests.LoopInWhile()
                .With("nodes/refine/judge.md", Agent("Judging the notes", "\"verdict\"",
                    "Say whether the notes are good or need another pass.\n\n{{ node.notes.content }}\n\nLast pass: {{ pass.previous.node.note.content }}\n")),
            ["an earlier pass's node, itself"] = ResolveFixture.NestedNote()
                .With("nodes/refine/judge.md", Agent("Judging the summary", "\"verdict\"",
                    "Say whether the summary is good or needs another pass.\n\n{{ node.draft_summary.content }}\n\nLast verdict: {{ pass.previous.node.judge.content }}\n")),
            ["an earlier pass's node, the enclosing one"] = PlanRunnerFlowTests.LoopInWhile()
                .With("nodes/refine/notes/note.md", Agent("Noting a section", Plain,
                    "Note the section '{{ item.value.title }}' after the last notes: {{ pass.previous.node.notes.content }}\n")),
            ["the enclosing while's pass and node"] = PlanRunnerFlowTests.NestedWhiles(forBetween: false),
            ["the enclosing while's pass and node, through a loop"] = PlanRunnerFlowTests.NestedWhiles(forBetween: true),
        };
    }

    private static IReadOnlyList<SourceRefusal> Refusals(Dictionary<string, byte[]> files) =>
        CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed).Refusals;

    /// <summary>An agent node whose header <c>if</c> is <paramref name="condition"/>.</summary>
    private static string Gated(string label, string format, string condition, string prompt) => Md(
        SourceFixture.Node("agent", label, format, false, true,
            $"\"if\": {condition}, \"onInvalid\": \"retry\", \"failIfEmpty\": null, \"parts\": null, \"ordered\": false"),
        prompt);
}
