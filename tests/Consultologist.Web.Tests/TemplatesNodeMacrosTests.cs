using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// v23 (#955) Layer C: authoring a node's macro attachments in the node pane —
/// attach a library macro, place it before/after, gate it with the node-level
/// condition. Gated to specVersion 23 and a prompt-bearing node; a bare
/// before/always attachment round-trips as the string form, a placed or gated
/// one as the object.
/// </summary>
public class TemplatesNodeMacrosTests : ClientRenderTestContext
{
    /// <summary>The fixture, with node macros already attached where a test wants a loaded one (JSON entries, per node id).</summary>
    private static string Manifest(int specVersion, string draftSectionMacros = "", string scopeMacros = "") => $$"""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": {{specVersion}},
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [ { "id": "consult_draft", "label": "Consult draft", "required": true } ],
          "data": { "standards": "data/standards/" },
          "macros": [ { "id": "tool_guidance", "label": "Tool guidance", "file": "macros/tool_guidance.md" } ],
          "prompts": [
            { "id": "classify", "file": "prompts/classify.md", "variables": ["referral"] },
            { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["section_name", "consult_draft"] }
          ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note" }
          ],
          "nodes": [
            { "id": "scope", "label": "Is the referral in scope?", "prompt": "classify", "kind": "classifier",
              "values": ["in_scope", "out_of_scope"], "bindings": { "referral": "input:consult_draft" }{{(scopeMacros.Length > 0 ? $", \"macros\": [ {scopeMacros} ]" : "")}} },
            { "id": "draft-section", "forEach": "data:standards", "label": "Drafting section", "prompt": "draft-section",
              "bindings": { "section_name": "item:name", "consult_draft": "input:consult_draft" }{{(draftSectionMacros.Length > 0 ? $", \"macros\": [ {draftSectionMacros} ]" : "")}} },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """;

    private WorkflowPackageContentResponse Fixture(int specVersion, string draftSectionMacros = "", string scopeMacros = "", string macroText = "Use short SNOMED search terms.") =>
        EditorFixtures.Package(Manifest(specVersion, draftSectionMacros, scopeMacros), specVersion,
            ("prompts/classify.md", "Is this in scope? {{ referral }}"),
            ("prompts/draft-section.md", "{{ section_name }}: {{ consult_draft }}"),
            ("macros/tool_guidance.md", macroText));

