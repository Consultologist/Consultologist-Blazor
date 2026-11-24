using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): <see cref="ScopeTree.Resolve"/>, the one resolution every reference goes
/// through: the head a reference names at a place (part 03 § 4.1, part 04 §§ 2.5, 2.6, 3.3, 3.4, 7.4; D14), and the
/// sentences of the reads a place may not make (S04-036 to S04-040, S04-050, S04-051).
/// </summary>
public class ResolvedScopeTests
{
    private const string Element = "{ title: plaintext, kind: string {narrative, table, omit} }";

    /// <summary>The fixture named <paramref name="name"/>.</summary>
    private static Dictionary<string, byte[]> Fixture(string name) => name switch
    {
        "nested" => ResolveFixture.NestedNote(),
        "conditional" => ResolveFixture.Conditional(),
        "functions" => ResolveFixture.Functions(),
        "flows" => ResolveFixture.LoopFlows(),
        // plan runs only when the draft is not blank, and stamp reads it
        "gated" => Minimal()
            .WithJson("nodes/plan.md", h => h["if"] = System.Text.Json.Nodes.JsonNode.Parse(
                "{ \"!\": [{ \"blank\": [{ \"var\": \"input.consult_draft.content\" }] }] }"))
            .With("nodes/index.json", Index(["plan", "stamp"]))
            .With("nodes/stamp.md", Md(Node("computed", "Stamping the plan", "\"plaintext\"", true, true,
                "\"if\": null, \"expression\": { \"var\": \"node.plan.content\" }"), "")),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>
    /// A place in a fixture: <c>root</c> (the top of <c>nodes/</c>), <c>checks</c>, <c>function:&lt;id&gt;</c> (a
    /// function's root), or a node's key (its frame), <c>&lt;function&gt;/&lt;key&gt;</c> inside a function.
    /// </summary>
    private static (ScopeFrame Frame, FormatSet Formats) At(SourcePackage package, PackageTypes types, ScopeTree tree, string at)
    {
        if (at == "root")
            return (tree.PackageRoot, types.Formats);
        if (at == "checks")
            return (tree.ChecksRoot, types.Formats);
        if (at.StartsWith("function:", StringComparison.Ordinal))
        {
            var id = at["function:".Length..];
            return (tree.FunctionRoot(package.Functions.Items.Single(f => f.Id == id)), types.FunctionFormats[id]);
        }

        var slash = at.IndexOf('/');
        return slash < 0
            ? (tree.FrameOf(ResolveFixture.Node(package, at)), types.Formats)
            : (tree.FrameOf(ResolveFixture.Node(package, at[(slash + 1)..], at[..slash])), types.FunctionFormats[at[..slash]]);
    }

    public static TheoryData<string, string, string, ScopeHeadKind, string?, int, string?, bool> HeadCases => new()
    {
        { "nested", "node.section_count", "node.plan.value.sections", ScopeHeadKind.Format, "node.plan", 2, "section_plan", false },
        { "nested", "root", "node.plan.title", ScopeHeadKind.Format, "node.plan", 2, "section_plan", false },
        { "nested", "root", "node.refine.passes", ScopeHeadKind.Value, "node.refine", 3, "integer", false },
        { "nested", "root", "node.refine.stopped", ScopeHeadKind.Value, "node.refine", 3, "string {check, until, bound}", false },
        { "nested", "root", "node.refine.history", ScopeHeadKind.Value, "node.refine", 3, "list<{}>", false },
        { "nested", "root", "node.sections.stopped", ScopeHeadKind.Value, "node.sections", 3, "string {until, bound, end}", false },
        { "nested", "root", "node.plan.state", ScopeHeadKind.Value, "node.plan", 3, "string {completed, skipped, not_run}", false },
        { "nested", "root", "node.sections.value", ScopeHeadKind.Format, "node.sections", 2, "list<list<plaintext>>", false },
        { "nested", "node.sections.outline", "item.value.title", ScopeHeadKind.Value, null, 2, Element, false },
        { "nested", "node.sections.outline", "item.id", ScopeHeadKind.Value, null, 2, "string", false },
        { "nested", "node.sections.paragraphs.draft_paragraph", "item.parent.value.title", ScopeHeadKind.Value, null, 3, Element, false },
        { "nested", "node.sections.paragraphs.draft_paragraph", "item.value", ScopeHeadKind.Value, null, 2, "plaintext", false },
        { "nested", "node.sections.paragraphs.draft_paragraph", "node.outline.value.points", ScopeHeadKind.Format, "node.sections.outline", 2, "outline", false },
        { "nested", "node.refine.judge", "node.judge.value.verdict", ScopeHeadKind.Format, "node.refine.judge", 2, "verdict", false },
        { "nested", "node.refine.judge", "pass.number", ScopeHeadKind.Value, null, 2, "integer", false },
        { "nested", "node.refine.judge", "pass.history", ScopeHeadKind.Value, null, 2, "list<{}>", false },
        { "nested", "node.refine.draft_summary", "pass.node.judge.value", ScopeHeadKind.Format, null, 3, "verdict", false },
        { "nested", "node.refine.draft_summary", "pass.previous.node.judge.value", ScopeHeadKind.Format, null, 4, "verdict", true },
        { "nested", "root", "input.consult_draft.content", ScopeHeadKind.Format, "input.consult_draft", 2, "markdown_block", false },
        { "nested", "root", "run.date", ScopeHeadKind.Value, null, 2, "date", false },
        { "conditional", "root", "input.include_billing.value", ScopeHeadKind.Format, "input.include_billing", 2, "boolean", true },
        { "conditional", "node.patient_summaries.narrative_only", "item.value.kind", ScopeHeadKind.Value, null, 2, "section_request", false },
        { "functions", "draft_section/node.standard_draft", "param.standard.value.title", ScopeHeadKind.Format, "param.standard", 2, "standard_section", false },
        { "functions", "function:draft_section", "node.patient_draft.content", ScopeHeadKind.Format, "node.patient_draft", 2, "plaintext", false },
        // D14: standard_draft has an if
        { "functions", "function:draft_section", "node.standard_draft.content", ScopeHeadKind.Format, "node.standard_draft", 2, "plaintext", true },
        { "functions", "node.standards.section", "item.value.title", ScopeHeadKind.Value, null, 2, "standard_section", false },
        { "functions", "node.standards.section", "item.content", ScopeHeadKind.Value, null, 2, "markdown_block", false },
        { "functions", "check.all_sections", "check.section_coverage.value", ScopeHeadKind.Format, "check.section_coverage", 2, "list<check_result>", false },
        { "functions", "check.section_coverage.coverage", "node.extract_input_terms.value", ScopeHeadKind.Format, "node.extract_input_terms", 2, "concept_list", false },
        // D14: a leaf with an if may be skipped, so what it holds may be null
        { "gated", "root", "node.plan.content", ScopeHeadKind.Format, "node.plan", 2, "plaintext", true },
    };

    [Theory]
    [MemberData(nameof(HeadCases))]
    public void Heads(string fixture, string at, string reference, ScopeHeadKind kind, string? key, int length, string? type, bool nullable)
    {
        var (package, types, tree) = ResolveFixture.Tree(Fixture(fixture));
        var (frame, formats) = At(package, types, tree, at);

        var resolved = tree.Resolve(ReferenceString.Parse(reference)!, frame, "Node 'x'", "nodes/x.md", true);

        Assert.Equal(kind, resolved.Kind);
        Assert.Equal(key, resolved.Key);
        Assert.Equal(length, resolved.Length);
        Assert.Equal(type, TypeStrings.Print(resolved.Format, formats));
        Assert.Equal(nullable, resolved.Nullable);
        Assert.Null(resolved.Refusal);
    }

    public static TheoryData<string, string, string, string?, int, string, bool> LoopContentCases => new()
    {
        // #1507 (D23; part 04 § 2.5, part 05 § 5.3): a for loop's .content is the standard rendering (## <item name>, the
        // item's text, joined by blank lines), typed markdown_block whatever its items' format: sections returns
        // list<list<plaintext>>, paragraphs and standards list<plaintext>; read from outside, from inside another loop's
        // body, and from the checks
        { "nested", "root", "node.sections.content", "node.sections", 3, "markdown_block", false },
        { "nested", "node.sections.outline", "node.paragraphs.content", "node.sections.paragraphs", 3, "markdown_block", false },
        { "functions", "checks", "node.standards.content", "node.standards", 3, "markdown_block", false },
        // a check loop's, and a for read through a while's pass
        { "functions", "checks", "check.section_coverage.content", "check.section_coverage", 3, "markdown_block", false },
        { "flows", "node.redo.redo_loop", "pass.node.redo_loop.content", null, 4, "markdown_block", false },
        // Task 24's fix round 1: a flow's .content is its ret's (part 04 §§ 3, 5), so a flow whose ret chain reaches a for
        // holds that loop's rendering: an if, a case and a while whose rets are fors
        { "flows", "root", "node.pick.content", "node.pick", 3, "markdown_block", false },
        { "flows", "root", "node.choose.content", "node.choose", 3, "markdown_block", false },
        { "flows", "root", "node.redo.content", "node.redo", 3, "markdown_block", false },
        // branches that differ (a for, a plain-text leaf) give the union, which a Markdown deliverable escapes (R10)
        { "flows", "root", "node.mixed.content", "node.mixed", 3, "markdown_block | plaintext", false },
    };

    [Theory]
    [MemberData(nameof(LoopContentCases))]
    public void ALoopsContent_IsMarkdownBlock(string fixture, string at, string reference, string? key, int length, string type, bool nullable)
    {
        var (package, types, tree) = ResolveFixture.Tree(Fixture(fixture));
        var (frame, formats) = At(package, types, tree, at);

        var resolved = tree.Resolve(ReferenceString.Parse(reference)!, frame, "Node 'x'", "nodes/x.md", true);

        Assert.Equal((ScopeHeadKind.Value, key, length, nullable), (resolved.Kind, resolved.Key, resolved.Length, resolved.Nullable));
        Assert.Equal(type, TypeStrings.Print(resolved.Format, formats));
        Assert.Null(resolved.Refusal);
    }

    [Fact]
    public void ALoopsContent_LeavesEveryOtherContentAsItWas()
    {
        // D23 and Task 24's fix round 1 change only what a for gives: a while's .content whose ret is a leaf, and an
        // object-valued node's, are typed as before (D10).
        var (_, types, tree) = ResolveFixture.Tree(ResolveFixture.NestedNote());
        var formats = types.Formats;

        string Typed(string reference)
        {
            var resolved = tree.Resolve(ReferenceString.Parse(reference)!, tree.PackageRoot, "Node 'x'", "nodes/x.md", true);
            Assert.Equal((ScopeHeadKind.Format, 2), (resolved.Kind, resolved.Length));
            var read = FormatReads.Read(resolved.Format, ["content"], formats);
            Assert.Equal(ReadFault.None, read.Fault);
            return TypeStrings.Print(read.Format, formats);
        }

        Assert.Equal("plaintext", Typed("node.refine.content"));   // a while whose ret is a plain-text leaf
        Assert.Equal("plaintext", Typed("node.plan.content"));     // an object (section_plan)

        // through .items: an item's .content is its ret's, here the inner for paragraphs, so markdown_block (fix round 1);
        // a leaf's is its format's text (D10); an inner for's is its own, markdown_block; each nullable
        var items = tree.Resolve(ReferenceString.Parse("node.sections.items")!, tree.PackageRoot, "Node 'x'", "nodes/x.md", true);
        var element = ExpressionChecker.ItemsOf(items.Format!, formats);
        string Field(params string[] fields) =>
            TypeStrings.Print(FormatReads.Read(element, ["value", .. fields], formats).Format, formats);
        Assert.Equal("markdown_block | null", Field("content"));
        Assert.Equal("plaintext | null", Field("nodes", "outline", "content"));
        Assert.Equal("markdown_block | null", Field("nodes", "paragraphs", "content"));

        // an outer for's item whose ret is an inner for (visits over points): markdown_block, nullable
        var (_, flowTypes, flowTree) = ResolveFixture.Tree(ResolveFixture.LoopFlows());
        var visits = flowTree.Resolve(ReferenceString.Parse("node.visits.items")!, flowTree.PackageRoot, "Node 'x'", "nodes/x.md", true);
        var visit = ExpressionChecker.ItemsOf(visits.Format!, flowTypes.Formats);
        Assert.Equal("markdown_block | null", TypeStrings.Print(FormatReads.Read(visit, ["value", "content"], flowTypes.Formats).Format, flowTypes.Formats));
    }

    [Fact]
    public void AnInput_IsWhatPackageScopeGave()
    {
        var (package, _, tree) = ResolveFixture.Tree(ResolveFixture.NestedNote());
        var segments = new[] { "input", "consult_draft", "content" };

        var resolved = tree.Resolve(new ReferenceString("input.consult_draft.content", segments), tree.PackageRoot, "Node 'x'", "nodes/x.md", true);
        var given = new PackageScope(package, inFunction: false).Head(segments);

        Assert.Equal((given.Kind, given.Length, given.Nullable), (resolved.Kind, resolved.Length, resolved.Nullable));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(given.Format, resolved.Format));
    }

