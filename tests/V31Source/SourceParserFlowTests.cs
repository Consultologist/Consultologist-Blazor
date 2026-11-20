using System.Text.Json.Nodes;
using Consultologist.PackageFormat.V31;

namespace Consultologist.Api.Tests.V31Source;

/// <summary>
/// #1499 (specVersion 31, area 02): the flow nodes — <c>for</c>, <c>while</c>, <c>if</c> and
/// <c>case</c> — read as folders through their <c>index.json</c> into the model, and the shape
/// rules of part 04 §§ 1–6 that need nothing but the node itself (D16).
/// </summary>
public class SourceParserFlowTests
{
    private const string Sections = "nodes/sections/index.json";
    private const string Standards = "nodes/standards/index.json";
    private const string Refine = "nodes/refine/index.json";
    private const string Gate = "nodes/billing_gate/index.json";
    private const string Route = "nodes/sections_draft/route/index.json";

    private static SourceRefusal Only(SourceParseResult result)
    {
        var refusal = Assert.Single(result.Refusals);
        Assert.Null(result.Package);
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private static Dictionary<string, byte[]> Edit(string path, Action<JsonObject> edit) =>
        SourceFixture.Full().WithJson(path, edit);

    private static JsonArray Ids(params string[] ids) => new(ids.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray());

    private static JsonObject Branch(JsonObject index, int n) => index["branches"]![n - 1]!.AsObject();

    private static SourcePackage Full()
    {
        var result = SourceFixture.Full().Parse();
        Assert.Empty(result.Refusals);
        return result.Package!;
    }

    [Fact]
    public void Full_ParsesItsLoops()
    {
        var package = Full();

        Assert.Equal(
            ["plan", "section_count", "sections", "sections_draft", "refine", "billing_gate", "standards", "extract_input_terms", "extract_note_terms"],
            package.Nodes.Nodes.Select(n => n.Id));
        var sections = Assert.IsType<SourceFor>(package.Nodes.Nodes[2]);
        Assert.Equal(("for", "node.sections", "nodes/sections"), (sections.Kind, sections.Key, sections.Path));
        Assert.Equal("Sections", (string?)sections.Header["label"]);
        Assert.Equal("node.plan.value.sections", sections.In);
        Assert.True(sections.Parallel);
        Assert.Null(sections.MaxParallel);
        Assert.Null(sections.Until);
        Assert.Null(sections.Max);
        Assert.Equal("title", sections.ItemLabel);
        Assert.Equal("node.sections.paragraphs", sections.Body.Ret);
        Assert.Equal(["outline", "paragraphs"], sections.Body.Nodes.Select(n => n.Id));

        Assert.Equal("node.sections.outline", Assert.IsType<SourceLeaf>(sections.Body.Nodes[0]).Key);
        var paragraphs = Assert.IsType<SourceFor>(sections.Body.Nodes[1]);
        Assert.Equal(("node.sections.paragraphs", "nodes/sections/paragraphs"), (paragraphs.Key, paragraphs.Path));
        Assert.Null(paragraphs.ItemLabel);
        var draft = Assert.IsType<SourceLeaf>(Assert.Single(paragraphs.Body.Nodes));
        Assert.Equal("node.sections.paragraphs.draft_paragraph", draft.Key);
        Assert.Equal("nodes/sections/paragraphs/draft_paragraph.md", draft.Path);

        var standards = Assert.IsType<SourceFor>(package.Nodes.Nodes[6]);
        Assert.Equal(4, standards.MaxParallel);
        Assert.Equal("snippet.standards", standards.In);
        Assert.Equal("node.standards.section", Assert.Single(standards.Body.Nodes).Key);
    }

    [Fact]
    public void Full_ParsesItsWhile()
    {
        var refine = Assert.IsType<SourceWhile>(Full().Nodes.Nodes[4]);

        Assert.Equal(("while", "node.refine", "nodes/refine"), (refine.Kind, refine.Key, refine.Path));
        Assert.Equal(3, refine.Max);
        Assert.False(refine.CheckFirst);
        Assert.Null(refine.Until);
        var condition = Assert.IsType<JsonObject>(refine.While);
        Assert.True(condition.ContainsKey("!=="));
        Assert.Equal(["draft_summary", "judge"], refine.Body.Nodes.Select(n => n.Id));
        Assert.Equal("node.refine.draft_summary", refine.Body.Ret);
        Assert.Equal("node.refine.judge", refine.Body.Nodes[1].Key);
    }

    [Fact]
    public void Full_ParsesItsIfAndCase()
    {
        var package = Full();

        var gate = Assert.IsType<SourceIf>(package.Nodes.Nodes[5]);
        Assert.Equal(("if", "node.billing_gate"), (gate.Kind, gate.Key));
        Assert.True(Assert.IsType<JsonObject>(gate.If).ContainsKey("==="));
        var text = Assert.IsType<SourceLeaf>(Assert.Single(gate.Then.Nodes));
        Assert.Equal(("billing_text", "node.billing_gate.billing_text", "rendered"), (text.Id, text.Key, text.Kind));
        Assert.Equal("node.billing_gate.billing_text", gate.Then.Ret);
        Assert.Null(gate.Else);

        var drafting = Assert.IsType<SourceFor>(package.Nodes.Nodes[3]);
        var route = Assert.IsType<SourceCase>(Assert.Single(drafting.Body.Nodes));
        Assert.Equal(("case", "node.sections_draft.route", "nodes/sections_draft/route"), (route.Kind, route.Key, route.Path));
        Assert.Equal("item.value.kind", route.Case);
        Assert.Equal(3, route.Branches.Count);
        Assert.Equal(["narrative"], route.Branches[0].When);
        Assert.Equal("node.sections_draft.route.narrative_draft", route.Branches[0].Body.Ret);
        // The branches' children sit flat in the case's folder.
        var table = Assert.Single(route.Branches[1].Body.Nodes);
        Assert.Equal(("node.sections_draft.route.table_draft", "nodes/sections_draft/route/table_draft.md"), (table.Key, table.Path));
        Assert.Equal(["omit"], route.Branches[2].When);
        Assert.Empty(route.Branches[2].Body.Nodes);
        Assert.Null(route.Branches[2].Body.Ret);
        Assert.Null(route.Else);
    }

    [Fact]
    public void AnIfElseAndACaseElse_AreRead()
    {
        var files = SourceFixture.Full()
            .With("nodes/billing_gate/no_billing.md", SourceFixture.Text(SourceFixture.Full(), "nodes/billing_gate/billing_text.md"))
            .With("nodes/sections_draft/route/other_draft.md", SourceFixture.Text(SourceFixture.Full(), "nodes/sections_draft/route/table_draft.md"));
        files.WithJson(Gate, json => json["else"] = new JsonObject { ["nodes"] = Ids("no_billing"), ["ret"] = "node.billing_gate.no_billing" });
        files.WithJson(Route, json => json["else"] = new JsonObject { ["nodes"] = Ids("other_draft"), ["ret"] = "node.sections_draft.route.other_draft" });

        var result = files.Parse();

        Assert.Empty(result.Refusals);
        var gate = (SourceIf)result.Package!.Nodes.Nodes[5];
        Assert.Equal("node.billing_gate.no_billing", Assert.Single(gate.Else!.Nodes).Key);
        Assert.Equal("node.billing_gate.no_billing", gate.Else.Ret);
        var route = (SourceCase)((SourceFor)result.Package.Nodes.Nodes[3]).Body.Nodes[0];
        Assert.Equal("node.sections_draft.route.other_draft", Assert.Single(route.Else!.Nodes).Key);
    }

    [Fact]
    public void Full_ParsesItsCheckLoop()
    {
        var checks = Full().Checks;

        Assert.Equal(["section_coverage", "all_sections"], checks.Nodes.Select(n => n.Id));
        var loop = Assert.IsType<SourceFor>(checks.Nodes[0]);
        Assert.Equal(("check.section_coverage", "node.standards.items"), (loop.Key, loop.In));
        Assert.Equal("check.section_coverage.coverage", Assert.Single(loop.Body.Nodes).Key);
        Assert.Equal("check.section_coverage.coverage", loop.Body.Ret);
    }

    [Fact]
    public void Minimal_StillParses()
    {
        var result = SourceFixture.Minimal().Parse();

        Assert.Empty(result.Refusals);
        Assert.Equal("node.plan", Assert.Single(result.Package!.Nodes.Nodes).Key);
    }

    [Fact]
    public void AFlowKindInAFolder_NeedsAFlowKind()
    {
        var refusal = Only(Edit(Refine, json => json["kind"] = "agent").Parse());

        Assert.Equal("A02-007", refusal.Id);
        Assert.Equal("Node 'refine' is kind agent in a folder; a agent node is a file, refine.md.", refusal.Sentence);
        Assert.Equal(Refine, refusal.Path);
    }

    [Fact]
    public void AFolderKind_IsReadFirst_AndAFaultEndsTheRead()
    {
        // Each stops the read: the folder's children are not strays, and nothing else is said.
        Assert.Equal(
            "Node 'refine' is missing 'kind'; every member is written, and null means none.",
            Only(Edit(Refine, json => json.Remove("kind")).Parse()).Sentence);
        Assert.Equal("S01-022", Only(Edit(Refine, json => json["kind"] = null).Parse()).Id);
        Assert.Equal(
            "Node 'refine' declares 'kind' as a whole number; it is text.",
            Only(Edit(Refine, json => json["kind"] = 3).Parse()).Sentence);
        Assert.Equal(
            "Node 'refine' declares unknown kind 'loop' (accepted: agent, rendered, computed, for, while, if, case, call).",
            Only(Edit(Refine, json => json["kind"] = "loop").Parse()).Sentence);
        Assert.Equal(
            "Check 'section_coverage' is missing 'kind'; every member is written, and null means none.",
            Only(Edit("checks/section_coverage/index.json", json => json.Remove("kind")).Parse()).Sentence);
    }

    [Theory]
    [InlineData(Sections, "Loop", "sections")]
    [InlineData(Refine, "While", "refine")]
    [InlineData(Gate, "If", "billing_gate")]
    [InlineData(Route, "Case", "route")]
    [InlineData("checks/section_coverage/index.json", "Loop", "section_coverage")]
    public void AFlowNodeWithANullLabel_IsRefused(string path, string word, string id)
    {
        var refusal = Only(Edit(path, json => json["label"] = null).Parse());

        Assert.Equal("S04-009", refusal.Id);
        Assert.Equal(
            $"{word} '{id}' declares label null; a loop's or branch's label is what the rail, History and the diagram show: write one.",
            refusal.Sentence);
        Assert.Equal(path, refusal.Path);
    }

    [Fact]
    public void AFlowNodesLabel_IsOtherwiseHeldToTheMemberRules()
    {
        Assert.Equal("S01-021", Only(Edit(Sections, json => json.Remove("label")).Parse()).Id);
        Assert.Equal(
            "Loop 'sections' declares 'label' as a whole number; it is text.",
            Only(Edit(Sections, json => json["label"] = 3).Parse()).Sentence);
        Assert.Equal("Loop 'sections' has an empty label.", Only(Edit(Sections, json => json["label"] = " ").Parse()).Sentence);
    }

    [Theory]
    [InlineData(Refine, "parallel", "While 'refine' declares parallel, which belongs to a for.")]
    [InlineData(Refine, "then", "While 'refine' declares then, which belongs to an if.")]
    [InlineData(Refine, "else", "While 'refine' declares else, which belongs to an if.")]
    [InlineData(Sections, "else", "Loop 'sections' declares else, which belongs to an if.")]
    [InlineData(Sections, "checkFirst", "Loop 'sections' declares checkFirst, which belongs to a while.")]
    [InlineData(Sections, "branches", "Loop 'sections' declares branches, which belongs to a case.")]
    [InlineData(Gate, "nodes", "If 'billing_gate' declares nodes, which belongs to a for.")]
    [InlineData(Gate, "until", "If 'billing_gate' declares until, which belongs to a for.")]
    [InlineData(Gate, "case", "If 'billing_gate' declares case, which belongs to a case.")]
    [InlineData(Route, "ret", "Case 'route' declares ret, which belongs to a for.")]
    [InlineData(Route, "while", "Case 'route' declares while, which belongs to a while.")]
    [InlineData(Route, "if", "Case 'route' declares if, which belongs to an if.")]
    public void AMemberOfAnotherKind_IsNamed(string path, string member, string sentence)
    {
        var refusal = Only(Edit(path, json => json[member] = true).Parse());

        Assert.Equal("S04-010", refusal.Id);
        Assert.Equal(sentence, refusal.Sentence);
    }

    [Fact]
    public void AMemberOfNoKind_IsUnknown()
    {
        var refusal = Only(Edit(Refine, json => json["maxx"] = 3).Parse());

        Assert.Equal("S01-023", refusal.Id);
        Assert.Equal("While 'refine' in 'nodes/refine/index.json' declares unknown member 'maxx'. Did you mean 'max'?", refusal.Sentence);
    }

    [Fact]
    public void AProducerOtherThanComputed_IsRefused()
    {
        var refusal = Only(Edit(Refine, json => json["producer"] = "generated").Parse());

        Assert.Equal("S01-028", refusal.Id);
        Assert.Equal(
            "Node 'node.refine' is kind while and declares producer 'generated'; a while node's producer is computed.",
            refusal.Sentence);
    }

    [Fact]
    public void ANullRet_IsRefused()
    {
        Assert.Equal(
            "Loop 'sections' returns nothing; name the body node the outside reads with ret.",
            Only(Edit(Sections, json => json["ret"] = null).Parse()).Sentence);
        Assert.Equal(
            "While 'refine' returns nothing; name the body node the outside reads with ret.",
            Only(Edit(Refine, json => json["ret"] = null).Parse()).Sentence);
        Assert.Equal(
            "If 'billing_gate' then returns nothing; name the body node the outside reads with ret.",
            Only(Edit(Gate, json => json["then"]!["ret"] = null).Parse()).Sentence);

        var refusal = Only(Edit(Sections, json => json["ret"] = 3).Parse());
        Assert.Equal("Loop 'sections' declares 'ret' as a whole number; it is text.", refusal.Sentence);
    }

    [Fact]
    public void ABodyListingANodeTwice_IsRefused()
    {
        var then = Only(Edit(Gate, json => json["then"]!["nodes"] = Ids("billing_text", "billing_text")).Parse());
        Assert.Equal("S04-013", then.Id);
        Assert.Equal("If 'billing_gate' then lists node 'billing_text' twice; a body is a set.", then.Sentence);
        Assert.Equal(Gate, then.Path);

        Assert.Equal(
            "While 'refine' lists node 'judge' twice; a body is a set.",
            Only(Edit(Refine, json => json["nodes"] = Ids("draft_summary", "judge", "judge", "judge")).Parse()).Sentence);
    }

    [Fact]
    public void ANodeListedInTwoBodies_IsListedTwiceInTheLaterOne()
    {
        var other = Only(Edit(Gate, json => json["else"] = new JsonObject
        {
            ["nodes"] = Ids("billing_text"), ["ret"] = "node.billing_gate.billing_text",
        }).Parse());
        Assert.Equal("If 'billing_gate' else lists node 'billing_text' twice; a body is a set.", other.Sentence);

        var branch = Only(Edit(Route, json => Branch(json, 2)["nodes"] = Ids("table_draft", "narrative_draft")).Parse());
        Assert.Equal("Case 'route' branch 2 lists node 'narrative_draft' twice; a body is a set.", branch.Sentence);
    }

    [Fact]
    public void ABodyListEntryThatIsNotText_IsRefused()
    {
        var refusal = Only(Edit(Refine, json => ((JsonArray)json["nodes"]!).Add(3)).Parse());

        Assert.Equal("A02-001", refusal.Id);
        Assert.Equal("While 'refine' declares 'nodes' as a list; it is a list of text.", refusal.Sentence);

        var then = Only(Edit(Gate, json => ((JsonArray)json["then"]!["nodes"]!).Add(null)).Parse());
        Assert.Equal("If 'billing_gate' then declares 'nodes' as a list; it is a list of text.", then.Sentence);
    }

    [Fact]
    public void AnEmptyLoopBody_IsRefused()
    {
        var loop = Edit(Standards, json => json["nodes"] = new JsonArray()).Without("nodes/standards/section.md");
        var refusal = Only(loop.Parse());
        Assert.Equal("S04-018", refusal.Id);
        Assert.Equal("Loop 'standards' has no body; name the nodes that run per item in nodes.", refusal.Sentence);

        var pass = Edit(Refine, json => json["nodes"] = new JsonArray())
            .Without("nodes/refine/draft_summary.md").Without("nodes/refine/judge.md");
        refusal = Only(pass.Parse());
        Assert.Equal("S04-034", refusal.Id);
        Assert.Equal("While 'refine' has no body; name the nodes that run per pass in nodes.", refusal.Sentence);
    }

    [Fact]
    public void AnEmptyThenOrElse_HasNoBody()
    {
        var then = Only(Edit(Gate, json => json["then"]!["nodes"] = new JsonArray()).Without("nodes/billing_gate/billing_text.md").Parse());
        Assert.Equal("A02-008", then.Id);
        Assert.Equal("If 'billing_gate' then has no body; name the nodes that run in nodes.", then.Sentence);

        var other = Only(Edit(Gate, json => json["else"] = new JsonObject { ["nodes"] = new JsonArray(), ["ret"] = "node.billing_gate.x" }).Parse());
        Assert.Equal("If 'billing_gate' else has no body; name the nodes that run in nodes.", other.Sentence);

        var caseElse = Only(Edit(Route, json => json["else"] = new JsonObject { ["nodes"] = new JsonArray(), ["ret"] = "node.x" }).Parse());
        Assert.Equal("Case 'route' else has no body; name the nodes that run in nodes.", caseElse.Sentence);
    }

    [Fact]
    public void ABodyObject_IsHeldToItsShape()
    {
        var result = Edit(Gate, json => json["then"] = new JsonObject { ["nodes"] = Ids("billing_text"), ["extra"] = 1 }).Parse();

        Assert.Equal(["S01-021", "S01-023"], result.Refusals.Select(r => r.Id));
        Assert.Equal("If 'billing_gate' then is missing 'ret'; every member is written, and null means none.", result.Refusals[0].Sentence);
        Assert.All(result.Refusals, V31Contract.AssertSentence);

        var text = Only(Edit(Gate, json => json["then"] = "billing_text").Parse());
        Assert.Equal("If 'billing_gate' declares 'then' as text; it is an object.", text.Sentence);
    }

    [Theory]
    [InlineData(1, true, "S04-019")]
    [InlineData(16, true, "S01-005")]
    [InlineData(0, true, "S04-021")]
    [InlineData(-2, true, "S04-021")]
    [InlineData(4, false, "S04-022")]
    [InlineData(1, false, "S04-022")]
    [InlineData(16, false, "S04-022")]
    [InlineData(0, false, "S04-021")]
    public void MaxParallel_IsHeldToItsRange(int value, bool parallel, string expected)
    {
        var refusal = Only(Edit(Standards, json => { json["maxParallel"] = value; json["parallel"] = parallel; }).Parse());

        Assert.Equal(expected, refusal.Id);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(15, true)]
    [InlineData(null, true)]
    [InlineData(null, false)]
    public void MaxParallel_InItsRange_Passes(int? value, bool parallel)
    {
        var result = Edit(Standards, json => { json["maxParallel"] = value; json["parallel"] = parallel; }).Parse();

        Assert.Empty(result.Refusals);
        Assert.Equal(value, ((SourceFor)result.Package!.Nodes.Nodes[6]).MaxParallel);
    }

    [Fact]
    public void MaxParallel_Sentences_WriteTheNumberAsWritten()
    {
        Assert.Equal(
            "Loop 'standards' declares maxParallel 16; the most is FormatBounds.parallel (15).",
            Only(Edit(Standards, json => json["maxParallel"] = 16).Parse()).Sentence);
        Assert.Equal(
            "Loop 'standards' declares parallel true with maxParallel 1, which runs one item at a time; declare \"parallel\": false and \"maxParallel\": null instead.",
            Only(Edit(Standards, json => json["maxParallel"] = 1).Parse()).Sentence);
        Assert.Equal(
            "Loop 'standards' declares maxParallel 4 with parallel false; a sequential loop runs one item at a time: write \"maxParallel\": null.",
            Only(Edit(Standards, json => json["parallel"] = false).Parse()).Sentence);
        Assert.Equal(
            "Loop 'standards' declares maxParallel -2; the least is 2, or null for no cap of its own.",
            Only(Edit(Standards, json => json["maxParallel"] = -2).Parse()).Sentence);
    }

    [Fact]
    public void MaxParallelOnePointFive_IsRefused()
    {
        var refusal = Only(Edit(Sections, json => json["maxParallel"] = 1.5).Parse());

        Assert.Equal("S04-021", refusal.Id);
        Assert.Equal("Loop 'sections' declares maxParallel 1.5; the least is 2, or null for no cap of its own.", refusal.Sentence);
    }

    [Fact]
    public void MaxParallelThatIsNotANumber_IsAWrongType()
    {
        var refusal = Only(Edit(Sections, json => json["maxParallel"] = "4").Parse());

        Assert.Equal("Loop 'sections' declares 'maxParallel' as text; it is a whole number.", refusal.Sentence);
    }

    [Fact]
    public void UntilWithParallel_IsRefused()
    {
        var until = new JsonObject { ["var"] = "node.outline.value.done" };
        var refusal = Only(Edit(Sections, json => json["until"] = until.DeepClone()).Parse());

        Assert.Equal("S04-023", refusal.Id);
        Assert.Equal(
            "Loop 'sections' declares until with parallel true; until stops after the item where it holds, which needs items in order: declare \"parallel\": false and \"maxParallel\": null.",
            refusal.Sentence);

        var sequential = Edit(Sections, json => { json["until"] = until.DeepClone(); json["parallel"] = false; }).Parse();
        Assert.Empty(sequential.Refusals);
        Assert.NotNull(((SourceFor)sequential.Package!.Nodes.Nodes[2]).Until);
    }

    [Fact]
    public void ALoopMax_IsHeldAsWritten_UpToWhatALongHolds()
    {
        foreach (var (written, expected) in new (string, long)[] { ("3000000000", 3000000000L), ("9223372036854775807", long.MaxValue), ("2147483648", 2147483648L), ("1e3", 1000L) })
        {
            var result = Edit(Sections, json => json["max"] = JsonNode.Parse(written)).Parse();

            Assert.Empty(result.Refusals);
            Assert.Equal(expected, ((SourceFor)result.Package!.Nodes.Nodes[2]).Max);
        }

        foreach (var past in new[] { "9223372036854775808", "1e30", "100000000000000000000000000000000" })
        {
            var refusal = Only(Edit(Sections, json => json["max"] = JsonNode.Parse(past)).Parse());

            Assert.Equal("A02-001", refusal.Id);
        }
    }

    [Fact]
    public void ALoopMaxOfZero_IsRefused()
    {
        var refusal = Only(Edit(Sections, json => json["max"] = 0).Parse());

        Assert.Equal("S04-024", refusal.Id);
        Assert.Equal("Loop 'sections' declares max 0; the least is 1.", refusal.Sentence);
        Assert.Equal(refusal.Sentence, Only(Edit(Sections, json => json["max"] = -3).Parse()).Sentence);
        Assert.Equal(
            "Loop 'sections' declares 'max' as a number; it is a whole number.",
            Only(Edit(Sections, json => json["max"] = 1.5).Parse()).Sentence);

        var many = Edit(Sections, json => json["max"] = 1000).Parse();
        Assert.Empty(many.Refusals);
        Assert.Equal(1000, ((SourceFor)many.Package!.Nodes.Nodes[2]).Max);
    }

    [Theory]
    [InlineData(null, "S04-031")]
    [InlineData(16, "S01-003")]
    [InlineData(0, "S04-033")]
    [InlineData(-1, "S04-033")]
    public void AWhileMax_IsHeldToItsRange(int? value, string expected)
    {
        var refusal = Only(Edit(Refine, json => json["max"] = value).Parse());

        Assert.Equal(expected, refusal.Id);
    }

    [Fact]
    public void AWhileMax_Sentences()
    {
        Assert.Equal(
            "While 'refine' declares max 16; a while runs at most FormatBounds.passes (15) passes.",
            Only(Edit(Refine, json => json["max"] = 16).Parse()).Sentence);
        Assert.Equal(
            "While 'refine' declares max null; a while must declare its bound (1 to 15).",
            Only(Edit(Refine, json => json["max"] = null).Parse()).Sentence);
        Assert.Equal("While 'refine' declares max 0; the least is 1.", Only(Edit(Refine, json => json["max"] = 0).Parse()).Sentence);
        Assert.Equal(
            "While 'refine' declares 'max' as a number; it is a whole number.",
            Only(Edit(Refine, json => json["max"] = 2.5).Parse()).Sentence);
        Assert.Equal("S01-021", Only(Edit(Refine, json => json.Remove("max")).Parse()).Id);

        Assert.Equal(1, ((SourceWhile)Edit(Refine, json => json["max"] = 1).Parse().Package!.Nodes.Nodes[4]).Max);
        Assert.Equal(15, ((SourceWhile)Edit(Refine, json => json["max"] = 15).Parse().Package!.Nodes.Nodes[4]).Max);
    }

    [Fact]
    public void ABoundWrittenAsAWholeDecimalOrExponent_IsItsWholeNumber()
    {
        // 1.0 and 2e0 are whole numbers, read as 1 and 2, for maxParallel, a for's max and a while's max.
        var two = Edit(Standards, json => json["maxParallel"] = JsonNode.Parse("2e0")).Parse();
        Assert.Empty(two.Refusals);
        Assert.Equal(2, ((SourceFor)two.Package!.Nodes.Nodes[6]).MaxParallel);
        Assert.Equal("S04-019", Only(Edit(Standards, json => json["maxParallel"] = JsonNode.Parse("1.0")).Parse()).Id);
        Assert.Equal(
            "Loop 'standards' declares maxParallel 2e0 with parallel false; a sequential loop runs one item at a time: write \"maxParallel\": null.",
            Only(Edit(Standards, json => { json["maxParallel"] = JsonNode.Parse("2e0"); json["parallel"] = false; }).Parse()).Sentence);

        foreach (var (written, expected) in new[] { ("1.0", 1L), ("2e0", 2L) })
        {
            var loop = Edit(Sections, json => json["max"] = JsonNode.Parse(written)).Parse();
            Assert.Empty(loop.Refusals);
            Assert.Equal(expected, ((SourceFor)loop.Package!.Nodes.Nodes[2]).Max);

            var pass = Edit(Refine, json => json["max"] = JsonNode.Parse(written)).Parse();
            Assert.Empty(pass.Refusals);
            Assert.Equal((int)expected, ((SourceWhile)pass.Package!.Nodes.Nodes[4]).Max);
        }
    }

    [Fact]
    public void OneE30_IsNotAWholeNumber_ToTheFlowReaderEither()
    {
        // As SourceMembers judges it (A02-001): 1e30 is past what the reader holds exactly, so it is not a whole number.
        Assert.Equal(
            "While 'refine' declares 'max' as a number; it is a whole number.",
            Only(Edit(Refine, json => json["max"] = JsonNode.Parse("1e30")).Parse()).Sentence);
        Assert.Equal(
            "Loop 'sections' declares 'max' as a number; it is a whole number.",
            Only(Edit(Sections, json => json["max"] = JsonNode.Parse("1e30")).Parse()).Sentence);
        // maxParallel's own sentence covers a number that is not whole (S04-021).
        Assert.Equal(
            "Loop 'standards' declares maxParallel 1e30; the least is 2, or null for no cap of its own.",
            Only(Edit(Standards, json => json["maxParallel"] = JsonNode.Parse("1e30")).Parse()).Sentence);

        var members = new List<SourceRefusal>();
        SourceMembers.CheckType(JsonNode.Parse("1e30"), MemberType.Integer, "While 'refine'", "max", Refine, members);
        Assert.Equal("While 'refine' declares 'max' as a number; it is a whole number.", Assert.Single(members).Sentence);
    }

    [Fact]
    public void AnIfWithoutThen_IsRefused()
    {
        var refusal = Only(Edit(Gate, json => json["then"] = null).Parse());

        Assert.Equal("S04-041", refusal.Id);
        Assert.Equal("If 'billing_gate' declares then null; an if runs a then body.", refusal.Sentence);
    }

    [Fact]
    public void ACaseWithoutBranches_IsRefused()
    {
        var files = Edit(Route, json => json["branches"] = new JsonArray())
            .Without("nodes/sections_draft/route/narrative_draft.md").Without("nodes/sections_draft/route/table_draft.md");

        var refusal = Only(files.Parse());

        Assert.Equal("S04-042", refusal.Id);
        Assert.Equal("Case 'route' has no branches; a case runs one branch per set of values.", refusal.Sentence);
    }

    [Fact]
    public void BranchesThatAreNotAListOfObjects_AreAWrongType()
    {
        Assert.Equal(
            "Case 'route' declares 'branches' as an object; it is a list.",
            Only(Edit(Route, json => json["branches"] = new JsonObject()).Parse()).Sentence);
        Assert.Equal("S01-022", Only(Edit(Route, json => json["branches"] = null).Parse()).Id);
        Assert.Equal(
            "Case 'route' declares 'branches.3' as text; it is an object.",
            Only(Edit(Route, json => ((JsonArray)json["branches"]!)[2] = "omit").Parse()).Sentence);
    }

    [Fact]
    public void ABranchWithoutValues_IsRefused()
    {
        var refusal = Only(Edit(Route, json => Branch(json, 2)["when"] = new JsonArray()).Parse());

        Assert.Equal("S04-043", refusal.Id);
        Assert.Equal("Case 'route' branch 2 lists no value; a branch's when names one or more values.", refusal.Sentence);
    }

    [Fact]
    public void ABranchListingNull_IsRefused()
    {
        var refusal = Only(Edit(Route, json => ((JsonArray)Branch(json, 2)["when"]!).Add(null)).Parse());

        Assert.Equal("S04-076", refusal.Id);
        Assert.Equal("Case 'route' branch 2 lists null; a null subject goes to the else.", refusal.Sentence);

        var number = Only(Edit(Route, json => ((JsonArray)Branch(json, 2)["when"]!).Add(3)).Parse());
        Assert.Equal("Case 'route' branch 2 declares 'when' as a list; it is a list of text.", number.Sentence);
    }

    [Fact]
    public void AnEmptyBranch_WritesBothTogether()
    {
        var withRet = Only(Edit(Route, json => Branch(json, 3)["ret"] = "node.sections_draft.route.table_draft").Parse());
        Assert.Equal("S04-048", withRet.Id);
        Assert.Equal(
            "Case 'route' branch 3 writes nodes [] with ret node.sections_draft.route.table_draft; an empty branch writes nodes [] and ret null together.",
            withRet.Sentence);

        var withNodes = Only(Edit(Route, json => Branch(json, 1)["ret"] = null).Parse());
        Assert.Equal(
            "Case 'route' branch 1 writes nodes narrative_draft with ret null; an empty branch writes nodes [] and ret null together.",
            withNodes.Sentence);

        var both = Only(Edit(Route, json => { Branch(json, 1)["nodes"] = Ids("narrative_draft", "table_draft"); Branch(json, 1)["ret"] = null; Branch(json, 2)["nodes"] = Ids(); Branch(json, 2)["ret"] = null; }).Parse());
        Assert.Equal(
            "Case 'route' branch 1 writes nodes narrative_draft, table_draft with ret null; an empty branch writes nodes [] and ret null together.",
            both.Sentence);
    }

    [Fact]
    public void AChildOfABranchNotListedAnywhere_IsAStray()
    {
        var refusal = Only(SourceFixture.Full().With("nodes/sections_draft/route/omit_draft.md", "x").Parse());

        Assert.Equal("S01-006", refusal.Id);
        Assert.Equal(
            "File 'nodes/sections_draft/route/omit_draft.md' is not listed: every file in nodes/sections_draft/route is named once in its index.json, or is index.json or README.md.",
            refusal.Sentence);
    }

    [Fact]
    public void AChildListedByAnyBody_IsNotAStray()
    {
        var files = SourceFixture.Full()
            .With("nodes/billing_gate/no_billing.md", SourceFixture.Text(SourceFixture.Full(), "nodes/billing_gate/billing_text.md"));
        files.WithJson(Gate, json => json["else"] = new JsonObject { ["nodes"] = Ids("no_billing"), ["ret"] = "node.billing_gate.no_billing" });

        Assert.Empty(files.Parse().Refusals);
    }

    [Fact]
    public void FaultsInsideAFlowNode_DoNotStopItsChildren()
    {
        var files = Edit(Sections, json => json["label"] = null);
        files.WithJson("nodes/sections/paragraphs/draft_paragraph.md", h => h.Remove("label"));

        var result = files.Parse();

        Assert.Equal(["S04-009", "S01-021"], result.Refusals.Select(r => r.Id));
        Assert.Null(result.Package);
    }

    [Fact]
    public void WrongTypesInFlowIndexes_AreRefused_AndNeverThrow()
    {
        string[] indexes = [Sections, Refine, Gate, Route, "checks/section_coverage/index.json"];
        JsonNode?[] values = [null, 1, 1.5, "x", true, new JsonObject(), new JsonArray(1, null, new JsonObject())];
        foreach (var index in indexes)
        {
            var kind = FlowKind(index);
            var shape = Shape(kind);
            var members = shape.Members.Select(m => m.Name).Concat(["parallel", "while", "then", "branches", "else", "nodes"]).Distinct();
            foreach (var member in members)
            {
                foreach (var value in values)
                {
                    var result = Edit(index, json => json[member] = value?.DeepClone()).Parse();
                    Assert.Equal(result.Refusals.Count == 0, result.Package != null);
                    if (!MayHold(kind, shape, member, value))
                        Assert.True(result.Refusals.Count > 0, $"{index} {member} = {value?.ToJsonString() ?? "null"} was accepted.");
                }
            }
        }

        // The members of the bodies and branches.
        (string Index, Func<JsonObject, JsonObject> Body)[] bodies =
        [
            (Gate, json => json["then"]!.AsObject()),
            (Route, json => Branch(json, 1)),
            (Route, json => Branch(json, 3)),
        ];
        foreach (var (index, body) in bodies)
        {
            foreach (var member in new[] { "when", "nodes", "ret", "extra" })
            {
                foreach (var value in values)
                {
                    var result = Edit(index, json => body(json)[member] = value?.DeepClone()).Parse();
                    Assert.Equal(result.Refusals.Count == 0, result.Package != null);
                }
            }
        }
    }

    private static string FlowKind(string index) => index switch
    {
        Refine => "while",
        Gate => "if",
        Route => "case",
        _ => "for",
    };

    private static DeclarationShape Shape(string kind) => kind switch
    {
        "while" => SourceShapes.FlowWhile,
        "if" => SourceShapes.FlowIf,
        "case" => SourceShapes.FlowCase,
        _ => SourceShapes.FlowFor,
    };

    /// <summary>Whether the fixture's flow node stays valid with <paramref name="member"/> set to <paramref name="value"/> (the for's are parallel).</summary>
    private static bool MayHold(string kind, DeclarationShape shape, string member, JsonNode? value)
    {
        var declared = shape.Members.FirstOrDefault(m => m.Name == member);
        if (declared == null)
            return false;
        if (value is null)
            return declared.Nullable && member != "then";
        var kindOf = value.GetValueKind();
        return member switch
        {
            "kind" or "producer" or "nodes" or "description" or "branches" or "maxParallel" => false,
            "until" => kind == "while",
            "max" => kindOf == System.Text.Json.JsonValueKind.Number && !value.ToJsonString().Contains('.'),
            "while" or "if" => true,
            "parallel" => kindOf is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
            "else" or "then" => false,
            _ => declared.Type switch
            {
                MemberType.Text or MemberType.Reference => kindOf == System.Text.Json.JsonValueKind.String,
                MemberType.Boolean => kindOf is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
                MemberType.Format => value is JsonObject || kindOf == System.Text.Json.JsonValueKind.String,
                _ => false,
            },
        };
    }

    [Fact]
    public void EveryFlowSentence_IsTheDesigns()
    {
        var sentences = new[]
        {
            SourceSentences.FlowLabelNull("Loop", "sections", "p"),
            SourceSentences.FlowLabelNull("While", "refine", "p"),
            SourceSentences.MemberOfAnotherKind("While", "refine", "parallel", "a for", "p"),
            SourceSentences.ReturnsNothing("Case 'route' else", "p"),
            SourceSentences.BodyListsTwice("If 'applies' then", "draft", "p"),
            SourceSentences.LoopHasNoBody("sections", "p"),
            SourceSentences.ParallelOfOne("sections", "p"),
            SourceSentences.MaxParallelTooHigh("sections", "16", "p"),
            SourceSentences.MaxParallelTooLow("sections", "1.5", "p"),
            SourceSentences.MaxParallelWhenSequential("sections", "4", "p"),
            SourceSentences.UntilWhenParallel("sections", "p"),
            SourceSentences.LoopMaxTooLow("sections", "p"),
            SourceSentences.WhileMaxNull("refine", "p"),
            SourceSentences.WhileMaxTooHigh("refine", "16", "p"),
            SourceSentences.WhileMaxTooLow("refine", "0", "p"),
            SourceSentences.WhileHasNoBody("refine", "p"),
            SourceSentences.ThenNull("applies", "p"),
            SourceSentences.NoBranches("route", "p"),
            SourceSentences.BranchListsNoValue("route", 2, "p"),
            SourceSentences.EmptyBranchHalfWritten("route", 3, "[]", "node.x", "p"),
            SourceSentences.BranchListsNull("route", 2, "p"),
            SourceSentences.NoBody("If 'applies' then", "p"),
        };

        Assert.Equal(
            [
                "S04-009", "S04-009", "S04-010", "S04-011", "S04-013", "S04-018", "S04-019", "S01-005", "S04-021", "S04-022",
                "S04-023", "S04-024", "S04-031", "S01-003", "S04-033", "S04-034", "S04-041", "S04-042", "S04-043", "S04-048",
                "S04-076", "A02-008",
            ],
            sentences.Select(s => s.Id));
        Assert.All(sentences, V31Contract.AssertSentence);
    }
}
