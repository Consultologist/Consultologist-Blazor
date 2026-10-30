using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v27 (#1236, package-format-v27-design.md § 6): the editor at 27 — a loop's body holds an
/// if/case (the loop pane lists it per item and returns it; its add offers an unplaced one),
/// the if pane's "in" picker places it in a loop, the nav nests two levels, the + Node form
/// births an if in a loop, and the desk says the 27 sentences — all through the test-only
/// seam (AuthorsAboveCeiling) until the bump (#1237). Without the seam a 27 package meets
/// the accepted-set sentence.
/// </summary>
public class TemplatesV27LoopPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null, bool seam = true)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V27Flow());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>(parameters => parameters.Add(p => p.AuthorsAboveCeiling, seam));
    }

    private IRenderedComponent<Templates> ShowNode(string nodeId, WorkflowPackageContentResponse? fixture = null)
    {
        var page = RenderEditor(fixture);
        ShowNode(page, nodeId);
        return page;
    }

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private static void Navigate(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item").First(button => button.TextContent.Replace("●", string.Empty).Trim() == label).Click();

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.10.2", "acct-1234567890ab@v2026.10.2"), Array.Empty<string>()));

    /// <summary>The fixture with its manifest rewritten in place.</summary>
    private static WorkflowPackageContentResponse With(WorkflowPackageContentResponse fixture, Action<System.Text.Json.Nodes.JsonObject> change)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(fixture.Manifest.GetRawText())!.AsObject();
        change(root);
        return new(fixture.Name, fixture.Version, fixture.SpecVersion, System.Text.Json.JsonDocument.Parse(root.ToJsonString()).RootElement.Clone(), fixture.Files);
    }

    private static System.Text.Json.Nodes.JsonObject NodeOf(System.Text.Json.Nodes.JsonObject root, string id) =>
        root["nodes"]!.AsArray().First(n => n!["id"]!.GetValue<string>() == id)!.AsObject();

    /// <summary>Publish wants a pending edit: the if's label, nudged.</summary>
    private void Nudge(IRenderedComponent<Templates> page)
    {
        ShowNode(page, "triage");
        page.Find("fluent-text-field[aria-label='If label']").Change("Triage (edited)");
    }

    [Fact]
    public void A27Package_PublishesClean_ThroughTheSeam_AndMeetsTheVersionSentence_Without()
    {
        CapturePublish();
        var page = RenderEditor();
        Nudge(page);
        Publish(page);
        Assert.Empty(Refusals(page));
        Assert.NotNull(sent);

        sent = null;
        var bare = RenderEditor(seam: false);
        Nudge(bare);
        Publish(bare);
        Assert.Null(sent);
        // The manifest read's sentence — the public accepted set, the bump's to move.
        Assert.Contains(Refusals(bare), r => r.Contains("is specVersion 27; this engine runs specVersion", StringComparison.Ordinal));
    }

    [Fact]
    public void LoopPane_ListsTheInnerIf_PerItem_AndReturnsIt()
    {
        var page = ShowNode("sections");

        var rows = page.FindAll(".loop-body__row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Triage", rows[1].TextContent);
        Assert.Equal("· if item.id == \"hpi\" — per item", rows[1].QuerySelector(".loop-body__flow")!.TextContent.Trim());
        Assert.Contains("· returned", rows[1].TextContent);
        Assert.Equal("triage", page.Find("fluent-select.loop-ret").GetAttribute("current-value"));
    }

    [Fact]
    public void LoopPane_AddOffersAnUnplacedIf_AndPlacesIt()
    {
        CapturePublish();
        var page = ShowNode("sections", EditorFixtures.V27Flow(inLoop: false));

        page.Find("fluent-select.loop-body-add").Change("triage");

        var rows = page.FindAll(".loop-body__row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Triage", rows[1].TextContent);
        Assert.Contains(page.FindAll("button.editor-nav__item--nested"), b => b.TextContent.Contains("Triage"));
        Publish(page);
        // Composed into the loop: the validator judges the if inside it (its branches now read no item — they read inputs — but the if is neither returned nor read).
        Assert.Contains(Refusals(page), r => r.Contains("Node 'triage' in loop 'sections' is neither returned nor read"));
    }

    [Fact]
    public void IfPane_ShowsTheInLoopPicker_AndMovingOut_RemovesItFromTheLoop()
    {
        var page = ShowNode("triage");

        var picker = page.Find("fluent-select.flow-in-loop");
        Assert.Equal("sections", picker.GetAttribute("current-value"));
        var targets = picker.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal(new[] { "", "sections" }, targets);

        picker.Change("");

        ShowNode(page, "sections");
        Assert.Single(page.FindAll(".loop-body__row"));
        Assert.Equal("", page.Find("fluent-select.loop-ret").GetAttribute("current-value") ?? string.Empty);
    }

    [Fact]
    public void Nav_NestsTheInnerIf_UnderTheLoop_AndItsBranchNodesDeeper_Once()
    {
        var page = RenderEditor();

        var items = page.FindAll("button.editor-nav__item").ToList();
        Assert.Single(items, b => b.TextContent.Contains("Triage"));
        var sections = items.FindIndex(b => b.TextContent.Contains("Sections"));
        var triage = items.FindIndex(b => b.TextContent.Contains("Triage"));
        var patient = items.FindIndex(b => b.TextContent.Contains("Patient draft"));
        var table = items.FindIndex(b => b.TextContent.Contains("Table draft"));
        Assert.True(sections < triage && triage == patient - 1 && patient == table - 1);
        Assert.Contains("editor-nav__item--nested", items[triage].ClassName);
        Assert.DoesNotContain("editor-nav__item--nested-2", items[triage].ClassName);
        Assert.Contains("· if", items[triage].TextContent);
        Assert.Contains("editor-nav__item--nested-2", items[patient].ClassName);
        Assert.Contains("· then", items[patient].TextContent);
        Assert.Contains("· else", items[table].TextContent);
    }

    [Fact]
    public void PlusNode_AnIf_BornInALoop_JoinsItsBody()
    {
        CapturePublish();
        var page = RenderEditor(EditorFixtures.V27Flow(inLoop: false));
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("if");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("gate");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Gate");
        var loops = page.Find("fluent-select.new-node-loop").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal(new[] { "", "sections" }, loops);
        page.Find("fluent-select.new-node-loop").Change("sections");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();

        ShowNode(page, "sections");
        Assert.Contains(page.FindAll(".loop-body__row"), row => row.TextContent.Contains("Gate"));
    }

    [Fact]
    public void TheDesk_SaysTheSentencesOf27()
    {
        CapturePublish();
        var attachedTwice = With(EditorFixtures.V27Flow(), root => NodeOf(root, "sections")["nodes"]!.AsArray().Add("patient_draft"));
        var page = RenderEditor(attachedTwice);
        Nudge(page);
        Publish(page);
        Assert.Contains("Node 'patient_draft' is attached to 'triage' and to loop 'sections'; a branch node is attached once, through its flow node.", Refusals(page));

        var readsLoop = With(EditorFixtures.V27Flow(), root => NodeOf(root, "triage")["if"] = "node.sections.content != \"\"");
        var loopPage = RenderEditor(readsLoop);
        Nudge(loopPage);
        Publish(loopPage);
        Assert.Contains("Node 'triage' if reads node.sections, the loop it runs in; a condition inside a loop reads the item, inputs and earlier body nodes.", Refusals(loopPage));
    }
}