    private IRenderedComponent<Templates> RenderEditor(int specVersion, string draftSectionMacros = "", string scopeMacros = "", string macroText = "Use short SNOMED search terms.")
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(Fixture(specVersion, draftSectionMacros, scopeMacros, macroText));
        return Render<Templates>();
    }

    private void ShowNode(IRenderedComponent<Templates> page, string nodeId)
    {
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = $"node:{nodeId}";
        page.Render();
    }

    private WorkflowPackagePublishRequest? sent;

    private void CapturePublish() =>
        WorkflowService.PublishPackageAsync(Arg.Do<WorkflowPackagePublishRequest>(request => sent = request))
            .Returns(new WorkflowPublishOutcome(
                new WorkflowPackagePublishResponse("acct-1234567890ab", "v2026.08.2", "acct-1234567890ab@v2026.08.2"),
                Array.Empty<string>()));

    private static void Publish(IRenderedComponent<Templates> page) =>
        page.FindAll("fluent-button").First(button => button.TextContent.Contains("Publish")).Click();

    private static IReadOnlyList<string> Refusals(IRenderedComponent<Templates> page) =>
        page.FindAll(".fluent-messagebar-message li").Select(item => item.TextContent.Trim()).ToList();

    private static JsonElement NodeMacros(WorkflowPackagePublishRequest request, string nodeId)
    {
        var node = JsonDocument.Parse(request.Manifest.GetRawText()).RootElement
            .GetProperty("nodes").EnumerateArray().First(n => n.GetProperty("id").GetString() == nodeId);
        return node.GetProperty("macros").Clone();
    }

    // ----- gating -----------------------------------------------------------

    [Fact]
    public void At23_APromptNode_OffersTheMacrosSection()
    {
        var page = RenderEditor(23);
        ShowNode(page, "draft-section");

        Assert.Contains("Macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        Assert.NotEmpty(page.FindAll("[aria-label='Attach a macro to draft-section']"));
    }

    [Fact]
    public void Below23_TheMacrosSection_IsAbsent()
    {
        var page = RenderEditor(22);
        ShowNode(page, "draft-section");

        Assert.Empty(page.FindAll("[aria-label='Attach a macro to draft-section']"));
    }

    [Fact]
    public void AnAggregatorNode_HasNoPromptToComposeInto_SoNoSection()
    {
        var page = RenderEditor(23);
        ShowNode(page, "assemble-note");

        Assert.Empty(page.FindAll("[aria-label='Attach a macro to assemble-note']"));
    }

    // ----- composition + round trip -----------------------------------------

    [Fact]
    public void AttachingAMacro_ComposesItBare_OntoTheNode()
    {
        var page = RenderEditor(23);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find("[aria-label='Attach a macro to draft-section']").Change("tool_guidance");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var macros = NodeMacros(sent!, "draft-section");
        Assert.Equal(JsonValueKind.Array, macros.ValueKind);
        // The before/always form round-trips as the bare string.
        Assert.Equal("tool_guidance", macros[0].GetString());
    }

    [Fact]
    public void PlacingAMacroAfter_WritesThePlacementObject()
    {
        var page = RenderEditor(23);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find("[aria-label='Attach a macro to draft-section']").Change("tool_guidance");
        page.Find("[aria-label='Placement for macro tool_guidance on draft-section']").Change("after");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var entry = NodeMacros(sent!, "draft-section")[0];
        Assert.Equal(JsonValueKind.Object, entry.ValueKind);
        Assert.Equal("tool_guidance", entry.GetProperty("id").GetString());
        Assert.Equal("after", entry.GetProperty("at").GetString());
    }

    [Fact]
    public void GatingAMacro_OnAClassifier_WritesItsWhen()
    {
        var page = RenderEditor(23);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find("[aria-label='Attach a macro to draft-section']").Change("tool_guidance");
        // The macro's condition subject names the macro, so it never collides
        // with the node's own "Runs when".
        page.Find("[aria-label='Add a condition for draft-section macro tool_guidance']").Click();
        page.Find("[aria-label^='Condition operand for draft-section macro tool_guidance']").Change("node:scope");
        page.Find("[aria-label^='Condition value for draft-section macro tool_guidance']").Change("in_scope");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal("node:scope == in_scope", NodeMacros(sent!, "draft-section")[0].GetProperty("when").GetString());
    }

    // ----- v24 (#957): the per-item layer ----------------------------------

    private const string FanItemSelect = "fluent-select[aria-label='Fan item for macro tool_guidance on draft-section']";
    private const string Attach = "[aria-label='Attach a macro to draft-section']";
    private const string AddCondition = "[aria-label='Add a condition for draft-section macro tool_guidance']";
    private const string Operand = "[aria-label^='Condition operand for draft-section macro tool_guidance']";
    private const string Value = "[aria-label^='Condition value for draft-section macro tool_guidance']";

    /// <summary>A pending node-macro list restored from the draft (the TemplatesV11MacrosTests.WithDraft precedent), so Publish has something to send and the desk speaks.</summary>
    private IRenderedComponent<Templates> RenderWithDraftedNodeMacros(int specVersion, string nodeId, string macrosJson, string macroText = "Use short SNOMED search terms.")
    {
        var package = Fixture(specVersion, macroText: macroText);
        WorkflowService.GetCurrentPackageContentAsync().Returns(package);
        JSInterop.Setup<string?>("localStorage.getItem", $"workflow-editor-draft:{package.Ref}")
            .SetResult($$"""{ "Version": 15, "NodeMacroEdits": [ { "NodeId": "{{nodeId}}", "Macros": [ {{macrosJson}} ] } ] }""");
        return Render<Templates>();
    }

    private static IReadOnlyList<string> OptionValues(IRenderedComponent<Templates> page, string selector) =>
        page.Find(selector).QuerySelectorAll("fluent-option").Select(option => option.GetAttribute("value") ?? string.Empty).ToList();

    [Fact]
    public void At24_AFanNodeMacro_OffersTheFanItemSelect_NamingTheItems()
    {
        var page = RenderEditor(24);
        ShowNode(page, "draft-section");
        page.Find(Attach).Change("tool_guidance");

        var select = page.Find(FanItemSelect);
        // Value = the item's id, label = its name; the empty choice is every item.
        Assert.Equal(new[] { "", "hpi" }, OptionValues(page, FanItemSelect));
        Assert.Contains("History", select.TextContent);
    }

    [Fact]
    public void At23_TheFanItemSelect_IsAbsent()
    {
        var page = RenderEditor(23);
        ShowNode(page, "draft-section");
        page.Find(Attach).Change("tool_guidance");

        Assert.Empty(page.FindAll(FanItemSelect));
    }

    [Fact]
    public void ANodeThatDoesNotFan_OffersNeitherTheFanItemSelect_NorItemId()
    {
        var page = RenderEditor(24);
        ShowNode(page, "scope");
        page.Find("[aria-label='Attach a macro to scope']").Change("tool_guidance");
        page.Find("[aria-label='Add a condition for scope macro tool_guidance']").Click();

        Assert.Empty(page.FindAll("fluent-select[aria-label='Fan item for macro tool_guidance on scope']"));
        Assert.DoesNotContain("item:id", OptionValues(page, "[aria-label^='Condition operand for scope macro tool_guidance']"));
    }

    [Fact]
    public void AnchoringToAnItem_WritesForItem()
    {
        var page = RenderEditor(24);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find(Attach).Change("tool_guidance");
        page.Find(FanItemSelect).Change("hpi");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var entry = NodeMacros(sent!, "draft-section")[0];
        Assert.Equal(JsonValueKind.Object, entry.ValueKind);
        Assert.Equal("hpi", entry.GetProperty("forItem").GetString());
    }

    [Fact]
    public void GatingOnItemId_OffersTheCollectionsItems_AndWritesTheWhen()
    {
        var page = RenderEditor(24);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find(Attach).Change("tool_guidance");
        page.Find(AddCondition).Click();
        // item:id is offered only to this target — the node's own "Runs when"
        // (and every deliverable condition) keeps the package-wide list.
        Assert.Contains("item:id", OptionValues(page, Operand));
        page.Find(Operand).Change("item:id");
        // The closed set: the collection's item ids, labelled by name.
        Assert.Equal(new[] { "hpi" }, OptionValues(page, Value));
        Assert.Contains("History", page.Find(Value).TextContent);
        page.Find(Value).Change("hpi");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal("item:id == hpi", NodeMacros(sent!, "draft-section")[0].GetProperty("when").GetString());
    }

    [Fact]
    public void ALoadedItemIdWhen_ReadsBackIntoTheBuilder()
    {
        var page = RenderEditor(24, draftSectionMacros: """{ "id": "tool_guidance", "when": "item:id == hpi" }""");
        ShowNode(page, "draft-section");

        Assert.Equal("item:id", page.Find(Operand).GetAttribute("current-value"));
        Assert.Equal("hpi", page.Find(Value).GetAttribute("current-value"));
    }

    // ----- the desk (the Phase 1 gap closed: node-macro whens are checked here) ---

    [Fact]
    public async Task AnItemIdTheCollectionLacks_IsRefusedAtTheDesk()
    {
        var page = RenderWithDraftedNodeMacros(24, "draft-section", """{ "Id": "tool_guidance", "When": "item:id == ros" }""");
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "Node 'draft-section' macro 'tool_guidance' condition compares 'item:id' to 'ros', which the collection does not contain (items: hpi).",
            Refusals(page));
    }

    [Fact]
    public async Task AClassifierValueTheClassifierLacks_IsRefusedAtTheDesk()
    {
        // The v23 gap: a node macro's when got no desk check at all.
        var page = RenderWithDraftedNodeMacros(23, "draft-section", """{ "Id": "tool_guidance", "When": "node:scope == nonsense" }""");
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "Node 'draft-section' macro 'tool_guidance' condition compares 'node:scope' to 'nonsense', which it does not declare (values: in_scope, out_of_scope).",
            Refusals(page));
    }

    [Fact]
    public async Task ForItem_Below24_IsRefusedAtTheDesk_ByName()
    {
        var page = RenderWithDraftedNodeMacros(23, "draft-section", """{ "Id": "tool_guidance", "ForItem": "hpi" }""");
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "Node 'draft-section' macro 'tool_guidance' declares forItem, which requires specVersion 24. Use \"Upgrade to specVersion 24\" and publish.",
            Refusals(page));
    }

    [Fact]
    public async Task AnItemToken_OnANodeThatDoesNotFan_IsRefusedAtTheDesk()
    {
        var page = RenderWithDraftedNodeMacros(24, "scope", """{ "Id": "tool_guidance" }""", macroText: "Draft {{item:name}}.");
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "Node 'scope' macro 'tool_guidance' carries '{{item:name}}' but the node declares no forEach.",
            Refusals(page));
    }

    [Fact]
    public async Task AnItemToken_Below24_IsAVersionRequirementAtTheDesk()
    {
        var page = RenderWithDraftedNodeMacros(23, "draft-section", """{ "Id": "tool_guidance" }""", macroText: "Draft {{item:name}}.");
        Publish(page);

        await WorkflowService.DidNotReceiveWithAnyArgs().PublishPackageAsync(default!);
        Assert.Contains(
            "Macro 'tool_guidance' placeholder '{{item:name}}' requires specVersion 24. Use \"Upgrade to specVersion 24\" and publish.",
            Refusals(page));
    }

    [Fact]
    public void AnItemToken_OnAFanNode_Publishes_AndTheMacroPaneOffersTheFields()
    {
        var page = RenderWithDraftedNodeMacros(24, "draft-section", """{ "Id": "tool_guidance" }""", macroText: "Draft {{item:name}}.");
        CapturePublish();
        Services.GetRequiredService<WorkflowEditorSession>().SelectedKey = "macros/tool_guidance.md";
        page.Render();

        Assert.Contains("{{item:name}}", page.Find(".macro-placeholders__list").TextContent);
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
    }
}