    [Fact]
    public void ALoopOverItems_HasTheElementAsItsItem()
    {
        var (package, types, tree) = ResolveFixture.Tree(ResolveFixture.Functions());
        var frame = tree.FrameOf(ResolveFixture.Node(package, "check.section_coverage.coverage"));

        var item = tree.Resolve(ReferenceString.Parse("item.state")!, frame, "Check 'coverage'", "checks/section_coverage/coverage.md", true);
        Assert.Equal(ScopeHeadKind.Value, item.Kind);
        Assert.Equal(1, item.Length);

        string Read(params string[] fields)
        {
            var read = FormatReads.Read(item.Format, ["value", .. fields], types.Formats);
            Assert.Equal(ReadFault.None, read.Fault);
            return TypeStrings.Print(read.Format, types.Formats) + (read.Nullable ? " (nullable)" : "");
        }

        Assert.Equal("string {completed, skipped, not_run}", Read("state"));
        Assert.Equal("string", Read("label"));
        Assert.Equal("plaintext | null (nullable)", Read("value"));
        Assert.Equal("plaintext | null (nullable)", Read("nodes", "section", "value"));
        Assert.Equal("string {completed, skipped, not_run}", Read("nodes", "section", "state"));
        Assert.Equal("standard_section", Read("item", "value"));
        Assert.Equal("string", Read("item", "id"));

        var id = tree.Resolve(ReferenceString.Parse("item.id")!, frame, "Check 'coverage'", "checks/section_coverage/coverage.md", true);
        Assert.Equal((ScopeHeadKind.Value, 2, "string"), (id.Kind, id.Length, TypeStrings.Print(id.Format, types.Formats)));

        // node.standards.items, read from the checks, is a list of that element
        var items = tree.Resolve(ReferenceString.Parse("node.standards.items")!, tree.ChecksRoot, "Check 'x'", "checks/x.md", true);
        Assert.Equal((ScopeHeadKind.Value, "node.standards", 3), (items.Kind, items.Key, items.Length));
        Assert.StartsWith("list<{ item: ", TypeStrings.Print(items.Format, types.Formats), StringComparison.Ordinal);
    }

