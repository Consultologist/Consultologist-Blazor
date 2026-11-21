using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): the resolve step of <see cref="SourceCheck.Run"/>, after the types and before
/// the expressions, whose scopes the expressions are read in: nothing a node, an item, a pass or a parameter is read
/// through is deferred any more, macro and snippet sites aside (area 07's).
/// </summary>
public class SourceResolveTests
{
    private static readonly string[] Resolved = ["node", "item", "pass", "param"];

    public static TheoryData<string> Examples => ["nested", "conditional", "functions"];

    private static Dictionary<string, byte[]> Example(string name) => name switch
    {
        "nested" => ResolveFixture.NestedNote(),
        "conditional" => ResolveFixture.Conditional(),
        _ => ResolveFixture.Functions(),
    };

    [Theory]
    [MemberData(nameof(Examples))]
    public void TheExamples_Resolve(string name)
    {
        var result = ResolveFixture.Run(Example(name));

        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
        Assert.NotNull(result.Resolution);
        Assert.IsType<ResolvedScopes>(result.Resolution.Scopes);
        Assert.NotEmpty(result.Expressions!.Sites);
        var deferred = result.Expressions.Sites
            .Where(s => s.Context.Site.Kind is not (ExpressionSiteKind.MacroIf or ExpressionSiteKind.SnippetIf))
            .Where(s => s.Check.Type?.Format == null && s.Check.Reads.Any(r => Resolved.Contains(r.Split('.')[0])))
            .Select(s => $"{s.Context.Site.Subject} {s.Context.Site.Member}")
            .ToList();
        Assert.Empty(deferred);
    }

    [Fact]
    public void EveryNode_HasItsKey()
    {
        var result = ResolveFixture.Run(ResolveFixture.Functions());
        var package = result.Parse.Package!;
        var keys = result.Resolution!.SourceKeys;

        var all = ResolveFixture.Walk(package.Nodes.Nodes.Concat(package.Checks.Nodes))
            .Concat(package.Functions.Items.SelectMany(f => ResolveFixture.Walk(f.Body.Nodes)))
            .ToList();
        Assert.Equal(all.Count, keys.Count);
        Assert.All(all, n => Assert.Equal(n.Key, keys[n]));
        Assert.Equal("check.section_coverage.coverage", keys[ResolveFixture.Node(package, "check.section_coverage.coverage")]);
        Assert.Equal("node.apply_instructions", keys[ResolveFixture.Node(package, "node.apply_instructions", "draft_section")]);
    }

    [Fact]
    public void AnExpressionReadingAnUnknownNode_IsS03014()
    {
        var files = Minimal()
            .With("nodes/index.json", Index(["plan", "stamp"]))
            .With("nodes/stamp.md", Md(Node("computed", "Stamping the plan", "\"plaintext\"", true, true,
                "\"if\": null, \"expression\": {\"var\":\"node.nothing.value\"}"), ""));

        var result = ResolveFixture.Run(files);

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("S03-014", refusal.Id);
        Assert.Equal("nodes/stamp.md", refusal.Path);
        Assert.Equal("Computed node 'stamp' reads node.nothing.value, which doesn't exist here.", refusal.Sentence);
        V31Contract.AssertSentence(refusal);
        Assert.NotNull(result.Resolution);
        Assert.Null(result.Expressions);
    }

    [Fact]
    public void AWhilesCondition_ReadsItsOwnBody()
    {
        // refine's while reads node.judge, a node of its own body, typed: a verdict compared with a value it lacks is refused.
        var files = ResolveFixture.NestedNote().WithJson("nodes/refine/index.json",
            h => h["while"] = System.Text.Json.Nodes.JsonNode.Parse("{ \"!==\": [{ \"var\": \"node.judge.value.verdict\" }, \"fine\"] }"));

        var result = ResolveFixture.Run(files);

        var refusal = Assert.Single(result.Refusals);
        Assert.Equal("S03-023", refusal.Id);
        Assert.Equal("nodes/refine/index.json", refusal.Path);
    }

    [Fact]
    public void ADeliverablesCondition_ReadsTheTopLevel()
    {
        var files = ResolveFixture.NestedNote().WithJson("deliverables/note.md",
            h => h["if"] = System.Text.Json.Nodes.JsonNode.Parse("{ \">\": [{ \"var\": \"node.refine.passes\" }, 0] }"));
        Assert.True(ResolveFixture.Run(files).Refusals.Count == 0, ResolveFixture.Said(ResolveFixture.Run(files)));

        var hidden = ResolveFixture.NestedNote().WithJson("deliverables/note.md",
            h => h["if"] = System.Text.Json.Nodes.JsonNode.Parse("{ \"!\": [{ \"blank\": [{ \"var\": \"node.judge.value.verdict\" }] }] }"));
        var refusal = Assert.Single(ResolveFixture.Run(hidden).Refusals);
        Assert.Equal("S04-039", refusal.Id);
        Assert.Equal("Deliverable 'note' reads node 'judge', which runs inside loop 'refine'; read node.refine.", refusal.Sentence);
    }

    [Fact]
    public void AMacrosCondition_StaysDeferred()
    {
        // Where a macro is placed is area 07's: its if is read in PackageScope's scope until then.
        var files = Minimal()
            .With("macros/index.json", Index(["footer"]))
            .With("macros/footer.md", Md(
                "{ \"label\": \"Footer\", \"description\": null, \"if\": { \"!\": [{ \"blank\": [{ \"var\": \"node.anything.content\" }] }] }, "
                + "\"format\": \"plaintext\", \"instruction\": [\"Place {{ macro.footer.content }} at the end of the note.\"] }",
                "Seen with Dr A. Sample.\n"));

        var result = ResolveFixture.Run(files);

        Assert.True(result.Refusals.Count == 0, ResolveFixture.Said(result));
    }

    [Fact]
    public void Check_TakesNoNull()
    {
        var (package, types, _) = ResolveFixture.Tree(Minimal());
        Assert.Throws<ArgumentNullException>(() => SourceResolve.Check(null!, types));
        Assert.Throws<ArgumentNullException>(() => SourceResolve.Check(package, null!));
        var (refusals, resolution) = SourceResolve.Check(package, types);
        Assert.Empty(refusals);
        Assert.NotNull(resolution);
    }
}
