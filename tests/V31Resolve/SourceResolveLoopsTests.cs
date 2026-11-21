using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): a loop's source and its <c>itemLabel</c>, typed (part 04 §§ 2.2, 2.7), and the
/// while's body nodes that nothing reads (§ 3; S04-035, judged only when no body node's reads are unknown, D1). Each
/// refusal is one change to a <see cref="ResolveFixture"/> package, and is the only one said.
/// </summary>
public class SourceResolveLoopsTests
{
    private const string Sections = "nodes/sections/index.json";

    private const string Coverage = "checks/section_coverage/index.json";

    private const string Element = "{ title: plaintext, kind: string {narrative, table, omit} }";

    /// <summary>The one refusal <see cref="SourceCheck.Run"/> says of <paramref name="files"/>, held to its row.</summary>
    private static SourceRefusal Refused(Dictionary<string, byte[]> files)
    {
        var result = ResolveFixture.Run(files);
        Assert.True(result.Refusals.Count == 1, ResolveFixture.Said(result));
        var refusal = result.Refusals[0];
        V31Contract.AssertSentence(refusal);
        return refusal;
    }

    private static void AssertRefusal(Dictionary<string, byte[]> files, string id, string sentence, string path)
    {
        var refusal = Refused(files);
        Assert.Equal((id, sentence, path), (refusal.Id, refusal.Sentence, refusal.Path));
    }

    /// <summary><paramref name="files"/> with the member <paramref name="member"/> of the flow node at <paramref name="path"/> set to <paramref name="json"/>.</summary>
    private static Dictionary<string, byte[]> Set(Dictionary<string, byte[]> files, string path, string member, string json) =>
        files.WithJson(path, h => h[member] = JsonNode.Parse(json));

    /// <summary><see cref="ResolveFixture.NestedNote"/> with the format <c>section_plan</c>'s element edited by <paramref name="edit"/>.</summary>
    private static Dictionary<string, byte[]> WithSectionElement(Action<JsonObject> edit) =>
        ResolveFixture.NestedNote().WithJson("formats/section_plan.json",
            f => edit(f["properties"]!["sections"]!["items"]!.AsObject()));

    // ---- in ------------------------------------------------------------------------------------

    [Theory]
    [InlineData("{ \"var\": \"node.plan.value.sections\" }")]
    [InlineData("[\"node.plan.value.sections\"]")]
    public void AnInThatIsAnExpression_IsS04017(string json) =>
        AssertRefusal(Set(ResolveFixture.NestedNote(), Sections, "in", json), "S04-017",
            "Loop 'sections' in is an expression; it names one readable thing: compute the value in a computed node and name that.",
            Sections);

    [Fact]
    public void AnInThatIsANumber_IsTheReadersA02001() =>
        AssertRefusal(Set(ResolveFixture.NestedNote(), Sections, "in", "5"), "A02-001",
            "Loop 'sections' declares 'in' as a whole number; it is text.", Sections);

    [Theory]
    [InlineData("node..plan")]
    [InlineData("node.plan.value.sections.0")]
    [InlineData(" node.plan.value.sections")]
    public void AnInThatIsNotAReference_IsA05001(string text) =>
        AssertRefusal(Set(ResolveFixture.NestedNote(), Sections, "in", JsonValue.Create(text)!.ToJsonString()), "A05-001",
            $"Loop 'sections' in '{text}' is not a reference; a reference is a root, then names joined by dots.", Sections);

    [Fact]
    public void AnEmptyIn_IsA05001()
    {
        // the row's <text> is empty here, which AssertSentence's pattern (one character or more) cannot hold
        var result = ResolveFixture.Run(Set(ResolveFixture.NestedNote(), Sections, "in", "\"\""));

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal(("A05-001", "Loop 'sections' in '' is not a reference; a reference is a root, then names joined by dots.", Sections),
            (refusal.Id, refusal.Sentence, refusal.Path));
    }

