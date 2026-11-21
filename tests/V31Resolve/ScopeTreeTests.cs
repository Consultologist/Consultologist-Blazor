using Consultologist.Api.Tests.V31Source;
using Consultologist.PackageFormat.V31;
using static Consultologist.Api.Tests.V31Source.SourceFixture;

namespace Consultologist.Api.Tests.V31Resolve;

/// <summary>
/// #1502 (specVersion 31, area 05): who encloses whom (part 01 § 9.2, part 04 § 4): <c>node.x</c> resolves outward,
/// to the one node its own scope sees, stopping at a function's root; checks see the top level of <c>nodes/</c>.
/// </summary>
public class ScopeTreeTests
{
    private const string Plain = "\"plaintext\"";

    [Fact]
    public void Visible_ResolvesOutward()
    {
        var (package, _, tree) = ResolveFixture.Tree(ResolveFixture.NestedNote());
        var frame = tree.FrameOf(ResolveFixture.Node(package, "node.sections.paragraphs.draft_paragraph"));

        Assert.Equal("node.sections.outline", frame.Visible("node", "outline")?.Key);
        Assert.Equal("node.plan", frame.Visible("node", "plan")?.Key);
        Assert.Null(frame.Visible("node", "judge"));
        Assert.Null(frame.Function);
        Assert.Equal("paragraphs", frame.Owner?.Id);
        Assert.Equal("sections", frame.Parent?.Owner?.Id);
        Assert.Equal(["draft_paragraph"], frame.Nodes.Select(n => n.Id));

        Assert.Null(tree.PackageRoot.Visible("node", "outline"));
        Assert.Equal("node.sections.outline", tree.PackageRoot.Hidden("node", "outline")?.Key);
        Assert.Null(tree.PackageRoot.Hidden("node", "plan"));
        Assert.Null(tree.PackageRoot.Owner);
        Assert.Null(tree.PackageRoot.Parent);
    }

    [Fact]
    public void LoopsAndWhiles_AreCountedOutward()
    {
        var (package, _, tree) = ResolveFixture.Tree(ResolveFixture.NestedNote());
        var paragraph = tree.FrameOf(ResolveFixture.Node(package, "node.sections.paragraphs.draft_paragraph"));
        var judge = tree.FrameOf(ResolveFixture.Node(package, "node.refine.judge"));

        Assert.Equal("paragraphs", paragraph.Loop(0)?.Id);
        Assert.Equal("sections", paragraph.Loop(1)?.Id);
        Assert.Null(paragraph.Loop(2));
        Assert.Null(paragraph.While(0));
        Assert.Equal("refine", judge.While(0)?.Id);
        Assert.Null(judge.While(1));
        Assert.Null(judge.Loop(0));
    }

    [Fact]
    public void SiblingScopes_ResolveToTheirOwn()
    {
        // Two sibling loops, each holding a draft and a reader beside it.
        var files = Minimal()
            .WithJson("manifest.json", m => m["inputs"]!["prior_notes"] = System.Text.Json.Nodes.JsonNode.Parse(
                "{ \"label\": \"Prior notes\", \"description\": null, \"format\": { \"type\": \"array\", \"items\": { \"$ref\": \"plaintext\" } }, "
                + "\"required\": true, \"channel\": null }"))
            .With("nodes/index.json", Index(["plan", "first_pass", "second_pass"]));
        foreach (var loop in new[] { "first_pass", "second_pass" })
        {
            files.With($"nodes/{loop}/index.json", Json(Node("for", "Reading the prior notes", ArrayOf(Plain), false, true,
                    "\"in\": \"input.prior_notes.value\", \"parallel\": true, \"maxParallel\": null, \"until\": null, "
                    + $"\"max\": null, \"itemLabel\": null, \"nodes\": [\"draft\", \"reader\"], \"ret\": \"node.{loop}.reader\"")))
                .With($"nodes/{loop}/draft.md", Agent("Drafting from a prior note", Plain, "Summarise this prior note: {{ item.value }}\n"))
                .With($"nodes/{loop}/reader.md", Md(Node("computed", "Reading the draft", Plain, true, true,
                    "\"if\": null, \"expression\": { \"var\": \"node.draft.content\" }"), ""));
        }

        var (package, _, tree) = ResolveFixture.Tree(files);
        foreach (var loop in new[] { "first_pass", "second_pass" })
        {
            var frame = tree.FrameOf(ResolveFixture.Node(package, $"node.{loop}.reader"));
            var resolved = tree.Resolve(ReferenceString.Parse("node.draft.content")!, frame, "Computed node 'reader'", $"nodes/{loop}/reader.md", true);
            Assert.Equal(ScopeHeadKind.Format, resolved.Kind);
            Assert.Equal($"node.{loop}.draft", resolved.Key);
            Assert.Same(ResolveFixture.Node(package, $"node.{loop}.draft"), frame.Visible("node", "draft"));
        }

        Assert.Null(tree.PackageRoot.Visible("node", "draft"));
        Assert.Equal("node.first_pass.draft", tree.PackageRoot.Hidden("node", "draft")?.Key);
        Assert.True(ResolveFixture.Run(files).Refusals.Count == 0, ResolveFixture.Said(ResolveFixture.Run(files)));
    }

