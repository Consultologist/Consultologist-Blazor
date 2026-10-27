using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1160, design § 1 and § 12): the editor at 26 — a loop's pane (collection,
/// body, ret), the in-loop picker that replaces the for-each select on a plain
/// node, the add pane's "for (a loop)" in place of the aggregator, and the nav
/// nesting a body beneath its loop. The fixture is the V25 fixture through the real
/// migrator, so the loop is the migrator's: `sections` over snippet.sections, body
/// [section_notes], ret node.section_notes. The 25 fixture is pinned unchanged.
/// </summary>
public class TemplatesV26LoopPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26());
        WorkflowService.GetFormatsAsync().Returns(EditorFormatsCatalog.View());
        return Render<Templates>();
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
        page.FindAll("button.editor-nav__item")
            .First(button => button.TextContent.Replace("●", string.Empty).Trim() == label)
            .Click();

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

    private static JsonElement Node(WorkflowPackagePublishRequest request, string id) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement.GetProperty("nodes").EnumerateArray()
            .Single(n => n.GetProperty("id").GetString() == id);

    private static IReadOnlyList<string> Body(JsonElement loop) =>
        loop.GetProperty("nodes").EnumerateArray().Select(n => n.GetString()!).ToList();

    private static string? Ret(JsonElement loop) =>
        loop.TryGetProperty("ret", out var ret) ? ret.GetString() : null;

    /// <summary>The composed manifest against the REAL validator and the real formats catalogue.</summary>
    private PF.WorkflowPackageValidator.ValidationResult Validated()
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(
            sent!.Manifest.GetRawText(), PF.WorkflowPackageManifestJson.ReadOptions)!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            sent.Files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    // ---- the loop pane ----------------------------------------------------
    //
    // The desk runs the REAL validator on the composed manifest before any publish
    // call, so an invalid composition never reaches PublishPackageAsync: those
    // tests read the validator's own sentences off the desk, which name the exact
    // shape composed ("Loop 'sections' returns nothing", "Node 'meds' in loop
    // 'sections' is neither returned nor read"). A valid composition is captured.

    [Fact]
    public void LoopPane_ShowsCollectionBodyAndRet_NoFormatOrPromptSections()
    {
        var page = ShowNode("sections");

        Assert.Contains("loop over snippet.sections", page.Find(".pane-identity").TextContent);
        Assert.Equal("snippet.sections", page.Find("fluent-select.loop-collection").GetAttribute("current-value"));
        // The collection picker offers every folder in the 25 fan spelling (the fixture has one).
        Assert.Equal(new[] { "snippet.sections" }, page.Find("fluent-select.loop-collection").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToArray());

        var rows = page.FindAll(".loop-body__row");
        Assert.Single(rows);
        Assert.Contains("Section notes", rows[0].TextContent);
        Assert.Contains("returned", rows[0].TextContent);
        Assert.Equal("section_notes", page.Find("fluent-select.loop-ret").GetAttribute("current-value"));

        // Structural: no format picker, no in-loop picker, no gate, no prompt on a loop.
        Assert.Empty(page.FindAll("fluent-select.node-format"));
        Assert.Empty(page.FindAll("fluent-select.node-in-loop"));
        Assert.DoesNotContain("Runs when", page.Markup);
        Assert.DoesNotContain("Prompt text", page.Markup);
    }

    [Fact]
    public void LoopPane_ClearingRet_LeavesPublishOpen_AndTheDeskRefusesWithTheValidatorsSentence()
    {
        CapturePublish();
        var page = ShowNode("sections");

        page.Find("fluent-select.loop-ret").Change("");
        Assert.Equal(1, PendingCount(page.Instance));
        Assert.Equal("", page.Find("fluent-select.loop-ret").GetAttribute("current-value"));
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Loop 'sections' returns nothing; name the body node the outside reads with ret."));
    }

    [Fact]
    public void LoopPane_RePickingTheLoadedRet_SelfRemovesTheEdit()
    {
        var page = ShowNode("sections");

        page.Find("fluent-select.loop-ret").Change("");
        page.Find("fluent-select.loop-ret").Change("section_notes");

        Assert.Equal(0, PendingCount(page.Instance));
        Assert.Equal("section_notes", page.Find("fluent-select.loop-ret").GetAttribute("current-value"));
    }

    [Fact]
    public void LoopPane_RemoveFromLoop_TakesTheNodeOut_AndClearsARetThatNamedIt()
    {
        CapturePublish();
        var page = ShowNode("sections");

        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Remove from loop")).Click();

        Assert.Empty(page.FindAll(".loop-body__row"));
        Assert.Contains("No body yet", page.Markup);
        Assert.Equal("", page.Find("fluent-select.loop-ret").GetAttribute("current-value"));
        // The body node is top-level in the nav now.
        Assert.DoesNotContain(page.FindAll("button.editor-nav__item--nested"), b => b.TextContent.Contains("Section notes"));
        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.StartsWith("Loop 'sections'", StringComparison.Ordinal));
    }

    [Fact]
    public void LoopPane_AddANodeToTheBody_AppendsIt()
    {
        CapturePublish();
        var page = ShowNode("sections");

        page.Find("fluent-select.loop-body-add").Change("meds");

        var rows = page.FindAll(".loop-body__row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Medications", rows[1].TextContent);
        Assert.Contains(page.FindAll("button.editor-nav__item--nested"), b => b.TextContent.Contains("Medications"));
        Publish(page);
        // Composed into the body: the validator sees meds inside the loop.
        Assert.Contains(Refusals(page), r => r.Contains("Node 'meds' in loop 'sections' is neither returned nor read"));
    }

    // ---- the in-loop picker on a plain node ---------------------------------

    [Fact]
    public void PlainNodePane_At26_ShowsTheInLoopPicker_NotForEach()
    {
        var page = ShowNode("section_notes");

        Assert.Equal("sections", page.Find("fluent-select.node-in-loop").GetAttribute("current-value"));
        Assert.Empty(page.FindAll("fluent-select[aria-label='Node forEach collection']"));
    }

    [Fact]
    public void InLoopPicker_MovingANodeOut_RemovesItFromTheBody_AndClearsTheRet()
    {
        CapturePublish();
        var page = ShowNode("section_notes");

        page.Find("fluent-select.node-in-loop").Change("");
        Assert.Equal("", page.Find("fluent-select.node-in-loop").GetAttribute("current-value"));
        ShowNode(page, "sections");
        Assert.Empty(page.FindAll(".loop-body__row"));
        Assert.Equal("", page.Find("fluent-select.loop-ret").GetAttribute("current-value"));
        Publish(page);
        Assert.Contains(Refusals(page), r => r.StartsWith("Loop 'sections'", StringComparison.Ordinal));
    }

    [Fact]
    public void InLoopPicker_MovingANodeIn_AppendsItToTheBody()
    {
        CapturePublish();
        var page = ShowNode("meds");

        page.Find("fluent-select.node-in-loop").Change("sections");
        ShowNode(page, "sections");
        var rows = page.FindAll(".loop-body__row");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Medications", rows[1].TextContent);
        Publish(page);
        Assert.Contains(Refusals(page), r => r.Contains("Node 'meds' in loop 'sections' is neither returned nor read"));
    }

    [Fact]
    public void TheReader_TypesABodyNodesFanThroughItsLoop()
    {
        var nodes = WorkflowManifestReader.ReadNodes(EditorFixtures.V26().Manifest);
        var loop = nodes.Single(n => n.Id == "sections");
        var body = nodes.Single(n => n.Id == "section_notes");

        Assert.True(loop.IsLoop);
        Assert.Equal("snippet.sections", loop.Collection);
        Assert.Equal("section_notes", loop.RetNodeId);
        Assert.Null(body.ForEach);
        Assert.Same(loop, WorkflowManifestReader.NodeView.LoopOf(nodes, "section_notes"));
        Assert.Equal("snippet.sections", WorkflowManifestReader.NodeView.EffectiveFan(body, nodes));
        Assert.Null(WorkflowManifestReader.NodeView.EffectiveFan(nodes.Single(n => n.Id == "meds"), nodes));
        // A 25 node answers its own forEach.
        var at25 = WorkflowManifestReader.ReadNodes(EditorFixtures.V25().Manifest);
        Assert.Equal("snippet.sections", WorkflowManifestReader.NodeView.EffectiveFan(at25.Single(n => n.Id == "section_notes"), at25));
    }

    // ---- the nav ----------------------------------------------------------

    [Fact]
    public void Nav_NestsTheBodyBeneathTheLoop()
    {
        var page = RenderEditor();

        var items = page.FindAll("button.editor-nav__item").ToList();
        var loopIndex = items.FindIndex(b => b.TextContent.Contains("Sections") && !b.TextContent.Contains("Section notes"));
        var bodyIndex = items.FindIndex(b => b.TextContent.Contains("Section notes"));
        Assert.True(loopIndex >= 0 && bodyIndex == loopIndex + 1, $"loop at {loopIndex}, body at {bodyIndex}");
        Assert.Contains("editor-nav__item--nested", items[bodyIndex].ClassName);
        Assert.DoesNotContain("editor-nav__item--nested", items[loopIndex].ClassName);
    }

    // ---- the add pane -------------------------------------------------------

    [Fact]
    public void PlusNode_At26_OffersALoop_NeverTheAggregator()
    {
        var page = RenderEditor();
        Navigate(page, "+ Node");

        var offered = page.Find("fluent-select.new-node-format").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal("", offered[0]);
        Assert.Equal("for", offered[1]);
        Assert.DoesNotContain("aggregator", offered);
        Assert.Contains("prose", offered);
        Assert.Empty(page.FindAll("fluent-select[aria-label='New node forEach']"));
        Assert.Equal(new[] { "", "sections" }, page.Find("fluent-select.new-node-loop").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToArray());
    }

    [Fact]
    public void PlusNode_At26_ALoop_NeedsItsCollection_ThenComposesWithoutRet()
    {
        CapturePublish();
        var page = RenderEditor();
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("for");
        Assert.Empty(page.FindAll("fluent-select.new-node-loop"));
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("standards_loop");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Standards loop");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();
        Assert.Contains(page.FindAll(".fluent-messagebar-message"), bar => bar.TextContent.Contains("Choose what the loop iterates."));

        page.Find("fluent-select.new-loop-collection").Change("snippet.sections");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();

        // Lands on the loop's pane: no body, no ret; the desk names both.
        Assert.Contains("loop over snippet.sections", page.Find(".pane-identity").TextContent);
        Assert.Contains("No body yet", page.Markup);
        Assert.Equal("", page.Find("fluent-select.loop-ret").GetAttribute("current-value"));
        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.StartsWith("Loop 'standards_loop'", StringComparison.Ordinal));
    }

    [Fact]
    public void PlusNode_At26_APlainNodeInALoop_JoinsItsBody_AndAValidLoopPublishes()
    {
        CapturePublish();
        var page = RenderEditor();
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("prose");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("section_summary");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Section summary");
        page.Find("fluent-select.new-node-loop").Change("sections");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();
        page.Find("fluent-text-area").Change("Summarise {{ node.section_notes.content }} for {{ item.label }}.");

        // Return the new node instead: section_notes is read by it, so the loop is whole.
        ShowNode(page, "sections");
        Assert.Equal(2, page.FindAll(".loop-body__row").Count);
        page.Find("fluent-select.loop-ret").Change("section_summary");
        Publish(page);

        Assert.NotNull(sent);
        var loop = Node(sent!, "sections");
        Assert.Equal(new[] { "section_notes", "section_summary" }, Body(loop));
        Assert.Equal("node.section_summary", Ret(loop));
        Assert.False(Node(sent!, "section_summary").TryGetProperty("forEach", out _));
        Assert.Empty(Validated().Errors);
    }

    // ---- 25 unchanged -------------------------------------------------------

    [Fact]
    public void At25_TheForEachSelect_TheFlatNav_AndTheAggregatorChoice_AreUnchanged()
    {
        var page = ShowNode("section_notes", EditorFixtures.V25());

        Assert.NotNull(page.Find("fluent-select[aria-label='Node forEach collection']"));
        Assert.Empty(page.FindAll("fluent-select.node-in-loop"));
        Assert.DoesNotContain(page.FindAll("button.editor-nav__item--nested"), b => b.TextContent.Contains("Section notes"));

        Navigate(page, "+ Node");
        var offered = page.Find("fluent-select.new-node-format").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal("aggregator", offered[1]);
        Assert.DoesNotContain("for", offered);
        Assert.Empty(page.FindAll("fluent-select.new-node-loop"));
    }

    private static int PendingCount(Templates editor) =>
        (int)typeof(Templates).GetProperty("PendingCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.GetValue(editor)!;
}
