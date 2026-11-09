using System.Text.Json.Nodes;
using Consultologist.PackageFormat;

namespace Consultologist.Api.Tests;

/// <summary>
/// #1339 A2 (package-format-v30-design.md § 3): a <c>while</c> and a <c>for … until</c>
/// at 30, through the seam. <see cref="AddRefine"/> adds to the 30 minimal a
/// <c>refine</c> while that redrafts a summary until a judge calls it good (at most
/// three passes): <c>draft</c> reads the judge's last feedback one pass back
/// (<c>pass.previous</c>, no edge), <c>judge</c> reads this pass's draft.
/// </summary>
public class WorkflowV30WhileTests
{
    public const string Verdict = """
        { "type": "object", "producer": "custom", "outputMacros": false,
          "description": "A judge's verdict on a draft.",
          "schema": { "type": "object", "additionalProperties": false, "required": ["verdict", "feedback"],
                      "properties": {
                        "verdict": { "type": "string", "enum": ["good", "redo"] },
                        "feedback": { "type": "string" } } } }
        """;

    /// <summary>The refine while, its two body nodes, the verdict format, and the deliverable reading it.</summary>
    public static void AddRefine(JsonObject root, Dictionary<string, string> files)
    {
        root["formats"]!.AsArray().Add(new JsonObject { ["id"] = "verdict", ["file"] = "formats/verdict.json" });
        var nodes = root["nodes"]!.AsArray();
        nodes.Add(new JsonObject { ["id"] = "draft", ["label"] = "Draft the summary", ["format"] = "prose" });
        nodes.Add(new JsonObject { ["id"] = "judge", ["label"] = "Judge the draft", ["format"] = "verdict", ["onInvalid"] = "fail" });
        nodes.Add(new JsonObject
        {
            ["id"] = "refine", ["label"] = "Refining the summary", ["while"] = "node.judge.value.verdict != \"good\"", ["max"] = 3,
            ["nodes"] = new JsonArray("draft", "judge"), ["ret"] = "node.draft"
        });
        root["deliverables"]![0]!["sources"]!.AsArray().Add("node.refine");

        files["formats/verdict.json"] = Verdict;
        files["prompts/draft.md"] = "Pass {{ pass.number }}. Summarise: {{ input.notes }}{{ if pass.number > 1 }} Feedback: {{ pass.previous.judge.value.feedback }}{{ end }}";
        files["prompts/judge.md"] = "Judge this draft: {{ node.draft.content }}";
    }

    internal static (WorkflowPackageManifest Manifest, Dictionary<string, string> Files) Refining(Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null)
    {
        var (json, bundleFiles) = V30Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        AddRefine(root, bundleFiles);
        change?.Invoke(root);
        files?.Invoke(bundleFiles);
        return (V30Package.Read(root.ToJsonString()), bundleFiles);
    }

    private static List<string> Errors(Action<JsonObject>? change = null, Action<Dictionary<string, string>>? files = null) =>
        V30Package.Errors(Refining(change, files));

    private static JsonObject Node(JsonObject root, string id) => V30Package.Node(root, id);

    private static void AddNode(JsonObject root, JsonObject node) => root["nodes"]!.AsArray().Add(node);

    private static void NoErrors(List<string> errors) => Assert.True(errors.Count == 0, string.Join("\n", errors));

    private static void Says(string sentence, List<string> errors) =>
        Assert.True(errors.Contains(sentence), $"Expected: {sentence}\nGot:\n{string.Join("\n", errors)}");

    private static WorkflowDerivedEdges.Walk Walk((WorkflowPackageManifest Manifest, Dictionary<string, string> Files) bundle)
    {
        var manifest = WorkflowImpliedPaths.WithImpliedPrompts(bundle.Manifest, bundle.Files);
        var nodes = WorkflowDerivedEdges.PrepareNodes(manifest, bundle.Files, V25Package.Catalog(), out var snippets, out var contextFor);
        return WorkflowDerivedEdges.WalkAll(manifest, bundle.Files, nodes, snippets, contextFor);
    }

    // ---- § 3.1 the shape ------------------------------------------------------------------

