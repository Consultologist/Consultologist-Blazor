using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Source;
using Consultologist.Api.Tests.V31Templates;
using Consultologist.PackageFormat.V31;
using Consultologist.PackageFormat.V31.Run;
using Consultologist.PackageFormat.V31.Templates;
using static Consultologist.Api.Tests.V31Templates.FolderItemFixture;

namespace Consultologist.Api.Tests.V31Run;

/// <summary>
/// #1504 (specVersion 31, area 07; part 05 §§ 3.2, 6.2; D12): at run time a loop over a snippet folder's <c>item.content</c>
/// is the item's body rendered in the composition's scope: the package's own text for the item the scope's
/// <c>item.content</c> is, from the plan's <c>snippets</c>, counted against the composition's limits. A scope whose
/// <c>item.content</c> is no item of the folder fails with S07-006, and is never rendered. Content is fictional
/// (Margaret Okafor, Dr A. Sample).
/// </summary>
public class FolderItemComposeTests
{
    private const string Draft = "Margaret Okafor, 58, referred by Dr A. Sample for fatigue (fictional).";

    private const string Hpi = "Open with '{{ item.value.title }}'. The draft:\n{{ input.consult_draft.content }}\n";

    private const string Section = "node.standards.section";

    private static Composition Compose(RunPlan plan, JsonObject scope, int steps = 100_000) =>
        BodyComposer.Compose(plan, new ComposeRequest(Section, null, scope, new Dictionary<string, bool>(StringComparer.Ordinal), steps));

    /// <summary>The data <c>section</c> is given for the item titled <paramref name="title"/> whose body is <paramref name="content"/>, as the runner gives it (<c>item.content</c> the item's body as written).</summary>
    private static JsonObject Scope(string title, string? content) => new()
    {
        ["item"] = new JsonObject { ["value"] = new JsonObject { ["title"] = title }, ["content"] = content },
        ["input"] = new JsonObject { ["consult_draft"] = new JsonObject { ["content"] = Draft } },
        ["node"] = new JsonObject { ["plan"] = new JsonObject { ["value"] = Sections(3) } },
    };

    private static JsonObject Sections(int count) => new()
    {
        ["sections"] = new JsonArray(Enumerable.Range(1, count)
            .Select(i => (JsonNode?)new JsonObject { ["title"] = $"Section {i}", ["kind"] = "narrative" }).ToArray()),
    };

    private static string Composed(Composition composition)
    {
        Assert.True(composition.Failure == null, composition.Failure);
        Assert.False(composition.TooManySteps);
        return composition.Text!;
    }

    [Fact]
    public void AFolderItemsBody_IsRenderedInTheLoopsScope()
    {
        var plan = RunFixture.Plan(Standards(Hpi, "Then '{{ item.value.title }}', one line each.\n"));
        Assert.Contains("input.consult_draft.content", plan.Entries[Section].Reads);

        var hpi = Compose(plan, Scope("History of Present Illness", Hpi));
        var pmh = Compose(plan, Scope("Past Medical History", "Then '{{ item.value.title }}', one line each.\n"));

        Assert.Equal(
            $"Draft the section 'History of Present Illness' for Margaret Okafor.\n\nOpen with 'History of Present Illness'. The draft:\n{Draft}\n\n",
            Composed(hpi));
        Assert.Equal(
            "Draft the section 'Past Medical History' for Margaret Okafor.\n\nThen 'Past Medical History', one line each.\n\n",
            Composed(pmh));
        Assert.Empty(hpi.Units);
        Assert.Empty(hpi.Renders);
    }

    [Fact]
    public void AnEmptyFolderItemBody_IsEmpty()
    {
        var plan = RunFixture.Plan(Standards(Hpi, ""));

        Assert.Equal("Draft the section 'Past Medical History' for Margaret Okafor.\n\n\n", Composed(Compose(plan, Scope("Past Medical History", ""))));
    }

