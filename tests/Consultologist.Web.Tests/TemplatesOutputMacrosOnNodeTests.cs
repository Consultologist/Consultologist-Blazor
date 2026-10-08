using System.Text.Json;
using Bunit;
using Consultologist.Web.Pages;
using Consultologist.Web.Services.Workflow;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Consultologist.Web.Tests;

/// <summary>
/// #956 (view-only) → #965 (edit in place): the node pane surfaces the OUTPUT
/// macros anchored around a node's output — a deliverable macro whose
/// before/after names this node — and edits them in place: flip before/after
/// this node, the gated-when (the same ConditionField target the Documents pane
/// uses), remove. Attaching, another source and forItem stay out (#966).
/// </summary>
public class TemplatesOutputMacrosOnNodeTests : ClientRenderTestContext
{
    // v12 (placement arrives at 12): consult_note aggregates draft-section, and
    // the `closing` macro is placed BEFORE draft-section, gated on an enum input
    // (so its ConditionField renders expanded, not resting).
    private const string ManifestJson = """
        {
          "name": "acct-1234567890ab",
          "version": "v2026.08.1",
          "specVersion": 12,
          "tags": [],
          "templating": { "engine": "scriban", "engineVersion": "7.2.5" },
          "inputs": [
            { "id": "consult_draft", "label": "Consult draft", "required": true },
            { "id": "encounter_kind", "label": "Encounter kind", "required": false, "type": "enum", "values": ["new_patient", "follow_up"] }
          ],
          "macros": [ { "id": "closing", "label": "Closing", "file": "macros/closing.md" } ],
          "prompts": [ { "id": "draft-section", "file": "prompts/draft-section.md", "variables": ["consult_draft"] } ],
          "results": [
            { "id": "consult_note", "node": "node:assemble-note", "label": "Consultation note",
              "macros": [ { "id": "closing", "before": "node:draft-section", "when": "encounter_kind == follow_up" } ] }
          ],
          "nodes": [
            { "id": "draft-section", "label": "Drafting section", "prompt": "draft-section",
              "bindings": { "consult_draft": "input:consult_draft" } },
            { "id": "assemble-note", "label": "Assembling note", "aggregate": ["node:draft-section"] }
          ]
        }
        """;

    private WorkflowPackageContentResponse Fixture() =>
        EditorFixtures.Package(ManifestJson, 12,
            ("prompts/draft-section.md", "{{ consult_draft }}"),
            ("macros/closing.md", "Thank you for this referral."));

    private IRenderedComponent<Templates> RenderEditor()
    {
        WorkflowService.GetCurrentPackageContentAsync().Returns(Fixture());
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

    // ----- surface ----------------------------------------------------------

    [Fact]
    public void ASourceNode_SurfacesItsAnchoredOutputMacros()
    {
        var page = RenderEditor();
        ShowNode(page, "draft-section");

        Assert.Contains("Output macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        var row = Assert.Single(page.FindAll(".node-output-macro-row"));
        Assert.Contains("closing", row.TextContent);
        Assert.Contains("consult_note", row.TextContent);
        // The placement is a select scoped to this node, and the gated-when
        // surfaces in the builder (expanded, since the fixture carries one).
        Assert.NotEmpty(page.FindAll(Placement));
        Assert.NotEmpty(page.FindAll("[aria-label^='Condition operand for consult_note macro closing']"));
    }

    [Fact]
    public void ANodeWithNoAnchoredOutputMacro_HasNoSection()
    {
        var page = RenderEditor();
        // assemble-note is the aggregator — nothing is anchored before/after it.
        ShowNode(page, "assemble-note");

        Assert.DoesNotContain("Output macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
        Assert.Empty(page.FindAll(".node-output-macro-row"));
    }

    [Fact]
    public void TheSection_IsEditableInPlace_ButNotAttachable()
    {
        var page = RenderEditor();
        ShowNode(page, "draft-section");

        // Rung 1: placement (this node only), the condition builder, remove.
        Assert.NotEmpty(page.FindAll(Placement));
        Assert.NotEmpty(page.FindAll(ConditionValue));
        Assert.NotEmpty(page.FindAll(Remove));
        // Not rung 2: no attach picker, no fan-item select.
        Assert.Empty(page.FindAll("[aria-label^='Attach']"));
        Assert.Empty(page.FindAll(".result-macro-attach-doc"));
        Assert.Empty(page.FindAll(".result-macro-foritem"));
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
    public void RemovingFromTheNodePane_DetachesIt()
    {
        // Detaching the only reference leaves the macro declared but orphaned —
        // which the validator refuses (#571), so assert the detach on the UI
        // rather than through a (correctly) refused publish, as #940 does.
        var page = RenderEditor();
        ShowNode(page, "draft-section");
        Assert.Single(page.FindAll(".node-output-macro-row"));

        page.Find(Remove).Click();

        Assert.Empty(page.FindAll(".node-output-macro-row"));
        Assert.DoesNotContain("Output macros", page.FindAll(".node-section__head").Select(head => head.TextContent.Trim()));
    }
}