    [Fact]
    public void ALoopWhoseSourceIsRefused_DefersItsItem()
    {
        // The loop iterates the plan's sections, but plan is plaintext: the loop's source is refused (S03-014), so its
        // item says nothing more (one mistake, one sentence).
        var files = Minimal()
            .With("nodes/index.json", Index(["plan", "sections"]))
            .With("nodes/sections/index.json", Json(Node("for", "Sections", ArrayOf("\"plaintext\""), false, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"outline\"], \"ret\": \"node.sections.outline\"")))
            .With("nodes/sections/outline.md", Agent("Outlining the section", "\"plaintext\"", "Outline the section.\n"));
        var (package, _, tree) = ResolveFixture.Tree(files);

        var resolved = tree.Resolve(ReferenceString.Parse("item.value.title")!, tree.FrameOf(ResolveFixture.Node(package, "node.sections.outline")),
            "Node 'outline'", "nodes/sections/outline.md", true);

        Assert.Equal(ScopeHeadKind.Deferred, resolved.Kind);
        Assert.Null(resolved.Refusal);
        Assert.Equal("S03-014", tree.Source((SourceFor)ResolveFixture.Node(package, "node.sections")).Resolved.Refusal?.Id);
    }

    public static TheoryData<string, string, string, string?> RefusalCases => new()
    {
        { "nested", "node.sections.outline", "item.parent.value.x", "S04-040" },
        { "nested", "node.sections.outline", "item.parent.parent.parent", "S04-040" },
        { "nested", "node.section_count", "pass.number", "S04-036" },
        { "nested", "node.refine.judge", "pass.parent.number", "S04-037" },
        { "nested", "node.refine.judge", "pass.previous.node.plan.value", "S04-038" },
        { "nested", "node.section_count", "node.outline.value", "S04-039" },
        { "nested", "node.section_count", "node.draft_paragraph.content", "S04-039" },
        { "nested", "node.section_count", "node.judge.value", "S04-039" },
        { "functions", "draft_section/node.standard_draft", "input.consult_draft.value", "S04-050" },
        { "functions", "draft_section/node.standard_draft", "run.date", "S04-051" },
        { "functions", "draft_section/node.standard_draft", "check.section_coverage.value", "S04-051" },
        { "functions", "draft_section/node.standard_draft", "node.plan.value", "S04-051" },
        { "functions", "draft_section/node.standard_draft", "item.value.title", "S04-051" },
        { "functions", "draft_section/node.standard_draft", "pass.number", "S04-051" },
        { "nested", "root", "node.nothing.value", null },
        { "nested", "root", "check.nothing.value", null },
        { "nested", "root", "item.value", null },
        { "nested", "root", "item.parent.value", null },
        { "nested", "root", "pass.node.judge.value", null },
        { "nested", "node.refine.judge", "pass.node.plan.value", null },
        { "nested", "root", "param.standard.value", null },
        { "functions", "draft_section/node.standard_draft", "param.nothing.value", null },
        { "functions", "draft_section/node.standard_draft", "node.nothing.value", null },
        { "nested", "root", "profile.signature", null },
    };

