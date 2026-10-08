using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #956 (view-only) → #965 (edit in place) → #966 (full parity): the node pane
/// surfaces the OUTPUT macros anchored around a node's output — a deliverable
/// macro whose before/after names this node — and edits them as the Documents
/// row does: the whole placement list (appended / before·after any source /
/// slot), the fan item, the gated-when (the same ConditionField target the
/// Documents pane uses), remove, and an attach-here picker placing a library
/// macro before this node. Rows are the entries anchored to THIS node; moving
/// one elsewhere takes it out of the view. Reordering stays in Documents.
/// </summary>
public class TemplatesOutputMacrosOnNodeTests : ClientRenderTestContext
{
    // v12 (placement arrives at 12): consult_note aggregates draft-section, and
    // the `closing` macro is placed BEFORE draft-section, gated on an enum input
    // (so its ConditionField renders expanded, not resting). Knobs: the spec
    // version; whether the macro is attached at all; whether draft-section fans
    // data:standards (the fixture package ships its index with `hpi`); a second
    // deliverable aggregating the same node.
    private static string Manifest(int specVersion = 12, bool attached = true, bool fans = false, bool secondDoc = false)
    {
        var dataLine = fans ? "\"data\": { \"standards\": \"data/standards/\" }," : "";
        var forEachLine = fans ? "\"forEach\": \"data:standards\"," : "";
        var closingEntry = attached ? ", \"macros\": [ { \"id\": \"closing\", \"before\": \"node:draft-section\", \"when\": \"encounter_kind == follow_up\" } ]" : "";
        var summaryResult = secondDoc ? ", { \"id\": \"summary\", \"node\": \"node:assemble-summary\", \"label\": \"Summary\" }" : "";
        var summaryNode = secondDoc ? ", { \"id\": \"assemble-summary\", \"label\": \"Assembling summary\", \"aggregate\": [\"node:draft-section\"] }" : "";
        return $$"""
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": {{specVersion}},
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "encounter_kind", "label": "Encounter kind", "required": false, "type": "enum", "values": ["new_patient", "follow_up"] }
          ],
          {{dataLine}}
          "macros": [ { "id": "closing", "label": "Closing", "file": "macros/closing.md" } ],
          "prompts": [ { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["consult_draft"] } ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note"{{closingEntry}} }{{summaryResult}}
          ],
          "nodes": [
            { "id": "draft-section", "label": "Drafting section", "prompt": "draft-section", {{forEachLine}}
              "bindings": { "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }{{summaryNode}}
          ]
        }
        """;
    }

    private WorkflowPackageContentResponse Fixture(int specVersion = 12, bool attached = true, bool fans = false, bool secondDoc = false) =>
        EditorFixtures.Package(Manifest(specVersion, attached, fans, secondDoc), specVersion,
            ("prompts/draft-section.md", "{{ consult_draft }}"),
            ("macros/closing.md", "Thank you for this referral."));

    private IRenderedComponent<Templates> RenderEditor(int specVersion = 12, bool attached = true, bool fans = false, bool secondDoc = false)
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(Fixture(specVersion, attached, fans, secondDoc));
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

    /// <summary>The one macro entry on consult_note as published.</summary>
    private static JsonElement ClosingEntry(WorkflowPackagePublishRequest request) =>
        JsonDocument.Parse(request.Manifest.GetRawText()).RootElement
            .GetProperty("results").EnumerateArray().First(r => r.GetProperty("id").GetString() == "consult_note")
            .GetProperty("macros").EnumerateArray().Single().Clone();

    private const string Placement = "fluent-select[aria-label='Placement for macro closing around draft-section']";
    private const string Remove = "fluent-button[aria-label='Remove macro closing from document consult_note']";
    private const string ConditionValue = "fluent-select[aria-label^='Condition value for consult_note macro closing']";
    private const string FanItem = "fluent-select[aria-label='Fan item for macro closing around draft-section']";
    private const string Attach = "fluent-select[aria-label='Attach an output macro before draft-section']";
    private const string AttachDoc = "fluent-select[aria-label='Deliverable for an output macro before draft-section']";