    [Theory]
    [InlineData("nested", Sections, "node.nothing.value.sections")]
    [InlineData("nested", Sections, "node.plan.value.headings")]
    [InlineData("nested", Sections, "node.plan")]
    [InlineData("nested", Sections, "profile.specialty")]
    [InlineData("functions", "nodes/standards/index.json", "snippet.nothing")]
    [InlineData("functions", "nodes/standards/index.json", "snippet.standards.nothing")]
    public void AnInThatNamesNothing_IsS03014(string fixture, string path, string reference)
    {
        var files = fixture == "nested" ? ResolveFixture.NestedNote() : ResolveFixture.Functions();
        var loop = path.Split('/')[1];
        AssertRefusal(Set(files, path, "in", JsonValue.Create(reference)!.ToJsonString()), "S03-014",
            $"Loop '{loop}' reads {reference}, which doesn't exist here.", path);
    }

    [Fact]
    public void ABareInput_IsS02018() =>
        AssertRefusal(Set(ResolveFixture.Conditional(), "nodes/sections_draft/index.json", "in", "\"input.sections\""), "S02-018",
            "'input.sections' is read bare; read input.sections.value, or input.sections.content for text.",
            "nodes/sections_draft/index.json");

    [Theory]
    [InlineData("nested", Sections, "node.plan.value", "section_plan")]
    [InlineData("nested", Sections, "node.section_count.value", "number")]
    [InlineData("nested", Sections, "run.date", "date")]
    [InlineData("functions", "nodes/standards/index.json", "snippet.standards.hpi", "standard_section")]
    public void AnInThatIsNotAList_IsS04025(string fixture, string path, string reference, string format)
    {
        var files = fixture == "nested" ? ResolveFixture.NestedNote() : ResolveFixture.Functions();
        var loop = path.Split('/')[1];
        AssertRefusal(Set(files, path, "in", JsonValue.Create(reference)!.ToJsonString()), "S04-025",
            $"Loop '{loop}' iterates {reference}, which is {format}; only a list can be iterated.", path);
    }

    [Fact]
    public void ASingleSnippet_IsS04025()
    {
        var files = ResolveFixture.Functions()
            .With("snippets/index.json", Index(["standards", "signature"]))
            .With("snippets/signature.md", Md(
                "{ \"label\": \"Signature\", \"description\": null, \"if\": null, \"format\": \"plaintext\" }",
                "Dr A. Sample\n"));

        AssertRefusal(Set(files, "nodes/standards/index.json", "in", "\"snippet.signature\""), "S04-025",
            "Loop 'standards' iterates snippet.signature, which is plaintext; only a list can be iterated.", "nodes/standards/index.json");
    }

    [Fact]
    public void AnItemValueWithNoLoop_IsS04026() =>
        AssertRefusal(Set(ResolveFixture.NestedNote(), Sections, "in", "\"item.value.sections\""), "S04-026",
            "Loop 'sections' iterates item.value.sections, but it runs inside no loop; item.value names the enclosing loop's item.",
            Sections);

    [Fact]
    public void ANodeInsideAnotherLoop_IsS04027()
    {
        const string standing = "nodes/standing_sections/index.json";
        AssertRefusal(Set(ResolveFixture.Conditional(), standing, "in", "\"node.patient_summary.value.points\""), "S04-027",
            "Loop 'standing_sections' iterates node.patient_summary.value.points; patient_summary runs inside loop 'patient_summaries' "
            + "— a loop iterates a node outside it, or in an enclosing loop's body.",
            standing);
    }

    // ---- itemLabel -----------------------------------------------------------------------------

    [Theory]
    [InlineData(Sections, "heading", "node.plan.value.sections", "title, kind")]
    [InlineData("nodes/sections/paragraphs/index.json", "title", "node.outline.value.points", "none")]
    public void AnItemLabelThatIsNoField_IsS04028(string path, string field, string reference, string fields)
    {
        var loop = path.Split('/')[^2];
        AssertRefusal(Set(ResolveFixture.NestedNote(), path, "itemLabel", JsonValue.Create(field)!.ToJsonString()), "S04-028",
            $"Loop '{loop}' itemLabel '{field}' is not a field of the elements of {reference} (fields: {fields}).", path);
    }

    [Fact]
    public void AnItemLabelThatIsNullable_IsS04029() =>
        // a package's formats list every property in required (S02-026), so an optional one is a nullable one
        AssertRefusal(WithSectionElement(e => e["properties"]!["kind"] = JsonNode.Parse(
                "{ \"type\": [\"string\", \"null\"], \"enum\": [\"narrative\", \"table\", \"omit\", null] }"))
            .WithJson(Sections, h => h["itemLabel"] = "kind"),
            "S04-029", "Loop 'sections' itemLabel 'kind' may be missing or null; itemLabel names a field every element has as a string.",
            Sections);

