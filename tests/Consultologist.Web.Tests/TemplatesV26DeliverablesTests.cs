using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1161, design § 3 and § 12): the Deliverables card at 26 — the ORDERED SOURCES LIST
/// in place of the node select (arrows, remove, a one-shot "+ add source…" over what can
/// compose prose), "Composed as" naming the sources, "Sourced by deliverable 'd'" on a node
/// pane in place of "Delivers →", the gate keyed if (label only; written as if). Rendered
/// through the #1209 seam; a 25 card is pinned unchanged by the ≤25 suites.
/// </summary>
public class TemplatesV26DeliverablesTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>(parameters => parameters.Add(p => p.AuthorsAboveCeiling, true));
    }

    private IRenderedComponent<Templates> ShowDeliverables(WorkflowPackageContentResponse? fixture = null, string deliverableId = "consult")
    {
        var page = RenderEditor(fixture);
        Select(page, $"results:{deliverableId}");
        return page;
    }

    private void Select(IRenderedComponent<Templates> page, string key)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = key;
        page.Render();
    }

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"),
                Array.Empty<string>()));

    private static JsonElement Deliverable(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("deliverables").EnumerateArray()
            .Single(d => d.GetProperty("id").GetString() == id);

    private static IReadOnlyList<string> Sources(JsonElement deliverable) =>
        deliverable.GetProperty("sources").EnumerateArray().Select(s => s.GetString()!).ToList();

    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(sent!.Manifest.GetRawText(), PF.WorkflowPackageManifestJson.ReadOptions)!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    private static IReadOnlyList<string> SourceRows(IRenderedComponent<Templates> page) =>
        page.FindAll(".deliverable-source").Select(row => row.GetAttribute("data-source")!).ToList();

    private static IReadOnlyList<string> Offered(IRenderedComponent<Templates> page) =>
        page.Find("fluent-select.deliverable-source-add").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")!).Where(v => v.Length > 0).ToList();

    // ---- the card ----------------------------------------------------------------

    [Fact]
    public void TheCard_ListsTheSourcesInOrder_NoNodeSelect()
    {
        var page = ShowDeliverables();

        Assert.Equal(new[] { "assessment", "sections" }, SourceRows(page));
        Assert.Contains("Assessment", page.Find(".deliverable-source[data-source='assessment']").TextContent);
        Assert.Empty(page.FindAll("fluent-select[aria-label='Node for deliverable consult']"));
        Assert.Empty(page.FindAll(".deliverable-card__node-link"));
        // The picker offers what can compose prose and is not yet listed: nothing else in V26.
        Assert.Empty(Offered(page));
    }

    [Fact]
    public void ThePicker_OffersFlowNodesAndTextNodes_NeverBodyEnumObjectOrCheckNodes()
    {
        var page = ShowDeliverables(EditorFixtures.V26Flow());

        // V26Flow sources triage and sections; route is the one left (a case). extract (concept_list),
        // scope (classification), coverage (check), assessment/meds/section_notes (body nodes) are never offered.
        Assert.Equal(new[] { "triage", "sections" }, SourceRows(page));
        Assert.Equal(new[] { "route" }, Offered(page));
    }

    [Fact]
    public void MovingASourceDown_SwapsTheOrder_AndTheComposedPackageValidates()
    {
        CapturePublish();
        var page = ShowDeliverables();

        page.Find("fluent-button[aria-label='Move source 1 down of deliverable consult']").Click();
        Assert.Equal(new[] { "sections", "assessment" }, SourceRows(page));
        Publish(page);

        Assert.NotNull(sent);
        var consult = Deliverable(sent!, "consult");
        Assert.Equal(new[] { "node.sections", "node.assessment" }, Sources(consult));
        Assert.False(consult.TryGetProperty("node", out _));
        Assert.False(consult.TryGetProperty("when", out _));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void RemovingTheLoopSource_TheDeskSaysTheDeliverableNeedsALoop()
    {
        CapturePublish();
        var page = ShowDeliverables();

        page.Find("fluent-button[aria-label='Remove source 2 of deliverable consult']").Click();
        Assert.Equal(new[] { "assessment" }, SourceRows(page));
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Deliverable 'consult' must transitively include at least one loop"));
    }

    [Fact]
    public void RemovingEverySource_TheDeskSaysNamesNoSources()
    {
        CapturePublish();
        var page = ShowDeliverables();

        page.Find("fluent-button[aria-label='Remove source 1 of deliverable consult']").Click();
        page.Find("fluent-button[aria-label='Remove source 1 of deliverable consult']").Click();
        Assert.Contains("No sources yet", page.Find(".deliverable-sources").TextContent);
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Deliverable 'consult' names no sources"));
    }

    [Fact]
    public void AddingAFlowSource_AppendsIt_AndTheDeskJudgesItsRet()
    {
        CapturePublish();
        var page = ShowDeliverables(EditorFixtures.V26Flow());

        // route is offered (a flow node); its one branch returns meds, a medication_list — the
        // validator's sentence, not the picker, says so.
        page.Find("fluent-select.deliverable-source-add").Change("route");
        Assert.Equal(new[] { "triage", "sections", "route" }, SourceRows(page));
        Assert.Empty(Offered(page));
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Deliverable 'consult' source node.route returns 'meds' of format 'medication_list'; a deliverable composes prose."));
    }

    [Fact]
    public void RemovingThenReAddingASource_RoundTripsAndValidates()
    {
        CapturePublish();
        var page = ShowDeliverables();

        page.Find("fluent-button[aria-label='Remove source 1 of deliverable consult']").Click();
        Assert.Equal(new[] { "assessment" }, Offered(page));
        page.Find("fluent-select.deliverable-source-add").Change("assessment");
        Assert.Equal(new[] { "sections", "assessment" }, SourceRows(page));
        Publish(page);

        Assert.NotNull(sent);
        Assert.Equal(new[] { "node.sections", "node.assessment" }, Sources(Deliverable(sent!, "consult")));
        Assert.Empty(Validated().Errors);
    }

    // ---- Composed as --------------------------------------------------------------

    [Fact]
    public void ComposedAs_ListsPrependedMacros_TheSources_ThenTheSignature()
    {
        var page = ShowDeliverables();

        var composed = page.Find(".deliverable-composed").TextContent.Replace("\n", " ");
        composed = System.Text.RegularExpressions.Regex.Replace(composed, @"\s+", " ").Trim();
        // #1163: the macro's label is read from its file header at 26.
        Assert.Equal("Composed as: Closing when input.kind == \"new\" → Assessment → Sections", composed);

        page.Find("input[type='checkbox'][aria-label='Signed for deliverable consult']").Change(true);
        composed = System.Text.RegularExpressions.Regex.Replace(page.Find(".deliverable-composed").TextContent.Replace("\n", " "), @"\s+", " ").Trim();
        Assert.EndsWith("→ Sections → signature", composed);
    }

    [Fact]
    public void ComposedAs_ShowsAnIfSourcesCondition()
    {
        var page = ShowDeliverables(EditorFixtures.V26Flow());

        var composed = System.Text.RegularExpressions.Regex.Replace(page.Find(".deliverable-composed").TextContent.Replace("\n", " "), @"\s+", " ").Trim();
        Assert.Contains("Triage when input.kind == \"new\" → Sections", composed);
    }

    // ---- the gate reads if ----------------------------------------------------------

    [Fact]
    public void TheGate_ReadsIf_RestsAlways_AndWritesIf()
    {
        CapturePublish();
        var page = ShowDeliverables();

        var gate = page.Find("section[data-when-for='consult']");
        Assert.Equal("if", gate.QuerySelector(".deliverable-section__head")!.TextContent.Trim());
        Assert.Equal("Always", gate.QuerySelector(".condition-field__resting")!.TextContent.Trim());
        Assert.DoesNotContain("Produced when", page.Find(".deliverable-card").TextContent);

        // The deliverable macro's gate row reads if too, with the migrated condition.
        Assert.Contains("if", page.Find(".result-macro-when").TextContent);

        gate.QuerySelector(".condition-field__resting")!.Click();
        page.Find("section[data-when-for='consult'] fluent-select[aria-label='Condition operand for consult']").Change("kind");
        page.Find("section[data-when-for='consult'] fluent-select[aria-label='Condition value for consult']").Change("new");
        Publish(page);

        Assert.NotNull(sent);
        var consult = Deliverable(sent!, "consult");
        Assert.Equal("input.kind == \"new\"", consult.GetProperty("if").GetString());
        Assert.False(consult.TryGetProperty("when", out _));
        // The migrated macro entry keeps its if, never a when.
        var closing = consult.GetProperty("macros")[0];
        Assert.Equal("input.kind == \"new\"", closing.GetProperty("if").GetString());
        Assert.False(closing.TryGetProperty("when", out _));
        Assert.Empty(Validated().Errors);
    }

    // ---- Sourced by -------------------------------------------------------------------

    [Fact]
    public void ANodePane_SaysSourcedBy_NotDelivers()
    {
        var page = RenderEditor();

        Select(page, "node:assessment");
        Assert.Contains("Sourced by deliverable 'consult'", System.Text.RegularExpressions.Regex.Replace(page.Find(".node-sourced-by").TextContent, @"\s+", " "));
        Assert.Empty(page.FindAll(".node-delivers"));

        Select(page, "node:sections");
        Assert.NotNull(page.Find(".node-sourced-by"));
        page.Find(".node-sourced-by button").Click();
        Assert.Equal("results:consult", Services.GetRequiredService<WorkflowEditorSession>().SelectedKey);

        Select(page, "node:extract");
        Assert.Empty(page.FindAll(".node-sourced-by"));
    }

    // ---- + Deliverable -------------------------------------------------------------------

    [Fact]
    public void PlusDeliverable_IsBornWithNoSources_AndTheDeskNamesIt()
    {
        CapturePublish();
        var page = ShowDeliverables();

        page.Find("input[aria-label='New deliverable id']").Change("letter");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("+ Deliverable")).Click();

        Assert.Contains("No sources yet", page.Find(".deliverable-sources[data-sources-for='letter']").TextContent);
        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Deliverable 'letter' names no sources"));
    }

    // ---- 25 unchanged ----------------------------------------------------------------------

    [Fact]
    public void At25_TheNodeSelect_DeliversAndProducedWhen_Stay()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(EditorFixtures.V25());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        var page = Render<Templates>();
        Select(page, "results:consult");

        Assert.NotNull(page.Find("fluent-select[aria-label='Node for deliverable consult']"));
        Assert.Empty(page.FindAll(".deliverable-sources"));
        Assert.Equal("Produced when", page.Find("section[data-when-for='consult'] .deliverable-section__head").TextContent.Trim());

        Select(page, "node:all");
        Assert.NotNull(page.Find(".node-delivers"));
        Assert.Empty(page.FindAll(".node-sourced-by"));
    }
}
