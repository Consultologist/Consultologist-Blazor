using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v30 (#1346 C2c, design § 3, § 4, § 11): the while pane and the let pane, now public (#1347) — a new while's max is chosen or publish is refused with the validator's sentence; a
/// loaded while's check stays as written, its bound, until and body edited; a let's expression
/// shows the validator's own sentences as it stands, its type and onInvalid chosen (never retry);
/// and the 30 fixture with every pane opened round-trips unchanged.
/// </summary>
public class TemplatesV30WhileLetTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse fixture)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture);
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        WorkflowService.GetCatalogSchemasAsync().Returns(EditorCatalogSchemas.CatalogSchemas);
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.11.3", "acct-1234567890ab@v2026.11.3"),
                Array.Empty<string>()));
        return Render<Templates>();
    }

    private WorkflowPackagePublishRequest? sent;

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item").First(button => button.TextContent.Replace("●", string.Empty).Trim() == label).Click();

    private static IReadOnlyList<string> Options(IRenderedComponent<Templates> page, string select) =>
        page.Find(select).QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value") ?? string.Empty).ToList();

    private static void ClickPublish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    /// <summary>The publish-rejected bar's sentences, or none.</summary>
    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page)
    {
        var markup = page.Markup;
        var at = markup.IndexOf("rejected — nothing was saved.", StringComparison.Ordinal);
        if (at < 0)
        {
            return Array.Empty<string>();
        }

        var list = markup[at..];
        var end = list.IndexOf("</ul>", StringComparison.Ordinal);
        return Regex.Matches(end < 0 ? list : list[..end], "<li[^>]*>(.*?)</li>").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim()).ToList();
    }

    private void Published()
    {
        Assert.True(sent is not null, "no publish");
        var result = TemplatesV30ReadTests.Validate(sent!);
        Assert.True(result.Errors.Count == 0, string.Join("\n", result.Errors));
    }

    private JsonObject SentNode(string id) =>
        JsonNode.Parse(sent!.Manifest.GetRawText())!["nodes"]!.AsArray().OfType<JsonObject>().Single(n => (string?)n["id"] == id);

    private static void Create(IRenderedComponent<Templates> page, string format, string id, string label)
    {
        Navigate(page, "+ Node");
        page.Find("fluent-select.new-node-format").Change(format);
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change(id);
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change(label);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();
    }

    // ---- "+ Node" -------------------------------------------------------------------------

    [Fact]
    public void PlusNode_At30_OffersAWhileAndALet_At29_Neither()
    {
        var at30 = RenderEditor(EditorFixtures.V30());
        Navigate(at30, "+ Node");
        Assert.Contains("while", Options(at30, "fluent-select.new-node-format"));
        Assert.Contains("let", Options(at30, "fluent-select.new-node-format"));
    }

    [Fact]
    public void PlusNode_At29_OffersNoWhileNorLet()
    {
        var at29 = RenderEditor(EditorFixtures.V29());
        Navigate(at29, "+ Node");
        Assert.DoesNotContain("while", Options(at29, "fluent-select.new-node-format"));
        Assert.DoesNotContain("let", Options(at29, "fluent-select.new-node-format"));
    }

    // ---- a while ---------------------------------------------------------------------------

    [Fact]
    public void ANewWhile_HasNoBound_AndIsRefusedWithTheSentence_UntilItIsChosen()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Create(page, "while", "review", "Review");

        Select(page, "node:review");
        Assert.Equal(string.Empty, page.Find("fluent-select.while-max").GetAttribute("current-value") ?? string.Empty);
        ClickPublish(page);
        Assert.Null(sent);
        Assert.Contains("While 'review' declares no max; a while must declare its bound (1 to 10).", Refusals(page));

        page.Find("fluent-select.while-max").Change("3");
        ClickPublish(page);
        Assert.DoesNotContain(Refusals(page), r => r.Contains("declares no max", StringComparison.Ordinal));
    }

    [Fact]
    public void ALoadedWhile_KeepsItsCheckAsWritten_AndItsBoundUntilAndCheckFirstAreWritten()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "node:refine");
        Assert.True(page.Find(".while-section").TextContent.Contains("pass.number < 2"), System.Text.RegularExpressions.Regex.Replace(page.Find(".while-section").TextContent, @"\s+", " "));

        page.Find("fluent-select.while-max").Change("3");
        page.Find(".while-check-first input[type='checkbox']").Change(true);
        ClickPublish(page);

        Published();
        var refine = SentNode("refine");
        Assert.Equal(("pass.number < 2", 3, true, "pass.number >= 2"),
            (refine["while"]!.GetValue<string>(), refine["max"]!.GetValue<int>(), refine["checkFirst"]!.GetValue<bool>(), refine["until"]!.GetValue<string>()));
        Assert.Equal(new[] { "draft_problem" }, refine["nodes"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void AWhilesBody_GivesUpANode_TheDeskNamesWhatIsMissing_AndTakesItBack()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "node:refine");

        page.FindAll(".flow-body fluent-button.loop-body__remove").Single().Click();
        Assert.Contains("draft_problem", Options(page, ".flow-body fluent-select.loop-body-add"));
        Assert.Empty(page.FindAll(".flow-body .loop-body__row"));
        ClickPublish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("refine", StringComparison.Ordinal));

        page.Find(".flow-body fluent-select.loop-body-add").Change("draft_problem");
        page.Find(".flow-body fluent-select.loop-ret").Change("draft_problem");
        var row = page.Find(".flow-body .loop-body__row").TextContent;
        Assert.Contains("Draft problem", row);
        Assert.Contains("· returned", row);
    }

    // ---- a let -----------------------------------------------------------------------------

    [Fact]
    public void ANewLet_ShowsTheValidatorsSentencesAsItStands_AndOffersNoRetry()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Create(page, "let", "risk_count", "Counting the risks");
        Select(page, "node:risk_count");

        Assert.Equal(new[] { string.Empty, "fail", "flag" }, Options(page, "fluent-select.let-on-invalid"));
        Assert.Equal(string.Empty, page.Find("fluent-select.let-type").GetAttribute("current-value") ?? string.Empty);
        Assert.Contains("problem_list", Options(page, "fluent-select.let-type"));

        page.Find("fluent-text-field.let-expression").Change("date.now");
        Assert.Contains(page.FindAll(".let-problem"), p => p.TextContent.Contains("date.now", StringComparison.Ordinal));

        page.Find("fluent-text-field.let-expression").Change("node.find_problems.value.risks | array.size");
        page.Find("fluent-select.let-type").Change("text");
        Assert.Contains(page.FindAll(".let-problem"), p => p.TextContent.Contains("yields a number; it declares type text", StringComparison.Ordinal));

        page.Find("fluent-select.let-type").Change("number");
        page.Find("fluent-select.let-on-invalid").Change("fail");
        Assert.Empty(page.FindAll(".let-problem"));
    }

    [Fact]
    public void ALoadedLet_IsEdited_AndWritten()
    {
        var page = RenderEditor(EditorFixtures.V30());
        Select(page, "node:problem_count");
        Assert.Equal("node.find_problems.value.problems | array.size", page.Find("fluent-text-field.let-expression").GetAttribute("value"));

        page.Find("fluent-select.let-on-invalid").Change("flag");
        ClickPublish(page);

        Published();
        var let = SentNode("problem_count");
        Assert.Equal(("node.find_problems.value.problems | array.size", "number", "flag"),
            (let["let"]!.GetValue<string>(), let["type"]!.GetValue<string>(), let["onInvalid"]!.GetValue<string>()));
    }

    // ---- the done-when ---------------------------------------------------------------------

    [Fact]
    public void The30Fixture_WithEveryPaneOpened_RoundTripsUnchanged()
    {
        var fixture = EditorFixtures.V30();
        var page = RenderEditor(fixture);
        foreach (var key in new[] { "node:refine", "node:problem_count", "node:problems", "graph" })
        {
            Select(page, key);
        }

        Navigate(page, "Inputs");
        page.FindAll("li.declared-row input")[1].Change("Referral notes");
        ClickPublish(page);

        Published();
        var expected = JsonNode.Parse(fixture.Manifest.GetRawText())!.AsObject();
        expected["inputs"]![0]!["label"] = "Referral notes";
        var actual = JsonNode.Parse(sent!.Manifest.GetRawText())!.AsObject();
        Assert.True(JsonNode.DeepEquals(expected, actual), actual.ToJsonString());
    }
}