    [Fact]
    public void AWhile_IsValidAt30()
    {
        var result = V30Package.Validate(Refining());
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void AWhile_DeclaresNoMax_IsRefused() =>
        Says("While 'refine' declares no max; a while must declare its bound (1 to 10).", Errors(root => Node(root, "refine").Remove("max")));

    [Theory]
    [InlineData(11, "While 'refine' declares max 11; the most is 10.")]
    [InlineData(0, "While 'refine' declares max 0; the least is 1.")]
    public void AWhile_OutsideItsBound_IsRefused(int max, string sentence) =>
        Says(sentence, Errors(root => Node(root, "refine")["max"] = max));

    [Fact]
    public void AWhile_AtItsCap_IsValid() =>
        NoErrors(Errors(root => Node(root, "refine")["max"] = 10));

    [Fact]
    public void AWhile_WithNoBody_IsRefused() =>
        Says("While 'refine' has no body; name the nodes that run per pass in nodes.", Errors(root =>
        {
            var refine = Node(root, "refine");
            refine["nodes"] = new JsonArray();
        }));

    [Fact]
    public void Max_OnALoop_BelongsToAWhile() =>
        Says("Node 'parts' declares max, which belongs to a while node.", Errors(root => Node(root, "parts")["max"] = 2));

    [Fact]
    public void Until_OnAWhile_BelongsToAFor() =>
        Says("Node 'refine' declares until, which belongs to a for node.", Errors(root => Node(root, "refine")["until"] = "node.judge.value.verdict == \"good\""));

    [Fact]
    public void Then_OnAWhile_IsNotItsKey() =>
        Says("Node 'refine' declares then, which a while node does not carry.", Errors(root => Node(root, "refine")["then"] = new JsonObject { ["nodes"] = new JsonArray("draft"), ["ret"] = "node.draft" }));

    [Fact]
    public void WhileAndFor_OnOneNode_AreTwoFlowKeys() =>
        Says("Node 'refine' declares for, while; a node is one flow node — for, while, if or case.", Errors(root => Node(root, "refine")["for"] = "item in input.notes"));

    [Fact]
    public void AWhile_ReturnsANodeOfItsBody() =>
        Says("While 'refine' returns node.plan, which is not in its body (nodes: draft, judge).", Errors(root => Node(root, "refine")["ret"] = "node.plan"));

    [Fact]
    public void AWhile_NeedsNoPromptFile_AndMayDescribeItself()
    {
        var bundle = Refining(root => Node(root, "refine")["description"] = "Redraft until the judge is satisfied.");
        Assert.DoesNotContain("prompts/refine.md", WorkflowImpliedPaths.CandidatePrompts(bundle.Manifest));
        NoErrors(V30Package.Errors(bundle));
    }

    // ---- § 3.2 what a pass reads ----------------------------------------------------------

    [Fact]
    public void PassPrevious_AddsNoEdge_SoDraftAndJudgeFormNoCycle()
    {
        var walk = Walk(Refining());

        Assert.DoesNotContain("judge", walk.EdgesOf("draft"));
        Assert.Contains("draft", walk.EdgesOf("judge"));
        var draft = walk.Templates.Single(t => t.Kind == TemplateKind.Prompt && t.Id == "draft");
        Assert.Contains("judge", draft.References.PassPrevious!);
    }

    [Fact]
    public void ABodyNode_ReadingAnotherByIdBothWays_IsACycle()
    {
        var errors = Errors(files: files => files["prompts/draft.md"] = "Feedback: {{ node.judge.value.feedback }}");
        Assert.Contains(errors, e => e.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void PassPrevious_OfANodeOutsideTheWhile_IsRefused() =>
        Says("Node 'draft' prompt reads pass.previous.plan, which is not in while 'refine'; pass.previous reads the while's own nodes.",
            Errors(files: files => files["prompts/draft.md"] = "{{ pass.previous.plan.content }}"));

    [Fact]
    public void Pass_OutsideEveryWhile_IsRefused() =>
        Says("Node 'plan' prompt reads pass.number, but it runs inside no while; pass is a while's pass.",
            Errors(files: files => files["prompts/plan.md"] = "Plan {{ pass.number }}: {{ input.notes }}"));

    [Fact]
    public void Pass_IsNoObjectBelow30()
    {
        var (json, files) = V29Package.MinimalJson();
        files["prompts/find_problems.md"] = "List {{ pass.number }}: {{ input.notes }}";
        var errors = V29Package.Errors((V29Package.Read(json), files));
        Assert.Contains(errors, e => e.Contains("reads 'pass', which is not an object", StringComparison.Ordinal));
    }

    [Fact]
    public void Pass_AMemberItDoesNotHave_IsNamed() =>
        Says("Node 'draft' prompt reads pass.count, which is not a member (pass has .number, .previous and .parent).",
            Errors(files: files => files["prompts/draft.md"] = "{{ pass.count }}"));

    [Fact]
    public void PassParent_WithNoEnclosingWhile_IsRefused() =>
        Says("Node 'draft' prompt reads pass.parent, but its while runs inside no while; pass.parent is the enclosing while's pass.",
            Errors(files: files => files["prompts/draft.md"] = "{{ pass.parent.number }}"));

    [Fact]
    public void PassParent_ReachesTheEnclosingWhile_AndItsPrevious()
    {
        // review → (redraft: draft, judge) — a while inside a while; draft reads the outer pass.
        NoErrors(Errors(root =>
        {
            AddNode(root, new JsonObject { ["id"] = "critic", ["label"] = "Critic", ["format"] = "verdict", ["onInvalid"] = "fail" });
            AddNode(root, new JsonObject
            {
                ["id"] = "review", ["label"] = "Review", ["while"] = "node.critic.value.verdict != \"good\"", ["max"] = 2,
                ["nodes"] = new JsonArray("refine", "critic"), ["ret"] = "node.refine"
            });
            var sources = root["deliverables"]![0]!["sources"]!.AsArray();
            sources.Remove(sources.First(s => s!.GetValue<string>() == "node.refine"));
            sources.Add("node.review");
        }, files =>
        {
            files["prompts/critic.md"] = "Critique: {{ node.refine.content }} after {{ node.refine.passes }} passes ({{ node.refine.stopped }}).";
            files["prompts/draft.md"] = "Round {{ pass.parent.number }}, pass {{ pass.number }}. {{ pass.parent.previous.critic.value.feedback }} {{ pass.previous.judge.value.feedback }}";
        }));
    }

    [Fact]
    public void PassParentParent_PastTheOutermostWhile_IsRefused()
    {
        var errors = Errors(root =>
        {
            AddNode(root, new JsonObject { ["id"] = "critic", ["label"] = "Critic", ["format"] = "verdict", ["onInvalid"] = "fail" });
            AddNode(root, new JsonObject
            {
                ["id"] = "review", ["label"] = "Review", ["while"] = "node.critic.value.verdict != \"good\"", ["max"] = 2,
                ["nodes"] = new JsonArray("refine", "critic"), ["ret"] = "node.refine"
            });
        }, files =>
        {
            files["prompts/critic.md"] = "Critique: {{ node.refine.content }}";
            files["prompts/draft.md"] = "{{ pass.parent.parent.number }}";
        });
        Says("Node 'draft' prompt reads pass.parent.parent, but that while runs inside no while; pass.parent is the enclosing while's pass.", errors);
    }

    [Fact]
    public void PassPrevious_OfAnInnerLoopsNode_FromInsideThatLoop_IsAligned()
    {
        // refine's body holds a loop over input items; a body node of that loop reads its own previous pass.
        NoErrors(Errors(root => AddInnerLoop(root), files =>
        {
            files["prompts/note_point.md"] = "Point {{ item.id }}: {{ item.value.title }}. Last pass: {{ pass.previous.note_point.content }}";
            files["prompts/draft.md"] = "{{ for p in pass.previous.points }}{{ p.content }}{{ end }}{{ input.notes }}";
        }));
    }

    [Fact]
    public void PassPrevious_OfAnInnerLoopsNode_FromOutsideThatLoop_IsRefused() =>
        Says("Node 'draft' prompt reads pass.previous.note_point, which runs inside loop 'points'; read pass.previous.points.",
            Errors(root => AddInnerLoop(root), files =>
            {
                files["prompts/note_point.md"] = "Point {{ item.value.title }}";
                files["prompts/draft.md"] = "{{ pass.previous.note_point.content }}";
            }));

    [Fact]
    public void PassPrevious_OfAnInnerLoop_ReadsThatLoopWhole() =>
        NoErrors(Errors(root => AddInnerLoop(root), files =>
        {
            files["prompts/note_point.md"] = "Point {{ item.value.title }}";
            files["prompts/draft.md"] = "{{ for p in pass.previous.points }}{{ p.content }}{{ end }}{{ input.notes }}";
        }));

    private static void AddInnerLoop(JsonObject root)
    {
        // note_point's prompt is set by each test; points is read one pass back by draft.
        AddNode(root, new JsonObject { ["id"] = "note_point", ["label"] = "Note a point", ["format"] = "prose" });
        AddNode(root, new JsonObject
        {
            ["id"] = "points", ["label"] = "Points", ["for"] = "item in node.plan.value.sections",
            ["nodes"] = new JsonArray("note_point"), ["ret"] = "node.note_point"
        });
        Node(root, "refine")["nodes"]!.AsArray().Add("points");
    }

    [Fact]
    public void ABodyNode_ReadingItsWhile_IsRefused()
    {
        var errors = Errors(files: files => files["prompts/judge.md"] = "Judge: {{ node.refine.content }}");
        Assert.Contains(errors, e => e.StartsWith("Node 'judge'", StringComparison.Ordinal) && e.Contains("refine", StringComparison.Ordinal));
    }

    [Fact]
    public void TheOutside_ReadsTheWhile_NotItsBody() =>
        Says("Node 'plan' prompt reads node 'draft', which runs inside while 'refine'; read node.refine.",
            Errors(files: files => files["prompts/plan.md"] = "{{ node.draft.content }} {{ input.notes }}"));

    [Fact]
    public void TheOutside_ReadsPassesAndStopped()
    {
        var bundle = Refining(root =>
        {
            AddNode(root, new JsonObject { ["id"] = "report", ["label"] = "Report", ["format"] = "prose" });
            root["deliverables"]![0]!["sources"]!.AsArray().Add("node.report");
        }, files => files["prompts/report.md"] = "After {{ node.refine.passes }} passes{{ if node.refine.stopped == \"bound\" }} (at its bound){{ end }} the summary is {{ node.refine.content }}.");

        NoErrors(V30Package.Errors(bundle));
        Assert.Contains("refine", Walk(bundle).EdgesOf("report"));
    }

    [Fact]
    public void Stopped_IsTypedByItsTwoValues()
    {
        var errors = Errors(root =>
        {
            AddNode(root, new JsonObject
            {
                ["id"] = "gate", ["label"] = "Gate", ["if"] = "node.refine.stopped == \"never\"",
                ["then"] = new JsonObject { ["nodes"] = new JsonArray("report"), ["ret"] = "node.report" }
            });
            AddNode(root, new JsonObject { ["id"] = "report", ["label"] = "Report", ["format"] = "prose" });
            root["deliverables"]![0]!["sources"]!.AsArray().Add("node.gate");
        }, files => files["prompts/report.md"] = "{{ node.refine.content }}");
        Assert.Contains(errors, e => e.StartsWith("Node 'gate' if", StringComparison.Ordinal) && e.Contains("never", StringComparison.Ordinal));
    }

    [Fact]
    public void AWhile_AMemberItDoesNotHave_IsNamed() =>
        Says("Node 'plan' prompt reads node.refine.count, which is not a member (a while has .content, .value, .passes and .stopped).",
            Errors(files: files => files["prompts/plan.md"] = "{{ node.refine.count }} {{ input.notes }}"));

    [Fact]
    public void Passes_OnAPlainNode_IsNotAMember() =>
        Says("Node 'judge' prompt reads node.draft.passes, which is not a member (node.<id> has .content and .value).",
            Errors(files: files => files["prompts/judge.md"] = "{{ node.draft.passes }}"));

    // ---- § 3.1 the check ------------------------------------------------------------------

    [Fact]
    public void TheCheck_ReadsItsBody_AnEarlierNode_AndThePassNumber()
    {
        var bundle = Refining(root => Node(root, "refine")["while"] = "node.judge.value.verdict != \"good\" && pass.number < 3 && (node.plan.value.sections | array.size) > 1");
        NoErrors(V30Package.Errors(bundle));

        var edges = Walk(bundle).EdgesOf("refine");
        Assert.Contains("judge", edges);
        Assert.Contains("plan", edges);
    }

    [Fact]
    public void TheCheck_ReadingANodeThatReadsTheWhile_IsACycle()
    {
        var errors = Errors(root =>
        {
            AddNode(root, new JsonObject { ["id"] = "after", ["label"] = "After", ["format"] = "prose" });
            Node(root, "refine")["while"] = "node.after.content != \"\"";
            root["deliverables"]![0]!["sources"]!.AsArray().Add("node.after");
        }, files => files["prompts/after.md"] = "{{ node.refine.content }}");
        Assert.Contains(errors, e => e.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCheck_IsACondition_NotATemplate()
    {
        var errors = Errors(root => Node(root, "refine")["while"] = "{{ node.judge.value.verdict }}");
        Assert.Contains(errors, e => e.StartsWith("While 'refine' check", StringComparison.Ordinal));
    }

    // ---- § 3.4 until ----------------------------------------------------------------------

    [Fact]
    public void Until_ReadsTheItemsBody_TheItem_AndAnEarlierNode()
    {
        var bundle = V30Package.With(root =>
            Node(root, "parts")["until"] = "(node.outline.value.points | array.size) > 4 || item.value.kind == \"plan\" || input.notes == \"\"");
        NoErrors(V30Package.Errors(bundle));

        Assert.Contains("outline", Walk(bundle).EdgesOf("parts"));
    }

    [Fact]
    public void Until_ReadingAnotherNodeBeforeTheLoop_IsAnEdge()
    {
        var bundle = V30Package.With(root =>
        {
            AddNode(root, new JsonObject { ["id"] = "triage", ["label"] = "Triage", ["format"] = "prose" });
            Node(root, "parts")["until"] = "node.triage.content == \"stop\"";
            root["deliverables"]![0]!["sources"]!.AsArray().Add("node.triage");
        }, files => files["prompts/triage.md"] = "{{ input.notes }}");
        NoErrors(V30Package.Errors(bundle));
        Assert.Contains("triage", Walk(bundle).EdgesOf("parts"));
    }

    [Fact]
    public void Until_ReadingPass_OutsideAWhile_IsRefused()
    {
        var errors = V30Package.Errors(V30Package.With(root => Node(root, "parts")["until"] = "pass.number > 1"));
        Says("Loop 'parts' until reads pass.number, but it runs inside no while; pass is a while's pass.", errors);
    }

    // ---- nesting -------------------------------------------------------------------------

    [Fact]
    public void AWhile_RunsInsideALoop_AndReadsTheItem()
    {
        NoErrors(Errors(root =>
        {
            Node(root, "parts")["nodes"]!.AsArray().Add("refine");
            var sources = root["deliverables"]![0]!["sources"]!.AsArray();
            sources.Remove(sources.First(s => s!.GetValue<string>() == "node.refine"));
        }, files =>
        {
            files["prompts/draft.md"] = "Section {{ item.value.title }}, pass {{ pass.number }}: {{ pass.previous.judge.value.feedback }}";
            files["prompts/judge.md"] = "Judge {{ node.draft.content }} for {{ item.value.title }}";
            files["prompts/draft_paragraph.md"] = "Point {{ item.value.point }} of {{ item.parent.value.title }}, after the summary {{ node.refine.content }}";
        }));
    }

    [Fact]
    public void ALoopsElement_ReadsAWhileInItsBody_WithPassesAndStopped()
    {
        NoErrors(Errors(root =>
        {
            Node(root, "parts")["nodes"]!.AsArray().Add("refine");
            var sources = root["deliverables"]![0]!["sources"]!.AsArray();
            sources.Remove(sources.First(s => s!.GetValue<string>() == "node.refine"));
            AddNode(root, new JsonObject { ["id"] = "assemble", ["label"] = "Assemble", ["format"] = "prose" });
            sources.Add("node.assemble");
        }, files =>
        {
            files["prompts/draft.md"] = "Section {{ item.value.title }}";
            files["prompts/draft_paragraph.md"] = "Point {{ item.value.point }}, after {{ node.refine.content }}";
            files["prompts/assemble.md"] = "{{ for s in node.parts }}{{ s.refine.content }} ({{ s.refine.passes }}, {{ s.refine.stopped }}){{ end }}";
        }));
    }

    // ---- below 30: the gate --------------------------------------------------------------

    [Theory]
    [InlineData("while", "Node 'x' declares while, which requires specVersion 30.")]
    [InlineData("max", "Node 'x' declares max, which requires specVersion 30.")]
    [InlineData("until", "Node 'x' declares until, which requires specVersion 30.")]
    public void At29_TheNewKeys_AreGatedByName(string key, string sentence)
    {
        var (json, files) = V29Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        var node = new JsonObject { ["id"] = "x", ["label"] = "X", ["format"] = "prose" };
        node[key] = key == "max" ? 3 : "input.notes == \"\"";
        root["nodes"]!.AsArray().Add(node);
        files["prompts/x.md"] = "{{ input.notes }}";

        Says(sentence, V29Package.Errors((V29Package.Read(root.ToJsonString()), files)));
    }

    [Fact]
    public void At29_TwoFlowKeys_KeepThe26Sentence()
    {
        var (json, files) = V29Package.MinimalJson();
        var root = JsonNode.Parse(json)!.AsObject();
        V29Package.Node(root, "problems")["if"] = "input.notes == \"\"";

        Says("Node 'problems' declares for, if; a node is one flow node — for, if or case.",
            V29Package.Errors((V29Package.Read(root.ToJsonString()), files)));
    }
}