    [Theory]
    [MemberData(nameof(RefusalCases))]
    public void Refusals(string fixture, string at, string reference, string? id)
    {
        var (package, types, tree) = ResolveFixture.Tree(Fixture(fixture));
        var (frame, _) = At(package, types, tree, at);
        var subject = frame.Function == null ? "Node 'x'" : $"Node 'standard_draft' in function '{frame.Function.Id}'";

        var resolved = tree.Resolve(ReferenceString.Parse(reference)!, frame, subject, "nodes/x.md", true,
            frame.Function == null ? null : ResolveFixture.Node(package, "node.standard_draft", frame.Function.Id));

        if (id == null)
        {
            Assert.Equal(ScopeHeadKind.Unknown, resolved.Kind);
            Assert.Null(resolved.Refusal);
            return;
        }

        Assert.Equal(ScopeHeadKind.Refused, resolved.Kind);
        Assert.Equal(id, resolved.Refusal?.Id);
        Assert.Equal("nodes/x.md", resolved.Refusal!.Path);
        V31Contract.AssertSentence(resolved.Refusal);
    }

    [Fact]
    public void TheSentences_NameWhatWasRead()
    {
        var (package, _, tree) = ResolveFixture.Tree(ResolveFixture.NestedNote());
        var top = tree.FrameOf(ResolveFixture.Node(package, "node.section_count"));
        var judge = tree.FrameOf(ResolveFixture.Node(package, "node.refine.judge"));
        string Said(string reference, ScopeFrame frame) =>
            tree.Resolve(ReferenceString.Parse(reference)!, frame, "Computed node 'section_count'", "nodes/x.md", true).Refusal!.Sentence;

        Assert.Equal("Computed node 'section_count' reads node 'outline', which runs inside loop 'sections'; read node.sections.",
            Said("node.outline.value", top));
        Assert.Equal("Computed node 'section_count' reads node 'draft_paragraph', which runs inside loop 'sections'; read node.sections.",
            Said("node.draft_paragraph.value", top));
        Assert.Equal("Computed node 'section_count' reads pass.previous.node.plan, which is not in while 'refine'; a pass reads the while's own nodes.",
            Said("pass.previous.node.plan.value", judge));

        var (functions, _, inner) = ResolveFixture.Tree(ResolveFixture.Functions());
        var reader = ResolveFixture.Node(functions, "node.standard_draft", "draft_section");
        var resolved = inner.Resolve(ReferenceString.Parse("input.consult_draft.value")!, inner.FrameOf(reader),
            "Node 'standard_draft' in function 'draft_section'", "functions/draft_section/standard_draft.md", true, reader);
        Assert.Equal("Node 'standard_draft' in function 'draft_section' reads input.consult_draft; a function reads only its parameters: "
            + "add a parameter and pass it in with.", resolved.Refusal!.Sentence);
        resolved = inner.Resolve(ReferenceString.Parse("run.date")!, inner.FrameOf(reader),
            "Node 'standard_draft' in function 'draft_section'", "functions/draft_section/standard_draft.md", true, reader);
        Assert.Equal("Node 'standard_draft' in function 'draft_section' reads run.date; a function reads only its parameters: "
            + "add a parameter and pass it in with.", resolved.Refusal!.Sentence);
    }

