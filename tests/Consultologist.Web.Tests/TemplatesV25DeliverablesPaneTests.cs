using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1079 (Milestone 31, the Deliverables pane walk): the pane's facts at 25 —
/// the check picker offers the nodes of a computed format as node.&lt;id&gt; (kind is
/// retired), the refusals speak the registry's noun, the resting Produced-when
/// help names both gating sources, and the edit state is honest: folding a blank
/// gate records no edit, and Discard and the sidebar dot follow the deliverables'
/// own drafts rather than every pane's. Rendered at 25 alone; the ≤24 suites
/// (V12 check picker, V7/V9 refusals) stay as the parity proof.
/// </summary>
public class TemplatesV25DeliverablesPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static void Publish(IRenderedComponent<Templates> page) =>
        PublishButton(page).Click();

    private static AngleSharp.Dom.IElement PublishButton(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish"));

    private static string DeliverablesDot(IRenderedComponent<Templates> page) =>
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Contains("Deliverables"))
            .QuerySelector(".editor-nav__dot")!.TextContent.Trim();

    private static IReadOnlyList<string?> OptionValues(IRenderedComponent<Templates> page, string selector) =>
        page.Find(selector).QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    private static JsonElement Manifest(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement;

    private static JsonElement Deliverable(JsonElement manifest) =>
        manifest.GetProperty("deliverables").EnumerateArray().Single();

    /// <summary>The V25 package with nothing that can gate a deliverable: no enum input, no enum-format node, no gate reading them.</summary>
    private static WorkflowPackageContentResponse V25WithNothingToGateOn()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        var inputs = root["inputs"]!.AsArray();
        inputs.Remove(inputs.First(i => i!["id"]!.GetValue<string>() == "kind"));
        // The intake object carries a boolean field — testable too.
        inputs.Remove(inputs.First(i => i!["id"]!.GetValue<string>() == "intake"));
        var nodes = root["nodes"]!.AsArray();
        nodes.Remove(nodes.First(n => n!["id"]!.GetValue<string>() == "scope"));
        var prompts = root["prompts"]!.AsArray();
        prompts.Remove(prompts.First(p => p!["id"]!.GetValue<string>() == "scope"));
        var deliverable = root["deliverables"]!.AsArray()[0]!.AsObject();
        deliverable["macros"]!.AsArray()[0]!.AsObject().Remove("when");
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    // ----- the check picker -----

    [Fact]
    public void TheCheckPicker_OffersComputedFormatNodes_AsNodeDot_At25()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");

        var picker = page.Find("fluent-select[aria-label='Check for deliverable consult']");
        // coverage is the one terms_subset (computed) node; kind: check is retired, so
        // before #1079 the list was empty and the authored value rode the fallback.
        Assert.Equal(new[] { "", "node.coverage" }, OptionValues(page, "fluent-select[aria-label='Check for deliverable consult']"));
        Assert.Equal("Coverage", picker.QuerySelectorAll("fluent-option").Last().TextContent.Trim());
        Assert.Equal("node.coverage", picker.GetAttribute("current-value"));
        Assert.DoesNotContain("node:", picker.OuterHtml);
    }

    [Fact]
    public void ChoosingACheck_WritesNodeDot_AndValidates()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "Deliverables");

        page.Find("fluent-select[aria-label='Check for deliverable consult']").Change("");
        page.Find("fluent-select[aria-label='Check for deliverable consult']").Change("node.coverage");
        Publish(page);

        Assert.Equal("node.coverage", Deliverable(Manifest(sent!)).GetProperty("check").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void AnAuthoredCheck_NoComputedNodeMatches_IsOfferedAsItself()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["deliverables"]!.AsArray()[0]!.AsObject()["check"] = "node.elsewhere";
        var page = RenderEditor(package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() });
        Navigate(page, "Deliverables");

        Assert.Equal(new[] { "", "node.coverage", "node.elsewhere" }, OptionValues(page, "fluent-select[aria-label='Check for deliverable consult']"));
    }

    // ----- the registry's words -----

    [Fact]
    public void ThePanesRefusals_SpeakTheRegistrysWords_At25()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");

        page.Find("input[aria-label='New deliverable id']").Change("Bad Id");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("+ Deliverable")).Click();
        Assert.StartsWith("Deliverable id 'Bad Id' must be snake_case", page.Find(".editor-warning").TextContent);

        page.Find("input[aria-label='New deliverable id']").Change("consult");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("+ Deliverable")).Click();
        Assert.Equal("Duplicate deliverable id 'consult'.", page.Find(".editor-warning").TextContent.Trim());

        page.Find("input[aria-label='Label for deliverable consult']").Change("");
        Assert.Equal("Deliverable 'consult' has no label.", page.Find(".editor-warning").TextContent.Trim());
        Assert.DoesNotContain("Result", page.Find(".editor-warning").TextContent);
    }

    // ----- the when help -----

    [Fact]
    public void TheRestingHelp_NamesInputsAndEnumNodes_At25()
    {
        var page = RenderEditor(V25WithNothingToGateOn());
        Navigate(page, "Deliverables");

        var when = page.Find("[data-when-for='consult']").TextContent;
        Assert.Contains("Always produced.", when);
        Assert.Contains("or an enum-format node to make it conditional", when);
        Assert.Empty(page.FindAll("[data-when-for='consult'] fluent-button.condition-field__resting"));
    }

    // ----- the edit state -----

    [Fact]
    public void FoldingABlankGate_RecordsNoEdit()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");
        Assert.True(PublishButton(page).HasAttribute("disabled"));

        OpenCondition(page);
        page.Find("[data-when-for='consult'] fluent-button.condition-field__collapse").Click();

        // Before #1079 the fold wrote null over null through the target's writer,
        // materialising the deliverables' edit: Publish, Discard and the dot lit.
        Assert.True(PublishButton(page).HasAttribute("disabled"));
        Assert.Empty(page.FindAll(".pane-discard-button"));
        Assert.Equal(string.Empty, DeliverablesDot(page));
    }

    [Fact]
    public void ANodesDraft_DoesNotLightTheDeliverablesPane()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");
        OpenCondition(page, ".node-when-editor");
        // A second, unchosen clause: a draft the format cannot write (choosing an
        // enum operand would complete the clause with its first value instead).
        page.Find(".node-when-editor fluent-button[aria-label='Add condition clause to assessment']").Click();
        Assert.Equal(2, page.FindAll(".node-when-editor .condition-clause").Count);

        Navigate(page, "Deliverables");

        // Before #1079 the shared drafts dictionary lit this pane's Discard.
        Assert.Empty(page.FindAll(".pane-discard-button"));
        Assert.Equal(string.Empty, DeliverablesDot(page));
    }

    [Fact]
    public void ADeliverablesDraft_LightsTheDot_AndItsDiscardClearsOnlyIt()
    {
        var page = RenderEditor();
        ShowNode(page, "assessment");
        OpenCondition(page, ".node-when-editor");
        page.Find(".node-when-editor fluent-button[aria-label='Add condition clause to assessment']").Click();

        Navigate(page, "Deliverables");
        OpenCondition(page);
        page.Find("[data-when-for='consult'] fluent-button[aria-label='Add condition clause to consult']").Click();

        // A draft on the deliverable's own when: the dot (before #1079 it followed the edit only).
        Assert.Equal("●", DeliverablesDot(page));
        page.Find(".pane-discard-button").Click();
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Confirm discard")).Click();

        Assert.Equal(string.Empty, DeliverablesDot(page));
        Assert.Empty(page.FindAll(".pane-discard-button"));
        Assert.Single(page.FindAll("[data-when-for='consult'] .condition-clause"));

        // The node's own draft survives the Deliverables pane's discard (before #1079 it was cleared with it).
        ShowNode(page, "assessment");
        Assert.Equal(2, page.FindAll(".node-when-editor .condition-clause").Count);
    }

    // ----- #1081: layout and focus -----

    private static string Normalised(string text) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private static string SelectedNav(IRenderedComponent<Templates> page) =>
        page.Find("button.editor-nav__item--selected").TextContent.Replace("●", string.Empty).Trim();

    [Fact]
    public void TheFocusKey_OpensThePane_AndMarksTheCard()
    {
        var page = RenderEditor();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "results:consult";
        page.Render();

        Assert.Equal("Deliverables", SelectedNav(page));
        Assert.NotNull(page.Find("li.deliverable-card.deliverable-card--focused input[aria-label='Id for deliverable consult']"));
    }

    [Fact]
    public void TheMacroPanesDeliverableRow_LandsOnTheCard()
    {
        var page = RenderEditor();
        Navigate(page, "closing");

        page.Find(".macro-where [data-instance-deliverable='consult'] .macro-instance__owner").Click();

        Assert.Equal("results:consult", Services.GetRequiredService<WorkflowEditorSession>().SelectedKey);
        Assert.Equal("Deliverables", SelectedNav(page));
        Assert.NotNull(page.Find("li.deliverable-card.deliverable-card--focused[data-deliverable='consult']"));
    }

    [Fact]
    public void AnAggregatorsPane_LinksToItsDeliverable()
    {
        var page = RenderEditor();
        ShowNode(page, "all");

        var link = page.Find(".node-delivers button");
        Assert.Equal("Consultation note", link.TextContent.Trim());
        link.Click();

        Assert.NotNull(page.Find("li.deliverable-card.deliverable-card--focused[data-deliverable='consult']"));
        // A section node delivers nothing itself.
        ShowNode(page, "assessment");
        Assert.Empty(page.FindAll(".node-delivers"));
    }

    [Fact]
    public void TheCard_HasAHeaderStrip_AndRemoveInItsActions()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");

        Assert.Equal("Consultation note", page.Find(".deliverable-card__title").TextContent.Trim());
        var identity = Normalised(page.Find(".deliverable-card .pane-identity").TextContent);
        Assert.Contains("id consult", identity);
        Assert.Contains("delivered as consult-<job>.pdf", identity);
        var remove = page.Find(".deliverable-card .pane-header__actions fluent-button[aria-label='Remove deliverable']");
        Assert.True(remove.HasAttribute("disabled"));
        // The editable fields stay.
        Assert.NotNull(page.Find("input[aria-label='Id for deliverable consult']"));
        Assert.NotNull(page.Find("input[aria-label='Label for deliverable consult']"));
        Assert.NotNull(page.Find("fluent-select[aria-label='Node for deliverable consult']"));
    }

    [Fact]
    public void TheNodeSelect_HasALinkToTheAggregator()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");

        page.Find("fluent-button[aria-label='Open node all']").Click();

        Assert.Equal("Everything", SelectedNav(page));
    }

    [Fact]
    public void AMacroRow_LeadsWithItsLabel_AsALink()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");

        var label = page.Find("[data-v11-for='consult'] .result-macro__label");
        Assert.Equal("Closing", label.TextContent.Trim());
        Assert.Equal("Edit macro closing", label.GetAttribute("aria-label"));
        Assert.Empty(label.QuerySelectorAll("code"));
        Assert.Equal("attach a macro…", page.Find("fluent-select[aria-label='Append macro to deliverable consult'] fluent-option").TextContent.Trim());

        label.Click();

        Assert.Equal("closing", SelectedNav(page));
    }

    [Fact]
    public void ComposedAs_ListsTheOrder_WithGatesAndLinks()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");

        Assert.Equal("Composed as: Closing when input.kind == \"new\" → Assessment → Section notes", Normalised(page.Find(".deliverable-composed").TextContent));

        page.FindAll(".deliverable-composed button").First(b => b.TextContent.Trim() == "Assessment").Click();
        Assert.Equal("Assessment", SelectedNav(page));
    }

    [Fact]
    public void ComposedAs_EndsWithAppendedMacros_ThenTheSignature()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        var deliverable = root["deliverables"]!.AsArray()[0]!.AsObject();
        deliverable["signature"] = true;
        deliverable["macros"]!.AsArray().Add(new JsonObject { ["id"] = "tool_note", ["at"] = "append" });
        var page = RenderEditor(package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() });
        Navigate(page, "Deliverables");

        Assert.Equal("Composed as: Closing when input.kind == \"new\" → Assessment → Section notes → Tool note → signature", Normalised(page.Find(".deliverable-composed").TextContent));
    }

    [Fact]
    public void TheHelpExpander_IsAtThePanesFoot_At25Only()
    {
        var page = RenderEditor();
        Navigate(page, "Deliverables");

        var help = page.Find("details.pane-help");
        Assert.Contains("How deliverables work", help.TextContent);
        Assert.Contains("Delivered as", help.TextContent);
        Assert.True(page.Markup.IndexOf("New deliverable id", StringComparison.Ordinal) < page.Markup.IndexOf("How deliverables work", StringComparison.Ordinal));

        var below = RenderEditor(EditorFixtures.V12());
        Navigate(below, "Deliverables");
        Assert.Empty(below.FindAll("details.pane-help"));
        Assert.Empty(below.FindAll(".deliverable-card__header"));
        Assert.Empty(below.FindAll(".deliverable-composed"));
    }
}
