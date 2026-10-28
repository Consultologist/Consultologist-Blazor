using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// v26 (#1159, design § 1 and § 12): the editor's if and case panes — the condition
/// builder on an if, the branch rows with value chips on a case, a body section with
/// its ret picker per then/else/branch, the ONE placement picker over every body, the
/// add pane's three flow choices, "Runs when" retired at 26. The fixture is V26Flow:
/// the migrated V25 fixture plus `triage` (if over input.kind, then [assessment]) and
/// `route` (case over node.scope.value, branch [in_scope] → [meds]). Rendered through the
/// #1209 seam. The desk runs the REAL validator before any publish call, so invalid
/// compositions are read off its sentences; valid ones are captured.
/// </summary>
public class TemplatesV26FlowPaneTests : ClientRenderTestContext
{
    private IRenderedComponent<Templates> RenderEditor(WorkflowPackageContentResponse? fixture = null)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(fixture ?? EditorFixtures.V26Flow());
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

    private static IReadOnlyList<string> Ids(JsonElement array) => array.EnumerateArray().Select(n => n.GetString()!).ToList();

    private static PF.WorkflowPackageValidator.ValidationResult Validate(JsonElement manifestJson, IReadOnlyDictionary<string, string> files)
    {
        var manifest = JsonSerializer.Deserialize<PF.WorkflowPackageManifest>(manifestJson.GetRawText(), PF.WorkflowPackageManifestJson.ReadOptions)!;
        return PF.WorkflowPackageValidator.Validate(
            manifest,
            files.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal),
            null,
            EditorFormatsCatalog.Catalog);
    }

    private PF.WorkflowPackageValidator.ValidationResult Validated() => Validate(sent!.Manifest, sent.Files);

    private static int PendingCount(Templates editor) =>
        (int)typeof(Templates).GetProperty("PendingCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!.GetValue(editor)!;

    // ---- the fixture is a real 26 package ----------------------------------

    [Fact]
    public void TheFlowFixture_IsValidAt26()
    {
        var fixture = EditorFixtures.V26Flow();
        Assert.Empty(Validate(fixture.Manifest, fixture.Files).Errors);
    }

    [Fact]
    public void TheReader_ReadsAnIfAndACase_AndAddressesTheirBodies()
    {
        var nodes = WorkflowManifestReader.ReadNodes(EditorFixtures.V26Flow().Manifest);
        var triage = nodes.Single(n => n.Id == "triage");
        var route = nodes.Single(n => n.Id == "route");

        Assert.True(triage.IsIf && triage.IsFlow && !triage.IsLoop);
        Assert.Equal("input.kind == \"new\"", triage.If);
        Assert.Equal(new[] { "assessment" }, triage.Then!.Nodes);
        Assert.Equal("assessment", triage.Then.RetNodeId);
        Assert.Null(triage.Else);
        Assert.True(route.IsCase);
        Assert.Equal("node.scope.value", route.Case);
        Assert.Equal(new[] { "in_scope" }, route.Branches![0].When);
        Assert.Equal("meds", route.Branches[0].RetNodeId);

        Assert.Equal(new[] { "triage/then" }, triage.Bodies().Select(b => b.Key));
        Assert.Equal("Triage · then", triage.Bodies()[0].Label);
        Assert.Equal(new[] { "route/branch:0" }, route.Bodies().Select(b => b.Key));
        Assert.Equal("Route · in_scope", route.Bodies()[0].Label);
        Assert.Equal("triage/then", WorkflowManifestReader.NodeView.BodyOf(nodes, "assessment")!.Key);
        Assert.Equal("sections", WorkflowManifestReader.NodeView.BodyOf(nodes, "section_notes")!.Key);
        Assert.Null(WorkflowManifestReader.NodeView.BodyOf(nodes, "extract"));
        Assert.Equal(new WorkflowManifestReader.FlowBodyRef("route", "branch:0"), WorkflowManifestReader.FlowBodyRef.Parse("route/branch:0"));
        Assert.Equal(0, WorkflowManifestReader.FlowBodyRef.Parse("route/branch:0").BranchIndex);
    }

    // ---- the if pane --------------------------------------------------------

    [Fact]
    public void IfPane_ShowsTheCondition_TheThenBody_AndNoElse()
    {
        var page = ShowNode("triage");

        Assert.Contains("if · input.kind == \"new\"", page.Find(".pane-identity").TextContent);
        Assert.NotNull(page.Find(".node-if-editor"));
        var then = page.Find(".flow-body[data-body='triage/then']");
        var rows = then.QuerySelectorAll(".loop-body__row");
        Assert.Single(rows);
        Assert.Contains("Assessment", rows[0].TextContent);
        Assert.Contains("returned", rows[0].TextContent);
        Assert.Equal("assessment", then.QuerySelector("fluent-select.loop-ret")!.GetAttribute("current-value"));
        Assert.Empty(page.FindAll(".flow-body[data-body='triage/else']"));
        Assert.NotNull(page.Find("fluent-button.flow-add-else"));
        Assert.DoesNotContain("Runs when", page.Markup);
        Assert.Empty(page.FindAll("fluent-select.node-format"));
    }

    [Fact]
    public void IfPane_ClearingTheThenRet_TheDeskSaysThenReturnsNothing()
    {
        CapturePublish();
        var page = ShowNode("triage");

        page.Find(".flow-body[data-body='triage/then'] fluent-select.loop-ret").Change("");
        Assert.Equal(1, PendingCount(page.Instance));
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Node 'triage' then returns nothing"));
    }

    [Fact]
    public void IfPane_AddingAnElse_ANode_AndItsRet_ComposesElse_AndValidates()
    {
        CapturePublish();
        var page = ShowNode("triage");

        page.Find("fluent-button.flow-add-else").Click();
        Assert.Contains("No body yet", page.Find(".flow-body[data-body='triage/else']").TextContent);

        // A fresh prose node born straight into the else body, then returned by it.
        Navigate(page, "+ Node");
        page.Find("fluent-select.new-node-format").Change("prose");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("brief_update");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Brief update");
        page.Find("fluent-select.new-node-loop").Change("triage/else");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();
        page.Find("fluent-text-area").Change("Brief update for {{ input.notes }}.");
        ShowNode(page, "triage");
        Assert.Single(page.FindAll(".flow-body[data-body='triage/else'] .loop-body__row"));
        page.Find(".flow-body[data-body='triage/else'] fluent-select.loop-ret").Change("brief_update");
        Publish(page);

        Assert.NotNull(sent);
        var triage = Node(sent!, "triage");
        Assert.Equal(new[] { "brief_update" }, Ids(triage.GetProperty("else").GetProperty("nodes")));
        Assert.Equal("node.brief_update", triage.GetProperty("else").GetProperty("ret").GetString());
        Assert.Equal(new[] { "assessment" }, Ids(triage.GetProperty("then").GetProperty("nodes")));
        Assert.Equal("prompts/brief_update.md", Node(sent!, "brief_update").GetProperty("prompt").GetString());
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void IfPane_RemovingTheElse_DropsIt_AndItsNodesFallOut()
    {
        var page = ShowNode("triage");

        page.Find("fluent-button.flow-add-else").Click();
        page.Find(".flow-body[data-body='triage/else'] fluent-select.loop-body-add").Change("extract");
        page.Find("fluent-button.flow-remove-else").Click();

        Assert.Empty(page.FindAll(".flow-body[data-body='triage/else']"));
        Assert.Equal(0, PendingCount(page.Instance));
        ShowNode(page, "extract");
        Assert.Equal("", page.Find("fluent-select.node-in-loop").GetAttribute("current-value"));
    }

    // ---- the case pane ------------------------------------------------------

    [Fact]
    public void CasePane_ShowsTheSubject_TheBranchChip_ItsBody_AndTheUnroutedValues()
    {
        var page = ShowNode("route");

        Assert.Contains("case · over node.scope.value", page.Find(".pane-identity").TextContent);
        Assert.Equal("node.scope.value", page.Find("fluent-select.case-subject").GetAttribute("current-value"));
        var branch = page.Find(".case-branch[data-branch='0']");
        Assert.Contains("in_scope", branch.QuerySelector(".case-branch__chip")!.TextContent);
        Assert.Contains("Medications", branch.QuerySelector(".loop-body__row")!.TextContent);
        Assert.Equal("meds", branch.QuerySelector("fluent-select.loop-ret")!.GetAttribute("current-value"));
        Assert.Contains("Not routed: out_of_scope", page.Find(".case-unrouted").TextContent);
        // The add-value one-shot offers only the unrouted values.
        Assert.Equal(new[] { "", "out_of_scope" }, branch.QuerySelector("fluent-select.case-branch__add-value")!.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToArray());
    }

    [Fact]
    public void CasePane_AddingAValueChip_WritesWhenWithBothValues_AndValidates()
    {
        CapturePublish();
        var page = ShowNode("route");

        page.Find(".case-branch[data-branch='0'] fluent-select.case-branch__add-value").Change("out_of_scope");
        Assert.Equal(2, page.FindAll(".case-branch[data-branch='0'] .case-branch__chip").Count);
        Assert.Empty(page.FindAll(".case-unrouted"));
        Publish(page);

        Assert.NotNull(sent);
        var route = Node(sent!, "route");
        Assert.Equal(new[] { "in_scope", "out_of_scope" }, Ids(route.GetProperty("branches")[0].GetProperty("when")));
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void CasePane_DroppingAChip_ThenAddingItBack_SelfRemovesTheEdit()
    {
        var page = ShowNode("route");

        page.FindAll("fluent-button.case-branch__chip-remove").First().Click();
        Assert.Contains("lists no value yet", page.Find(".case-branch[data-branch='0']").TextContent);
        Assert.Equal(1, PendingCount(page.Instance));
        page.Find(".case-branch[data-branch='0'] fluent-select.case-branch__add-value").Change("in_scope");

        Assert.Equal(0, PendingCount(page.Instance));
    }

    [Fact]
    public void CasePane_ANewBranch_WithANodeAndItsRet_Validates()
    {
        CapturePublish();
        var page = ShowNode("route");

        page.Find("fluent-button.case-add-branch").Click();
        Assert.Equal(2, page.FindAll(".case-branch").Count);
        page.Find(".case-branch[data-branch='1'] fluent-select.case-branch__add-value").Change("out_of_scope");

        // A fresh prose node born into branch 2, then returned by it — the branches then
        // disagree on format (medication_list vs prose), which the outside's node.route.content read allows.
        Navigate(page, "+ Node");
        page.Find("fluent-select.new-node-format").Change("prose");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("letter");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Letter");
        page.Find("fluent-select.new-node-loop").Change("route/branch:1");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();
        page.Find("fluent-text-area").Change("A letter for {{ input.notes }}.");
        ShowNode(page, "route");
        page.Find(".flow-body[data-body='route/branch:1'] fluent-select.loop-ret").Change("letter");
        Publish(page);

        Assert.NotNull(sent);
        var route = Node(sent!, "route");
        var branches = route.GetProperty("branches");
        Assert.Equal(2, branches.GetArrayLength());
        Assert.Equal(new[] { "out_of_scope" }, Ids(branches[1].GetProperty("when")));
        Assert.Equal(new[] { "letter" }, Ids(branches[1].GetProperty("nodes")));
        Assert.Equal("node.letter", branches[1].GetProperty("ret").GetString());
        Assert.Empty(Validated().Errors);
    }

    [Fact]
    public void CasePane_AnEmptyNewBranch_TheDeskSaysItListsNoValue()
    {
        CapturePublish();
        var page = ShowNode("route");

        page.Find("fluent-button.case-add-branch").Click();
        Publish(page);

        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Node 'route' branch 2 lists no value"));
    }

    // ---- the one placement picker -------------------------------------------

    [Fact]
    public void ThePlacementPicker_OffersEveryBody_AndMovesANodeFromTheLoopIntoTheThen()
    {
        CapturePublish();
        var page = ShowNode("section_notes");

        var picker = page.Find("fluent-select.node-in-loop");
        Assert.Equal("sections", picker.GetAttribute("current-value"));
        var offered = picker.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToArray();
        Assert.Equal(new[] { "", "triage/then", "route/branch:0", "sections" }, offered);

        picker.Change("triage/then");
        ShowNode(page, "triage");
        Assert.Equal(2, page.FindAll(".flow-body[data-body='triage/then'] .loop-body__row").Count);
        ShowNode(page, "sections");
        Assert.Empty(page.FindAll(".loop-body__row"));
        Assert.Equal("", page.Find("fluent-select.loop-ret").GetAttribute("current-value"));
        Publish(page);
        // The loop lost its body and its ret; the then holds both nodes.
        Assert.Contains(Refusals(page), r => r.StartsWith("Loop 'sections'", StringComparison.Ordinal));
    }

    [Fact]
    public void Nav_NestsThenAndBranchRows_WithTheirTags()
    {
        var page = RenderEditor();

        var items = page.FindAll("button.editor-nav__item").ToList();
        var triage = items.FindIndex(b => b.TextContent.Contains("Triage"));
        var assessment = items.FindIndex(b => b.TextContent.Contains("Assessment"));
        Assert.Equal(triage + 1, assessment);
        Assert.Contains("editor-nav__item--nested", items[assessment].ClassName);
        Assert.Contains("· then", items[assessment].TextContent);
        var route = items.FindIndex(b => b.TextContent.Contains("Route"));
        var meds = items.FindIndex(b => b.TextContent.Contains("Medications"));
        Assert.Equal(route + 1, meds);
        Assert.Contains("· in_scope", items[meds].TextContent);
    }

    // ---- the add pane ---------------------------------------------------------

    [Fact]
    public void PlusNode_At26_OffersForIfAndCase_NeverTheAggregator()
    {
        var page = RenderEditor();
        Navigate(page, "+ Node");

        var offered = page.Find("fluent-select.new-node-format").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.Equal(new[] { "", "for", "if", "case" }, offered.Take(4));
        Assert.DoesNotContain("aggregator", offered);
        Assert.Equal(new[] { "", "triage/then", "route/branch:0", "sections" }, page.Find("fluent-select.new-node-loop").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToArray());
    }

    [Fact]
    public void PlusNode_AnIf_IsBornWithoutACondition_AndTheDeskNamesIt()
    {
        CapturePublish();
        var page = RenderEditor();
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("if");
        Assert.Empty(page.FindAll("fluent-select.new-node-loop"));
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("followup_gate");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Follow-up gate");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();

        Assert.Contains("if · no condition yet", page.Find(".pane-identity").TextContent);
        Assert.NotNull(page.Find(".flow-body[data-body='followup_gate/then']"));
        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("'followup_gate'"));
    }

    [Fact]
    public void PlusNode_ACase_NeedsItsSubject_ThenIsBornWithNoBranches()
    {
        CapturePublish();
        var page = RenderEditor();
        Navigate(page, "+ Node");

        page.Find("fluent-select.new-node-format").Change("case");
        page.Find("fluent-text-field[placeholder='summarize-guidelines']").Change("kind_switch");
        page.Find("fluent-text-field[placeholder='Summarizing guidelines']").Change("Kind switch");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();
        Assert.Contains(page.FindAll(".fluent-messagebar-message"), bar => bar.TextContent.Contains("Choose what the case switches on."));

        var subjects = page.Find("fluent-select.new-case-subject").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToArray();
        Assert.Equal(new[] { "", "input.kind", "node.scope.value" }, subjects);
        page.Find("fluent-select.new-case-subject").Change("input.kind");
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Create")).Click();

        Assert.Contains("case · over input.kind", page.Find(".pane-identity").TextContent);
        Assert.Contains("No branches yet", page.Markup);
        Publish(page);
        Assert.Null(sent);
        Assert.Contains(Refusals(page), r => r.Contains("Node 'kind_switch' declares case without branches"));
    }

    // ---- 25 unchanged -----------------------------------------------------------

    [Fact]
    public void At25_RunsWhenStays_AndTheAddPaneOffersNoFlowNode()
    {
        var page = ShowNode("assessment", EditorFixtures.V25());

        Assert.Contains("Runs when", page.Markup);
        Navigate(page, "+ Node");
        var offered = page.Find("fluent-select.new-node-format").QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();
        Assert.DoesNotContain("if", offered);
        Assert.DoesNotContain("case", offered);
    }
}