    [Fact]
    public void AnyReference_IsResolvedOrRefused_NeverThrows()
    {
        string[] words = ["node", "check", "input", "item", "pass", "param", "run", "parent", "previous", "history", "number",
            "value", "content", "items", "state", "plan", "sections", "outline", "x", "0"];
        var random = new Random(31);
        var references = Enumerable.Range(0, 2000)
            .Select(_ => string.Join('.', Enumerable.Range(0, random.Next(1, 302)).Select(_ => words[random.Next(words.Length)])))
            .ToList();

        var resolved = 0;
        foreach (var files in new[] { ResolveFixture.NestedNote(), ResolveFixture.Functions() })
        {
            var (package, _, tree) = ResolveFixture.Tree(files);
            var frames = new List<ScopeFrame> { tree.PackageRoot, tree.ChecksRoot };
            frames.AddRange(package.Functions.Items.Select(tree.FunctionRoot));
            frames.AddRange(ResolveFixture.Walk(package.Nodes.Nodes.Concat(package.Checks.Nodes)).Select(tree.FrameOf));
            frames.AddRange(package.Functions.Items.SelectMany(f => ResolveFixture.Walk(f.Body.Nodes)).Select(tree.FrameOf));

            foreach (var text in references)
            {
                // a reference string with a position is not one; the scope is still asked for it by its segments
                var reference = ReferenceString.Parse(text) ?? new ReferenceString(text, text.Split('.'));
                foreach (var frame in frames.Distinct())
                {
                    var result = tree.Resolve(reference, frame, "Node 'x'", "nodes/x.md", resolved % 2 == 0);
                    Assert.True(result.Length >= 0 && result.Length <= reference.Segments.Count, $"{text}: length {result.Length}");
                    Assert.Equal(result.Kind == ScopeHeadKind.Refused, result.Refusal != null);
                    resolved++;
                }
            }
        }

        Assert.True(resolved > 2000);
    }

    [Fact]
    public void Resolve_TakesNoNull()
    {
        var (_, _, tree) = ResolveFixture.Tree(Minimal());
        var reference = ReferenceString.Parse("node.plan")!;
        Assert.Throws<ArgumentNullException>(() => tree.Resolve(null!, tree.PackageRoot, "Node 'x'", "x", true));
        Assert.Throws<ArgumentNullException>(() => tree.Resolve(reference, null!, "Node 'x'", "x", true));
        Assert.Throws<ArgumentNullException>(() => tree.Resolve(reference, tree.PackageRoot, null!, "x", true));
        Assert.Throws<ArgumentNullException>(() => tree.Resolve(reference, tree.PackageRoot, "Node 'x'", null!, true));
    }
}
