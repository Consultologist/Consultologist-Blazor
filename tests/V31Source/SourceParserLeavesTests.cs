using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the leaves of <c>nodes/</c>, <c>checks/</c> and a function —
/// agent, rendered, computed and call — read and judged (part 01 §§ 4, 5.1, 5.2).
/// </summary>
public class SourceParserLeavesTests
{
    private static SourceRefusal Only(SourceParseResult result)
    {
        var refusal = Assert.Single(result.Refusals);
        Assert.Null(result.Package);
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private static Dictionary<string, byte[]> Edit(string path, Action<JsonObject> edit) =>
        SourceFixture.Full().WithJson(path, edit);

    private static string Body(Dictionary<string, byte[]> files, string path)
    {
        var text = SourceFixture.Text(files, path);
        return text[(text.IndexOf("\n---\n", 4, StringComparison.Ordinal) + 5)..];
    }

    [Fact]
    public void Full_ParsesItsTopLevelLeaves()
    {
        var files = SourceFixture.Full();
        var result = files.Parse();

        Assert.Empty(result.Refusals);
        var package = result.Package!;
        // The flow folders are not in the model until the flow nodes are built.
        Assert.Equal(
            ["plan", "section_count", "extract_input_terms", "extract_note_terms"],
            package.Nodes.Nodes.Select(n => n.Id));
        var plan = Assert.IsType<SourceLeaf>(package.Nodes.Nodes[0]);
        Assert.Equal("agent", plan.Kind);
        Assert.Equal("node.plan", plan.Key);
        Assert.Equal("nodes/plan.md", plan.Path);
        Assert.Equal("Planning the sections", (string?)plan.Header["label"]);
        Assert.Equal(Body(files, "nodes/plan.md"), plan.Body);
        Assert.StartsWith("Plan the sections", plan.Body);

        var count = Assert.IsType<SourceLeaf>(package.Nodes.Nodes[1]);
        Assert.Equal("computed", count.Kind);
        Assert.Equal("", count.Body);

        Assert.Equal(["all_sections"], package.Checks.Nodes.Select(n => n.Id));
        var check = Assert.IsType<SourceLeaf>(package.Checks.Nodes[0]);
        Assert.Equal("check.all_sections", check.Key);
    }

    [Fact]
    public void LeavesInFoldersAndFunctions_AreReadAndJudged()
    {
        var files = SourceFixture.Full();
        Assert.Empty(files.Parse().Refusals);

        files.WithJson("nodes/sections/paragraphs/draft_paragraph.md", h => h.Remove("kind"));
        files.WithJson("functions/draft_section/standard_draft.md", h => h["kind"] = "let2");
        files.WithJson("checks/section_coverage/coverage.md", h => h["producer"] = "generated");
        var refusals = files.Parse().Refusals;

        Assert.Equal(
            ["S01-028", "S02-007", "S01-021"],
            refusals.OrderBy(r => r.Path, StringComparer.Ordinal).Select(r => r.Id));
        var producer = refusals.Single(r => r.Id == "S01-028");
        Assert.Equal(
            "Check 'check.section_coverage.coverage' is kind computed and declares producer 'generated'; a computed node's producer is computed.",
            producer.Sentence);
        Assert.All(refusals, V31Contract.AssertSentence);
    }

    [Fact]
    public void Keys_AreRootedAtTheirFolders()
    {
        var files = SourceFixture.Full();
        files.WithJson("nodes/sections/paragraphs/draft_paragraph.md", h => h["producer"] = "computed");
        files.WithJson("functions/draft_section/standard_draft.md", h => h["producer"] = "computed");
        var sentences = files.Parse().Refusals.Select(r => r.Sentence).ToList();

        Assert.Contains(sentences, s => s.StartsWith("Node 'node.sections.paragraphs.draft_paragraph' is kind agent"));
        Assert.Contains(sentences, s => s.StartsWith("Node 'node.standard_draft' is kind agent"));
    }

    [Fact]
    public void AWindowsCheckout_ParsesToTheSamePackage()
    {
        var unix = SourceFixture.Full();
        var windows = unix.ToDictionary(
            f => f.Key,
            f => f.Key.EndsWith(".json", StringComparison.Ordinal) || f.Key.EndsWith(".md", StringComparison.Ordinal)
                ? SourceFixture.Utf8(SourceFixture.Text(unix, f.Key).Replace("\n", "\r\n"))
                : f.Value);

        var expected = unix.Parse();
        var actual = SourceFixture.Parse(windows);

        Assert.Empty(actual.Refusals);
        Assert.Equal(unix.Keys.Where(k => k.EndsWith(".json") || k.EndsWith(".md")).OrderBy(k => k, StringComparer.Ordinal), actual.Normalised);
        var want = expected.Package!.Nodes.Nodes.Cast<SourceLeaf>().Select(l => l.Body);
        var got = actual.Package!.Nodes.Nodes.Cast<SourceLeaf>().Select(l => l.Body);
        Assert.Equal(want, got);
    }

    [Fact]
    public void ABlankLineAfterTheFence_IsPartOfTheBody()
    {
        var files = SourceFixture.Full();
        var text = SourceFixture.Text(files, "nodes/plan.md");
        var fence = text.IndexOf("\n---\n", 4, StringComparison.Ordinal) + 5;
        files.With("nodes/plan.md", text[..fence] + "\n" + text[fence..]);

        var plan = (SourceLeaf)files.Parse().Package!.Nodes.Nodes[0];

        Assert.StartsWith("\nPlan the sections", plan.Body);
    }

    [Fact]
    public void AnAgentWithoutAPrompt_IsRefused()
    {
        var files = SourceFixture.Full();
        var text = SourceFixture.Text(files, "nodes/plan.md");
        files.With("nodes/plan.md", text[..(text.IndexOf("\n---\n", 4, StringComparison.Ordinal) + 5)] + "\n  \n");

        var refusal = Only(files.Parse());

        Assert.Equal("S01-019", refusal.Id);
        Assert.Equal("Node 'plan' has an empty body; its prompt is the body.", refusal.Sentence);
    }

    [Fact]
    public void ARenderedNodeWithoutATemplate_IsRefused()
    {
        var files = SourceFixture.Full();
        var text = SourceFixture.Text(files, "nodes/billing_gate/billing_text.md");
        files.With("nodes/billing_gate/billing_text.md", text[..(text.IndexOf("\n---\n", 4, StringComparison.Ordinal) + 5)]);

        var refusal = Only(files.Parse());

        Assert.Equal("Node 'billing_text' has an empty body; its template is the body.", refusal.Sentence);
    }

    [Fact]
    public void AComputedNodeWithABody_IsRefused()
    {
        var files = SourceFixture.Full();
        files.With("nodes/section_count.md", SourceFixture.Text(files, "nodes/section_count.md") + "text\n");

        var refusal = Only(files.Parse());

        Assert.Equal("S01-020", refusal.Id);
        Assert.Equal("Node 'section_count' has a body; a computed node keeps everything in its header.", refusal.Sentence);
    }

    [Fact]
    public void ACallWithABody_IsRefused()
    {
        var files = SourceFixture.Full();
        files.With("nodes/standards/section.md", SourceFixture.Text(files, "nodes/standards/section.md") + "text\n");

        var refusal = Only(files.Parse());

        Assert.Equal("Node 'section' has a body; a call keeps everything in its header.", refusal.Sentence);
    }

    [Fact]
    public void ACheckWithABody_IsRefused()
    {
        var files = SourceFixture.Full();
        files.With("checks/all_sections.md", SourceFixture.Text(files, "checks/all_sections.md") + "text\n");

        var refusal = Only(files.Parse());

        Assert.Equal("Check 'all_sections' has a body; a check keeps everything in its header.", refusal.Sentence);
    }

    [Fact]
    public void AnUnknownKind_IsRefused()
    {
        var refusal = Only(Edit("nodes/plan.md", h => h["kind"] = "let2").Parse());

        Assert.Equal("S02-007", refusal.Id);
        Assert.Equal(
            "Node 'plan' declares unknown kind 'let2' (accepted: agent, rendered, computed, for, while, if, case, call).",
            refusal.Sentence);
    }

    [Fact]
    public void AnUnknownKindInAChecksFile_SaysNode()
    {
        var refusal = Only(Edit("checks/all_sections.md", h => h["kind"] = "let2").Parse());

        Assert.StartsWith("Node 'all_sections' declares unknown kind", refusal.Sentence);
    }

    [Fact]
    public void AMissingKind_IsRefused_AndTheFileReadNoFurther()
    {
        var result = Edit("nodes/plan.md", h => { h.Remove("kind"); h.Remove("producer"); }).Parse();

        var refusal = Only(result);
        Assert.Equal("S01-021", refusal.Id);
        Assert.Equal("Node 'plan' is missing 'kind'; every member is written, and null means none.", refusal.Sentence);
    }

    [Fact]
    public void ACheckWithoutKind_IsDeclaredAsACheck()
    {
        var refusal = Only(Edit("checks/all_sections.md", h => h.Remove("kind")).Parse());

        Assert.StartsWith("Check 'all_sections' is missing 'kind'", refusal.Sentence);
    }

    [Fact]
    public void ANullOrNonTextKind_IsRefused()
    {
        var nul = Only(Edit("nodes/plan.md", h => h["kind"] = null).Parse());
        var number = Only(Edit("nodes/plan.md", h => h["kind"] = 3).Parse());

        Assert.Equal("S01-022", nul.Id);
        Assert.Equal("Node 'plan' sets 'kind' to null, which it can never be.", nul.Sentence);
        Assert.Equal("A02-001", number.Id);
        Assert.Equal("Node 'plan' declares 'kind' as a whole number; it is text.", number.Sentence);
    }

    [Theory]
    [InlineData("for")]
    [InlineData("while")]
    [InlineData("if")]
    [InlineData("case")]
    public void AFlowKindInAFile_IsRefused(string kind)
    {
        var refusal = Only(Edit("nodes/plan.md", h => h["kind"] = kind).Parse());

        Assert.Equal("A02-006", refusal.Id);
        Assert.Equal($"Node 'plan' is kind {kind} in a file; a {kind} is a folder with an index.json.", refusal.Sentence);
    }

    [Theory]
    [InlineData("nodes/plan.md", "computed", "Node", "node.plan", "agent", "generated")]
    [InlineData("nodes/section_count.md", "generated", "Node", "node.section_count", "computed", "computed")]
    [InlineData("nodes/billing_gate/billing_text.md", "grounded", "Node", "node.billing_gate.billing_text", "rendered", "computed")]
    [InlineData("nodes/standards/section.md", "generated", "Node", "node.standards.section", "call", "computed")]
    [InlineData("checks/all_sections.md", "generated", "Check", "check.all_sections", "computed", "computed")]
    public void AProducerThatDisagreesWithItsKind_IsRefused(
        string path, string producer, string word, string key, string kind, string expected)
    {
        var refusal = Only(Edit(path, h => h["producer"] = producer).Parse());

        Assert.Equal("S01-028", refusal.Id);
        Assert.Equal(
            $"{word} '{key}' is kind {kind} and declares producer '{producer}'; a {kind} node's producer is {expected}.",
            refusal.Sentence);
    }

    [Fact]
    public void AProducerThatIsNotText_IsOnlyTheMemberRules()
    {
        var refusal = Only(Edit("nodes/plan.md", h => h["producer"] = 3).Parse());

        Assert.Equal("A02-001", refusal.Id);
    }

    [Fact]
    public void APartId_IsHeldToTheIdRules()
    {
        var refusal = Only(Edit("nodes/plan.md", h => h["parts"] = new JsonObject { ["Bad-Part"] = new JsonObject(), ["ok_part"] = new JsonObject() }).Parse());

        Assert.Equal("S01-030", refusal.Id);
        Assert.Contains("'Bad-Part'", refusal.Sentence);
        Assert.Equal("nodes/plan.md", refusal.Path);
    }

    [Fact]
    public void AValidPartsObject_IsAccepted()
    {
        var result = Edit("nodes/plan.md", h => h["parts"] = new JsonObject { ["ok_part"] = new JsonObject() }).Parse();

        Assert.Empty(result.Refusals);
    }

    [Fact]
    public void AWithValueThatIsAnExpression_IsRefused()
    {
        var files = Edit("nodes/standards/section.md", h => h["with"] = new JsonObject
        {
            ["standard"] = "item",
            ["draft"] = new JsonObject { ["var"] = "input.consult_draft.content" },
            ["spare"] = null,
        });

        var refusal = Only(files.Parse());

        Assert.Equal("A02-001", refusal.Id);
        Assert.Equal("Node 'section' declares 'with.draft' as an object; it is text.", refusal.Sentence);
    }

    [Fact]
    public void IndependentFaults_AreAllReported()
    {
        var files = SourceFixture.Full();
        files.With("nodes/stray.md", "x");
        files.WithJson("nodes/section_count.md", h => h.Remove("expression"));
        files.WithJson("nodes/index.json", h => ((JsonArray)h["items"]!).Add("Bad-Id"));

        var result = files.Parse();

        Assert.Equal(
            ["nodes/index.json", "nodes/section_count.md", "nodes/stray.md"],
            result.Refusals.Select(r => r.Path));
        Assert.Equal(["S01-030", "S01-021", "S01-006"], result.Refusals.Select(r => r.Id));
        Assert.All(result.Refusals, V31Contract.AssertSentence);
    }

    [Fact]
    public void ABrokenHeader_IsRefusedOnceAndNothingElse()
    {
        var files = SourceFixture.Full().With("nodes/plan.md", "no fences\n");

        var refusal = Only(files.Parse());

        Assert.Equal("S01-016", refusal.Id);
    }

    [Fact]
    public void EveryLeafSentence_IsTheDesigns()
    {
        var sentences = new[]
        {
            SourceSentences.ProducerDisagrees("Node", "node.plan", "agent", "computed", "generated", "p"),
            SourceSentences.UnknownKind("plan", "let2", "p"),
            SourceSentences.FlowKindInFile("plan", "for", "p"),
            SourceSentences.LeafKindInFolder("plan", "agent", "p"),
        };

        Assert.All(sentences, V31Contract.AssertSentence);
        Assert.Equal(
            "Node 'plan' is kind agent in a folder; a agent node is a file, plan.md.",
            sentences[3].Sentence);
    }
}