    private static IReadOnlyList<string> OptionValues(IRenderedComponent<Templates> page, string selector) =>
        page.Find(selector).QuerySelectorAll("fluent-option").Select(option => option.GetAttribute("value") ?? string.Empty).ToList();

    // ----- surface ----------------------------------------------------------

    [Fact]
    public void ASourceNode_SurfacesItsAnchoredOutputMacros()
    {
        var page = RenderEditor();
        ShowNode(page, "draft-section");

        Assert.Contains("Output macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        var row = Assert.Single(page.FindAll(".node-output-macro .node-output-macro-row"));
        Assert.Contains("closing", row.TextContent);
        Assert.Contains("consult_note", row.TextContent);
        // The placement is a select scoped to this node, and the gated-when
        // surfaces in the builder (expanded, since the fixture carries one).
        Assert.NotEmpty(page.FindAll(Placement));
        Assert.NotEmpty(page.FindAll("[aria-label^='Condition operand for consult_note macro closing']"));
    }

    [Fact]
    public void ANodeNoDeliverableAggregates_HasNoSection()
    {
        var page = RenderEditor();
        // assemble-note is the aggregator — no deliverable aggregates IT.
        ShowNode(page, "assemble-note");

        Assert.DoesNotContain("Output macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        Assert.Empty(page.FindAll(".node-output-macro-row"));
    }

    [Fact]
    public void TheSection_HasFullParity_WithoutReorder()
    {
        var page = RenderEditor();
        ShowNode(page, "draft-section");

        // The whole placement list, as the Documents row offers it (slot at 20).
        Assert.Equal(new[] { "", "before|node:draft-section", "after|node:draft-section" }, OptionValues(page, Placement));
        Assert.Contains("before this node", page.Find(Placement).TextContent);
        Assert.NotEmpty(page.FindAll(ConditionValue));
        Assert.NotEmpty(page.FindAll(Remove));
        // Rung 2: the attach-here row (the one deliverable is named inline; the
        // one library macro is already attached, so the picker gives way).
        var attachRow = page.Find(".node-output-macro-attach-row");
        Assert.Contains("every library macro is already attached", attachRow.TextContent);
        Assert.Contains("consult_note", attachRow.TextContent);
        Assert.Empty(page.FindAll(AttachDoc));
        // Reordering stays in Documents (#940's call for the macro pane too).
        Assert.Empty(page.FindAll(".result-macro-up"));
        Assert.Empty(page.FindAll(".result-macro-down"));
    }

    [Fact]
    public void At20_ThePlacementList_OffersSlot()
    {
        var page = RenderEditor(specVersion: 20);
        ShowNode(page, "draft-section");

        Assert.Contains("slot", OptionValues(page, Placement));
    }

    // ----- round trips ------------------------------------------------------

    [Fact]
    public void FlippingPlacement_RoundTrips()
    {
        var page = RenderEditor();
        CapturePublish();
        ShowNode(page, "draft-section");

        // The same SetMacroPlacementAsync the Documents pane calls — the entry
        // moves from before to after this node.
        page.Find(Placement).Change("after|node:draft-section");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var entry = ClosingEntry(sent!);
        Assert.Equal("node:draft-section", entry.GetProperty("after").GetString());
        Assert.False(entry.TryGetProperty("before", out _));
    }

    [Fact]
    public void EditingTheGatedWhen_RoundTrips()
    {
        var page = RenderEditor();
        CapturePublish();
        ShowNode(page, "draft-section");

        // The same ConditionField target the Documents and macro panes address.
        page.Find(ConditionValue).Change("new_patient");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal("encounter_kind == new_patient", ClosingEntry(sent!).GetProperty("when").GetString());
    }

    [Fact]
    public void MovingToAppended_LeavesTheNodeView_AndPublishesBare()
    {
        var page = RenderEditor();
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find(Placement).Change("");

        // Out of this node's view (the section stays: the deliverable still
        // aggregates the node), and the entry publishes with no anchor.
        Assert.Empty(page.FindAll(".node-output-macro"));
        Assert.Contains("Output macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var entry = ClosingEntry(sent!);
        Assert.False(entry.TryGetProperty("before", out _));
        Assert.False(entry.TryGetProperty("after", out _));
    }

    [Fact]
    public void MovingToSlot_PublishesSlot_WithoutAnAnchor()
    {
        var page = RenderEditor(specVersion: 20);
        CapturePublish();
        ShowNode(page, "draft-section");

        page.Find(Placement).Change("slot");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var entry = ClosingEntry(sent!);
        Assert.True(entry.GetProperty("slot").GetBoolean());
        Assert.False(entry.TryGetProperty("before", out _));
    }

    [Fact]
    public void AnchoringToAFanItem_PublishesForItem()
    {
        var page = RenderEditor(specVersion: 19, fans: true);
        CapturePublish();
        ShowNode(page, "draft-section");

        // The anchor section fans data:standards, so the fan-item select appears
        // (label = the item's name, value = its id).
        Assert.Equal(new[] { "", "hpi" }, OptionValues(page, FanItem));
        page.Find(FanItem).Change("hpi");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        Assert.Equal("hpi", ClosingEntry(sent!).GetProperty("forItem").GetString());
    }

    [Fact]
    public void ANodeThatDoesNotFan_HasNoFanItemSelect()
    {
        var page = RenderEditor(specVersion: 19);
        ShowNode(page, "draft-section");

        Assert.Empty(page.FindAll(FanItem));
    }

    [Fact]
    public void AttachingHere_PlacesTheMacroBeforeThisNode()
    {
        // Nothing attached yet: the section still renders (consult_note
        // aggregates the node) with the picker and an empty-rows sentence.
        var page = RenderEditor(attached: false);
        CapturePublish();
        ShowNode(page, "draft-section");
        Assert.Empty(page.FindAll(".node-output-macro"));
        Assert.Contains("No macro is placed around this node's output yet.", page.Markup);

        page.Find(Attach).Change("closing");

        var row = Assert.Single(page.FindAll(".node-output-macro"));
        Assert.Contains("closing", row.TextContent);
        Publish(page);
        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var entry = ClosingEntry(sent!);
        Assert.Equal("closing", entry.GetProperty("id").GetString());
        Assert.Equal("node:draft-section", entry.GetProperty("before").GetString());
    }

    [Fact]
    public void WhenEveryMacroIsAttached_ThePickerGivesWay()
    {
        var page = RenderEditor();
        ShowNode(page, "draft-section");

        Assert.Empty(page.FindAll(Attach));
        Assert.Contains("every library macro is already attached", page.Markup);
    }

    [Fact]
    public void WithTwoAggregatingDeliverables_TheDocumentIsChosenFirst()
    {
        var page = RenderEditor(attached: false, secondDoc: true);
        CapturePublish();
        ShowNode(page, "draft-section");

        // Explicit: no document is chosen until the author chooses, and the
        // macro picker waits for that.
        Assert.Equal(new[] { "", "consult_note", "summary" }, OptionValues(page, AttachDoc));
        Assert.NotNull(page.Find(Attach).GetAttribute("disabled"));

        page.Find(AttachDoc).Change("summary");
        Assert.Null(page.Find(Attach).GetAttribute("disabled"));
        page.Find(Attach).Change("closing");
        Publish(page);

        Assert.True(sent != null, string.Join(" | ", Refusals(page)));
        var results = JsonDocument.Parse(sent!.Manifest.GetRawText()).RootElement.GetProperty("results").EnumerateArray().ToList();
        Assert.False(results.First(r => r.GetProperty("id").GetString() == "consult_note").TryGetProperty("macros", out _));
        var entry = results.First(r => r.GetProperty("id").GetString() == "summary").GetProperty("macros").EnumerateArray().Single();
        Assert.Equal("node:draft-section", entry.GetProperty("before").GetString());
    }

    [Fact]
    public void RemovingFromTheNodePane_DetachesIt()
    {
        // Detaching the only reference leaves the macro declared but orphaned —
        // which the validator refuses (#571), so assert the detach on the UI
        // rather than through a (correctly) refused publish, as #940 does.
        var page = RenderEditor();
        ShowNode(page, "draft-section");
        Assert.Single(page.FindAll(".node-output-macro"));

        page.Find(Remove).Click();

        // The row is gone; the section stays, offering to attach again.
        Assert.Empty(page.FindAll(".node-output-macro"));
        Assert.NotEmpty(page.FindAll(Attach));
    }
}
