using System.Text.Json.Nodes;
using Consultologist.Api.Tests.V31Resolve;
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

    [Fact]
    public void AFolderItemsBody_CountsAgainstTheOutputLimit()
    {
        // The folder body writes the draft twice: 600,000 bytes each pass 1 MiB inside the folder item's own text (S05-014).
        const string twice = "{{ input.consult_draft.content }}{{ input.consult_draft.content }}\n";
        var plan = RunFixture.Plan(Standards(twice));
        var scope = Scope("History of Present Illness", twice);
        scope["input"]!["consult_draft"]!["content"] = new string('x', 600_000);

        var composition = Compose(plan, scope);

        Assert.Null(composition.Text);
        Assert.Equal("Node 'section' stopped rendering: its output passed 1 MiB.", composition.Failure);
        V31Contract.AssertSentence(new SourceRefusal("S05-014", composition.Failure!, ""));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFunctionsFolderItem_IsFoundInItsOwnDefinitions_ThenThePackages(bool own)
    {
        // The function draft_section loops over the folder steps: its own (the package's steps, of other text, unlisted),
        // or, holding none, the package's, listed in its uses.
        const string step = "Use the draft: {{ param.draft.content }}\n";
        var files = ResolveFixture.Functions()
            .With("snippets/index.json", SourceFixture.Index(["standards", "steps"]))
            .With("snippets/steps/index.json", SourceFixture.Index(["one"], extra: ", \"format\": \"standard_section\""))
            .With("snippets/steps/one.md", SourceFixture.Md("{ \"title\": \"Package step\" }", own ? "The package's own text.\n" : step))
            .With("functions/draft_section/each/index.json", SourceFixture.Json(SourceFixture.Node("for", "Each step", SourceFixture.ArrayOf(TemplateFixture.Plain), false, true,
                "\"in\": \"snippet.steps\", \"parallel\": false, \"maxParallel\": null, \"until\": null, \"max\": null, "
                + "\"itemLabel\": null, \"nodes\": [\"note\"], \"ret\": \"node.each.note\"")))
            .With("functions/draft_section/each/note.md", SourceFixture.Agent("Noting the step", TemplateFixture.Plain, "Step:\n{{ item.content }}\n"));
        if (own)
        {
            files.With("functions/draft_section/snippets/index.json", SourceFixture.Index(["steps"]))
                .With("functions/draft_section/snippets/steps/index.json", SourceFixture.Index(["one"], extra: ", \"format\": \"standard_section\""))
                .With("functions/draft_section/snippets/steps/one.md", SourceFixture.Md("{ \"title\": \"First step\" }", step));
        }

        var index = System.Text.Encoding.UTF8.GetString(files["functions/draft_section/index.json"])
            .Replace("\"nodes\": [\"standard_draft\", \"patient_draft\", \"apply_instructions\"]",
                "\"nodes\": [\"standard_draft\", \"patient_draft\", \"apply_instructions\", \"each\"]", StringComparison.Ordinal);
        if (!own)
            index = index.Replace("\"uses\": { \"snippets\": []", "\"uses\": { \"snippets\": [\"steps\"]", StringComparison.Ordinal);
        files["functions/draft_section/index.json"] = System.Text.Encoding.UTF8.GetBytes(index);
        var plan = RunFixture.Plan(files);
        var scope = new JsonObject
        {
            ["item"] = new JsonObject { ["value"] = new JsonObject { ["title"] = "First step" }, ["content"] = step },
            ["param"] = new JsonObject { ["draft"] = new JsonObject { ["content"] = Draft } },
        };

        var composition = BodyComposer.Compose(plan, new ComposeRequest(
            "node.each.note", "draft_section", scope, new Dictionary<string, bool>(StringComparer.Ordinal), 100_000));

        Assert.Equal($"Step:\nUse the draft: {Draft}\n\n", Composed(composition));
    }

    [Theory]
    [InlineData("On {{ run.date }}.\n", null)]
    [InlineData("{{~ snippet.standards.pmh.content }}\n", null)]
    [InlineData("{{ item.value.title | renderer.line }}\n", null)]
    [InlineData("Open {{ if item.value.title }}plainly.\n", null)]
    [InlineData("Open {{ item.value.title\n", null)]
    [InlineData("Open differently, {{ item.value.title }}.\n", "Open plainly.\n")]
    [InlineData("Open differently.\n", "{{ input.consult_draft.content }}\n")]
    public void AFolderBodyEditedAfterPublish_IsS07006(string edited, string? given)
    {
        // A document edited after publish: the folder item's body writes a run fact, places a unit, pipes into a renderer
        // or no longer parses (the scope giving it as the runner would), or the scope's item.content is no longer the
        // body the document holds (the text before the edit, or one that looks like a template): never a throw, and the
        // scope's value is never parsed.
        var files = Standards("Open plainly.\n");
        var (_, compiled) = CompiledPackage.Publish(files, ResolveFixture.Published(), CompiledGoldenTests.Fixed);
        var document = JsonNode.Parse(compiled!.Resolve)!;
        document["snippets"]!["snippet.standards.hpi"]!["body"] = edited;
        var (plan, _) = RunPlan.Build(document.ToJsonString(), compiled.ResolveHash, compiled.LockHash, null);

        var composition = Compose(plan!, Scope("History of Present Illness", given ?? edited));

        Assert.Null(composition.Text);
        Assert.Equal(ResolveSentences.ResolveDoesNotMatch(plan!.Package, plan.Version).Sentence, composition.Failure);
        V31Contract.AssertSentence(new SourceRefusal("S07-006", composition.Failure!, ""));
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
