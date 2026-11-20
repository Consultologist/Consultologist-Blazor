using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the rules that need the whole tree — keys, disjoint scopes
/// (S01-002, S01-036), a body's <c>ret</c> (S04-012) and the size of an expression (S01-042) —
/// through <see cref="SourceParser.Parse"/>, since the scope rules run on the tree the walk built.
/// </summary>
public class SourceScopesTests
{
    private const string Sections = "nodes/sections/index.json";
    private const string Paragraphs = "nodes/sections/paragraphs/index.json";
    private const string Refine = "nodes/refine/index.json";

    private static SourceRefusal Only(SourceParseResult result)
    {
        var refusal = Assert.Single(result.Refusals);
        Assert.Null(result.Package);
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private static Dictionary<string, byte[]> Copy(Dictionary<string, byte[]> files, string from, string to)
    {
        files[to] = files[from];
        return files;
    }

    private static JsonArray Ids(params string[] ids) => new(ids.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray());

    /// <summary>An expression of exactly <paramref name="nodes"/> nodes (D1): an <c>and</c> over literals.</summary>
    private static JsonNode Big(int nodes) => new JsonObject { ["and"] = new JsonArray(Enumerable.Repeat(1, nodes - 1).Select(_ => (JsonNode)JsonValue.Create(1)!).ToArray()) };

    [Fact]
    public void Keys_AreRootedPaths()
    {
        var result = SourceFixture.Full().Parse();
        Assert.Empty(result.Refusals);
        var package = result.Package!;

        Assert.Equal("node.plan", package.Nodes.Nodes[0].Key);
        var sections = (SourceFor)package.Nodes.Nodes[2];
        var paragraphs = (SourceFor)sections.Body.Nodes[1];
        Assert.Equal("node.sections.paragraphs", paragraphs.Key);
        Assert.Equal("node.sections.paragraphs.draft_paragraph", paragraphs.Body.Nodes[0].Key);
        var coverage = (SourceFor)package.Checks.Nodes[0];
        Assert.Equal("check.section_coverage.coverage", coverage.Body.Nodes[0].Key);
        Assert.Equal("deliverable.emr.consult", package.Deliverables.Folders[0].Deliverables[0].Key);
        Assert.Equal(
            ["node.standard_draft", "node.patient_draft", "node.apply_instructions"],
            package.Functions.Items[0].Body.Nodes.Select(n => n.Key));
    }

    [Fact]
    public void TwoSiblingLoops_MayEachHoldADraft()
    {
        var files = SourceFixture.Full();
        Copy(files, "nodes/sections/paragraphs/draft_paragraph.md", "nodes/standards/draft_paragraph.md");
        files.WithJson("nodes/standards/index.json", json => json["nodes"] = Ids("section", "draft_paragraph"));

        Assert.Empty(files.Parse().Refusals);
    }

    [Fact]
    public void ABodyNode_MayNotReuseATopLevelId()
    {
        var files = SourceFixture.Full();
        Copy(files, "nodes/plan.md", "nodes/sections/paragraphs/plan.md");
        files.WithJson(Paragraphs, json => json["nodes"] = Ids("draft_paragraph", "plan"));

        var refusal = Only(files.Parse());

        Assert.Equal("S01-002", refusal.Id);
        Assert.Equal(
            "Node 'plan' in sections.paragraphs reuses the id of node 'plan' at the top level, which sections.paragraphs can read: rename one. "
            + "Ids may repeat only where neither can see the other (for example in two sibling loops).",
            refusal.Sentence);
        Assert.Equal("nodes/sections/paragraphs/plan.md", refusal.Path);
    }

    [Fact]
    public void ABodyNode_MayNotReuseAnEnclosingBodysId()
    {
        var files = SourceFixture.Full();
        Copy(files, "nodes/sections/outline.md", "nodes/sections/paragraphs/outline.md");
        files.WithJson(Paragraphs, json => json["nodes"] = Ids("draft_paragraph", "outline"));

        var refusal = Only(files.Parse());

        Assert.Equal(
            "Node 'outline' in sections.paragraphs reuses the id of node 'outline' in sections, which sections.paragraphs can read: rename one. "
            + "Ids may repeat only where neither can see the other (for example in two sibling loops).",
            refusal.Sentence);
    }

    [Fact]
    public void ANodeInsideALoopOfTheSameId_IsRefused()
    {
        var files = SourceFixture.Full();
        Copy(files, "nodes/plan.md", "nodes/sections/sections.md");
        files.WithJson(Sections, json => json["nodes"] = Ids("outline", "paragraphs", "sections"));

        var refusal = Only(files.Parse());

        Assert.StartsWith("Node 'sections' in sections reuses the id of node 'sections' at the top level, which sections can read", refusal.Sentence);
    }

    [Fact]
    public void AChecksPath_IsWrittenWithItsRoot()
    {
        var files = SourceFixture.Full();
        Copy(files, "checks/all_sections.md", "checks/section_coverage/all_sections.md");
        files.WithJson("checks/section_coverage/index.json", json => json["nodes"] = Ids("coverage", "all_sections"));

        var refusal = Only(files.Parse());

        Assert.StartsWith(
            "Node 'all_sections' in check.section_coverage reuses the id of node 'all_sections' at the top level, which check.section_coverage can read",
            refusal.Sentence);
    }

    [Fact]
    public void BranchesOfOneIf_ShareAFolder_SoCannotShareAnId()
    {
        var files = SourceFixture.Full().WithJson("nodes/billing_gate/index.json", json =>
            json["else"] = new JsonObject { ["nodes"] = Ids("billing_text"), ["ret"] = "node.billing_gate.billing_text" });

        var result = files.Parse();

        Assert.NotEmpty(result.Refusals);
        Assert.DoesNotContain(result.Refusals, r => r.Id == "S01-002");
    }

    [Fact]
    public void AFunction_MayReuseATopLevelId_AndAnotherFunctions()
    {
        var files = SourceFixture.Full();
        Copy(files, "nodes/plan.md", "functions/draft_section/plan.md");
        files.WithJson("functions/draft_section/index.json", json => json["nodes"] = Ids("standard_draft", "patient_draft", "apply_instructions", "plan"));
        foreach (var path in files.Keys.Where(k => k.StartsWith("functions/draft_section/", StringComparison.Ordinal)).ToList())
            files["functions/other/" + path["functions/draft_section/".Length..]] = files[path];
        files.WithJson("functions/index.json", json => json["items"] = Ids("draft_section", "other"));

        var result = files.Parse();

        Assert.Empty(result.Refusals);
    }

    [Fact]
    public void ANodeAndACheck_MayShareAnId()
    {
        var files = SourceFixture.Full();
        Copy(files, "checks/all_sections.md", "checks/plan.md");
        files.WithJson("checks/index.json", json => json["items"] = Ids("section_coverage", "all_sections", "plan"));

        Assert.Empty(files.Parse().Refusals);
    }

    [Fact]
    public void InsideAWhile_IdsAreUniqueAtAnyDepth()
    {
        var files = SourceFixture.Full();
        files.Remove("nodes/refine/judge.md");
        foreach (var loop in new[] { "first", "second" })
        {
            files[$"nodes/refine/{loop}/index.json"] = files[Paragraphs];
            files.WithJson($"nodes/refine/{loop}/index.json", json =>
            {
                json["nodes"] = Ids("judge");
                json["ret"] = $"node.refine.{loop}.judge";
            });
            files[$"nodes/refine/{loop}/judge.md"] = files["nodes/sections/paragraphs/draft_paragraph.md"];
        }

        files.WithJson(Refine, json => json["nodes"] = Ids("draft_summary", "first", "second"));

        var refusal = Only(files.Parse());

        Assert.Equal("S01-036", refusal.Id);
        Assert.Equal(
            "Node 'judge' appears twice inside while 'refine' (refine.first.judge and refine.second.judge): pass.….node.judge must name one node.",
            refusal.Sentence);
        Assert.Equal("nodes/refine/index.json", refusal.Path);
    }

    [Fact]
    public void ANodeSeenByAnother_InsideAWhile_IsS01002Only()
    {
        var files = SourceFixture.Full();
        Copy(files, "nodes/refine/judge.md", "nodes/refine/deeper/judge.md");
        files["nodes/refine/deeper/index.json"] = files[Paragraphs];
        files.WithJson("nodes/refine/deeper/index.json", json =>
        {
            json["nodes"] = Ids("judge");
            json["ret"] = "node.refine.deeper.judge";
        });
        files.WithJson(Refine, json => json["nodes"] = Ids("draft_summary", "judge", "deeper"));

        var refusal = Only(files.Parse());

        Assert.Equal("S01-002", refusal.Id);
    }

    [Fact]
    public void RetMustNameAnOwnChild()
    {
        var wrong = SourceFixture.Full().WithJson(Sections, json => json["ret"] = "node.plan").Parse();
        var refusal = Only(wrong);
        Assert.Equal("S04-012", refusal.Id);
        Assert.Equal(
            "Loop 'sections' returns node.plan, which is not one of its own children (node.sections.outline, node.sections.paragraphs): "
            + "write the child's full name, for example node.sections.paragraphs.",
            refusal.Sentence);
        Assert.Equal(Sections, refusal.Path);

        var bare = Only(SourceFixture.Full().WithJson(Sections, json => json["ret"] = "paragraphs").Parse());
        Assert.StartsWith("Loop 'sections' returns paragraphs, which is not one of its own children", bare.Sentence);

        Assert.Empty(SourceFixture.Full().WithJson(Sections, json => json["ret"] = "node.sections.outline").Parse().Refusals);
    }

    [Fact]
    public void ARetInAFunction_IsCountedFromTheFunctionsRoot()
    {
        const string function = "functions/draft_section/index.json";
        Assert.Empty(SourceFixture.Full().Parse().Refusals);

        var refusal = Only(SourceFixture.Full().WithJson(function, json => json["ret"] = "node.draft_section.apply_instructions").Parse());

        Assert.Equal(
            "Function 'draft_section' returns node.draft_section.apply_instructions, which is not one of its own children "
            + "(node.standard_draft, node.patient_draft, node.apply_instructions): write the child's full name, for example node.sections.paragraphs.",
            refusal.Sentence);
    }

    [Fact]
    public void ARetInAnIfAndACase_NamesTheBodySubject()
    {
        var gate = Only(SourceFixture.Full().WithJson("nodes/billing_gate/index.json", json => json["then"]!["ret"] = "node.billing_text").Parse());
        Assert.StartsWith("If 'billing_gate' then returns node.billing_text, which is not one of its own children (node.billing_gate.billing_text)", gate.Sentence);

        var route = Only(SourceFixture.Full().WithJson("nodes/sections_draft/route/index.json", json =>
            json["branches"]![1]!["ret"] = "node.sections_draft.route.narrative_draft").Parse());
        Assert.StartsWith(
            "Case 'route' branch 2 returns node.sections_draft.route.narrative_draft, which is not one of its own children (node.sections_draft.route.table_draft)",
            route.Sentence);
    }

    [Fact]
    public void ARetThatWasAlreadyRefused_IsNotJudgedAgain()
    {
        var result = SourceFixture.Full().WithJson(Sections, json => json["ret"] = null).Parse();

        Assert.Equal(["S04-011"], result.Refusals.Select(r => r.Id));
    }

    [Fact]
    public void AScopeFault_IsReportedWithIndependentFaults()
    {
        var files = SourceFixture.Full();
        Copy(files, "nodes/plan.md", "nodes/sections/paragraphs/plan.md");
        files.WithJson(Paragraphs, json => json["nodes"] = Ids("draft_paragraph", "plan"));
        files.WithJson(Sections, json => json["max"] = 0);
        files.WithJson("tests/note_basic.json", json => json["bogus"] = 1);

        var result = files.Parse();

        Assert.Equal(["S01-002", "S01-023", "S04-024"], result.Refusals.Select(r => r.Id).Order());
        Assert.Null(result.Package);
    }

    [Theory]
    [InlineData("{\"var\":\"a\"}", 1)]
    [InlineData("{\"===\":[{\"var\":\"a\"},1]}", 3)]
    [InlineData("true", 1)]
    [InlineData("{\"and\":[[1,2],{\"!\":[null]}]}", 5)]
    [InlineData("{}", 1)]
    [InlineData("{\"a\":1,\"b\":[2,3]}", 4)]
    public void CountExpressionNodes_FollowsD1(string json, int expected) =>
        Assert.Equal(expected, SourceScopes.CountExpressionNodes(JsonNode.Parse(json)));

    [Fact]
    public void ANullSite_CountsNothing() => Assert.Equal(0, SourceScopes.CountExpressionNodes(null));

    [Fact]
    public void AnExpressionOf1001Nodes_IsRefused()
    {
        var refusal = Only(SourceFixture.Full().WithJson("nodes/section_count.md", json => json["expression"] = Big(1001)).Parse());

        Assert.Equal("S01-042", refusal.Id);
        Assert.Equal(
            "Node 'section_count' expression in 'nodes/section_count.md' has 1,001 expression nodes; the most is SourceBounds.expressionNodes (1,000).",
            refusal.Sentence);
        Assert.Equal("nodes/section_count.md", refusal.Path);
        Assert.Empty(SourceFixture.Full().WithJson("nodes/section_count.md", json => json["expression"] = Big(1000)).Parse().Refusals);
    }

    [Theory]
    [InlineData("nodes/plan.md", "if", "Node 'plan' if", "nodes/plan.md")]
    [InlineData("checks/all_sections.md", "expression", "Check 'all_sections' expression", "checks/all_sections.md")]
    [InlineData("checks/section_coverage/coverage.md", "if", "Check 'coverage' if", "checks/section_coverage/coverage.md")]
    [InlineData("nodes/sections/index.json", "until", "Loop 'sections' until", "nodes/sections/index.json")]
    [InlineData("nodes/refine/index.json", "while", "While 'refine' while", "nodes/refine/index.json")]
    [InlineData("nodes/refine/index.json", "until", "While 'refine' until", "nodes/refine/index.json")]
    [InlineData("nodes/billing_gate/index.json", "if", "If 'billing_gate' if", "nodes/billing_gate/index.json")]
    [InlineData("functions/draft_section/standard_draft.md", "if", "Node 'standard_draft' if", "functions/draft_section/standard_draft.md")]
    [InlineData("snippets/closing_line.md", "if", "Snippet 'closing_line' if", "snippets/closing_line.md")]
    [InlineData("macros/follow_up.md", "if", "Macro 'follow_up' if", "macros/follow_up.md")]
    [InlineData("macros/phrases/index.json", "if", "macros/phrases/index.json if", "macros/phrases/index.json")]
    [InlineData("macros/phrases/thanks.md", "if", "Item 'phrases.thanks' if", "macros/phrases/thanks.md")]
    [InlineData("deliverables/note.md", "if", "Deliverable 'note' if", "deliverables/note.md")]
    [InlineData("deliverables/emr/consult.md", "if", "Deliverable 'consult' if", "deliverables/emr/consult.md")]
    public void EverySite_IsNamedAndBounded(string file, string member, string site, string path)
    {
        var result = SourceFixture.Full().WithJson(file, json => json[member] = Big(1001)).Parse();

        var refusal = Assert.Single(result.Refusals, r => r.Id == "S01-042");
        V31Contract.AssertSentence(refusal);
        Assert.Equal($"{site} in '{path}' has 1,001 expression nodes; the most is SourceBounds.expressionNodes (1,000).", refusal.Sentence);
    }

    [Fact]
    public void AnAgentPartsCondition_IsBounded()
    {
        var result = SourceFixture.Full().WithJson("nodes/plan.md", json =>
            json["parts"] = new JsonObject { ["tone"] = new JsonObject { ["if"] = Big(1001) } }).Parse();

        var refusal = Assert.Single(result.Refusals, r => r.Id == "S01-042");
        Assert.StartsWith("Node 'plan' part 'tone' if in 'nodes/plan.md' has 1,001", refusal.Sentence);
    }

    [Fact]
    public void ADeeplyNestedExpression_IsCountedWithoutOverflow()
    {
        JsonNode inner = JsonValue.Create(1)!;
        for (var i = 0; i < 30; i++)
            inner = new JsonObject { ["!"] = new JsonArray(inner) };

        Assert.Equal(31, SourceScopes.CountExpressionNodes(inner));
    }

    [Fact]
    public void EveryScopeSentence_IsTheDesigns()
    {
        SourceRefusal[] sentences =
        [
            SourceSentences.NodeReusesVisibleId("plan", "sections.paragraphs", "at the top level", "p"),
            SourceSentences.NodeReusesVisibleId("outline", "check.a.b", "in check.a", "p"),
            SourceSentences.NodeTwiceInWhile("judge", "refine", "refine.a.judge", "refine.b.judge", "p"),
            SourceSentences.ExpressionTooLarge("Node 'x' if", "nodes/x.md", 1001),
            SourceSentences.ReturnsNonChild("Loop 'x'", "node.y", "node.x.a, node.x.b", "p"),
        ];

        Assert.Equal(["S01-002", "S01-002", "S01-036", "S01-042", "S04-012"], sentences.Select(s => s.Id));
        foreach (var sentence in sentences)
            V31Contract.AssertSentence(sentence);
    }

    // ---- S04-012 is judged against the ids as written ---------------------------------------------

    private const string Function = "functions/draft_section/index.json";

    private static void AssertRetNotJudged(Dictionary<string, byte[]> files, string real)
    {
        var result = files.Parse();

        Assert.Null(result.Package);
        Assert.Contains(result.Refusals, r => r.Id == real);
        Assert.DoesNotContain(result.Refusals, r => r.Id == "S04-012");
    }

    [Fact]
    public void ARetNamingAChildWithABrokenFile_IsNotJudged()
    {
        AssertRetNotJudged(SourceFixture.Full().With("nodes/sections/outline.md", "no front matter\n")
            .WithJson(Sections, json => json["ret"] = "node.sections.outline"), "S01-016");
        AssertRetNotJudged(SourceFixture.Full().With("functions/draft_section/apply_instructions.md", "no front matter\n"), "S01-016");
    }

    [Fact]
    public void ARetNamingAChildWithNoFile_IsNotJudged()
    {
        AssertRetNotJudged(SourceFixture.Full().Without("nodes/sections/outline.md")
            .WithJson(Sections, json => json["ret"] = "node.sections.outline"), "S01-009");
        AssertRetNotJudged(SourceFixture.Full().Without("functions/draft_section/apply_instructions.md"), "S01-009");
    }

    [Fact]
    public void ARetNamingAChildListedTwice_IsNotJudged()
    {
        AssertRetNotJudged(SourceFixture.Full().WithJson(Sections, json =>
        {
            json["nodes"] = Ids("outline", "paragraphs", "outline");
            json["ret"] = "node.sections.outline";
        }), "S04-013");
        AssertRetNotJudged(SourceFixture.Full().WithJson(Function, json =>
            json["nodes"] = Ids("standard_draft", "patient_draft", "apply_instructions", "apply_instructions")), "S04-013");
    }

    [Fact]
    public void ARetNamingAChildWhoseIdIsRefused_IsNotJudged()
    {
        AssertRetNotJudged(SourceFixture.Full().WithJson(Sections, json =>
        {
            json["nodes"] = Ids("outline", "paragraphs", "Bad-Id");
            json["ret"] = "node.sections.Bad-Id";
        }), "S01-030");
        AssertRetNotJudged(SourceFixture.Full().WithJson(Function, json =>
        {
            json["nodes"] = Ids("standard_draft", "patient_draft", "Bad-Id");
            json["ret"] = "node.Bad-Id";
        }), "S01-030");
    }

    // ---- Scopes: the remaining shapes -------------------------------------------------------------

    private static void AddJudgeLoop(Dictionary<string, byte[]> files, string folder, string key)
    {
        files[$"{folder}/index.json"] = files[Paragraphs];
        files.WithJson($"{folder}/index.json", json =>
        {
            json["nodes"] = Ids("judge");
            json["ret"] = $"{key}.judge";
        });
        files[$"{folder}/judge.md"] = files["nodes/sections/paragraphs/draft_paragraph.md"];
    }

    [Fact]
    public void AFunctionsScope_IsJudgedToo()
    {
        var files = SourceFixture.Full();
        files["functions/draft_section/l/index.json"] = files[Paragraphs];
        files.WithJson("functions/draft_section/l/index.json", json =>
        {
            json["nodes"] = Ids("standard_draft");
            json["ret"] = "node.l.standard_draft";
        });
        files["functions/draft_section/l/standard_draft.md"] = files["functions/draft_section/standard_draft.md"];
        files.WithJson(Function, json => json["nodes"] = Ids("standard_draft", "patient_draft", "l", "apply_instructions"));

        var refusal = Only(files.Parse());

        Assert.Equal("S01-002", refusal.Id);
        Assert.StartsWith("Node 'standard_draft' in l reuses the id of node 'standard_draft' at the top level, which l can read", refusal.Sentence);
    }

    [Fact]
    public void NestedWhiles_ReportOnTheOutermost()
    {
        var files = SourceFixture.Full();
        files.Remove("nodes/refine/judge.md");
        files["nodes/refine/inner/index.json"] = files[Refine];
        files.WithJson("nodes/refine/inner/index.json", json =>
        {
            json["nodes"] = Ids("first", "second");
            json["ret"] = "node.refine.inner.first";
        });
        AddJudgeLoop(files, "nodes/refine/inner/first", "node.refine.inner.first");
        AddJudgeLoop(files, "nodes/refine/inner/second", "node.refine.inner.second");
        files.WithJson(Refine, json => json["nodes"] = Ids("draft_summary", "inner"));

        var refusal = Only(files.Parse());

        Assert.Equal("S01-036", refusal.Id);
        Assert.Equal(
            "Node 'judge' appears twice inside while 'refine' (refine.inner.first.judge and refine.inner.second.judge): pass.\u2026.node.judge must name one node.",
            refusal.Sentence);
        Assert.Equal("nodes/refine/index.json", refusal.Path);
    }

    [Fact]
    public void APairSplitAcrossThenAndElse_IsS01036()
    {
        var files = SourceFixture.Full();
        files.Remove("nodes/refine/judge.md");
        files["nodes/refine/gate/index.json"] = files["nodes/billing_gate/index.json"];
        files.WithJson("nodes/refine/gate/index.json", json =>
        {
            json["then"] = new JsonObject { ["nodes"] = Ids("a"), ["ret"] = "node.refine.gate.a" };
            json["else"] = new JsonObject { ["nodes"] = Ids("b"), ["ret"] = "node.refine.gate.b" };
        });
        AddJudgeLoop(files, "nodes/refine/gate/a", "node.refine.gate.a");
        AddJudgeLoop(files, "nodes/refine/gate/b", "node.refine.gate.b");
        files.WithJson(Refine, json => json["nodes"] = Ids("draft_summary", "gate"));

        var refusal = Only(files.Parse());

        Assert.Equal("S01-036", refusal.Id);
        Assert.Contains("(refine.gate.a.judge and refine.gate.b.judge)", refusal.Sentence);
    }
}
