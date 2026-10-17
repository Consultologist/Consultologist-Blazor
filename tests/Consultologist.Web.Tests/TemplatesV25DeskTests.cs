using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C1 (#1007) → #1039: at 25 the desk IS the registry's validator, run
/// client-side over the composed package (Templates.RegistryDesk25); the words
/// are the registry's by construction. These hold the desk to a sample of each
/// family — the node and with rules (C1), the spellings and whens (#1039).
/// </summary>
public class TemplatesV25DeskTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(string manifestJson, bool withCatalogue = true)
    {
        var package = EditorFixtures.V25();
        package = package with { Manifest = JsonDocument.Parse(manifestJson).RootElement.Clone() };
        WorkflowService.GetCurrentPackageContentAsync().Returns(package);
        if (withCatalogue)
        {
            WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        }

        return Render<Templates>();
    }

    /// <summary>C4 (#1010): the fixture reshaped (manifest and/or files) — the refusals of publishing it.</summary>
    private IReadOnlyList<string> PublishRefusals(WorkflowPackageContentResponse package)
    {
        WorkflowService.PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>())
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));
        WorkflowService.GetCurrentPackageContentAsync().Returns(package);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:assessment";
        page.Render();
        page.Find("input[aria-label='Node label']").Change("Assessment (edited)");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();
        return Refusals(page);
    }

    private static WorkflowPackageContentResponse Reshaped(Action<System.Text.Json.Nodes.JsonObject>? reshape = null, params (string Path, string Text)[] files)
    {
        var package = EditorFixtures.V25();
        var root = System.Text.Json.Nodes.JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        reshape?.Invoke(root);
        var replaced = new Dictionary<string, string>(package.Files, StringComparer.Ordinal);
        foreach (var (path, text) in files)
        {
            replaced[path] = text;
        }

        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone(), Files = replaced };
    }

    private static System.Text.Json.Nodes.JsonObject PromptObject(System.Text.Json.Nodes.JsonObject root, string id) =>
        root["prompts"]!.AsArray().First(p => p!["id"]!.GetValue<string>() == id)!.AsObject();

    private static System.Text.Json.Nodes.JsonObject NodeObject(System.Text.Json.Nodes.JsonObject root, string id) =>
        root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == id)!.AsObject();

    public static IEnumerable<object[]> TemplateCases()
    {
        yield return new object[] { "prompt variables", new Action<System.Text.Json.Nodes.JsonObject>(root => PromptObject(root, "scope")["variables"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "referral" ]""")), null!,
            "Prompt 'scope' declares variables, which are retired at specVersion 25; a template reads objects (input.<id>, node.<id>.content, snippet.<id>.content, item.<member>, macro.<id>.content) and its reads are derived." };
        yield return new object[] { "prompt raw false", new Action<System.Text.Json.Nodes.JsonObject>(root => PromptObject(root, "scope")["raw"] = false), null!,
            "Prompt 'scope' declares raw, which is retired at specVersion 25; escape a literal '{{' as {{ \"{{\" }} or wrap it in {%{ … }%}." };
        yield return new object[] { "node bindings", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "scope")["bindings"] = new System.Text.Json.Nodes.JsonObject()), null!,
            "Node 'scope' declares bindings, which are retired at specVersion 25; a template reads objects (input.<id>, node.<id>.content, snippet.<id>.content, item.<member>, macro.<id>.content) and its reads are derived." };
        yield return new object[] { "undeclared node", null!, ("prompts/assessment.md", "Assess {{ node.nope.content }} {{ snippet.standards.content }} {{ macro.tool_note.prompt }}"),
            "Prompt 'assessment' reads node 'nope', which the package does not declare." };
        yield return new object[] { "unclosed brace names the line", null!, ("prompts/scope.md", "Is this in scope?\n{{ input.notes"),
            "Prompt 'scope' does not parse at line 2: '{{' is not closed; escape a literal '{{' as {{ \"{{\" }} or wrap it in {%{ … }%}." };
        yield return new object[] { "bare node", null!, ("prompts/scope.md", "{{ node.extract }} {{ input.notes }}"),
            "Prompt 'scope' reads node 'extract' bare; read node.extract.content (its composed output) or node.extract.value." };
        yield return new object[] { "retired data root", null!, ("prompts/scope.md", "{{ data.standards }} {{ input.notes }}"),
            "Prompt 'scope' reads 'data'; at specVersion 25 data is 'snippet' — read snippet.<id>.content." };
        yield return new object[] { "a non-member", null!, ("prompts/scope.md", "{{ snippet.standards.body }} {{ input.notes }}"),
            "Prompt 'scope' reads snippet.standards.body, which is not a member (a snippet has .id, .name and .content)." };
        yield return new object[] { "value on a text format", null!, ("prompts/scope.md", "{{ node.assessment.value }} {{ input.notes }}"),
            "Prompt 'scope' reads node.assessment.value, but format 'prose' is text and has no value; read node.assessment.content." };
        // #1058: renderer pipes are typed.
        yield return new object[] { "renderer pipe on another format", null!, ("prompts/assessment.md", "Assess {{ node.scope.content | concept_bullets }} {{ snippet.standards.content }} {{ macro.tool_note.prompt }}"),
            "Prompt 'assessment' pipes node.scope.content through concept_bullets, but format 'classification' (enum) is not concept_list; concept_bullets renders a concept_list node's text (node.<id>.content)." };
    }

    [Theory]
    [MemberData(nameof(TemplateCases))]
    public void TheDesk_SaysTheRegistrysTemplateSentence(string _, Action<System.Text.Json.Nodes.JsonObject>? reshape, (string Path, string Text)? file, string sentence)
    {
        var refusals = file is { } replaced
            ? PublishRefusals(Reshaped(reshape, replaced))
            : PublishRefusals(Reshaped(reshape));

        Assert.Contains(sentence, refusals);
    }

    [Fact]
    public void TheDesk_SaysASentenceOnce_WhenTwoNodesSharePrompt()
    {
        // meds and assessment both on the same text: one sentence, as the validator de-duplicates.
        var refusals = PublishRefusals(Reshaped(
            root => NodeObject(root, "meds")["prompt"] = "assessment",
            ("prompts/assessment.md", "Assess {{ node.nope.content }} {{ snippet.standards.content }} {{ macro.tool_note.prompt }}")));

        Assert.Single(refusals, r => r == "Prompt 'assessment' reads node 'nope', which the package does not declare.");
    }

    /// <summary>The fixture's manifest with one node replaced by the given JSON object.</summary>
    private static string WithNode(string nodeId, string nodeJson)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(EditorFixtures.V25().Manifest.GetRawText())!.AsObject();
        var nodes = root["nodes"]!.AsArray();
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i]!["id"]!.GetValue<string>() == nodeId)
            {
                nodes[i] = System.Text.Json.Nodes.JsonNode.Parse(nodeJson);
            }
        }

        return root.ToJsonString();
    }

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private IReadOnlyList<string> PublishRefusals(string manifestJson, bool withCatalogue = true)
    {
        WorkflowService.PublishPackageAsync(Arg.Any<WorkflowPackagePublishRequest>())
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));
        var page = RenderEditor(manifestJson, withCatalogue);
        // A pending change opens the publish button: the fixture's assessment label.
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:assessment";
        page.Render();
        page.Find("input[aria-label='Node label']").Change("Assessment (edited)");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();
        return Refusals(page);
    }

    [Theory]
    [InlineData("all", """{ "id": "all", "label": "Everything", "aggregate": ["node.assessment", "node.section_notes"], "format": "prose" }""",
        "Node 'all' aggregates and must not declare format; an aggregator is structural.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment" }""",
        "Node 'assessment' must declare a format at specVersion 25.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "poem" }""",
        "Node 'assessment' declares format 'poem', which is neither a package format nor a catalogue format.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "prompt": "scope", "format": "terms_subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' declares prompt, but format 'terms_subset' is computed and takes none.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "format": "prose" }""",
        "Node 'assessment' must declare a prompt; format 'prose' is attested.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "missing", "format": "prose" }""",
        "Node 'assessment' names prompt 'missing', which the package does not declare.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "prose", "onInvalid": "fail" }""",
        "Node 'assessment' declares onInvalid, but format 'prose' is text and validates nothing.")]
    [InlineData("extract", """{ "id": "extract", "label": "Extract", "prompt": "extract", "format": "concept_list" }""",
        "Node 'extract' must declare onInvalid (fail | retry | flag); format 'concept_list' is validated.")]
    [InlineData("extract", """{ "id": "extract", "label": "Extract", "prompt": "extract", "format": "concept_list", "onInvalid": "ignore" }""",
        "Node 'extract' onInvalid 'ignore' is not one of fail, retry, flag.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "prose", "failIfEmpty": "  " }""",
        "Node 'assessment' failIfEmpty must not be blank.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms_subset", "onInvalid": "fail", "failIfEmpty": "x", "with": { "of": "node.extract", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' declares failIfEmpty, but format 'terms_subset' is computed and has no reply to judge.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms_subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract", "failWith": "x", "extra": "y" } }""",
        "Node 'coverage' with 'extra' names no parameter of the format (parameters: of, in, failWith).")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms_subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract" } }""",
        "Node 'coverage' with must supply parameter 'failWith'.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms_subset", "onInvalid": "fail", "with": { "of": "extract", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' with 'of' must be a bare node reference (node.<id>); got \"extract\".")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms_subset", "onInvalid": "fail", "with": { "of": "node.nope", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' with 'of' names node 'nope', which the package does not declare.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms_subset", "onInvalid": "fail", "with": { "of": "node.assessment", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' with 'of' names node 'assessment' of format 'prose', but the parameter requires format 'concept_list'.")]
    [InlineData("scope", """{ "id": "scope", "label": "Scope", "prompt": "scope", "format": "classification", "onInvalid": "fail", "with": { "values": ["in_scope"] } }""",
        "Node 'scope' with 'values' declares one enum value; an enum with one value is a constant, not a choice.")]
    [InlineData("scope", """{ "id": "scope", "label": "Scope", "prompt": "scope", "format": "classification", "onInvalid": "fail", "with": { "values": "yes" } }""",
        "Node 'scope' with 'values' must be a list of snake_case values or an input.<id> reference to an enum input; got \"yes\".")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms_subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract", "failWith": 3 } }""",
        "Node 'coverage' with 'failWith' must be a text literal or an input.<id> reference to a text input; got 3.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "prose", "with": { "values": ["a", "b"] } }""",
        "Node 'assessment' declares with, but format 'prose' declares no parameters.")]
    public void TheDesk_SaysTheRegistrysNodeSentence(string nodeId, string nodeJson, string sentence)
    {
        var refusals = PublishRefusals(WithNode(nodeId, nodeJson));

        Assert.Contains(sentence, refusals);
    }

    private static System.Text.Json.Nodes.JsonObject DeliverableObject(System.Text.Json.Nodes.JsonObject root) =>
        root["deliverables"]!.AsArray().First()!.AsObject();

    /// <summary>
    /// #1039: the registry's spelling and when sentences at the desk — said by the
    /// registry's own validator, run client-side at 25, so the words are its by
    /// construction (the ≤24 mirrors never said these).
    /// </summary>
    public static IEnumerable<object[]> SpellingCases()
    {
        yield return new object[] { "aggregate source in the ≤24 spelling", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "all")["aggregate"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "node:assessment", "node.section_notes" ]""")),
            "Aggregator node 'all' source 'node:assessment' is the ≤24 spelling; at specVersion 25 a source is node.<id>." };
        yield return new object[] { "aggregate source bare", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "all")["aggregate"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "assessment", "node.section_notes" ]""")),
            "Aggregator node 'all' source 'assessment' must be node.<id>." };
        yield return new object[] { "aggregate source unknown", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "all")["aggregate"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "node.nope", "node.section_notes" ]""")),
            "Aggregator node 'all' references unknown node 'nope'." };
        yield return new object[] { "aggregate source itself", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "all")["aggregate"] = System.Text.Json.Nodes.JsonNode.Parse("""[ "node.all", "node.section_notes" ]""")),
            "Aggregator node 'all' aggregates itself." };
        yield return new object[] { "deliverable node in the ≤24 spelling", new Action<System.Text.Json.Nodes.JsonObject>(root => DeliverableObject(root)["node"] = "node:all"),
            "Deliverable 'consult' node 'node:all' is the ≤24 spelling; at specVersion 25 a deliverable names node.<id>." };
        yield return new object[] { "deliverable node bare", new Action<System.Text.Json.Nodes.JsonObject>(root => DeliverableObject(root)["node"] = "all"),
            "Deliverable 'consult' node 'all' must be node.<id>." };
        yield return new object[] { "node when in the ≤24 grammar", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["when"] = "node:scope == in_scope"),
            "Node 'assessment' condition 'node:scope == in_scope' is the ≤24 condition grammar; at specVersion 25 a when is a Scriban expression over the objects a template reads — write node.scope.value == \"in_scope\"." };
        yield return new object[] { "node when against an undeclared value", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["when"] = "node.scope.value == \"maybe\""),
            "Node 'assessment' condition compares node.scope.value to 'maybe', which it does not declare (values: in_scope, out_of_scope)." };
        yield return new object[] { "node when testing a text for truth", new Action<System.Text.Json.Nodes.JsonObject>(root => NodeObject(root, "assessment")["when"] = "input.notes"),
            "Node 'assessment' condition tests input.notes for truth; a text cannot be tested — compare it to a value." };
        yield return new object[] { "deliverable when yielding a text", new Action<System.Text.Json.Nodes.JsonObject>(root => DeliverableObject(root)["when"] = "snippet.standards.content | string.upcase"),
            "Deliverable 'consult' condition must be a boolean expression; it yields a text." };
    }

    [Theory]
    [MemberData(nameof(SpellingCases))]
    public void TheDesk_SaysTheRegistrysSpellingSentence(string _, Action<System.Text.Json.Nodes.JsonObject> reshape, string sentence)
    {
        Assert.Contains(sentence, PublishRefusals(Reshaped(reshape)));
    }

    [Fact]
    public void TheDesk_RefusesAHyphenatedPromptId_InTheRegistrysWords()
    {
        var refusals = PublishRefusals(Reshaped(
            root => root["prompts"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{ "id": "draft-section", "file": "prompts/draft-section.md" }""")),
            ("prompts/draft-section.md", "Draft {{ input.notes }}")));

        Assert.Contains("Prompt id 'draft-section' must be snake_case at specVersion 25 (draft_section); every declared id is.", refusals);
    }

    [Fact]
    public void TheDesk_AcceptsADeliverableWhen_ThatReadsANodesText()
    {
        // #1038 widened the boundary: a gating when may read a node's text.
        Assert.Empty(PublishRefusals(Reshaped(root => DeliverableObject(root)["when"] = "node.assessment.content != \"\"")));
    }

    [Fact]
    public void TheDesk_PassesTheWholeFixture()
    {
        Assert.Empty(PublishRefusals(EditorFixtures.V25().Manifest.GetRawText()));
    }

    [Fact]
    public void WithoutTheCatalogue_ACatalogueFormat_IsTakenOnTrust()
    {
        // The validator without a catalogue trusts a non-local id (it reports the
        // catalogue's absence, not the id); so does the desk, check reference included.
        Assert.Empty(PublishRefusals(EditorFixtures.V25().Manifest.GetRawText(), withCatalogue: false));
    }

    [Fact]
    public void ARemovedFormat_StillNamed_IsRefusedByName()
    {
        // Removal is refused in the pane while a node names the format; the desk's
        // sentence covers the draft that restores a removal whose reader came back.
        var page = RenderEditor(EditorFixtures.V25().Manifest.GetRawText());
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Replace("●", "").Trim() == "Formats").Click();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:meds";
        page.Render();
        page.Find("fluent-select.node-format").Change("prose");
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Replace("●", "").Trim() == "Formats").Click();
        page.Find("fluent-button[aria-label='Remove format medication_list']").Click();
        // The reader comes back onto the removed format (the picker no longer offers
        // it, so through the node's own pick staying as loaded: undo the re-pick).
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "node:meds";
        page.Render();
        page.Find("fluent-select.node-format").Change("medication_list");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

        Assert.Contains("Format 'medication_list' is removed but node 'meds' still names it as its format; change that node's format first.", Refusals(page));
    }

    [Fact]
    public void APackageFormatsBody_IsRefusedAtTheDesk_WithTheRegistrysSentence()
    {
        var page = RenderEditor(EditorFixtures.V25().Manifest.GetRawText());
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Replace("●", "").Trim() == "Formats").Click();
        page.Find("[data-format='medication_list'] fluent-text-area").Change("""{ "type": "object", "producer": "custom", "description": "x" }""");
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

        Assert.Contains("Format 'medication_list' is type 'object' and must declare a schema (every object format carries its shape; the engine validates replies against it).", Refusals(page));
    }
}
