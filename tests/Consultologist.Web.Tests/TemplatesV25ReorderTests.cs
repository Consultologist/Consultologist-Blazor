using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using PF = Consultologist.PackageFormat;

namespace Consultologist.Web.Tests;

/// <summary>
/// #1103 (Milestone 31): reorder where order is a fact. A snippet folder's items
/// (index.json's array — the fan's run order and the {{ for }} iteration order) move
/// from the nav rows' arrows through a self-reverting per-folder overlay written into
/// index.json on publish, in the ledger as "N folder orders", in the draft, with a
/// "(reset order)" link in the folder; the fan-item selects and the preview pick follow
/// the order; an added item sits last until moved; the ≤24 data folder moves the same
/// way. The deliverables (the card's header strip) and the inputs (the row, beside
/// Remove — the Create page's form order) move in their pending lists.
/// </summary>
public class TemplatesV25ReorderTests : ClientRenderTestContext
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

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static void AddItem(IRenderedComponent<Templates> page, string id, string name, string text)
    {
        var fields = page.FindAll(".new-item-fields fluent-text-field");
        fields[0].Change(id);
        fields[1].Change(name);
        page.Find(".pane-editor fluent-text-area").Change(text);
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("Add to")).Click();
    }

    private static IElement Folder(IRenderedComponent<Templates> page, string id) =>
        page.FindAll("details.editor-nav__folder").First(d => d.QuerySelector("summary")!.TextContent.Contains($"{id} ("));

    /// <summary>The folder's live rows in the nav's order (a removed row is struck, not a row).</summary>
    private static IReadOnlyList<string> FolderOrder(IRenderedComponent<Templates> page, string id) =>
        Folder(page, id).QuerySelectorAll(".editor-nav__row button.editor-nav__item")
            .Select(b => b.TextContent.Replace("●", string.Empty).Trim()).ToList();

    private static string FolderDot(IRenderedComponent<Templates> page, string id) =>
        Folder(page, id).QuerySelector("summary .editor-nav__dot")!.TextContent.Trim();

    private static IElement Up(IRenderedComponent<Templates> page, string name) =>
        page.Find($"fluent-button[aria-label=\"Move item {name} up\"]");

    private static IElement Down(IRenderedComponent<Templates> page, string name) =>
        page.Find($"fluent-button[aria-label=\"Move item {name} down\"]");

    private static string Dot(IRenderedComponent<Templates> page, string label) =>
        page.FindAll("button.editor-nav__item").First(b => b.TextContent.Contains(label))
            .QuerySelector(".editor-nav__dot")!.TextContent.Trim();

    private static IReadOnlyList<string?> OptionValues(IElement select) =>
        select.QuerySelectorAll("fluent-option").Select(o => o.GetAttribute("value")).ToList();

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

    private static IReadOnlyList<string?> IndexIds(WorkflowPackagePublishRequest request, string path) =>
        JsonDocument.Parse(request.Files[path]).RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("id").GetString()).ToList();

    private static JsonElement Manifest(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement;

    private static IReadOnlyList<string?> Ids(JsonElement manifest, string array) =>
        manifest.GetProperty(array).EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToList();

    private IRenderedComponent<Templates> Reload()
    {
        var saved = JSInterop.Invocations["localStorage.setItem"]
            .Where(i => i.Arguments[0] is string key && key.StartsWith("workflow-editor-draft:", StringComparison.Ordinal))
            .Select(i => (Key: (string)i.Arguments[0]!, Json: (string)i.Arguments[1]!))
            .Last();
        JSInterop.Setup<string?>("localStorage.getItem", saved.Key).SetResult(saved.Json);
        return Render<Templates>();
    }

    /// <summary>The V25 package with a second aggregator and a second deliverable on it.</summary>
    private static WorkflowPackageContentResponse V25WithTwoDeliverables()
    {
        var package = EditorFixtures.V25();
        var root = JsonNode.Parse(package.Manifest.GetRawText())!.AsObject();
        root["nodes"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "all_2",
            ["label"] = "Letter body",
            // A deliverable must reach a fan: the second aggregator takes the fanning node too.
            ["aggregate"] = new JsonArray("node.section_notes"),
        });
        root["deliverables"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "letter",
            ["node"] = "node.all_2",
            ["label"] = "Letter",
        });
        return package with { Manifest = JsonDocument.Parse(root.ToJsonString()).RootElement.Clone() };
    }

    // ----- a snippet folder's items -----

    [Fact]
    public void MovingAFolderItem_RewritesIndexJson_InTheNewOrder_AndLightsTheFolderDot()
    {
        var page = RenderEditor();
        CapturePublish();
        Assert.Equal(new[] { "HPI", "Plan" }, FolderOrder(page, "sections"));
        Assert.Equal(string.Empty, FolderDot(page, "sections"));

        Up(page, "Plan").Click();

        Assert.Equal(new[] { "Plan", "HPI" }, FolderOrder(page, "sections"));
        Assert.Equal("●", FolderDot(page, "sections"));
        Assert.Contains("1 folder order", page.Markup);

        Publish(page);

        Assert.Equal(new[] { "plan", "hpi" }, IndexIds(sent!, "snippets/sections/index.json"));
        var plan = JsonDocument.Parse(sent!.Files["snippets/sections/index.json"]).RootElement.GetProperty("items")[0];
        Assert.Equal("input.kind == \"new\"", plan.GetProperty("when").GetString());
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void FirstAndLastArrows_AreDisabled()
    {
        var page = RenderEditor();

        Assert.True(Up(page, "HPI").HasAttribute("disabled"));
        Assert.False(Down(page, "HPI").HasAttribute("disabled"));
        Assert.False(Up(page, "Plan").HasAttribute("disabled"));
        Assert.True(Down(page, "Plan").HasAttribute("disabled"));

        Up(page, "Plan").Click();

        Assert.True(Up(page, "Plan").HasAttribute("disabled"));
        Assert.True(Down(page, "HPI").HasAttribute("disabled"));
    }

    [Fact]
    public void MovingBack_RestoresTheOrder_AndClearsThePending()
    {
        var page = RenderEditor();

        Up(page, "Plan").Click();
        Down(page, "Plan").Click();

        Assert.Equal(new[] { "HPI", "Plan" }, FolderOrder(page, "sections"));
        Assert.Equal(string.Empty, FolderDot(page, "sections"));
        Assert.DoesNotContain("folder order", page.Markup);
        Assert.Empty(page.FindAll("button.editor-nav__reset-order"));
    }

    [Fact]
    public void ResetOrder_ClearsTheFoldersOverlay()
    {
        var page = RenderEditor();
        Up(page, "Plan").Click();

        page.Find("button[aria-label='Reset order of sections']").Click();

        Assert.Equal(new[] { "HPI", "Plan" }, FolderOrder(page, "sections"));
        Assert.Equal(string.Empty, FolderDot(page, "sections"));
        Assert.DoesNotContain("folder order", page.Markup);
    }

    [Fact]
    public void AnAddedItem_SitsLast_AndMoves()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "+ Add item");
        AddItem(page, "ros", "Review of systems", "ROS.");
        Assert.Equal(new[] { "HPI", "Plan", "Review of systems" }, FolderOrder(page, "sections"));

        Up(page, "Review of systems").Click();
        Up(page, "Review of systems").Click();

        Assert.Equal(new[] { "Review of systems", "HPI", "Plan" }, FolderOrder(page, "sections"));

        Publish(page);

        Assert.Equal(new[] { "ros", "hpi", "plan" }, IndexIds(sent!, "snippets/sections/index.json"));
        Assert.Equal("ROS.", sent!.Files["snippets/sections/ros.md"]);
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void TheOrder_SurvivesAReload_WithItsDraft()
    {
        var page = RenderEditor();
        Up(page, "Plan").Click();

        var again = Reload();

        Assert.Equal(new[] { "Plan", "HPI" }, FolderOrder(again, "sections"));
        Assert.Equal("●", FolderDot(again, "sections"));
        Assert.Contains("1 folder order", again.Markup);
    }

    [Fact]
    public void TheOrder_FollowsIntoTheFanItemSelect_AndThePreviewPick()
    {
        var page = RenderEditor();
        Up(page, "Plan").Click();

        Navigate(page, "tool_note");
        var fanItem = page.Find(".macro-where [data-instance-node='section_notes'] fluent-select[aria-label='Fan item for macro tool_note on section_notes']");
        Assert.Equal(new[] { "plan", "hpi" }, OptionValues(fanItem).Where(v => !string.IsNullOrEmpty(v)).ToList());

        Navigate(page, "section");
        var pick = page.Find("fluent-select[aria-label='Preview as item of snippet.sections']");
        Assert.Equal(new[] { "", "plan", "hpi" }, OptionValues(pick));
    }

    [Fact]
    public void GlobalDiscard_DropsTheOrder()
    {
        var page = RenderEditor();
        Up(page, "Plan").Click();

        // The global Discard arms on the first click and discards on the second.
        GlobalDiscard(page).Click();
        GlobalDiscard(page).Click();

        Assert.Equal(new[] { "HPI", "Plan" }, FolderOrder(page, "sections"));
        Assert.Equal(string.Empty, FolderDot(page, "sections"));
    }

    private static IElement GlobalDiscard(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(b => b.TextContent.Contains("iscard") && !b.TextContent.Contains("pane"));

    [Fact]
    public void Below25_ADataFoldersItems_MoveTheSameWay()
    {
        var page = RenderEditor(EditorFixtures.V24Full());
        CapturePublish();
        Assert.Equal(new[] { "HPI", "Plan" }, FolderOrder(page, "sections"));

        Up(page, "Plan").Click();

        Assert.Equal(new[] { "Plan", "HPI" }, FolderOrder(page, "sections"));
        Assert.Equal("●", FolderDot(page, "sections"));

        Publish(page);

        Assert.Equal(new[] { "plan", "hpi" }, IndexIds(sent!, "data/sections/index.json"));
    }

    // ----- the deliverables -----

    [Fact]
    public void MovingADeliverable_WritesDeliverablesReordered_AndLightsTheDot()
    {
        var page = RenderEditor(V25WithTwoDeliverables());
        CapturePublish();
        Navigate(page, "Deliverables");
        Assert.Equal(new[] { "consult", "letter" }, page.FindAll(".deliverable-card").Select(c => c.GetAttribute("data-deliverable")).ToList());
        Assert.Equal(string.Empty, Dot(page, "Deliverables"));

        page.Find("fluent-button[aria-label='Move deliverable letter up']").Click();

        Assert.Equal(new[] { "letter", "consult" }, page.FindAll(".deliverable-card").Select(c => c.GetAttribute("data-deliverable")).ToList());
        Assert.Equal("●", Dot(page, "Deliverables"));

        Publish(page);

        Assert.Equal(new[] { "letter", "consult" }, Ids(Manifest(sent!), "deliverables"));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void DeliverableArrows_DisabledAtTheEnds_AndAbsentFromASoleCard()
    {
        var page = RenderEditor(V25WithTwoDeliverables());
        Navigate(page, "Deliverables");

        Assert.True(page.Find("fluent-button[aria-label='Move deliverable consult up']").HasAttribute("disabled"));
        Assert.False(page.Find("fluent-button[aria-label='Move deliverable consult down']").HasAttribute("disabled"));
        Assert.False(page.Find("fluent-button[aria-label='Move deliverable letter up']").HasAttribute("disabled"));
        Assert.True(page.Find("fluent-button[aria-label='Move deliverable letter down']").HasAttribute("disabled"));

        var sole = RenderEditor();
        Navigate(sole, "Deliverables");
        Assert.True(sole.Find("fluent-button[aria-label='Move deliverable consult up']").HasAttribute("disabled"));
        Assert.True(sole.Find("fluent-button[aria-label='Move deliverable consult down']").HasAttribute("disabled"));
    }

    // ----- the inputs -----

    [Fact]
    public void MovingAnInput_WritesInputsReordered_EvenWhenTested()
    {
        var page = RenderEditor();
        CapturePublish();
        Navigate(page, "Inputs");
        Assert.Equal(string.Empty, Dot(page, "Inputs"));

        // kind is tested by the deliverable's macro gate: the lock gates remove and retype, not a move.
        var up = page.Find("fluent-button[aria-label='Move input kind up']");
        Assert.False(up.HasAttribute("disabled"));
        up.Click();

        Assert.Equal(new[] { "kind", "notes", "intake" },
            page.FindAll("li.declared-row input.declared-row__id").Select(i => i.GetAttribute("value")).ToList());
        Assert.Equal("●", Dot(page, "Inputs"));

        Publish(page);

        Assert.Equal(new[] { "kind", "notes", "intake" }, Ids(Manifest(sent!), "inputs"));
        var validated = Validated();
        Assert.True(validated.IsValid, string.Join("\n", validated.Errors));
    }

    [Fact]
    public void InputArrows_DisabledAtTheEnds()
    {
        var page = RenderEditor();
        Navigate(page, "Inputs");

        Assert.True(page.Find("fluent-button[aria-label='Move input notes up']").HasAttribute("disabled"));
        Assert.False(page.Find("fluent-button[aria-label='Move input notes down']").HasAttribute("disabled"));
        Assert.False(page.Find("fluent-button[aria-label='Move input intake up']").HasAttribute("disabled"));
        Assert.True(page.Find("fluent-button[aria-label='Move input intake down']").HasAttribute("disabled"));
    }
}