    [Fact]
    public void AFolderItemsBody_CountsAgainstTheLimits()
    {
        const string loops = "Sections: {{ for s in node.plan.value.sections }}{{ s.title }}; {{ end }}\n";
        var plan = RunFixture.Plan(Standards(loops));

        Assert.Equal(
            "Draft the section 'History of Present Illness' for Margaret Okafor.\n\nSections: Section 1; Section 2; Section 3; \n\n",
            Composed(Compose(plan, Scope("History of Present Illness", loops), steps: 3)));
        var stopped = Compose(plan, Scope("History of Present Illness", loops), steps: 2);
        Assert.True(stopped.TooManySteps);
        Assert.Null(stopped.Text);

        var many = Scope("History of Present Illness", loops);
        many["node"]!["plan"]!["value"] = Sections(1001);
        var limit = Compose(plan, many);
        Assert.Null(limit.Text);
        Assert.Equal("Node 'section' stopped rendering: the for at line 1 reached 1,000 iterations.", limit.Failure);
        V31Contract.AssertSentence(new SourceRefusal("S05-012", limit.Failure!, ""));
    }

    [Fact]
    public void AnOuterLoopsFolderItem_IsRenderedThroughParent()
    {
        // The loop standards over the folder holds the loop pieces over the plan's sections; its agent reads the outer item's
        // body as item.parent.content, rendered where it is read (its item is the section).
        var files = Folder("For '{{ item.value.title }}': {{ input.consult_draft.content }}\n")
            .Listed("nodes/index.json", "standards")
            .With("nodes/standards/index.json", SourceFixture.Json(SourceFixture.Node("for", "Standards", SourceFixture.ArrayOf(SourceFixture.ArrayOf(TemplateFixture.Plain)), false, true,
                "\"in\": \"snippet.standards\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"pieces\"], \"ret\": \"node.standards.pieces\"")))
            .With("nodes/standards/pieces/index.json", SourceFixture.Json(SourceFixture.Node("for", "Pieces", SourceFixture.ArrayOf(TemplateFixture.Plain), false, true,
                "\"in\": \"node.plan.value.sections\", \"parallel\": true, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": \"title\", \"nodes\": [\"part_draft\"], \"ret\": \"node.standards.pieces.part_draft\"")))
            .With("nodes/standards/pieces/part_draft.md", SourceFixture.Agent("Drafting a part", TemplateFixture.Plain,
                "Under '{{ item.parent.value.title }}':\n{{ item.parent.content }}\n"));
        var plan = RunFixture.Plan(files);
        var scope = new JsonObject
        {
            ["item"] = new JsonObject
            {
                ["value"] = new JsonObject { ["title"] = "Section 1", ["kind"] = "narrative" },
                ["parent"] = new JsonObject
                {
                    ["value"] = new JsonObject { ["title"] = "History of Present Illness" },
                    ["content"] = "For '{{ item.value.title }}': {{ input.consult_draft.content }}\n",
                },
            },
            ["input"] = new JsonObject { ["consult_draft"] = new JsonObject { ["content"] = Draft } },
        };

        var composition = BodyComposer.Compose(plan, new ComposeRequest(
            "node.standards.pieces.part_draft", null, scope, new Dictionary<string, bool>(StringComparer.Ordinal), 100_000));

        Assert.Equal($"Under 'History of Present Illness':\nFor 'Section 1': {Draft}\n\n", Composed(composition));
    }

    [Theory]
    [InlineData("Some other text.\n")]
    [InlineData("{{ input.consult_draft.content }}\n")]
    [InlineData(null)]
    public void AnItemThePlanLacks_IsS07006(string? content)
    {
        var plan = RunFixture.Plan(Standards(Hpi));
        var sentence = ResolveSentences.ResolveDoesNotMatch(plan.Package, plan.Version).Sentence;

        var composition = Compose(plan, Scope("History of Present Illness", content));

        Assert.Null(composition.Text);
        Assert.Equal(sentence, composition.Failure);
        V31Contract.AssertSentence(new SourceRefusal("S07-006", composition.Failure!, ""));
    }
}