    [Fact]
    public void AnItemLabelThatMayBeNull_IsS04029() =>
        AssertRefusal(Set(ResolveFixture.Functions(), Coverage, "itemLabel", "\"reason\""), "S04-029",
            "Loop 'section_coverage' itemLabel 'reason' may be missing or null; itemLabel names a field every element has as a string.",
            Coverage);

    [Fact]
    public void AnItemLabelThatIsNotAString_IsS04030()
    {
        var files = WithSectionElement(e =>
        {
            e["required"] = new JsonArray("title", "kind", "position");
            e["properties"]!["position"] = new JsonObject { ["type"] = "integer" };
        }).WithJson(Sections, h => h["itemLabel"] = "position");

        AssertRefusal(files, "S04-030", "Loop 'sections' itemLabel 'position' is integer; itemLabel names a string field.", Sections);
    }

    [Fact]
    public void ItemLabel_OverItems_IsLabel()
    {
        var result = ResolveFixture.Run(ResolveFixture.Functions());
        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));

        var package = result.Parse.Package!;
        var source = result.Resolution!.Tree.Source((SourceFor)ResolveFixture.Node(package, "check.section_coverage"));
        Assert.True(source.OverItems);
        Assert.Same(ResolveFixture.Node(package, "node.standards"), source.Production);
        Assert.Equal("node.standards.items", source.Reference);
        Assert.Equal((ScopeHeadKind.Value, "node.standards", 3), (source.Resolved.Kind, source.Resolved.Key, source.Resolved.Length));
    }

    // ---- What each source gives ----------------------------------------------------------------

    /// <summary>
    /// <see cref="ResolveFixture.Functions"/> with the parameter <c>terms</c> (a <c>concept_list</c>, passed the draft's terms)
    /// and, in the function's body, the loop <c>each_term</c> over <c>param.terms.value.concepts</c>.
    /// </summary>
    private static Dictionary<string, byte[]> WithParameterLoop() =>
        ResolveFixture.Functions()
            .WithJson("nodes/standards/section.md", h => h["with"]!["terms"] = "node.extract_input_terms.value")
            .WithJson("functions/draft_section/index.json", f =>
            {
                f["inputs"]!["terms"] = JsonNode.Parse("{ \"label\": \"Terms\", \"description\": null, \"format\": \"concept_list\" }");
                f["uses"]!["formats"]!.AsArray().Add("concept_list");
                f["nodes"]!.AsArray().Add("each_term");
            })
            .With("functions/draft_section/each_term/index.json", Json(Node("for", "Each term", ArrayOf("\"plaintext\""), false, true,
                "\"in\": \"param.terms.value.concepts\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"term_note\"], \"ret\": \"node.each_term.term_note\"")))
            .With("functions/draft_section/each_term/term_note.md", Agent("Noting a term", "\"plaintext\"",
                "Say in one line where the section mentions this term.\n"));

    /// <summary><see cref="ResolveFixture.NestedNote"/> whose sections each carry <c>paragraphs</c>, iterated by the inner loop as <c>item.value.paragraphs</c>.</summary>
    private static Dictionary<string, byte[]> WithItemParagraphs() =>
        WithSectionElement(e =>
            {
                e["required"] = new JsonArray("title", "kind", "paragraphs");
                e["properties"]!["paragraphs"] = JsonNode.Parse("{ \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }");
            })
            .WithJson("nodes/sections/paragraphs/index.json", h => h["in"] = "item.value.paragraphs");

    public static TheoryData<string, string, string, string, int?, bool> SourceCases => new()
    {
        { "conditional", "node.sections_draft", "input.sections.value", "section_request", null, false },
        { "functions", "node.standards", "snippet.standards", "standard_section", 2, false },
        { "nested", "node.sections", "node.plan.value.sections", Element, null, false },
        { "paragraphs", "node.sections.paragraphs", "item.value.paragraphs", "plaintext", null, false },
        { "nested", "node.sections.paragraphs", "node.outline.value.points", "plaintext", null, false },
        { "functions", "check.section_coverage", "node.standards.items", "{ item: { id: string, value: standard_section }, ", null, true },
        { "parameter", "draft_section/node.each_term", "param.terms.value.concepts", "{ term: string, type: string, ", null, false },
    };

    [Theory]
    [MemberData(nameof(SourceCases))]
    public void ValidSources(string fixture, string at, string reference, string element, int? length, bool overItems)
    {
        var files = fixture switch
        {
            "conditional" => ResolveFixture.Conditional(),
            "functions" => ResolveFixture.Functions(),
            "nested" => ResolveFixture.NestedNote(),
            "paragraphs" => WithItemParagraphs(),
            _ => WithParameterLoop(),
        };
        var result = ResolveFixture.Run(files);
        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));

        var package = result.Parse.Package!;
        var slash = at.IndexOf('/');
        var loop = (SourceFor)(slash < 0 ? ResolveFixture.Node(package, at) : ResolveFixture.Node(package, at[(slash + 1)..], at[..slash]));
        var formats = slash < 0 ? result.Types!.Formats : result.Types!.FunctionFormats[at[..slash]];
        var source = result.Resolution!.Tree.Source(loop);

        Assert.Equal(reference, source.Reference);
        Assert.Null(source.Resolved.Refusal);
        Assert.StartsWith(element, TypeStrings.Print(source.Element, formats), StringComparison.Ordinal);
        Assert.Equal(length, source.KnownLength);
        Assert.Equal(overItems, source.OverItems);
        Assert.Equal(overItems, source.Production != null);
    }

    [Fact]
    public void AnInnerLoopsItem_IsTheOuterItemsField()
    {
        // the inner loop over item.value.paragraphs gives its body item.value, a plaintext
        var (package, types, tree) = ResolveFixture.Tree(WithItemParagraphs());
        var frame = tree.FrameOf(ResolveFixture.Node(package, "node.sections.paragraphs.draft_paragraph"));

        var item = tree.Resolve(ReferenceString.Parse("item.value")!, frame, "Node 'draft_paragraph'", "nodes/x.md", true);

        Assert.Equal((ScopeHeadKind.Value, "plaintext"), (item.Kind, TypeStrings.Print(item.Format, types.Formats)));
    }

    [Fact]
    public void AnyIn_IsTypedOrRefusedOnce_NeverThrows()
    {
        // every in, however written, gives at most one sentence (none when it is a list), never an exception
        string[] written =
        [
            "node", "snippet", "item", "pass", "param", "run", "input", "check", "item.parent.parent.parent.value.x",
            "snippet.standards.hpi.x", "pass.history", "pass.number", "node.sections.items", "node.refine.history",
            "check.nothing.items", "param.x.value", "node.plan.value.sections.title", "node.plan.title", "node.plan.content",
            "input.consult_draft", "input.nothing.value", "run.nothing", "profile.x.y", "é.ü", "node.sections",
            string.Join('.', Enumerable.Repeat("node", 300)),
        ];
        foreach (var text in written)
        {
            var result = ResolveFixture.Run(Set(ResolveFixture.NestedNote(), Sections, "in", JsonValue.Create(text)!.ToJsonString()));
            Assert.True(result.Refusals.Count <= 1, text + ": " + ResolveFixture.Said(result));
        }
    }

    [Fact]
    public void Source_TakesNoNull()
    {
        var (_, _, tree) = ResolveFixture.Tree(ResolveFixture.NestedNote());
        Assert.Throws<ArgumentNullException>(() => tree.Source(null!));
    }

    // ---- S04-035 -------------------------------------------------------------------------------

    /// <summary>
    /// Minimal() with the while <c>tally</c> of computed nodes: <c>count</c> (its ret, read by the check) and
    /// <c>extra</c>; <paramref name="check"/> is the while's rule.
    /// </summary>
    private static Dictionary<string, byte[]> WithTally(string check) =>
        Minimal()
            .With("nodes/index.json", Index(["plan", "tally"]))
            .With("nodes/tally/index.json", Json(Node("while", "Counting the passes", "\"number\"", true, false,
                $"\"while\": {check}, \"max\": 3, \"until\": null, \"checkFirst\": false, "
                + "\"nodes\": [\"count\", \"extra\"], \"ret\": \"node.tally.count\"")))
            .With("nodes/tally/count.md", Md(Node("computed", "Counting", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"+\": [{ \"var\": \"pass.number\" }, 0] }"), ""))
            .With("nodes/tally/extra.md", Md(Node("computed", "Naming the pass", "\"plaintext\"", true, false,
                "\"if\": null, \"expression\": { \"cat\": [\"Pass \", { \"var\": \"pass.number\" }] }"), ""));

    private const string CountBelowThree = "{ \"<\": [{ \"var\": \"node.count.value\" }, 3] }";

    [Fact]
    public void AWhileOfComputedNodes_WithAnUnreadNode_IsS04035()
    {
        AssertRefusal(WithTally(CountBelowThree), "S04-035",
            "Node 'extra' in while 'tally' is neither returned nor read; name it with ret, read it from a body node or from the check.",
            "nodes/tally/extra.md");

        // read by the check, it is used
        var read = WithTally("{ \"and\": [" + CountBelowThree + ", { \"!\": [{ \"blank\": [{ \"var\": \"node.extra.value\" }] }] }] }");
        Assert.True(ResolveFixture.Run(read).Refusals.Count == 0, ResolveFixture.Said(ResolveFixture.Run(read)));

        // read by another body node's expression, it is used
        var used = WithTally(CountBelowThree).WithJson("nodes/tally/count.md",
            h => h["expression"] = JsonNode.Parse("{ \"+\": [{ \"size\": [{ \"var\": \"node.extra.value\" }] }, 0] }"));
        Assert.True(ResolveFixture.Run(used).Refusals.Count == 0, ResolveFixture.Said(ResolveFixture.Run(used)));
    }

    // ---- A06-002 (#1507, item 4) -----------------------------------------------------------------

    private const string Refine = "nodes/refine/index.json";

    /// <summary><see cref="ResolveFixture.NestedNote"/> with <c>refine</c> checked before each pass, its check <paramref name="rule"/>.</summary>
    private static Dictionary<string, byte[]> CheckedFirst(string rule) =>
        ResolveFixture.NestedNote().WithJson(Refine, h =>
        {
            h["checkFirst"] = true;
            h["while"] = JsonNode.Parse(rule);
        });

    [Theory]
    [InlineData("node.judge.value.verdict")]
    [InlineData("pass.node.judge.value.verdict")]
    public void ACheckFirstWhile_ReadingItsOwnBody_IsA06002(string path) =>
        AssertRefusal(CheckedFirst($$"""{ "!==": [{ "var": "{{path}}" }, "good"] }"""), "A06-002",
            "While 'refine' declares checkFirst true, but its check reads node 'judge' of its own body, which has no value "
            + "before the first pass: declare \"checkFirst\": false, or read something that runs before the while.",
            Refine);

    [Theory]
    [InlineData("""{ "!==": [{ "var": "pass.previous.node.judge.value.verdict" }, "good"] }""")]
    [InlineData("""{ "<": [{ "var": "pass.number" }, 3] }""")]
    [InlineData("""{ ">": [{ "size": [{ "var": "node.plan.value.sections" }] }, 0] }""")]
    public void ACheckFirstWhile_ReadingAnEarlierPassOrOutside_IsAccepted(string rule)
    {
        var result = ResolveFixture.Run(CheckedFirst(rule));

        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
    }

    [Fact]
    public void AWhileCheckedAfterEachPass_MayReadItsOwnBody()
    {
        // the fixture's own refine: checkFirst false, its check reads judge
        var result = ResolveFixture.Run(ResolveFixture.NestedNote());

        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
    }

    [Fact]
    public void AWhileWithAnAgent_IsNotJudgedYet()
    {
        // refine's body holds agents, whose prompts' reads are unknown until the seam is supplied (D1): an unread node is not judged
        var files = ResolveFixture.NestedNote()
            .WithJson("nodes/refine/index.json", h => h["nodes"]!.AsArray().Add("aside"))
            .With("nodes/refine/aside.md", Agent("Noting an aside", "\"plaintext\"", "Note one thing Dr A. Sample may want to add.\n"));

        var result = ResolveFixture.Run(files);

        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
    }
}
