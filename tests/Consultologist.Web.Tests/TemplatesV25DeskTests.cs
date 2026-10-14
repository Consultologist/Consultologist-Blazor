using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v25 C1 (#1007): the desk's mirror of the A4 node rules and the with rules —
/// each the validator's own sentence, said before publish. The editor never runs
/// the validator (a hand-written mirror, as every version's); these hold the
/// words to the registry's.
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
    [InlineData("all", """{ "id": "all", "label": "Everything", "aggregate": ["node:assessment", "node:section_notes"], "format": "prose" }""",
        "Node 'all' aggregates and must not declare format; an aggregator is structural.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment" }""",
        "Node 'assessment' must declare a format at specVersion 25.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "poem" }""",
        "Node 'assessment' declares format 'poem', which is neither a package format nor a catalogue format.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "prompt": "scope", "format": "terms-subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' declares prompt, but format 'terms-subset' is computed and takes none.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "format": "prose" }""",
        "Node 'assessment' must declare a prompt; format 'prose' is attested.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "missing", "format": "prose" }""",
        "Node 'assessment' names prompt 'missing', which the package does not declare.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "prose", "onInvalid": "fail" }""",
        "Node 'assessment' declares onInvalid, but format 'prose' is text and validates nothing.")]
    [InlineData("extract", """{ "id": "extract", "label": "Extract", "prompt": "extract", "format": "concept-list" }""",
        "Node 'extract' must declare onInvalid (fail | retry | flag); format 'concept-list' is validated.")]
    [InlineData("extract", """{ "id": "extract", "label": "Extract", "prompt": "extract", "format": "concept-list", "onInvalid": "ignore" }""",
        "Node 'extract' onInvalid 'ignore' is not one of fail, retry, flag.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "prose", "failIfEmpty": "  " }""",
        "Node 'assessment' failIfEmpty must not be blank.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms-subset", "onInvalid": "fail", "failIfEmpty": "x", "with": { "of": "node.extract", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' declares failIfEmpty, but format 'terms-subset' is computed and has no reply to judge.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms-subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract", "failWith": "x", "extra": "y" } }""",
        "Node 'coverage' with 'extra' names no parameter of the format (parameters: of, in, failWith).")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms-subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract" } }""",
        "Node 'coverage' with must supply parameter 'failWith'.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms-subset", "onInvalid": "fail", "with": { "of": "extract", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' with 'of' must be a bare node reference (node.<id>); got \"extract\".")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms-subset", "onInvalid": "fail", "with": { "of": "node.nope", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' with 'of' names node 'nope', which the package does not declare.")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms-subset", "onInvalid": "fail", "with": { "of": "node.assessment", "in": "node.extract", "failWith": "x" } }""",
        "Node 'coverage' with 'of' names node 'assessment' of format 'prose', but the parameter requires format 'concept-list'.")]
    [InlineData("scope", """{ "id": "scope", "label": "Scope", "prompt": "scope", "format": "classification", "onInvalid": "fail", "with": { "values": ["in_scope"] } }""",
        "Node 'scope' declares 1 value; a classifier must declare at least two values it may answer.")]
    [InlineData("scope", """{ "id": "scope", "label": "Scope", "prompt": "scope", "format": "classification", "onInvalid": "fail", "with": { "values": "yes" } }""",
        "Node 'scope' with 'values' must be a list of snake_case values or an input.<id> reference to an enum input; got \"yes\".")]
    [InlineData("coverage", """{ "id": "coverage", "label": "Coverage", "format": "terms-subset", "onInvalid": "fail", "with": { "of": "node.extract", "in": "node.extract", "failWith": 3 } }""",
        "Node 'coverage' with 'failWith' must be a text literal or an input.<id> reference to a text input; got 3.")]
    [InlineData("assessment", """{ "id": "assessment", "label": "Assessment", "prompt": "assessment", "format": "prose", "with": { "values": ["a", "b"] } }""",
        "Node 'assessment' declares with, but format 'prose' takes no parameters.")]
    public void TheDesk_SaysTheRegistrysNodeSentence(string nodeId, string nodeJson, string sentence)
    {
        var refusals = PublishRefusals(WithNode(nodeId, nodeJson));

        Assert.Contains(sentence, refusals);
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
