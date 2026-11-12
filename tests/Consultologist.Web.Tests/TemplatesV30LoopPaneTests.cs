using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v30 (#1346 C2b, design § 2, § 3.4, § 5, § 11): the loop pane at 30, under the pre-bump seam —
/// a body takes a for, while, if or case (never one that holds it), an inner loop is offered its
/// outer item's lists, a for's until and max, and labelField pickers on an input and on a loop,
/// each beside revision 3's sentence. Below 30 none of it shows.
/// </summary>
public class TemplatesV30LoopPaneTests : ClientRenderTestContext
{
    private const string Note = "The item's name is kept in the job's record after its text is deleted — pick a field that names the item without patient detail.";

    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        return Render<Templates>(parameters => parameters.Add(page => page.AuthorsAboveCeiling, true));
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static IReadOnlyList<string> Options(IRenderedComponent<Templates> page, string select) =>
        page.Find(select).QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value") ?? string.Empty).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void Publish(IRenderedComponent<Templates> page)
    {
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.11.3", "acct-1234567890ab@v2026.11.3"),
                Array.Empty<string>()));
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();
        Assert.True(sent is not null, System.Text.RegularExpressions.Regex.Replace(page.Markup, @"\s+", " ") is var m && m.IndexOf("rejected", StringComparison.Ordinal) is var at and >= 0 ? m.Substring(at, Math.Min(1200, m.Length - at)) : "no publish");
    }

    private JsonObject SentNode(string id) =>
        JsonNode.Parse(sent!.Manifest.GetRawText())!["nodes"]!.AsArray().OfType<JsonObject>().Single(n => (string?)n["id"] == id);

    private void Valid()
    {
        var result = TemplatesV30ReadTests.Validate(sent!);
        Assert.True(result.Errors.Count == 0, string.Join("\n", result.Errors));
    }

    /// <summary>The 30 fixture, with each problem's actions a list (an element field of an array) and an unplaced loop over the risks drafting an action.</summary>
    private static WorkflowPackageContentResponse WithActions()
    {
        var at30 = EditorFixtures.V30();
        var root = JsonNode.Parse(at30.Manifest.GetRawText())!.AsObject();
        var nodes = root["nodes"]!.AsArray();
        nodes.Add(new JsonObject { ["id"] = "draft_action", ["label"] = "Draft action", ["format"] = "prose" });
        nodes.Add(new JsonObject
        {
            ["id"] = "actions", ["label"] = "Actions", ["for"] = "item in node.find_problems.value.risks",
            ["nodes"] = new JsonArray("draft_action"), ["ret"] = "node.draft_action"
        });
        var files = new Dictionary<string, string>(at30.Files, StringComparer.Ordinal)
        {
            ["prompts/draft_action.md"] = "Action {{ item.id }}.",
            // The problem's draft reads its actions — once nested, its own item's.
            ["prompts/draft_problem.md"] = "Problem {{ item.value.name }} of {{ node.problem_count.value }}: {{ for a in node.actions }}{{ a.content }} {{ end }}",
        };
        var element = """{ "type": "object", "additionalProperties": false, "required": ["name", "severity", "actions"], "properties": { "name": { "type": "string" }, "severity": { "type": "string", "enum": ["low", "high"] }, "actions": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["step"], "properties": { "step": { "type": "string" } } } } } }""";
        files["formats/problem_list.json"] = $$"""
            { "type": "object", "producer": "custom", "outputMacros": false,
              "description": "The problems a referral names.",
              "schema": { "type": "object", "additionalProperties": false, "required": ["problems", "risks", "summary"],
                          "properties": { "summary": { "type": "string" },
                                          "problems": { "type": "array", "items": {{element}} },
                                          "risks": { "type": "array", "items": {{element}} } } } }
            """;
        return new(at30.Name, at30.Version, 30, JsonDocument.Parse(root.ToJsonString()).RootElement.Clone(), files);
    }

    // ---- bodies take flows ------------------------------------------------------------------

    [Fact]
    public void ALoopsBody_TakesAnUnplacedLoop_AndTheInnerLoopIsOfferedItsOuterItemsList()
    {
        var page = RenderEditor(WithActions());
        Select(page, "node:problems");
        Assert.Contains("actions", Options(page, "fluent-select.loop-body-add"));

        page.Find("fluent-select.loop-body-add").Change("actions");
        Select(page, "node:actions");

        var sources = Options(page, "fluent-select.loop-collection");
        Assert.Contains("item.value.actions", sources);
        Assert.DoesNotContain("item.value.name", sources);

        page.Find("fluent-select.loop-collection").Change("item.value.actions");
        Publish(page);
        Assert.Equal(new[] { "refine", "actions" }, SentNode("problems")["nodes"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal("item in item.value.actions", SentNode("actions")["for"]!.GetValue<string>());
        Valid();
    }

    [Fact]
    public void ALoop_IsNeverOfferedIntoItsOwnBody_NorUnderIt()
    {
        var page = RenderEditor(WithActions());
        Select(page, "node:problems");
        page.Find("fluent-select.loop-body-add").Change("actions");
        Select(page, "node:actions");

        Assert.DoesNotContain("actions", Options(page, "fluent-select.loop-body-add"));
        Assert.DoesNotContain("problems", Options(page, "fluent-select.loop-body-add"));
    }

    [Fact]
    public void At29_ALoopsBody_OffersNoLoop()
    {
        var page = RenderEditor(EditorFixtures.V29());
        Select(page, "node:problems");

        Assert.DoesNotContain(page.FindAll("fluent-select.loop-body-add fluent-option"), o => o.TextContent.Contains(", for)"));
        Assert.Empty(page.FindAll(".loop-stop"));
    }

    // ---- until and max ----------------------------------------------------------------------

    [Fact]
    public void Max_StartsWithNoBound_IsWrittenOnceChosen_AndRemovedWhenUnbounded()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "node:problems");
        Assert.Equal(string.Empty, page.Find("fluent-select.loop-max").GetAttribute("current-value") ?? string.Empty);

        page.Find("fluent-select.loop-max").Change("3");
        Publish(page);
        Assert.Equal(3, SentNode("problems")["max"]!.GetValue<int>());
        Valid();
    }

    [Fact]
    public void AnUntil_IsWrittenThroughTheConditionBuilder_WithItsNote()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "node:problems");
        Assert.Contains("Runs the items one at a time, in order, stopping after the item where this holds.", page.Find(".loop-until-note").TextContent);

        page.Find(".loop-until .condition-field__resting").Click();
        var operands = Options(page, "fluent-select[aria-label='Condition operand for problems']");
        page.Find("fluent-select[aria-label='Condition operand for problems']").Change(operands.First(o => o.Contains("severity", StringComparison.Ordinal)));
        page.Find("fluent-select[aria-label='Condition value for problems']").Change("high");
        Publish(page);
        Assert.Equal("item.value.severity == \"high\"", SentNode("problems")["until"]!.GetValue<string>());
        Valid();
    }

    [Fact]
    public void AnUntilSetThenCleared_IsNotWritten()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "node:problems");
        page.Find(".loop-until .condition-field__resting").Click();
        var operands = Options(page, "fluent-select[aria-label='Condition operand for problems']");
        page.Find("fluent-select[aria-label='Condition operand for problems']").Change(operands.First(o => o.Contains("severity", StringComparison.Ordinal)));
        page.Find("fluent-select[aria-label='Condition value for problems']").Change("high");

        page.Find("fluent-button.loop-until-clear").Click();
        page.Find("fluent-select.loop-max").Change("2");
        Publish(page);

        Assert.False(SentNode("problems").ContainsKey("until"));
        Assert.Equal(2, SentNode("problems")["max"]!.GetValue<int>());
    }

    // ---- labelField pickers -----------------------------------------------------------------

    [Fact]
    public void TheInputsPicker_OffersOnlyRequiredTextFields_BesideTheSentence_AndWritesThePick()
    {
        var page = RenderEditor(EditorFixtures.V30());
        page.FindAll("button.editor-nav__item").First(button => button.TextContent.Replace("●", string.Empty).Trim() == "Inputs").Click();

        Assert.Equal(new[] { string.Empty, "title" }, Options(page, "fluent-select.input-label-field"));
        Assert.Contains(Note, page.Find(".label-field-note").TextContent);

        page.Find("fluent-select.input-label-field").Change(string.Empty);
        Publish(page);
        var cases = JsonNode.Parse(sent!.Manifest.GetRawText())!["inputs"]!.AsArray().OfType<JsonObject>().Single(i => (string?)i["id"] == "cases");
        Assert.False(cases.ContainsKey("labelField"));
        Valid();
    }

    [Fact]
    public void ALoopOverANodesList_OffersItsElementsRequiredTextFields_NotItsEnum()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "node:problems");

        Assert.Equal(new[] { string.Empty, "name" }, Options(page, "fluent-select.loop-label-field"));
        Assert.Contains(Note, page.Find(".label-field-note").TextContent);

        page.Find("fluent-select.loop-label-field").Change(string.Empty);
        Publish(page);
        Assert.False(SentNode("problems").ContainsKey("labelField"));
        Valid();
    }
}