    [Fact]
    public void AFunctionsNode_ShadowsNothingOutside()
    {
        // The function holds a node plan, as the top level does.
        var files = ResolveFixture.Functions()
            .WithJson("functions/draft_section/index.json", f => f["nodes"]!.AsArray().Insert(0, "plan"))
            .With("functions/draft_section/plan.md", Agent("Planning the section", Plain,
                "Plan the section '{{ param.standard.value.title }}'.\n"));

        var (package, _, tree) = ResolveFixture.Tree(files);
        var function = package.Functions.Items.Single();
        var inner = ResolveFixture.Node(package, "node.plan", "draft_section");
        var frame = tree.FrameOf(ResolveFixture.Node(package, "node.apply_instructions", "draft_section"));

        Assert.Same(function, frame.Function);
        Assert.Same(function, tree.FunctionRoot(function).Function);
        Assert.Same(tree.FunctionRoot(function), frame);
        Assert.Same(inner, frame.Visible("node", "plan"));
        Assert.NotSame(package.Nodes.Nodes.Single(n => n.Id == "plan"), inner);
        var resolved = tree.Resolve(ReferenceString.Parse("node.plan.content")!, frame,
            "Node 'apply_instructions' in function 'draft_section'", "functions/draft_section/apply_instructions.md", true);
        Assert.Equal("node.plan", resolved.Key);
        Assert.Equal(ScopeHeadKind.Format, resolved.Kind);
    }

    [Fact]
    public void ANodeAndACheck_ShareAnId()
    {
        var files = Minimal()
            .With("nodes/index.json", Index(["plan", "coverage"]))
            .With("nodes/coverage.md", Md(Node("computed", "Counting the words", "\"number\"", true, false,
                "\"if\": null, \"expression\": { \"words\": [{ \"var\": \"input.consult_draft.content\" }] }"), ""))
            .With("checks/index.json", Index(["coverage"]))
            .With("checks/coverage.md", Md(Node("computed", "The draft has words", "\"check_result\"", true, false,
                "\"if\": null, \"expression\": { \"object\": { \"pass\": { \">\": [{ \"var\": \"node.coverage.value\" }, 0] }, "
                + "\"message\": null, \"uncovered\": null, \"untested\": null, \"passed\": null, \"total\": null } }"), ""));

        var (package, _, tree) = ResolveFixture.Tree(files);
        var check = ResolveFixture.Node(package, "check.coverage");
        var frame = tree.FrameOf(check);

        Assert.Same(tree.ChecksRoot, frame);
        var node = frame.Visible("node", "coverage");
        Assert.Equal("node.coverage", node?.Key);
        Assert.Same(check, frame.Visible("check", "coverage"));
        Assert.NotSame(node, check);
        Assert.Same(check, tree.PackageRoot.Visible("check", "coverage"));
        Assert.True(ResolveFixture.Run(files).Refusals.Count == 0, ResolveFixture.Said(ResolveFixture.Run(files)));
    }

    [Fact]
    public void Build_TakesNoNull()
    {
        var (package, types, _) = ResolveFixture.Tree(Minimal());
        Assert.Throws<ArgumentNullException>(() => ScopeTree.Build(null!, types));
        Assert.Throws<ArgumentNullException>(() => ScopeTree.Build(package, null!));
    }
}
